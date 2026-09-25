namespace RevitCortex.Core.Hosting;

public static class CortexBuild
{
    public const string Id = "2026.09.26-confirmation-timeout.3";
    public static string CoreModuleId => typeof(CortexBuild).Module.ModuleVersionId.ToString("D");
}
