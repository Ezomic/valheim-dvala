using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Ezomic.Core;
using UnityEngine;

namespace Dvala
{
    /// <summary>
    /// Dvala. One sentence saying what the mod does, then a paragraph saying why it is
    /// worth having - the design argument, not the feature list. That paragraph is the thing
    /// future-you reads first.
    ///
    /// Say here whether the mod is client-side, and say it in terms of where the work
    /// happens rather than by habit. "Client-side" means every effect is computed by the
    /// owning client off state it already has. The moment a decision reads another player's
    /// progress, writes a shared ZDO, or registers a prefab, it is not client-side any more
    /// and Requirement.Everyone below is load-bearing.
    ///
    /// There is deliberately no BepInProcess attribute. A dedicated server runs
    /// valheim_server.exe, and Core's gate only refuses on the server side of RPC_PeerInfo -
    /// so a mod that must be enforced has to be allowed to load there.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Soft, not hard. A hard dependency that is absent does not degrade - the plugin never
    // loads at all - and every mod here has to be installable on its own, because a stranger
    // should not need two installs to get one mod. Soft still buys the load-order guarantee
    // when Core is present, which is all that registering with the gate needs.
    [BepInDependency(CoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class DvalaPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.dvala";
        public const string PluginName = "Dvala";
        public const string PluginVersion = "0.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        /// <summary>Core's plugin GUID. Optional - see TryRegisterWithCore.</summary>
        private const string CoreGuid = "ezomic.valheim.core";

        internal static ManualLogSource Log;

        /// <summary>
        /// Whether Core answered at load. Worth keeping even when nothing reads it yet: the
        /// difference between gated and ungated is invisible to a player otherwise, and this
        /// is what a warning on spawn would be driven by.
        /// </summary>
        internal static bool CorePresent;

        /// <summary>Real seconds since the last look. Not saved, and does not need to be.</summary>
        private float _since;

        /// <summary>
        /// Set after the first exception out of the sweep, so a fault reports once instead of
        /// once a tick. A throw in Update lands in Player.log rather than in BepInEx's own
        /// log, where nobody looks first - so it is caught here and said out loud.
        /// </summary>
        private bool _faulted;

        private void Awake()
        {
            Log = Logger;

            // Config first. Registering absorbs every entry the mod has bound, so anything
            // bound after this line is carried only because Core re-absorbs at manifest
            // time - and depending on the order of two lines in an Awake is not a thing
            // worth relying on.
            DvalaConfig.Bind(Config);

            TryRegisterWithCore();

            // No Harmony, and that is worth one line rather than an empty file. Dvala patches
            // nothing: it reads dungeons the game has already loaded and writes to their own
            // ZDOs on a timer. The one thing that would need a patch is holding a fully mined
            // vein back from destroying itself, which is a decision nobody has made yet.

            // The startup line every mod in the suite writes. It is how a log answers "which
            // build of what is actually loaded" without anyone guessing.
            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        /// <summary>
        /// Joins Core's version gate when Core is installed, and does nothing when it is not.
        ///
        /// Name here exactly what standing alone costs, because it is usually not the mod.
        /// For most of these it is the *enforcement*: without Core nothing refuses a client
        /// that lacks the plugin, so the rule becomes an agreement between players rather
        /// than a property of the server. That is a real loss and a legitimate choice, and
        /// it is the server owner's to make - which is why this logs rather than refusing
        /// to run.
        /// </summary>
        private void TryRegisterWithCore()
        {
            CorePresent = Chainloader.PluginInfos.ContainsKey(CoreGuid);

            if (!CorePresent)
            {
                Log.LogInfo("Core not installed - running standalone, without the version gate.");
                return;
            }

            RegisterWithCore();
        }

        /// <summary>
        /// Kept separate and never inlined on purpose. The JIT resolves the assemblies a
        /// method needs when it first compiles that method, so a Suite call sitting directly
        /// in Awake would drag Ezomic.Core in before the check above could prevent it - and
        /// the missing-assembly exception would land during plugin load, which is the exact
        /// failure this arrangement exists to avoid. Isolating it means the type is only
        /// ever resolved on a machine that has Core.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterWithCore()
        {
            // Requirement.Everyone or Requirement.HostOnly, and the choice is not a matter of
            // taste. Everyone for anything that registers a prefab or changes item data,
            // whether it looks networked or not: a client that cannot resolve a prefab hash
            // does not fail loudly, ZNetScene discards the ZDO as junk and the thing a player
            // built is simply gone. HostOnly only when a client without the mod is genuinely
            // unaffected.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config, Requirement.Everyone);

            // Registering already absorbs the whole config file, so this is a formality now.
            // It is still worth writing: naming an entry here is saying out loud that the
            // host decides it. Keybinds are excluded by Core itself - a host taking away
            // someone's keys for the evening is the kind of sync that gets a mod uninstalled.
            Suite.Sync(DvalaConfig.Enabled);

            // If the mod reads a data file that decides what it does, hash it too. The gate
            // catches two ends on different builds; it cannot catch two ends running the
            // same build over different text unless it is told.
            //
            //     Suite.Data(File.ReadAllText(path));
        }

        /// <summary>
        /// The whole of the mod's timing. Cheap by construction: the tick reads one integer
        /// off each loaded dungeon and compares it to the day, and the expensive part - the
        /// component sweep in <see cref="Restore"/> - only runs for a dungeon that is actually
        /// due, which is once per thirty days per dungeon.
        /// </summary>
        private void Update()
        {
            if (!DvalaConfig.Enabled.Value) return;

            _since += Time.deltaTime;
            if (_since < Mathf.Max(1f, DvalaConfig.CheckSeconds.Value)) return;
            _since = 0f;

            try
            {
                Sweep();
            }
            catch (System.Exception error)
            {
                if (_faulted) return;

                _faulted = true;
                Log.LogError("Dvala's sweep threw and will keep being attempted, but this is "
                             + "reported once: " + error);
            }
        }

        private void Sweep()
        {
            // Both are absent in the main menu and for a while after a world starts loading.
            if (ZNetScene.instance == null || EnvMan.instance == null) return;

            int today = Dungeons.Today();
            if (today < 0) return;

            int due = Mathf.Max(1, DvalaConfig.Days.Value);

            foreach (DungeonGenerator generator in Dungeons.Live())
            {
                if (!Dungeons.Wanted(generator)) continue;

                int stamped = Dungeons.Stamped(generator);

                // First sight. Today, never day zero - see Dungeons for why an unstamped
                // dungeon is not thirty days overdue the moment this mod is installed.
                if (stamped < 0)
                {
                    Dungeons.Stamp(generator, today);
                    continue;
                }

                if (today - stamped < due) continue;

                // Occupied is asked last, so a dungeon somebody is standing in keeps its old
                // stamp and comes back to this the moment they leave, rather than losing its
                // turn for another thirty days.
                if (DvalaConfig.SkipOccupied.Value && Dungeons.Occupied(generator)) continue;

                int touched = Restore.Dungeon(generator);
                Dungeons.Stamp(generator, today);

                if (DvalaConfig.Verbose.Value || touched > 0)
                {
                    Log.LogInfo("Restocked " + generator.name + " after "
                                + (today - stamped) + " days: " + touched
                                + " objects put back.");
                }
            }
        }
    }
}
