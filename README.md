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
- Scenario files: put a plan (or a copied mission record) in `BepInEx/MissionLab/scenarios/`
  and set `Scenario` to its name. Edit positions, roles and patrol routes by hand; a position
  off the walkable surface becomes a fixed sentry. Sense values of 0 mean "the map's usual".
  `scene` may list several scenes of one map (`"Suburb Day, Suburb Night"`); other maps
  keep generating. Check a file before playing:
  `dotnet tests/CoreTest/bin/CoreTest.dll --check <scenario.json> <navmesh.bytes>`.
- Game Master experiment: while hosting, F6 spawns one enemy about 15 m in front of you
  through the game's own spawner. Every second press asks for an enemy type the mod
  chose (not yet verified).

## MissionLab.Recon.dll
Read-only. Writes what a loaded mission contains to `BepInEx/MissionLab/recon/`
(`<scene>.early.json` at start, `.late.json` after 45 s, `.manual.json` on F7), and
follows every enemy through the mission (`<scene>.movement.json`).

## Status
Director 0.2.2 is tested in game on Suburb Day (solo host): enemies spawn on the
generated positions and wanderers move. Scenario files, patrol routes and the F6
live spawn are tested there too (0.3.0). Other maps, co-op and mission types are
not tested yet. See `docs/findings.md` for what is known about the game's internals.

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
