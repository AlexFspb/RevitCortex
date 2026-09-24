using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Core.Hosting;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Commands;

/// <summary>
/// Creates a local diagnostic ZIP for this independent Revit 2026 fork.
/// Nothing is uploaded or emailed automatically.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class SendSupportReport : IExternalCommand
{
    private const int DefaultKeepCount = 10;
    private static int _running;

    public static string ReportsFolder => CortexEnvironment.Current.SupportReportsFolder;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var title = Localization.T("support.title");

        if (System.Threading.Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            TaskDialog.Show(title, Localization.T("support.already_running"));
            return Result.Succeeded;
        }

        try
        {
            Directory.CreateDirectory(ReportsFolder);
            RotateOldReports(ReportsFolder, ReadKeepCount());

            var zipPath = BuildReportZip(commandData);

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
            }
            catch { }

            TaskDialog.Show(title,
                $"Diagnostic report created locally:\n\n{zipPath}\n\nNothing was sent automatically.");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = Localization.T("support.package_failed", ex.Message);
            TaskDialog.Show(title, message);
            return Result.Failed;
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _running, 0);
        }
    }

    private static int ReadKeepCount()
    {
        try
        {
            string settingsPath = CortexEnvironment.Current.SettingsFilePath;
            if (!File.Exists(settingsPath)) return DefaultKeepCount;

            var json = File.ReadAllText(settingsPath);
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
            var n = obj["SupportReportKeepCount"]?.ToObject<int?>();
            if (n is int v && v >= 1 && v <= 200) return v;
        }
        catch { }
        return DefaultKeepCount;
    }

    private static void RotateOldReports(string folder, int keep)
    {
        try
        {
            var zips = new DirectoryInfo(folder)
                .EnumerateFiles("RevitCortex-BugReport-*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            for (int i = keep; i < zips.Count; i++)
            {
                try { zips[i].Delete(); } catch { }
            }
        }
        catch { }
    }

    public static (int deleted, int failed, long bytesFreed) DeleteAllReports()
    {
        int deleted = 0, failed = 0;
        long bytes = 0;
        try
        {
            if (!Directory.Exists(ReportsFolder)) return (0, 0, 0);
            foreach (var f in new DirectoryInfo(ReportsFolder)
                .EnumerateFiles("RevitCortex-BugReport-*.zip", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    long len = f.Length;
                    f.Delete();
                    deleted++;
                    bytes += len;
                }
                catch { failed++; }
            }
        }
        catch { }
        return (deleted, failed, bytes);
    }

    public static int CountReports()
    {
        try
        {
            if (!Directory.Exists(ReportsFolder)) return 0;
            return new DirectoryInfo(ReportsFolder)
                .EnumerateFiles("RevitCortex-BugReport-*.zip", SearchOption.TopDirectoryOnly)
                .Count();
        }
        catch { return 0; }
    }

    public static long TotalReportsBytes()
    {
        try
        {
            if (!Directory.Exists(ReportsFolder)) return 0;
            return new DirectoryInfo(ReportsFolder)
                .EnumerateFiles("RevitCortex-BugReport-*.zip", SearchOption.TopDirectoryOnly)
                .Sum(f => f.Length);
        }
        catch { return 0; }
    }

    private static string BuildReportZip(ExternalCommandData commandData)
    {
        string rcFolder = CortexEnvironment.Current.RootFolder;
        string reportsDir = ReportsFolder;
        Directory.CreateDirectory(reportsDir);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        string zipPath = Path.Combine(reportsDir,
            $"RevitCortex-BugReport-{Environment.UserName}-{stamp}.zip");

        var included = new List<string>();
        var skipped = new List<string>();

        var candidates = new List<(string src, string entry, bool optional)>
        {
            (Path.Combine(rcFolder, "audit.jsonl"),               "audit.jsonl",            false),
            (Path.Combine(rcFolder, "usage-mcp.db"),              "usage-mcp.db",           true),
            (Path.Combine(rcFolder, "logs", "token-usage.jsonl"), "logs/token-usage.jsonl", true),
            (Path.Combine(rcFolder, "settings.json"),             "settings.json",          true),
        };

        using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (src, entry, optional) in candidates)
            {
                if (!File.Exists(src))
                {
                    skipped.Add($"{entry} (not found)");
                    continue;
                }

                try
                {
                    AddFileSafely(zip, src, entry);
                    included.Add(entry);
                }
                catch (Exception ex)
                {
                    skipped.Add($"{entry} ({ex.Message})");
                    if (!optional) throw;
                }
            }

            var journal = FindLatestJournal();
            if (journal != null)
            {
                try
                {
                    var info = new FileInfo(journal);
                    if (info.Length <= 10 * 1024 * 1024)
                    {
                        AddFileSafely(zip, journal, $"journal/{info.Name}");
                        included.Add($"journal/{info.Name} ({info.Length / 1024} KB)");
                    }
                    else
                    {
                        skipped.Add($"journal/{info.Name} (too large: {info.Length / 1024 / 1024} MB)");
                    }
                }
                catch (Exception ex)
                {
                    skipped.Add($"journal ({ex.Message})");
                }
            }

            var contextEntry = zip.CreateEntry("context.txt");
            using var writer = new StreamWriter(contextEntry.Open(), Encoding.UTF8);
            WriteContextFile(writer, commandData, included, skipped);
        }

        return zipPath;
    }

    private static void AddFileSafely(ZipArchive zip, string source, string entryName)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var dst = entry.Open();
        src.CopyTo(dst);
    }

    private static string? FindLatestJournal()
    {
        try
        {
            var app = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var revitRoot = Path.Combine(app, "Autodesk");
            if (!Directory.Exists(revitRoot)) return null;

            var journal = Directory
                .EnumerateDirectories(revitRoot, "Autodesk Revit*", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    var j = Path.Combine(d, "Journals");
                    return Directory.Exists(j)
                        ? Directory.EnumerateFiles(j, "journal.*.txt")
                        : Enumerable.Empty<string>();
                })
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .FirstOrDefault();

            return journal?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteContextFile(StreamWriter w, ExternalCommandData commandData,
        IReadOnlyCollection<string> included, IReadOnlyCollection<string> skipped)
    {
        w.WriteLine("RevitCortex 2026 diagnostic context");
        w.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} local / {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        w.WriteLine($"OS:        {Environment.OSVersion}");
        w.WriteLine($"Culture:   {System.Globalization.CultureInfo.CurrentUICulture.Name}");

        try
        {
            var app = commandData.Application.Application;
            w.WriteLine($"Revit:     {app.VersionName} build {app.VersionBuild} ({app.VersionNumber})");
            w.WriteLine($"Language:  {app.Language}");
        }
        catch (Exception ex)
        {
            w.WriteLine($"Revit:     (unreadable: {ex.Message})");
        }

        try
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            w.WriteLine($"Document open: {doc != null}");
            if (doc != null)
                w.WriteLine($"Workshared:    {doc.IsWorkshared}");
        }
        catch (Exception ex)
        {
            w.WriteLine($"Document:  (unreadable: {ex.Message})");
        }

        w.WriteLine();
        var pluginVersion = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "unknown";
        w.WriteLine($"plugin_version: {pluginVersion}");
        w.WriteLine("Plugin assembly: " + System.Reflection.Assembly.GetExecutingAssembly().Location);

        try
        {
            var env = CortexEnvironment.Current;
            w.WriteLine($"Profile:        {env.ProfileName}{(env.IsDev ? " (DEV build)" : "")}");
            w.WriteLine($"Config folder:  {env.RootFolder}");
            w.WriteLine($"Settings file:  {env.SettingsFilePath}");
            var cortex = RevitCortexApp.Instance;
            w.WriteLine($"Bridge port:    {(cortex?.Session?.BridgePort?.ToString() ?? "not bound")}");
            w.WriteLine($"Bridge running: {cortex?.IsServiceRunning == true}");
        }
        catch (Exception ex)
        {
            w.WriteLine($"Profile:        (unreadable: {ex.Message})");
        }

        w.WriteLine();
        w.WriteLine("Included files:");
        foreach (var item in included) w.WriteLine("  + " + item);
        if (skipped.Count > 0)
        {
            w.WriteLine("Skipped files:");
            foreach (var item in skipped) w.WriteLine("  - " + item);
        }
    }
}
