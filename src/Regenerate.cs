using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// A whole new dungeon: delete what the generator made, then run the generator again with
    /// a seed it has not used. Opt-in (<c>NewDungeon</c>, off by default), and the one thing in
    /// this mod that deletes saved objects, which everything else here promises never to do.
    ///
    /// <b>Why this and not "put the old one back".</b> A destroyed pot or crate leaves no
    /// record, so restoring it means rebuilding the room from a list, and the list is only as
    /// long as the kinds somebody thought of. A new generation lays every chest, pot, door and
    /// trap by itself. The price is that it has to remove the old ones first, because
    /// <c>DungeonGenerator.Generate</c> is additive: <c>Clear()</c> only destroys the room
    /// shells, whose networked children were never part of the generator, so running it over a
    /// live dungeon stacks a second dungeon's contents on the first.
    ///
    /// <b>Where the seed lives.</b> Nowhere. <c>m_generatedSeed</c> is not written to a ZDO.
    /// What the world keeps is the RESULT, <c>s_roomData</c>, and every later load reads that
    /// back without generating anything. So the seed here is Dvala's own: the one vanilla used
    /// plus a counter kept on the generator's ZDO times a prime, deterministic and never
    /// repeating for that dungeon. <c>GetSeed()</c> is not used for the base, because after one
    /// <c>Generate(seed, mode)</c> it returns the seed that call was given.
    ///
    /// <b>What is deleted.</b> Never "everything inside the rooms". A ZDO's sector is its x and
    /// z only, and the interior hangs about 5000 m above the entrance in the same sector, so a
    /// height test catches nothing useful and a box test catches whatever a player dropped. The
    /// filter is a whitelist: a networked object is taken only when it is inside this
    /// generator's room boxes AND its prefab is one of the networked prefabs in this
    /// generator's own room prefabs or door prefabs. Then three refusals on top: anything a
    /// player placed (a non-zero creator on the ZDO), anything tamed, and any dropped item.
    /// Creatures are added by the one other route the game gives, a spawner's Spawned
    /// connection, since a creature is not a child of a room.
    ///
    /// <b>Known cost, stated plainly.</b> A chest the generator placed is deleted with whatever
    /// is in it, including a player's stash. There is no way to tell an unlooted chest from one
    /// somebody has been using, and refusing every non-empty chest would refuse almost all of
    /// them. The README says so.
    ///
    /// <b>UNPROVEN, and the part most likely to need a second round:</b> the shell rebuild on
    /// other clients (see <see cref="Shells"/>). Nothing in this file has run in game.
    /// </summary>
    internal static class Regenerate
    {
        /// <summary>
        /// The generator ZDO's count of regenerations. Permanent in the same sense as the stamp
        /// key: it is written into saved worlds, and the seed is derived from it, so changing
        /// the name restarts every dungeon on the first seed again.
        /// </summary>
        internal static readonly int GenKey = "dvala_gen".GetStableHashCode();

        private const int SeedStep = 7919;

        internal struct Outcome
        {
            internal bool Ok;
            internal string Reason;
            internal int Removed;
            internal int Creatures;
            internal int Kept;
            internal int RoomsBefore;
            internal int Rooms;
            internal int Gen;
            internal int Seed;
            internal bool Rerolled;

            // name=value with nothing inside a value, so a Devkit `printed` step can match it.
            public override string ToString()
            {
                if (!Ok) return "refused " + Reason;

                return "ok removed=" + Removed + " creatures=" + Creatures + " kept=" + Kept
                       + " rooms=" + Rooms + " was=" + RoomsBefore + " gen=" + Gen
                       + " seed=" + Seed + " rerolled=" + (Rerolled ? "yes" : "no");
            }
        }

        private sealed class Target
        {
            internal ZDO Zdo;
            internal ZNetView View;
            internal string Prefab;
        }

        /// <summary>
        /// Which generators this may touch: algorithm Dungeon, and themes made up ONLY of ones
        /// the config allows.
        ///
        /// "Only", not "any". Wanted accepts a generator when any one of its themes is
        /// switched on, which is right for restocking and wrong for deleting: a generator that
        /// mixes a boss theme into an allowed one would pass an "any" test. The Queen's theme
        /// (DvergerBoss), mines (DvergerTown), Ashlands and camps are never in the allowed
        /// mask, so a generator carrying any of them is out.
        /// </summary>
        internal static bool Eligible(DungeonGenerator generator)
        {
            if (generator.m_algorithm != DungeonGenerator.Algorithm.Dungeon) return false;

            Room.Theme allowed = Allowed();
            Room.Theme themes = generator.m_themes;

            return (themes & allowed) != 0 && (themes & ~allowed) == 0;
        }

        private static Room.Theme Allowed()
        {
            Room.Theme mask = Room.Theme.None;

            if (DvalaConfig.Crypts.Value)
                mask |= Room.Theme.Crypt | Room.Theme.ForestCrypt | Room.Theme.SunkenCrypt;
            if (DvalaConfig.Caves.Value) mask |= Room.Theme.Cave;

            if (DvalaConfig.NewDungeonHildir.Value)
                mask |= Room.Theme.ForestCryptHildir | Room.Theme.CaveHildir
                        | Room.Theme.PlainsFortHildir;

            return mask;
        }

        /// <summary>
        /// Whether a player is anywhere in this dungeon's zone, entrance included.
        ///
        /// The timer path asks this with <paramref name="sparing"/> false: nobody in the zone
        /// at all, so no client is standing next to walls that are about to change. That makes
        /// it a rule that holds only while a player is near enough to have the zone loaded and
        /// still outside it, usually one zone away, so on a busy server it rarely runs. That
        /// was a choice, made for how much simpler it keeps multiplayer.
        ///
        /// The command asks with it true: the local player may stand at the entrance, which is
        /// in the zone but outside every room, and that is exactly where it is meant to be used.
        /// </summary>
        internal static bool ZoneOccupied(DungeonGenerator generator, bool sparing)
        {
            Vector2s zone = ZoneSystem.GetZone(generator.transform.position);

            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null) continue;
                if (ZoneSystem.GetZone(player.transform.position) != zone) continue;

                if (sparing && player == Player.m_localPlayer && !Dungeons.Occupied(generator))
                    continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Replaces the dungeon. The caller has already stamped it and checked that the zone
        /// is clear.
        ///
        /// Nothing is deleted until every refusal that can be known in advance has been checked:
        /// a live ZDO, placed rooms, a located prefab for a custom interior, and a non-empty
        /// whitelist. After that point the only way out is forwards, so a throw inside Generate
        /// is answered by trying once more with the next seed rather than by leaving an empty
        /// dungeon behind.
        /// </summary>
        internal static Outcome Run(DungeonGenerator generator)
        {
            var outcome = new Outcome();

            ZNetView nview = generator.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
                return Refuse(outcome, "the generator has no live ZDO");

            outcome.RoomsBefore = generator.GetComponentsInChildren<Room>().Length;
            if (outcome.RoomsBefore == 0)
                return Refuse(outcome, "no rooms are built yet, so there is nothing to tell the "
                                       + "dungeon's objects from anyone else's");

            Vector3 original;
            string why;
            if (!TryOriginalPosition(generator, out original, out why))
                return Refuse(outcome, why);

            HashSet<int> allowed = Whitelist(generator);
            if (allowed.Count == 0)
                return Refuse(outcome, "no room prefab could be read, so nothing is known to belong "
                                       + "to this dungeon");

            ZDO zdo = nview.GetZDO();
            nview.ClaimOwnership();

            Location locationOf;
            bool damage = TryLocation(generator, out locationOf) && locationOf.m_applyRandomDamage;

            int baseSeed = BaseSeed(generator);
            int gen = zdo.GetInt(GenKey, 0);

            outcome.Rerolled = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                List<Target> targets = Collect(generator, allowed, ref outcome);
                Remove(targets);

                gen++;
                zdo.Set(GenKey, gen);

                bool generated = Generate(generator, baseSeed + gen * SeedStep, original, damage);

                outcome.Gen = gen;
                outcome.Seed = baseSeed + gen * SeedStep;

                if (generated && !Poor(generator, outcome.RoomsBefore, out outcome.Rooms))
                    break;

                if (attempt == 0)
                {
                    DvalaPlugin.Log.LogWarning("The new layout of " + Dungeons.Describe(generator)
                                               + " was poor (" + outcome.Rooms + " rooms, was "
                                               + outcome.RoomsBefore + "). Rolling once more.");
                    outcome.Rerolled = true;
                    allowed = Whitelist(generator);
                    continue;
                }

                DvalaPlugin.Log.LogWarning("The second layout of " + Dungeons.Describe(generator)
                                           + " was poor too (" + outcome.Rooms + " rooms). Keeping it.");
            }

            Shells.Remember(generator);

            outcome.Ok = true;
            return outcome;
        }

        private static Outcome Refuse(Outcome outcome, string reason)
        {
            outcome.Ok = false;
            outcome.Reason = reason;
            return outcome;
        }

        /// <summary>
        /// The seed vanilla's own GetSeed() computes the first time it is asked, copied rather
        /// than called. GetSeed() caches in m_generatedSeed, and Generate(seed, mode) writes the
        /// seed it was given back into that same field, so a second call after one of ours would
        /// return OUR seed and every regeneration would drift from the last instead of from the
        /// dungeon. m_forceSeed, a developer override, is deliberately not honoured here.
        /// </summary>
        private static int BaseSeed(DungeonGenerator generator)
        {
            int seed = WorldGenerator.instance.GetSeed();
            Vector3 position = generator.transform.position;
            Vector2i zone = ZoneSystem.GetZone(position).ToVector2i();

            return seed + zone.x * 4271 + zone.y * -7187 + (int)position.x * -4271
                   + (int)position.y * 9187 + (int)position.z * -2134;
        }

        /// <summary>
        /// The location prefab this generator was spawned from, found through the
        /// LocationProxy in its zone whose location's generator carries the same name.
        ///
        /// The generator is a top level object, not a child of the proxy, and a Location in
        /// the scene is an instance whose generator was moved while it was built, so neither
        /// holds the number needed. The prefab asset does, and ZoneSystem.m_locations lists it
        /// by name.
        /// </summary>
        private static bool TryLocation(DungeonGenerator generator, out Location location)
        {
            location = null;
            if (ZoneSystem.instance == null) return false;

            Vector2s zone = ZoneSystem.GetZone(generator.transform.position);
            string wanted = Utils.GetPrefabName(generator.gameObject);

            foreach (LocationProxy proxy in UnityEngine.Object.FindObjectsOfType<LocationProxy>())
            {
                if (proxy == null) continue;
                if (ZoneSystem.GetZone(proxy.transform.position) != zone) continue;

                ZNetView view = proxy.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;

                int hash = view.GetZDO().GetInt(ZDOVars.s_location);

                foreach (ZoneSystem.ZoneLocation entry in ZoneSystem.instance.m_locations)
                {
                    if (entry == null || !entry.m_prefab.IsValid) continue;
                    if (entry.m_prefab.Name.GetStableHashCode() != hash) continue;

                    entry.m_prefab.Load();
                    try
                    {
                        GameObject asset = entry.m_prefab.Asset;
                        Location found = asset == null ? null : asset.GetComponent<Location>();
                        if (found == null || found.m_generator == null) continue;
                        if (found.m_generator.name != wanted) continue;

                        location = found;
                        return true;
                    }
                    finally
                    {
                        entry.m_prefab.Release();
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// <c>DungeonGenerator.m_originalPosition</c>, which only SpawnLocation ever sets and
        /// only when the location uses a custom interior transform. A generator rebuilt from
        /// its ZDO has zero there, and Generate subtracts it from the generator's height to
        /// find the zone's centre, so rooms fail the bounds test and the layout comes out tiny.
        /// For the ordinary case (no custom transform) zero is what the first generation had,
        /// and nothing needs doing.
        /// </summary>
        private static bool TryOriginalPosition(DungeonGenerator generator, out Vector3 original,
                                                out string why)
        {
            original = Vector3.zero;
            why = null;

            if (!generator.m_useCustomInteriorTransform) return true;

            Location location;
            if (!TryLocation(generator, out location))
            {
                why = "this dungeon uses a custom interior transform and its location prefab could "
                      + "not be found to read where its generator belongs";
                return false;
            }

            if (location.m_interiorTransform != null)
                original = location.m_generator.transform.localPosition;

            return true;
        }

        /// <summary>
        /// Every prefab hash a room of this generator could have put into the world: the
        /// networked children of each placed room's prefab, and the generator's door prefabs.
        ///
        /// Inactive children are counted. RandomObject and RandomSpawn switch alternatives on
        /// and off, so the enabled-only list the game itself walks at placement time can miss a
        /// variant that this generation happened to pick, and a missed name is an object left
        /// behind to be doubled by the next layout. A wider whitelist is still this dungeon's
        /// own prefabs, and the creator, tamed and dropped-item refusals do the rest.
        /// </summary>
        private static HashSet<int> Whitelist(DungeonGenerator generator)
        {
            var names = new HashSet<int>();
            var seen = new HashSet<int>();

            if (DungeonDB.instance == null) return names;

            foreach (Room room in generator.GetComponentsInChildren<Room>())
            {
                if (room == null) continue;

                int hash = room.GetHash();
                if (!seen.Add(hash)) continue;

                DungeonDB.RoomData data = DungeonDB.instance.GetRoom(hash);
                if (data == null) continue;

                data.m_prefab.Load();
                try
                {
                    GameObject asset = data.m_prefab.Asset;
                    if (asset == null) continue;

                    foreach (ZNetView view in asset.GetComponentsInChildren<ZNetView>(true))
                        names.Add(Utils.GetPrefabName(view.gameObject).GetStableHashCode());
                }
                finally
                {
                    data.m_prefab.Release();
                }
            }

            foreach (DungeonGenerator.DoorDef door in generator.m_doorTypes)
            {
                if (door == null || door.m_prefab == null) continue;
                names.Add(Utils.GetPrefabName(door.m_prefab).GetStableHashCode());
            }

            return names;
        }

        private static bool Protected(ZNetView view, ZDO zdo)
        {
            if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L) return true;
            if (zdo.GetBool(ZDOVars.s_tamed)) return true;

            if (view.TryGetComponent(out ItemDrop _)) return true;
            if (view.TryGetComponent(out Player _)) return true;
            if (view.TryGetComponent(out DungeonGenerator _)) return true;

            return false;
        }

        private static List<Target> Collect(DungeonGenerator generator, HashSet<int> allowed,
                                            ref Outcome outcome)
        {
            var targets = new List<Target>();
            var taken = new HashSet<ZDOID>();
            var counts = new Dictionary<string, int>();

            foreach (ZNetView view in UnityEngine.Object.FindObjectsOfType<ZNetView>())
            {
                if (view == null || !view.IsValid()) continue;

                ZDO zdo = view.GetZDO();
                if (!allowed.Contains(zdo.GetPrefab())) continue;
                if (!Dungeons.Inside(generator, view.transform.position)) continue;

                if (Protected(view, zdo))
                {
                    outcome.Kept++;
                    continue;
                }

                if (!taken.Add(zdo.m_uid)) continue;

                string prefab = Utils.GetPrefabName(view.gameObject);
                targets.Add(new Target { Zdo = zdo, View = view, Prefab = prefab });
                Count(counts, prefab);
            }

            int roomObjects = targets.Count;

            for (int i = 0; i < roomObjects; i++)
            {
                ZDO spawner = targets[i].Zdo;
                if (spawner.GetConnectionType() != ZDOExtraData.ConnectionType.Spawned) continue;

                ZDOID spawnedId = spawner.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                if (spawnedId.IsNone()) continue;

                ZDO creature = ZDOMan.instance.GetZDO(spawnedId);
                if (creature == null || taken.Contains(creature.m_uid)) continue;
                if (creature.GetBool(ZDOVars.s_tamed)) { outcome.Kept++; continue; }
                if (creature.GetLong(ZDOVars.s_creator, 0L) != 0L) { outcome.Kept++; continue; }

                ZNetView view = ZNetScene.instance.FindInstance(creature);
                if (view != null && view.TryGetComponent(out Player _)) continue;

                taken.Add(creature.m_uid);
                targets.Add(new Target { Zdo = creature, View = view, Prefab = "(creature)" });
                outcome.Creatures++;
            }

            if (DvalaConfig.Verbose.Value)
            {
                foreach (KeyValuePair<string, int> pair in counts)
                    DvalaPlugin.Log.LogInfo("  new dungeon: " + pair.Value + " x " + pair.Key);
            }

            outcome.Removed += targets.Count;
            return targets;
        }

        private static void Count(Dictionary<string, int> counts, string name)
        {
            int have;
            counts.TryGetValue(name, out have);
            counts[name] = have + 1;
        }

        /// <summary>
        /// Ownership first, then the destroy. ZNetScene.Destroy only calls DestroyZDO for a ZDO
        /// this machine owns, and DestroyZDO ignores a non owner without a word, so skipping the
        /// claim leaves every object that another peer had last touched standing.
        /// </summary>
        private static void Remove(List<Target> targets)
        {
            long session = ZDOMan.GetSessionID();

            foreach (Target target in targets)
            {
                target.Zdo.SetOwner(session);

                if (target.View != null && target.View.GetZDO() != null)
                    ZNetScene.instance.Destroy(target.View.gameObject);
                else
                    ZDOMan.instance.DestroyZDO(target.Zdo);
            }
        }

        /// <summary>
        /// The generate call with the two things SpawnLocation does around it: the restored
        /// original position, and WearNTear's random initial damage set from the location for
        /// the length of the call and cleared after it, as vanilla clears it.
        /// </summary>
        private static bool Generate(DungeonGenerator generator, int seed, Vector3 original,
                                     bool damage)
        {
            generator.m_originalPosition = original;
            WearNTear.m_randomInitialDamage = damage;

            try
            {
                generator.Generate(seed, ZoneSystem.SpawnMode.Full);
                return true;
            }
            catch (Exception error)
            {
                DvalaPlugin.Log.LogError("Generate threw for " + Dungeons.Describe(generator)
                                         + " with seed " + seed + ": " + error);
                return false;
            }
            finally
            {
                WearNTear.m_randomInitialDamage = false;
            }
        }

        /// <summary>
        /// Whether the layout that came out is worth keeping. Checked afterwards because there
        /// is no safe dry run: Save() runs in every mode, Ghost included, so a trial generation
        /// would overwrite the saved layout like a real one.
        ///
        /// Poor means fewer than half the old room count (and never fewer than two), a saved
        /// room count that disagrees with the shells, or a generator that asks for required
        /// rooms and did not get them.
        /// </summary>
        private static bool Poor(DungeonGenerator generator, int before, out int rooms)
        {
            Room[] shells = generator.GetComponentsInChildren<Room>();
            rooms = shells.Length;

            ZNetView nview = generator.GetComponent<ZNetView>();
            byte[] data;
            if (nview == null || !nview.IsValid()
                || !nview.GetZDO().GetByteArray(ZDOVars.s_roomData, out data) || data.Length < 4)
            {
                return true;
            }

            if (BitConverter.ToInt32(data, 0) != rooms) return true;
            if (rooms < Mathf.Max(2, (before + 1) / 2)) return true;

            if (generator.m_minRequiredRooms > 0 && generator.m_requiredRooms.Count > 0)
            {
                int have = 0;
                foreach (Room shell in shells)
                    if (generator.m_requiredRooms.Contains(shell.gameObject.name)) have++;

                if (have < generator.m_minRequiredRooms) return true;
            }

            return false;
        }
    }
}
