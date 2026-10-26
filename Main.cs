using AOSharp.Core;
using AOSharp.Core.UI;
using Newtonsoft.Json;

namespace MalisDungeonMapNet;

/// <summary>
/// Mali's Dungeon Map - Net.
///
/// The plugin is a data source and nothing else: it reads the dungeon out of
/// the running client and pushes it down a socket. Drawing happens in the
/// toolkit window (src/apps/dungeon-map), which means no D3D hooks, no render
/// loop, and no dependence on which graphics mode the launcher is in.
///
/// Everything that touches the game happens on the frame tick. Socket handlers
/// only set fields.
/// </summary>
public class Main : AOPluginEntry
{
    private static ToolkitApp? _server;
    private static string? _snapshotJson;
    private static int _snapshotPf;
    private static int _snapshotRooms;

    private static volatile bool _resend;
    private static bool _sendDynels = true;
    private static float _sinceRoomCheck;

    public override void Run()
    {
        Chat.WriteLine("Mali's Dungeon Map - Net");

        SpawnTable.Load(PluginDirectory);

        _server = ToolkitHub.Register("app_7071");
        _server.PageConnected += () => _resend = true;
        _server.Register("updateConfig", root =>
        {
            if (root.TryGetProperty("settings", out var settings) &&
                settings.TryGetProperty("showDynels", out var showDynels) &&
                showDynels.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                _sendDynels = showDynels.GetBoolean();
        });
        _server.Register("resend", _ => _resend = true);

        Chat.RegisterCommand("dmap", (_, _, _) =>
        {
            _resend = true;
            Chat.WriteLine(_snapshotJson == null
                ? "Dungeon Map: no dungeon loaded."
                : $"Dungeon Map: playfield {_snapshotPf}, {_snapshotRooms} rooms, {_snapshotJson.Length / 1024}KB.");
        });

        Game.OnUpdate += OnUpdate;
    }

    private void OnUpdate(object? sender, float deltaTime)
    {
        if (_server == null) return;

        try
        {
            _sinceRoomCheck += deltaTime;
            if (_sinceRoomCheck > 0.5f)
            {
                _sinceRoomCheck = 0f;
                RefreshDungeon();
            }

            if (_resend)
            {
                _resend = false;
                _server.Send(_snapshotJson ?? JsonConvert.SerializeObject(new { type = "dungeon", rooms = Array.Empty<object>() }));
            }

            if (!_server.HasPages || _snapshotJson == null) return;

            // Every frame, and everything in one message. The map is for
            // showing where things are now, and anything slower shows you
            // where they were.
            _server.Send(JsonConvert.SerializeObject(LiveFrame.Frame(_sendDynels)));
        }
        catch (Exception ex)
        {
            Chat.WriteLine($"Dungeon Map: {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuild when the playfield changes, and while a zone is still filling
    /// in - rooms arrive over a few frames, so the room count settling is the
    /// signal that the map is complete.
    /// </summary>
    private static void RefreshDungeon()
    {
        if (!Playfield.IsDungeon)
        {
            if (_snapshotJson == null) return;

            _snapshotJson = null;
            _snapshotPf = 0;
            _snapshotRooms = 0;
            _server?.Send(JsonConvert.SerializeObject(new { type = "dungeon", rooms = Array.Empty<object>() }));
            return;
        }

        int pf = Playfield.ModelIdentity.Instance;
        int rooms = Playfield.Rooms.Count;

        if (pf == _snapshotPf && rooms == _snapshotRooms) return;

        object? snapshot = DungeonSnapshot.Build();
        if (snapshot == null) return;

        _snapshotJson = JsonConvert.SerializeObject(snapshot);
        _snapshotPf = pf;
        _snapshotRooms = rooms;
        _server?.Send(_snapshotJson);
    }

    public override void Teardown()
    {
        Game.OnUpdate -= OnUpdate;
        _server?.Dispose();
        _server = null;
        _snapshotJson = null;
    }
}
