using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MissionLab.Core
{
    /// <summary>One fact of a debrief, and the recorded source it was read from (an event kind, "frames" or a run-record field).</summary>
    public sealed class DebriefFact
    {
        public string Key = "", Label = "", Value = "", Source = "";
        public double Count = -1;      // the number itself, when the fact is a count or a duration in seconds (-1: it is neither)
        public double At = -1;         // seconds into the recording, when the fact is one moment that can be jumped to (-1: none)
        public bool Guess;             // worked out by a guess from an older recording
    }

    /// <summary>One trigger firing, as the recording names it.</summary>
    public sealed class DebriefMilestone
    {
        public double At;
        public int Trigger;            // 0: the recording names it only by its words
        public string Name = "", Place = "", Source = "";
        public bool Guess;

        public string Words() =>
            (Trigger > 0 ? "trigger " + Trigger.ToString(CultureInfo.InvariantCulture) + (Name != "" ? " '" + Name + "'" : "") : Name != "" ? "'" + Name + "'" : "a trigger")
            + (Place != "" ? " in " + Place : "");
    }

    /// <summary>
    /// A short, factual debrief of one recorded mission (ML-024), read from its recording (missionlab-replay/1) and, when it can be
    /// found, its run record (missionlab-run/1). Every number says where it came from. Nothing the recording does not hold is filled
    /// in: health, shots fired and players' use of doors are not recorded, so they are named as not recorded rather than guessed.
    /// Recording semantics, as the plugin writes them (Replay.cs):
    ///   1  rows without identity; every enemy that left the game's list counts as down, edits included;
    ///   2  "removed" apart from "down", but an enemy that left for a reason not known still counts as down;
    ///   3  identity in every enemy row and enemy event; "down" only on the game's word, "gone" and "remade" apart;
    ///   4  as 3, and "alert" when an enemy first leaves its idle state and "fire" with the trigger's id when a trigger fires.
    /// Numbers an older recording can only give by a guess are marked so. Also the input of the unused-content report: which
    /// triggers fired and which enemies (by who they are) ever left their idle state.
    /// </summary>
    public sealed class Debrief
    {
        public const string Schema = "missionlab-debrief/1", ReplaySchema = "missionlab-replay/1";
        public const string FromFrames = "frames", FromRunEnd = "run.end", FromRunPlayers = "run.players", FromAlert = "alert", FromFire = "fire", FromNote = "note",
                            FromDown = "down", FromRemoved = "removed", FromRemade = "remade", FromGone = "gone", FromIntel = "intel", FromCommand = "command",
                            FromBudget = "budget", FromEnd = "end", FromPlayers = "players";
        public const string NotRecorded = "health, shots fired, door use by players";
        public const int WhoSemantics = 3, FireSemantics = 4;
        public const int MessageLines = 3;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Regex WhoPattern = new Regex("^(?:([sm])(\\d+))?(?:v(\\d+))?$", RegexOptions.CultureInvariant);

        public string Replay = "", Run = "", Scene = "", Plugin = "", Scenario = "", ScenarioId = "", ScenarioHash = "", ResolvedHash = "", Outcome = "";
        public int Semantics = 1;
        public bool HasRun;                                  // its run record was found and read
        public MissionPlan Plan;                             // the mission as it was played (the run's resolved plan, else the recording's own); null: unknown
        public readonly List<DebriefFact> Facts = new List<DebriefFact>();
        public readonly List<DebriefMilestone> Milestones = new List<DebriefMilestone>();
        public readonly List<string> Players = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public readonly List<int> Off = new List<int>();             // triggers that lost their roll (run record only)
        public readonly List<int> LeftOut = new List<int>();         // the scenario's spawns that lost their roll, by their place in the file
        public readonly SortedSet<int> Fired = new SortedSet<int>(); // trigger ids seen to fire (from "fire" events; from the wording, older recordings)
        public readonly Dictionary<string, bool> Enemies = new Dictionary<string, bool>(StringComparer.Ordinal);   // authored enemy -> it left its idle state

        public bool FiresRecorded => Semantics >= FireSemantics;
        public bool WhoRecorded => Semantics >= WhoSemantics;

        private double _recorded, _contact = -1;
        private int _down, _edits, _gone, _cards, _fired, _triggers = -1;

        private struct Ev { public double T; public string Kind, Text; public bool HasAt; public V3 At; public int Id; }
        private struct Row { public int Id; public V3 Pos; public string State, Who; }

        /// <summary>
        /// Reads a recording. runText is asked for the run record named in the recording's header and answers its text, or null
        /// when there is none. Null with error set when the text is not a readable recording.
        /// </summary>
        public static Debrief Read(string replayName, string replayJson, Func<string, string> runText, out string error)
        {
            error = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(replayJson ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object || Str(r, "schema") != ReplaySchema) { error = "this is not a MissionLab recording (" + ReplaySchema + ")"; return null; }
                    var d = new Debrief
                    {
                        Replay = replayName ?? "", Run = Str(r, "run"), Scene = Str(r, "scene"), Plugin = Str(r, "plugin"), Scenario = Str(r, "scenario"), ScenarioHash = Str(r, "scenarioHash"),
                        Semantics = r.TryGetProperty("semantics", out JsonElement sm) && sm.ValueKind == JsonValueKind.Number && sm.TryGetInt32(out int s) && s >= 1 ? s : 1
                    };
                    var frames = new List<KeyValuePair<double, List<Row>>>();
                    if (r.TryGetProperty("frames", out JsonElement fr) && fr.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement f in fr.EnumerateArray())
                        {
                            if (f.ValueKind != JsonValueKind.Array || f.GetArrayLength() < 3 || !Number(f[0], out double t) || f[1].ValueKind != JsonValueKind.Array || f[2].ValueKind != JsonValueKind.Array) continue;
                            var rows = new List<Row>();
                            foreach (JsonElement q in f[2].EnumerateArray())
                                if (EnemyRow(q, out Row row)) rows.Add(row);
                            frames.Add(new KeyValuePair<double, List<Row>>(t, rows));
                        }
                    if (frames.Count == 0) { error = "it is a recording without any readable moments"; return null; }
                    var events = new List<Ev>();
                    if (r.TryGetProperty("events", out JsonElement evs) && evs.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in evs.EnumerateArray())
                        {
                            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 3 || !Number(e[0], out double t)) continue;
                            var ev = new Ev { T = t, Kind = e[1].ValueKind == JsonValueKind.String ? e[1].GetString() ?? "" : "", Text = e[2].ValueKind == JsonValueKind.String ? e[2].GetString() ?? "" : "" };
                            if (e.GetArrayLength() > 3) ev.HasAt = Vec(e[3], out ev.At);
                            if (e.GetArrayLength() > 4 && e[4].ValueKind == JsonValueKind.Number && e[4].TryGetInt32(out int id)) ev.Id = id;
                            events.Add(ev);
                        }
                    if (r.TryGetProperty("players", out JsonElement pl) && pl.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement p in pl.EnumerateArray()) if (p.ValueKind == JsonValueKind.String) d.Players.Add(p.GetString() ?? "");
                    if (r.TryGetProperty("leftOut", out JsonElement lo) && lo.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement x in lo.EnumerateArray()) if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out int at) && at >= 0) d.LeftOut.Add(at);
                    string header = r.TryGetProperty("plan", out JsonElement plan) && plan.ValueKind == JsonValueKind.Object ? plan.GetRawText() : "";
                    int runPlayers = -1; double runSeconds = -1; string runResult = "";
                    string runJson = d.Run != "" && runText != null ? runText(d.Run) : null;
                    if (runJson != null) d.ReadRun(runJson, ref runPlayers, ref runSeconds, ref runResult, ref header);
                    if (d.Plan == null && header != "") d.Plan = PlanOf(header);
                    if (d.ScenarioId == "" && d.Plan != null) d.ScenarioId = d.Plan.Id ?? "";
                    d.Work(frames, events, runPlayers, runSeconds, runResult);
                    return d;
                }
            }
            catch (JsonException e) { error = "the recording is not readable JSON: " + e.Message; return null; }
        }

        private void ReadRun(string json, ref int players, ref double seconds, ref string result, ref string header)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return;
                    HasRun = true;
                    if (Str(r, "authoredHash") != "") ScenarioHash = Str(r, "authoredHash");
                    if (Str(r, "scenario") != "") Scenario = Str(r, "scenario");
                    ScenarioId = Str(r, "scenarioId"); Outcome = Str(r, "outcome");
                    if (r.TryGetProperty("players", out JsonElement p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int n)) players = n;
                    if (r.TryGetProperty("end", out JsonElement end) && end.ValueKind == JsonValueKind.Object)
                    {
                        result = Str(end, "result");
                        seconds = end.TryGetProperty("seconds", out JsonElement sec) && Number(sec, out double v) && v >= 0 ? v : -1;
                        if (seconds < 0) seconds = -2;          // an end without its length
                    }
                    if (r.TryGetProperty("leftOut", out JsonElement lo) && lo.ValueKind == JsonValueKind.Object)
                    {
                        if (lo.TryGetProperty("triggersOff", out JsonElement off) && off.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement x in off.EnumerateArray()) if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out int id)) Off.Add(id);
                        if (LeftOut.Count == 0 && lo.TryGetProperty("spawns", out JsonElement sp) && sp.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement x in sp.EnumerateArray())
                                if (x.ValueKind == JsonValueKind.Object && x.TryGetProperty("index", out JsonElement ix) && ix.ValueKind == JsonValueKind.Number && ix.TryGetInt32(out int at) && at >= 0) LeftOut.Add(at);
                        LeftOut.Sort();
                    }
                    if (r.TryGetProperty("resolved", out JsonElement res) && res.ValueKind == JsonValueKind.Object)
                    {
                        string raw = res.GetRawText();
                        ResolvedHash = ScenarioFile.Hash(raw);
                        MissionPlan resolved = PlanOf(raw);
                        if (resolved != null) { Plan = resolved; header = raw; }
                    }
                }
            }
            catch (JsonException) { HasRun = false; }
        }

        private static MissionPlan PlanOf(string json)
        {
            try { return MissionPlan.FromJson(json); }
            catch (Exception) { return null; }       // a plan that cannot be read leaves names and places unknown, nothing else
        }

        private void Work(List<KeyValuePair<double, List<Row>>> frames, List<Ev> events, int runPlayers, double runSeconds, string runResult)
        {
            bool guessed = Semantics < WhoSemantics;
            // Who each row id was, from the rows that say so.
            var who = new Dictionary<int, string>();
            foreach (KeyValuePair<double, List<Row>> f in frames)
                foreach (Row q in f.Value)
                {
                    if (q.Who != "" && !who.ContainsKey(q.Id)) who[q.Id] = q.Who;
                    if (WhoRecorded && q.Who.StartsWith("s", StringComparison.Ordinal))
                    {
                        bool active = q.State != "" && q.State != "I";
                        Enemies[q.Who] = (Enemies.TryGetValue(q.Who, out bool was) && was) || active;
                    }
                }
            string Call(int id) => who.TryGetValue(id, out string w) && WhoName(w) != "" ? WhoName(w) : "enemy " + id.ToString(Inv);

            _recorded = Math.Max(0, frames[frames.Count - 1].Key - frames[0].Key);
            Add("recorded", "Recorded", Mmss(_recorded), FromFrames, _recorded, -1, false);
            if (!HasRun) Add("elapsed", "Setup to end", "not known without the run record", FromRunEnd, -1, -1, false);
            else if (runSeconds >= 0) Add("elapsed", "Setup to end", Mmss(runSeconds), FromRunEnd, runSeconds, -1, false);
            else Add("elapsed", "Setup to end", runSeconds == -2 ? "not known: the run record has an end without its length" : "not known: the end was not seen", FromRunEnd, -1, -1, false);
            if (runResult != "") Add("result", "How it ended", runResult, FromRunEnd, -1, -1, false);

            // First recorded contact: the first "alert" event; recordings before semantics 4 have none, so their rows' states are read.
            if (FiresRecorded)
            {
                int i = events.FindIndex(e => e.Kind == FromAlert);
                if (i < 0) Add("contact", "First recorded contact", "none recorded", FromAlert, -1, -1, false);
                else
                {
                    Ev e = events[i]; _contact = e.T;
                    Add("contact", "First recorded contact", Mmss(e.T) + ", " + Call(e.Id) + (e.HasAt ? Where(e.At) : ""), FromAlert, -1, e.T, false);
                }
            }
            else
            {
                bool found = false;
                foreach (KeyValuePair<double, List<Row>> f in frames)
                {
                    int k = f.Value.FindIndex(q => q.State != "" && q.State != "I");
                    if (k < 0) continue;
                    Row q = f.Value[k]; _contact = f.Key; found = true;
                    Add("contact", "First recorded contact", Mmss(f.Key) + ", " + Call(q.Id) + Where(q.Pos), FromFrames, -1, f.Key, guessed);
                    break;
                }
                if (!found) Add("contact", "First recorded contact", "none recorded", FromFrames, -1, -1, guessed);
            }

            // Trigger milestones: "fire" events, or the timeline's wording in older recordings.
            int firings = 0;
            foreach (Ev e in events)
            {
                if (FiresRecorded && e.Kind == FromFire)
                {
                    TriggerPlan t = e.Id > 0 && Plan != null ? Plan.Triggers.Find(x => x.Id == e.Id) : null;
                    Milestones.Add(new DebriefMilestone { At = e.T, Trigger = e.Id, Name = t != null ? t.Name ?? "" : "", Place = e.HasAt ? PlaceAt(e.At) : "", Source = FromFire });
                    if (e.Id > 0) Fired.Add(e.Id);
                    firings++;
                }
                else if (!FiresRecorded && e.Kind == FromNote && FiredWords(e.Text, out int id, out string name))
                {
                    if (id == 0 && name != "" && Plan != null && Plan.Triggers.FindAll(x => x.Name == name).Count == 1) id = Plan.Triggers.Find(x => x.Name == name).Id;
                    TriggerPlan t = id > 0 && Plan != null ? Plan.Triggers.Find(x => x.Id == id) : null;
                    Milestones.Add(new DebriefMilestone { At = e.T, Trigger = id, Name = t != null ? t.Name ?? "" : name, Source = FromNote, Guess = true });
                    if (id > 0) Fired.Add(id);
                    firings++;
                }
            }
            if (Plan != null) { _triggers = 0; foreach (TriggerPlan t in Plan.Triggers) if (!Off.Contains(t.Id)) _triggers++; }
            int distinct = 0;
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (DebriefMilestone m in Milestones) if (seen.Add(m.Trigger > 0 ? "#" + m.Trigger.ToString(Inv) : "'" + m.Name)) distinct++;
            }
            Add("fired", "Triggers fired", distinct.ToString(Inv) + (_triggers >= 0 ? " of " + _triggers.ToString(Inv) : "") + (firings > distinct ? ", " + firings.ToString(Inv) + " firings" : ""),
                FiresRecorded ? FromFire : FromNote, distinct, -1, !FiresRecorded);
            _fired = distinct;

            // Outcomes: the game's own word, edits, and disappearances nobody explained, never added together.
            int Count(string kind) { int n = 0; foreach (Ev e in events) if (e.Kind == kind) n++; return n; }
            _down = Count(FromDown);
            int removed = Count(FromRemoved), remade = Count(FromRemade);
            _gone = Count(FromGone); _edits = removed + remade;
            if (!guessed) Add("down", "Enemies down, as the game reported", _down.ToString(Inv), FromDown, _down, FirstAt(events, FromDown), false);
            else Add("down", "Enemies down or gone", _down.ToString(Inv), FromDown, _down, FirstAt(events, FromDown), true);
            if (Semantics >= 2) Add("removed", "Taken out by an edit, not counted as down", removed.ToString(Inv), FromRemoved, removed, -1, false);
            if (!guessed)
            {
                Add("remade", "Made again after an edit", remade.ToString(Inv), FromRemade, remade, -1, false);
                Add("gone", "Gone for a reason not known", _gone.ToString(Inv), FromGone, _gone, FirstAt(events, FromGone), false);
            }
            int intelAt = events.FindLastIndex(e => e.Kind == FromIntel);
            if (intelAt >= 0)
            {
                string said = events[intelAt].Text;
                int n = said.StartsWith("an intel item", StringComparison.Ordinal) ? 1 : LeadingNumber(said);
                if (n >= 0) Add("intel", "Intel items taken", n.ToString(Inv), FromIntel, n, events[intelAt].T, false);
            }
            int asked = Count(FromCommand);
            if (asked > 0) Add("command", "Game Master requests", asked.ToString(Inv), FromCommand, asked, -1, false);
            string cards = Cards(events, out int fired);
            _cards = fired;
            if (cards != "") Add("budget", "Game Master action cards", cards, FromBudget, fired, -1, false);
            int endAt = events.FindLastIndex(e => e.Kind == FromEnd);
            if (endAt >= 0) Add("end", "Recording ended", Mmss(events[endAt].T) + ", " + events[endAt].Text, FromEnd, -1, events[endAt].T, false);
            Add("players", "Players seen", Players.Count.ToString(Inv) + (Players.Count > 0 ? " (" + string.Join(", ", Players) + ")" : ""), FromPlayers, Players.Count, -1, false);
            if (runPlayers >= 0) Add("squad", "Squad size rolled for", runPlayers.ToString(Inv), FromRunPlayers, runPlayers, -1, false);
            Add("notRecorded", "Not recorded", NotRecorded, "", -1, -1, false);

            if (guessed)
                Notes.Add("An older recording (semantics " + Semantics.ToString(Inv) + "): enemies are linked by a guess and every enemy that left the game's list counts as down"
                          + (Semantics < 2 ? ", even one an edit took out" : "") + ". Its states may show an enemy's first moments or a gun truck's gunner as alerted. Numbers marked (a guess) come from it.");
            if (!FiresRecorded)
                Notes.Add("Made before trigger firings and alerts were recorded as events of their own (MissionLab 0.26): milestones are read from the timeline's wording, and first contact from the enemies' recorded states.");
            if (!HasRun) Notes.Add("No run record was found for this recording, so the time from setup to end, how it ended and the squad size are not known.");
        }

        private void Add(string key, string label, string value, string source, double count, double at, bool guess) =>
            Facts.Add(new DebriefFact { Key = key, Label = label, Value = value, Source = source, Count = count, At = at, Guess = guess });

        private static double FirstAt(List<Ev> events, string kind)
        {
            int i = events.FindIndex(e => e.Kind == kind);
            return i < 0 ? -1 : events[i].T;
        }

        private string Where(V3 p)
        {
            string n = PlaceAt(p);
            return n != "" ? " in " + n : "";
        }

        private string PlaceAt(V3 p) => Plan != null ? Plan.PlaceAt(p) : "";

        // The Game Master's action cards (ML-025), counted by what each "budget" event says it is: the word before the action's id.
        private static string Cards(List<Ev> events, out int fired)
        {
            string[] verbs = { "request", "spend", "free", "fire", "wait", "refused", "cancel", "refund" };
            string[] words = { "asked", "paid", "free", "fired", "waited", "refused", "cancelled", "refunded" };
            var n = new int[verbs.Length];
            foreach (Ev e in events)
            {
                if (e.Kind != FromBudget) continue;
                int sp = e.Text.IndexOf(' ');
                string verb = sp > 0 ? e.Text.Substring(0, sp) : e.Text;
                int k = Array.IndexOf(verbs, verb);
                if (k >= 0) n[k]++;
            }
            fired = n[3];
            var parts = new List<string>();
            for (int k = 0; k < verbs.Length; k++) if (n[k] > 0) parts.Add(n[k].ToString(Inv) + " " + words[k]);
            return string.Join(", ", parts);
        }

        /// <summary>
        /// A timeline line of an older recording that says a trigger fired: "zone 'Gate' reached: ..." or "trigger 3 (time): ...",
        /// with the enemies it brought. Its id when it names one, else its name.
        /// </summary>
        private static bool FiredWords(string text, out int id, out string name)
        {
            id = 0; name = "";
            string rest = text.StartsWith("zone ", StringComparison.Ordinal) ? text.Substring(5) : text.StartsWith("trigger ", StringComparison.Ordinal) ? text.Substring(8) : null;
            if (rest == null || !(text.Contains(" enemy appears") || text.Contains(" enemies appear"))) return false;
            if (rest.StartsWith("'", StringComparison.Ordinal))
            {
                int close = rest.IndexOf("' ", 1, StringComparison.Ordinal);
                if (close < 1) return false;
                name = rest.Substring(1, close - 1);
                return true;
            }
            int digits = 0;
            while (digits < rest.Length && rest[digits] >= '0' && rest[digits] <= '9') digits++;
            return digits > 0 && int.TryParse(rest.Substring(0, digits), NumberStyles.None, Inv, out id) && id > 0;
        }

        private static int LeadingNumber(string text)
        {
            int digits = 0;
            while (digits < text.Length && text[digits] >= '0' && text[digits] <= '9') digits++;
            return digits > 0 && int.TryParse(text.Substring(0, digits), NumberStyles.None, Inv, out int n) ? n : -1;
        }

        /// <summary>"spawn #5", "marker 12", "spawn #2, gun truck 1"; "" when the identity cannot be read.</summary>
        public static string WhoName(string who)
        {
            if (string.IsNullOrEmpty(who)) return "";
            Match m = WhoPattern.Match(who);
            if (!m.Success) return "";
            var said = new List<string>();
            if (m.Groups[1].Value == "s" && int.TryParse(m.Groups[2].Value, NumberStyles.None, Inv, out int s)) said.Add("spawn #" + (s + 1).ToString(Inv));
            if (m.Groups[1].Value == "m") said.Add("marker " + m.Groups[2].Value);
            if (m.Groups[3].Success && m.Groups[3].Value != "") said.Add("gun truck " + m.Groups[3].Value);
            return string.Join(", ", said);
        }

        /// <summary>The scenario's spawn behind an authored identity ("s4", "s4v1"), in the plan as played; null when not known.</summary>
        public SpawnPlan SpawnOf(string who)
        {
            if (Plan == null || string.IsNullOrEmpty(who)) return null;
            Match m = WhoPattern.Match(who);
            if (!m.Success || m.Groups[1].Value != "s" || !int.TryParse(m.Groups[2].Value, NumberStyles.None, Inv, out int authored) || LeftOut.Contains(authored)) return null;
            int played = authored;
            foreach (int lost in LeftOut) if (lost < authored) played--;
            return played >= 0 && played < Plan.Spawns.Count ? Plan.Spawns[played] : null;
        }

        public static string Mmss(double t)
        {
            t = Math.Max(0, t);
            return ((long)Math.Floor(t / 60)).ToString(Inv) + ":" + ((int)Math.Floor(t % 60)).ToString("00", Inv);
        }

        /// <summary>The debrief as plain text, to copy and hand round. The page's copy (32a-debrief.js) writes the same text.</summary>
        public string Text()
        {
            var lines = new List<string> { "MissionLab debrief: " + (Replay != "" ? Replay : "a recording") };
            if (Scene != "") lines.Add("Map: " + Scene);
            if (Scenario != "" || ScenarioId != "") lines.Add("Scenario: " + (Scenario != "" ? Scenario : ScenarioId) + (Scenario != "" && ScenarioId != "" ? " (" + ScenarioId + ")" : ""));
            lines.Add("Recording semantics " + Semantics.ToString(Inv) + (Plugin != "" ? ", plugin " + Plugin : ""));
            foreach (DebriefFact f in Facts)
            {
                lines.Add(f.Label + ": " + f.Value + (f.Source != "" ? " [" + f.Source + "]" : "") + (f.Guess ? " (a guess)" : ""));
                if (f.Key == "fired")
                    foreach (DebriefMilestone m in Milestones) lines.Add("  " + Mmss(m.At) + " " + m.Words() + " [" + m.Source + "]" + (m.Guess ? " (a guess)" : ""));
            }
            foreach (string n in Notes) lines.Add("Note: " + n);
            return string.Join("\n", lines);
        }

        /// <summary>Three short lines for every player's screen at the mission's end (each within a game message's length).</summary>
        public List<string> Message()
        {
            bool guessed = Semantics < WhoSemantics;
            var lines = new List<string>
            {
                "Debrief: " + Mmss(_recorded) + " recorded" + (_contact >= 0 ? ", first contact at " + Mmss(_contact) : ", no contact recorded") + ".",
                "Enemies down" + (guessed ? " (a guess)" : "") + ": " + _down.ToString(Inv) + (_edits > 0 ? "; " + _edits.ToString(Inv) + " taken out by edits, not counted" : "")
                    + (_gone > 0 ? "; " + _gone.ToString(Inv) + " gone, reason not known" : "") + ".",
                "Triggers fired: " + _fired.ToString(Inv) + (_triggers >= 0 ? " of " + _triggers.ToString(Inv) : "") + (_cards > 0 ? "; Game Master cards fired: " + _cards.ToString(Inv) : "") + "."
            };
            for (int i = 0; i < lines.Count; i++) if (lines[i].Length > TriggerPlan.MessageLength) lines[i] = lines[i].Substring(0, TriggerPlan.MessageLength);
            return lines;
        }

        public string ToJson()
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\"schema\":").Append(Q(Schema)).Append(",\"replay\":").Append(Q(Replay)).Append(",\"run\":").Append(Q(Run)).Append(",\"semantics\":").Append(Semantics.ToString(Inv))
              .Append(",\"scene\":").Append(Q(Scene)).Append(",\"plugin\":").Append(Q(Plugin)).Append(",\"scenario\":").Append(Q(Scenario)).Append(",\"scenarioId\":").Append(Q(ScenarioId))
              .Append(",\"scenarioHash\":").Append(Q(ScenarioHash)).Append(",\"resolvedHash\":").Append(Q(ResolvedHash)).Append(",\"hasRun\":").Append(HasRun ? "true" : "false").Append(",\"facts\":[");
            for (int i = 0; i < Facts.Count; i++)
            {
                DebriefFact f = Facts[i];
                sb.Append(i > 0 ? "," : "").Append("{\"key\":").Append(Q(f.Key)).Append(",\"label\":").Append(Q(f.Label)).Append(",\"value\":").Append(Q(f.Value))
                  .Append(",\"count\":").Append(f.Count >= 0 ? N(f.Count) : "null").Append(",\"at\":").Append(f.At >= 0 ? N(f.At) : "null")
                  .Append(",\"source\":").Append(Q(f.Source)).Append(",\"guess\":").Append(f.Guess ? "true" : "false").Append('}');
            }
            sb.Append("],\"milestones\":[");
            for (int i = 0; i < Milestones.Count; i++)
            {
                DebriefMilestone m = Milestones[i];
                sb.Append(i > 0 ? "," : "").Append("{\"at\":").Append(N(m.At)).Append(",\"trigger\":").Append(m.Trigger.ToString(Inv)).Append(",\"name\":").Append(Q(m.Name))
                  .Append(",\"place\":").Append(Q(m.Place)).Append(",\"source\":").Append(Q(m.Source)).Append(",\"guess\":").Append(m.Guess ? "true" : "false").Append('}');
            }
            sb.Append("],\"players\":").Append(List(Players)).Append(",\"notRecorded\":").Append(List(new List<string>(NotRecorded.Split(new[] { ", " }, StringSplitOptions.None))))
              .Append(",\"notes\":").Append(List(Notes)).Append(",\"message\":").Append(List(Message())).Append(",\"text\":").Append(Q(Text())).Append('}');
            return sb.ToString();
        }

        internal static string Q(string s) => "\"" + Guard.Esc(s ?? "") + "\"";
        internal static string N(double v) => Math.Round(v, 2).ToString(Inv);
        internal static string List(List<string> items)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(items[i]));
            return sb.Append(']').ToString();
        }

        internal static string Str(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static bool Number(JsonElement e, out double v)
        {
            v = 0;
            return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out v) && !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private static bool Vec(JsonElement e, out V3 p)
        {
            p = new V3();
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 3 || !Number(e[0], out double x) || !Number(e[1], out double y) || !Number(e[2], out double z)) return false;
            p = new V3((float)x, (float)y, (float)z);
            return true;
        }

        // [id, x, y, z, yaw, state, who]: a row without a place is not read; an identity that cannot be read is dropped (as the page does).
        private static bool EnemyRow(JsonElement q, out Row row)
        {
            row = new Row { State = "", Who = "" };
            if (q.ValueKind != JsonValueKind.Array || q.GetArrayLength() < 4 || !Number(q[0], out double id) || !Number(q[1], out double x) || !Number(q[2], out double y) || !Number(q[3], out double z)) return false;
            row.Id = (int)id; row.Pos = new V3((float)x, (float)y, (float)z);
            if (q.GetArrayLength() > 5 && q[5].ValueKind == JsonValueKind.String) row.State = q[5].GetString() ?? "";
            if (q.GetArrayLength() > 6 && q[6].ValueKind == JsonValueKind.String && WhoName(q[6].GetString()) != "") row.Who = q[6].GetString();
            return true;
        }
    }

    /// <summary>One suggestion of the unused-content report: a trigger or an enemy to look at again.</summary>
    public sealed class UnusedSuggestion
    {
        public const string Trigger = "trigger", Enemy = "enemy";
        public string Kind = "", Ref = "", Name = "", Text = "";
    }

    /// <summary>Runs whose scenario and setup after the dice match, so what they did can be compared.</summary>
    public sealed class UnusedGroup
    {
        public string ScenarioHash = "", ResolvedHash = "";
        public readonly List<string> Runs = new List<string>();
        public int TriggerRuns, EnemyRuns;
        public readonly List<UnusedSuggestion> Suggestions = new List<UnusedSuggestion>();
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Content that was never used across comparable runs (ML-024): triggers that did not fire, and authored enemies that never left
    /// their idle state, in any run of the group. Runs are compared only when both the scenario's fingerprint and the fingerprint of
    /// its setup after the dice (ScenarioFile.Hash of the run record's "resolved" text) match. Every item is a suggestion for the
    /// author to review, never a failure: a guard can add tension without ever engaging, and a trigger can be a backup.
    /// </summary>
    public sealed class UnusedReport
    {
        public const string Schema = "missionlab-unused/1";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string ScenarioId = "";
        public int Looked;                                  // runs of this scenario that were looked at
        public readonly List<UnusedGroup> Groups = new List<UnusedGroup>();
        public readonly List<KeyValuePair<string, string>> Skipped = new List<KeyValuePair<string, string>>();    // run record -> why it was not compared

        /// <summary>
        /// runs: run records (file name, text), newest first. replayText answers a recording's text by its file name (null: not there).
        /// Only runs of scenarioId are looked at.
        /// </summary>
        public static UnusedReport Build(string scenarioId, IList<KeyValuePair<string, string>> runs, Func<string, string> replayText)
        {
            var report = new UnusedReport { ScenarioId = scenarioId ?? "" };
            var groups = new List<KeyValuePair<string, List<Debrief>>>();
            foreach (KeyValuePair<string, string> run in runs)
            {
                string id = "", outcome = "", replay = "", resolved = "";
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(run.Value ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                    {
                        JsonElement r = doc.RootElement;
                        if (r.ValueKind != JsonValueKind.Object) continue;
                        id = Debrief.Str(r, "scenarioId"); outcome = Debrief.Str(r, "outcome"); replay = Debrief.Str(r, "replay");
                        resolved = r.TryGetProperty("resolved", out JsonElement res) && res.ValueKind == JsonValueKind.Object ? "y" : "";
                    }
                }
                catch (JsonException) { continue; }
                if (id == "" || id != report.ScenarioId) continue;
                report.Looked++;
                if (outcome == "refused" || resolved == "") { report.Skipped.Add(new KeyValuePair<string, string>(run.Key, "the setup was refused, so nothing was played")); continue; }
                if (replay == "") { report.Skipped.Add(new KeyValuePair<string, string>(run.Key, "no recording belongs to it (recording off, or the mission ended within seconds)")); continue; }
                string text = replayText == null ? null : replayText(replay);
                if (text == null) { report.Skipped.Add(new KeyValuePair<string, string>(run.Key, "its recording " + replay + " is no longer there (only the newest are kept)")); continue; }
                string runName = run.Key, runJson = run.Value;
                Debrief d = Debrief.Read(replay, text, n => n == runName ? runJson : null, out string error);
                if (d == null) { report.Skipped.Add(new KeyValuePair<string, string>(run.Key, "its recording could not be read: " + error)); continue; }
                if (!d.HasRun) { report.Skipped.Add(new KeyValuePair<string, string>(run.Key, "its recording names another run record")); continue; }
                if (d.ScenarioHash == "" || d.ResolvedHash == "") { report.Skipped.Add(new KeyValuePair<string, string>(run.Key, "it has no fingerprint of its scenario or setup to compare")); continue; }
                string key = d.ScenarioHash + "|" + d.ResolvedHash;
                int at = groups.FindIndex(g => g.Key == key);
                if (at < 0) { groups.Add(new KeyValuePair<string, List<Debrief>>(key, new List<Debrief>())); at = groups.Count - 1; }
                groups[at].Value.Add(d);
            }
            // The largest group first; among equals, the one with the newest run.
            var order = new List<int>();
            for (int i = 0; i < groups.Count; i++) order.Add(i);
            order.Sort((a, b) => groups[b].Value.Count != groups[a].Value.Count ? groups[b].Value.Count.CompareTo(groups[a].Value.Count) : a.CompareTo(b));
            foreach (int i in order) report.Groups.Add(Compare(groups[i].Value));
            return report;
        }

        /// <summary>The suggestions for runs already known to be comparable (one run on its own too: the page's offline report).</summary>
        public static UnusedGroup Compare(IList<Debrief> runs)
        {
            var g = new UnusedGroup();
            if (runs == null || runs.Count == 0) return g;
            Debrief first = runs[0];
            g.ScenarioHash = first.ScenarioHash; g.ResolvedHash = first.ResolvedHash;
            var fired = new HashSet<int>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var active = new HashSet<string>(StringComparer.Ordinal);
            bool allRuns = true;
            foreach (Debrief d in runs)
            {
                g.Runs.Add(d.Run != "" ? d.Run : d.Replay);
                if (!d.HasRun) allRuns = false;
                if (d.FiresRecorded) { g.TriggerRuns++; foreach (int id in d.Fired) fired.Add(id); }
                if (d.WhoRecorded)
                {
                    g.EnemyRuns++;
                    foreach (KeyValuePair<string, bool> e in d.Enemies) { seen.Add(e.Key); if (e.Value) active.Add(e.Key); }
                }
            }
            if (first.Plan != null && g.TriggerRuns > 0)
                foreach (TriggerPlan t in first.Plan.Triggers)
                {
                    if (first.Off.Contains(t.Id) || fired.Contains(t.Id)) continue;
                    string name = "trigger " + t.Id.ToString(Inv) + (string.IsNullOrEmpty(t.Name) ? "" : " '" + t.Name + "'");
                    g.Suggestions.Add(new UnusedSuggestion
                    {
                        Kind = UnusedSuggestion.Trigger, Ref = t.Id.ToString(Inv), Name = name,
                        Text = Upper(name) + " did not fire in " + Runs(g.TriggerRuns) + ". Worth a look, not a fault: it may be a backup, or out of the way."
                    });
                }
            var quiet = new List<string>();
            foreach (string who in seen) if (!active.Contains(who)) quiet.Add(who);
            quiet.Sort(ByWho);
            foreach (string who in quiet)
            {
                SpawnPlan s = first.SpawnOf(who);
                string name = Debrief.WhoName(who);
                if (s != null && s.Tag != "") name += ", tagged '" + s.Tag + "'";
                if (s != null && s.Trigger > 0) name += ", brought in by trigger " + s.Trigger.ToString(Inv);
                g.Suggestions.Add(new UnusedSuggestion
                {
                    Kind = UnusedSuggestion.Enemy, Ref = who, Name = name,
                    Text = Upper(name) + " never left its idle state in " + Runs(g.EnemyRuns) + ". Worth a look, not a fault: a guard can add tension without ever engaging."
                });
            }
            if (g.TriggerRuns == 0) g.Notes.Add("None of these recordings note when triggers fire (made before MissionLab 0.26), so triggers are not judged.");
            if (g.EnemyRuns == 0) g.Notes.Add("None of these recordings say which enemy is which (older recordings), so enemies are not judged.");
            if (first.Plan == null) g.Notes.Add("The mission as it was played could not be read, so its triggers are not known.");
            if (!allRuns) g.Notes.Add("Without the run record, a trigger that lost its roll in this run is listed too, and the setup cannot be compared with other runs.");
            if (runs.Count == 1) g.Notes.Add("One run says little: look again after a few more runs of the same setup.");
            return g;
        }

        private static string Runs(int n) => n == 1 ? "the one run looked at" : "any of the " + n.ToString(Inv) + " runs looked at";
        private static string Upper(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // Spawn order: s2 before s10, a truck's gunner right after its truck, markers last.
        private static int ByWho(string a, string b)
        {
            int Index(string w) { int d = 1; while (d < w.Length && char.IsDigit(w[d])) d++; return d > 1 && int.TryParse(w.Substring(1, d - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue; }
            int c = Index(a).CompareTo(Index(b));
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }

        public string Text()
        {
            var lines = new List<string> { "Unused content in " + (ScenarioId != "" ? ScenarioId : "this scenario") + ": suggestions for review, from " + Looked.ToString(Inv) + (Looked == 1 ? " run" : " runs") + " looked at" };
            if (Groups.Count == 0) lines.Add("No comparable runs with a recording were found.");
            for (int i = 0; i < Groups.Count; i++) lines.AddRange(GroupLines(Groups[i], "Setup " + (i + 1).ToString(Inv)));
            foreach (KeyValuePair<string, string> s in Skipped) lines.Add("Not compared: " + s.Key + " (" + s.Value + ")");
            return string.Join("\n", lines);
        }

        /// <summary>One group as text lines; the page writes the same lines for its one-run report.</summary>
        public static List<string> GroupLines(UnusedGroup g, string title)
        {
            var lines = new List<string> { title + ": " + g.Runs.Count.ToString(Inv) + (g.Runs.Count == 1 ? " run" : " comparable runs")
                                           + (g.ScenarioHash != "" && g.ResolvedHash != "" ? " (scenario " + Short(g.ScenarioHash) + ", setup " + Short(g.ResolvedHash) + ")" : "") };
            if (g.Suggestions.Count == 0) lines.Add("- Nothing stood unused.");
            foreach (UnusedSuggestion s in g.Suggestions) lines.Add("- " + s.Text);
            foreach (string n in g.Notes) lines.Add("  Note: " + n);
            return lines;
        }

        private static string Short(string hash)
        {
            int c = (hash ?? "").IndexOf(':');
            string h = c >= 0 ? hash.Substring(c + 1) : hash ?? "";
            return h.Length > 8 ? h.Substring(0, 8) : h;
        }

        public string ToJson()
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"schema\":").Append(Debrief.Q(Schema)).Append(",\"scenarioId\":").Append(Debrief.Q(ScenarioId)).Append(",\"looked\":").Append(Looked.ToString(Inv)).Append(",\"groups\":[");
            for (int i = 0; i < Groups.Count; i++)
            {
                UnusedGroup g = Groups[i];
                sb.Append(i > 0 ? "," : "").Append("{\"scenarioHash\":").Append(Debrief.Q(g.ScenarioHash)).Append(",\"resolvedHash\":").Append(Debrief.Q(g.ResolvedHash))
                  .Append(",\"runs\":").Append(Debrief.List(g.Runs)).Append(",\"triggerRuns\":").Append(g.TriggerRuns.ToString(Inv)).Append(",\"enemyRuns\":").Append(g.EnemyRuns.ToString(Inv)).Append(",\"suggestions\":[");
                for (int k = 0; k < g.Suggestions.Count; k++)
                {
                    UnusedSuggestion s = g.Suggestions[k];
                    sb.Append(k > 0 ? "," : "").Append("{\"kind\":").Append(Debrief.Q(s.Kind)).Append(",\"ref\":").Append(Debrief.Q(s.Ref)).Append(",\"name\":").Append(Debrief.Q(s.Name)).Append(",\"text\":").Append(Debrief.Q(s.Text)).Append('}');
                }
                sb.Append("],\"notes\":").Append(Debrief.List(g.Notes)).Append('}');
            }
            sb.Append("],\"skipped\":[");
            for (int i = 0; i < Skipped.Count; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"run\":").Append(Debrief.Q(Skipped[i].Key)).Append(",\"why\":").Append(Debrief.Q(Skipped[i].Value)).Append('}');
            sb.Append("],\"text\":").Append(Debrief.Q(Text())).Append('}');
            return sb.ToString();
        }
    }
}
