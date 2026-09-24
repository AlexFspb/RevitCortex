using System;
using System.Collections.Concurrent;
using System.IO;
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
    private TcpListener? _listener;
    private volatile bool _isRunning;
    private Thread? _listenerThread;
    private readonly CortexRouter _router;
    private int _port;
    private readonly ConcurrentDictionary<TcpClient, byte> _activeClients = new();

    public bool IsRunning => _isRunning;
    public bool HasBoundPort { get; private set; }

    public SocketService(CortexRouter router, int port = 8080)
    {
        _router = router;
        _port = port;
    }

    public void Start()
    {
        StartOnFirstAvailablePort(_port);
    }

    public int StartOnFirstAvailablePort(params int[] ports)
    {
        if (_isRunning) return _port;
        // Once assigned, keep the port through Stop/Start for this process.
        // Never silently move an existing client to another instance's port.
        var candidates = HasBoundPort ? new[] { _port } : ports;
        SocketException? lastError = null;
        foreach (var port in candidates)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.ExclusiveAddressUse = true;
            try
            {
                // Binding is the availability check and reservation, atomically.
                listener.Start();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                || ex.SocketErrorCode == SocketError.AccessDenied)
            {
                listener.Stop();
                lastError = ex;
                continue;
            }
            _listener = listener;
            _port = port;
            HasBoundPort = true;
            _isRunning = true;
            _listenerThread = new Thread(ListenForClients) { IsBackground = true };
            _listenerThread.Start();
            return _port;
        }
        throw new InvalidOperationException(
            $"Cannot start Cortex: TCP port(s) {string.Join(", ", candidates)} are unavailable. " +
            "Stop the conflicting service or launch Revit with an explicit REVITCORTEX_PORT.", lastError);
    }

    public void Stop()
    {
        _isRunning = false;
        _listener?.Stop();
        // Drain AcceptTcpClient before another Start can replace the listener.
        _listenerThread?.Join(1000);

        // Close the active client connections too: a connection accepted before
        // Stop() would otherwise keep serving requests after a manual shutdown.
        // Document closure keeps the listener alive; execution validates context.
        foreach (var client in _activeClients.Keys)
        {
            try { client.Close(); } catch { /* already gone */ }
        }
        _activeClients.Clear();
    }

    private void ListenForClients()
    {
        while (_isRunning)
        {
            try
            {
                var client = _listener!.AcceptTcpClient();
                var thread = new Thread(HandleClient) { IsBackground = true };
                thread.Start(client);
            }
            catch (SocketException) when (!_isRunning)
            {
                // Normal shutdown via Stop() — exit cleanly.
                break;
            }
            catch (Exception ex)
            {
                // Unexpected listener crash — reset flag so IsRunning reflects reality.
                _isRunning = false;
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Listener crashed unexpectedly: {ex.Message}");
                break;
            }
        }
    }

    private void HandleClient(object? state)
    {
        var client = (TcpClient)state!;
        _activeClients.TryAdd(client, 0);
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
