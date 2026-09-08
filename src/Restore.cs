using System;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// Putting a dungeon's contents back, without deleting anything.
    ///
    /// The premise this mod was started on turned out to be wrong, and the correction is the
    /// whole reason it can exist safely. Lur's readme says that most of what a dungeon loses is
    /// deleted rather than changed, and that only regenerating a room puts it back. Reading the
    /// game says otherwise: a looted chest, an unpicked-then-picked bush, a half-mined vein and
    /// a fired spawner are all still there, carrying a flag or a value that says what happened
    /// to them. Four writes put four kinds of thing back, and nothing in this file destroys a
    /// saved object.
    ///
    /// What genuinely cannot come back, and is therefore never attempted:
    ///
    ///  - A Pickable with no <c>m_hideWhenPicked</c> and no respawn time. Picking one runs
    ///    <c>m_nview.Destroy()</c>, so by the time Dvala looks the ZDO is gone. Note the trap
    ///    on the way past: writing <c>picked = true</c> onto that class of prefab is a delayed
    ///    delete, because <c>Pickable.Awake</c> claims ownership and destroys any it finds
    ///    already picked. Dvala only ever writes false.
    ///  - A <c>PickableItem</c>, which has no persistent flag at all and ends in Destroy.
    ///  - A vein that was mined out completely. <c>MineRock5</c> destroys itself once every
    ///    area is dead, and the server tombstones the id so a re-sent copy is destroyed again.
    ///    A partly mined one is entirely restorable, which covers most of what a player leaves.
    ///
    /// <b>Never regenerate.</b> <c>DungeonGenerator.Generate</c> is public and re-runnable and
    /// it is not a reset: it is purely additive with respect to content, because Clear() only
    /// destroys the room shells, whose networked children were deactivated before instantiation
    /// and never made a ZDO. Re-running it lays a second full set of chests and spawners on top
    /// of the first. It also reloads every room prefab of the theme synchronously and runs an
    /// O(rooms squared) narrow-phase collision test, in one frame, with a seed that is cached
    /// after the first call.
    ///
    /// <b>Who runs this.</b> Whichever machine has the dungeon loaded, which on a dedicated
    /// server is never the server: <c>ZNet.m_referencePosition</c> is only ever moved by a
    /// local Player, so a headless server sits at the origin and instantiates nothing. The
    /// work therefore belongs to the client that walks up to the place, and that client is
    /// also the one that owns the ZDOs, which is what makes the writes stick.
    /// </summary>
    internal static class Restore
    {
        /// <summary>
        /// Restocks one dungeon and returns how many objects were actually changed.
        ///
        /// Zero is a normal answer and does not mean failure - a dungeon nobody looted has
        /// nothing to put back.
        /// </summary>
        internal static int Dungeon(DungeonGenerator generator)
        {
            return Pickables(generator) + Chests(generator) + Veins(generator)
                   + Spawners(generator);
        }

        /// <summary>
        /// Berries, mushrooms, surtling cores, the lot.
        ///
        /// Vanilla's own respawn does exactly this call, and copying it is what buys the
        /// visual for free: a ZDO write alone would not do, because Pickable reads its picked
        /// flag once in Awake and never again, so a loaded bush stays invisible until the
        /// zone is unloaded and rebuilt. The RPC runs SetPicked on every client, and on the
        /// owner that also writes the ZDO.
        /// </summary>
        private static int Pickables(DungeonGenerator generator)
        {
            int touched = 0;

            foreach (Pickable pickable in UnityEngine.Object.FindObjectsOfType<Pickable>())
            {
                if (pickable == null) continue;
                if (!Dungeons.Inside(generator, pickable.transform.position)) continue;

                ZNetView nview = pickable.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                // Read the flag off the ZDO rather than the component's private m_picked. Same
                // answer, no reflection, and it is the value that actually persists.
                if (!nview.GetZDO().GetBool(ZDOVars.s_picked)) continue;

                nview.ClaimOwnership();
                nview.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", false);
                touched++;
            }

            return touched;
        }

        /// <summary>
        /// Chests, refilled from their own drop table rather than from a remembered list.
        ///
        /// Rolling the table again is the honest reading of "it fills back up": the room does
        /// not remember what was in it, it remembers what kind of room it is. It also means a
        /// mod that adds loot to a table is picked up for free.
        ///
        /// The clean vanilla route - clear <c>addedDefaultItems</c> and let Awake re-roll - is
        /// not available to us and the reason is worth writing down. AddDefaultItems is called
        /// from Awake and nowhere else, so it only fires when the GameObject is created from
        /// the ZDO, and it is gated on <c>IsOwner</c>. A dungeon chest is routinely ownerless
        /// while nobody is near it, so the flag can sit cleared through any number of loads
        /// with nothing re-rolling it. Doing it here, on a container that is loaded and owned,
        /// is the case that actually happens.
        /// </summary>
        private static int Chests(DungeonGenerator generator)
        {
            int touched = 0;

            foreach (Container container in UnityEngine.Object.FindObjectsOfType<Container>())
            {
                if (container == null) continue;
                if (!Dungeons.Inside(generator, container.transform.position)) continue;

                // A player's own chest, standing in a dungeon somebody moved into. Two tests
                // rather than one, because either alone is answerable: a piece a player placed
                // has a creator, and a container the game placed has a drop table. Emptying
                // somebody's storage and replacing it with crypt loot is the single worst
                // thing this mod could do, so it refuses on either signal.
                if (container.TryGetComponent(out Piece piece) && piece.GetCreator() != 0L)
                    continue;

                if (container.m_defaultItems == null
                    || container.m_defaultItems.m_drops == null
                    || container.m_defaultItems.m_drops.Count == 0)
                {
                    continue;
                }

                ZNetView nview = container.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                nview.ClaimOwnership();

                Inventory inventory = container.GetInventory();
                if (inventory == null) continue;

                inventory.RemoveAll();
                foreach (ItemDrop.ItemData item in container.m_defaultItems.GetDropListItems())
                    inventory.AddItem(item);

                // No explicit save. Inventory raises m_onChanged, Container.OnContainerChanged
                // marks it, and the CheckForChanges tick started in Awake writes it within a
                // second - which is the same path a player closing the lid goes through.
                touched++;
            }

            return touched;
        }

        /// <summary>
        /// Ore veins, healed area by area.
        ///
        /// One ZDO string holds the whole vein: a count, then a float of remaining health per
        /// hit area, base64 of a ZPackage. Writing a full-health array back is enough on its
        /// own - MineRock5 runs CheckForUpdate on a ten second repeat, notices its ZDO's
        /// DataRevision moved, and calls LoadHealth and UpdateMesh itself. So the geometry and
        /// the colliders come back on every client without an RPC, and an unloaded vein reads
        /// the same value whenever it is next created.
        ///
        /// The count is the number of colliders in the children, because that is exactly how
        /// Awake builds the area list. Full health is the same expression Awake seeds with,
        /// world level included: a vein restored to the base value would be softer than the
        /// world it is in.
        /// </summary>
        private static int Veins(DungeonGenerator generator)
        {
            int touched = 0;

            foreach (MineRock5 rock in UnityEngine.Object.FindObjectsOfType<MineRock5>())
            {
                if (rock == null) continue;
                if (!Dungeons.Inside(generator, rock.transform.position)) continue;

                ZNetView nview = rock.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                // Untouched veins carry no health string at all. Skipping them keeps the write
                // count honest and costs nothing - a rewrite of an identical value is dropped
                // before it reaches the network anyway.
                if (nview.GetZDO().GetString(ZDOVars.s_health).Length == 0) continue;

                int areas = rock.GetComponentsInChildren<Collider>().Length;
                if (areas == 0) continue;

                float full = rock.m_health
                             + Game.m_worldLevel * rock.m_health
                             * Game.instance.m_worldLevelMineHPMultiplier;

                var package = new ZPackage();
                package.Write(areas);
                for (int i = 0; i < areas; i++) package.Write(full);

                nview.ClaimOwnership();
                nview.GetZDO().Set(ZDOVars.s_health,
                                   Convert.ToBase64String(package.GetArray()));
                touched++;
            }

            return touched;
        }

        /// <summary>
        /// Spawners, re-armed by clearing the record that they fired.
        ///
        /// A spent one-shot spawner is not empty, it is connected: its ZDO carries a
        /// connection of type Spawned, and after a world load ZDOMan.ConnectSpawners rewrites
        /// that to (Spawned, ZDOID.None) rather than removing it, which is why a dungeon boss
        /// never returns on its own. Clearing the connection to None is the whole re-arm.
        ///
        /// <b>The live-creature guard is not politeness.</b> Once the connection is gone,
        /// UpdateSpawner's only liveness test is against the connection it no longer has, and
        /// Spawn() has no instance cap at all. Re-arming a spawner whose creature is still
        /// wandering the room produces a second one and orphans the first, every time the
        /// timer comes round. So a spawner is only cleared when the thing it made is provably
        /// gone.
        ///
        /// <b>And the write does not travel.</b> ZDO.Serialize writes the connection only when
        /// its type is non-zero, and Deserialize never clears one, so a connection set to None
        /// is invisible to every other machine. That is survivable here precisely because the
        /// machine doing the work is the one that has the dungeon loaded and owns the spawner:
        /// it is the machine whose UpdateSpawner will read it. It would not be survivable from
        /// a dedicated server, which is another reason the work is not done there.
        /// </summary>
        private static int Spawners(DungeonGenerator generator)
        {
            int touched = 0;

            foreach (CreatureSpawner spawner
                     in UnityEngine.Object.FindObjectsOfType<CreatureSpawner>())
            {
                if (spawner == null) continue;
                if (!Dungeons.Inside(generator, spawner.transform.position)) continue;

                ZNetView nview = spawner.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                ZDO zdo = nview.GetZDO();
                if (zdo.GetConnectionType() != ZDOExtraData.ConnectionType.Spawned) continue;

                // Whatever it spawned, is it still alive? A ZDOID that no longer resolves is
                // the definition of gone; ZDOID.None resolves to null and reads the same way,
                // which is the state a used-up spawner is left in after a world load.
                ZDOID spawned = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                if (!spawned.IsNone() && ZDOMan.instance.GetZDO(spawned) != null) continue;

                nview.ClaimOwnership();
                zdo.SetConnection(ZDOExtraData.ConnectionType.None, ZDOID.None);
                touched++;
            }

            return touched;
        }
    }
}
