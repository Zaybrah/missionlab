using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using MissionLab.Core;
using UnityEngine;
using Il2Generic = Il2CppSystem.Collections.Generic;

namespace MissionLab
{
    /// <summary>
    /// Decides and applies the enemy layout for one mission.
    /// Order of work: read what the map offers, generate a plan (pure code), build
    /// every new spawn object, and only then swap them into the game's list. If
    /// anything fails before the swap, the mission is left exactly as shipped.
    /// </summary>
    internal static class Director
    {
        /// <summary>The mission we are finished with (changed, or deliberately left alone).</summary>
        internal static IntPtr HandledRaidPtr = IntPtr.Zero;
        internal static string Banner;
        internal static float BannerUntil;

        // What the Game Master tools need to know about the mission in progress.
        internal static IntPtr CurrentRaidPtr = IntPtr.Zero;
        internal static GameObject CurrentTemplate;
        internal static NavMesh CurrentNav;
        internal static float CurrentDetect, CurrentHear, CurrentFov;
        internal static float HandledAt;
        internal static MissionPlan CurrentPlan;            // the layout this mission started with
        internal static string CurrentScenarioPath;         // the file it came from, or null for a generated layout
        internal static string CurrentScenarioScene;        // that file's own "scene" value
        private static string _lastScenarioPath;

        private static IntPtr _configReadFor = IntPtr.Zero;
        private static IntPtr _waitingPtr = IntPtr.Zero;
        private static float _waitingSince;
        private const float WaitForSpawnListSeconds = 8f;

        /// <summary>
        /// Called from the hook and, every frame until the mission is handled, from the
        /// polling fallback. The game fills its spawn list during start-up, so an empty
        /// list means "not yet": we simply come back next frame.
        /// </summary>
        public static void OnRaidStart(RaidManager raid, string via)
        {
            if (raid == null || raid.Pointer == HandledRaidPtr) return;
            IntPtr ptr = raid.Pointer;
            if (_configReadFor != ptr)
            {
                // Read the settings file again for every mission, so edits apply without restarting the game.
                _configReadFor = ptr;
                try { DirectorPlugin.Settings.Reload(); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("settings file could not be re-read (" + e.Message + "); keeping the previous settings"); }
            }
            bool done;
            try { done = TryApply(raid, via); }
            catch { HandledRaidPtr = ptr; throw; }       // never retry after a failure
            if (done) { HandledRaidPtr = ptr; HandledAt = Time.unscaledTime; _waitingPtr = IntPtr.Zero; return; }

            if (_waitingPtr != ptr) { _waitingPtr = ptr; _waitingSince = Time.unscaledTime; }
            else if (Time.unscaledTime - _waitingSince > WaitForSpawnListSeconds)
            {
                DirectorPlugin.L.LogWarning("the mission's enemy spawn list never appeared; mission left unchanged");
                HandledRaidPtr = ptr; _waitingPtr = IntPtr.Zero;
            }
        }

