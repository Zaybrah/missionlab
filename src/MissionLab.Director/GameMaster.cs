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
            public int Id = _nextId++;  // stable name for the browser editor
            public SpawnPlan Plan;
            public IntPtr Bot;          // the live enemy made from this marker, if any
            public bool Waiting;        // not spawned yet
            public bool Queued;         // on its way to the spawner
            public float TurnedAt = -1f; // facing changed by hand; a live enemy is re-made shortly after
            public float RemakeAt = -1f; // its old enemy has been removed; spawn the new one at this time
            public float FaceYaw = float.NaN; // for this one spawn: look this way instead (toward the player who reached its zone)
        }
        private sealed class Batch { public readonly List<Marker> Items = new List<Marker>(); public string Type; }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] Roles = { "Guard", "Wander", "Patrol" };
        private static int _nextId = 1;
        private static List<TriggerPlan> _zones = new List<TriggerPlan>();
        private static readonly HashSet<int> Fired = new HashSet<int>();
        private static float _zoneCheckAt;
        private static bool _yawLogged, _objectivesPlaced;
        private static List<ObjectivePlan> _wanted = new List<ObjectivePlan>();
        private sealed class Pose { public Vector3 Pos; public float Yaw; }
        private static readonly Dictionary<IntPtr, Pose> Homes = new Dictionary<IntPtr, Pose>();   // where the game itself put each item
        private static bool _resendFailed;
        private static List<DoorPlan> _doorPlans = new List<DoorPlan>();    // what the scenario or the Game Master asked of doors
        private static readonly Dictionary<int, float> MetAt = new Dictionary<int, float>();          // trigger -> when the thing it waits for happened
        private static readonly Dictionary<int, Vector3> MetWhere = new Dictionary<int, Vector3>();   // zone -> where the player stood
        private static readonly Dictionary<IntPtr, string[]> Items = new Dictionary<IntPtr, string[]>();   // intel seen in play -> what it goes by
        private static readonly HashSet<IntPtr> Taken = new HashSet<IntPtr>();
        private static bool _alerted;
        internal static bool HoldExtraction;      // read by the hook: the game may not open the extraction yet
        internal static bool ExtractionAsked;     // ...but it has tried to
        internal static int ForcedInfil = -1;     // read by the hook: the insertion every player gets (-1: their own choice)
        private static int _holdAfter;            // the trigger the extraction waits for
        private static string _insertion = "";
        private static WeatherPlan _weather = new WeatherPlan();
        private static string _power = "";
        private static float _vehicleCheckAt = -1f;
        private static List<DoorV2> _doorList;
        private static float _doorScanAt;
        private static string _doorError;
        private static IntPtr _raidPtr = IntPtr.Zero;
        private static List<Marker> _markers;
        private static HashSet<SpawnPlan> _later;       // the mission layout's own typed enemies, spawned shortly after the start
        private static int _current = -1;
        private static bool _active, _weOpenedCam;
        private static int _role, _stance, _type, _stay, _when, _group = -1;
        private static int _aiming = -1;                // marker whose facing follows the crosshair until the second click
        private static bool _aimOk; private static Vector3 _aim;
        private static float _removeAllAt = -10f, _clearAllAt = -10f, _exportAt = -1f;
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
            // Intel goes to its place straight away. A friend's game is told where every item lies when it finishes
            // loading, which is normally after this moment, so they get the new place with nothing extra being sent.
            MissionPlan plan = Director.CurrentPlan;
            string sceneName = plan != null ? plan.Scene : "";
            try { RaidManager now = RaidManager.singleton; if (now != null) sceneName = now.gameObject.scene.name; } catch { }
            try { Replay.Begin(sceneName, Director.CurrentNav != null ? Director.CurrentNav.SourceFile : "", plan != null ? plan.ToJson() : null); }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the replay could not be started: " + e.Message); }
            if (plan == null) return;
            _weather = plan.Weather ?? new WeatherPlan();
            _power = plan.Power ?? "";
            _insertion = plan.Insertion ?? "";
            if (_insertion != "")
            {
                try { DirectorPlugin.L.LogInfo("insertion: " + ForceInsertion(_insertion)); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the insertion could not be set: " + e.Message); }
            }
            if (plan.ExtractionAfter != 0 && plan.Triggers.Exists(t => t.Id == plan.ExtractionAfter))
            {
                _holdAfter = plan.ExtractionAfter; HoldExtraction = true;
                DirectorPlugin.PatchExtraction();
                DirectorPlugin.L.LogInfo("extraction: held back until trigger " + _holdAfter + " has fired");
            }
            if (plan.Doors.Count > 0)
            {
                // Doors too: a door may not have decided yet whether it starts locked, so both its starting
                // settings and its present state are set, and the state is checked again a few seconds in.
                try
                {
                    _doorPlans = new List<DoorPlan>(plan.Doors);
                    DirectorPlugin.L.LogInfo("doors at the start: " + ApplyDoors(true));
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("doors could not be set at the start: " + e.Message); }
            }
            if (plan.Objectives.Count == 0) return;
            try
            {
                RaidManager raid = RaidManager.singleton;
                if (raid == null || raid.Pointer != raidPtr) return;
                _wanted = new List<ObjectivePlan>(plan.Objectives);
                DirectorPlugin.L.LogInfo("objectives at the start: moved " + PlaceObjectives(raid) + " item(s) for " + _wanted.Count + " named in the scenario; in the map now: " + Describe(raid));
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("objectives could not be placed at the start: " + e.Message); }
        }

        private static void Reset()
        {
            if (_weOpenedCam)
                try { ScreenshotFreeCamera fc = FreeCam(); if (fc != null && fc.ToggledCam) fc.ToggleOffCamera(); } catch { }
            _weOpenedCam = false;
            if (_active) ReleaseInput();
            _aiming = -1; _aimOk = false; FacingChecks.Clear();
            _zones = new List<TriggerPlan>(); Fired.Clear(); _yawLogged = false;
            _wanted = new List<ObjectivePlan>(); _objectivesPlaced = false; Homes.Clear();
            _doorPlans = new List<DoorPlan>(); _doorList = null; _doorScanAt = 0f; _doorError = null;
            MetAt.Clear(); MetWhere.Clear(); Items.Clear(); Taken.Clear(); _alerted = false; BornAs.Clear(); Changed.Clear(); Vehicles.Clear(); TryOuts.Reset();
            HoldExtraction = false; ExtractionAsked = false; ForcedInfil = -1; _holdAfter = 0; _insertion = ""; _weather = new WeatherPlan(); _power = ""; _vehicleCheckAt = -1f;
            _raidPtr = IntPtr.Zero; _markers = null; _later = null; _current = -1; _active = false; _group = -1; _savePath = null;
            Pending.Clear(); _running = null; _savedList = null; _savedCustom = null; _before = null;
        }

        public static void Tick(float now)
        {
            if (_raidPtr == IntPtr.Zero) return;
            RaidManager raid = RaidManager.singleton;
            GameManager gm = GameManager.instance;
            if (raid == null || gm == null || raid.Pointer != _raidPtr || !Mirror.NetworkServer.active) { Replay.End(); Reset(); return; }

            Replay.Tick(raid, gm, now);
            TryOuts.Tick(Time.unscaledTime);
            PumpSpawns(raid, gm, now);
            if (!_keysOk || !DirectorPlugin.GmEnabled.Value) return;
            try { Keys(raid, gm); }
            catch (InvalidOperationException e) { _keysOk = false; DirectorPlugin.L.LogWarning("Game Master keys disabled: " + e.Message); }
        }

        internal static void Say(string text)
        {
            Director.Banner = "MissionLab GM: " + text;
            Director.BannerUntil = Time.unscaledTime + 7f;
            DirectorPlugin.L.LogInfo(Director.Banner);
        }

        /// <summary>On screen only while someone is acting as Game Master; a player should not be told an ambush just appeared.</summary>
        private static void Tell(string text)
        {
            Replay.Event("note", text);
            if (_active || Time.unscaledTime - LiveLink.LastRequestAt < 5f) Say(text);
            else DirectorPlugin.L.LogInfo("MissionLab: " + text);
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
            _zones = new List<TriggerPlan>(plan.Triggers);
            _wanted = new List<ObjectivePlan>(plan.Objectives);
        }

        /// <summary>The mission's objective items that are in play (the game switches the unused ones off).</summary>
        private static List<ObjectiveObject> Objectives(RaidManager raid)
        {
            var found = new List<ObjectiveObject>();
            var sets = raid.objectives;
            for (int i = 0; sets != null && i < sets.Length; i++)
            {
                var items = sets[i] == null ? null : sets[i].objectives;
                for (int k = 0; items != null && k < items.Length; k++)
                    if (items[k] != null && items[k].gameObject.activeInHierarchy) found.Add(items[k]);
            }
            return found;
        }

        // ------------------------------------------------------------------ mission-wide settings
        private static bool Special(SpawnPlan s) => s.Hvt || s.Vehicle;     // not spawned like an ordinary enemy

        internal static List<string> Insertions()
        {
            var names = new List<string>();
            try
            {
                InfiltrationManager im = InfiltrationManager.instance;
                var methods = im == null ? null : im.infilMethods;
                for (int i = 0; methods != null && i < methods.Length; i++) names.Add(methods[i] == null ? "" : methods[i].infilName ?? "");
            }
            catch { }
            return names;
        }

        private static string ForceInsertion(string name)
        {
            List<string> names = Insertions();
            int at = names.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (at < 0) return "'" + name + "' is not an insertion of this map (" + string.Join(", ", names) + "); players keep their own choice";
            ForcedInfil = at;
            DirectorPlugin.PatchInsertion();
            ApplyForcedInfil("when the mission was set up");
            try
            {
                InfilSelectorDisplayer ui = InfilSelectorDisplayer.instance;
                if (ui != null) for (int i = 0; i < names.Count; i++) ui.SetInfilEnabled(i, i == at);        // the host's own map shows only this one
            }
            catch { }
            return "every player gets '" + names[at] + "' (number " + at + " of " + names.Count + ")";
        }

        /// <summary>
        /// Players choose their insertion in the lobby and bring the choice with them. This writes the scenario's
        /// over it, through the game's own synced value, so every player's game agrees.
        /// </summary>
        internal static void ApplyForcedInfil(string when)
        {
            if (ForcedInfil < 0) return;
            var players = PlayerMaster.AllPlayerMasters;
            int moved = 0, all = 0;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerMaster pm = players[i];
                if (pm == null) continue;
                all++;
                try { if (pm.CurrentInfilIndex != ForcedInfil) { pm.NetworkCurrentInfilIndex = ForcedInfil; moved++; } } catch { }
            }
            DirectorPlugin.L.LogInfo("insertion: " + moved + " of " + all + " player(s) switched to number " + ForcedInfil + " " + when);
        }

        /// <summary>Which insertion a spot belongs to, for the log.</summary>
        private static string InsertionAt(Vector3 p)
        {
            string best = "no insertion"; float near = 15f * 15f;
            try
            {
                InfiltrationManager im = InfiltrationManager.instance;
                var methods = im == null ? null : im.infilMethods;
                for (int i = 0; methods != null && i < methods.Length; i++)
                {
                    GameObject go = methods[i] == null ? null : methods[i].infilObject;
                    if (go == null) continue;
                    Transform t = go.transform;
                    for (int k = 0; k < t.childCount; k++)
                    {
                        float sq = (t.GetChild(k).position - p).sqrMagnitude;
                        if (sq < near) { near = sq; best = "'" + methods[i].infilName + "'"; }
                    }
                }
            }
            catch { }
            return best;
        }

        private static bool ExtractionOpen(RaidManager raid)
        {
            var zones = raid.exfilZones;
            for (int i = 0; zones != null && i < zones.Count; i++)
                try { if (zones[i] != null && zones[i].canExtract) return true; } catch { }
            return false;
        }

        /// <summary>Lets the team extract now, by the game's own switch first.</summary>
        private static void OpenExtraction(RaidManager raid, string why)
        {
            HoldExtraction = false;
            string how = "the game's own switch";
            try { raid.EnableGlobalExtraction(); } catch (Exception e) { how = "set directly (" + e.Message + ")"; }
            if (!ExtractionOpen(raid))
            {
                how = "set on each extraction zone";
                var zones = raid.exfilZones;
                for (int i = 0; zones != null && i < zones.Count; i++)
                    try { if (zones[i] != null) zones[i].canExtract = true; } catch { }
                try { raid.RPC_CanExfil(); } catch { }
            }
            Tell("extraction is open (" + why + "; " + how + ")");
        }

        /// <summary>While a scenario holds the extraction back, an opening that slipped past the hook is undone.</summary>
        private static void KeepExtractionShut(RaidManager raid)
        {
            var zones = raid.exfilZones;
            for (int i = 0; zones != null && i < zones.Count; i++)
                try { if (zones[i] != null && zones[i].canExtract) { zones[i].canExtract = false; ExtractionAsked = true; } } catch { }
        }

        private static string ApplyWeather()
        {
            if (!_weather.HasWish) return null;
            VectorTimeOfDay v = VectorTimeOfDay.singleton;
            if (v == null) return "this map has no weather or time of day to set";
            var done = new List<string>();
            if (_weather.Hour.HasValue)
            {
                if (v.isStaticLighting) done.Add("the time of day is fixed on this map");
                else { float before = v.timeOfDay; v.SetTimeOfDay(_weather.Hour.Value); done.Add("time " + before.ToString("0.##", Inv) + " -> " + v.timeOfDay.ToString("0.##", Inv) + " (hour " + v.hour + ")"); }
            }
            if (_weather.Rain.HasValue)
            {
                if (v.isRaining != _weather.Rain.Value) v.ToggleRain();
                v.RainChance = _weather.Rain.Value ? 100f : 0f;             // so the game's own weather turn keeps it
                done.Add("rain " + (v.isRaining ? "on" : "off"));
            }
            if (_weather.Sandstorm.HasValue)
            {
                if (v.isSandStorm != _weather.Sandstorm.Value) v.ToggleSandstorm();
                v.sandStormChance = _weather.Sandstorm.Value ? 100f : 0f;
                done.Add("sandstorm " + (v.isSandStorm ? "on" : "off"));
            }
            return string.Join(", ", done);
        }

        private static string SettingsStateJson(RaidManager raid, System.Text.StringBuilder rev)
        {
            var sb = new System.Text.StringBuilder(256);
            bool open = ExtractionOpen(raid);
            sb.Append(",\"extraction\":{\"after\":").Append(_holdAfter).Append(",\"held\":").Append(HoldExtraction ? "true" : "false").Append(",\"asked\":").Append(ExtractionAsked ? "true" : "false")
              .Append(",\"open\":").Append(open ? "true" : "false").Append('}');
            sb.Append(",\"power\":").Append(Q(_power)).Append(",\"satOut\":").Append(Director.SatOut.Count);
            rev.Append('P').Append(_power);
            sb.Append(",\"insertion\":").Append(Q(_insertion)).Append(",\"insertions\":[");
            List<string> names = Insertions();
            for (int i = 0; i < names.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(names[i]));
            sb.Append("],\"weather\":{");
            VectorTimeOfDay v = null;
            try { v = VectorTimeOfDay.singleton; } catch { }
            sb.Append("\"has\":").Append(v != null ? "true" : "false");
            if (v != null)
                try { sb.Append(",\"fixedTime\":").Append(v.isStaticLighting ? "true" : "false").Append(",\"nowHour\":").Append(F(v.timeOfDay)).Append(",\"nowRain\":").Append(v.isRaining ? "true" : "false").Append(",\"nowSandstorm\":").Append(v.isSandStorm ? "true" : "false"); } catch { }
            if (_weather.Hour.HasValue) sb.Append(",\"hour\":").Append(F(_weather.Hour.Value));
            if (_weather.Rain.HasValue) sb.Append(",\"rain\":").Append(_weather.Rain.Value ? "true" : "false");
            if (_weather.Sandstorm.HasValue) sb.Append(",\"sandstorm\":").Append(_weather.Sandstorm.Value ? "true" : "false");
            sb.Append('}');
            rev.Append('X').Append(_holdAfter).Append(HoldExtraction ? 'h' : '-').Append(open ? 'o' : '-').Append(_insertion).Append(names.Count)
               .Append(_weather.Hour.HasValue ? F(_weather.Hour.Value) : "").Append(_weather.Rain).Append(_weather.Sandstorm).Append(v != null ? 'w' : '-');
            return sb.ToString();
        }

        private static int _vehicleLooks;

        /// <summary>
        /// Did the gun trucks come? If the map never starts its vehicle spawner, it is started here, once. The game's
        /// spawner does not always bring one (a run with one planned truck ended with none), so whatever is still
        /// missing after that is placed directly from the same prefab.
        /// </summary>
        private static void CheckVehicles(RaidManager raid, float now)
        {
            if (Director.VehiclesWanted <= 0 || _vehicleCheckAt < 0f || now < _vehicleCheckAt) return;
            var at = new List<Vector3>(); var where = new List<string>();
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<VehicleAI>(), true);
                for (int i = 0; found != null && i < found.Length; i++)
                {
                    VehicleAI v = found[i] == null ? null : found[i].TryCast<VehicleAI>();
                    if (v == null) continue;
                    if (!Vehicles.Contains(v)) Vehicles.Add(v);
                    at.Add(v.transform.position);
                    where.Add(v.gameObject.name.Replace("(Clone)", "").Trim() + " at " + P(v.transform.position) + (v.gameObject.activeInHierarchy ? "" : " (switched off)"));
                }
            }
            catch { }
            int have = at.Count;
            if (have < Director.VehiclesWanted && !Director.VehiclesAsked)
            {
                Director.VehiclesAsked = true; _vehicleCheckAt = now + 3f; _vehicleLooks = 0;
                try { raid.SpawnStaticVehicleAI(); DirectorPlugin.L.LogInfo("gun trucks: none came by themselves, so the game's vehicle spawner was started"); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("gun trucks: the game's vehicle spawner failed: " + e.Message); }
                return;
            }
            _vehicleLooks++;
            DirectorPlugin.L.LogInfo("gun trucks: " + have + " in the map, " + Director.VehiclesWanted + " planned" +
                                     (Director.VehiclesAsked ? " (the vehicle spawner was started by MissionLab)" : " (spawned by the game itself)") +
                                     (where.Count > 0 ? ": " + string.Join("; ", where) : ""));
            if (have >= Director.VehiclesWanted || _vehicleLooks >= 3)
            {
                foreach (Vector3 p in at) Replay.Event("appears", "a gun truck is in the map", p);
                if (have < Director.VehiclesWanted) DirectorPlugin.L.LogWarning("gun trucks: " + (Director.VehiclesWanted - have) + " planned truck(s) never came");
                _vehicleCheckAt = -1f;
                return;
            }
            if (_vehicleLooks == 1)
            {
                int placed = 0;
                try
                {
                    var spots = raid.staticVehicleSpawnPoints; var kinds = raid.staticVehicleAI;
                    for (int i = 0; spots != null && kinds != null && kinds.Length > 0 && i < spots.Length && have + placed < Director.VehiclesWanted; i++)
                    {
                        if (spots[i] == null) continue;
                        Vector3 spot = spots[i].transform.position;
                        if (at.Exists(p => (p - spot).sqrMagnitude < 16f)) continue;
                        GameObject kind = kinds[i % kinds.Length];
                        if (kind == null) continue;
                        GameObject go = UnityEngine.Object.Instantiate((UnityEngine.Object)kind, spot, spots[i].transform.rotation).Cast<GameObject>();
                        Mirror.NetworkServer.Spawn(go, (Mirror.NetworkConnection)null);
                        placed++;
                    }
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("gun trucks: placing one directly failed: " + e.Message); }
                DirectorPlugin.L.LogInfo("gun trucks: the game's spawner brought " + have + ", so " + placed + " were placed directly by MissionLab");
            }
            _vehicleCheckAt = now + 4f;
        }

        // ------------------------------------------------------------------ triggers
        private static string TriggerName(TriggerPlan t) =>
            (t.When == TriggerPlan.OnZone ? "zone " : "trigger ") + (string.IsNullOrEmpty(t.Name) ? t.Id.ToString() : "'" + t.Name + "'");

        private static readonly Dictionary<IntPtr, BrainAI.States> BornAs = new Dictionary<IntPtr, BrainAI.States>();
        private static readonly HashSet<IntPtr> Changed = new HashSet<IntPtr>();
        private static readonly List<VehicleAI> Vehicles = new List<VehicleAI>();

        /// <summary>
        /// Is this enemy's state worth reading yet? Every enemy is born reporting "Evade" (seen in two recordings: half
        /// a second on a soldier, the whole mission on a gun truck's gunner, who has no legs to stand on and never
        /// leaves it). So a state only counts once it has differed from the one the enemy was first seen in.
        /// </summary>
        internal static bool StateCounts(BrainAI b, out BrainAI.States state)
        {
            state = b.CurrentState;
            IntPtr key = b.Pointer;
            if (Changed.Contains(key)) return true;
            if (!BornAs.TryGetValue(key, out BrainAI.States first)) { BornAs[key] = state; return false; }
            if (state == first) return false;
            Changed.Add(key);
            return true;
        }

        /// <summary>Has any enemy noticed the players? Once true it stays true. A gun truck counts once it has a target.</summary>
        private static bool Alerted(GameManager gm, float now)
        {
            if (_alerted) return true;
            var ai = gm.allAI;
            bool yes = false;
            for (int i = 0; ai != null && i < ai.Count; i++)
            {
                BrainAI b = ai[i];
                try { if (b != null && StateCounts(b, out BrainAI.States state) && state != BrainAI.States.Idle) yes = true; } catch { }
            }
            foreach (VehicleAI v in Vehicles)
                try { if (v != null && v.CurrentTarget != null) yes = true; } catch { }
            return _alerted = yes;
        }

        /// <summary>
        /// Which intel has been picked up. An item counts as taken when the game marks it so, or when it was in play
        /// and is gone. Only looked at once the game has settled on its items, a few seconds into the mission.
        /// </summary>
        private static bool IntelTaken(RaidManager raid, string item)
        {
            var live = new HashSet<IntPtr>();
            foreach (ObjectiveObject o in Objectives(raid))
            {
                live.Add(o.Pointer);
                if (!Items.ContainsKey(o.Pointer)) Items[o.Pointer] = new[] { ObjectivePlan.Kind(o.DisplayName), ObjectivePlan.Kind(o.gameObject.name) };
                try { if (o.isObtained) Taken.Add(o.Pointer); } catch { }
            }
            foreach (IntPtr seen in Items.Keys) if (!live.Contains(seen)) Taken.Add(seen);

            string kind = ObjectivePlan.Kind(item);
            if (kind == "" || kind == "any")
            {
                if (Taken.Count > 0) return true;
                try { return raid.collectedItemsCount > 0; } catch { return false; }
            }
            bool some = false;
            foreach (var kv in Items)
            {
                if (kind != "all" && Array.IndexOf(kv.Value, kind) < 0) continue;
                some = true;
                if (!Taken.Contains(kv.Key)) return false;
            }
            return some;
        }

        /// <summary>Where an enemy really is. Its own object stays at the spawn spot; the navigation agent has the truth.</summary>
        internal static void EnemyPose(BrainAI b, out Vector3 pos, out float yaw)
        {
            pos = b.transform.position; yaw = b.transform.eulerAngles.y;
            try { AgentController ac = b.agent; if (ac != null && ac.entityExists) { pos = ac.position; if (ac.Agent != null) yaw = ac.Agent.rotation.eulerAngles.y; } } catch { }
        }

        private static bool NearestPlayer(Vector3 from, out Vector3 where)
        {
            where = Vector3.zero;
            float best = float.MaxValue;
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerMaster pm = players[i];
                PlayerNetworking body = pm == null ? null : pm.PlayerSpawnedObject;
                if (body == null) continue;
                Vector3 p = body.transform.position;
                float sq = (p - from).sqrMagnitude;
                if (sq < best) { best = sq; where = p; }
            }
            return best < float.MaxValue;
        }

        /// <summary>Looks at every trigger that has not fired: has its moment come, and has its wait run out?</summary>
        private static void CheckTriggers(RaidManager raid, GameManager gm, float now)
        {
            for (int pass = 0; pass < 4; pass++)            // a trigger that waits for another may follow in the same look
            {
                bool any = false;
                foreach (TriggerPlan t in _zones)
                {
                    if (Fired.Contains(t.Id)) continue;
                    if (!MetAt.TryGetValue(t.Id, out float met))
                    {
                        Vector3 who = Vector3.zero;
                        bool yes;
                        switch (t.When)
                        {
                            case TriggerPlan.OnAlert: yes = Alerted(gm, now); break;
                            case TriggerPlan.OnIntel: yes = IntelTaken(raid, t.Item); break;
                            case TriggerPlan.OnTime: yes = true; break;
                            case TriggerPlan.OnAfter: yes = t.After != 0 && t.After != t.Id && Fired.Contains(t.After); break;
                            default: yes = PlayerInside(t, out who); break;
                        }
                        if (!yes) continue;
                        MetAt[t.Id] = met = t.When == TriggerPlan.OnTime ? Director.HandledAt : now;     // a timer counts from the start of the mission
                        if (t.When == TriggerPlan.OnZone) MetWhere[t.Id] = who;
                    }
                    if (now < met + t.Delay) continue;
                    Fire(t, now);
                    any = true;
                }
                if (!any) break;
            }
        }

        /// <summary>The trigger's enemies appear and its doors lock or unlock.</summary>
        private static void Fire(TriggerPlan t, float now)
        {
            Fired.Add(t.Id);
            if (!MetAt.ContainsKey(t.Id)) MetAt[t.Id] = now;
            bool stood = MetWhere.TryGetValue(t.Id, out Vector3 who);
            var mine = new List<Marker>();
            if (_markers != null)
                foreach (Marker m in _markers)
                {
                    if (m.Plan.Trigger != t.Id || !m.Waiting || m.Queued || Special(m.Plan)) continue;
                    var at = new Vector3(m.Plan.Pos.X, m.Plan.Pos.Y, m.Plan.Pos.Z);
                    Vector3 look = who;
                    if (t.FacePlayer && (stood || NearestPlayer(at, out look)))
                        m.FaceYaw = (Mathf.Atan2(look.x - at.x, look.z - at.z) * Mathf.Rad2Deg + 360f) % 360f;
                    mine.Add(m);
                }
            Enqueue(mine);
            int doors = 0;
            foreach (DoorPlan p in t.Doors)
            {
                if (!p.Locked.HasValue) continue;
                DoorV2 d = DoorAt(p.Pos);
                if (d == null) continue;
                try { if (d.CurrentlyLocked != p.Locked.Value) d.Server_SetDoorLocked(p.Locked.Value); doors++; }
                catch (Exception e) { DirectorPlugin.L.LogWarning("a door could not be set by " + TriggerName(t) + ": " + e.Message); }
            }
            RaidManager raidNow = RaidManager.singleton;
            if (raidNow != null)
            {
                bool wasHeld = HoldExtraction && t.Id == _holdAfter;
                if (wasHeld) HoldExtraction = false;
                if (t.OpensExtraction) OpenExtraction(raidNow, TriggerName(t) + " opens it");
                else if (wasHeld && ExtractionAsked) OpenExtraction(raidNow, "it was waiting for " + TriggerName(t));
            }
            string more = "";
            if (t.Power == "off" || t.Power == "on")
                try { TryOuts.Power(t.Power == "on"); more += ", the lights go " + t.Power; }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the lights could not be set by " + TriggerName(t) + ": " + e.Message); }
            if (!string.IsNullOrEmpty(t.Message) && raidNow != null)
                try { TryOuts.Message(raidNow, t.Message); more += ", a message is shown"; Replay.Event("note", "message: " + t.Message); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the message of " + TriggerName(t) + " could not be shown: " + e.Message); }
            string why = t.When == TriggerPlan.OnZone ? "reached" : t.When == TriggerPlan.OnAlert ? "(the enemy is alerted)" : t.When == TriggerPlan.OnIntel ? "(intel taken)"
                       : t.When == TriggerPlan.OnTime ? "(time)" : "(after trigger " + t.After + ")";
            Tell(TriggerName(t) + " " + why + ": " + mine.Count + (mine.Count == 1 ? " enemy appears" : " enemies appear") + (doors > 0 ? ", " + doors + (doors == 1 ? " door changes" : " doors change") : "") + more);
            if (t.End == "success" || t.End == "failed")
                try { Say(TriggerName(t) + " ends the mission: " + TryOuts.End(t.End == "success")); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the mission could not be ended by " + TriggerName(t) + ": " + e.Message); }
        }

        /// <summary>The Game Master sets a trigger off by hand.</summary>
        private static void FireNow(int id)
        {
            TriggerPlan t = _zones.Find(z => z.Id == id);
            if (t == null || Fired.Contains(id)) { Say(t == null ? "there is no trigger " + id : TriggerName(t) + " has already fired"); return; }
            Fire(t, Time.unscaledTime);
        }

        // ------------------------------------------------------------------ doors
        /// <summary>The map's doors that still exist.</summary>
        private static List<DoorV2> Doors()
        {
            float now = Time.unscaledTime;
            if (_doorList != null && (_doorList.Count > 0 || now < _doorScanAt))
            {
                _doorList.RemoveAll(d => d == null);
                return _doorList;
            }
            var list = new List<DoorV2>();
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<DoorV2>(), false);
                for (int i = 0; found != null && i < found.Length; i++)
                {
                    DoorV2 d = found[i] == null ? null : found[i].TryCast<DoorV2>();
                    if (d != null) list.Add(d);
                }
            }
            catch (Exception e) { if (_doorError == null) DirectorPlugin.L.LogWarning("doors could not be listed: " + e.Message); _doorError = e.Message; }
            _doorList = list; _doorScanAt = now + 2f;         // a map without doors is not searched again every moment
            return list;
        }

        /// <summary>Where the door turns. This stays put while the door swings, so it tells doors apart.</summary>
        internal static Vector3 Hinge(DoorV2 d)
        {
            Transform p = null;
            try { p = d.PivotTransform; } catch { }
            return p != null ? p.position : d.transform.position;
        }

        /// <summary>The middle of the door leaf, for drawing.</summary>
        private static Vector3 Middle(DoorV2 d)
        {
            try { BoxCollider b = d.DoorHitBox; if (b != null) return b.bounds.center; } catch { }
            return Hinge(d);
        }

        private static DoorV2 DoorAt(V3 pos)
        {
            var want = new Vector3(pos.X, pos.Y, pos.Z);
            DoorV2 pick = null; float best = 0.75f * 0.75f;
            foreach (DoorV2 d in Doors())
            {
                float sq = (Hinge(d) - want).sqrMagnitude;
                if (sq < best) { best = sq; pick = d; }
            }
            return pick;
        }

        /// <summary>Locks, unlocks and closes doors to enemies as asked. Lock state is the game's own and reaches every player.</summary>
        private static string DoorKey(DoorPlan p) => Math.Round(p.Pos.X, 1) + "/" + Math.Round(p.Pos.Y, 1) + "/" + Math.Round(p.Pos.Z, 1);

        /// <param name="before">
        /// Given when the browser sends its list again: only doors whose setting differs from this earlier list are
        /// touched, so a door that a trigger locked, or that players got open, is not put back by an unrelated change.
        /// </param>
        private static string ApplyDoors(bool atStart, List<DoorPlan> before = null)
        {
            int found = 0, changed = 0, failed = 0;
            var old = new Dictionary<string, DoorPlan>();
            if (before != null) foreach (DoorPlan b in before) old[DoorKey(b)] = b;
            foreach (DoorPlan p in _doorPlans)
            {
                if (before != null && old.TryGetValue(DoorKey(p), out DoorPlan was) && was.Locked == p.Locked && was.EnemiesCanOpen == p.EnemiesCanOpen) continue;
                DoorV2 d = DoorAt(p.Pos);
                if (d == null) continue;
                found++;
                try
                {
                    if (p.Locked.HasValue)
                    {
                        bool v = p.Locked.Value;
                        // 100 is "always" whether the game counts the chance to 1 or to 100.
                        if (atStart) { d.StartLocked = v; d.StartLockedChance = v ? 100f : 0f; }
                        if (d.CurrentlyLocked != v) { d.Server_SetDoorLocked(v); changed++; }
                    }
                    if (p.EnemiesCanOpen.HasValue)
                    {
                        bool cant = !p.EnemiesCanOpen.Value;
                        if (atStart) d.AiCantOpenChance = cant ? 100f : 0f;
                        if (d.AiCantOpen != cant) { d.AiCantOpen = cant; changed++; }
                    }
                }
                catch (Exception e) { failed++; _doorError = e.Message; }
            }
            int all = 0, locked = 0, shut = 0;
            foreach (DoorV2 d in Doors())
            {
                all++;
                try { if (d.CurrentlyLocked) locked++; if (d.AiCantOpen) shut++; } catch { }
            }
            return _doorPlans.Count + " asked for, " + found + " found, " + changed + " setting(s) changed" + (failed > 0 ? ", " + failed + " failed (" + _doorError + ")" : "") +
                   "; the map has " + all + " doors, " + locked + " locked, " + shut + " that enemies cannot open";
        }

        private static string DoorsStateJson(System.Text.StringBuilder rev)
        {
            var wish = new Dictionary<IntPtr, DoorPlan>();
            foreach (DoorPlan p in _doorPlans) { DoorV2 d = DoorAt(p.Pos); if (d != null) wish[d.Pointer] = p; }
            var sb = new System.Text.StringBuilder(256);
            sb.Append('[');
            bool first = true;
            foreach (DoorV2 d in Doors())
            {
                try
                {
                    Vector3 hinge = Hinge(d);
                    bool locked = d.CurrentlyLocked, cant = d.AiCantOpen, dead = d.isDead;
                    wish.TryGetValue(d.Pointer, out DoorPlan w);
                    string wl = w != null && w.Locked.HasValue ? (w.Locked.Value ? "true" : "false") : "null";
                    string we = w != null && w.EnemiesCanOpen.HasValue ? (w.EnemiesCanOpen.Value ? "true" : "false") : "null";
                    sb.Append(first ? "" : ",").Append("{\"name\":").Append(Q(d.gameObject.name)).Append(",\"pos\":").Append(P(hinge)).Append(",\"mid\":").Append(P(Middle(d)))
                      .Append(",\"isLocked\":").Append(locked ? "true" : "false").Append(",\"enemiesKeptOut\":").Append(cant ? "true" : "false").Append(",\"broken\":").Append(dead ? "true" : "false")
                      .Append(",\"locked\":").Append(wl).Append(",\"enemiesCanOpen\":").Append(we).Append('}');
                    rev.Append('D').Append(P(hinge)).Append(locked ? 'l' : 'u').Append(cant ? 'c' : 'o').Append(dead ? 'x' : '-').Append(wl[0]).Append(we[0]);
                    first = false;
                }
                catch { }
            }
            return sb.Append(']').ToString();
        }

        /// <summary>Remembers where the game itself put an item, the first time the item is seen.</summary>
        private static Pose Home(ObjectiveObject o)
        {
            if (!Homes.TryGetValue(o.Pointer, out Pose h))
            {
                Transform t = o.transform;
                Homes[o.Pointer] = h = new Pose { Pos = t.position, Yaw = t.eulerAngles.y };
            }
            return h;
        }

        private static bool Moved(ObjectiveObject o)
        {
            Pose h = Home(o); Transform t = o.transform;
            return (t.position - h.Pos).sqrMagnitude > 0.0004f || Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, h.Yaw)) > 0.5f;
        }

        private static string Describe(RaidManager raid)
        {
            var sb = new System.Text.StringBuilder();
            foreach (ObjectiveObject o in Objectives(raid))
            {
                Vector3 p = o.transform.position;
                sb.Append(sb.Length > 0 ? ", " : "").Append(o.gameObject.name).Append(" at ").Append(p.x.ToString("0.0", Inv)).Append('/').Append(p.y.ToString("0.0", Inv)).Append('/').Append(p.z.ToString("0.0", Inv));
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        /// <summary>
        /// Moves objective items to where the scenario or the Game Master wants them.
        /// A map holds several copies of each item ("Cell Phone_1", "Cell Phone_2") and keeps one per mission, so an entry
        /// applies to whichever copy is in play: the same name first, else the same kind of item.
        /// </summary>
        private static int PlaceObjectives(RaidManager raid, bool tellClients = true)
        {
            int moved = 0;
            var used = new HashSet<ObjectivePlan>();
            var sets = raid.objectives;
            for (int i = 0; sets != null && i < sets.Length; i++)
            {
                var items = sets[i] == null ? null : sets[i].objectives;
                var live = new List<ObjectiveObject>();
                for (int k = 0; items != null && k < items.Length; k++)
                    if (items[k] != null && items[k].gameObject.activeInHierarchy) { live.Add(items[k]); Home(items[k]); }
                if (live.Count == 0 || _wanted.Count == 0) continue;

                // The copies in one set stand in for each other, so one entry is found for the set and given to every copy still there.
                ObjectivePlan want = null;
                foreach (ObjectiveObject o in live)
                {
                    string name = o.gameObject.name;
                    want = _wanted.Find(w => !used.Contains(w) && w.Name == name);
                    if (want != null) break;
                }
                if (want == null)
                {
                    var kinds = new HashSet<string> { ObjectivePlan.Kind(sets[i].name) };
                    foreach (ObjectiveObject o in live) { kinds.Add(ObjectivePlan.Kind(o.gameObject.name)); kinds.Add(ObjectivePlan.Kind(o.DisplayName)); }
                    kinds.Remove("");
                    want = _wanted.Find(w => !used.Contains(w) && kinds.Contains(ObjectivePlan.Kind(w.Name)));
                }
                if (want == null) continue;
                used.Add(want);
                var pos = new Vector3(want.Pos.X, want.Pos.Y, want.Pos.Z);
                foreach (ObjectiveObject o in live)
                {
                    Transform t = o.transform;
                    if ((t.position - pos).sqrMagnitude < 0.0004f && Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, want.Yaw)) < 0.5f) continue;
                    t.SetPositionAndRotation(pos, Quaternion.Euler(0f, want.Yaw, 0f));
                    moved++;
                    if (tellClients) TellClients(o);
                }
            }
            return moved;
        }

        /// <summary>
        /// A friend's game only learns where an item lies when the item is first shown to it. After a later move the
        /// host sends that same "here it is" message again, which their game applies to the item it already has.
        /// </summary>
        private static void TellClients(ObjectiveObject o)
        {
            if (_resendFailed) return;
            try
            {
                Mirror.NetworkIdentity id = o.GetComponent<Mirror.NetworkIdentity>();
                var all = Mirror.NetworkServer.connectionsCopy;
                if (id == null || all == null || id.observers == null) return;
                int sent = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    Mirror.NetworkConnectionToClient c = all[i];
                    if (c == null || c.connectionId == 0 || !id.observers.ContainsKey(c.connectionId)) continue;    // 0 is the host's own game
                    Mirror.NetworkServer.SendSpawnMessage(id, c);
                    sent++;
                }
                if (sent > 0) DirectorPlugin.L.LogInfo("objectives: told " + sent + " other player(s) where " + o.gameObject.name + " lies now");
            }
            catch (Exception e)
            {
                _resendFailed = true;
                DirectorPlugin.L.LogWarning("objectives: other players could not be told about a moved item (" + e.Message + "); they see it in its old place");
            }
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
                if (m.Bot != IntPtr.Zero || (m.Waiting && !m.Queued) || m.Plan.Vehicle) continue;
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
                foreach (Marker m in _markers) if (_later.Contains(m.Plan) && m.Plan.Trigger == 0 && !Special(m.Plan)) mine.Add(m);
                _later = null;
                Enqueue(mine);
            }
            if (!_objectivesPlaced)
            {
                // By now the game has chosen its objective items; put them where the scenario says.
                _objectivesPlaced = true;
                MissionPlan plan = Director.CurrentPlan;
                // And every door has made up its mind. Said once per mission, asked for or not: it shows what the map has.
                try { DirectorPlugin.L.LogInfo("doors, a few seconds in: " + ApplyDoors(false)); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("doors could not be checked: " + e.Message); }
                try { string w = ApplyWeather(); if (w != null) DirectorPlugin.L.LogInfo("weather: " + w); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("weather could not be set: " + e.Message); }
                try
                {
                    var where = new List<string>();
                    var ps = PlayerMaster.AllPlayerMasters;
                    for (int i = 0; ps != null && i < ps.Count; i++)
                    {
                        PlayerNetworking body = ps[i] == null ? null : ps[i].PlayerSpawnedObject;
                        if (body != null) where.Add(ps[i].thisPlayerName + " at " + InsertionAt(body.transform.position) + " (chose number " + ps[i].CurrentInfilIndex + ")");
                    }
                    DirectorPlugin.L.LogInfo("insertions of this map: " + string.Join(", ", Insertions()) + "; players: " + (where.Count > 0 ? string.Join(", ", where) : "none placed yet") +
                                             "; extraction " + (ExtractionOpen(raid) ? "open" : "shut") + " at the start");
                }
                catch { }
                _vehicleCheckAt = now + 2f;
                if (_power == "off" || _power == "on")
                    try { DirectorPlugin.L.LogInfo("lights at the start: " + TryOuts.Power(_power == "on")); }
                    catch (Exception e) { DirectorPlugin.L.LogWarning("the lights could not be set at the start: " + e.Message); }
                if (_insertion != "" && ForcedInfil < 0)
                {
                    // The insertion list was not there yet when the mission was set up.
                    try { DirectorPlugin.L.LogInfo("insertion, second try: " + ForceInsertion(_insertion)); } catch { }
                }
                if (plan != null && plan.Objectives.Count > 0)
                {
                    EnsureMarkers();
                    // A second look once the game has settled on its items; normally nothing is left to do.
                    int late = PlaceObjectives(raid);
                    int stay = 0;
                    foreach (ObjectiveObject o in Objectives(raid)) if (Moved(o)) stay++;
                    DirectorPlugin.L.LogInfo("objectives: " + stay + " of " + Objectives(raid).Count + " item(s) lie where the scenario says (" + plan.Objectives.Count + " named, " + late + " moved late); in the map: " + Describe(raid));
                }
            }
            try { CheckVehicles(raid, now); } catch (Exception e) { DirectorPlugin.L.LogWarning("gun trucks could not be checked: " + e.Message); _vehicleCheckAt = -1f; }
            if (HoldExtraction) KeepExtractionShut(raid);
            // A scenario whose triggers only work doors holds no enemies back, so nothing above has set the markers up.
            if (_markers == null && _later == null && Director.CurrentPlan != null && Director.CurrentPlan.Triggers.Count > 0) EnsureMarkers();
            if (_markers != null && now >= _zoneCheckAt)
            {
                _zoneCheckAt = now + 0.25f;
                if (_later != null) EnsureMarkers();
                try { CheckTriggers(raid, gm, now); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("triggers could not be checked: " + e.Message); _zoneCheckAt = now + 5f; }
            }
            if (_markers != null)
            {
                // An enemy that was turned by hand is taken out and made again facing the new way.
                foreach (Marker m in _markers)
                {
                    if (m.TurnedAt < 0f || now - m.TurnedAt < 0.6f || m.Queued) continue;
                    m.TurnedAt = -1f;
                    if (Special(m.Plan)) continue;                            // the target person is never re-made: it would come back as an ordinary enemy
                    BrainAI b = m.Bot == IntPtr.Zero ? null : Find(gm, m.Bot);
                    if (b == null) continue;                              // not alive: the new facing applies when it spawns
                    if (!Destroy(b)) continue;
                    // The game will not spawn onto a spot that is still occupied, and the old enemy is only gone a moment later.
                    m.Bot = IntPtr.Zero; m.Waiting = true;
                    bool held = m.Plan.Trigger != 0 && !Fired.Contains(m.Plan.Trigger);      // now bound to a zone nobody has reached: it waits there
                    m.RemakeAt = held ? -1f : now + 0.7f;
                }
                foreach (Marker m in _markers)
                {
                    if (m.RemakeAt < 0f || now < m.RemakeAt || m.Queued) continue;
                    m.RemakeAt = -1f;
                    if (m.Waiting && m.TurnedAt < 0f) Enqueue(new List<Marker> { m });
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
                foreach (Marker m in batch.Items) { points.Add(Director.BuildPoint(template, raid.transform, m.Plan, "ML gm " + (++n), m.FaceYaw)); m.FaceYaw = float.NaN; }
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
            Tell((made == batch.Items.Count ? "spawned " + made : "spawned " + made + " of " + batch.Items.Count) + (made == 1 ? " enemy" : " enemies") +
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
            if (Down(DirectorPlugin.KeyExport)) { _exportAt = now + 0.15f; Say("exporting the map for the 3D editor: the game may freeze for a few seconds..."); }
            if (_exportAt > 0f && now >= _exportAt) { _exportAt = -1f; ExportMap(); }

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
            for (int i = 0; i < _markers.Count; i++)
                if (i != _aiming && _markers[i].Waiting && !_markers[i].Queued && !Special(_markers[i].Plan) && (_markers[i].Plan.Trigger == 0 || Fired.Contains(_markers[i].Plan.Trigger))) list.Add(_markers[i]);
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

        private static void ExportMap()
        {
            NavMesh nav = Director.CurrentNav;
            string name = nav != null && !string.IsNullOrEmpty(nav.SourceFile) ? Path.GetFileNameWithoutExtension(nav.SourceFile) : "map";
            string path = Path.Combine(DirectorPlugin.BaseDir, "maps", name + ".mlgeo");
            try
            {
                string result = MapExport.Run(path);
                DirectorPlugin.L.LogInfo("map export -> " + path + " | " + result);
                Say("map saved to MissionLab/maps/" + name + ".mlgeo. Open it in the scenario editor for the 3D view.");
            }
            catch (Exception e)
            {
                DirectorPlugin.L.LogError("map export failed: " + e);
                Say("the map could not be exported (see log)");
            }
        }

        private static bool PlayerInside(TriggerPlan z, out Vector3 where)
        {
            where = Vector3.zero;
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerMaster pm = players[i];
                PlayerNetworking body = pm == null ? null : pm.PlayerSpawnedObject;
                if (body == null) continue;
                Vector3 p = body.transform.position;
                float dx = p.x - z.Pos.X, dz = p.z - z.Pos.Z;
                if (dx * dx + dz * dz <= z.Radius * z.Radius && Mathf.Abs(p.y - z.Pos.Y) <= 2f) { where = p; return true; }      // 2 m: the floor above or below does not count
            }
            return false;
        }

        /// <summary>
        /// Which way a player looks. The player object itself does not turn; the game keeps the
        /// view direction in CharacterRotationY (sent to everyone), and the host's own camera is exact.
        /// </summary>
        internal static float PlayerYaw(PlayerMaster pm, PlayerNetworking body)
        {
            float synced = body.CharacterRotationY;
            Transform character = body.Character;
            float model = character != null ? character.eulerAngles.y : 0f;
            float yaw = Mathf.Abs(synced) > 0.01f ? synced : model;
            ScreenshotFreeCamera fc = FreeCam();
            Camera cam = MainCameraSingleton.MainCam;
            bool own = pm.Pointer == (PlayerMaster.MyPlayerMaster != null ? PlayerMaster.MyPlayerMaster.Pointer : IntPtr.Zero);
            if (own && cam != null && !(fc != null && fc.ToggledCam)) yaw = cam.transform.eulerAngles.y;
            if (!_yawLogged)
            {
                _yawLogged = true;
                DirectorPlugin.L.LogInfo("player facing sources: synced " + synced.ToString("0", Inv) + ", character " + model.ToString("0", Inv) + ", object " + body.transform.eulerAngles.y.ToString("0", Inv) +
                                         ", camera " + (cam != null ? cam.transform.eulerAngles.y.ToString("0", Inv) : "none") + ", own player " + own);
            }
            return (yaw % 360f + 360f) % 360f;
        }

        // ------------------------------------------------------------------ browser editor (called by LiveLink on the game thread)
        private static bool MissionRunning(out RaidManager raid, out GameManager gm)
        {
            raid = RaidManager.singleton; gm = GameManager.instance;
            return _raidPtr != IntPtr.Zero && raid != null && gm != null && raid.Pointer == _raidPtr && Mirror.NetworkServer.active;
        }

        internal static string LiveNavFile()
        {
            if (_raidPtr == IntPtr.Zero) return null;
            NavMesh nav = Director.CurrentNav;
            return nav != null ? nav.SourceFile : null;
        }

        private static string Q(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";
        private static string F(float f) => (float.IsNaN(f) || float.IsInfinity(f) ? 0f : (float)Math.Round(f, 2)).ToString(Inv);
        private static string P(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";

        /// <summary>Everything the browser shows: players, enemies where they really are, and the markers.</summary>
        internal static string StateJson()
        {
            string page = ",\"page\":" + Q(LiveLink.PageStamp) + "," + Picker.Json();
            if (!MissionRunning(out RaidManager raid, out GameManager gm)) return "{\"mission\":false" + page + "}";
            if (Time.unscaledTime - Director.HandledAt < 3f) return "{\"mission\":false,\"starting\":true" + page + "}";
            EnsureMarkers(); MapBots(gm, null);
            HashSet<IntPtr> alive = Alive(gm);
            var byBot = new Dictionary<IntPtr, int>();
            foreach (Marker m in _markers) if (m.Bot != IntPtr.Zero) byBot[m.Bot] = m.Id;
            string nav = LiveNavFile();
            var sb = new System.Text.StringBuilder(8192);
            var rev = new System.Text.StringBuilder(2048);
            sb.Append("{\"mission\":true").Append(page).Append(",\"scene\":").Append(Q(raid.gameObject.scene.name)).Append(",\"navFile\":").Append(Q(nav));
            sb.Append(",\"hasGeo\":").Append(nav != null && File.Exists(Path.Combine(DirectorPlugin.BaseDir, "maps", Path.GetFileNameWithoutExtension(nav) + ".mlgeo")) ? "true" : "false");
            var hvtPoints = raid.hvtSpawnPoints;
            sb.Append(",\"targetMission\":").Append(hvtPoints != null && hvtPoints.Length > 0 && gm.botHVTAmount > 0 ? "true" : "false");
            sb.Append(",\"busy\":").Append(_running != null || Pending.Count > 0 ? "true" : "false");
            sb.Append(",\"message\":").Append(Q(Time.unscaledTime < Director.BannerUntil ? Director.Banner : ""));

            sb.Append(",\"players\":[");
            var players = PlayerMaster.AllPlayerMasters;
            bool first = true;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerMaster pm = players[i];
                PlayerNetworking body = pm == null ? null : pm.PlayerSpawnedObject;
                if (body == null) continue;
                Transform t = body.transform;
                sb.Append(first ? "" : ",").Append("{\"name\":").Append(Q(pm.thisPlayerName)).Append(",\"pos\":").Append(P(t.position)).Append(",\"yaw\":").Append(F(PlayerYaw(pm, body))).Append('}');
                first = false;
            }

            sb.Append("],\"enemies\":[");
            var ai = gm.allAI; first = true;
            for (int i = 0; ai != null && i < ai.Count; i++)
            {
                BrainAI b = ai[i];
                if (b == null) continue;
                Vector3 pos = b.transform.position; float yaw = b.transform.eulerAngles.y;
                try { AgentController ac = b.agent; if (ac != null && ac.entityExists) { pos = ac.position; if (ac.Agent != null) yaw = ac.Agent.rotation.eulerAngles.y; } } catch { }
                sb.Append(first ? "" : ",").Append("{\"pos\":").Append(P(pos)).Append(",\"yaw\":").Append(F(yaw)).Append(",\"state\":").Append(Q(b.CurrentState.ToString()))
                  .Append(",\"hvt\":").Append(b.isHVT ? "true" : "false").Append(",\"marker\":").Append(byBot.TryGetValue(b.Pointer, out int id) ? id : -1).Append('}');
                first = false;
            }

            sb.Append("],\"markers\":[");
            for (int i = 0; i < _markers.Count; i++)
            {
                Marker m = _markers[i];
                bool live = m.Plan.Vehicle ? !m.Waiting : m.Bot != IntPtr.Zero && alive.Contains(m.Bot);     // a gun truck placed at the start is simply there
                string spawn = MissionPlan.SpawnJson(m.Plan);
                sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(m.Id).Append(",\"live\":").Append(live ? "true" : "false").Append(",\"waiting\":").Append(m.Waiting ? "true" : "false").Append(',').Append(spawn, 1, spawn.Length - 1);
                rev.Append(m.Id).Append(live ? 'L' : m.Waiting ? 'W' : 'G').Append(spawn);
            }
            sb.Append("],\"objectives\":[");
            first = true;
            foreach (ObjectiveObject o in Objectives(raid))
            {
                Transform t = o.transform;
                Pose home = Home(o);
                sb.Append(first ? "" : ",").Append("{\"name\":").Append(Q(o.gameObject.name)).Append(",\"label\":").Append(Q(o.DisplayName)).Append(",\"pos\":").Append(P(t.position))
                  .Append(",\"yaw\":").Append(F(t.eulerAngles.y)).Append(",\"home\":").Append(P(home.Pos)).Append(",\"homeYaw\":").Append(F(home.Yaw))
                  .Append(",\"moved\":").Append(Moved(o) ? "true" : "false").Append(",\"obtained\":").Append(o.isObtained ? "true" : "false").Append('}');
                rev.Append(o.gameObject.name).Append(P(t.position)).Append(o.isObtained ? 'x' : 'o');
                first = false;
            }
            sb.Append("],\"doors\":").Append(DoorsStateJson(rev));
            try { sb.Append(SettingsStateJson(raid, rev)); } catch { }
            sb.Append(",\"triggers\":").Append(MissionPlan.TriggersJson(_zones)).Append(",\"fired\":[");
            first = true;
            foreach (int id in Fired) { sb.Append(first ? "" : ",").Append(id); first = false; }
            rev.Append(MissionPlan.TriggersJson(_zones)).Append(Fired.Count);
            sb.Append("],\"types\":[");
            var types = Types(gm);
            for (int i = 1; i < types.Count; i++) sb.Append(i > 1 ? "," : "").Append(Q(types[i]));
            sb.Append("],\"infilPoints\":[");
            try { List<V3> pts = Director.CurrentInfils; for (int i = 0; pts != null && i < pts.Count; i++) sb.Append(i > 0 ? "," : "").Append('[').Append(F(pts[i].X)).Append(',').Append(F(pts[i].Y)).Append(',').Append(F(pts[i].Z)).Append(']'); } catch { }
            sb.Append("],\"things\":").Append(TryOuts.ThingsJson()).Append(",\"tried\":").Append(Q(TryOuts.Last));
            sb.Append(",\"vehicleTypes\":[");
            try { List<GameObject> trucks = Director.VehiclePrefabs(raid); for (int i = 0; i < trucks.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(trucks[i].name)); } catch { }
            // The page takes the marker list over only when this changes.
            sb.Append("],\"rev\":").Append(Q(rev.ToString().GetHashCode().ToString("x") + "-" + _markers.Count)).Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// The page sends its whole enemy list. New entries become markers (spawned at once if asked),
        /// missing ones are removed with their enemy, and changed ones are updated; a live enemy whose
        /// marker changed is taken out and made again.
        /// </summary>
        internal static string Reconcile(string json)
        {
            if (!MissionRunning(out RaidManager raid, out GameManager gm)) return "{\"mission\":false}";
            EnsureMarkers();
            var ids = new List<int>(); bool spawnNow; MissionPlan sent; MissionPlan settings = null;
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
            {
                System.Text.Json.JsonElement root = doc.RootElement, list = root.GetProperty("markers");
                spawnNow = root.TryGetProperty("spawnNow", out var sn) && sn.ValueKind == System.Text.Json.JsonValueKind.True;
                foreach (System.Text.Json.JsonElement e in list.EnumerateArray())
                    if (e.ValueKind == System.Text.Json.JsonValueKind.Object) ids.Add(e.TryGetProperty("id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.Number ? id.GetInt32() : 0);
                if (root.TryGetProperty("settings", out var st) && st.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    // The page sends the mission-wide settings as they stand in a scenario file; read them the same way.
                    string raw = st.GetRawText().Trim(), inner = raw.Substring(1, raw.Length - 2).Trim();
                    settings = MissionPlan.FromJson("{" + (inner.Length > 0 ? inner + "," : "") + "\"spawns\":[]}");
                }
                string zonesText = root.TryGetProperty("triggers", out var zs) && zs.ValueKind == System.Text.Json.JsonValueKind.Array ? zs.GetRawText() : MissionPlan.TriggersJson(_zones);
                string itemsText = root.TryGetProperty("objectives", out var os) && os.ValueKind == System.Text.Json.JsonValueKind.Array ? os.GetRawText() : MissionPlan.ObjectivesJson(_wanted);
                string doorsText = root.TryGetProperty("doors", out var ds) && ds.ValueKind == System.Text.Json.JsonValueKind.Array ? ds.GetRawText() : MissionPlan.DoorsJson(_doorPlans);
                sent = MissionPlan.FromJson("{\"triggers\":" + zonesText + ",\"objectives\":" + itemsText + ",\"doors\":" + doorsText + ",\"spawns\":" + list.GetRawText() + "}");
            }
            if (sent.Spawns.Count != ids.Count) return StateJson();

            float now = Time.unscaledTime;
            var byId = new Dictionary<int, Marker>();
            foreach (Marker m in _markers) byId[m.Id] = m;
            var result = new List<Marker>(); var fresh = new List<Marker>();
            for (int i = 0; i < sent.Spawns.Count; i++)
            {
                SpawnPlan s = sent.Spawns[i];
                if (s.Fixed) { s.Role = SpawnRole.Guard; s.Patrol.Clear(); }
                if (ids[i] > 0 && byId.TryGetValue(ids[i], out Marker m))
                {
                    byId.Remove(ids[i]);
                    if (MissionPlan.SpawnJson(m.Plan) != MissionPlan.SpawnJson(s))
                    {
                        bool released = m.Plan.Trigger != 0 && s.Trigger == 0 && m.Waiting && !m.Queued && m.Bot == IntPtr.Zero;   // unbound from its zone
                        bool target = Special(s) || Special(m.Plan);  // the target person and gun trucks are spawned by the game at the start only
                        m.Plan = s;
                        if (target) { }
                        else if (m.Bot != IntPtr.Zero) m.TurnedAt = now;
                        else if (released && spawnNow) fresh.Add(m);
                    }
                    result.Add(m);
                }
                else
                {
                    if (!s.Fixed && !OnWalkable(new Vector3(s.Pos.X, s.Pos.Y, s.Pos.Z))) { s.Fixed = true; s.Role = SpawnRole.Guard; s.Patrol.Clear(); }
                    var created = new Marker { Plan = s, Waiting = true };
                    result.Add(created);
                    if (spawnNow && s.Trigger == 0 && !Special(s)) fresh.Add(created);       // a zone enemy waits for its zone; a target for the next start
                }
            }
            foreach (Marker gone in byId.Values)
            {
                if (gone.Queued) { result.Add(gone); continue; }          // on its way into the game: keep it for now
                BrainAI b = gone.Bot == IntPtr.Zero ? null : Find(gm, gone.Bot);
                if (b != null) Destroy(b);
            }
            _markers = result; _aiming = -1; _current = -1;
            // A timer made during the mission counts from now; one that was taken away forgets what it saw.
            var had = new HashSet<int>();
            foreach (TriggerPlan z in _zones) had.Add(z.Id);
            var have = new HashSet<int>();
            foreach (TriggerPlan z in sent.Triggers) { have.Add(z.Id); if (!had.Contains(z.Id) && z.When == TriggerPlan.OnTime) MetAt[z.Id] = now; }
            foreach (int id in had) if (!have.Contains(id)) { MetAt.Remove(id); MetWhere.Remove(id); Fired.Remove(id); }
            _zones = sent.Triggers;
            _wanted = sent.Objectives; PlaceObjectives(raid);
            if (settings != null)
            {
                // The insertion is for the next start; weather applies now; the extraction rule applies from now on.
                _insertion = settings.Insertion ?? "";
                bool weatherChanged = MissionPlan.SettingsJson(new MissionPlan { Weather = settings.Weather }) != MissionPlan.SettingsJson(new MissionPlan { Weather = _weather });
                _weather = settings.Weather;
                if ((settings.Power ?? "") != _power)
                {
                    _power = settings.Power ?? "";
                    if (_power != "") try { Tell("lights: " + TryOuts.Power(_power == "on")); } catch (Exception e) { DirectorPlugin.L.LogWarning("the lights could not be set: " + e.Message); }
                }
                if (weatherChanged) { try { string w = ApplyWeather(); if (w != null) Tell("weather: " + w); } catch (Exception e) { DirectorPlugin.L.LogWarning("weather could not be set: " + e.Message); } }
                int holdNow = settings.ExtractionAfter != 0 && sent.Triggers.Exists(t => t.Id == settings.ExtractionAfter) ? settings.ExtractionAfter : 0;
                if (holdNow != _holdAfter)
                {
                    _holdAfter = holdNow;
                    bool hold = holdNow != 0 && !Fired.Contains(holdNow);
                    if (hold) { HoldExtraction = true; DirectorPlugin.PatchExtraction(); }
                    else if (HoldExtraction) { HoldExtraction = false; if (ExtractionAsked) OpenExtraction(raid, "nothing holds it back any more"); }
                }
            }
            List<DoorPlan> doorsBefore = _doorPlans;
            _doorPlans = sent.Doors;
            try { ApplyDoors(false, doorsBefore); } catch (Exception e) { DirectorPlugin.L.LogWarning("doors could not be set: " + e.Message); }
            if (fresh.Count > 0) Enqueue(fresh);
            return StateJson();
        }

        internal static string Command(string json)
        {
            if (!MissionRunning(out RaidManager raid, out GameManager gm)) return "{\"mission\":false}";
            EnsureMarkers();
            string cmd; int id = 0; string what = "", arg = ""; bool hasPos = false; Vector3 pos = Vector3.zero;
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                cmd = root.GetProperty("cmd").GetString();
                if (root.TryGetProperty("id", out var idv) && idv.ValueKind == System.Text.Json.JsonValueKind.Number) id = idv.GetInt32();
                if (root.TryGetProperty("what", out var wv) && wv.ValueKind == System.Text.Json.JsonValueKind.String) what = wv.GetString();
                if (root.TryGetProperty("arg", out var av) && av.ValueKind == System.Text.Json.JsonValueKind.String) arg = av.GetString();
                if (root.TryGetProperty("pos", out var pv) && pv.ValueKind == System.Text.Json.JsonValueKind.Array && pv.GetArrayLength() == 3)
                {
                    pos = new Vector3(pv[0].GetSingle(), pv[1].GetSingle(), pv[2].GetSingle()); hasPos = true;
                }
            }
            if (cmd == "fire") FireNow(id);
            else if (cmd == "try") Say(TryOuts.Run(what, arg, hasPos, pos, raid, gm));
            else if (cmd == "spawnWaiting") SpawnWaiting();
            else if (cmd == "removeAllEnemies") RemoveAllEnemies(gm);
            else if (cmd == "save") Save();
            else if (cmd == "export") ExportMap();
            else Say("unknown request from the browser: " + cmd);
            return StateJson();
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
            plan.Triggers.AddRange(_zones);
            plan.Doors.AddRange(_doorPlans);
            foreach (SpawnPlan s in Director.SatOut) { plan.Spawns.Add(s); plan.Groups = Math.Max(plan.Groups, s.Group + 1); }   // lost their roll this time; still part of the scenario
            plan.ExtractionAfter = _holdAfter; plan.Insertion = _insertion; plan.Weather = _weather; plan.Power = _power;
            RaidManager raidNow = RaidManager.singleton;
            if (raidNow != null)
                foreach (ObjectiveObject o in Objectives(raidNow))
                {
                    if (!Moved(o)) continue;                 // left alone: the game keeps choosing its place itself
                    Vector3 op = o.transform.position;
                    plan.Objectives.Add(new ObjectivePlan { Name = o.gameObject.name, Pos = new V3(op.x, op.y, op.z), Yaw = o.transform.eulerAngles.y });
                }
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
                DirectorPlugin.KeyExport.Value + "   export this map for the 3D view of the scenario editor",
                LiveLink.Running ? "In a browser on this PC: http://localhost:" + LiveLink.Port + " (the same tools, live)" : "",
            };
            float x = 24f, y = 96f, wPanel = 730f, lh = 21f;
            Fill(new Rect(x - 10f, y - 8f, wPanel, lines.Count * lh + 16f), new Color(0f, 0f, 0f, 0.62f));
            for (int i = 0; i < lines.Count; i++, y += lh)
                if (lines[i].Length > 0) Text(x, y, wPanel, lines[i], (i == 8 && _aiming >= 0) || i == 4 ? waiting : white);
        }
    }
}
