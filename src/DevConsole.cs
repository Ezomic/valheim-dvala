using HarmonyLib;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// `dvala newdungeon` in the console: replace the dungeon whose entrance you are standing
    /// at, now, without waiting thirty days and without turning the setting on.
    ///
    /// It exists so the new-dungeon path can be tried on one crypt in singleplayer, and for a
    /// Devkit scenario, which runs it through the `mod` step.
    ///
    /// isCheat true, unlike a harmless switch: this deletes saved objects. A player has to
    /// type devcommands first, which also marks the character, and that is the right price for
    /// a command that does what the setting does on demand. Devkit's `mod` step calls the
    /// delegate directly and does not pay it.
    ///
    /// It refuses while you are inside the dungeon (stand at the entrance, outside the rooms)
    /// and while any other player is in the zone, for the reason the timer does.
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
                    "newdungeon - replace the dungeon whose entrance you stand at with a freshly "
                    + "generated one. Deletes that dungeon's saved objects; ignores the NewDungeon "
                    + "setting, never the theme and Hildir switches",
                    OnCommand, isCheat: true);
            }
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            Terminal term = args.Context;
            if (term == null) return;

            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (verb != "newdungeon")
            {
                term.AddString("dvala: try 'dvala newdungeon' while standing at a dungeon entrance.");
                return;
            }

            term.AddString("dvala newdungeon: " + Do());
        }

        private static string Do()
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null) return "refused no player in a world";

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

            if (nearest == null) return "refused no dungeon generator in this zone";

            if (!Regenerate.Eligible(nearest))
                return "refused " + nearest.name + " is not one the config lets this replace "
                       + "(themes " + (int)nearest.m_themes + ")";

            if (Dungeons.Occupied(nearest))
                return "refused you are inside it, stand at the entrance outside the rooms";

            if (Regenerate.ZoneOccupied(nearest, true))
                return "refused another player is in this zone";

            Dungeons.Stamp(nearest, Dungeons.Today());

            Regenerate.Outcome outcome = Regenerate.Run(nearest);
            DvalaPlugin.Log.LogInfo("Command: new dungeon for " + Dungeons.Describe(nearest) + ": "
                                    + outcome + ".");
            return outcome.ToString();
        }
    }
}