        /// <summary>Returns true when this mission is finished with, false to try again next frame.</summary>
        private static bool TryApply(RaidManager raid, string via)
        {
            var log = DirectorPlugin.L;

            if (!DirectorPlugin.Enabled.Value) { log.LogInfo("disabled in config; mission left unchanged"); return true; }
            if (!Mirror.NetworkServer.active) { log.LogInfo("not hosting; mission left unchanged"); return true; }
            string scene = raid.gameObject.scene.name ?? "";
            if (!SceneAllowed(scene)) { log.LogInfo("scene '" + scene + "' is not listed in General/Scenes; mission left unchanged"); return true; }

            OperationsManager om = OperationsManager.singleton;
            if (om != null)
            {
                var mode = om.CurrentGameMode;
                if (mode == OperationsManager.GameMode.PVE_WAVESURVIVAL || mode == OperationsManager.GameMode.StandardPVP || mode == OperationsManager.GameMode.FFAPVP)
                {
                    log.LogInfo("game mode " + mode + " is not handled; mission left unchanged");
                    return true;
                }
            }

            var sw = Stopwatch.StartNew();
            GameManager gm = GameManager.instance;

            // 1. What the map ships with. Not filled yet? Come back next frame.
            var list = raid.botSpawnPoints;
            if (list == null || list.Count == 0) return false;

            // Arriving through the polling fallback with enemies already present means the main
            // spawn has probably happened. In the hook we are always ahead of it; some maps put
            // vehicle crews or groups in place during start-up, which is fine.
            int alive = gm != null && gm.allAI != null ? gm.allAI.Count : 0;
            if (alive > 0 && via != "hook")
            {
                log.LogWarning("mission '" + scene + "' already has " + alive + " enemies; too late to change it");
                return true;
            }
            if (alive > 0) log.LogInfo(alive + " enemies were placed during start-up (vehicle crews or groups); the layout applies to the rest");
            var originals = new List<GameObject>();
            var authored = new List<V3>();
            var withDetails = new List<GameObject>();
            var detect = new List<float>(); var hear = new List<float>(); var fov = new List<float>();
            for (int i = 0; i < list.Count; i++)
            {
                GameObject go = list[i];
                if (go == null) continue;
                originals.Add(go);
                Vector3 p = go.transform.position;
                authored.Add(new V3(p.x, p.y, p.z));
                var d = go.GetComponent<BotSpawnDetails>();
                if (d == null) continue;
                withDetails.Add(go);
                detect.Add(d.DetectionRange); hear.Add(d.HearingRange); fov.Add(d.FOV);
            }
            if (withDetails.Count == 0) { log.LogWarning("no spawn point with settings to copy; mission left unchanged"); return true; }

            // The map's usual senses are the most common values among its own spawn points.
            // (A single point can be a special case, e.g. a fixed sentry with navigation off.)
            float tDetect = MostCommon(detect), tHear = MostCommon(hear), tFov = MostCommon(fov);
            GameObject template = null; int bestFit = -1;
            foreach (GameObject go in withDetails)
            {
                var d = go.GetComponent<BotSpawnDetails>();
                var pp = d.patrolPoints;
                int fit = (d.isSpawnGroup ? 0 : 8) + (d.disableNavmesh ? 0 : 4) + (pp == null || pp.Count == 0 ? 2 : 0) + (d.isProne ? 0 : 1)
                        + (d.DetectionRange == tDetect && d.FOV == tFov ? 1 : 0);
                if (fit > bestFit) { bestFit = fit; template = go; }
            }

            // 2. Where players come in, and the map's walkable surface.
            int laptopCount = gm != null ? gm.botAmount : 0;
            var infils = CollectInfils(raid, out string infilInfo);
            NavMesh nav = NavPicker.Pick(scene, authored, ReadLiveGraph(), out string navInfo);

            // Remember this mission for the Game Master tools, whatever happens next.
            CurrentRaidPtr = raid.Pointer; CurrentTemplate = template; CurrentNav = nav;
            CurrentPlan = null; CurrentScenarioPath = null; CurrentScenarioScene = null;
            CurrentDetect = tDetect; CurrentHear = tHear; CurrentFov = tFov;

            // 3. The plan: a scenario file if one is configured for this scene, otherwise generated.
            MissionPlan plan = TryLoadScenario(scene, nav, out string scenarioName);
            bool fromFile = plan != null;
            long seed; int enemies; string source;
            var later = new HashSet<SpawnPlan>();
            string scenarioScene = fromFile ? plan.Scene : "";
            if (fromFile)
            {
                seed = plan.Seed;
                source = "scenario '" + scenarioName + "'";
                // Enemies of a chosen type cannot go through the game's own start-up spawn; they follow a few seconds later.
                bool keptOne = false;
                foreach (SpawnPlan s in plan.Spawns)
                    if (!string.IsNullOrEmpty(s.Type)) later.Add(s); else keptOne = true;
                if (!keptOne) later.Remove(plan.Spawns[0]);   // the game's spawner needs at least one point of its own
                enemies = Math.Min(100, plan.Spawns.Count - later.Count);
                int fixedCount = 0;
                foreach (SpawnPlan s in plan.Spawns)
                {
                    // 0 in a file means "the map's usual value".
                    if (s.DetectionRange <= 0) s.DetectionRange = tDetect;
                    if (s.HearingRange <= 0) s.HearingRange = tHear;
                    if (s.Fov <= 0) s.Fov = tFov;
                    // A position off the walkable surface can only be a fixed sentry.
                    if (!s.Fixed && nav != null && nav.TriAt(s.Pos.X, s.Pos.Z, s.Pos.Y, 1.0f, out _) < 0) s.Fixed = true;
                    if (s.Fixed) { fixedCount++; s.Role = SpawnRole.Guard; s.Patrol.Clear(); }
                }
                if (fixedCount > 0) plan.Notes.Add(fixedCount + " position(s) treated as fixed sentries (navigation off)");
            }
            else
            {
                if (nav == null) { log.LogWarning("no map data for '" + scene + "' (" + navInfo + "); mission left unchanged"); return true; }
                enemies = DirectorPlugin.EnemyCount.Value > 0 ? DirectorPlugin.EnemyCount.Value : laptopCount;
                if (enemies <= 0) enemies = 10;
                enemies = Math.Max(1, Math.Min(60, enemies));
                seed = DirectorPlugin.Seed.Value != 0 ? DirectorPlugin.Seed.Value : NewSeed();
                source = "seed " + seed;
                var input = new GeneratorInput { Nav = nav, Seed = seed, Scene = scene, Enemies = enemies, Infils = infils, Authored = authored };
                if (infils.Count == 0) AddExfilBoxes(raid, input);
                plan = Generator.Generate(input);
                if (plan.Spawns.Count < 3)
                {
                    log.LogWarning("generator found only " + plan.Spawns.Count + " positions on '" + scene + "'; mission left unchanged. " + string.Join("; ", plan.Notes));
                    return true;
                }
                // Indoors keep the map's own senses; outdoors never go below sensible open-ground values.
                foreach (SpawnPlan s in plan.Spawns)
                {
                    s.DetectionRange = s.Indoor ? tDetect : Math.Max(tDetect, s.DetectionRange);
                    s.HearingRange = s.Indoor ? tHear : Math.Max(tHear, s.HearingRange);
                    s.Fov = s.Indoor ? tFov : Math.Max(tFov, s.Fov);
                }
            }

            // 4. Build everything, then swap.
            int generated, toppedUp;
            Apply(raid, plan, template, fromFile ? null : originals, enemies, new Rng(seed ^ 0x5EED), later, out generated, out toppedUp);
            string senses = "usual senses " + tDetect + "/" + tHear + "/" + tFov + ", template '" + template.name + "'";
            if (gm != null && (fromFile || DirectorPlugin.EnemyCount.Value > 0)) gm.botAmount = enemies;
            sw.Stop();

            // 5. Tell the player and leave a record. A record copied into the scenarios folder replays exactly.
            Banner = "MissionLab: " + source + " | " + enemies + " enemies in " + plan.Groups + " groups | " + (nav != null ? nav.SourceFile : "no map file");
            BannerUntil = Time.unscaledTime + 12f;
            log.LogInfo(Banner + " | via " + via + " | " + generated + " planned + " + toppedUp + " original spawn points | insertion: " + infilInfo +
                        " | map: " + navInfo + " | " + senses + " | " + sw.ElapsedMilliseconds + " ms");
            plan.Scene = scene;
            CurrentPlan = plan; CurrentScenarioPath = fromFile ? _lastScenarioPath : null; CurrentScenarioScene = scenarioScene;
            GameMaster.BeginMission(raid.Pointer, later);
            if (later.Count > 0) log.LogInfo(later.Count + " enemies of a chosen type will be added a few seconds after the start");
            WriteRecord(scene, seed, via, source, enemies, laptopCount, generated, toppedUp, infils, infilInfo, navInfo, sw.ElapsedMilliseconds, plan);
            return true;
        }

