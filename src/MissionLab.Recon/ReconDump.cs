using System;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2Generic = Il2CppSystem.Collections.Generic;

namespace MissionLab
{
    /// <summary>
    /// Reads mission data out of the running game and writes it as JSON.
    /// Every section is isolated: if one fails, the others are still written.
    /// Nothing here assigns to a game field or calls a game method that changes state.
    /// </summary>
    internal static class ReconDump
    {
        private static int _spawnCount, _hvtCount, _exfilCount, _botCount;

        public static string Write(string phase)
        {
            _spawnCount = _hvtCount = _exfilCount = _botCount = -1;
            Scene scene = SceneManager.GetActiveScene();
            string sceneName = scene.name ?? "unknown";

            var root = new J();
            root.Obj();
            root.V("schema", "missionlab-recon/1");
            root.V("plugin", Plugin.Version);
            root.V("phase", phase);
            root.V("writtenUtc", DateTime.UtcNow.ToString("o"));
            root.V("scene", sceneName);
            root.V("sceneBuildIndex", scene.buildIndex);
            Section(root, "app", App);
            Section(root, "loadedScenes", LoadedScenes);
            Section(root, "network", Network);
            Section(root, "gameManager", GameMgr);
            Section(root, "operations", Operations);
            Section(root, "raid", Raid);
            Section(root, "infiltration", Infiltration);
            Section(root, "playerSpawns", PlayerSpawns);
            Section(root, "playerSlots", PlayerSlots);
            Section(root, "timeOfDay", TimeOfDay);
            Section(root, "navGraphs", NavGraphs);
            if (phase != "early") Section(root, "bots", Bots);
            if (phase == "manual") Section(root, "boards", Boards);
            root.EndObj();

            string file = Path.Combine(Plugin.OutDir, Sanitize(sceneName) + "." + phase + ".json");
            File.WriteAllText(file, root.ToString(), new UTF8Encoding(false));

            var s = new StringBuilder(sceneName);
            if (_spawnCount >= 0) s.Append(", ").Append(_spawnCount).Append(" enemy spawn points");
            if (_hvtCount >= 0) s.Append(", ").Append(_hvtCount).Append(" HVT points");
            if (_exfilCount >= 0) s.Append(", ").Append(_exfilCount).Append(" exfil zones");
            if (_botCount >= 0) s.Append(", ").Append(_botCount).Append(" bots alive");
            s.Append(" -> ").Append(Path.GetFileName(file));
            return s.ToString();
        }

        private static void Section(J root, string key, Action<J> fill)
        {
            var j = new J();
            try
            {
                fill(j);
                root.Raw(key, j.ToString());
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("recon section '" + key + "' failed: " + e);
                root.Obj(key).V("error", e.GetType().Name + ": " + e.Message).EndObj();
            }
        }

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
            return sb.ToString();
        }

        // ---------------------------------------------------------------- helpers

        private static string PathOf(Transform t)
        {
            if (t == null) return null;
            string p = t.name;
            Transform cur = t.parent;
            for (int depth = 0; depth < 6 && cur != null; depth++)
            {
                p = cur.name + "/" + p;
                cur = cur.parent;
            }
            return p;
        }

        private static void Tf(J j, Transform t)
        {
            j.V("name", t.name);
            j.V("path", PathOf(t));
            j.Vec("pos", t.position);
            j.V("yaw", t.rotation.eulerAngles.y);
            j.V("active", t.gameObject.activeInHierarchy);
        }

        private static void Go(J j, string key, GameObject go)
        {
            if (go == null) { j.Raw(key, "null"); return; }
            j.Obj(key);
            Tf(j, go.transform);
            j.EndObj();
        }

        private static void Names(J j, string key, Il2CppReferenceArray<GameObject> arr)
        {
            j.Arr(key);
            if (arr != null)
                for (int i = 0; i < arr.Length; i++)
                    j.V(null, arr[i] == null ? null : arr[i].name);
            j.EndArr();
        }

        private static void Names(J j, string key, Il2Generic.List<GameObject> list)
        {
            j.Arr(key);
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                    j.V(null, list[i] == null ? null : list[i].name);
            j.EndArr();
        }

