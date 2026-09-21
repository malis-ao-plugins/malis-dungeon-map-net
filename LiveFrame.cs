using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.UI;
using System.Runtime.InteropServices;

namespace MalisDungeonMapNet;

/// <summary>
/// What moves: the player, and the dynels worth a dot on a map.
///
/// Both go out together in one message. They are read from the same frame, so
/// splitting them across two messages at two rates only let the map draw a
/// mob where the player no longer was.
/// </summary>
public static class LiveFrame
{
    public static object Frame(bool withDynels) => new
    {
        type = "frame",
        p = Player(),
        d = withDynels ? Dynels() : new List<object>(),
        o = Objective(),
    };

    private static object? Player()
    {
        var me = DynelManager.LocalPlayer;
        if (me == null || !me.IsValid) return null;

        return new
        {
            x = R(me.Position.X),
            z = R(me.Position.Z),
            a = R((float)(me.Rotation.Yaw * 180.0 / Math.PI)),
            f = me.Room?.Floor ?? 0,
            room = me.Room?.Instance ?? 0,
        };
    }

    private static List<object> Dynels()
    {
        var me = DynelManager.LocalPlayer;
        var items = new List<object>();

        foreach (Dynel dynel in DynelManager.AllDynels)
        {
            int floor = dynel.Room?.Floor ?? int.MinValue;
            if (floor == int.MinValue) continue;

            switch (dynel.Identity.Type)
            {
                case IdentityType.SimpleChar:
                    var chr = new SimpleChar(dynel);
                    items.Add(new
                    {
                        k = chr.Identity == me?.Identity ? "self" : chr.IsNpc ? "npc" : "player",
                        x = R(dynel.Position.X),
                        z = R(dynel.Position.Z),
                        a = R((float)(chr.Rotation.Yaw * 180.0 / Math.PI)),
                        f = floor,
                        n = dynel.Name,
                    });
                    break;

                case IdentityType.Container:
                    items.Add(new
                    {
                        k = IsOpened(dynel) ? "chest-open" : "chest",
                        x = R(dynel.Position.X),
                        z = R(dynel.Position.Z),
                        f = floor,
                        n = dynel.Name,
                    });
                    break;

                case IdentityType.Terminal:
                    items.Add(new
                    {
                        k = "terminal",
                        x = R(dynel.Position.X),
                        z = R(dynel.Position.Z),
                        f = floor,
                        n = dynel.Name,
                    });
                    break;
            }
        }

        return items;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 0, Size = 0x100)]
    private struct MissionMemStruct
    {
        [FieldOffset(0xB4)]
        public Identity Playfield;
    }

    public static unsafe bool GetMissionDynel(out Dynel? objectiveDynel)
    {
        objectiveDynel = null;
        Mission? mission = Mission.List.FirstOrDefault(x => (*(MissionMemStruct*)x.Pointer).Playfield == Playfield.ModelIdentity);

        if (mission == null)
            return false;

        List<Identity> targets = new List<Identity>();
        foreach (MissionAction missionAction in mission.Actions)
        {
            switch (missionAction.Type)
            {
                case MissionActionType.FindPerson:
                    targets.Add(((FindPersonAction)missionAction).Target);
                    break;
                case MissionActionType.FindItem:
                    targets.Add(((FindItemAction)missionAction).Target);
                    break;
                case MissionActionType.UseItemOnItem:
                    // both ends go in: the one that is loaded is not always Source
                    targets.Add(((UseItemOnItemAction)missionAction).Source);
                    targets.Add(((UseItemOnItemAction)missionAction).Destination);
                    break;
                case MissionActionType.KillPerson:
                    targets.Add(((KillPersonAction)missionAction).Target);
                    break;
            }
        }

        foreach (Identity target in targets)
        {
            objectiveDynel = DynelManager.AllDynels.FirstOrDefault(x => x.Identity == target);

            if (objectiveDynel != null)
                break;
        }

        return objectiveDynel != null;
    }

    private static object? Objective()
    {
        if (!GetMissionDynel(out Dynel? found) || found == null) return null;

        return new
        {
            x = R(found.Position.X),
            z = R(found.Position.Z),
            f = found.Room?.Floor ?? 0,
            n = found.Name,
        };
    }

    /// <summary>A chest you have already been into has contents the client knows.</summary>
    private static bool IsOpened(Dynel dynel)
    {
        try { return Inventory.GetContainerItems(dynel.Identity).Any(); }
        catch { return false; }
    }

    private static float R(float v) => MathF.Round(v, 1);
}
