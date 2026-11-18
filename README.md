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

- Config: `BepInEx/config/missionlab.director.cfg`. `[General]`: Enabled, Seed, EnemyCount, Scenes, MoveTarget,
  RandomExtras, RandomIntel, FairPlay, TellPlayers, Scenario. `[Replay]`: Enabled, Keep, DebriefMessage. `[GameMaster]`: Enabled, BlockGameKeys
  and every key. `[LiveLink]`: Enabled, Port. `[Advanced]`: UseHook. Each setting is explained in the file itself.
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
  Check a file: `dotnet run --project tests/Pure -- --read <scenario.json>` reads it the way the plugin does and says
  whether it would be refused; `dotnet run --project tests/CoreTest -c Release -- --check <scenario.json> <navmesh.bytes> [recon.json]`
  shows which enemies are off the walkable surface or on ground cut off from the rest of the map.
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

### Missions to play
Three finished missions for Clean House ship in `examples/`, each with a short briefing and clear endings:
`clean_house_quiet_raid.json` (stealth: take the phone and leave; a four-minute countdown only after an alarm),
`clean_house_escalation.json` (hold a room upstairs against timed waves) and `clean_house_game_master.json` (the
host fires events by hand; taking the phone or 20 minutes opens the way out without them). Squad size and minutes
in each file are the author's estimates: none has been playtested yet, so no measured duration exists. Each stays
finishable when its chance enemies are absent. `docs/first-edit.md` walks a first-time host through playing one,
changing an encounter, fixing a Mission check warning and saving it.

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

