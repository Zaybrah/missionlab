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

## Not yet verified
- Co-op with unmodded clients, HVT/intel/asset modes, maps other than Suburb and Mexico Depot.
