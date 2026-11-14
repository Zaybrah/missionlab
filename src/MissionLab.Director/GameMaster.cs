using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MissionLab.Core;
using UnityEngine;

namespace MissionLab
{
    /// <summary>
    /// Game Master mode for the host of a running mission.
    ///
    /// Every enemy of the mission has a marker, drawn over the 3D view. The host can fly
    /// the game's own free camera through walls, place new markers where the crosshair
    /// points or where the camera is, spawn them as live enemies, remove enemies, and
    /// save all markers as a scenario file that replays later.
    ///
    /// While the mode is open the game's own key binds are switched off (they all go
    /// through InputLayer.GetKey), so the keys used here do nothing else. The free
    /// camera reads the keyboard and mouse by itself and keeps working.
    ///
    /// Live spawning goes through the game's own spawner (verified in game): hand it a
    /// list holding only our points and a matching count, call it, and put the mission's
    /// own list and count back once the enemies exist. A chosen enemy type goes through
    /// the game's custom-type list in the same way.
    /// </summary>
    internal static class GameMaster
    {
        private sealed class Marker
        {
            public SpawnPlan Plan;
            public IntPtr Bot;          // the live enemy made from this marker, if any
            public bool Waiting;        // not spawned yet
            public bool Queued;         // on its way to the spawner
            public float TurnedAt = -1f; // facing changed by hand; a live enemy is re-made shortly after
        }
        private sealed class Batch { public readonly List<Marker> Items = new List<Marker>(); public string Type; }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] Roles = { "Guard", "Wander", "Patrol" };
        private static IntPtr _raidPtr = IntPtr.Zero;
        private static List<Marker> _markers;
        private static HashSet<SpawnPlan> _later;       // the mission layout's own typed enemies, spawned shortly after the start
        private static int _current = -1;
        private static bool _active, _weOpenedCam;
        private static int _role, _stance, _type, _stay, _when, _group = -1;
        private static int _aiming = -1;                // marker whose facing follows the crosshair until the second click
        private static bool _aimOk; private static Vector3 _aim;
        private static float _removeAllAt = -10f, _clearAllAt = -10f;
        private static bool _patchTried, _weHoldInput;
        private static int _held, _resetSeen;
        private static readonly List<KeyValuePair<Marker, float>> FacingChecks = new List<KeyValuePair<Marker, float>>();

        /// <summary>True while the game's own key binds must stay silent.</summary>
        internal static bool BlockGameKeys => _active && DirectorPlugin.GmBlockKeys.Value;
        private static string _savePath;

        private static readonly Queue<Batch> Pending = new Queue<Batch>();
        private static Batch _running;
        private static float _runningSince;
        private static List<GameObject> _savedList, _savedCustom;
        private static bool _customWasNull;
        private static int _savedAmount;
        private static HashSet<IntPtr> _before;
        private static bool _keysOk = true;
        private static GUIStyle _style;
        private static bool _styleTried;

        // ------------------------------------------------------------------ mission life cycle
        /// <summary>Called by the Director once it has laid out a mission.</summary>
        public static void BeginMission(IntPtr raidPtr, HashSet<SpawnPlan> later)
        {
            Reset();
            _raidPtr = raidPtr;
            _later = later != null && later.Count > 0 ? later : null;
        }

        private static void Reset()
        {
            if (_weOpenedCam)
                try { ScreenshotFreeCamera fc = FreeCam(); if (fc != null && fc.ToggledCam) fc.ToggleOffCamera(); } catch { }
            _weOpenedCam = false;
            if (_active) ReleaseInput();
            _aiming = -1; _aimOk = false; FacingChecks.Clear();
            _raidPtr = IntPtr.Zero; _markers = null; _later = null; _current = -1; _active = false; _group = -1; _savePath = null;
            Pending.Clear(); _running = null; _savedList = null; _savedCustom = null; _before = null;
        }

        public static void Tick(float now)
        {
            if (_raidPtr == IntPtr.Zero) return;
            RaidManager raid = RaidManager.singleton;
            GameManager gm = GameManager.instance;
            if (raid == null || gm == null || raid.Pointer != _raidPtr || !Mirror.NetworkServer.active) { Reset(); return; }

            PumpSpawns(raid, gm, now);
            if (!_keysOk || !DirectorPlugin.GmEnabled.Value) return;
            try { Keys(raid, gm); }
            catch (InvalidOperationException e) { _keysOk = false; DirectorPlugin.L.LogWarning("Game Master keys disabled: " + e.Message); }
        }

        private static void Say(string text)
        {
            Director.Banner = "MissionLab GM: " + text;
            Director.BannerUntil = Time.unscaledTime + 7f;
            DirectorPlugin.L.LogInfo(Director.Banner);
        }

        // ------------------------------------------------------------------ markers
        private static void EnsureMarkers()
        {
            if (_markers != null) return;
            _markers = new List<Marker>();
            MissionPlan plan = Director.CurrentPlan;
            if (plan == null) return;
            foreach (SpawnPlan s in plan.Spawns)
                _markers.Add(new Marker { Plan = s, Waiting = _later != null && _later.Contains(s) });
        }

