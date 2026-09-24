using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Plugin.Caching;
using RevitCortex.Plugin.Communication;
using RevitCortex.Plugin.Discovery;
using RevitCortex.Plugin.PowerBiLive;
using RevitCortex.Plugin.Threading;
using RevitCortex.Plugin.UI;
using System;
using System.Reflection;

namespace RevitCortex.Plugin;

public class RevitCortexApp : IExternalApplication
{
    private SocketService? _socketService;
    private CortexRouter? _router;
    private CortexSession? _session;
    private DocumentChangeWatcher? _cacheWatcher;
    private UIApplication? _uiApplication;
    private int _port = CortexPort.PrimaryPort;
    private Autodesk.Revit.UI.PushButton? _connectButton;

    private bool _updateNotificationShown;
    private string? _portWarning;
    private PbiSelectHttpListener? _pbiSelectListener;
    private PbiActionEventHandler? _pbiActionHandler;
    private ExternalEvent? _pbiActionEvent;

    public static RevitCortexApp? Instance { get; private set; }

    public event Action? ServiceStateChanged;

    public bool IsServiceRunning
    {
        get
        {
            if (_socketService?.IsRunning != true) return false;
            try
            {
                using var probe = new System.Net.Sockets.TcpClient();
                probe.Connect("127.0.0.1", _port);
                return true;
            }
            catch
            {
                _socketService.Stop();
                return false;
            }
        }
    }

    public int Port => _port;
    public bool IsPortOverridden { get; private set; }
    public bool HasAssignedPort => IsPortOverridden || _socketService?.HasBoundPort == true;
    public UIApplication? UiApplication => _uiApplication;
    public CortexRouter? Router => _router;
    public CortexSession? Session => _session;

    public Result OnStartup(UIControlledApplication application)
    {
        Instance = this;

        AppDomain.CurrentDomain.AssemblyResolve += ResolveBundledDependency;

        try
        {
            LoadPort();
            CreateRibbonPanel(application);

            var store = new SessionStore();
            _session = new CortexSession(store);
            _session.ConfirmAction = ConfirmationHelper.Confirm;
            _session.CriticalConfirmAction = ConfirmationHelper.ConfirmCritical;
            var analyzer = new DocumentAnalyzer();

            var auditLogger = new AuditLogger(CortexEnvironment.Current.AuditLogPath);

            Telemetry.TelemetryBootstrap.Init(application);

            _router = new CortexRouter(_session, analyzer, auditLogger: auditLogger,
                errorReporter: Telemetry.TelemetryBootstrap.Reporter);

            var toolsAssembly = LoadToolsAssembly();
            if (toolsAssembly != null)
                _router.RegisterToolsFromAssembly(toolsAssembly);

            _router.RegisterToolsFromAssembly(Assembly.GetExecutingAssembly());

            var executionHandler = new ToolExecutionHandler(auditLogger);
            var externalEvent = ExternalEvent.Create(executionHandler);
            var dispatcher = new RevitThreadDispatcher(executionHandler, externalEvent);
            _router.SetDispatcher(dispatcher);

            LoadDisabledTools();
            LoadReadOnlyMode();

            RevitCortex.Plugin.Updates.UpdateChecker.UpdateAvailable += OnUpdateAvailable;
            RevitCortex.Plugin.Updates.UpdateChecker.CheckInBackground();

            _pbiActionHandler = new PbiActionEventHandler();
            _pbiActionEvent = ExternalEvent.Create(_pbiActionHandler);

            _socketService = new SocketService(_router, _port);

            application.ControlledApplication.DocumentOpened += OnDocumentOpened;
            application.ControlledApplication.DocumentClosing += OnDocumentClosing;

            _cacheWatcher = new DocumentChangeWatcher(_session);
            _cacheWatcher.Attach(application.ControlledApplication);

            application.Idling += OnIdling;

            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Started. {_router.TotalToolCount} tools registered.");

            Telemetry.TelemetryBootstrap.PromptConsentIfNeeded();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Telemetry.TelemetryBootstrap.Reporter?.Record("_startup", false, "Unknown",
                ex.Message, failureStage: "startup", durationMs: 0, responseBytes: 0);
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Startup failed: {ex}");
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            Telemetry.TelemetryBootstrap.Shutdown();

            _pbiSelectListener?.Dispose();
            _pbiSelectListener = null;
            _socketService?.Stop();
            application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
            application.Idling -= OnIdling;
            if (_uiApplication != null)
                _uiApplication.ViewActivated -= OnViewActivated;

            _cacheWatcher?.Dispose();
            _cacheWatcher = null;

            CleanupTempScripts();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Shutdown error: {ex.Message}");
        }
        return Result.Succeeded;
    }

