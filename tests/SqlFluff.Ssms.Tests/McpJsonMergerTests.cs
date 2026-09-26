using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class McpJsonMergerTests
    {
        private const string DllPath = @"C:\Users\me\AppData\Local\SqlFluff.Ssms\Mcp\SqlFluff.Mcp.dll";

        [Fact]
        public void AddSqlFluffServer_CreatesServersObject_WhenFileDoesNotExist()
        {
            string result = McpJsonMerger.AddSqlFluffServer(null, DllPath);

            Assert.NotNull(result);
            Assert.Contains("\"sqlfluff\"", result);
            Assert.Contains("\"servers\"", result);
            Assert.Contains("\"dotnet\"", result);
            Assert.Contains(DllPath.Replace(@"\", @"\\"), result);
        }

        [Fact]
        public void AddSqlFluffServer_CreatesServersObject_WhenFileIsEmpty()
        {
            Assert.NotNull(McpJsonMerger.AddSqlFluffServer(string.Empty, DllPath));
        }

        [Fact]
        public void AddSqlFluffServer_PreservesExistingUnrelatedServers()
        {
            const string existing = @"{""servers"": {""github"": {""url"": ""https://api.githubcopilot.com/mcp/""}}}";

            string result = McpJsonMerger.AddSqlFluffServer(existing, DllPath);

            Assert.NotNull(result);
            Assert.Contains("\"github\"", result);
            Assert.Contains("api.githubcopilot.com", result);
            Assert.Contains("\"sqlfluff\"", result);
        }

        [Fact]
        public void AddSqlFluffServer_PreservesUnrelatedTopLevelKeys()
        {
            const string existing = @"{""inputs"": [], ""servers"": {}}";

            string result = McpJsonMerger.AddSqlFluffServer(existing, DllPath);

            Assert.NotNull(result);
            Assert.Contains("\"inputs\"", result);
        }

        [Fact]
        public void AddSqlFluffServer_ReturnsNull_WhenSqlFluffEntryAlreadyExists()
        {
            const string existing = @"{""servers"": {""sqlfluff"": {""type"": ""stdio"", ""command"": ""dotnet"", ""args"": [""C:\\custom\\path.dll""]}}}";

            Assert.Null(McpJsonMerger.AddSqlFluffServer(existing, DllPath));
        }

        [Fact]
        public void AddSqlFluffServer_ReturnsNull_WhenJsonIsMalformed()
        {
            Assert.Null(McpJsonMerger.AddSqlFluffServer("{ not valid json", DllPath));
        }

        [Fact]
        public void AddSqlFluffServer_ReturnsNull_WhenServersIsNotAnObject()
        {
            const string existing = @"{""servers"": ""oops""}";

            Assert.Null(McpJsonMerger.AddSqlFluffServer(existing, DllPath));
        }
    }
}
