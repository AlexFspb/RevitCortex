# RevitCortex 2026 — Command Index

This fork targets **Autodesk Revit 2026 only**.

The command catalog changes as tools are added or removed, so this file intentionally does not publish a hard-coded tool count.

## Sources of truth

| Need | Source |
|---|---|
| End-user command/workflow documentation | [`USER_GUIDE.md`](USER_GUIDE.md) |
| Current compact MCP signatures | [`tool-schemas.txt`](../tool-schemas.txt) |
| Tested operational workflows | [`WORKFLOWS.md`](../WORKFLOWS.md) |
| Fork target/build/safety rules | [`AGENTS.md`](../AGENTS.md) |

For the current executable tool surface, the C# MCP wrappers and generated `tool-schemas.txt` are authoritative.

Legacy upstream documentation may contain older hard-coded tool counts or multi-version Revit references. Those counts and version matrices are not authoritative for this Revit 2026 fork.
