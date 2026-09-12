using System;
using System.IO;
using System.Reflection;

namespace RevitCortex.Plugin.Updates;

/// <summary>
/// Update state facade for the Revit 2026 fork.
///
/// Automatic update checks are intentionally disabled in this fork. The upstream
/// RevitCortex updater points to LuDattilo/revitcortex-releases; consuming that
/// channel could replace this customized Revit 2026 build with the upstream
/// multi-version build. Keep this facade so the existing Settings UI and tests
/// retain a stable API. A fork-owned release channel can be wired here later.
/// </summary>
public static class UpdateChecker
{
    public static bool AutomaticUpdatesEnabled => false;

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public static UpdateInfo? Latest { get; private set; }

    public enum DownloadState { Idle, Downloading, Ready, Installing, Done, Error }

    private static volatile DownloadState _state = DownloadState.Idle;
    public static DownloadState State => _state;

    private static volatile string? _downloadError;
    public static string? DownloadError => _downloadError;

    private static volatile string? _extractedPath;
    public static string? ExtractedPath => _extractedPath;

    private static readonly object _progressLock = new();
    private static (long Received, long Total) _downloadProgress;
    public static (long Received, long Total) DownloadProgress
    {
        get { lock (_progressLock) return _downloadProgress; }
    }

    /// <summary>
    /// Kept for compatibility with RevitCortexApp startup. No network request is
    /// made while this fork has no dedicated release channel.
    /// </summary>
    public static void CheckInBackground()
    {
        Latest = null;
        _state = DownloadState.Idle;
        _downloadError = null;
        System.Diagnostics.Trace.WriteLine(
            "[RevitCortex] Automatic updates are disabled for the Revit 2026 fork.");
    }

    /// <summary>
    /// Event retained for API compatibility. It is not raised while automatic
    /// updates are disabled.
    /// </summary>
    public static event Action? UpdateAvailable;

    /// <summary>
    /// Download is unavailable until a fork-owned update manifest is configured.
    /// </summary>
    public static void StartDownloadAsync()
    {
        _state = DownloadState.Error;
        _downloadError = "Automatic updates are disabled for this Revit 2026 fork.";
    }

    public static void CancelDownload()
    {
        _state = DownloadState.Idle;
        _downloadError = null;
        _extractedPath = null;
        lock (_progressLock) { _downloadProgress = (0, 0); }
    }

    /// <summary>
    /// No automatic installer is launched by this fork until a dedicated release
    /// channel is configured.
    /// </summary>
    public static bool LaunchInstaller()
    {
        _downloadError = "Automatic updates are disabled for this Revit 2026 fork.";
        return false;
    }

    public static void ResetDownload()
    {
        _state = DownloadState.Idle;
        _downloadError = null;
        _extractedPath = null;
        lock (_progressLock) { _downloadProgress = (0, 0); }
    }

    // Kept because security tests and future fork-owned updater code can reuse them.
    private static readonly string[] AllowedDownloadHostSuffixes =
    {
        "githubusercontent.com",
        "github.com",
        "1drv.ms",
        "onedrive.live.com",
        "sharepoint.com",
    };

    public static bool IsTrustedDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri is null) return false;
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;

        var host = uri.Host;
        foreach (var suffix in AllowedDownloadHostSuffixes)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool Sha256Matches(string filePath, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return false;
        if (!File.Exists(filePath)) return false;

        string actual;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var stream = File.OpenRead(filePath))
        {
            var hash = sha.ComputeHash(stream);
            actual = BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        var expected = expectedHex!.Trim().Replace(" ", string.Empty);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}

public class UpdateInfo
{
    public Version RemoteVersion { get; }
    public string DownloadUrl { get; }
    public string Changelog { get; }
    public bool HasUpdate { get; }
    public string? Sha256 { get; }

    public UpdateInfo(Version remoteVersion, string downloadUrl, string changelog, bool hasUpdate, string? sha256 = null)
    {
        RemoteVersion = remoteVersion;
        DownloadUrl = downloadUrl;
        Changelog = changelog;
        HasUpdate = hasUpdate;
        Sha256 = sha256;
    }
}