        /// <summary>Bounds and size of the navigation graph the game has loaded for this scene.</summary>
        private static LiveGraph ReadLiveGraph()
        {
            try
            {
                AstarPath a = AstarPath.active;
                var gs = a == null ? null : a.graphs;
                if (gs == null) return null;
                for (int i = 0; i < gs.Length; i++)
                {
                    var rg = gs[i] == null ? null : gs[i].TryCast<Pathfinding.RecastGraph>();
                    if (rg == null) continue;
                    Vector3 c = rg.forcedBoundsCenter, s = rg.forcedBoundsSize;
                    var live = new LiveGraph { Center = new V3(c.x, c.y, c.z), Size = new V3(s.x, s.y, s.z), TilesX = rg.tileXCount, TilesZ = rg.tileZCount };
                    if (!a.isScanning) live.Nodes = rg.CountNodes();
                    return live;
                }
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("could not read the navigation graph: " + e.Message); }
            return null;
        }

        /// <summary>
        /// Picks the scenario file for this mission from BepInEx/MissionLab/scenarios.
        /// General.Scenario = a file name: that file, if it fits this map.
        /// General.Scenario = *: any file in the folder that fits this map (a random one if several do).
        /// Any problem is logged and the mission falls back to a generated layout.
        /// </summary>
        private static MissionPlan TryLoadScenario(string scene, NavMesh nav, out string name)
        {
            var log = DirectorPlugin.L;
            name = (DirectorPlugin.Scenario.Value ?? "").Trim();
            if (name.Length == 0) return null;
            string dir = Path.Combine(DirectorPlugin.BaseDir, "scenarios");
            string mapFile = nav == null ? null : nav.SourceFile;

            if (name != "*")
            {
                string file = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
                string path = Path.Combine(dir, Path.GetFileName(file));
                if (!File.Exists(path)) { log.LogWarning("scenario '" + name + "' not found in " + dir + "; generating instead"); return null; }
                MissionPlan plan = ReadScenario(path, scene, mapFile, true, out string why);
                if (plan == null) log.LogInfo("scenario '" + name + "' " + why + "; generating instead");
                _lastScenarioPath = path;
                return plan;
            }

            // Every file in the folder that is made for this map. Subfolders are ignored,
            // so moving a file into one switches it off.
            var fits = new List<MissionPlan>(); var names = new List<string>(); var paths = new List<string>();
            if (Directory.Exists(dir))
                foreach (string path in Directory.GetFiles(dir, "*.json"))
                {
                    MissionPlan plan = ReadScenario(path, scene, mapFile, false, out string why);
                    if (plan != null) { fits.Add(plan); names.Add(Path.GetFileNameWithoutExtension(path)); paths.Add(path); }
                    else log.LogInfo("scenario '" + Path.GetFileNameWithoutExtension(path) + "' " + why);
                }
            if (fits.Count == 0) return null;
            int pick = fits.Count == 1 ? 0 : new System.Random().Next(fits.Count);
            name = names[pick]; _lastScenarioPath = paths[pick];
            if (fits.Count > 1) log.LogInfo(fits.Count + " scenarios fit this map (" + string.Join(", ", names) + "); picked '" + name + "'");
            return fits[pick];
        }

