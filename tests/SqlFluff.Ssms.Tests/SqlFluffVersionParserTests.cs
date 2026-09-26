using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class SqlFluffVersionParserTests
    {
        [Theory]
        [InlineData("sqlfluff, version 3.1.2", "3.1.2")]
        [InlineData("sqlfluff, version 3.1.2\r\n", "3.1.2")]
        [InlineData("sqlfluff 3.1.2", "3.1.2")]
        [InlineData("sqlfluff, version 3.1", "3.1")]
        public void Parse_ExtractsVersion_FromRealisticOutput(string versionOutput, string expected)
        {
            Assert.Equal(expected, SqlFluffVersionParser.Parse(versionOutput));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenNoVersionNumberIsPresent()
        {
            Assert.Null(SqlFluffVersionParser.Parse("command not found"));
        }

        [Fact]
        public void Parse_ReturnsNull_WhenInputIsEmpty()
        {
            Assert.Null(SqlFluffVersionParser.Parse(string.Empty));
            Assert.Null(SqlFluffVersionParser.Parse(null));
        }
    }
}
