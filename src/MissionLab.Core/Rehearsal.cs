using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>What a rehearsal expects at one step. A part left empty is not checked.</summary>
    public sealed class RehearsalExpect
    {
        public List<int> Fired = new List<int>();                                  // these triggers have fired by now
        public List<int> NotFired = new List<int>();                               // these have not
        public SortedDictionary<int, string> Phase = new SortedDictionary<int, string>();   // trigger -> phase word (TriggerEngine.Waiting ...)
        public string Extraction = "";                                             // Rehearsal.Open, Rehearsal.Held, or "" (not checked)
        public int Enemies = -1;                                                   // enemies in the mission by now (-1: not checked)
    }

    /// <summary>One moment of a rehearsal: players move, something happens, a trigger is set off by hand, or something is checked.</summary>
    public sealed class RehearsalStep
    {
        public float At;                      // seconds from the mission's start
        public List<V3> Players;              // where every living player stands from now on (null: nobody moves; empty: nobody is alive)
        public string Event = "";             // Rehearsal.Alert, Intel, Down or Door ("": none)
        public string Item = "";              // intel: what was picked up ("": some item)
        public string Tag = "";               // down: only enemies that carry this tag
        public int Count;                     // down: how many (0: every enemy there is, with that tag)
        public V3 Pos;                        // door: its hinge
        public int Fire;                      // a trigger set off by hand (0: none)
        public RehearsalExpect Expect;        // null: nothing is checked
    }

    /// <summary>A rehearsal script, missionlab-rehearsal/1, saved beside a scenario as "name.rehearsal.json".</summary>
    public sealed class RehearsalScript
    {
        public const string Schema = "missionlab-rehearsal/1";
        public const int Version = 1;
        public string Name = "";
        public string Scenario = "";          // ScenarioFile.Hash of the scenario text it was written against
        public int Revision;                  // the scenario's revision then
        public int Players = 1;               // how many players the mission is rolled for
        public long Roll;                     // the roll the variants are settled with, as the Director's seed
        public List<RehearsalStep> Steps = new List<RehearsalStep>();
    }

    /// <summary>What one step did or found. What names the step (counted from 1) and the trigger.</summary>
    public sealed class StepResult
    {
        public float At;
        public string What = "";
        public string Expected = "";
        public string Actual = "";
        public bool Ok;
    }

    public sealed class RehearsalResult
    {
        public bool Passed;                   // every step was as expected, and the rolled mission can be finished
        public string Blocked = "";           // why the Director would refuse this roll ("": it would not)
        public List<StepResult> Steps = new List<StepResult>();
        public string Assumptions = "";
    }

    /// <summary>
    /// Plays a scenario's triggers through a script, without the game: the variants are rolled exactly as the Director rolls
    /// them, the mission is checked for being possible to finish, and a fresh trigger engine runs on a clock of half-second
    /// looks while the script says where the players stand and what happens. Nothing here predicts how the game would play:
    /// the events are told, not found. Pure Core: the plugin calls it from the browser link's worker thread.
    /// </summary>
    public static class Rehearsal
    {
        public const string Alert = "alert", Intel = "intel", Down = "down", Door = "door";
        public const string Open = "open", Held = "held";
        public const float Tick = 0.5f;
        public const float DoorReach = 0.5f;        // metres: a door event opens the door whose hinge is this close
        private const string Prefix = "missionlab-rehearsal/";
        private static readonly string[] Events = { Alert, Intel, Down, Door };
        private static readonly string[] Phases = { TriggerEngine.Off, TriggerEngine.Circle, TriggerEngine.Waiting, TriggerEngine.Holding, TriggerEngine.Counting,
                                                    TriggerEngine.Fired, TriggerEngine.Cooling, TriggerEngine.Exhausted };

        // ----- reading

        /// <summary>
        /// Reads a script strictly. script is null when an error was found. With the scenario given, every trigger the script
        /// fires or checks must exist in it (missing-ref); without it, only the file itself is judged.
        /// </summary>
        public static List<Finding> Read(string json, out RehearsalScript script, MissionPlan plan = null)
        {
            script = null;
            var found = new List<Finding>();
            void Bad(string code, string path, string text) => found.Add(new Finding { Code = code, Path = path, Text = text });

            JsonDocument doc;
            try { doc = JsonDocument.Parse(json ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (JsonException e) { Bad("not-json", "", "this is not a JSON file: " + e.Message); return found; }
            var s = new RehearsalScript();
            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { Bad("wrong-type", "", "a rehearsal is a JSON object"); return found; }

                // The version comes first: a newer file is not looked at any further.
                if (!Has(root, "schema", out JsonElement schema)) { Bad("missing-field", "schema", "a rehearsal names its format: \"schema\": \"" + RehearsalScript.Schema + "\""); return found; }
                string sv = schema.ValueKind == JsonValueKind.String ? schema.GetString() : "";
                int v = sv.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(sv.Substring(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
                if (v <= 0) { Bad("unknown-format", "schema", "\"" + sv + "\" is not a MissionLab rehearsal format"); return found; }
                if (v > RehearsalScript.Version) { Bad("future-version", "schema", "this file is " + Prefix + v + "; this version of MissionLab reads " + RehearsalScript.Schema + ". Update MissionLab to use it. The file was not changed"); return found; }

                s.Name = Text(root, "name", "name", Bad) ?? "";
                s.Scenario = Text(root, "scenario", "scenario", Bad) ?? "";
                if (Has(root, "revision", out JsonElement rev))
                {
                    if (!Whole(rev)) Bad("wrong-type", "revision", "\"revision\" must be a whole number");
                    else if (rev.GetInt64() < 0 || rev.GetInt64() > int.MaxValue) Bad("out-of-range", "revision", "\"revision\" is a save counter from 0");
                    else s.Revision = (int)rev.GetInt64();
                }
                if (Has(root, "players", out JsonElement pl))
                {
                    if (!Whole(pl)) Bad("wrong-type", "players", "\"players\" must be a whole number");
                    else if (pl.GetInt64() < 1 || pl.GetInt64() > 16) Bad("out-of-range", "players", "\"players\" is from 1 to 16");
                    else s.Players = (int)pl.GetInt64();
                }
                if (Has(root, "roll", out JsonElement roll))
                {
                    if (!Whole(roll)) Bad("wrong-type", "roll", "\"roll\" must be a whole number");
                    else s.Roll = roll.GetInt64();
                }

                if (!Has(root, "steps", out JsonElement steps)) Bad("missing-field", "steps", "a rehearsal needs \"steps\": a list of moments");
                else if (steps.ValueKind != JsonValueKind.Array) Bad("wrong-type", "steps", "\"steps\" is a list");
                else
                {
                    int i = 0; float last = 0f;
                    foreach (JsonElement e in steps.EnumerateArray())
                    {
                        string at = "steps[" + i++ + "]";
                        if (e.ValueKind != JsonValueKind.Object) { Bad("wrong-type", at, "a step is an object"); continue; }
                        var st = new RehearsalStep();
                        if (!Has(e, "at", out JsonElement when)) Bad("missing-field", at + ".at", "a step needs \"at\": seconds from the mission's start");
                        else if (when.ValueKind != JsonValueKind.Number) Bad("wrong-type", at + ".at", "\"at\" must be a number");
                        else
                        {
                            st.At = when.GetSingle();
                            if (st.At < 0f || st.At > 86400f) Bad("out-of-range", at + ".at", "\"at\" is from 0 to 86400 seconds");
                            else if (st.At < last) Bad("out-of-range", at + ".at", "steps go in time order: this one comes before the step above it");
                            else last = st.At;
                        }
                        bool any = false;
                        if (Has(e, "players", out JsonElement who))
                        {
                            any = true;
                            if (who.ValueKind != JsonValueKind.Array) Bad("wrong-type", at + ".players", "\"players\" is a list of places [x, y, z]");
                            else
                            {
                                st.Players = new List<V3>(); int k = 0;
                                foreach (JsonElement one in who.EnumerateArray())
                                {
                                    if (Spot(one, out V3 p)) st.Players.Add(p); else Bad("wrong-type", at + ".players[" + k + "]", "a player's place is [x, y, z]");
                                    k++;
                                }
                            }
                        }
                        if (Has(e, "event", out JsonElement ev))
                        {
                            any = true;
                            string kind = ev.ValueKind == JsonValueKind.String ? ev.GetString() : null;
                            if (kind == null) Bad("wrong-type", at + ".event", "\"event\" must be text");
                            else if (Array.IndexOf(Events, kind) < 0) Bad("unknown-value", at + ".event", "\"" + kind + "\" is not an event this version knows (alert, intel, down, door)");
                            else
                            {
                                st.Event = kind;
                                if (kind == Intel) st.Item = Text(e, "item", at + ".item", Bad) ?? "";
                                if (kind == Down)
                                {
                                    st.Tag = Text(e, "tag", at + ".tag", Bad) ?? "";
                                    if (Has(e, "count", out JsonElement c))
                                    {
                                        if (!Whole(c)) Bad("wrong-type", at + ".count", "\"count\" must be a whole number");
                                        else if (c.GetInt64() < 0 || c.GetInt64() > 1000) Bad("out-of-range", at + ".count", "\"count\" is from 0 (all of them) to 1000");
                                        else st.Count = (int)c.GetInt64();
                                    }
                                }
                                if (kind == Door)
                                {
                                    if (!Has(e, "pos", out JsonElement hinge) || !Spot(hinge, out st.Pos)) Bad("missing-field", at + ".pos", "a door event needs \"pos\": the hinge [x, y, z]");
                                }
                            }
                        }
                        if (Has(e, "fire", out JsonElement fire))
                        {
                            any = true;
                            if (!Whole(fire) || fire.GetInt64() < 1 || fire.GetInt64() > int.MaxValue) Bad("wrong-type", at + ".fire", "\"fire\" is a trigger number, a whole number from 1");
                            else
                            {
                                st.Fire = (int)fire.GetInt64();
                                if (plan != null && !plan.Triggers.Exists(t => t.Id == st.Fire)) Bad("missing-ref", at + ".fire", "there is no trigger " + st.Fire);
                            }
                        }
                        if (Has(e, "expect", out JsonElement ex))
                        {
                            any = true;
                            if (ex.ValueKind != JsonValueKind.Object) Bad("wrong-type", at + ".expect", "\"expect\" is an object");
                            else st.Expect = ReadExpect(ex, at + ".expect", plan, Bad);
                        }
                        if (!any) Bad("missing-field", at, "a step needs \"players\", \"event\", \"fire\" or \"expect\"");
                        s.Steps.Add(st);
                    }
                }
            }
            if (found.Exists(f => f.Level == Finding.Error)) return found;
            script = s;
            return found;
        }

        private static RehearsalExpect ReadExpect(JsonElement ex, string at, MissionPlan plan, Action<string, string, string> bad)
        {
            var x = new RehearsalExpect();
            void Ids(string key, List<int> into)
            {
                if (!Has(ex, key, out JsonElement list)) return;
                if (list.ValueKind != JsonValueKind.Array) { bad("wrong-type", at + "." + key, "\"" + key + "\" is a list of trigger numbers"); return; }
                int k = 0;
                foreach (JsonElement one in list.EnumerateArray())
                {
                    string op = at + "." + key + "[" + k++ + "]";
                    if (!Whole(one) || one.GetInt64() < 1 || one.GetInt64() > int.MaxValue) bad("wrong-type", op, "a trigger number is a whole number from 1");
                    else if (plan != null && !plan.Triggers.Exists(t => t.Id == one.GetInt64())) bad("missing-ref", op, "there is no trigger " + one.GetInt64());
                    else if (!into.Contains((int)one.GetInt64())) into.Add((int)one.GetInt64());
                }
            }
            Ids("fired", x.Fired);
            Ids("notFired", x.NotFired);
            if (Has(ex, "phase", out JsonElement phase))
            {
                if (phase.ValueKind != JsonValueKind.Object) bad("wrong-type", at + ".phase", "\"phase\" is an object: trigger number -> phase");
                else
                    foreach (JsonProperty p in phase.EnumerateObject())
                    {
                        string op = at + ".phase." + p.Name;
                        string word = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                        if (!int.TryParse(p.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 1) bad("wrong-type", op, "\"" + p.Name + "\" is not a trigger number");
                        else if (word == null) bad("wrong-type", op, "a phase is text");
                        else if (Array.IndexOf(Phases, word) < 0) bad("unknown-value", op, "\"" + word + "\" is not a phase this version knows (" + string.Join(", ", Phases) + ")");
                        else if (plan != null && !plan.Triggers.Exists(t => t.Id == id)) bad("missing-ref", op, "there is no trigger " + id);
                        else x.Phase[id] = word;
                    }
            }
            if (Has(ex, "extraction", out JsonElement exn))
            {
                string word = exn.ValueKind == JsonValueKind.String ? exn.GetString() : null;
                if (word == null) bad("wrong-type", at + ".extraction", "\"extraction\" must be text");
                else if (word != Open && word != Held) bad("unknown-value", at + ".extraction", "\"" + word + "\" is not a value this version knows for \"extraction\" (open, held)");
                else x.Extraction = word;
            }
            if (Has(ex, "enemies", out JsonElement en))
            {
                if (!Whole(en)) bad("wrong-type", at + ".enemies", "\"enemies\" must be a whole number");
                else if (en.GetInt64() < 0 || en.GetInt64() > 10000) bad("out-of-range", at + ".enemies", "\"enemies\" is from 0 to 10000");
                else x.Enemies = (int)en.GetInt64();
            }
            return x;
        }

        private static bool Has(JsonElement o, string key, out JsonElement value) => o.TryGetProperty(key, out value) && value.ValueKind != JsonValueKind.Null;
        private static bool Whole(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out _);
        private static bool Spot(JsonElement e, out V3 p)
        {
            p = default;
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3) return false;
            for (int i = 0; i < 3; i++) if (e[i].ValueKind != JsonValueKind.Number) return false;
            p = new V3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
            return true;
        }
        private static string Text(JsonElement o, string key, string path, Action<string, string, string> bad)
        {
            if (!Has(o, key, out JsonElement v)) return null;
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            bad("wrong-type", path, "\"" + key + "\" must be text");
            return null;
        }

        // ----- writing

        /// <summary>The script as canonical JSON: fixed key order, two decimals, keys left out when they hold their default, one step per line.</summary>
        public static string Write(RehearsalScript s)
        {
            var sb = new StringBuilder(512);
            CultureInfo inv = CultureInfo.InvariantCulture;
            string F(float f) => Math.Round(f, 2).ToString(inv);
            void P(V3 v) => sb.Append('[').Append(F(v.X)).Append(',').Append(F(v.Y)).Append(',').Append(F(v.Z)).Append(']');
            void Ids(List<int> ids) { sb.Append('['); for (int k = 0; k < ids.Count; k++) sb.Append(k > 0 ? "," : "").Append(ids[k].ToString(inv)); sb.Append(']'); }

            sb.Append("{\"schema\":\"").Append(RehearsalScript.Schema).Append('"');
            if (!string.IsNullOrEmpty(s.Name)) sb.Append(",\"name\":\"").Append(Guard.Esc(s.Name)).Append('"');
            if (!string.IsNullOrEmpty(s.Scenario)) sb.Append(",\"scenario\":\"").Append(Guard.Esc(s.Scenario)).Append('"');
            if (s.Revision > 0) sb.Append(",\"revision\":").Append(s.Revision.ToString(inv));
            sb.Append(",\"players\":").Append(s.Players.ToString(inv));     // always written: it decides which variants the mission has
            if (s.Roll != 0) sb.Append(",\"roll\":").Append(s.Roll.ToString(inv));
            sb.Append(",\"steps\":[");
            for (int i = 0; i < s.Steps.Count; i++)
            {
                RehearsalStep st = s.Steps[i];
                sb.Append(i > 0 ? "," : "").Append("\n{\"at\":").Append(F(st.At));
                if (st.Players != null)
                {
                    sb.Append(",\"players\":[");
                    for (int k = 0; k < st.Players.Count; k++) { if (k > 0) sb.Append(','); P(st.Players[k]); }
                    sb.Append(']');
                }
                if (!string.IsNullOrEmpty(st.Event))
                {
                    sb.Append(",\"event\":\"").Append(Guard.Esc(st.Event)).Append('"');
                    if (st.Event == Intel && !string.IsNullOrEmpty(st.Item)) sb.Append(",\"item\":\"").Append(Guard.Esc(st.Item)).Append('"');
                    if (st.Event == Down && !string.IsNullOrEmpty(st.Tag)) sb.Append(",\"tag\":\"").Append(Guard.Esc(st.Tag)).Append('"');
                    if (st.Event == Down && st.Count > 0) sb.Append(",\"count\":").Append(st.Count.ToString(inv));
                    if (st.Event == Door) { sb.Append(",\"pos\":"); P(st.Pos); }
                }
                if (st.Fire > 0) sb.Append(",\"fire\":").Append(st.Fire.ToString(inv));
                if (st.Expect != null)
                {
                    RehearsalExpect x = st.Expect;
                    sb.Append(",\"expect\":{");
                    bool first = true;
                    void Key(string key) { sb.Append(first ? "" : ",").Append('"').Append(key).Append("\":"); first = false; }
                    if (x.Fired.Count > 0) { Key("fired"); Ids(x.Fired); }
                    if (x.NotFired.Count > 0) { Key("notFired"); Ids(x.NotFired); }
                    if (x.Phase.Count > 0)
                    {
                        Key("phase"); sb.Append('{');
                        bool one = true;
                        foreach (KeyValuePair<int, string> p in x.Phase) { sb.Append(one ? "" : ",").Append('"').Append(p.Key.ToString(inv)).Append("\":\"").Append(Guard.Esc(p.Value)).Append('"'); one = false; }
                        sb.Append('}');
                    }
                    if (!string.IsNullOrEmpty(x.Extraction)) { Key("extraction"); sb.Append('"').Append(Guard.Esc(x.Extraction)).Append('"'); }
                    if (x.Enemies >= 0) { Key("enemies"); sb.Append(x.Enemies.ToString(inv)); }
                    sb.Append('}');
                }
                sb.Append('}');
            }
            sb.Append("\n]}");
            return sb.ToString();
        }

        // ----- running

        /// <summary>
        /// Plays the script against the scenario. The plan given is not changed: a copy is rolled, as the Director rolls it
        /// (Variants with roll ^ 0x5EED and the script's player count), and checked with Completion. A roll the Director would
        /// refuse is named in Blocked, and the steps still run, so that the author sees both.
        /// </summary>
        public static RehearsalResult Run(MissionPlan plan, RehearsalScript s)
        {
            var r = new RehearsalResult();
            MissionPlan p;
            try { p = MissionPlan.FromJson(plan.ToJson()); }
            catch (Exception e) { r.Blocked = "the scenario could not be read: " + e.Message; r.Assumptions = Assume(s, null); return r; }

            // 1. The variants, exactly as at a mission's start: only when the scenario leaves something to chance.
            Variants.Out gone = null; int squad = 0;
            var notes = new List<string>();
            if (Variants.Any(p)) { gone = Variants.Resolve(p, new Rng(s.Roll ^ 0x5EED), notes, s.Players); squad = s.Players; }
            List<Finding> broken = Completion.Check(p, gone, squad);
            if (Completion.Blocks(broken)) r.Blocked = string.Join("; ", broken.FindAll(f => f.Level == Finding.Error).ConvertAll(f => f.ToString()));
            r.Assumptions = Assume(s, notes);

            // 2. The clock: a look every half second up to the last step, and one at every step's own time.
            var engine = new TriggerEngine();
            engine.Reset(0f);
            engine.Sync(p.Triggers);
            var w = new ScriptWorld(p, engine);
            var order = new List<int>();
            for (int i = 0; i < s.Steps.Count; i++) order.Add(i);
            order.Sort((a, b) => s.Steps[a].At != s.Steps[b].At ? s.Steps[a].At.CompareTo(s.Steps[b].At) : a.CompareTo(b));    // stable: steps at one time keep their order
            float end = 0f;
            foreach (RehearsalStep st in s.Steps) end = Math.Max(end, st.At);
            var times = new SortedSet<float>();
            for (int k = 0; k * Tick <= end; k++) times.Add(k * Tick);
            foreach (RehearsalStep st in s.Steps) times.Add(Math.Max(0f, st.At));

            int next = 0;
            foreach (float now in times)
            {
                // What happens at this time comes first, then the engine looks, then the checks: an event and what it sets off are seen together.
                int first = next;
                for (; next < order.Count && s.Steps[order[next]].At <= now; next++) Happen(s.Steps[order[next]], order[next] + 1, now, w, engine, r);
                engine.Step(now, w);
                for (int i = first; i < next; i++) if (s.Steps[order[i]].Expect != null) Expect(s.Steps[order[i]], order[i] + 1, now, p, w, engine, r);
            }
            r.Passed = r.Blocked == "" && r.Steps.TrueForAll(x => x.Ok);
            return r;
        }

        private static string Assume(RehearsalScript s, List<string> notes)
        {
            return "Events happen when the script says, as if injected; spotting, pathfinding, physics, networking and combat are not predicted. " +
                   "The mission is rolled as the Director rolls it, with roll " + s.Roll.ToString(CultureInfo.InvariantCulture) + " for " + s.Players + (s.Players == 1 ? " player" : " players") +
                   (notes != null && notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : "") + ". Enemies are counted as people: gun trucks are left out.";
        }

        private static string Secs(float f) => f.ToString("0.##", CultureInfo.InvariantCulture) + " s";
        private static string Step(int number, float at) => "step " + number + " at " + Secs(at);
        private static string Name(MissionPlan p, int id)
        {
            TriggerPlan t = p.Triggers.Find(x => x.Id == id);
            return "trigger " + id + (t == null || string.IsNullOrEmpty(t.Name) ? "" : " '" + t.Name + "'");
        }

        // The script's inputs: players move, an event happens, a trigger is set off by hand.
        private static void Happen(RehearsalStep st, int number, float now, ScriptWorld w, TriggerEngine engine, RehearsalResult r)
        {
            string step = Step(number, st.At);
            if (st.Players != null)
            {
                w.Players.Clear(); w.Players.AddRange(st.Players);
                r.Steps.Add(new StepResult { At = st.At, What = step + ": " + (st.Players.Count == 0 ? "no player is alive" : st.Players.Count + (st.Players.Count == 1 ? " living player stands" : " living players stand") + " where the script says"), Ok = true });
            }
            if (st.Event != "")
            {
                string said = w.Happen(st);
                r.Steps.Add(new StepResult { At = st.At, What = step + ": " + said, Ok = true });
            }
            if (st.Fire > 0)
            {
                bool ok = engine.FireNow(st.Fire, now, out _, out string why);
                r.Steps.Add(new StepResult { At = st.At, What = step + ": trigger " + st.Fire + " is fired by hand", Expected = "it fires", Actual = ok ? "it fired" : "refused: " + why, Ok = ok });
            }
        }

        private static void Expect(RehearsalStep st, int number, float now, MissionPlan p, ScriptWorld w, TriggerEngine engine, RehearsalResult r)
        {
            RehearsalExpect x = st.Expect;
            string step = Step(number, st.At);
            void Add(string what, string expected, string actual, bool ok) => r.Steps.Add(new StepResult { At = st.At, What = step + ": " + what, Expected = expected, Actual = actual, Ok = ok });
            TriggerPlan Find(int id) => p.Triggers.Find(t => t.Id == id);

            foreach (int id in x.Fired)
            {
                TriggerPlan t = Find(id);
                if (t == null) { Add(Name(p, id) + " has fired", "fired", "there is no trigger " + id, false); continue; }
                bool ok = engine.EverFired(id);
                Add(Name(p, id) + " has fired", "fired", ok ? "fired" : "not fired: " + engine.Explain(t, now, w).Why, ok);
            }
            foreach (int id in x.NotFired)
            {
                TriggerPlan t = Find(id);
                if (t == null) { Add(Name(p, id) + " has not fired", "not fired", "there is no trigger " + id, false); continue; }
                bool fired = engine.EverFired(id);
                TriggerState s = engine.State(id);
                Add(Name(p, id) + " has not fired", "not fired", fired ? "fired " + (s.ByHand ? "by hand " : "") + "at " + Secs(s.LastFiredAt ?? 0f) : "not fired", !fired);
            }
            foreach (KeyValuePair<int, string> want in x.Phase)
            {
                TriggerPlan t = Find(want.Key);
                if (t == null) { Add(Name(p, want.Key) + " is " + want.Value, want.Value, "there is no trigger " + want.Key, false); continue; }
                TriggerView v = engine.Explain(t, now, w);
                Add(Name(p, want.Key) + " is " + want.Value, want.Value, v.Phase + ": " + v.Why, v.Phase == want.Value);
            }
            if (x.Extraction != "")
            {
                string actual = ExtractionOf(p, engine, out string why);
                Add("the extraction is " + x.Extraction, x.Extraction, actual + ": " + why, actual == x.Extraction);
            }
            if (x.Enemies >= 0)
            {
                int start = w.Present(null, true), all = w.Present(null, false);
                Add(x.Enemies + " enemies are in the mission", x.Enemies.ToString(CultureInfo.InvariantCulture),
                    all + " (" + start + " from the start, " + (all - start) + " brought by triggers)", all == x.Enemies);
            }
        }

        // Open when the scenario does not hold it, when what it waits for has fired, or when a fired trigger opens it.
        private static string ExtractionOf(MissionPlan p, TriggerEngine engine, out string why)
        {
            if (p.ExtractionAfter == 0) { why = "the scenario does not hold it"; return Open; }
            if (engine.EverFired(p.ExtractionAfter)) { why = Name(p, p.ExtractionAfter) + " has fired"; return Open; }
            TriggerPlan opener = p.Triggers.Find(t => t.OpensExtraction && engine.EverFired(t.Id));
            if (opener != null) { why = Name(p, opener.Id) + " opened it"; return Open; }
            why = "it waits for " + Name(p, p.ExtractionAfter);
            return Held;
        }

        /// <summary>The world as the script tells it: nothing is seen, everything is told.</summary>
        private sealed class ScriptWorld : ITriggerWorld
        {
            private readonly MissionPlan _plan;
            private readonly TriggerEngine _engine;
            public readonly List<V3> Players = new List<V3>();               // the living players, where they stand
            private bool _alarm;
            private readonly HashSet<string> _taken = new HashSet<string>(); // kinds of intel picked up ("" for an item the script did not name)
            private readonly Dictionary<string, int> _down = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // tag ("" none) -> enemies down
            private readonly List<V3> _opened = new List<V3>();

            public ScriptWorld(MissionPlan plan, TriggerEngine engine) { _plan = plan; _engine = engine; }

            /// <summary>Enemies in the mission now, with a tag (null: any). fromStart: only those there from the start.</summary>
            public int Present(string tag, bool fromStart)
            {
                int n = 0;
                foreach (SpawnPlan s in _plan.Spawns)
                {
                    if (s.Vehicle) continue;                     // a gun truck is not a person
                    if (s.Trigger != 0 && (fromStart || !_engine.EverFired(s.Trigger))) continue;
                    if (tag != null && !string.Equals(s.Tag ?? "", tag, StringComparison.OrdinalIgnoreCase)) continue;
                    n++;
                }
                return n;
            }

            /// <summary>Applies one event and says what happened.</summary>
            public string Happen(RehearsalStep st)
            {
                switch (st.Event)
                {
                    case Rehearsal.Alert:          // qualified: inside this class, Down names the world's own question
                        _alarm = true;
                        return "the enemy raises the alarm";
                    case Rehearsal.Intel:
                    {
                        string kind = ObjectivePlan.Kind(st.Item);
                        _taken.Add(kind);
                        return kind == "" ? "an intel item is picked up" : "the " + kind + " is picked up";
                    }
                    case Rehearsal.Down:
                    {
                        string tag = st.Tag ?? "";
                        string tagged = tag == "" ? "" : " tagged '" + tag + "'";
                        if (st.Count > 0)
                        {
                            _down.TryGetValue(tag, out int had);
                            _down[tag] = had + st.Count;
                            return st.Count + (st.Count == 1 ? " enemy" : " enemies") + tagged + " go down";
                        }
                        if (tag == "")
                        {
                            _down.Clear();
                            foreach (SpawnPlan s in _plan.Spawns)
                            {
                                if (s.Vehicle || (s.Trigger != 0 && !_engine.EverFired(s.Trigger))) continue;
                                _down.TryGetValue(s.Tag ?? "", out int had);
                                _down[s.Tag ?? ""] = had + 1;
                            }
                        }
                        else _down[tag] = Present(tag, false);
                        return "every enemy" + tagged + " there is goes down";
                    }
                    case Rehearsal.Door:
                        _opened.Add(st.Pos);
                        return "the door with its hinge at (" + st.Pos.X.ToString("0.##", CultureInfo.InvariantCulture) + ", " + st.Pos.Z.ToString("0.##", CultureInfo.InvariantCulture) + ") is opened";
                }
                return "nothing happens";
            }

            public bool Alerted() => _alarm;

            public bool IntelTaken(string item)
            {
                string kind = ObjectivePlan.Kind(item);
                if (kind == "" || kind == "any") return _taken.Count > 0;
                if (kind != "all") return _taken.Contains(kind);
                // Every kind of item the scenario places: none placed means there is nothing to take.
                bool some = false;
                foreach (ObjectivePlan o in _plan.Objectives)
                {
                    some = true;
                    if (!_taken.Contains(ObjectivePlan.Kind(o.Name))) return false;
                }
                return some;
            }

            public bool Occupied(TriggerPlan t, out V3 where)
            {
                where = default;
                if (t.Everyone)
                {
                    foreach (V3 p in Players) { if (!Membership.Inside(t, p)) return false; where = p; }
                    return Players.Count > 0;
                }
                foreach (V3 p in Players) if (Membership.Inside(t, p)) { where = p; return true; }
                return false;
            }

            public int Inside(TriggerPlan t, out int alive)
            {
                alive = Players.Count;
                int n = 0;
                foreach (V3 p in Players) if (Membership.Inside(t, p)) n++;
                return n;
            }

            public bool Down(int count, string tag)
            {
                tag = tag ?? "";
                if (tag == "")
                {
                    int total = 0;
                    foreach (int n in _down.Values) total += n;
                    if (count > 0) return total >= count;
                    int there = Present(null, false);
                    return there > 0 && total >= there;
                }
                _down.TryGetValue(tag, out int down);
                if (count > 0) return down >= count;
                int tagged = Present(tag, false);
                return tagged > 0 && down >= tagged;
            }

            public bool DoorOpen(V3 hinge)
            {
                foreach (V3 d in _opened)
                {
                    float dx = d.X - hinge.X, dy = d.Y - hinge.Y, dz = d.Z - hinge.Z;
                    if (dx * dx + dy * dy + dz * dz <= DoorReach * DoorReach) return true;
                }
                return false;
            }
        }
    }
}
