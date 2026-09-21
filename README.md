# Mali's Dungeon Map - Net

The dungeon you are standing in, pushed down a socket for the toolkit's
**Dungeon Map** window (`src/apps/dungeon-map`, port **7071**) to draw.

```
ws://localhost:7071/ws
```

The plugin draws nothing. There is no D3D hook, no render loop and no
dependence on the launcher's graphics mode — it reads the playfield and sends
it, and the window is a web app like every other one in the toolkit.

## Where the walls come from

A dungeon playfield carries a tilemap with **one collision byte per tile**, and
each room names the rectangle of that tilemap it occupies (`Room.LocalRect`).
So a room's shape is already in the client, exactly, and the outline is the
boundary of that bitmap:

1. read the room's rectangle out of the tilemap's collision data,
2. trace the edges where an occupied tile meets an empty one,
3. merge runs, so a straight thirty-tile wall is one segment,
4. put the result through the room's own position and rotation.

That is the whole of `DungeonSnapshot` and `TileOutline`. The previous
generation of this plugin built a triangle mesh out of terrain heights and ran
edge detection over it to find the walls; this reads the walls.

Doors come from the room's own door slots (`GetDoorPosRot`), so every door is
on the map whether or not you have seen it.

## Spawn points

In the four generated-dungeon templates, each room carries a list of positions
where the server may put a monster. They live in the playfield's district
table — resource type 1000014, `GameData::PlayfieldDistrictInfo_t` — which the
client has no reader for, so the four extracted files ship in `data/`.

The points name no creature: the template says where something stands, the
server says what. In an ordinary playfield the same table's points *do* name
one, which is how the table was identified. The extraction, and the check that
puts all 984 points on a floor tile, are in the ao-acg notes.

Any other dungeon still maps; it just has no spawn points to show.

## Messages

Out:

| type | when | carries |
| --- | --- | --- |
| `dungeon` | on zoning in, and on connect | every room: walls, doors, spawn points, floor |
| `player` | 10Hz | position, heading, floor |
| `dynels` | 4Hz | characters, chests, terminals, with their floor |

In:

| type | effect |
| --- | --- |
| `updateConfig` | `settings.showDynels` stops the 4Hz frame when the window is not drawing them |
| `resend` | send the current dungeon again |

A room is
`{ i, n, f, c: [x, z], w: [[x1,z1,x2,z2], ...], d: [{x,z,a,link}], s: [[x,z]] }`
— world coordinates, one decimal.

## Building

```
dotnet build
```

Needs `..\AOSharp.Net` built in Release beside this folder, the same as the
Mission Roller. `data\*.json` and `manifest.json` copy to the output.

`/dmap` reports what the plugin currently holds and re-sends it.

## Notes

- Everything that touches the game happens on the frame tick. Socket handlers
  only set a field; `Main.OnUpdate` does the work.
- The map is rebuilt when the playfield changes or while its room count is
  still settling, which is how a zone finishes filling in.
