# Changelog

What each release added, newest first. Each heading says how much of that release has run in the game; the Status
table in the README is the current word on what has been seen to work, on which build and by whom. Ticket numbers
(ML-NNN) are those of `docs/backlog.md`; `docs/backlog-status.md` says where each one stands.

## Unreleased
- **Clicking an enemy in 3D keeps it selected on a slow computer.** Selecting rebuilds the figures, and a release that
  came before the next frame was drawn missed the enemy the press had just selected, so the click counted as one on
  empty ground and cleared the selection. Seen as an intermittent failure of the browser suite on GitHub; the suite now
  presses and releases with no frame in between, which failed every time before the fix.

## 0.27.0 (run once solo on 21 November: packs, an operation and objectives; the rest not yet)
The v0.27 milestone of `docs/backlog.md` (ML-028 to ML-053, and ML-055 from the run of 21 November). Everything here is
covered by `./test.sh`, which now also works the page in a real browser. On 21 November one host alone installed a pack
through the running game, played an operation of two missions across a restart and played the objectives example; nothing
else has run in OPERATOR yet, and none of it has been seen by a friend without the mod. `docs/backlog-status.md` says what
each ticket still needs in the game.

**Found in the run of 21 November, fixed (not run in the game again yet)**
- A zone or hold can wait for another trigger (`after`, `missionlab-plan/4`; ML-055): the objectives example's upstairs
  hold had been reached while the phone was searched for, so the second objective was done the moment it appeared. The
  example now counts the hold only once the phone is taken.
- Operations: after the game refused a change to a branch, the page went on showing the refused choice; it now shows
  what is saved.
- Export pack puts a scenario opened unchanged in as its file's own text, so importing it back finds the same file
  instead of a clash.
- Missions with the same title in Play's list now show their file name (and revision) in the row.

**Stabilisation**
- **A circle never opens the way out** (ML-029). A trigger that waits, through `after`, `all` or `any`, on a chain that
  comes back to itself never fires by itself (only "Fire now" sets it off), so it no longer counts as a way to open the
  extraction, not even through an `any` with another way in. The plugin and the Mission check now both refuse a
  scenario whose only way out is such a trigger, whatever the order of the list; another trigger that can open the
  extraction still makes it acceptable.
- **Game Master lets go completely after an error** (ML-030). When the mission rules are paused after errors while
  Game Master is open, the mod closes it, gives the game's keys back, switches off the free camera it opened and drops a
  half-placed enemy; recovering does not take the keys again, and F8 opens Game Master as usual. Closing Game Master by
  hand now also switches off a free camera it opened. Still for the game: a run with the rules failing while Game Master
  is open and `BlockGameKeys` on.
- **Repeating a generated run uses that run's roll** (ML-031), not the `Seed` in the config. The run record now keeps
  what the layout was made from (generator version, scene, map file and its fingerprint, the scene's spawn and insertion
  points, enemy count, `RandomExtras`, `RandomIntel`, `MoveTarget`, the target mission), and all of it is compared before
  anything is changed: if one differs, or the record is older, unreadable or from a refused mission, the mission is left
  as shipped and the page and the log say why. "A new roll" plays a fresh setup. A repeat promises the same setup, not
  the same play. Still for the game: a repeated generated setup seen to match. The doors that random extras lock are
  taken from the scene and are not part of the comparison.
- **An area drag is kept or given up as a whole** (ML-032). Giving up a corner, edge or whole-area drag (Esc, the
  browser cancelling the pointer, another tool, Done) puts the area and its anchor back exactly, with nothing to undo or
  send; a finished drag is one undo step for the shape, the anchor and the inside test, which follows the shape while
  you drag. What the game makes of a shape sent during a mission is still unseen.
- **Updating and uninstalling keep your work** (ML-033). The ZIP now puts the examples in `BepInEx/MissionLab/examples/`
  (copy one into `scenarios/` to play it), so an update never writes into `scenarios/`, `revisions/`, `runs/` or
  `replays/`; an example you edited where it was installed is put back by the next update. Uninstalling is deleting
  `BepInEx/plugins/MissionLab/`; removing your data as well is a separate step (see Install in the README).
  `python3 tools/package.py --check` tries both on a scratch install; an update of a real install has not been done.
