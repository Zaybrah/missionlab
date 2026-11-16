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

### Triggers
A trigger waits for something and then brings in the enemies bound to it, locks or
unlocks its doors, and can open the extraction. What it can wait for:

| `"when"` | Fires when |
|---|---|
| `zone` | a player walks into the circle (`pos`, `radius`) |
| `alert` | any enemy notices the players |
| `intel` | an intel item is picked up (`item`: its name, `""` for any, `"all"` for every one) |
| `time` | `delay` seconds after the mission started |
| `after` | another trigger (`after`: its id) has fired |

Every kind takes `delay` (seconds to wait before firing), `doors` (a list like the
scenario's own, with `locked`), and `"extraction": "open"`. In the editor triggers have
their own tab; connected to the game each has a "Fire now" button.

### Mission settings
- `"extraction": { "after": 3 }` holds the extraction shut until trigger 3 has fired; the
  mission's own rule still has to be met as well.
- `"insertion": "Alpha 2"` gives every player that insertion, whatever they pick.
- `"weather": { "hour": 5.5, "rain": true, "sandstorm": false }` on maps that have
  weather. Clean House, Mexico Depot and Georgia have none, so it does nothing there.

### Gun trucks
An enemy marked `"vehicle": true` is not a person but one of the game's gun trucks,
parked at that spot facing `yaw`. It is placed when the mission starts.

### Random layouts
A random layout also gets a wave of enemies that comes once the enemy is alerted, an
ambush where players walk into the busiest building, about one in seven doors locked,
and the intel laid on a table somewhere else in the same building. `RandomExtras = false`
in the config gives positions only.

### Replay
Each mission you host is recorded to `BepInEx/MissionLab/replays` (positions twice a
second, and what happened). In the browser page pick one under "Open replay"; in the
editor open the file. It plays on the 2D map with a timeline and a list of events.
`[Replay] Enabled = false` turns recording off; `Keep` says how many files to keep.

All of the above is built from the game's class listing and has not run in the game yet.

### Danger check
With the 3D map file open, the editor tests every enemy that is there from the start
against every insertion point: is the point within the enemy's sight range, and is the
line between them free of walls? Those that can see one are marked "check" in the list
and joined to the point with a red dashed line. It counts walls as the game's colliders
have them, so it errs on the side of warning: an enemy it flags may still take a while
to notice anyone.

### Squads
Shift-click enemies, or hold Shift and drag a box, to select several. Drag one to move
all; Ctrl+C / Ctrl+V copy them to where the pointer is, Ctrl+D doubles them, Delete
removes them. The Squads box on the Enemies tab turns the selection, puts it in one
group, binds it to one trigger, and saves it as a named squad that can be placed again
on any map. Squads are kept in the browser on this computer.

### Waves
"Add waves" on the Triggers tab makes enemies that come in wave after wave: each wave is
a trigger with its own enemies, the first on a timer and every next one a set time after
the one before. A wave starts 45 to 95 m out on one side, away from insertion points,
walks to the chosen spot and stays there.

### Mission stats
The Replay tab lists how a recorded mission went: its length, when and from how far the
players were first noticed, how long the enemy was alerted, enemies down, and how far
each player walked. A row with a time jumps to that moment.

### Chance and either/or
A hand-made scenario can leave parts to chance, rolled anew each time the mission starts (or the
same every time, with `Seed` set in the config):
- **Chance.** An enemy with `"chance": 60` is there 60 times in 100. Enemies that share a `"lot"`
  share one roll: the squad is there, or none of it. In the editor: select one or several, set
  "Chance (%)".
- **Either/or.** Enemies with the same `"either"` number form a set; each belongs to an
  `"option"` (1 = A, 2 = B ...), and one option is there each mission. In the editor: "Either/or"
  in the enemy panel, for everything selected.
Enemies that lose their roll are kept: saving from the game writes them back. The log names the
roll and what came of it.

### What a trigger can do
Besides bringing enemies and working doors, a trigger can switch the lights (`"power": "off"` or
`"on"`, on maps with a breaker box), show a message on every player's screen (`"message"`, up to
120 characters) and end the mission (`"end": "success"` or `"failed"`). A mission ended as a success
this way is marked as modded first, and the game then shows its own notice and gives no
achievements. `"power": "off"` at the top of the file switches the lights off when the mission starts.
Tested in the game: lights out, and the ending as a success. Not yet: the message, the ending as failed.

