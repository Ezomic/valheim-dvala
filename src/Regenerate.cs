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
    /// repeating for that dungeon. <c>GetSeed()</c> is not used for the base, because what it
    /// returns depends on the generator's history (see <c>BaseSeed</c>).
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
    /// <b>What blocks a run.</b> Refusing to delete is not enough for things a player made,
    /// because a new layout is laid over whatever is standing there. So the run is refused
    /// outright, before anything is touched, when a placed piece, a tamed creature or a
    /// tombstone is inside the room boxes; the caller then restocks the ordinary way. A loose
    /// item does not block it.
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
        /// How many in-game days a due dungeon waits for its zone to empty before it gets the
        /// ordinary restock instead. Not saved: a restart starts the wait again, which only
        /// ever makes it longer.
        /// </summary>
        internal const int GraceDays = 3;

        private static readonly Dictionary<ZDOID, int> Waiting = new Dictionary<ZDOID, int>();

        /// <summary>
        /// Whether a due dungeon whose zone is occupied has waited long enough to be restocked
        /// the ordinary way. The first call starts the wait and answers no.
        ///
        /// Without this a zone somebody always stands in, a base beside a crypt, keeps the
        /// dungeon due and untouched forever with NewDungeon on, where without NewDungeon it
        /// would have been restocked.
        /// </summary>
        internal static bool WaitedLongEnough(DungeonGenerator generator, int today)
        {
            ZNetView nview = generator.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return false;

            ZDOID id = nview.GetZDO().m_uid;

            int since;
            if (!Waiting.TryGetValue(id, out since))
            {
                Waiting[id] = today;
                return false;
            }

            return today - since >= GraceDays;
        }

        internal static void StopWaiting(DungeonGenerator generator)
        {
            ZNetView nview = generator.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;

            Waiting.Remove(nview.GetZDO().m_uid);
        }

        /// <summary>
        /// Replaces the dungeon. The caller has checked that the zone is clear and that this
        /// machine owns the generator's ZDO; this checks the second again, because it is the
        /// only thing standing between two clients and two copies of every chest.
        ///
        /// <paramref name="expectedStamp"/> is the stamp the caller decided on. It is written
        /// here, not by the caller, after the claim: ClaimOwnership is not a lock, so the stamp
        /// and the generation counter are read back once this machine is the owner and the run
        /// stops if either moved, which is what another peer finishing the same job looks like.
        ///
        /// Nothing is deleted until every refusal that can be known in advance has been checked:
        /// a live ZDO, placed rooms, a located prefab for a custom interior, a non-empty
        /// whitelist, and nothing a player made inside the rooms. After that point the only way
        /// out is forwards: a failed layout is answered by a second seed, and a second failure
        /// by rebuilding the old layout from its own seed. Whenever the run does not end with
        /// a dungeon it is happy with, the stamp goes back to what it was, so the caller's
        /// ordinary restock (or the next sweep) deals with the dungeon.
        /// </summary>
        internal static Outcome Run(DungeonGenerator generator, int today, int expectedStamp)
        {
            var outcome = new Outcome();

            ZNetView nview = generator.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
                return Refuse(outcome, "the generator has no live ZDO");

            if (!nview.IsOwner())
                return Refuse(outcome, "another peer owns this dungeon's record, and only the owner "
                                       + "may replace it");

            if (WorldGenerator.instance == null)
                return Refuse(outcome, "the world generator is not up, so no seed can be derived");

            outcome.RoomsBefore = generator.GetComponentsInChildren<Room>().Length;
            if (outcome.RoomsBefore == 0)
                return Refuse(outcome, "no rooms are built yet, so there is nothing to tell the "
                                       + "dungeon's objects from anyone else's");

            LocationData location;
            bool haveLocation = TryLocation(generator, out location);

            Vector3 original = Vector3.zero;
            if (generator.m_useCustomInteriorTransform)
            {
                if (!haveLocation)
                    return Refuse(outcome, "this dungeon uses a custom interior transform and its "
                                           + "location prefab could not be found to read where its "
                                           + "generator belongs");

                if (location.HasInterior) original = location.GeneratorLocalPosition;
            }

            bool damage = haveLocation && location.ApplyRandomDamage;

            HashSet<int> allowed = Whitelist(generator);
            if (allowed.Count == 0)
                return Refuse(outcome, "no room prefab could be read, so nothing is known to belong "
                                       + "to this dungeon");

            ZDO zdo = nview.GetZDO();
            int gen0 = zdo.GetInt(GenKey, 0);

            string blocked;
            List<Target> targets = Collect(generator, allowed, ref outcome, true, out blocked);
            if (blocked != null) return Refuse(outcome, blocked);

            nview.ClaimOwnership();
            if (!zdo.IsOwner() || Dungeons.Stamped(generator) != expectedStamp
                || zdo.GetInt(GenKey, 0) != gen0)
            {
                return Refuse(outcome, "the dungeon's record changed while it was being claimed, so "
                                       + "another peer is replacing it");
            }

            Dungeons.Stamp(generator, today);
            if (Dungeons.Stamped(generator) != today)
            {
                Dungeons.Stamp(generator, expectedStamp);
                return Refuse(outcome, "the stamp did not stick, so this machine does not really "
                                       + "own the dungeon");
            }

            bool built;
            try
            {
                built = Replace(generator, zdo, allowed, targets, gen0, original, damage,
                                ref outcome);
            }
            catch (Exception error)
            {
                DvalaPlugin.Log.LogError("Replacing " + Dungeons.Describe(generator)
                                         + " threw: " + error);
                outcome.Reason = "threw " + error.GetType().Name;
                built = false;
            }

            Shells.Remember(generator);
            StopWaiting(generator);

            if (!built)
            {
                Dungeons.Stamp(generator, expectedStamp);
                outcome.Ok = false;
                return outcome;
            }

            outcome.Ok = true;
            return outcome;
        }

        /// <summary>
        /// Past the point of no return: remove, generate, and if the layout is poor or the
        /// generation threw, once more with the next seed. Returns true only when a layout this
        /// run chose is standing. When both generations threw, the old layout is rebuilt from
        /// its own seed (the counter before this run), which is deterministic, and the run is
        /// reported as failed so the caller restocks the dungeon the ordinary way. If even that
        /// throws the dungeon is empty, and the log says so at error level.
        /// </summary>
        private static bool Replace(DungeonGenerator generator, ZDO zdo, HashSet<int> allowed,
                                    List<Target> targets, int gen0, Vector3 original, bool damage,
                                    ref Outcome outcome)
        {
            int baseSeed = BaseSeed(generator);
            int gen = gen0;
            bool built = false;

            outcome.Rerolled = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                {
                    string ignored;
                    targets = Collect(generator, allowed, ref outcome, false, out ignored);
                }

                Remove(targets);

                gen++;
                zdo.Set(GenKey, gen);

                int seed = baseSeed + gen * SeedStep;
                built = Generate(generator, seed, original, damage);

                outcome.Gen = gen;
                outcome.Seed = seed;

                allowed.UnionWith(Whitelist(generator));

                if (built && !Poor(generator, outcome.RoomsBefore, out outcome.Rooms))
                    return true;

                if (attempt == 0)
                {
                    DvalaPlugin.Log.LogWarning("The new layout of " + Dungeons.Describe(generator)
                                               + (built ? " was poor (" + outcome.Rooms + " rooms, was "
                                                          + outcome.RoomsBefore + ")."
                                                        : " could not be generated.")
                                               + " Rolling once more.");
                    outcome.Rerolled = true;
                }
            }

            if (built)
            {
                DvalaPlugin.Log.LogWarning("The second layout of " + Dungeons.Describe(generator)
                                           + " was poor too (" + outcome.Rooms + " rooms). Keeping it.");
                return true;
            }

            DvalaPlugin.Log.LogError("Both new layouts of " + Dungeons.Describe(generator)
                                     + " failed to generate. Rebuilding the old one from its seed.");

            string unused;
            Remove(Collect(generator, allowed, ref outcome, false, out unused));

            int previous = baseSeed + gen0 * SeedStep;
            if (Generate(generator, previous, original, damage))
            {
                outcome.Seed = previous;
                Poor(generator, outcome.RoomsBefore, out outcome.Rooms);
                outcome.Reason = "generation failed twice and the old layout was rebuilt from its seed";
                DvalaPlugin.Log.LogError("The old layout of " + Dungeons.Describe(generator)
                                         + " is back (" + outcome.Rooms + " rooms, seed " + previous
                                         + "). It is not new, and it will be restocked.");
            }
            else
            {
                outcome.Reason = "generation failed twice and the old layout could not be rebuilt";
                DvalaPlugin.Log.LogError(Dungeons.Describe(generator) + " IS NOW EMPTY: the old "
                                         + "layout could not be rebuilt either. Reload the zone "
                                         + "to load whatever the world saved.");
            }

            return false;
        }

        private static Outcome Refuse(Outcome outcome, string reason)
        {
            outcome.Ok = false;
            outcome.Reason = reason;
            return outcome;
        }

        /// <summary>
        /// The seed vanilla computes for this generator's first layout, worked out here from
        /// the same formula and never by calling GetSeed().
        ///
        /// GetSeed() only hands back the seed it was last given when m_hasGeneratedSeed is
        /// already true, which is the case for a generator that has just generated and not for
        /// one rebuilt from its ZDO, where the flag starts false and it recomputes the formula
        /// and overwrites m_generatedSeed. Which of the two a caller gets depends on the
        /// generator's history, and Generate(seed, mode) writes our seed into m_generatedSeed
        /// either way. Reading the formula directly gives the same number every time. It is the
        /// first layout's seed unless a developer set m_forceSeed, which is deliberately not
        /// honoured here.
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
        /// What is needed from the location prefab, copied while the prefab is held. The asset
        /// is only guaranteed between Load and Release, so nothing here may keep a reference to
        /// it or to anything under it.
        /// </summary>
        private struct LocationData
        {
            internal bool ApplyRandomDamage;
            internal bool HasInterior;
            internal Vector3 GeneratorLocalPosition;
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
        private static bool TryLocation(DungeonGenerator generator, out LocationData data)
        {
            data = new LocationData();
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

                        data.ApplyRandomDamage = found.m_applyRandomDamage;
                        data.HasInterior = found.m_interiorTransform != null;
                        data.GeneratorLocalPosition = found.m_generator.transform.localPosition;
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

        /// <summary>
        /// What would be deleted, and, when <paramref name="guard"/> is set, whether anything a
        /// player made stands in the way. <paramref name="blocked"/> is then a sentence saying
        /// what, and null when the way is clear.
        ///
        /// The guard looks at every networked object inside the room boxes, not only the
        /// whitelist: a wall or a workbench a player built is not one of the dungeon's own
        /// prefabs, so the whitelist never sees it, and a new layout would be laid over it. A
        /// piece with a creator, a tamed creature and a tombstone all count. A loose item does
        /// not, because dropped items are left alone and a layout is allowed to bury one.
        /// </summary>
        private static List<Target> Collect(DungeonGenerator generator, HashSet<int> allowed,
                                            ref Outcome outcome, bool guard, out string blocked)
        {
            var targets = new List<Target>();
            var taken = new HashSet<ZDOID>();
            var counts = new Dictionary<string, int>();

            int pieces = 0, tamed = 0, stones = 0;
            if (guard) CountPlayerMade(generator, ref pieces, ref tamed, ref stones);

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
                if (creature.GetBool(ZDOVars.s_tamed)) { outcome.Kept++; if (guard) tamed++; continue; }
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

            blocked = null;
            if (pieces + tamed + stones > 0)
            {
                var found = new List<string>();
                if (pieces > 0) found.Add(pieces + " placed piece" + (pieces == 1 ? "" : "s"));
                if (tamed > 0) found.Add(tamed + " tamed creature" + (tamed == 1 ? "" : "s"));
                if (stones > 0) found.Add(stones + " tombstone" + (stones == 1 ? "" : "s"));

                blocked = "player-made things are inside its rooms (" + string.Join(", ", found.ToArray())
                          + "), and a new layout would bury them. Move them out, or let it be "
                          + "restocked the ordinary way";
            }

            outcome.Removed += targets.Count;
            return targets;
        }

        private static void CountPlayerMade(DungeonGenerator generator, ref int pieces,
                                            ref int tamed, ref int stones)
        {
            Room[] rooms = generator.GetComponentsInChildren<Room>();

            foreach (ZNetView view in UnityEngine.Object.FindObjectsOfType<ZNetView>())
            {
                if (view == null || !view.IsValid()) continue;

                ZDO zdo = view.GetZDO();
                bool made = zdo.GetLong(ZDOVars.s_creator, 0L) != 0L;
                bool tame = zdo.GetBool(ZDOVars.s_tamed);
                bool stone = !made && !tame && view.TryGetComponent(out TombStone _);
                if (!made && !tame && !stone) continue;

                if (view.TryGetComponent(out ItemDrop _)) continue;
                if (view.TryGetComponent(out Player _)) continue;
                if (!Dungeons.Inside(rooms, view.transform.position)) continue;

                if (tame) tamed++;
                else if (stone) stones++;
                else pieces++;
            }
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
            int failed = 0;

            foreach (Target target in targets)
            {
                try
                {
                    target.Zdo.SetOwner(session);

                    if (target.View != null && target.View.GetZDO() != null)
                        ZNetScene.instance.Destroy(target.View.gameObject);
                    else
                        ZDOMan.instance.DestroyZDO(target.Zdo);
                }
                catch (Exception error)
                {
                    if (failed++ == 0)
                    {
                        DvalaPlugin.Log.LogWarning("Removing " + target.Prefab + " threw, and the "
                                                   + "rest are still being removed: " + error.Message);
                    }
                }
            }

            if (failed > 1)
                DvalaPlugin.Log.LogWarning(failed + " objects could not be removed in all.");
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

            // Generate restores the global random state only when it gets to its last line, so
            // a throw halfway leaves every other system on the seed this one set.
            UnityEngine.Random.State state = UnityEngine.Random.state;

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
                UnityEngine.Random.state = state;
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