        private static HashSet<IntPtr> Alive(GameManager gm)
        {
            var set = new HashSet<IntPtr>();
            var ai = gm.allAI;
            for (int i = 0; ai != null && i < ai.Count; i++) if (ai[i] != null) set.Add(ai[i].Pointer);
            return set;
        }

        private static BrainAI Find(GameManager gm, IntPtr ptr)
        {
            var ai = gm.allAI;
            for (int i = 0; ai != null && i < ai.Count; i++) if (ai[i] != null && ai[i].Pointer == ptr) return ai[i];
            return null;
        }

        /// <summary>Links markers to the enemies standing on them. An enemy's own transform stays where it was spawned.</summary>
        private static void MapBots(GameManager gm, HashSet<IntPtr> onlyNew)
        {
            if (_markers == null) return;
            var taken = new HashSet<IntPtr>();
            foreach (Marker m in _markers) if (m.Bot != IntPtr.Zero) taken.Add(m.Bot);
            var ai = gm.allAI;
            foreach (Marker m in _markers)
            {
                if (m.Bot != IntPtr.Zero || (m.Waiting && !m.Queued)) continue;
                float best = 0.8f; BrainAI pick = null;
                for (int i = 0; ai != null && i < ai.Count; i++)
                {
                    BrainAI b = ai[i];
                    if (b == null || taken.Contains(b.Pointer) || (onlyNew != null && !onlyNew.Contains(b.Pointer))) continue;
                    Vector3 p = b.transform.position;
                    float d = Mathf.Sqrt((p.x - m.Plan.Pos.X) * (p.x - m.Plan.Pos.X) + (p.z - m.Plan.Pos.Z) * (p.z - m.Plan.Pos.Z)) + Mathf.Abs(p.y - m.Plan.Pos.Y) * 0.5f;
                    if (d < best) { best = d; pick = b; }
                }
                if (pick != null) { m.Bot = pick.Pointer; taken.Add(pick.Pointer); m.Waiting = false; }
            }
        }

        // ------------------------------------------------------------------ live spawning
        private static void PumpSpawns(RaidManager raid, GameManager gm, float now)
        {
            if (_running != null) { Watch(raid, gm, now); return; }
            float age = now - Director.HandledAt;
            if (age < 4f) return;                                       // let the game's own start-up spawn finish first
            if (_later != null && ((gm.allAI != null && gm.allAI.Count > 0) || age >= 8f))
            {
                EnsureMarkers();
                var mine = new List<Marker>();
                foreach (Marker m in _markers) if (_later.Contains(m.Plan)) mine.Add(m);
                _later = null;
                Enqueue(mine);
            }
            if (_markers != null)
            {
                // An enemy that was turned by hand is taken out and made again facing the new way.
                foreach (Marker m in _markers)
                {
                    if (m.TurnedAt < 0f || now - m.TurnedAt < 0.6f || m.Queued) continue;
                    m.TurnedAt = -1f;
                    BrainAI b = m.Bot == IntPtr.Zero ? null : Find(gm, m.Bot);
                    if (b == null) continue;                              // not alive: the new facing applies when it spawns
                    if (!Destroy(b)) continue;
                    m.Bot = IntPtr.Zero; m.Waiting = true;
                    Enqueue(new List<Marker> { m });
                }
                // Does a new enemy really face the way its marker says? Written to the log once per spawn.
                for (int i = FacingChecks.Count - 1; i >= 0; i--)
                {
                    if (now < FacingChecks[i].Value) continue;
                    Marker m = FacingChecks[i].Key; FacingChecks.RemoveAt(i);
                    BrainAI b = m.Bot == IntPtr.Zero ? null : Find(gm, m.Bot);
                    if (b == null) continue;
                    try
                    {
                        AgentController ac = b.agent;
                        string agentYaw = ac != null && ac.entityExists && ac.Agent != null ? ac.Agent.rotation.eulerAngles.y.ToString("0", Inv) : "none";
                        DirectorPlugin.L.LogInfo("facing check: planned " + m.Plan.Yaw.ToString("0", Inv) + ", object " + b.transform.rotation.eulerAngles.y.ToString("0", Inv) +
                                                 ", agent " + agentYaw + ", state " + b.CurrentState + ", fixed " + m.Plan.Fixed + ", prone " + m.Plan.Prone);
                    }
                    catch (Exception e) { DirectorPlugin.L.LogInfo("facing check failed: " + e.Message); }
                }
            }
            if (Pending.Count > 0) Start(Pending.Dequeue(), raid, gm, now);
        }

        /// <summary>One batch per enemy type, so each needs a single call into the game.</summary>
        private static void Enqueue(List<Marker> items)
        {
            var byType = new Dictionary<string, Batch>();
            foreach (Marker m in items)
            {
                if (m.Queued || !m.Waiting) continue;
                string type = string.IsNullOrEmpty(m.Plan.Type) ? "" : m.Plan.Type;
                if (!byType.TryGetValue(type, out Batch b)) { b = new Batch { Type = type.Length == 0 ? null : type }; byType[type] = b; Pending.Enqueue(b); }
                b.Items.Add(m); m.Queued = true;
            }
        }