**Debrief** (0.26, replay `semantics` 4: an `alert` event when an enemy first leaves its idle state, a `fire` event
with the trigger's id). Under the replay, the Debrief block lists the recording's facts as copyable text: length,
setup-to-end time and result (from the run record), first recorded contact, each trigger firing with its place,
enemies down by the game's word kept apart from editor removals and unexplained disappearances, intel, Game Master
requests and action cards, players. Every number names its source event; older recordings mark their guesses, and
health, shots fired and door use are never given because they are not recorded. `GET /api/debrief/<replay file>`.
**Unused content** (Mission tab) lists triggers that never fired and enemies that never left their idle state in any
comparable run (same scenario fingerprint and same setup after the dice), as suggestions for review with the number of
runs looked at; offline it looks at one replay file. `GET /api/debrief/unused/<scenario id>`.
`[Replay] DebriefMessage = true` (experimental, off by default) shows every player three lines of debrief when a
trigger ends the mission. Whether they appear before the game's end screen has not been checked with an unmodded
client, and a mission the game ends by itself gets none: the browser page's debrief is the full one.

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

### New in 0.26.1 (fix after the run of 18 November; the fix itself has not run in the game yet)
0.26.0 ran once in the game, solo on Clean House by night: what was seen is in `docs/findings.md` and the Status table.
- **A mission ending waits for its messages.** When a trigger ended the mission, messages fired with it (and the
  ending's own message) were still waiting their turn when the end screen came, and were lost. The ending now waits
  until the last waiting message has been on screen for 2 s, at most 12 s; with nothing waiting it comes at once as
  before. Once an ending is decided no other trigger fires, so a countdown can no longer end a won mission as failed.

### New in 0.26.0 (not run in the game yet)
The growth candidates of `docs/backlog.md` (ML-021 to ML-026). Everything here is covered by `./test.sh`; what it
does in a running mission, and for a friend without the mod, still has to be seen (`docs/backlog-status.md`).
- **Sealed missions** (ML-021). `"sealed": true` keeps enemy positions, counts, rolls and coming trigger outcomes off
  the Play view, the maps, the inspector and the catalogue, so the host can discover the mission with the squad.
  Opening it in Create, or after the run, asks first. A reload or another tab is sealed again. This is spoiler
  avoidance only: the files stay readable. Example: `examples/clean_house_sealed.json`.
- **Host options** (ML-022). An author offers a few named choices (garrison size, reinforcement waves, skill); each
  drops enemy lots or triggers or changes a mission rule. The same seed rolls the same dice whatever is picked; the
  picks are in the run record and are used again by "Reproduce this setup". "Check every combination" tries each
  choice, squad size and a number of rolls and names any combination that cannot be finished (sampled, never a
  proof of every roll). A **quick mission** form generates a layout on the game's own map with a chosen size, mix
  and extras, to preview, reroll and keep.
- **Scatter** (ML-023). `"scatter": 6` lets an enemy stand somewhere within 6 m of its spot, on the same floor, on the
  same connected ground, inside an "allow" placement area and outside "empty" ones, away from other enemies and
  insertions; otherwise it stays (or, if the author says so, sits the mission out; never a target or a gun truck).
  The editor previews the eligible ground; "Try seeds" resolves many rolls through the plugin and lists every
  counterexample. A Game Master save keeps the authored spots, options and areas, not this run's roll.
- **Debrief** (ML-024) of a recording: time, first contact, trigger milestones, deaths the game confirmed apart from
  edits and unknown disappearances, Game Master interventions, each with where the number came from. "Unused
  content" lists triggers that never fired and enemies that never moved across comparable runs, as suggestions.
  `[Replay] DebriefMessage` (experimental, off) shows three lines in the game when a trigger ends the mission.
  Recordings are now `semantics` 4 (an `alert` event per enemy, a `fire` event per trigger).
- **Game Master budget and action cards** (ML-025). `"director"` gives the Game Master points, a refill rate and
  actions (fire a trigger, at a cost, with a rest time, only when no player is close or looking). The Direct pane
  shows them as cards with the reason one cannot be played; a retry never pays twice and a cancelled or failed action
  is refunded. The host can switch the budget rules off for the session; every spend and refund is recorded.
  The "looking" test is a view cone and a ray, not a proven visibility check.
- **Scenario packs** (ML-026). Export the scenario and chosen modules as one `.mlpack.json` with a manifest of files,
  map, mode, version and features; import shows every file's findings, missing features, map differences and name or
  id clashes before installing a copy or a new revision. Nothing is ever overwritten, and no game data can be in a pack.
- **Saving in the game keeps the author's work.** A Game Master save of a scenario keeps its id (revision + 1),
  places, sealed flag, options, placement areas, director block, squad, minutes and map print.

### New in 0.25.0 (not run in the game yet)
The authoring milestone of `docs/backlog.md` (ML-027, ML-013 to ML-020). Everything here is covered by `./test.sh`;
what it does for players in a running mission still has to be seen in the game (`docs/backlog-status.md`).
- **Painted trigger areas** (ML-027). A zone or hold trigger can cover a painted shape instead of a circle: brush,
  eraser, rectangle and polygon, with corner handles, holes (a courtyard) and separate pieces, each on its own floor
  band, so a house can be its ground floor, its upstairs and its stairs and nothing else. Entering, holding and
  "everyone" use the shape; moving between touching pieces is not leaving. The circle stays for every existing file.
  A file with a painted area is written as `missionlab-plan/2` with the scenario under `"mission"`, so 0.23 and 0.24
  refuse it rather than reading a circle. A shape changed during a mission restarts a hold in progress and keeps what
  has fired. Checking a player against four complex areas costs about 33 µs a frame (`CoreTest --bench-areas`).
- **Play, Create and Direct** (ML-013). Play lists the scenarios in the game's folder with their briefing, the author's
  squad size and estimate, the measured duration of earlier runs (or "not measured yet") and the last run's result,
  and picks the next mission without any placement controls. Create is the editor; Direct is the running mission.
- **Draft changes** (ML-014). In Draft, changes to a running mission are collected, shown as a short list of what
  they do, and sent together with Apply; each part reports back. Revisions can be saved with a note and opened as a
  separate draft; runs played with a revision are listed with it.
- **Why a trigger waits** (ML-015). The Direct pane says for every trigger what it waits for, with the real values
  ("1 of 2 living players inside", "12 s left", "trigger 4 sat this mission out"), whether its actions were carried
  out, failed or only asked for, and highlights what it depends on and what it brings.
- **Rehearsal** (ML-016). Write down events (the phone is taken at 0:30, an alarm at 1:00) and what should follow,
  run them through the plugin's own trigger rules, and keep them as tests beside the scenario. It checks the rules
  only: spotting, paths, physics, the network and fights are not predicted.
- **Repairs** (ML-017) for a few Mission check findings: put a thing back on walkable ground, move enemies standing
  on one spot apart, renumber a trigger number used twice. Each is shown before it is applied and is one Undo step.
- **Named places** (ML-018) such as "Rear stairs", a point or a painted area with its floor; their names appear in
  the trigger panel, Mission check and replays. A scenario remembers which map file it was made on
  (`"mapPrint"`), and asks you to check its places when that file changed.
- **Encounter modules** (ML-019): save enemies with their triggers as a module and insert copies elsewhere, turned
  and moved, with fresh numbers; doors and other outside links must be bound first. Two examples in `examples/modules/`.
- **Three missions** (ML-020) for Clean House and a first-edit walkthrough (`docs/first-edit.md`), not playtested yet.
- **About this mission:** a stable `"id"`, a `"revision"` counted on every save, the author's `"squad"` range and
  `"minutes"` estimate.

### New in 0.24.3 (not run in the game yet)
The rest of v0.24 in `docs/backlog.md` that can be done without the game (ML-006, ML-009, ML-010, ML-011 preparation, ML-012).
- **Each part of a change has its own result.** A change from the page that moves three enemies and adds one says
  per part whether it was applied, failed, is waiting for the game to make the enemy, or was only asked for
  ("unconfirmed"). The page marks a failed or waiting enemy in the list, follows a change that was still running when
  the page stopped waiting, and then takes the game's own list again. `GET /api/command/<id>` gives the parts after a reload.
- **Recordings know who is who** (replay `semantics` 3). Every enemy row carries its scenario number (`s4`), the
  Game Master marker (`m12`) or the gun truck (`v1`); a moved enemy keeps its name. "Down" is written only when the
  game says the enemy is dead, "gone" when it left for a reason the mod does not know, and the recording says why it
  ended. The header names the session, the run record and the scenario fingerprint. Older recordings still open, with
  a note that they link enemies by a guess.
- **Is everything ready?** compares the page's version with the plugin's and says how to put it right; it shows the
  game mode, the map data and whether this map and mode are tested. "Copy a report" can add the scenario on the page
  and the last run's resolved plan, lists what it holds, and takes out folders and player names.
- **Safer defaults.** Moved intel (new setting `RandomIntel`) and a moved target person (`MoveTarget`) are off by
  default until they have been seen working for a player without the mod. Existing config files keep their values.
- **A co-op acceptance mission** (`examples/clean_house_acceptance.json`) and a record to fill in with a friend
  (`docs/coop-record.md`). The Status section below is now a table of what is tested, experimental, unavailable or untested.
- **Builds on Windows too:** `python3 tools/build.py --refs <OPERATOR folder>` (or `./build.sh`) finds the compiler
  itself and checks the game references before it starts.
- **The editor's tests are in the repository** and run with `./test.sh` and on GitHub, without game files. The page is
  put together from `tools/editor/src` by `tools/editor/build.py`; the result is the same single page.

### New in 0.24.1
Fixes after the run of 17 November, and the next tickets of the backlog (ML-007, ML-008, ML-009, ML-012).
- **Doors that stand open.** Telling a door it is "not closed" only unlatches it. The mod now swings it, trying four ways
  one after the other, and the log says which way did it. A door asked to stand open is unlocked first.
- **Orders are followed up.** A called enemy and a hunter stood still in that run. The mod now keeps at each ordered
  enemy once a second until it has arrived, and falls back on the game's own call for backup. The log says how far each
  enemy got, and by which way.
- **The file format is a contract.** A scenario with a wrong type, a number used twice, a link to nothing, an unknown
  kind or a newer format is not used, and is named with its place in the file, for example `triggers[2].after`. The
  editor refuses the same files for the same reasons. `docs/scenario.schema.json` describes the format.
- **A record of each run** in `BepInEx/MissionLab/runs`: scenario fingerprint, roll, players, what the dice left out, the
  scenario as played. No player names, no folders. The page can repeat an earlier run ("Dice").
- **Recordings tell "down" from "taken out".** An enemy the mod removed or made again after an edit is no longer counted
  as down. What the Game Master asked for is recorded apart from what happened.
- **Is everything ready?** on the page: plugin and game version, host or not, map data, the 3D library, a blocked port,
  a paused part. "Copy a report" gives a short text without names or folders.
- **3D without the internet:** the page's 3D library now sits beside the page.
- **A release ZIP:** `python3 tools/package.py --check` builds `dist/MissionLab-<version>.zip` and checks what is in it.

### New in 0.24 (run in the game on 17 November: refusal, shared wave, trigger engine)
The first part of `docs/backlog.md`; `docs/backlog-status.md` says where each ticket stands.
- **A scenario that cannot be finished is refused.** After the dice have fallen the plugin checks that the
  extraction can still open and that the scenario's own target person is still there. If not, nothing of the
  mission is changed, the game plays its own mission, and the log and the page say why, with the roll and the
  squad size. The Mission check warns about this while you write the scenario.
- **Respawn is a shared reinforcement wave.** `"respawn": {"after": 20, "times": 2}`: 20 seconds after the first
  death everyone who is dead comes back together. A wave counts when someone is seen to be back; a wave nobody saw
  is not counted and is tried again. The game's own respawn setting is put back afterwards.
- **Triggers remember.** "after", "all" and "any" ask whether another trigger has ever fired; a repeat no longer takes
  that back. Triggers that wait for each other in a circle are named. The order of the list does not matter.
- **Calls and orders use where an enemy is now**, not where it was made, and the log says what was asked, where the
  walkable ground was and who took the order.
- **One broken part no longer stops the rest.** Each part of the mod counts its own errors, is paused and tried again;
  the page shows which part is paused and has a "Try again now" button.
- **Changes from the page are carried out once.** Each carries a number and the mission it was made for. A change the
  game did not take in time is struck off; a change for another mission is refused; the page says which it was.
- **`"gameMaster": true`** says the mission is meant to be run by a Game Master, so triggers fired by hand may be part
  of its way out.

### Tests
`./test.sh` runs the tests that need nothing of the game (scenario model, dice, trigger rules, completion check,
reinforcement waves, and the editor's own checks): the .NET 8 SDK, Python 3 and Node 18 or newer (without Node the editor
checks are skipped). `./test.sh --map <navmesh.bytes> <recon.json>` adds the checks that read one of your own map files.

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
  says that the host runs MissionLab. The notice uses the same call as trigger messages, which the
  host has seen on screen; that a player without the mod sees it has not been checked yet.
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
Every result below is from OPERATOR 0.09.5 with BepInEx 6.0.0-be.785 (IL2CPP), the only game version tried. Statuses:
**tested** = seen to work in the game on the named build, map and mode; **experimental** = built and switched on, but
the last run failed or the fix has not run in the game yet; **unavailable** = tried and the game does not do it, so the
mod does not use it; **untested** = never run in that combination. "Solo" means a modded host alone; "co-op" means a
modded host with a friend who has no mod. 0.24.1 to 0.26.0 have run once in the game (18 November, 0.26.0, solo; the rows
below name what was seen); everything else they add is covered only by `./test.sh` and the editor tests. Sources:
`docs/findings.md` (what was seen, run by run) and `docs/backlog-status.md` (each ticket). `docs/coop-record.md` is the
record to fill in for the next co-op run.

| Capability | Status | Build, map, mode | Evidence and what is still open |
|---|---|---|---|
| Client joins a modded host; scenario enemies, enemies spawned later and zone enemies appear for the client | tested | 0.13.0, Clean House, co-op | Findings, co-op 0.13.0. Nothing added after 0.13 has been seen by a client |
| Everything else in co-op on 0.14 to 0.24.3 (triggers, doors, messages, intel, extraction, waves, endings, live edits) | untested | 0.24.3, Clean House, co-op | ML-011 not started; `docs/coop-record.md` lists every check |
| Rejoin or late join; a modded player who is not the host | untested | any | ML-011 |
| Generated layouts | tested | build not recorded (before 0.15), Suburb and Mexico Depot Day, solo | Findings: enemies on every planned point; Mexico Depot 31 of 31, patrols walking |
| Generated layouts on Georgia | untested | Georgia | Only the game's own target person is recorded there; an earlier note said layouts ran, with no build or record |
| Maps other than Suburb, Mexico Depot and Georgia | untested | any | Findings, "Not yet verified" |
| Scenario files and patrol routes | tested | 0.15 to 0.24.0, Clean House, solo | Findings, runs 0.15.0 to 0.24.0; patrols walked their routes |
| The scenario picker in the lobby, the empty map | tested | build not recorded, Clean House, solo | An earlier status note only; no run in the findings records them |
| A scenario that cannot be finished is refused, the game's own mission runs | tested | 0.24.0, Clean House, solo | Two refused missions with the refusal test (ML-001) |
| Trigger engine: time with repeat and cap, zone, hold, hold by everyone, alarm, door, enemies down, tagged down, intel, all-of, after, chance, fire by hand, countdown | tested | 0.24.0 and 0.26.0, Clean House by night, solo | Findings, 17 and 18 November; repeat fired at 60, 150 and 240 s and stopped |
| Messages on screen, taking turns; briefing | tested | 0.24.0, Clean House, solo | Findings: messages took turns. Never confirmed on a client's screen |
| Shared respawn wave | tested | 0.24.0 and 0.26.0, Clean House, solo | 18 November: two waves, back after 20.2 s each; the third death, with no wave left, let the game end the mission 27 s later. Two players dead at once: untested |
| Respawn by trigger (`"respawn": true`) | tested | 0.22.0, Clean House, solo | The dead host was back within a second |
| Failed ending (red message, back to the lobby without a result screen) | tested | 0.22.0 and 0.24.0, Clean House, solo | Findings. The game's own fail call does not end a mission while a player lives: unavailable, not used |
| Success ending | tested | 0.17.0 and 0.26.0, Clean House, solo | The mission ended 2.5 to 2.7 s after the call. In 0.26.0 the messages fired with the ending were lost; 0.26.1 waits for them (not run yet) |
| Door locked or unlocked at the start and by a trigger | tested | 0.15.0 to 0.24.0, Clean House, solo | Findings: our doors hold after the game's own roll |
| Door that stands open | tested, solo | 0.26.0, Clean House by night, solo | 18 November: opened by the third way (turning it by hand) 5 s after the trigger, seen by the plugin; a client's view: untested |
| Calling enemies (`rally`), hunters, Game Master orders to fresh enemies | experimental | 0.24.1 and later, untried | 0.24.0: a call moved nobody; 0.22.0: a Game Master order moved two enemies after a fight (ML-003) |
| Moved intel (`objectives` in a scenario) | tested, solo | 0.26.0, Clean House by night, solo | 18 November: the phone was moved upstairs at the start, picked up there, and fired its trigger. How an unmodded client sees it: untested |
| Random layouts laying the intel elsewhere (`RandomIntel`) | experimental, off by default | 0.24.3 | Off until moved intel is seen on an unmodded client. The rest of `RandomExtras` (waves, ambush, locked doors) stays on |
| Target person placement (`hvt`, `MoveTarget`) | untested | any | Only the game's own target was seen, on Georgia |
| Insertion chosen by the scenario | tested | 0.15.2, Clean House, solo | The host started on Alpha 2. A client's insertion: untested |
| Placed weapons | tested | 0.19.0, Clean House by night, solo | Picked up and used |
| Placed grenades and landmines | untested | 0.17.0, Clean House, solo | Placed without error but not seen |
| Two sides, lights out, suicide bomber type, gun truck (with the plugin's fallback) | tested | 0.15.0 to 0.20.0, Clean House, solo | Findings |
| Skill and night vision | experimental | 0.21.0 and 0.22.0, Clean House, solo | Applied by the log; the difference was hardly noticed in play |
| Weather and time of day | unavailable | Suburb, Mexico Depot, Georgia | The game has no weather object on these maps |
| Noise to alert enemies (`HeardTarget`, `ShotFired`) | unavailable | 0.19.0 and 0.20.0, Clean House, solo | Nobody reacted; replaced by patrol orders |
| Game Master mode in the game and in the browser, recordings | tested | 0.15 to 0.24.0, Clean House, solo | Findings, every run |
| Notice to other players that the host runs MissionLab (`TellPlayers`) | untested | co-op | Uses the message call that works solo; never seen by a client |
| PvP, wave survival and other modes left alone | untested | any | Not tried in the game |
| Scenario contract, run records, recording identities, readiness check, release ZIP | tested, solo | 0.26.0, Clean House, solo | 18 November: readiness all OK, catalogue right, run records and semantics 4 recordings written. Run repeat, debrief: untested in the game |
| Painted trigger areas, Play/Create/Direct, inspector, sealed, options, scatter, cards, packs (0.25 and 0.26) | inspector tested, the rest untested | 0.26.0, Clean House, solo | 18 November: the inspector followed every trigger of the diagnostic. No walk through a painted area was recorded; the rest was not played |

See `docs/findings.md` for what is known about the game's internals.

## Source layout
- `src/MissionLab.Core`      pure C#: navmesh reader, seeded generator, plan model, scenario reader, trigger rules (no game references)
- `src/MissionLab.Director`  the plugin that applies a plan in game
- `src/MissionLab.Recon`     the observer plugin
- `tests/Pure`               the tests that need nothing of the game (`./test.sh`, also on GitHub)
- `tests/CoreTest`           offline checks of the generator against real map data
- `tests/fixtures`           scenario files the tests read, and the findings expected for the broken ones
- `examples`                 hand-made scenarios, shipped in the ZIP
- `tools/editor`             the scenario editor page and its 3D library
- `tools/package.py`         builds the release ZIP from `dist/`
- `tools/preview`            Python scripts that draw a plan on a map's walkable surface
- `docs/scenario.schema.json` the scenario format as a JSON Schema

Build: `python3 tools/build.py --refs <OPERATOR folder>` (or set `OPERATOR_REFS` and run `./build.sh`). It compiles with
Roslyn from the newest installed .NET SDK against the game's `dotnet`, `BepInEx/core` and `BepInEx/interop`; the game folder itself works. Built for Unity 6000.3.8f1,
BepInEx 6.0.0-be.785, game code dated 7 July 2026.

Install: unzip the release ZIP (`python3 tools/package.py` after `./build.sh`) into the OPERATOR folder. By hand: copy
both DLLs to `BepInEx/plugins/MissionLab/`, and `tools/editor/index.html` with `three.min.js` to
`BepInEx/MissionLab/web/` for the browser link. Remove: delete `BepInEx/plugins/MissionLab/`.

## Permission
In November 2026 the maker of MissionLab sent Vector Interactive a video of the mod and asked
whether a mod that only the host installs fits the mod policy. The studio's answer by email
(16 November 2026), in short: it looks good to them as long as it does not interfere with PvP
or cause griefing in PvE; a server-side mod that only the host needs is fine; and whether modded
missions carry the "cheated" flag is up to the mod.

This is permission to make and use the mod on those conditions. It is not an endorsement:
MissionLab is unofficial and is not made or endorsed by Vector Interactive. If you host with it,
do so for people who know the lobby is modded.