### Things
A scenario can lay things on the ground for every player: `"things": [{"name": "AKM", "pos":
[x, y, z], "yaw": 90, "trigger": 0}]`. The name is the game's own name for the thing; with
`"trigger"` it is placed when that trigger fires, otherwise from the start. In the editor: the
World tab, pick a thing, choose Add thing and click the map; drag to move. With the game
connected, a thing added in the page is placed at once and one removed is taken away.
Seen to work in the game: rifles, which can be picked up and used. Intel items, mines and the
rest of the list are offered as the game names them and have not been tried.

### Sides
`"team": 4` on an enemy puts it on a second enemy side that fights the first (seen in the game:
they shoot each other); `"team": 1` puts it on the players' side (not tried). In the editor: "Side"
in the enemy panel, for everything selected.

### More trigger kinds
- `"when": "hold"` with `"hold": 45`: a player has stayed inside the zone for that many seconds.
  Leaving starts the count again. In the editor it is a zone with "and stay" set.
- `"when": "down"` with `"count": 3`: that many enemies have gone down; `0` means all of them.

### What else a scenario can say (0.21)
- **Briefing.** `"briefing": ["line", ...]`, up to four lines, each shown on every player's screen
  when the mission starts, one after another. Mission tab in the editor.
- **Trigger logic.** `"when": "all"` or `"any"` with `"of": [1, 2]` fires when all, or one, of
  those triggers have fired. `"chance": 40` on a trigger makes it part of only 40 missions in 100;
  one that sits out takes its enemies, things and actions with it ("Fire now" still works).
  `"warn": true` tells the players when one minute and when ten seconds are left of its wait.
- **Chance for doors and things.** `"chance"` on a door setting or a thing: how often it applies.
- **Players needed.** `"minPlayers": 3` on an enemy: only there with three or more players.
- **Skill and night vision** (applied in a run, by the log; the difference was not noticed in play). `"skill": "recruit"` or `"veteran"`
  scales the enemy's reaction time, inaccuracy and hit chance from what the game gave it;
  `"nvg": true` gives it night vision.
### New in 0.22
- **Countdown, told in full.** `"warn": true` on a trigger with a wait now tells the players how long
  they have a few seconds after the clock starts, again at five minutes (waits of six minutes and
  more), at one minute and, in red, at ten seconds. Each line is written to the log and the recording.
- **Tags.** `"tag": "officer"` on enemies, and `"when": "down"` with `"tag": "officer"` on a trigger:
  it fires when the enemies with that tag are down (`"count": 0` for all of them).
- **Door trigger.** `"when": "door"` with the door's `"pos"`: fires when that door is opened or broken.
- **Every player.** `"everyone": true` on a zone or hold trigger: all living players must be inside.
- **Repeat.** `"repeat": 120` lets a trigger fire again 120 seconds later, `"times": 3` caps it. A
  timer fires on the clock; other kinds wait for their condition again. Enemies bound to it that
  have gone down come back.
- **Calling enemies.** `"rally": 40` on a trigger: enemies within 40 m walk to its place (the player
  who set off a zone, the door of a door trigger, otherwise the first player).
- **Messages.** `"red": true` shows a trigger's message as a red warning. `{enemies}`, `{players}`
  and `{time}` in any message become the numbers of the moment.
