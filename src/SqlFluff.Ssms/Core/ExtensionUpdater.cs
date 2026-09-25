using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SqlFluff.Ssms.Core
{
    // Checks GitHub for a newer release of the extension itself (not sqlfluff), downloads its
    // .vsix, and hands it to whatever's registered to open one (VSIXInstaller, normally) — the
    // same thing that runs when a user double-clicks a manually downloaded .vsix (see README's
    // Install section).
    internal static class ExtensionUpdater
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/mnigliazzo/sqlfluff-extension-ssms/releases/latest";

        // Returns null when there's no newer release, or the check itself failed (offline,
        // rate-limited, GitHub down, unexpected response). Callers can't tell those apart and
        // don't need to: either way there's nothing actionable to show the user.
        public static async Task<UpdateInfo> CheckForNewerReleaseAsync(string installedVersion, CancellationToken ct)
        {
            string json = await FetchLatestReleaseJsonAsync(ct).ConfigureAwait(false);
            if (json == null)
            {
                return null;
            }

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

                try
                {
                    using (HttpResponseMessage response = await client.GetAsync(LatestReleaseUrl, ct).ConfigureAwait(false))
                    {
                        return response.IsSuccessStatusCode
                            ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                            : null;
                    }
                }
                catch (HttpRequestException)
                {
                    return null;
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    // HttpClient's own Timeout firing surfaces as a TaskCanceledException too.
                    return null;
                }
            }
        }

        // Downloads the .vsix to a temp file and returns its path. Throws on failure (unlike the
        // release check above) so the caller can tell the user the download specifically failed,
        // rather than silently doing nothing after they said "yes, install it".
        public static async Task<string> DownloadVsixAsync(string vsixDownloadUrl, CancellationToken ct)
        {
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

        public static void LaunchInstaller(string vsixPath)
        {
            Process.Start(new ProcessStartInfo(vsixPath) { UseShellExecute = true });
        }
    }
}
