# What we know about OPERATOR's mission internals

Verified on game version 0.09.5 (Unity 6000.3.8f1, game code dated 7 July 2026),
BepInEx 6.0.0-be.785. Everything here came from running the plugins in this repo.

## Spawning
- `RaidManager` is the per-map mission director. It fills `botSpawnPoints` during
  `OnStartServer` (the list is still empty in a prefix on that method) and spawns
  enemies about 2 seconds later (`botSpawnDelay`).
- The game spawns exactly `GameManager.botAmount` bots, each on a randomly drawn
  entry of `botSpawnPoints`. The list itself is not consumed.
- Each spawn point is a GameObject with a `BotSpawnDetails` component (idle state,
  react type, detection/hearing range, FOV, patrol points, flags).
- Replacing the list's contents in place (Clear + Add) before the spawn works: bots
  appear exactly on the new points. Tested solo as host on Suburb Day.
- Pitfall: individual authored points can be special cases (`disableNavmesh = true`,
  double detection range). Copy settings from the most common values, not one point.

## Maps
- 31 scenes; day and night are separate scenes. `OPERATOR_Data/NavMeshData/*.bytes`
  holds A* Pathfinding Project 5.3.8 recast-graph caches (zip). `graph0_extra.binary`
  layout per tile: x, z, w, d, tris[], verts[] (Int3, mm), vertsInGraphSpace[],
  one flag byte (if set: preCutTags[], preCutVerts[], preCutTris[]), nodes[] (20 bytes each).
- A file belongs to a scene when its `forcedBoundsCenter/Size` and tile counts equal
  the live `RecastGraph`'s. "Spawn points stand on the mesh" is NOT enough: flat
  ground at similar heights exists on several maps.
- Suburb: all vanilla enemy spawn points sit in one house. Other lots are fenced
  off and not connected to the walkable mesh.
- Insertion points are child transforms of each `InfilMethods.infilObject`.

