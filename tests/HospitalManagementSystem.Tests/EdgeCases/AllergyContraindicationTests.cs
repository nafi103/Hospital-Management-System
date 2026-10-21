using System;
using System.Collections.Generic;
using System.Linq;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using Xunit;

namespace HospitalManagementSystem.Tests.EdgeCases
{
    public class AllergyContraindicationTests
    {
        private static readonly MedicineInfo AmoxClav = new(10, "Augmentin 625mg", "Amoxicillin and Clavulanic Acid", "625mg", 100);
        private static readonly MedicineInfo PenicillinV = new(11, "Pen-VK 250mg", "Penicillin V", "250mg", 50);
        private static readonly MedicineInfo Cipro = new(12, "Cipro 500mg", "Ciprofloxacin", "500mg", 80);
        private static readonly MedicineInfo Bactrim = new(13, "Bactrim DS", "Sulfamethoxazole and Trimethoprim", "800/160mg", 40);
        private static readonly MedicineInfo AspirinBrand = new(14, "Ecotrin 81mg", "Aspirin", "81mg", 200);

        private static readonly List<MedicineInfo> Formulary = new() { AmoxClav, PenicillinV, Cipro, Bactrim, AspirinBrand };

        [Theory]
        [InlineData("penicillin", "Penicillin")]
        [InlineData("PENICILLIN", "penicillin")]
        [InlineData("Penicillin", "PENICILLIN")]
        public void Check_CaseInsensitiveGenericMatching_RaisesCriticalConflict(string allergenGeneric, string prescribedGeneric)
        {
            var med = new MedicineInfo(1, "Test Med", prescribedGeneric, "100mg", 50);
            var allergies = new List<SafetyCheckAllergy>
            {
                new("Penicillin allergy", allergenGeneric, AllergySeverity.Severe)
            };

            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: false,
                allergies: allergies,
                items: new[] { new SafetyCheckItem(1, 10, DoseUnit.Tablet) },
                allMedicines: new[] { med });

            var warning = Assert.Single(warnings, w => w.Category == "Allergy Conflict");
            Assert.Equal(SafetyWarningSeverity.Critical, warning.Severity);
            Assert.Contains("Severe", warning.Message);
        }

        [Fact]
        public void Check_FreeTextSubstanceBidirectionalMatch_AllergenShorterThanGeneric_RaisesConflict()
        {
            // Allergy substance is "Sulfa", generic is "Sulfamethoxazole and Trimethoprim"
            var allergies = new List<SafetyCheckAllergy>
            {
                new("Sulfa", null, AllergySeverity.Moderate)
            };

            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: false,
                allergies: allergies,
                items: new[] { new SafetyCheckItem(Bactrim.Id, 14, DoseUnit.Tablet) },
                allMedicines: Formulary);

