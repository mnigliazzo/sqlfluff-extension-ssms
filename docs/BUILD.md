# Build & Test

`src/SqlFluff.Ssms/SqlFluff.Ssms.csproj` is the VSIX.

## Local development

This repo was authored on a machine with SSMS 22 but no full Visual Studio, so local builds use SSMS's own bundled MSBuild:

```powershell
cd src\SqlFluff.Ssms
dotnet restore SqlFluff.Ssms.csproj
$env:MSBuildSDKsPath = "C:\Program Files\dotnet\sdk\<version>\Sdks"
$env:DOTNET_HOST_PATH = "C:\Program Files\dotnet\dotnet.exe"
$env:DOTNET_ROOT = "C:\Program Files\dotnet"
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\MSBuild\Current\Bin\amd64\MSBuild.exe" SqlFluff.Ssms.csproj /p:Configuration=Release
```

The three env vars are required *only* when building with SSMS's bundled MSBuild — without them it fails to resolve the SDK-style project's implicit imports. If building with a full Visual Studio 2022 install instead, plain `msbuild SqlFluff.Ssms.csproj /p:Configuration=Release` works and the env vars aren't needed (this is what CI does).

Output: `src\SqlFluff.Ssms\bin\Release\SqlFluff.Ssms.vsix` (plus the loose `.dll`/`.pkgdef`).

## Lint

`dotnet format` (built into the SDK, no extra tooling) checks code style — CI runs it with `--verify-no-changes` in both `build.yml` (against `SqlFluff.Ssms.csproj`) and `test.yml` (against `SqlFluff.Ssms.Tests.csproj`), so a style violation fails the same required checks a build/test failure would. Run it locally before pushing:

```powershell
dotnet format src\SqlFluff.Ssms\SqlFluff.Ssms.csproj --verify-no-changes   # add without --verify-no-changes to auto-fix
dotnet format tests\SqlFluff.Ssms.Tests\SqlFluff.Ssms.Tests.csproj --verify-no-changes
```

It works with a plain `dotnet restore` — no SSMS/VS MSBuild needed, unlike the actual build.

**Always do a clean rebuild before treating a `.vsix` as release-ready** (`Remove-Item bin,obj -Recurse -Force` first). An incremental build has been observed to repackage a stale `extension.vsixmanifest` (wrong version number) even after the source manifest was edited. The `.claude/skills/release-build` skill runs this checklist for you.

## Tests

`tests/SqlFluff.Ssms.Tests/SqlFluff.Ssms.Tests.csproj` is a separate, plain `net8.0` xUnit project — no VS SDK, no VSSDK.BuildTools, no SSMS/Visual Studio MSBuild needed:

```bash
dotnet test tests/SqlFluff.Ssms.Tests/SqlFluff.Ssms.Tests.csproj
```

It only covers the pure logic that can be extracted without touching `Process`/VS SDK types — currently `Core/ArgumentQuoting.cs` (Windows command-line quoting) and `Core/LintJsonParser.cs` (parsing `sqlfluff lint --format json` output). These files are compiled directly into the test project via linked `<Compile Include>` entries rather than a `ProjectReference` to the main VSIX project — a `ProjectReference` would drag in `SqlFluff.Ssms.csproj`'s VSSDK targets and require the same SSMS/VS MSBuild the tests are trying to avoid needing.

**When adding new pure logic to `Core/`** (parsing, string manipulation, anything that doesn't touch `ITextBuffer`/`Process`/VS SDK types), prefer putting it in its own file so it can be linked into the test project the same way, and add tests for it. Logic that inherently needs the VS SDK (editor services, tagging, commands) has no test coverage and isn't expected to — there's no reasonable way to unit test that without a running SSMS/VS host.
