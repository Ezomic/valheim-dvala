# Dvala

Valheim's dungeons are one-shot. Once you have cleared a crypt it stays an empty corridor for
the rest of the world's life. Dvala puts the contents back on a timer: a dungeon left alone
for thirty in-game days fills up again.

It restocks, it does not regenerate. Nothing here deletes a saved object, moves a wall or
re-rolls a layout. The rooms stay exactly as they are, and anything you built inside a dungeon
is left alone.

## Features

- **Chests** refill from their own drop table. It is a fresh roll, not a snapshot of what was
  in them, so a mod that adds loot to a crypt table is picked up automatically. Only empty
  chests are touched.
- **Pickables** reset to unpicked: berries, mushrooms, surtling cores, and the rest of what
  lies around on the floor.
- **Ore veins** heal back to full health, including the world level bonus.
- **Spawners** are re-armed, but only where the creature they produced is provably gone.
  Re-arming a spawner whose draugr is still walking around would add a second one, and vanilla
  puts no ceiling on that.
- Player-placed containers are never touched, and neither is anything else you built.
- Each dungeon carries its own timer, stored on the dungeon and shared with everyone in the
  world.

## How the timer works

The count is in **in-game days**, not real ones. A Valheim day is twenty minutes of a running
world, so thirty days is roughly ten hours of play. On a dedicated server the clock keeps
turning while nobody is connected, which is usually what you want and occasionally a surprise.

Counting starts the first time Dvala sees a dungeon, not at day zero. Installing this on a
world you have played for two years does not restock everything in it on the next tick; each
dungeon starts its clock when you next load it.

A dungeon with a player inside is skipped and keeps its old date, so it comes back round as
soon as they leave instead of losing its turn for another thirty days. You can turn that off
with `SkipOccupied`, but a restock under your feet re-arms the spawners you just cleared.

Membership is tested against the dungeon generator's own room boxes rather than a radius
around it. That matters for dungeons that stand on the ground, like Hildir's Sealed Tower: a
box around the generator would reach into the Fuling camp next door and claim its contents
too.

## What does not come back

Two kinds of object are genuinely destroyed when a player takes them rather than flagged as
taken, so nothing short of regenerating the room can restore them:

- Pickables with no respawn time and nothing to hide. A few one-off props fall in this class.
- `PickableItem` pedestal pieces, which keep no persistent record of themselves.

The Queen's room in an infested mine is never included and has no setting. She is a boss with
a summoning ritual and a permanent global key, which is a different argument from a content
toggle.

## Ore veins and KeepVeins

An ore vein is a cluster of chunks with a health value each. Break the last one and the game
does not mark the vein as empty, it deletes the object, and nothing anywhere records that a
vein was ever at that spot or which kind it was. A chest keeps its note when you empty it, a
vein does not survive being finished.

`KeepVeins`, on by default, holds that last deletion back. What stays behind is an object with
every chunk dead: invisible, no collision, drops nothing, cannot be hit. It costs the world one
saved object, the same as a half-mined vein already does, and the timer fills it back in like
everything else.

This is the one setting that changes vanilla behaviour rather than restoring it. Turn it off
and Dvala is purely restorative, at the price of the obvious hole: strip a crypt bare and only
its chests, spawners and pickables come back.

It applies inside managed dungeon rooms only. A copper deposit in the open behaves exactly as
it always did, whatever else you have switched on.

## Hildir's three

The Sealed Tower, the Howling Cavern and the Smouldering Tomb are included by default, on the
same timer as everything else.

