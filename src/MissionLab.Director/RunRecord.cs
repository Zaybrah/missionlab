using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// What was actually played. One small file per mission in BepInEx/MissionLab/runs: which scenario (by a fingerprint of its text),
    /// the roll, how many players, what the dice left out, and the scenario as it stood after the dice. A refused setup gets a
    /// record too. No player names and no folder names are written, so a record can be handed to someone else as it is.
    /// "Repeat this run" takes the roll from a record and says afterwards whether the same setup came out.
    /// </summary>
    internal static class RunRecord
    {
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "runs");
        internal static string LastFile = "";          // this mission's record ("": none was written)
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal sealed class Entry { public string File = "", Scenario = "", Hash = "", Outcome = "", Scene = "", Started = "", Resolved = "", Plugin = ""; public long Roll; public int Players; }

        internal static string Sha(string text) => ScenarioFile.Hash(text);

        private static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";

        internal static void Write(string scene, string navFile, string scenario, string authoredText, long roll, int players, MissionPlan resolved, Variants.Out gone, List<string> notes, bool used, string reason)
        {
            LastFile = "";
            try
            {
                Directory.CreateDirectory(Dir);
                Prune();
                string safe = string.IsNullOrEmpty(scene) ? "mission" : scene.Replace(' ', '_');
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                string file = DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv) + "_" + safe + ".run.json";
                var sb = new StringBuilder(4096);
                sb.Append("{\"schema\":\"missionlab-run/1\",\"plugin\":").Append(Q(DirectorPlugin.Version)).Append(",\"game\":").Append(Q(UnityEngine.Application.version))
                  .Append(",\"startedUtc\":").Append(Q(DateTime.UtcNow.ToString("o", Inv))).Append(",\"session\":").Append(Q(Director.Session))
                  .Append(",\"scene\":").Append(Q(scene)).Append(",\"navFile\":").Append(Q(navFile)).Append(",\"scenario\":").Append(Q(scenario))
                  .Append(",\"authoredHash\":").Append(Q(Sha(authoredText))).Append(",\"roll\":").Append(roll).Append(",\"players\":").Append(players)
                  .Append(",\"outcome\":").Append(Q(used ? "used" : "refused")).Append(",\"reason\":").Append(Q(reason));
                sb.Append(",\"leftOut\":{\"spawns\":[");
                for (int i = 0; gone != null && i < gone.Spawns.Count; i++)
                {
                    SpawnPlan s = gone.Spawns[i];
                    string why = players > 0 && s.MinPlayers > players ? "minPlayers" : s.Either > 0 && s.Chance >= 100 ? "either" : "chance";
                    sb.Append(i == 0 ? "" : ",").Append("{\"index\":").Append(i < gone.SpawnIndex.Count ? gone.SpawnIndex[i] : -1).Append(",\"why\":\"").Append(why).Append("\"}");
                }
                sb.Append("],\"doors\":").Append(gone == null ? 0 : gone.Doors.Count).Append(",\"things\":").Append(gone == null ? 0 : gone.Things.Count).Append(",\"triggersOff\":[");
                bool first = true;
                if (resolved != null) foreach (TriggerPlan t in resolved.Triggers) if (t.Off) { sb.Append(first ? "" : ",").Append(t.Id); first = false; }
                sb.Append("]},\"notes\":[");
                for (int i = 0; notes != null && i < notes.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Q(notes[i]));
                sb.Append("],\"replay\":\"\",\"resolved\":").Append(resolved == null ? "null" : resolved.ToJson()).Append('}');
                File.WriteAllText(Path.Combine(Dir, file), sb.ToString());
                LastFile = file;
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the run record could not be written: " + e.Message); }
        }

        /// <summary>Notes in this mission's record which recording belongs to it.</summary>
        internal static void LinkReplay(string replayFile)
        {
            if (LastFile == "" || string.IsNullOrEmpty(replayFile)) return;
            try
            {
                string path = Path.Combine(Dir, LastFile), text = File.ReadAllText(path);
                int at = text.IndexOf("\"replay\":\"\"", StringComparison.Ordinal);
                if (at >= 0) File.WriteAllText(path, text.Substring(0, at) + "\"replay\":" + Q(Path.GetFileName(replayFile)) + text.Substring(at + 11));
            }
            catch { }
        }

        private static void Prune()
        {
            try
            {
                var files = new List<string>(Directory.GetFiles(Dir, "*.run.json"));
                files.Sort(StringComparer.Ordinal);
                for (int i = 0; i < files.Count - 60; i++) File.Delete(files[i]);
            }
            catch { }
        }

        internal static Entry Read(string file)
        {
            try
            {
                string path = Path.Combine(Dir, Path.GetFileName(file ?? ""));
                if (!File.Exists(path)) return null;
                using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
                {
                    var r = doc.RootElement;
                    string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : "";
                    return new Entry
                    {
                        File = Path.GetFileName(path), Scenario = S("scenario"), Hash = S("authoredHash"), Outcome = S("outcome"), Scene = S("scene"), Started = S("startedUtc"), Plugin = S("plugin"),
                        Roll = r.TryGetProperty("roll", out var roll) && roll.ValueKind == System.Text.Json.JsonValueKind.Number ? roll.GetInt64() : 0,
                        Players = r.TryGetProperty("players", out var pl) && pl.ValueKind == System.Text.Json.JsonValueKind.Number ? pl.GetInt32() : 0,
                        Resolved = r.TryGetProperty("resolved", out var res) && res.ValueKind == System.Text.Json.JsonValueKind.Object ? MissionPlan.FromJson(res.GetRawText()).ToJson() : ""
                    };
                }
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("run record '" + file + "' could not be read: " + e.Message); return null; }
        }

        /// <summary>The newest records, newest first, for the page.</summary>
        internal static string ListJson(int count)
        {
            var sb = new StringBuilder("[");
            try
            {
                if (Directory.Exists(Dir))
                {
                    var files = new List<string>(Directory.GetFiles(Dir, "*.run.json"));
                    files.Sort(StringComparer.Ordinal); files.Reverse();
                    int n = 0;
                    foreach (string f in files)
                    {
                        if (n >= count) break;
                        Entry e = Read(f);
                        if (e == null) continue;
                        sb.Append(n++ == 0 ? "" : ",").Append("{\"file\":").Append(Q(e.File)).Append(",\"scenario\":").Append(Q(e.Scenario)).Append(",\"scene\":").Append(Q(e.Scene))
                          .Append(",\"roll\":").Append(e.Roll).Append(",\"players\":").Append(e.Players).Append(",\"outcome\":").Append(Q(e.Outcome)).Append(",\"startedUtc\":").Append(Q(e.Started)).Append('}');
                    }
                }
            }
            catch { }
            return sb.Append(']').ToString();
        }
    }
}
