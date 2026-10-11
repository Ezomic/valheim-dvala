using System.Collections.Generic;
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
                    "restock - restock the dungeon whose entrance you stand at now, pickups included. "
                    + "scan - every dungeon generator loaded, networked or not. "
                    + "wanted - whether the config covers the dungeon whose entrance you stand at, "
                    + "by its room themes, which is the test a visit restocks by. "
                    + "newdungeon - replace the dungeon whose entrance you stand at with a freshly "
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
            if (verb == "restock")
            {
                term.AddString("dvala restock: " + Restock());
                return;
            }

            if (verb == "scan")
            {
                foreach (string line in Scan())
                {
                    term.AddString("dvala scan: " + line);
                    DvalaPlugin.Log.LogInfo("scan: " + line);
                }
                return;
            }

            if (verb == "wanted")
            {
                term.AddString("dvala wanted: " + Wanted());
                return;
            }

            if (verb != "newdungeon")
            {
                term.AddString("dvala: try 'dvala newdungeon' or 'dvala restock' while standing at a dungeon entrance.");
                return;
            }

            term.AddString("dvala newdungeon: " + Do());
        }

        /// <summary>
        /// Every DungeonGenerator the game has loaded, networked or not, nearest first. Dungeons.Live
        /// only returns the networked ones, so a dungeon whose generator has no ZDO is invisible to
        /// the mod and to `dvala wanted`; this is how to tell "not there" from "there and not seen".
        /// </summary>
        private static List<string> Scan()
        {
            var lines = new List<string>();
            Player player = Player.m_localPlayer;
            if (player == null) { lines.Add("refused no player in a world"); return lines; }

            DungeonGenerator[] all = Object.FindObjectsOfType<DungeonGenerator>(true);
            var rows = new List<KeyValuePair<float, string>>();

            foreach (DungeonGenerator generator in all)
            {
                if (generator == null) continue;

                Vector3 apart = generator.transform.position - player.transform.position;
                apart.y = 0f;

                ZNetView nview = generator.GetComponent<ZNetView>();
                string net = nview == null ? "none" : (nview.IsValid() ? "valid" : "invalid");

                rows.Add(new KeyValuePair<float, string>(apart.magnitude,
                    "name=" + generator.name.Replace("(Clone)", "") + " themes=" + (int)generator.m_themes
                    + " znetview=" + net + " active=" + (generator.gameObject.activeInHierarchy ? "yes" : "no")
                    + " away=" + Mathf.RoundToInt(apart.magnitude) + "m y=" + Mathf.RoundToInt(generator.transform.position.y)));
            }

            rows.Sort((x, y) => x.Key.CompareTo(y.Key));
            lines.Add("count=" + rows.Count);
            for (int i = 0; i < rows.Count && i < 12; i++) lines.Add(rows[i].Value);
            return lines;
        }

        private static string Wanted()
        {
            DungeonGenerator nearest = Nearest(out string refusal);
            if (nearest == null) return refusal;

            return "name=" + nearest.name.Replace("(Clone)", "") + " themes=" + (int)nearest.m_themes
                   + " covered=" + (Dungeons.Wanted(nearest) ? "yes" : "no");
        }

        private static string Restock()
        {
            DungeonGenerator nearest = Nearest(out string refusal);
            if (nearest == null) return refusal;

            if (Dungeons.Occupied(nearest))
                return "refused you are inside it, stand at the entrance outside the rooms";

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

        private static string Do()
        {
            DungeonGenerator nearest = Nearest(out string refusal);
            if (nearest == null) return refusal;

            if (!Regenerate.Eligible(nearest))
                return "refused " + nearest.name + " is not one the config lets this replace "
                       + "(themes " + (int)nearest.m_themes + ")";

            if (Dungeons.Occupied(nearest))
                return "refused you are inside it, stand at the entrance outside the rooms";

            if (Regenerate.ZoneOccupied(nearest, true))
                return "refused another player is in this zone";

            Regenerate.Outcome outcome = Regenerate.Run(nearest, Dungeons.Today(),
                                                        Dungeons.Stamped(nearest));
            DvalaPlugin.Log.LogInfo("Command: new dungeon for " + Dungeons.Describe(nearest) + ": "
                                    + outcome + ".");
            return outcome.ToString();
        }
    }
}
