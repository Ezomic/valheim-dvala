# Changelog

Notable changes to Dvala. Format follows [Keep a Changelog](https://keepachangelog.com),
and the mod uses [semantic versioning](https://semver.org).

## [1.0.4] - 2026-10-06

### Fixed

- **A gravestone stops a restock.** A due dungeon with a player's gravestone in any of its rooms
  is left alone, stamp included, and tried again on the next sweep. Spawners are no longer
  re-armed around a grave before you collect it. Always on, no setting. `dvala restock` refuses
  the same way, and Verbose logs one line per dungeon. (LHM-74)

## [1.0.3] - 2026-10-05

### Added

- **Pickups come back (LHM-67).** `Contents/Pickups`, on by default, puts back the hanging items
  and pedestal pieces of the frost caves, and any other pickable that is destroyed when taken.
  A restock could not do this before because nothing is left to reset. The list is rebuilt from
  the placed rooms' own prefabs the way the game lays a dungeon. A pickup counts as missing when
  no object of its kind is within three quarters of a metre of its spot, and only the missing
  ones are spawned, so a second pass spawns nothing and a dropped item is never touched. Only
  the owner of the dungeon does it, and while the setting is on only the owner restocks a
  dungeon at all. The restock log line now reads `pickups spawned/expected`.
- `dvala restock` in the console runs the ordinary restock on the dungeon you stand at. A Devkit
  scenario checks that a fresh dungeon spawns nothing.

### Fixed

- **A restocked crypt could keep every chest empty with no sign why (LHM-68).** The chest pass
  decided a chest was empty from the component, which fills itself from the saved record a frame
  after it appears and then once a second, so a chest reached in that window looked empty while
  holding loot. It now reads the saved record. It also no longer counts a refill as done when the
  ownership claim had not taken, when the drop table rolled nothing, or when the write did not
  reach the record. A chest that could not be refilled leaves the dungeon unstamped, so the next
  sweep retries it instead of waiting thirty days. `Verbose` logs one line per chest skipped, with
  the reason. The cause of the original report is a hypothesis until a game test shows which skip
  it was.

## [1.0.2] - 2026-09-19

### Fixed

- **Creatures now come back on a server, not only in singleplayer.** A restocked dungeon on a
  server got its chests, pickables and veins back but none of its creatures. Re-arming a
  spawner means clearing the record that it already fired, and the game never sends that
  clear to anyone else: it stayed on the one client that swept the dungeon and was lost as soon
  as that client dropped the area, logged out or handed ownership on. The day stamp had already
  synced, so the dungeon then counted as restocked for thirty days with no creatures in it. The
  sweep now also leaves a note on each spawner it re-arms, which does sync, and whichever
  client owns that spawner when it next ticks clears the record itself right before the game
  decides whether to spawn. The note is lifted the moment the spawner spawns. Dungeons already
  restocked without their creatures fill in properly at their next restock. Tested on a
  dedicated server: a cleared crypt re-armed all ten of its spawners, and its creatures were
  there after logging out and back in, which is exactly the step that lost them before.

## [1.0.1] - 2026-09-12

### Changed

- Rewritten README. Same mod, clearer documentation: what it does and how to install it come
  first, then configuration, multiplayer behaviour, compatibility and troubleshooting. Every
  config table was checked against the plugin's own Config.Bind calls, so the settings,
  sections and defaults listed are the ones actually bound. No code changed in this release.

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
