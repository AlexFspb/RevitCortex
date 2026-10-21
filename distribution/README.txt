RevitCortex 2026 - MCP Assistant for Autodesk Revit
====================================================


Revit 2026 multi-instance policy (2026-10-09): the server starts once automatically at the first Idling event, including empty Revit. Manual Stop stays off until manual Start or restart. Release selects 8080 → 8888 → 8880 → 8088; Dev selects 8081 → 8889 → 8083 → 8891. Every MCP entry has an explicit fixed port; never fall back to another connection. Inspect get_connection_status/list_revit_instances for cached identity, then get_project_info or say_hello to verify the intended document. Automatic execution defaults to ON at every Revit launch for scripts and ordinary operations. The ribbon toggle and General Settings control one process-local policy. When ON there are no approval windows or countdowns; when OFF manual per-request confirmation remains. Orange indicates C# execution; blue indicates other tool execution. UI-thread work can pause blinking. No approval flags are persisted. The first C# request with no active document prepares a private metric service project. Never substitute that project for a user-named model. Shared settings must not be edited concurrently; temp scripts are isolated by process.
This fork supports Autodesk Revit 2026 only.

Install:
1. Close Autodesk Revit 2026.
2. Right-click install.ps1 and choose "Run with PowerShell".
3. Follow the on-screen prompts.
4. Restart Revit 2026 and your MCP client.

Custom C# execution (send_code_to_revit) is disabled by default.
The ribbon **Автовыполнение** toggle and **Settings → General → Автоматическое выполнение** share one process-local policy. It defaults to ON at each Revit launch, as explicitly requested by the user on 2026-10-21. Switching documents does not reset it. Nothing is persisted to settings.json and other Revit processes are unaffected.

When ON, scripts and ordinary operations pass the host approval step immediately, without windows, countdowns or focus changes. When OFF, ordinary modifying operations use Allow once and critical C# uses Yes/No; X/Escape cancels. Read-only tools retain their existing confirmation requirements (normally none).

Cortex Switch is gray when stopped, green when idle, orange while handling C# and blue while handling other ExternalEvent tools. Busy icons alternate bright/dim when the UI Dispatcher is available. Long synchronous Revit work may pause both blinking and repaint; this is not a liveness or progress guarantee. No nested message pump, screen access or window activation is used. Connection probes do not blink. The indicator clears when the handler actually finishes, not when the MCP caller times out.

Sandbox checks, EnableCodeExecution, disabled tools, read-only mode, context checks, expiration and audit remain mandatory. Scripts cannot change host controls. Do not reintroduce per-dialog automatic approval or chat approval for already authorized work.

To uninstall: right-click uninstall.ps1 and choose "Run with PowerShell".

Project: https://github.com/AlexFspb/RevitCortex
Upstream: https://github.com/LuDattilo/RevitCortex

Document lifecycle: once enabled, Cortex stays connected when families/projects close. With no active project, the first C# request prepares a service project; ordinary queries do not create one. Manually stopping Cortex keeps it off. Queued commands for an old document are cancelled, not replayed.
