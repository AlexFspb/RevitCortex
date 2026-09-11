using RevitCortex.Core.Hosting;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Licensing is intentionally transparent in this fork.
/// The Revit 2026 build must never block write commands because of a Premium/dev license state.
/// </summary>
internal static class LicenseBootstrap
{
    public static LicenseGate? Gate { get; private set; }
    public static LicenseManager? Manager { get; private set; }
    public static ILicenseBackend? Backend { get; private set; }
    public static IFingerprintProvider? Fingerprint { get; private set; }

    public static void Init(CortexEnvironment env)
    {
        // This fork is used as an unrestricted local Revit 2026 tool.
        // Keep a gate instance so the router wiring stays unchanged, but make it
        // explicitly transparent for every configuration (Debug and Release).
        Gate = new LicenseGate(() => LicenseState.Active, isDev: true);
        Manager = null;
        Backend = null;
        Fingerprint = null;
    }
}
