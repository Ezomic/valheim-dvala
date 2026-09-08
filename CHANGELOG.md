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

### Known gaps

- A vein mined out completely does not come back. `MineRock5` destroys itself once every hit
  area is dead and the server tombstones the id, so only regeneration could restore it. The
  fix is known and not taken: a postfix on `AllDestroyed` holding the object back would turn
  it into a flagged state like every other, at the cost of leaving invisible husks in the
  world forever. That is a gameplay decision, not a bug.
- Pickables with no respawn time and no hidden child, and `PickableItem` pedestals, are
  destroyed on pick and cannot be restored either.
