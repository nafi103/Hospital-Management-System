using System;
using System.Collections.Generic;
using System.Linq;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    public enum SafetyWarningSeverity
    {
        Warning,
        Critical
    }

    public record SafetyWarning(string Category, SafetyWarningSeverity Severity, string Message);

    // Plain input shapes rather than EF entities, so this stays a pure function the caller
    // feeds pre-fetched data into - same pattern as News2Calculator. No DbContext, no I/O.
    public record MedicineInfo(int Id, string Name, string GenericName, string Strength, int StockQuantity);
    public record SafetyCheckItem(int MedicineId, int RequestedQuantity, DoseUnit DoseUnit);
    public record SafetyCheckAllergy(string Substance, string? AllergenGenericName, AllergySeverity Severity);
    public record ActiveMedicationInfo(
        int MedicineId,
        string MedicineName,
        string GenericName,
        DateTime PrescribedOrDispensedAt,
        int? DurationDays,
        int PrescriptionId);

    // Checks run on prescription save, all deterministic - no AI call for any of them.
    // Three are plain SQL-shaped lookups (duplicate generic, allergy match, stock shortfall);
    // the fourth (pediatric strength) is a documented heuristic, not a clinical dosing
    // calculation - see the comment on CheckPediatricStrength for its limits.
    public static class PrescriptionSafetyChecker
    {
        public static List<SafetyWarning> Check(
            bool patientIsChild,
            IReadOnlyList<SafetyCheckAllergy> allergies,
            IReadOnlyList<SafetyCheckItem> items,
            IReadOnlyList<MedicineInfo> allMedicines,
            IReadOnlyList<ActiveMedicationInfo>? activeMedications = null)
        {
            var warnings = new List<SafetyWarning>();
            var medById = allMedicines.ToDictionary(m => m.Id);

            var resolvedItems = items
                .Where(i => medById.ContainsKey(i.MedicineId))
                .Select(i => (Item: i, Medicine: medById[i.MedicineId]))
                .ToList();

            CheckDuplicateTherapy(resolvedItems, activeMedications, warnings);
            CheckAllergyConflicts(resolvedItems, allergies, activeMedications, warnings);
            CheckDrugInteractions(resolvedItems, activeMedications, warnings);
            CheckStock(resolvedItems, allMedicines, warnings);
            if (patientIsChild)
            {
                CheckPediatricStrength(resolvedItems, warnings);
            }

            return warnings;
        }

        private static void CheckDuplicateTherapy(
            List<(SafetyCheckItem Item, MedicineInfo Medicine)> resolvedItems,
            IReadOnlyList<ActiveMedicationInfo>? activeMedications,
            List<SafetyWarning> warnings)
        {
            var byGeneric = resolvedItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Medicine.GenericName))
                .GroupBy(x => x.Medicine.GenericName, StringComparer.OrdinalIgnoreCase);

            foreach (var group in byGeneric.Where(g => g.Count() > 1))
            {
                var names = string.Join(" and ", group.Select(x => x.Medicine.Name).Distinct());
                warnings.Add(new SafetyWarning(
                    "Duplicate Therapy",
                    SafetyWarningSeverity.Warning,
                    $"{names} are both {group.Key} - confirm this isn't unintentional duplicate therapy."));
            }

            if (activeMedications != null && activeMedications.Count > 0)
            {
                foreach (var (_, medicine) in resolvedItems)
                {
                    if (string.IsNullOrWhiteSpace(medicine.GenericName)) continue;

                    var activeDuplicates = activeMedications
                        .Where(a => string.Equals(a.GenericName, medicine.GenericName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var active in activeDuplicates)
                    {
                        var dispensedDateStr = active.PrescribedOrDispensedAt.ToString("MMM dd, yyyy");
                        warnings.Add(new SafetyWarning(
                            "Duplicate Therapy",
                            SafetyWarningSeverity.Warning,
                            $"{medicine.Name} ({medicine.GenericName}) duplicates active dispensed medication {active.MedicineName} from Prescription #{active.PrescriptionId} (dispensed {dispensedDateStr}) - confirm this isn't unintentional duplicate therapy."));
                    }
                }
            }
        }

        private static void CheckDrugInteractions(
            List<(SafetyCheckItem Item, MedicineInfo Medicine)> resolvedItems,
            IReadOnlyList<ActiveMedicationInfo>? activeMedications,
            List<SafetyWarning> warnings)
        {
            // 1. Within current prescription: pairwise interaction check
            for (int i = 0; i < resolvedItems.Count; i++)
            {
                var medA = resolvedItems[i].Medicine;
                for (int j = i + 1; j < resolvedItems.Count; j++)
                {
                    var medB = resolvedItems[j].Medicine;
                    var ddis = AtcClassificationGraph.EvaluateInteraction(
                        medA.Name, medA.GenericName,
                        medB.Name, medB.GenericName);

                    foreach (var ddi in ddis)
                    {
                        warnings.Add(ddi);
                    }
                }
            }

            // 2. Between current prescription and active dispensed medications
            if (activeMedications != null && activeMedications.Count > 0)
            {
                foreach (var (_, medicine) in resolvedItems)
                {
                    foreach (var active in activeMedications)
                    {
                        var ddis = AtcClassificationGraph.EvaluateInteraction(
                            medicine.Name, medicine.GenericName,
                            active.MedicineName, active.GenericName);

                        foreach (var ddi in ddis)
                        {
                            var dispensedDateStr = active.PrescribedOrDispensedAt.ToString("MMM dd, yyyy");
                            warnings.Add(new SafetyWarning(
                                "Drug-Drug Interaction",
                                ddi.Severity,
                                $"{ddi.Message} (Active medication {active.MedicineName} from Prescription #{active.PrescriptionId}, dispensed {dispensedDateStr})"));
                        }
                    }
                }
            }
        }

        private static void CheckAllergyConflicts(
            List<(SafetyCheckItem Item, MedicineInfo Medicine)> resolvedItems,
            IReadOnlyList<SafetyCheckAllergy> allergies,
            IReadOnlyList<ActiveMedicationInfo>? activeMedications,
            List<SafetyWarning> warnings)
        {
            foreach (var (_, medicine) in resolvedItems)
            {
                var match = allergies.FirstOrDefault(a => IsAllergyConflict(a, medicine.GenericName, medicine.Name));
                if (match != null)
                {
                    warnings.Add(new SafetyWarning(
                        "Allergy Conflict",
                        SafetyWarningSeverity.Critical,
                        $"Patient has a recorded {match.Severity} allergy to {match.Substance}. " +
                        $"{medicine.Name} ({medicine.GenericName}) conflicts with this."));
                }
            }

            if (activeMedications != null && activeMedications.Count > 0)
            {
                foreach (var active in activeMedications)
                {
                    var match = allergies.FirstOrDefault(a => IsAllergyConflict(a, active.GenericName, active.MedicineName));
                    if (match != null)
                    {
                        warnings.Add(new SafetyWarning(
                            "Allergy Conflict",
                            SafetyWarningSeverity.Critical,
                            $"Patient has a recorded {match.Severity} allergy to {match.Substance}. " +
                            $"Active dispensed medication {active.MedicineName} ({active.GenericName}) from Prescription #{active.PrescriptionId} conflicts with this."));
                    }
                }
            }
        }

        private static bool IsAllergyConflict(SafetyCheckAllergy allergy, string genericName, string medicineName)
        {
            var gen = genericName?.Trim() ?? "";
            var med = medicineName?.Trim() ?? "";
            var allergenGen = allergy.AllergenGenericName?.Trim() ?? "";
            var substance = allergy.Substance?.Trim() ?? "";

            // 1. Direct normalized generic equality
            if (!string.IsNullOrEmpty(allergenGen) &&
                string.Equals(allergenGen, gen, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. Class-level Cross-Reactivity: Penicillins / Beta-Lactams
            // Catches Penicillin allergy -> Amoxicillin, Ampicillin, Flucloxacillin, Augmentin, etc.
            if (IsPenicillinClass(allergenGen) || IsPenicillinClass(substance))
            {
                if (IsPenicillinClass(gen) || IsPenicillinClass(med))
                {
                    return true;
                }
            }

            // 3. Sulfonamides ("Sulfa") with chemical salt disambiguation
            // "Sulfa" matches Sulfamethoxazole/Trimethoprim (Bactrim), but strictly excludes chemical salts like Sulfate/Sulphate (Morphine Sulfate, Salbutamol Sulfate, Ferrous Sulfate)
            if (IsSulfaAllergy(allergenGen) || IsSulfaAllergy(substance))
            {
                if (IsSulfaMedication(gen, med))
                {
                    return true;
                }
                if (IsChemicalSulfateSaltOnly(gen, med))
                {
                    return false;
                }
            }

            // 4. Quinolone / Fluoroquinolone class cross-reactivity
            if (IsQuinoloneClass(allergenGen) || IsQuinoloneClass(substance))
            {
                if (IsQuinoloneClass(gen) || IsQuinoloneClass(med))
                {
                    return true;
                }
            }

            // 5. Cephalosporin class cross-reactivity
            if (IsCephalosporinClass(allergenGen) || IsCephalosporinClass(substance))
            {
                if (IsCephalosporinClass(gen) || IsCephalosporinClass(med))
                {
                    return true;
                }
            }

            // 6. Macrolide class cross-reactivity
            if (IsMacrolideClass(allergenGen) || IsMacrolideClass(substance))
            {
                if (IsMacrolideClass(gen) || IsMacrolideClass(med))
                {
                    return true;
                }
            }

            // 7. ATC Graph Traversal for Cross-Reactivity
            if (!string.IsNullOrWhiteSpace(allergenGen) && AtcClassificationGraph.MatchesAllergyClass(allergenGen, med, gen))
            {
                if (IsSulfaAllergy(allergenGen) && IsChemicalSulfateSaltOnly(gen, med))
                {
                    return false;
                }
                return true;
            }

            if (!string.IsNullOrWhiteSpace(substance) && AtcClassificationGraph.MatchesAllergyClass(substance, med, gen))
            {
                if (IsSulfaAllergy(substance) && IsChemicalSulfateSaltOnly(gen, med))
                {
                    return false;
                }
                return true;
            }

            // 8. General bidirectional substring/token matching with chemical salt exclusion
            if (!string.IsNullOrWhiteSpace(substance))
            {
                // Disambiguate chemical salts from sulfa
                if (substance.Equals("Sulfa", StringComparison.OrdinalIgnoreCase) && IsChemicalSulfateSaltOnly(gen, med))
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(gen) && (
                    gen.Contains(substance, StringComparison.OrdinalIgnoreCase) ||
                    substance.Contains(gen, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(med) && (
                    med.Contains(substance, StringComparison.OrdinalIgnoreCase) ||
                    substance.Contains(med, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPenicillinClass(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();
            if (t.Contains("penicillin") || t.Contains("beta-lactam") || t.Contains("augmentin") || t.Contains("amoxil") || t.Contains("pen-vk"))
                return true;

            var words = System.Text.RegularExpressions.Regex.Split(t, @"[^a-z0-9]+");
            return words.Any(w => w.EndsWith("cillin") || w.Equals("amox", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSulfaAllergy(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();
            return t.Contains("sulfa") || t.Contains("sulpha") || t.Contains("sulfonamide");
        }

        private static bool IsSulfaMedication(string genericName, string medicineName)
        {
            var combined = $"{genericName} {medicineName}".ToLowerInvariant();
            if (combined.Contains("bactrim") || combined.Contains("septra") || combined.Contains("cotrimoxazole") || combined.Contains("co-trimoxazole"))
                return true;

            var words = System.Text.RegularExpressions.Regex.Split(combined, @"[^a-z0-9]+");
            return words.Any(w => (w.StartsWith("sulfa") || w.StartsWith("sulpha")) 
                               && !w.StartsWith("sulfat") && !w.StartsWith("sulphat"));
        }

        private static bool IsChemicalSulfateSaltOnly(string genericName, string medicineName)
        {
            var combined = $"{genericName} {medicineName}".ToLowerInvariant();
            bool hasSulfateSalt = combined.Contains("sulfate") || combined.Contains("sulphate");
            return hasSulfateSalt && !IsSulfaMedication(genericName, medicineName);
        }

        private static bool IsQuinoloneClass(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();
            if (t.Contains("quinolone") || t.Contains("fluoroquinolone")) return true;
            var words = System.Text.RegularExpressions.Regex.Split(t, @"[^a-z0-9]+");
            return words.Any(w => w.EndsWith("floxacin"));
        }

        private static bool IsCephalosporinClass(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();
            if (t.Contains("cephalosporin")) return true;
            var words = System.Text.RegularExpressions.Regex.Split(t, @"[^a-z0-9]+");
            return words.Any(w => w.StartsWith("cef") || w.StartsWith("ceph"));
        }

        private static bool IsMacrolideClass(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();
            if (t.Contains("macrolide")) return true;
            var words = System.Text.RegularExpressions.Regex.Split(t, @"[^a-z0-9]+");
            return words.Any(w => w.EndsWith("thromycin"));
        }

        private static bool IsPotentPediatricDrug(string genericName, string medicineName)
        {
            var combined = $"{genericName} {medicineName}".ToLowerInvariant();
            return combined.Contains("digoxin") ||
                   combined.Contains("warfarin") ||
                   combined.Contains("morphine") ||
                   combined.Contains("prednisolone") ||
                   combined.Contains("methotrexate") ||
                   combined.Contains("phenytoin") ||
                   combined.Contains("theophylline");
        }

        private static void CheckStock(
            List<(SafetyCheckItem Item, MedicineInfo Medicine)> resolvedItems,
            IReadOnlyList<MedicineInfo> allMedicines,
            List<SafetyWarning> warnings)
        {
            foreach (var (item, medicine) in resolvedItems)
            {
                if (item.RequestedQuantity <= medicine.StockQuantity)
                {
                    continue;
                }

                var alternatives = allMedicines
                    .Where(m => m.Id != medicine.Id
                        && m.StockQuantity > 0
                        && string.Equals(m.GenericName, medicine.GenericName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.StockQuantity)
                    .Select(m => m.Name)
                    .ToList();

                var altText = alternatives.Count > 0
                    ? $" In-stock alternative(s): {string.Join(", ", alternatives)}."
                    : " No in-stock alternative found for this generic.";

                warnings.Add(new SafetyWarning(
                    "Insufficient Stock",
                    SafetyWarningSeverity.Warning,
                    $"{medicine.Name} has only {medicine.StockQuantity} in stock; {item.RequestedQuantity} requested.{altText}"));
            }
        }

        // Heuristic, not a dosing calculation: flags solid oral dose forms (tablet/capsule)
        // at 500mg or more for a pediatric patient, or potent/narrow-therapeutic-index drugs at lower thresholds.
        private static void CheckPediatricStrength(
            List<(SafetyCheckItem Item, MedicineInfo Medicine)> resolvedItems,
            List<SafetyWarning> warnings)
        {
            const int adultStrengthThresholdMg = 500;

            foreach (var (item, medicine) in resolvedItems)
            {
                if (item.DoseUnit != DoseUnit.Tablet && item.DoseUnit != DoseUnit.Capsule)
                {
                    continue;
                }

                bool isPotent = IsPotentPediatricDrug(medicine.GenericName, medicine.Name);
                int thresholdMg = isPotent ? 5 : adultStrengthThresholdMg;

                if (TryParseLeadingMilligrams(medicine.Strength, out var mg) && mg >= thresholdMg)
                {
                    var reason = isPotent
                        ? "is a potent/narrow-index pediatric medication"
                        : "is an adult-strength solid dose";

                    warnings.Add(new SafetyWarning(
                        "Pediatric Dose",
                        SafetyWarningSeverity.Warning,
                        $"{medicine.Name} ({medicine.Strength}) {reason} - confirm this is appropriate for a child before dispensing."));
                }
            }
        }

        private static bool TryParseLeadingMilligrams(string strength, out int milligrams)
        {
            milligrams = 0;
            if (string.IsNullOrWhiteSpace(strength))
            {
                return false;
            }

            var digits = new string(strength.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                return false;
            }

            return int.TryParse(digits, out milligrams) && strength.Contains("mg", StringComparison.OrdinalIgnoreCase);
        }
    }
}