        private static void Details(J j, BotSpawnDetails d)
        {
            if (d == null) { j.Raw("details", "null"); return; }
            j.Obj("details");
            j.V("idleState", d.idleState.ToString());
            j.V("reactType", d.reactType.ToString());
            j.V("doorInteractionType", d.doorInteractionType.ToString());
            j.V("detectionTimeMultiplier", d.DetectionTimeMultiplier);
            j.V("detectionRange", d.DetectionRange);
            j.V("hearingRange", d.HearingRange);
            j.V("fov", d.FOV);
            j.V("maxEffectiveRange", d.maxEffectiveRange);
            j.V("useComms", d.useComms);
            j.V("doesCounterSuppression", d.DoesCounterSuppression);
            j.V("wanderDistance", d.WanderDistance);
            j.V("modifyStance", d.modifyStance);
            j.V("doesCrouch", d.doesCrouch);
            j.V("doesProne", d.doesProne);
            j.Vec("crouchDistance", d.crouchDistance);
            j.Vec("proneDistance", d.proneDistance);
            j.V("isProne", d.isProne);
            j.V("disableNavmesh", d.disableNavmesh);
            j.V("isSpawnGroup", d.isSpawnGroup);
            j.V("patrolSpeed", d.PatrolSpeed);
            j.V("patrolLooping", d.PatrolLooping);
            j.V("patrolWaitTime", d.patrolWaitTime);
            j.Arr("patrolPoints");
            var pts = d.patrolPoints;
            if (pts != null)
                for (int i = 0; i < pts.Count; i++)
                    if (pts[i] != null) j.Vec(null, pts[i].position);
            j.EndArr();
            j.Arr("groupSpawnPoints");
            var grp = d.GroupSpawnPoints;
            if (grp != null)
                for (int i = 0; i < grp.Count; i++)
                    if (grp[i] != null) { j.Obj(); j.V("name", grp[i].name); j.Vec("pos", grp[i].transform.position); j.EndObj(); }
            j.EndArr();
            var linked = d.hvtLinkedSpawnGroup;
            j.V("hvtLinkedSpawnGroup", linked == null ? null : linked.name);
            j.EndObj();
        }

        private static void SpawnObject(J j, GameObject go)
        {
            if (go == null) { j.Raw(null, "null"); return; }
            j.Obj();
            Tf(j, go.transform);
            Details(j, go.GetComponent<BotSpawnDetails>());
            j.EndObj();
        }

        private static void Objective(J j, ObjectiveObject o)
        {
            if (o == null) { j.Raw(null, "null"); return; }
            j.Obj();
            Tf(j, o.transform);
            j.V("displayName", o.DisplayName);
            j.V("isImportant", o.IsImportant);
            j.V("isObtained", o.isObtained);
            j.V("confirmHVT", o.confirmHVT);
            j.V("hvtName", o.hvtName);
            j.EndObj();
        }

        // ---------------------------------------------------------------- sections

        private static void App(J j)
        {
            j.Obj();
            j.V("gameVersion", Application.version);
            j.V("unityVersion", Application.unityVersion);
            j.V("realtimeSinceStartup", Time.realtimeSinceStartup);
            j.EndObj();
        }

        private static void LoadedScenes(J j)
        {
            j.Arr();
            int n = SceneManager.sceneCount;
            for (int i = 0; i < n; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                j.Obj().V("name", s.name).V("buildIndex", s.buildIndex).V("isLoaded", s.isLoaded).EndObj();
            }
            j.EndArr();
        }

        private static void Network(J j)
        {
            j.Obj();
            j.V("serverActive", Mirror.NetworkServer.active);
            j.V("clientActive", Mirror.NetworkClient.active);
            j.EndObj();
        }

        private static void GameMgr(J j)
        {
            GameManager g = GameManager.instance;
            if (g == null) { j.Raw(null, "null"); return; }
            j.Obj();
            j.V("botAmount", g.botAmount);
            j.V("botHVTAmount", g.botHVTAmount);
            j.V("aiDifficulty", g.AI_Difficulty.ToString());
            j.Arr("allAITypes_s");
            var names = g.allAITypes_s;
            if (names != null)
                for (int i = 0; i < names.Length; i++) j.V(null, names[i]);
            j.EndArr();
            Names(j, "allAITypes", g.AllAITypes);
            var ai = g.allAI;
            j.V("allAICount", ai == null ? 0 : ai.Count);
            j.EndObj();
        }

