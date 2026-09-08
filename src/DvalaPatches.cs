using HarmonyLib;

namespace Dvala
{
    /// <summary>
    /// The mod's Harmony patches. One class named in the plugin's PatchAll, so nothing goes
    /// live by being written.
    ///
    /// Two rules that this file exists to hold in view.
    ///
    /// Ride vanilla systems rather than hand-rolling them. The suite's mods do their work by
    /// reading the game's own tables - Smelter.m_conversion, the Hammer's piece table - and
    /// by going through Player.PlacePiece so validity stays the game's problem. Keeping new
    /// features on that seam is what makes them survive a game update; a custom subclass or
    /// a patch on movement trades that away.
    ///
    /// Never guess an API. Read it, with
    /// <c>ilspycmd -t &lt;Type&gt; -r "&lt;ManagedDir&gt;" "&lt;ManagedDir&gt;\assembly_valheim.dll"</c>,
    /// or take the numbers off a devkit rip. A wrong method name is a Harmony patch that
    /// throws once at load and then quietly never runs.
    /// </summary>
    internal static class DvalaPatches
    {
        /// <summary>
        /// A patch that does nothing, kept so the wiring is proved rather than assumed. It
        /// is the first thing to check when a mod loads and appears to do nothing at all: if
        /// this line is absent from the log, the problem is the patch not applying, not the
        /// logic behind it.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static void OnSpawned(Player __instance)
        {
            // Every player object in the scene runs this, not only yours. Anything meant for
            // the person at the keyboard needs this line.
            if (__instance != Player.m_localPlayer) return;
            if (!DvalaConfig.Enabled.Value || !DvalaConfig.Verbose.Value) return;

            DvalaPlugin.Log.LogInfo("Player spawned - patches are live.");
        }

        // Traps worth having in front of you while writing the real ones. All of these were
        // paid for once already:
        //
        //   Character.OnDeath runs on the OWNING CLIENT ONLY. Its own !IsOwner() early
        //   return is dead code, so the block above it looks like it runs everywhere and
        //   does not. Anything per-player at a kill has to be done by the owner for
        //   everybody, e.g. through Player.GetPlayersInRange.
        //
        //   SEMan.Internal_AddStatusEffect refreshes an already-running effect in place and
        //   returns without reaching the public AddStatusEffect overload. Patching only the
        //   public one misses every refresh.
        //
        //   Player.ConsumeItem removes the item whatever EatFood returned. Refuse food in
        //   CanConsumeItem, which is the gate that path respects; refusing later destroys it.
        //
        //   The first ObjectDB.Awake of a session fires against a stub with no items. Gate
        //   anything that reads the item database on m_items.Count > 0, and hook
        //   ObjectDB.CopyOtherDB as well - that is the path a client takes on joining a
        //   server.
        //
        //   Writing to a container or ZDO you do not own is silently discarded. Call
        //   nview.ClaimOwnership() first, which is what vanilla's Take All does.
    }
}
