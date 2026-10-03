using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// Makes every other client rebuild a dungeon's walls when its layout changes.
    ///
    /// <b>UNPROVEN.</b> This is read from the 1.0 assemblies and has never run. Singleplayer
    /// does not need it, because the machine that regenerates builds its own shells inside
    /// Generate. It matters only with a second client that has the zone loaded.
    ///
    /// The problem: a room's walls and floors are plain children of the generator with no ZDO
    /// of their own, built once in the generator's Awake from the saved room list, and nothing
    /// ever builds them again. When a client regenerates, the contents change under the others
    /// through ordinary ZDO traffic (old objects vanish, new ones appear) while their walls stay
    /// as they were, so they would see new chests inside old rooms until they reloaded the zone.
    ///
    /// The fix is the smallest one that reuses vanilla: destroy the generator's GameObject
    /// without destroying its ZDO and let ZNetScene make it again. That is exactly what
    /// <c>ZNetScene.RemoveObjects</c> does to an object leaving the active area, and a ZDO whose
    /// <c>Created</c> flag has been cleared is picked up by <c>CreateObjects</c> on the next
    /// pass, which runs the generator's Awake, Load and Spawn against the new saved layout.
    ///
    /// Each client watches the generator's <c>dvala_gen</c> counter. The first value it sees for
    /// a generator instance is remembered and acted on never, since the shells it just built already
    /// match; a later, different value is the signal. The client that regenerated records the
    /// new value itself, so it does not tear down the shells Generate just made.
    ///
    /// The private <c>ZNetScene.m_instances</c> is reached by reflection, and bound lazily: a
    /// renamed field costs the rebuild and never the mod. Without it the public
    /// <c>ZNetScene.Destroy</c> still works on a generator this machine does not own, because
    /// it only destroys the ZDO for an owner. For an owner it is not used at all, since that
    /// would delete the dungeon's ZDO and with it the whole layout.
    /// </summary>
    internal static class Shells
    {
        /// <summary>
        /// The generation each generator INSTANCE was last known to match, keyed by instance id
        /// and not by ZDO. An instance built from the saved layout already has the current
        /// walls, so its first-seen value is the baseline; keyed by ZDO, a client that left the
        /// zone and came back to a rebuilt generator would find a stale entry, see a
        /// difference, and tear down walls that were just built correctly.
        /// </summary>
        private static readonly Dictionary<int, int> Built = new Dictionary<int, int>();
        private static readonly List<int> Gone = new List<int>();
        private static readonly HashSet<int> Seen = new HashSet<int>();

        internal static void Remember(DungeonGenerator generator)
        {
            ZNetView nview = generator.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;

            Built[generator.GetInstanceID()] = nview.GetZDO().GetInt(Regenerate.GenKey, 0);
        }

        internal static void Watch()
        {
            Seen.Clear();

            foreach (DungeonGenerator generator in Dungeons.Live())
            {
                ZNetView nview = generator.GetComponent<ZNetView>();
                ZDO zdo = nview.GetZDO();

                int id = generator.GetInstanceID();
                Seen.Add(id);

                int now = zdo.GetInt(Regenerate.GenKey, 0);

                int known;
                if (!Built.TryGetValue(id, out known))
                {
                    Built[id] = now;
                    continue;
                }

                if (known == now) continue;

                Built[id] = now;
                Rebuild(generator, nview);
            }

            Gone.Clear();
            foreach (int id in Built.Keys)
                if (!Seen.Contains(id)) Gone.Add(id);
            foreach (int id in Gone) Built.Remove(id);
        }

        private static void Rebuild(DungeonGenerator generator, ZNetView nview)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;

            string name = Dungeons.Describe(generator);
            ZDO zdo = nview.GetZDO();

            Dictionary<ZDO, ZNetView> instances = null;
            try
            {
                instances = Traverse.Create(scene).Field("m_instances")
                                    .GetValue<Dictionary<ZDO, ZNetView>>();
            }
            catch (System.Exception error)
            {
                DvalaPlugin.Log.LogWarning("Could not read ZNetScene's instance table: " + error.Message);
            }

            if (instances != null)
            {
                nview.ResetZDO();
                instances.Remove(zdo);
                Object.Destroy(generator.gameObject);

                DvalaPlugin.Log.LogInfo("Rebuilding the walls of " + name
                                        + " for its new layout (unproven path).");
                return;
            }

            if (!zdo.IsOwner())
            {
                scene.Destroy(generator.gameObject);
                DvalaPlugin.Log.LogInfo("Rebuilding the walls of " + name
                                        + " through ZNetScene.Destroy (unproven path).");
                return;
            }

            DvalaPlugin.Log.LogWarning("Cannot rebuild the walls of " + name + ": the instance table "
                                       + "is unreachable and this machine owns the dungeon. Its "
                                       + "walls are stale until the zone is reloaded.");
        }
    }
}
