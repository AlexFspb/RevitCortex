using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;

namespace RevitCortex.Plugin.Communication;

public class SocketService
{
    private readonly object _lifecycleLock = new();
    private readonly Action<int>? _boundPort;
    private TcpListener? _listener;
    private volatile bool _isRunning;
    private Thread? _listenerThread;
    private readonly CortexRouter _router;
    private int _port;
    private readonly ConcurrentDictionary<TcpClient, byte> _activeClients = new();

    public bool IsRunning => _isRunning;
    public bool HasBoundPort { get; private set; }

    public SocketService(CortexRouter router, int port = 8080, Action<int>? boundPort = null)
    {
        _router = router;
        _boundPort = boundPort;
        _port = port;
    }

    public void Start()
    {
        StartOnFirstAvailablePort(_port);
    }

    public int StartOnFirstAvailablePort(params int[] ports)
    {
        lock (_lifecycleLock) return StartLocked(ports);
    }

    private int StartLocked(int[] ports)
    {
        if (_isRunning) return _port;
        // Prefer the previous assignment, but automatic callers may recover on another candidate.
        // Start() supplies only _port, so explicit launcher overrides never fall back.
        // MCP clients remain fixed to their configured port and validate instance identity.
        var candidates = (HasBoundPort ? ports.Prepend(_port) : ports).Distinct().ToArray();
        SocketException? lastError = null;
        foreach (var port in candidates)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.ExclusiveAddressUse = true;
                // Binding is the availability check and reservation, atomically.
                StartListener(listener);
                _listener = listener;
                _isRunning = true;
                _boundPort?.Invoke(port); // Identity must be ready before the first accepted request.
                _listenerThread = new Thread(() => ListenForClients(listener)) { IsBackground = true };
                StartListenerThread(_listenerThread);
                // Commit the assignment only after the entire startup succeeds.
                _port = port;
                HasBoundPort = true;
                return _port;
            }
            catch (Exception ex)
            {
                _isRunning = false;
                try { listener.Stop(); }
                catch (Exception stopError)
                {
                    System.Diagnostics.Trace.WriteLine($"[RevitCortex] Listener cleanup failed: {stopError.Message}");
                }
                _listener = null;
                _listenerThread = null;
                if (ex is SocketException socketError &&
                    (socketError.SocketErrorCode == SocketError.AddressAlreadyInUse ||
                     socketError.SocketErrorCode == SocketError.AccessDenied))
                {
                    lastError = socketError;
                    continue;
                }
                throw;
            }
        }
        throw new InvalidOperationException(
            $"Cannot start Cortex: TCP port(s) {string.Join(", ", candidates)} are unavailable. " +
            "Stop the conflicting service or launch Revit with an explicit REVITCORTEX_PORT.", lastError);
    }

    // Fault-injection seams also exercise failures after a successful bind.
    protected virtual void StartListener(TcpListener listener) => listener.Start();
    protected virtual void StartListenerThread(Thread thread) => thread.Start();

    public void Stop()
    {
        Thread? thread;
        lock (_lifecycleLock)
        {
            _isRunning = false;
            _listener?.Stop();
            _listener = null;
            thread = _listenerThread;
            _listenerThread = null;
            CloseClients();
        }
        // Never join under the lock: the exiting accept loop also uses it.
        if (thread != Thread.CurrentThread) thread?.Join(1000);
    }

    private void CloseClients()
    {
        foreach (var client in _activeClients.Keys)
            try { client.Close(); } catch { }
        _activeClients.Clear();
    }

    protected virtual TcpClient AcceptClient(TcpListener listener) => listener.AcceptTcpClient();

    private void ListenForClients(TcpListener listener)
    {
        try
        {
            while (_isRunning && ReferenceEquals(_listener, listener))
            {
                var client = AcceptClient(listener);
                lock (_lifecycleLock)
                {
                    if (!_isRunning || !ReferenceEquals(_listener, listener)) { client.Dispose(); return; }
                    _activeClients.TryAdd(client, 0);
                    try { new Thread(HandleClient) { IsBackground = true }.Start(client); }
                    catch { _activeClients.TryRemove(client, out _); client.Dispose(); throw; }
                }
            }
        }
        catch (Exception ex)
        {
            lock (_lifecycleLock)
            {
                // A stopped generation must never stop a replacement listener.
                if (ReferenceEquals(_listener, listener))
                {
                    try { listener.Stop(); }
                    catch (Exception stopError) { System.Diagnostics.Trace.WriteLine($"[RevitCortex] Listener cleanup: {stopError}"); }
                    finally
                    {
                        _listener = null;
                        CloseClients();
                        _isRunning = false;
                    }
                    System.Diagnostics.Trace.WriteLine($"[RevitCortex] Listener crashed: {ex}");
                }
            }
        }
    }

    private void HandleClient(object? state)
    {
        var client = (TcpClient)state!;
        // Covers the accept-vs-Stop race: a client registered after Stop()'s
        // sweep must not survive it.
        if (!_isRunning)
        {
            _activeClients.TryRemove(client, out _);
            client.Close();
            return;
        }
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var response = ProcessRequest(line);
                writer.WriteLine(response);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Client error: {ex.Message}");
        }
        finally
        {
            _activeClients.TryRemove(client, out _);
            client.Close();
        }
    }

    private string ProcessRequest(string requestJson)
    {
        JsonRpcRequest? request;
        try
        {
            request = JsonConvert.DeserializeObject<JsonRpcRequest>(requestJson);
        }
        catch
        {
            return JsonConvert.SerializeObject(
                JsonRpcResponse.Fail(null, -32700, "Parse error"));
        }

        if (request == null || string.IsNullOrEmpty(request.Method))
        {
            return JsonConvert.SerializeObject(
                JsonRpcResponse.Fail(request?.Id, -32600, "Invalid request"));
        }

        try
        {
            var result = _router.Route(request.Method, request.Params ?? new JObject());

            if (result.Success)
            {
                return JsonConvert.SerializeObject(
                    JsonRpcResponse.Success(request.Id, result.Data!));
            }
            else
            {
                return JsonConvert.SerializeObject(
                    JsonRpcResponse.Fail(request.Id,
                        (int)result.Error!.Code,
                        result.Error.Message,
                        JToken.FromObject(result.Error)));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] ProcessRequest internal failure: {ex}");
            return JsonConvert.SerializeObject(
                JsonRpcResponse.Fail(request.Id, -32603, SafeErrorMessages.ForInternal(ex)));
        }
    }
}
