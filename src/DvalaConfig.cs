using BepInEx.Configuration;

namespace Dvala
{
    /// <summary>
    /// Everything tunable, bound in one place so the .cfg reads as a document rather than as
    /// whatever order the code happened to need things in.
    ///
    /// Note the standing BepInEx trap: every entry is written to disk on first run and the
    /// saved value beats a new default in code. Changing a default here does nothing on a
    /// machine that has already run the plugin - edit
    /// <c>&lt;profile&gt;\BepInEx\config\ezomic.valheim.dvala.cfg</c> as part of the same
    /// change. When a config-driven change appears to do nothing in game, read the cfg
    /// before reading any code.
    ///
    /// The comments are documentation, not units. They are what somebody reads in the file
    /// instead of the README, so they carry the reasoning and the consequences - including
    /// the ones that will look like a bug.
    /// </summary>
    internal static class DvalaConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Days;
        internal static ConfigEntry<bool> SkipOccupied;
        internal static ConfigEntry<float> RoomPadding;
        internal static ConfigEntry<float> CheckSeconds;

        internal static ConfigEntry<bool> Crypts;
        internal static ConfigEntry<bool> Caves;
        internal static ConfigEntry<bool> Mines;
        internal static ConfigEntry<bool> Ashlands;
        internal static ConfigEntry<bool> HildirRooms;
        internal static ConfigEntry<bool> Camps;
        internal static ConfigEntry<bool> KeepVeins;

        internal static ConfigEntry<bool> Verbose;

