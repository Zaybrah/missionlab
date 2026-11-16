# Ideas

What could come next, with what the game already offers for each. "Odds" is the chance it
works as hoped. Everything here comes from the game's class listing and from recon files;
none of it has run in the game unless it says so.

## Built, waiting for a test in the game

| Idea | How it is done | Version |
|---|---|---|
| Locked doors | `DoorV2.Server_SetDoorLocked`, `AiCantOpen`; live setting confirmed in game | 0.14 |
| Bombers on any map | the suicide bomber is in the game-wide enemy type list | 0.14 |
| More triggers | alert, intel taken, timer, chains; door actions; "Fire now" | 0.15 |
| Random missions that use it all | wave on alert, ambush zone, locked doors, intel on another table | 0.15 |
| Extraction rules | a trigger opens it; a scenario holds it until a trigger has fired | 0.15 |
| Choosing the insertion | the host hands every player the scenario's insertion | 0.15 |
| Weather and time | `VectorTimeOfDay`; none of the three maps recorded has it | 0.15 |
| Gun trucks | the game's own vehicle spawner, with our parking spots | 0.15 |
| Replay | positions and events recorded; played back on the 2D map | 0.15 |

## Second round

Built, in the editor only (tested there, no game needed):

| Idea | What it does | Version |
|---|---|---|
| Danger check | flags enemies that can see an insertion point, using the 3D map export | 0.16 |
| Squads | several enemies selected, moved, turned, copied, pasted and saved together | 0.16 |
| Waves | timed waves that walk in on a spot, for defend missions | 0.16 |
| Mission stats | first noticed, time alerted, enemies down, distance walked, from the recording | 0.16 |

Waiting for a run of the game, because they need facts only a mission can give (Recon 0.2.3
collects them: breakers, alarms, mines, weapon racks, teams, everything spawnable):

| Idea | What the game has |
|---|---|
| Lights out | `BreakerSwitch` (`PowerOn` synced, `CMD_SetPowerState`), on maps that have one |
| Win and lose rules | `GameManagerNetwork.ShowMissionCompleteScreen`, the mission-failed popup |
| Mines and other placeable things | `Landmine`, `ProximityExplosive`, `N_WeaponSpawner`; anything in `NetworkManager.spawnPrefabs` |
| Factions | `TeamIdentifier.TeamID` (synced) on enemies and players, `TeamManager.GrabAllEnemies` |
| Weapon rules | `RaidManager.prohibitedWeapons`, `mapSpecificWeapons` |
| Adaptive pacing | built from triggers and the alert state the mod already reads |
| Game Master on another device | the browser link opened to the local network behind a password |

## Grading: what to build next (15 November 2026, after the third run)

Graded on four things: what it adds to the three goals (new missions, never the same twice, Game
Master), whether the game has shown it works, how much work it is, and whether the V10 game
update can break it. Editor and core features survive V10; anything that touches players is the
most likely to break.

### A: built in 0.18

