using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Plugin.PowerBi;

namespace RevitCortex.Plugin.Commands;

[Transaction(TransactionMode.Manual)]
public class OpenPowerBiExport : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("Power BI Export", "Apri prima un modello Revit.");
                return Result.Cancelled;
            }

            var window = new PowerBiExportWindow(doc)
            {
                // Override the legacy upstream XAML title so runtime branding is fork-specific.
                Title = "RevitCortex 2026 — Power BI Export"
            };
            try
            {
                _ = new System.Windows.Interop.WindowInteropHelper(window)
                {
                    Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle
                };
            }
            catch
            {
                // Owner handle not strictly required; ignore if unavailable
            }
            window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            window.Show();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            var dlg = new TaskDialog("Power BI Export — errore")
            {
                MainInstruction = "Impossibile aprire la finestra.",
                MainContent = $"{ex.GetType().Name}: {ex.Message}",
                ExpandedContent = ex.StackTrace ?? "",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dlg.Show();
            message = ex.Message;
            return Result.Failed;
        }
    }
}
