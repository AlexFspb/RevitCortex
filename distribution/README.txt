RevitCortex 2026 - MCP Assistant for Autodesk Revit
====================================================


Revit 2026 multi-instance policy (2026-10-09): the server starts once automatically at the first Idling event, including empty Revit. Manual Stop stays off until manual Start or restart. Release selects 8080 → 8888 → 8082 → 8890; Dev selects 8081 → 8889 → 8083 → 8891. Every MCP entry has an explicit fixed port; never fall back to another connection. Inspect get_connection_status/list_revit_instances for cached identity, then get_project_info or say_hello to verify the intended document. Critical C# auto-run now defaults to ON at each Revit launch, independently of ordinary auto-run; both retain the visible 3-second countdown, cancellation and process-local opt-out. No approval flags are persisted. The first C# request with no active document prepares a private metric service project. Never substitute that project for a user-named model. Shared settings must not be edited concurrently; temp scripts are isolated by process.
This fork supports Autodesk Revit 2026 only.

Install:
1. Close Autodesk Revit 2026.
2. Right-click install.ps1 and choose "Run with PowerShell".
3. Follow the on-screen prompts.
4. Restart Revit 2026 and your MCP client.

Custom C# execution (send_code_to_revit) is disabled by default.
Ordinary confirmations now have one Allow once button and a 3-second auto-run
checkbox, checked by default at startup. X/Escape cancels. The old two-minute/
unlimited menu and floating Auto mode window are removed. The ordinary checkbox
is independent of the critical C# preference below.
When enabled, critical script execution is confirmed in Revit. The confirmation
window includes a session-only "Allow auto-run" checkbox, checked by default, with a visible
3-second countdown. The auto-run preference resets to enabled on the next Revit launch.

To uninstall: right-click uninstall.ps1 and choose "Run with PowerShell".

Project: https://github.com/AlexFspb/RevitCortex
Upstream: https://github.com/LuDattilo/RevitCortex

Document lifecycle: once enabled, Cortex stays connected when families/projects close. With no active project, the first C# request prepares a service project; ordinary queries do not create one. Manually stopping Cortex keeps it off. Queued commands for an old document are cancelled, not replayed.
