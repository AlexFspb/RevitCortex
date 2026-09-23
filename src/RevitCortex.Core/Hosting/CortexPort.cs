using System;
using System.Globalization;

namespace RevitCortex.Core.Hosting;

/// <summary>Port selection shared by the Revit add-in and the stdio MCP server.</summary>
public static class CortexPort
{
    public const string EnvironmentVariable = "REVITCORTEX_PORT";
    public const int PrimaryPort = 8080;
    public const int SecondaryPort = 8888;

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
