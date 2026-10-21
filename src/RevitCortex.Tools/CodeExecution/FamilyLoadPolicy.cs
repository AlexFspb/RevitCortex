using Autodesk.Revit.DB;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// Noninteractive reload decisions usable from a script method body without declaring a class.
/// Both decisions are mandatory. Constructing this policy opts into continuing reload conflicts;
/// it does not approve a Cortex request, resolve model failures or open transactions.
/// </summary>
public sealed class FamilyLoadPolicy : IFamilyLoadOptions
{
    public bool OverwriteParameterValues { get; }
    public bool UseProjectSharedFamilies { get; }

    public FamilyLoadPolicy(bool overwriteParameterValues, bool useProjectSharedFamilies)
    {
        OverwriteParameterValues = overwriteParameterValues;
        UseProjectSharedFamilies = useProjectSharedFamilies;
    }

    public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
    {
        overwriteParameterValues = OverwriteParameterValues;
        return true;
    }

    public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
        out FamilySource source, out bool overwriteParameterValues)
    {
        source = UseProjectSharedFamilies ? FamilySource.Project : FamilySource.Family;
        overwriteParameterValues = OverwriteParameterValues;
        return true;
    }
}
