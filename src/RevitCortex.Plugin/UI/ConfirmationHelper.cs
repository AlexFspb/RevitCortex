using System;

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
    /// <returns>true for this operation only; false when cancelled or the UI fails.</returns>
    public static bool? Confirm(string action, int elementCount, string? description)
    {
        if (elementCount <= 0) return true;
        try
        {
            var dialog = new OperationConfirmationWindow(action, elementCount, description);
            return dialog.ShowDialog() == true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Operation confirmation window failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Shows the critical confirmation window. The user can enable "Allow auto-run".
    /// When enabled, the Yes button counts down from 3 seconds and approves automatically
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
            return false;
        }
    }
    public static Core.Results.CortexResult<object> CancelledResult()
    {
        return Core.Results.CortexResult<object>.Fail(
            Core.Results.CortexErrorCode.Cancelled,
            "Operation cancelled by user",
            suggestion: "The user declined the confirmation dialog. Ask if they want to retry.");
    }
}
