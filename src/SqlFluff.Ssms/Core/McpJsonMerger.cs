using System.Text.Json;
using System.Text.Json.Nodes;

namespace SqlFluff.Ssms.Core
{
    // Adds a "sqlfluff" MCP server entry to an .mcp.json's "servers" object (the format
    // documented at https://learn.microsoft.com/ssms/github-copilot/mcp-servers), without
    // disturbing anything else already in the file - other servers, unrelated top-level keys,
    // etc. Uses System.Text.Json.Nodes rather than hand-rolling a JSON editor: that assembly is
    // already part of Microsoft.VisualStudio.SDK's own dependency closure (with
    // ExcludeAssets="runtime" on that PackageReference, so SSMS/VS supplies the actual runtime
    // assembly itself rather than this VSIX bundling a possibly-conflicting copy - the standard
    // way VS SDK extensions consume shared framework assemblies), so this doesn't introduce the
    // version-clash risk a plain NuGet reference to a JSON library would in a VSIX. The
    // trade-off versus hand-editing the raw text is that re-serializing doesn't preserve the
    // original file's key order or comments - acceptable here since Microsoft's own .mcp.json
    // examples have neither.
    internal static class McpJsonMerger
    {
        private const string ServersKey = "servers";
        private const string SqlFluffKey = "sqlfluff";

        // Returns the updated .mcp.json content with a "sqlfluff" stdio server entry pointing at
        // mcpDllPath, or null when nothing should be written: either a "sqlfluff" entry already
        // exists (never overwritten - it may have been customized by hand) or existingJson isn't
        // valid JSON / isn't shaped like an .mcp.json (callers should leave the file untouched
        // and log why rather than guess). existingJson may be null/empty for "file doesn't exist
        // yet".
        public static string AddSqlFluffServer(string existingJson, string mcpDllPath)
        {
            JsonObject root;
            if (string.IsNullOrWhiteSpace(existingJson))
            {
                root = new JsonObject();
            }
            else
            {
                JsonNode parsed;
                try
                {
                    parsed = JsonNode.Parse(existingJson);
                }
                catch (JsonException)
                {
                    return null;
                }

                root = parsed as JsonObject;
                if (root == null)
                {
                    return null;
                }
            }

            JsonObject servers = root[ServersKey] as JsonObject;
            if (servers == null)
            {
                if (root.ContainsKey(ServersKey))
                {
                    // "servers" exists but isn't a JSON object - too malformed to safely edit.
                    return null;
                }

                servers = new JsonObject();
                root[ServersKey] = servers;
            }

            if (servers.ContainsKey(SqlFluffKey))
            {
                return null;
            }

            var entry = new JsonObject();
            entry["type"] = JsonValue.Create("stdio");
            entry["command"] = JsonValue.Create("dotnet");
            entry["args"] = new JsonArray(JsonValue.Create(mcpDllPath));
            servers[SqlFluffKey] = entry;

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
