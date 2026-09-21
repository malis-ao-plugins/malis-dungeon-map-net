using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.DbObjects;
using AOSharp.Core;

namespace MalisDungeonMapNet;

/// <summary>
/// The map, built once per dungeon.
///
/// A room's walls are not guessed. The playfield's tilemap carries one
/// collision byte per tile, and a room names the rectangle of it that it
/// occupies, so the outline is the boundary of that bitmap - traced, merged
/// into runs, and put through the room's own transform. No mesh, no terrain
/// sampling, no edge detection over a triangle soup.
/// </summary>
public static class DungeonSnapshot
{
    /// <summary>A tile you can stand on. 0x80 is the "blocked" marker.</summary>
    private static bool Walkable(byte collision) => collision > 0 && collision != 0x80;

    public static object? Build()
    {
        if (!Playfield.IsDungeon) return null;
        if (Playfield.RDBTilemap is not DungeonRDBTilemap tilemap) return null;

        var rooms = Playfield.Rooms;
        if (rooms.Count == 0) return null;

        int tilemapId = Playfield.TilemapResourceId;
        float tileSize = tilemap.TileSize;
        var seenDoors = new HashSet<(int, int, int)>();
        var roomPayload = new List<object>();

        foreach (Room room in rooms)
        {
            int minX = (int)room.LocalRect.MinX;
            int minZ = (int)room.LocalRect.MinY;
            int w = (int)room.LocalRect.MaxX - minX;
            int h = (int)room.LocalRect.MaxY - minZ;
            if (w <= 0 || h <= 0) continue;

            var mask = new bool[w, h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    mask[x, z] = InBounds(tilemap, minX + x, minZ + z) &&
                                 Walkable(tilemap.CollisionData[minX + x, minZ + z]);

            var toWorld = WorldTransform(room, w, h, tileSize);

            var walls = new List<float[]>();
            foreach (var (x1, z1, x2, z2) in TileOutline.Trace(mask, w, h))
            {
                Vector3 a = toWorld(x1, z1);
                Vector3 b = toWorld(x2, z2);
                walls.Add([R(a.X), R(a.Z), R(b.X), R(b.Z)]);
            }

            var spawns = new List<float[]>();
            foreach (var (sx, sz) in SpawnTable.For(tilemapId, room.Name))
            {
                Vector3 p = toWorld(sx, sz);
                spawns.Add([R(p.X), R(p.Z)]);
            }

            var doors = new List<object>();
            for (int i = 0; i < room.NumDoors; i++)
            {
                int link = room.GetDoorConnectZone(i);
                if (link == room.Instance) continue;

                room.GetDoorPosRot(i, out Vector3 pos, out Quaternion rot);

                var key = ((int)(pos.X * 4), (int)(pos.Z * 4), room.Floor);
                if (!seenDoors.Add(key)) continue;

                doors.Add(new
                {
                    x = R(pos.X),
                    z = R(pos.Z),
                    a = R((float)(rot.Yaw * 180.0 / Math.PI)),
                    link,
                });
            }

            Vector3 centre = toWorld(w / 2f, h / 2f);

            // The tile grid itself, for anything that wants to draw the floor
            // rather than its outline. Three transformed points give the basis
            // - origin, one tile along local x, one along local z - so the
            // room's rotation stays here and a client only has to add.
            Vector3 origin = toWorld(0, 0);
            Vector3 alongX = toWorld(1, 0);
            Vector3 alongZ = toWorld(0, 1);

            roomPayload.Add(new
            {
                i = room.Instance,
                n = room.Name,
                f = room.Floor,
                c = new[] { R(centre.X), R(centre.Z) },
                y = R(room.Position.Y),
                w = walls,
                d = doors,
                s = spawns,
                // tile grid: origin, basis vectors, size, and the bitmap
                o = new[] { R(origin.X), R(origin.Z) },
                u = new[] { R(alongX.X - origin.X), R(alongX.Z - origin.Z) },
                v = new[] { R(alongZ.X - origin.X), R(alongZ.Z - origin.Z) },
                mw = w,
                mh = h,
                m = PackMask(mask, w, h),
            });
        }

        return new
        {
            type = "dungeon",
            pf = Playfield.ModelIdentity.Instance,
            name = Playfield.Name,
            tilemap = tilemapId,
            tileSize = R(tileSize),
            acg = SpawnTable.IsAcgTemplate(tilemapId),
            rooms = roomPayload,
        };
    }

    private static bool InBounds(RDBTilemap tilemap, int x, int z) =>
        x >= 0 && z >= 0 && x < tilemap.Width && z < tilemap.Height;

    /// <summary>
    /// Room-local tile coordinates to world. The local frame and the anchor
    /// are the ones the client itself uses to lay a room's tiles out; the
    /// rotation and position come off the room.
    /// </summary>
    private static Func<float, float, Vector3> WorldTransform(Room room, int w, int h, float tileSize)
    {
        float width = w * tileSize;
        float length = h * tileSize;
        float anchorX = 1f + (room.Center.X - w / 2f) * tileSize;
        float anchorZ = 1f + (room.Center.Z - h / 2f) * tileSize;

        var mesh = new Mesh
        {
            Vertices = new List<Vector3>(),
            Triangles = new List<int>(),
            Position = room.Position - new Vector3(0, room.YOffset, 0),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, room.Rotation * (Math.PI / 180)),
            Scale = new Vector3(1, 1, 1),
        };

        var matrix = mesh.LocalToWorldMatrix;

        return (x, z) => matrix.MultiplyPoint3x4(new Vector3(
            x * tileSize - width / 2f - anchorX,
            0,
            z * tileSize - length / 2f - anchorZ));
    }

    private static float R(float v) => MathF.Round(v, 1);

    /// <summary>
    /// The tile bitmap as base64, row by row, one bit per tile. A 30x45 room
    /// is 169 bytes this way, so the whole grid costs less than its outline.
    /// </summary>
    private static string PackMask(bool[,] mask, int w, int h)
    {
        var bytes = new byte[(w * h + 7) / 8];

        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!mask[x, z]) continue;
                int bit = z * w + x;
                bytes[bit >> 3] |= (byte)(1 << (bit & 7));
            }
        }

        return Convert.ToBase64String(bytes);
    }
}
