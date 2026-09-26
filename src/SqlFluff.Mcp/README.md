# SqlFluff.Mcp

An MCP (Model Context Protocol) server, exposed over stdio, that gives an AI coding
assistant access to the same sqlfluff lint/fix/format pipeline the SSMS extension uses -
so SQL an assistant writes or is about to insert can be run through the project's `.sqlfluff`
config first, the same way a human would hit Format after typing it.

This is a separate, standalone tool. It doesn't require the VSIX to be installed, doesn't
talk to SSMS or any open editor, and doesn't get installed by the VSIX - it only needs
`sqlfluff` itself on the PATH (`pip install sqlfluff`), same as the extension does.

## Tools

- `sqlfluff_lint(sql, dialect?, filePath?, workingDirectory?, configFile?, executablePath?)`
  - returns `{ clean, violations[] }`.
- `sqlfluff_fix(sql, dialect?, filePath?, workingDirectory?, configFile?, executablePath?)`
  - returns `{ sql, changed }`.
- `sqlfluff_format(sql, dialect?, filePath?, workingDirectory?, configFile?, executablePath?)`
  - returns `{ sql, changed }`.

`filePath` / `workingDirectory` let sqlfluff discover the right `.sqlfluff` config the same
way the extension does (walking up from the file's or project's directory). Pass
`workingDirectory` (your project root) when the SQL doesn't correspond to a saved file yet -
the usual case for AI-generated SQL. `configFile` is a fallback used only when no `.sqlfluff`
is discovered. `dialect` defaults to `tsql`; `executablePath` is only needed if `sqlfluff`
isn't on this process's PATH.

## Getting the DLL

No build required for normal use - every GitHub Release attaches a `SqlFluff.Mcp.zip`
built by CI. Download it from [the latest release](../../../../releases/latest) and unzip
it anywhere (needs the .NET 8 runtime installed; it's framework-dependent, not
self-contained).

To build from source instead (e.g. for local changes), you need the .NET 8 SDK:

```powershell
dotnet build src\SqlFluff.Mcp\SqlFluff.Mcp.csproj --configuration Release
```

Output: `src\SqlFluff.Mcp\bin\Release\net8.0\SqlFluff.Mcp.dll`.

## Configuring an MCP client

Point your MCP-capable client at the DLL (from either option above), run with `dotnet`.
There's no installer for this piece, and nothing registers it with a client for you
automatically - see the main [README](../../README.md#ai-assistant-integration-mcp) for
why registration in particular stays manual.

### GitHub Copilot in SSMS / Visual Studio

Either add it from Copilot Chat's Tools panel (**+** > **Add custom MCP server**, type
`stdio`, command `dotnet`, args the full path to `SqlFluff.Mcp.dll`), or edit
`%USERPROFILE%\.mcp.json` directly:

```json
{
  "servers": {
    "sqlfluff": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["C:\\path\\to\\SqlFluff.Mcp.dll"]
    }
  }
}
```

SSMS/VS detects the change and initializes the server on save. New tools are added
disabled by default - enable them in the Tools panel. See Microsoft's
[Use MCP servers with GitHub Copilot in SQL Server Management Studio](https://learn.microsoft.com/ssms/github-copilot/mcp-servers)
for more (registry install, per-solution vs. global config, tool approval).

### Other MCP clients

The config shape varies by client - consult its own MCP server documentation - but it
boils down to the same `command`/`args` pair:

```json
{
  "command": "dotnet",
  "args": ["C:\\path\\to\\SqlFluff.Mcp.dll"]
}
```