| What | State |
|---|---|
| Mission check panel | built, tested in the editor |
| Chance and either/or groups | built; rolls tested in the core, not yet in the game |
| Messages on screen | built as a trigger action; not yet seen in the game |
| Win and lose rules | built as a trigger action; success seen in the game (with the game's "cheating detected" notice), failed not yet |
| Lights out | built as a trigger action and a mission setting; seen in the game |

### Built in 0.20

| What | State |
|---|---|
| Things as markers | built; rifles seen to work in the game, placing from a scenario not yet |
| A side per enemy | built; two sides seen to fight, the side from a scenario not yet |
| "Hold the zone" and "enemies down" triggers | built; not yet in the game |
| Messages on screen | seen in the game |

### Built in 0.21 (tested in the editor and the core, not yet in the game)

Briefing on screen; triggers that wait for all or one of several others; chance for triggers,
doors and things; a countdown warning; players needed per enemy; skill and night vision per
enemy; Game Master box (message, lights) in the live page; danger check for the chosen insertion
and for night; pop-in check.

### Built in 0.23 (tested in the editor and the core, not yet in the game)

Messages that take turns; a respawn rule and a respawn trigger action; doors open or closed at the
start and by trigger; hunters (a trigger's enemies walk to the nearest player); order targets
moved to walkable ground; orders and calls in the recording.

### Built in 0.22 (seen in the game on 16 November, see findings)

Countdown told at its start, at five minutes, one minute and ten seconds, each logged; tags on
enemies and a "tagged enemies down" trigger; door trigger; "every player" for zones; repeating
triggers; triggers that call nearby enemies; red messages and {enemies} {players} {time}; skill and
night vision for the whole scenario; Game Master "send selected enemies"; ready-made trigger sets;
try-out "every door opens". Orders: the one-stop patrol route moved enemies in the run of
16 November, some twenty seconds late; they are now told to leave at once.

### B: next

| What | Why this grade | Shown in the game so far | Work |
|---|---|---|---|
| Orders for enemies | The base for hunters and adaptive pacing. | two "sound" calls moved nobody; a one-stop patrol route is the next try | medium |
| A way to fail a mission | Red message, then the game's end operation: built in 0.20.1. | end operation seen; the pair not yet | done once seen |
| Which call marks a run as cheated | Decides whether a mod-ended win can look clean. | one of respawns on, failed, forced extraction | small |
| Hunters from behind, adaptive pacing | Orders work (0.22: send, rally). Next: enemies sent to a spot behind the players, and more or fewer by how the players fare. | orders seen once, late | medium |
| Game Master on another device | A friend runs the mission from a second screen; needs a password and care. | not started | medium |
| Enemies that sit out, shown in the live page | The Game Master sees what the roll left out and can bring it in. | needs no game | small |
| Things in the 3D view, by hand | Things show in 3D but can only be placed and moved in 2D. | needs no game | small |

### C: later

| What | Why this grade |
|---|---|
| Factions | Fun, but few missions need it; try-out not clicked yet. |
| Drone strike, custom wave survival | Clean House by night has a drone and a wave-survival object; each needs a try-out first. |
| Mission type on any map | High value, but the game sets its screens and extraction from the mode at load. After V10. |
| World states | Doors open or broken at the start, single rooms dark, enemy radio jammed. |
| Game Master extras | Player hand signals, puppet actions (an enemy throws a grenade), team status board, hand tools. |
| Pages and campaigns | Briefing page, after-action page and heat map, chained missions, checkpoint. |
| People and goals | Bystanders and a person to rescue, side objectives and par time, stripped start, intel hunt with a clue. |
| Weapon rules, radio cues, complications, objective recipes | Nice to have. |

## Try-outs (0.17)

One click each in the browser page (Mission tab, while a mission runs). Each calls one thing the
game has and reports what happened; the ones that work become real features.

| Try-out | What it calls | Answers |
|---|---|---|
| Message on every screen | `ExfilZone.RPCHint(text, YellowWarning)` | can the mod talk to players without the mod? |
| Lights out / back on | `BreakerSwitch.UserCode_CMD_SetPowerState__Boolean` | lights out |
| Enemies hunt the nearest player | `BrainAI.HeardTarget(position, distance, player)` | orders for enemies |
| Split the enemies into two sides | `TeamIdentifier.NetworkTeamID = 4` on every second enemy | factions |
| Place a thing | `Instantiate` + `NetworkServer.Spawn` of any network prefab | mines, weapons on the ground, intel anywhere |
| Bring every player back | `GameManagerNetwork.SERVER_SetRespawns(true)`, `UserCode_SERVER_RespawnALL()` | respawn rules |
| End as failed / as a success | `SERVER_FailOperation()` / `ServerCompleteExfil()` (with `OperationsManager.HasCheated` set first) | win and lose rules |

## Third round: from research (15 November 2026)

The full report, with sources, is in `research-2026-11-15.md`. Its first picks:

| Idea | What it gives | Needs |
|---|---|---|
| Messages and title card | text on every player's screen as a trigger action | the message try-out |
| Fair-play switch and lobby notice | modded missions earn nothing; joiners are told | a decision; `OperationsManager.HasCheated` |
| Chance and either/or groups | "appears 60 % of the time", "one of these three" | core only |
| Mission check panel | a list of problems before a mission is played | editor only |
| Respawn rules and rally points | the team comes back at a checkpoint | the respawn try-out |
| Enemy profiles | conscript, bodyguard, grenadier, night-vision sentry | `BrainAI`, `WeaponsAI` settings on the host |
| Mission type on any map | target person, find intel, destroy the vehicle | `OperationsManager.CurrentGameMode`; risky |
| Radio calls and alarm as cues | the game's own voice lines when a trigger fires | a map with a helicopter, drone or alarm |
| Trigger logic and "hold the zone" | AND/OR, counters, stay in the circle for N seconds | core only |
| Team status board and hand tools | health and position of every player; teleport, heal, resupply | reads on the host; player calls |

Also from the report: the game has its own roadside-bomb spawner (`RaidManager.hasIEDs`, `IED`,
`IED_locations`, `SERVER_SpawnIEDs()`), a lead for the parked "placed bombs" idea.

## Fourth round: brainstorm (16 November 2026)

Two agents, 43 ideas. One worked outward from what MissionLab already has that other tools lack
(the recording, the live page, the mission check, the generator, the readable file). The other
went through mission editors, Game Master modes and directors of other games. Both read this file
and the research of 15 November first, so nothing here repeats an earlier list. Sources are in
`docs/research-2026-11-16.md`. Nothing in this round is built.

### First picks

Graded by: uses pieces already seen working in the game, small work, friends without the mod notice
it, and it survives a game update (editor and generator work does).

| What | Why first | Work |
|---|---|---|
| Scatter radius, then Area brush | More variety per run from the generator and editor alone. No game call, so V10 cannot break it. | S, then M |
| Host options and Planning assets | A choice before every run, made from either/or sets, doors, lights and placed weapons: all seen working. | S |
| Event table | One new action ("fire one of these, by weight") on top of the repeating timer that fired on time in the last run. | S |
| Debrief in words | The cheapest way for friends without the mod to get something from the recording. Open: does a message still show before the end screen. | S |
| Fix it for me | Every mission check finding gets a repair button. Editor only. | S |
| Sealed missions | Fixes the one weakness of a host-only mod: the host always knows the mission. | S |
| Game Master budget with a keep-out ring, then cards | Makes the live Game Master fair by rule, which is what the studio asked for. Cards and the Fair commander use the same rules. | S, then M |
| Choices you walk into | Zones, doors and items as the only buttons unmodded friends have. Needs either/or decided during the mission. | S |
| Escalation ladder | One scenario, five levels, built like "players needed". | S |
| The runner | Needs one test first: does an alerted enemy obey an order. | M |

Larger, for after those: Dry run, Enemy that remembers, Sleeping enemies, Threat budget, Spree,
Mission as a link, Daily seed, Race your ghost.

Overlaps: Fair commander and Game Master budget are one rule set seen from two sides. Dry run
(in the editor) and Test walk (in the game) answer the same question. Score card, Threat budget
and the Tension graph all need the same measure of how hard a run was.

### What would make MissionLab its own

| What | What you get | Why it is ours | Needs | Work |
|---|---|---|---|---|
| **Dry run** | Walk an old recording or a drawn route through a scenario without starting the game: when triggers fire, who sees the squad, where it breaks. | Needs real routes, sight lines and trigger rules together. Only a tool that records and checks has all three. | Recordings, mission check, 3D map file, trigger logic run in the editor. Unknown: how closely enemy noticing and patrol timing can be modelled. | M |
| **Sealed missions** | The host loads a friend's or a generated mission without seeing it. Only name, check result and difficulty show. It opens after the run. | With a host-only mod the maker is always a player. The check can vouch for a file nobody opened. | Scenario file, mission check, picker, a hidden mode in the page and replay. No game call. It rests on honour: the file stays readable. | S |
| **Game Master by cards** | The page deals three fair moves at a time; the Game Master taps one. Spectators can vote instead. No map skills needed. | Each card is tested first: out of sight, reachable, within the scenario's limits. The check does that already. | Live page, pop-in check with live positions, spawn, orders, lights, doors, messages. Unknown: orders sometimes start late. Votes only when the group opted in. | M |
| **Enemy that remembers** | The next generated layout answers the squad's habits: guards watch the door it always uses, an ambush waits on its usual route. | The generator and the recorder sit in the same tool, so past runs can steer the next seed. | Recordings of several runs on one map, generator, sight lines, danger check as a limit. Unknown: how many runs before habits show; must stay beatable. | M |
| **Debrief in words** | At the end every player reads three lines on screen: time, first seen, closest call. The host gets a text for the group chat. | Friends without the mod see nothing of the recording today. Words on screen reach them and carry no map data. | Recording, mission stats, sight lines for close calls, messages. Unknown: place names need naming rooms once per map; messages must show before the end screen. | S |
| **Mission as a link** | A scenario packed into a link or short text block. Paste it in chat; a friend opens it, sees a spoiler-free card, loads it. | The file is small and holds no map data; each player's own game supplies the map. No workshop or server. | Scenario file, compression in the page, the local link to the game. Unknown: link length limits in chat apps; a hosted copy of the page. | S |
| **Race your ghost** | Split times on every screen at each goal, against the squad's best run. In the replay, two runs of one mission play side by side. | Every run is recorded with its trigger times, and messages can show live numbers to unmodded friends. | Recordings, trigger events, messages with numbers, a fixed seed. Unknown: runs only compare when the roll is the same; shared recordings need a matching version. | S |
| **Score card** | Each scenario gets three numbers before it is played: how hard, how fair, how different two runs will be. Your own results correct them. | The check knows sight lines and the dice; the recordings say how runs really went. | Mission check, 3D map file, chance and either/or data, mission stats. Unknown: which measures predict a hard run; needs twenty or more recordings. | M |
| **Fair commander** | A friend plays the enemy from the page under rules the squad agreed to: a budget, no spawns in view, sight only through his men. | The page already has sight tests and live positions, so the rules can be enforced, not just promised. | Live page on a second device (planned), pop-in check, spawn, orders. Unknown: remote access without sending map data; group consent shown in the lobby notice. | M |
| **Improvise, then keep** | Run a session as Game Master; afterwards the tool turns what you did, and when, into a scenario with triggers that anyone can replay. | The recording holds Game Master actions with their time and where the players stood. | Recording of live spawns, orders, messages, lights, doors. Unknown: not every action is logged yet; the cause of each (place, time, event) needs review. | M |
| **Daily seed** | One generated mission per day and map, the same for every host. Squads compare by a short result code pasted in chat. | Every copy of the game ships the same map file, so a date is enough. No server. | Seeded generator, random extras, mission stats. Unknown: the generator must give equal results on every PC and version; codes can be faked. | S |
| **Fix it for me** | Each mission check finding gets a button that repairs it: move the enemy out of view, turn him, snap to ground, delay the spawn. | The check already knows the cause and the map, so it can search for the nearest spot that passes. | Mission check, walkable ground, sight lines. Also re-fits shared scenarios after a game update moves a map. Unknown: a fix may spoil the maker's intent. | S |
| **Choices you walk into** | Players choose the mission's path in the game: step into one of two marked zones, take one of two items, open one of two doors. | Friends have no mod and no menu. Zones, items and doors are the only buttons they have. | Zone, intel and door triggers, placed things as signs, either/or sets decided mid-mission (new). Unknown: placed intel from another map as a pick-up. | S |
| **Hints that wait** | The maker writes up to three hints per goal. They show only when the squad makes no progress, with distance and direction filled in. | The mod sees positions twice a second and knows the walking distance to each goal. | Messages with live numbers, player positions, walkable ground for route distance. Unknown: a fair measure of "stuck"; whether a message can go to one player. | S |
| **Mission from a text brief** | Describe a mission in a few sentences, get a scenario. The tool hands any chatbot a map card and the format, then checks the answer. | The file is plain and small, and the mission check catches what a language model gets wrong. | Scenario format, mission check, auto-fix, named places per map. Unknown: the map card must hold names and rough spots only, no geometry. | M |
| **Handler who remembers** | A mission voice in text that knows the squad's record: "Third try at this house. Last time you were seen at the gate." | Lines are filled from old recordings on the host's PC. Unmodded friends just see the mission talk to them. | Messages, mission stats over several runs, named places. Unknown: player names in the recording; the 120-character limit; players must find it fun, not nagging. | S |
| **Squad-aware triggers** | Triggers for how the squad moves: running near a guard, splitting up, standing still. And doors that stay open only while someone holds a zone. | Stealth and teamwork rules the game lacks, made from positions the mod reads anyway. | Player positions and speed, zone triggers, door open and close actions. Unknown: closing a door by trigger is built but not yet seen in game. | S |
| **Briefing from the odds** | The briefing is written from the scenario's own chances: "twelve to sixteen hostiles, a truck is likely, the back door may be locked." | Chance and either/or values are in the file, so honest but uncertain intel costs the maker nothing. | Briefing lines, chance and either/or data, danger check facts, named places. Unknown: four short lines is little room. | S |
| **Dead weight report** | After several runs the editor lists what the mission never used: guards who never saw anyone, triggers that never fired, doors nobody opened. | The mission check says what can go wrong; recordings say what did. Other hosts can send theirs back. | Recordings tied to a scenario version, enemy states, trigger events. Unknown: whether door use is in the recording today. | S |
| **Drill loop** | One room, new enemy positions each round, a stopwatch on screen: "Round 4: 11 s, best 9 s." The replay cuts itself into rounds. | Repeat triggers, chance, respawn and the recording already exist; together they make a training tool. | Repeat and enemies-down triggers, respawn, door close, messages with numbers. New: a fresh roll each round. Unknown: sending players back to the start line. | M |
| **Squad tablet** | Each friend opens a page on a phone: goals, the clock, found documents, and a map view only when the mission grants it. | The mod cannot add screens to the game, but it already serves a page. Friends need no install. | Live page, triggers. Unknown: how friends reach the host (relay or tunnel). No map data is sent: text and dots, or the viewer's own file. | L |
| **Director's cut** | One button cuts a recording to a minute: first contact, the closest call, the last enemy. Played with moving camera and captions, saved as video. | The recording has events with times, so the page can pick the moments without anyone editing. | Recording, events, the page's map view, video capture in the browser. Unknown: whether a clip showing the map may be shared; fallback: a plain grid. | M |

The agent's top five: Dry run, Sealed missions, Game Master by cards, Enemy that remembers,
Debrief in words.

What MissionLab would be if these were built, in one line (three tries):
- MissionLab is a mission tool that learns from every run: it records what happened, tests new missions against it, and builds the next one to fit your squad.
- MissionLab lets one host give friends without any mod a mission that nobody in the squad has seen, checked for fairness before it starts.
- MissionLab is a browser page that writes, checks, runs and replays missions for OPERATOR, with a Game Master seat that plays by rules.

### Taken from other games

| What | What you get | Seen in | Needs | Work |
|---|---|---|---|---|
| **Planning assets** | Before the mission the team picks two helps from the maker's list: a side door unlocked, lights out, a weapon cache, a later alarm. | Payday 2 pre-planning | Doors, lights, placed weapons and trigger delays, switched by the host's picks. Unknown: how friends see the list before the start. | S |
| **Area brush** | The maker paints areas as full, light or empty. Random layouts follow the paint, so each run differs but the plan holds. | ALiVE placement areas and blacklists (Arma 3) | The seeded generator and the editor only. No game call. Unknown: nothing; needs a polygon tool on the 2D map. | M |
| **Game Master budget** | Spawning costs the Game Master points that refill slowly; moving enemies is free. Nothing can be placed near players. | Arma 3 Zeus curator points and editing areas; Arma Reforger Game Master budgets | Live console only: a cost per enemy type, a keep-out ring from player positions. Unknown: good costs and refill rate. | S |
| **The runner** | When alerted, a marked guard runs for an alarm point; if he arrives, help comes. A target can run for an exit the same way. | Far Cry 3 outpost alarms; Hitman Freelancer (the leader flees); Halo Infinite Forge "squad reached zone" event | Alert trigger, send-to-spot order, tags, waves, end-as-failed. New: "enemy reached a spot" check. Unknown: whether an alerted enemy obeys the order. | M |
| **Sleeping enemies** | Enemies far from every player are not in the game yet. They appear, unseen, when someone comes near. Missions can hold more enemies. | Arma 3 Dynamic Simulation; ALiVE virtual profiles | Spawn and remove calls, player positions, pop-in check. Unknown: cost of many spawns at once; patrols lose their place. | M |
| **Host options** | The maker offers up to three choices per scenario, such as garrison size or alarm on/off. The host sets them in the picker. | Arma 3 mission parameters in the lobby | Either/or sets and chance values, set by choice instead of by roll. Picker in the browser page. Unknown: nothing. | S |
| **Threat budget** | Random layouts spend points, not a head count. A truck or a veteran costs more. The budget grows with the number of players. | D&D encounter XP budget; Deep Rock Galactic hazard table by player count | Generator, enemy types, skill, gun trucks, player count. The editor can show the same total. Unknown: the costs; fit them to recordings. | M |
| **Event table** | Every few minutes the mission rolls on a weighted list: a patrol arrives, lights go out, a door locks, a cache appears, or nothing. | Tabletop wandering-monster checks; Foundry VTT roll tables | Repeating timer plus one new action: fire one of these triggers by weight. Alert state can add rolls. Unknown: nothing. | S |
| **Scatter radius** | An enemy with "scatter 8 m" starts at a random walkable spot within 8 m, new each mission. | Arma 3 Eden Editor (Placement Radius) | Core only: pick a navmesh point at the roll. The danger check must test the whole circle. Unknown: nothing. | S |
| **Find the right man** | Several suspects, one real target. Intel gives clues such as floor or uniform. Shooting the wrong one raises the alarm. | Hitman Freelancer showdowns | Tags, "tagged enemies down", intel trigger, messages, enemy types as looks. New: roll which suspect is real. Unknown: how different types look. | M |
| **Search the building** | An order for the Game Master or a trigger: chosen enemies walk through every room of a building, then go back to their posts. | Ares "Search Building" Zeus module (Arma 3) | Patrol routes (seen walked), the order call, a route made from the navmesh. Unknown: several-stop orders; rooms must be guessed from walkable ground. | M |
| **Building fill tool** | Click a building and give a number. The editor puts that many guards at doors, windows and corners, facing outward. | Ares and ACE3 "Garrison" Zeus modules (Arma 3) | Editor only: navmesh, door positions, the 3D map file. Unknown: finding windows from collider geometry may be rough. | M |
| **Contract from a run** | After a run, mark up to three enemies you downed as targets. Layout, targets and your time become a challenge for others. | Hitman contracts mode; Super Mario Maker (clear it before upload) | Mission record, recording, tags, "tagged enemies down", countdown. Unknown: whether the recording says how a mission ended. | S |
| **Quick mission form** | One form with a few sliders: force size, share that patrols, extras. Preview on the map, roll again, then play. | SWAT 4 Quick Mission Maker; ALiVE placement module settings (force size, readiness) | Generator and seed, random extras, the live page asking the mod for a layout. Unknown: nothing; the generator only runs in the mod today. | M |
| **Escalation ladder** | One scenario, five levels. Each win unlocks the next level, which adds a patrol, a locked door or a time limit. | Hitman Escalations | A "from level N" field on enemies, doors and triggers, like players-needed. Level saved per scenario. Unknown: nothing. | S |
| **Tension graph and lull flags** | After a mission, a graph of pressure over time. Flags mark quiet stretches that ran too long and spawns close to players. | Left 4 Dead survivor intensity; Zeus after-action questions | The recording only: enemy states, enemies down, distances. Unknown: shots and player health are not recorded, so pressure is an estimate. | S |
| **Squad that hunts by hints** | One squad keeps searching near the team, not at it. After a fight it pulls back and gives a breather. | Alien: Isolation director and menace gauge | Send-to-spot order with a random offset, player positions, alert state. Unknown: order delay; pulling back enemies that are fighting. | M |
| **Optional event with a reward** | A marked spot offers a hard timed fight. The team may start it. Winning brings dead players back or opens a cache. | Deep Rock Galactic machine events | Hold-the-zone trigger, waves, countdown, respawn and place-things actions. A ready-made set. Unknown: nothing beyond those. | S |
| **Test walk** | Solo only: enemies neither see nor hear while the maker walks the mission, checks zones and fires triggers by hand. | Halo Infinite Forge test settings ("Undetectable to AI", "Deathless") | Per-enemy sight and hearing values (already set by the mod), "Fire now". Refused when friends are in. Unknown: whether tiny values blind fully. | S |
| **Spree** | Random missions back to back. Pick the next from three. Each win adds a rule. Shared lives; a wipe ends it. | Payday 2 Crime Spree; Deep Rock Galactic Deep Dives; Super Mario Maker 2 Endless Challenge; SWAT: Elite Force hardcore campaign | Generator, scenario picker, respawn rule, a small state file. Unknown: friends cannot see the three cards; host reads them out. | M |
| **Director moods** | The host picks how surprises are spread: steady build, long breathers, or wild. Same mission, different rhythm. | RimWorld storytellers | Presets over wave timing, the event table and hunters. Depends on adaptive pacing, which is not built yet. | S |

The agent's top five: Planning assets, Area brush, Game Master budget, The runner, Sleeping enemies.

## Parked

- **Arriving by car on a new route.** The game's rides are fixed animations that belong
  to a map. A new route needs an animation that does not exist. A car-driving library
  sits in the game's files, but no map seen so far uses it.
- **Reinforcements by vehicle.** The game has the system (`aiReinforcement`), but none of
  the three maps recorded uses it. Waves on foot work today through zones.
- **Placed bombs.** `ProximityExplosive` (set off by a phone) exists in the code, but none
  of the maps recorded ships one to copy.