- **Fresh enemies' orders are followed to the end in the log** (ML-035). Each order is logged in stages: accepted, path
  found, on its way, and one last word (arrived, in a fight, no progress, stopped short, unreachable after 45 s, gone or
  replaced), at most 150 such lines a mission. An order to a spot more than 3 m across or 1.8 m up or down from walkable
  ground sends nobody. The enemy's own path search is switched on and a path is asked for only when one is needed. Game
  Master orders say "accepted, not yet seen to move", a refused call (`rally`) fails its trigger's action, and a hunter's
  line says it accepted an order, never that it moved. Moving fresh enemies stays **failing** until one is seen to arrive:
  the starter missions no longer use hunters or calls (README, Missions to play), and
  `examples/clean_house_movement_probe.json` is the test to play next.
- **One list of what has been seen to work** (ML-036). `docs/capabilities.json` is read by the plugin, the page (Play
  view, "Is everything ready?") and the README Status table, so they cannot disagree. "Failing" (tried, did not work)
  is kept apart from "untested"; a result counts as confirmed only on the build that ran it, and a solo run or a host's
  log never counts as what a client saw. Before a mission Play names what is missing (an intel kind the map's recon file
  lacks, the wrong scene, map or mode), and the plugin leaves the mission as shipped when none of its intel items
  matches a scenario's intel trigger or moved item. A scenario that uses something failing or experimental can still be
  chosen, as "Play this next (experimental)". Two assumptions are not yet seen in the game: that the recon's map file
  name is the map file, and that the game lists every candidate intel item when the mission starts.
- **A co-op record for 0.27** (ML-037). `docs/coop-record.md` now holds a 21-row record for one host and one friend with
  no mod at all, on the 0.27.0 candidate, played with `examples/clean_house_acceptance.json` and
  `examples/clean_house_objectives.json`: messages, doors, moved intel, delayed enemies, both endings, the extraction,
  shared waves after two deaths at once, the painted house with its stairs and crouching, live edits, stale and retried
  commands, and two missions in a row. A row passes only on the word of the player whose screen it is. Nobody has
  played it yet.
- **The first-session path** (ML-039) is prepared, not proven: the examples folder, the update and removal steps in the README
  and `docs/first-edit.md` agree, and the walkthrough no longer relies on enemies walking where they are sent. Still
  needed: both plugins built against the game from the release commit, the ZIP with its checksum, and a first-time host
  following `docs/first-edit.md` through to playing the saved mission with a friend without the mod.

**Authoring**
- **Rooms painted once** (ML-040). A zone or hold trigger can "Link to Place" (`"place": <id>`, no area of its own)
  instead of copying the place's shape. Changing the place's footprint changes every linked trigger in one undo step and
  one staged change; renaming keeps the links; a place with links cannot be deleted until they are moved or detached
  (Detach, Detach all, Move links). Links make the file `missionlab-plan/3`; "Export a copy without links" and the pack
  option "Without links to places" write plan/2 with the exact shapes. During a mission a footprint change is one
  shape change: holds in progress start again, what has fired stays. A Game Master save keeps the links while the shapes
  agree and copies the shape where they do not. An inserted module asks whether to link a place of this map or keep its
  own shape (`docs/modules.md`). Still for the game: a place edited live, a Game Master save of a linked mission.
- **Floors and rooms** (ML-041, Map tab). Floors are height bands (presets for Clean House and for 3 m storeys, or the
  floors found on the map), buildings are sets of named places; each can be focused, isolated, hidden, locked and
  ordered. Anything locked, hidden or faded cannot be clicked, boxed, dragged, deleted or added to, in 2D or 3D. A
  vertical section along a drawn line shows the floor bands, stairs, feet and where floors overlap. The layout is kept
  in this browser per map print; a different print asks for a review. Still open: the 3D view with the real 3D library
  (hidden floors are still drawn there), and whether the floors found on Clean House are right.
- **A mission graph** (ML-042, read-only, Triggers tab and Direct): the start, every trigger with what it waits for, its
  delay, chance and repeat and what it does, missing references, the extraction and the endings, with a line style per
  kind of link and the Mission check's findings marked on it. In Direct it shows how often each trigger fired, its phase
  and its outcome; after a rehearsal, its results. Clicking selects the trigger (Direct) or its enemies (Create). A sealed
  mission shows nothing. Editing in the graph is for later; its look has not been checked in a real browser.
