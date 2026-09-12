# 22 — Revit 2026 / .NET 8 Target Rules

**Scope:** C# development for this RevitCortex fork.
**Target:** Autodesk Revit **2026** only.
**Framework:** `net8.0-windows10.0.19041.0`.
**Last verified for fork scope:** 2026-09-11.

## Target rule

This fork intentionally removed the R23, R24, R25 and R27 plugin configurations. Do not spend development time preserving net48 or .NET 10 compatibility unless another Revit version is explicitly reintroduced later.

Plugin and Tools configurations:

- `Debug R26`
- `Release R26`

## Build checks

After changing shared Plugin or Tools C# code, validate Revit 2026:

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
```

Before release, also validate release builds:

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Release R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Release R26"
```

Build the MCP server separately:

```powershell
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj -c Release
```

## Revit 2026 API assumptions

- Revit 2026 runs on .NET 8 in this project.
- `ElementId.Value` is the expected modern API property.
- Roslyn is the only custom C# execution path used by this fork; the old net48 CodeDom fallback is not part of the R26 path.
- WPF UI code may use .NET 8 features supported by the Revit 2026 target.

## Required checks

- [ ] Plugin builds with `Debug R26`.
- [ ] Tools build with `Debug R26`.
- [ ] Relevant unit tests pass.
- [ ] Release changes also build with `Release R26`.
- [ ] No documentation claims this fork supports another Revit version.

## If another Revit version is needed later

Reintroduce it deliberately as a separate compatibility task. Restore the required target framework/configuration from Git history or upstream, compile it independently, and fix compatibility issues at that time. Do not keep unused compatibility branches in day-to-day R26 development.
