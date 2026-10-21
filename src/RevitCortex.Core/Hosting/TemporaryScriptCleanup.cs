using System;
using System.IO;

namespace RevitCortex.Core.Hosting;

public static class TemporaryScriptCleanup
{
    /// <summary>Only this run's TEMP files; reusable scripts and other processes are never removed.</summary>
    public static void Clean(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder, "*.cs"))
        {
            try
            {
                string firstLine;
                using (var reader = new StreamReader(file)) firstLine = reader.ReadLine() ?? "";
                if (firstLine.TrimStart().StartsWith("// TEMP", StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[RevitCortex] TEMP cleanup failed: {ex.Message}"); }
        }
        try { Directory.Delete(folder, recursive: false); }
        catch (IOException) { /* Retain reusable or unknown files. */ }
    }
}
