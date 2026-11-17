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
    /// </summary>
    internal static class Replay
    {
        private const float Step = 0.5f, MaxSeconds = 3600f;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static StringBuilder _frames, _events;
        private static int _frameCount, _eventCount, _nextId, _lastTaken;
        private static float _startedAt, _nextSampleAt, _nextFlushAt;
        private static string _path, _head;
        private static bool _on;
        private static readonly Dictionary<IntPtr, int> Ids = new Dictionary<IntPtr, int>();
        private static readonly Dictionary<int, Vector3> LastSeen = new Dictionary<int, Vector3>();
        private static readonly List<string> Names = new List<string>();

        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "replays");
        // Enemies the mod itself took out, with the reason: they are recorded as "removed", never as "down".
        private static readonly Dictionary<int, string> TakenOut = new Dictionary<int, string>();
        internal static void Removed(IntPtr bot, string why) { if (Ids.TryGetValue(bot, out int id)) TakenOut[id] = why; }

        private static string F(float v) => Math.Round(v, 2).ToString(Inv);
        private static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";

        public static void Begin(string scene, string navFile, string planJson)
        {
            End();
            if (!DirectorPlugin.ReplayEnabled.Value) return;
            try
            {
                Directory.CreateDirectory(Dir);
                Prune();
                string safe = string.IsNullOrEmpty(scene) ? "mission" : scene.Replace(' ', '_');
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                _path = Path.Combine(Dir, DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv) + "_" + safe + ".json");
                TakenOut.Clear();
                RunRecord.LinkReplay(_path);
                _head = "{\"schema\":\"missionlab-replay/1\",\"semantics\":2,\"run\":" + Q(RunRecord.LastFile) + ",\"plugin\":" + Q(DirectorPlugin.Version) + ",\"scene\":" + Q(scene) + ",\"navFile\":" + Q(navFile) +
                        ",\"startedUtc\":" + Q(DateTime.UtcNow.ToString("o", Inv)) + ",\"step\":" + F(Step) + ",\"plan\":" + (string.IsNullOrEmpty(planJson) ? "null" : planJson);
                _frames = new StringBuilder(1 << 16); _events = new StringBuilder(1024);
                _frameCount = 0; _eventCount = 0; _nextId = 1; _lastTaken = 0;
                Ids.Clear(); LastSeen.Clear(); Names.Clear();
                _startedAt = Time.unscaledTime; _nextSampleAt = _startedAt + 4f; _nextFlushAt = _startedAt + 30f;       // the first seconds are loading
                _on = true;
            }
            catch (Exception e) { _on = false; DirectorPlugin.L.LogWarning("the replay could not be started: " + e.Message); }
        }

        /// <summary>Something worth a line on the timeline.</summary>
        public static void Event(string kind, string text, Vector3? at = null)
        {
            if (!_on) return;
            _events.Append(_eventCount++ > 0 ? "," : "").Append('[').Append(F(Time.unscaledTime - _startedAt)).Append(',').Append(Q(kind)).Append(',').Append(Q(text));
            if (at.HasValue) _events.Append(",[").Append(F(at.Value.x)).Append(',').Append(F(at.Value.y)).Append(',').Append(F(at.Value.z)).Append(']');
            _events.Append(']');
        }

        public static void Tick(RaidManager raid, GameManager gm, float now)
        {
            if (!_on || now < _nextSampleAt) return;
            _nextSampleAt = now + Step;
            if (now - _startedAt > MaxSeconds) { Event("note", "the recording stops after an hour"); End(); return; }
            try { Sample(raid, gm, now); }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the replay stopped: " + e.Message); End(); return; }
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
                if (!Ids.TryGetValue(b.Pointer, out int id))
                {
                    Ids[b.Pointer] = id = _nextId++;
                    if (now - _startedAt > 8f) { GameMaster.EnemyPose(b, out Vector3 born, out _); Event("appears", "enemy " + id + " appears", born); }
                }
                GameMaster.EnemyPose(b, out Vector3 pos, out float yaw);
                char state = 'I';
                // An enemy's state as it is born is not a real one (see GameMaster.StateCounts).
                try { if (GameMaster.StateCounts(b, now, out BrainAI.States real)) state = real.ToString()[0]; } catch { }
                sb.Append(first ? "" : ",").Append('[').Append(id).Append(',').Append(F(pos.x)).Append(',').Append(F(pos.y)).Append(',').Append(F(pos.z)).Append(',').Append(Mathf.RoundToInt(yaw)).Append(",\"").Append(state).Append("\"]");
                first = false; seen.Add(id); LastSeen[id] = pos;
            }
            sb.Append("]]");
            // Who was there a moment ago and is not now, went down.
            var gone = new List<int>();
            foreach (var kv in LastSeen) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int id in gone)
            {
                if (TakenOut.TryGetValue(id, out string why)) { Event("removed", "enemy " + id + " is taken out: " + why, LastSeen[id]); TakenOut.Remove(id); }
                else Event("down", "enemy " + id + " is down", LastSeen[id]);
                LastSeen.Remove(id);
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

        public static void End()
        {
            if (!_on) return;
            _on = false;
            Flush();
            if (_frameCount >= 20) DirectorPlugin.L.LogInfo("replay: " + _frameCount + " moments and " + _eventCount + " events written to " + _path);
            _frames = null; _events = null;
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
