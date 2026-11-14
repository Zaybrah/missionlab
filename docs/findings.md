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
- Collider layers seen: Default, Shrapnel_Ignore, AI_VisionBlock, Water, PlayerBlock,
  IgnoreFakeGI, Interactable. PlayerBlock holds the invisible walls. "Has a renderer nearby"
  does not tell visible from invisible shapes.

## Not yet verified
- Moved intel as seen by an unmodded client; HVT/intel/asset modes in co-op; maps other than
  Suburb, Mexico Depot and Georgia.
