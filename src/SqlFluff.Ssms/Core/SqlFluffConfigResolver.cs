using System;
using System.IO;

namespace SqlFluff.Ssms.Core
{
    // Decides which .sqlfluff config file (if any) to pass explicitly via --config.
    //
    // Priority:
    //   1. A .sqlfluff found by walking up from the open document's own directory — only when
    //      the document actually exists on disk (a real, saved file).
    //   2. A .sqlfluff found by walking up from the currently open folder/solution root — covers
    //      an unsaved new document (no on-disk path to walk up from at all) opened while a
    //      project folder is open in SSMS.
    //   3. The fixed path configured in Tools > Options > SQLFluff.
    //   4. None (let SQLFluff fall back to its own defaults).
    //
    // A discovered .sqlfluff (#1 or #2) always wins over the Options-configured path (#3) — the
    // idea being that a project's own config should take precedence over one global path set in
    // the extension's settings.
    //
    // This does not replace SQLFluff's own upward config discovery (driven by --stdin-filename,
    // which happens regardless when the document has a real path) — it exists because that only
    // covers case #1, and because SQLFluff has no way to let a discovered file win over a fixed
    // one when we're only ever able to pass one of the two via --config.
    internal static class SqlFluffConfigResolver
    {
        private const string ConfigFileName = ".sqlfluff";

        public static string Resolve(string documentPath, string openFolderPath, string optionsConfigFile)
        {
            string startDir = null;

            if (!string.IsNullOrEmpty(documentPath) && File.Exists(documentPath))
            {
                startDir = SafeDirectoryName(documentPath);
            }
            else if (!string.IsNullOrEmpty(openFolderPath) && Directory.Exists(openFolderPath))
            {
                startDir = openFolderPath;
            }

            string discovered = startDir != null ? FindUpward(startDir) : null;
            if (discovered != null)
            {
                return discovered;
            }

            string configured = (optionsConfigFile ?? string.Empty).Trim();
            return configured.Length > 0 ? configured : null;
        }

        private static string FindUpward(string startDir)
        {
            string dir = startDir;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(dir, ConfigFileName);
                }
                catch (ArgumentException)
                {
                    return null;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                string parent = Path.GetDirectoryName(dir);
                if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                {
                    break; // reached the root
                }

                dir = parent;
            }

            return null;
        }

        private static string SafeDirectoryName(string documentPath)
        {
            try
            {
                return Path.GetDirectoryName(Path.GetFullPath(documentPath));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is PathTooLongException || ex is NotSupportedException)
            {
                return null;
            }
        }
    }
}