    public void StartService(Document? activeDocument = null)
    {
        if (_socketService != null && !_socketService.IsRunning)
        {
            if (activeDocument != null && _router != null)
            {
                var locale = LocaleDetector.Detect(activeDocument);
                _router.OnDocumentChanged(activeDocument, locale);
                if (_uiApplication != null)
                    _session?.Store.Set("uiApplication", _uiApplication);
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Session initialized with document: {activeDocument.Title}, locale: {locale}");
            }

            if (IsPortOverridden)
                _socketService.Start();
            else
                _port = _socketService.StartOnFirstAvailablePort(CortexPort.AutomaticPorts(CortexEnvironment.Current.IsDev));

            _session!.BridgePort = _port;

            if (_pbiSelectListener == null && _pbiActionHandler != null && _pbiActionEvent != null)
            {
                var handler = _pbiActionHandler;
                handler.BindExternalEvent(_pbiActionEvent);

                _pbiSelectListener = new PbiSelectHttpListener(
                    new PbiSelectHttpListener.Callbacks(
                        selection:       (ids, action) => _uiApplication == null ? null : handler.DispatchSelection(ids, action),
                        color:           items         => _uiApplication == null ? null : handler.DispatchColor(items),
                        resetOverrides:  ()            => _uiApplication == null ? null : handler.DispatchReset(),
                        createView:      (ids, name)   => _uiApplication == null ? null : handler.DispatchCreateView(ids, name),
                        selectionRich:   input => RichDispatch(handler, input, isCreateView: false),
                        createViewRich:  input => RichDispatch(handler, input, isCreateView: true)),
                    port: 27016);
                _pbiSelectListener.Start();
            }

            UpdateConnectionButtonIcon();
            ServiceStateChanged?.Invoke();
        }
    }

    public void StopService()
    {
        _pbiSelectListener?.Stop();
        _pbiSelectListener = null;
        _socketService?.Stop();
        UpdateConnectionButtonIcon();
        ServiceStateChanged?.Invoke();
    }

    private PbiSelectHttpListener.RichRequestResult RichDispatch(
        PbiActionEventHandler handler,
        PbiSelectHttpListener.RichRequestInput input,
        bool isCreateView)
    {
        if (_uiApplication == null)
            return PbiSelectHttpListener.RichRequestResult.Fail("no_application", "RevitCortex not bound to a UIApplication.");

        var req = isCreateView
            ? handler.DispatchCreateViewRich(input.ElementIds, input.UniqueIds, input.ViewName, input.DocumentTitle)
            : handler.DispatchSelectionRich(input.ElementIds, input.UniqueIds, input.Action, input.DocumentTitle);

        if (!string.IsNullOrEmpty(req.Error))
        {
            var sep = req.Error!.IndexOf(':');
            return sep > 0
                ? PbiSelectHttpListener.RichRequestResult.Fail(req.Error.Substring(0, sep), req.Error.Substring(sep + 1))
                : PbiSelectHttpListener.RichRequestResult.Fail(req.Error, req.Error);
        }
        return PbiSelectHttpListener.RichRequestResult.Ok(req.Result);
    }

    private void UpdateConnectionButtonIcon()
    {
        if (_connectButton == null) return;
        bool active = IsServiceRunning;
        _connectButton.Image = IconFactory.CreateConnectionIcon(16, active);
        _connectButton.LargeImage = IconFactory.CreateConnectionIcon(32, active);
        _connectButton.ToolTip = active
            ? $"RevitCortex 2026 running on port {_port} — click to stop"
            : "Start RevitCortex 2026 server";
    }

