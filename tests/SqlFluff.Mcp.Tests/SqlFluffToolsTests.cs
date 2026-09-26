using System;
using System.IO;
using ModelContextProtocol;
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

        [Fact]
        public void BuildSettings_DiscoversConfigFromFilePathsDirectory_WhenFileDoesNotExistYet()
        {
            string dir = Directory.CreateTempSubdirectory("sqlfluff-mcp-tests-").FullName;
            try
            {
                string configPath = Path.Combine(dir, ".sqlfluff");
                File.WriteAllText(configPath, "[sqlfluff]\n");

                string notYetSavedFile = Path.Combine(dir, "new_query.sql");
                var settings = SqlFluffTools.BuildSettings(
                    dialect: null, filePath: notYetSavedFile, workingDirectory: null, configFile: null, executablePath: null);

                Assert.Equal(configPath, settings.ConfigFile);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void EffectiveWorkingDirectory_UsesFilePathsDirectory_WhenFileDoesNotExistAndNoWorkingDirectoryGiven()
        {
            string dir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            string notYetSavedFile = Path.Combine(dir, "definitely-does-not-exist.sql");

            Assert.Equal(dir, SqlFluffTools.EffectiveWorkingDirectory(notYetSavedFile, null));
        }

        [Fact]
        public void EffectiveWorkingDirectory_PrefersFilePathsDirectory_OverExplicitWorkingDirectory_WhenItExists()
        {
            string dir = Directory.CreateTempSubdirectory("sqlfluff-mcp-tests-").FullName;
            try
            {
                string notYetSavedFile = Path.Combine(dir, "new_query.sql");

                Assert.Equal(dir, SqlFluffTools.EffectiveWorkingDirectory(notYetSavedFile, "/some/unrelated/workingDirectory"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void EffectiveWorkingDirectory_FallsBackToWorkingDirectory_WhenFilePathsDirectoryAlsoDoesNotExist()
        {
            string doesNotExist = OperatingSystem.IsWindows()
                ? @"C:\definitely\does\not\exist\query.sql"
                : "/definitely/does/not/exist/query.sql";

            Assert.Equal("/explicit", SqlFluffTools.EffectiveWorkingDirectory(doesNotExist, "/explicit"));
        }

        [Fact]
        public void EffectiveWorkingDirectory_ReturnsNull_WhenNeitherGiven()
        {
            Assert.Null(SqlFluffTools.EffectiveWorkingDirectory(null, null));
        }

        [Fact]
        public void RequireAbsoluteIfGiven_Accepts_FullyQualifiedPath()
        {
            string absolute = OperatingSystem.IsWindows() ? @"C:\project\query.sql" : "/project/query.sql";
            SqlFluffTools.RequireAbsoluteIfGiven(absolute, "filePath");
        }

        [Fact]
        public void RequireAbsoluteIfGiven_Accepts_Null()
        {
            SqlFluffTools.RequireAbsoluteIfGiven(null, "filePath");
        }

        [Fact]
        public void RequireAbsoluteIfGiven_Rejects_RelativePath()
        {
            McpException ex = Assert.Throws<McpException>(() => SqlFluffTools.RequireAbsoluteIfGiven("query.sql", "filePath"));
            Assert.Contains("filePath", ex.Message);
        }

        [Fact]
        public void RequireAbsoluteIfGiven_Rejects_DriveRelativePath()
        {
            // "C:folder\file.sql" (no separator after the drive letter) is drive-relative, not
            // absolute - .NET resolves it against that drive's current directory. Path.IsPathRooted
            // wrongly accepts it; Path.IsPathFullyQualified correctly rejects it.
            Assert.Throws<McpException>(() => SqlFluffTools.RequireAbsoluteIfGiven(@"C:folder\file.sql", "filePath"));
        }

        [Theory]
        [InlineData("SELECT 1;\n", "\n")]
        [InlineData("SELECT 1;\r\n", "\r\n")]
        [InlineData("SELECT 1;", "\n")]
        public void DominantNewline_DetectsConvention(string text, string expected)
        {
            Assert.Equal(expected, SqlFluffTools.DominantNewline(text));
        }
    }
}
