using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services.Contracts;

namespace HospitalManagementSystem.Services
{
    // Shared by the full-page (_SuggestionCard) and compact (dashboard panel) suggestion
    // renderings so the citation-marker HTML logic lives in exactly one place.
    public static class AiNarrativeRenderer
    {
        private static readonly Regex CitationPattern = new(@"\[\[rec:(\d+)\]\]", RegexOptions.Compiled);

        // The model is prompted for exactly this shape: one **bolded** headline line,
        // then bullet lines each starting with "- ". Any line that doesn't start with
        // "- " is treated as a headline paragraph - this tolerates a stray extra line
        // rather than throwing, and degrades reasonably for older rows generated before
        // this format existed (their prose renders as a single bold paragraph).
        public static IHtmlContent? RenderCitedNarrative(AiSuggestion suggestion)
        {
            if (suggestion.SuggestionType != AiSuggestionType.CaseSummary)
            {
                return null;
            }

            CaseSummaryDraft? draft;
            try
            {
                draft = JsonSerializer.Deserialize<CaseSummaryDraft>(suggestion.PayloadJson);
            }
            catch (JsonException)
            {
                return null;
            }
            if (draft == null)
            {
                return null;
            }

            var sb = new StringBuilder();
            var inList = false;
            var lines = draft.NarrativeText.Replace("\r\n", "\n").Split('\n');

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    if (!inList)
                    {
                        sb.Append("<ul class=\"mb-2 ps-3\">");
                        inList = true;
                    }
                    sb.Append("<li>").Append(RenderInline(line[2..])).Append("</li>");
                }
                else
                {
                    if (inList)
                    {
                        sb.Append("</ul>");
                        inList = false;
                    }
                    var headline = line;
                    if (headline.StartsWith("**", StringComparison.Ordinal) && headline.EndsWith("**", StringComparison.Ordinal) && headline.Length > 4)
                    {
                        headline = headline[2..^2];
                    }
                    sb.Append("<p class=\"fw-bold mb-2\">").Append(RenderInline(headline)).Append("</p>");
                }
            }
            if (inList)
            {
                sb.Append("</ul>");
            }

            return new HtmlString(sb.ToString());
        }

        // Each surviving [[rec:N]] marker was already validated server-side against the
        // records this suggestion was actually given - safe to link directly.
        private static string RenderInline(string text)
        {
            var encoded = System.Net.WebUtility.HtmlEncode(text);
            return CitationPattern.Replace(
                encoded,
                m => $"<a href=\"/MedicalRecords/Details/{m.Groups[1].Value}\" target=\"_blank\" class=\"badge bg-secondary-subtle text-secondary-emphasis border border-secondary-subtle text-decoration-none align-text-top\" title=\"Source: Record #{m.Groups[1].Value}\"><i class=\"bi bi-file-earmark-text\"></i> {m.Groups[1].Value}</a>");
        }
    }
}
