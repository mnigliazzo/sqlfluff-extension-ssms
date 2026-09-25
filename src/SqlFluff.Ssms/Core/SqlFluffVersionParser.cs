using System.Text.RegularExpressions;

namespace SqlFluff.Ssms.Core
{
    // Parses `sqlfluff --version` stdout (e.g. "sqlfluff, version 3.1.2") down to just the version
    // number, so it can be compared against PyPI's latest via PyPiPackageInfoParser.IsNewer.
    internal static class SqlFluffVersionParser
    {
        private static readonly Regex VersionPattern = new Regex(@"\d+(?:\.\d+){1,3}", RegexOptions.Compiled);

        // Returns null when no dotted version number is found anywhere in the text.
        public static string Parse(string versionOutput)
        {
            if (string.IsNullOrWhiteSpace(versionOutput))
            {
                return null;
            }

            Match match = VersionPattern.Match(versionOutput);
            return match.Success ? match.Value : null;
        }
    }
}