        /// <summary>
        /// A scenario fits when its "scene" lists this scene ("Suburb Day, Suburb Night"), or when it has
        /// no scene and its "navFile" is this map's file. A file naming neither fits anything, but only
        /// when it was asked for by name.
        /// </summary>
        private static MissionPlan ReadScenario(string path, string scene, string mapFile, bool askedByName, out string why)
        {
            why = null;
            MissionPlan plan;
            try { plan = MissionPlan.FromJson(File.ReadAllText(path)); }
            catch (Exception e) { why = "could not be read (" + e.Message + ")"; return null; }
            if (plan.Spawns.Count == 0) { why = "has no enemies"; return null; }

            bool hasScene = !string.IsNullOrWhiteSpace(plan.Scene), hasMap = !string.IsNullOrWhiteSpace(plan.NavFile);
            if (hasScene)
            {
                foreach (string part in plan.Scene.Split(','))
                    if (part.Trim() == "*" || string.Equals(part.Trim(), scene, StringComparison.OrdinalIgnoreCase)) return plan;
                why = "is for '" + plan.Scene + "', this is '" + scene + "'";
                return null;
            }
            if (hasMap)
            {
                if (mapFile != null && string.Equals(Path.GetFileName(plan.NavFile.Trim()), mapFile, StringComparison.OrdinalIgnoreCase)) return plan;
                why = "is for map file '" + plan.NavFile + "', this mission uses '" + (mapFile ?? "an unknown map") + "'";
                return null;
            }
            if (askedByName) return plan;
            why = "names no scene and no map file, so it is only used when asked for by name";
            return null;
        }

        private static float MostCommon(List<float> values)
        {
            float best = values[0]; int bestCount = 0;
            foreach (float v in values)
            {
                int n = 0;
                foreach (float w in values) if (w == v) n++;
                if (n > bestCount) { bestCount = n; best = v; }
            }
            return best;
        }

