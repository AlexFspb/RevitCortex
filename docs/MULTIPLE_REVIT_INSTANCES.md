# Two Revit 2026 instances with separate AI clients

Use a distinct TCP port for each Revit process and pin each client's MCP server to
that same port. For example:

```text
Client A (Codex)   -> RevitCortex.Server [8888] -> Revit A [8888]
Client B (Claude)  -> RevitCortex.Server [8889] -> Revit B [8889]
```

This requires the plugin and server built from the revision that adds this
feature. Existing installations must be rebuilt/deployed first, with Revit and
the MCP clients closed. Use `deploy.ps1` and `deploy-server.ps1` as described in
the main README. Updating GitHub alone does not update installed binaries.

## 1. Launch each Revit with its own port

From the repository folder, run these separately:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\start-revit-instance.ps1 -Port 8888
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\start-revit-instance.ps1 -Port 8889
```

The script defaults to `C:\Program Files\Autodesk\Revit 2026\Revit.exe`.
Pass `-RevitPath` for a custom installation. `-WhatIf` validates the arguments
and reports the launch without starting Revit.

For Windows shortcuts, use the corresponding command above with an **absolute**
script path (for example `C:\RevitCortex\start-revit-instance.ps1`). Name the
shortcuts "Revit - Codex" and "Revit - Claude". Launch each Revit using its
shortcut, open the intended project, then enable **Cortex Switch** in each.

The launcher sets `REVITCORTEX_PORT` only in the new Revit process's environment.
It does not edit `settings.json`, the parent shell, or Windows user/machine
environment variables. Avoid setting this variable globally with `setx`.

In **Settings > General**, the port is read-only when supplied by the launcher.
Saving or resetting other settings leaves the shared saved port untouched.
The status badge shows the actual port for that Revit process. To change its
port, close that instance and relaunch with a different `-Port`.

## 2. Pin each MCP server to its matching port

In each client's configuration for the existing `RevitCortex.Server.exe` stdio
server, set a **per-server environment variable**:

| Client | Executable | Environment |
|---|---|---|
| A | `%USERPROFILE%\.revitcortex\server\RevitCortex.Server.exe` | `REVITCORTEX_PORT=8888` |
| B | The same executable | `REVITCORTEX_PORT=8889` |

These are two separate server processes; no second copy of the executable is
needed. Use the real absolute executable path in client configuration. Restart
the MCP clients after changing their environment settings. Merely changing the
port in Revit does not reconfigure an already-running MCP server.

The MCP server does not select Revit by window focus or project title. One
server stays attached to its configured port for its entire lifetime. If that
Revit is closed, calls fail; they do not fall back to the other port. Before
making model changes, use `get_project_info` from each client to check the
expected project.

## Port precedence and compatibility

Both the C# MCP server and plugin use this order:

1. Process environment `REVITCORTEX_PORT`, when nonempty.
2. Saved `Port` in the existing settings file.
3. Default port (8080 for the ordinary installation).

Explicit overrides must be integers from 1 through 65535. Invalid overrides
fail startup rather than silently connecting to a different model. A missing
override preserves the previous single-instance behavior. The dev plugin keeps
its existing separate settings file and default port 8081; explicitly set the
same port on its MCP server as well.

If the chosen port is occupied, choose another matching pair. No automatic port
switching occurs. If you start two Revit processes before enabling Cortex in
either, the launcher cannot reserve their ports: you must give them different
numbers. Revit instances that were already running before using the launcher
retain their original ports.

## Scope and remaining limitations

- This separates MCP routing, not all user data. Ordinary instances still share
  settings other than the process port, script files, diagnostic reports, audit
  and telemetry files. Avoid editing shared settings from both instances at once.
- The Power BI browser callback listener still uses port 27016. Only one instance
  can own that callback endpoint; separate MCP ports do not isolate Power BI
  browser callbacks. A collision skips that listener without stopping MCP.
- Both instances must use Revit 2026. Other Revit versions are outside this fork.
- Read-only mode, disabled tools, custom-code enablement, sandbox validation,
  audit logging and Revit confirmations remain in force.

## Manual verification after deployment

1. Launch the two instances on 8888 and 8889 and enable Cortex Switch in each.
2. Confirm each settings badge reports its assigned port.
3. Save another setting and use Reset Defaults: the assigned port stays visible
   and the saved shared `Port` is not overwritten by it.
4. Connect each client with its matching environment variable; check different
   projects using `get_project_info`.
5. Close and relaunch one instance through its shortcut. The other keeps working.
6. With one instance closed, its client must report a connection error while the
   other client still reaches its own project.

Automated tests cover override precedence and validation, legacy fallback, and
concurrent routing to two real loopback listeners. They do not replace these
checks inside Revit.
