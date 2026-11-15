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

## Parked

- **Arriving by car on a new route.** The game's rides are fixed animations that belong
  to a map. A new route needs an animation that does not exist. A car-driving library
  sits in the game's files, but no map seen so far uses it.
- **Reinforcements by vehicle.** The game has the system (`aiReinforcement`), but none of
  the three maps recorded uses it. Waves on foot work today through zones.
- **Placed bombs.** `ProximityExplosive` (set off by a phone) exists in the code, but none
  of the maps recorded ships one to copy.
