using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MissionLab
{
    /// <summary>
    /// Writes down where everybody was, twice a second, and what happened, so the mission can be played back on
    /// the map afterwards. The file lands in BepInEx/MissionLab/replays and stays on this computer.
    /// Every enemy row says who the enemy is, so a reader links rows by that and not by where they stood: "s4" is the
    /// scenario's spawns[4] (its place in the file, before the dice; for a generated layout, in the header's plan), "m12"
    /// a marker the Game Master placed, and a "v1" after either marks the gunner of gun truck 1. Nothing is written when
    /// it is not known. Enemy events carry the row id as their fifth value.
    /// </summary>
    internal static class Replay
    {
        // What an enemy event can say. "down" is written only when the game itself says the enemy is dead; one that leaves
        // the game's list for a reason the mod does not know is "gone", so a cleanup or a despawn never counts as a kill.
        internal const string KindDown = "down", KindGone = "gone", KindRemoved = "removed", KindRemade = "remade", KindAppears = "appears", KindEnd = "end";
        // 1: rows without identity, removals counted as down; 2: "removed" apart; 3: identity in every enemy row and enemy event, "gone" and "remade", session in the header.
        private const int Semantics = 3;
        private const float Step = 0.5f, MaxSeconds = 3600f, WhoWindow = 6f;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static StringBuilder _frames, _events;
        private static int _frameCount, _eventCount, _nextId, _lastTaken;
        private static float _startedAt, _nextSampleAt, _nextFlushAt;
        private static string _path, _head;
        private static bool _on;

        private sealed class Tracked
        {
            public IntPtr Ptr;
            public BrainAI Body;        // kept to ask the game, once the enemy has left its list, whether it died
            public Vector3 Last;
            public float Since;
            public string Who = "", Name = "";
            public bool Dead;           // the game said so while the enemy was still listed
        }
        private static readonly Dictionary<IntPtr, int> Ids = new Dictionary<IntPtr, int>();
        private static readonly Dictionary<int, Tracked> Live = new Dictionary<int, Tracked>();
        private static readonly List<string> Names = new List<string>();
        private static readonly List<int> LeftOut = new List<int>();             // the scenario's spawns that lost their roll, by their place in the file
        private static readonly HashSet<string> Remaking = new HashSet<string>(); // who was taken out to be made again and has not come back yet

        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "replays");
        // Enemies the mod itself took out, with the kind of event and the reason: never recorded as "down".
        private static readonly Dictionary<int, KeyValuePair<string, string>> TakenOut = new Dictionary<int, KeyValuePair<string, string>>();
        internal static void Removed(IntPtr bot, string kind, string why) { if (Ids.TryGetValue(bot, out int id)) TakenOut[id] = new KeyValuePair<string, string>(kind, why); }

        private static string F(float v) => Math.Round(v, 2).ToString(Inv);
        private static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";

        public static void Begin(string scene, string navFile, string planJson)
        {
            End("a new mission began before this one was seen to end");
            if (!DirectorPlugin.ReplayEnabled.Value) return;
            try
            {
                Directory.CreateDirectory(Dir);
                Prune();
                string safe = string.IsNullOrEmpty(scene) ? "mission" : scene.Replace(' ', '_');
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                _path = Path.Combine(Dir, DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv) + "_" + safe + ".json");
                TakenOut.Clear(); LeftOut.Clear(); Remaking.Clear();
                string run = ReadRun(out string scenario, out string hash, out long roll);
                if (run != "") RunRecord.LinkReplay(_path);
                var head = new StringBuilder(512 + (planJson ?? "").Length);
                head.Append("{\"schema\":\"missionlab-replay/1\",\"semantics\":").Append(Semantics).Append(",\"session\":").Append(Q(Director.Session)).Append(",\"run\":").Append(Q(run));
                if (run != "")
                {
                    head.Append(",\"scenario\":").Append(Q(scenario)).Append(",\"scenarioHash\":").Append(Q(hash)).Append(",\"roll\":").Append(roll.ToString(Inv));
                    if (LeftOut.Count > 0)
                    {
                        head.Append(",\"leftOut\":[");
                        for (int i = 0; i < LeftOut.Count; i++) head.Append(i > 0 ? "," : "").Append(LeftOut[i].ToString(Inv));
                        head.Append(']');
                    }
                }
                head.Append(",\"plugin\":").Append(Q(DirectorPlugin.Version)).Append(",\"scene\":").Append(Q(scene)).Append(",\"navFile\":").Append(Q(navFile))
                    .Append(",\"startedUtc\":").Append(Q(DateTime.UtcNow.ToString("o", Inv))).Append(",\"step\":").Append(F(Step)).Append(",\"plan\":").Append(string.IsNullOrEmpty(planJson) ? "null" : planJson);
                _head = head.ToString();
                _frames = new StringBuilder(1 << 16); _events = new StringBuilder(1024);
                _frameCount = 0; _eventCount = 0; _nextId = 1; _lastTaken = 0;
                Ids.Clear(); Live.Clear(); Names.Clear();
                _startedAt = Time.unscaledTime; _nextSampleAt = _startedAt + 4f; _nextFlushAt = _startedAt + 30f;       // the first seconds are loading
                _on = true;
            }
            catch (Exception e) { _on = false; DirectorPlugin.L.LogWarning("the replay could not be started: " + e.Message); }
        }

        /// <summary>
        /// This mission's run record: the scenario and the fingerprint of its text, the roll, and which of the scenario's
        /// enemies lost their roll (so a row can name an enemy by its place in the file). A record of another mission is
        /// not claimed: an empty map writes none, so the newest record may belong to an earlier session.
        /// </summary>
        private static string ReadRun(out string scenario, out string hash, out long roll)
        {
            scenario = ""; hash = ""; roll = 0;
            string file = RunRecord.LastFile;
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(Director.Session)) return "";
            try
            {
                string path = Path.Combine(RunRecord.Dir, file);
                if (!File.Exists(path)) return "";
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
                {
                    System.Text.Json.JsonElement r = doc.RootElement;
                    if (Text(r, "session") != Director.Session) return "";
                    scenario = Text(r, "scenario"); hash = Text(r, "authoredHash");
                    if (r.TryGetProperty("roll", out System.Text.Json.JsonElement n) && n.ValueKind == System.Text.Json.JsonValueKind.Number && n.TryGetInt64(out long rolled)) roll = rolled;
                    if (r.TryGetProperty("leftOut", out System.Text.Json.JsonElement lo) && lo.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        lo.TryGetProperty("spawns", out System.Text.Json.JsonElement sp) && sp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        foreach (System.Text.Json.JsonElement e in sp.EnumerateArray())
                            if (e.ValueKind == System.Text.Json.JsonValueKind.Object && e.TryGetProperty("index", out System.Text.Json.JsonElement ix) &&
                                ix.ValueKind == System.Text.Json.JsonValueKind.Number && ix.TryGetInt32(out int at) && at >= 0) LeftOut.Add(at);
                    LeftOut.Sort();
                    return file;
                }
            }
            catch (Exception e)
            {
                LeftOut.Clear(); scenario = ""; hash = ""; roll = 0;
                DirectorPlugin.L.LogWarning("the replay could not read this mission's run record, so it names no scenario: " + e.Message);
                return "";
            }
        }

        private static string Text(System.Text.Json.JsonElement r, string key) =>
            r.TryGetProperty(key, out System.Text.Json.JsonElement v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";

        /// <summary>A spawn's place in the scenario file, from its place in the mission as it was rolled (the losers are not in that list).</summary>
        private static int Authored(int played)
        {
            int at = played;
            foreach (int lost in LeftOut)
            {
                if (lost > at) break;
                at++;
            }
            return at;
        }

        /// <summary>Who an enemy is, short for the row and spelt out for the timeline. Both are "" when it is not known.</summary>
        private static string WhoIs(BrainAI b, out string name)
        {
            name = "";
            try
            {
                GameMaster.Identity(b, out int spawn, out int marker, out int truck);
                int authored = spawn >= 0 ? Authored(spawn) : -1;
                string who = authored >= 0 ? "s" + authored.ToString(Inv) : marker >= 0 ? "m" + marker.ToString(Inv) : "";
                string said = authored >= 0 ? "spawn #" + (authored + 1).ToString(Inv) : marker >= 0 ? "marker " + marker.ToString(Inv) : "";
                if (truck > 0)
                {
                    who += "v" + truck.ToString(Inv);
                    said += (said == "" ? "" : ", ") + "gun truck " + truck.ToString(Inv);
                }
                if (said != "") name = " (" + said + ")";
                return who;
            }
            catch { return ""; }
        }

        /// <summary>Has the game itself said that this enemy died? A body that is gone, or cannot be asked, has not.</summary>
        private static bool IsDead(BrainAI b)
        {
            try
            {
                if (b == null) return false;
                Health h = b.health;
                return h != null && h.isDead;
            }
            catch { return false; }
        }

        /// <summary>Something worth a line on the timeline. enemy: the row id it is about (0: none), so a reader can tell who it was.</summary>
        public static void Event(string kind, string text, Vector3? at = null, int enemy = 0)
        {
            if (!_on) return;
            _events.Append(_eventCount++ > 0 ? "," : "").Append('[').Append(F(Time.unscaledTime - _startedAt)).Append(',').Append(Q(kind)).Append(',').Append(Q(text));
            if (at.HasValue) _events.Append(",[").Append(F(at.Value.x)).Append(',').Append(F(at.Value.y)).Append(',').Append(F(at.Value.z)).Append(']');
            else if (enemy > 0) _events.Append(",null");
            if (enemy > 0) _events.Append(',').Append(enemy);
            _events.Append(']');
        }

        public static void Tick(RaidManager raid, GameManager gm, float now)
        {
            if (!_on || now < _nextSampleAt) return;
            _nextSampleAt = now + Step;
            if (now - _startedAt > MaxSeconds) { End("the recording stops after an hour"); return; }
            int mark = _frames.Length, count = _frameCount;
            try { Sample(raid, gm, now); }
            catch (Exception e)
            {
                // A moment that broke halfway is taken back out, so the file that is written stays readable.
                _frames.Length = mark; _frameCount = count;
                DirectorPlugin.L.LogWarning("the replay stopped: " + e.Message);
                End("the recording stopped after an error");
                return;
            }
            if (now >= _nextFlushAt) { _nextFlushAt = now + 30f; Flush(); }
        }

        private static void Sample(RaidManager raid, GameManager gm, float now)
        {
            var sb = _frames;
            sb.Append(_frameCount++ > 0 ? ",\n" : "").Append('[').Append(F(now - _startedAt)).Append(",[");
            var players = PlayerMaster.AllPlayerMasters;
            bool first = true;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerMaster pm = players[i];
                PlayerNetworking body = pm == null ? null : pm.PlayerSpawnedObject;
                if (body == null) continue;
                string name = pm.thisPlayerName ?? "Player";
                int who = Names.IndexOf(name);
                if (who < 0) { who = Names.Count; Names.Add(name); }
                Vector3 p = body.transform.position;
                sb.Append(first ? "" : ",").Append('[').Append(who).Append(',').Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z)).Append(',').Append(Mathf.RoundToInt(GameMaster.PlayerYaw(pm, body))).Append(']');
                first = false;
            }
            sb.Append("],[");
            var seen = new HashSet<int>();
            var ai = gm.allAI; first = true;
            for (int i = 0; ai != null && i < ai.Count; i++)
            {
                BrainAI b = ai[i];
                if (b == null) continue;
                if (!Ids.TryGetValue(b.Pointer, out int id) || !Live.TryGetValue(id, out Tracked t))
                {
                    Ids[b.Pointer] = id = _nextId++;
                    t = new Tracked { Ptr = b.Pointer, Body = b, Since = now };
                    t.Who = WhoIs(b, out t.Name);
                    Live[id] = t;
                    if (now - _startedAt > 8f)
                    {
                        bool back = t.Who != "" && Remaking.Remove(t.Who);
                        GameMaster.EnemyPose(b, out Vector3 born, out _);
                        Event(KindAppears, "enemy " + id + t.Name + (back ? " is back after an edit" : " appears"), born, id);
                    }
                }
                // A new enemy is linked to its marker a moment after it appears, and a truck may get its gunner late: ask again for a few seconds.
                else if (t.Who == "" && now - t.Since < WhoWindow) t.Who = WhoIs(b, out t.Name);
                if (!t.Dead) t.Dead = IsDead(b);
                GameMaster.EnemyPose(b, out Vector3 pos, out float yaw);
                char state = 'I';
                // An enemy's state as it is born is not a real one (see GameMaster.StateCounts).
                try { if (GameMaster.StateCounts(b, now, out BrainAI.States real)) state = real.ToString()[0]; } catch { }
                sb.Append(first ? "" : ",").Append('[').Append(id).Append(',').Append(F(pos.x)).Append(',').Append(F(pos.y)).Append(',').Append(F(pos.z)).Append(',').Append(Mathf.RoundToInt(yaw)).Append(",\"").Append(state).Append('"');
                if (t.Who != "") sb.Append(",\"").Append(t.Who).Append('"');      // letters and digits only: nothing to escape
                sb.Append(']');
                first = false; seen.Add(id); t.Last = pos;
            }
            sb.Append("]]");
            // Who was there a moment ago and is not now: taken out by the mod, dead by the game's own word, or gone for a reason nobody told us.
            var gone = new List<int>();
            foreach (KeyValuePair<int, Tracked> kv in Live) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int id in gone)
            {
                Tracked t = Live[id];
                string who = "enemy " + id + t.Name;
                if (TakenOut.TryGetValue(id, out KeyValuePair<string, string> took))
                {
                    TakenOut.Remove(id);
                    bool remade = took.Key == KindRemade;
                    if (remade && t.Who != "") Remaking.Add(t.Who);
                    Event(took.Key, who + (remade ? " is taken out to be made again: " : " is taken out: ") + took.Value, t.Last, id);
                }
                else if (t.Dead || IsDead(t.Body)) Event(KindDown, who + " is down", t.Last, id);
                else Event(KindGone, who + " has left the game's list; why is not known", t.Last, id);
                Live.Remove(id);
                Ids.Remove(t.Ptr);           // the game may give its memory to a new enemy, which must not inherit this id
            }
            int taken = 0;
            try { taken = raid.collectedItemsCount; } catch { }
            if (taken > _lastTaken) { Event("intel", taken == 1 ? "an intel item is taken" : taken + " intel items are taken"); _lastTaken = taken; }
        }

        private static void Flush()
        {
            if (_frameCount < 20) return;                      // not worth a file yet
            try
            {
                var sb = new StringBuilder(_head.Length + _frames.Length + _events.Length + 256);
                sb.Append(_head).Append(",\"players\":[");
                for (int i = 0; i < Names.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(Names[i]));
                sb.Append("],\"events\":[").Append(_events).Append("],\"frames\":[\n").Append(_frames).Append("\n]}");
                File.WriteAllText(_path, sb.ToString());
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the replay could not be written: " + e.Message); }
        }

        /// <summary>Stops the recording and writes it. why: the reason, put on the timeline (null: none worth saying).</summary>
        public static void End(string why = null)
        {
            if (!_on) return;
            if (!string.IsNullOrEmpty(why)) Event(KindEnd, why);
            _on = false;
            Flush();
            if (_frameCount >= 20) DirectorPlugin.L.LogInfo("replay: " + _frameCount + " moments and " + _eventCount + " events written to " + _path);
            _frames = null; _events = null;
            Ids.Clear(); Live.Clear(); TakenOut.Clear(); Remaking.Clear();     // the game's objects are not held on to after the mission
        }

        /// <summary>Keeps the newest recordings; the oldest go when there are more than the setting allows.</summary>
        private static void Prune()
        {
            int keep = Math.Max(1, DirectorPlugin.ReplayKeep.Value);
            string[] files = Directory.GetFiles(Dir, "*.json");
            Array.Sort(files, StringComparer.Ordinal);          // names start with the date, so this is oldest first
            for (int i = 0; i < files.Length - (keep - 1); i++)
                try { File.Delete(files[i]); } catch { }
        }

        /// <summary>The recordings on disk, newest first, for the browser page.</summary>
        public static string ListJson()
        {
            var sb = new StringBuilder("[");
            try
            {
                if (Directory.Exists(Dir))
                {
                    string[] files = Directory.GetFiles(Dir, "*.json");
                    Array.Sort(files, StringComparer.Ordinal); Array.Reverse(files);
                    for (int i = 0; i < files.Length && i < 50; i++)
                        sb.Append(i > 0 ? "," : "").Append("{\"name\":").Append(Q(Path.GetFileName(files[i]))).Append(",\"bytes\":").Append(new FileInfo(files[i]).Length).Append('}');
                }
            }
            catch { }
            return sb.Append(']').ToString();
        }
    }
}
