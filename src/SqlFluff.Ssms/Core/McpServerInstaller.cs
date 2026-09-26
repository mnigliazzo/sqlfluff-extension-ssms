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
    //
    // "Latest release" lookups go through ExtensionUpdater.GetLatestReleaseInfoAsync (shared with
    // the extension's own self-update check, so a startup with both checks enabled hits GitHub
    // once, not twice) rather than living here.
    internal static class McpServerInstaller
    {
        public static readonly string ServerDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SqlFluff.Ssms", "Mcp");

        private const string VersionMarkerFileName = ".bundle-version";
        private const string DllFileName = "SqlFluff.Mcp.dll";

        public static string DllPath => Path.Combine(ServerDirectory, DllFileName);

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

        // Downloads zipUrl, extracts it into a temp staging directory, and only once that fully
        // succeeds does it replace ServerDirectory's contents (delete old, move staging into
        // place) and record `version` as the new local marker. Staging first - rather than
        // deleting ServerDirectory up front - means a corrupt download or a failed extraction
        // never leaves a previously-working install missing; the old install is only ever touched
        // once the new one is known-good on disk. Throws McpInstallException on failure so the
        // caller can report specifically what went wrong.
        public static async Task InstallAsync(string zipUrl, string version, CancellationToken ct)
        {
            string tempZipPath = null;
            string stagingDirectory = Path.Combine(Path.GetTempPath(), "SqlFluff.Mcp-staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                try
                {
                    tempZipPath = await ExtensionUpdater.DownloadToTempFileAsync(zipUrl, "SqlFluff.Mcp", ".zip", ct).ConfigureAwait(false);
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

                try
                {
                    Directory.CreateDirectory(stagingDirectory);
                    ZipFile.ExtractToDirectory(tempZipPath, stagingDirectory);
                    File.WriteAllText(Path.Combine(stagingDirectory, VersionMarkerFileName), version);

                    // Only now that the new bundle is fully staged and verified do we touch the
                    // existing install.
                    if (Directory.Exists(ServerDirectory))
                    {
                        Directory.Delete(ServerDirectory, recursive: true);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(ServerDirectory));
                    Directory.Move(stagingDirectory, ServerDirectory);
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
                TryDelete(tempZipPath);
                TryDeleteDirectory(stagingDirectory);
            }
        }

        private static void TryDelete(string filePath)
        {
            try
            {
                if (filePath != null && File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        // Best-effort - if InstallAsync succeeded, Directory.Move already emptied stagingDirectory
        // out from under this path, so there's normally nothing left to clean up here; this only
        // does real work when installation failed partway (e.g. after extraction but before the
        // move) and left the staging directory behind.
        private static void TryDeleteDirectory(string directoryPath)
        {
            try
            {
                if (directoryPath != null && Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
