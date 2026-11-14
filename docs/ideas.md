# Ideas

What could come next, with what the game already offers for each. "Odds" is the chance it
works as hoped. Everything here comes from the game's class listing and from recon files;
none of it has run in the game unless it says so.

## In progress

| Idea | What the game has | State |
|---|---|---|
| Locked doors | `DoorV2`: a synced lock, a "starts locked" chance, "enemies cannot open", latch and hinges that take damage | Built in 0.14.0, waiting for a test |
| Bombers on any map | The suicide bomber is in the game-wide enemy type list | Can be chosen today; not yet tried off its own map |

## Next, in this order

1. **More triggers.** When: an enemy spots a player, an intel item is picked up, the
   target dies, a timer runs out. Then: enemies appear (exists), a door locks or unlocks,
   an alarm starts. All of the "when" side can already be read. Odds: high.
2. **Random missions that use all of it.** The generator also rolls zones, intel spots on
   table tops and locked doors, so a mission is never the same twice. Odds: high once
   doors and triggers exist.
3. **Extraction rules.** `ExfilZone.canExtract` is synced to all players: extraction opens
   only after both items are taken, or after a timer. Odds: medium.
4. **Choosing the insertion.** Each map lists its insertions (Clean House: three on foot,
   one ride). A scenario picks one. Odds: high.
5. **Weather and time.** Hour of day, rain and sandstorm are switches on
   `VectorTimeOfDay`. Whether other players see the same is unknown. Odds: medium.
6. **Replay after the mission.** Positions are already recorded; the browser plays the
   mission back on the map. Odds: high.
7. **Enemy gun vehicles.** Mexico Depot has them (six parking spots). Placing them there
   should work; bringing one to another map may not. Odds: medium there, low elsewhere.

## Parked

- **Arriving by car on a new route.** The game's rides are fixed animations that belong
  to a map. A new route needs an animation that does not exist. A car-driving library
  sits in the game's files, but no map seen so far uses it.
- **Reinforcements by vehicle.** The game has the system (`aiReinforcement`), but none of
  the three maps recorded uses it. Waves on foot work today through zones.
- **Placed bombs.** `ProximityExplosive` (set off by a phone) exists in the code, but none
  of the maps recorded ships one to copy.
