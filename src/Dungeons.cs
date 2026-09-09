using System.Collections.Generic;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// Which dungeons exist right now, which of them are ours to touch, and how long each has
    /// been left alone.
    ///
    /// Everything here reads, except <see cref="Stamp"/>, which writes one integer. The
    /// restoring is <see cref="Restore"/>'s job and the split is deliberate: this file runs on
    /// a timer against every loaded dungeon in the world, so it has to be cheap and it has to
    /// be unable to do harm.
    ///
    /// Three decisions worth having in view.
    ///
    /// <b>The clock lives on the dungeon, not in the world's global keys.</b> Every
    /// <c>GlobalKeyAdd</c> also writes <c>key + " " + value</c> into the saved player profile's
    /// known-keys dictionary, so a per-dungeon key whose value is a day number would grow that
    /// dictionary forever - one entry per dungeon per reset. The generator already has a
    /// ZNetView and a ZDO that is saved with the world and travels with the zone, which is
    /// exactly the lifetime this number wants.
    ///
    /// <b>A dungeon nobody has seen is not overdue.</b> The first time Dvala sees a generator
    /// with no stamp it writes today rather than treating the missing value as day zero.
    /// Otherwise installing the mod on a long-running world resets every dungeon in it on the
    /// next tick, which is a fair reading of "thirty days have passed" and absolutely not what
    /// anybody means by it.
    ///
    /// <b>Membership is the generator's own room boxes.</b> Copied from Lur, where it was paid
    /// for: a box of <c>m_zoneSize</c> centred on a generator that sits on the ground - the
    /// Sealed Tower, a Fuling camp - reaches into whatever is next door. The rooms exclude the
    /// neighbours by construction, and for a sky crypt hanging kilometres above the terrain
    /// either test would have done.
    /// </summary>
    internal static class Dungeons
    {
        /// <summary>
        /// The ZDO key holding the day a dungeon was last stocked, hashed the ordinary way.
        ///
        /// The name is permanent in the same sense a prefab name is: it is written into saved
        /// worlds, and changing it does not migrate anything, it silently restarts every
        /// dungeon's clock from the day of the update.
        /// </summary>
        private static readonly int StampKey = "dvala_day".GetStableHashCode();

        /// <summary>
        /// Every live, network-backed dungeon generator in the scene.
        ///
        /// The ZNetView guard is load-bearing rather than defensive, and this is the one piece
        /// of this file that is not obvious. A client-side location instance carries an
        /// inactive duplicate of every networked child, generator included, because
        /// ZoneSystem.SpawnLocation's client branch disables them on the prefab before
        /// instantiating it. A plain component scan therefore finds shells with no ZDO and no
        /// rooms - and a mod that reads one does not fail, it reports an empty dungeon.
        /// </summary>
        internal static IEnumerable<DungeonGenerator> Live()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) yield break;

            foreach (DungeonGenerator generator
                     in Object.FindObjectsOfType<DungeonGenerator>())
            {
                if (generator == null) continue;

                ZNetView nview = generator.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                yield return generator;
            }
        }

        /// <summary>
        /// Whether this dungeon is one the config says to restock.
        ///
        /// Bit tests against Room.Theme, never a string comparison. Theme is not decorated
        /// [Flags], so a multi-theme value stringifies to a plain number - the third-party mod
        /// this one replaces matches <c>m_themes.ToString()</c> against a config string, which
        /// is a substring test and wrong in both directions: a config asking for "SunkenCrypt"
        /// silently takes plain "Crypt" with it.
        /// </summary>
        internal static bool Wanted(DungeonGenerator generator)
        {
            return (generator.m_themes & DvalaConfig.Themes()) != 0;
        }

        /// <summary>
        /// The day this dungeon was last stocked, or -1 if it has never been stamped.
        ///
        /// Reading needs no ownership. Writing does, which is why that is a separate call.
        /// </summary>
        internal static int Stamped(DungeonGenerator generator)
        {
            ZNetView nview = generator.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return -1;

            return nview.GetZDO().GetInt(StampKey, -1);
        }

        /// <summary>
        /// Writes today onto the dungeon, claiming it first.
        ///
        /// The claim is not optional and its failure is silent: a write to a ZDO this machine
        /// does not own is discarded without an error, so a reset would run again on the next
        /// tick, and the next, for as long as the zone stayed loaded. Vanilla's own Take All
        /// does exactly this before it touches a chest.
        /// </summary>
        internal static void Stamp(DungeonGenerator generator, int day)
        {
            ZNetView nview = generator.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;

            nview.ClaimOwnership();
            nview.GetZDO().Set(StampKey, day);
        }

        private static readonly List<DungeonGenerator> Managed = new List<DungeonGenerator>();
        private static float _managedAt = -999f;

        /// <summary>
        /// Whether a point is inside a dungeon this mod is set to restock.
        ///
        /// Asked from a Harmony patch on a per-hit path, so the cost matters in a way the
        /// thirty second sweep's does not. It used to open with a height test - dungeon
        /// interiors hang above y 3000, which is vanilla's own <c>Character.InInterior</c>
        /// number - and that was free, because it rejected every copper deposit on the
        /// surface without looking at anything. It had to go: Hildir's Sealed Tower is a
        /// dungeon that stands on the ground, so a height test excludes exactly the place
        /// that was asked for.
        ///
        /// What replaces it is the empty check below, which is free in the same way whenever
        /// no dungeon this mod cares about is loaded - the ordinary case for somebody mining
        /// in the open. When one is loaded the cost is a few box tests against its rooms, and
        /// rooms rather than a zone box is what keeps a ground-level dungeon from claiming the
        /// Fuling camp next door. The list itself is rebuilt at most every two seconds.
        /// </summary>
        internal static bool Inside(Vector3 point)
        {
            if (Time.realtimeSinceStartup - _managedAt > 2f)
            {
                _managedAt = Time.realtimeSinceStartup;

                Managed.Clear();
                foreach (DungeonGenerator generator in Live())
                    if (Wanted(generator)) Managed.Add(generator);
            }

            if (Managed.Count == 0) return false;

            for (int i = 0; i < Managed.Count; i++)
            {
                DungeonGenerator generator = Managed[i];
                if (generator == null) continue;
                if (Inside(generator, point)) return true;
            }

            return false;
        }

        /// <summary>
        /// A name that tells two of them apart.
        ///
        /// Every burial chamber in the world is called DG_ForestCrypt(Clone), so the first log
        /// of a run reading "restocked DG_ForestCrypt(Clone)" four times could not be told from
        /// one dungeon restocked four times - which is the exact question that mattered. The
        /// ZDO's id is unique and stable for the life of the object.
        /// </summary>
        internal static string Describe(DungeonGenerator generator)
        {
            ZNetView nview = generator.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return generator.name;

            return generator.name + " " + nview.GetZDO().m_uid;
        }

        /// <summary>Today, as the world counts days. -1 while the world is still coming up.</summary>
        internal static int Today()
        {
            EnvMan env = EnvMan.instance;
            return env == null ? -1 : env.GetDay();
        }

        /// <summary>
        /// Whether any player is inside this dungeon's rooms.
        ///
        /// Cheap by construction: the player list is short, and the room test is a handful of
        /// box checks. It is asked per dungeon per tick and never per object.
        /// </summary>
        internal static bool Occupied(DungeonGenerator generator)
        {
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null) continue;
                if (Inside(generator, player.transform.position)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a point is inside one of this generator's placed rooms.
        ///
        /// In the room's own space, not the world's. Rooms are placed at whatever rotation the
        /// generator's walk gave them, so an axis-aligned Bounds around a rotated room is a
        /// box that is both too big on the diagonal and too small on the face - and the error
        /// is largest exactly where it matters, at the walls. Lur's copy of this test is where
        /// that was worked out.
        ///
        /// m_size is a Vector3i, so the cast is deliberate rather than incidental.
        /// </summary>
        internal static bool Inside(DungeonGenerator generator, Vector3 point)
        {
            float pad = Mathf.Max(0f, DvalaConfig.RoomPadding.Value);

            foreach (Room room in generator.GetComponentsInChildren<Room>())
            {
                if (room == null) continue;

                Vector3 local = Quaternion.Inverse(room.transform.rotation)
                                * (point - room.transform.position);
                Vector3 half = (Vector3)room.m_size * 0.5f;

                if (Mathf.Abs(local.x) <= half.x + pad
                    && Mathf.Abs(local.y) <= half.y + pad
                    && Mathf.Abs(local.z) <= half.z + pad)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
