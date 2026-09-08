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
        internal static ConfigEntry<float> Amount;
        internal static ConfigEntry<bool> Verbose;

        internal static void Bind(ConfigFile cfg)
        {
            // Every mod here has one, and it means the same thing every time: loaded, bound,
            // patched, and deciding nothing. Not "unloaded" - a plugin cannot unload itself,
            // and a switch that pretends otherwise is a lie somebody will debug.
            Enabled = cfg.Bind("Dvala", "Enabled", true,
                "Off leaves the plugin loaded and changing nothing.");

            // A multiplier rather than a flat number wherever there is a designed spread to
            // preserve: it keeps the gap between a bronze one and a flametal one, and it
            // lands modded content somewhere sensible instead of flattening it onto vanilla.
            Amount = cfg.Bind("Dvala", "Amount", 1f,
                "What this actually does, in the units a player thinks in. 1 = unchanged. "
                + "Say what happens at the extremes, and say which other setting beats it.");

            // Not synced by intent - see the plugin. A diagnostic flag is personal, and a
            // host turning on someone else's logging is not a thing anybody asked for.
            Verbose = cfg.Bind("Dvala", "Verbose", false,
                "Write what was found and what was changed to BepInEx/LogOutput.log. Off "
                + "unless something looks wrong; it is one line per item.");
        }
    }
}
