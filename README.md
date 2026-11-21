# MissionLab

**An unofficial, free mission toolkit for OPERATOR.** Not made or endorsed by Vector Interactive.
Requires a legitimate copy of OPERATOR. Free; do not sell.

MissionLab changes the enemy side of a PvE mission you host. It lays the enemies out fresh from a seed, or plays a
scenario you made: enemies where you want them, triggers that bring reinforcements, lock doors, show messages or end the
mission, objectives, and an extraction held shut until the job is done. A page in your browser is the scenario editor,
and while you host it is connected to the running mission. Only the host installs it; friends join without it. It never
touches PvP.

Tried with OPERATOR 0.09.5 and BepInEx 6.0.0-be.785 (IL2CPP). Not everything has been seen to work in the game, or by a
friend without the mod: the [Status](#status) table says what has, on which build and by whom. What each version added
is in [CHANGELOG.md](CHANGELOG.md).

## Install
1. Install BepInEx 6 for IL2CPP games (build be.785) into the OPERATOR folder and start the game once, so BepInEx can
   set itself up.
2. Download `MissionLab-<version>.zip` from [Releases](https://github.com/Zaybrah/missionlab/releases) and unzip it into
   the OPERATOR folder, so that its `BepInEx` folder lands on the one that is there.
3. To play a ready-made mission, copy it from `BepInEx/MissionLab/examples/` into `BepInEx/MissionLab/scenarios/`
   ([Missions to play](#missions-to-play)).
4. Start the game and host a PvE mission. Open http://localhost:8765 in a browser on the same PC for the editor and the
   live tools; "Is everything ready?" on that page says what is missing, if anything.

New to it? [`docs/first-edit.md`](docs/first-edit.md) walks a first-time host through playing a mission, changing an
encounter, fixing a Mission check warning and playing the saved result.

**Update:** unzip the new ZIP over the old install and let it replace files. It writes only the plugins, the page, the
examples and the example modules, never your own work (`scenarios/`, `revisions/`, `runs/`, `replays/`,
`rehearsals/`, `playlists/`, `operations/`, `validations/`, `proposals/`). An example you edited where it was installed
is put back as shipped, so save changes under another name.

**Uninstall:** delete `BepInEx/plugins/MissionLab/`; the mod stops loading and everything under `BepInEx/MissionLab/`
stays for a later install. Deleting `BepInEx/MissionLab/` as well erases your scenarios, modules, revisions, runs,
replays, rehearsals, playlists, operations, validations, proposals, recon notes, maps and missions, and cannot be undone:
copy it somewhere first, or use Export pack on the page. The settings are in `BepInEx/config/missionlab.director.cfg`.

## Playing

### What happens when you host
- **Generated layouts.** In a PvE mission on a scene listed in the config, the enemy layout is replaced with one made
  from a seed. MissionLab reads the map's walkable surface from the navmesh files the game ships
  (`OPERATOR_Data/NavMeshData`), places guards, wanderers and patrols on reachable, enclosed ground away from the
  players' insertion points, and hands the result to the game's own spawner. A random layout also gets a wave that comes
  once the enemy is alerted, an ambush in the busiest building and about one door in seven locked (`RandomExtras`).
  Laying the intel elsewhere (`RandomIntel`) and moving the target person to an indoor guard post (`MoveTarget`) are off
  by default until they have been seen to work for a player without the mod.
- **Scenarios.** Every `.json` in `BepInEx/MissionLab/scenarios/` made for the map being played is used instead (a
  random one if several fit; moving a file into a subfolder switches it off). A scenario fits when its `scene` lists the
  scene (`"Suburb Day, Suburb Night"`) or, without a `scene`, when its `navFile` is the map file the mission uses.
- **The mission is never left broken.** If anything fails before the swap, or a scenario could not be finished once its
  dice are rolled, the mission is left exactly as shipped, and the log and the page say why.
- **Every mission is kept**, under `BepInEx/MissionLab/`: the layout in `missions/`, what was played and how it ended in
  `runs/` (no player names, no folders), and a recording in `replays/` (positions twice a second, and what happened).
  The page plays a recording back on the map, with a debrief whose every number names where it came from. A run can be
  repeated with exactly its setup.

Only PvE raids the player hosts are touched (standard, target person, find intel, destroy asset). PvP, wave survival
and every other mode are left alone.

### Choosing the next mission
In the lobby, a line at the bottom left shows what the next mission will use, and F8 steps through the choices: any
scenario made for the map, a random layout, an empty map to build your own, or one scenario file. The page's Play view
has the same list ("Next mission uses"), with each scenario's briefing, the author's squad size and estimate, the
measured length of earlier runs, the last result, the host options it offers, and whether what it uses has been seen to
work. The choice is the `Scenario` setting; the config is read again at every mission start, so no restart is needed.

For an evening, Play also keeps **session playlists** (each entry pins a scenario, its host options, squad size and
dice: a new roll, an exact repeat, or a retry of the last failed setup) and **operations**: two to six missions in a
chain, where the next one depends on how the last ended and which objectives were done. MissionLab does not switch the
map for you; it says which map and mode to host.

### Missions to play
Four finished missions for Clean House ship in `examples/`, each with a briefing and clear endings:
- `clean_house_quiet_raid.json`: stealth. Take the phone and leave; a four-minute countdown starts only after an alarm.
- `clean_house_escalation.json`: hold a room upstairs against timed waves.
- `clean_house_game_master.json`: the host fires events by hand; taking the phone, or 20 minutes, opens the way out
  without them.
- `clean_house_two_man_job.json`: sealed, for two. Take the intel and hold the upstairs together for a minute, or last
  six minutes after the intel; host options for garrison, skill, respawns and time limit, scattered guards, an either/or
  courier and two optional Game Master cards.

Squad size and minutes are the author's estimates; none has been playtested for balance, and only the Two-Man Job has
been played (once, with two players: 11 minutes to the extraction). Their waves walk patrol routes instead of hunting
the players, because orders to freshly spawned enemies have not yet been seen to move anyone (Status).

Three more are for trying things out: `clean_house_objectives.json` (two required objectives in a chain and an optional
one, with the extraction waiting for the required ones), `clean_house_recipe_intel_raid.json` (what the editor's
"New mission: Intel raid" recipe makes) and `clean_house_movement_probe.json` (the test of whether enemies walk where
MissionLab sends them). `examples/rehearsals/` holds saved rehearsals, `examples/modules/` two encounter modules.

### Other players and fair play
The studio allows the mod on two conditions (see [Permission](#permission)), and two settings in `[General]` go with
them:
- `TellPlayers = true`: when others are in the mission, a notice on every screen a few seconds in says that the host
  runs MissionLab. It uses the same call as trigger messages; that a player without the mod sees it has not been checked.
- `FairPlay = Off`: whether missions carry the game's own "cheated" flag (the end screen then says cheating was detected,
  and there are no achievements). `Off` never marks, `ModEndedWins` marks a mission that a trigger or the Game Master
  ends as a success, `Always` marks every mission the mod sets up. The studio leaves this choice to the mod.

Host with it for people who know the lobby is modded.

### Settings
`BepInEx/config/missionlab.director.cfg` explains each setting in the file itself:
- `[General]`: Enabled, Seed, EnemyCount, Scenes, MoveTarget, RandomExtras, RandomIntel, FairPlay, TellPlayers, Scenario
- `[Replay]`: Enabled, Keep, DebriefMessage (experimental: a three-line debrief on every screen when a trigger ends the
  mission)
- `[GameMaster]`: Enabled, BlockGameKeys and every key
- `[LiveLink]`: Enabled, Port
- `[Advanced]`: UseHook

## Making missions

### The editor
`tools/editor/index.html` (installed as `BepInEx/MissionLab/web/index.html`) is one page that runs in your browser.
Offline, open a map file from `OPERATOR_Data/NavMeshData` (it is read locally and never uploaded), place enemies, set
roles, draw patrol routes and save the scenario. It also opens mission records, so a generated mission you liked can be
edited and kept, recon files (insertion points, doors, intel) and recordings. No game files are in this repository.

While you host, the mod serves the same page at http://localhost:8765, reachable from your own computer only. Connected
to the game it has three views:
- **Play:** choose and set up the next mission (above).
- **Create:** the editor, with the map and its 3D geometry loaded by themselves; "Save in the game" writes the file.
- **Direct:** the running mission. Players and enemies are shown where they are now, every trigger says what it waits
  for ("1 of 2 living players inside", "12 s left") and has "Fire now", and objectives and Game Master cards are at hand.
  Changes go to the running mission one at a time, or are collected in Draft and applied together; each part reports
  whether it was applied, failed or only asked for. "Try-outs" holds experiments whose effect in the game is not known
  yet.

The 3D view flies through the map and cuts away upper floors. With only the map file it shows the walkable ground; for
real walls, stairs and furniture press M in Game Master mode, which writes the map's geometry to
`BepInEx/MissionLab/maps/<map>.mlgeo` on your own PC. That file is derived from game assets: keep it local.

What else the page does:
- **Mission check** (Mission tab): triggers that can never fire, an extraction that can never open, enemies off walkable
  ground or in plain view of an insertion point, two enemies on one spot, and more; some findings have a repair to
  preview and apply. The **danger check** marks starting enemies that can see an insertion point, walls counted.
- **Validate before playing:** every host-option combination, 1, 2 and 4 players and several rolls, within a budget;
  the first failing combination is kept so it can be reproduced.
- **Rehearsal:** write down events (the phone is taken at 0:30) and what should follow, run them through the plugin's
  own trigger rules, and keep them as tests beside the scenario. A **walkthrough** moves test players along waypoints.
- **Places and painted areas:** name spots and rooms ("Rear stairs"), and paint a zone's shape with brush, rectangle or
  polygon, with holes and floors. A trigger can link to a painted room, so changing the room changes every trigger that
  uses it. **Floors and rooms** (Map tab) organise height bands and buildings; with a `.mlgeo` open, corners can snap to
  walls (a first try).
- **Mission graph:** every trigger, what it waits for and what it does, with the Mission check's findings on it.
- **Squads, waves and modules:** select and copy groups of enemies; "Add waves" makes timed reinforcement waves; an
  encounter module saves enemies with their triggers to insert elsewhere, turned and renumbered
  ([`docs/modules.md`](docs/modules.md)).
- **New mission: Intel raid:** a recipe that builds a complete mission from a few choices.
- **Named undo and redo**, and **revisions** saved with a note.
- **Packs:** export a scenario, with modules if you like, as one `.mlpack.json` for other players. Importing shows every
  file, what it needs and any clash first, and never writes over a file.
- **Proposals from a coding assistant** through the local API ([`docs/agent-api.md`](docs/agent-api.md),
  `tools/agent/`): each opens as a draft for you to review; nothing changes until you apply it.

### Game Master mode
In a mission you host, F8 opens Game Master mode: every enemy gets a marker over the 3D view, and the game's own key
binds are switched off while it is open. F9 switches the game's free camera, which flies through walls. Left click
places an enemy at the crosshair; aim where it should look and click again. Space places one where the camera is (for
spots like under a bed). 1/2/3 choose the role, Z the stance, T the enemy type. Right click picks an enemy, the mouse
wheel turns it, Delete removes it, F10 saves all markers as a scenario. Every key is listed on screen and can be changed
in `[GameMaster]`.

### Scenario format
A scenario is a JSON file. [`docs/scenario.schema.json`](docs/scenario.schema.json) describes every key, and the plugin
and the editor refuse a file with a wrong type, a number used twice, a link to nothing, an unknown kind or a newer
format, naming the place (`triggers[2].after`). To check a file the way the plugin reads it:
`dotnet run --project tests/Pure -- --read <scenario.json>`; with your own map file,
`dotnet run --project tests/CoreTest -c Release -- --check <scenario.json> <navmesh.bytes> [recon.json]` shows which
enemies are off the walkable surface or on ground cut off from the rest of the map.

A file is written in the oldest format that holds everything in it, so an older MissionLab refuses what it would misread
instead of playing it wrongly: `missionlab-plan/1` by default, `/2` with a painted area, `/3` with objectives or a
trigger linked to a place, `/4` with a zone or hold that waits for another trigger.

**Enemies** (`"spawns"`). A position off the walkable surface becomes a fixed sentry, and sense values of 0 mean "the
map's usual".
- `"trigger": <id>`: not on the map at the start; appears when that trigger fires. At least one enemy must be there
  from the start.
- `"type"`: one of the game's enemy types, the same list on every map (so the suicide bomber can be placed anywhere);
  such an enemy appears a few seconds after the mission starts. `"prone": true` lays it down.
- `"hvt": true`: in a target-person mission, where the target stands; the game spawns its own target there.
- `"vehicle": true`: one of the game's gun trucks, parked facing `yaw`.
- `"team": 4`: a second enemy side that fights the first; `"team": 1`: the players' side (not tried).
- `"skill": "recruit"` or `"veteran"` scales reaction time and accuracy; `"nvg": true` gives night vision. Both also
  work at the top of the file for every enemy without its own.
- `"tag": "officer"`: a name for a `down` trigger to wait for.
- `"chance": 60`: there 60 times in 100; enemies that share a `"lot"` share one roll. `"either": 1` with
  `"option": 1, 2 …`: one option of the set is there each mission. `"minPlayers": 3`: only with three or more players.
  Each mission rolls anew, or the same every time with `Seed` set; the log names the roll.
- `"scatter": 6`: the enemy may stand anywhere within 6 m of its spot, on the same floor and connected ground, inside
  an `"allow"` placement area and outside `"empty"` ones (`"placement"` at the top of the file).

**Triggers** (`"triggers"`, each with an `id`). A trigger waits for something, then brings the enemies bound to it and
does its actions.

| `"when"` | Fires when |
|---|---|
| `zone` (the default) | a player walks into the circle (`pos`, `radius`), a painted `area`, or the room of a linked `place` |
| `hold` | a player has stayed inside for `hold` seconds; leaving starts the count again |
| `alert` | any enemy notices the players |
| `intel` | an intel item is picked up (`item`: its name, `""` for any, `"all"` for every one) |
| `time` | `delay` seconds after the mission started |
| `after` | another trigger (`after`: its id) has fired; `"after": 0` fires only by hand ("Fire now") |
| `all`, `any` | all, or one, of the triggers in `"of": [1, 2]` have fired |
| `down` | `count` enemies have gone down (`0`: all of them); with `tag`, only those enemies |
| `door` | the door whose hinge is at `pos` has been opened or broken |
| `outcome` | the host has seen what became of another trigger's latest firing (`after`): `"outcome"` is `applied`, `failed`, `partial`, `unconfirmed` or `timeout` (no answer within `within` seconds) |

How a trigger waits:
- `delay`: seconds between the condition and the firing. `"warn": true` tells the players how long is left (when the
  clock starts, at five minutes, at one minute and, in red, at ten seconds).
- `"chance": 40`: part of only 40 missions in 100; one that sits out takes its enemies, things and actions with it.
- `"repeat": 120`: may fire again 120 s later; `"times": 3` caps it. Enemies bound to it that have gone down come back.
- `"everyone": true` (zone and hold): every living player has to be inside.
- `"after": 1` on a zone or hold: it does not watch its area until trigger 1 has fired, so a hold reached early still
  has to be held again.
- "after", "all" and "any" ask whether a trigger has ever fired this mission. Triggers that wait for each other in a
  circle never fire by themselves; the Mission check names them.

What a trigger does, besides bringing its enemies:
- `"doors"`: a list like the scenario's own, with `locked` and `open`.
- `"extraction": "open"`: the players may extract.
- `"message"` (up to 120 characters) on every screen; `"red": true` makes it a warning. `{enemies}`, `{players}` and
  `{time}` become the numbers of the moment. Messages take turns rather than pushing each other off the screen.
- `"power": "off"` or `"on"`: the lights, on maps with a breaker box.
- `"end": "success"` or `"failed"`: ends the mission.
- `"respawn": true`: brings dead players back.
- `"rally": 40` (enemies within 40 m walk to the trigger's place) and `"hunt": true` (its enemies walk to the nearest
  player) are kept, but are failing: orders to freshly spawned enemies have not yet been seen to move anyone.

**Mission settings** (top of the file):
- `"briefing": ["line", …]`: up to four lines on every screen at the start.
- `"extraction": { "after": 3 }` holds the extraction shut until trigger 3 has fired (the mission's own rule still has
  to be met too); `{ "goals": true }` also until every required objective is done.
- `"goals": [{ "id": "phone", "name": "Recover the phone", "succeed": 3 }]`: objectives, each started, done or failed by
  triggers (`activate`, `succeed`, `fail`), possibly after other goals (`after`), and `required` unless it says `false`.
  Players get "New objective", "Objective complete" and, in red, "Objective failed"; `"notify": false` keeps one quiet.
  When a goal's success and failure come in the same moment, failure wins.
- `"insertion": "Alpha 2"`: every player gets that insertion.
- `"respawn": { "after": 20, "times": 2 }`: 20 s after the first death, everyone who is dead comes back together, at
  most twice.
- `"power": "off"`: the lights are off from the start. `"weather": { "hour": 5.5, "rain": true }` on maps that have
  weather (Clean House, Mexico Depot and Georgia have none).
- `"gameMaster": true`: meant to be run by a Game Master, so triggers fired by hand may be part of the way out.
- `"sealed": true`: positions, counts, rolls and coming trigger outcomes stay off the Play view, the maps and the
  inspector, so the host can discover the mission with the squad. Spoiler avoidance only: the file stays readable.
- `"options"`: a few named choices for the host before the mission (garrison size, waves, skill), each dropping enemy
  lots or triggers or changing a rule.
- `"director"`: a Game Master budget and action cards (fire a trigger, at a cost, with a rest time, only when no player
  is close or looking). `"director": { "pressure": … }` lets the budget be spent by itself within bands the author
  writes; it is an experiment.
- `"places"`: named spots and painted rooms. `"id"`, `"revision"`, `"squad"`, `"minutes"` and `"mapPrint"` describe the
  mission and the map file it was made on.

**Intel, doors and things:**
- `"objectives": [{ "name": "Cell Phone", "pos": [x, y, z], "yaw": 0 }]` moves the mission's own intel items. A map
  holds a few copies of each item ("Cell Phone_1", "Cell Phone_2") and uses one per mission; an entry applies to
  whichever copy is in play. Whether a friend without the mod sees a moved item in its new place is not yet confirmed.
- `"doors": [{ "name": "Door (3)", "pos": [-12.5, 0.3, -6.0], "locked": true, "enemiesCanOpen": false, "open": true }]`:
  `pos` is the hinge, and the nearest door within 0.75 m is taken. A door a scenario names is always as the scenario
  says, whatever the game's own roll locked.
- `"things": [{ "name": "AKM", "pos": [x, y, z], "yaw": 90, "trigger": 0 }]` lays the game's own items on the ground,
  from the start or when a trigger fires. Rifles have been seen to work; the rest of the list has not been tried.

### Recon
`MissionLab.Recon.dll` is read-only. It writes what a loaded mission contains to `BepInEx/MissionLab/recon/`
(`<scene>.early.json` at the start, `.late.json` after 45 s, `.manual.json` on F7) and follows every enemy through the
mission (`<scene>.movement.json`). The editor reads these files for insertion points, doors and intel. Like everything
derived from the game, keep them local.

## Status
<!-- capability table: made from docs/capabilities.json by python3 tools/status.py --write; change that file, not this table -->
Every result below is from OPERATOR 0.09.5 with BepInEx 6.0.0-be.785 (IL2CPP), the only game version tried. Statuses: **supported** = seen to work in the game on the named build, map and mode; **experimental** = built and switched on, but not proven in the game (the effect was hardly noticed, or it is off by default until something else is seen); **failing** = tried in the game, and it did not work; **unavailable** = tried and the game does not do it, so the mod does not use it; **untested** = never run in that combination. "Solo" means a modded host alone; "co-op" means a modded host with a friend who has no mod; "by the host's log" means a run with two players of which only the host's log and recording were kept, which is never a client's word.

A status holds for the builds it names. The plugin and the editor show anything seen only on an older build as "seen on <build>, not yet on <current>", never as confirmed, and solo or host's-log evidence never as seen by a client. 0.24.1 to 0.26.0 have run once in the game (18 November, 0.26.0, solo; the rows below name what was seen); everything else they add is covered only by `./test.sh` and the editor tests. 0.26.1 ran once with two players (19 November); only the host's log and recording were kept, so nothing from that run is a client's word and it does not count as co-op here. 0.27.0 ran once solo (21 November: packs, an operation over two missions and a restart, objectives). Sources: `docs/findings.md` (what was seen, run by run) and `docs/backlog-status.md` (each ticket). `docs/coop-record.md` is the record to fill in for the next co-op run.

This table is made from `docs/capabilities.json` by `python3 tools/status.py --write`: change the file, not the table. The plugin and the editor read the same file.

| Capability | Status | Build, map, mode | Evidence and what is still open |
|---|---|---|---|
| Client joins a modded host; scenario enemies, enemies spawned later and zone enemies appear for the client | supported, co-op | 0.13.0, Clean House, co-op | Findings, co-op 0.13.0. Nothing added after 0.13 has been seen by a client |
| Everything else in co-op on 0.14 to 0.26.1 (triggers, doors, messages, intel, extraction, waves, endings, live edits) | untested | 0.26.1, Clean House, co-op | 19 November, two players: triggers, the hold by everyone, messages, the held extraction and the squad's extraction all ran by the host's log; what the second player saw, and whether they had the mod, was not recorded. `docs/coop-record.md` lists every check |
| Rejoin or late join; a modded player who is not the host | untested | any | ML-011 |
| Generated layouts | supported, solo | build not recorded (before 0.15), Suburb and Mexico Depot Day, solo | Findings: enemies on every planned point; Mexico Depot 31 of 31, patrols walking |
| Generated layouts on Georgia | untested | Georgia | Only the game's own target person is recorded there; an earlier note said layouts ran, with no build or record |
| Maps other than Suburb, Mexico Depot and Georgia | untested | any | Findings, "Not yet verified" |
| Scenario files and patrol routes | supported, solo | 0.15 to 0.24.0, Clean House, solo | Findings, runs 0.15.0 to 0.24.0; patrols walked their routes |
| The scenario picker in the lobby, the empty map | supported, solo | build not recorded, Clean House, solo | An earlier status note only; no run in the findings records them |
| A scenario that cannot be finished is refused, the game's own mission runs | supported, solo | 0.24.0, Clean House, solo | Two refused missions with the refusal test (ML-001) |
| Trigger engine: time with repeat and cap, zone, hold, hold by everyone, alarm, door, enemies down, tagged down, intel, all-of, after, chance, fire by hand, countdown | supported, solo | 0.24.0 and 0.26.0, Clean House by night, solo | Findings, 17 and 18 November; repeat fired at 60, 150 and 240 s and stopped |
| Messages on screen, taking turns; briefing | supported, solo | 0.24.0, Clean House, solo | Findings: messages took turns. Never confirmed on a client's screen |
| Shared respawn wave | supported, solo | 0.24.0 and 0.26.0, Clean House, solo | 18 November: two waves, back after 20.2 s each; the third death, with no wave left, let the game end the mission 27 s later. Extracting players were counted as dead, and a wave announced on every screen as the mission was won (19 November, and after every success ending before); 0.26.2 stops that, not run yet. Two players dead at once: untested |
| Respawn by trigger (`"respawn": true`) | supported, solo | 0.22.0, Clean House, solo | The dead host was back within a second |
| Failed ending (red message, back to the lobby without a result screen) | supported, solo | 0.22.0, 0.24.0 and 0.26.1, Clean House, solo | Findings. The game's own fail call does not end a mission while a player lives: unavailable, not used. 0.26.1: the trigger's red message and "Mission failed." go out together |
| Success ending | supported, solo | 0.17.0, 0.26.0 and 0.26.1, Clean House, solo | The mission ends 2.5 to 2.7 s after the call. 0.26.1: the ending waited 11 s for the two messages fired with it. The game's own extraction: two players extracted on 19 November, recorded as "over"; 0.26.2 records it as a success, not run yet |
| Extraction held shut until a trigger (`extraction.after`, a trigger's `"extraction": "open"`) | supported, solo and by the host's log | 0.15.2 to 0.26.1, Clean House, solo and two players | Not a row before 0.27; taken from the findings. 18 November: "two down" and the phone opened the extraction. 19 November: held until trigger 5, then the squad extracted (host's log). Opened by the fallback "set on each extraction zone" in every run. What a client sees: untested |
| Door locked or unlocked at the start and by a trigger | supported, solo | 0.15.0 to 0.24.0, Clean House, solo | Findings: our doors hold after the game's own roll |
| Door that stands open | supported, solo | 0.26.0, Clean House by night, solo | 18 November: opened by the third way (turning it by hand) 5 s after the trigger, seen by the plugin; a client's view: untested |
| Calling enemies (`rally`), hunters, Game Master orders to fresh enemies | failing | 0.26.1, Clean House, solo | 18 November: two hunters had no path and moved 3.9 m in 90 s after all three ways. 0.22.0: a Game Master order moved two enemies after a fight (ML-003) |
| Moved intel (`objectives` in a scenario) | supported, solo | 0.26.0, Clean House by night, solo | 18 November: the phone was moved upstairs at the start, picked up there, and fired its trigger. How an unmodded client sees it: untested |
| Random layouts laying the intel elsewhere (`RandomIntel`) | experimental, off by default | 0.24.3 | Off until moved intel is seen on an unmodded client. The rest of `RandomExtras` (waves, ambush, locked doors) stays on |
| Target person placement (`hvt`, `MoveTarget`) | untested | any | Only the game's own target was seen, on Georgia |
| Insertion chosen by the scenario | supported, solo | 0.15.2, Clean House, solo | The host started on Alpha 2. A client's insertion: untested |
| Placed weapons | supported, solo | 0.19.0, Clean House by night, solo | Picked up and used |
| Placed grenades and landmines | untested | 0.17.0, Clean House, solo | Placed without error but not seen |
| Two sides, lights out, suicide bomber type, gun truck (with the plugin's fallback) | supported, solo | 0.15.0 to 0.20.0, Clean House, solo | Findings |
| Skill and night vision | experimental | 0.21.0 and 0.22.0, Clean House, solo | Applied by the log; the difference was hardly noticed in play |
| Weather and time of day | unavailable | Suburb, Mexico Depot, Georgia | The game has no weather object on these maps |
| Noise to alert enemies (`HeardTarget`, `ShotFired`) | unavailable | 0.19.0 and 0.20.0, Clean House, solo | Nobody reacted; replaced by patrol orders |
| Game Master mode in the game and in the browser, recordings | supported, solo | 0.15 to 0.24.0, Clean House, solo | Findings, every run |
| Notice to other players that the host runs MissionLab (`TellPlayers`) | untested, co-op | co-op | Uses the message call that works solo; never seen by a client. 19 November: sent with 2 players in, by the host's log |
| PvP, wave survival and other modes left alone | untested | any | Not tried in the game |
| Scenario contract, run records, recording identities, readiness check, release ZIP | supported, solo | 0.26.0, Clean House, solo | 18 November: readiness all OK, catalogue right, run records and semantics 4 recordings written. Run repeat, debrief: untested in the game |
| Named places (`places`: labels, and painted rooms that zone and hold triggers can link to) | untested | any | Not a row before 0.27. A place labels a spot or a painted room for logs, recordings and explanations; from 0.27 a zone or hold trigger may link to a painted place (missionlab-plan/3) and uses its shape, resolved when the file is read. No run on record names or links one |
| Mission objectives (`goals`: required and optional, notices, extraction waiting for them) | supported, solo | 0.27.0, Clean House by night, solo | 21 November (solo, by the host's log): `clean_house_objectives` played to a success; the goals moved in order, the extraction stayed shut until both required goals were done, and the run record holds all three as succeeded. The notices on the host's screen were not checked; a client's view is unseen. The upstairs hold had been reached before the phone, so that goal was done the moment it became active; 0.27 lets the hold wait for the phone (row zone-after) |
| Zones and holds that wait for another trigger (`after` on zone and hold, missionlab-plan/4) | untested | any | New in 0.27 (ML-055), after the objectives run of 21 November. Covered only by the tests; not run in the game yet |
| Branches on observed outcomes (`when: "outcome"`: applied, failed, partial, unconfirmed, timeout) | untested | any | New in 0.27. A trigger waits for what the host saw of another trigger's latest firing; only enemies made by the spawner count as seen to take, and messages, doors and calls are never more than unconfirmed. A late result is shown but never moves the branch, and nothing is retried. No run on record has one |
| Pressure director, an experiment (`director.pressure`: bands, caps, preview, rest) | experimental | any | New in 0.27, a spike. Spends the Game Master budget on the author's own cards within authored bands, never past the budget, the living-enemy cap or a card's placement rule, and rests after a casualty; preview logs without firing. Placement checks are an approximation. Covered only by the tests: no playtest has compared it with the director off |
| Draft proposals from coding agents (`/api/agent`, `tools/agent/`) | untested | any | New in 0.27. Proposals are checked by the strict reader, Completion and the capability list on a copy and stored under proposals/; they never touch scenario files or the running mission until the author applies one as a draft. No run on record has used it |
| Painted trigger areas | supported, offline on recorded movement; hold by everyone run with two players | 0.26.1, Clean House by night | 18 November: the house painted on the real map and all 8,564 recorded player positions (15–18 November) played through the plugin's own rules: entered and left only at the doors, floors and stairs right, hold not reset by the stairs, basement left out. 19 November: the painted upstairs hold by everyone counted both players and fired 60 s after the second went up (host's log). A shape sent live in a mission: untested |
| Sealed missions | supported, solo | 0.26.1, Clean House by night, solo | 18 November: played to its failed ending; the host reported the page view as expected (the logs cannot show what the page drew). 19 November: the sealed Two-Man Job played with two players to its extraction |
| Play, Create and Direct views and the trigger inspector (0.25) | supported, solo | 0.26.0, Clean House | 18 November: the inspector followed every trigger of the diagnostic. Until 0.27 this row also held options, scatter, cards and packs |
| Host options before the mission (0.26) | supported, by the host's log | 0.26.1, Clean House by night, two players | 19 November (two players): the picks applied at the start (Standard, Regulars, Two, 25 minutes) |
| Constrained scatter (0.26) | supported, by the host's log | 0.26.1, Clean House by night, two players | 19 November (two players): scatter moved 5 of 5 enemies |
| Game Master budget and action cards (`director`, 0.26) | supported, by the host's log | 0.26.1, Clean House by night, two players | 19 November (two players): both cards were spent (the breaker reported the lights off; the street patrol appeared) |
| Scenario packs (0.26) | supported, solo | 0.27.0, Clean House by night, solo | 21 November: exported from the live page; installed through the running plugin as the same text (nothing written), as a copy with a new name and id and as a revision; the copy played to its extraction as the second mission of an operation. The install was sent to the plugin's route directly; the page's Open pack file was not used |
<!-- end of the capability table -->

What is known about the game's internals, run by run, is in [`docs/findings.md`](docs/findings.md).

## For developers
**Want to help?** Code, docs, missions and bug reports are all welcome, and so is a co-op run with a friend who has no
mod, which nobody can test alone: see [CONTRIBUTING.md](CONTRIBUTING.md).

**Build:** `python3 tools/build.py --refs <OPERATOR folder>` (or set `OPERATOR_REFS` and run `./build.sh`). It compiles
with Roslyn from the newest installed .NET SDK against the game's `dotnet`, `BepInEx/core` and `BepInEx/interop`; the
game folder itself works. Built for Unity 6000.3.8f1, BepInEx 6.0.0-be.785, game code dated 7 July 2026.
`python3 tools/package.py --check` builds `dist/MissionLab-<version>.zip` and checks it, including an update and an
uninstall on a scratch install.

**Tests:** `./test.sh` runs everything that needs nothing of the game: the C# rules (scenario model, dice, trigger rules,
completion check, waves), the editor's own checks, the JSON Schema over the examples and fixtures, the coding-agent
adapter's self-test, and the page in a real browser; it also checks that the built page and the Status table are what
their sources make. It needs the .NET 8 SDK, Python 3 and Node 18 or newer; the schema check also needs
`pip install jsonschema`, and the browser checks Node 22 or newer and Chrome or Edge. Locally a missing one is skipped
with a note; on GitHub a skip fails the run. `./test.sh --map <navmesh.bytes> <recon.json>` adds the checks that read
one of your own map files.

**Where things are:**
- `src/MissionLab.Core`: pure C# (navmesh reader, seeded generator, scenario model and readers, trigger rules), with no
  game references; compiled into the plugin and the tests
- `src/MissionLab.Director`, `src/MissionLab.Recon`: the two plugins
- `tests/Pure`, `tests/editor`, `tests/browser`: the C#, editor and real-browser checks; `tests/CoreTest` checks the
  generator against real map data; `tests/fixtures` holds the scenario files they read
- `tools/editor`: the editor page, built from `tools/editor/src` by `tools/editor/build.py`, and its 3D library
- `tools/package.py`, `tools/status.py`, `tools/schema_check.py`: the release ZIP, the Status table, the schema check
- `tools/agent`: the coding-agent adapter (MCP over stdio) and an example client; `tools/preview`: plan renders
- `docs/`: the scenario schema, what has been seen in the game (`capabilities.json`, `findings.md`), the backlog and
  where each ticket stands (`backlog.md`, `backlog-status.md`), the co-op record to fill in (`coop-record.md`), modules
  and the agent API
- [`AGENTS.md`](AGENTS.md): how the code fits together and the rules for changing it

## Permission
In November 2026 the maker of MissionLab sent Vector Interactive a video of the mod and asked whether a mod that only the
host installs fits the mod policy. The studio's answer by email (16 November 2026), in short: it looks good to them as
long as it does not interfere with PvP or cause griefing in PvE; a server-side mod that only the host needs is fine; and
whether modded missions carry the "cheated" flag is up to the mod.

This is permission to make and use the mod on those conditions. It is not an endorsement: MissionLab is unofficial and
is not made or endorsed by Vector Interactive. If you host with it, do so for people who know the lobby is modded.

## License
MIT: see [`LICENSE`](LICENSE), also shipped as `BepInEx/plugins/MissionLab/LICENSE.txt`. The page's 3D library,
three.js (`tools/editor/three.min.js`), is MIT as well, by the three.js authors (`tools/editor/three.LICENSE.txt`).
OPERATOR's mod policy applies to anyone who shares a mod for the game, whatever this license allows: it stays free for
everyone, with no paid access or supporter-only files, and is labelled unofficial.