        private static void Start(Batch batch, RaidManager raid, GameManager gm, float now)
        {
            var list = raid.botSpawnPoints;
            GameObject template = Director.CurrentTemplate;
            if (list == null || template == null) { Fail(batch, "the mission has no spawn point to copy"); return; }

            GameObject prefab = null;
            if (batch.Type != null)
            {
                var all = gm.AllAITypes;
                for (int i = 0; all != null && i < all.Count; i++) if (all[i] != null && all[i].name == batch.Type) prefab = all[i];
                if (prefab == null) DirectorPlugin.L.LogWarning("enemy type '" + batch.Type + "' does not exist in this game version; using the mission's usual types");
            }
            bool missionUsesCustom = raid.customAI != null && raid.customAI.Count > 0;

            _before = Alive(gm);
            _savedList = new List<GameObject>();
            for (int i = 0; i < list.Count; i++) _savedList.Add(list[i]);
            _savedAmount = gm.botAmount;
            _savedCustom = null;
            _running = batch; _runningSince = now;
            try
            {
                var points = new List<GameObject>();
                int n = 0;
                foreach (Marker m in batch.Items) points.Add(Director.BuildPoint(template, raid.transform, m.Plan, "ML gm " + (++n)));
                list.Clear();
                foreach (GameObject p in points) list.Add(p);
                gm.botAmount = points.Count;
                if (prefab != null)
                {
                    _customWasNull = raid.customAI == null;
                    if (_customWasNull) raid.customAI = new Il2CppSystem.Collections.Generic.List<GameObject>();
                    _savedCustom = new List<GameObject>();
                    for (int i = 0; i < raid.customAI.Count; i++) _savedCustom.Add(raid.customAI[i]);
                    raid.customAI.Clear();
                    raid.customAI.Add(prefab);
                    raid.ServerSpawnAI(true);
                }
                else raid.ServerSpawnAI(missionUsesCustom);
            }
            catch (Exception e)
            {
                DirectorPlugin.L.LogError("Game Master: the game's spawner threw: " + e);
                Restore(raid, gm);
                Fail(batch, "the game refused to spawn (see log)");
            }
        }

        private static void Fail(Batch batch, string why)
        {
            foreach (Marker m in batch.Items) m.Queued = false;
            _running = null;
            Say(why);
        }

        private static void Watch(RaidManager raid, GameManager gm, float now)
        {
            var fresh = new HashSet<IntPtr>();
            var ai = gm.allAI;
            for (int i = 0; ai != null && i < ai.Count; i++) if (ai[i] != null && !_before.Contains(ai[i].Pointer)) fresh.Add(ai[i].Pointer);
            if (fresh.Count < _running.Items.Count && now - _runningSince < 3f) return;

            Batch batch = _running;
            Restore(raid, gm);
            MapBots(gm, fresh);
            int made = 0;
            foreach (Marker m in batch.Items) { m.Queued = false; if (m.Bot != IntPtr.Zero) { made++; FacingChecks.Add(new KeyValuePair<Marker, float>(m, now + 1.5f)); } }
            _running = null;
            Say((made == batch.Items.Count ? "spawned " + made : "spawned " + made + " of " + batch.Items.Count) + (made == 1 ? " enemy" : " enemies") +
                (batch.Type != null ? " of type " + batch.Type : "") + " after " + (now - _runningSince).ToString("0.00", Inv) + " s | enemies alive: " + (ai != null ? ai.Count : 0));
        }

        private static void Restore(RaidManager raid, GameManager gm)
        {
            var list = raid.botSpawnPoints;
            if (list != null && _savedList != null)
            {
                list.Clear();
                foreach (GameObject g in _savedList) if (g != null) list.Add(g);
            }
            if (_savedList != null) gm.botAmount = _savedAmount;
            if (_savedCustom != null)
            {
                if (_customWasNull) raid.customAI = null;
                else
                {
                    raid.customAI.Clear();
                    foreach (GameObject g in _savedCustom) if (g != null) raid.customAI.Add(g);
                }
            }
            _savedList = null; _savedCustom = null;
        }

        // ------------------------------------------------------------------ camera and aiming
        private static ScreenshotFreeCamera FreeCam()
        {
            MainCameraSingleton mcs = MainCameraSingleton.instance;
            return mcs == null ? null : mcs.freeCamera;
        }

        private static Camera ViewCam()
        {
            ScreenshotFreeCamera fc = FreeCam();
            if (fc != null && fc.ToggledCam) { Camera c = fc.FreeCamera; if (c != null && c.isActiveAndEnabled) return c; }
            Camera m = MainCameraSingleton.MainCam;
            if (m != null && m.isActiveAndEnabled) return m;
            return Camera.main;
        }