    private void CreateRibbonPanel(UIControlledApplication application)
    {
        string panelTitle = CortexEnvironment.Current.IsDev ? "RevitCortex 2026 Dev" : "RevitCortex 2026";
        RibbonPanel panel = application.CreateRibbonPanel(panelTitle);
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;

        var connectBtnData = new PushButtonData(
            "ID_CORTEX_TOGGLE", "Cortex\r\nSwitch",
            assemblyLocation, "RevitCortex.Plugin.Commands.ToggleConnection");
        connectBtnData.ToolTip = "Start RevitCortex 2026 server";
        connectBtnData.Image = IconFactory.CreateConnectionIcon(16, false);
        connectBtnData.LargeImage = IconFactory.CreateConnectionIcon(32, false);
        _connectButton = panel.AddItem(connectBtnData) as Autodesk.Revit.UI.PushButton;

        var settingsBtn = new PushButtonData(
            "ID_CORTEX_SETTINGS", "Settings",
            assemblyLocation, "RevitCortex.Plugin.Commands.OpenSettings");
        settingsBtn.ToolTip = "RevitCortex 2026 settings";
        settingsBtn.Image = IconFactory.CreateSettingsIcon(16);
        settingsBtn.LargeImage = IconFactory.CreateSettingsIcon(32);
        panel.AddItem(settingsBtn);

        var powerBiBtn = new PushButtonData(
            "ID_CORTEX_POWERBI", "Power BI\r\nExport",
            assemblyLocation, "RevitCortex.Plugin.Commands.OpenPowerBiExport");
        powerBiBtn.ToolTip = "Esporta dati e parametri in CSV per Power BI";
        powerBiBtn.LongDescription =
            "Apre il wizard di export Power BI: scegli categorie e parametri, " +
            "salva profili riutilizzabili, abilita auto-export al salvataggio e " +
            "registra il protocol handler revitcortex:// per drillthrough da PBI a Revit.";
        powerBiBtn.Image = IconFactory.CreatePowerBiIcon(16);
        powerBiBtn.LargeImage = IconFactory.CreatePowerBiIcon(32);
        panel.AddItem(powerBiBtn);

        var supportBtn = new PushButtonData(
            "ID_CORTEX_SUPPORT", "Diagnostic\r\nReport",
            assemblyLocation, "RevitCortex.Plugin.Commands.SendSupportReport");
        supportBtn.ToolTip = "Create a RevitCortex 2026 diagnostic report";
        supportBtn.LongDescription =
            "Collects diagnostic logs, settings and the recent Revit journal into a local ZIP " +
            "report and opens it in Explorer. Nothing is uploaded or emailed automatically.";
        supportBtn.Image = IconFactory.CreateSupportIcon(16);
        supportBtn.LargeImage = IconFactory.CreateSupportIcon(32);
        panel.AddItem(supportBtn);
    }

    private void OnDocumentOpened(object? sender, DocumentOpenedEventArgs args)
    {
        // OpenDocumentFile may open a background family. Only ActiveUIDocument
        // defines the MCP target; ViewActivated/Idling reconcile it when ready.
        SynchronizeActiveDocument();
    }

    private void OnDocumentClosing(object? sender, DocumentClosingEventArgs args)
    {
        try
        {
            _router?.OnDocumentClosing(args.Document);
            if (_uiApplication != null)
                _session?.Store.Set("uiApplication", _uiApplication);
            // The listener belongs to the Revit process, not to a document.
            // Keep it (and its port) alive even when the last document closes.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Error on document closing: {ex.Message}");
        }
    }

    private void SynchronizeActiveDocument()
    {
        if (_uiApplication == null || _router == null) return;
        try
        {
            var doc = _uiApplication.ActiveUIDocument?.Document;
            if (doc != null && !doc.IsValidObject) doc = null;
            var currentDoc = _session?.CaptureDocumentContext().Document;
            if (!object.Equals(currentDoc, doc))
            {
                _router.SynchronizeActiveDocument(doc, doc == null ? "en" : LocaleDetector.Detect(doc));
                _session?.Store.Set("uiApplication", _uiApplication);
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Active document synchronized: {doc?.Title ?? "(none)"}");
            }
        }
        catch (Exception ex)
        {
            // Do not retain a stale model if analysis fails. The next Idling retries.
            try
            {
                _router.SynchronizeActiveDocument(null);
                _session?.Store.Set("uiApplication", _uiApplication);
            }
            catch (Exception cleanupError)
            {
                System.Diagnostics.Trace.WriteLine($"[RevitCortex] Context cleanup failed: {cleanupError}");
            }
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Active document synchronization failed: {ex.Message}");
        }
    }

    private void OnIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
        if (_uiApplication == null)
        {
            _uiApplication = sender as UIApplication;
            if (_uiApplication == null) return;
            _uiApplication.ViewActivated += OnViewActivated;
            _session?.Store.Set("uiApplication", _uiApplication);
            if (RevitCortex.Plugin.Updates.UpdateChecker.Latest?.HasUpdate == true)
                ShowUpdateNotification();
        }

