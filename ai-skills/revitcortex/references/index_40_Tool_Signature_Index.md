# 40 — Tool Signature Index

**Scope:** Quick lookup for the current RevitCortex MCP tool catalog.
**Source of truth:** `tool-schemas.txt` generated from the C# MCP server definitions.

This fork targets Autodesk Revit 2026 only. The exact tool count can change as tools are added or removed, so this index intentionally does not hard-code a count.

## Lookup

Search a specific tool:

```bash
grep "^get_element_parameters" tool-schemas.txt
```

Search groups:

```bash
grep -E "^(ifc_|pbi_|workflow_)" tool-schemas.txt
```

## Categories

| Prefix | Category | Examples |
|---|---|---|
| `get_`, `list_`, `find_`, `analyze_`, `check_`, `export_`, `measure_`, `audit_` | Primarily read-only | `get_project_info`, `analyze_model_statistics` |
| `set_`, `bulk_`, `sync_`, `create_`, `delete_`, `purge_`, `wipe_`, `rename_`, `modify_`, `override_`, `change_` | Write | `set_element_parameters`, `bulk_modify_parameter_values` |
| `ifc_*` | IFC | `ifc_link`, `ifc_rebuild_walls` |
| `pbi_*` | Power BI | `pbi_publish_elements`, `pbi_query` |
| `workflow_*` | Composite workflows | `workflow_model_audit`, `workflow_clash_review` |

## Regeneration

After changing MCP tool signatures:

```bash
node server/generate-tool-schemas-csharp.mjs
```

Commit the regenerated `tool-schemas.txt` together with the implementation/wrapper change.