        private static void Opboard(J j, string key, CerebusOpboard b)
        {
            if (b == null) { j.Raw(key, "null"); return; }
            j.Obj(key);
            j.V("name", b.name);
            j.V("minAI", b.MinAI);
            j.V("maxAI", b.MaxAI);
            j.V("difficulty", b.Difficulty.ToString());
            j.V("affectGamemode", b.AffectGamemode);
            j.V("gameModeOverride", b.GameModeOverride.ToString());
            j.V("hvtSpawnIsRandom", b.HVTSpawnIsRandom);
            j.V("prepTimeInSeconds", b.PrepTimeInSeconds);
            j.V("targetRaidTime", b.TargetRaidTime);
            j.V("isLocked", b.isLocked);
            j.V("isSimulation", b.isSimulation);
            j.V("requireEnoughInfils", b.requireEnoughInfils);
            j.V("requireEnoughExfils", b.requireEnoughExfils);
            j.V("infiltrationTarget", b.INFILTRATION_TARGET);
            j.V("infiltrationTime", b.INFILTRATION_TIME);
            j.V("selectedInfiltrationTime", b.SelectedInfiltrationTime);
            j.V("achievementName", b.AchievementName);
            j.V("unlockOnExfil", b.UnlockOnExfil);
            j.V("exfilUnlockingKey", b.exfilUnlockingKey);
            j.V("mapPrefab", b.MapPrefab == null ? null : b.MapPrefab.name);
            j.Arr("targetPackages");
            var tp = b.TARGETPACKAGE;
            if (tp != null)
                for (int i = 0; i < tp.Length; i++)
                {
                    var d = tp[i];
                    if (d == null) continue;
                    j.Obj().V("scene", d.OPERATION_SCENE).V("displayName", d.DISPLAY_NAME).V("infiltrationTime", d.INFILTRATION_TIME).EndObj();
                }
            j.EndArr();
            j.EndObj();
        }

        private static void Operations(J j)
        {
            OperationsManager o = OperationsManager.singleton;
            if (o == null) { j.Raw(null, "null"); return; }
            j.Obj();
            j.V("currentScene", o.currentScene);
            j.V("infiltrationName", o.InfiltrationName);
            j.V("operationMapID", o.operationMapID);
            j.V("currentGameMode", o.CurrentGameMode.ToString());
            j.V("isSimulation", o.IsSimulation);
            j.V("wasNight", o.WasNight);
            j.V("hvtSpawnIsRandom", o.HVTSpawnIsRandom);
            j.V("totalBots", o.TotalBots);
            j.V("totalKills", o.TotalKills);
            j.V("totalOperatorsInRaid", o.TotalOperatorsInRaid);
            j.V("intelSpawned", o.IntelSpawned);
            j.V("intelCollected", o.IntelCollected);
            j.V("raidTargetTime", o.RaidTargetTime);
            j.V("hvtDead", o.HVTdead);
            j.V("exfilUnlockingKey", o.exfilUnlockingKey);
            j.V("activeAchievementName", o.activeAchievementName);
            var hvts = o.AI_HVTs;
            j.V("hvtCount", hvts == null ? 0 : hvts.Count);
            var info = o.currentOpInfo;
            Opboard(j, "currentOpboard", info == null ? null : info.CerebusOpboard);
            j.EndObj();
        }

        private static void OperationList(J j, string key, Il2CppReferenceArray<CerebusOperation> ops)
        {
            j.Arr(key);
            if (ops != null)
                for (int i = 0; i < ops.Length; i++)
                {
                    var op = ops[i];
                    if (op == null) continue;
                    j.Obj();
                    j.V("name", op.name);
                    j.V("gameMode", op.gameMode.ToString());
                    j.V("isSimulation", op.isSimulation);
                    j.V("isLocked", op.isLocked);
                    j.V("operationIndex", op.OperationIndex);
                    j.Arr("targetPackages");
                    var tps = op.TargetPackages;
                    if (tps != null)
                        for (int k = 0; k < tps.Length; k++)
                        {
                            var tp = tps[k];
                            if (tp == null) continue;
                            j.Obj();
                            j.V("name", tp.name);
                            j.V("areaOfOperation", tp.OPERATION_AREA_OF_OPERATION);
                            j.V("isLocked", tp.isLocked);
                            j.V("isRegion", tp.isRegion);
                            j.V("isSimulation", tp.isSimulation);
                            j.V("targetPackageIndex", tp.TargetPackageIndex);
                            Opboard(j, "opboard", tp.TargetPackage);
                            j.EndObj();
                        }
                    j.EndArr();
                    j.EndObj();
                }
            j.EndArr();
        }

