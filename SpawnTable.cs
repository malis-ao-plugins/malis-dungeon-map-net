using AOSharp.Core.UI;
using Newtonsoft.Json.Linq;

namespace MalisDungeonMapNet;

/// <summary>
/// Spawn points per template room, in room-local tile coordinates.
///
/// These come out of the playfield's district table (resource type 1000014,
/// GameData::PlayfieldDistrictInfo_t). In a live playfield a district's points
/// each carry a creature code; in the four ACG templates they carry none, so
/// the template says where something stands and the server says what. The
/// extraction and the checks behind these files are in the ao-acg notes.
///
/// The client has no reader for that table, so the four files ship with the
/// plugin. Everything else on the map is read out of the running game.
/// </summary>
public static class SpawnTable
{
    // tilemap resource id -> the template playfield the file was cut from
    private static readonly Dictionary<int, int> TilemapToTemplate = new()
    {
        [306] = 322, // Autocontentdungcave
        [307] = 320, // AutocontentMidtech
        [308] = 321, // AutocontentHITech
        [310] = 324, // AutocontentClan
    };

    private static readonly Dictionary<int, Dictionary<string, List<(float X, float Z)>>> ByTemplate = new();
    private static bool _loaded;

    public static bool IsAcgTemplate(int tilemapId) => TilemapToTemplate.ContainsKey(tilemapId);

    /// <summary>Room-local tile points for a room, or an empty list.</summary>
    public static List<(float X, float Z)> For(int tilemapId, string roomName)
    {
        if (!TilemapToTemplate.TryGetValue(tilemapId, out int template))
            return [];

        return ByTemplate.TryGetValue(template, out var rooms) && rooms.TryGetValue(roomName, out var points)
            ? points
            : [];
    }

    public static void Load(string pluginDir)
    {
        if (_loaded) return;
        _loaded = true;

        foreach (int template in TilemapToTemplate.Values)
        {
            string path = Path.Combine(pluginDir, "data", $"pf{template}_spawns.json");
            if (!File.Exists(path))
            {
                Chat.WriteLine($"Dungeon Map: no spawn file for template {template}");
                continue;
            }

            try
            {
                var rooms = new Dictionary<string, List<(float, float)>>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in JArray.Parse(File.ReadAllText(path)))
                {
                    string? name = entry["room"]?.ToString();
                    if (name == null) continue;

                    var points = new List<(float, float)>();
                    foreach (var p in entry["points"] ?? new JArray())
                        points.Add(((float?)p["x"] ?? 0f, (float?)p["z"] ?? 0f));

                    rooms[name] = points;
                }

                ByTemplate[template] = rooms;
            }
            catch (Exception ex)
            {
                Chat.WriteLine($"Dungeon Map: spawn file {template} - {ex.Message}");
            }
        }
    }
}
