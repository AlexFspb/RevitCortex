using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Newtonsoft.Json;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

/// <summary>UI-thread presentation watchdog. Never grants approval, even when a window is invisible.</summary>
internal static class ConfirmationPresentation
{
    public static bool? Show(Window dialog, Func<bool?> show)
    {
        var request = ToolRequestLifetime.Current;
        request?.ThrowIfExpired();
        var owner = request?.OwnerHandle ?? IntPtr.Zero;
        if (owner != IntPtr.Zero) new WindowInteropHelper(dialog).Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var elapsed = Stopwatch.StartNew();
        bool finished = false, rendered = false, loaded = false;
        Exception? failure = null;
        var id = Guid.NewGuid().ToString("N");
        void Log(string phase, Exception? error = null)
        {
            try
            {
                var folder = Path.Combine(CortexEnvironment.Current.SupportReportsFolder, "confirmation");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, $"lifecycle-{Environment.ProcessId}.jsonl"),
                    JsonConvert.SerializeObject(new { utc = DateTime.UtcNow, id, phase, pid = Environment.ProcessId,
                        buildId = CortexBuild.Id, coreModuleId = CortexBuild.CoreModuleId,
                        requestPhase = request?.Phase, owner = owner.ToInt64(),
                        hwnd = new WindowInteropHelper(dialog).Handle.ToInt64(), loaded, rendered,
                        visible = dialog.IsVisible, left = dialog.Left, top = dialog.Top,
                        width = dialog.ActualWidth, height = dialog.ActualHeight, error = error?.ToString() }) + Environment.NewLine);
            }
            catch { }
        }
        void Cancel(Exception reason)
        {
            if (finished) return;
            failure ??= reason;
            Log("cancel_requested", reason);
            // Close works even before Loaded; DialogResult requires a fully modal window.
            try { dialog.Close(); }
            catch (Exception ex) { failure = new AggregateException(reason, ex); Log("close_failed", ex); }
        }
        void Place()
        {
            try { PlaceOnOwnerMonitor(dialog, owner); }
            catch (Exception ex) { Log("placement_failed", ex); Cancel(ex); }
        }
        EventHandler initialized = (_, _) => { Place(); Log("source_initialized"); };
        RoutedEventHandler onLoaded = (_, _) => { loaded = true; Place(); Log("loaded"); };
        EventHandler onRendered = (_, _) => { rendered = true; Place(); Log("content_rendered"); };
        EventHandler closed = (_, _) => { finished = true; Log("closed"); };
        dialog.SourceInitialized += initialized;
        dialog.Loaded += onLoaded;
        dialog.ContentRendered += onRendered;
        dialog.Closed += closed;
        var watchdog = new DispatcherTimer(DispatcherPriority.Send, dialog.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(250) };
        EventHandler tick = (_, _) =>
        {
            if (finished) return;
            if (request?.IsExpired == true)
                Cancel(new ConfirmationExpiredException());
            else if (elapsed.Elapsed >= TimeSpan.FromSeconds(5) && (!loaded || !rendered || !dialog.IsVisible))
                Cancel(new InvalidOperationException("Confirmation did not become visible and render within 5 seconds."));
            else if (elapsed.Elapsed >= TimeSpan.FromSeconds(120))
            {
                request?.Expire("confirmation", 120000);
                Cancel(new ConfirmationExpiredException());
            }
        };
        watchdog.Tick += tick;
        // Registration only queues cleanup; the socket timeout never blocks on the UI thread.
        using var registration = (request?.Expiration ?? CancellationToken.None).Register(() =>
        {
            try { dialog.Dispatcher.BeginInvoke(DispatcherPriority.Send,
                new Action(() => Cancel(new ConfirmationExpiredException()))); }
            catch (InvalidOperationException) { }
        });
        try
        {
            request?.ThrowIfExpired();
            Log("show_requested");
            watchdog.Start(); // Independent of Loaded and of the auto-approve timer.
            var result = show();
            request?.ThrowIfExpired();
            if (failure != null) throw failure;
            return result;
        }
        finally
        {
            finished = true;
            watchdog.Stop(); watchdog.Tick -= tick;
            dialog.SourceInitialized -= initialized; dialog.Loaded -= onLoaded;
            dialog.ContentRendered -= onRendered; dialog.Closed -= closed;
            Log("presentation_finished");
        }
    }

    private static void PlaceOnOwnerMonitor(Window dialog, IntPtr owner)
    {
        var hwnd = new WindowInteropHelper(dialog).Handle;
        if (hwnd == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(owner != IntPtr.Zero ? owner : hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(hwnd, out var rect))
            throw new InvalidOperationException("Could not determine the confirmation monitor bounds.");
        var work = info.Work;
        var source = PresentationSource.FromVisual(dialog);
        if (source?.CompositionTarget != null)
        {
            var size = source.CompositionTarget.TransformFromDevice.Transform(
                new Vector(work.Right - work.Left, work.Bottom - work.Top));
            dialog.MaxWidth = Math.Max(1, size.X);
            dialog.MaxHeight = Math.Max(1, size.Y);
        }
        var position = ConfirmationPlacement.Center(work.Left, work.Top, work.Right - work.Left,
            work.Bottom - work.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        // Coordinates are native pixels, including negative monitor coordinates / mixed DPI.
        if (!SetWindowPos(hwnd, IntPtr.Zero, position.X, position.Y, 0, 0, 0x0001 | 0x0004 | 0x0010))
            throw new InvalidOperationException("Could not position the confirmation on its monitor.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
