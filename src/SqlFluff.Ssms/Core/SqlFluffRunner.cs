using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlFluff.Ssms.Core
{
    internal sealed class SqlFluffException : Exception
    {
        public SqlFluffException(string message) : base(message) { }
    }

    internal static class SqlFluffRunner
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(2);
        private static readonly object CacheLock = new object();
        private static Launch _cachedLaunch;

        public static async Task<IReadOnlyList<LintViolation>> LintAsync(
            string text, string filePath, SqlFluffSettings settings, CancellationToken ct)
        {
            ProcessResult result = await RunAsync("lint", text, filePath, settings, ct).ConfigureAwait(false);

            // lint: 0 = clean, 1 = violations found, anything else = tool failure.
            if (result.ExitCode > 1)
            {
                throw new SqlFluffException(Describe(result));
            }

            try
            {
                return LintJsonParser.Parse(result.StdOut);
            }
            catch (LintJsonParseException ex)
            {
                throw new SqlFluffException(ex.Message + StderrSuffix(result));
            }
        }

        public static Task<string> FixAsync(
            string text, string filePath, SqlFluffSettings settings, CancellationToken ct)
        {
            return RewriteAsync("fix", text, filePath, settings, ct);
        }

        public static Task<string> FormatAsync(
            string text, string filePath, SqlFluffSettings settings, CancellationToken ct)
        {
            return RewriteAsync("format", text, filePath, settings, ct);
        }

        private static async Task<string> RewriteAsync(
            string verb, string text, string filePath, SqlFluffSettings settings, CancellationToken ct)
        {
            ProcessResult result = await RunAsync(verb, text, filePath, settings, ct).ConfigureAwait(false);

            // fix/format: exit code 1 only means "some violations could not be fixed"; stdout still holds the rewritten SQL.
            if (result.ExitCode > 1)
            {
                throw new SqlFluffException(Describe(result));
            }

            if (result.StdOut.Length == 0 && text.Trim().Length > 0)
            {
                throw new SqlFluffException("SQLFluff returned no output; the document was left unchanged." + StderrSuffix(result));
            }

            return result.StdOut;
        }

        public static void ResetCache()
        {
            lock (CacheLock)
            {
                _cachedLaunch = null;
            }
        }

        // Returns null when sqlfluff is reachable and runnable, otherwise a user-facing description of the problem.
        public static async Task<string> CheckAvailabilityAsync(SqlFluffSettings settings, CancellationToken ct)
        {
            Launch launch;
            try
            {
                launch = Resolve(settings);
            }
            catch (SqlFluffException ex)
            {
                return ex.Message;
            }

            var psi = new ProcessStartInfo
            {
                FileName = launch.FileName,
                Arguments = (string.IsNullOrEmpty(launch.PrefixArguments) ? string.Empty : launch.PrefixArguments + " ") + "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };

            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token))
                using (var process = new Process { StartInfo = psi })
                {
                    process.Start();
                    process.StandardInput.Close();

                    using (linked.Token.Register(() => TryKill(process)))
                    {
                        string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                        await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);

                        if (timeout.IsCancellationRequested)
                        {
                            return "Checking for SQLFluff timed out (" + launch.FileName + ").";
                        }

                        if (process.ExitCode != 0)
                        {
                            ResetCache();
                            string detail = stderr.Trim();
                            return "SQLFluff did not run correctly (" + launch.FileName + ")" +
                                   (detail.Length > 0 ? ": " + detail : ".");
                        }

                        return null;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                ResetCache();
                return "Could not start SQLFluff (" + launch.FileName + "): " + ex.Message +
                       ". Install it with 'pip install sqlfluff' or set its path in Tools > Options > SQLFluff.";
            }
        }

        private static async Task<ProcessResult> RunAsync(
            string verb, string text, string filePath, SqlFluffSettings settings, CancellationToken ct)
        {
            Launch launch = Resolve(settings);
            string arguments = BuildArguments(verb, launch, settings, filePath);

            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = launch.FileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false),
                    WorkingDirectory = PickWorkingDirectory(filePath),
                };
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, settings.TimeoutSeconds))))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token))
                using (var process = new Process { StartInfo = psi })
                {
                    try
                    {
                        process.Start();
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        ResetCache();
                        throw new SqlFluffException("Could not start SQLFluff (" + launch.FileName + "): " + ex.Message);
                    }

                    using (linked.Token.Register(() => TryKill(process)))
                    {
                        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                        try
                        {
                            // net48 has no StandardInputEncoding, so write UTF-8 bytes (no BOM) to the raw stream.
                            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                            await process.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length, linked.Token).ConfigureAwait(false);
                            process.StandardInput.Close();
                        }
                        catch (IOException)
                        {
                            // The process exited before reading all input; its exit code and stderr explain why.
                        }

                        await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);

                        if (ct.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(ct);
                        }

                        if (timeout.IsCancellationRequested)
                        {
                            throw new SqlFluffException("SQLFluff timed out after " + Math.Max(5, settings.TimeoutSeconds) + " seconds.");
                        }

                        return new ProcessResult
                        {
                            ExitCode = process.ExitCode,
                            StdOut = await stdoutTask.ConfigureAwait(false),
                            StdErr = await stderrTask.ConfigureAwait(false),
                        };
                    }
                }
            }
            finally
            {
                Gate.Release();
            }
        }

        private static string BuildArguments(string verb, Launch launch, SqlFluffSettings s, string filePath)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(launch.PrefixArguments))
            {
                sb.Append(launch.PrefixArguments).Append(' ');
            }

            sb.Append(verb);
            if (verb == "lint")
            {
                sb.Append(" --format json");
            }

            sb.Append(" --disable-progress-bar");
            AppendOption(sb, "--dialect", s.Dialect);
            AppendOption(sb, "--config", s.ConfigFile);
            AppendOption(sb, "--rules", s.Rules);
            AppendOption(sb, "--exclude-rules", s.ExcludeRules);
            AppendOption(sb, "--stdin-filename", filePath);
            sb.Append(" -");
            return sb.ToString();
        }

        private static void AppendOption(StringBuilder sb, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sb.Append(' ').Append(name).Append(' ').Append(ArgumentQuoting.Quote(value.Trim()));
            }
        }

        private static string PickWorkingDirectory(string filePath)
        {
            try
            {
                string dir = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    return dir;
                }
            }
            catch (ArgumentException)
            {
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        private static string Describe(ProcessResult r)
        {
            string detail = r.StdErr.Trim();
            if (detail.Length == 0)
            {
                detail = r.StdOut.Trim();
            }

            if (detail.Length > 1500)
            {
                detail = detail.Substring(0, 1500) + "...";
            }

            return "SQLFluff failed (exit code " + r.ExitCode + ")" + (detail.Length > 0 ? ": " + detail : ".");
        }

        private static string StderrSuffix(ProcessResult r)
        {
            string detail = r.StdErr.Trim();
            return detail.Length == 0 ? string.Empty : " " + detail;
        }

        // ---- Executable discovery -------------------------------------------------------------

        private static Launch Resolve(SqlFluffSettings s)
        {
            string configured = (s.ExecutablePath ?? string.Empty).Trim().Trim('"');
            bool isDefault = configured.Length == 0 ||
                             string.Equals(configured, "sqlfluff", StringComparison.OrdinalIgnoreCase);

            if (!isDefault)
            {
                string resolved = File.Exists(configured) ? configured : FindOnPath(configured);
                if (resolved == null)
                {
                    throw new SqlFluffException("The configured SQLFluff executable was not found: " + configured +
                                                ". Check Tools > Options > SQLFluff > General.");
                }

                return new Launch(resolved, null);
            }

            lock (CacheLock)
            {
                if (_cachedLaunch != null)
                {
                    return _cachedLaunch;
                }
            }

            string exe = FindOnPath("sqlfluff") ?? FindInPythonScriptDirectories();
            Launch launch = null;
            if (exe != null)
            {
                launch = new Launch(exe, null);
            }
            else
            {
                string py = FindOnPath("py");
                if (py != null)
                {
                    launch = new Launch(py, "-m sqlfluff");
                }
            }

            if (launch == null)
            {
                throw new SqlFluffException(
                    "SQLFluff was not found. Install it with 'pip install sqlfluff' or set its full path in " +
                    "Tools > Options > SQLFluff > General.");
            }

            lock (CacheLock)
            {
                _cachedLaunch = launch;
            }

            return launch;
        }

        private static string FindOnPath(string name)
        {
            string[] extensions = Path.HasExtension(name) ? new[] { string.Empty } : new[] { ".exe", ".cmd", ".bat" };
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string ext in extensions)
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim().Trim('"'), name + ext);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }

            return null;
        }

        private static string FindInPythonScriptDirectories()
        {
            var roots = new List<string>
            {
                Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? string.Empty, "Python"),
                Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty, "Programs", "Python"),
                Environment.GetEnvironmentVariable("ProgramFiles") ?? string.Empty,
            };

            foreach (string root in roots.Where(r => r.Length > 0 && Directory.Exists(r)))
            {
                // Newest Python first: Python313 sorts after Python39 only lexically, so order by numeric suffix.
                IEnumerable<string> pythonDirs = Directory.GetDirectories(root, "Python*")
                    .OrderByDescending(d => VersionKey(Path.GetFileName(d)));

                foreach (string dir in pythonDirs)
                {
                    string candidate = Path.Combine(dir, "Scripts", "sqlfluff.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static int VersionKey(string dirName)
        {
            string digits = new string(dirName.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int n) ? n : 0;
        }

        // ---- Types ----------------------------------------------------------------------------

        private sealed class Launch
        {
            public Launch(string fileName, string prefixArguments)
            {
                FileName = fileName;
                PrefixArguments = prefixArguments;
            }

            public string FileName { get; }
            public string PrefixArguments { get; }
        }

        private sealed class ProcessResult
        {
            public int ExitCode { get; set; }
            public string StdOut { get; set; }
            public string StdErr { get; set; }
        }
    }
}
