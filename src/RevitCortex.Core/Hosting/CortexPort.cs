using System;
using System.Globalization;

namespace RevitCortex.Core.Hosting;

/// <summary>Port selection shared by the Revit add-in and the stdio MCP server.</summary>
public static class CortexPort
{
    public const string EnvironmentVariable = "REVITCORTEX_PORT";
    public const int PrimaryPort = 8080;
    public const int SecondaryPort = 8888;
    public const int DevPrimaryPort = 8081;
    public const int DevSecondaryPort = 8889;

    public static int[] AutomaticPorts(bool isDev) => isDev
        ? new[] { DevPrimaryPort, DevSecondaryPort }
        : new[] { PrimaryPort, SecondaryPort };

    public static int ResolvePlugin(string? value, bool isDev, out bool overridden, out string? warning)
    {
        warning = null;
        try
        {
            var port = ParseOverride(value);
            overridden = port.HasValue;
            return port ?? AutomaticPorts(isDev)[0];
        }
        catch (ArgumentException ex)
        {
            overridden = false;
            var ports = AutomaticPorts(isDev);
            warning = $"{ex.Message} Using automatic ports {ports[0]}/{ports[1]}.";
            return ports[0];
        }
    }

    public static int? ParseOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port >= 1 && port <= 65535)
            return port;

        // A typo must not silently route commands to a different Revit instance.
        throw new ArgumentException($"{EnvironmentVariable} must be an integer from 1 to 65535.");
    }

    // Shared settings are deliberately not consulted: another Revit window must
    // not change the next client's destination. Only the plugin tries SecondaryPort.
    public static int Resolve(string? environmentPort) => ParseOverride(environmentPort) ?? PrimaryPort;
}
