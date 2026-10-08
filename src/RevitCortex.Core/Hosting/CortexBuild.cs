namespace RevitCortex.Core.Hosting;

public static class CortexBuild
{
    public const string Id = "2026.10.08-family-edit-guard.5";
    public static string CoreModuleId => typeof(CortexBuild).Module.ModuleVersionId.ToString("D");
}
