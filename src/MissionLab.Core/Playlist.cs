using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// One mission of a squad-session playlist, pinned as the host added it: which scenario (file, id, revision and a
    /// fingerprint of its text), which options, how many players are expected and how the dice are set. The map and mode are
    /// kept so the page can tell the host what to choose in the game's own lobby; MissionLab never switches the map itself.
    /// </summary>
    public sealed class PlaylistEntry
    {
        public string Label = "";                   // the host's own name for it ("Warm-up"); "" = the scenario's
        public string File = "";                    // the next-mission choice: the scenario's file name without ".json"
        public string ScenarioId = "";
        public int Revision;
        public string Hash = "";                    // ScenarioFile.Hash of the file's text when it was pinned
        public string Map = "", NavFile = "", Mode = "pve";
        public int Squad;                           // players expected (0: not said)
        public SortedDictionary<string, int> Picks = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public string Seed = Playlist.NewRoll;      // new, repeat or retry
        public string Record = "";                  // Seed repeat: the run record whose setup is played again
    }

    /// <summary>What one run record said, counted once for the entry it was played for.</summary>
    public sealed class PlaylistResult
    {
        public string Record = "";                  // the run record's file name: the key that makes a run count once
        public int Entry;                           // the entry it was played for, by position when it was counted
        public string Label = "";
        public string Result = "";                  // success, failed, unknown, incomplete or refused
        public bool Manual;                         // the host said how it ended, not the game
        public bool Advanced;                       // this run moved the playlist on (at most once)
    }

    /// <summary>
    /// A host's ordered agenda for an evening (missionlab-playlist/1). Next is the entry the next mission is for; Armed says
    /// the host set it up in the game (scenario, options and dice), and ArmedAfter is the newest run record at that moment,
    /// so only a mission started afterwards counts for it. A run moves the playlist on only when its end is known (success or
    /// failed, from the game or marked by the host), and each run record does so at most once, however often it is seen.
    /// </summary>
    public sealed class Playlist
    {
        public const string Schema = "missionlab-playlist/1";
        public const string NewRoll = "new", Repeat = "repeat", RetryFailed = "retry";
        public const string Success = "success", Failed = "failed", Unknown = "unknown", Incomplete = "incomplete", Refused = "refused";
        public const string Same = "same", Missing = "missing", Changed = "changed";
        public const int MostEntries = 50, MostResults = 100, LongestName = 60;

        public string Name = "";
        public List<PlaylistEntry> Entries = new List<PlaylistEntry>();
        public int Next;                            // Entries.Count: the playlist is done
        public bool Armed;
        public string ArmedAfter = "";
        public List<PlaylistResult> Results = new List<PlaylistResult>();

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        public PlaylistEntry Current => Next >= 0 && Next < Entries.Count ? Entries[Next] : null;

        // ----- the file

        public string ToJson()
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"schema\":\"").Append(Schema).Append("\",\"name\":").Append(Q(Name)).Append(",\"next\":").Append(Next.ToString(Inv));
            if (Armed) sb.Append(",\"armed\":true");
            if (ArmedAfter != "") sb.Append(",\"armedAfter\":").Append(Q(ArmedAfter));
            sb.Append(",\"entries\":[");
            for (int i = 0; i < Entries.Count; i++) { sb.Append(i > 0 ? "," : ""); EntryJson(Entries[i], sb); }
            sb.Append("],\"results\":[");
            for (int i = 0; i < Results.Count; i++)
            {
                PlaylistResult r = Results[i];
                sb.Append(i > 0 ? "," : "").Append("{\"record\":").Append(Q(r.Record)).Append(",\"entry\":").Append(r.Entry.ToString(Inv))
                  .Append(",\"label\":").Append(Q(r.Label)).Append(",\"result\":").Append(Q(r.Result));
                if (r.Manual) sb.Append(",\"manual\":true");
                if (r.Advanced) sb.Append(",\"advanced\":true");
                sb.Append('}');
            }
            return sb.Append("]}").ToString();
        }

        /// <summary>One entry as JSON; also how two entries are compared, so a save that changes nothing keeps the setup.</summary>
        public static void EntryJson(PlaylistEntry e, StringBuilder sb)
        {
            sb.Append("{\"label\":").Append(Q(e.Label)).Append(",\"file\":").Append(Q(e.File)).Append(",\"id\":").Append(Q(e.ScenarioId))
              .Append(",\"revision\":").Append(e.Revision.ToString(Inv)).Append(",\"hash\":").Append(Q(e.Hash))
              .Append(",\"map\":").Append(Q(e.Map)).Append(",\"navFile\":").Append(Q(e.NavFile)).Append(",\"mode\":").Append(Q(e.Mode));
            if (e.Squad > 0) sb.Append(",\"squad\":").Append(e.Squad.ToString(Inv));
            if (e.Picks.Count > 0)
            {
                sb.Append(",\"picks\":{");
                bool first = true;
                foreach (KeyValuePair<string, int> p in e.Picks) { sb.Append(first ? "" : ",").Append(Q(p.Key)).Append(':').Append(p.Value.ToString(Inv)); first = false; }
                sb.Append('}');
            }
            sb.Append(",\"seed\":").Append(Q(e.Seed));
            if (e.Record != "") sb.Append(",\"record\":").Append(Q(e.Record));
            sb.Append('}');
        }

        public static string EntryText(PlaylistEntry e) { var sb = new StringBuilder(256); EntryJson(e, sb); return sb.ToString(); }

        /// <summary>A playlist file's text; null with the reason when it cannot be read. A file from a later MissionLab is refused, never rewritten.</summary>
        public static Playlist Read(string json, out string why)
        {
            why = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? "", Lenient))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) { why = "a playlist is a JSON object"; return null; }
                    string schema = Text(r, "schema");
                    if (schema != Schema)
                    {
                        why = schema.StartsWith("missionlab-playlist/", StringComparison.Ordinal) ? "it was written by another MissionLab version (" + schema + "), so it is left as it is"
                                                                                                    : "it is not a MissionLab playlist (" + Schema + " expected)";
                        return null;
                    }
                    var p = new Playlist { Name = Clean(Text(r, "name"), LongestName), Armed = r.TryGetProperty("armed", out JsonElement a) && a.ValueKind == JsonValueKind.True, ArmedAfter = RecordName(Text(r, "armedAfter")) };
                    if (r.TryGetProperty("entries", out JsonElement es) && es.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement x in es.EnumerateArray())
                        {
                            if (p.Entries.Count >= MostEntries) break;
                            PlaylistEntry e = ReadEntry(x);
                            if (e != null) p.Entries.Add(e);
                        }
                    p.Next = Math.Max(0, Math.Min(p.Entries.Count, Int(r, "next")));
                    if (r.TryGetProperty("results", out JsonElement rs) && rs.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement x in rs.EnumerateArray())
                        {
                            if (x.ValueKind != JsonValueKind.Object) continue;
                            string record = RecordName(Text(x, "record"));
                            if (record == "" || p.Results.Exists(o => o.Record == record)) continue;     // a record counts once, also in a hand-edited file
                            p.Results.Add(new PlaylistResult
                            {
                                Record = record, Entry = Int(x, "entry"), Label = Clean(Text(x, "label"), LongestName), Result = Text(x, "result"),
                                Manual = x.TryGetProperty("manual", out JsonElement m) && m.ValueKind == JsonValueKind.True,
                                Advanced = x.TryGetProperty("advanced", out JsonElement ad) && ad.ValueKind == JsonValueKind.True
                            });
                        }
                    Trim(p.Results);
                    if (p.Current == null) p.Armed = false;
                    return p;
                }
            }
            catch (JsonException e) { why = "it is not JSON: " + e.Message; return null; }
        }

        private static PlaylistEntry ReadEntry(JsonElement x)
        {
            if (x.ValueKind != JsonValueKind.Object) return null;
            string file = CatalogueInfo.ValueOf(Text(x, "file").Trim());
            if (!FileOk(file)) return null;
            var e = new PlaylistEntry
            {
                Label = Clean(Text(x, "label"), LongestName), File = file, Hash = Text(x, "hash"),
                Revision = Math.Max(0, Int(x, "revision")), Map = Clean(Text(x, "map"), 80), NavFile = Clean(Text(x, "navFile"), 80),
                Mode = Text(x, "mode") == "" ? "pve" : Clean(Text(x, "mode"), 40), Squad = Math.Max(0, Math.Min(8, Int(x, "squad"))),
                Seed = Text(x, "seed") == "" ? NewRoll : Clean(Text(x, "seed"), 20), Record = RecordName(Text(x, "record"))
            };
            string id = Text(x, "id");
            e.ScenarioId = MissionPlan.IdOk(id) ? id : "";
            if (x.TryGetProperty("picks", out JsonElement picks) && picks.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in picks.EnumerateObject())
                    if (OptionPlan.IdOk(p.Name) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int at) && at >= 0 && at < OptionPlan.MostChoices) e.Picks[p.Name] = at;
            return e;
        }

        /// <summary>A choice value that can only name a file directly in the scenarios folder.</summary>
        public static bool FileOk(string file)
        {
            if (string.IsNullOrEmpty(file) || file.Length > 120 || file.Contains("..") || file == "*" || file == "(empty)") return false;
            foreach (char c in file) if (c == '/' || c == '\\' || c == ':' || c < ' ') return false;
            return true;
        }

        // A run record named by its file name only, never a folder.
        private static string RecordName(string s)
        {
            string n = (s ?? "").Trim();
            return n.EndsWith(".run.json", StringComparison.OrdinalIgnoreCase) && n.IndexOf('/') < 0 && n.IndexOf('\\') < 0 && !n.Contains("..") && n.Length <= 120 ? n : "";
        }

        private static void Trim(List<PlaylistResult> results) { if (results.Count > MostResults) results.RemoveRange(0, results.Count - MostResults); }

        // ----- is the scenario still the one that was pinned?

        /// <summary>
        /// Same, Missing (text null: the file is not there) or Changed, with the reason. An entry without a fingerprint cannot
        /// be told apart from a changed one, so it counts as changed: the host decides, never a silent switch to current content.
        /// </summary>
        public static string Identity(PlaylistEntry e, string text, out string why)
        {
            why = "";
            if (e == null) { why = "there is no such entry"; return Missing; }
            if (text == null) { why = "scenarios/" + e.File + ".json is not there any more"; return Missing; }
            CatalogueInfo.Identity(text, out string id, out int revision);
            string hash = ScenarioFile.Hash(text);
            var said = new List<string>();
            if (e.ScenarioId != "" && id != e.ScenarioId) said.Add(id == "" ? "the file no longer has scenario id " + e.ScenarioId : "the file now holds scenario " + id + ", not " + e.ScenarioId);
            if (revision != e.Revision) said.Add("revision " + e.Revision.ToString(Inv) + " when it was added, revision " + revision.ToString(Inv) + " now");
            if (e.Hash == "") said.Add("it was added without a fingerprint, so whether it changed cannot be told");
            else if (hash != e.Hash && said.Count == 0) said.Add("its text has changed since it was added, under the same revision number");
            if (said.Count == 0) return Same;
            why = string.Join("; ", said);
            return Changed;
        }

        /// <summary>Whether a run was of this entry's scenario: the ids decide when both have one, else the file name (as CatalogueInfo.Belongs).</summary>
        public static bool Belongs(PlaylistEntry e, RunSummary r)
        {
            if (e == null || r == null) return false;
            if (e.ScenarioId != "" && r.ScenarioId != "") return string.Equals(e.ScenarioId, r.ScenarioId, StringComparison.Ordinal);
            return r.Scenario != "" && string.Equals(CatalogueInfo.ValueOf(r.Scenario), e.File, StringComparison.OrdinalIgnoreCase);
        }

        // ----- runs seen

        /// <summary>
        /// How a run ended as far as is known: success or failed only when the game recorded it so; an end without a known
        /// result is unknown; no end while another mission (or none) runs is incomplete; "" while its own mission still runs.
        /// </summary>
        public static string ResultOf(RunSummary r, string session)
        {
            if (r == null) return "";
            if (r.Outcome == "refused") return Refused;
            if (r.Ended != "" || r.Result != "") return r.Result == Success || r.Result == Failed ? r.Result : Unknown;
            if (r.Session != "" && r.Session == (session ?? "")) return "";
            return Incomplete;
        }

        /// <summary>
        /// One run record looked at (session: the mission running now, "" for none). A new record counts for the entry set up
        /// before it began; a record already counted only gains a better-known end. Returns what changed, "" for nothing.
        /// </summary>
        public string Take(RunSummary r, string session)
        {
            if (r == null || RecordName(r.File) == "") return "";
            string result = ResultOf(r, session);
            PlaylistResult known = Results.Find(x => x.Record == r.File);
            if (known != null)
            {
                // The same record seen again: the host's word stands, and only an end that was not known can be filled in.
                if (known.Manual || result == "" || result == known.Result || (known.Result != Unknown && known.Result != Incomplete)) return "";
                known.Result = result;
                return Settle(known);
            }
            PlaylistEntry e = Current;
            if (!Armed || e == null || result == "" || string.CompareOrdinal(r.File, ArmedAfter) <= 0 || !Belongs(e, r)) return "";
            // Played from other content than was pinned (the file changed after it was set up): not this entry's run.
            if (e.Hash != "" && r.Hash != "" && r.Hash != e.Hash) return "";
            var res = new PlaylistResult { Record = r.File, Entry = Next, Label = NameOf(e), Result = result };
            Results.Add(res);
            Trim(Results);
            return Settle(res);
        }

        /// <summary>The host says how a run ended (success or failed). Labelled as manual; it moves the playlist on at most once.</summary>
        public bool Mark(string record, string result, out string said)
        {
            said = "";
            if (result != Success && result != Failed) { said = "a run can only be marked as a success or as failed"; return false; }
            PlaylistResult res = Results.Find(x => x.Record == (record ?? ""));
            if (res == null) { said = "that run was not played for this playlist"; return false; }
            if (res.Result == Refused) { said = "that run was refused, so nothing was played to mark"; return false; }
            res.Result = result; res.Manual = true;
            said = Settle(res);
            return true;
        }

        private string Settle(PlaylistResult res)
        {
            string label = res.Label == "" ? "entry " + (res.Entry + 1).ToString(Inv) : res.Label, how = ResultWords(res);
            if ((res.Result == Success || res.Result == Failed) && !res.Advanced && res.Entry == Next)
            {
                res.Advanced = true; Next++; Armed = false;
                PlaylistEntry n = Current;
                return label + ": " + how + ". Next: " + (n == null ? "nothing, the playlist is done" : NameOf(n));
            }
            return label + ": " + how + (res.Result == Unknown || res.Result == Incomplete ? ". The playlist stays on this entry until you mark how it ended" : "");
        }

        public static string ResultWords(PlaylistResult r)
        {
            if (r == null) return "";
            string how = r.Result == Success ? "a success" : r.Result == Failed ? "failed" : r.Result == Refused ? "refused, so it was not played"
                       : r.Result == Incomplete ? "incomplete: its end was not seen" : "unknown: the game did not say how it ended";
            return r.Manual ? how + " (marked by the host)" : how;
        }

        public static string NameOf(PlaylistEntry e) => e == null ? "" : e.Label != "" ? e.Label : e.File;

        // ----- the dice

        /// <summary>
        /// The run record the next mission repeats ("" for a new roll), or false with the reason: a repeat whose record is gone,
        /// refused or of another scenario, and a retry with no failed run of this scenario on record, are not played as anything else.
        /// runs: the run records on disk.
        /// </summary>
        public bool SeedFor(PlaylistEntry e, List<RunSummary> runs, out string repeatFile, out string why)
        {
            repeatFile = ""; why = "";
            if (e == null) { why = "there is no such entry"; return false; }
            if (e.Seed == NewRoll) return true;
            if (e.Seed == Repeat)
            {
                if (e.Record == "") { why = "no run is chosen to repeat"; return false; }
                RunSummary r = (runs ?? new List<RunSummary>()).Find(x => x != null && x.File == e.Record);
                if (r == null) { why = "run record " + e.Record + " is no longer on disk (MissionLab keeps the newest 60)"; return false; }
                if (r.Outcome != "used") { why = "run " + e.Record + " was refused, so it has no setup to repeat"; return false; }
                if (!Belongs(e, r)) { why = "run " + e.Record + " played another scenario"; return false; }
                repeatFile = e.Record;
                return true;
            }
            if (e.Seed == RetryFailed)
            {
                repeatFile = LastFailed(e, runs);
                if (repeatFile == "") { why = "no failed run of " + NameOf(e) + " is on record yet, so there is no setup to retry"; return false; }
                return true;
            }
            why = "the dice setting '" + e.Seed + "' is not known to this MissionLab";
            return false;
        }

        /// <summary>The newest run of the entry's scenario that failed, by the game's record or by the host's mark ("" for none).</summary>
        public string LastFailed(PlaylistEntry e, List<RunSummary> runs)
        {
            string best = "";
            foreach (RunSummary r in runs ?? new List<RunSummary>())
            {
                if (r == null || r.Outcome != "used" || !Belongs(e, r)) continue;
                PlaylistResult res = Results.Find(x => x.Record == r.File);
                bool failed = res != null && res.Manual ? res.Result == Failed : r.Result == Failed;
                if (failed && string.CompareOrdinal(r.File, best) > 0) best = r.File;
            }
            return best;
        }

        // ----- helpers

        private static string Q(string s) => "\"" + Guard.Esc(s ?? "") + "\"";
        private static string Text(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        private static int Int(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;

        private static string Clean(string s, int most)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").Trim()) if (c >= ' ') sb.Append(c);
            string t = sb.ToString();
            return t.Length > most ? t.Substring(0, most) : t;
        }
    }
}
