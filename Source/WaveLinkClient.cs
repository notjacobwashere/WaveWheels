using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace WaveWheels;

// Independent JSON-RPC client. No dependency on Stream Deck, no audio-device emulation.
public sealed class WaveLinkClient : IDisposable
{
    private ClientWebSocket? socket;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> requests = new();
    private readonly ConcurrentDictionary<Target, PendingWrite> writes = new();
    private readonly object dataGate = new();
    private readonly Dictionary<string, JsonObject> channels = new(StringComparer.Ordinal);
    private List<Mix> mixes = new();
    private long nextId;
    private int portOverride;
    private bool legacy;
    private volatile bool connected;
    private string protocol = "", status = "Looking for Wave Link…";
    private CancellationTokenSource? connection;
    private Task? runner;
    public event Action<MixerSnapshot>? Updated;
    public bool IsConnected => connected;
    public Action<string>? Log { get; set; }
    private sealed record PendingWrite(double? Level, bool? Muted);

    public void Start(int port = 0) { portOverride = port; runner = Task.Run(RunAsync); }
    public void Reconnect(int port) { portOverride = port; connection?.Cancel(); }
    public void QueueLevel(Target target, double level) => Queue(target, new(Math.Clamp(level, 0, 1), null));
    public void QueueMute(Target target, bool muted) => Queue(target, new(null, muted));
    private void Queue(Target target, PendingWrite write)
    {
        if (!connected || string.IsNullOrWhiteSpace(target.ChannelId)) return;
        writes.AddOrUpdate(target, write, (_, old) => new(write.Level ?? old.Level, write.Muted ?? old.Muted));
    }

