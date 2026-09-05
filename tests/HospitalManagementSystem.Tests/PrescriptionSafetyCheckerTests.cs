using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Tests;

public class PrescriptionSafetyCheckerTests
{
    private static readonly MedicineInfo Napa = new(1, "Napa 500mg", "Paracetamol", "500mg", 1000);
    private static readonly MedicineInfo Ace = new(2, "Ace 500mg", "Paracetamol", "500mg", 1500);
    private static readonly MedicineInfo Zithromax = new(3, "Zithromax 500mg", "Azithromycin", "500mg", 50);
    private static readonly MedicineInfo Azithral = new(4, "Azithral 500mg", "Azithromycin", "500mg", 300);
    private static readonly MedicineInfo Zmax = new(5, "Zmax 500mg", "Azithromycin", "500mg", 0);
    private static readonly MedicineInfo Seclo = new(6, "Seclo 20mg", "Omeprazole", "20mg", 600);

    private static readonly List<MedicineInfo> Formulary = new() { Napa, Ace, Zithromax, Azithral, Zmax, Seclo };

    private static List<SafetyWarning> CheckNoAllergiesNoChild(params SafetyCheckItem[] items) =>
        PrescriptionSafetyChecker.Check(patientIsChild: false, allergies: [], items: items, allMedicines: Formulary);

    [Fact]
    public void SingleItem_NoIssues_RaisesNoWarnings()
    {
        var warnings = CheckNoAllergiesNoChild(new SafetyCheckItem(Napa.Id, 10, DoseUnit.Tablet));
        Assert.Empty(warnings);
    }

    [Fact]
    public void TwoItemsSameGeneric_RaisesDuplicateTherapyWarning()
    {
        var warnings = CheckNoAllergiesNoChild(
            new SafetyCheckItem(Napa.Id, 10, DoseUnit.Tablet),
            new SafetyCheckItem(Ace.Id, 10, DoseUnit.Tablet));

        var warning = Assert.Single(warnings);
        Assert.Equal("Duplicate Therapy", warning.Category);
        Assert.Equal(SafetyWarningSeverity.Warning, warning.Severity);
        Assert.Contains("Paracetamol", warning.Message);
    }

    [Fact]
    public void ThreeItemsSameGeneric_StillRaisesOnlyOneDuplicateWarning()
    {
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: false,
            allergies: [],
            items: [
                new SafetyCheckItem(Zithromax.Id, 6, DoseUnit.Tablet),
                new SafetyCheckItem(Azithral.Id, 6, DoseUnit.Tablet),
                new SafetyCheckItem(Zmax.Id, 6, DoseUnit.Tablet)
            ],
            allMedicines: Formulary);