- When the list holds exactly `botAmount` points, every point gets an enemy
  (11 of 11 within 0.1 m of a scenario file's positions).

## Enemies at run time
- `BrainAI.transform.position` stays at the spawn position for the whole mission.
  The real position is `BrainAI.agent.position` (`AgentController`, an A* `FollowerEntity`).
- Patrol routes built from child transforms in `BotSpawnDetails.patrolPoints` are
  walked (two patrols covered 40 m and 44 m in 90 s, waypoint index advancing).
  Wanderers start moving after about 25 s. `PatrolSpeed = 0` and
  `WanderDistance = -1` on a spawn point mean "use the default".
- Dead enemies are removed from `GameManager.allAI`.

## Spawning during a mission
- Works through the game's own spawner: put one spawn point in `botSpawnPoints`,
  set `botAmount = 1`, call `RaidManager.ServerSpawnAI(false)`, restore both.
  The enemy appears on the point, is registered in `allAI`, fights and can be killed.
- Does not work: `InstantiateBot` + `ApplyBotSpawnSettings` alone. That enemy is
  visible but never enters `allAI` and cannot be killed.
- `RaidManager.CanSpawn(pos, radius)` returned true for every radius tried, even
  on the host's own position.
- The enemy type can be chosen: put one prefab from `GameManager.AllAITypes` in
  `RaidManager.customAI` and call `ServerSpawnAI(true)`; restore the list afterwards.
  The enemy exists within a frame or two (0.02 to 0.05 s).

## Other maps
- Mexico Depot Day: 31 enemies on 31 generated points, 4 patrols walking, 19 of 20
  wanderers moving. The map's vehicle crews (5 enemies, no pathfinding agent) are
  spawned separately from `botSpawnPoints` and are not affected.

## Input, camera, geometry
- Every key bind of the game goes through `InputLayer.GetKey(InputKey, bool)`. A Harmony
  prefix that returns false there silences all of them. `InputLayer.InputDisabled` is
  no use for this: the game sets it back every frame.
- `MainCameraSingleton.instance.freeCamera` (`ScreenshotFreeCamera`) is a built-in
  noclip camera; `ToggleFreeCamera()` switches it. The picture still comes from the
  main camera ('GAME CAMERA'), so `MainCameraSingleton.MainCam` is the one to project with.
- A spawned enemy faces the way its spawn point is rotated (agent yaw equals planned yaw).
- `NetworkServer.Destroy(bot.gameObject)` removes an enemy cleanly. Spawning onto the
  same spot in the same frame fails; half a second later it works.
- Map geometry can be read at run time: of 140 distinct collider meshes on Suburb, 95
  were readable from main memory and 45 through `Mesh.GetVertexBuffer` +
  `GraphicsBuffer.GetData`; none failed. Two invisible boxes 20 km wide wrap the map.

## Targets, objectives, layers
- Target person (mode `PVE_HVTKILL`, seen on Georgia): `RaidManager.hvtSpawnPoints` is an array of
  six ordinary spawn points (with `BotSpawnDetails`), `hvtAI` holds the target's type, and
  `GameManager.botHVTAmount` says how many. The target spawned on one of the six and stayed there.
- Objective items: `RaidManager.objectives` is an array of sets (`ObjectiveSetter`, with a `name`
  such as "Cell Phone"); each set lists candidate `ObjectiveObject`s and the game keeps one per
  mission. The others are already gone 0.7 s after `OnStartServer`. They are plain scene objects.
  Clean House: "Cell Phone_1" (-16.66, 1.41, 4.72) or "Cell Phone_2" (-17.89, 4.40, 8.42);
  "Sim cards_1" or "Sim cards_2" (-17.82, 4.40, 8.55). A scenario that names one copy exactly
  misses whenever the game picks the other, so items are matched by set (0.13.1).
- An item has no position sync of its own. Other players get its place in the spawn message
  (`SpawnMessage.position`), sent when their game becomes ready after loading. Moving it inside
  the `OnStartServer` hook is therefore early enough; for a later move the host sends the spawn
  message again (`NetworkServer.SendSpawnMessage`), which a client applies to the object it
  already has. Not yet confirmed in a co-op session.
- Co-op (0.13.0, one unmodded client, Clean House): the client can join; enemies from a
  scenario, enemies spawned later through `ServerSpawnAI`, and zone spawns all appear for it.
- Doors are `DoorV2` (a `NetworkBehaviour`; `Door` is empty and `DoorScript` is an unused asset).
  Lock state is `CurrentlyLocked`, a SyncVar, set with `Server_SetDoorLocked(bool)`. A door rolls
  its own state from `StartLocked` / `StartLockedChance`, and `AiCantOpen` / `AiCantOpenChance`
  keeps enemies from opening it. `PivotTransform` is the hinge, `DoorHitBox` the leaf's box
  collider, `LatchHealth` and two hinge healths with `ShootableDoorPart`s take the damage, and
  `DoorOpenableNavLink` / `DoorWalkableNavLink` are the A* links through the doorway. From the
  class listing only: when a door rolls, and the scale of the two chances, are not yet seen in game.
- Enemy types: `GameManager.AllAITypes` is one list for the whole game (17 entries, the same on
  Suburb, Mexico Depot and Georgia), including "BOT V2 Insurgent Afghan 1 Suicide Bomber Variant"
  and "BOT V2 Russian Spetnaz".
- Other systems present in the code: `InfiltrationManager.infilMethods` (per map, some flagged
  `VehicleInfiltration` with fixed animation clips; Suburb: Alpha 1-3 on foot, Hotel 1 a ride),
  `aiReinforcement` + `ReinforcementTrigger` (no map seen so far uses them), `VehicleAI` gun
  vehicles (Mexico Depot: 1 type, 6 spots), `ProximityExplosive` (no map seen ships one),
  `ExfilZone.canExtract` (SyncVar), `AlarmSystem`, `VectorTimeOfDay` (hour, rain, sandstorm).
- Insertion is each player's own choice in the mission (`PlayerMaster.CMD_RequestInfilTarget(int)`,
  `CurrentInfilIndex`, UI in `InfilSelectorDisplayer` with `SetInfilEnabled(index, bool)`).
