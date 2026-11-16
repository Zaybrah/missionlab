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

### Built in 0.22 (tested in the editor and the core, not yet in the game)

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
| Respawn rules and rally points | Co-op friends stop watching after a death. Touches players, so V10 may break it. | the call did nothing to a living player | medium |
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

## Parked

- **Arriving by car on a new route.** The game's rides are fixed animations that belong
  to a map. A new route needs an animation that does not exist. A car-driving library
  sits in the game's files, but no map seen so far uses it.
- **Reinforcements by vehicle.** The game has the system (`aiReinforcement`), but none of
  the three maps recorded uses it. Waves on foot work today through zones.
- **Placed bombs.** `ProximityExplosive` (set off by a phone) exists in the code, but none
  of the maps recorded ships one to copy.
