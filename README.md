# Dvala

One sentence saying what the mod does, in a player's words.

Then the *why*. This file explains the design argument, not the settings - the settings
explain themselves in the config file, and repeating them here is two places to get out of
step. Say what problem this exists to solve, what the obvious alternative was, and why it
was rejected. That paragraph is the reason a stranger installs it and the reason future-you
does not undo it.

## Installing

Needs BepInEx. Nothing else. Through a mod manager it is one install. By hand, put
`Dvala.dll` in `BepInEx/plugins/Dvala/`.

Then start the game once and quit. That first run writes the config file. It does not exist
before the mod has loaded, which is the usual reason people think it is broken.

## Settings

The file is `BepInEx/config/ezomic.valheim.dvala.cfg`. Open it in any text editor. Every
setting has a comment above it, so the file explains itself.

Note that changing a default in a new version does nothing on a machine that has already run
the mod. BepInEx writes every entry on first run and the saved value wins.

## Multiplayer

Say plainly which of the three this is, because it is the question people actually ask:

- **Everyone needs it.** The server refuses a client that does not have it, at the same
  build. Anything that registers a prefab or changes item data is this.
- **The host needs it.** Clients without it are let in and are unaffected.
- **Nobody else needs it.** Purely local, purely visual.

If [Core](https://github.com/Ezomic/valheim-core) is installed, this mod registers with its
version gate and the host's settings apply to everyone connected to it, in memory only -
your own config file is never written to and comes back the moment you disconnect. Keybinds
stay yours. Without Core the mod still runs; what is lost is the enforcement.

## Licence

MIT. See `LICENSE`.