            var warning = Assert.Single(warnings, w => w.Category == "Allergy Conflict");
            Assert.Equal(SafetyWarningSeverity.Critical, warning.Severity);
            Assert.Contains("Sulfa", warning.Message);
            Assert.Contains("Bactrim DS", warning.Message);
        }

        [Fact]
        public void Check_FreeTextSubstanceBidirectionalMatch_AllergenLongerThanGeneric_RaisesConflict()
        {
            // Allergy substance is "Aspirin 81mg Enteric Coated", generic is "Aspirin"
            var allergies = new List<SafetyCheckAllergy>
            {
                new("Aspirin 81mg Enteric Coated", null, AllergySeverity.Severe)
            };

            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: false,
                allergies: allergies,
                items: new[] { new SafetyCheckItem(AspirinBrand.Id, 30, DoseUnit.Tablet) },
                allMedicines: Formulary);

            var warning = Assert.Single(warnings, w => w.Category == "Allergy Conflict");
            Assert.Equal(SafetyWarningSeverity.Critical, warning.Severity);
            Assert.Contains("Aspirin", warning.Message);
        }

        [Fact]
        public void Check_BrandNameSubstanceMatch_RaisesConflictEvenWhenGenericDiffers()
        {
            // Allergy recorded by brand name e.g. "Augmentin" with no generic entered
            var allergies = new List<SafetyCheckAllergy>
            {
                new("Augmentin", null, AllergySeverity.Severe)
            };

            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: false,
                allergies: allergies,
                items: new[] { new SafetyCheckItem(AmoxClav.Id, 14, DoseUnit.Tablet) },
                allMedicines: Formulary);

            var warning = Assert.Single(warnings, w => w.Category == "Allergy Conflict");
            Assert.Equal(SafetyWarningSeverity.Critical, warning.Severity);
            Assert.Contains("Augmentin", warning.Message);
        }

        [Fact]
        public void Check_UnrelatedMedicine_RaisesNoAllergyWarning()
        {
            var allergies = new List<SafetyCheckAllergy>
            {
                new("Penicillin", "Penicillin", AllergySeverity.Severe)
            };

            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: false,
                allergies: allergies,
                items: new[] { new SafetyCheckItem(Cipro.Id, 10, DoseUnit.Tablet) },
                allMedicines: Formulary);

            Assert.DoesNotContain(warnings, w => w.Category == "Allergy Conflict");
        }

        [Fact]
        public void Check_ActiveMedicationAllergyConflict_TriggersWarningForPriorPrescription()
        {
            // Patient was prescribed Penicillin in a prior prescription, and today has a documented Penicillin allergy
            var allergies = new List<SafetyCheckAllergy>
            {
                new("Penicillin", "Penicillin V", AllergySeverity.Severe)
            };

            var activeMeds = new List<ActiveMedicationInfo>
            {
                new(PenicillinV.Id, PenicillinV.Name, PenicillinV.GenericName, DateTime.UtcNow.AddDays(-2), 7, 105)
            };

            // Today's new prescription is just Cipro (safe)
            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: false,
                allergies: allergies,
                items: new[] { new SafetyCheckItem(Cipro.Id, 10, DoseUnit.Tablet) },
                allMedicines: Formulary,
                activeMedications: activeMeds);

            var warning = Assert.Single(warnings, w => w.Category == "Allergy Conflict");
            Assert.Equal(SafetyWarningSeverity.Critical, warning.Severity);
            Assert.Contains("Active dispensed medication Pen-VK 250mg", warning.Message);
            Assert.Contains("Prescription #105", warning.Message);
        }

        [Fact]
        public void Check_PenicillinAllergy_FlagsAmoxicillinCrossReactivity()
        {
            // Patient has a documented allergy to "Penicillin"
            var allergies = new List<SafetyCheckAllergy>
            {
                new("Penicillin", "Penicillin", AllergySeverity.Severe)
            };

            // Doctor prescribes Augmentin (Amoxicillin and Clavulanic Acid)
            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: false,
                allergies: allergies,
                items: new[] { new SafetyCheckItem(AmoxClav.Id, 14, DoseUnit.Tablet) },
                allMedicines: Formulary);

            var warning = Assert.Single(warnings, w => w.Category == "Allergy Conflict");
            Assert.Equal(SafetyWarningSeverity.Critical, warning.Severity);
            Assert.Contains("Augmentin", warning.Message);
        }

        [Fact]
        public void Check_SulfaAllergy_DoesNotFlagMorphineSulfate()
        {
            // Patient has a documented allergy to "Sulfa" (sulfonamide antibiotic)
            var allergies = new List<SafetyCheckAllergy>
            {
                new("Sulfa", null, AllergySeverity.Severe)
            };

            // Formulary includes Morphine Sulfate (chemical salt, NOT a sulfonamide antibiotic)
            var morphineSulfate = new MedicineInfo(50, "Morphine Sulfate 10mg", "Morphine Sulfate", "10mg", 100);
            var medicines = new List<MedicineInfo>(Formulary) { morphineSulfate };

            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: false,
                allergies: allergies,
                items: new[] { new SafetyCheckItem(morphineSulfate.Id, 10, DoseUnit.Tablet) },
                allMedicines: medicines);

            // Should NOT trigger a false positive allergy warning
            Assert.DoesNotContain(warnings, w => w.Category == "Allergy Conflict");
        }

        [Fact]
        public void Check_SulfaAllergy_DoesNotFlagSalbutamolSulfate()
        {
            var allergies = new List<SafetyCheckAllergy>
            {
                new("Sulfa", null, AllergySeverity.Severe)
            };

            var salbutamolSulfate = new MedicineInfo(51, "Ventolin 2mg", "Salbutamol Sulfate", "2mg", 100);
            var medicines = new List<MedicineInfo>(Formulary) { salbutamolSulfate };

            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: false,
                allergies: allergies,
                items: new[] { new SafetyCheckItem(salbutamolSulfate.Id, 10, DoseUnit.Tablet) },
                allMedicines: medicines);

            Assert.DoesNotContain(warnings, w => w.Category == "Allergy Conflict");
        }

        [Fact]
        public void Check_PotentPediatricDrug_FlagsLowerThreshold()
        {
            // Potent drug (Morphine Sulfate 10mg) prescribed to a child (10mg < 500mg adult threshold)
            var morphineSulfate = new MedicineInfo(50, "Morphine Sulfate 10mg", "Morphine Sulfate", "10mg", 100);
            var medicines = new List<MedicineInfo>(Formulary) { morphineSulfate };

            var warnings = PrescriptionSafetyChecker.Check(
                patientIsChild: true,
                allergies: [],
                items: new[] { new SafetyCheckItem(morphineSulfate.Id, 10, DoseUnit.Tablet) },
                allMedicines: medicines);

            var warning = Assert.Single(warnings, w => w.Category == "Pediatric Dose");
            Assert.Equal(SafetyWarningSeverity.Warning, warning.Severity);
            Assert.Contains("potent/narrow-index pediatric medication", warning.Message);
        }
    }
}
