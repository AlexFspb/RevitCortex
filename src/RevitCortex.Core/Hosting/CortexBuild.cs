namespace RevitCortex.Core.Hosting;

public static class CortexBuild
{
    public const string Id = "2026.09.25-confirmation-fix.1";
    public static string CoreModuleId => typeof(CortexBuild).Module.ModuleVersionId.ToString("D");
}