        private static void ToggleCam()
        {
            ScreenshotFreeCamera fc = FreeCam();
            if (fc == null) { Say("this map has no free camera; placing still works from your own view"); return; }
            bool was = fc.ToggledCam;
            fc.ToggleFreeCamera();
            if (fc.ToggledCam == was) { if (was) fc.ToggleOffCamera(); else fc.ToggleOnCamera(); }     // the plain toggle did nothing: ask directly
            _weOpenedCam = fc.ToggledCam;
            Camera c = fc.FreeCamera;
            Say("free camera " + (fc.ToggledCam ? "ON: fly with WASD and the mouse" : "off") + (fc.ToggledCam == was ? " (the game did not switch it)" : ""));
            DirectorPlugin.L.LogInfo("free camera: toggled " + was + " -> " + fc.ToggledCam + ", camera '" + (c != null ? c.name : "none") + "' enabled " + (c != null && c.isActiveAndEnabled) +
                                     ", view camera '" + (ViewCam() != null ? ViewCam().name : "none") + "'");
        }

        private static bool Ray(Vector3 origin, Vector3 dir, float max, out Vector3 point)
        {
            if (Physics.Raycast(origin, dir, out RaycastHit hit, max, ~0, QueryTriggerInteraction.Ignore)) { point = hit.point; return true; }
            point = Vector3.zero;
            return false;
        }

        private static bool OnWalkable(Vector3 p)
        {
            NavMesh nav = Director.CurrentNav;
            return nav == null || nav.TriAt(p.x, p.z, p.y, 1.0f, out _) >= 0;
        }

        private static Vector3 Screen2(Camera cam, Vector3 world)
        {
            Vector3 p = cam.WorldToScreenPoint(world);
            return new Vector3(p.x, Screen.height - p.y, p.z);
        }

        /// <summary>The marker closest to the crosshair on screen, or -1.</summary>
        private static int AtCrosshair(Camera cam)
        {
            if (_markers == null || cam == null) return -1;
            float best = 90f; int pick = -1;
            for (int i = 0; i < _markers.Count; i++)
            {
                SpawnPlan s = _markers[i].Plan;
                Vector3 p = Screen2(cam, new Vector3(s.Pos.X, s.Pos.Y + (s.Prone ? 0.3f : 1.0f), s.Pos.Z));
                if (p.z <= 0.1f) continue;
                float d = Mathf.Sqrt((p.x - Screen.width * 0.5f) * (p.x - Screen.width * 0.5f) + (p.y - Screen.height * 0.5f) * (p.y - Screen.height * 0.5f));
                if (d < best) { best = d; pick = i; }
            }
            return pick;
        }

        // ------------------------------------------------------------------ keys
        private static bool Down(BepInEx.Configuration.ConfigEntry<KeyCode> key) => key.Value != KeyCode.None && Input.GetKeyDown(key.Value);

        private static List<string> Types(GameManager gm)
        {
            var names = new List<string> { "Random" };
            var all = gm.AllAITypes;
            for (int i = 0; all != null && i < all.Count; i++) if (all[i] != null) names.Add(all[i].name);
            return names;
        }

        /// <summary>Keeps the game's own key binds silent while the mode is open.</summary>
        private static void HoldInput()
        {
            if (!DirectorPlugin.GmBlockKeys.Value) return;
            if (!_patchTried) { _patchTried = true; DirectorPlugin.PatchInputBlock(); }
            try
            {
                if (_held > 0 && !InputLayer.InputDisabled) _resetSeen++;      // the game switched it back on by itself
                InputLayer.InputDisabled = true;
                ScreenshotFreeCamera fc = FreeCam();
                InputLayer.MouseInputSuppressed = fc != null && fc.ToggledCam;     // your own view must not turn while you fly
                _held++; _weHoldInput = true;
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("could not switch the game's keys off: " + e.Message); }
        }

        private static void ReleaseInput()
        {
            if (!_weHoldInput) return;
            _weHoldInput = false;
            try { InputLayer.InputDisabled = false; InputLayer.MouseInputSuppressed = false; } catch { }
            DirectorPlugin.L.LogInfo("game keys back on (held " + _held + " frames; the game reset its own switch " + _resetSeen + " times; key hook " + (DirectorPlugin.InputHooked ? "active" : "not active") + ")");
            _held = 0; _resetSeen = 0;
        }

