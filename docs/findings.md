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
- Objective items: `RaidManager.objectives` is an array of sets; each set lists candidate
  `ObjectiveObject`s and the game leaves one active. They are plain scene objects.
- Collider layers seen: Default, Shrapnel_Ignore, AI_VisionBlock, Water, PlayerBlock,
  IgnoreFakeGI, Interactable. PlayerBlock holds the invisible walls. "Has a renderer nearby"
  does not tell visible from invisible shapes.

## Not yet verified
- Co-op with unmodded clients, HVT/intel/asset modes, maps other than Suburb and Mexico Depot.
