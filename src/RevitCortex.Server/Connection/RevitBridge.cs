using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;

namespace RevitCortex.Server.Connection;

/// <summary>
/// TCP bridge to the RevitCortex plugin running inside Revit.
/// Sends JSON-RPC requests and reads line-delimited responses.
/// </summary>
public sealed class RevitBridge : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _commandTimeout;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private int _requestCounter;

    public RevitBridge(string host = "127.0.0.1", int port = 8080, int commandTimeoutSeconds = 300)
    {
        _host = host;
        _port = port;
        _commandTimeout = TimeSpan.FromSeconds(commandTimeoutSeconds);
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client is { Connected: true }) return;

        Disconnect();
        _client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await _client.ConnectAsync(_host, _port, cts.Token);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Cannot connect to Revit on {_host}:{_port}. " +
                $"Make sure Revit is open and the RevitCortex plugin is loaded (green icon in the ribbon). " +
                $"Set REVITCORTEX_PORT to the port shown in that Revit's Cortex settings. " +
                $"(SocketError: {ex.SocketErrorCode})", ex);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"Timed out connecting to Revit on {_host}:{_port}. " +
                $"Make sure Revit is open and the RevitCortex plugin is active.");
        }
        var stream = _client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    /// <summary>
    /// Send a JSON-RPC command to the Revit plugin and return the result.
    /// Opens a TCP connection per call (same pattern as the TS server).
    /// </summary>
    public async Task<JToken> SendCommandAsync(string method, JObject parameters, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var id = Interlocked.Increment(ref _requestCounter).ToString();
        var request = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
            ["id"] = id
        };

        await _writer!.WriteLineAsync(request.ToString(Formatting.None));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_commandTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var line = await _reader!.ReadLineAsync(cts.Token);
            if (line == null) throw new IOException("Connection closed by Revit plugin");

            var response = JObject.Parse(line);
            if (response["id"]?.ToString() != id) continue;

            if (response["error"] != null)
            {
                // The plugin uses JSON-RPC `error` as a semantic application-failure channel
                // (CortexResult.Fail). MCP clients render any thrown exception as an opaque
                // "An error occurred invoking <tool>", hiding code/message/suggestion from the
                // model. So we surface the structured CortexError as a normal application
                // payload {"success": false, "error": {...}} that flows through to the LLM.
                var err = response["error"]!;
                var errorPayload = err["data"] is JObject structured
                    ? (JToken)structured.DeepClone()
                    : new JObject
                    {
                        ["message"] = err["message"]?.ToString() ?? "Unknown Revit error"
                    };
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = errorPayload
                };
            }

            return response["result"] ?? JValue.CreateNull();
        }

        throw new TimeoutException($"Command '{method}' timed out after {_commandTimeout.TotalSeconds}s");
    }

    private void Disconnect()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
    }

    public void Dispose() => Disconnect();
}

