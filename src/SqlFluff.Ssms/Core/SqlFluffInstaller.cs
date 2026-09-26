using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlFluff.Ssms.Core
{
    // Thrown when checking PyPI for the latest sqlfluff release itself couldn't run (offline,
    // PyPI down, unexpected response) — distinct from "ran fine, nothing newer", which is a null
    // return. Mirrors UpdateCheckException in ExtensionUpdater.cs for the extension's own updates.
    internal sealed class SqlFluffUpdateCheckException : Exception
    {
        public SqlFluffUpdateCheckException(string message) : base(message) { }
    }

    // Thrown when `pip install`/`pip install --upgrade` itself couldn't even be started (no
    // Python found on PATH). A pip run that starts but fails (bad network, permissions, ...) is
    // not an exception - see PipInstallResult.
    internal sealed class SqlFluffInstallException : Exception
    {
        public SqlFluffInstallException(string message) : base(message) { }
    }

    internal sealed class PipInstallResult
    {
        public PipInstallResult(bool success, int exitCode)
        {
            Success = success;
            ExitCode = exitCode;
        }

        public bool Success { get; }
        public int ExitCode { get; }
    }

    // Installs or upgrades the sqlfluff *tool* (not the extension) via pip, and checks PyPI for
    // its latest release. Separate from SqlFluffRunner, which only ever launches an
    // already-installed sqlfluff to lint/fix/format - this is the one place that shells out to pip
    // instead.
    internal static class SqlFluffInstaller
    {
        private const string PackageInfoUrl = "https://pypi.org/pypi/sqlfluff/json";

        // Returns null when the check couldn't determine a version at all (malformed response).
        // Throws SqlFluffUpdateCheckException when the check itself couldn't run, so callers can
        // tell "couldn't tell" apart from "no version reported".
        public static async Task<string> GetLatestVersionAsync(CancellationToken ct)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SqlFluff.Ssms-UpdateCheck");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

                HttpResponseMessage response;
                try
                {
                    response = await client.GetAsync(PackageInfoUrl, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new SqlFluffUpdateCheckException("Could not reach PyPI: " + ex.Message);
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new SqlFluffUpdateCheckException("Checking PyPI for updates timed out.");
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new SqlFluffUpdateCheckException(
                            "PyPI returned " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                    }

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return PyPiPackageInfoParser.Parse(json);
                }
            }
        }

        // Runs `<python> -m pip install [--upgrade] sqlfluff`, streaming each output line to
        // onOutputLine (for the Output pane) as it arrives rather than only at the end, since an
        // install can take a while. Throws only when pip couldn't even be started; a pip run that
        // starts and fails is reported via the returned result's ExitCode instead.
        public static async Task<PipInstallResult> InstallAsync(bool upgrade, Action<string> onOutputLine, CancellationToken ct)
        {
            string python = FindPython();
            if (python == null)
            {
                throw new SqlFluffInstallException(
                    "Python was not found on PATH. Install Python from https://www.python.org/downloads/ " +
                    "(check \"Add python.exe to PATH\" during setup), then try again.");
            }

            string args = "-m pip install " + (upgrade ? "--upgrade " : string.Empty) + "sqlfluff --disable-pip-version-check";
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token))
            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) onOutputLine?.Invoke(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) onOutputLine?.Invoke(e.Data); };

                try
                {
                    process.Start();
                }
                catch (Win32Exception ex)
                {
                    throw new SqlFluffInstallException("Could not start Python (" + python + "): " + ex.Message);
                }

                process.StandardInput.Close();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (linked.Token.Register(() => TryKill(process)))
                {
                    await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);

                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }

                    if (timeout.IsCancellationRequested)
                    {
                        throw new SqlFluffInstallException("pip install timed out after 5 minutes.");
                    }

                    return new PipInstallResult(process.ExitCode == 0, process.ExitCode);
                }
            }
        }

        // Prefers the Windows 'py' launcher (what python.org's installer registers by default)
        // over a bare 'python'/'python3' on PATH, since 'py' reliably resolves to whichever
        // Python was actually installed even when PATH itself wasn't updated.
        private static string FindPython()
        {
            return SqlFluffRunner.FindOnPath("py") ??
                   SqlFluffRunner.FindOnPath("python") ??
                   SqlFluffRunner.FindOnPath("python3");
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
            catch (Win32Exception)
            {
            }
        }
    }
}