- **Walk the map, keep it as a test** (ML-043). The area probe says why a point is inside or outside (which part, a hole,
  the floor band or the circle's distance). A walkthrough moves up to four test players along timed waypoints with set
  heights, samples every 0.5 s and warns when a step jumps through an area; tracks from a replay can be used as paths.
  Rehearsal results go event by event, with the state before and after, the first failed check, the first difference
  from the previous run, and what the rehearsal models and what it does not. Offline, "Export trace (not run)" writes it
  down without running it. Saved tests ship in `examples/rehearsals/`. Still open: running it through the plugin from the
  page, clicking across real floors, importing a real replay (its clock is assumed to start with the mission).
- **New mission: Intel raid** (ML-044, Map tab): insertion, the kind of intel, the building (a place, or paint one),
  encounter modules for its rooms, garrison, skill, time limit and alarm. It says what is missing or not supported
  (hunters and calls are left out) before it makes anything, and the whole mission is one undo step: triggers, guards,
  host options, patrol reinforcements, briefing, held extraction and a rehearsal "Intel raid: in, intel, out". Also
  fixed: the editor dropped an enemy's `lot` when its chance was 100, which broke the Two-Man Job's garrison option once
  saved from the page. Still open: a new author completing it, the example played with a friend without the mod.
- **Named undo and redo** (ML-045). The History panel above Save lists each change by name; Ctrl+Z, Ctrl+Shift+Z and
  Ctrl+Y work (not while typing). A drag in 2D or 3D, the facing slider and an area edit are one step each, and a
  cancelled one leaves nothing. "Move with everything" (Enemies tab) moves or turns the selected enemies with their
  routes, the triggers that bring them, those triggers' things and the painted places they stand in, around one pivot,
  with a preview; doors stay with the map and are listed, and a landing off walkable ground is refused. Opening another
  file or a new mission session clears the history, says so, and keeps the unsaved page under Revisions as a separate
  draft. Undoing a change the running game already has stages the reverse change in Draft mode. Still open: jumping by
  clicking a history entry, undo against a running game.
- **Wall snapping and room suggestions** (ML-050, a first try). With a `.mlgeo` file open, rectangle, polygon and corner
  points can snap to the walls cut at waist height on the current floor, and "Suggest room" floods out from a click
  between walls and drawn barriers (30 m around by default, at most 80 m), with pillars as holes. If it leaks, it shows
  where and how wide the narrowest gap is and proposes nothing until a barrier or "Close the gap" seals it; the draft is
  kept as one undo step or dropped. It fails where you would expect: open windows and doorless doorways leak, tall
  furniture becomes holes, low railings stop nothing, rising stairs join floors, curved walls hit the 256-corner limit,
  and the geometry file is matched to the map by name and ground overlap. Tested only on synthetic walls; not yet on a
  real export or by hand in a browser.
- **Proposals from a coding assistant** (ML-053). A coding agent can read the scenarios and the capability list through
  the browser link (`/api/agent/…`, local only) and propose changes as operations on stable ids. MissionLab applies them
  to a copy and checks the result with the strict reader, the completion check (only problems the change introduces
  refuse it) and the capability list (a feature marked failing or unavailable is refused); a proposal made against an
  older text or revision is refused as stale. Accepted proposals are kept in `BepInEx/MissionLab/proposals/`, never in
  `scenarios/` and never sent to the running mission. In the editor, "Agent proposals" (Mission tab) opens one as a
  separate draft with the usual list of differences, and Apply makes it one undo step (in Draft mode while a mission
  runs), refused if the draft or the scenario has changed since. `tools/agent/missionlab_agent.py` lets an MCP client
  (such as Claude Code) use it; `tools/agent/example.py` is a scripted example; `docs/agent-api.md` describes it all.
  The tools stay in the repository and are not in the ZIP. Still to do: the example against a running plugin, an MCP
  client through the adapter, and the editor part in a real browser.

**Mission design and sessions**
- **Objectives** (ML-046): `goals` in a scenario (README, scenario format), the Objectives section of the Mission tab, a
  board in Direct, and only the objectives already given in a sealed mission's Play view. Completion, rehearsal and the
  Game Master read them through the same rules, so a chain finishes in one step and each change happens once. Notices go
  through the same queue as trigger messages (none for objectives given from the start). The extraction can wait for
  the required ones, and the plugin, rehearsal and Direct say which are still missing. Run records and replays note
  them; a Game Master save keeps them. Example: `examples/clean_house_objectives.json`. Still for the game: the notices
  on a client without the mod, and the extraction opening when the last required objective is done.
- **Branches on what was seen to happen** (ML-047): `"when": "outcome"` (README, triggers) waits for the host's answer to
  the latest firing of another trigger and is met at most once per firing. "Unconfirmed" is an answer; "timeout" means
  none came in time. An answer that comes late, or for an older firing, is shown in the inspector and the recording but
  never moves the branch, and nothing is retried. A trigger that brings enemies and only some appear is now "partial"
  (it was "failed"); the mission's end fails any branch still waiting. The Mission check blocks an `applied` or
  `partial` branch the host can never see (doors, calls, messages and the like are never more than unconfirmed) when the
  way out depends on it, and notes it otherwise.
- **Validate before playing** (ML-048, Mission tab): the format, whether the mission can be finished for every host
  option combination (all of them up to 256, a fixed sample above), 1, 2 and 4 players and several rolls, scatter (with
  the map open) and the saved rehearsals, within a budget (2,000 combinations by default) that, like Cancel, keeps what
  was found. The report names the file's hash, the map print and the rules version, and says it is out of date when one
  changes; the first failing combination is kept (`validations/<scenario id>/`) and can be reproduced, and "Show what it
  is about" marks it in the graph. Offline only the format and the Mission check run. It never claims balance, paths or
  anything about multiplayer.
- **Session playlists** (ML-049, Play). A host's list for an evening, kept in `BepInEx/MissionLab/playlists/`: each entry
  pins a scenario (file, id, revision and hash), its host options, squad size and dice (a new roll, an exact repeat, or a
  retry of the last failed setup), and tells the host which map and mode to start. After a restart of the game the
  entry's options and dice are put back at the mission start. The list moves on once per run record: success or failure
  moves on, a run without a clear result waits for the host, whose mark is shown as the host's. A changed or missing
  scenario asks what to do.
- **Operations** (ML-051, Play): two to six missions in a chain, each a pinned setup, where the next one depends on the
  last run's result and its objectives, by the author's branches in order; a branch the run record cannot answer waits
  for the host, and the fallback is used only when every branch is ruled out. The host can choose any branch, the
  fallback, or "does not count, play again"; such choices are kept and labelled. Files in `BepInEx/MissionLab/operations/`
  are saved whole with the previous version beside them, a damaged file is restored from it with a message, and a file
  from a newer version is left alone. Nothing from inside a mission is carried into the next one.
- **A pressure director, as an experiment** (ML-052). An optional `director.pressure` lets the Game Master budget be
  spent by itself on the author's own cards, within bands the author writes (from a time, while given objectives are
  active, with at least so many living players; at most so many points every 10 to 600 s), never past the budget, a
  card's cooldown or placement rule, or a cap on living enemies, and with a rest after a player goes down. When
  something it needs is unknown it holds and says what. `mode` is `off`, `preview` (logs what it would play, fires
  nothing) or `on`, and the host can switch it in Direct, where every decision and every card passed over is listed
  with its reason, with Hold now and Spend now marked as the host's. Older versions ignore the key and play without it.
  The placement check is an approximation, and no playtest has compared a mission with it on and off. Still for the
  game: that playtest, and whether downed players and the game's enemy list are counted as the director assumes.