- Extraction: `RaidManager.EnableGlobalExtraction()` / `RPC_CanExfil()`, `ExfilZone.canExtract`.
- Vehicles: `RaidManager.SpawnStaticVehicleAI()` uses `spawnVehicleAI`, `maxStaticVehicles`,
  `staticVehicleSpawnPoints` and `staticVehicleAI` ("Hilux Technical Dshk" on Mexico Depot).
- `VectorTimeOfDay.singleton` is null on Suburb, Mexico Depot and Georgia: no weather there.
- Clean House has 10 doors (7 interior, 2 exterior, a fence gate). `StartLockedChance` counts
  to 100 (seen: 0, 50, 90). Setting a door live works (`Server_SetDoorLocked`, `AiCantOpen`).
- First run of 0.15.0 (solo, Clean House, feature test scenario):
  - A door set in the `OnStartServer` hook holds: the game rolls its own doors after the hook
    (1 locked at the hook, 3 a few seconds in) and leaves ours as set.
  - The alert trigger fired 10 s after the first enemy noticed the player, as planned.
  - The suicide bomber works off its own map. It noticed the player at about 50 m, ran to
    them and blew up 16 s after it appeared.
  - `SpawnStaticVehicleAI()` places a vehicle on a map that has none, once our spots and a prefab
    are set and the method is called. The first armed vehicle in `NetworkManager.spawnPrefabs`
    is a "BTR 82A", so the pick-up has to be asked for by name.
  - The insertion is chosen in the lobby (`OperationBoardUI`, `MissionLaptopNetworkState.ActiveInfilMask`)
    and carried into the mission in `PlayerMaster.CurrentInfilIndex`; `CMD_RequestInfilTarget` is not
    sent during the mission, so hooking it changes nothing. 0.15.1 writes the index itself.
  - The recording was written: 61 moments, 5 events.
- Second run (0.15.2, solo, Clean House by day, feature test scenario, 4:55 long):
  - The insertion can be set: the player started on Alpha 2's spot (-40.3, 0.6, -61.2). The index
    was written when the mission was set up; the second write, just before the insertion, found it set.
  - Timer and chain triggers fire on time (60.2 s and 75.2 s), and the chain trigger opened the
    extraction on the host. Nobody walked to it, so "open for the player" is still unseen.
  - **A new enemy reports a state other than idle for its first half second** (`CurrentState`
    reads `Evade`). The alert trigger took that for an alert and fired 10 s after the first enemy
    appeared; the recording showed the same false state. From 0.17 an enemy's state counts only
    once it has been around for 2 s; the editor reads old recordings the same way.
  - **The game's vehicle spawner does not always bring a vehicle.** With one spot, one prefab
    ("Hilux Technical Dshk"), `maxStaticVehicles = 1` and `spawnVehicleAI = true`, no `VehicleAI`
    existed 3 s after `SpawnStaticVehicleAI()`. (The first run got its BTR the same way.)
    `RaidManager._currentVehicleCount` suggests the spawner counts or rolls. From 0.17 whatever is
    missing 3 s later is placed directly from the same prefab with `NetworkServer.Spawn`.
  - Clean House has one `BreakerSwitch` ("PowerBox" at -16.7, 0.0, -18.6) wired to 59 lights.
    No alarm, landmine or weapon rack in the map.
  - Teams: players are team 1, enemies team 2; the game knows USA (0), BLUFOR (1), OPFOR (2),
    NotApplicable (3), RU (4).
  - `NetworkManager.spawnPrefabs` holds 125 prefabs: 45 weapons, 26 enemy types, 16 intel items
    (Hard Drive, Thumbdrive, RFID Card, the Mexico documents ...), grenades (9 Bang, FlashBang, M67,
    Slap Charge), `Landmine`, `Proximity Explosive_IED`, doors, two drones (`Satelite ISR`, `UAV ISR`),
    an `AGM-114 Hellfire`, helicopters (UH-60, Littlebird, MH-6), the BTR and the Hilux.
  - The extraction zone "Exfil" is one box of 67 x 124 m over the east side of the map.
