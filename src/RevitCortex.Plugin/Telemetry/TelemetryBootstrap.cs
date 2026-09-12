using System;
using System.IO;
using Autodesk.Revit.UI;
using RevitCortex.Core.Telemetry;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Telemetry;

/// <summary>
/// Builds the process-wide telemetry stack (config, queue, sender, reporter)
/// and owns the first-run consent prompt. Everything here is best-effort:
/// telemetry failures must never affect Revit startup.
/// </summary>
internal static class TelemetryBootstrap
{
    public static ErrorReporter? Reporter { get; private set; }
    public static TelemetryConfig? Config { get; private set; }
    private static TelemetrySender? _sender;

    public static void Init(UIControlledApplication application)
    {
        try
        {
            var config = TelemetryConfig.Load();
            var queue = new TelemetryQueue(
                RevitCortex.Core.Hosting.CortexEnvironment.Current.TelemetryQueuePath);
            var sender = new TelemetrySender(config, queue);
            sender.KnownIssueMatched += m =>
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Known issue matched: {m.IssueId} fixed in {m.FixVersion}");

            int revitYear = 0;
            try { revitYear = int.Parse(application.ControlledApplication.VersionNumber); }
            catch { }

            var env = new TelemetryEnvironment
            {
                PluginVersion = typeof(TelemetryBootstrap).Assembly.GetName()
                    .Version?.ToString() ?? "unknown",
                RevitVersion = revitYear.ToString(),
                Target = revitYear > 2000 ? "R" + (revitYear - 2000) : "unknown",
                OsMajor = "Windows " + Environment.OSVersion.Version.ToString(2),
                Locale = Localization.Locale
            };

            var reporter = new ErrorReporter(config, queue, sender, env);
            reporter.RepeatedFailureDetected += (fp, count) =>
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Repeated failure {fp} x{count}");

            sender.Start();
            Config = config;
            Reporter = reporter;
            _sender = sender;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Telemetry init failed: {ex.Message}");
        }
    }

    /// <summary>First-run consent. Startup runs on the Revit UI thread, so a
    /// TaskDialog is legal here. Cancel/close = not answered, ask next startup.</summary>
    public static void PromptConsentIfNeeded()
    {
        try
        {
            var config = Config;
            if (config == null || !config.NeedsConsentPrompt) return;

            var dlg = new TaskDialog("RevitCortex 2026")
            {
                MainInstruction = Localization.T("telemetry.consent_instruction"),
                MainContent = Localization.T("telemetry.consent_body"),
                CommonButtons = TaskDialogCommonButtons.None,
                AllowCancellation = true,
                TitleAutoPrefix = false
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                Localization.T("telemetry.consent_enable"));
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                Localization.T("telemetry.consent_decline"));

            var r = dlg.Show();
            if (r == TaskDialogResult.CommandLink1) config.MarkConsent(true);
            else if (r == TaskDialogResult.CommandLink2) config.MarkConsent(false);
        }
        catch { }
    }

    public static void Shutdown()
    {
        try { _sender?.Dispose(); } catch { }
        _sender = null;
        Reporter = null;
    }
}
