using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MissionLab.Core
{
    /// <summary>
    /// One run record as the catalogue needs it: which scenario (by name, and by id and revision when the plan had them),
    /// whether it was used or refused, and when it ended. Seconds stays -1 when no end was seen: an older record, a mission
    /// still running, or a game that was closed. Unknown is never turned into a guess.
    /// </summary>
    public sealed class RunSummary
    {
        public string File = "", Scenario = "", ScenarioId = "", Hash = "", Outcome = "", Reason = "", Started = "", Session = "", Ended = "", Result = "";
        public int Revision;
        public double Seconds = -1;

        /// <summary>A run record's text (missionlab-run/1); null when it cannot be read.</summary>
        public static RunSummary Read(string file, string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return null;
                    var s = new RunSummary
                    {
                        File = file ?? "", Scenario = Text(r, "scenario"), ScenarioId = Text(r, "scenarioId"), Hash = Text(r, "authoredHash"), Outcome = Text(r, "outcome"),
                        Reason = Text(r, "reason"), Started = Text(r, "startedUtc"), Session = Text(r, "session")
                    };
                    if (r.TryGetProperty("revision", out JsonElement rev) && rev.ValueKind == JsonValueKind.Number && rev.TryGetInt32(out int rv) && rv > 0) s.Revision = rv;
                    if (r.TryGetProperty("end", out JsonElement end) && end.ValueKind == JsonValueKind.Object)
                    {
                        s.Ended = Text(end, "utc"); s.Result = Text(end, "result");
                        if (end.TryGetProperty("seconds", out JsonElement sec) && sec.ValueKind == JsonValueKind.Number && sec.TryGetDouble(out double d) && d >= 0) s.Seconds = d;
                    }
                    return s;
                }
            }
            catch (Exception) { return null; }
        }

        internal static string Text(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    /// <summary>
    /// One scenario as the Play view lists it. What the author says (squad range, estimated minutes) and what was measured
    /// (how long finished runs took) are kept in separate fields and never fall back on each other.
    /// </summary>
    public sealed class CatalogueEntry
    {
        public string File = "", Value = "";          // the file name, and the name the next-mission choice uses (no ".json")
        public bool Readable, Usable;                  // strict reader accepted it; and MissionLab would lay it out (it has enemies)
        public string Why = "";                        // why it cannot be used ("" when it can)
        public List<Finding> Findings = new List<Finding>();
        public string Title = "", Id = "", NavFile = "", Scene = "", Hash = "";
        public int Revision, SquadMin, SquadMax, Enemies;
        public float Minutes;                          // the author's estimate (0: not given)
        public List<string> Briefing = new List<string>();
        public List<string> Capabilities = new List<string>();
        public int Runs, Finished;                     // runs of this scenario on record; of those, used and seen to end
        public double? Measured;                       // median minutes of the finished runs; null when there is none
        public RunSummary LastRun;                     // the newest run on record, or null
        public bool Sealed;                            // the author sealed it: Play shows no placements, rolls or future trigger outcomes (ML-021)
        public List<OptionPlan> Options = new List<OptionPlan>();   // the host's choices before the mission, by name (ML-022)
    }

    /// <summary>
    /// Builds catalogue entries from a scenario's text and the run records. Pure: the plugin reads the files and the
    /// compatibility table and hands them in, so the rules here can be tested without the game.
    /// </summary>
    public static class CatalogueInfo
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const int MaxFindings = 20;            // a badly broken file lists its first problems, not hundreds

        public static CatalogueEntry Build(string file, string text, List<RunSummary> runs)
        {
            var e = new CatalogueEntry { File = file ?? "", Value = ValueOf(file), Hash = ScenarioFile.Hash(text ?? "") };
            e.Findings = ScenarioFile.Read(text ?? "", out MissionPlan plan);
            e.Readable = plan != null;
            // A refused file is sealed when its text says so, so a broken sealed mission does not spill its positions either.
            e.Sealed = plan != null ? plan.Sealed : SealedIn(text);
            if (e.Sealed) e.Findings = Unplaced(e.Findings);
            if (plan == null)
            {
                Finding first = e.Findings.Find(f => f.Level == Finding.Error);
                e.Why = "MissionLab would refuse this file: " + (first != null ? first.ToString() : "it could not be read");
            }
            else
            {
                e.Title = (plan.Title ?? "").Trim(); e.Id = plan.Id ?? ""; e.Revision = plan.Revision;
                e.SquadMin = plan.SquadMin; e.SquadMax = plan.SquadMax; e.Minutes = plan.Minutes;
                e.NavFile = (plan.NavFile ?? "").Trim(); e.Scene = (plan.Scene ?? "").Trim();
                e.Briefing = new List<string>(plan.Briefing); e.Enemies = plan.Spawns.Count;
                e.Capabilities = MissionLab.Core.Capabilities.Of(plan);
                if (plan.Options != null) e.Options = new List<OptionPlan>(plan.Options);
                e.Usable = plan.Spawns.Count > 0;
                if (!e.Usable) e.Why = "it has no enemies, so MissionLab leaves the game's own mission";
            }
            if (!e.Readable) Identity(text, out e.Id, out e.Revision);
            var times = new List<double>();
            foreach (RunSummary r in runs ?? new List<RunSummary>())
            {
                if (r == null || !Belongs(e, r)) continue;
                e.Runs++;
                if (e.LastRun == null || string.CompareOrdinal(r.File, e.LastRun.File) > 0) e.LastRun = r;
                if (r.Outcome == "used" && r.Seconds >= 0) { e.Finished++; times.Add(r.Seconds / 60.0); }
            }
            e.Measured = Median(times);
            return e;
        }

        /// <summary>The choice value for a file: its name without ".json".</summary>
        public static string ValueOf(string file)
        {
            string f = file ?? "";
            return f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? f.Substring(0, f.Length - 5) : f;
        }

        /// <summary>
        /// Whether a run was of this scenario. Both have an id: the ids decide, so a renamed file keeps its history and a new
        /// file under an old name does not inherit it. Otherwise the file name decides, as older records name nothing else.
        /// </summary>
        public static bool Belongs(CatalogueEntry e, RunSummary r)
        {
            if (e == null || r == null) return false;
            if (e.Id != "" && r.ScenarioId != "") return string.Equals(e.Id, r.ScenarioId, StringComparison.Ordinal);
            return r.Scenario != "" && string.Equals(ValueOf(r.Scenario), e.Value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The middle value (the mean of the two middle ones for an even count), or null for none.</summary>
        public static double? Median(List<double> values)
        {
            if (values == null || values.Count == 0) return null;
            var v = new List<double>(values); v.Sort();
            int n = v.Count;
            return n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2.0;
        }

        /// <summary>
        /// A scenario's id and revision straight from its text, also when the strict reader refuses it: a refused run is
        /// then still linked to the scenario it was meant to be. Looks at the root and, for plan/2, under "mission".
        /// </summary>
        public static void Identity(string text, out string id, out int revision)
        {
            id = ""; revision = 0;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return;
                    if (r.TryGetProperty("mission", out JsonElement m) && m.ValueKind == JsonValueKind.Object) r = m;
                    string s = RunSummary.Text(r, "id");
                    if (MissionPlan.IdOk(s)) id = s;
                    if (r.TryGetProperty("revision", out JsonElement rev) && rev.ValueKind == JsonValueKind.Number && rev.TryGetInt32(out int rv) && rv > 0) revision = rv;
                }
            }
            catch (Exception) { id = ""; revision = 0; }
        }

        /// <summary>
        /// Whether a scenario's text says "sealed": true, read leniently (root, or under "mission" for plan/2), so a file
        /// the strict reader refuses is still treated as sealed when its author meant it to be.
        /// </summary>
        public static bool SealedIn(string text)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return false;
                    if (r.TryGetProperty("mission", out JsonElement m) && m.ValueKind == JsonValueKind.Object) r = m;
                    return r.TryGetProperty("sealed", out JsonElement s) && s.ValueKind == JsonValueKind.True;
                }
            }
            catch (Exception) { return false; }
        }

        // A place written into a message: "at (12.5, -4)", "from 12, 34", "near 1, 2, 3" or a bare "(12.5, -4)".
        private static readonly Regex Spot = new Regex(@"\s*(?:\b(?:at|from|near)\s+\(?|\()\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?(?:\s*,\s*-?\d+(?:\.\d+)?)?(?:\s*\))?", RegexOptions.CultureInvariant);

        /// <summary>
        /// A message with the places in it left out. A sealed mission's findings still say what is wrong and where in the
        /// file (the path), but not where on the map: the host should not learn an enemy's spot from an error.
        /// The same pattern is in the editor (20b-sealed.js, sealedStrip).
        /// </summary>
        public static string Unplaced(string text) => string.IsNullOrEmpty(text) ? text ?? "" : Spot.Replace(text, "");

        /// <summary>Copies of the findings with the places left out of their text (see Unplaced).</summary>
        public static List<Finding> Unplaced(List<Finding> found)
        {
            var list = new List<Finding>();
            foreach (Finding f in found ?? new List<Finding>())
                list.Add(new Finding { Level = f.Level, Code = f.Code, Path = f.Path, Text = Unplaced(f.Text) });
            return list;
        }

        /// <summary>
        /// A safe file name for the scenarios folder: letters, digits, '_' and '-' only, at most 60 of them, ".json" added.
        /// "" when nothing usable is left. Never a folder, so a save cannot land anywhere else.
        /// </summary>
        public static string SafeName(string name)
        {
            string n = (name ?? "").Trim();
            if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 5);
            var sb = new StringBuilder(n.Length);
            foreach (char c in n)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (ok) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            string s = sb.ToString().Trim('_', '-');
            if (s.Length > 60) s = s.Substring(0, 60).Trim('_', '-');
            if (s.Length == 0) return "";
            // Names Windows keeps for devices cannot be files.
            string up = s.ToUpperInvariant();
            if (up == "CON" || up == "PRN" || up == "AUX" || up == "NUL" || (up.Length == 4 && (up.StartsWith("COM", StringComparison.Ordinal) || up.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(up[3]))) s += "_";
            return s + ".json";
        }

        /// <summary>Findings as a JSON list, the first MaxFindings only.</summary>
        public static void WriteFindings(StringBuilder sb, List<Finding> found)
        {
            sb.Append('[');
            for (int i = 0; found != null && i < found.Count && i < MaxFindings; i++)
            {
                Finding f = found[i];
                sb.Append(i > 0 ? "," : "").Append("{\"level\":\"").Append(Guard.Esc(f.Level)).Append("\",\"code\":\"").Append(Guard.Esc(f.Code))
                  .Append("\",\"path\":\"").Append(Guard.Esc(f.Path)).Append("\",\"text\":\"").Append(Guard.Esc(f.Text)).Append("\"}");
            }
            sb.Append(']');
        }

        /// <summary>
        /// The entry as JSON for the page. compat: the compatibility table's word for its map ("" when the scenario names
        /// no map file, so nothing can be judged). seen: capabilities seen working in the game; others are marked untested.
        /// mapLabel: a readable name for the map ("" when unknown).
        /// </summary>
        public static void Write(CatalogueEntry e, StringBuilder sb, string compat, ICollection<string> seen, string mapLabel)
        {
            string Q(string s) => "\"" + Guard.Esc(s ?? "") + "\"";
            sb.Append("{\"file\":").Append(Q(e.File)).Append(",\"value\":").Append(Q(e.Value))
              .Append(",\"readable\":").Append(e.Readable ? "true" : "false").Append(",\"usable\":").Append(e.Usable ? "true" : "false").Append(",\"why\":").Append(Q(e.Why))
              .Append(",\"findings\":");
            WriteFindings(sb, e.Findings);
            sb.Append(",\"title\":").Append(Q(e.Title)).Append(",\"id\":").Append(Q(e.Id)).Append(",\"revision\":").Append(e.Revision.ToString(Inv))
              .Append(",\"sealed\":").Append(e.Sealed ? "true" : "false").Append(",\"briefing\":[");
            for (int i = 0; i < e.Briefing.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(e.Briefing[i]));
            sb.Append("],\"author\":{\"squad\":");
            if (e.SquadMin > 0 && e.SquadMax > 0) sb.Append('[').Append(e.SquadMin.ToString(Inv)).Append(',').Append(e.SquadMax.ToString(Inv)).Append(']'); else sb.Append("null");
            sb.Append(",\"minutes\":").Append(e.Minutes > 0f ? Math.Round(e.Minutes, 2).ToString(Inv) : "null");
            sb.Append("},\"measured\":{\"minutes\":").Append(e.Measured.HasValue ? Math.Round(e.Measured.Value, 1).ToString(Inv) : "null")
              .Append(",\"finished\":").Append(e.Finished.ToString(Inv)).Append(",\"runs\":").Append(e.Runs.ToString(Inv)).Append('}');
            sb.Append(",\"navFile\":").Append(Q(e.NavFile)).Append(",\"scene\":").Append(Q(e.Scene)).Append(",\"map\":").Append(Q(mapLabel))
              .Append(",\"mode\":\"pve\",\"compat\":").Append(Q(compat)).Append(",\"enemies\":");
            // A sealed mission leaves its enemy count out: knowing there are exactly twelve tells the squad when the house is
            // clear, which is a tactical spoiler. The author's squad range and minutes still give the scale.
            sb.Append(e.Sealed ? "null" : e.Enemies.ToString(Inv)).Append(",\"options\":[");
            for (int i = 0; i < e.Options.Count; i++)
            {
                OptionPlan o = e.Options[i];
                sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(Q(o.Id)).Append(",\"name\":").Append(Q(o.Name)).Append(",\"pick\":").Append(o.Pick.ToString(Inv)).Append(",\"choices\":[");
                for (int j = 0; j < o.Choices.Count; j++) sb.Append(j > 0 ? "," : "").Append(Q(o.Choices[j] != null ? o.Choices[j].Name : ""));
                sb.Append("]}");
            }
            sb.Append("],\"capabilities\":[");
            for (int i = 0; i < e.Capabilities.Count; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"name\":").Append(Q(e.Capabilities[i])).Append(",\"tested\":").Append(seen != null && seen.Contains(e.Capabilities[i]) ? "true" : "false").Append('}');
            sb.Append("],\"lastRun\":");
            RunSummary r = e.LastRun;
            if (r == null) sb.Append("null");
            else
                sb.Append("{\"file\":").Append(Q(r.File)).Append(",\"started\":").Append(Q(r.Started)).Append(",\"session\":").Append(Q(r.Session))
                  .Append(",\"outcome\":").Append(Q(r.Outcome)).Append(",\"reason\":").Append(Q(e.Sealed ? Unplaced(r.Reason) : r.Reason)).Append(",\"revision\":").Append(r.Revision.ToString(Inv))
                  .Append(",\"ended\":").Append(Q(r.Ended)).Append(",\"result\":").Append(Q(r.Result))
                  .Append(",\"minutes\":").Append(r.Seconds >= 0 ? Math.Round(r.Seconds / 60.0, 1).ToString(Inv) : "null").Append('}');
            sb.Append(",\"hash\":").Append(Q(e.Hash)).Append('}');
        }
    }
}
