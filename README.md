# Dvala

A dungeon left alone for thirty in-game days fills back up.

Valheim's dungeons are one-shot. A crypt you have cleared is a corridor with nothing in it
forever, and the map fills with them - so a server that people keep playing on slowly turns
into a landscape of empty rooms nobody has any reason to enter again. Dvala puts the contents
back on a timer and leaves everything else exactly as it was.

**It restocks. It never regenerates.** Nothing here deletes a saved object, moves a wall or
re-rolls a layout. The rooms you know stay the rooms you know, down to the corridor you got
lost in, and anything you built inside is untouched.

## What comes back

- **Chests**, refilled from their own drop table. Not a snapshot of what was in them - the
  room does not remember what it held, it remembers what kind of room it is. A mod that adds
  loot to a crypt table is therefore picked up for free.
- **Pickables**: berries, mushrooms, surtling cores, the things lying on the floor.
- **Ore veins**, healed. A vein you half-mined is whole again, and one you stripped bare
  comes back too - see below, because that one costs something.
- **Spawners**, re-armed - but only where the creature they made is provably gone. Re-arming
  a spawner whose draugr is still walking around does not replace it, it adds a second one,
  and vanilla puts no ceiling on that at all.

## What does not

Two kinds of thing are genuinely destroyed when a player takes them, rather than marked as
taken, and no mod can put those back without regenerating the room:

- Pickables with no respawn time and nothing to hide - a few one-off props.
- `PickableItem` pedestal pieces, which keep no record of themselves at all.

Everything else in a dungeon turned out to be a flag or a value on an object that is still
there, which is the reason this mod can be as careful as it is. That was worth checking: the
mod it replaces, and Lur's own readme, both assume a looted dungeon is mostly deleted and
therefore that a reset must mean regeneration. It is not, and it does not.

## The one thing it changes rather than restores

An ore vein is a cluster of chunks, each with its own health. Break the last one and the game
does not mark the vein as empty, it **deletes** it - and nothing anywhere records that a vein
was ever at that spot or which kind it was. A chest keeps its note when you empty it. A vein
does not survive being finished.

So `KeepVeins`, on by default, holds that last deletion back. What stays behind is an object
with every chunk dead: invisible, no collision, drops nothing, cannot be hit. It is a saved
object and nothing else, which is exactly what a half-mined vein already was. When the timer
comes round it fills back in like everything else.

Turn it off and the mod is purely restorative again, at the price of the obvious hole: strip a
crypt bare and only its chests, spawners and pickables come back.

**Only inside dungeons this mod manages.** A copper deposit in the open behaves exactly as it
always did; the test is room membership, so even a dungeon standing on the ground does not
claim the veins outside its own walls.

## Your own things are safe

- A container **you placed** is never touched, on two independent tests: a piece a player put
  down carries its creator, and a chest the game placed carries a drop table. Either one is
  enough to refuse. Emptying somebody's storage and filling it with crypt loot is the worst
  thing this mod could do, so it declines on the first sign.
- A dungeon with **a player inside it** is skipped and keeps its old date, so it comes back
  round the moment they leave rather than losing its turn for another thirty days.
- **Fuling camps, Meadows villages and farms are off by default.** They are built by the same
  generator as the dungeons, but they sit on the surface where people build houses.

## The clock

Thirty **in-game** days, not thirty of yours. A Valheim day is twenty minutes of a running
world, so thirty days is about ten hours of play - and on a dedicated server the clock keeps
turning while nobody is on, which is usually what you want and occasionally a surprise.

The count starts when Dvala **first sees** a dungeon, never at day zero. Installing this on a
world you have played for two years does not restock everything in it on the next tick.

## Hildir's three

The Sealed Tower, the Howling Cavern and the Smouldering Tomb are included, on the same timer
as everything else.

If you use [Lur](https://github.com/Ezomic/valheim-lur), that changes what its horn is for and
it is worth saying plainly. The horn still works and still wakes a mini-boss on the spot - what
it stops being is the *only* way to fight one again. It becomes the way to do it now rather
than in thirty days. Turn `HildirRooms` off to keep the horn as the only route.

The two never fight. Both re-arm the same spawner by clearing the same record, so whichever
gets there first simply finds the work already done.

## Installing

Needs BepInEx. Nothing else. Through a mod manager it is one install. By hand, put
`Dvala.dll` in `BepInEx/plugins/Dvala/`.

Then start the game once and quit. That first run writes the config file. It does not exist
before the mod has loaded, which is the usual reason people think it is broken.

## Settings

The file is `BepInEx/config/ezomic.valheim.dvala.cfg`. Every setting has a comment above it,
so the file explains itself.

Note that changing a default in a new version does nothing on a machine that has already run
the mod. BepInEx writes every entry on first run and the saved value wins.

## Multiplayer

**Everyone needs it**, and the reason is where the work happens. A dedicated server never
loads a dungeon's contents at all - it has no player, so it has no position to load anything
around - which means the machine that can do this is the one standing outside the door. That
is a client, and it is also the machine that owns those objects, which is what makes the
writes stick. A server-side-only version of this mod would be writing into the dark.

So each player restocks the dungeons they walk up to. The date lives on the dungeon itself and
is shared, so two players cannot restock the same crypt twice, and somebody without the mod
still sees the results.

**Untested in multiplayer.** Singleplayer is another matter: spawners, veins and chests have
all been watched working in a running game, and the three bugs that found were exactly the
kind reading cannot find - a vein came back half because the game deactivates a mined chunk's
object and the count that sizes the restore skips inactive children. Everything here was read
out of the game's own code rather than guessed, and read twice by different readers, and it
still took an evening in a crypt to get right.

If [Core](https://github.com/Ezomic/valheim-core) is installed, this mod registers with its
version gate and the host's settings apply to everyone connected to it, in memory only - your
own config file is never written to and comes back the moment you disconnect. Without Core
the mod still runs; what is lost is the enforcement, which here means players can disagree
about how long thirty days is.

## Licence

MIT. See `LICENSE`.
