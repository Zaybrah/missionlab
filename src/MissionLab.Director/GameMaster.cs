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
            public IntPtr SideFor;      // the live enemy that has already been given this marker's side
            public bool HadBot;         // an enemy has been made from this marker at some time
            public float HuntAt;        // below 0: sets off for the players once its enemy exists; above 0: at that time
            public string Command = ""; // the page's change that waits to hear whether this marker's enemy was made ("": none)
            public int Spawn = -1;      // its place in the plan's list of spawns when the mission began (-1: placed during the mission); kept through edits, for the recording
        }
        private sealed class LiveThing { public ThingPlan Plan; public GameObject Go; public bool Placed; }
        private static readonly List<LiveThing> _things = new List<LiveThing>();
        private static readonly HashSet<IntPtr> EverAlive = new HashSet<IntPtr>();
        private sealed class Batch { public readonly List<Marker> Items = new List<Marker>(); public string Type; }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] Roles = { "Guard", "Wander", "Patrol" };
        private static int _nextId = 1;
        private static List<TriggerPlan> _zones = new List<TriggerPlan>();
        // Which triggers fire, and when, is decided by the engine in the core; this file asks the game and does what a firing does.
        private static readonly TriggerEngine Engine = new TriggerEngine();
        private static readonly GameWorld Asks = new GameWorld();

        private sealed class GameWorld : ITriggerWorld
        {
            public RaidManager Raid; public GameManager Gm; public float Now;
            public bool Alerted() => GameMaster.Alerted(Gm, Now);
            public bool IntelTaken(string item) => GameMaster.IntelTaken(Raid, item);
            public bool Occupied(TriggerPlan t, out V3 where)
            {
                Vector3 who;
                bool yes = t.Everyone ? GameMaster.EveryoneInside(t, out who) : GameMaster.PlayerInside(t, out who);
                where = new V3(who.x, who.y, who.z);
                return yes;
            }
            public bool Down(int count, string tag) => string.IsNullOrEmpty(tag) ? GameMaster.EnemiesDown(Gm, count) : GameMaster.TaggedDown(Gm, tag, count);
            public bool DoorOpen(V3 hinge) { DoorV2 door = GameMaster.DoorAt(hinge); return door != null && (door.isDead || door.IsOpenEnoughForAI); }
            public int Inside(TriggerPlan t, out int alive)
            {
                // Only asked to explain a trigger to the page: a failure says "nobody alive" rather than stopping the rules.
                alive = 0; int inside = 0;
                try
                {
                    var players = PlayerMaster.AllPlayerMasters;
                    for (int i = 0; players != null && i < players.Count; i++)
                    {
                        PlayerNetworking body = players[i] == null ? null : players[i].PlayerSpawnedObject;
                        if (body == null) continue;
                        alive++;
                        Vector3 p = body.transform.position;
                        if (Membership.Inside(t, new V3(p.x, p.y, p.z))) inside++;
                    }
                }
                catch { alive = 0; return 0; }
                return inside;
            }
        }
        private static float _zoneCheckAt;
        private static bool _yawLogged, _objectivesPlaced;
        private static List<ObjectivePlan> _wanted = new List<ObjectivePlan>();
        private sealed class Pose { public Vector3 Pos; public float Yaw; }
        private static readonly Dictionary<IntPtr, Pose> Homes = new Dictionary<IntPtr, Pose>();   // where the game itself put each item
        private static bool _resendFailed;
        private static List<DoorPlan> _doorPlans = new List<DoorPlan>();    // what the scenario or the Game Master asked of doors
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
        private static string _skillAll = "";       // the scenario's own difficulty: every enemy without a skill of its own
        private static bool _nvgAll;
        private static bool _needsGm;               // the scenario says it is meant to be run by a Game Master
        private static readonly WaveRule Wave = new WaveRule();                 // the scenario's respawn rule: a shared reinforcement wave
        private static readonly HashSet<IntPtr> WaveDead = new HashSet<IntPtr>();   // who was dead when the game was last asked
        private static float _vehicleCheckAt = -1f;
        private static float _noticeAt = -1f;
        private static string _title = "";
        private static List<string> _briefing = new List<string>();
        private static readonly Queue<string> BriefQueue = new Queue<string>();
        private static float _briefAt = -1f;
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
            BeginBudget(plan);
            _weather = plan.Weather ?? new WeatherPlan();
            _power = plan.Power ?? ""; _skillAll = plan.Skill ?? ""; _nvgAll = plan.Nvg; _needsGm = plan.GameMaster; Wave.After = plan.RespawnAfter; Wave.Times = plan.RespawnTimes;
            _things.Clear(); foreach (ThingPlan thing in plan.Things) _things.Add(new LiveThing { Plan = thing });
            _title = plan.Title ?? ""; _briefing = new List<string>(plan.Briefing);
            BriefQueue.Clear(); 
            foreach (string line in _briefing) BriefQueue.Enqueue(line);
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
            _zones = new List<TriggerPlan>(); Engine.Reset(0f); _yawLogged = false;
            _wanted = new List<ObjectivePlan>(); _objectivesPlaced = false; Homes.Clear();
            _doorPlans = new List<DoorPlan>(); _doorList = null; _doorScanAt = 0f; _doorError = null;
            Items.Clear(); Taken.Clear(); _alerted = false; Known.Clear(); Crew.Clear(); Vehicles.Clear(); TryOuts.Reset(); EverAlive.Clear();
            HoldExtraction = false; ExtractionAsked = false; ForcedInfil = -1; _holdAfter = 0; _insertion = ""; _weather = new WeatherPlan(); _power = ""; _vehicleCheckAt = -1f; _noticeAt = -1f; _briefAt = -1f; BriefQueue.Clear(); _skillAll = ""; _nvgAll = false; _needsGm = false; Wave.After = 0f; Wave.Times = 0; Wave.Reset(); WaveDead.Clear(); Openings.Clear(); _title = ""; _briefing = new List<string>();
            _raidPtr = IntPtr.Zero; _markers = null; _later = null; _current = -1; _active = false; _group = -1; _savePath = null;
            Pending.Clear(); _running = null; _savedList = null; _savedCustom = null; _before = null; Firings.Clear(); ResetBudget();
        }

        public static void Tick(float now)
        {
            if (_raidPtr == IntPtr.Zero) return;
            RaidManager raid = RaidManager.singleton;
            GameManager gm = GameManager.instance;
            if (raid == null || gm == null || raid.Pointer != _raidPtr || !Mirror.NetworkServer.active) { Replay.End("the mission is over"); try { RunRecord.Finish(); } catch { } Reset(); Director.EndSession("the mission is over"); return; }

            Replay.Tick(raid, gm, now);
            TryOuts.Tick(Time.unscaledTime);
            ApplySides(gm);
            Hunters(gm, now);
            RespawnRule(raid, now);
            SwingDoors(now);
            PumpSpawns(raid, gm, now);
            BudgetTick(Time.unscaledTime);
            if (!_keysOk || !DirectorPlugin.GmEnabled.Value) return;
            try { Keys(raid, gm); }
            catch (InvalidOperationException e) { _keysOk = false; DirectorPlugin.L.LogWarning("Game Master keys disabled: " + e.Message); }
        }

        internal static string LastSaid = ""; internal static int SaidCount;

        /// <summary>The mod lets go of what it holds in the game: the camera it opened and the input it took.</summary>
        internal static void LetGo()
        {
            if (_weOpenedCam)
                try { ScreenshotFreeCamera fc = FreeCam(); if (fc != null && fc.ToggledCam) fc.ToggleOffCamera(); } catch { }
            _weOpenedCam = false;
            if (_active) ReleaseInput();
        }

        /// <summary>What the page is told whether or not a mission runs: the session, each part's health, a refused scenario, recent changes.</summary>
        internal static string Standing()
        {
            return ",\"plugin\":" + Q(DirectorPlugin.Version) + ",\"repeat\":" + Q(Director.RepeatFile) + ",\"run\":" + Q(RunRecord.LastFile) + ",\"session\":" + Q(Director.Session) + ",\"rejected\":" + Q(Director.Rejected ?? "")
                 + ",\"startError\":" + Q(LiveLink.StartError) + ",\"health\":" + DirectorPlugin.HealthJson() + ",\"commands\":" + LiveLink.Ledger.RecentJson(12);
        }

        internal static void Say(string text)
        {
            LastSaid = text; SaidCount++;
            Director.Banner = "MissionLab GM: " + text;
            Director.BannerUntil = Time.unscaledTime + 7f;
            DirectorPlugin.L.LogInfo(Director.Banner);
        }

        /// <summary>
        /// On screen only while someone is acting as Game Master; a player should not be told an ambush just appeared.
        /// kind, at, id: how the timeline records it (a trigger firing is a "fire" event with the trigger's id).
        /// </summary>
        private static void Tell(string text, string kind = "note", Vector3? at = null, int id = 0)
        {
            Replay.Event(kind, text, at, id);
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
            for (int i = 0; i < plan.Spawns.Count; i++)
                _markers.Add(new Marker { Plan = plan.Spawns[i], Spawn = i, Waiting = _later != null && _later.Contains(plan.Spawns[i]) });
            _zones = new List<TriggerPlan>(plan.Triggers);
            AreaEdits.Prepare(_zones);         // painted areas get their bounds once here, so the check every frame builds nothing
            Engine.Reset(Time.unscaledTime); Engine.Sync(_zones);
            foreach (string problem in Engine.Problems) DirectorPlugin.L.LogWarning("triggers: " + problem);
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
            sb.Append(",\"respawn\":{\"after\":").Append(Wave.After.ToString("0.##", Inv)).Append(",\"times\":").Append(Wave.Times).Append(",\"done\":").Append(Wave.Done)
              .Append(",\"state\":\"").Append(Wave.State).Append("\",\"left\":").Append(Wave.WavesLeft).Append(",\"in\":").Append(Wave.SecondsLeft(Time.unscaledTime).ToString("0", Inv))
              .Append(",\"error\":").Append(Q(Wave.Error)).Append('}');
            sb.Append(",\"gameMaster\":").Append(_needsGm ? "true" : "false");
            sb.Append(",\"skill\":").Append(Q(_skillAll)).Append(",\"nvg\":").Append(_nvgAll ? "true" : "false");
            sb.Append(",\"power\":").Append(Q(_power)).Append(",\"satOut\":").Append(Director.SatOut.Count + Director.DoorsOut.Count + Director.ThingsOut.Count);
            sb.Append(",\"title\":").Append(Q(_title)).Append(",\"briefing\":[");
            for (int i = 0; i < _briefing.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(_briefing[i]));
            sb.Append(']'); rev.Append('B').Append(_title).Append(string.Join("|", _briefing));
            bool night = false; try { night = (raid.gameObject.scene.name ?? "").IndexOf("Night", StringComparison.OrdinalIgnoreCase) >= 0; } catch { }
            sb.Append(",\"night\":").Append(night ? "true" : "false").Append(",\"insertionSpots\":[");
            try
            {
                InfiltrationManager im = InfiltrationManager.instance;
                var methods = im == null ? null : im.infilMethods; bool first = true;
                for (int i = 0; methods != null && i < methods.Length; i++)
                {
                    if (methods[i] == null || methods[i].infilObject == null) continue;
                    Vector3 at = methods[i].infilObject.transform.position;
                    sb.Append(first ? "" : ",").Append("{\"name\":").Append(Q(methods[i].infilName ?? "")).Append(",\"pos\":[").Append(F(at.x)).Append(',').Append(F(at.y)).Append(',').Append(F(at.z)).Append("]}");
                    first = false;
                }
            }
            catch { }
            sb.Append(']');
            rev.Append('P').Append(_power).Append(_skillAll).Append(_nvgAll ? '1' : '0').Append(_needsGm ? 'G' : 'g').Append(Wave.After.ToString("0.##", Inv)).Append('x').Append(Wave.Times).Append('x').Append(Wave.Done).Append(Wave.State);
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

        private struct Seen { public float Since; public bool Settled; public BrainAI.States Rest; public bool Crew; public float LookAgainAt; }
        private static readonly HashSet<IntPtr> Crew = new HashSet<IntPtr>();

        /// <summary>
        /// Is this the gunner of a gun truck? He cannot walk, and what he reports at rest differs from run to run
        /// ("Evade" in two recordings, "WasSighted" in a third), so his state is never read; the truck's own target is.
        /// </summary>
        private static bool IsCrew(BrainAI b)
        {
            if (Crew.Contains(b.Pointer)) return true;
            try
            {
                AgentController legs = b.agent;
                if (legs != null && legs.entityExists) return false;
                var found = UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<VehicleAI>(), true);
                for (int i = 0; found != null && i < found.Length; i++)
                {
                    VehicleAI v = found[i] == null ? null : found[i].TryCast<VehicleAI>();
                    BrainAI gunner = v == null ? null : v.turrentBot;
                    if (gunner != null) Crew.Add(gunner.Pointer);
                }
            }
            catch { }
            return Crew.Contains(b.Pointer);
        }
        private static readonly Dictionary<IntPtr, Seen> Known = new Dictionary<IntPtr, Seen>();
        private static readonly List<VehicleAI> Vehicles = new List<VehicleAI>();

        /// <summary>
        /// Who an enemy is, for the recording: the plan's spawn it was made from (its place in this mission's list), or the
        /// Game Master marker it was made from (-1 for either: not known), and the number of the gun truck it mans (0: none).
        /// An enemy made again after an edit comes from the same marker, so it stays who it was. An enemy's own object stands
        /// where it was made, so one that is not linked to its marker yet is matched by that place, as MapBots does.
        /// </summary>
        internal static void Identity(BrainAI b, out int spawn, out int marker, out int truck)
        {
            spawn = -1; marker = -1; truck = 0;
            VehicleAI v = TruckOf(b);
            Vector3 at = b.transform.position;
            if (v != null) { truck = TruckNumber(v); at = v.transform.position; }
            float reach = v != null ? 4f : 0.8f;          // a truck is parked on its spot; the gunner sits above it
            if (_markers != null)
            {
                Marker found = null;
                if (v == null) foreach (Marker m in _markers) if (m.Bot == b.Pointer) { found = m; break; }
                if (found == null)
                    foreach (Marker m in _markers)
                    {
                        if (m.Plan.Vehicle != (v != null) || (m.Bot != IntPtr.Zero && m.Bot != b.Pointer)) continue;
                        float d = PlanDistance(m.Plan.Pos, at);
                        if (d < reach) { reach = d; found = m; }
                    }
                if (found == null) return;
                if (found.Spawn >= 0) spawn = found.Spawn; else marker = found.Id;
                return;
            }
            // No markers yet (nobody has opened the Game Master tools): the plan's list is what the markers will be made from, in the same order.
            MissionPlan plan = Director.CurrentPlan;
            for (int i = 0; plan != null && i < plan.Spawns.Count; i++)
            {
                SpawnPlan s = plan.Spawns[i];
                if (s.Vehicle != (v != null)) continue;
                float d = PlanDistance(s.Pos, at);
                if (d < reach) { reach = d; spawn = i; }
            }
        }

        private static float PlanDistance(V3 p, Vector3 at) => Mathf.Sqrt((at.x - p.X) * (at.x - p.X) + (at.z - p.Z) * (at.z - p.Z)) + Mathf.Abs(at.y - p.Y) * 0.5f;

        /// <summary>The gun truck this enemy mans, or null. A gunner cannot walk, so anyone who can is not looked up.</summary>
        private static VehicleAI TruckOf(BrainAI b)
        {
            try
            {
                AgentController legs = b.agent;
                if (legs != null && legs.entityExists) return null;
                var found = UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<VehicleAI>(), true);
                for (int i = 0; found != null && i < found.Length; i++)
                {
                    VehicleAI v = found[i] == null ? null : found[i].TryCast<VehicleAI>();
                    BrainAI gunner = v == null ? null : v.turrentBot;
                    if (gunner != null && gunner.Pointer == b.Pointer) { Crew.Add(b.Pointer); return v; }
                }
            }
            catch { }
            return null;
        }

        /// <summary>A gun truck's number in this mission, counted in the order the trucks were first seen; a number once given stays.</summary>
        private static int TruckNumber(VehicleAI v)
        {
            for (int i = 0; i < Vehicles.Count; i++) if (Vehicles[i] != null && Vehicles[i].Pointer == v.Pointer) return i + 1;
            Vehicles.Add(v);
            return Vehicles.Count;
        }

        /// <summary>
        /// What state an enemy is really in. Recordings show that a state read off a new enemy is not to be trusted:
        /// a soldier reports "Evade" for about half a second (sometimes after one first moment of "Idle"), and a gun
        /// truck's gunner reports "Evade" for as long as nothing happens. So for two seconds nothing counts; what the
        /// enemy reports then is taken as its state of rest and read as idle, and only something else is news.
        /// </summary>
        internal static bool StateCounts(BrainAI b, float now, out BrainAI.States state)
        {
            state = b.CurrentState;
            IntPtr key = b.Pointer;
            if (!Known.TryGetValue(key, out Seen s)) { Known[key] = new Seen { Since = now }; return false; }
            if (!s.Settled)
            {
                if (now - s.Since < 2f) return false;
                s.Settled = true;
                s.Rest = state == BrainAI.States.Evade ? BrainAI.States.Evade : BrainAI.States.Idle;
                s.Crew = IsCrew(b); s.LookAgainAt = now + 1f;
                Known[key] = s;
            }
            if (!s.Crew && now >= s.LookAgainAt && now - s.Since < 15f)      // a truck may get its gunner a little later
            {
                s.Crew = IsCrew(b); s.LookAgainAt = now + 1f; Known[key] = s;
            }
            if (s.Crew || state == s.Rest) state = BrainAI.States.Idle;
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
                try { if (b != null && StateCounts(b, now, out BrainAI.States state) && state != BrainAI.States.Idle) yes = true; } catch { }
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

        /// <summary>A fixed sentry of the scenario, or a vehicle's crew: neither takes a walking order.</summary>
        internal static bool Posted(BrainAI b)
        {
            try { if (IsCrew(b)) return true; } catch { }
            if (_markers != null) foreach (Marker m in _markers) if (m.Bot == b.Pointer) return m.Plan.Fixed;
            return false;
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

        /// <summary>Asks the engine which triggers fire now and which countdown lines are due, and does what they say.</summary>
        private static void CheckTriggers(RaidManager raid, GameManager gm, float now)
        {
            // An ending is decided: nothing more fires (18 November: a timer fired 2 s after the mission was won).
            if (TryOuts.Ending) return;
            Engine.Start = Director.HandledAt;                 // timers count from the start of the mission
            Asks.Raid = raid; Asks.Gm = gm; Asks.Now = now;
            foreach (TriggerEvent ev in Engine.Step(now, Asks))
            {
                TriggerPlan t = _zones.Find(z => z.Id == ev.Id);
                if (t == null) continue;
                if (ev.Kind == TriggerEvent.Fire) { Fire(t, now, ev.Again); continue; }
                try
                {
                    switch (ev.Step)
                    {
                        case TriggerEvent.Started: WarnLeft(raid, t, TryOuts.Clock(t.Delay) + " from now.", false); break;
                        case TriggerEvent.FiveMinutes: WarnLeft(raid, t, "five minutes left.", false); break;
                        case TriggerEvent.OneMinute: WarnLeft(raid, t, "one minute left.", false); break;
                        case TriggerEvent.TenSeconds: WarnLeft(raid, t, "ten seconds left.", true); break;
                    }
                }
                catch { }
            }
        }

        /// <summary>Have that many enemies gone down? 0 asks for every enemy there is, once there has been one.</summary>
        private static bool EnemiesDown(GameManager gm, int count)
        {
            var ai = gm.allAI; int alive = 0;
            for (int i = 0; ai != null && i < ai.Count; i++) { BrainAI b = ai[i]; if (b == null) continue; alive++; EverAlive.Add(b.Pointer); }
            return count <= 0 ? EverAlive.Count > 0 && alive == 0 : EverAlive.Count - alive >= count;
        }

        // Seen on 17 November: telling a door it is "not closed" only unlatches it; it stays shut. To stand open it has to be
        // swung. How the game likes that best is not known yet, so four ways are tried one after the other, a second and a
        // half apart, until the door is open enough for an enemy to walk through. The log says which way did it.
        private sealed class Opening { public IntPtr Door; public string Name; public float NextAt; public int Way = -1; }
        private static readonly List<Opening> Openings = new List<Opening>();
        private static readonly string[] Ways = { "a push, as a player gives it", "a push the other way", "turning it by hand", "setting its angle" };

        private static void SetDoorOpen(DoorV2 d, bool open)
        {
            if (!open)
            {
                Openings.RemoveAll(o => o.Door == d.Pointer);
                try { d.UserCode_CMD_SetDoorClosed__Boolean(true); } catch { d.RPC_SetDoorClosed(true); }
                return;
            }
            try { if (d.CurrentlyLocked) d.Server_SetDoorLocked(false); } catch { }        // a door that stands open is not locked
            try { d.UserCode_CMD_SetDoorClosed__Boolean(false); } catch { try { d.RPC_SetDoorClosed(false); } catch { } }
            if (!Openings.Exists(o => o.Door == d.Pointer)) Openings.Add(new Opening { Door = d.Pointer, Name = d.name, NextAt = Time.unscaledTime + 0.5f });
        }

        private static void SwingDoors(float now)
        {
            if (Openings.Count == 0) return;
            PlayerNetworking player = null;
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count && player == null; i++) if (players[i] != null) player = players[i].PlayerSpawnedObject;
            if (player == null) return;                                   // a push needs someone to give it: wait for the first player
            for (int i = Openings.Count - 1; i >= 0; i--)
            {
                Opening o = Openings[i];
                if (now < o.NextAt) continue;
                DoorV2 d = null;
                foreach (DoorV2 one in Doors()) if (one.Pointer == o.Door) { d = one; break; }
                bool gone = d == null; try { gone = gone || d.isDead; } catch { gone = true; }
                if (gone) { Openings.RemoveAt(i); continue; }
                bool isOpen = false; try { isOpen = d.IsOpenEnoughForAI; } catch { }
                if (isOpen)
                {
                    DirectorPlugin.L.LogInfo("door '" + o.Name + "' stands open" + (o.Way < 0 ? " already" : " (by " + Ways[o.Way] + ")"));
                    if (o.Way >= 0) Replay.Event("note", "door '" + o.Name + "' stands open (by " + Ways[o.Way] + ")");
                    Openings.RemoveAt(i); continue;
                }
                if (o.Way >= Ways.Length - 1)
                {
                    DirectorPlugin.L.LogWarning("door '" + o.Name + "' could not be made to stand open: " + Ways.Length + " ways were tried");
                    Replay.Event("note", "door '" + o.Name + "' could not be opened");
                    Openings.RemoveAt(i); continue;
                }
                o.Way++; o.NextAt = now + 1.5f;
                try
                {
                    switch (o.Way)
                    {
                        case 0: d.AddForceAndUnlatch(4f, player); break;
                        case 1: d.AddForceAndUnlatch(-4f, player); break;
                        case 2: d.AddDirectRotation(80f, player); break;
                        default: d.SetRotationHotReload(80f); break;
                    }
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("door '" + o.Name + "': " + Ways[o.Way] + " failed: " + e.Message); }
            }
        }

        /// <summary>Enemies of a "hunt" trigger set off for the nearest player a moment after they appear.</summary>
        private static void Hunters(GameManager gm, float now)
        {
            if (_markers == null) return;
            foreach (Marker m in _markers)
            {
                if (m.HuntAt == 0f) continue;
                if (m.HuntAt < 0f) { if (m.Bot != IntPtr.Zero) m.HuntAt = now + 2.5f; continue; }
                if (now < m.HuntAt) continue;
                m.HuntAt = 0f;
                try
                {
                    BrainAI b = m.Bot == IntPtr.Zero ? null : Find(gm, m.Bot);
                    if (b == null) continue;
                    EnemyPose(b, out Vector3 from, out _);          // its own object stays where it was made
                    Vector3 to = TryOuts.NearestPlayer(from);
                    if (to == Vector3.zero) continue;
                    int sent = TryOuts.WalkTo(gm, to, 0f, new HashSet<IntPtr> { m.Bot }, true);
                    DirectorPlugin.L.LogInfo("hunter: enemy #" + m.Id + (sent == 1 ? " sets off for the nearest player, " + (to - from).magnitude.ToString("0", Inv) + " m away" : " could not be sent"));
                    if (sent == 1) Replay.Event("note", "hunter: an enemy sets off for the nearest player");
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("a hunter could not be sent: " + e.Message); }
            }
        }

        /// <summary>
        /// The scenario's respawn rule: a shared reinforcement wave. The core decides (WaveRule); this asks the game and
        /// reports what it sees. A wave counts when a player who was dead is alive again, not when the game was asked.
        /// </summary>
        private static void RespawnRule(RaidManager raid, float now)
        {
            if (Wave.After <= 0f || now - Director.HandledAt < 20f) return;
            int dead = 0, back = 0;
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerMaster pm = players[i];
                if (pm == null) continue;
                if (pm.PlayerSpawnedObject == null) dead++; else if (WaveDead.Contains(pm.Pointer)) back++;
            }
            switch (Wave.Step(now, dead, back))
            {
                case WaveRule.Act.Announce:
                    DirectorPlugin.L.LogInfo("reinforcement wave: " + dead + " dead; the wave comes in " + Wave.After.ToString("0", Inv) + " s" + (Wave.Times > 0 ? ", " + Wave.WavesLeft + " left" : ""));
                    Replay.Event("note", "reinforcement wave: the clock runs, " + Wave.After.ToString("0", Inv) + " s");
                    try { TryOuts.Message(raid, (dead == 1 ? "A player is down." : dead + " players are down.") + " Reinforcement wave in " + TryOuts.Clock(Wave.After) + "."); } catch { }
                    break;
                case WaveRule.Act.Ask:
                    WaveDead.Clear();
                    for (int i = 0; players != null && i < players.Count; i++) if (players[i] != null && players[i].PlayerSpawnedObject == null) WaveDead.Add(players[i].Pointer);
                    bool ok = false; string said;
                    try { said = TryOuts.RespawnNow(true); ok = !said.Contains("was not found"); }
                    catch (Exception e) { said = e.GetType().Name + ": " + e.Message; }
                    Wave.Asked(now, ok, ok ? "" : said);
                    TryOuts.Did("asking for a reinforcement wave");
                    DirectorPlugin.L.LogInfo("reinforcement wave: the game is asked (try " + Wave.Tries + ", " + dead + " dead): " + said);
                    Replay.Event("note", "reinforcement wave asked for: " + dead + " dead");
                    if (!ok) Say("the reinforcement wave could not be asked for: " + said);
                    break;
                case WaveRule.Act.Observed:
                    WaveDead.Clear();
                    TryOuts.RestoreRespawns("the wave was seen");
                    string left = Wave.Times > 0 ? " " + Wave.WavesLeft + (Wave.WavesLeft == 1 ? " wave" : " waves") + " left." : "";
                    DirectorPlugin.L.LogInfo("reinforcement wave " + Wave.Done + " seen: " + back + " back, " + dead + " still dead." + left);
                    Replay.Event("note", "reinforcement wave " + Wave.Done + ": " + back + (back == 1 ? " player is" : " players are") + " back");
                    try { TryOuts.Message(raid, (back == 1 ? "A player is" : back + " players are") + " back in the fight." + left); } catch { }
                    break;
                case WaveRule.Act.GaveUp:
                    TryOuts.RestoreRespawns("the wave was not seen");
                    DirectorPlugin.L.LogWarning("reinforcement wave: " + Wave.Error + " (try " + Wave.Tries + " of " + WaveRule.MaxTries + "). It is not counted.");
                    Replay.Event("note", "reinforcement wave failed: " + Wave.Error);
                    Say("reinforcement wave: " + Wave.Error + (Wave.Tries < WaveRule.MaxTries ? "; it is tried again in a moment" : "; it is not tried again"));
                    break;
            }
        }

        private static void WarnLeft(RaidManager raid, TriggerPlan t, string text, bool red)
        {
            string line = (string.IsNullOrEmpty(t.Name) ? "" : t.Name + ": ") + text;
            TryOuts.Message(raid, line, red);
            DirectorPlugin.L.LogInfo("countdown of " + TriggerName(t) + ": \"" + line + "\" is shown");
            Replay.Event("note", "countdown: " + line);
        }

        /// <summary>
        /// Is every living player inside the zone (its painted area, or else its circle)? For a house of several parts that means
        /// somewhere in the union, not one player in every room. Nobody alive: no. where: the last player looked at, who is inside.
        /// </summary>
        private static bool EveryoneInside(TriggerPlan z, out Vector3 where)
        {
            where = Vector3.zero; int alive = 0;
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerNetworking body = players[i] == null ? null : players[i].PlayerSpawnedObject;
                if (body == null) continue;
                alive++;
                Vector3 p = body.transform.position;
                if (!Membership.Inside(z, new V3(p.x, p.y, p.z))) return false;
                where = p;
            }
            return alive > 0;
        }

        /// <summary>Have enough of the enemies that carry a tag gone down? 0 asks for all of them, once each has been in the mission.</summary>
        private static bool TaggedDown(GameManager gm, string tag, int count)
        {
            if (_markers == null) return false;
            int all = 0, down = 0;
            foreach (Marker m in _markers)
            {
                if (!string.Equals(m.Plan.Tag, tag, StringComparison.OrdinalIgnoreCase)) continue;
                all++;
                if (m.HadBot && !m.Waiting && !m.Queued && (m.Bot == IntPtr.Zero || Find(gm, m.Bot) == null)) down++;
            }
            return all > 0 && (count <= 0 ? down == all : down >= count);
        }

        /// <summary>Places the things that belong to a trigger (0: the start of the mission). Each is placed once.</summary>
        private static int PlaceThings(int trigger)
        {
            int n = 0;
            foreach (LiveThing t in _things)
            {
                if (t.Placed || t.Plan.Trigger != trigger) continue;
                t.Placed = true;
                try
                {
                    t.Go = TryOuts.Place(t.Plan.Name, new Vector3(t.Plan.Pos.X, t.Plan.Pos.Y, t.Plan.Pos.Z), t.Plan.Yaw, out string said);
                    if (t.Go != null) { n++; Replay.Event("appears", "'" + t.Plan.Name + "' is placed", t.Go.transform.position); }
                    else DirectorPlugin.L.LogWarning("thing: " + said);
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("thing '" + t.Plan.Name + "' could not be placed: " + e.Message); }
            }
            return n;
        }

        /// <summary>The page's list of things becomes the mission's: what is gone is taken away, what is new is placed if its time has come.</summary>
        private static void ReconcileThings(List<ThingPlan> want)
        {
            var keep = new HashSet<string>(); foreach (ThingPlan t in want) keep.Add(t.Key);
            for (int i = _things.Count - 1; i >= 0; i--)
            {
                if (keep.Contains(_things[i].Plan.Key)) continue;
                try { if (_things[i].Go != null) Mirror.NetworkServer.Destroy(_things[i].Go); } catch { }
                _things.RemoveAt(i);
            }
            var have = new HashSet<string>(); foreach (LiveThing t in _things) have.Add(t.Plan.Key);
            foreach (ThingPlan t in want) if (have.Add(t.Key)) _things.Add(new LiveThing { Plan = t });
            PlaceThings(0);
            foreach (int id in new List<int>(Engine.FiredIds())) PlaceThings(id);
        }

        /// <summary>What a marker says about its enemy beyond where it stands: its side, its skill, night vision. Applied once to each live enemy.</summary>
        private static void ApplySides(GameManager gm)
        {
            if (_markers == null) return;
            for (int i = 0; i < _markers.Count; i++)
            {
                Marker m = _markers[i];
                SpawnPlan p = m.Plan;
                if (m.Bot != IntPtr.Zero) m.HadBot = true;
                string skill = p.Skill != "" ? p.Skill : _skillAll; bool nvg = p.Nvg || _nvgAll;
                if ((p.Team <= 0 && skill == "" && !nvg) || m.Bot == IntPtr.Zero || m.SideFor == m.Bot) continue;
                BrainAI b = Find(gm, m.Bot);
                if (b == null) continue;
                m.SideFor = m.Bot;
                var said = new List<string>();
                if (p.Team > 0)
                    try
                    {
                        TeamIdentifier t = b.GetComponent<TeamIdentifier>();
                        if (t == null) t = b.GetComponentInParent<TeamIdentifier>();
                        if (t == null) t = b.GetComponentInChildren<TeamIdentifier>(true);
                        if (t == null) said.Add("nothing to carry a team");
                        else { int before = t.TeamID; if (before != p.Team) t.NetworkTeamID = p.Team; said.Add("team " + p.Team + " (was " + before + ")"); }
                    }
                    catch (Exception e) { said.Add("team not set: " + e.Message); }
                if (skill == "recruit" || skill == "veteran")
                    try
                    {
                        // Relative to what the game gave this enemy, so the mission's difficulty setting still counts.
                        bool vet = skill == "veteran";
                        float slow = vet ? 0.6f : 1.6f, wild = vet ? 0.6f : 1.6f, sure = vet ? 1.35f : 0.6f;
                        float r0 = b.ReactionTime, w0 = b.WeaponInaccuracy, h0 = b.baseHitChance;
                        b.ReactionTime = r0 * slow; b._baseReactionTime = b._baseReactionTime * slow; b.WeaponInaccuracy = w0 * wild; b.baseHitChance = h0 * sure;
                        said.Add(skill + ": reaction " + r0.ToString("0.00", Inv) + " to " + b.ReactionTime.ToString("0.00", Inv) + ", inaccuracy " + w0.ToString("0.00", Inv) + " to " + b.WeaponInaccuracy.ToString("0.00", Inv) +
                                 ", hit chance " + h0.ToString("0.00", Inv) + " to " + b.baseHitChance.ToString("0.00", Inv));
                    }
                    catch (Exception e) { said.Add("skill not set: " + e.Message); }
                if (nvg)
                    try { bool had = b.HasNVG; b.HasNVG = true; said.Add("night vision " + (had ? "(had it)" : "on")); }
                    catch (Exception e) { said.Add("night vision not set: " + e.Message); }
                DirectorPlugin.L.LogInfo("enemy #" + (i + 1) + ": " + string.Join("; ", said));
            }
        }

        /// <summary>The trigger's enemies appear and its doors lock or unlock.</summary>
        private static void Fire(TriggerPlan t, float now, bool again)
        {
            if (again && _markers != null)
            {
                // Fired again: its enemies that have gone down since come back.
                GameManager gmNow = GameManager.instance;
                foreach (Marker m in _markers)
                    if (m.Plan.Trigger == t.Id && !m.Waiting && !m.Queued && !Special(m.Plan) && (m.Bot == IntPtr.Zero || gmNow == null || Find(gmNow, m.Bot) == null)) { m.Waiting = true; m.Bot = IntPtr.Zero; }
            }
            // Where the player stood who set it off: the engine keeps the entrant's real position (TriggerState.Where), which for a
            // painted area is a point inside it. Nothing here samples the zone's anchor or radius: facing turns to that player (or the
            // player nearest each enemy), and no pop-in visibility test is made at all, so none depends on the old circle. A single
            // check from one point could not show that a whole painted house is out of sight anyway.
            TriggerState memory = Engine.State(t.Id);
            bool stood = memory != null && memory.HasWhere;
            Vector3 who = stood ? new Vector3(memory.Where.X, memory.Where.Y, memory.Where.Z) : Vector3.zero;
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
            // What became of this firing's actions, for the page's inspector (Engine.Report). Its enemies are only known to
            // exist once the spawner has answered, so a firing that brings any is told when the last of them settles (Landed).
            var f = new Firing();
            if (Firings.TryGetValue(t.Id, out Firing before)) foreach (Marker m in before.Waiting) f.Waiting.Add(m);   // fired again before the last lot was made
            foreach (Marker m in mine) if (m.Queued) f.Waiting.Add(m);
            if (t.Hunt && mine.Count > 0) { foreach (Marker m in mine) m.HuntAt = -1f; f.Unsure.Add("sending its enemies after the players"); }
            int doors = 0;
            foreach (DoorPlan p in t.Doors)
            {
                if (!p.Locked.HasValue && !p.Open.HasValue) continue;
                DoorV2 d = DoorAt(p.Pos);
                if (d == null) { f.Fail("no door was found at (" + p.Pos.X.ToString("0.#", Inv) + ", " + p.Pos.Z.ToString("0.#", Inv) + ")"); continue; }
                try
                {
                    // Closing comes before locking, unlocking before opening.
                    if (p.Open.HasValue && !p.Open.Value) SetDoorOpen(d, false);
                    if (p.Locked.HasValue && d.CurrentlyLocked != p.Locked.Value) d.Server_SetDoorLocked(p.Locked.Value);
                    if (p.Open.HasValue && p.Open.Value) SetDoorOpen(d, true);
                    doors++;
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("a door could not be set by " + TriggerName(t) + ": " + e.Message); f.Fail("a door could not be set: " + e.Message); }
            }
            if (doors > 0) f.Unsure.Add(doors == 1 ? "a door" : doors + " doors");     // asked of the game; an opening door swings over the next seconds
            if (t.Respawn)
                try
                {
                    string said = TryOuts.RespawnNow(true); TryOuts.Did("bringing players back (" + TriggerName(t) + ")"); DirectorPlugin.L.LogInfo(TriggerName(t) + ": " + said); Replay.Event("note", "dead players are brought back by " + TriggerName(t));
                    if (said.Contains("was not found")) f.Fail(said); else f.Unsure.Add("bringing dead players back");
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("players could not be brought back by " + TriggerName(t) + ": " + e.Message); f.Fail("players could not be brought back: " + e.Message); }
            RaidManager raidNow = RaidManager.singleton;
            if (raidNow != null)
            {
                bool wasHeld = HoldExtraction && t.Id == _holdAfter;
                if (wasHeld) HoldExtraction = false;
                bool opens = t.OpensExtraction || (wasHeld && ExtractionAsked);
                if (t.OpensExtraction) OpenExtraction(raidNow, TriggerName(t) + " opens it");
                else if (wasHeld && ExtractionAsked) OpenExtraction(raidNow, "it was waiting for " + TriggerName(t));
                try { if (opens && !ExtractionOpen(raidNow)) f.Fail("the extraction did not open"); } catch { }
            }
            else if (t.OpensExtraction) f.Fail("the mission was not found, so the extraction could not be opened");
            TryOuts.Did("trigger " + t.Id + (string.IsNullOrEmpty(t.Name) ? "" : " '" + t.Name + "'"));
            string more = "";
            if (t.Power == "off" || t.Power == "on")
                try
                {
                    string said = TryOuts.Power(t.Power == "on"); more += ", the lights go " + t.Power;
                    if (said.Contains("no breaker box")) f.Fail("this map has no breaker box for the lights");
                    else if (!said.Contains("the game now says the power is " + t.Power)) f.Unsure.Add("the lights");     // the breaker boxes say what they were told: that is the effect seen
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the lights could not be set by " + TriggerName(t) + ": " + e.Message); f.Fail("the lights could not be set: " + e.Message); }
            if (!string.IsNullOrEmpty(t.Message))
            {
                if (raidNow == null) f.Fail("the mission was not found, so the message could not be shown");
                else
                    try
                    {
                        string said = TryOuts.Message(raidNow, t.Message, t.Red); more += ", a message is shown"; Replay.Event("note", "message: " + t.Message);
                        if (said.StartsWith("message dropped", StringComparison.Ordinal) || said.Contains("no extraction zone")) f.Fail(said); else f.Unsure.Add("a message");
                    }
                    catch (Exception e) { DirectorPlugin.L.LogWarning("the message of " + TriggerName(t) + " could not be shown: " + e.Message); f.Fail("the message could not be shown: " + e.Message); }
            }
            if (t.Rally > 0f)
                try
                {
                    // The place: where the player stood who set it off, else the zone's authored anchor (pos; never a middle worked out
                    // from a painted area, which may lie outside it or in a hole), else the nearest player to nobody in particular.
                    Vector3 to = stood && who != Vector3.zero ? who : TriggerPlan.Placed(t.When) || t.When == TriggerPlan.OnDoor ? new Vector3(t.Pos.X, t.Pos.Y, t.Pos.Z) : TryOuts.FirstPlayer();
                    int called = TryOuts.WalkTo(GameManager.instance, to, t.Rally, null);
                    more += ", " + called + (called == 1 ? " enemy is called" : " enemies are called") + " there";
                    f.Unsure.Add("calling " + called + (called == 1 ? " enemy" : " enemies"));
                    Replay.Event("note", "called: " + TryOuts.LastWalk.Words());
                    DirectorPlugin.L.LogInfo("call by " + TriggerName(t) + ": " + TryOuts.LastWalk.Words());
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the enemies could not be called by " + TriggerName(t) + ": " + e.Message); f.Fail("the enemies could not be called: " + e.Message); }
            int due = 0;
            foreach (LiveThing lt in _things) if (!lt.Placed && lt.Plan.Trigger == t.Id) due++;
            int placedNow = PlaceThings(t.Id);
            if (placedNow > 0) more += ", " + placedNow + (placedNow == 1 ? " thing is placed" : " things are placed");
            if (placedNow < due) f.Fail((due - placedNow) + " of " + due + (due == 1 ? " thing" : " things") + " could not be placed (see the log)");
            string why = t.When == TriggerPlan.OnDoor ? "(a door is opened)" : t.When == TriggerPlan.OnAll ? "(all of triggers " + string.Join(", ", t.Of) + ")" : t.When == TriggerPlan.OnAny ? "(one of triggers " + string.Join(", ", t.Of) + ")" :
                         t.When == TriggerPlan.OnHold ? "(zone held for " + t.Hold.ToString("0", Inv) + " s)" : t.When == TriggerPlan.OnDown ? "(enemies down)" :
                         t.When == TriggerPlan.OnZone ? "reached" : t.When == TriggerPlan.OnAlert ? "(the enemy is alerted)" : t.When == TriggerPlan.OnIntel ? "(intel taken)"
                       : t.When == TriggerPlan.OnTime ? "(time)" : "(after trigger " + t.After + ")";
            Vector3? fireAt = stood ? who : TriggerPlan.Placed(t.When) || t.When == TriggerPlan.OnDoor ? new Vector3(t.Pos.X, t.Pos.Y, t.Pos.Z) : (Vector3?)null;
            Tell(TriggerName(t) + " " + why + ": " + mine.Count + (mine.Count == 1 ? " enemy appears" : " enemies appear") + (doors > 0 ? ", " + doors + (doors == 1 ? " door changes" : " doors change") : "") + more,
                 Replay.KindFire, fireAt, t.Id);
            if (t.End == "success" || t.End == "failed")
                try
                {
                    if (DirectorPlugin.DebriefMessage.Value) Debriefs.ShowAtEnd(raidNow);      // experimental: before the game's end screen, if it gets there in time
                    string said = TryOuts.End(t.End == "success"); Say(TriggerName(t) + " ends the mission: " + said);
                    if (said.Contains("was not found")) f.Fail(said); else f.Unsure.Add("ending the mission");
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the mission could not be ended by " + TriggerName(t) + ": " + e.Message); f.Fail("the mission could not be ended: " + e.Message); }
            if (f.Waiting.Count > 0) Firings[t.Id] = f;
            else Told(t.Id, f);
        }

        /// <summary>
        /// What one firing's actions came to, until it is told to the engine. Applied: every call went through and its effect
        /// was seen (an enemy exists, the extraction is open, the breaker boxes say so). Unconfirmed: the calls went through,
        /// but nothing here can see them take (a message, a door, an order to walk). Failed: the first call that threw or
        /// was refused, which says more than any later one.
        /// </summary>
        private sealed class Firing
        {
            public readonly HashSet<Marker> Waiting = new HashSet<Marker>();   // its enemies the spawner has not answered for yet
            public int Made;
            public string Error = "";
            public readonly List<string> Unsure = new List<string>();
            public void Fail(string why) { if (Error.Length == 0) Error = why; }
        }
        private static readonly Dictionary<int, Firing> Firings = new Dictionary<int, Firing>();

        private static void Told(int id, Firing f)
        {
            Firings.Remove(id);
            try
            {
                if (f.Error.Length > 0) Engine.Report(id, TriggerEngine.Failed, f.Error);
                else if (f.Unsure.Count > 0) Engine.Report(id, TriggerEngine.Unconfirmed, "asked of the game, not seen to take: " + string.Join(", ", f.Unsure));
                else Engine.Report(id, TriggerEngine.Applied, f.Made > 0 ? f.Made + (f.Made == 1 ? " enemy appeared" : " enemies appeared") : "");
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("what became of a trigger's actions could not be kept: " + e.Message); }
        }

        /// <summary>The spawner has answered for a marker: a firing that waited for its enemy hears whether it was made.</summary>
        private static void Landed(Marker m, bool made, string why)
        {
            if (Firings.Count == 0) return;
            foreach (KeyValuePair<int, Firing> p in new List<KeyValuePair<int, Firing>>(Firings))
            {
                if (!p.Value.Waiting.Remove(m)) continue;
                if (made) p.Value.Made++; else p.Value.Fail(why);
                if (p.Value.Waiting.Count == 0) Told(p.Key, p.Value);
            }
        }

        /// <summary>The Game Master sets a trigger off by hand.</summary>
        private static void FireNow(int id)
        {
            float now = Time.unscaledTime;
            TriggerPlan t = _zones.Find(z => z.Id == id);
            if (!Engine.FireNow(id, now, out TriggerEvent ev, out string why)) { Say(t == null ? why : TriggerName(t) + ": " + why); return; }
            Fire(t, now, ev.Again);
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
                if (before != null && old.TryGetValue(DoorKey(p), out DoorPlan was) && was.Locked == p.Locked && was.EnemiesCanOpen == p.EnemiesCanOpen && was.Open == p.Open) continue;
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
                    if (p.Open.HasValue && !d.isDead && d.IsOpenEnoughForAI != p.Open.Value) { SetDoorOpen(d, p.Open.Value); changed++; }
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
                    string wo = w != null && w.Open.HasValue ? (w.Open.Value ? "true" : "false") : "null";
                    bool isOpen = false; try { isOpen = d.IsOpenEnoughForAI; } catch { }
                    sb.Append(first ? "" : ",").Append("{\"name\":").Append(Q(d.gameObject.name)).Append(",\"pos\":").Append(P(hinge)).Append(",\"mid\":").Append(P(Middle(d)))
                      .Append(",\"isLocked\":").Append(locked ? "true" : "false").Append(",\"enemiesKeptOut\":").Append(cant ? "true" : "false").Append(",\"broken\":").Append(dead ? "true" : "false")
                      .Append(",\"isOpen\":").Append(isOpen ? "true" : "false").Append(",\"open\":").Append(wo).Append(",\"locked\":").Append(wl).Append(",\"enemiesCanOpen\":").Append(we).Append(w != null && w.Chance < 100 ? ",\"chance\":" + w.Chance : "").Append('}');
                    rev.Append('D').Append(P(hinge)).Append(locked ? 'l' : 'u').Append(cant ? 'c' : 'o').Append(dead ? 'x' : '-').Append(wl[0]).Append(we[0]).Append(wo[0]).Append(isOpen ? 'O' : 'C');
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

        internal static BrainAI Find(GameManager gm, IntPtr ptr)
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
                if (DirectorPlugin.TellPlayers.Value) _noticeAt = now + 8f;
                if (BriefQueue.Count > 0) _briefAt = now + 14f;
                int thingsNow = PlaceThings(0);
                if (_things.Count > 0) DirectorPlugin.L.LogInfo("things: " + thingsNow + " placed at the start, " + (_things.Count - thingsNow) + " wait for a trigger or could not be placed");
                if (TryOuts.FairPlayIs("Always")) DirectorPlugin.L.LogInfo("fair play: " + TryOuts.Mark());
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
            if (_briefAt >= 0f && now >= _briefAt)
            {
                // The mission's name and its briefing, one line at a time: the game drops messages that come too close together.
                if (BriefQueue.Count == 0) _briefAt = -1f;
                else
                {
                    string line = BriefQueue.Dequeue(); _briefAt = BriefQueue.Count > 0 ? now + 7f : -1f;
                    try { TryOuts.Message(raid, line); Replay.Event("note", "briefing: " + line); }
                    catch (Exception e) { DirectorPlugin.L.LogWarning("the briefing could not be shown: " + e.Message); }
                }
            }
            if (_noticeAt >= 0f && now >= _noticeAt)
            {
                _noticeAt = -1f;
                try { DirectorPlugin.L.LogInfo("notice to the other players: " + TryOuts.Notice(raid)); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the notice to the other players could not be shown: " + e.Message); }
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
                    if (Special(m.Plan)) { Settle(m, CommandLedger.Unconfirmed, "the target and gun trucks are only made at the start; the change applies to the next start"); continue; }   // the target person is never re-made: it would come back as an ordinary enemy
                    BrainAI b = m.Bot == IntPtr.Zero ? null : Find(gm, m.Bot);
                    if (b == null) { Settle(m, CommandLedger.Applied, "its enemy is no longer alive; the change applies when it is made"); continue; }    // not alive: the new facing applies when it spawns
                    if (!Destroy(b, "its marker was changed", Replay.KindRemade)) { Settle(m, CommandLedger.Failed, "its enemy could not be taken out to be made again"); continue; }
                    // The game will not spawn onto a spot that is still occupied, and the old enemy is only gone a moment later.
                    m.Bot = IntPtr.Zero; m.Waiting = true;
                    bool held = m.Plan.Trigger != 0 && !Engine.EverFired(m.Plan.Trigger);      // now bound to a zone nobody has reached: it waits there
                    m.RemakeAt = held ? -1f : now + 0.7f;
                    if (held) Settle(m, CommandLedger.Applied, "its enemy was taken out; it waits for its trigger now");
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
            foreach (Marker m in batch.Items) { m.Queued = false; Settle(m, CommandLedger.Failed, why); Landed(m, false, why); }
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
            foreach (Marker m in batch.Items)
            {
                m.Queued = false;
                if (m.Bot != IntPtr.Zero) { made++; FacingChecks.Add(new KeyValuePair<Marker, float>(m, now + 1.5f)); Settle(m, CommandLedger.Applied, "its enemy exists"); Landed(m, true, ""); }
                else
                {
                    Settle(m, CommandLedger.Failed, "the game did not make its enemy");
                    Landed(m, false, "the game did not make the enemy at (" + m.Plan.Pos.X.ToString("0.#", Inv) + ", " + m.Plan.Pos.Z.ToString("0.#", Inv) + ")");
                }
            }
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
                if (i != _aiming && _markers[i].Waiting && !_markers[i].Queued && !Special(_markers[i].Plan) && (_markers[i].Plan.Trigger == 0 || Engine.EverFired(_markers[i].Plan.Trigger))) list.Add(_markers[i]);
            if (list.Count == 0) { Say("no waiting enemies"); return; }
            Enqueue(list);
            Say("spawning " + list.Count + " waiting enemies");
        }

        private static bool Destroy(BrainAI b, string why = "removed by the Game Master", string kind = Replay.KindRemoved)
        {
            try { Replay.Removed(b.Pointer, kind, why); Mirror.NetworkServer.Destroy(b.gameObject); return true; }
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

        /// <summary>
        /// Is a living player inside the zone? Membership is the one rule for this, the page and the rehearsal: the painted area
        /// when there is one (bounds prepared when the list was read), else the old circle, where the floor above or below does
        /// not count. where: the first player found inside.
        /// </summary>
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
                if (Membership.Inside(z, new V3(p.x, p.y, p.z))) { where = p; return true; }
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

        // ------------------------------------------------------------------ director budget
        // The scenario's Game Master budget (ML-025). Every way of asking for an action (page, cards, any later UI) comes through
        // /api/command {"cmd":"action","id":"rear"} and so through ActionRequest: the budget says whether it may be used, pays
        // for it, and only then is the placement checked and the trigger fired. Whatever is not fired in the end is refunded.
        // Each request, spend, refund, wait, cancel and firing is a replay event of kind "budget", so a debrief can explain it.
        private sealed class ActionWait { public ActionPlan Action; public string Key = "", Command = ""; public float Since, CheckAt; }
        private sealed class ActionFired { public ActionPlan Action; public string Key = "", Command = ""; public float Since; }
        private static Budget _budget;                                                   // null: the scenario has no director block
        private static readonly List<ActionWait> ActionWaits = new List<ActionWait>();   // paid for, waiting for a safe moment
        private static readonly List<ActionFired> ActionsFired = new List<ActionFired>(); // fired, waiting to hear what the trigger's actions came to
        private static int _actionKeys;                                                  // ids for requests that came without a command id
        private static bool _budgetWarned;                                               // said once: the state is asked for several times a second
        private const float ActionWaitLimit = 30f, ActionAnswerLimit = 30f;
        // The host's choice for this game session, kept from mission to mission: false (the default) = the budget rules apply
        // whenever the plan has a director block; true = free directing, cards fire without paying and plain firing is allowed.
        private static bool _budgetFree;

        // Seconds since the mission was laid out: the budget's clock and the "at" of each spend.
        private static float BudgetClock => Time.unscaledTime - Director.HandledAt;

        private static void BeginBudget(MissionPlan plan)
        {
            _budget = plan != null && plan.Director != null ? new Budget(plan.Director, ActionTriggerWhy) : null;
            if (_budget != null) DirectorPlugin.L.LogInfo("director budget: " + _budget.Initial + " points, " + _budget.Regen + " a minute, " + _budget.Actions.Count + " action(s)");
            if (_budget != null && _budgetFree) BudgetEvent("budget mode off: the host chose free directing for this session; cards fire without spending");
        }

        private static void ResetBudget()
        {
            // Whatever still waits ends with the mission; the budget goes with it, so there is nothing to refund into.
            foreach (ActionWait w in ActionWaits) SettleAction(w.Command, w.Action.Id, CommandLedger.Failed, "cancelled: the mission ended");
            foreach (ActionFired f in ActionsFired) SettleAction(f.Command, f.Action.Id, CommandLedger.Failed, "the mission ended before the game answered");
            ActionWaits.Clear(); ActionsFired.Clear();
            if (_budget != null) _budget.Reset();
            _budget = null;
        }

        /// <summary>For the budget: "" when the trigger can be fired by hand now, else why not.</summary>
        private static string ActionTriggerWhy(int id)
        {
            if (!_zones.Exists(z => z.Id == id) || !Engine.Known(id)) return "its trigger " + id.ToString(Inv) + " does not exist";
            TriggerState s = Engine.State(id);
            if (s == null || !s.Latched) return "";
            return s.RearmAt != null ? "its trigger " + id.ToString(Inv) + " fires again by itself in " + Math.Max(0, (int)Math.Ceiling(s.RearmAt.Value - Time.unscaledTime)).ToString(Inv) + " s"
                                     : "its trigger " + id.ToString(Inv) + " has already fired";
        }

        private static void BudgetEvent(string text) { try { Replay.Event("budget", text); } catch { } DirectorPlugin.L.LogInfo("director budget: " + text); }

        private static string PointsNow() => _budget == null ? "0" : ((float)Math.Round(_budget.Points, 2)).ToString(Inv);

        private static void SettleAction(string command, string actionId, string status, string said)
        {
            if (string.IsNullOrEmpty(command)) return;
            try { LiveLink.Ledger.Settle(command, actionId, status, said); }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the result of an action could not be updated: " + e.Message); }
        }

        /// <summary>
        /// "" when the Game Master may fire this trigger with the plain fire command; else why not. Under budget rules a trigger
        /// that some action fires is fired only through its card, so the price, rest and safe-placement checks cannot be skipped.
        /// </summary>
        private static string CardGuard(int triggerId)
        {
            if (_budget == null || _budgetFree) return "";
            return _budget.Actions.Exists(a => a.Fire == triggerId) ? "this trigger is an action card: use the card" : "";
        }

        /// <summary>{"cmd":"budgetMode","on":true|false}: the host turns the budget rules on or off for the rest of the session.</summary>
        private static void BudgetModeRequest(string json, Outcome done)
        {
            bool? on = null;
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
                    if (doc.RootElement.TryGetProperty("on", out var ov) && (ov.ValueKind == System.Text.Json.JsonValueKind.True || ov.ValueKind == System.Text.Json.JsonValueKind.False))
                        on = ov.GetBoolean();
            }
            catch { on = null; }
            if (on == null) { done.Add(new CommandOp("budgetMode", "", -1, CommandLedger.Failed, "say \"on\": true or false")); return; }
            bool changed = _budgetFree == on.Value;
            _budgetFree = !on.Value;
            if (changed) BudgetEvent(on.Value ? "budget mode on: cards cost points again and their triggers are fired only through the cards" : "budget mode off");
            string said = on.Value ? "budget rules apply" : "free directing: cards fire without spending";
            if (_budget == null) said += " (this mission has no Game Master budget; the choice holds for later missions)";
            done.Add(new CommandOp("budgetMode", on.Value ? "on" : "off", -1, CommandLedger.Applied, said));
        }

        /// <summary>
        /// One request for an action: Can, Spend, the placement check, then fire, wait or cancel. The op answers applied (fired
        /// and its effect seen), pending (waiting for a safe moment or for the game's answer), failed (refused or refunded), or
        /// failed with "cancelled: ..." when no safe moment came; the cards recognise that prefix. With the budget rules off
        /// (budgetMode) nothing is paid and no rest applies; the trigger check and the placement rule still do.
        /// </summary>
        private static void ActionRequest(string json, string command, Outcome done)
        {
            string actionId = "";
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
                    if (doc.RootElement.TryGetProperty("id", out var iv))
                        actionId = iv.ValueKind == System.Text.Json.JsonValueKind.String ? iv.GetString() ?? "" : iv.ValueKind == System.Text.Json.JsonValueKind.Number ? iv.GetRawText() : "";
            }
            catch { actionId = ""; }
            BudgetEvent("request '" + actionId + "'" + (string.IsNullOrEmpty(command) ? "" : " (command " + command + ")"));
            if (_budget == null) { done.Add(new CommandOp("action", actionId, -1, CommandLedger.Failed, "this mission has no Game Master budget")); return; }
            // Without a command id from the page the spend gets one of its own, so a refund can still find it; only a retry with the page's id is recognised.
            string key = string.IsNullOrEmpty(command) ? "local-" + (++_actionKeys).ToString(Inv) : command;
            if (_budget.Has(key))
            {
                bool back = _budget.Refunded(key);
                done.Add(new CommandOp("action", actionId, -1, back ? CommandLedger.Failed : CommandLedger.Applied, back ? "this command was refunded; a new request is needed" : "already paid for by this command; not paid or fired again"));
                return;
            }
            if (_budgetFree)
            {
                // Free directing: nothing is paid and no rest applies, but the trigger and the safe-placement rule still do,
                // because those protect the players rather than the budget. Refunds below find nothing to give back.
                if (!_budget.CanFree(actionId, out string freeWhy))
                {
                    BudgetEvent("refused '" + actionId + "': " + freeWhy);
                    done.Add(new CommandOp("action", actionId, -1, CommandLedger.Failed, freeWhy));
                    return;
                }
                BudgetEvent("free '" + actionId + "': budget mode off, nothing paid");
            }
            else if (!_budget.Spend(actionId, key, BudgetClock, out string why))
            {
                BudgetEvent("refused '" + actionId + "': " + why);
                done.Add(new CommandOp("action", actionId, -1, CommandLedger.Failed, why));
                return;
            }
            ActionPlan a = _budget.Find(actionId);
            if (!_budgetFree) BudgetEvent("spend '" + a.Id + "' (" + a.Name + "): " + a.Cost.ToString(Inv) + " point(s), " + PointsNow() + " left");
            string risky = ActionUnsafe(a);
            if (risky.Length == 0) { done.Add(FireAction(a, key, command)); return; }
            if (a.Fallback == ActionPlan.Cancel)
            {
                done.Add(new CommandOp("action", a.Id, -1, CommandLedger.Failed, CancelAction(a, key, risky)));
                return;
            }
            float now = Time.unscaledTime;
            ActionWaits.Add(new ActionWait { Action = a, Key = key, Command = command ?? "", Since = now, CheckAt = now + 1f });
            BudgetEvent("wait '" + a.Id + "': " + risky + "; checked again every second for up to " + ActionWaitLimit.ToString("0", Inv) + " s");
            done.Add(new CommandOp("action", a.Id, -1, CommandLedger.Pending, "waiting: " + risky));
        }

        // Refunds and says so; the text is the op's "said".
        private static string CancelAction(ActionPlan a, string key, string why)
        {
            bool back = _budget != null && _budget.Refund(key);
            BudgetEvent("cancel '" + a.Id + "': " + why);
            if (back) BudgetEvent("refund '" + a.Id + "': " + a.Cost.ToString(Inv) + " point(s), " + PointsNow() + " now");
            return "cancelled: " + why + (back ? "; " + a.Cost.ToString(Inv) + " point(s) refunded" : "");
        }

        /// <summary>Fires the action's trigger. A firing the engine refuses is refunded at once; one that fired waits for what its actions came to.</summary>
        private static CommandOp FireAction(ActionPlan a, string key, string command)
        {
            int before = Engine.Fires(a.Fire), saidBefore = SaidCount;
            try { FireNow(a.Fire); }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the action '" + a.Id + "' could not fire its trigger: " + e.Message); }
            if (Engine.Fires(a.Fire) <= before)
            {
                string why = SaidCount != saidBefore ? LastSaid : "its trigger " + a.Fire.ToString(Inv) + " did not fire";
                bool back = _budget != null && _budget.Refund(key);
                BudgetEvent("refund '" + a.Id + "': the trigger was not fired (" + why + ")");
                return new CommandOp("action", a.Id, -1, CommandLedger.Failed, why + (back ? "; " + a.Cost.ToString(Inv) + " point(s) refunded" : ""));
            }
            BudgetEvent("fire '" + a.Id + "': trigger " + a.Fire.ToString(Inv));
            var fired = new ActionFired { Action = a, Key = key, Command = command ?? "", Since = Time.unscaledTime };
            if (ActionAnswered(fired, out string status, out string said)) return new CommandOp("action", a.Id, -1, status, said);
            ActionsFired.Add(fired);
            return new CommandOp("action", a.Id, -1, CommandLedger.Pending, "trigger " + a.Fire.ToString(Inv) + " fired; waiting to see what it brings");
        }

        /// <summary>
        /// What the fired trigger's actions came to, once the engine has been told. Failed, or no answer within ActionAnswerLimit,
        /// is refunded: the points pay for an effect, not for asking. Unconfirmed (the call went through, the effect cannot be
        /// seen, such as a door or a message) stays paid, because otherwise an action made only of those would always be free.
        /// </summary>
        private static bool ActionAnswered(ActionFired f, out string status, out string said)
        {
            status = ""; said = "";
            TriggerState s = Engine.State(f.Action.Fire);
            string outcome = s != null ? s.Outcome ?? "" : "";
            if (outcome == TriggerEngine.Applied) { status = CommandLedger.Applied; said = s.OutcomeWhy; }
            else if (outcome == TriggerEngine.Unconfirmed) { status = CommandLedger.Unconfirmed; said = s.OutcomeWhy; }
            else if (outcome == TriggerEngine.Failed || Time.unscaledTime - f.Since > ActionAnswerLimit)
            {
                string why = outcome == TriggerEngine.Failed ? s.OutcomeWhy : "the game did not answer within " + ActionAnswerLimit.ToString("0", Inv) + " s";
                bool back = _budget != null && _budget.Refund(f.Key);
                BudgetEvent("refund '" + f.Action.Id + "': " + why);
                status = CommandLedger.Failed; said = why + (back ? "; " + f.Action.Cost.ToString(Inv) + " point(s) refunded" : "");
            }
            else return false;
            if (status != CommandLedger.Failed) BudgetEvent("done '" + f.Action.Id + "': " + status + (said.Length > 0 ? " (" + said + ")" : ""));
            return true;
        }

        /// <summary>Every frame: actions waiting for a safe moment are looked at once a second; fired ones until the game has answered.</summary>
        private static void BudgetTick(float now)
        {
            for (int i = ActionsFired.Count - 1; i >= 0; i--)
            {
                ActionFired f = ActionsFired[i];
                if (!ActionAnswered(f, out string status, out string said)) continue;
                ActionsFired.RemoveAt(i);
                SettleAction(f.Command, f.Action.Id, status, said);
            }
            for (int i = 0; i < ActionWaits.Count; i++)
            {
                ActionWait w = ActionWaits[i];
                if (now < w.CheckAt) continue;
                w.CheckAt = now + 1f;
                string risky = ActionUnsafe(w.Action);
                if (risky.Length > 0 && now - w.Since < ActionWaitLimit) continue;
                ActionWaits.RemoveAt(i--);
                if (risky.Length > 0) { SettleAction(w.Command, w.Action.Id, CommandLedger.Failed, CancelAction(w.Action, w.Key, "no safe moment within " + ActionWaitLimit.ToString("0", Inv) + " s (" + risky + ")")); continue; }
                // Safe now: the budget is asked again about the trigger only (it was paid for already), then it fires like a direct request.
                string blocked = ActionTriggerWhy(w.Action.Fire);
                if (blocked.Length > 0) { SettleAction(w.Command, w.Action.Id, CommandLedger.Failed, CancelAction(w.Action, w.Key, blocked)); continue; }
                CommandOp op = FireAction(w.Action, w.Key, w.Command);
                if (op.Status != CommandLedger.Pending) SettleAction(w.Command, w.Action.Id, op.Status, op.Said);
            }
        }

        /// <summary>
        /// Whether firing the action now would be unsafe: "" when not. The points checked are the trigger's place (zone and hold
        /// only) and where each of its waiting enemies would appear; see Budget.Placement. The sight test is a ray from each
        /// player's eye height to 1 m above the point, started half a metre out so it does not hit the player's own body. It
        /// is NOT a proven visibility test (the game's own sight checks were not found): anything in a player's view cone the
        /// ray reaches counts as seen. If the players cannot be read at all, the action is treated as unsafe.
        /// </summary>
        private static string ActionUnsafe(ActionPlan a)
        {
            if (a.MinDistance <= 0f && !a.Unseen) return "";
            try
            {
                TriggerPlan t = _zones.Find(z => z.Id == a.Fire);
                var points = new List<V3>();
                if (_markers != null)
                    foreach (Marker m in _markers) if (m.Plan.Trigger == a.Fire && m.Waiting && !m.Queued && !Special(m.Plan)) points.Add(m.Plan.Pos);
                var where = new List<V3>(); var yaws = new List<float>();
                var players = PlayerMaster.AllPlayerMasters;
                for (int i = 0; players != null && i < players.Count; i++)
                {
                    PlayerNetworking body = players[i] == null ? null : players[i].PlayerSpawnedObject;
                    if (body == null) continue;
                    Vector3 p = body.transform.position;
                    where.Add(new V3(p.x, p.y, p.z));
                    float yaw = float.NaN;
                    try { yaw = PlayerYaw(players[i], body); } catch { }
                    yaws.Add(yaw);
                }
                return Budget.Placement(a, t, points, where, yaws, ClearSight);
            }
            catch (Exception e) { return "the players could not be read (" + e.Message + ")"; }
        }

        private static bool ClearSight(V3 feet, V3 point)
        {
            var eye = new Vector3(feet.X, feet.Y + 1.6f, feet.Z);
            var to = new Vector3(point.X, point.Y + 1f, point.Z);
            Vector3 dir = to - eye; float dist = dir.magnitude;
            if (dist < 1f) return true;
            dir /= dist;
            try { return !Physics.Raycast(eye + dir * 0.5f, dir, out RaycastHit _, dist - 1f, ~0, QueryTriggerInteraction.Ignore); }
            catch { return true; }     // no answer: counts as seen
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
        private static string GeoFile(string nav) => Path.Combine(DirectorPlugin.BaseDir, "maps", Path.GetFileNameWithoutExtension(nav) + ".mlgeo");

        /// <summary>
        /// The state answer with what became of each part of the change, after the last state field:
        /// "result":{"command":"c1","status":"partial","ops":[{"op":"move","ref":"12","at":3,"status":"failed","said":"why"}],"left":0}
        /// status of the whole: applied (every part), failed (every part), partial (some failed), pending (some still on their
        /// way into the game), unconfirmed (asked of the game, the effect cannot be seen). The parts go to the ledger too, so
        /// GET /api/command/c1 tells a reloaded page, and queued spawns update them when they are made or not.
        /// </summary>
        private static string WithResult(string state, string command, Outcome done)
        {
            string json = state.Substring(0, state.Length - 1) + ",\"result\":{\"command\":" + Q(command ?? "") + ",\"status\":\"" + done.Overall(CommandLedger.Applied) + "\"," + done.Json() + "}}";
            try { LiveLink.Ledger.Record(command, done); } catch (Exception e) { DirectorPlugin.L.LogWarning("the result of a browser change could not be kept: " + e.Message); }
            return json;
        }

        /// <summary>This marker's enemy is now what the given change waits for. One it waited for before has to make do with the later answer.</summary>
        private static void Follow(Marker m, string command)
        {
            if (m.Command.Length > 0 && m.Command != command) Settle(m, CommandLedger.Unconfirmed, "changed again before its enemy was made; the later change says what became of it");
            m.Command = command ?? "";
        }

        /// <summary>The change waiting on this marker hears what became of it.</summary>
        private static void Settle(Marker m, string status, string said)
        {
            if (m.Command.Length == 0) return;
            string command = m.Command; m.Command = "";
            try { LiveLink.Ledger.Settle(command, m.Id.ToString(Inv), status, said); }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the result of a browser change could not be updated: " + e.Message); }
        }

        private static bool _explainWarned;     // said once: the state is asked for several times a second

        /// <summary>
        /// One trigger's explanation for the page's inspector:
        /// {"phase":"cooling","left":12.5,"why":"Fired 2 of 3 times; armed again in 13 s","fires":2,"times":3,"waiting":[],"excluded":[],
        ///  "inside":-1,"alive":-1,"outcome":"unconfirmed","outcomeWhy":"asked of the game, not seen to take: a message"}
        /// inside and alive are -1 where they were not asked (not a zone or hold); outcome is "" until the runtime knows.
        /// </summary>
        private static string ExplainJson(TriggerView v)
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append("{\"phase\":\"").Append(Guard.Esc(v.Phase)).Append("\",\"left\":").Append(F(v.Left)).Append(",\"why\":\"").Append(Guard.Esc(v.Why))
              .Append("\",\"fires\":").Append(v.Fires.ToString(Inv)).Append(",\"times\":").Append(v.Times.ToString(Inv)).Append(",\"waiting\":[");
            for (int k = 0; k < v.Waiting.Count; k++) sb.Append(k == 0 ? "" : ",").Append(v.Waiting[k].ToString(Inv));
            sb.Append("],\"excluded\":[");
            for (int k = 0; k < v.Excluded.Count; k++) sb.Append(k == 0 ? "" : ",").Append(v.Excluded[k].ToString(Inv));
            sb.Append("],\"inside\":").Append(v.Inside.ToString(Inv)).Append(",\"alive\":").Append(v.Alive.ToString(Inv))
              .Append(",\"outcome\":\"").Append(Guard.Esc(v.Outcome)).Append("\",\"outcomeWhy\":\"").Append(Guard.Esc(v.OutcomeWhy)).Append("\"}");
            return sb.ToString();
        }

        /// <summary>Everything the browser shows: players, enemies where they really are, and the markers.</summary>
        internal static string StateJson()
        {
            string page = ",\"page\":" + Q(LiveLink.PageStamp) + "," + Picker.Json() + Standing();
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
            { MissionPlan cp = Director.CurrentPlan; if (cp != null && !string.IsNullOrEmpty(cp.Id)) sb.Append(",\"scenarioId\":").Append(Q(cp.Id)).Append(",\"revision\":").Append(cp.Revision.ToString(Inv)); }   // the page files revisions under it (ML-014)
            { MissionPlan cp = Director.CurrentPlan; if (cp != null && cp.Sealed) sb.Append(",\"sealed\":true"); }   // the page hides placements and outcomes until the host reveals them (ML-021)
            sb.Append(",\"hasGeo\":").Append(nav != null && File.Exists(GeoFile(nav)) ? "true" : "false");
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
            foreach (int id in Engine.FiredIds()) { sb.Append(first ? "" : ",").Append(id); first = false; }
            rev.Append(MissionPlan.TriggersJson(_zones));
            // Where every trigger stands right now and why (ML-015), so the page can say why one waits and a reloaded page shows
            // the same: everything comes from the engine, nothing from the page. Explain only reads, and only asks the game how
            // many players stand in a zone, so it is cheap enough for every poll.
            sb.Append("],\"triggerState\":[");
            float stateNow = Time.unscaledTime;
            Engine.Start = Director.HandledAt;
            for (int i = 0; i < _zones.Count; i++)
            {
                string phase = Engine.Phase(_zones[i], stateNow, out float left);
                sb.Append(i == 0 ? "" : ",").Append("{\"id\":").Append(_zones[i].Id).Append(",\"phase\":\"").Append(phase).Append("\",\"fires\":").Append(Engine.Fires(_zones[i].Id))
                  .Append(",\"left\":").Append(left.ToString("0.#", Inv));
                try { string json = ExplainJson(Engine.Explain(_zones[i], stateNow, Asks)); sb.Append(",\"explain\":").Append(json); }
                catch (Exception e) { if (!_explainWarned) DirectorPlugin.L.LogWarning("a trigger could not be explained to the page: " + e.Message); _explainWarned = true; }
                sb.Append('}');
                rev.Append(_zones[i].Id).Append(phase[0]).Append(phase.Length).Append(Engine.Fires(_zones[i].Id));
            }
            sb.Append("],\"triggerProblems\":[");
            for (int i = 0; i < Engine.Problems.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Q(Engine.Problems[i]));
            sb.Append("],\"types\":[");
            var types = Types(gm);
            for (int i = 1; i < types.Count; i++) sb.Append(i > 1 ? "," : "").Append(Q(types[i]));
            sb.Append("],\"infilPoints\":[");
            try { List<V3> pts = Director.CurrentInfils; for (int i = 0; pts != null && i < pts.Count; i++) sb.Append(i > 0 ? "," : "").Append('[').Append(F(pts[i].X)).Append(',').Append(F(pts[i].Y)).Append(',').Append(F(pts[i].Z)).Append(']'); } catch { }
            var marks = new List<ThingPlan>(); foreach (LiveThing lt in _things) { marks.Add(lt.Plan); rev.Append('T').Append(lt.Plan.Key); }
            sb.Append("],\"things\":").Append(TryOuts.ThingsJson()).Append(",\"thingMarks\":").Append(MissionPlan.ThingsJson(marks)).Append(",\"tried\":").Append(Q(TryOuts.Last));
            if (_budget != null)
                try { string budget = _budget.Json(BudgetClock, !_budgetFree); sb.Append(",\"budget\":").Append(budget); }
                catch (Exception e) { if (!_budgetWarned) DirectorPlugin.L.LogWarning("the director budget could not be shown to the page: " + e.Message); _budgetWarned = true; }
            sb.Append(",\"vehicleTypes\":[");
            try { List<GameObject> trucks = Director.VehiclePrefabs(raid); for (int i = 0; i < trucks.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(trucks[i].name)); } catch { }
            // The page takes the marker list over only when this changes.
            sb.Append("],\"rev\":").Append(Q(rev.ToString().GetHashCode().ToString("x") + "-" + _markers.Count)).Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// The page sends its whole enemy list. New entries become markers (spawned at once if asked),
        /// missing ones are removed with their enemy, and changed ones are updated; a live enemy whose
        /// marker changed is taken out and made again. Each marker added, changed or removed gets a result of its own
        /// (see WithResult); a spawn stays pending until its enemy exists or the game gives up.
        /// </summary>
        internal static string Reconcile(string json, string command)
        {
            if (!MissionRunning(out RaidManager raid, out GameManager gm)) return "{\"mission\":false}";
            EnsureMarkers();
            var done = new Outcome();
            var ids = new List<int>(); bool spawnNow; MissionPlan sent; MissionPlan settings = null;
            Dictionary<int, string> refusedAreas;
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
                refusedAreas = AreaEdits.Refused(zs);       // checked on the text as sent, with the file rules, before anything is taken over
                string itemsText = root.TryGetProperty("objectives", out var os) && os.ValueKind == System.Text.Json.JsonValueKind.Array ? os.GetRawText() : MissionPlan.ObjectivesJson(_wanted);
                string doorsText = root.TryGetProperty("doors", out var ds) && ds.ValueKind == System.Text.Json.JsonValueKind.Array ? ds.GetRawText() : MissionPlan.DoorsJson(_doorPlans);
                var thingsNow = new List<ThingPlan>(); foreach (LiveThing lt in _things) thingsNow.Add(lt.Plan);
                string thingsText = root.TryGetProperty("things", out var ths) && ths.ValueKind == System.Text.Json.JsonValueKind.Array ? ths.GetRawText() : MissionPlan.ThingsJson(thingsNow);
                sent = MissionPlan.FromJson("{\"triggers\":" + zonesText + ",\"objectives\":" + itemsText + ",\"doors\":" + doorsText + ",\"things\":" + thingsText + ",\"spawns\":" + list.GetRawText() + "}");
            }
            if (sent.Spawns.Count != ids.Count)
            {
                done.Add(new CommandOp("markers", "", -1, CommandLedger.Failed, "the enemy list could not be read as sent; nothing was changed"));
                return WithResult(StateJson(), command, done);
            }

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
                        string kind = m.Plan.Pos.X != s.Pos.X || m.Plan.Pos.Y != s.Pos.Y || m.Plan.Pos.Z != s.Pos.Z ? "move" : "change";
                        string reference = m.Id.ToString(Inv);
                        bool there = m.Plan.Vehicle ? !m.Waiting : m.Bot != IntPtr.Zero;
                        if (kind == "change" && m.Plan.Authored.HasValue) s.Authored = m.Plan.Authored;   // not moved by hand: a scattered enemy keeps its authored place for saving
                        m.Plan = s;
                        if (target)
                            done.Add(there ? new CommandOp(kind, reference, i, CommandLedger.Unconfirmed, "kept for the next start; the target and gun trucks in the game stay as they are")
                                           : new CommandOp(kind, reference, i, CommandLedger.Applied, "the target and gun trucks are made at the start only"));
                        else if (m.Bot != IntPtr.Zero)
                        {
                            // Its live enemy is taken out and made again in a moment; that remake gives the answer.
                            m.TurnedAt = now; Follow(m, command);
                            done.Add(new CommandOp(kind, reference, i, CommandLedger.Pending, "its enemy is made again with the change"));
                        }
                        else if (released && spawnNow)
                        {
                            fresh.Add(m); Follow(m, command);
                            done.Add(new CommandOp(kind, reference, i, CommandLedger.Pending, "no longer bound to a trigger: its enemy is on its way"));
                        }
                        else if (m.Queued)
                        {
                            Follow(m, command);
                            done.Add(new CommandOp(kind, reference, i, CommandLedger.Pending, "its enemy is on its way into the game"));
                        }
                        else done.Add(new CommandOp(kind, reference, i, CommandLedger.Applied, m.Plan.Trigger != 0 ? "waits for trigger " + m.Plan.Trigger.ToString(Inv) : ""));
                    }
                    result.Add(m);
                }
                else
                {
                    string where = "";
                    if (!s.Fixed && !OnWalkable(new Vector3(s.Pos.X, s.Pos.Y, s.Pos.Z))) { s.Fixed = true; s.Role = SpawnRole.Guard; s.Patrol.Clear(); where = "not on walkable ground, so it became a fixed sentry; "; }
                    var created = new Marker { Plan = s, Waiting = true };
                    result.Add(created);
                    string reference = created.Id.ToString(Inv);
                    if (spawnNow && s.Trigger == 0 && !Special(s))       // a zone enemy waits for its zone; a target for the next start
                    {
                        fresh.Add(created); Follow(created, command);
                        done.Add(new CommandOp("add", reference, i, CommandLedger.Pending, where + "its enemy is on its way"));
                    }
                    else done.Add(new CommandOp("add", reference, i, CommandLedger.Applied, where + (s.Trigger != 0 ? "waits for trigger " + s.Trigger.ToString(Inv) : Special(s) ? "made at the next start" : "placed; not spawned")));
                }
            }
            foreach (Marker gone in byId.Values)
            {
                string reference = gone.Id.ToString(Inv);
                if (gone.Queued)
                {
                    // On its way into the game: kept for now, or its enemy would appear with no marker to remove it by.
                    result.Add(gone);
                    done.Add(new CommandOp("remove", reference, -1, CommandLedger.Failed, "it is being spawned; remove it again once its enemy is there"));
                    continue;
                }
                Settle(gone, CommandLedger.Failed, "removed before its enemy was made");
                BrainAI b = gone.Bot == IntPtr.Zero ? null : Find(gm, gone.Bot);
                if (b == null) done.Add(new CommandOp("remove", reference, -1, CommandLedger.Applied, ""));
                else if (Destroy(b)) done.Add(new CommandOp("remove", reference, -1, CommandLedger.Unconfirmed, "its enemy was told to go; the game removes it a moment later"));
                else done.Add(new CommandOp("remove", reference, -1, CommandLedger.Failed, "the marker is gone, but its enemy could not be removed"));
            }
            _markers = result; _aiming = -1; _current = -1;
            // A timer made during the mission counts from now; one that was taken away forgets what it saw.
            foreach (TriggerPlan nt in sent.Triggers) { TriggerPlan was = _zones.Find(z => z.Id == nt.Id); if (was != null) nt.Off = was.Off; }   // a trigger that lost its roll stays out
            // A painted area is one change: a shape that breaks the rules is refused whole and its trigger keeps the place it had,
            // so the engine sees no move and its progress stands. Every area kept has its bounds worked out here, not every frame.
            foreach (KeyValuePair<int, string> no in AreaEdits.Apply(sent.Triggers, _zones, refusedAreas))
            {
                Say("trigger " + no.Key.ToString(Inv) + ": " + no.Value);
                done.Add(new CommandOp("triggers", no.Key.ToString(Inv), -1, CommandLedger.Failed, no.Value));
            }
            _zones = sent.Triggers;
            var knownProblems = new HashSet<string>(Engine.Problems);
            Engine.Sync(_zones, now);
            foreach (string problem in Engine.Problems)
            {
                Say("triggers: " + problem);
                if (!knownProblems.Contains(problem)) done.Add(new CommandOp("triggers", "", -1, CommandLedger.Failed, problem));     // new with this change: it will not fire as meant
            }
            _wanted = sent.Objectives; PlaceObjectives(raid);
            if (settings != null)
            {
                // The insertion is for the next start; weather applies now; the extraction rule applies from now on.
                _insertion = settings.Insertion ?? "";
                bool weatherChanged = MissionPlan.SettingsJson(new MissionPlan { Weather = settings.Weather }) != MissionPlan.SettingsJson(new MissionPlan { Weather = _weather });
                _weather = settings.Weather;
                if (!string.IsNullOrEmpty(settings.Title)) _title = settings.Title;
                _briefing = new List<string>(settings.Briefing);
                Wave.After = settings.RespawnAfter; Wave.Times = settings.RespawnTimes;
                _skillAll = settings.Skill ?? ""; _nvgAll = settings.Nvg; _needsGm = settings.GameMaster;          // for enemies that appear from now on
                if ((settings.Power ?? "") != _power)
                {
                    _power = settings.Power ?? "";
                    // The switches are asked to change; whether every light follows is not seen from here.
                    if (_power != "")
                        try { string lit = TryOuts.Power(_power == "on"); Tell("lights: " + lit); done.Add(new CommandOp("lights", _power, -1, CommandLedger.Unconfirmed, lit)); }
                        catch (Exception e) { DirectorPlugin.L.LogWarning("the lights could not be set: " + e.Message); done.Add(new CommandOp("lights", _power, -1, CommandLedger.Failed, e.Message)); }
                }
                if (weatherChanged)
                {
                    try { string w = ApplyWeather(); if (w != null) { Tell("weather: " + w); done.Add(new CommandOp("weather", "", -1, CommandLedger.Unconfirmed, w)); } }
                    catch (Exception e) { DirectorPlugin.L.LogWarning("weather could not be set: " + e.Message); done.Add(new CommandOp("weather", "", -1, CommandLedger.Failed, e.Message)); }
                }
                int holdNow = settings.ExtractionAfter != 0 && sent.Triggers.Exists(t => t.Id == settings.ExtractionAfter) ? settings.ExtractionAfter : 0;
                if (holdNow != _holdAfter)
                {
                    _holdAfter = holdNow;
                    bool hold = holdNow != 0 && !Engine.EverFired(holdNow);
                    if (hold) { HoldExtraction = true; DirectorPlugin.PatchExtraction(); }
                    else if (HoldExtraction) { HoldExtraction = false; if (ExtractionAsked) OpenExtraction(raid, "nothing holds it back any more"); }
                }
            }
            List<DoorPlan> doorsBefore = _doorPlans;
            bool doorsChanged = MissionPlan.DoorsJson(doorsBefore) != MissionPlan.DoorsJson(sent.Doors);
            _doorPlans = sent.Doors;
            ReconcileThings(sent.Things);
            try { string swung = ApplyDoors(false, doorsBefore); if (doorsChanged) done.Add(new CommandOp("doors", "", -1, CommandLedger.Unconfirmed, swung)); }
            catch (Exception e) { DirectorPlugin.L.LogWarning("doors could not be set: " + e.Message); done.Add(new CommandOp("doors", "", -1, CommandLedger.Failed, e.Message)); }
            if (fresh.Count > 0) Enqueue(fresh);
            return WithResult(StateJson(), command, done);
        }

        internal static string Command(string json, string command)
        {
            if (!MissionRunning(out RaidManager raid, out GameManager gm)) return "{\"mission\":false}";
            EnsureMarkers();
            string cmd; int id = 0; string what = "", arg = "";
            string asked = ""; bool hasPos = false; Vector3 pos = Vector3.zero; var ids = new List<int>();
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                cmd = root.GetProperty("cmd").GetString();
                if (root.TryGetProperty("id", out var idv) && idv.ValueKind == System.Text.Json.JsonValueKind.Number) id = idv.GetInt32();
                if (root.TryGetProperty("ids", out var iv) && iv.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var one in iv.EnumerateArray()) if (one.ValueKind == System.Text.Json.JsonValueKind.Number) ids.Add(one.GetInt32());
                if (root.TryGetProperty("what", out var wv) && wv.ValueKind == System.Text.Json.JsonValueKind.String) what = wv.GetString();
                if (root.TryGetProperty("arg", out var av) && av.ValueKind == System.Text.Json.JsonValueKind.String) arg = av.GetString();
                if (root.TryGetProperty("pos", out var pv) && pv.ValueKind == System.Text.Json.JsonValueKind.Array && pv.GetArrayLength() == 3)
                {
                    pos = new Vector3(pv[0].GetSingle(), pv[1].GetSingle(), pv[2].GetSingle()); hasPos = true;
                }
            }
            asked = cmd + (what != "" ? " " + what : "") + (id != 0 ? " " + id : "");
            Replay.Event("command", "the Game Master asked: " + asked);          // what was asked; what came of it is recorded by whoever did it
            var done = new Outcome();
            int saidBefore = SaidCount;
            if (cmd == "order" && hasPos)
            {
                // The Game Master sends the chosen enemies to a spot on the map. Each is told or not; whether it gets there is not followed.
                var who = new HashSet<IntPtr>();
                for (int i = 0; i < ids.Count; i++)
                {
                    string reference = ids[i].ToString(Inv);
                    Marker m = _markers == null ? null : _markers.Find(x => x.Id == ids[i]);
                    BrainAI b = m == null || m.Bot == IntPtr.Zero ? null : Find(gm, m.Bot);
                    string why = m == null ? "there is no such marker" : b == null ? "its enemy is not alive" : "";
                    if (why == "")
                        try
                        {
                            AgentController legs = b.agent;
                            if (legs == null || !legs.entityExists) why = "it cannot walk";
                            else if (Posted(b)) why = "it holds a fixed post or sits in a vehicle";
                        }
                        catch (Exception e) { why = "it could not be asked: " + e.Message; }
                    if (why != "") { done.Add(new CommandOp("order", reference, i, CommandLedger.Failed, why)); continue; }
                    who.Add(m.Bot);
                    done.Add(new CommandOp("order", reference, i, CommandLedger.Unconfirmed, "told to walk there"));
                }
                int sent = who.Count == 0 ? 0 : TryOuts.WalkTo(gm, pos, 0f, who);
                TryOuts.Did("an order to walk");
                Replay.Event("note", "order: " + TryOuts.LastWalk.Words());
                DirectorPlugin.L.LogInfo("order: " + TryOuts.LastWalk.Words());
                Say(sent == 0 ? "no live enemy among those selected could be sent" : sent + (sent == 1 ? " enemy is" : " enemies are") + " on the way to that spot");
            }
            else if (cmd == "fire" && CardGuard(id).Length > 0)
                done.Add(new CommandOp("fire", id.ToString(Inv), -1, CommandLedger.Failed, CardGuard(id)));   // under budget rules an action's trigger is fired through its card only (ML-025)
            else if (cmd == "fire")
            {
                // Fired means the trigger engine counted the firing; the enemies it releases are spawned like any other.
                int before = Engine.Fires(id);
                FireNow(id);
                bool fired = Engine.Fires(id) > before;
                done.Add(new CommandOp("fire", id.ToString(Inv), -1, fired ? CommandLedger.Applied : CommandLedger.Failed, SaidCount != saidBefore ? LastSaid : ""));
            }
            else if (cmd == "action") ActionRequest(json, command, done);     // a director budget action (ML-025): paid, checked, then fired, waited or cancelled
            else if (cmd == "budgetMode") BudgetModeRequest(json, done);      // the host turns the budget rules on or off for the session (ML-025)
            else if (cmd == "try")
            {
                string said = TryOuts.Run(what, arg, hasPos, pos, raid, gm);
                Say(said);
                done.Add(new CommandOp("try", what, -1, CommandLedger.Unconfirmed, said));      // the game was asked; the sentence says what it answered
            }
            else if (cmd == "spawnWaiting")
            {
                var queuedBefore = new HashSet<Marker>();
                foreach (Marker m in _markers) if (m.Queued) queuedBefore.Add(m);
                SpawnWaiting();
                foreach (Marker m in _markers)
                {
                    if (!m.Queued || queuedBefore.Contains(m)) continue;
                    Follow(m, command);
                    done.Add(new CommandOp("spawn", m.Id.ToString(Inv), -1, CommandLedger.Pending, "its enemy is on its way"));
                }
                if (done.Ops.Count == 0) done.Add(new CommandOp("spawn", "", -1, CommandLedger.Failed, SaidCount != saidBefore ? LastSaid : "nothing was waiting"));
            }
            else if (cmd == "removeAllEnemies")
            {
                RemoveAllEnemies(gm);
                done.Add(new CommandOp("clear", "", -1, CommandLedger.Unconfirmed, SaidCount != saidBefore ? LastSaid : ""));   // the game removes them a moment later
            }
            else if (cmd == "save")
            {
                DateTime before = _savePath != null && File.Exists(_savePath) ? File.GetLastWriteTimeUtc(_savePath) : DateTime.MinValue;
                Save();
                bool written = _savePath != null && File.Exists(_savePath) && File.GetLastWriteTimeUtc(_savePath) > before;
                done.Add(new CommandOp("save", _savePath != null ? Path.GetFileName(_savePath) : "", -1, written ? CommandLedger.Applied : CommandLedger.Failed, SaidCount != saidBefore ? LastSaid : ""));
            }
            else if (cmd == "export")
            {
                string nav = LiveNavFile(), geo = nav != null ? GeoFile(nav) : null;
                DateTime before = geo != null && File.Exists(geo) ? File.GetLastWriteTimeUtc(geo) : DateTime.MinValue;
                ExportMap();
                bool written = geo != null && File.Exists(geo) && File.GetLastWriteTimeUtc(geo) > before;
                done.Add(new CommandOp("export", geo != null ? Path.GetFileName(geo) : "", -1, written ? CommandLedger.Applied : CommandLedger.Failed, SaidCount != saidBefore ? LastSaid : ""));
            }
            else
            {
                Say("unknown request from the browser: " + cmd);
                done.Add(new CommandOp(cmd, "", -1, CommandLedger.Failed, "unknown request"));
            }
            return WithResult(StateJson(), command, done);
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
            foreach (LiveThing lt in _things) plan.Things.Add(lt.Plan);
            plan.Doors.AddRange(Director.DoorsOut); plan.Things.AddRange(Director.ThingsOut);            // what lost its roll this time is still part of the scenario
            plan.Title = _title; plan.Briefing = new List<string>(_briefing);
            foreach (SpawnPlan s in Director.SatOut) { plan.Spawns.Add(s); plan.Groups = Math.Max(plan.Groups, s.Group + 1); }   // lost their roll this time; still part of the scenario
            plan.ExtractionAfter = _holdAfter; plan.Insertion = _insertion; plan.Weather = _weather; plan.Power = _power; plan.Skill = _skillAll; plan.Nvg = _nvgAll; plan.GameMaster = _needsGm; plan.RespawnAfter = Wave.After; plan.RespawnTimes = Wave.Times;
            MissionPlan began = Director.CurrentPlan;
            if (fromFile && began != null)
            {
                // What an option choice set for this run is saved as the author had it, unless the Game Master has changed it since.
                if (began.SkillAuthored != null && _skillAll == (began.Skill ?? "")) plan.Skill = began.SkillAuthored;
                if (began.RespawnTimesAuthored.HasValue && Wave.Times == began.RespawnTimes) plan.RespawnTimes = began.RespawnTimesAuthored.Value;
            }
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
                // Everything of the scenario the Game Master cannot change live (id, options, places, director and the rest) is
                // saved as the author wrote it, from the file this mission started with, and the revision goes up by one.
                if (fromFile) MissionPlan.CarryAuthored(MissionPlan.FromJson(Director.ScenarioText), plan);
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
                // A scattered enemy the Game Master did not move is saved where the author put it, not where this roll placed it:
                // its two positions are exchanged for the writing and exchanged back after it.
                List<SpawnPlan> scattered = plan.Spawns.FindAll(s => s.Authored.HasValue);
                void Exchange() { foreach (SpawnPlan s in scattered) { V3 here = s.Pos; s.Pos = s.Authored.Value; s.Authored = here; } }
                string text;
                Exchange();
                try { text = plan.ToJson(); }
                finally { Exchange(); }
                File.WriteAllText(_savePath, text);
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