        private static void Boards(J j)
        {
            OperationsManager o = OperationsManager.singleton;
            if (o == null) { j.Raw(null, "null"); return; }
            j.Obj();
            OperationList(j, "active", o.ActiveOperations);
            OperationList(j, "simulation", o.SimulationOperations);
            j.EndObj();
        }

        private static void Raid(J j)
        {
            RaidManager r = RaidManager.singleton;
            if (r == null) { j.Raw(null, "null"); return; }
            j.Obj();
            j.V("objectName", r.name);
            j.V("spawnChancePerPoint", r.spawnChancePerPoint);
            j.V("botSpawnDelay", r.botSpawnDelay);
            j.V("extractTimer", r.EXTRACT_TIMER);
            j.V("globalCombatTimer", r.GlobalCombatTimer);
            Names(j, "standardAI", r.standardAI);
            Names(j, "customAI", r.customAI);
            Names(j, "hvtAI", r.hvtAI);

            j.Arr("botSpawnPoints");
            var pts = r.botSpawnPoints;
            int n = pts == null ? 0 : pts.Count;
            for (int i = 0; i < n; i++) SpawnObject(j, pts[i]);
            j.EndArr();
            _spawnCount = n;

            j.Arr("hvtSpawnPoints");
            var hv = r.hvtSpawnPoints;
            int hn = hv == null ? 0 : hv.Length;
            for (int i = 0; i < hn; i++) SpawnObject(j, hv[i]);
            j.EndArr();
            _hvtCount = hn;

            j.V("spawnVehicleAI", r.spawnVehicleAI);
            j.V("maxStaticVehicles", r.maxStaticVehicles);
            Names(j, "staticVehicleAI", r.staticVehicleAI);
            j.Arr("staticVehicleSpawnPoints");
            var sv = r.staticVehicleSpawnPoints;
            if (sv != null)
                for (int i = 0; i < sv.Length; i++) Go(j, null, sv[i]);
            j.EndArr();

            j.Obj("reinforcements");
            j.V("hasReinforcements", r.hasReinforcements);
            j.V("timedBackup", r.timedBackup);
            j.Vec("minMaxTimer", r.reinforcementMinMaxTimer);
            j.V("minAIBackupAmount", r.minAIBackupAmount);
            j.Arr("units");
            var re = r.Reinforcements;
            if (re != null)
                for (int i = 0; i < re.Length; i++)
                {
                    var u = re[i];
                    if (u == null) continue;
                    j.Obj();
                    Tf(j, u.transform);
                    j.V("activated", u.activated);
                    Names(j, "bots", u.Bots);
                    j.Arr("spawns");
                    var sp = u.Spawns;
                    if (sp != null)
                        for (int k = 0; k < sp.Length; k++)
                            if (sp[k] != null) j.Vec(null, sp[k].transform.position);
                    j.EndArr();
                    j.EndObj();
                }
            j.EndArr();
            j.EndObj();

            j.Obj("ieds");
            j.V("hasIEDs", r.hasIEDs);
            j.V("chance", r.iedChance);
            j.V("count", r.iedCount);
            j.V("prefab", r.IED == null ? null : r.IED.name);
            j.Arr("locations");
            var ied = r.IED_locations;
            if (ied != null)
                for (int i = 0; i < ied.Length; i++)
                    if (ied[i] != null) { j.Obj(); Tf(j, ied[i]); j.EndObj(); }
            j.EndArr();
            j.EndObj();

            j.Arr("objectiveSets");
            var sets = r.objectives;
            if (sets != null)
                for (int i = 0; i < sets.Length; i++)
                {
                    var set = sets[i];
                    if (set == null) continue;
                    j.Obj();
                    j.V("name", set.name);
                    j.Arr("objectives");
                    var os = set.objectives;
                    if (os != null)
                        for (int k = 0; k < os.Length; k++) Objective(j, os[k]);
                    j.EndArr();
                    j.EndObj();
                }
            j.EndArr();

            j.Arr("objectiveObjects");
            var oo = r.objectiveObjects;
            if (oo != null)
                for (int i = 0; i < oo.Count; i++) Objective(j, oo[i]);
            j.EndArr();

            j.Arr("importantObjectiveObjects");
            var io = r.ImportantObjectiveObjects;
            if (io != null)
                for (int i = 0; i < io.Count; i++) Objective(j, io[i]);
            j.EndArr();
            j.V("collectedItemsCount", r.collectedItemsCount);
            j.V("collectedImportantItemsCount", r.collectedImportantItemsCount);

            j.Arr("missionAssets");
            var ma = r.missionAssets;
            if (ma != null)
                for (int i = 0; i < ma.Count; i++)
                    if (ma[i] != null) { j.Obj(); Tf(j, ma[i].transform); j.EndObj(); }
            j.EndArr();

            j.Arr("exfilZones");
            var ex = r.exfilZones;
            int en = ex == null ? 0 : ex.Count;
            for (int i = 0; i < en; i++)
            {
                var z = ex[i];
                if (z == null) continue;
                j.Obj();
                Tf(j, z.transform);
                j.V("exfilName", z.exfilName);
                j.V("unlockingKey", z.unlockingKey);
                j.V("isHelicopter", z.isHelicopter);
                j.V("canExtract", z.canExtract);
                j.Arr("linkedInfils");
                var li = z.linkedInfils;
                if (li != null)
                    for (int k = 0; k < li.Count; k++) j.V(null, li[k]);
                j.EndArr();
                var col = z.GetComponent<Collider>();
                if (col != null)
                {
                    Bounds b = col.bounds;
                    j.Vec("boundsCenter", b.center);
                    j.Vec("boundsSize", b.size);
                }
                j.EndObj();
            }
            j.EndArr();
            _exfilCount = en;

            j.Arr("prohibitedWeapons");
            var pw = r.prohibitedWeapons;
            if (pw != null)
                for (int i = 0; i < pw.Length; i++) j.V(null, pw[i] == null ? null : pw[i].name);
            j.EndArr();
            j.Arr("mapSpecificWeapons");
            var mw = r.mapSpecificWeapons;
            if (mw != null)
                for (int i = 0; i < mw.Length; i++) j.V(null, mw[i] == null ? null : mw[i].name);
            j.EndArr();
            j.EndObj();
        }