        internal static void Bind(ConfigFile cfg)
        {
            // Every mod here has one, and it means the same thing every time: loaded, bound,
            // patched, and deciding nothing. Not "unloaded" - a plugin cannot unload itself,
            // and a switch that pretends otherwise is a lie somebody will debug.
            Enabled = cfg.Bind("Dvala", "Enabled", true,
                "Off leaves the plugin loaded and changing nothing.");

            // Thirty days is ten hours of a running world, not ten hours of your evening: a
            // Valheim day is EnvMan.m_dayLengthSec, 1200 seconds, and the clock only turns
            // while somebody has the world open. On a dedicated server it turns all night.
            Days = cfg.Bind("Dvala", "Days", 30,
                "In-game days a dungeon must be left alone before it fills back up. A day is "
                + "20 minutes of a running world, so 30 is about 10 hours of play - and on a "
                + "dedicated server the clock keeps turning while nobody is on. The count "
                + "starts when Dvala first sees the dungeon, never at day zero, so installing "
                + "this on an old world does not restock everything in it at once.");

            // The reason this is not merely polite is the loot. Restocking is not a snapshot
            // restore - a chest is refilled from its own drop table - so a reset under
            // somebody's feet does not duplicate what they took, it overwrites the room they
            // are standing in and re-arms what they just killed.
            SkipOccupied = cfg.Bind("Dvala", "SkipOccupied", true,
                "Never restock a dungeon with a player inside it. Off means a dungeon can "
                + "refill around you mid-run, which re-arms the spawners you just cleared and "
                + "puts fresh loot in chests you already emptied. It also makes the day it "
                + "happens unpredictable, since it turns on where people happen to be.");

            // Rooms are the membership test, and this is the slack on it. Small: the boxes are
            // the generator's own and they already meet at the walls.
            RoomPadding = cfg.Bind("Dvala", "RoomPadding", 1.5f,
                "Metres of slack when asking whether a point is inside a dungeon room. Only "
                + "raise it if a player standing in a doorway is treated as outside; a large "
                + "value starts catching the ground above a shallow crypt.");

            // Real seconds, not game days: this is the tick that asks the question, and asking
            // it is a handful of box tests per loaded dungeon.
            CheckSeconds = cfg.Bind("Dvala", "CheckSeconds", 30f,
                "How often to look, in real seconds. This is not how often anything resets - "
                + "that is Days. Lower costs nothing measurable and only changes how soon "
                + "after midnight a due dungeon notices.");

            // Selection is by what a player calls the place, not by Room.Theme's names. The
            // themes behind each of these are in Dungeons.Themes().
            Crypts = cfg.Bind("Contents", "Crypts", true,
                "Burial chambers, forest crypts and sunken crypts.");

            Caves = cfg.Bind("Contents", "Caves", true,
                "Troll caves and the frost caves in the mountains.");

            Mines = cfg.Bind("Contents", "Mines", true,
                "Mistlands infested mines. The Queen's own room is never included.");

            Ashlands = cfg.Bind("Contents", "Ashlands", true,
                "Ashlands ruins and fortresses.");

            // Off, and the reason is another mod rather than taste. Lur sells a horn that
            // wakes exactly these three bosses once per sounding; a dungeon that restocks on
            // its own undercuts the item somebody paid Hildir for.
            HildirRooms = cfg.Bind("Contents", "HildirRooms", false,
                "Hildir's three: the Sealed Tower, the Howling Cavern and the Smouldering "
                + "Tomb. Off, because Lur exists to wake exactly those on purpose and a timer "
                + "that does it for free takes the point out of the horn. On is supported and "
                + "the two do not fight - whichever gets there first simply finds it already "
                + "done.");

            // Off because these are not dungeons in the sense anybody means. They are surface
            // camps that happen to be built by the same generator.
            Camps = cfg.Bind("Contents", "Camps", false,
                "Fuling camps, Meadows villages and farms. These are built by the dungeon "
                + "generator but they sit on the surface where people build, so restocking "
                + "them means re-arming spawners next door to somebody's house.");

            // The one setting here that changes vanilla rather than restoring it, which is why
            // it says so out loud. A vein is a cluster of chunks; break the last one and the
            // game deletes the whole object, and nothing anywhere records that a vein was ever
            // at that spot - so unlike a chest or a spawner there is no note left for Dvala to
            // rewrite. This holds that last deletion back inside dungeon interiors only.
            KeepVeins = cfg.Bind("Contents", "KeepVeins", true,
                "Stop a fully mined vein from deleting itself, so it can be restocked later. "
                + "Without this, a vein you strip bare is gone for good and only the chests, "
                + "spawners and pickables in that room come back. What is left behind is an "
                + "invisible husk you can walk through, holding nothing, until the timer fills "
                + "it back in - and it costs the world exactly what a half-mined vein already "
                + "costs it, one saved object. Interiors only: veins on the surface are never "
                + "held back, whatever else is switched on here.");

            // Not synced by intent - see the plugin. A diagnostic flag is personal, and a
            // host turning on someone else's logging is not a thing anybody asked for.
            Verbose = cfg.Bind("Dvala", "Verbose", false,
                "Write what was found and what was changed to BepInEx/LogOutput.log. Off "
                + "unless something looks wrong; it is one line per dungeon and one per item.");
        }

        /// <summary>
        /// The Room.Theme mask the config adds up to.
        ///
        /// Built here rather than stored, because Core hands a host's values over at runtime
        /// and a mask cached at Bind time would be yesterday's answer on a server.
        /// </summary>
        internal static Room.Theme Themes()
        {
            Room.Theme mask = Room.Theme.None;

            if (Crypts.Value) mask |= Room.Theme.Crypt | Room.Theme.ForestCrypt
                                      | Room.Theme.SunkenCrypt;
            if (Caves.Value) mask |= Room.Theme.Cave;
            if (Mines.Value) mask |= Room.Theme.DvergerTown;
            if (Ashlands.Value) mask |= Room.Theme.AshlandRuins | Room.Theme.FortressRuins;

            if (HildirRooms.Value) mask |= Room.Theme.ForestCryptHildir | Room.Theme.CaveHildir
                                           | Room.Theme.PlainsFortHildir;

            if (Camps.Value) mask |= Room.Theme.GoblinCamp | Room.Theme.MeadowsVillage
                                     | Room.Theme.MeadowsFarm;

            // DvergerBoss is never in the mask and has no setting. It is the Queen's room, and
            // the Queen is a boss with a summoning ritual and a permanent global key - putting
            // her back is a different mod with a different argument, not a content toggle.
            return mask;
        }
    }
}
