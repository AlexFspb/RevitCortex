# Two Revit 2026 instances: automatic ports 8080 and 8888

Start both Revit instances normally. No special shortcut is required.
When **Cortex Switch** is enabled for the first time in each process, the plugin
tries to bind **8080**, then **8888** if 8080 is unavailable. The operating system
reserves the port as part of the bind, so simultaneous starts cannot claim the
same port. Assignment follows the order Cortex is enabled, not Revit launch order.
If another application already owns 8080, the first Cortex uses 8888.

Successful start/stop operations show no OK dialog. The ribbon icon remains the
status indicator (green: running; gray: stopped). **Settings > General** shows
this instance's actual port. Errors such as both ports being unavailable are
still reported; no third automatic port is selected.

## Connect the two clients

```text
Client A (Codex)  -> MCP server fixed to 8080 -> Revit that claimed 8080
Client B (Claude) -> MCP server fixed to 8888 -> Revit that claimed 8888
```

Set a per-server environment variable for the `RevitCortex.Server.exe` stdio
entry in each client:

| Client | Environment |
|---|---|
| A | `REVITCORTEX_PORT=8080` |
| B | `REVITCORTEX_PORT=8888` |

Both entries may use the same installed executable, normally
`%USERPROFILE%\.revitcortex\server\RevitCortex.Server.exe`, as separate processes.
Use its real absolute path in client configuration and restart the MCP clients
after configuration changes. A server without an override defaults to 8080.
It never scans for another available Revit port.

Always check `get_project_info` from each client before making changes to verify
which project is attached. Window focus does not change the destination. A
port identifies a running endpoint, not a permanent project identity: after
closing Revit and launching a new process, that port can belong to a new model.

## Stop/start behavior

Once assigned, the port stays fixed for that Revit process, including Cortex
Switch off/on and document close/reopen. A stopped instance does not retain a
listening socket. If another process takes its assigned port, restarting Cortex
reports an error instead of silently moving to the other port. An MCP client
whose endpoint is unavailable also reports an error instead of trying another.

On a completely new Revit launch, the next first activation tries 8080 then
8888 again. Use explicit overrides below if you want a stable client role
regardless of activation order.

## Optional explicit launch ports

For deterministic assignment, the launcher from the repository is still available:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\start-revit-instance.ps1 -Port 8080
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\start-revit-instance.ps1 -Port 8888
```

The default executable is `C:\Program Files\Autodesk\Revit 2026\Revit.exe`.
Use `-RevitPath` for a custom installation, or `-WhatIf` to validate without
launching. Shortcut targets must use the absolute script path.
The launcher sets `REVITCORTEX_PORT` only for the new child process. An explicit
port has no fallback. It must be an integer from 1 through 65535; invalid values
fail startup. Do not use `setx` to assign a global port to all Revit instances.

## Shared settings and upgrade

The legacy saved `Port` in `settings.json` is no longer used for routing by the
plugin or C# MCP server. Editing it from one Revit must not change another
instance's destination. Settings displays the assigned port read-only and never
writes it back, including when other settings are saved or reset.
This is an intentional change from the previous shared-port setup: clients that
used a saved custom port must now specify `REVITCORTEX_PORT` explicitly. The dev
plugin also follows the 8080/8888 automatic pair unless explicitly overridden;
its other dev settings remain separate.

Deploy updated Plugin/Core/Tools and the updated MCP server before testing.
Close Revit and MCP clients before using `deploy.ps1` and `deploy-server.ps1`.
Updating GitHub alone does not update installed binaries.

Other settings and user data remain shared between ordinary instances. This
includes script files, diagnostic reports, audit and telemetry files. Avoid
concurrent edits of shared settings. Power BI browser callbacks still use the
single port 27016; a second instance skips that callback listener while its MCP
connection can run normally.

## Document lifecycle

Once enabled with Cortex Switch, the TCP server and assigned port stay active
until explicitly stopped or Revit exits. Closing a temporary family, changing
projects or closing the last project does not stop the server. With no active
document, model commands return “No document open in Revit”. Opening a new
project restores the document context automatically; manually stopping Cortex
still keeps it off.

The target comes only from Revit's ActiveUIDocument. Opening or closing a
background family does not replace the active project's session. Pending commands
are checked again on Revit's UI thread and cancelled if the target closed or
changed. They are never automatically replayed against a replacement project.
Cancelled document closure is reconciled on Idling. Both confirmation checkbox
preferences remain independent and process-local.

## Script confirmation

The optional, session-only **Allow auto-run** now counts down for **3 seconds**.
The critical confirmation window still offers Yes and No. Code-execution
settings, sandbox checks, read-only/disabled-tool enforcement, audit logging and
transaction handling are unchanged. Removing routine connection status dialogs
does not remove critical or destructive-operation confirmations.

## Manual verification after deployment

1. Launch two Revit 2026 processes normally and enable Cortex in each.
2. Verify 8080/8888 in their Settings pages and no success OK dialogs.
3. Connect the matching clients and verify each project's identity.
4. Stop/start the second Cortex while 8080 is free: it must stay on 8888.
5. Confirm occupied ports produce an error instead of another assignment.
6. Save/reset other settings: the displayed active port must stay unchanged.
7. Verify Yes/No and the 3-second auto-run countdown in Revit.
8. Open/close a temporary background family: the same TCP connection and project
   remain available. Repeat with an active family and return to the project.
9. Close the last project, query (expect no-document error), open another project
   and query again without toggling Cortex. Cancel a document close and verify
   the original project context returns. A queued command for a previous context
   must be cancelled without modifying either project.
10. Stop Cortex manually, then close/open a document: it must remain off.

Automated tests exercise port precedence, concurrent exclusive TCP binding,
exhaustion, sticky reassignment and routing isolation. They do not replace the
Revit UI and model checks above.
