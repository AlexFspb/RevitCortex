---
name: revitcortex
description: Use for RevitCortex 2026 operations, MCP tool workflows, Revit model automation, or RevitCortex C# development. This fork targets Autodesk Revit 2026 / .NET 8 only.
---

# RevitCortex 2026 Skill Router

Revit 2026 multi-instance policy (2026-10-09): the server starts once automatically at the first Idling event, including empty Revit. Manual Stop stays off until manual Start or restart. Automatic listener restart prefers its previous port, then tries the other profile ports if that port is occupied. Explicit REVITCORTEX_PORT stays fixed; MCP clients never switch ports automatically. Release selects 8080 → 8888 → 8880 → 8088; Dev selects 8081 → 8889 → 8083 → 8891. Every MCP entry has an explicit fixed port; never fall back to another connection. Inspect get_connection_status/list_revit_instances for cached identity, then get_project_info or say_hello to verify the intended document. Automatic execution defaults to ON at every Revit launch for scripts and ordinary operations. The ribbon toggle and General Settings control one process-local policy. When ON there are no approval windows or countdowns; when OFF manual per-request confirmation remains. Orange indicates C# execution; blue indicates other tool execution. UI-thread work can pause blinking. No approval flags are persisted. The first C# request with no active document prepares a private metric service project. Never substitute that project for a user-named model. Shared settings must not be edited concurrently; temp scripts are isolated by process.

This fork supports **Autodesk Revit 2026 only**. Do not apply upstream R23/R24/R25/R27 build instructions to this repository.

## Always-on rules

1. For Revit model requests, load `operator_01_Session_Start_Locale.md` before locale-sensitive work.
2. For destructive changes, load `operator_03_Destructive_Operations_DryRun.md` and use `dryRun: true` first where supported.
3. Prefer dedicated RevitCortex tools over `send_code_to_revit`.
4. Before using `send_code_to_revit`, follow `operator_10_SendCodeToRevit_Escalation.md`.
5. C# development targets **Revit 2026 / .NET 8** only; validate `Debug R26` and `Release R26` as appropriate.
6. Read-only mode must never be bypassed; see `developer_24_ReadOnly_Audit_Security.md`.

## Request classification

| Request | References |
|---|---|
| Model operation | `operator_01`, `operator_02` + domain reference |
| Parameter modification | `operator_01`, `operator_03`, `operator_04` |
| Destructive operation | `operator_03` (+ `operator_10` for custom C#) |
| Health / clash | `operator_01`, `operator_05` |
| View / annotation | `operator_01`, `operator_06` |
| IFC | `operator_01`, `operator_07` |
| Power BI | `operator_01`, `operator_08` |
| Obsidian / knowledge | `operator_09` |
| Custom C# script | `operator_10` |
| Loaded-family editing | `operator_10`, `operator_11` |
| New C# tool | `developer_20`, `developer_21`, `developer_22`, `developer_25` |
| Build / C# failure | `developer_22`, `developer_25` |
| Dynamic tools | `developer_23` |
| Security / audit | `developer_24` |

## Automatic execution in this fork

The ribbon **Автовыполнение** toggle and **Settings → General → Автоматическое выполнение** share one process-local policy. It defaults to ON at each Revit launch, as explicitly requested by the user on 2026-10-21. Switching documents does not reset it. Nothing is persisted to settings.json and other Revit processes are unaffected.

When ON, scripts and ordinary operations pass the host approval step immediately, without windows, countdowns or focus changes. When OFF, ordinary modifying operations use Allow once and critical C# uses Yes/No; X/Escape cancels. Read-only tools retain their existing confirmation requirements (normally none).

Cortex Switch is gray when stopped, green when idle, orange while handling C# and blue while handling other ExternalEvent tools. Busy icons alternate bright/dim when the UI Dispatcher is available. Long synchronous Revit work may pause both blinking and repaint; this is not a liveness or progress guarantee. No nested message pump, screen access or window activation is used. Connection probes do not blink. The indicator clears when the handler actually finishes, not when the MCP caller times out.

Sandbox checks, EnableCodeExecution, disabled tools, read-only mode, context checks, expiration and audit remain mandatory. Scripts cannot change host controls. Do not reintroduce per-dialog automatic approval or chat approval for already authorized work.

## Reference usage

1. Read `references/00_Master_Index.md` if the correct reference is unclear.
2. Load only references relevant to the current task.
3. Follow decision rules and required checks before execution.
4. If a workflow is changed for this fork, document the Revit 2026 behavior rather than copying a legacy multi-version rule.

## Indices

- `index_40_Tool_Signature_Index.md`: quick tool-signature lookup; `tool-schemas.txt` is canonical.
- `index_41_Workflow_Source_Map.md`: workflow source map.

Document closure no longer stops an enabled Cortex server. Background families do not replace the active UI document. With no active document, inspect get_connection_status or say_hello. The first C# request can prepare a service project; never substitute it for a named working project. A command cancelled because its document changed must not be blindly retried: verify the active project first.
