using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SqlFluff.Ssms.Core
{
    // Thrown when the update check itself couldn't run (offline, rate-limited, GitHub down,
    // unexpected response) — distinct from "ran fine, nothing newer", which is a null return.
    internal sealed class UpdateCheckException : Exception
    {
        public UpdateCheckException(string message) : base(message) { }
    }

    // Checks GitHub for a newer release of the extension itself (not sqlfluff), downloads its
    // .vsix, and hands it to whatever's registered to open one (VSIXInstaller, normally) — the
    // same thing that runs when a user double-clicks a manually downloaded .vsix (see README's
    // Install section).
    internal static class ExtensionUpdater
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/mnigliazzo/sqlfluff-extension-ssms/releases/latest";

        // Returns null when there's no newer release, or the response didn't parse into a usable
        // one. Throws UpdateCheckException when the check itself couldn't run at all, so callers
        // can tell "you're up to date" apart from "couldn't tell".
        public static async Task<UpdateInfo> CheckForNewerReleaseAsync(string installedVersion, CancellationToken ct)
        {
            string json = await FetchLatestReleaseJsonAsync(ct).ConfigureAwait(false);
            UpdateInfo latest = UpdateInfoParser.Parse(json);
            if (latest == null || !UpdateInfoParser.IsNewer(installedVersion, latest.Version))
            {
                return null;
            }

            return latest;
        }

        private static async Task<string> FetchLatestReleaseJsonAsync(CancellationToken ct)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                // A User-Agent is required by GitHub's API (unauthenticated requests without one get a 403).
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SqlFluff.Ssms-UpdateCheck");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                HttpResponseMessage response;
                try
                {
                    response = await client.GetAsync(LatestReleaseUrl, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new UpdateCheckException("Could not reach GitHub: " + ex.Message);
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    // HttpClient's own Timeout firing surfaces as a TaskCanceledException too.
                    throw new UpdateCheckException("Checking GitHub for updates timed out.");
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new UpdateCheckException(
                            "GitHub returned " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                    }

                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        // Downloads the .vsix to a temp file and returns its path. Throws on failure (unlike the
        // release check above) so the caller can tell the user the download specifically failed,
        // rather than silently doing nothing after they said "yes, install it".
        public static async Task<string> DownloadVsixAsync(string vsixDownloadUrl, CancellationToken ct)
        {
            CleanUpStaleDownloads();

            string path = Path.Combine(Path.GetTempPath(), "SqlFluff.Ssms-" + Guid.NewGuid().ToString("N") + ".vsix");

            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            using (var request = new HttpRequestMessage(HttpMethod.Get, vsixDownloadUrl))
            using (HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var destination = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    await source.CopyToAsync(destination, 81920, ct).ConfigureAwait(false);
                }
            }

            return path;
        }

        // Best-effort cleanup of .vsix files left behind by earlier downloads (the installer was
        // never launched, failed to launch, or the download itself was interrupted) so they don't
        // accumulate indefinitely in %TEMP%. Run right before starting a new download rather than
        // after launching the installer, since the installer still needs its own file to exist
        // for as long as it's running (which can be a while — it may wait on SSMS to close first).
        private static void CleanUpStaleDownloads()
        {
            try
            {
                foreach (string file in Directory.GetFiles(Path.GetTempPath(), "SqlFluff.Ssms-*.vsix"))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // Still in use (e.g. an installer from a previous download is still running) - leave it.
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
        }

        public static void LaunchInstaller(string vsixPath)
        {
            Process.Start(new ProcessStartInfo(vsixPath) { UseShellExecute = true });
        }
    }
}