- Third run (0.17.0, solo, Clean House by night, feature test scenario, 7:22 long, all six enemies down):
  - The gun truck came from the game's own spawner this time ("Hilux Technical Dshk" at -4, 0.2, 54)
    and was seen in the game. So the spawner brings a vehicle on some runs and none on others.
  - **A gun truck's gunner is an enemy of his own** ("BOT V2 Cold Insurgent 03 Variant", no
    navigation agent) and reports `Evade` for as long as he lives. He set the alert trigger off
    10 s after the truck appeared. From 0.17.1 a state counts only once it has differed from the
    one the enemy was first seen in, and a truck counts as alerted when `VehicleAI.CurrentTarget` is set.
  - The intel trigger fired when the first item was picked up and changed its door.
  - Try-outs that ran: "lights out" (the game then reports `PowerOn` false; 23 lights are wired
    to the breaker at night, 59 by day), "place a thing" (two `Flashbang`, one `Landmine`; no
    error), "end as a success" (`ServerCompleteExfil()` ended the mission 2.5 s later, with
    `HasCheated` set first). What each looked like in the game is not known yet.
  - Not tried yet: message, hunt, two sides, bring players back, end as failed.
  - Clean House by night holds 88 `DynamicLight`, one drone (`ISR`), one `WaveSurvival` object,
    no destructibles. Enemy sight at night is 25 m (50 m by day).
- Seen by the player in the third run: "lights out" makes the house go dark. The two flashbangs
  and the landmine placed by hand were not seen (neither on the ground nor going off); whether
  they were there is open, so from 0.18 the try-out looks at a placed thing again 2 s later and
  reports whether it is still there, where, and how many of its parts are drawn. After
  `ServerCompleteExfil()` with `HasCheated` set, the end screen says that cheating was detected and
  that there are no achievements: the flag does what its name says.
- Fourth run (0.19.0, solo, Clean House by night, two missions):
  - **Placed weapons work.** An `AKM` and an `AK-74M` spawned from the network prefab list lay
    where they were put, were drawn, and the player could pick one up and use it.
  - **Two sides fight.** After `TeamIdentifier.NetworkTeamID = 4` on every second enemy, a guard
    shot the guard next to him within 2 s and the gun truck's gunner shot two more. The end
    screen counted 4 kills, 1 by the player.
  - `BrainAI.HeardTarget` did nothing that could be seen: no enemy changed state or moved towards
    the player in the 12 s after it. 0.19.1 adds `GameManager.ShotFired` as another way.
  - `UserCode_SERVER_RespawnALL()` did nothing to a living player. `SERVER_FailOperation()` did
    not end the mission (it ran on for 10 s until the forced success did).
  - **The game marks a run as cheated by itself.** With `FairPlay = Off` the mod set no flag, yet
    the end screen said "cheating detected, not saving score". One of the calls above makes the
    game set `HasCheated` (respawns on, the failed call or the forced extraction). 0.19.1 watches
    the flag and logs what the mod did last when it flips. `OperationsManager.DevCheatBypass` and
    `GameManager.DevBypassCheatCheck()` exist; not used.
  - Chance and either/or rolled differently in the two missions (option B, then option A).
  - The gun truck fallback works: the game's spawner brought none both times, the plugin placed it.
  - A false alert again in the first mission, 1 s after the first enemies appeared: an enemy can
    report "Idle" for a moment before its "Evade", so "differs from the first state" was not
    enough. 0.19.1 trusts nothing for 2 s and takes the state then as the enemy's state of rest.
  - Trigger messages were sent twice ("a message is shown"); whether they appeared is not known yet.
- Seen by the player after the fourth run: the two trigger messages appeared on screen
  (`ExfilZone.RPCHint` works as a message to players). `SERVER_FailOperation()` shows the failed
  screen for a moment and closes it again while a player is alive; `GameManagerNetwork` also has
  `CloseAllFailScreens()`, `EndOperation()` and `RestartOperation()`, not tried yet.
