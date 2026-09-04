using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    public record ScrubResult(string ScrubbedText, IReadOnlyDictionary<string, string> Map);

    // Replaces identifying details with stable pseudonyms before text leaves the process
    // for an external model call, and reverses the substitution on the way back. The
    // mapping only needs to be stable within one scrub/rehydrate round trip, not across
    // requests - each call builds and returns its own map.
    public class PhiScrubber
    {
        private static readonly Regex PhonePattern = new(@"\b\d{10,11}\b", RegexOptions.Compiled);

        public ScrubResult Scrub(string text, Patient patient)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var scrubbed = text;

            if (!string.IsNullOrWhiteSpace(patient.FullName))
            {
                var pseudonym = $"Patient-{patient.Uhid}";
                map[pseudonym] = patient.FullName;
                scrubbed = ReplaceWholeWord(scrubbed, patient.FullName, pseudonym);
            }

            if (!string.IsNullOrWhiteSpace(patient.EmergencyContactName))
            {
                var pseudonym = $"Contact-{patient.Uhid}";
                map[pseudonym] = patient.EmergencyContactName;
                scrubbed = ReplaceWholeWord(scrubbed, patient.EmergencyContactName, pseudonym);
            }

            scrubbed = PhonePattern.Replace(scrubbed, "[phone-redacted]");

            return new ScrubResult(scrubbed, map);
        }

        // A plain string.Replace matches inside longer words - a patient named "Ali"
        // would corrupt the unrelated word "Alia" elsewhere in the same note. Word
        // boundaries confine the match to the whole name, not a substring of it.
        private static string ReplaceWholeWord(string text, string word, string replacement)
        {
            var pattern = $@"\b{Regex.Escape(word)}\b";
            return Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }

        public string Rehydrate(string text, IReadOnlyDictionary<string, string> map)
        {
            foreach (var (pseudonym, real) in map)
            {
                text = text.Replace(pseudonym, real, StringComparison.OrdinalIgnoreCase);
            }
            return text;
        }
    }
}