**Tools and tests**
- **Tests that fail when they cannot run** (ML-028). `./test.sh` now also checks that the README Status table is what
  `docs/capabilities.json` makes (`tools/status.py --check`), runs the published schema over the examples and fixtures
  (`tools/schema_check.py`, needs `jsonschema`), runs the browser suite and the coding-agent adapter's self-test. On
  GitHub a missing Node, .NET leg or `jsonschema` fails the run instead of skipping it. The workflow has not yet run on
  the release commit.
- **The schema judges files as the readers do** (ML-034). `docs/scenario.schema.json` now says where the scenario sits
  for each format, that only zone and hold triggers carry an `area` and that zone, hold and door triggers need a `pos`.
  Both readers now refuse a plan/1 file (or one without a format line) holding a `"mission"`, a plan/2 scenario with a
  `schema` of its own, and a format number with anything but digits; a very large number reads as a newer format. The
  editor opens a mission record that holds a plan/2 scenario. One difference is left: the schema needs a thing's `name`,
  the plugin does not.
- **A real-browser suite** (ML-038): `node tests/browser/run.mjs` (Node 22 or later, Chrome or Edge, no packages) opens
  the built page and works it with real mouse and keys: open and save, drawing, editing and giving up an area, choosing
  across floors, 2D and 3D lining up, and Draft changes against a stand-in for the plugin (a stale session, a change still
  running, a retry carried out once). Its 67 checks passed on the first run, with Edge on Windows.
