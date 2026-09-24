RevitCortex 2026 - MCP Assistant for Autodesk Revit
====================================================

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
window includes an optional session-only "Allow auto-run" checkbox with a visible
3-second countdown. The auto-run preference resets when Revit closes.

To uninstall: right-click uninstall.ps1 and choose "Run with PowerShell".

Project: https://github.com/AlexFspb/RevitCortex
Upstream: https://github.com/LuDattilo/RevitCortex
