using System;
using Autodesk.Revit.UI;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Shows confirmation UI before destructive/bulk operations.
/// Called inside tool Execute() after parameter validation but BEFORE opening Transaction.
/// </summary>
public static class ConfirmationHelper
{
    /// <summary>
    /// Shows a confirmation dialog for destructive operations.
    /// </summary>
    /// <param name="action">Action verb: "delete", "purge", "rename", "modify", etc.</param>
    /// <param name="elementCount">Number of elements affected.</param>
    /// <param name="description">Optional description of what the operation will do.</param>
    /// <returns>true = Yes, false = No, null = Yes to All.</returns>
    public static bool? Confirm(string action, int elementCount, string? description)
    {
        if (elementCount <= 0) return true;

        var dialog = new TaskDialog("RevitCortex Premium Confirmation")
        {
            MainInstruction = $"About to {action} ({elementCount} element(s))",
            CommonButtons = TaskDialogCommonButtons.None
        };

        if (!string.IsNullOrEmpty(description))
            dialog.MainContent = description;

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Yes",
            "Approve this operation");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Yes to All",
            "Approve this and all remaining operations without asking again (2 min)");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Auto",
            "Approve all operations automatically — a floating window lets you stop at any time");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "No",
            "Cancel this operation");

        var result = dialog.Show();
        if (result == TaskDialogResult.CommandLink2) return null;
        if (result == TaskDialogResult.CommandLink1) return true;
        if (result == TaskDialogResult.CommandLink3) return AutoSentinel;
        return false;
    }

    public const bool AutoSentinel = true;

    /// <summary>
    /// Variant wired to a CortexSession: sets session.AutoMode = true when Auto is clicked
    /// and fires AutoModeChanged so the ribbon can update its button visibility immediately.
    /// </summary>
    public static bool? ConfirmWithSession(string action, int elementCount, string? description,
        CortexSession session)
    {
        if (elementCount <= 0) return true;

        var dialog = new TaskDialog("RevitCortex Premium Confirmation")
        {
            MainInstruction = $"About to {action} ({elementCount} element(s))",
            CommonButtons = TaskDialogCommonButtons.None
        };

        if (!string.IsNullOrEmpty(description))
            dialog.MainContent = description;

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Yes",
            "Approve this operation");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Yes to All",
            "Approve this and all remaining operations without asking again (2 min)");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Auto",
            "Approve all operations automatically — a floating window lets you stop at any time");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "No",
            "Cancel this operation");

        var result = dialog.Show();
        if (result == TaskDialogResult.CommandLink2) return null;
        if (result == TaskDialogResult.CommandLink1) return true;
        if (result == TaskDialogResult.CommandLink3)
        {
            session.AutoMode = true;
            AutoModeChanged?.Invoke(true);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Shows the critical confirmation window. The user can enable "Allow auto-run".
    /// When enabled, the Yes button counts down from 10 seconds and approves automatically
    /// at zero. The preference is session-scoped and resets when Revit restarts.
    /// </summary>
    public static bool? ConfirmCritical(string action, int elementCount, string? description)
    {
        if (elementCount <= 0) return true;

        try
        {
            var dialog = new CriticalConfirmationWindow(action, elementCount, description);
            return dialog.ShowDialog() == true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Critical confirmation window failed: {ex.Message}");

            // Fail closed: a critical action must never execute because the custom UI failed.
            return false;
        }
    }

    public static event Action<bool>? AutoModeChanged;

    public static void NotifyAutoModeChanged(bool active) => AutoModeChanged?.Invoke(active);

    public static Core.Results.CortexResult<object> CancelledResult()
    {
        return Core.Results.CortexResult<object>.Fail(
            Core.Results.CortexErrorCode.Cancelled,
            "Operation cancelled by user",
            suggestion: "The user declined the confirmation dialog. Ask if they want to retry.");
    }
}