        private static bool SceneAllowed(string scene)
        {
            string cfg = DirectorPlugin.Scenes.Value ?? "";
            foreach (string part in cfg.Split(','))
            {
                string s = part.Trim();
                if (s == "*" || string.Equals(s, scene, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static long NewSeed()
        {
            long t = DateTime.UtcNow.Ticks ^ (Environment.TickCount64 * 2654435761L);
            return 100000 + Math.Abs(t % 900000);   // six digits: easy to read out and share
        }

        private static void AddUnique(List<V3> list, Vector3 p)
        {
            if (Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z) < 0.01f) return;   // unplaced container at the origin
            foreach (var q in list)
                if (Math.Abs(q.X - p.x) + Math.Abs(q.Y - p.y) + Math.Abs(q.Z - p.z) < 1f) return;
            list.Add(new V3(p.x, p.y, p.z));
        }

        /// <summary>Player start positions: per-player spawn slots, plus each insertion method's objects.</summary>
        private static List<V3> CollectInfils(RaidManager raid, out string info)
        {
            var res = new List<V3>();
            int slots = 0, methods = 0;
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PlayerSpawnDetails>(), true);
                if (found != null)
                    for (int i = 0; i < found.Length; i++)
                    {
                        var s = found[i] == null ? null : found[i].TryCast<PlayerSpawnDetails>();
                        if (s == null) continue;
                        slots++;
                        AddUnique(res, s.transform.position);
                    }
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("could not read player spawn slots: " + e.Message); }
            try
            {
                InfiltrationManager im = raid.infiltrationManager;
                if (im == null) im = InfiltrationManager.instance;
                var ms = im == null ? null : im.infilMethods;
                if (ms != null)
                    for (int i = 0; i < ms.Length; i++)
                    {
                        var m = ms[i];
                        GameObject o = m == null ? null : m.infilObject;
                        if (o == null) continue;
                        methods++;
                        Transform t = o.transform;
                        AddUnique(res, t.position);
                        for (int c = 0; c < t.childCount; c++)
                        {
                            Transform child = t.GetChild(c);
                            AddUnique(res, child.position);
                            for (int g = 0; g < child.childCount; g++) AddUnique(res, child.GetChild(g).position);
                        }
                    }
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("could not read insertion methods: " + e.Message); }
            info = res.Count + " positions (" + slots + " player slots, " + methods + " insertion methods)";
            return res;
        }

        /// <summary>With no known insertion point, keep the extraction zones clear instead.</summary>
        private static void AddExfilBoxes(RaidManager raid, GeneratorInput input)
        {
            try
            {
                var ex = raid.exfilZones;
                if (ex == null) return;
                for (int i = 0; i < ex.Count; i++)
                {
                    var z = ex[i];
                    var col = z == null ? null : z.GetComponent<Collider>();
                    if (col == null) continue;
                    Bounds b = col.bounds;
                    input.AvoidBoxes.Add(new[] { b.min.x, b.max.x, b.min.z, b.max.z });
                }
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("could not read extraction zones: " + e.Message); }
        }

        /// <summary>
        /// One spawn point for one planned enemy: a copy of one of the map's own points, so it carries
        /// everything the game expects, with our position and behaviour written over it.
        /// </summary>
        internal static GameObject BuildPoint(GameObject template, Transform parent, SpawnPlan s, string name)
        {
            var pos = new Vector3(s.Pos.X, s.Pos.Y + (s.Fixed ? 0f : 0.1f), s.Pos.Z);
            GameObject go = UnityEngine.Object.Instantiate((UnityEngine.Object)template, pos, Quaternion.Euler(0f, s.Yaw, 0f), parent).Cast<GameObject>();
            go.name = name;
            bool patrol = s.Role == SpawnRole.Patrol && s.Patrol.Count >= 2 && !s.Fixed;

            var d = go.GetComponent<BotSpawnDetails>();
            d.idleState = s.Fixed || s.Role == SpawnRole.Guard ? BrainAI.IdleStates.Idle
                        : patrol ? BrainAI.IdleStates.Patrol
                        : BrainAI.IdleStates.Wander;
            d.reactType = s.Defensive ? BrainAI.ReactType.Defensive : BrainAI.ReactType.Offensive;
            // 0 means "the map's usual value".
            if (s.DetectionRange > 0) d.DetectionRange = s.DetectionRange; else if (CurrentDetect > 0) d.DetectionRange = CurrentDetect;
            if (s.HearingRange > 0) d.HearingRange = s.HearingRange; else if (CurrentHear > 0) d.HearingRange = CurrentHear;
            if (s.Fov > 0) d.FOV = s.Fov; else if (CurrentFov > 0) d.FOV = CurrentFov;
            if (s.Role == SpawnRole.Wander && s.WanderDistance > 0) d.WanderDistance = s.WanderDistance;
            d.isProne = s.Prone;
            d.isSpawnGroup = false;
            d.disableNavmesh = s.Fixed;     // everyone else must be able to walk: wander, patrol, react
            d.hvtLinkedSpawnGroup = null;

            var pts = d.patrolPoints;
            if (pts == null) { pts = new Il2Generic.List<Transform>(); d.patrolPoints = pts; }
            pts.Clear();
            if (patrol)
            {
                for (int k = 0; k < s.Patrol.Count; k++)
                {
                    var wp = new GameObject(name + " stop " + k);
                    wp.transform.SetParent(parent);
                    wp.transform.position = new Vector3(s.Patrol[k].X, s.Patrol[k].Y + 0.1f, s.Patrol[k].Z);
                    pts.Add(wp.transform);
                }
                d.PatrolLooping = true;
                d.patrolWaitTime = s.PatrolWaitSeconds;
            }
            return go;
        }

        private static void Apply(RaidManager raid, MissionPlan plan, GameObject template, List<GameObject> originals, int enemies,
                                  Rng rng, HashSet<SpawnPlan> later, out int generated, out int toppedUp)
        {
            var root = new GameObject("MissionLab Spawns");
            var created = new List<GameObject>();
            try
            {
                root.transform.SetParent(raid.transform);
                foreach (SpawnPlan s in plan.Spawns)
                {
                    if (later.Contains(s)) continue;    // enemies of a chosen type are added right after the game's own spawn
                    created.Add(BuildPoint(template, root.transform, s, "ML g" + s.Group + " " + s.Role + " " + created.Count));
                }
            }
            catch
            {
                // Nothing has been swapped in yet: remove our objects and leave the mission as shipped.
                try { UnityEngine.Object.Destroy(root); } catch { }
                throw;
            }

            // Never hand the game fewer points than enemies: top up with the map's own points.
            generated = created.Count;
            toppedUp = 0;
            int need = originals == null ? 0 : enemies - created.Count;
            var pool = originals == null ? new List<GameObject>() : new List<GameObject>(originals);
            while (need > 0 && pool.Count > 0)
            {
                int i = rng.Range(0, pool.Count);
                created.Add(pool[i]);
                pool.RemoveAt(i);
                need--; toppedUp++;
            }

            var list = raid.botSpawnPoints;
            list.Clear();
            foreach (GameObject go in created) list.Add(go);
        }

        private static void WriteRecord(string scene, long seed, string via, string source, int enemies, int laptopCount, int generated, int toppedUp,
                                        List<V3> infils, string infilInfo, string navInfo, long ms, MissionPlan plan)
        {
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var sb = new StringBuilder(8192);
                string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                sb.Append("{\"schema\":\"missionlab-mission/1\"");
                sb.Append(",\"plugin\":").Append(Q(DirectorPlugin.Version));
                sb.Append(",\"writtenUtc\":").Append(Q(DateTime.UtcNow.ToString("o")));
                sb.Append(",\"scene\":").Append(Q(scene));
                sb.Append(",\"seed\":").Append(seed.ToString(inv));
                sb.Append(",\"via\":").Append(Q(via));
                sb.Append(",\"source\":").Append(Q(source));
                sb.Append(",\"enemies\":").Append(enemies);
                sb.Append(",\"laptopEnemyCount\":").Append(laptopCount);
                sb.Append(",\"generatedSpawnPoints\":").Append(generated);
                sb.Append(",\"originalSpawnPointsAdded\":").Append(toppedUp);
                sb.Append(",\"insertionInfo\":").Append(Q(infilInfo));
                sb.Append(",\"insertionPoints\":[");
                for (int i = 0; i < infils.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('[').Append(Math.Round(infils[i].X, 2).ToString(inv)).Append(',').Append(Math.Round(infils[i].Y, 2).ToString(inv)).Append(',').Append(Math.Round(infils[i].Z, 2).ToString(inv)).Append(']');
                }
                sb.Append("],\"map\":").Append(Q(navInfo));
                sb.Append(",\"milliseconds\":").Append(ms);
                sb.Append(",\"plan\":").Append(plan.ToJson());
                sb.Append('}');
                string json = sb.ToString();
                string dir = Path.Combine(DirectorPlugin.BaseDir, "missions");
                File.WriteAllText(Path.Combine(dir, "last.json"), json, new UTF8Encoding(false));
                string safe = scene.Replace(' ', '_');
                File.WriteAllText(Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "_" + safe + "_seed" + seed + ".json"), json, new UTF8Encoding(false));
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("could not write the mission record: " + e.Message); }
        }
    }
}