        private static void Infiltration(J j)
        {
            InfiltrationManager m = InfiltrationManager.instance;
            if (m == null) { j.Raw(null, "null"); return; }
            j.Obj();
            j.V("objectName", m.name);
            j.V("allowRespawns", m.AllowRespawns);
            j.V("selectableExfiltration", m.SelectableExfiltration);
            j.V("raidTimer", m.RaidTimer);
            Go(j, "defaultSpawn", m.DefaultSpawn);
            j.Arr("infilMethods");
            var ms = m.infilMethods;
            if (ms != null)
                for (int i = 0; i < ms.Length; i++)
                {
                    var im = ms[i];
                    if (im == null) continue;
                    j.Obj();
                    j.V("index", i);
                    j.V("infilName", im.infilName);
                    j.V("vehicleInfiltration", im.VehicleInfiltration);
                    j.V("isPrepared", im.isPrepared);
                    Go(j, "infilObject", im.infilObject);
                    j.Arr("children");
                    if (im.infilObject != null)
                    {
                        Transform it = im.infilObject.transform;
                        int written = 0;
                        for (int c = 0; c < it.childCount && written < 60; c++)
                        {
                            Transform ch = it.GetChild(c);
                            j.Obj().V("name", ch.name).Vec("pos", ch.position).V("yaw", ch.rotation.eulerAngles.y).V("active", ch.gameObject.activeInHierarchy).EndObj();
                            written++;
                            for (int g = 0; g < ch.childCount && written < 60; g++)
                            {
                                Transform gc = ch.GetChild(g);
                                j.Obj().V("name", ch.name + "/" + gc.name).Vec("pos", gc.position).V("yaw", gc.rotation.eulerAngles.y).V("active", gc.gameObject.activeInHierarchy).EndObj();
                                written++;
                            }
                        }
                    }
                    j.EndArr();
                    var sb = im.SpawnBehaviour;
                    if (sb != null) { j.Obj("spawnBehaviour"); Tf(j, sb.transform); j.EndObj(); }
                    j.Arr("extracts");
                    var exs = im.extracts;
                    if (exs != null)
                        for (int k = 0; k < exs.Length; k++)
                            if (exs[k] != null) j.V(null, exs[k].exfilName ?? exs[k].name);
                    j.EndArr();
                    j.EndObj();
                }
            j.EndArr();
            j.EndObj();
        }

