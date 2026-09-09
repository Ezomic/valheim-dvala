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
        /// What one pass changed, split by kind.
        ///
        /// Split rather than totalled because a total cannot be debugged. The first run in
        /// game reported the same handful of objects put back on every sweep, which means
        /// either the writes are not sticking or the same objects are being found spent
        /// again - and one number cannot tell those apart, let alone say which of the four
        /// paths is the one failing.
        /// </summary>
        internal struct Counts
        {
            internal int Pickables;
            internal int Chests;
            internal int Veins;
            internal int Spawners;

            // How many of each kind were in the rooms at all, restored or not. Without this,
            // "pickables 0" has three possible meanings - none here, none picked, or the flag
            // read wrong - and the one that matters is the one it cannot say.
            internal int PickablesSeen;
            internal int ChestsSeen;
            internal int VeinsSeen;
            internal int SpawnersSeen;

            internal int Total => Pickables + Chests + Veins + Spawners;

            public override string ToString()
            {
                return "pickables " + Pickables + "/" + PickablesSeen
                       + ", chests " + Chests + "/" + ChestsSeen
                       + ", veins " + Veins + "/" + VeinsSeen
                       + ", spawners " + Spawners + "/" + SpawnersSeen;
            }
        }

        /// <summary>
        /// Restocks one dungeon and returns what it changed.
        ///
        /// All zeroes is a normal answer and does not mean failure - a dungeon nobody looted
        /// has nothing to put back.
        /// </summary>
        internal static Counts Dungeon(DungeonGenerator generator)
        {
            var counts = new Counts();

            Pickables(generator, ref counts);
            Chests(generator, ref counts);
            Veins(generator, ref counts);
            Spawners(generator, ref counts);

            return counts;
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
        private static void Pickables(DungeonGenerator generator, ref Counts counts)
        {
            foreach (Pickable pickable in UnityEngine.Object.FindObjectsOfType<Pickable>())
            {
                if (pickable == null) continue;
                if (!Dungeons.Inside(generator, pickable.transform.position)) continue;

                ZNetView nview = pickable.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                counts.PickablesSeen++;

                // Read the flag off the ZDO rather than the component's private m_picked. Same
                // answer, no reflection, and it is the value that actually persists.
                if (!nview.GetZDO().GetBool(ZDOVars.s_picked)) continue;

                nview.ClaimOwnership();
                nview.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", false);
                counts.Pickables++;
            }
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
        private static void Chests(DungeonGenerator generator, ref Counts counts)
        {
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

                Inventory inventory = container.GetInventory();
                if (inventory == null) continue;

                counts.ChestsSeen++;

                // Empty only, and the first run in game is why this test exists rather than
                // being obvious. Without it every dungeon chest was re-rolled on every sweep:
                // the same crypts reported the same two or three objects put back over and
                // over, which read as writes that were not sticking when in fact they were
                // sticking perfectly and being redone.
                //
                // It is also the honest reading of "fills back up". A chest with something in
                // it has not been emptied, and if that something is a player's - a stash left
                // in a crypt they are working through - then replacing it with a fresh roll of
                // crypt loot is the worst thing this mod could do. The creator and drop-table
                // tests above catch a chest somebody built; this catches a chest somebody is
                // using.
                //
                // And it makes the pass idempotent, which is what stops the repeat: a chest
                // this fills is no longer empty, so the next sweep walks past it.
                if (inventory.NrOfItems() > 0) continue;

                nview.ClaimOwnership();

                foreach (ItemDrop.ItemData item in container.m_defaultItems.GetDropListItems())
                    inventory.AddItem(item);

                // No explicit save. Inventory raises m_onChanged, Container.OnContainerChanged
                // marks it, and the CheckForChanges tick started in Awake writes it within a
                // second - which is the same path a player closing the lid goes through.
                counts.Chests++;
            }
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
        private static void Veins(DungeonGenerator generator, ref Counts counts)
        {

            foreach (MineRock5 rock in UnityEngine.Object.FindObjectsOfType<MineRock5>())
            {
                if (rock == null) continue;
                if (!Dungeons.Inside(generator, rock.transform.position)) continue;

                ZNetView nview = rock.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                counts.VeinsSeen++;

                // Untouched veins carry no health string at all. Skipping them keeps the write
                // count honest and costs nothing - a rewrite of an identical value is dropped
                // before it reaches the network anyway.
                if (nview.GetZDO().GetString(ZDOVars.s_health).Length == 0) continue;

                // Inactive included, and that word is the whole difference between a vein
                // that comes back and one that comes back half. MineRock5.Awake builds its
                // area list with GetComponentsInChildren<Collider>() while every chunk is
                // still active, so its list is the full set. UpdateMesh then deactivates each
                // chunk's GameObject as that chunk's health reaches zero - and the same call
                // without includeInactive skips exactly those. Counting live chunks writes a
                // shorter array than there are areas, LoadHealth fills only the first N, and
                // the dead ones are never given a value. Reported from the game as "the vein
                // only came back half", which is literally what it was.
                int areas = rock.GetComponentsInChildren<Collider>(true).Length;
                if (areas == 0) continue;

                float full = rock.m_health
                             + Game.m_worldLevel * rock.m_health
                             * Game.instance.m_worldLevelMineHPMultiplier;

                var package = new ZPackage();
                package.Write(areas);
                for (int i = 0; i < areas; i++) package.Write(full);

                string healed = Convert.ToBase64String(package.GetArray());

                // Compare before writing, and this is about the count rather than the cost -
                // an identical value is already free, because ZDO.Set only bumps DataRevision
                // when the value actually changes. But a vein that was never damaged still
                // carries a health string, so writing blindly reported eleven veins restored
                // in a crypt where one had been mined. A log that overstates what it did is
                // worse than no log: the next person debugging this believes it.
                if (nview.GetZDO().GetString(ZDOVars.s_health) == healed) continue;

                nview.ClaimOwnership();
                nview.GetZDO().Set(ZDOVars.s_health, healed);
                counts.Veins++;
            }
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
        private static void Spawners(DungeonGenerator generator, ref Counts counts)
        {

            foreach (CreatureSpawner spawner
                     in UnityEngine.Object.FindObjectsOfType<CreatureSpawner>())
            {
                if (spawner == null) continue;
                if (!Dungeons.Inside(generator, spawner.transform.position)) continue;

                ZNetView nview = spawner.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                counts.SpawnersSeen++;

                ZDO zdo = nview.GetZDO();
                if (zdo.GetConnectionType() != ZDOExtraData.ConnectionType.Spawned) continue;

                // Whatever it spawned, is it still alive? A ZDOID that no longer resolves is
                // the definition of gone; ZDOID.None resolves to null and reads the same way,
                // which is the state a used-up spawner is left in after a world load.
                ZDOID spawned = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Spawned);
                if (!spawned.IsNone() && ZDOMan.instance.GetZDO(spawned) != null) continue;

                nview.ClaimOwnership();
                zdo.SetConnection(ZDOExtraData.ConnectionType.None, ZDOID.None);
                counts.Spawners++;
            }
        }
    }
}
