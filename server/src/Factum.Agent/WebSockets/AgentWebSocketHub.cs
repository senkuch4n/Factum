using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Factum.Agent.Models;

namespace Factum.Agent.WebSockets;

public sealed class AgentWebSocketHub : IDisposable
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly ILogger<AgentWebSocketHub> _log;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public AgentWebSocketHub(ILogger<AgentWebSocketHub> log) => _log = log;

    public async Task HandleAsync(WebSocket ws, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        _clients.TryAdd(id, ws);
        _log.LogInformation("WS client connected: {Id}", id);

        // Ping loop — mantiene la conexión viva
        using var pingTimer = new PeriodicTimer(TimeSpan.FromSeconds(25));
        var pingTask = Task.Run(async () =>
        {
            try
            {
                while (await pingTimer.WaitForNextTickAsync(ct))
                {
                    if (ws.State != WebSocketState.Open) break;
                    await ws.SendAsync(
                        Encoding.UTF8.GetBytes("{\"type\":\"ping\"}"),
                        WebSocketMessageType.Text, true, ct);
                }
            }
            catch { /* conexión cerrada */ }
        }, ct);

        // Read loop — mantiene la lectura activa para detectar cierre
        var buffer = new byte[1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) { _log.LogDebug("WS cerrado: {Msg}", ex.Message); }
        finally
        {
            _clients.TryRemove(id, out _);
            await pingTask;
            _log.LogInformation("WS client disconnected: {Id}", id);
        }
    }

    public async Task BroadcastAsync(AgentEvent evt, CancellationToken ct = default)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt, JsonOpts));
        var dead = new List<Guid>();

        foreach (var (id, ws) in _clients)
        {
            try
            {
                if (ws.State != WebSocketState.Open) { dead.Add(id); continue; }
                await ws.SendAsync(payload, WebSocketMessageType.Text, true, ct);
            }
            catch
            {
                dead.Add(id);
            }
        }

        foreach (var id in dead) _clients.TryRemove(id, out _);
    }

    public void Dispose()
    {
        foreach (var (_, ws) in _clients)
            ws.Dispose();
    }
}
