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
            if (!string.IsNullOrEmpty(allergy.AllergenGenericName) &&
                string.Equals(allergy.AllergenGenericName, genericName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(allergy.Substance))
            {
                var substance = allergy.Substance.Trim();
                if (!string.IsNullOrEmpty(genericName) && (
                    genericName.Contains(substance, StringComparison.OrdinalIgnoreCase) ||
                    substance.Contains(genericName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(medicineName) && (
                    medicineName.Contains(substance, StringComparison.OrdinalIgnoreCase) ||
                    substance.Contains(medicineName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
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

        // Heuristic, not a dosing calculation: flags a solid oral dose form (tablet/capsule)
        // at 500mg or more for a patient flagged IsChild. It cannot know the child's actual
        // weight or age in months, so it can neither confirm nor rule out an overdose - it
        // only tells the doctor "look at this one before dispensing." A false negative is
        // possible for a smaller child on a lower-strength tablet; a false positive is
        // possible for an older, larger child for whom the adult strength is appropriate.
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

                if (TryParseLeadingMilligrams(medicine.Strength, out var mg) && mg >= adultStrengthThresholdMg)
                {
                    warnings.Add(new SafetyWarning(
                        "Pediatric Dose",
                        SafetyWarningSeverity.Warning,
                        $"{medicine.Name} ({medicine.Strength}) is an adult-strength solid dose - " +
                        "confirm this is appropriate for a child before dispensing."));
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