- Fifth run (0.20.0, solo, Clean House by night, 5:30):
  - From a scenario: a rifle lay at its spot from the start; "hold the zone" fired after 15 s in
    the doorway and placed its weapon; "enemies down" fired when two were down; the two enemies
    it brought were put on team 4 as they appeared and a firefight between the sides followed.
  - `GameManager.ShotFired` moved nobody either: four enemies 21 to 28 m away kept their state
    and their distance. 0.20.1 gives enemies a one-stop patrol route instead (`BrainAI.aiPatrol`,
    `idleStates = Patrol`).
  - `GameManagerNetwork.EndOperation()` takes the player back to the lobby with no result screen.
    0.20.1 uses it for the failed ending: a red "Mission failed." and, 5 s later, the end.
  - The gunner reported `WasSighted` at rest this time (`Evade` before), so the alert trigger
    fired falsely once more. `VehicleAI.turrentBot` is the gunner; from 0.20.1 his state is never read.
  - No "cheated" mark in this run, which used none of: respawns on, the failed call, the forced
    extraction. One of those three sets it.
- Collider layers seen: Default, Shrapnel_Ignore, AI_VisionBlock, Water, PlayerBlock,
  IgnoreFakeGI, Interactable. PlayerBlock holds the invisible walls. "Has a renderer nearby"
  does not tell visible from invisible shapes.

## Run of 16 November 2026 (0.21.0, Clean House by night)

- **Orders by patrol route work, late.** Setting `aiPatrol` to a one-stop route and `idleStates` to
  Patrol moved idle enemies to the spot, but only some twenty seconds after the order (three orders
  at 313 s, 340 s and 357 s; movement from about 365 s). 0.22 also sets `patrolWaitTime = 0`,
  `onReachDestination = MoveImmediately` and `patrolTimer = 0`. Whether they now leave at once is open.
- **The countdown was not due.** Intel at 172.6 s plus a wait of 300 s puts the first warning at
  412 s; the mission was ended by hand at 396 s. 0.22 announces the clock when it starts.
- **Skill is applied.** Log: veteran reaction 0.24 to 0.14, inaccuracy 0.78 to 0.47, hit chance 1.00
  to 1.35; recruit reaction 0.23 to 0.36, inaccuracy 1.89 to 3.02, hit chance 0.80 to 0.48. One
  enemy more or less sharp was not noticed in play; 0.22 can set it for all enemies.
- **Failed ending:** red message, then `EndOperation()` five seconds later: ran as written.

## Second run of 16 November 2026 (0.22.0, Clean House by night)

- **Countdown:** all three lines shown (start, one minute, ten seconds), then "Out of time." and the
  failed ending; the player saw the popup.
- **Repeat:** a timer with `repeat: 90, times: 3` fired at 60 s, 150 s and 240 s and then stopped.
- **Door trigger:** fired when the player opened the door. `IsOpenEnoughForAI` is the test.
- **Tagged enemies down:** fired when the tagged guard went down. Its message came 3 s before
  another one and was not seen: messages replace each other (0.23 spaces them).
- **Respawn works on a dead player.** `SERVER_SetRespawns(true)` + `UserCode_SERVER_RespawnALL()`
  brought the dead host back within a second (dead from 207.9 s, call at 220.6 s, back at 221.1 s).
  The cheated flag was not set at that moment.
- **Every door opens:** `UserCode_CMD_SetDoorClosed__Boolean(false)` opened all ten doors.
- **Game Master order:** two selected enemies, one of them a guard that had not moved all mission,
  set off within the same half second and walked to the clicked spot at patrol pace (about 1 m/s).
- **Call by trigger did not move anyone** in the 18 s before the alarm. Its target was the door's
  hinge, which is not walkable ground; 0.23 moves targets to the nearest walkable point.
- **Skill for all (recruits):** the player thought enemies were slower and missed more.
- An enemy turned or moved on the page is taken out and made again (the 0.7 s "re-make"); in the
  recording that reads as "down" followed by "appears".

## Not yet verified
- Moved intel as seen by an unmodded client; HVT/intel/asset modes in co-op; maps other than
  Suburb, Mexico Depot and Georgia.
