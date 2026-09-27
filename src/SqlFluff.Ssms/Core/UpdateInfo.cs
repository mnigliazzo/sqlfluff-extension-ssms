namespace SqlFluff.Ssms.Core
{
    // A GitHub release relevant to update-checking: just enough to decide whether it's newer than
    // what's installed and, if so, where to send the user / what to download.
    internal sealed class UpdateInfo
    {
        public UpdateInfo(string version, string releaseUrl, string vsixDownloadUrl)
        {
            Version = version;
            ReleaseUrl = releaseUrl;
            VsixDownloadUrl = vsixDownloadUrl;
        }

        public string Version { get; }
        public string ReleaseUrl { get; }
        public string VsixDownloadUrl { get; }
    }
}
