using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlFluff.Ssms.Core
{
    // Parses the response of PyPI's package-info API (https://pypi.org/pypi/sqlfluff/json) and
    // compares versions. Pulled out of SqlFluffInstaller so it can be unit tested without touching
    // HttpClient, mirroring UpdateInfoParser's split from ExtensionUpdater.
    internal static class PyPiPackageInfoParser
    {
        // Returns null for anything that doesn't look like a usable response (malformed JSON, no
        // version) rather than throwing — a check failing to parse should be treated the same as
        // "couldn't check", not crash the extension.
        public static string Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            PackageJson package;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(PackageJson));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    package = (PackageJson)serializer.ReadObject(ms);
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is InvalidCastException || ex is FormatException)
            {
                return null;
            }

            string version = package?.Info?.Version;
            return string.IsNullOrEmpty(version) ? null : version;
        }

        // True when `latest` is a parseable version strictly newer than `installed`. Either side
        // failing to parse (e.g. a pre-release like "3.2.0a1", which System.Version can't handle)
        // means "can't tell" rather than "update available", so an unusual version string never
        // falsely nags the user.
        public static bool IsNewer(string installed, string latest)
        {
            return Version.TryParse(installed, out Version installedVersion) &&
                   Version.TryParse(latest, out Version latestVersion) &&
                   latestVersion > installedVersion;
        }

        [DataContract]
        private sealed class PackageJson
        {
            [DataMember(Name = "info")] public InfoJson Info { get; set; }
        }

        [DataContract]
        private sealed class InfoJson
        {
            [DataMember(Name = "version")] public string Version { get; set; }
        }
    }
}
