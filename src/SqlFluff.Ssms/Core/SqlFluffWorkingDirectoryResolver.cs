using System;
using System.IO;

namespace SqlFluff.Ssms.Core
{
    // Picks the directory sqlfluff's child process should be started in. sqlfluff has no
    // --ignore-path flag, so .sqlfluffignore (like .sqlfluff config, absent an explicit --config)
    // is only ever discovered relative to the process's working directory, not by walking up from
    // --stdin-filename independently of it. Starting the process directly in the target file's own
    // folder breaks that discovery for any file nested below wherever .sqlfluffignore/.sqlfluff
    // actually live (e.g. a "rollback/" subfolder) — so this walks up from the file looking for
    // either marker file and returns that directory instead, falling back to the file's own
    // directory when neither is found anywhere above it (a standalone file with no project
    // context, where the working directory choice doesn't matter).
    internal static class SqlFluffWorkingDirectoryResolver
    {
        public static string Resolve(string fileDirectory)
        {
            string dir = fileDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, ".sqlfluffignore")) || File.Exists(Path.Combine(dir, ".sqlfluff")))
                {
                    return dir;
                }

                string parent = Path.GetDirectoryName(dir);
                if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                {
                    break; // reached the root
                }

                dir = parent;
            }

            return fileDirectory;
        }
    }
}
