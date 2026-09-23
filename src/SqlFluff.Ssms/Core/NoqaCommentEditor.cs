using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace SqlFluff.Ssms.Core
{
    // Inserts/merges a `-- noqa: <rule>` comment into a single line of SQL, matching sqlfluff's own
    // noqa parsing (https://docs.sqlfluff.com/en/stable/perma/noqa.html): the comment only needs to
    // be the last thing on the physical line, so appending after any unrelated trailing content is
    // safe. This is a thin wrapper around sqlfluff's own mechanism, not a new suppression system —
    // a pipeline running plain `sqlfluff lint` on the resulting line honors it identically.
    internal static class NoqaCommentEditor
    {
        private static readonly Regex NoqaTrailer = new Regex(
            @"--\s*noqa\s*(?::\s*(?<rules>[A-Za-z0-9_,\s]+))?\s*$",
            RegexOptions.IgnoreCase);

        public static string AddNoqa(string lineText, string ruleCode)
        {
            if (lineText == null)
            {
                throw new ArgumentNullException(nameof(lineText));
            }

            if (string.IsNullOrEmpty(ruleCode))
            {
                return lineText;
            }

            Match match = NoqaTrailer.Match(lineText);
            if (match.Success)
            {
                if (!match.Groups["rules"].Success)
                {
                    // Bare "-- noqa" already suppresses every rule on this line.
                    return lineText;
                }

                var codes = match.Groups["rules"].Value
                    .Split(',')
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0)
                    .ToList();

                if (codes.Any(c => string.Equals(c, ruleCode, StringComparison.OrdinalIgnoreCase)))
                {
                    return lineText;
                }

                codes.Add(ruleCode);
                return lineText.Substring(0, match.Index) + "-- noqa: " + string.Join(",", codes);
            }

            string trimmed = lineText.TrimEnd();
            string separator = trimmed.Length > 0 ? "  " : string.Empty;
            return trimmed + separator + "-- noqa: " + ruleCode;
        }
    }
}
