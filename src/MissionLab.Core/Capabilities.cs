using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// The features a scenario uses, as short fixed words ("areas", "respawn", "chance" ...). Lists of scenarios show them, and
    /// they tell at a glance whether a file needs something an older or a different MissionLab does not have.
    /// The editor has the same list in capabilitiesOf.
    ///
    /// What each word has been seen to do in the game comes from docs/capabilities.json (CapabilityRegistry), judged against
    /// the build that is running (Judge), and what a scenario needs of the map and mode in hand is checked by Needs.
    /// The editor mirrors Judge and Needs in capJudge and capNeeds (tools/editor/src/20a-modes.js).
    /// </summary>
    public static class Capabilities
    {
        /// <summary>The features plan uses, sorted and each once.</summary>
        public static List<string> Of(MissionPlan plan)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            if (plan == null) return new List<string>();
            if (plan.Doors.Count > 0) found.Add("doors");
            if (plan.Things.Count > 0) found.Add("things");
            if (plan.Objectives.Count > 0) found.Add("intel-move");
            if (plan.RespawnAfter > 0f) found.Add("respawn");
            if (TriggerPlan.OnOff(plan.Power) != "") found.Add("power");
            if (plan.Weather.HasWish) found.Add("weather");
            if (!string.IsNullOrEmpty(plan.Insertion)) found.Add("insertion");
            if (plan.Briefing.Count > 0) found.Add("messages");
            if (plan.ExtractionAfter != 0 || plan.ExtractionGoals) found.Add("extraction-hold");
            if (plan.Goals.Count > 0) found.Add("goals");
            if (plan.Places.Count > 0) found.Add("places");
            if (plan.Sealed) found.Add("sealed");
            if (plan.Options.Count > 0) found.Add("options");
            if (plan.Director != null) found.Add("director");
            // Any pressure bounds, whatever the authored mode: the host can switch an "off" director on during the session (ML-052).
            if (plan.Director != null && plan.Director.Pressure != null) found.Add("pressure");
            foreach (TriggerPlan t in plan.Triggers)
            {
                if (t.Shaped) found.Add("areas");
                if (t.Doors.Count > 0) found.Add("doors");
                if (t.When == TriggerPlan.OnDoor) found.Add("door-trigger");
                if (t.Respawn) found.Add("respawn");
                if (TriggerPlan.OnOff(t.Power) != "") found.Add("power");
                if (TriggerPlan.Clean(t.Message) != "") found.Add("messages");
                if (TriggerPlan.Ending(t.End) != "") found.Add("endings");
                if (t.Hunt) found.Add("hunt");
                if (t.Rally > 0f) found.Add("rally");
                if (t.OpensExtraction) found.Add("extraction-hold");
                if (t.Chance < 100) found.Add("chance");
                if (t.Repeat > 0f) found.Add("repeat");
                if (t.When == TriggerPlan.OnOutcome) found.Add("outcomes");
            }
            foreach (SpawnPlan s in plan.Spawns)
            {
                if (s.Vehicle) found.Add("vehicles");
                if (s.Hvt) found.Add("hvt");
                if (s.Team > 0) found.Add("teams");
                if (s.Chance < 100 || s.Either > 0 || s.Lot > 0 || s.MinPlayers > 1) found.Add("chance");
                if (s.Scatter > 0f) found.Add("scatter");
            }
            foreach (ThingPlan th in plan.Things) if (th.Chance < 100) found.Add("chance");
            foreach (DoorPlan d in plan.Doors) if (d.Chance < 100) found.Add("chance");
            return new List<string>(found);
        }

        // ----- what a capability word has been seen to do, on which build

        // What a host is shown for one capability. Only "supported" is a confirmation, and only for the build that is running.
        public const string ShownSupported = "supported", ShownEarlier = "seen-earlier", ShownExperimental = "experimental", ShownFailing = "failing",
                            ShownUntested = "untested", ShownUnavailable = "unavailable", ShownUnknown = "unknown";

        /// <summary>
        /// The state of one capability word for the running build. current: the running MissionLab's version; "" when it is
        /// not known (the page offline), in which case nothing is confirmed. Evidence counts as confirmation only when it names
        /// exactly the running build: a newer build never inherits "supported" from an older one.
        /// </summary>
        public static CapabilityState Judge(CapabilityRegistry registry, string word, string current)
        {
            string cur = (current ?? "").Trim();
            var st = new CapabilityState { Name = word ?? "" };
            CapabilityRecord r = registry != null ? registry.For(word) : null;
            if (r == null)
            {
                st.Shown = ShownUnknown;
                st.Words = "not in this MissionLab's capability list, so nothing about it has been recorded in the game";
                return st;
            }
            st.Id = r.Id; st.Label = r.Label; st.Status = r.Status;
            CapabilityEvidence best = Newest(r, CapabilityRegistry.Worked);
            st.Client = r.Evidence.Exists(e => e.Result == CapabilityRegistry.Worked && e.Scope == CapabilityRegistry.Client);
            if (best != null) { st.Build = best.Build; st.Scope = best.Scope; }
            string seen = best != null ? "seen working on " + BuildWords(best.Build) + " (" + Context(best) + ")" : "";
            string noClient = st.Client ? "" : "; no client has seen it";
            switch (r.Status)
            {
                case CapabilityRegistry.Supported:
                    if (best == null) { st.Shown = ShownUntested; st.Words = "recorded as supported, but no evidence says it worked: treat it as untested"; }
                    else if (cur != "" && SameBuild(best.Build, cur)) { st.Shown = ShownSupported; st.Confirmed = true; st.Words = seen + noClient; }
                    else
                    {
                        st.Shown = ShownEarlier;
                        st.Words = seen + (cur != "" ? ", not yet on " + cur : "; which build the game runs is not known here") + noClient;
                    }
                    break;
                case CapabilityRegistry.Failing:
                {
                    CapabilityEvidence f = Newest(r, CapabilityRegistry.Failed);
                    st.Shown = ShownFailing;
                    if (f != null) { st.Build = f.Build; st.Scope = f.Scope; }
                    st.Words = "failing: " + (f != null ? (f.Note != "" ? f.Note : "it did not work") + " (" + BuildWords(f.Build) + ", " + Context(f) + ")" : "tried in the game, and it did not work")
                             + ". Using it is an experiment";
                    break;
                }
                case CapabilityRegistry.Experimental:
                    st.Shown = ShownExperimental;
                    st.Words = "experimental: built, but not proven in the game" + (r.Qualifier != "" ? " (" + r.Qualifier + ")" : "") + ". Using it is an experiment";
                    break;
                case CapabilityRegistry.Unavailable:
                    st.Shown = ShownUnavailable;
                    st.Words = "unavailable: the game does not do it, so MissionLab does not use it (" + r.Where + ")";
                    break;
                default:
                    st.Shown = ShownUntested;
                    st.Words = "untested in the game" + (best != null ? "; only " + seen + ", which is not what this needs" : "");
                    break;
            }
            return st;
        }

        /// <summary>The states of a scenario's capability words, in the order given.</summary>
        public static List<CapabilityState> Judge(CapabilityRegistry registry, List<string> words, string current)
        {
            var list = new List<CapabilityState>();
            foreach (string w in words ?? new List<string>()) list.Add(Judge(registry, w, current));
            return list;
        }

        /// <summary>
        /// What a scenario needs that is not there or not proven, as findings. An error is a known incompatibility (a mode
        /// MissionLab leaves alone, a scene or map the scenario is not for, an intel item the map's full list does not have):
        /// the scenario would not run as written. Everything else is a note: a failing, experimental or unavailable
        /// capability, an item missing from one mission's items, a target person the map's mission did not have. A note
        /// never stops anyone; choosing such a scenario is an experiment, and the words say so. facts may be null (nothing
        /// known of the map): then only the capabilities are judged.
        /// </summary>
        public static List<Finding> Needs(MissionPlan plan, MapFacts facts, CapabilityRegistry registry, string current)
        {
            var found = new List<Finding>();
            if (plan == null) return found;
            foreach (CapabilityState c in Judge(registry, Of(plan), current))
            {
                if (c.Shown == ShownFailing) Note(found, "capability-failing", c, "uses " + c.Name + ", which is " + c.Words);
                else if (c.Shown == ShownUnavailable) Note(found, "capability-unavailable", c, "uses " + c.Name + ", which is " + c.Words + "; that part of the scenario is left out");
                else if (c.Shown == ShownExperimental) Note(found, "capability-experimental", c, "uses " + c.Name + ", which is " + c.Words);
            }
            if (facts == null) return found;
            string from = facts.Source != "" ? " (" + facts.Source + ")" : "";
            if (facts.Mode != "" && !facts.Pve)
                Add(found, Finding.Error, "wrong-mode", "", "this is a " + facts.Mode + " mission, not a PvE raid" + from + ": MissionLab leaves it as the game made it, so this scenario is not used");
            string scene = (plan.Scene ?? "").Trim(), nav = Path.GetFileNameWithoutExtension((plan.NavFile ?? "").Trim());
            if (scene != "" && facts.Scene != "")
            {
                bool fits = false;
                foreach (string part in scene.Split(','))
                    if (part.Trim() == "*" || string.Equals(part.Trim(), facts.Scene, StringComparison.OrdinalIgnoreCase)) fits = true;
                if (!fits) Add(found, Finding.Error, "wrong-scene", "scene", "the scenario is for '" + scene + "' and this is '" + facts.Scene + "'" + from + ": MissionLab does not use it here");
            }
            else if (scene == "" && nav != "" && facts.MapFile != "" && !string.Equals(nav, Path.GetFileNameWithoutExtension(facts.MapFile), StringComparison.OrdinalIgnoreCase))
                Add(found, Finding.Error, "wrong-map", "navFile", "the scenario is for the map file '" + plan.NavFile + "' and this mission uses '" + facts.MapFile + "'" + from + ": MissionLab does not use it here");
            if (facts.Items != null)
            {
                var kinds = new HashSet<string>(StringComparer.Ordinal);
                foreach (string i in facts.Items) { string k = ObjectivePlan.Kind(i); if (k != "") kinds.Add(k); }
                string level = facts.ItemsComplete ? Finding.Error : Finding.Note;
                string missing = facts.ItemsComplete ? "which this map does not have" + from : "which is not among this mission's intel items" + from + "; the game picks the items, so it may be there another time";
                for (int i = 0; i < plan.Triggers.Count; i++)
                {
                    TriggerPlan t = plan.Triggers[i];
                    if (t.When != TriggerPlan.OnIntel) continue;
                    string k = ObjectivePlan.Kind(t.Item);
                    bool any = k == "" || k == "any" || k == "all";
                    if (any ? kinds.Count == 0 : !kinds.Contains(k))
                        Add(found, level, "missing-item", "triggers[" + i + "].item", "trigger " + t.Id + " waits for " + (any ? "intel" : "the " + k) + ", " + missing);
                }
                for (int i = 0; i < plan.Objectives.Count; i++)
                {
                    string k = ObjectivePlan.Kind(plan.Objectives[i].Name);
                    if (k != "" && !kinds.Contains(k)) Add(found, level, "missing-item", "objectives[" + i + "].name", "the scenario moves the " + k + ", " + missing);
                }
            }
            if (facts.TargetKnown && !facts.HasTarget && plan.Spawns.Exists(s => s.Hvt))
                Add(found, Finding.Note, "no-target", "spawns", "the scenario places the target person, and the game's mission had none" + from + "; whether the game makes one in another mode is untested");
            return found;
        }

        /// <summary>Whether two builds are the same version ("0.26" and "0.26.0" are).</summary>
        public static bool SameBuild(string a, string b)
        {
            int[] x = CapabilityRegistry.Version(a), y = CapabilityRegistry.Version(b);
            return x != null && y != null && CapabilityRegistry.Compare(x, y) == 0;
        }

        private static CapabilityEvidence Newest(CapabilityRecord r, string result)
        {
            CapabilityEvidence best = null;
            foreach (CapabilityEvidence e in r.Evidence)
            {
                if (e.Result != result) continue;
                if (best == null || Later(e, best)) best = e;
            }
            return best;
        }

        // Newer build first; an unrecorded build is older than every recorded one; at the same build the later date wins.
        private static bool Later(CapabilityEvidence a, CapabilityEvidence b)
        {
            int[] x = CapabilityRegistry.Version(a.Build), y = CapabilityRegistry.Version(b.Build);
            if (x != null && y == null) return true;
            if (x == null && y != null) return false;
            int c = x != null ? CapabilityRegistry.Compare(x, y) : 0;
            if (c != 0) return c > 0;
            return string.CompareOrdinal(a.Date == CapabilityRegistry.Unrecorded ? "" : a.Date, b.Date == CapabilityRegistry.Unrecorded ? "" : b.Date) > 0;
        }

        private static string BuildWords(string build) => build == CapabilityRegistry.Unrecorded || build == "" ? "an unrecorded build" : build;

        /// <summary>Who saw it, where and when: "solo, Clean House by night, 2026-11-18". Solo and host's-log evidence never reads as a client's.</summary>
        public static string Context(CapabilityEvidence e)
        {
            string who = e.Scope == CapabilityRegistry.Client ? "seen by a client" : e.Scope == CapabilityRegistry.HostLog ? "two players, by the host's log only" : "solo";
            return who + (e.Map != "" ? ", " + e.Map : "") + (e.Date != "" && e.Date != CapabilityRegistry.Unrecorded ? ", " + e.Date : "");
        }

        private static void Note(List<Finding> found, string code, CapabilityState c, string text) => Add(found, Finding.Note, code, "capabilities." + c.Name, "the scenario " + text);

        private static void Add(List<Finding> found, string level, string code, string path, string text) =>
            found.Add(new Finding { Level = level, Code = code, Path = path, Text = text });
    }

    /// <summary>One capability word as a host is shown it (Capabilities.Judge).</summary>
    public sealed class CapabilityState
    {
        public string Name = "", Id = "", Label = "", Status = "";   // the word; the registry row it belongs to and that row's recorded status
        public string Shown = "";                                    // Capabilities.Shown*: what the running build may claim
        public string Build = "", Scope = "";                        // the evidence the words rest on ("" when none)
        public bool Confirmed;                                       // seen working on exactly the running build
        public bool Client;                                          // some evidence is from a client's screen
        public string Words = "";
    }

    /// <summary>One time a capability was tried in the game.</summary>
    public sealed class CapabilityEvidence
    {
        public string Build = "", Date = "", Map = "", Mode = "", Scope = "", Result = "", Note = "";
    }

    /// <summary>One row of docs/capabilities.json.</summary>
    public sealed class CapabilityRecord
    {
        public string Id = "", Label = "", Status = "", Qualifier = "", Where = "", Summary = "";
        public List<string> Uses = new List<string>();
        public List<CapabilityEvidence> Evidence = new List<CapabilityEvidence>();
    }

    /// <summary>
    /// docs/capabilities.json (missionlab-capabilities/1): what each capability has been seen to do, with the build, map,
    /// mode and who saw it. The plugin carries the file as a resource; the editor page has it inlined; the README Status
    /// table is made from it by tools/status.py. Reading never throws: what is wrong is listed in Problems, a row with an
    /// unknown status counts as untested and an evidence record that names no proper build, date or scope is left out, so
    /// a broken file can only claim less, never more.
    /// </summary>
    public sealed class CapabilityRegistry
    {
        public const string Schema = "missionlab-capabilities/1", ResourceName = "MissionLab.capabilities.json";
        public const string Supported = "supported", Experimental = "experimental", Failing = "failing", Untested = "untested", Unavailable = "unavailable";
        public const string Solo = "solo", HostLog = "two-player-host-log", Client = "co-op-client";
        public const string Worked = "worked", Failed = "failed", Partial = "partial", NotSeen = "not-seen";
        public const string Unrecorded = "unrecorded";
        public static readonly string[] Statuses = { Supported, Experimental, Failing, Untested, Unavailable };
        public static readonly string[] Scopes = { Solo, HostLog, Client };
        public static readonly string[] Results = { Worked, Failed, Partial, NotSeen };

        public List<CapabilityRecord> Records = new List<CapabilityRecord>();
        public List<string> Problems = new List<string>();
        private readonly Dictionary<string, CapabilityRecord> _byWord = new Dictionary<string, CapabilityRecord>(StringComparer.Ordinal);

        /// <summary>The row a scenario capability word belongs to; null when no row names it.</summary>
        public CapabilityRecord For(string word) => word != null && _byWord.TryGetValue(word, out CapabilityRecord r) ? r : null;

        /// <summary>The copy built into this assembly (tools/build.py and tests/Pure/Pure.csproj embed docs/capabilities.json).</summary>
        public static CapabilityRegistry Embedded()
        {
            try
            {
                using (Stream s = typeof(CapabilityRegistry).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (s == null) return Broken("the capability list is not built into this MissionLab");
                    using (var reader = new StreamReader(s, Encoding.UTF8)) return Read(reader.ReadToEnd());
                }
            }
            catch (Exception e) { return Broken("the capability list could not be read: " + e.Message); }
        }

        public static CapabilityRegistry Read(string json)
        {
            var reg = new CapabilityRegistry();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) { reg.Problems.Add("not a JSON object"); return reg; }
                    if (RunSummary.Text(root, "schema") != Schema) { reg.Problems.Add("schema is not " + Schema); return reg; }
                    if (!root.TryGetProperty("capabilities", out JsonElement list) || list.ValueKind != JsonValueKind.Array) { reg.Problems.Add("no capabilities list"); return reg; }
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    int n = 0;
                    foreach (JsonElement c in list.EnumerateArray())
                    {
                        string at = "capabilities[" + n++ + "]";
                        if (c.ValueKind != JsonValueKind.Object) { reg.Problems.Add(at + " is not an object"); continue; }
                        var r = new CapabilityRecord
                        {
                            Id = RunSummary.Text(c, "id"), Label = RunSummary.Text(c, "label"), Status = RunSummary.Text(c, "status"),
                            Qualifier = RunSummary.Text(c, "qualifier"), Where = RunSummary.Text(c, "where"), Summary = RunSummary.Text(c, "summary"),
                        };
                        if (r.Id == "" || !ids.Add(r.Id)) { reg.Problems.Add(at + ".id is empty or used twice"); continue; }
                        if (r.Label == "") reg.Problems.Add(at + ".label is empty");
                        if (Array.IndexOf(Statuses, r.Status) < 0) { reg.Problems.Add(at + ".status '" + r.Status + "' is not one of " + string.Join(", ", Statuses) + "; read as untested"); r.Status = Untested; }
                        if (c.TryGetProperty("uses", out JsonElement uses) && uses.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement u in uses.EnumerateArray())
                            {
                                string w = u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
                                if (w == "") continue;
                                if (reg._byWord.ContainsKey(w)) { reg.Problems.Add(at + ".uses: '" + w + "' already belongs to " + reg._byWord[w].Id); continue; }
                                reg._byWord[w] = r; r.Uses.Add(w);
                            }
                        int k = 0;
                        if (c.TryGetProperty("evidence", out JsonElement ev) && ev.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in ev.EnumerateArray())
                            {
                                string eat = at + ".evidence[" + k++ + "]";
                                if (e.ValueKind != JsonValueKind.Object) { reg.Problems.Add(eat + " is not an object"); continue; }
                                var x = new CapabilityEvidence
                                {
                                    Build = RunSummary.Text(e, "build"), Date = RunSummary.Text(e, "date"), Map = RunSummary.Text(e, "map"), Mode = RunSummary.Text(e, "mode"),
                                    Scope = RunSummary.Text(e, "scope"), Result = RunSummary.Text(e, "result"), Note = RunSummary.Text(e, "note"),
                                };
                                string why = EvidenceProblem(x);
                                if (why != "") { reg.Problems.Add(eat + ": " + why + "; left out"); continue; }
                                r.Evidence.Add(x);
                            }
                        reg.Records.Add(r);
                    }
                }
            }
            catch (Exception e) { reg.Problems.Add("not JSON: " + e.Message); reg.Records.Clear(); reg._byWord.Clear(); }
            return reg;
        }

        /// <summary>"" when the record names its build, date, map, mode and scope properly.</summary>
        public static string EvidenceProblem(CapabilityEvidence x)
        {
            if (x.Build != Unrecorded && Version(x.Build) == null) return "build '" + x.Build + "' is neither a version nor \"" + Unrecorded + "\"";
            if (x.Date != Unrecorded && !IsDate(x.Date)) return "date '" + x.Date + "' is neither YYYY-MM-DD nor \"" + Unrecorded + "\"";
            if (x.Map == "" || x.Mode == "") return "map or mode is empty";
            if (Array.IndexOf(Scopes, x.Scope) < 0) return "scope '" + x.Scope + "' is not one of " + string.Join(", ", Scopes);
            if (Array.IndexOf(Results, x.Result) < 0) return "result '" + x.Result + "' is not one of " + string.Join(", ", Results);
            return "";
        }

        /// <summary>"0.26.1" as {0, 26, 1}; null for anything that is not dot-separated numbers.</summary>
        public static int[] Version(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] parts = s.Trim().Split('.');
            var v = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 6) return null;
                foreach (char ch in parts[i]) if (ch < '0' || ch > '9') return null;
                v[i] = int.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
            }
            return v;
        }

        /// <summary>Numeric order, missing parts read as 0: 0.9 &lt; 0.10, and 0.26 equals 0.26.0.</summary>
        public static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        private static bool IsDate(string s)
        {
            if (s == null || s.Length != 10 || s[4] != '-' || s[7] != '-') return false;
            for (int i = 0; i < 10; i++) if (i != 4 && i != 7 && (s[i] < '0' || s[i] > '9')) return false;
            return true;
        }

        private static CapabilityRegistry Broken(string why) { var r = new CapabilityRegistry(); r.Problems.Add(why); return r; }
    }

    /// <summary>
    /// What is known of a map and mode before a scenario runs there. Every part may be unknown: an empty string, a null
    /// Items list or TargetKnown false mean "not known", and nothing is judged on them.
    /// </summary>
    public sealed class MapFacts
    {
        public string Source = "";        // where the facts come from, for the words ("recon/Suburb.late.json", "the running mission")
        public string Scene = "";         // the game's scene name
        public string MapFile = "";       // the map data file (navFile), e.g. "suburbv2.bytes"
        public string NavCache = "";      // the navigation cache the scene loads (recon navGraphs.cachedStartupFile)
        public string Mode = "";          // the game mode as the game names it
        public bool Pve;                  // read with Mode: it is a PvE raid
        public List<string> Items;        // intel item names or kinds; null: not known
        public bool ItemsComplete;        // Items lists every candidate the map has, not only the ones one mission kept
        public bool TargetKnown, HasTarget;

        /// <summary>
        /// Facts from a Recon file (missionlab-recon/1). The objective sets list every candidate item of the map and their
        /// set names, so the kinds are complete even when the game has already removed the copies it did not keep. Target
        /// points are read from raid.hvtSpawnPoints. null when the text is not a recon file or has no raid.
        /// </summary>
        public static MapFacts FromRecon(string name, string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || RunSummary.Text(root, "schema") != "missionlab-recon/1") return null;
                    if (!root.TryGetProperty("raid", out JsonElement raid) || raid.ValueKind != JsonValueKind.Object) return null;
                    var f = new MapFacts { Source = name ?? "", Scene = RunSummary.Text(root, "scene") };
                    if (root.TryGetProperty("navGraphs", out JsonElement ng) && ng.ValueKind == JsonValueKind.Object) f.NavCache = RunSummary.Text(ng, "cachedStartupFile");
                    if (raid.TryGetProperty("objectiveSets", out JsonElement sets) && sets.ValueKind == JsonValueKind.Array)
                    {
                        f.Items = new List<string>(); f.ItemsComplete = true;
                        foreach (JsonElement set in sets.EnumerateArray())
                        {
                            if (set.ValueKind != JsonValueKind.Object) continue;
                            AddItem(f.Items, RunSummary.Text(set, "name"));
                            if (!set.TryGetProperty("objectives", out JsonElement os) || os.ValueKind != JsonValueKind.Array) continue;
                            foreach (JsonElement o in os.EnumerateArray())
                                if (o.ValueKind == JsonValueKind.Object) { AddItem(f.Items, RunSummary.Text(o, "name")); AddItem(f.Items, RunSummary.Text(o, "displayName")); }
                        }
                    }
                    if (raid.TryGetProperty("hvtSpawnPoints", out JsonElement hv) && hv.ValueKind == JsonValueKind.Array) { f.TargetKnown = true; f.HasTarget = hv.GetArrayLength() > 0; }
                    return f;
                }
            }
            catch (Exception) { return null; }
        }

        private static void AddItem(List<string> into, string name)
        {
            string k = ObjectivePlan.Kind(name);
            if (k != "" && !into.Contains(k)) into.Add(k);
        }

        /// <summary>Whether these facts are of the map a scenario is for: a scene it names, or its map file's navigation cache.</summary>
        public bool Fits(MissionPlan plan)
        {
            if (plan == null) return false;
            string scene = (plan.Scene ?? "").Trim();
            if (scene != "")
            {
                if (Scene == "") return false;
                foreach (string part in scene.Split(',')) if (string.Equals(part.Trim(), Scene, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            string nav = Path.GetFileNameWithoutExtension((plan.NavFile ?? "").Trim());
            if (nav == "") return false;
            return (NavCache != "" && string.Equals(Path.GetFileNameWithoutExtension(NavCache), nav, StringComparison.OrdinalIgnoreCase))
                || (MapFile != "" && string.Equals(Path.GetFileNameWithoutExtension(MapFile), nav, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The facts of every recon of the scenario's map in one: item kinds together (complete when any list is), a target
        /// when any had one. null when none fits. Scene and map are left out: they decided the fit, so there is no mismatch to report.
        /// </summary>
        public static MapFacts For(MissionPlan plan, List<MapFacts> known)
        {
            MapFacts all = null;
            foreach (MapFacts f in known ?? new List<MapFacts>())
            {
                if (f == null || !f.Fits(plan)) continue;
                if (all == null) all = new MapFacts();
                all.Source += (all.Source != "" ? ", " : "") + f.Source;
                if (f.Items != null)
                {
                    if (all.Items == null) all.Items = new List<string>();
                    foreach (string i in f.Items) if (!all.Items.Contains(i)) all.Items.Add(i);
                    all.ItemsComplete |= f.ItemsComplete;
                }
                if (f.TargetKnown) { all.TargetKnown = true; all.HasTarget |= f.HasTarget; }
            }
            return all;
        }
    }
}