        Assert.Single(warnings, w => w.Category == "Duplicate Therapy");
    }

    [Fact]
    public void ItemMatchingPatientAllergyGeneric_RaisesCriticalAllergyConflict()
    {
        var allergies = new List<SafetyCheckAllergy> { new("Paracetamol", "Paracetamol", AllergySeverity.Mild) };
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: false, allergies: allergies,
            items: [new SafetyCheckItem(Napa.Id, 10, DoseUnit.Tablet)],
            allMedicines: Formulary);

        var warning = Assert.Single(warnings);
        Assert.Equal("Allergy Conflict", warning.Category);
        Assert.Equal(SafetyWarningSeverity.Critical, warning.Severity);
    }

    [Fact]
    public void AllergyWithNoAllergenGenericName_NeverMatchesAnyItem()
    {
        // Mirrors a real seeded case: Karim's Penicillin allergy has no matching formulary
        // generic, so AllergenGenericName is null and must never accidentally match.
        var allergies = new List<SafetyCheckAllergy> { new("Penicillin", null, AllergySeverity.Moderate) };
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: false, allergies: allergies,
            items: [new SafetyCheckItem(Napa.Id, 10, DoseUnit.Tablet)],
            allMedicines: Formulary);

        Assert.Empty(warnings);
    }

    [Fact]
    public void AllergyMatch_IsCaseInsensitive()
    {
        var allergies = new List<SafetyCheckAllergy> { new("azithromycin", "AZITHROMYCIN", AllergySeverity.Severe) };
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: false, allergies: allergies,
            items: [new SafetyCheckItem(Zithromax.Id, 6, DoseUnit.Tablet)],
            allMedicines: Formulary);

        Assert.Contains(warnings, w => w.Category == "Allergy Conflict");
    }

    [Fact]
    public void RequestedQuantityWithinStock_RaisesNoStockWarning()
    {
        var warnings = CheckNoAllergiesNoChild(new SafetyCheckItem(Zithromax.Id, 6, DoseUnit.Tablet));
        Assert.Empty(warnings);
    }

    [Fact]
    public void RequestedQuantityExceedsStock_RaisesWarningWithAlternatives()
    {
        // Zmax has zero stock; Zithromax and Azithral share its generic and are both
        // in stock, so both should be offered.
        var warnings = CheckNoAllergiesNoChild(new SafetyCheckItem(Zmax.Id, 1, DoseUnit.Tablet));

        var warning = Assert.Single(warnings);
        Assert.Equal("Insufficient Stock", warning.Category);
        Assert.Contains("Zithromax", warning.Message);
        Assert.Contains("Azithral", warning.Message);
    }

    [Fact]
    public void RequestedQuantityExceedsStock_NoAlternativeAvailable_SaysSo()
    {
        var isolated = new List<MedicineInfo> { Zmax }; // no in-stock sibling in this formulary
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: false, allergies: [],
            items: [new SafetyCheckItem(Zmax.Id, 1, DoseUnit.Tablet)],
            allMedicines: isolated);

        var warning = Assert.Single(warnings);
        Assert.Contains("No in-stock alternative", warning.Message);
    }

    [Fact]
    public void AdultTabletFor500mg_ForChildPatient_RaisesPediatricWarning()
    {
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: true, allergies: [],
            items: [new SafetyCheckItem(Ace.Id, 1, DoseUnit.Tablet)],
            allMedicines: Formulary);

        var warning = Assert.Single(warnings);
        Assert.Equal("Pediatric Dose", warning.Category);
    }

    [Fact]
    public void SameItem_ForAdultPatient_RaisesNoPediatricWarning()
    {
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: false, allergies: [],
            items: [new SafetyCheckItem(Ace.Id, 1, DoseUnit.Tablet)],
            allMedicines: Formulary);

        Assert.DoesNotContain(warnings, w => w.Category == "Pediatric Dose");
    }

    [Fact]
    public void LowerStrengthTablet_ForChildPatient_RaisesNoPediatricWarning()
    {
        var seclo = Seclo with { }; // 20mg, below the 500mg heuristic threshold
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: true, allergies: [],
            items: [new SafetyCheckItem(seclo.Id, 1, DoseUnit.Tablet)],
            allMedicines: Formulary);

        Assert.Empty(warnings);
    }

    [Fact]
    public void AdultStrengthLiquidForm_ForChildPatient_RaisesNoPediatricWarning()
    {
        // The heuristic only applies to solid oral forms (Tablet/Capsule) - a syrup at the
        // same nominal strength is dosed by volume, not by whole units, so it's out of scope.
        var syrup = Ace with { Id = 99, StockQuantity = 100 };
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: true, allergies: [],
            items: [new SafetyCheckItem(syrup.Id, 1, DoseUnit.Ml)],
            allMedicines: [syrup]);

        Assert.Empty(warnings);
    }

    [Fact]
    public void MizanurRahmanScenario_NapaAndAceTogether_RaisesDuplicateAndOneAllergyWarningPerItem()
    {
        // The exact seeded demo scenario: Mizanur Rahman is allergic to Paracetamol, and
        // prescribing both Napa and Ace together is also duplicate therapy - one submission,
        // three warnings: one duplicate-therapy warning, plus one allergy-conflict warning
        // for each of the two Paracetamol items (each names its own drug).
        var allergies = new List<SafetyCheckAllergy> { new("Paracetamol", "Paracetamol", AllergySeverity.Mild) };
        var warnings = PrescriptionSafetyChecker.Check(
            patientIsChild: false, allergies: allergies,
            items: [
                new SafetyCheckItem(Napa.Id, 10, DoseUnit.Tablet),
                new SafetyCheckItem(Ace.Id, 10, DoseUnit.Tablet)
            ],
            allMedicines: Formulary);

        Assert.Equal(3, warnings.Count);
        Assert.Single(warnings, w => w.Category == "Duplicate Therapy");
        var allergyWarnings = warnings.Where(w => w.Category == "Allergy Conflict").ToList();
        Assert.Equal(2, allergyWarnings.Count);
        Assert.All(allergyWarnings, w => Assert.Equal(SafetyWarningSeverity.Critical, w.Severity));
        Assert.Contains(allergyWarnings, w => w.Message.Contains("Napa"));
        Assert.Contains(allergyWarnings, w => w.Message.Contains("Ace"));
    }

    [Fact]
    public void UnknownMedicineId_IsIgnoredRatherThanThrowing()
    {
        var exception = Record.Exception(() => CheckNoAllergiesNoChild(new SafetyCheckItem(9999, 1, DoseUnit.Tablet)));
        Assert.Null(exception);
    }
}
