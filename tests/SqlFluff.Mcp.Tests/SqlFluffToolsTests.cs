using System.IO;
using SqlFluff.Mcp;
using Xunit;

namespace SqlFluff.Mcp.Tests
{
    public class SqlFluffToolsTests
    {
        [Fact]
        public void ResolveFilePathHint_ReturnsFilePath_WhenGiven()
        {
            Assert.Equal(@"C:\project\query.sql", SqlFluffTools.ResolveFilePathHint(@"C:\project\query.sql", @"C:\project"));
        }

        [Fact]
        public void ResolveFilePathHint_SynthesizesPathUnderWorkingDirectory_WhenFilePathMissing()
        {
            string result = SqlFluffTools.ResolveFilePathHint(null, @"C:\project");

            Assert.Equal(Path.Combine(@"C:\project", "ai-generated.sql"), result);
        }

        [Fact]
        public void ResolveFilePathHint_FallsBackToBareName_WhenNeitherGiven()
        {
            Assert.Equal("ai-generated.sql", SqlFluffTools.ResolveFilePathHint(null, null));
        }

        [Fact]
        public void BuildSettings_DefaultsDialectToTsql_WhenOmitted()
        {
            var settings = SqlFluffTools.BuildSettings(dialect: null, filePath: null, workingDirectory: null, configFile: null, executablePath: null);

            Assert.Equal("tsql", settings.Dialect);
        }

        [Fact]
        public void BuildSettings_UsesGivenDialect_WhenProvided()
        {
            var settings = SqlFluffTools.BuildSettings(dialect: "postgres", filePath: null, workingDirectory: null, configFile: null, executablePath: null);

            Assert.Equal("postgres", settings.Dialect);
        }

        [Fact]
        public void BuildSettings_DefaultsExecutablePathToSqlfluff_WhenOmitted()
        {
            var settings = SqlFluffTools.BuildSettings(dialect: null, filePath: null, workingDirectory: null, configFile: null, executablePath: null);

            Assert.Equal("sqlfluff", settings.ExecutablePath);
        }

        [Fact]
        public void BuildSettings_UsesGivenExecutablePath_WhenProvided()
        {
            var settings = SqlFluffTools.BuildSettings(dialect: null, filePath: null, workingDirectory: null, configFile: null, executablePath: @"C:\tools\sqlfluff.exe");

            Assert.Equal(@"C:\tools\sqlfluff.exe", settings.ExecutablePath);
        }

        [Fact]
        public void BuildSettings_FallsBackToExplicitConfigFile_WhenNoneDiscovered()
        {
            var settings = SqlFluffTools.BuildSettings(dialect: null, filePath: null, workingDirectory: null, configFile: @"C:\shared\.sqlfluff", executablePath: null);

            Assert.Equal(@"C:\shared\.sqlfluff", settings.ConfigFile);
        }
    }
}
