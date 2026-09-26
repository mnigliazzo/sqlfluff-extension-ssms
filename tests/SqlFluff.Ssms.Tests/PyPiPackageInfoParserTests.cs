using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class PyPiPackageInfoParserTests
    {
        [Fact]
        public void Parse_ReturnsVersion_ForARealisticResponse()
        {
            const string json = @"{
                ""info"": {
                    ""name"": ""sqlfluff"",
                    ""version"": ""3.1.2""
                },
                ""releases"": {}
            }";

            Assert.Equal("3.1.2", PyPiPackageInfoParser.Parse(json));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenVersionIsMissing()
        {
            const string json = @"{""info"": {""name"": ""sqlfluff""}}";

            Assert.Null(PyPiPackageInfoParser.Parse(json));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenInfoIsMissing()
        {
            Assert.Null(PyPiPackageInfoParser.Parse("{}"));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenJsonIsMalformed()
        {
            Assert.Null(PyPiPackageInfoParser.Parse("{\"info\": {}"));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenInputIsEmpty()
        {
            Assert.Null(PyPiPackageInfoParser.Parse(string.Empty));
            Assert.Null(PyPiPackageInfoParser.Parse(null));
        }

        [Theory]
        [InlineData("3.1.0", "3.1.2", true)]
        [InlineData("3.1.2", "3.1.2", false)]
        [InlineData("3.1.2", "3.1.0", false)]
        [InlineData("3.1.0", "3.2.0", true)]
        [InlineData("2.3.5", "3.0.0", true)]
        [InlineData("3.1", "3.1.0", false)] // sqlfluff's CLI can report a shorter version than PyPI's full one
        public void IsNewer_ComparesVersions(string installed, string latest, bool expected)
        {
            Assert.Equal(expected, PyPiPackageInfoParser.IsNewer(installed, latest));
        }

        [Theory]
        [InlineData(null, "3.1.2")]
        [InlineData("3.1.0", null)]
        [InlineData("not-a-version", "3.1.2")]
        [InlineData("3.1.0", "3.2.0a1")]
        public void IsNewer_ReturnsFalse_WhenEitherVersionIsUnparsable(string installed, string latest)
        {
            Assert.False(PyPiPackageInfoParser.IsNewer(installed, latest));
        }
    }
}
