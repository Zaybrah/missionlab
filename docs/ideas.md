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
| Orders for enemies | `BrainAI.HeardTarget(position, ...)`, `CalledBackup(position, ...)`, `SetNewAiState` |
| Win and lose rules | `GameManagerNetwork.ShowMissionCompleteScreen`, the mission-failed popup |
| Mines and other placeable things | `Landmine`, `ProximityExplosive`, `N_WeaponSpawner`; anything in `NetworkManager.spawnPrefabs` |
| Factions | `TeamIdentifier.TeamID` (synced) on enemies and players, `TeamManager.GrabAllEnemies` |
| Weapon rules | `RaidManager.prohibitedWeapons`, `mapSpecificWeapons` |
| Adaptive pacing | built from triggers and the alert state the mod already reads |
| Game Master on another device | the browser link opened to the local network behind a password |

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
