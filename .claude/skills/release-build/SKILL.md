---
name: release-build
description: Produce a release-ready local build of the VSIX — clean rebuild plus a sanity check that the VSIX/DLL/pkgdef actually came out. Use before handing someone a locally-built VSIX to install, or before trusting a local build's version number. Full build details in docs/BUILD.md.
---

# release-build

Full build reference: [docs/BUILD.md](../../../docs/BUILD.md).

An incremental build has been observed to repackage a stale `extension.vsixmanifest` (wrong version number) even after the source manifest was edited — always clean first.

## Steps

1. From the repo root:
   ```powershell
   Remove-Item src\SqlFluff.Ssms\bin, src\SqlFluff.Ssms\obj -Recurse -Force -ErrorAction SilentlyContinue
   ```
2. Restore and build Release, using whichever MSBuild is available (SSMS's bundled one needs the three env vars in [docs/BUILD.md](../../../docs/BUILD.md); a full VS 2022 install doesn't):
   ```powershell
   cd src\SqlFluff.Ssms
   dotnet restore SqlFluff.Ssms.csproj
   msbuild SqlFluff.Ssms.csproj /p:Configuration=Release
   ```
3. Sanity-check the output exists:
   ```powershell
   Test-Path bin\Release\SqlFluff.Ssms.vsix
   Test-Path bin\Release\SqlFluff.Ssms.dll
   Test-Path bin\Release\SqlFluff.Ssms.pkgdef
   ```
4. If `SqlFluffPackage.vsct` changed in this branch, confirm `[ProvideMenuResource("Menus.ctmenu", N)]` in `SqlFluffPackage.cs` was actually bumped (the `check-vsct-version.ps1` hook should already have nagged about this at the end of any turn where it wasn't — this is the last chance to catch it before install).

Remember: the version baked into the manifest right now is whatever was last hand-set — it is **not** the real published version. Only the CI release workflow's computed version (see [docs/RELEASE.md](../../../docs/RELEASE.md)) is the one that ships.
