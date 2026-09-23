using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Hosting;

/// <summary>Port selection shared by the Revit add-in and the stdio MCP server.</summary>
public static class CortexPort
{
    public const string EnvironmentVariable = "REVITCORTEX_PORT";

    public static int? ParseOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port >= 1 && port <= 65535)
            return port;

        // A typo must not silently route commands to a different Revit instance.
        throw new ArgumentException($"{EnvironmentVariable} must be an integer from 1 to 65535.");
    }

    public static int Resolve(string? environmentPort, string settingsPath, int defaultPort = 8080)
    {
        var instancePort = ParseOverride(environmentPort);
        if (instancePort.HasValue) return instancePort.Value;

        try
        {
            if (File.Exists(settingsPath))
            {
                var port = JObject.Parse(File.ReadAllText(settingsPath))["Port"]?.Value<int>();
                if (port is >= 1 and <= 65535) return port.Value;
            }
        }
        catch
        {
            // Preserve the existing fallback for missing/unreadable legacy settings.
        }

        return defaultPort;
    }
}
