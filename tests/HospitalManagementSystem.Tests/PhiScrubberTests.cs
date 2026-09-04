using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Tests;

public class PhiScrubberTests
{
    private readonly PhiScrubber _scrubber = new();

    private static Patient MakePatient(string? fullName, string uhid = "PT-202609-0001", string? emergencyContactName = null)
    {
        return new Patient
        {
            Id = 1,
            Uhid = uhid,
            FullName = fullName,
            EmergencyContactName = emergencyContactName,
            Gender = "Male",
            BloodGroup = "O+",
            DateOfBirth = new DateTime(1990, 1, 1)
        };
    }

    [Fact]
    public void Scrub_ReplacesPatientNameWithPseudonym()
    {
        var patient = MakePatient("Rahim Uddin");
        var result = _scrubber.Scrub("Patient Rahim Uddin presented with fever.", patient);

        Assert.DoesNotContain("Rahim Uddin", result.ScrubbedText);
        Assert.Contains($"Patient-{patient.Uhid}", result.ScrubbedText);
    }

    [Fact]
    public void Scrub_ReplacesEmergencyContactNameWithPseudonym()
    {
        var patient = MakePatient("Rahim Uddin", emergencyContactName: "Karim Uddin");
        var result = _scrubber.Scrub("Contact Karim Uddin was notified.", patient);

        Assert.DoesNotContain("Karim Uddin", result.ScrubbedText);
        Assert.Contains($"Contact-{patient.Uhid}", result.ScrubbedText);
    }

    [Fact]
    public void Scrub_RedactsPhoneNumbers()
    {
        var patient = MakePatient("Rahim Uddin");
        var result = _scrubber.Scrub("Call 01712345678 for updates.", patient);

        Assert.Contains("[phone-redacted]", result.ScrubbedText);
        Assert.DoesNotContain("01712345678", result.ScrubbedText);
    }

    [Fact]
    public void Scrub_IsCaseInsensitiveForPatientName()
    {
        var patient = MakePatient("Rahim Uddin");
        var result = _scrubber.Scrub("patient RAHIM UDDIN was seen today.", patient);

        Assert.DoesNotContain("RAHIM UDDIN", result.ScrubbedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rehydrate_ReversesTheSubstitutionExactly()
    {
        var patient = MakePatient("Rahim Uddin", emergencyContactName: "Karim Uddin");
        var original = "Patient Rahim Uddin was seen; contact Karim Uddin was informed.";

        var scrubResult = _scrubber.Scrub(original, patient);
        var rehydrated = _scrubber.Rehydrate(scrubResult.ScrubbedText, scrubResult.Map);

        Assert.Equal(original, rehydrated);
    }

    [Fact]
    public void Scrub_DoesNotThrowOrPseudonymizeWhenChildPatientHasNoFullName()
    {
        // Child patients (IsChild) are titled "Baby of {EmergencyContactName}" and
        // carry a null FullName - Scrub must handle this without throwing and must
        // not emit a "Patient-" pseudonym for a name that was never there.
        var patient = MakePatient(fullName: null, emergencyContactName: "Fatema Begum");
        patient.IsChild = true;

        var exception = Record.Exception(() => _scrubber.Scrub("The baby is stable.", patient));

        Assert.Null(exception);
        var result = _scrubber.Scrub("The baby is stable.", patient);
        Assert.DoesNotContain($"Patient-{patient.Uhid}", result.ScrubbedText);
    }

    [Fact]
    public void Scrub_DoesNotCorruptUnrelatedWordsSharingThePatientsNameAsASubstring()
    {
        // A patient named "Ali" must not corrupt the unrelated word "Alia"
        // elsewhere in the same note - Scrub matches whole words only.
        var patient = MakePatient("Ali", uhid: "PT-202609-0002");
        var result = _scrubber.Scrub("Ali was seen. Alia Rahman, a different person, was not.", patient);

        Assert.Contains("Alia Rahman", result.ScrubbedText);
        Assert.Contains($"Patient-{patient.Uhid}", result.ScrubbedText);
    }
}
