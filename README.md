# MissionLab (unofficial, for OPERATOR)

Two BepInEx 6 IL2CPP plugins. Not made or endorsed by Vector Interactive.
Requires a legitimate copy of OPERATOR. Free; do not sell.

## MissionLab.Director.dll
When you host a PvE mission in a scene listed in its config, it replaces the
enemy layout with one generated from a seed. It reads the map's walkable surface
from the navmesh files the game ships (`OPERATOR_Data/NavMeshData`), places groups
of guards, wanderers and patrols on reachable, enclosed ground away from the
players' insertion points, and hands the result to the game's own spawner.
Only the host runs it. If anything fails before the swap, the mission is left
exactly as shipped.

- Config: `BepInEx/config/missionlab.director.cfg` (Enabled, Seed, EnemyCount, Scenes, Scenario, UseHook)
- Record of each mission (seed + every position): `BepInEx/MissionLab/missions/`
- Replay a mission: put its seed in the config.
- Scenario files: every `.json` in `BepInEx/MissionLab/scenarios/` that was made for the
  map being played is used instead of a generated layout (a random one if several fit).
  Maps without a scenario keep generating. Move a file into a subfolder to switch it off.
  `Scenario` in the config can also name one file, or be empty to always generate.
  The config is read again at every mission start, so no restart is needed.
- A scenario fits a mission when its `scene` lists the scene (`"Suburb Day, Suburb Night"`)
  or, with no `scene`, when its `navFile` is the map file the mission uses. A position off
  the walkable surface becomes a fixed sentry. Sense values of 0 mean "the map's usual".
  Check a file: `dotnet tests/CoreTest/bin/CoreTest.dll --check <scenario.json> <navmesh.bytes>`.
- Game Master mode (host only, F8): every enemy gets a marker drawn over the 3D view, and
  the game's own key binds are switched off while it is open. F9 switches the game's free
  camera, which flies through walls. Left click places an enemy at the crosshair; aim where
  it should look and click again. Space places one where the camera is (for spots like
  under a bed). 1/2/3 choose the role, Z the stance, T the enemy type. Right click picks an
  enemy, the mouse wheel turns it, Delete removes it, F10 saves all markers as a scenario.
  All keys are listed on screen and can be changed in the `[GameMaster]` config section.
- Scenario files may give an enemy `"prone": true` and a `"type"` (an enemy type name).
  Typed enemies are added a few seconds after the mission's own spawn.

## MissionLab.Recon.dll
Read-only. Writes what a loaded mission contains to `BepInEx/MissionLab/recon/`
(`<scene>.early.json` at start, `.late.json` after 45 s, `.manual.json` on F7), and
follows every enemy through the mission (`<scene>.movement.json`).

## Scenario editor
`tools/editor/index.html` is a single page that runs in a browser, offline. Open a map
file from `OPERATOR/OPERATOR_Data/NavMeshData` (it is read locally and never uploaded),
place enemies, set roles, draw patrol routes, and save the scenario file into
`BepInEx/MissionLab/scenarios/`. It also opens mission records from
`BepInEx/MissionLab/missions/`, so a generated mission you liked can be edited and kept,
and recon files, to show insertion points. No game files are included in this repo.

The editor has a 3D view (button "3D"): fly through the map, cut away upper floors,
click to place enemies. With only the game's map file it shows the walkable ground. For
real walls, stairs and furniture, press M in Game Master mode: the mod writes the loaded
map's solid geometry to `BepInEx/MissionLab/maps/<map>.mlgeo` on your own PC; open that
file in the editor too. The `.mlgeo` file is derived from game assets: keep it local.

