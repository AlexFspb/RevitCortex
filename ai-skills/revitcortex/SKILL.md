---
name: revitcortex
description: Use for RevitCortex 2026 operations, MCP tool workflows, Revit model automation, or RevitCortex C# development. This fork targets Autodesk Revit 2026 / .NET 8 only.
---

# RevitCortex 2026 Skill Router

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
| New C# tool | `developer_20`, `developer_21`, `developer_22`, `developer_25` |
| Build / C# failure | `developer_22`, `developer_25` |
| Dynamic tools | `developer_23` |
| Security / audit | `developer_24` |

## Script confirmation in this fork

Normal destructive/bulk requests use a separate one-button confirmation window.
Its auto-run checkbox defaults to on at Revit startup and counts down for 3 seconds
on each request. X/Escape cancels. This preference is independent of critical C#
auto-run below. The old two-minute/unlimited menu and floating Auto mode UI are gone.

Critical C# execution uses the RevitCortex confirmation window. `Allow auto-run` is optional and session-only. When enabled, a visible **3-second countdown** auto-approves the script unless the user presses No or closes the dialog. The setting resets when Revit closes.

This does not disable sandbox validation, audit logging, read-only protection, or the rule to prefer dedicated tools.

## Reference usage

1. Read `references/00_Master_Index.md` if the correct reference is unclear.
2. Load only references relevant to the current task.
3. Follow decision rules and required checks before execution.
4. If a workflow is changed for this fork, document the Revit 2026 behavior rather than copying a legacy multi-version rule.

## Indices

- `index_40_Tool_Signature_Index.md`: quick tool-signature lookup; `tool-schemas.txt` is canonical.
- `index_41_Workflow_Source_Map.md`: workflow source map.

Document closure no longer stops an enabled Cortex server. Background families do not replace the active UI document. If no project is active, open a project; do not toggle the server unnecessarily. A command cancelled because its document changed must not be blindly retried: verify the active project first.
