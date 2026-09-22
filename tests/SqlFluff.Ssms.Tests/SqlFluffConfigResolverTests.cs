using System;
using System.IO;
using System.Linq;
using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    // Uses real temp directories/files since the resolver does real filesystem walking — there's
    // no I/O abstraction to fake here without adding one solely for this test.
    public class SqlFluffConfigResolverTests : IDisposable
    {
        private readonly string _root;

        public SqlFluffConfigResolverTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "sqlfluff-config-resolver-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private string Dir(params string[] segments)
        {
            string path = Path.Combine(new[] { _root }.Concat(segments).ToArray());
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Touch(string path, string contents = "")
        {
            File.WriteAllText(path, contents);
        }

        // ---- Discovery near a saved document ---------------------------------------------------

        [Fact]
        public void Resolve_FindsSqlfluffInTheSameDirectoryAsASavedDocument()
        {
            string projectDir = Dir("project");
            string configPath = Path.Combine(projectDir, ".sqlfluff");
            Touch(configPath);
            string documentPath = Path.Combine(projectDir, "query.sql");
            Touch(documentPath);

            string result = SqlFluffConfigResolver.Resolve(documentPath, openFolderPath: null, optionsConfigFile: null);

            Assert.Equal(configPath, result);
        }

        [Fact]
        public void Resolve_WalksUpMultipleLevelsToFindSqlfluff()
        {
            string projectDir = Dir("project");
            string configPath = Path.Combine(projectDir, ".sqlfluff");
            Touch(configPath);
            string documentPath = Path.Combine(Dir("project", "queries", "reports"), "query.sql");
            Touch(documentPath);

            string result = SqlFluffConfigResolver.Resolve(documentPath, openFolderPath: null, optionsConfigFile: null);

            Assert.Equal(configPath, result);
        }

        [Fact]
        public void Resolve_PrefersTheClosestSqlfluffOverAFartherOneUpTheTree()
        {
            string outerConfig = Path.Combine(Dir("project"), ".sqlfluff");
            Touch(outerConfig);
            string innerDir = Dir("project", "subproject");
            string innerConfig = Path.Combine(innerDir, ".sqlfluff");
            Touch(innerConfig);
            string documentPath = Path.Combine(innerDir, "query.sql");
            Touch(documentPath);

            string result = SqlFluffConfigResolver.Resolve(documentPath, openFolderPath: null, optionsConfigFile: null);

            Assert.Equal(innerConfig, result);
        }

        [Fact]
        public void Resolve_IgnoresTheDocumentDirectory_WhenTheDocumentDoesNotActuallyExistOnDisk()
        {
            // An unsaved document's ITextDocument.FilePath may still point somewhere, but if
            // nothing is actually there yet, walking up from it would be walking up from an
            // arbitrary location, not a real project directory.
            string projectDir = Dir("project");
            Touch(Path.Combine(projectDir, ".sqlfluff"));
            string documentPath = Path.Combine(projectDir, "not-actually-saved.sql");

            string result = SqlFluffConfigResolver.Resolve(documentPath, openFolderPath: null, optionsConfigFile: null);

            Assert.Null(result);
        }

        // ---- Fallback to the open folder (unsaved new document) --------------------------------

        [Fact]
        public void Resolve_UsesTheOpenFolderSqlfluff_WhenTheDocumentIsUnsaved()
        {
            string folderPath = Dir("workspace");
            string configPath = Path.Combine(folderPath, ".sqlfluff");
            Touch(configPath);
            string unsavedDocumentPath = Path.Combine(folderPath, "SQLQuery1.sql"); // never touched: not saved yet

            string result = SqlFluffConfigResolver.Resolve(unsavedDocumentPath, folderPath, optionsConfigFile: null);

            Assert.Equal(configPath, result);
        }

        [Fact]
        public void Resolve_WalksUpFromTheOpenFolderToo()
        {
            string outerConfig = Path.Combine(Dir("workspace"), ".sqlfluff");
            Touch(outerConfig);
            string folderPath = Dir("workspace", "project-subfolder");

            string result = SqlFluffConfigResolver.Resolve(null, folderPath, optionsConfigFile: null);

            Assert.Equal(outerConfig, result);
        }

        [Fact]
        public void Resolve_SavedDocumentSqlfluffWinsOverTheOpenFolderSqlfluff()
        {
            string folderPath = Dir("workspace");
            Touch(Path.Combine(folderPath, ".sqlfluff")); // folder-level config
            string subDir = Dir("workspace", "subproject");
            string closerConfig = Path.Combine(subDir, ".sqlfluff");
            Touch(closerConfig); // the document's own, closer config
            string documentPath = Path.Combine(subDir, "query.sql");
            Touch(documentPath);

            string result = SqlFluffConfigResolver.Resolve(documentPath, folderPath, optionsConfigFile: null);

            Assert.Equal(closerConfig, result);
        }

        [Fact]
        public void Resolve_IgnoresOpenFolderPath_WhenItDoesNotExist()
        {
            string optionsConfigFile = Path.Combine(Dir("elsewhere"), "fallback.sqlfluff");
            Touch(optionsConfigFile);
            string bogusFolderPath = Path.Combine(_root, "does-not-exist");

            string result = SqlFluffConfigResolver.Resolve(null, bogusFolderPath, optionsConfigFile);

            Assert.Equal(optionsConfigFile, result);
        }

        // ---- Fallback to the Options-configured path --------------------------------------------

        [Fact]
        public void Resolve_DiscoveredSqlfluffWinsOverTheOptionsConfiguredPath()
        {
            string projectDir = Dir("project");
            string discovered = Path.Combine(projectDir, ".sqlfluff");
            Touch(discovered);
            string documentPath = Path.Combine(projectDir, "query.sql");
            Touch(documentPath);
            string optionsConfigFile = Path.Combine(Dir("elsewhere"), "fallback.sqlfluff");
            Touch(optionsConfigFile);

            string result = SqlFluffConfigResolver.Resolve(documentPath, openFolderPath: null, optionsConfigFile);

            Assert.Equal(discovered, result);
        }

        [Fact]
        public void Resolve_FallsBackToOptionsConfigFile_WhenNoSqlfluffIsFoundAnywhere()
        {
            string documentPath = Path.Combine(Dir("project", "no", "config", "here"), "query.sql");
            Touch(documentPath);
            string optionsConfigFile = Path.Combine(Dir("elsewhere"), "fallback.sqlfluff");
            Touch(optionsConfigFile);

            string result = SqlFluffConfigResolver.Resolve(documentPath, openFolderPath: null, optionsConfigFile);

            Assert.Equal(optionsConfigFile, result);
        }

        [Fact]
        public void Resolve_ReturnsNull_WhenNothingIsFoundAndOptionsConfigFileIsEmpty()
        {
            string documentPath = Path.Combine(Dir("project", "no", "config"), "query.sql");
            Touch(documentPath);

            string result = SqlFluffConfigResolver.Resolve(documentPath, openFolderPath: null, optionsConfigFile: "");

            Assert.Null(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Resolve_FallsBackDirectlyToOptionsConfigFile_WhenDocumentPathIsMissing(string documentPath)
        {
            string optionsConfigFile = Path.Combine(Dir("elsewhere"), "fallback.sqlfluff");

            string result = SqlFluffConfigResolver.Resolve(documentPath, openFolderPath: null, optionsConfigFile);

            Assert.Equal(optionsConfigFile, result);
        }

        [Fact]
        public void Resolve_ReturnsNull_WhenNothingIsAvailableAtAll()
        {
            Assert.Null(SqlFluffConfigResolver.Resolve(null, null, null));
        }

        [Fact]
        public void Resolve_TrimsWhitespaceFromOptionsConfigFile()
        {
            string optionsConfigFile = Path.Combine(Dir("elsewhere"), "fallback.sqlfluff");

            string result = SqlFluffConfigResolver.Resolve(null, null, "  " + optionsConfigFile + "  ");

            Assert.Equal(optionsConfigFile, result);
        }
    }
}