If you also run [Lur](https://github.com/Ezomic/valheim-lur), this changes what its horn is
for. The horn still works and still wakes a mini-boss on the spot; what it stops being is the
only way to fight one again. It becomes the way to do it now rather than in thirty days. Set
`HildirRooms` to false to keep the horn as the only route.

The two mods do not conflict. Both re-arm the same spawner by clearing the same record, so
whichever gets there first finds the work already done.

## Installation

Requires [BepInEx 5.4.2350](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
BepInEx 5 only, not 6.

Through a mod manager it is a single install from
[Thunderstore](https://thunderstore.io/c/valheim/p/Ezomic/Dvala/). By hand, put `Dvala.dll` in
`BepInEx/plugins/Dvala/`.

Start the game once and quit before looking for the config file. BepInEx writes it on the
first run, and it does not exist before the plugin has loaded.

[Longhouse Core](https://thunderstore.io/c/valheim/p/Ezomic/Longhouse_Core/) is an optional
soft dependency. See [Multiplayer](#multiplayer) for what it adds.

## Configuration

The file is `BepInEx/config/ezomic.valheim.dvala.cfg`. Every entry has a comment above it in
the file itself.

### Dvala

| Setting | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Off leaves the plugin loaded and changing nothing. |
| `Days` | `30` | In-game days a dungeon must be left alone before it fills back up. |
| `SkipOccupied` | `true` | Never restock a dungeon with a player inside it. Off means a dungeon can refill around you mid-run. |
| `RoomPadding` | `1.5` | Metres of slack when asking whether a point is inside a dungeon room. Raise it only if a player standing in a doorway is treated as outside. A large value starts catching the ground above a shallow crypt. |
| `CheckSeconds` | `30` | How often to look, in real seconds. This is not how often anything resets, that is `Days`. It only changes how soon after midnight a due dungeon notices. |
| `Verbose` | `false` | Write what was found and what was changed to `BepInEx/LogOutput.log`. One line per dungeon and one per item. |

### Contents

| Setting | Default | What it covers |
| --- | --- | --- |
| `Crypts` | `true` | Burial chambers, forest crypts and sunken crypts. |
| `Caves` | `true` | Troll caves and mountain frost caves. |
| `Mines` | `true` | Mistlands infested mines. The Queen's own room is never included. |
| `Ashlands` | `true` | Ashlands ruins and fortresses. |
| `HildirRooms` | `true` | The Sealed Tower, the Howling Cavern and the Smouldering Tomb. |
| `Camps` | `false` | Fuling camps, Meadows villages and farms. These come out of the same generator as the dungeons but sit on the surface where people build, so restocking them means re-arming spawners next door to somebody's house. |
| `KeepVeins` | `true` | Stop a fully mined vein inside a dungeon from deleting itself, so it can be restocked later. |

Changing a default in a new version does nothing on a machine that has already run the mod.
BepInEx writes every entry on the first run and the saved value wins, so edit the `.cfg` if you
want a new default.

## Multiplayer

**Install it on every client.** The work has to happen on a machine that has the dungeon
loaded, and on a dedicated server that is never the server: with no local player it has no
position to load anything around, so it never instantiates a dungeon's contents at all. Each
player restocks the dungeons they walk up to, and that client is also the one that owns those
objects, which is what makes the writes stick.

Install the DLL on the dedicated server as well. It does no restocking there, but Core needs
it present to enforce the version check.

The last-stocked day lives on the dungeon itself and is shared, so two players cannot restock
the same crypt twice, and a player without the mod still sees the restocked contents.

With Core installed, Dvala registers as required on both sides. A client missing it, or on a
different version or build, is rejected when it connects. The host's settings are then applied
to connected clients in memory only: your own config file is not written to, and your values
come back when you disconnect.

Without Core the mod still runs. What is lost is the enforcement, which here means players can
disagree about how long thirty days is. One player with `Days` set to 5 restocks the world's
dungeons every five days for everybody.

## Status

Confirmed in single player on 2026-09-09: spawners re-arm and their creatures return, veins
restore whole including partly mined ones, and chests refill when empty and are left alone when
they still hold something.

Pickables are **unconfirmed**. Every run so far reported none restored, and it is not yet known
whether nothing was picked in the rooms tested or whether those particular pickables are the
class that is destroyed on pick rather than flagged.

Untested in multiplayer.

## Troubleshooting

**Nothing has restocked yet.** The count starts when Dvala first sees a dungeon, so on an
existing world you need `Days` in-game days from that point, not from when the dungeon was
cleared. Set `Verbose` to true and the log will say what it found.

**A dungeon restocks over and over.** Look for a `Stamp did not stick` warning in
`BepInEx/LogOutput.log`. That means the write to the dungeon's own record was discarded, which
should not happen on the client that has it loaded. Report it with the log.

**The config file is not there.** Run the game once with the mod installed. BepInEx writes it
on the first load.

**A setting has no effect.** Check the `.cfg` on disk before anything else. The saved value
beats a new default in code, and on a server the host's values override yours while you are
connected.

## Bug reports

Report in the [Discord](https://discord.gg/hJzAVaZ5wb) or on the
[issue tracker](https://github.com/Ezomic/valheim-dvala/issues). Please include:

- `BepInEx/LogOutput.log`, ideally with `Verbose` set to true.
- Whether you were in single player, hosting, or on a dedicated server.
- Your `ezomic.valheim.dvala.cfg`.
- Which kind of dungeon it was, and what you expected to come back.
- `AppData/LocalLow/IronGate/Valheim/Player.log` if a vanilla mechanic broke rather than Dvala
  itself. Exceptions thrown mid-frame land there and not in the BepInEx log.

## Discord

The [Discord](https://discord.gg/hJzAVaZ5wb) is used for mod information, updates, support,
bug reports and compatibility questions.

## Server

There is also a small EU server running the pack if you want somewhere to play. Connection
details are in the Discord.

## Licence

MIT. See `LICENSE`.

## Part of Longhouse

Dvala is part of the [Longhouse](https://thunderstore.io/c/valheim/p/Ezomic/Longhouse/)
modpack, which pins the exact versions used by the Ezomic setup. It behaves the same
installed on its own.
