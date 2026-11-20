using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace MissionLab.Core
{
    /// <summary>What a validation run is asked to do (ML-048). The strict format check always runs: every other check needs it.</summary>
    public sealed class ValidationAsk
    {
        public bool Completion = true, Scatter = true, Rehearsals = true;
        public List<int> Players = new List<int>();          // squad sizes to try; empty: 1, 2 and 4
        public List<long> Seeds = new List<long>();          // rolls to try; empty: 1 to MissionValidation.DefaultSeeds
        public int Budget = MissionValidation.DefaultBudget; // the most combinations executed in the whole run
        public SortedDictionary<string, List<int>> Choices = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);   // option id -> choices to try; an option not named: every choice
    }

    /// <summary>What the caller could find for the map-dependent and saved checks. Logic checks need none of it.</summary>
    public sealed class ValidationInputs
    {
        public IWalk Walk;                                  // the scenario's walkable ground; null when the map file is not here
        public string NavFile = "", MapPrint = "", MapWhy = "";   // the map file read and its print; or why it could not be read
        public List<V3> Insertions = new List<V3>();        // the map's insertion points, as the scatter preview takes them
        public List<KeyValuePair<string, string>> Rehearsals;     // saved rehearsals by file name; null when they could not be had
        public string RehearsalsWhy = "";
    }

    /// <summary>How far a run has got, for the page to show while it waits. Written by the run, read by anyone.</summary>
    public sealed class ValidationProgress
    {
        public volatile int Executed, Planned;
        public volatile string Check = "";
    }

    /// <summary>
    /// The first failing combination of a check, with everything needed to play it again: the seed (the Director's Seed
    /// setting), the host's picks, the squad size, what was expected and what was found, and the triggers and enemies it is about.
    /// </summary>
    public sealed class ValidationCounter
    {
        public string Check = "";
        public long Seed;
        public int Players;
        public SortedDictionary<string, int> Picks = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public string Options = "";                         // the picks in words, as Options.Describe says them
        public string Expected = "";
        public string Found = "";
        public string Path = "";                            // where in the file, as the finding says it
        public string Rehearsal = "";                       // the saved rehearsal's file, for a rehearsal that failed
        public string Resolved = "";                        // ScenarioFile.Hash of the mission as it was set up (completion only)
        public List<int> Triggers = new List<int>();
        public List<int> Spawns = new List<int>();          // spawn numbers in the file
        public List<string> Trace = new List<string>();     // every finding of that combination, or the rehearsal's steps
    }

    /// <summary>One check of the run: what it covered and what it found.</summary>
    public sealed class ValidationCheck
    {
        public string Name = "";
        public string State = MissionValidation.NotRun;
        public string Coverage = "";                        // Exhaustive or Sampled; "" when nothing ran
        public long Planned;                                // combinations the asked matrix holds
        public int Executed, Failed;
        public string Why = "";
        public List<string> Notes = new List<string>();
        public ValidationCounter Counter;
    }

    /// <summary>The run's report. Everything here is decided by the inputs alone: the same inputs give the same report.</summary>
    public sealed class ValidationReport
    {
        public string ScenarioHash = "", ScenarioId = "", ResolvedHash = "", NavFile = "", MapPrint = "";
        public int Revision, Budget, Executed;
        public List<long> Seeds = new List<long>();
        public List<int> Players = new List<int>();
        public List<string> Options = new List<string>();  // per option: "id: choices tried of all"
        public bool Cancelled, BudgetHit, RollsAlike;
        public string Verdict = MissionValidation.Incomplete;
        public string Covers = "";
        public List<ValidationCheck> Checks = new List<ValidationCheck>();
        public ValidationCounter First => Checks.Find(c => c.Counter != null)?.Counter;
    }

    /// <summary>
    /// One validation run over a scenario (ML-048): the strict format check, Completion for every combination of the host's
    /// options with every squad size and seed asked (the dice, then the picks, then Completion.Check, as the Director starts a
    /// mission), the scatter seeds on the map's own ground when the matching map file is here, and every saved rehearsal. A
    /// work budget caps the combinations and a cancellation stops between two of them; both keep what was found so far. The
    /// first failing combination of a check is kept as a counterexample that Reproduce plays again. The report holds no clock
    /// and nothing random, so unchanged inputs give the same report; it names the scenario's fingerprint (and the map's,
    /// where one was used) so that Stale can tell when it no longer speaks for the file. Pure: no game objects, no files.
    /// </summary>
    public static class MissionValidation
    {
        public const string Schema = "missionlab-validation/1";
        public const int Evaluator = 1;                      // raised whenever a check changes what it executes or how it judges
        public const int DefaultSeeds = 10, MaxSeeds = 200, MaxPlayers = 16, DefaultBudget = 2000, MaxBudget = 20000;
        public const int MaxOptionCombinations = 256;        // more than this and the choices tried are a fixed sample
        public const int MaxRehearsals = RehearsalFile.MaxListed;
        public const string Format = "format", CompletionCheck = "completion", ScatterCheck = "scatter", RehearsalsCheck = "rehearsals";
        public const string Passed = "passed", Failed = "failed", NotRun = "not-run", NotNeeded = "not-needed", Partial = "partial";
        public const string Exhaustive = "exhaustive", Sampled = "sampled";
        public const string Incomplete = "incomplete";
        public const string Limits = "A pass covers only the checks above, on the combinations they executed. It does not certify combat balance, " +
                                     "navigation, spotting, physics or multiplayer behaviour, and a roll or squad size that was not tried may still differ.";
        private const long VariantSalt = 0x5EED;             // the Director's dice salt, so a counterexample plays again in the game with that Seed
        private const long SampleSalt = 0x48A11D;            // fixed: sampled combinations are the same on every run
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        private static readonly Regex TriggerWords = new Regex(@"\btriggers? (\d+(?:, \d+)*)", RegexOptions.CultureInvariant);
        private static readonly Regex Indexed = new Regex(@"^(triggers|spawns)\[(\d+)\]", RegexOptions.CultureInvariant);

        // ----- the run

        public static ValidationReport Run(string text, ValidationAsk ask, ValidationInputs inputs, CancellationToken cancel, ValidationProgress progress = null)
        {
            ask = ask ?? new ValidationAsk();
            inputs = inputs ?? new ValidationInputs();
            progress = progress ?? new ValidationProgress();
            var report = new ValidationReport { ScenarioHash = ScenarioFile.Hash(text ?? ""), Budget = Math.Max(1, Math.Min(MaxBudget, ask.Budget)) };
            report.Players = Players(ask.Players);
            report.Seeds = Seeds(ask.Seeds);
            var format = new ValidationCheck { Name = Format };
            var completion = new ValidationCheck { Name = CompletionCheck };
            var scatter = new ValidationCheck { Name = ScatterCheck };
            var rehearsals = new ValidationCheck { Name = RehearsalsCheck };
            report.Checks.AddRange(new[] { format, completion, scatter, rehearsals });

            // 1. The strict reader, as at a mission's start: a refused file is refused whatever else is asked.
            List<Finding> read = ScenarioFile.Read(text ?? "", out MissionPlan plan);
            format.Executed = 1; format.Planned = 1; format.Coverage = Exhaustive;
            foreach (Finding f in read) if (format.Notes.Count < CatalogueInfo.MaxFindings) format.Notes.Add((f.Level == Finding.Error ? "" : "note: ") + f.ToString());
            if (plan == null)
            {
                format.State = Failed; format.Failed = 1;
                Finding bad = read.Find(f => f.Level == Finding.Error);
                format.Why = "the game's reader refuses this scenario: " + (bad == null ? "no reason given" : bad.ToString());
                foreach (ValidationCheck c in new[] { completion, scatter, rehearsals }) { c.State = NotRun; c.Why = "not run: the scenario must pass the format check first"; }
                Finish(report);
                return report;
            }
            format.State = Passed;
            report.ScenarioId = plan.Id ?? ""; report.Revision = plan.Revision;

            var options = new List<OptionPlan>();
            foreach (OptionPlan o in plan.Options) if (o != null && o.Choices.Count > 0) options.Add(o);
            var allowed = new List<List<int>>();
            foreach (OptionPlan o in options)
            {
                var list = new List<int>();
                if (ask.Choices != null && ask.Choices.TryGetValue(o.Id ?? "", out List<int> named) && named != null)
                    foreach (int c in named) if (c >= 0 && c < o.Choices.Count && !list.Contains(c)) list.Add(c);
                if (list.Count == 0) for (int c = 0; c < o.Choices.Count; c++) list.Add(c);
                list.Sort();
                allowed.Add(list);
            }
            List<int[]> combos = Combinations(options, allowed, out long total);
            bool optionsWhole = combos.Count >= total;
            for (int k = 0; k < options.Count; k++)
                report.Options.Add(options[k].Id + ": choices " + string.Join(", ", allowed[k]) + " of " + options[k].Choices.Count +
                                   (allowed[k].Count == options[k].Choices.Count ? " (all)" : " (as asked)"));
            bool dice = Variants.Any(plan);
            report.RollsAlike = !dice;
            var rolls = dice ? report.Seeds : new List<long> { report.Seeds[0] };   // with nothing left to chance every roll is the same mission

            bool scatterNeeded = Scatter.Any(plan);
            string mapWhy = MapProblem(plan, inputs);
            completion.Planned = ask.Completion ? (long)combos.Count * report.Players.Count * rolls.Count : 0;
            scatter.Planned = ask.Scatter && scatterNeeded && mapWhy == "" ? (long)report.Seeds.Count * report.Players.Count : 0;
            var saved = Saved(inputs.Rehearsals);
            rehearsals.Planned = ask.Rehearsals && saved != null ? saved.Count : 0;
            progress.Planned = (int)Math.Min(int.MaxValue, Math.Min(report.Budget, completion.Planned + scatter.Planned + rehearsals.Planned));

            int spent = 0;
            string stop = "";
            // Called before every combination: false once the run must stop, with stop saying why.
            bool Go()
            {
                if (stop != "") return false;
                if (cancel.IsCancellationRequested) { stop = "cancelled"; report.Cancelled = true; return false; }
                if (spent >= report.Budget) { stop = "budget"; report.BudgetHit = true; return false; }
                spent++; progress.Executed = spent;
                return true;
            }
            string StopWords() => stop == "cancelled" ? "the run was cancelled" : "the work budget of " + report.Budget + " combinations was used up";

            // 2. Completion over the options, squad sizes and seeds: the Director's own order of setting up a mission.
            if (!ask.Completion) { completion.State = NotRun; completion.Why = "not asked for"; }
            else
            {
                progress.Check = CompletionCheck;
                var resolved = new StringBuilder(1024);
                var notes = new SortedSet<string>(StringComparer.Ordinal);
                foreach (int[] combo in combos)
                {
                    var picks = Picks(options, combo);
                    foreach (int squad in report.Players)
                        foreach (long seed in rolls)
                        {
                            if (!Go()) goto doneCompletion;
                            completion.Executed++;
                            Finding bad = Try(text, picks, squad, seed, out string hash, out List<Finding> all);
                            resolved.Append(hash).Append('\n');
                            foreach (Finding f in all) if (f.Level == Finding.Note) notes.Add(f.ToString());
                            if (bad == null) continue;
                            completion.Failed++;
                            if (completion.Counter == null) completion.Counter = CounterOf(plan, picks, squad, seed, hash, bad, all);
                        }
                }
                doneCompletion:
                report.ResolvedHash = resolved.Length == 0 ? "" : ScenarioFile.Hash(resolved.ToString());
                foreach (string n in notes) if (completion.Notes.Count < CatalogueInfo.MaxFindings) completion.Notes.Add(n);
                if (!optionsWhole) completion.Notes.Add("the choices are a fixed sample: " + total + " combinations of the choices asked is more than " + MaxOptionCombinations + ", so " + combos.Count + " of them are tried");
                completion.Notes.Add(dice ? "the seeds are a sample of rolls, not proof that every roll can be finished"
                                          : "nothing is left to chance, so one roll covers every roll");
                completion.Coverage = optionsWhole && !dice && completion.Executed == completion.Planned ? Exhaustive : Sampled;
                Judge(completion, stop, StopWords());
            }

            // 3. Scatter on the map's own ground: only with the matching map file here.
            if (!ask.Scatter) { scatter.State = NotRun; scatter.Why = "not asked for"; }
            else if (!scatterNeeded) { scatter.State = NotNeeded; scatter.Why = "no enemy has a scatter radius"; }
            else if (mapWhy != "") { scatter.State = NotRun; scatter.Why = mapWhy; }
            else
            {
                progress.Check = ScatterCheck;
                report.NavFile = inputs.NavFile ?? ""; report.MapPrint = inputs.MapPrint ?? "";
                int stayed = 0;
                foreach (int squad in report.Players)
                    foreach (long seed in report.Seeds)
                    {
                        if (!Go()) goto doneScatter;
                        scatter.Executed++;
                        var counters = new List<ScatterSweep.Counter>();
                        ScatterSweep.Try(text, seed, squad, inputs.Walk, inputs.Insertions ?? new List<V3>(), counters);
                        ScatterSweep.Counter broke = counters.Find(c => c.Kind != ScatterSweep.Stayed);
                        stayed += counters.FindAll(c => c.Kind == ScatterSweep.Stayed).Count;
                        if (broke == null) continue;
                        scatter.Failed++;
                        if (scatter.Counter == null) scatter.Counter = ScatterCounterOf(plan, broke, squad, counters);
                    }
                doneScatter:
                if (stayed > 0) scatter.Notes.Add(stayed + (stayed == 1 ? " time" : " times") + " a scattered enemy found no spot and stayed at its authored place");
                scatter.Notes.Add("the seeds are a sample of rolls, with the options at their defaults; the ground is " + report.NavFile + " as it is on this computer");
                scatter.Coverage = Sampled;
                Judge(scatter, stop, StopWords());
            }

            // 4. The author's own assertions: every saved rehearsal, against the scenario as it is now.
            if (!ask.Rehearsals) { rehearsals.State = NotRun; rehearsals.Why = "not asked for"; }
            else if (saved == null) { rehearsals.State = NotRun; rehearsals.Why = inputs.RehearsalsWhy == "" ? "the saved rehearsals could not be read" : inputs.RehearsalsWhy; }
            else if (saved.Count == 0) { rehearsals.State = NotNeeded; rehearsals.Why = "no rehearsal is saved for this scenario"; }
            else
            {
                progress.Check = RehearsalsCheck;
                foreach (KeyValuePair<string, string> file in saved)
                {
                    if (!Go()) break;
                    rehearsals.Executed++;
                    ValidationCounter c = Rehearse(plan, report.ScenarioHash, file.Key, file.Value, out string note);
                    if (note != "") rehearsals.Notes.Add(note);
                    if (c == null) continue;
                    rehearsals.Failed++;
                    if (rehearsals.Counter == null) rehearsals.Counter = c;
                }
                rehearsals.Coverage = rehearsals.Executed == rehearsals.Planned ? Exhaustive : Sampled;
                Judge(rehearsals, stop, StopWords());
            }
            report.Executed = spent;
            Finish(report);
            return report;
        }

        /// <summary>
        /// Plays one counterexample again, exactly as it was found. Returns the counterexample as found now, or null when that
        /// combination no longer fails. A scatter or rehearsal counterexample needs the same inputs as the run that found it.
        /// </summary>
        public static ValidationCounter Reproduce(string text, ValidationCounter c, ValidationInputs inputs = null)
        {
            if (c == null) return null;
            ScenarioFile.Read(text ?? "", out MissionPlan plan);
            if (plan == null) return null;
            if (c.Check == CompletionCheck)
            {
                Finding bad = Try(text, c.Picks, c.Players, c.Seed, out string hash, out List<Finding> all);
                return bad == null ? null : CounterOf(plan, c.Picks, c.Players, c.Seed, hash, bad, all);
            }
            if (c.Check == ScatterCheck && inputs != null && inputs.Walk != null)
            {
                var counters = new List<ScatterSweep.Counter>();
                ScatterSweep.Try(text, c.Seed, c.Players, inputs.Walk, inputs.Insertions ?? new List<V3>(), counters);
                ScatterSweep.Counter broke = counters.Find(x => x.Kind != ScatterSweep.Stayed);
                return broke == null ? null : ScatterCounterOf(plan, broke, c.Players, counters);
            }
            if (c.Check == RehearsalsCheck && inputs != null && inputs.Rehearsals != null)
            {
                foreach (KeyValuePair<string, string> file in inputs.Rehearsals)
                    if (file.Key == c.Rehearsal) return Rehearse(plan, ScenarioFile.Hash(text), file.Key, file.Value, out string note);
            }
            return null;
        }

        /// <summary>
        /// Why a stored report no longer speaks for this scenario text and map ("" when it still does): another scenario text,
        /// another map file where the report used one, or an evaluator that judges differently. fileJson: the report as stored.
        /// currentMapPrint: the print of the map file here now ("" when unknown: the map is then not held against the report).
        /// </summary>
        public static string Stale(string fileJson, string currentText, string currentMapPrint)
        {
            string hash = "", print = ""; int evaluator = 0;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(fileJson ?? "", Lenient))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("report", out JsonElement inner) && inner.ValueKind == JsonValueKind.Object) root = inner;
                    if (root.ValueKind != JsonValueKind.Object) return "the stored report cannot be read";
                    if (root.TryGetProperty("scenarioHash", out JsonElement h) && h.ValueKind == JsonValueKind.String) hash = h.GetString();
                    if (root.TryGetProperty("mapPrint", out JsonElement m) && m.ValueKind == JsonValueKind.String) print = m.GetString();
                    if (root.TryGetProperty("evaluator", out JsonElement e) && e.ValueKind == JsonValueKind.Number) e.TryGetInt32(out evaluator);
                }
            }
            catch (JsonException) { return "the stored report cannot be read"; }
            if (hash != ScenarioFile.Hash(currentText ?? "")) return "the scenario has changed since this report was made";
            if (print != "" && !string.IsNullOrEmpty(currentMapPrint) && print != currentMapPrint) return "the map file has changed since this report was made";
            if (evaluator != Evaluator) return "this report was made by another version of the checks";
            return "";
        }

        // ----- one combination of each kind

        // The Director's order at a mission's start: a fresh strict read, the dice (only when something is left to chance), the
        // host's picks, then Completion.Check, and the target person taken out by the picks. Returns the first error, or null.
        private static Finding Try(string text, IDictionary<string, int> picks, int players, long seed, out string resolved, out List<Finding> all)
        {
            ScenarioFile.Read(text, out MissionPlan plan);
            resolved = "";
            all = new List<Finding>();
            if (plan == null) { Finding refused = new Finding { Code = "unreadable", Text = "the scenario cannot be read" }; all.Add(refused); return refused; }
            Variants.Out gone = null; int squad = 0;
            if (Variants.Any(plan)) { gone = Variants.Resolve(plan, new Rng(seed ^ VariantSalt), null, players); squad = players; }
            bool target = plan.Spawns.Exists(s => s.Hvt && s.Trigger == 0);
            Options.Apply(plan, picks);
            all = Completion.Check(plan, gone, squad);
            if (target && !plan.Spawns.Exists(s => s.Hvt && s.Trigger == 0))
                all.Add(new Finding { Code = Options.Unfinishable, Path = "options", Text = "the target person is left out by these choices" });
            resolved = ScenarioFile.Hash(plan.ToJson());
            return all.Find(f => f.Level == Finding.Error);
        }

        private static ValidationCounter CounterOf(MissionPlan file, IDictionary<string, int> picks, int players, long seed, string resolved, Finding bad, List<Finding> all)
        {
            var c = new ValidationCounter { Check = CompletionCheck, Seed = seed, Players = players, Resolved = resolved, Path = bad.Path, Found = bad.ToString() };
            foreach (KeyValuePair<string, int> p in picks) c.Picks[p.Key] = p.Value;
            c.Options = Options.Describe(file, picks);
            bool target = bad.Path == "options" || bad.Path.StartsWith("spawns", StringComparison.Ordinal);
            c.Expected = target ? "the target person is in the mission" : "the extraction can open, so the squad can leave";
            foreach (Finding f in all) if (f.Level == Finding.Error) { c.Trace.Add(f.ToString()); About(file, f.Path, f.Text, c); }
            // The finding names the trigger the extraction waits for; the triggers that could have opened it instead are as much
            // what the author has to look at, and the target person the picks took out is where the file has it.
            if (!target) foreach (TriggerPlan t in file.Triggers) if (t.OpensExtraction) AddTrigger(file, t.Id, c);
            if (bad.Path == "options")
                for (int i = 0; i < file.Spawns.Count; i++) if (file.Spawns[i].Hvt && file.Spawns[i].Trigger == 0 && !c.Spawns.Contains(i)) c.Spawns.Add(i);
            return c;
        }

        private static ValidationCounter ScatterCounterOf(MissionPlan file, ScatterSweep.Counter broke, int players, List<ScatterSweep.Counter> all)
        {
            var c = new ValidationCounter { Check = ScatterCheck, Seed = broke.Seed, Players = players, Found = broke.Text, Path = broke.Spawn >= 0 ? "spawns[" + broke.Spawn + "]" : "" };
            c.Options = Options.Describe(file, null);
            foreach (OptionPlan o in file.Options) if (o != null && o.Choices.Count > 0) c.Picks[o.Id ?? ""] = Options.Chosen(o, null);
            c.Expected = broke.Kind == ScatterSweep.SkipBreaks ? "leaving out an enemy that found no spot keeps the mission finishable" : "the dice and the default options leave the mission finishable";
            if (broke.Spawn >= 0) c.Spawns.Add(broke.Spawn);
            foreach (ScatterSweep.Counter x in all) { c.Trace.Add(x.Kind + ": " + x.Text); About(file, "", x.Text, c); }
            return c;
        }

        // One saved rehearsal against the scenario as it is now. null when it passes; note says when it was written for other text.
        private static ValidationCounter Rehearse(MissionPlan plan, string hash, string file, string text, out string note)
        {
            note = "";
            List<Finding> heard = Rehearsal.Read(text ?? "", out RehearsalScript s, plan);
            if (s == null)
            {
                Finding bad = heard.Find(f => f.Level == Finding.Error);
                var unread = new ValidationCounter { Check = RehearsalsCheck, Rehearsal = file, Expected = "the saved rehearsal can be read against this scenario",
                                                     Found = bad == null ? "it cannot be read" : bad.ToString(), Path = bad == null ? "" : bad.Path };
                foreach (Finding f in heard) if (f.Level == Finding.Error) unread.Trace.Add(f.ToString());
                return unread;
            }
            if (RehearsalFile.Match(s, hash, plan.Revision, out string why) == RehearsalFile.Changed) note = file + ": " + why;
            RehearsalResult r = Rehearsal.Run(plan, s);
            if (r.Passed) return null;
            var c = new ValidationCounter { Check = RehearsalsCheck, Rehearsal = file, Seed = s.Roll, Players = s.Players };
            StepResult first = r.Steps.Find(x => !x.Ok);
            if (r.Blocked != "" && first == null) { c.Expected = "the Director would use this roll"; c.Found = "refused: " + r.Blocked; About(plan, "", r.Blocked, c); }
            else if (first != null) { c.Expected = first.What + ": " + first.Expected; c.Found = first.Actual; About(plan, "", first.What, c); }
            if (r.Blocked != "") c.Trace.Add("refused at the start: " + r.Blocked);
            foreach (StepResult x in r.Steps) c.Trace.Add((x.Ok ? "ok " : "FAILED ") + x.What + (x.Ok ? "" : " (expected " + x.Expected + ", got " + x.Actual + ")"));
            return c;
        }

        // The triggers and enemies a finding is about: the one its path names first, then every trigger its words name.
        private static void About(MissionPlan file, string path, string words, ValidationCounter c)
        {
            Match at = Indexed.Match(path ?? "");
            if (at.Success && int.TryParse(at.Groups[2].Value, NumberStyles.Integer, Inv, out int i))
            {
                if (at.Groups[1].Value == "triggers" && i < file.Triggers.Count) AddTrigger(file, file.Triggers[i].Id, c);
                if (at.Groups[1].Value == "spawns" && i < file.Spawns.Count && !c.Spawns.Contains(i)) c.Spawns.Add(i);
            }
            foreach (Match m in TriggerWords.Matches(words ?? ""))
                foreach (string n in m.Groups[1].Value.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(n, NumberStyles.Integer, Inv, out int id)) AddTrigger(file, id, c);
        }

        private static void AddTrigger(MissionPlan file, int id, ValidationCounter c)
        {
            if (c.Triggers.Contains(id) || !file.Triggers.Exists(t => t.Id == id)) return;
            c.Triggers.Add(id);
        }

        // ----- what is tried

        private static List<int> Players(List<int> asked)
        {
            var list = new List<int>();
            if (asked != null) foreach (int p in asked) { int n = Math.Max(1, Math.Min(MaxPlayers, p)); if (!list.Contains(n)) list.Add(n); }
            if (list.Count == 0) list.AddRange(new[] { 1, 2, 4 });
            list.Sort();
            return list;
        }

        private static List<long> Seeds(List<long> asked)
        {
            var list = new List<long>();
            if (asked != null) foreach (long s in asked) { if (list.Count >= MaxSeeds) break; if (!list.Contains(s)) list.Add(s); }
            if (list.Count == 0) for (long s = 1; s <= DefaultSeeds; s++) list.Add(s);
            return list;
        }

        private static SortedDictionary<string, int> Picks(List<OptionPlan> options, int[] combo)
        {
            var picks = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int k = 0; k < options.Count; k++) picks[options[k].Id ?? ""] = combo[k];
            return picks;
        }

        // Every combination of the choices asked when there are at most MaxOptionCombinations, the first option counting
        // fastest. Otherwise a fixed sample: the defaults, each single choice away from them, then draws with a fixed salt.
        private static List<int[]> Combinations(List<OptionPlan> options, List<List<int>> allowed, out long total)
        {
            total = 1;
            foreach (List<int> a in allowed) { total *= a.Count; if (total > int.MaxValue) { total = int.MaxValue; break; } }
            var list = new List<int[]>();
            if (total <= MaxOptionCombinations)
            {
                for (long n = 0; n < total; n++)
                {
                    var combo = new int[options.Count];
                    long rest = n;
                    for (int k = 0; k < options.Count; k++) { combo[k] = allowed[k][(int)(rest % allowed[k].Count)]; rest /= allowed[k].Count; }
                    list.Add(combo);
                }
                return list;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(int[] combo) { if (list.Count < MaxOptionCombinations && seen.Add(string.Join(",", combo))) list.Add(combo); }
            var defaults = new int[options.Count];
            for (int k = 0; k < options.Count; k++) { int d = Options.Chosen(options[k], null); defaults[k] = allowed[k].Contains(d) ? d : allowed[k][0]; }
            Add(defaults);
            for (int k = 0; k < options.Count; k++)
                foreach (int c in allowed[k])
                {
                    if (c == defaults[k]) continue;
                    var one = (int[])defaults.Clone(); one[k] = c; Add(one);
                }
            var rng = new Rng(SampleSalt);
            for (int tries = 0; tries < MaxOptionCombinations * 8 && list.Count < MaxOptionCombinations; tries++)
            {
                var combo = new int[options.Count];
                for (int k = 0; k < options.Count; k++) combo[k] = allowed[k][rng.Range(0, allowed[k].Count)];
                Add(combo);
            }
            return list;
        }

        private static List<KeyValuePair<string, string>> Saved(List<KeyValuePair<string, string>> files)
        {
            if (files == null) return null;
            var list = new List<KeyValuePair<string, string>>(files);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            if (list.Count > MaxRehearsals) list.RemoveRange(MaxRehearsals, list.Count - MaxRehearsals);
            return list;
        }

        // Why the scatter seeds cannot be tried on this computer, or "" when the matching map is here.
        private static string MapProblem(MissionPlan plan, ValidationInputs inputs)
        {
            string nav = plan.NavFile ?? "";
            if (nav == "") return "not run: the scenario names no map file (navFile), and scatter needs the map's walkable ground";
            if (inputs.Walk == null)
                return "not run: needs the matching map file " + nav + " on this computer (the game's NavMeshData folder, read by the game's plugin)" + (string.IsNullOrEmpty(inputs.MapWhy) ? "" : "; " + inputs.MapWhy);
            if (!string.IsNullOrEmpty(inputs.NavFile) && !string.Equals(inputs.NavFile, nav, StringComparison.OrdinalIgnoreCase))
                return "not run: the map file here is " + inputs.NavFile + ", the scenario is for " + nav;
            if (!string.IsNullOrEmpty(plan.MapPrint) && !string.IsNullOrEmpty(inputs.MapPrint) && plan.MapPrint != inputs.MapPrint)
                return "not run: the map file " + nav + " here is not the one the scenario was made on (its print differs), so the scattered spots would not be the author's";
            return "";
        }

        // A check's state once its loop is over: a failure wins; a loop that stopped early is partial and says why.
        private static void Judge(ValidationCheck c, string stop, string why)
        {
            bool cut = c.Executed < c.Planned;
            if (c.Failed > 0) c.State = Failed;
            else if (cut) c.State = c.Executed == 0 ? NotRun : Partial;
            else c.State = Passed;
            if (cut) c.Why = (c.Executed == 0 ? "not run: " : "stopped early: ") + (stop == "" ? "the run stopped" : why) + "; " + c.Executed + " of " + c.Planned + " combinations executed";
            else if (c.Failed > 0) c.Why = c.Failed + " of " + c.Executed + (c.Executed == 1 ? " combination fails" : " combinations fail");
            if (c.Executed == 0) c.Coverage = "";
        }

        private static void Finish(ValidationReport report)
        {
            bool failed = report.Checks.Exists(c => c.State == Failed);
            bool gap = report.Checks.Exists(c => c.State == NotRun || c.State == Partial);
            report.Verdict = failed ? Failed : gap || report.Cancelled || report.BudgetHit ? Incomplete : Passed;
            var parts = new List<string>();
            foreach (ValidationCheck c in report.Checks)
            {
                string what = c.Name == Format ? "the strict format" : c.Name == CompletionCheck ? "completion across options, squad sizes and seeds"
                            : c.Name == ScatterCheck ? "scatter seeds on the map" : "saved rehearsals";
                if (c.State == NotRun || c.State == NotNeeded) parts.Add(what + ": " + (c.State == NotNeeded ? "not needed" : "not run"));
                else parts.Add(what + ": " + c.Executed + " of " + c.Planned + (c.Planned == 1 ? " combination" : " combinations") + " executed (" + c.Coverage + "), " + c.State);
            }
            report.Covers = "Checked " + string.Join("; ", parts) + ". Squad sizes " + string.Join(", ", report.Players) +
                            (report.RollsAlike ? "; nothing is left to chance, so one roll covers every roll." : "; seeds " + Range(report.Seeds) + ".");
        }

        private static string Range(List<long> seeds)
        {
            bool run = seeds.Count > 2;
            for (int i = 1; i < seeds.Count && run; i++) run = seeds[i] == seeds[i - 1] + 1;
            return run ? seeds[0].ToString(Inv) + " to " + seeds[seeds.Count - 1].ToString(Inv) : string.Join(", ", seeds.ConvertAll(s => s.ToString(Inv)));
        }

        // ----- the request and the report as JSON

        /// <summary>
        /// Reads POST /api/validate {"scenario": text, "checks": ["completion", "scatter", "rehearsals"], "players": [1, 2, 4],
        /// "seeds": [1, 2, ...] or a count, "budget": n, "choices": {"option id": [0, 1]}, "insertions": [[x, y, z], ...]}.
        /// Returns "" with what was asked, or why the request cannot be used.
        /// </summary>
        public static string Ask(string body, out string scenario, out ValidationAsk ask, out List<V3> insertions)
        {
            scenario = null; ask = new ValidationAsk(); insertions = new List<V3>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body ?? "", Lenient))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return "a validation is sent as {scenario, checks, players, seeds, budget}";
                    if (root.TryGetProperty("scenario", out JsonElement sc) && sc.ValueKind == JsonValueKind.String) scenario = sc.GetString();
                    if (root.TryGetProperty("checks", out JsonElement ch) && ch.ValueKind == JsonValueKind.Array)
                    {
                        var names = new HashSet<string>(StringComparer.Ordinal);
                        foreach (JsonElement e in ch.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) names.Add(e.GetString());
                        ask.Completion = names.Contains(CompletionCheck); ask.Scatter = names.Contains(ScatterCheck); ask.Rehearsals = names.Contains(RehearsalsCheck);
                    }
                    if (root.TryGetProperty("players", out JsonElement pl) && pl.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in pl.EnumerateArray())
                        {
                            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out int n) || n < 1 || n > MaxPlayers) return "every squad size is a whole number from 1 to " + MaxPlayers;
                            ask.Players.Add(n);
                        }
                    if (root.TryGetProperty("seeds", out JsonElement sd))
                    {
                        if (sd.ValueKind == JsonValueKind.Number && sd.TryGetInt32(out int count) && count >= 1)
                            for (int i = 1; i <= Math.Min(count, MaxSeeds); i++) ask.Seeds.Add(i);
                        else if (sd.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in sd.EnumerateArray())
                            {
                                if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out long one)) return "every seed is a whole number";
                                if (ask.Seeds.Count >= MaxSeeds) return "at most " + MaxSeeds + " seeds are tried at once";
                                if (!ask.Seeds.Contains(one)) ask.Seeds.Add(one);
                            }
                    }
                    if (root.TryGetProperty("budget", out JsonElement bu) && bu.ValueKind == JsonValueKind.Number && bu.TryGetInt32(out int budget)) ask.Budget = budget;
                    if (root.TryGetProperty("choices", out JsonElement cs) && cs.ValueKind == JsonValueKind.Object)
                        foreach (JsonProperty p in cs.EnumerateObject())
                        {
                            if (p.Value.ValueKind != JsonValueKind.Array) return "the choices of an option are a list of choice numbers";
                            var list = new List<int>();
                            foreach (JsonElement e in p.Value.EnumerateArray()) if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int c)) list.Add(c);
                            ask.Choices[p.Name] = list;
                        }
                    if (root.TryGetProperty("insertions", out JsonElement ins) && ins.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in ins.EnumerateArray())
                        {
                            if (insertions.Count >= ScatterSweep.MaxInsertions) break;
                            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 3 || e[0].ValueKind != JsonValueKind.Number || e[1].ValueKind != JsonValueKind.Number || e[2].ValueKind != JsonValueKind.Number)
                                return "an insertion point is [x, y, z]";
                            insertions.Add(new V3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle()));
                        }
                }
            }
            catch (JsonException) { return "the request is not JSON"; }
            if (string.IsNullOrEmpty(scenario)) return "the scenario text is missing; nothing was checked";
            if (scenario.Length > RevisionFile.MaxText) return "the scenario text is too long to be a scenario; nothing was checked";
            return "";
        }

        /// <summary>The report as JSON. Decided by the inputs alone: two runs on the same inputs give the same text.</summary>
        public static string Json(ValidationReport r)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"schema\":\"").Append(Schema).Append("\",\"evaluator\":").Append(Evaluator)
              .Append(",\"scenarioHash\":\"").Append(Guard.Esc(r.ScenarioHash)).Append("\",\"scenarioId\":\"").Append(Guard.Esc(r.ScenarioId))
              .Append("\",\"revision\":").Append(r.Revision.ToString(Inv)).Append(",\"resolvedHash\":\"").Append(Guard.Esc(r.ResolvedHash))
              .Append("\",\"navFile\":\"").Append(Guard.Esc(r.NavFile)).Append("\",\"mapPrint\":\"").Append(Guard.Esc(r.MapPrint)).Append('"');
            sb.Append(",\"players\":[").Append(string.Join(",", r.Players.ConvertAll(p => p.ToString(Inv)))).Append(']');
            sb.Append(",\"seeds\":[").Append(string.Join(",", r.Seeds.ConvertAll(s => s.ToString(Inv)))).Append(']');
            sb.Append(",\"rollsAlike\":").Append(r.RollsAlike ? "true" : "false");
            sb.Append(",\"options\":"); Strings(sb, r.Options);
            sb.Append(",\"budget\":").Append(r.Budget.ToString(Inv)).Append(",\"executed\":").Append(r.Executed.ToString(Inv))
              .Append(",\"budgetHit\":").Append(r.BudgetHit ? "true" : "false").Append(",\"cancelled\":").Append(r.Cancelled ? "true" : "false")
              .Append(",\"verdict\":\"").Append(r.Verdict).Append("\",\"covers\":\"").Append(Guard.Esc(r.Covers)).Append("\",\"limits\":\"").Append(Guard.Esc(Limits)).Append('"');
            sb.Append(",\"checks\":[");
            for (int i = 0; i < r.Checks.Count; i++)
            {
                ValidationCheck c = r.Checks[i];
                sb.Append(i > 0 ? "," : "").Append("{\"name\":\"").Append(c.Name).Append("\",\"state\":\"").Append(c.State).Append("\",\"coverage\":\"").Append(c.Coverage)
                  .Append("\",\"planned\":").Append(c.Planned.ToString(Inv)).Append(",\"executed\":").Append(c.Executed.ToString(Inv)).Append(",\"failed\":").Append(c.Failed.ToString(Inv))
                  .Append(",\"why\":\"").Append(Guard.Esc(c.Why)).Append("\",\"notes\":");
                Strings(sb, c.Notes);
                sb.Append(",\"counterexample\":"); Counter(sb, c.Counter);
                sb.Append('}');
            }
            sb.Append("],\"first\":"); Counter(sb, r.First);
            return sb.Append('}').ToString();
        }

        /// <summary>The report as kept under validations/&lt;scenario id&gt;/: the compared part, with the time it was made outside it.</summary>
        public static string File(ValidationReport r, string made) =>
            "{\"schema\":\"" + Schema + "\",\"made\":\"" + Guard.Esc(made ?? "") + "\",\"report\":" + Json(r) + "}\n";

        /// <summary>A counterexample on its own, as kept beside the report: enough to play it again with the Seed setting and picks.</summary>
        public static string CounterFile(ValidationReport r, string made)
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"schema\":\"").Append(Schema).Append("-counterexample\",\"made\":\"").Append(Guard.Esc(made ?? "")).Append("\",\"evaluator\":").Append(Evaluator)
              .Append(",\"scenarioHash\":\"").Append(Guard.Esc(r.ScenarioHash)).Append("\",\"mapPrint\":\"").Append(Guard.Esc(r.MapPrint)).Append("\",\"counterexample\":");
            Counter(sb, r.First);
            return sb.Append("}\n").ToString();
        }

        private static void Counter(StringBuilder sb, ValidationCounter c)
        {
            if (c == null) { sb.Append("null"); return; }
            sb.Append("{\"check\":\"").Append(c.Check).Append("\",\"seed\":").Append(c.Seed.ToString(Inv)).Append(",\"players\":").Append(c.Players.ToString(Inv)).Append(",\"picks\":{");
            bool first = true;
            foreach (KeyValuePair<string, int> p in c.Picks) { sb.Append(first ? "" : ",").Append('"').Append(Guard.Esc(p.Key)).Append("\":").Append(p.Value.ToString(Inv)); first = false; }
            sb.Append("},\"options\":\"").Append(Guard.Esc(c.Options)).Append("\",\"expected\":\"").Append(Guard.Esc(c.Expected)).Append("\",\"found\":\"").Append(Guard.Esc(c.Found))
              .Append("\",\"path\":\"").Append(Guard.Esc(c.Path)).Append("\",\"rehearsal\":\"").Append(Guard.Esc(c.Rehearsal)).Append("\",\"resolved\":\"").Append(Guard.Esc(c.Resolved)).Append('"');
            sb.Append(",\"triggers\":[").Append(string.Join(",", c.Triggers.ConvertAll(t => t.ToString(Inv)))).Append(']');
            sb.Append(",\"spawns\":[").Append(string.Join(",", c.Spawns.ConvertAll(s => s.ToString(Inv)))).Append(']');
            sb.Append(",\"trace\":"); Strings(sb, c.Trace);
            sb.Append('}');
        }

        private static void Strings(StringBuilder sb, List<string> list)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++) sb.Append(i > 0 ? "," : "").Append('"').Append(Guard.Esc(list[i])).Append('"');
            sb.Append(']');
        }
    }
}