        private static void Keys(RaidManager raid, GameManager gm)
        {
            float now = Time.unscaledTime;
            if (Down(DirectorPlugin.KeyGm))
            {
                if (!_active && now - Director.HandledAt < 4f) { Say("wait a few seconds until the mission has finished spawning"); return; }
                _active = !_active;
                if (_active) { EnsureMarkers(); MapBots(gm, null); Say("Game Master mode on. " + DirectorPlugin.KeyCam.Value + " switches the free camera."); }
                else { CancelAiming(); ReleaseInput(); Say("Game Master mode off"); }
            }
            if (!_active) return;
            HoldInput();

            if (Down(DirectorPlugin.KeyCam)) ToggleCam();
            if (Down(DirectorPlugin.KeySave)) Save();

            // What the next enemy will be.
            if (Input.GetKeyDown(KeyCode.Alpha1)) _role = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2)) _role = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3)) _role = 2;
            if (Down(DirectorPlugin.KeyStance)) _stance = 1 - _stance;
            if (Down(DirectorPlugin.KeyType)) { int n = Types(gm).Count; _type = (_type + (Input.GetKey(KeyCode.LeftShift) ? n - 1 : 1)) % n; }
            if (Down(DirectorPlugin.KeyStay)) _stay = 1 - _stay;
            if (Down(DirectorPlugin.KeyWhen)) _when = 1 - _when;

            // Where the crosshair points; also drawn as a preview.
            Camera cam = ViewCam();
            _aimOk = false;
            if (cam != null) { Vector3 f = cam.transform.forward; _aimOk = Ray(cam.transform.position + f * 0.6f, f, 400f, out _aim); }

            // Between the two clicks, the new enemy looks at the crosshair.
            if (_aiming >= _markers.Count) _aiming = -1;
            if (_aiming >= 0 && _aimOk)
            {
                SpawnPlan p = _markers[_aiming].Plan;
                float dx = _aim.x - p.Pos.X, dz = _aim.z - p.Pos.Z;
                if (dx * dx + dz * dz > 0.25f) p.Yaw = (Mathf.Atan2(dx, dz) * Mathf.Rad2Deg + 360f) % 360f;
            }

            if (Input.GetMouseButtonDown(0)) { if (_aiming >= 0) ConfirmAiming(); else Place(gm, cam, true); }
            if (Down(DirectorPlugin.KeyPlaceHere)) { CancelAiming(); Place(gm, cam, false); }
            if (Input.GetMouseButtonDown(1))
            {
                if (_aiming >= 0) { CancelAiming(); Say("placing cancelled"); }
                else { int i = AtCrosshair(cam); if (i >= 0) { _current = i; Say("enemy #" + (i + 1) + " picked: turn it with the mouse wheel"); } else Say("no marker near the crosshair"); }
            }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f && _aiming < 0 && _current >= 0 && _current < _markers.Count)
            {
                Marker m = _markers[_current];
                m.Plan.Yaw = ((m.Plan.Yaw + (wheel > 0f ? 15f : -15f)) % 360f + 360f) % 360f;
                m.TurnedAt = now;
            }
            if (Down(DirectorPlugin.KeyStop)) AddStop();
            if (Down(DirectorPlugin.KeySpawn)) SpawnWaiting();
            if (Down(DirectorPlugin.KeyRemove))
            {
                if (Input.GetKey(KeyCode.LeftControl))
                {
                    if (now - _clearAllAt < 3f) { _clearAllAt = -10f; CancelAiming(); RemoveAllEnemies(gm); int n = _markers.Count; _markers.Clear(); _current = -1; Say("all " + n + " markers and their enemies removed: the map is empty"); }
                    else { _clearAllAt = now; Say("press Ctrl+" + DirectorPlugin.KeyRemove.Value + " again within 3 seconds to remove EVERY marker and enemy"); }
                }
                else Remove(gm, cam);
            }
            if (Down(DirectorPlugin.KeyRemoveAll))
            {
                if (now - _removeAllAt < 3f) { _removeAllAt = -10f; RemoveAllEnemies(gm); }
                else { _removeAllAt = now; Say("press " + DirectorPlugin.KeyRemoveAll.Value + " again within 3 seconds to remove all live enemies (their markers stay)"); }
            }
        }

        private static void Place(GameManager gm, Camera cam, bool atCrosshair)
        {
            if (cam == null) { Say("no camera to aim with"); return; }
            Vector3 eye = cam.transform.position, pos;
            float yaw;
            if (atCrosshair)
            {
                if (!_aimOk) { Say("nothing solid under the crosshair within 400 m"); return; }
                pos = _aim;
                yaw = Mathf.Atan2(eye.x - pos.x, eye.z - pos.z) * Mathf.Rad2Deg;       // until the second click: faces the camera
            }
            else
            {
                if (!Ray(eye + Vector3.up * 0.1f, Vector3.down, 6f, out pos)) { Say("no floor within 6 m below the camera"); return; }
                yaw = cam.transform.eulerAngles.y;                                     // faces where the camera looks
            }

            if (_group < 0) { _group = 0; foreach (Marker m in _markers) _group = Math.Max(_group, m.Plan.Group + 1); }
            var types = Types(gm);
            bool walkable = OnWalkable(pos);
            var s = new SpawnPlan
            {
                Group = _group, Role = (SpawnRole)_role, Pos = new V3(pos.x, pos.y, pos.z), Yaw = (yaw % 360f + 360f) % 360f,
                Defensive = _role == 0, Prone = _stance == 1, Type = _type > 0 && _type < types.Count ? types[_type] : null,
                Fixed = _stay == 1 || !walkable, PatrolWaitSeconds = 4f,
            };
            if (s.Fixed) s.Role = SpawnRole.Guard;
            _markers.Add(new Marker { Plan = s, Waiting = true });
            _current = _markers.Count - 1;
            if (atCrosshair)
            {
                _aiming = _current;
                Say("enemy #" + _markers.Count + ": now aim where it should look and click again (right click cancels)" + (!walkable ? ". Not walkable ground here, so it will stay put." : ""));
            }
            else Finish(_current, !walkable);
        }

        private static void ConfirmAiming()
        {
            int i = _aiming; _aiming = -1;
            if (i >= 0 && i < _markers.Count) Finish(i, false);
        }

        private static void CancelAiming()
        {
            if (_aiming >= 0 && _aiming < _markers.Count) { _markers.RemoveAt(_aiming); if (_current >= _aiming) _current = -1; }
            _aiming = -1;
        }

        private static void Finish(int index, bool offGround)
        {
            Marker m = _markers[index]; SpawnPlan s = m.Plan;
            bool now = _when == 0 && s.Role != SpawnRole.Patrol;
            if (now) Enqueue(new List<Marker> { m });
            Say("enemy #" + (index + 1) + " placed: " + (s.Fixed ? "fixed sentry" : s.Role.ToString()) + (s.Prone ? ", prone" : "") +
                (offGround ? " (not on walkable ground, so it stays put)" : "") +
                (now ? "" : s.Role == SpawnRole.Patrol ? ". Aim at each patrol stop and press " + DirectorPlugin.KeyStop.Value + ", then " + DirectorPlugin.KeySpawn.Value + " to spawn."
                                                       : ". It waits until you press " + DirectorPlugin.KeySpawn.Value + "."));
        }

        private static void AddStop()
        {
            if (_current < 0 || _current >= _markers.Count) { Say("pick an enemy first (right click on its marker)"); return; }
            Marker m = _markers[_current];
            if (m.Plan.Fixed) { Say("enemy #" + (_current + 1) + " is a fixed sentry and cannot patrol"); return; }
            if (!_aimOk) { Say("nothing solid under the crosshair"); return; }
            if (!OnWalkable(_aim)) { Say("that spot is not walkable ground; a patrol stop must be"); return; }
            m.Plan.Role = SpawnRole.Patrol;
            m.Plan.Patrol.Add(new V3(_aim.x, _aim.y, _aim.z));
            Say("patrol stop " + m.Plan.Patrol.Count + " for #" + (_current + 1) + (m.Waiting ? (m.Plan.Patrol.Count < 2 ? ": add at least one more" : ": " + DirectorPlugin.KeySpawn.Value + " spawns it") : " (this enemy is already alive; the route applies when the scenario is replayed)"));
        }

        private static void SpawnWaiting()
        {
            var list = new List<Marker>();
            for (int i = 0; i < _markers.Count; i++) if (i != _aiming && _markers[i].Waiting && !_markers[i].Queued) list.Add(_markers[i]);
            if (list.Count == 0) { Say("no waiting enemies"); return; }
            Enqueue(list);
            Say("spawning " + list.Count + " waiting enemies");
        }

        private static bool Destroy(BrainAI b)
        {
            try { Mirror.NetworkServer.Destroy(b.gameObject); return true; }
            catch (Exception e) { DirectorPlugin.L.LogWarning("could not remove enemy '" + b.name + "': " + e.Message); return false; }
        }

        private static void Remove(GameManager gm, Camera cam)
        {
            int i = AtCrosshair(cam);
            if (i < 0) i = _current;
            if (i < 0 || i >= _markers.Count) { Say("no marker near the crosshair"); return; }
            Marker m = _markers[i];
            if (m.Queued) { Say("enemy #" + (i + 1) + " is being spawned; try again in a moment"); return; }
            BrainAI b = m.Bot == IntPtr.Zero ? null : Find(gm, m.Bot);
            int before = gm.allAI != null ? gm.allAI.Count : 0;
            bool removed = b != null && Destroy(b);
            _markers.RemoveAt(i);
            _current = _current == i ? -1 : _current > i ? _current - 1 : _current;
            _aiming = _aiming == i ? -1 : _aiming > i ? _aiming - 1 : _aiming;
            Say("enemy #" + (i + 1) + " removed" + (removed ? " (enemies alive before: " + before + ")" : b != null ? "; its live enemy could not be removed" : ""));
        }

        private static void RemoveAllEnemies(GameManager gm)
        {
            var all = new List<BrainAI>();
            var ai = gm.allAI;
            for (int i = 0; ai != null && i < ai.Count; i++) if (ai[i] != null) all.Add(ai[i]);
            int removed = 0;
            foreach (BrainAI b in all) if (Destroy(b)) removed++;
            foreach (Marker m in _markers) { m.Bot = IntPtr.Zero; m.Waiting = true; }
            Say(removed + " of " + all.Count + " enemies removed. Their markers wait: " + DirectorPlugin.KeySpawn.Value + " spawns them again.");
        }

        // ------------------------------------------------------------------ saving
        private static void Save()
        {
            if (_markers == null || _markers.Count == 0) { Say("nothing to save: place at least one enemy"); return; }
            NavMesh nav = Director.CurrentNav;
            string mapFile = nav != null ? nav.SourceFile : (Director.CurrentPlan != null ? Director.CurrentPlan.NavFile : null);
            bool fromFile = Director.CurrentScenarioPath != null;
            var plan = new MissionPlan { Seed = 0, Scene = fromFile ? (Director.CurrentScenarioScene ?? "") : "", NavFile = mapFile };
            foreach (Marker m in _markers) { plan.Spawns.Add(m.Plan); plan.Groups = Math.Max(plan.Groups, m.Plan.Group + 1); }
            plan.RequestedEnemies = plan.Spawns.Count;
            plan.Notes.Add("Saved from Game Master mode");
            try
            {
                string dir = Path.Combine(DirectorPlugin.BaseDir, "scenarios");
                if (_savePath == null)
                {
                    if (fromFile)
                    {
                        // Editing the scenario this mission was started from: keep one copy of how it was.
                        _savePath = Director.CurrentScenarioPath;
                        string off = Path.Combine(dir, "off");
                        Directory.CreateDirectory(off);
                        File.Copy(_savePath, Path.Combine(off, Path.GetFileNameWithoutExtension(_savePath) + ".before-" + DateTime.Now.ToString("MMdd-HHmmss", Inv) + ".json"), true);
                    }
                    else
                    {
                        string map = string.IsNullOrEmpty(mapFile) ? "map" : Path.GetFileNameWithoutExtension(mapFile);
                        _savePath = Path.Combine(dir, map + "_gm_" + DateTime.Now.ToString("MMdd-HHmm", Inv) + ".json");
                    }
                }
                Directory.CreateDirectory(dir);
                File.WriteAllText(_savePath, plan.ToJson());
                Say(plan.Spawns.Count + " enemies saved to scenarios/" + Path.GetFileName(_savePath) + (string.IsNullOrEmpty(mapFile) && plan.Scene.Length == 0 ? " (map unknown: it will only load when named in the settings)" : ""));
            }
            catch (Exception e) { Say("could not save: " + e.Message); }
        }

        // ------------------------------------------------------------------ overlay
        private static void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

        private static void Line(Vector3 a, Vector3 b, Color c, float width)
        {
            if (a.z <= 0.1f || b.z <= 0.1f) return;                     // an end is behind the camera
            float dx = b.x - a.x, dy = b.y - a.y, len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f || len > 6000f) return;
            Matrix4x4 keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, new Vector2(a.x, a.y));
            Fill(new Rect(a.x, a.y - width * 0.5f, len, width), c);
            GUI.matrix = keep;
        }

        private static void Text(float x, float y, float w, string text, Color c)
        {
            if (!_styleTried)
            {
                _styleTried = true;
                try { _style = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false }; } catch { _style = null; }
            }
            var shadow = new Rect(x + 1f, y + 1f, w, 22f); var rect = new Rect(x, y, w, 22f);
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            if (_style != null) GUI.Label(shadow, text, _style); else GUI.Label(shadow, text);
            GUI.color = c;
            if (_style != null) GUI.Label(rect, text, _style); else GUI.Label(rect, text);
            GUI.color = Color.white;
        }

        public static void Draw()
        {
            if (!_active || _markers == null) return;
            Event ev = Event.current;
            if (ev != null && ev.type != EventType.Repaint) return;
            GameManager gm = GameManager.instance;
            Camera cam = ViewCam();
            if (gm == null || cam == null) return;
            float sw = Screen.width, sh = Screen.height;
            var white = new Color(1f, 1f, 1f, 0.95f); var live = new Color(1f, 0.42f, 0.3f, 0.95f); var waiting = new Color(1f, 0.82f, 0.25f, 0.95f);

            // Crosshair: the free camera has none.
            Fill(new Rect(sw * 0.5f - 9f, sh * 0.5f - 1f, 18f, 2f), white);
            Fill(new Rect(sw * 0.5f - 1f, sh * 0.5f - 9f, 2f, 18f), white);

            // Markers: a post where the enemy stands, a tick for where it faces, its number and role.
            HashSet<IntPtr> alive = Alive(gm);
            Vector3 eye = cam.transform.position;
            int liveCount = 0, waitCount = 0;
            for (int i = 0; i < _markers.Count; i++)
            {
                Marker m = _markers[i]; SpawnPlan s = m.Plan;
                bool isLive = m.Bot != IntPtr.Zero && alive.Contains(m.Bot);
                if (isLive) liveCount++; else if (m.Waiting) waitCount++;
                var foot = new Vector3(s.Pos.X, s.Pos.Y, s.Pos.Z);
                float dist = Vector3.Distance(eye, foot);
                if (dist > 350f) continue;
                Color c = i == _current || i == _aiming ? white : isLive ? live : m.Waiting ? waiting : new Color(0.75f, 0.75f, 0.75f, 0.8f);
                float h = s.Prone ? 0.35f : 1.8f;
                Vector3 head = foot + Vector3.up * h;
                Vector3 a = Screen2(cam, foot), b = Screen2(cam, head);
                if (a.z <= 0.1f || b.z <= 0.1f) continue;
                Line(a, b, c, 3f);
                float yaw = s.Yaw * Mathf.Deg2Rad;
                var facing = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                if (s.Prone) Line(Screen2(cam, foot - facing * 0.9f + Vector3.up * 0.2f), Screen2(cam, foot + facing * 0.9f + Vector3.up * 0.2f), c, 3f);   // lying body
                Line(b, Screen2(cam, head + facing * (i == _current || i == _aiming ? 1.6f : 0.8f)), c, i == _current || i == _aiming ? 3f : 2f);     // where it looks
                Fill(new Rect(b.x - 4f, b.y - 4f, 8f, 8f), c);
                Text(b.x + 8f, b.y - 12f, 420f, "#" + (i + 1) + " " + (s.Fixed ? "Sentry" : s.Role.ToString()) + (s.Prone ? " prone" : "") +
                     (isLive ? "" : m.Waiting ? " (waiting)" : " (gone)") + "  " + dist.ToString("0", Inv) + " m", c);

                if (s.Role == SpawnRole.Patrol && s.Patrol.Count > 0)
                {
                    Vector3 prev = Screen2(cam, foot + Vector3.up * 0.2f);
                    for (int k = 0; k <= s.Patrol.Count; k++)
                    {
                        if (k == s.Patrol.Count && s.Patrol.Count < 3) break;
                        V3 w = s.Patrol[k % s.Patrol.Count];
                        Vector3 p = Screen2(cam, new Vector3(w.X, w.Y + 0.2f, w.Z));
                        Line(prev, p, c, 2f);
                        if (p.z > 0.1f && k < s.Patrol.Count) Fill(new Rect(p.x - 3f, p.y - 3f, 6f, 6f), c);
                        prev = p;
                    }
                }
            }

            // Preview: where a click would put the next enemy.
            if (_aiming < 0 && _aimOk)
            {
                Vector3 g0 = Screen2(cam, _aim), g1 = Screen2(cam, _aim + Vector3.up * (_stance == 1 ? 0.35f : 1.8f));
                var ghost = new Color(1f, 1f, 1f, 0.55f);
                Line(g0, g1, ghost, 2f);
                if (g0.z > 0.1f) Fill(new Rect(g0.x - 5f, g0.y - 1f, 10f, 2f), ghost);
            }

            // The panel: state and every key.
            ScreenshotFreeCamera fc = FreeCam();
            var types = Types(gm);
            if (_type >= types.Count) _type = 0;
            string role = (_role == 0 ? "[1] >GUARD<   [2] Wander   [3] Patrol" : _role == 1 ? "[1] Guard   [2] >WANDER<   [3] Patrol" : "[1] Guard   [2] Wander   [3] >PATROL<");
            string kStance = DirectorPlugin.KeyStance.Value.ToString(), kType = DirectorPlugin.KeyType.Value.ToString(), kStay = DirectorPlugin.KeyStay.Value.ToString(), kWhen = DirectorPlugin.KeyWhen.Value.ToString();
            var lines = new List<string>
            {
                "MISSIONLAB GAME MASTER   " + DirectorPlugin.KeyGm.Value + " closes" + (DirectorPlugin.GmBlockKeys.Value ? "   (game keys are off while this is open)" : ""),
                liveCount + " of your markers alive, " + waitCount + " waiting, " + alive.Count + " enemies in the mission" + (_running != null || Pending.Count > 0 ? "   spawning..." : ""),
                "Camera: " + (fc == null ? "this map has no free camera" : fc.ToggledCam ? "FREE, flies through walls (" + DirectorPlugin.KeyCam.Value + " returns to you)" : "yours (" + DirectorPlugin.KeyCam.Value + " = free camera)"),
                "",
                "NEXT ENEMY   " + role,
                "[" + kStance + "] " + (_stance == 1 ? "PRONE (lying down)" : "Standing") + "      [" + kType + "] Type: " + types[_type],
                "[" + kStay + "] Stays put: " + (_stay == 1 ? "ALWAYS (fixed sentry)" : "only where it cannot walk") + "      [" + kWhen + "] Appears: " + (_when == 0 ? "at once" : "when you press " + DirectorPlugin.KeySpawn.Value),
                "",
                _aiming >= 0 ? ">> AIM WHERE IT SHOULD LOOK, THEN LEFT CLICK   (right click cancels)" : "Left click    place at the crosshair, then aim where it should look and click again",
                DirectorPlugin.KeyPlaceHere.Value + "   place where the camera is, looking where you look (for tight spots)",
                "Right click   pick the enemy at the crosshair      Mouse wheel   turn the picked enemy",
                DirectorPlugin.KeyStop.Value + "   patrol stop for the picked enemy      " + DirectorPlugin.KeyRemove.Value + "   remove the enemy at the crosshair",
                DirectorPlugin.KeySpawn.Value + "   spawn the waiting enemies (" + waitCount + ")      " + DirectorPlugin.KeySave.Value + "   save as scenario",
                DirectorPlugin.KeyRemoveAll.Value + " twice   remove all live enemies      Ctrl+" + DirectorPlugin.KeyRemove.Value + " twice   empty the map",
            };
            float x = 24f, y = 96f, wPanel = 730f, lh = 21f;
            Fill(new Rect(x - 10f, y - 8f, wPanel, lines.Count * lh + 16f), new Color(0f, 0f, 0f, 0.62f));
            for (int i = 0; i < lines.Count; i++, y += lh)
                if (lines[i].Length > 0) Text(x, y, wPanel, lines[i], (i == 8 && _aiming >= 0) || i == 4 ? waiting : white);
        }
    }
}
