# RevitCortex — Revit 2026 Fork

> **This fork is maintained for Autodesk Revit 2026 only.**
>
> Support for Revit 2023, 2024, 2025 and 2027 has been intentionally removed from this fork so development, testing and deployment can focus on Revit 2026 / .NET 8.
>
> Upstream project: `LuDattilo/RevitCortex`.

RevitCortex is an MCP (Model Context Protocol) server and Revit add-in that allows MCP-compatible AI clients to inspect and operate Autodesk Revit through dedicated tools and, when explicitly enabled, controlled C# script execution.

## Supported version

| Component | Supported version |
| --- | --- |
| Autodesk Revit | **2026** |
| .NET | **8** |
| Plugin configuration | `Debug R26` / `Release R26` |

Older Revit configurations are intentionally not supported by this fork. If support for another Revit release is needed later, it should be added and tested explicitly rather than assumed compatible.

## Development focus of this fork

This fork keeps the upstream RevitCortex architecture but is optimized for a single production target: Revit 2026. Current customizations include the confirmation/auto-approval workflow for controlled C# script execution.

## Build

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
```

## Deploy to Revit 2026

Close Revit before deployment, then run:

```powershell
.\deploy.ps1
```

For the side-by-side development profile:

```powershell
.\deploy-dev.ps1
```

The add-in is installed for Autodesk Revit 2026 only.

## Architecture

The runtime flow is:

```text
MCP client
  -> RevitCortex.Server
  -> local TCP / JSON-RPC bridge
  -> RevitCortex.Plugin
  -> CortexRouter
  -> dedicated Revit tool
  -> Autodesk Revit API
```

Revit API work is dispatched to the Revit UI thread through `ExternalEvent`.

## Script execution

`send_code_to_revit` is intended as a last-resort tool when no dedicated RevitCortex tool covers the requested operation. Code execution must be enabled explicitly in settings and is validated by the project sandbox before execution.

In this fork, critical C# script confirmation uses a dedicated confirmation window. The user may enable **Allow auto-run**; when enabled, the approval button counts down for 10 seconds and then approves automatically unless the user cancels first. The preference is session-only and resets when Revit restarts.

## Safety

Prefer dedicated tools over arbitrary C# execution. Use dry-run/preview functionality where available before destructive or bulk changes. Critical operations should remain explicit and auditable.

## Upstream

This repository is a fork of `LuDattilo/RevitCortex`. Upstream documentation may describe support for multiple Revit releases; that does **not** apply to this fork unless explicitly reintroduced here.

## License

See `LICENSE`.