        private static void PlayerSpawns(J j)
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<SpawnPoint>(), true);
            j.Arr();
            if (found != null)
                for (int i = 0; i < found.Length; i++)
                {
                    var sp = found[i] == null ? null : found[i].TryCast<SpawnPoint>();
                    if (sp == null) continue;
                    j.Obj();
                    Tf(j, sp.transform);
                    j.V("team", sp.Team);
                    j.V("canSpawnPlayer", sp.CanSpawnPlayer);
                    j.EndObj();
                }
            j.EndArr();
        }

        private static void PlayerSlots(J j)
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PlayerSpawnDetails>(), true);
            j.Arr();
            if (found != null)
                for (int i = 0; i < found.Length; i++)
                {
                    var ps = found[i] == null ? null : found[i].TryCast<PlayerSpawnDetails>();
                    if (ps == null) continue;
                    j.Obj();
                    Tf(j, ps.transform);
                    j.V("spawnAnimation", ps.spawnAnimation);
                    j.V("isKinematic", ps.isKinematic);
                    j.EndObj();
                }
            j.EndArr();
        }

        private static void TimeOfDay(J j)
        {
            VectorTimeOfDay t = VectorTimeOfDay.singleton;
            if (t == null) { j.Raw(null, "null"); return; }
            j.Obj();
            j.V("timeOfDay", t.timeOfDay);
            j.V("hour", t.hour);
            j.V("minute", t.minute);
            j.V("isNight", t.isNight);
            j.V("isStaticLighting", t.isStaticLighting);
            j.V("orbitSpeed", t.orbitSpeed);
            j.V("isRaining", t.isRaining);
            j.V("rainChance", t.RainChance);
            j.V("isSandStorm", t.isSandStorm);
            j.V("sandStormChance", t.sandStormChance);
            j.EndObj();
        }

        private static void NavGraphs(J j)
        {
            AstarPath a = AstarPath.active;
            if (a == null) { j.Raw(null, "null"); return; }
            j.Obj();
            j.V("isScanning", a.isScanning);
            var data = a.data;
            var cache = data == null ? null : data.file_cachedStartup;
            j.V("cachedStartupFile", cache == null ? null : cache.name);
            j.Arr("graphs");
            var gs = a.graphs;
            if (gs != null)
                for (int i = 0; i < gs.Length; i++)
                {
                    var g = gs[i];
                    if (g == null) continue;
                    j.Obj();
                    j.V("name", g.name);
                    if (!a.isScanning) j.V("nodes", g.CountNodes());   // never walk a graph mid-rebuild
                    var rg = g.TryCast<Pathfinding.RecastGraph>();
                    j.V("isRecast", rg != null);
                    if (rg != null)
                    {
                        j.Vec("boundsCenter", rg.forcedBoundsCenter);
                        j.Vec("boundsSize", rg.forcedBoundsSize);
                        j.V("tileXCount", rg.tileXCount);
                        j.V("tileZCount", rg.tileZCount);
                    }
                    j.EndObj();
                }
            j.EndArr();
            j.EndObj();
        }

        private static void Bots(J j)
        {
            GameManager g = GameManager.instance;
            var ai = g == null ? null : g.allAI;
            j.Arr();
            int alive = 0;
            if (ai != null)
            {
                int n = Math.Min(ai.Count, 400);
                for (int i = 0; i < n; i++)
                {
                    var b = ai[i];
                    if (b == null) continue;
                    alive++;
                    j.Obj();
                    j.V("name", b.name);
                    j.V("myName", b.myName);
                    j.V("isHVT", b.isHVT);
                    j.Vec("pos", b.transform.position);
                    j.V("yaw", b.transform.rotation.eulerAngles.y);
                    j.EndObj();
                }
            }
            j.EndArr();
            _botCount = alive;
        }
    }
}
