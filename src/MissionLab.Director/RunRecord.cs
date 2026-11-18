using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// What was actually played. One small file per mission in BepInEx/MissionLab/runs: which scenario (by a fingerprint of its text,
    /// and by its id and revision when it has them), the roll, how many players, what the dice left out, and the scenario as it stood
    /// after the dice. A refused setup gets a record too. No player names and no folder names are written, so a record can be handed
    /// to someone else as it is. "Repeat this run" takes the roll from a record and says afterwards whether the same setup came out.
    /// When the mission is seen to end, "end" is filled in (it stays null otherwise): the catalogue's measured durations come only
    /// from there, so a run whose end was never seen stays unmeasured.
    /// </summary>
    internal static class RunRecord
    {
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "runs");
        internal static string LastFile = "";          // this mission's record ("": none was written)
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static DateTime _startedUtc;            // when LastFile was written
        private static string _ending = "";             // how the mod ended this mission ("success", "failed"; "": it did not)
        private static string _finished = "";           // the record whose end has been written, so it is written once
        private static string _session = "";            // the mission LastFile belongs to: a later mission without a record never ends it

        internal sealed class Entry { public string File = "", Scenario = "", Hash = "", Outcome = "", Scene = "", Started = "", Resolved = "", Plugin = "", ScenarioId = "", EndedUtc = "", Result = ""; public long Roll; public int Players, Revision; public double Seconds = -1;
                                      public Dictionary<string, int> Picks = new Dictionary<string, int>(); }     // the host options it was played with (ML-022)

        internal static string Sha(string text) => ScenarioFile.Hash(text);

        private static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";

        /// <summary>picks: the option chosen for every option of the scenario, by id (null or empty: it has none, and no "picks" key is written).</summary>
        internal static void Write(string scene, string navFile, string scenario, string authoredText, long roll, int players, MissionPlan resolved, Variants.Out gone, List<string> notes, bool used, string reason,
                                   IDictionary<string, int> picks = null)
        {
            LastFile = ""; _ending = ""; _finished = "";
            try
            {
                Directory.CreateDirectory(Dir);
                Prune();
                string safe = string.IsNullOrEmpty(scene) ? "mission" : scene.Replace(' ', '_');
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                string file = DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv) + "_" + safe + ".run.json";
                // Which scenario, by its own id and revision: from the plan, or from the text when the plan was refused.
                string id = resolved != null ? resolved.Id ?? "" : "";
                int revision = resolved != null ? resolved.Revision : 0;
                if (resolved == null) CatalogueInfo.Identity(authoredText, out id, out revision);
                DateTime now = DateTime.UtcNow;
                var sb = new StringBuilder(4096);
                sb.Append("{\"schema\":\"missionlab-run/1\",\"plugin\":").Append(Q(DirectorPlugin.Version)).Append(",\"game\":").Append(Q(UnityEngine.Application.version))
                  .Append(",\"startedUtc\":").Append(Q(now.ToString("o", Inv))).Append(",\"session\":").Append(Q(Director.Session))
                  .Append(",\"scene\":").Append(Q(scene)).Append(",\"navFile\":").Append(Q(navFile)).Append(",\"scenario\":").Append(Q(scenario))
                  .Append(",\"authoredHash\":").Append(Q(Sha(authoredText))).Append(",\"scenarioId\":").Append(Q(id)).Append(",\"revision\":").Append(revision.ToString(Inv))
                  .Append(",\"roll\":").Append(roll).Append(",\"players\":").Append(players);
                if (picks != null && picks.Count > 0) sb.Append(",\"picks\":").Append(Picker.PicksJson(picks));
                sb.Append(",\"outcome\":").Append(Q(used ? "used" : "refused")).Append(",\"reason\":").Append(Q(reason)).Append(",\"end\":null");
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
                LastFile = file; _startedUtc = now; _session = Director.Session ?? "";
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the run record could not be written: " + e.Message); }
        }

        /// <summary>The mod is ending this mission ("success" or "failed"); written into the record when the end is seen.</summary>
        internal static void Ending(string result)
        {
            if (LastFile != "" && _finished != LastFile && _session == (Director.Session ?? "")) _ending = result ?? "";
        }

        /// <summary>
        /// The mission is over: its end, how long it took from setup to end, and how it ended ("over" when the mod did not end it
        /// and the game's own result is not known). Written once per record; an earlier record is never touched.
        /// </summary>
        internal static void Finish()
        {
            if (LastFile == "" || _finished == LastFile || _session != (Director.Session ?? "")) return;
            _finished = LastFile;
            try
            {
                string path = Path.Combine(Dir, LastFile), text = File.ReadAllText(path);
                int at = text.IndexOf("\"end\":null", StringComparison.Ordinal);
                if (at < 0) return;
                DateTime now = DateTime.UtcNow;
                double seconds = Math.Max(0, (now - _startedUtc).TotalSeconds);
                string end = "\"end\":{\"utc\":" + Q(now.ToString("o", Inv)) + ",\"seconds\":" + Math.Round(seconds).ToString(Inv) + ",\"result\":" + Q(_ending == "" ? "over" : _ending) + "}";
                File.WriteAllText(path, text.Substring(0, at) + end + text.Substring(at + 10));
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the run record's end could not be written, so this run stays unmeasured: " + e.Message); }
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
                string text = File.ReadAllText(path);
                using (var doc = System.Text.Json.JsonDocument.Parse(text))
                {
                    var r = doc.RootElement;
                    string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : "";
                    RunSummary sum = RunSummary.Read(Path.GetFileName(path), text) ?? new RunSummary();
                    var entry = new Entry
                    {
                        File = Path.GetFileName(path), Scenario = S("scenario"), Hash = S("authoredHash"), Outcome = S("outcome"), Scene = S("scene"), Started = S("startedUtc"), Plugin = S("plugin"),
                        Roll = r.TryGetProperty("roll", out var roll) && roll.ValueKind == System.Text.Json.JsonValueKind.Number ? roll.GetInt64() : 0,
                        Players = r.TryGetProperty("players", out var pl) && pl.ValueKind == System.Text.Json.JsonValueKind.Number ? pl.GetInt32() : 0,
                        Resolved = r.TryGetProperty("resolved", out var res) && res.ValueKind == System.Text.Json.JsonValueKind.Object ? res.GetRawText() : "",
                        ScenarioId = sum.ScenarioId, Revision = sum.Revision, EndedUtc = sum.Ended, Result = sum.Result, Seconds = sum.Seconds
                    };
                    // The options it was played with, so "Reproduce this setup" picks them again (records before 0.26 have none).
                    if (r.TryGetProperty("picks", out var picks) && picks.ValueKind == System.Text.Json.JsonValueKind.Object)
                        foreach (var p in picks.EnumerateObject())
                            if (p.Value.ValueKind == System.Text.Json.JsonValueKind.Number && p.Value.TryGetInt32(out int at)) entry.Picks[p.Name] = at;
                    return entry;
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
                          .Append(",\"roll\":").Append(e.Roll).Append(",\"players\":").Append(e.Players).Append(",\"outcome\":").Append(Q(e.Outcome)).Append(",\"startedUtc\":").Append(Q(e.Started))
                          .Append(",\"scenarioId\":").Append(Q(e.ScenarioId)).Append(",\"revision\":").Append(e.Revision.ToString(Inv)).Append(",\"result\":").Append(Q(e.Result))
                          .Append(",\"seconds\":").Append(e.Seconds >= 0 ? Math.Round(e.Seconds).ToString(Inv) : "null").Append('}');
                    }
                }
            }
            catch { }
            return sb.Append(']').ToString();
        }
    }
}