- Each new ticket has its own test file (`tests/Pure/Ml0NNTests.cs`, `tests/editor/cases/ml0NN-*.mjs`).

## 0.26.2 (fixes after the co-op run of 19 November; the fixes themselves have not run in the game yet)
0.26.1 ran once with two players on Clean House by night (`clean_house_two_man_job`, to the extraction in 11 minutes):
what the host's log and recording show is in `docs/findings.md`. What the second player saw was not recorded.
- **Extracting is not dying.** The game removes the players it extracts just as it removes dead ones, so the respawn
  rule counted the whole squad as dead the moment it left. It announced a reinforcement wave on every screen ("2
  players are down…") as the mission was won, and had done the same after every success ending before. Now no wave is
  counted while the game is extracting, once it has finished, or once an ending is decided.
- **A squad that extracts has won.** When the game itself finishes the extraction (`ServerCompleteExfil`, hooked for
  every mission the mod lays out), the mission counts as a success. The run record says "success" instead of "over"
  (Play view, debrief), the recording ends with "the players have extracted", and no trigger fires after it.
- **A repeat that brings nobody shows no message.** When a repeating trigger fires again but none of its enemies is
  down, nobody comes. If it does nothing else either (no doors, lights, respawn, call, ending or extraction), its
  message is now left out instead of announcing enemies who never come. A reminder with no enemies of its own, or a
  repeat that also cuts the power, still shows its message.
- **Two-Man Job messages.** In `examples/clean_house_two_man_job.json` the intel's and the hold's messages now read
  right whichever comes first, and the way out says "Job done". On 19 November the hold came first, so "Now hold the
  upstairs…" arrived after the hold, just ahead of the way-out message.
- Logs: the facing check gives the facing an enemy was actually given (toward a player) rather than its planned one,
  and opening the extraction also logs the game's mission-wide extraction flag.

## 0.26.1 (fix after the run of 18 November; the fix itself has not run in the game yet)
0.26.0 ran once in the game, solo on Clean House by night: what was seen is in `docs/findings.md` and the Status table.
- **A mission ending waits for its messages.** When a trigger ended the mission, messages fired with it (and the
  ending's own message) were still waiting their turn when the end screen came, and were lost. The ending now waits
  until the last waiting message has been on screen for 2 s, at most 12 s; with nothing waiting it comes at once as
  before. Once an ending is decided no other trigger fires, so a countdown can no longer end a won mission as failed.

## 0.26.0 (not run in the game yet)
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

## 0.25.0 (not run in the game yet)
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

## 0.24.3 (not run in the game yet)
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
  (`docs/coop-record.md`). The README's Status section is now a table of what is tested, experimental, unavailable or untested.
- **Builds on Windows too:** `python3 tools/build.py --refs <OPERATOR folder>` (or `./build.sh`) finds the compiler
  itself and checks the game references before it starts.
- **The editor's tests are in the repository** and run with `./test.sh` and on GitHub, without game files. The page is
  put together from `tools/editor/src` by `tools/editor/build.py`; the result is the same single page.

## 0.24.1
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

## 0.24 (run in the game on 17 November: refusal, shared wave, trigger engine)
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

## 0.23
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

## 0.22
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

## 0.21
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