### Choosing the scenario
In the lobby, a line at the bottom left shows what the next mission will use; F8 opens the
list and steps through it: any scenario made for the map, a random layout, an empty map to
build your own, or one scenario file. The browser page has the same list ("Next mission
uses"). The choice is the `Scenario` setting. `examples/` holds a hand-made scenario for
Clean House that uses zones, a patrol, a prone guard, a fixed sentry and moved intel.

### Zones
A scenario can hold zones (`"triggers"`: id, position, radius). An enemy with
`"trigger": <id>` is not on the map at the start; it appears when the first player walks
into that zone. In the editor: "Add zone", then set an enemy's "Appears" to the zone.
At least one enemy must be there from the start.

### Objectives
The mission's own intel items (for example "Cell Phone" on Clean House) show up in the
editor as amber markers when it is connected to the game, and can be dragged anywhere; in
3D onto a table or shelf. A scenario stores them as `"objectives"` (item name, position)
and they are put there the moment the mission starts.

A map holds a few copies of each item in different places ("Cell Phone_1",
"Cell Phone_2") and uses one of them per mission. An entry applies to whichever copy
is in play: `"Cell Phone_1"`, `"Cell Phone_2"` and `"Cell Phone"` all mean the phone.
Only items somebody moved are written to a scenario; the others keep the game's own
changing places. "Usual place" in the editor's item list puts an item back.

Whether a friend without the mod sees a moved item in its new place is not yet
confirmed. If they do not, leave `"objectives"` empty.

### Doors
Each of the game's doors can be locked, unlocked, or closed to enemies ("enemies cannot
open it"). The game locks some doors by chance, different ones each mission; a door a
scenario names is always as the scenario says. In the editor doors are thick lines on
the map (see-through slabs in 3D): choose Select, click one, and set it. Connected to
the game this works live, so a Game Master can lock a door behind the players.

The editor learns where the doors are from the game (connected) or from a recon file
written by Recon 0.2.2 or later. A scenario stores them as

```json
"doors": [{ "name": "Door (3)", "pos": [-12.5, 0.3, -6.0], "locked": true, "enemiesCanOpen": false }]
```

`pos` is the door's hinge; the nearest door within 0.75 m is taken. A setting that is
left out stays with the game. Not yet tested in the game.

### Enemy types on any map
Every enemy can be given one of the game's enemy types (`"type"` in a scenario, "Enemy
type" in the editor once it is connected to the game or a recon file is open). The list
is the same on every map, so types that the game itself only uses on one map, such as
the suicide bomber of the Afghan maps, can be placed anywhere. An enemy with a chosen
type appears a few seconds after the mission starts.

### Target person
In missions with a target person, an enemy marked `"hvt": true` is where the target stands;
the game spawns its own target type there at the start. A generated layout also moves the
target, to an indoor guard post (`MoveTarget` in the config switches that off).

### Live in the browser
While you host a mission, the mod serves the same editor at `http://localhost:8765` on your
own computer (nothing on the network can reach it). There it is connected to the game: the
map and the 3D geometry load by themselves, players and enemies are shown where they are
right now, and every change (place, move, turn, change role or type, delete) is carried out
in the running mission. "Save in the game" writes the scenario file. The page it serves is
`BepInEx/MissionLab/web/index.html`, a copy of `tools/editor/index.html`. Port and on/off
are in the `[LiveLink]` config section.

## Status
Tested in game as solo host on Suburb (Clean House), Mexico Depot and Georgia:
generated layouts, scenario files, patrol routes, Game Master mode in the game and in
the browser, zones, the scenario picker and the empty map.

Tested in co-op on Clean House (0.13.0, host with the mod, one friend without it): the
friend could join, saw the enemies where the host saw them, and saw the zone enemies
appear. Moved intel in co-op, target-person placement and the remaining maps are not
confirmed yet. See `docs/findings.md` for what is known about the game's internals.

## Source layout
- `src/MissionLab.Core`      pure C#: navmesh reader, seeded generator, plan model (no game references)
- `src/MissionLab.Director`  the plugin that applies a plan in game
- `src/MissionLab.Recon`     the observer plugin
- `tests/CoreTest`           offline checks of the generator against real map data
- `tools/preview`            Python scripts that draw a plan on a map's walkable surface

Build: `./build.sh` (Roslyn against `<game>/dotnet`, `BepInEx/core`, `BepInEx/interop`;
set `OPERATOR_REFS` to a folder containing those). Built for Unity 6000.3.8f1,
BepInEx 6.0.0-be.785, game code dated 7 July 2026.

Install: copy both DLLs to `BepInEx/plugins/MissionLab/`. Remove: delete that folder.