    private async Task RunAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            var established = false;
            foreach (var port in CandidatePorts())
            {
                if (lifetime.IsCancellationRequested) break;
                connection = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var ct = connection.Token;
                socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Origin", "streamdeck://");
                Task? receive = null, writer = null;
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(450);
                        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token);
                    }
                    receive = ReceiveAsync(socket, ct);
                    var info = await RpcAsync("getApplicationInfo", null, ct);
                    var appName = Text(info, "name", "appName");
                    if (Text(info, "appID", "appId") != "EWL" && !appName.Contains("Wave Link", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("The local service is not Wave Link.");
                    JsonNode? initial;
                    try { initial = await RpcAsync("getChannels", null, ct); legacy = false; }
                    catch (RpcException ex) when (ex.Code == -32601) { initial = await RpcAsync("getAllChannelInfo", null, ct); legacy = true; }
                    lock (dataGate) { channels.Clear(); mixes.Clear(); LoadChannels(initial); }
                    if (!legacy) await RefreshMixesAsync(ct);
                    connected = true; established = true;
                    protocol = legacy ? "Wave Link legacy" : "Wave Link 3";
                    status = $"Connected · {protocol} · port {port}";
                    Publish();
                    writer = WriteLoopAsync(ct);
                    // Notifications are applied immediately. Polling also catches versions with incomplete events.
                    while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        await Task.Delay(1800, ct);
                        var state = await RpcAsync(legacy ? "getAllChannelInfo" : "getChannels", null, ct);
                        lock (dataGate) { LoadChannels(state); }
                        if (!legacy) await RefreshMixesAsync(ct);
                        Publish();
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    if (!lifetime.IsCancellationRequested && (established || portOverride > 0)) Log?.Invoke($"Connection on port {port}: " + ex.Message);
                }
                finally
                {
                    connected = false;
                    connection.Cancel();
                    socket.Abort();
                    foreach (var request in requests.Values) request.TrySetException(new IOException("Wave Link disconnected."));
                    requests.Clear(); writes.Clear();
                    try { if (receive != null) await receive; } catch { }
                    try { if (writer != null) await writer; } catch { }
                    socket.Dispose(); socket = null;
                    connection.Dispose(); connection = null;
                }
                if (established) break;
            }
            status = portOverride > 0 ? $"Wave Link unavailable on port {portOverride} · retrying" : "Wave Link unavailable · open Wave Link to connect";
            Publish();
            try { await Task.Delay(2500, lifetime.Token); } catch (OperationCanceledException) { }
        }
    }

    private IEnumerable<int> CandidatePorts()
    {
        var custom = portOverride;
        if (custom > 0) return new[] { custom };
        var found = new List<int>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var files = new List<string> {
            Path.Combine(local, "Packages", "Elgato.WaveLink_g54w8ztgkx496", "LocalState", "ws-info.json"),
            Path.Combine(local, "Elgato", "WaveLink", "ws-info.json")
        };
        try
        {
            var packages = Path.Combine(local, "Packages");
            if (Directory.Exists(packages)) files.AddRange(Directory.EnumerateDirectories(packages, "Elgato.WaveLink*").Select(p => Path.Combine(p, "LocalState", "ws-info.json")));
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (var file in files.Distinct())
        {
            try { if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file))?["port"]?.GetValue<int>() is int port && port is > 0 and <= 65535) found.Add(port); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
        }
        found.AddRange(Enumerable.Range(1884, 10));
        found.AddRange(Enumerable.Range(1824, 10));
        return found.Distinct().ToArray();
    }

    private async Task<JsonNode?> RpcAsync(string method, JsonNode? parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        requests[id] = completion;
        try
        {
            var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
            var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
            await sendLock.WaitAsync(ct);
            try
            {
                var current = socket;
                if (current?.State != WebSocketState.Open) throw new IOException("Wave Link is not connected.");
                await current.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
            finally { sendLock.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
        }
        finally { requests.TryRemove(id, out _); }
    }

    private async Task ReceiveAsync(ClientWebSocket current, CancellationToken ct)
    {
        var buffer = new byte[32768];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var bytes = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await current.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    bytes.Write(buffer, 0, result.Count);
                    if (bytes.Length > 8 * 1024 * 1024) throw new IOException("Wave Link sent an oversized message.");
                } while (!result.EndOfMessage);
                if (result.MessageType != WebSocketMessageType.Text) continue;
                var root = JsonNode.Parse(bytes.ToArray());
                if (root is not JsonObject message) continue;
                if (message["id"] != null && long.TryParse(message["id"]!.ToString(), out var id) && requests.TryGetValue(id, out var pending))
                {
                    if (message["error"] is JsonNode error) pending.TrySetException(new RpcException(Number(error, "code", -1), Text(error, "message")));
                    else pending.TrySetResult(message["result"]?.DeepClone());
                }
                else ApplyNotification(Text(message, "method"), message["params"]);
            }
        }
        finally { try { connection?.Cancel(); } catch (ObjectDisposedException) { } }
    }

    private void ApplyNotification(string method, JsonNode? parameters)
    {
        if (parameters == null) return;
        var changed = false;
        lock (dataGate)
        {
            if (method == "channelsChanged") { LoadChannels(parameters); changed = true; }
            else if (method is "channelChanged" or "inputMixerChanged")
            {
                var id = Text(parameters, legacy ? "mixId" : "id");
                if (parameters is JsonObject patch && id.Length > 0)
                {
                    if (!channels.TryGetValue(id, out var existing)) channels[id] = existing = new JsonObject();
                    Merge(existing, patch); changed = true;
                }
            }
            else if (method == "mixesChanged") { LoadMixes(parameters); changed = true; }
            else if (method == "mixChanged") { /* Full list is refreshed by the short polling fallback. */ }
        }
        if (changed && connected) Publish();
    }

    internal static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var field in patch)
        {
            if (field.Value is JsonArray patches && field.Key == "mixes" && target[field.Key] is JsonArray prior)
            {
                foreach (var value in patches.OfType<JsonObject>())
                {
                    var id = Text(value, "mixId", "id");
                    var old = prior.OfType<JsonObject>().FirstOrDefault(n => Text(n, "mixId", "id") == id);
                    if (old == null) prior.Add(value.DeepClone()); else Merge(old, value);
                }
            }
            else if (field.Value is JsonObject child && target[field.Key] is JsonObject old) Merge(old, child);
            else target[field.Key] = field.Value?.DeepClone();
        }
    }

    private void LoadChannels(JsonNode? result)
    {
        var list = result is JsonObject obj && obj.ContainsKey("channels") ? obj["channels"] : result;
        IEnumerable<JsonNode?> entries = list is JsonArray array ? array : list is JsonObject map ? map.Select(p => p.Value) : Array.Empty<JsonNode?>();
        var replacement = entries.OfType<JsonObject>().Where(n => Text(n, legacy ? "mixId" : "id").Length > 0).ToArray();
        channels.Clear();
        foreach (var node in replacement) channels[Text(node, legacy ? "mixId" : "id")] = (JsonObject)node.DeepClone();
        if (legacy) mixes = new() { new("local", "Monitor mix"), new("stream", "Stream mix") };
    }
    private void LoadMixes(JsonNode? result)
    {
        var list = result is JsonArray ? result : result?["mixes"];
        if (list is JsonArray array) mixes = array.OfType<JsonObject>().Select(n => new Mix(Text(n, "id"), Text(n, "name"))).ToList();
    }
    private async Task RefreshMixesAsync(CancellationToken ct)
    {
        var result = await RpcAsync("getMixes", null, ct);
        lock (dataGate) LoadMixes(result);
    }
    private void Publish()
    {
        List<Channel> values; List<Mix> destinations;
        lock (dataGate)
        {
            values = channels.Values.Select(n => Decode(n, legacy)).ToList();
            destinations = mixes.ToList();
        }
        Updated?.Invoke(new(connected, status, protocol, values, destinations));
    }
    internal static Channel Decode(JsonObject node, bool isLegacy)
    {
        if (isLegacy) return new(Text(node, "mixId"), Text(node, "mixerName"), 0, false, Boolean(node, "isAvailable", true), new[] {
            new ChannelMix("local", Math.Clamp(Decimal(node,"localVolumeIn") / 100, 0, 1), Boolean(node,"isLocalInMuted")),
            new ChannelMix("stream", Math.Clamp(Decimal(node,"streamVolumeIn") / 100, 0, 1), Boolean(node,"isStreamInMuted"))
        }, false);
        var destinations = (node["mixes"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(m => new ChannelMix(Text(m, "mixId", "id"), Math.Clamp(Decimal(m, "level"), 0, 1), Boolean(m, "isMuted"))).ToArray();
        return new(Text(node, "id"), Text(node, "name"), Math.Clamp(Decimal(node, "level"), 0, 1), Boolean(node,"isMuted"), Boolean(node,"isAvailable",true), destinations, node.ContainsKey("level"));
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(35, ct);
            foreach (var key in writes.Keys)
            {
                if (!writes.TryRemove(key, out var write)) continue;
                try
                {
                    JsonObject payload;
                    lock (dataGate)
                    {
                        if (!channels.TryGetValue(key.ChannelId, out var channel)) continue;
                        if (legacy)
                        {
                            if (key.MixId is not ("local" or "stream")) continue;
                            payload = new JsonObject { ["mixId"] = key.ChannelId, ["slider"] = key.MixId,
                                ["localVolumeIn"] = Decimal(channel,"localVolumeIn"), ["streamVolumeIn"] = Decimal(channel,"streamVolumeIn"),
                                ["isLocalInMuted"] = Boolean(channel,"isLocalInMuted"), ["isStreamInMuted"] = Boolean(channel,"isStreamInMuted"),
                                ["filters"] = channel["filters"]?.DeepClone() ?? new JsonArray(),
                                ["localMixFilterBypass"] = Boolean(channel,"localMixFilterBypass"), ["streamMixFilterBypass"] = Boolean(channel,"streamMixFilterBypass") };
                            if (write.Level.HasValue) payload[key.MixId == "local" ? "localVolumeIn" : "streamVolumeIn"] = Math.Round(write.Level.Value * 100);
                            if (write.Muted.HasValue) payload[key.MixId == "local" ? "isLocalInMuted" : "isStreamInMuted"] = write.Muted.Value;
                        }
                        else
                        {
                            payload = new JsonObject { ["id"] = key.ChannelId };
                            var change = new JsonObject();
                            if (write.Level.HasValue) change["level"] = write.Level.Value;
                            if (write.Muted.HasValue) change["isMuted"] = write.Muted.Value;
                            if (key.MixId.Length > 0) { change["mixId"] = key.MixId; payload["mixes"] = new JsonArray(change); }
                            else foreach (var field in change) payload[field.Key] = field.Value?.DeepClone();
                        }
                    }
                    var result = await RpcAsync(legacy ? "setInputMixer" : "setChannel", payload, ct);
                    lock (dataGate)
                    {
                        if (channels.TryGetValue(key.ChannelId, out var current))
                        {
                            // Some versions return no payload for successful setters; preserve intended values until refresh.
                            Merge(current, payload);
                            if (result is JsonObject data && Text(data, legacy ? "mixId" : "id") == key.ChannelId) Merge(current, data);
                        }
                    }
                    Publish();
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Log?.Invoke("Volume change failed: " + ex.Message);
                    status = "Wave Link rejected a change · " + ex.Message;
                    Publish();
                }
            }
        }
    }
    private static string Text(JsonNode? node, params string[] fields)
    {
        if (node is not JsonObject obj) return "";
        foreach (var name in fields) if (obj[name] is JsonValue value) return value.ToString();
        return "";
    }
    private static double Decimal(JsonNode? node, string name) => double.TryParse(Text(node, name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : 0;
    private static int Number(JsonNode? node, string name, int fallback) => int.TryParse(Text(node, name), out var n) ? n : fallback;
    private static bool Boolean(JsonNode? node, string name, bool fallback = false) => bool.TryParse(Text(node, name), out var b) ? b : fallback;
    public void Dispose() { lifetime.Cancel(); connection?.Cancel(); socket?.Abort(); }
    private sealed class RpcException : Exception
    {
        public int Code { get; }
        public RpcException(int code, string message) : base(message) { Code = code; }
    }
}
