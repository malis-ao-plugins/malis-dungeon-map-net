using System.Net;
using System.Net.WebSockets;
using System.Text;
using AOSharp.Core.UI;
using Newtonsoft.Json.Linq;

namespace MalisDungeonMapNet;

/// <summary>
/// The same listener the Mission Roller uses: one HttpListener, JSON messages
/// with a "type", any number of clients.
///
/// Handlers run on a socket thread, so they must not touch the game. Every one
/// of them here only sets a field that <see cref="Main"/> reads on the next
/// frame.
/// </summary>
public class WebSocketServer
{
    private readonly HttpListener _listener;
    private readonly List<WebSocket> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, Action<JObject>> _handlers = new();
    private Task? _listenerTask;
    private readonly int _port;

    public event Action? OnClientConnected;

    public WebSocketServer(int port)
    {
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public bool HasClients => _clients.Any(c => c.State == WebSocketState.Open);

    public void Register(string messageType, Action<JObject> handler) => _handlers[messageType] = handler;

    public void Start()
    {
        try
        {
            _listener.Start();
        }
        catch (Exception ex)
        {
            Chat.WriteLine($"Dungeon Map: listen failed - {ex.Message}");
            return;
        }

        _listenerTask = Task.Run(async () =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested && _listener.IsListening)
                {
                    var ctx = await _listener.GetContextAsync();
                    if (!ctx.Request.IsWebSocketRequest)
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        continue;
                    }

                    var wsCtx = await ctx.AcceptWebSocketAsync(null);
                    lock (_clients) _clients.Add(wsCtx.WebSocket);
                    OnClientConnected?.Invoke();
                    _ = HandleClient(wsCtx.WebSocket);
                }
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        });
    }

    public void Broadcast(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        List<WebSocket> targets;
        lock (_clients) targets = _clients.ToList();

        foreach (var client in targets)
        {
            if (client.State != WebSocketState.Open) continue;
            try { client.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token); }
            catch { /* dropped between the check and the send */ }
        }
    }

    public void Stop()
    {
        _handlers.Clear();
        OnClientConnected = null;
        _cts.Cancel();

        try { _listener.Stop(); } catch { }

        List<WebSocket> targets;
        lock (_clients) { targets = _clients.ToList(); _clients.Clear(); }

        foreach (var c in targets)
        {
            try
            {
                if (c.State == WebSocketState.Open)
                    c.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None).Wait(500);
            }
            catch { }
            finally { c.Dispose(); }
        }

        try { _listenerTask?.Wait(1000); } catch { }
    }

    private async Task HandleClient(WebSocket ws)
    {
        var buffer = new byte[8192];

        while (ws.State == WebSocketState.Open)
        {
            try
            {
                var result = await ws.ReceiveAsync(buffer, _cts.Token);
                if (result.Count == 0 || result.MessageType == WebSocketMessageType.Close) break;

                var root = JObject.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
                var type = root["type"]?.ToString();

                if (type != null && _handlers.TryGetValue(type, out var handler))
                    handler(root);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Chat.WriteLine($"Dungeon Map: bad message - {ex.Message}");
                break;
            }
        }

        lock (_clients) _clients.Remove(ws);
    }
}
