using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class UpdateInfoParserTests
    {
        [Fact]
        public void Parse_ReturnsUpdateInfo_ForARealisticRelease()
        {
            const string json = @"{
                ""tag_name"": ""v1.6.0"",
                ""html_url"": ""https://github.com/mnigliazzo/sqlfluff-extension-ssms/releases/tag/v1.6.0"",
                ""assets"": [
                    { ""name"": ""SqlFluff.Ssms.vsix"", ""browser_download_url"": ""https://github.com/mnigliazzo/sqlfluff-extension-ssms/releases/download/v1.6.0/SqlFluff.Ssms.vsix"" }
                ]
            }";

            UpdateInfo result = UpdateInfoParser.Parse(json);

            Assert.NotNull(result);
            Assert.Equal("1.6.0", result.Version);
            Assert.Equal("https://github.com/mnigliazzo/sqlfluff-extension-ssms/releases/tag/v1.6.0", result.ReleaseUrl);
            Assert.Equal("https://github.com/mnigliazzo/sqlfluff-extension-ssms/releases/download/v1.6.0/SqlFluff.Ssms.vsix", result.VsixDownloadUrl);
        }

        [Fact]
        public void Parse_StripsLeadingVFromTag()
        {
            const string json = @"{""tag_name"": ""v2.0.0"", ""assets"": [{""name"": ""x.vsix"", ""browser_download_url"": ""u""}]}";

            Assert.Equal("2.0.0", UpdateInfoParser.Parse(json).Version);
        }

        [Fact]
        public void Parse_IgnoresNonVsixAssetsAndPicksTheVsixOne()
        {
            const string json = @"{
                ""tag_name"": ""v1.0.0"",
                ""assets"": [
                    { ""name"": ""checksums.txt"", ""browser_download_url"": ""wrong"" },
                    { ""name"": ""SqlFluff.Ssms.vsix"", ""browser_download_url"": ""right"" }
                ]
            }";

            Assert.Equal("right", UpdateInfoParser.Parse(json).VsixDownloadUrl);
        }

        [Fact]
        public void Parse_ReturnsNull_WhenNoVsixAssetPresent()
        {
            const string json = @"{""tag_name"": ""v1.0.0"", ""assets"": [{""name"": ""readme.txt"", ""browser_download_url"": ""u""}]}";

            Assert.Null(UpdateInfoParser.Parse(json));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenTagNameIsMissing()
        {
            const string json = @"{""assets"": [{""name"": ""x.vsix"", ""browser_download_url"": ""u""}]}";

            Assert.Null(UpdateInfoParser.Parse(json));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenTagNameIsNotAVersion()
        {
            const string json = @"{""tag_name"": ""latest"", ""assets"": [{""name"": ""x.vsix"", ""browser_download_url"": ""u""}]}";

            Assert.Null(UpdateInfoParser.Parse(json));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenJsonIsMalformed()
        {
            Assert.Null(UpdateInfoParser.Parse("{\"tag_name\": \"v1.0.0\", \"assets\": [}"));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenInputIsEmpty()
        {
            Assert.Null(UpdateInfoParser.Parse(string.Empty));
            Assert.Null(UpdateInfoParser.Parse(null));
        }

        [Theory]
        [InlineData("1.5.0", "1.6.0", true)]
        [InlineData("1.5.0", "1.5.0", false)]
        [InlineData("1.6.0", "1.5.0", false)]
        [InlineData("1.5.0", "1.5.1", true)]
        [InlineData("1.5.0", "2.0.0", true)]
        public void IsNewer_ComparesSemanticVersions(string installed, string latest, bool expected)
        {
            Assert.Equal(expected, UpdateInfoParser.IsNewer(installed, latest));
        }

        [Theory]
        [InlineData(null, "1.6.0")]
        [InlineData("1.5.0", null)]
        [InlineData("not-a-version", "1.6.0")]
        [InlineData("1.5.0", "not-a-version")]
        public void IsNewer_ReturnsFalse_WhenEitherVersionIsUnparsable(string installed, string latest)
        {
            Assert.False(UpdateInfoParser.IsNewer(installed, latest));
        }
    }
}
