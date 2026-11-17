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
        /// <summary>Enemies of the scenario that lost their roll this time (chance, either/or). A save puts them back.</summary>
        internal static readonly List<SpawnPlan> SatOut = new List<SpawnPlan>();
        internal static readonly List<DoorPlan> DoorsOut = new List<DoorPlan>();
        internal static readonly List<ThingPlan> ThingsOut = new List<ThingPlan>();
        internal static string CurrentScenarioScene;        // that file's own "scene" value
        private static string _lastScenarioPath;

        // One mission, one session. Changes sent by the page carry the session they were made for; a change for another mission is refused.
        internal static string Session = "";
        private static int _sessions;
        private static string _scenarioText = "";           // the text of the scenario file last read by name or picked
        private static RunRecord.Entry _repeat;             // the earlier run the next mission is to repeat (null: a new roll)
        internal static string RepeatFile => _repeat == null ? "" : _repeat.File;
        internal static string SetRepeat(string file)
        {
            if (string.IsNullOrEmpty(file)) { _repeat = null; return "the next mission gets a new roll"; }
            RunRecord.Entry e = RunRecord.Read(file);
            if (e == null) return "that run record could not be read";
            _repeat = e;
            return "the next mission repeats the run of roll " + e.Roll + " (" + (e.Scenario == "" ? "a generated layout" : "scenario '" + e.Scenario + "'") + ", " + e.Players + " player(s))";
        }
        private static string _refusedFile = "";            // set while a scenario asked for by name is read and found wrong
        internal static string Rejected = "";              // why the scenario was not used for this mission ("": it was, or there was none)
        internal static void NewSession()
        {
            Session = "m" + (++_sessions) + "-" + (DateTime.UtcNow.Ticks / 10000000L % 100000L);
            Rejected = "";
            DirectorPlugin.FreshGuards();
            DirectorPlugin.L.LogInfo("session " + Session + " begins");
        }
        internal static void EndSession(string why)
        {
            if (Session == "") return;
            DirectorPlugin.L.LogInfo("session " + Session + " ends (" + why + ")");
            Session = ""; Rejected = "";
        }

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
                NewSession();
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
            VehiclesWanted = 0; VehiclesAsked = false;
            var log = DirectorPlugin.L;

            if (!DirectorPlugin.Enabled.Value) { log.LogInfo("disabled in config; mission left unchanged"); return true; }
            if (!Mirror.NetworkServer.active) { log.LogInfo("not hosting; mission left unchanged"); return true; }
            string scene = raid.gameObject.scene.name ?? "";
            if (!SceneAllowed(scene)) { log.LogInfo("scene '" + scene + "' is not listed in General/Scenes; mission left unchanged"); return true; }

            // Only the PvE raids this was written for. Everything else is left alone: PvP in the first place (the studio's
            // condition for allowing the mod), wave survival, and any mode a later game version adds.
            OperationsManager om = OperationsManager.singleton;
            if (om == null) { log.LogInfo("the game mode could not be read; mission left unchanged"); return true; }
            var mode = om.CurrentGameMode;
            bool pveRaid = LiveLink.PveRaid(mode);
            if (!pveRaid)
            {
                log.LogInfo("game mode " + mode + " is not a PvE raid; MissionLab stays out of it and the mission is left unchanged");
                return true;
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
            CurrentInfils = infils;
            NavMesh nav = NavPicker.Pick(scene, authored, ReadLiveGraph(), out string navInfo);

            // Remember this mission for the Game Master tools, whatever happens next.
            CurrentRaidPtr = raid.Pointer; CurrentTemplate = template; CurrentNav = nav;
            CurrentPlan = null; CurrentScenarioPath = null; CurrentScenarioScene = null; SatOut.Clear(); DoorsOut.Clear(); ThingsOut.Clear();
            CurrentDetect = tDetect; CurrentHear = tHear; CurrentFov = tFov;

            // The lobby choice "Empty map": nobody spawns; you build the mission yourself and save it.
            if (Picker.Current() == Picker.Empty)
            {
                if (gm != null) gm.botAmount = 0;
                CurrentPlan = new MissionPlan { Scene = scene, NavFile = nav != null ? nav.SourceFile : null };
                GameMaster.BeginMission(raid.Pointer, null);
                Banner = "MissionLab: empty map. Build your own with " + DirectorPlugin.KeyGm.Value + (LiveLink.Running ? " or in a browser at http://localhost:" + LiveLink.Port : "") + ", then save.";
                BannerUntil = Time.unscaledTime + 15f;
                log.LogInfo(Banner + " | via " + via + " | the game's own spawn list is left alone, enemy count set to 0 | map: " + navInfo);
                return true;
            }

            // 3. The plan: a scenario file if one is configured for this scene, otherwise generated.
            _refusedFile = ""; _scenarioText = "";
            MissionPlan plan = TryLoadScenario(scene, nav, out string scenarioName);
            if (plan == null && _refusedFile != "")
            {
                // The scenario asked for by name has something wrong with it. Nothing has been changed yet: the game's own mission runs.
                Rejected = _refusedFile;
                Banner = "MissionLab: the scenario was not used (see the page or the log). The game's own mission runs."; BannerUntil = Time.unscaledTime + 20f;
                RunRecord.Write(scene, nav != null ? nav.SourceFile : "", scenarioName, _scenarioText, 0, 0, null, null, null, false, Rejected);
                return true;
            }
            bool fromFile = plan != null;
            long seed; int enemies; string source;
            var later = new HashSet<SpawnPlan>();
            string scenarioScene = fromFile ? plan.Scene : "";
            Variants.Out rolledOut = null; long rolledWith = 0; int squad = 0;
            if (fromFile && Variants.Any(plan))
            {
                // What the scenario leaves to chance is rolled now, anew for every mission unless a seed is set.
                long roll = _repeat != null ? _repeat.Roll : DirectorPlugin.Seed.Value != 0 ? DirectorPlugin.Seed.Value : NewSeed();
                var rolls = new List<string>();
                int before = plan.Spawns.Count;
                int players = 0;
                try { var all = PlayerMaster.AllPlayerMasters; for (int i = 0; all != null && i < all.Count; i++) if (all[i] != null) players++; } catch { }
                Variants.Out gone = Variants.Resolve(plan, new Rng(roll ^ 0x5EED), rolls, players);
                rolledOut = gone; rolledWith = roll; squad = players;
                SatOut.AddRange(gone.Spawns); DoorsOut.AddRange(gone.Doors); ThingsOut.AddRange(gone.Things);
                log.LogInfo("left to chance: " + string.Join("; ", rolls) + " | " + plan.Spawns.Count + " of " + before + " enemies are in this mission, " + players + " player(s)" +
                            (gone.Doors.Count + gone.Things.Count > 0 ? ", " + gone.Doors.Count + " door setting(s) and " + gone.Things.Count + " thing(s) left out" : "") + " (roll " + roll + ")");
            }
            if (fromFile)
            {
                // Can this mission still be finished, now that the dice have fallen? If its own rules make that impossible, the
                // scenario is not used at all: nothing of the mission has been changed yet, and the game's own mission runs.
                var broken = Completion.Check(plan, rolledOut, squad);
                foreach (Finding f in broken) if (f.Level == Finding.Note) log.LogInfo("scenario '" + scenarioName + "': " + f);
                if (Completion.Blocks(broken))
                {
                    string why = string.Join("; ", broken.FindAll(f => f.Level == Finding.Error).ConvertAll(f => f.ToString()));
                    string with = rolledOut != null ? " This was roll " + rolledWith + " with " + squad + " player(s); Seed = " + rolledWith + " in the settings gives the same result again." : "";
                    Rejected = "Scenario '" + scenarioName + "' was not used: " + why + "." + with + " The game's own mission runs.";
                    log.LogWarning(Rejected + " Nothing of the mission was changed.");
                    Banner = "MissionLab: the scenario was not used (see the page or the log). The game's own mission runs."; BannerUntil = Time.unscaledTime + 20f;
                    SatOut.Clear(); DoorsOut.Clear(); ThingsOut.Clear();
                    RunRecord.Write(scene, nav != null ? nav.SourceFile : "", scenarioName, _scenarioText, rolledWith, squad, plan, rolledOut, null, false, Rejected);
                    _repeat = null;
                    return true;
                }
                seed = plan.Seed;
                source = "scenario '" + scenarioName + "'";
                // Enemies of a chosen type cannot go through the game's own start-up spawn; they follow a few seconds later.
                bool keptOne = false;
                foreach (SpawnPlan s in plan.Spawns)
                    if (s.Vehicle || s.Trigger != 0 || !string.IsNullOrEmpty(s.Type)) later.Add(s); else keptOne = true;      // a gun truck is not a person: it never joins the enemy list
                if (!keptOne)
                {
                    // The game's own spawner needs at least one enemy that is simply there from the start.
                    SpawnPlan firstOne = plan.Spawns.Find(s => s.Trigger == 0 && !s.Vehicle) ?? plan.Spawns.Find(s => !s.Vehicle) ?? plan.Spawns[0];
                    firstOne.Trigger = 0; firstOne.Vehicle = false; later.Remove(firstOne);
                }
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
                // More than positions: a wave on alert, an ambush, locked doors, and intel somewhere else when RandomIntel is on.
                if (DirectorPlugin.RandomExtras.Value)
                {
                    try { RandomExtras.Add(plan, raid, nav, new Rng(seed ^ 0xE87A5), infils); }
                    catch (Exception e) { log.LogWarning("the random layout's extras could not be rolled: " + e.Message); }
                    foreach (SpawnPlan s in plan.Spawns) if (s.Trigger != 0) later.Add(s);
                    foreach (string note in plan.Notes) if (note.StartsWith("extras:")) log.LogInfo(note);
                }
            }

            // Missions with a target person: the plan may say where that person stands.
            var hvtPoints = raid.hvtSpawnPoints; var hvtTypes = raid.hvtAI;
            bool targetMission = hvtPoints != null && hvtPoints.Length > 0 && hvtTypes != null && hvtTypes.Length > 0 && gm != null && gm.botHVTAmount > 0;
            var targets = new List<SpawnPlan>();
            foreach (SpawnPlan s in plan.Spawns) { if (s.Hvt && targetMission && s.Trigger == 0) targets.Add(s); else s.Hvt = false; }
            if (targetMission && targets.Count == 0 && !fromFile && DirectorPlugin.MoveTarget.Value)
            {
                // A generated layout also moves the target: to a guard post indoors, in the strongest group if possible.
                // It stays in the part of the map the mission was built around: within 60 m of the game's own target spots.
                Vector3 centre = Vector3.zero; int counted = 0;
                for (int i = 0; i < hvtPoints.Length; i++) if (hvtPoints[i] != null) { centre += hvtPoints[i].transform.position; counted++; }
                if (counted > 0) centre /= counted;
                SpawnPlan pick = null; float best = float.MaxValue;
                foreach (SpawnPlan s in plan.Spawns)
                {
                    if (s.Role != SpawnRole.Guard || s.Trigger != 0) continue;
                    float dx = s.Pos.X - centre.x, dz = s.Pos.Z - centre.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > 60f || Mathf.Abs(s.Pos.Y - centre.y) > 8f) continue;
                    float score = d + (s.Indoor ? 0f : 30f);
                    if (score < best) { best = score; pick = s; }
                }
                if (pick != null && plan.Spawns.Count > enemies) { pick.Hvt = true; targets.Add(pick); }
                else log.LogInfo("no generated guard post near the mission's own target spots; the target keeps the game's place");
            }
            foreach (SpawnPlan s in targets) { later.Add(s); s.Type = null; }
            if (plan.Spawns.Count - later.Count == 0)
            {
                // Nothing ordinary is left for the game's own spawner: one held-back enemy is there from the start after all.
                SpawnPlan free = plan.Spawns.Find(s => !s.Hvt);
                if (free != null) { free.Trigger = 0; free.Type = null; later.Remove(free); }
            }
            bool heldBack = later.Count > 0;                 // then the start-up list is shorter than the plan, and the game must be told how many to spawn
            if (fromFile || heldBack) enemies = Math.Max(1, Math.Min(100, plan.Spawns.Count - later.Count));

            // 4. Build everything, then swap.
            int generated, toppedUp;
            Apply(raid, plan, template, fromFile ? null : originals, enemies, new Rng(seed ^ 0x5EED), later, out generated, out toppedUp);
            int movedTargets = targets.Count > 0 ? PlaceTargets(raid, targets, gm.botHVTAmount) : 0;
            foreach (SpawnPlan s in targets) later.Remove(s);           // the game itself spawns the target; nothing to add later
            List<SpawnPlan> trucks = plan.Spawns.FindAll(s => s.Vehicle);
            foreach (SpawnPlan s in trucks) later.Remove(s);            // parked by the game's own vehicle spawner
            if (trucks.Count > 0)
            {
                try { log.LogInfo("gun trucks: " + PlaceVehicles(raid, trucks)); }
                catch (Exception e) { log.LogWarning("gun trucks could not be set up: " + e.Message); }
            }
            string senses = (movedTargets > 0 ? movedTargets + " target position(s) set | " : "") + "usual senses " + tDetect + "/" + tHear + "/" + tFov + ", template '" + template.name + "'";
            if (gm != null && (fromFile || heldBack || DirectorPlugin.EnemyCount.Value > 0)) gm.botAmount = enemies;
            sw.Stop();

            // 5. Tell the player and leave a record. A record copied into the scenarios folder replays exactly.
            Banner = "MissionLab: " + source + " | " + enemies + " enemies in " + plan.Groups + " groups | " + (nav != null ? nav.SourceFile : "no map file");
            BannerUntil = Time.unscaledTime + 12f;
            log.LogInfo(Banner + " | via " + via + " | " + generated + " planned + " + toppedUp + " original spawn points | insertion: " + infilInfo +
                        " | map: " + navInfo + " | " + senses + " | " + sw.ElapsedMilliseconds + " ms");
            plan.Scene = scene;
            CurrentPlan = plan; CurrentScenarioPath = fromFile ? _lastScenarioPath : null; CurrentScenarioScene = scenarioScene;
            // What was actually played is written down before anything else can go wrong with it.
            try
            {
                if (fromFile && string.IsNullOrEmpty(_scenarioText) && _lastScenarioPath != null) _scenarioText = File.ReadAllText(_lastScenarioPath);
                long usedRoll = rolledOut != null ? rolledWith : seed;
                RunRecord.Write(scene, nav != null ? nav.SourceFile : "", fromFile ? scenarioName : "", fromFile ? _scenarioText : "", usedRoll, squad, plan, rolledOut, null, true, "");
                if (_repeat != null)
                {
                    // Was this the same setup as the run it repeats? The inputs are compared first, then the result.
                    var changed = new List<string>();
                    if (_repeat.Hash != RunRecord.Sha(fromFile ? _scenarioText : "")) changed.Add("the scenario file has changed since");
                    if (_repeat.Players != squad && rolledOut != null) changed.Add(_repeat.Players + " player(s) then, " + squad + " now");
                    if (_repeat.Plugin != DirectorPlugin.Version) changed.Add("MissionLab " + _repeat.Plugin + " then, " + DirectorPlugin.Version + " now");
                    bool same = _repeat.Resolved == plan.ToJson();
                    string said = same ? "this mission repeats the run of roll " + _repeat.Roll + ": the same setup came out" + (changed.Count > 0 ? " (although " + string.Join("; ", changed) + ")" : "")
                                       : "the run of roll " + _repeat.Roll + " could not be repeated exactly: " + (changed.Count > 0 ? string.Join("; ", changed) : "the same inputs gave another setup, which should not happen: please report it");
                    log.LogInfo("repeat: " + said);
                    Banner = "MissionLab: " + said; BannerUntil = Time.unscaledTime + 15f;
                    _repeat = null;
                }
            }
            catch (Exception e) { log.LogWarning("the run record could not be made: " + e.Message); }
            GameMaster.BeginMission(raid.Pointer, later);
            if (later.Count > 0) log.LogInfo(later.Count + " enemies are held back: a chosen type follows a few seconds after the start, zone enemies when a player reaches their zone");
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
            if (name.Length == 0 || name == Picker.Empty) return null;
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
            try
            {
                // Read strictly: anything wrong is named with its place in the file, and the scenario is not used.
                string label = Path.GetFileNameWithoutExtension(path);
                string text = File.ReadAllText(path);
                if (askedByName) _scenarioText = text;
                var findings = ScenarioFile.Read(text, out plan);
                foreach (Finding f in findings) if (f.Level == Finding.Note) DirectorPlugin.L.LogInfo("scenario '" + label + "': " + f);
                if (plan == null)
                {
                    var errors = findings.FindAll(f => f.Level == Finding.Error);
                    why = "has " + errors.Count + (errors.Count == 1 ? " problem" : " problems") + ": " + string.Join("; ", errors.GetRange(0, Math.Min(3, errors.Count)).ConvertAll(f => f.ToString()))
                        + (errors.Count > 3 ? "; and " + (errors.Count - 3) + " more" : "");
                    foreach (Finding f in errors) DirectorPlugin.L.LogWarning("scenario '" + label + "': " + f);
                    if (askedByName) _refusedFile = "Scenario '" + label + "' was not used: it " + why + ". The file was not changed. The game's own mission runs.";
                    return null;
                }
            }
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
        /// Replaces the mission's target-person spawn points with the planned ones. The game spawns
        /// the target on one of these about two seconds after the start, the same way it does enemies.
        /// </summary>
        internal static List<V3> CurrentInfils = new List<V3>();     // where players can arrive, for the browser page's danger check
        internal static int VehiclesWanted;
        internal static bool VehiclesAsked;

        /// <summary>The game's gun truck, from this map's own list or else from the list of everything the game can spawn.</summary>
        internal static GameObject VehiclePrefab(RaidManager raid, string wanted, out string from)
        {
            List<GameObject> all = VehiclePrefabs(raid);
            from = "the game's spawn list";
            if (!string.IsNullOrEmpty(wanted))
            {
                GameObject named = all.Find(p => string.Equals(p.name, wanted, StringComparison.OrdinalIgnoreCase));
                if (named != null) return named;
                DirectorPlugin.L.LogWarning("gun trucks: the game has no vehicle called '" + wanted + "'; the usual one is used");
            }
            var own = raid.staticVehicleAI;
            for (int i = 0; own != null && i < own.Length; i++) if (own[i] != null) { from = "this map's own list"; return own[i]; }
            // Elsewhere the pick-up with the machine gun, not whatever armour happens to be first in the list.
            foreach (string hint in new[] { "technical", "hilux", "pickup", "truck" })
            {
                GameObject light = all.Find(p => p.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (light != null) return light;
            }
            if (all.Count > 0) return all[0];
            from = null;
            return null;
        }

        private static List<GameObject> _vehiclePrefabs;
        /// <summary>Every armed vehicle the game can spawn: this map's own first, then the rest of the game's spawn list.</summary>
        internal static List<GameObject> VehiclePrefabs(RaidManager raid)
        {
            if (_vehiclePrefabs != null && _vehiclePrefabs.TrueForAll(p => p != null)) return _vehiclePrefabs;
            var list = new List<GameObject>();
            var own = raid == null ? null : raid.staticVehicleAI;
            for (int i = 0; own != null && i < own.Length; i++) if (own[i] != null && !list.Contains(own[i])) list.Add(own[i]);
            try
            {
                Mirror.NetworkManager net = Mirror.NetworkManager.singleton;
                var all = net == null ? null : net.spawnPrefabs;
                for (int i = 0; all != null && i < all.Count; i++)
                    if (all[i] != null && all[i].GetComponentInChildren<VehicleAI>(true) != null && !list.Exists(p => p.name == all[i].name)) list.Add(all[i]);
            }
            catch { }
            _vehiclePrefabs = list;
            var names = new List<string>();
            foreach (GameObject p in list) names.Add(p.name);
            DirectorPlugin.L.LogInfo("gun trucks the game offers: " + (names.Count > 0 ? string.Join(", ", names) : "none"));
            return list;
        }

        /// <summary>
        /// Hands the game's own vehicle spawner our parking spots, the same way the enemy spawn points are swapped.
        /// A map without gun trucks never starts that spawner, so GameMaster calls it a few seconds in if no truck came.
        /// </summary>
        private static string PlaceVehicles(RaidManager raid, List<SpawnPlan> trucks)
        {
            SpawnPlan named = trucks.Find(t => !string.IsNullOrEmpty(t.Type));        // one kind of vehicle per mission: the first that is named
            GameObject prefab = VehiclePrefab(raid, named != null ? named.Type : null, out string from);
            if (prefab == null) return "this build of the game offers no gun truck to place";
            var points = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(trucks.Count);
            for (int i = 0; i < trucks.Count; i++)
            {
                var go = new GameObject("ML truck " + (i + 1));
                go.transform.SetParent(raid.transform, false);
                go.transform.SetPositionAndRotation(new Vector3(trucks[i].Pos.X, trucks[i].Pos.Y, trucks[i].Pos.Z), Quaternion.Euler(0f, trucks[i].Yaw, 0f));
                points[i] = go;
            }
            int had = raid.staticVehicleSpawnPoints == null ? 0 : raid.staticVehicleSpawnPoints.Length;
            raid.staticVehicleSpawnPoints = points;
            var one = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(1); one[0] = prefab;
            raid.staticVehicleAI = one;
            raid.maxStaticVehicles = trucks.Count;
            bool was = raid.spawnVehicleAI;
            raid.spawnVehicleAI = true;
            VehiclesWanted = trucks.Count;
            return trucks.Count + " parking spot(s) set in place of the map's " + had + ", '" + prefab.name + "' from " + from + (was ? "" : "; this map does not use vehicles itself");
        }

        private static int PlaceTargets(RaidManager raid, List<SpawnPlan> targets, int wanted)
        {
            var old = raid.hvtSpawnPoints;
            GameObject template = null;
            for (int i = 0; i < old.Length && template == null; i++)
                if (old[i] != null && old[i].GetComponent<BotSpawnDetails>() != null) template = old[i];
            if (template == null) return 0;
            var points = new List<GameObject>();
            foreach (SpawnPlan s in targets) points.Add(BuildPoint(template, raid.transform, s, "ML target " + points.Count));
            for (int i = 0; i < old.Length && points.Count < wanted; i++) if (old[i] != null) points.Add(old[i]);     // more targets than planned spots
            var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(points.Count);
            for (int i = 0; i < points.Count; i++) arr[i] = points[i];
            raid.hvtSpawnPoints = arr;
            return targets.Count;
        }

        /// <summary>
        /// One spawn point for one planned enemy: a copy of one of the map's own points, so it carries
        /// everything the game expects, with our position and behaviour written over it.
        /// </summary>
        internal static GameObject BuildPoint(GameObject template, Transform parent, SpawnPlan s, string name, float yawOverride = float.NaN)
        {
            var pos = new Vector3(s.Pos.X, s.Pos.Y + (s.Fixed ? 0f : 0.1f), s.Pos.Z);
            GameObject go = UnityEngine.Object.Instantiate((UnityEngine.Object)template, pos, Quaternion.Euler(0f, float.IsNaN(yawOverride) ? s.Yaw : yawOverride, 0f), parent).Cast<GameObject>();
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
