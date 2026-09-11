# Documentation status — RevitCortex 2026 fork

This fork targets **Autodesk Revit 2026 only**.

## Current documentation

Use these files for current behavior:

- `../README.md` — fork overview, build/deploy/release policy
- `../AGENTS.md` — canonical AI/developer rules
- `../CLAUDE.md` — MCP/Claude operating rules
- `USER_GUIDE.md` — end-user guide
- `SECURITY.md` — current security model
- `../WORKFLOWS.md` — current operational workflows
- `COMMANDS.md` — command reference pointers
- `../ai-skills/revitcortex/` — focused AI skill/reference material
- `../tool-schemas.txt` — generated technical MCP signatures

## Historical upstream material

Dated files under folders such as:

- `superpowers/plans/`
- `superpowers/specs/`
- `superpowers/reports/`
- `reviews/`
- older performance/audit/handoff documents

are retained as historical engineering context from the upstream project. They may mention:

- Revit 2023/2024/2025/2027;
- old build matrices;
- old tool counts;
- superseded release/update behavior;
- earlier `send_code_to_revit` confirmation rules.

Those historical statements are **not** the source of truth for the current fork.

## Precedence

When documentation conflicts, use this order:

1. current source/project files;
2. `AGENTS.md`;
3. current `README.md`, `CLAUDE.md`, `USER_GUIDE.md`, `SECURITY.md`, `WORKFLOWS.md`;
4. current AI-skill references;
5. dated upstream/historical documents.

The current fork behavior is Revit 2026 / .NET 8 with session-only 10-second `Allow auto-run` critical C# approval and the upstream automatic update channel disabled.
