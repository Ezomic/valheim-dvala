using HarmonyLib;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// `dvala restock` in the console: restock the dungeon whose entrance you are standing at,
    /// now, without waiting thirty days. It exists for a Devkit scenario, which runs it through
    /// the `mod` step, and for trying the pickup rebuild on one cave in singleplayer.
    ///
    /// isCheat true, like the other console commands in this suite. Devkit's `mod` step calls
    /// the delegate directly and does not pay for it.
    ///
    /// It refuses while you are inside the dungeon (stand at the entrance, outside the rooms)
    /// and when this machine does not own the dungeon, for the reasons the timer does.
    /// </summary>
    internal static class DevConsole
    {
        private static bool _registered;

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        internal static class Hook
        {
            private static void Postfix()
            {
                if (_registered) return;
                _registered = true;

                new Terminal.ConsoleCommand("dvala",
                    "restock - restock the dungeon whose entrance you stand at now, pickups included",
                    OnCommand, isCheat: true);
            }
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            Terminal term = args.Context;
            if (term == null) return;

            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (verb != "restock")
            {
                term.AddString("dvala: try 'dvala restock' while standing at a dungeon entrance.");
                return;
            }

            term.AddString("dvala restock: " + Restock());
        }

        private static string Restock()
        {
            DungeonGenerator nearest = Nearest(out string refusal);
            if (nearest == null) return refusal;

            if (Dungeons.Occupied(nearest))
                return "refused you are inside it, stand at the entrance outside the rooms";

            if (Dungeons.HoldsTombstone(nearest))
                return "refused a gravestone stands in its rooms";

            if (!nearest.GetComponent<ZNetView>().IsOwner())
                return "refused this machine does not own the dungeon";

            Restore.Counts counts = Restore.Dungeon(nearest);
            DvalaPlugin.Log.LogInfo("Command: restock " + Dungeons.Describe(nearest) + ": "
                                    + counts + ".");
            return "ok " + counts;
        }

        private static DungeonGenerator Nearest(out string refusal)
        {
            refusal = null;
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null)
            {
                refusal = "refused no player in a world";
                return null;
            }

            Vector2s zone = ZoneSystem.GetZone(player.transform.position);
            DungeonGenerator nearest = null;
            float best = float.MaxValue;

            foreach (DungeonGenerator generator in Dungeons.Live())
            {
                if (ZoneSystem.GetZone(generator.transform.position) != zone) continue;

                Vector3 apart = generator.transform.position - player.transform.position;
                apart.y = 0f;
                if (apart.sqrMagnitude >= best) continue;

                best = apart.sqrMagnitude;
                nearest = generator;
            }

            if (nearest == null) refusal = "refused no dungeon generator in this zone";
            return nearest;
        }
    }
}
