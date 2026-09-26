namespace SqlFluff.Ssms.Core
{
    // A GitHub release relevant to update-checking: just enough to decide whether it's newer than
    // what's installed and, if so, where to send the user / what to download.
    internal sealed class UpdateInfo
    {
        public UpdateInfo(string version, string releaseUrl, string vsixDownloadUrl, string mcpZipDownloadUrl)
        {
            Version = version;
            ReleaseUrl = releaseUrl;
            VsixDownloadUrl = vsixDownloadUrl;
            McpZipDownloadUrl = mcpZipDownloadUrl;
        }

        public string Version { get; }
        public string ReleaseUrl { get; }
        public string VsixDownloadUrl { get; }

        // Null on releases published before SqlFluff.Mcp.zip started being attached (v1.14.0) -
        // callers that need it (McpServerInstaller) must handle that rather than assume presence.
        public string McpZipDownloadUrl { get; }
    }
}
