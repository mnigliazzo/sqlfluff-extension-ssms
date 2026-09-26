using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SqlFluff.Ssms.Core
{
    // Thrown when downloading/extracting the SqlFluff.Mcp.zip release asset fails - distinct from
    // UpdateCheckException (the "latest release" lookup itself failing).
    internal sealed class McpInstallException : Exception
    {
        public McpInstallException(string message) : base(message) { }
    }

    // Downloads the SqlFluff.Mcp.zip attached to the latest GitHub release (see release.yml,
    // added in v1.14.0) and extracts it into a stable, version-independent location this
    // extension manages: %LocalAppData%\SqlFluff.Ssms\Mcp. That location is deliberately NOT
    // this VSIX's own install directory - that path is version/instance-specific and changes on
    // every update (including this extension's own self-update), which would silently break an
    // .mcp.json entry pointing at it. See README's "AI assistant integration (MCP)" section.
    internal static class McpServerInstaller
    {
        public static readonly string ServerDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SqlFluff.Ssms", "Mcp");

        private const string VersionMarkerFileName = ".bundle-version";
        private const string DllFileName = "SqlFluff.Mcp.dll";

        public static string DllPath => Path.Combine(ServerDirectory, DllFileName);

        // Returns the latest release's info by reusing ExtensionUpdater's fetch/parse pipeline.
        // Unlike ExtensionUpdater.CheckForNewerReleaseAsync, this is NOT filtered by "newer than
        // the installed extension version" - the Mcp bundle's freshness is judged against a
        // separately tracked local marker (ReadLocalVersion), not this extension's own assembly
        // version. Throws UpdateCheckException when the check itself couldn't run (same as
        // ExtensionUpdater); returns null when the latest release doesn't parse as usable at all.
        public static async Task<UpdateInfo> GetLatestReleaseInfoAsync(CancellationToken ct)
        {
            string json = await ExtensionUpdater.FetchLatestReleaseJsonAsync(ct).ConfigureAwait(false);
            return UpdateInfoParser.Parse(json);
        }

        // The release version SqlFluff.Mcp.dll was last extracted from, or null if it's never
        // been installed (or the marker can't be read).
        public static string ReadLocalVersion()
        {
            string path = Path.Combine(ServerDirectory, VersionMarkerFileName);
            try
            {
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        // Downloads zipUrl and extracts it into ServerDirectory, replacing whatever was there
        // (a stale extraction from an older version) rather than merging into it, then records
        // `version` as the new local marker. Throws McpInstallException on failure so the caller
        // can report specifically what went wrong, rather than leaving a half-updated directory
        // and pretending nothing happened.
        public static async Task InstallAsync(string zipUrl, string version, CancellationToken ct)
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), "SqlFluff.Mcp-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                await DownloadAsync(zipUrl, tempZipPath, ct).ConfigureAwait(false);

                try
                {
                    if (Directory.Exists(ServerDirectory))
                    {
                        Directory.Delete(ServerDirectory, recursive: true);
                    }

                    Directory.CreateDirectory(ServerDirectory);
                    ZipFile.ExtractToDirectory(tempZipPath, ServerDirectory);

                    File.WriteAllText(Path.Combine(ServerDirectory, VersionMarkerFileName), version);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new McpInstallException("Could not write to " + ServerDirectory + ": " + ex.Message);
                }
                catch (InvalidDataException ex)
                {
                    throw new McpInstallException("SqlFluff.Mcp.zip could not be extracted (corrupt download): " + ex.Message);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZipPath))
                    {
                        File.Delete(tempZipPath);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
        }

        private static async Task DownloadAsync(string url, string destinationPath, CancellationToken ct)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
                    {
                        await source.CopyToAsync(destination, 81920, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                throw new McpInstallException("Could not download SqlFluff.Mcp.zip: " + ex.Message);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // HttpClient's own Timeout firing surfaces as a TaskCanceledException too.
                throw new McpInstallException("Downloading SqlFluff.Mcp.zip timed out.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new McpInstallException("Could not save SqlFluff.Mcp.zip: " + ex.Message);
            }
        }
    }
}
