# RevitCortex 2026 — Operational Workflows

This file contains the current recommended workflows for the **Autodesk Revit 2026** fork.

The exact MCP tool catalog changes over time; `tool-schemas.txt` is the technical source of truth. These workflows describe how tools should be combined safely and efficiently.

---

## 1. Session start

Recommended sequence:

1. Confirm Revit 2026 is open and **Cortex Switch** is active.
2. Use `get_project_info` when project/document context is required.
3. Detect locale before relying on localized parameter/category display names.
4. Keep queries scoped with categories, fields, limits or compact response options.

Do not re-query information that is already available in the current conversation.

---

## 2. Fast model health check

Sequence:

```text
check_model_health
-> get_warnings (small limit, e.g. 10)
-> optional clash_detection for a specific category pair
```

Use `workflow_model_audit` only when the user actually needs the more detailed audit workflow.

---

## 3. Find elements

Choose the narrowest search tool:

- exact/simple data filter → `export_elements_data`
- complex logical filtering → `ai_element_filter`
- active view → `get_current_view_elements`
- spatial volume → `get_elements_in_spatial_volume`

Always apply reasonable limits. Do not pull thousands of full element payloads when counts or a small subset are sufficient.

---

## 4. Discover a parameter before writing

When the parameter name or built-in identifier is uncertain:

```text
get_element_parameters on 1 representative element
-> identify the exact parameter / builtInParameter
-> perform the write
```

For Revit 2026 API work, prefer the current API and `ElementId.Value` where relevant.

Never guess custom parameter names.

---

## 5. Update the same parameter on many elements

Sequence:

```text
identify target elements
-> bulk_modify_parameter_values (dryRun: true)
-> inspect modifiedCount / skippedCount
-> bulk_modify_parameter_values (dryRun: false)
-> spot-check 1-2 elements with get_element_parameters
```

Do not inspect huge dry-run element lists unless the user needs them.

---

## 6. Different parameter values per element

Use `sync_csv_parameters` when many elements receive different values.

Typical CSV shape:

```text
ElementId,ParameterA,ParameterB
12345,Value 1,10
12346,Value 2,20
```

This is preferable to many repeated `set_element_parameters` calls.

---

## 7. Copy selected properties

Use `match_element_properties` with explicit `parameterNames`.

Avoid copying every transferable property unless that is specifically intended.

---

## 8. Destructive or bulk model changes

General sequence:

```text
read/identify targets
-> dryRun / preview if supported
-> inspect impact
-> execute real write
-> respect Revit confirmation
-> verify result
```

RevitCortex must not report success when Revit rolls back the transaction.

Normal destructive confirmations may use the existing Yes / Yes to All / Auto controls. This is separate from the critical custom-C# confirmation described below.

---

## 9. Custom C# (`send_code_to_revit`)

`send_code_to_revit` is a **last-resort** workflow.

Use it only when a dedicated RevitCortex tool does not adequately cover the requested operation.

Required sequence:

1. Check for a dedicated tool first.
2. Obtain explicit user consent where the MCP workflow requires it.
3. `EnableCodeExecution` must be enabled in Settings → Tools.
4. Code must pass sandbox validation.
5. Critical Revit confirmation must approve the script.
6. Execution remains audited.

Available globals:

- `document`
- `uiDocument`
- `app`

Do not call modal family-editing workflows such as `Document.EditFamily` from the MCP external-event context.

### Critical confirmation / Allow auto-run

For custom C# execution, the Revit 2026 fork shows a dedicated critical confirmation window:

- **Yes** → execute now
- **No** → cancel
- **Allow auto-run** → enable session-only timed approval

When **Allow auto-run** is enabled, the Yes action visibly counts down from **10 seconds**. At zero, the current script is approved automatically. Manual Yes and No remain available during the countdown.

The preference remains active for later critical C# confirmations in the same Revit process and resets when Revit closes.

This convenience feature does not bypass settings, sandbox validation, read-only enforcement, audit logging or router permissions.

---

## 10. Clash detection

Quick check:

```text
clash_detection
```

Visual review:

```text
workflow_clash_review
```

Specify the two categories/disciplines explicitly whenever possible.

---

## 11. Documentation / schedules / exports

Typical sequence:

```text
verify source data
-> create or identify schedule if needed
-> export_schedule / export_to_excel / workflow_data_roundtrip
```

Use file paths allowed by the project path-safety rules.

---

## 12. IFC workflow

Before large reconstruction work:

```text
ifc_validate_request
-> ifc_analyze_rebuildability
-> ifc_list_rebuild_candidates
-> targeted rebuild tool(s)
-> ifc_compare_original_vs_rebuilt where appropriate
```

Do not jump directly into large writes without inspecting rebuildability and candidate scope.

---

## 13. Power BI workflow

Power BI live/service operations can send model-derived data outside Revit to Microsoft services when explicitly configured and invoked.

Typical sequence:

```text
pbi_check_auth
-> pbi_list_workspaces / pbi_list_datasets
-> create/bind dataset if needed
-> pbi_publish_elements / pbi_publish_schedules / pbi_publish_selection
```

Respect the Power BI external-write settings and authentication flow.

---

## 14. Build validation after C# changes

This fork is **Revit 2026 only**.

Development validation:

```powershell
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj -c "Debug R26"
dotnet build src/RevitCortex.Tools/RevitCortex.Tools.csproj -c "Debug R26"
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj -c Release
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

Before release, also build Plugin and Tools with `Release R26`.

Do not run or document R23/R24/R25/R27 compatibility checks unless another Revit target is deliberately reintroduced.

---

## 15. Development deployment

Close Revit 2026 before replacing DLLs.

Machine scope:

```powershell
.\deploy.ps1
```

Side-by-side development profile:

```powershell
.\deploy-dev.ps1
```

User scope:

```powershell
.\deploy-userscope.ps1
```

Diagnose duplicate installations:

```powershell
.\check-install.ps1
```

---

## 16. Release workflow

Build package:

```powershell
.\build-release.ps1 -Version "1.0.51"
```

Expected package naming:

```text
RevitCortex-v1.0.51-R26.zip
```

`release.ps1` in this fork must remain fork-safe. It must not publish into `LuDattilo/revitcortex-releases` or modify upstream update manifests.

Automatic upstream updates are disabled until this fork has a dedicated release channel.

---

## 17. Documentation update rule

When behavior changes, keep the following aligned:

- implementation / MCP wrapper
- `README.md`
- `AGENTS.md`
- `CLAUDE.md`
- `docs/USER_GUIDE.md`
- this file when the workflow changes
- relevant `ai-skills/revitcortex/references/*`

Historical upstream plans and dated review reports may describe the original multi-version project. They are not the source of truth for current Revit 2026 fork behavior.
