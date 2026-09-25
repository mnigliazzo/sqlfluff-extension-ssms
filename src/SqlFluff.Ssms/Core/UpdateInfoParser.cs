using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlFluff.Ssms.Core
{
    // Parses the response of GitHub's "get the latest release" API
    // (https://api.github.com/repos/<owner>/<repo>/releases/latest) and compares versions. Pulled
    // out of ExtensionUpdater so it can be unit tested without touching HttpClient/Process.
    internal static class UpdateInfoParser
    {
        // Returns null for anything that doesn't look like a usable release (malformed JSON, no
        // tag, no .vsix asset) rather than throwing — an update check failing to parse should be
        // treated the same as "couldn't check", not crash the extension.
        public static UpdateInfo Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            ReleaseJson release;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(ReleaseJson));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    release = (ReleaseJson)serializer.ReadObject(ms);
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is InvalidCastException || ex is FormatException)
            {
                return null;
            }

            string version = NormalizeVersion(release?.TagName);
            string vsixUrl = release?.Assets?.FirstOrDefault(
                a => a.Name != null && a.Name.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl;

            if (version == null || string.IsNullOrEmpty(vsixUrl))
            {
                return null;
            }

            return new UpdateInfo(version, release.HtmlUrl, vsixUrl);
        }

        // True when `latest` is a parseable version strictly newer than `installed`. Either side
        // failing to parse means "can't tell" rather than "update available", so a malformed tag
        // or an unexpected assembly version never falsely nags the user.
        public static bool IsNewer(string installed, string latest)
        {
            return Version.TryParse(installed, out Version installedVersion) &&
                   Version.TryParse(latest, out Version latestVersion) &&
                   latestVersion > installedVersion;
        }

        // Release tags in this repo are always "vX.Y.Z" (see CLAUDE.md's Release process). Strips
        // the leading 'v'; returns null for anything that doesn't parse as a Version.
        private static string NormalizeVersion(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return null;
            }

            string trimmed = tagName.TrimStart('v', 'V');
            return Version.TryParse(trimmed, out _) ? trimmed : null;
        }

        [DataContract]
        private sealed class ReleaseJson
        {
            [DataMember(Name = "tag_name")] public string TagName { get; set; }
            [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
            [DataMember(Name = "assets")] public AssetJson[] Assets { get; set; }
        }

        [DataContract]
        private sealed class AssetJson
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "browser_download_url")] public string BrowserDownloadUrl { get; set; }
        }
    }
}
