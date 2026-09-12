# 25 — Revit 2026 Build, Test, Release Checklist

**Scope:** Pre-commit checks, build validation, deployment and release for this fork.
**Target:** Autodesk Revit 2026 only.
**Last verified for fork scope:** 2026-09-11.

## Build Plugin and Tools

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
```

For release validation:

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Release R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Release R26"
```

## Build MCP server

```powershell
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj -c Release
```

## Tests

Run the test project directly:

```powershell
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

RevitAPI-dependent tests may require an installed Revit API runtime or may be marked to skip outside Revit.

## Deploy

Machine-scope development deploy:

```powershell
.\deploy.ps1
```

Side-by-side dev profile:

```powershell
.\deploy-dev.ps1
```

User-scope deploy:

```powershell
.\deploy-userscope.ps1
```

All deploy scripts in this fork target **Revit 2026** only.

## Build release package

```powershell
.\build-release.ps1 -Version "1.0.51"
```

Expected package name:

`RevitCortex-v1.0.51-R26.zip`

`release.ps1` is fork-safe: it prepares the local R26 package and does **not** publish to `LuDattilo/revitcortex-releases` or modify the upstream manifest.

## Automatic updates

Automatic upstream updates are disabled in this fork until a dedicated `AlexFspb` release channel is configured. This prevents an upstream package from replacing the customized R26 build.

## Pre-commit checklist

- [ ] Plugin `Debug R26` builds.
- [ ] Tools `Debug R26` builds.
- [ ] Relevant unit tests pass.
- [ ] Tool schema regenerated if MCP signatures changed.
- [ ] User-facing documentation updated for behavior changes.
- [ ] No new R23/R24/R25/R27 build assumptions were introduced.

## Pre-release checklist

- [ ] Plugin `Release R26` builds.
- [ ] Tools `Release R26` builds.
- [ ] MCP server Release build passes.
- [ ] Tests pass.
- [ ] `build-release.ps1` produces the R26 ZIP.
- [ ] Installer is tested with Revit 2026 closed.
- [ ] Critical C# confirmation and 10-second auto-run countdown are tested in Revit 2026 if those files changed.

## Avoid

- Do not publish from this fork into the upstream release repository.
- Do not claim support for Revit versions other than 2026.
- Do not mix framework-dependent and self-contained MCP server installs in the same server folder.
