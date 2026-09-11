# 20 — New Tool Checklist (Revit 2026 Fork)

**Scope:** Add a new `ICortexTool` to this fork.
**Target:** Autodesk Revit 2026 / .NET 8 only.
**Last verified for fork scope:** 2026-09-11.

## Files commonly touched

| File | Responsibility |
|---|---|
| `src/RevitCortex.Tools/<Category>/<ToolName>Tool.cs` | `ICortexTool` implementation |
| `src/RevitCortex.Server/Tools/<Category>Tools.cs` | MCP wrapper / schema |
| `tool-schemas.txt` | Compact generated signatures |
| `docs/USER_GUIDE.md` | End-user documentation when relevant |
| `WORKFLOWS.md` | Workflow documentation when behavior changes |
| `ai-skills/revitcortex/references/operator_*.md` | Operational reference when needed |

## Naming

- MCP tool: `snake_case`
- C# class: `PascalCase` + `Tool`
- Category: stable domain name such as `Elements`, `Views`, `Materials`, `Ifc`, `PowerBI`

## Minimal implementation

```csharp
public class MyNewTool : ICortexTool
{
    public string Name => "my_new_tool";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // Validate input.
        // For destructive work: request confirmation before Transaction.
        // Use a Revit Transaction for model writes.
        // Return CortexResult<object>.Ok(...) or .Fail(...).
    }
}
```

## Write-tool rules

- Use `dryRun: true` first when the tool supports preview.
- Call `session.RequestConfirmation(...)` for destructive operations.
- Use the standard transaction failure handling helpers so Revit warnings do not block the MCP bridge with modal dialogs.
- Never report success if Revit rolled back the transaction.

## Required checks

- [ ] `ICortexTool` implementation is correct.
- [ ] MCP schema/wrapper is aligned with the implementation.
- [ ] `tool-schemas.txt` regenerated when the schema changes.
- [ ] Relevant documentation updated.
- [ ] Destructive operations have preview/confirmation where appropriate.
- [ ] Plugin builds with `Debug R26`.
- [ ] Tools build with `Debug R26`.
- [ ] Relevant tests added or updated.

## Avoid

- Do not add compatibility code for R23/R24/R25/R27 unless support for that version is explicitly reintroduced.
- Do not add a tool without schema/documentation alignment.
- Do not bypass `CortexResult`, confirmation, read-only, audit or transaction-safety conventions.
