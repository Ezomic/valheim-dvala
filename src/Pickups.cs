using System.Collections.Generic;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// Putting back the pickups that are destroyed when taken: the hanging items and pedestal
    /// pieces of the frost caves, and every Pickable that has no flag to clear.
    ///
    /// <see cref="Restore"/> cannot do this and says why: taking one of these runs
    /// <c>m_nview.Destroy()</c>, so there is no record of it left to rewrite. The list of what
    /// should be there is not kept anywhere either, but it does not need to be, because the
    /// game derives it from the room prefabs every time it lays a dungeon, and so can this.
    ///
    /// <b>The walk copies <c>DungeonGenerator.PlaceRoom</c>, line for line where it matters.</b>
    /// A room's networked children are not part of the room's instance. PlaceRoom takes the
    /// ENABLED ZNetViews of the prefab asset (not the inactive ones: they are the alternatives
    /// RandomObject and RandomSpawn have switched off), works out each one's world position
    /// and rotation from the placed room's transform, and instantiates the child's own
    /// GameObject there. So this reads the same list and instantiates the same object, which is
    /// why a child's own overrides (a PickableItem whose item is set on the child, a scale) come
    /// back exactly as laid rather than as the registered prefab has them.
    ///
    /// <b>The one thing that has to be replayed is the randomness.</b> RandomObject and
    /// RandomSpawn pick their alternative from the room's seed and then, at the end of
    /// PlaceRoom, <c>Reset</c> puts the prefab asset back with everything on, so the asset alone
    /// cannot say which alternatives this dungeon got. The seed is <c>Room.m_seed</c> on the
    /// machine that generated it and the same formula PlaceRoom uses everywhere else, so it is
    /// recomputed and the picks replayed, in PlaceRoom's order, with the random state saved and
    /// restored around it.
    ///
    /// <b>What decides "missing".</b> Not the scene, which only holds what is loaded, but the
    /// ZDO list of the sector, which holds everything the client has been sent. A child counts
    /// as present when a ZDO of its prefab hash is within <see cref="Radius"/> of the spot, and
    /// each ZDO is matched once, so two berries a hand apart need two ZDOs. That makes the pass
    /// idempotent: the object it spawns is a ZDO at that spot, so the next pass finds it.
    ///
    /// <b>What it never touches.</b> It only creates. Nothing is deleted, and a dropped item or
    /// a placed piece is not a Pickable or a PickableItem, so it is never a candidate and never
    /// matched against.
    ///
    /// <b>Who runs it.</b> Only the owner of the generator's ZDO (the sweep refuses everyone
    /// else before calling this), because two machines that both found a pickup missing would
    /// both spawn it, and a spawned ZDO is not something that can be un-doubled.
    ///
    /// <b>The random item is rerolled, on purpose.</b> PickableItem rolls its item in Awake
    /// when its ZDO has none, with <c>UnityEngine.Random</c>. The spawn happens after the
    /// replay's random state is put back, so it is a fresh roll and not the same item as before.
    /// </summary>
    internal static class Pickups
    {
        private const float Radius = 0.75f;

        private struct Candidate
        {
            internal ZNetView View;
            internal int Hash;
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        internal static void Run(DungeonGenerator generator, ref Restore.Counts counts)
        {
            if (!DvalaConfig.Pickups.Value) return;
            if (DungeonDB.instance == null || ZNetScene.instance == null) return;
            if (ZDOMan.instance == null || ZoneSystem.instance == null) return;

            ZNetView owner = generator.GetComponent<ZNetView>();
            if (owner == null || !owner.IsValid() || !owner.IsOwner()) return;

            Room[] rooms = generator.GetComponentsInChildren<Room>();

            // A dungeon whose neighbours have not been instantiated yet shows an incomplete
            // sector, and an incomplete sector reads as "everything is missing".
            foreach (Room room in rooms)
            {
                if (room == null) continue;
                if (!ZNetScene.instance.IsAreaReady(room.transform.position))
                {
                    Verbose("area not ready around " + room.name + ", pickups wait for the next sweep");
                    return;
                }
            }

            var sectors = new Dictionary<Vector2s, List<ZDO>>();
            var matched = new HashSet<ZDOID>();

            foreach (Room room in rooms)
            {
                if (room == null) continue;

                DungeonDB.RoomData data = DungeonDB.instance.GetRoom(room.GetHash());
                if (data == null || data.m_prefab == null) continue;

                data.m_prefab.Load();
                try
                {
                    GameObject asset = data.m_prefab.Asset;
                    if (asset == null) continue;

                    Walk.Run(generator, room, data, asset, sectors, matched,
                             ref counts);
                }
                finally
                {
                    data.m_prefab.Release();
                }
            }
        }

        private static void Verbose(string text)
        {
            if (DvalaConfig.Verbose.Value) DvalaPlugin.Log.LogInfo("  pickups: " + text);
        }

        private static class Walk
        {
            internal static void Run(DungeonGenerator generator, Room placed,
                                     DungeonDB.RoomData data, GameObject asset,
                                     Dictionary<Vector2s, List<ZDO>> sectors,
                                     HashSet<ZDOID> matched, ref Restore.Counts counts)
            {
                Room prefabRoom = asset.GetComponent<Room>();
                if (prefabRoom == null) return;

                ZNetView[] views = Utils.GetEnabledComponentsInChildren<ZNetView>(asset);
                RandomObject[] objects = Utils.GetEnabledComponentsInChildren<RandomObject>(asset);
                RandomSpawn[] spawns = Utils.GetEnabledComponentsInChildren<RandomSpawn>(asset);

                bool any = false;
                foreach (ZNetView view in views)
                {
                    if (view.TryGetComponent(out Pickable _) || view.TryGetComponent(out PickableItem _))
                    {
                        any = true;
                        break;
                    }
                }

                if (!any) return;

                Vector3 roomPos = placed.transform.position;
                Quaternion roomRot = placed.transform.rotation;
                Vector3 prefabPos = prefabRoom.transform.position;
                Quaternion inverse = Quaternion.Inverse(prefabRoom.transform.rotation);

                int seed = Seed(generator, placed, roomPos);

                var wanted = new List<Candidate>();

                foreach (RandomSpawn spawn in spawns) spawn.Prepare();

                Random.State state = Random.state;
                try
                {
                    Random.InitState(seed);

                    foreach (RandomSpawn spawn in spawns)
                    {
                        Vector3 local = inverse * (spawn.transform.position - prefabPos);
                        spawn.Randomize(roomPos + roomRot * local, null, generator);
                    }

                    foreach (RandomObject random in objects)
                    {
                        Vector3 local = inverse * (random.transform.position - prefabPos);
                        random.Randomize(roomPos + roomRot * local, null, generator);
                    }

                    foreach (ZNetView view in views)
                    {
                        if (!view.gameObject.activeSelf) continue;
                        if (!view.TryGetComponent(out Pickable _)
                            && !view.TryGetComponent(out PickableItem _))
                        {
                            continue;
                        }

                        int hash = Utils.GetPrefabName(view.gameObject).GetStableHashCode();
                        if (ZNetScene.instance.GetPrefab(hash) == null)
                        {
                            Verbose("no registered prefab for " + view.name + ", left out");
                            continue;
                        }

                        Vector3 local = inverse * (view.transform.position - prefabPos);
                        wanted.Add(new Candidate
                        {
                            View = view,
                            Hash = hash,
                            Position = roomPos + roomRot * local,
                            Rotation = roomRot * (inverse * view.transform.rotation),
                        });
                    }
                }
                finally
                {
                    Random.state = state;
                }

                try
                {
                    foreach (Candidate candidate in wanted)
                    {
                        counts.PickupsSeen++;

                        if (Present(candidate, sectors, matched)) continue;

                        GameObject clone = Object.Instantiate(candidate.View.gameObject,
                                                              candidate.Position,
                                                              candidate.Rotation);
                        SoftReferenceableAssets.GameObjectExtentions.HoldReferenceTo(clone, data.m_prefab);

                        if (!clone.TryGetComponent(out ZNetView made) || !made.IsValid())
                        {
                            Verbose("spawning " + candidate.View.name + " made no ZDO");
                            continue;
                        }

                        matched.Add(made.GetZDO().m_uid);
                        AddToSector(sectors, made.GetZDO());
                        counts.Pickups++;
                        Verbose("put back " + candidate.View.name + " at " + candidate.Position);
                    }
                }
                finally
                {
                    foreach (RandomSpawn spawn in spawns) spawn.Reset();
                    foreach (RandomObject random in objects) random.Reset();
                    foreach (ZNetView view in views) view.gameObject.SetActive(true);
                }
            }

            private static int Seed(DungeonGenerator generator, Room placed, Vector3 pos)
            {
                if (placed.m_seed != 0) return placed.m_seed;

                Vector3 vector = pos;
                if (generator.m_useCustomInteriorTransform)
                    vector = pos - generator.transform.position;

                int seed = (int)vector.x * 4271 + (int)vector.y * 9187 + (int)vector.z * 2134;
                if (generator.m_addBaseSeedToRandomSpawn) seed += generator.GetSeed();
                return seed;
            }

            private static bool Present(Candidate candidate,
                                        Dictionary<Vector2s, List<ZDO>> sectors,
                                        HashSet<ZDOID> matched)
            {
                Vector2s sector = ZoneSystem.GetZone(candidate.Position);
                if (!sectors.TryGetValue(sector, out List<ZDO> list))
                {
                    list = new List<ZDO>();
                    ZDOMan.instance.FindSectorObjects(sector, new SimulationDistance(0, 0), list);
                    sectors[sector] = list;
                }

                float limit = Radius * Radius;
                foreach (ZDO zdo in list)
                {
                    if (zdo == null || !zdo.IsValid()) continue;
                    if (zdo.GetPrefab() != candidate.Hash) continue;
                    if (matched.Contains(zdo.m_uid)) continue;
                    if ((zdo.GetPosition() - candidate.Position).sqrMagnitude > limit) continue;

                    matched.Add(zdo.m_uid);
                    return true;
                }

                return false;
            }

            private static void AddToSector(Dictionary<Vector2s, List<ZDO>> sectors, ZDO zdo)
            {
                Vector2s sector = ZoneSystem.GetZone(zdo.GetPosition());
                if (sectors.TryGetValue(sector, out List<ZDO> list)) list.Add(zdo);
            }
        }
    }
}