/// <summary>
/// Manages a per-request TCP connection to the Revit plugin.
/// Serializes access (only one command at a time, like the TS mutex).
/// </summary>
public sealed class RevitConnectionManager
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly int _port;
    private JObject? _target;
    public int Port => _port;

    public RevitConnectionManager(int port = 8080)
    {
        _port = port;
    }

    public async Task<JToken> ExecuteAsync(string method, JObject parameters, CancellationToken ct = default)
        => await ExecuteAsync(method, parameters, commandTimeoutSeconds: 300, ct);

    /// <summary>
    /// Overload with explicit command timeout — use for long-running operations
    /// such as IFC export on large models.
    /// </summary>
    public async Task<JToken> ExecuteAsync(string method, JObject parameters, int commandTimeoutSeconds, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            using var bridge = new RevitBridge(port: _port, commandTimeoutSeconds: commandTimeoutSeconds);
            // These explicit inspection calls may rebind after a user-requested document switch.
            if (method == "get_project_info" || method == "say_hello")
            {
                var inspected = await bridge.SendCommandAsync(method, parameters, ct);
                if (IsIdentity(inspected, _port)) _target = (JObject)inspected.DeepClone();
                return inspected;
            }
            if (method == "get_connection_status") return await bridge.SendCommandAsync(method, parameters, ct);

            var current = await bridge.SendCommandAsync("get_connection_status", new JObject(), ct);
            if (!IsIdentity(current, _port)) return TargetFailure("The plugin did not return a valid Cortex identity. Update/reconnect the matching plugin and server.");
            if (_target != null && !SameTarget(_target, current))
                return TargetFailure("The Revit process or document changed since the last verified request. Inspect the intended target with get_project_info or say_hello before continuing.", current);
            _target ??= (JObject)current.DeepClone();

            var servicePrepared = false;
            var routed = (JObject)parameters.DeepClone();
            routed["_cortexExpected"] = TargetToken(_target);
            if (method == "send_code_to_revit" && current.Value<bool?>("documentPresent") == false)
            {
                // Prepare once, before the script request. Never retry a script after a failure.
                var prepared = await bridge.SendCommandAsync("ensure_service_document", routed, ct);
                if (!IsIdentity(prepared, _port)) return prepared;
                if (prepared.Value<string>("instanceId") != current.Value<string>("instanceId"))
                    return TargetFailure("The Revit process changed during service-project preparation.", prepared);
                servicePrepared = prepared.Value<bool?>("serviceDocumentCreated") == true;
                _target = (JObject)prepared.DeepClone();
                routed["_cortexExpected"] = TargetToken(_target);
            }
            var result = await bridge.SendCommandAsync(method, routed, ct);
            if (method == "ensure_service_document" && IsIdentity(result, _port)) _target = (JObject)result.DeepClone();
            if (servicePrepared && result is JObject response)
            {
                response["serviceProjectPrepared"] = true;
                response["serviceProjectPath"] = _target?["activeDocumentPath"]?.DeepClone();
            }
            return result;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static JObject TargetToken(JObject identity) => new()
    {
        ["instanceId"] = identity["instanceId"]?.DeepClone(),
        ["bridgePort"] = identity["bridgePort"]?.DeepClone(),
        ["documentGeneration"] = identity["documentGeneration"]?.DeepClone()
    };

    private static bool IsIdentity(JToken result, int port) => result is JObject
        && result.Value<string>("protocol") == "RevitCortex/1"
        && result.Value<int?>("bridgePort") == port
        && !string.IsNullOrEmpty(result.Value<string>("instanceId"))
        && result.Value<long?>("documentGeneration") != null;

    private static bool SameTarget(JToken expected, JToken actual) =>
        expected.Value<string>("instanceId") == actual.Value<string>("instanceId")
        && expected.Value<long?>("documentGeneration") == actual.Value<long?>("documentGeneration")
        && expected.Value<int?>("bridgePort") == actual.Value<int?>("bridgePort");

    private JObject TargetFailure(string message, JToken? current = null) => new()
    {
        ["success"] = false, ["error"] = new JObject
        {
            ["code"] = "Cancelled", ["message"] = message,
            ["context"] = new JObject { ["reason"] = "TargetIdentityMismatch", ["configuredPort"] = _port,
                ["commandSent"] = false, ["current"] = current?.DeepClone() }
        }
    };

    public async Task<JArray> DiscoverAsync(int[]? requestedPorts, CancellationToken ct)
    {
        var ports = requestedPorts ?? CortexPort.AutomaticPorts(false).Concat(CortexPort.AutomaticPorts(true)).Append(_port).Distinct().ToArray();
        if (ports.Length > 16 || ports.Any(p => p < 1 || p > 65535)) throw new ArgumentException("Supply at most 16 ports in 1..65535.");
        var rows = await Task.WhenAll(ports.Distinct().Select(async port =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMilliseconds(1500));
            try
            {
                using var probe = new RevitBridge(port: port, commandTimeoutSeconds: 1);
                var result = await probe.SendCommandAsync("get_connection_status", new JObject(), deadline.Token);
                if (!IsIdentity(result, port)) return new JObject { ["port"] = port, ["status"] = "not_cortex_or_old_plugin" };
                var row = (JObject)result.DeepClone();
                row["status"] = "reachable";
                row["isThisConnection"] = port == _port;
                return row;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                return new JObject { ["port"] = port, ["status"] = "unavailable", ["message"] = ex.Message };
            }
        }));
        // Discovery is read-only and never changes the destination or the pinned identity.
        return new JArray(rows);
    }

    /// <summary>
    /// Pins this MCP server to its process override, or 8080. Never scans other ports.
    /// </summary>
    public static int ResolvePort()
    {
        return CortexPort.Resolve(Environment.GetEnvironmentVariable(CortexPort.EnvironmentVariable));
    }
}
