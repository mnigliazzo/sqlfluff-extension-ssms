using System;
using System.IO;
using System.Linq;
using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    // Uses real temp directories/files since the resolver does real filesystem walking — there's
    // no I/O abstraction to fake here without adding one solely for this test.
    public class SqlFluffWorkingDirectoryResolverTests : IDisposable
    {
        private readonly string _root;

        public SqlFluffWorkingDirectoryResolverTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "sqlfluff-workdir-resolver-tests-" + Guid.NewGuid().ToString("N"));
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

        private static void Touch(string path)
        {
            File.WriteAllText(path, string.Empty);
        }

        [Fact]
        public void Resolve_ReturnsTheFileDirectory_WhenItItselfHasSqlfluffignore()
        {
            string dir = Dir("project");
            Touch(Path.Combine(dir, ".sqlfluffignore"));

            string result = SqlFluffWorkingDirectoryResolver.Resolve(dir);

            Assert.Equal(dir, result);
        }

        [Fact]
        public void Resolve_WalksUpToFindSqlfluffignoreInAnAncestor()
        {
            string projectDir = Dir("project");
            Touch(Path.Combine(projectDir, ".sqlfluffignore"));
            string nestedDir = Dir("project", "2026", "20260922", "rollback");

            string result = SqlFluffWorkingDirectoryResolver.Resolve(nestedDir);

            Assert.Equal(projectDir, result);
        }

        [Fact]
        public void Resolve_WalksUpToFindSqlfluffConfigToo_NotJustIgnore()
        {
            string projectDir = Dir("project");
            Touch(Path.Combine(projectDir, ".sqlfluff"));
            string nestedDir = Dir("project", "sub");

            string result = SqlFluffWorkingDirectoryResolver.Resolve(nestedDir);

            Assert.Equal(projectDir, result);
        }

        [Fact]
        public void Resolve_PrefersTheClosestMarkerDirectoryOverAFartherOneUpTheTree()
        {
            Touch(Path.Combine(Dir("project"), ".sqlfluffignore"));
            string innerDir = Dir("project", "subproject");
            Touch(Path.Combine(innerDir, ".sqlfluffignore"));
            string nestedDir = Dir("project", "subproject", "deeper");

            string result = SqlFluffWorkingDirectoryResolver.Resolve(nestedDir);

            Assert.Equal(innerDir, result);
        }

        [Fact]
        public void Resolve_FallsBackToTheFileDirectory_WhenNoMarkerIsFoundAnywhere()
        {
            string nestedDir = Dir("standalone", "no", "markers", "here");

            string result = SqlFluffWorkingDirectoryResolver.Resolve(nestedDir);

            Assert.Equal(nestedDir, result);
        }
    }
}