- **Skill for the whole scenario.** `"skill": "recruit"` or `"veteran"` and `"nvg": true` at the top
  of the file apply to every enemy of the scenario that has no skill of its own. Mission tab.
- **Game Master: send enemies.** With the game connected: select live enemies, press "Send selected
  enemies…", click the spot. They walk there.
- **Ready-made sets** on the Triggers tab: a countdown after the intel, an alarm that calls help,
  help every two minutes, lights out on the alarm, success when the tagged enemies are down.
- **Try-out:** every door opens.

### New in 0.23
- **Messages take turns.** Two messages at the same moment used to push each other off the screen.
  Yellow ones now follow each other 4.5 seconds apart; red ones are shown at once.
- **Respawn.** `"respawn": {"after": 20, "times": 3}` at the top of the file: a player who has been
  dead for 20 seconds is brought back, at most three times per mission. `"respawn": true` on a
  trigger brings dead players back when it fires. The game may then not save the score.
- **Doors open or closed.** `"open": true` or `false` on a door: how it stands at the start. In a
  trigger's `"doors"` the same key opens or closes the door when the trigger fires, together with
  `"locked"` if wanted ("closes and locks it").
- **Hunters.** `"hunt": true` on a trigger: the enemies it brings walk to the nearest player, at
  twice their patrol speed, instead of waiting where they appear.
- **Orders land on walkable ground.** A spot by a wall or a door's hinge is moved to the nearest
  ground an enemy can stand on. Orders and calls are written to the recording with their time.

With the game connected, the Map tab has a Game Master box: a message for every player, and the
lights. In the editor the danger check now looks only at the insertion the scenario chose and
knows that enemies see 25 m at night; the mission check also flags enemies that would appear in
plain view of the zone that brings them.

### Mission check
The Mission tab lists what would go wrong before a mission is spent on it: triggers that can
never fire or do nothing, an extraction that can never open, an insertion the map does not have,
enemies off the walkable ground or in view of an insertion point, two enemies on one spot, a target
person left to chance, either/or sets with one option, door settings that fit no door. Click a
finding to go to the thing. The tab shows how many need a look.

### Fair play and other players
The studio allows the mod on two conditions (see "Permission" below), and two settings in the
config (`BepInEx/config/missionlab.director.cfg`, section `General`) go with them:
- `TellPlayers = true`: when others are in the mission, a notice on every screen a few seconds in
  says that the host runs MissionLab. The notice uses the same call as trigger messages, which has
  not been seen in the game yet.
- `FairPlay = Off`: whether missions are marked with the game's own "cheated" flag (the end screen
  then says cheating was detected, and there are no achievements). `Off` never marks,
  `ModEndedWins` marks a mission that a trigger or the Game Master ends as a success, `Always`
  marks every mission the mod sets up. The studio leaves this choice to the mod.
The plugin only acts in PvE raids the player hosts (standard, target person, find intel, destroy
asset). PvP, wave survival and any other mode are left alone.

### Try-outs
While a mission runs, the Mission tab of the browser page has a "Try-outs" box: a message on
every screen, lights out, enemies hunting the nearest player, two enemy sides, placing a thing
(landmine, weapon, intel item ...), bringing every player back, ending the mission. Each is one
call into the game whose effect is not known yet; the page shows what the game answered, and the
log and the recording keep it. See `docs/ideas.md`.

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

## Permission
In November 2026 the maker of MissionLab sent Vector Interactive a video of the mod and asked
whether a mod that only the host installs fits the mod policy. The studio's answer by email
(16 November 2026), in short: it looks good to them as long as it does not interfere with PvP
or cause griefing in PvE; a server-side mod that only the host needs is fine; and whether modded
missions carry the "cheated" flag is up to the mod.

This is permission to make and use the mod on those conditions. It is not an endorsement:
MissionLab is unofficial and is not made or endorsed by Vector Interactive. If you host with it,
do so for people who know the lobby is modded.
