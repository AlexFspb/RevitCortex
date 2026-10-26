using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitCortex.Plugin.Commands;

[Transaction(TransactionMode.Manual)]
public class ToggleAutomaticExecution : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var host = RevitCortexApp.Instance;
        if (host == null) return Result.Failed;
        host.SetAutomaticExecution(!host.AutomaticExecutionEnabled);
        return Result.Succeeded;
    }
}

public class CortexCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
