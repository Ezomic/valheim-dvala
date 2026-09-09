# Changelog

Notable changes to Dvala. Format follows [Keep a Changelog](https://keepachangelog.com),
and the mod uses [semantic versioning](https://semver.org).

## [1.0.0] - 2026-09-09

First version.

A dungeon left alone for thirty in-game days fills back up: chests re-roll from their own
drop tables, pickables return, part-mined veins heal, and spawners whose creature is gone are
re-armed. Nothing is regenerated and nothing saved is destroyed.

Every mechanism in it was read out of the game's assemblies rather than guessed, and read
twice by different readers. Three of the four are now also confirmed in a running game,
singleplayer, on 2026-09-09:

- **Spawners** re-arm and their creatures come back.
- **Veins** restore whole, including a partly mined one.
- **Chests** refill when empty and are left alone when they still hold something.

**Pickables are unconfirmed.** Every run so far reports none restored, and it is not yet known
whether that means none were picked in the rooms tested or whether the pickables in question
are the class that is destroyed on pick rather than flagged.

Hildir's three - the Sealed Tower, the Howling Cavern and the Smouldering Tomb - are included
on the same timer as everything else. That changes what Lur's horn is for: it still wakes a
mini-boss on the spot, but it is no longer the only way to fight one again. `HildirRooms` off
puts it back.

`KeepVeins`, on by default, is the one setting that changes vanilla rather than restoring it.
A vein deletes itself when its last chunk dies, and nothing records that it was ever there, so
a postfix on `AllDestroyed` holds that deletion back inside dungeon interiors. What is left is
an invisible husk with no collision that the timer fills back in. Off, and a stripped vein is
gone for good.

### Known gaps

- Pickables with no respawn time and no hidden child, and `PickableItem` pedestals, are
  destroyed on pick and cannot be restored.
