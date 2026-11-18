using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// Host options (ML-022): named choices an author offers ("Garrison size: Light or Full"), each of which leaves out lots
    /// of enemies, switches triggers off, or sets how often players come back and how good the enemies are. They are applied
    /// after the dice (Variants.Resolve) and consume no randomness, so the same seed rolls the same dice whatever is picked.
    /// Validate tries the choices against the scenario's own rules before anyone plays them.
    /// </summary>
    public static class Options
    {
        public const int MaxCombinations = 64;        // more than this and the choices tried are a sample, said so in a note
        public const int DefaultSeeds = 20, MaxSeeds = 200;
        public const string Unfinishable = "unfinishable";
        private const long SampleSalt = 0x0F7105;      // fixed: the sampled combinations are the same on every run
        private const long VariantSalt = 0x5EED;       // the salt the Director rolls variants with, so a failing roll reproduces in the game
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>
        /// The choice used for an option: the host's pick when it names one of the choices, otherwise the author's default.
        /// An unknown id or a pick out of range falls back to the default rather than refusing the mission.
        /// </summary>
        public static int Chosen(OptionPlan option, IDictionary<string, int> picks)
        {
            int count = option.Choices.Count;
            if (picks != null && picks.TryGetValue(option.Id ?? "", out int pick) && pick >= 0 && pick < count) return pick;
            return option.Pick >= 0 && option.Pick < count ? option.Pick : 0;
        }

        /// <summary>
        /// Applies the chosen choice of every option to a plan whose dice have already been rolled: enemies of a dropped lot are
        /// taken out, dropped triggers are switched off, and respawns and skill are set when the choice says so (a later option
        /// wins over an earlier one). The author's own respawns and skill are kept in RespawnTimesAuthored / SkillAuthored, so a
        /// Game Master save writes them back. Consumes no randomness.
        /// </summary>
        public static void Apply(MissionPlan plan, IDictionary<string, int> picks)
        {
            if (plan == null || plan.Options == null || plan.Options.Count == 0) return;
            var lots = new HashSet<int>();
            var triggers = new HashSet<int>();
            foreach (OptionPlan option in plan.Options)
            {
                if (option == null || option.Choices.Count == 0) continue;
                ChoicePlan choice = option.Choices[Chosen(option, picks)];
                foreach (int lot in choice.DropLots) if (lot > 0) lots.Add(lot);
                foreach (int id in choice.DropTriggers) triggers.Add(id);
                if (choice.RespawnTimes >= 0)
                {
                    if (!plan.RespawnTimesAuthored.HasValue) plan.RespawnTimesAuthored = plan.RespawnTimes;
                    plan.RespawnTimes = choice.RespawnTimes;
                }
                if (!string.IsNullOrEmpty(choice.Skill))
                {
                    if (plan.SkillAuthored == null) plan.SkillAuthored = plan.Skill ?? "";
                    plan.Skill = choice.Skill;
                }
            }
            if (lots.Count > 0) plan.Spawns.RemoveAll(s => s.Lot > 0 && lots.Contains(s.Lot));
            if (triggers.Count > 0) foreach (TriggerPlan t in plan.Triggers) if (triggers.Contains(t.Id)) t.Off = true;
        }

        /// <summary>The picks as the host reads them: "garrison=1 'Light', alarm=0 'Loud'".</summary>
        public static string Describe(MissionPlan plan, IDictionary<string, int> picks)
        {
            var parts = new List<string>();
            if (plan != null && plan.Options != null)
                foreach (OptionPlan option in plan.Options)
                {
                    if (option == null || option.Choices.Count == 0) continue;
                    int at = Chosen(option, picks);
                    parts.Add(option.Id + "=" + at.ToString(CultureInfo.InvariantCulture) + " '" + option.Choices[at].Name + "'");
                }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Tries every combination of choices (a fixed sample of 64 when there are more) with every squad size the author
        /// recommends (1 to 4 when none is said) and rolls 1 to seeds: the dice, then the options, then Completion.Check.
        /// Each combination that makes the mission unfinishable gives one error naming the combination, squad size and roll,
        /// so it can be played again with that Seed. A note says what was tried: rolls are a sample, never proof of every roll.
        /// The plan is not changed.
        /// </summary>
        public static List<Finding> Validate(MissionPlan plan, int seeds)
        {
            var found = new List<Finding>();
            if (plan == null) return found;
            seeds = Math.Max(1, Math.Min(MaxSeeds, seeds));
            bool dice = Variants.Any(plan);
            int rolls = dice ? seeds : 1;                  // with nothing left to chance every roll is the same mission
            int low = plan.SquadMin > 0 && plan.SquadMax >= plan.SquadMin ? plan.SquadMin : 1;
            int high = plan.SquadMin > 0 && plan.SquadMax >= plan.SquadMin ? plan.SquadMax : 4;

            var options = new List<OptionPlan>();
            if (plan.Options != null) foreach (OptionPlan o in plan.Options) if (o != null && o.Choices.Count > 0) options.Add(o);
            List<int[]> combos = Combinations(options, out long total);
            bool sampled = total > combos.Count;

            // Variants.Resolve and Apply switch triggers off on the shared trigger objects: their state is put back at the end.
            var wasOff = new bool[plan.Triggers.Count];
            for (int i = 0; i < wasOff.Length; i++) wasOff[i] = plan.Triggers[i].Off;
            var notes = new HashSet<string>();
            try
            {
                foreach (int[] combo in combos)
                {
                    var picks = new Dictionary<string, int>();
                    for (int k = 0; k < options.Count; k++) picks[options[k].Id ?? ""] = combo[k];
                    Finding first = null; int failed = 0, tried = 0; string where = "";
                    for (int squad = low; squad <= high; squad++)
                        for (int roll = 1; roll <= rolls; roll++)
                        {
                            tried++;
                            MissionPlan copy = Copy(plan);
                            foreach (TriggerPlan t in copy.Triggers) t.Off = false;
                            Variants.Out gone = Variants.Resolve(copy, new Rng(roll ^ VariantSalt), null, squad);
                            bool target = copy.Spawns.Exists(s => s.Hvt && s.Trigger == 0);
                            Apply(copy, picks);
                            Finding bad = null;
                            if (target && !copy.Spawns.Exists(s => s.Hvt && s.Trigger == 0))
                                bad = new Finding { Code = Unfinishable, Path = "options", Text = "the target person is left out by these choices" };
                            foreach (Finding f in Completion.Check(copy, gone, squad))
                            {
                                if (f.Level == Finding.Note) { notes.Add(f.ToString()); continue; }
                                if (bad == null) bad = new Finding { Code = Unfinishable, Path = f.Path, Text = f.Text };
                            }
                            if (bad == null) continue;
                            failed++;
                            if (first == null) { first = bad; where = squad + (squad == 1 ? " player" : " players") + (dice ? " and roll " + roll : ""); }
                        }
                    if (first == null) continue;
                    string with = options.Count == 0 ? "" : "with " + Describe(plan, picks) + ", ";
                    found.Add(new Finding { Code = Unfinishable, Path = first.Path,
                                            Text = with + where + ": " + first.Text + " (" + failed + " of " + tried + " tries fail)" });
                }
            }
            finally
            {
                for (int i = 0; i < wasOff.Length; i++) plan.Triggers[i].Off = wasOff[i];
            }

            var sorted = new List<string>(notes);
            sorted.Sort(StringComparer.Ordinal);
            foreach (string n in sorted) found.Add(new Finding { Level = Finding.Note, Text = n });
            if (sampled)
                found.Add(new Finding { Level = Finding.Note, Path = "options",
                                        Text = "coverage is sampled: " + total + " combinations of choices is more than " + MaxCombinations + ", so " + combos.Count + " of them were tried" });
            found.Add(new Finding { Level = Finding.Note,
                                    Text = "tried " + combos.Count + (combos.Count == 1 ? " combination" : " combinations") + " of choices, squad sizes " + low + " to " + high +
                                           (dice ? " and rolls 1 to " + rolls + ": a sample of rolls, not proof that every roll can be finished" : "; nothing is left to chance, so one roll covers every roll") });
            return found;
        }

        /// <summary>
        /// The browser link's options check: {"scenario": text, "seeds": n}. The text is read strictly, as at a mission's
        /// start; a readable scenario is validated. Answered with {"readable", "findings"}.
        /// </summary>
        public static string Answer(string body, out int status)
        {
            status = 400;
            string scenario = null; int seeds = DefaultSeeds;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body ?? "", Lenient))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return Error("an options check is sent as {scenario, seeds}");
                    if (root.TryGetProperty("scenario", out JsonElement sc) && sc.ValueKind == JsonValueKind.String) scenario = sc.GetString();
                    if (root.TryGetProperty("seeds", out JsonElement sd) && sd.ValueKind == JsonValueKind.Number && sd.TryGetInt32(out int n)) seeds = n;
                }
            }
            catch (JsonException) { return Error("the request is not JSON"); }
            if (string.IsNullOrEmpty(scenario)) return Error("the scenario text is missing; nothing was checked");
            if (scenario.Length > RevisionFile.MaxText) return Error("the scenario text is too long to be a scenario; nothing was checked");
            status = 200;
            List<Finding> read = ScenarioFile.Read(scenario, out MissionPlan plan);
            var sb = new StringBuilder(1024);
            sb.Append("{\"readable\":").Append(plan != null ? "true" : "false").Append(",\"findings\":");
            CatalogueInfo.WriteFindings(sb, plan != null ? Validate(plan, seeds) : read);
            return sb.Append('}').ToString();
        }

        private static string Error(string why) => "{\"error\":\"" + Guard.Esc(why) + "\"}";

        // Every combination when there are at most MaxCombinations, the first option counting fastest. Otherwise a fixed
        // sample: the defaults, then each single choice away from the defaults, then combinations drawn with a fixed salt.
        private static List<int[]> Combinations(List<OptionPlan> options, out long total)
        {
            total = 1;
            foreach (OptionPlan o in options) { total *= o.Choices.Count; if (total > int.MaxValue) { total = int.MaxValue; break; } }
            var list = new List<int[]>();
            if (total <= MaxCombinations)
            {
                for (int n = 0; n < total; n++)
                {
                    var combo = new int[options.Count];
                    int rest = n;
                    for (int k = 0; k < options.Count; k++) { combo[k] = rest % options[k].Choices.Count; rest /= options[k].Choices.Count; }
                    list.Add(combo);
                }
                return list;
            }
            var seen = new HashSet<string>();
            void Add(int[] combo) { if (list.Count < MaxCombinations && seen.Add(string.Join(",", combo))) list.Add(combo); }
            var defaults = new int[options.Count];
            for (int k = 0; k < options.Count; k++) defaults[k] = Chosen(options[k], null);
            Add(defaults);
            for (int k = 0; k < options.Count; k++)
                for (int c = 0; c < options[k].Choices.Count; c++)
                {
                    if (c == defaults[k]) continue;
                    var one = (int[])defaults.Clone(); one[k] = c; Add(one);
                }
            var rng = new Rng(SampleSalt);
            for (int tries = 0; tries < MaxCombinations * 8 && list.Count < MaxCombinations; tries++)
            {
                var combo = new int[options.Count];
                for (int k = 0; k < options.Count; k++) combo[k] = rng.Range(0, options[k].Choices.Count);
                Add(combo);
            }
            return list;
        }

        // What Variants, Apply and Completion read, with lists of their own so that taking things out leaves the plan alone.
        // Triggers are shared: only their Off flag changes, and Validate puts it back.
        private static MissionPlan Copy(MissionPlan plan) => new MissionPlan
        {
            Spawns = new List<SpawnPlan>(plan.Spawns), Doors = new List<DoorPlan>(plan.Doors), Things = new List<ThingPlan>(plan.Things),
            Triggers = new List<TriggerPlan>(plan.Triggers), Options = plan.Options, ExtractionAfter = plan.ExtractionAfter, GameMaster = plan.GameMaster,
            Skill = plan.Skill, RespawnTimes = plan.RespawnTimes, RespawnAfter = plan.RespawnAfter, SquadMin = plan.SquadMin, SquadMax = plan.SquadMax,
        };
    }
}
