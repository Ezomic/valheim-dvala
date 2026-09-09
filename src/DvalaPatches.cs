using HarmonyLib;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// The one patch this mod has, and the only place it changes vanilla rather than restoring
    /// it.
    ///
    /// Everything else Dvala does is a write to a value the game already keeps: a chest's
    /// flag, a pickable's flag, a vein's health, a spawner's connection. Nothing is destroyed
    /// and nothing is invented. Ore veins are the exception, and not by choice - a vein is a
    /// cluster of chunks with a health each, and the moment the last one dies MineRock5 calls
    /// Destroy on itself. That removes the ZDO from the world, the server tombstones the id so
    /// a re-sent copy is destroyed again, and nothing anywhere records that a vein was ever at
    /// that spot or which kind it was. A chest keeps its note when it is emptied. A vein does
    /// not survive being finished.
    ///
    /// So the choice is between a hole in the promise - strip a crypt bare and only the chests
    /// and spawners come back - and holding that one deletion back. This holds it back.
    ///
    /// What is left when it does: an object whose every area is dead. UpdateMesh deactivates
    /// each area's collider as its health reaches zero and rebuilds the combined mesh from the
    /// survivors, so a fully mined vein under this patch is invisible, has no collision, drops
    /// nothing and cannot be hit. It is a saved object and nothing else - which is exactly what
    /// a half-mined vein already is, and the world was already paying for those.
    ///
    /// <b>Inside a managed dungeon only.</b> The test is Dungeons.Inside(point), and it is
    /// room membership rather than a height or a radius - which matters because Hildir's Sealed
    /// Tower stands on the ground with a Fuling camp for a neighbour, and a box around the
    /// generator would claim the camp's veins along with the tower's. An earlier version opened
    /// with a height compare, since interiors hang above y 3000; it was free and it excluded
    /// the Sealed Tower, which is the one place that was asked for by name.
    ///
    /// This still runs on the swing of a pickaxe. What keeps that honest now is that the check
    /// returns immediately when no managed dungeon is loaded, which is the state anybody mining
    /// in the open is in.
    ///
    /// One call site, checked rather than assumed: AllDestroyed is read in exactly one place,
    /// the branch that destroys the object. The field it sets alongside, m_allDestroyed, only
    /// short-circuits the multi-area damage loop after a destroy, so leaving it false is right
    /// - the object is still there and its other areas are still damageable.
    /// </summary>
    internal static class DvalaPatches
    {
        [HarmonyPatch(typeof(MineRock5), "AllDestroyed")]
        [HarmonyPostfix]
        private static void KeepMinedVeins(MineRock5 __instance, ref bool __result)
        {
            // Cheapest first, and in this order for a reason: three bools and a float compare
            // before anything that walks a list.
            if (!__result) return;
            if (!DvalaConfig.Enabled.Value) return;
            if (!DvalaConfig.KeepVeins.Value) return;

            if (__instance == null) return;
            if (!Dungeons.Inside(__instance.transform.position)) return;

            __result = false;
        }
    }
}
