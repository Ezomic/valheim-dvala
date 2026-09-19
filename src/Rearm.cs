using HarmonyLib;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// Makes a re-armed spawner stay re-armed on a server.
    ///
    /// Re-arming a spent spawner means clearing its Spawned connection, and that write does
    /// not travel: ZDO.Serialize only writes a connection whose type is non-zero, and
    /// Deserialize never clears one it was not sent. So the cleared state lives on the one
    /// client that wrote it. In singleplayer that client is the world and it held, which is
    /// where 1.0.0 was proven. On a server it lasted until that client dropped the ZDO -
    /// leaving the area, logging out, another player taking ownership - and every later load
    /// came back from the server still spent. Meanwhile the day stamp, the chest contents, the
    /// pickable flags and the vein health had all synced, so the dungeon read as restocked for
    /// the next thirty days with its loot back and its creatures gone. That is the "items but
    /// no mobs" players saw, and it was worst exactly when the sweep ran for somebody walking
    /// past the entrance rather than going in, since the sweep waits until nobody is inside.
    ///
    /// The fix is a note that does travel. The sweep writes an int on each spawner it re-arms,
    /// and ints sync like any other ZDO value. Whichever client owns that spawner when it next
    /// ticks - whoever it is, whenever it is - sees the note and clears the connection itself,
    /// in the same call that then decides whether to spawn. The fragile write now only has to
    /// survive from the top of UpdateSpawner to its bottom, on one machine. The note is lifted
    /// once the spawner is found pointing at a living creature, which is Spawn()'s own
    /// connection write, and that one does sync. It is lifted straight after Spawn() returns a
    /// creature rather than on the next tick: UpdateSpawner runs every few seconds, and a
    /// skeleton killed inside that gap would otherwise leave the note standing over a spent
    /// connection, which this would clear again - a spawner that respawns as fast as it is
    /// emptied.
    ///
    /// A spawner in a group may never spawn itself - the group picks one member - so its note
    /// can outlive the restock. That is harmless: the member it clears counts nothing, and the
    /// member that did spawn still counts toward the group's cap, so the group cannot fill past
    /// what vanilla allows. The next sweep writes the notes fresh anyway.
    /// </summary>
    internal static class Rearm
    {
        internal static readonly int Key = "dvala_rearm".GetStableHashCode();

        /// <summary>Called by the sweep for every spawner it re-arms.</summary>
        internal static void Mark(ZDO zdo, int day)
        {
            zdo.Set(Key, Mathf.Max(1, day));
        }

        [HarmonyPatch(typeof(CreatureSpawner), "UpdateSpawner")]
        [HarmonyPrefix]
        private static void BeforeUpdate(CreatureSpawner __instance)
        {
            if (!DvalaConfig.Enabled.Value || __instance == null) return;

            // The same owner gate UpdateSpawner opens with. Only the owner's copy decides a
            // spawn, and a write on any other machine would be discarded anyway.
            ZNetView nview;
            if (!__instance.TryGetComponent(out nview) || !nview.IsValid() || !nview.IsOwner())
                return;

            ZDO zdo = nview.GetZDO();
            if (zdo.GetInt(Key) <= 0) return;
            if (zdo.GetConnectionType() != ZDOExtraData.ConnectionType.Spawned) return;

            // Pointing at something alive means it has spawned since the note was written.
            // The note has done its job.
            ZDOID spawned = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
            if (!spawned.IsNone() && ZDOMan.instance.GetZDO(spawned) != null)
            {
                zdo.Set(Key, 0);
                return;
            }

            zdo.SetConnection(ZDOExtraData.ConnectionType.None, ZDOID.None);
        }

        /// <summary>
        /// The spawn this note was waiting for. Spawn() is also what a group calls on whichever
        /// member it picked, so this lifts the note on the member that actually spawned.
        /// </summary>
        [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
        [HarmonyPostfix]
        private static void AfterSpawn(CreatureSpawner __instance, ZNetView __result)
        {
            if (__result == null || __instance == null) return;

            ZNetView nview;
            if (!__instance.TryGetComponent(out nview) || !nview.IsValid() || !nview.IsOwner())
                return;

            ZDO zdo = nview.GetZDO();
            if (zdo.GetInt(Key) > 0) zdo.Set(Key, 0);
        }
    }
}
