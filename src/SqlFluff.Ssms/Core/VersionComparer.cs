namespace SqlFluff.Ssms.Core
{
    // Compares dotted version strings (e.g. "3.1", "3.1.0", "1.6.0") the way both UpdateInfoParser
    // (extension releases on GitHub) and PyPiPackageInfoParser (sqlfluff releases on PyPI) need to.
    // System.Version treats a missing trailing component as -1 rather than 0, so "3.1" would
    // otherwise compare as older than the equivalent "3.1.0" - this pads with 0 instead, and isn't
    // limited to System.Version's four-component cap.
    internal static class VersionComparer
    {
        // True when `latest` is a parseable version strictly newer than `installed`. Either side
        // failing to parse (e.g. a pre-release like "3.2.0a1") means "can't tell" rather than
        // "update available", so an unusual version string never falsely nags the user.
        public static bool IsNewer(string installed, string latest)
        {
            return TryParse(installed, out int[] installedParts) &&
                   TryParse(latest, out int[] latestParts) &&
                   Compare(latestParts, installedParts) > 0;
        }

        private static bool TryParse(string raw, out int[] parts)
        {
            parts = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string[] segments = raw.Trim().Split('.');
            var result = new int[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                if (!int.TryParse(segments[i], out result[i]))
                {
                    return false;
                }
            }

            parts = result;
            return true;
        }

        private static int Compare(int[] a, int[] b)
        {
            int length = a.Length > b.Length ? a.Length : b.Length;
            for (int i = 0; i < length; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                int cmp = x.CompareTo(y);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return 0;
        }
    }
}
