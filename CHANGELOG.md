# Changelog

Notable changes to Dvala. Format follows [Keep a Changelog](https://keepachangelog.com),
and the mod uses [semantic versioning](https://semver.org).

## 0.1.0 - unreleased

First version.

A dungeon left alone for thirty in-game days fills back up: chests re-roll from their own
drop tables, pickables return, part-mined veins heal, and spawners whose creature is gone are
re-armed. Nothing is regenerated and nothing saved is destroyed.

Built and never run. Every mechanism in it was read out of the game's assemblies rather than
guessed, and read twice by different readers, but nothing here has been seen working in a
game.

`KeepVeins`, on by default, is the one setting that changes vanilla rather than restoring it.
A vein deletes itself when its last chunk dies, and nothing records that it was ever there, so
a postfix on `AllDestroyed` holds that deletion back inside dungeon interiors. What is left is
an invisible husk with no collision that the timer fills back in. Off, and a stripped vein is
gone for good.

### Known gaps

- Pickables with no respawn time and no hidden child, and `PickableItem` pedestals, are
  destroyed on pick and cannot be restored.