        // Runs after close completes OR is cancelled, and handles an empty Revit.
        SynchronizeActiveDocument();
        if (_portWarning != null)
        {
            var warning = _portWarning;
            _portWarning = null; // One attempt per startup, including if notification fails.
            try { new PortWarningWindow(warning, _uiApplication.MainWindowHandle).Show(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[RevitCortex] Port warning UI failed: {ex.Message}"); }
        }
    }

    private void OnUpdateAvailable()
    {
        if (_uiApplication == null) return;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            (System.Action)ShowUpdateNotification);
    }

    private void ShowUpdateNotification()
    {
        if (_updateNotificationShown) return;
        _updateNotificationShown = true;

        try
        {
            var win = new UI.UpdateNotificationWindow();
            win.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not show update notification: {ex.Message}");
        }
    }

    private void OnViewActivated(object? sender, ViewActivatedEventArgs e)
    {
        SynchronizeActiveDocument();
    }

    private void LoadPort()
    {
        var overrideValue = Environment.GetEnvironmentVariable(CortexPort.EnvironmentVariable);
        _port = CortexPort.ResolvePlugin(overrideValue, CortexEnvironment.Current.IsDev,
            out var overridden, out _portWarning);
        IsPortOverridden = overridden;
        if (_portWarning != null) System.Diagnostics.Trace.WriteLine($"[RevitCortex] {_portWarning}");
        System.Diagnostics.Trace.WriteLine(
            $"[RevitCortex] Port configured: {_port} (process override: {IsPortOverridden})");
    }

    private void LoadReadOnlyMode()
    {
        try
        {
            string settingsPath = CortexEnvironment.Current.SettingsFilePath;
            if (System.IO.File.Exists(settingsPath))
            {
                var json = System.IO.File.ReadAllText(settingsPath);
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Newtonsoft.Json.Linq.JObject>(json);
                var readOnly = settings?["ReadOnlyMode"]?.ToObject<bool>() ?? false;
                _router!.ReadOnlyMode = readOnly;
                if (readOnly)
                    System.Diagnostics.Trace.WriteLine("[RevitCortex] Read-only mode is ON");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load read-only setting: {ex.Message}");
        }
    }

    private void LoadDisabledTools()
    {
        try
        {
            string settingsPath = CortexEnvironment.Current.SettingsFilePath;
            if (System.IO.File.Exists(settingsPath))
            {
                var json = System.IO.File.ReadAllText(settingsPath);
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Newtonsoft.Json.Linq.JObject>(json);
                var disabled = settings?["DisabledTools"]?
                    .ToObject<string[]>() ?? Array.Empty<string>();
                _router!.SetDisabledTools(disabled);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load disabled tools setting: {ex.Message}");
        }
    }

    private static void CleanupTempScripts()
    {
        var scriptsFolder = CortexEnvironment.Current.ScriptsFolder;
        if (!System.IO.Directory.Exists(scriptsFolder)) return;
        foreach (var file in System.IO.Directory.GetFiles(scriptsFolder, "*.cs"))
        {
            try
            {
                using var reader = new System.IO.StreamReader(file);
                var firstLine = reader.ReadLine() ?? "";
                if (firstLine.TrimStart().StartsWith("// TEMP", StringComparison.OrdinalIgnoreCase))
                    System.IO.File.Delete(file);
            }
            catch { }
        }
    }

    private Assembly? LoadToolsAssembly()
    {
        try
        {
            var pluginDir = System.IO.Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location)!;
            var toolsPath = System.IO.Path.Combine(pluginDir, "RevitCortex.Tools.dll");
            return Assembly.LoadFrom(toolsPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Could not load Tools assembly: {ex.Message}");
            return null;
        }
    }

    private static Assembly? ResolveBundledDependency(object? sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name).Name;
        if (string.IsNullOrEmpty(requested))
            return null;

        bool wanted = requested.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
            || requested == "System.Collections.Immutable"
            || requested == "System.Reflection.Metadata";
        if (!wanted)
            return null;

        try
        {
            var dir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dir == null)
                return null;
            var candidate = System.IO.Path.Combine(dir, requested + ".dll");
            return System.IO.File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
        catch
        {
            return null;
        }
    }
}
