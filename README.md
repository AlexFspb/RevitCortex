# RevitCortex — Autodesk Revit 2026 Fork

> **Scope of this fork: Autodesk Revit 2026 only.**
>
> Revit 2023, 2024, 2025 and 2027 plugin targets were intentionally removed so development, testing, installation and release packaging can focus on **Revit 2026 / .NET 8**.

This is an **unofficial, independently maintained fork** of RevitCortex.

Fork repository: `AlexFspb/RevitCortex`  
Upstream project: `LuDattilo/RevitCortex`

RevitCortex is a Model Context Protocol (MCP) server plus Autodesk Revit add-in. It exposes dedicated Revit tools to MCP-compatible AI clients and, when explicitly enabled, a controlled custom C# execution path.

## Supported platform

| Component | This fork |
|---|---|
| Autodesk Revit | **2026** |
| Plugin runtime | **.NET 8** |
| Plugin configurations | `Debug R26`, `Release R26` |
| Revit LT | Not supported |
| Operating system | Windows 10/11 |

Support for another Revit release must be reintroduced and tested explicitly. Compatibility with other Revit versions is not implied by upstream code that may still be visible in historical documents or Git history.

## Architecture

```text
MCP client
  -> RevitCortex.Server (stdio MCP server)
  -> local TCP / JSON-RPC bridge
  -> RevitCortex.Plugin inside Revit 2026
  -> CortexRouter
  -> ICortexTool
  -> Autodesk Revit API
```

Revit API writes are dispatched into the proper Revit API context through `ExternalEvent`. Tool calls return structured `CortexResult<T>` success/error payloads.

The current tool catalog is defined by the C# MCP wrappers and `tool-schemas.txt`; avoid relying on a hard-coded tool count because the catalog can change.

## Fork access model

The upstream experimental **RevitCortex Premium / License & Account** entitlement subsystem is **not included in this fork**. There is no Premium activation key, trial state, machine fingerprint license gate, expiry state or license-based read-only fallback in the Revit 2026 fork.

This is separate from the software's legal license: the upstream **MIT License and original copyright notice remain unchanged in `LICENSE`**.

Write access is controlled by the actual operating/safety settings instead:

- **Settings → General → Read-Only Mode** can explicitly block write tools;
- individual tools can be disabled in **Settings → Tools**;
- destructive operations retain confirmation/safety handling;
- `send_code_to_revit` retains its dedicated enable switch, sandbox checks and audit logging.

## Main fork customization: timed script approval

`send_code_to_revit` remains a last-resort feature for operations that are not covered by a dedicated RevitCortex tool.

Custom C# execution is **disabled by default**. When enabled in **Settings → Tools**, every script still passes the existing settings gate, sandbox validation, router permissions and audit logging before execution.

For the final critical confirmation, this fork adds a dedicated Revit window with:

- **Yes** — approve immediately;
- **No** — cancel;
- **Allow auto-run** — optional session-only automatic approval;
- a visible **10-second countdown** on the Yes action when auto-run is enabled;
- automatic approval when the countdown reaches zero;
- manual Yes/No available at all times during the countdown.

`Allow auto-run` is intentionally **not persisted** to `settings.json`. It resets when Revit closes.

## Safety model

- Prefer dedicated RevitCortex tools over arbitrary C#.
- Use `dryRun: true` / preview-first workflows where supported.
- User-selected read-only mode continues to block write tools.
- `send_code_to_revit` continues to use sandbox validation and audit logging.
- The timed auto-run option automates only the final critical approval; it does not disable the other safety gates.
- Modal family-editing flows such as `Document.EditFamily` should not be executed from the MCP external-event path because they can deadlock Revit.

## Diagnostic reports

The ribbon action **Diagnostic Report** creates a ZIP locally and opens it in Explorer. This fork does **not** automatically email the upstream author or upload the report anywhere.

## Build

From the repository root:

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj -c Release
```

Tests:

```powershell
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

For release validation, also build Plugin and Tools with `Release R26`.

## Development deploy

Close Revit 2026 first.

Machine-scope deploy:

```powershell
.\deploy.ps1
```

Side-by-side development profile:

```powershell
.\deploy-dev.ps1
```

User-scope deploy:

```powershell
.\deploy-userscope.ps1
```

Install diagnostics:

```powershell
.\check-install.ps1
```

## Release package

Build an R26 package:

```powershell
.\build-release.ps1 -Version "1.0.51"
```

Expected output:

```text
RevitCortex-v1.0.51-R26.zip
```

`release.ps1` in this fork is intentionally local/fork-safe. It does **not** push releases or manifests to the upstream `LuDattilo` repositories.

## Installation package

The ZIP installer scripts and Inno Setup configuration target Revit 2026 only. The plugin is installed under the Revit 2026 add-ins location, with user-scope fallback where applicable.

See:

- `distribution/README.txt`
- `distribution/LEGGIMI.md`
- `installer/RevitCortex.iss`

## Automatic updates

The upstream automatic update channel is **disabled in this fork**.

Reason: the upstream manifest publishes upstream packages. Allowing this customized fork to consume that channel could overwrite the R26-specific modifications with an upstream release.

Until `AlexFspb/RevitCortex` has its own release manifest/channel, updates should be installed manually from this fork.

## Settings and data

For two Revit 2026 processes with different AI clients, use
[`start-revit-instance.ps1`](start-revit-instance.ps1) and assign a distinct
`REVITCORTEX_PORT` to each Revit process and its matching MCP server. A process
port overrides the shared settings and is never saved back by the Settings UI.
See [multiple-instance setup](docs/MULTIPLE_REVIT_INSTANCES.md) for launch commands,
client configuration and the remaining shared-data/Power BI limitations.

Default user data lives under:

```text
%USERPROFILE%\.revitcortex\
```

This includes settings, audit data, scripts and the installed MCP server. Development side-by-side builds use the project's separate dev profile where configured.

## AI/developer guidance

The fork-specific rules are documented in:

- `AGENTS.md`
- `CLAUDE.md`
- `ai-skills/revitcortex/SKILL.md`
- `ai-skills/revitcortex/references/`

For this fork, any legacy instruction that asks for R23/R24/R25/R27 build validation is obsolete unless that Revit target is deliberately reintroduced.

## Upstream and license

This repository is derived from `LuDattilo/RevitCortex`. The original project and its contributors remain the upstream source for the base implementation.

The upstream copyright notice and MIT license are intentionally retained. See `LICENSE` for the exact terms.
