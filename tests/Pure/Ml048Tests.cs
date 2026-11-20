using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MissionLab.Core;

/// <summary>
/// ML-048: one validation run over a scenario, on synthetic plans and a synthetic flat ground. A host option that switches
/// off the timer the extraction waits for leaves the officer (only there with two or more players) as the only way out, so
/// that combination fails for one player alone: the run must name the exact seed, picks and squad size, and playing that
/// combination again must fail the same way. Coverage, the work budget, cancellation, determinism and staleness are pinned.
/// </summary>
static class Ml048Tests
{
    private const string Head = "{\"schema\":\"missionlab-plan/1\",\"id\":\"sc-broken\",\"revision\":3,\"navFile\":\"synthetic.bytes\",\"extraction\":{\"after\":1},";
    private const string Triggers = "\"triggers\":[{\"id\":1,\"name\":\"Timer\",\"when\":\"time\",\"delay\":30},{\"id\":2,\"name\":\"Officer down\",\"when\":\"down\",\"tag\":\"officer\",\"count\":1,\"extraction\":\"open\"}],";
    private const string Pace = "\"options\":[{\"id\":\"pace\",\"name\":\"Pace\",\"choices\":[{\"name\":\"Slow\"},{\"name\":\"Fast\",\"drop\":{\"triggers\":[1]}}]}],";

    // The officer needs two players, so a lone player never meets him; "Fast" drops the timer.
    private static string Broken(string extraSpawn = "") =>
        Head + Triggers + Pace + "\"spawns\":[{\"pos\":[10,0,10]},{\"pos\":[20,0,20],\"tag\":\"officer\",\"minPlayers\":2}" + extraSpawn + "]}";

    private const string Passes = "{\"schema\":\"missionlab-rehearsal/1\",\"name\":\"timer opens\",\"players\":2,\"roll\":1,\"steps\":[{\"at\":31,\"expect\":{\"fired\":[1]}}]}";
    private const string FailsEarly = "{\"schema\":\"missionlab-rehearsal/1\",\"name\":\"officer early\",\"players\":2,\"roll\":1,\"steps\":[{\"at\":10,\"expect\":{\"fired\":[2]}}]}";

    // Flat open ground at height 0 over 0..100 on both axes, all one island. cancel: cancelled when the ground is first asked.
    private sealed class FlatWalk : IWalk
    {
        private readonly CancellationTokenSource _cancel;
        public FlatWalk(CancellationTokenSource cancel = null) { _cancel = cancel; }
        public bool Snap(V3 p, float maxRise, out V3 onGround)
        {
            if (_cancel != null) _cancel.Cancel();
            onGround = new V3(p.X, 0f, p.Z);
            return p.X >= 0 && p.X <= 100 && p.Z >= 0 && p.Z <= 100 && Math.Abs(p.Y) <= maxRise;
        }
        public int Island(V3 p) => p.X >= 0 && p.X <= 100 && p.Z >= 0 && p.Z <= 100 && Math.Abs(p.Y) <= Scatter.SameFloor ? 0 : -1;
    }

    private static ValidationAsk Ask(int budget = MissionValidation.DefaultBudget) =>
        new ValidationAsk { Players = new List<int> { 1, 2, 4 }, Seeds = new List<long> { 5, 6, 7 }, Budget = budget };

    private static ValidationInputs Inputs(IWalk walk, string print = "") => new ValidationInputs
    {
        Walk = walk, NavFile = walk == null ? "" : "synthetic.bytes", MapPrint = print,
        Rehearsals = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("timer_opens.json", Passes) },
    };

    private static ValidationCheck Of(ValidationReport r, string name) => r.Checks.Single(c => c.Name == name);

    private static string Say(ValidationReport r) => string.Join(" | ", r.Checks.Select(c => c.Name + ":" + c.State + " " + c.Executed + "/" + c.Planned + " " + c.Coverage + " " + c.Why));

    public static void Run(Action<string, bool, string> check)
    {
        string broken = Broken();
        {   // The broken combination is found with everything needed to play it again, and playing it again fails the same way.
            ValidationReport r = MissionValidation.Run(broken, Ask(), Inputs(null), CancellationToken.None);
            ValidationCheck done = Of(r, MissionValidation.CompletionCheck);
            ValidationCounter c = done.Counter;
            check("the run fails on the combination that leaves one player no way out", r.Verdict == MissionValidation.Failed && done.State == MissionValidation.Failed && c != null, Say(r));
            if (c != null)
            {
                check("the counterexample is the first failing combination: Fast, one player, the first seed asked",
                      c.Seed == 5 && c.Players == 1 && c.Picks.Count == 1 && c.Picks["pace"] == 1, c.Seed + " " + c.Players + " " + string.Join(",", c.Picks));
                check("it says what was expected and names the extraction's trigger as the finding's place", c.Expected.Contains("extraction") && c.Path == "triggers[0]" && c.Found.Contains("nobody could leave"), c.Expected + " | " + c.Path + " | " + c.Found);
                check("it names the trigger the choice switched off and the only other opener", c.Triggers.SequenceEqual(new[] { 1, 2 }), string.Join(",", c.Triggers));
                check("it keeps the fingerprint of the mission as it was set up", c.Resolved.StartsWith("fnv1a64:"), c.Resolved);
                ValidationCounter again = MissionValidation.Reproduce(broken, c);
                check("playing exactly that combination again fails with the same finding",
                      again != null && again.Seed == c.Seed && again.Players == c.Players && again.Found == c.Found && again.Path == c.Path && again.Resolved == c.Resolved, again == null ? "passed" : again.Found);
                var two = new ValidationCounter { Check = c.Check, Seed = c.Seed, Players = 2, Picks = c.Picks };
                check("the same picks with two players pass, so the squad size is what breaks it", MissionValidation.Reproduce(broken, two) == null, "");
            }
            check("only the three seeds of Fast with one player fail, of 2 x 3 x 3", done.Failed == 3 && done.Executed == 18 && done.Planned == 18, done.Failed + " of " + done.Executed);
            check("with dice in the scenario the seeds make the coverage a sample", done.Coverage == MissionValidation.Sampled && !r.RollsAlike, done.Coverage);
            check("the report names the seeds, squad sizes and options it tried", r.Seeds.SequenceEqual(new long[] { 5, 6, 7 }) && r.Players.SequenceEqual(new[] { 1, 2, 4 }) && r.Options.Count == 1 && r.Options[0].StartsWith("pace: choices 0, 1"), string.Join(";", r.Options));
            check("the report names the scenario, its revision and what the missions were set up as", r.ScenarioHash == ScenarioFile.Hash(broken) && r.ScenarioId == "sc-broken" && r.Revision == 3 && r.ResolvedHash.StartsWith("fnv1a64:"), r.ScenarioHash);
        }
        {   // With no map here the scatter seeds are not run and say why; the logic checks still run on their own.
            ValidationReport r = MissionValidation.Run(Broken(",{\"pos\":[50,0,50],\"scatter\":5}"), Ask(), Inputs(null), CancellationToken.None);
            ValidationCheck sc = Of(r, MissionValidation.ScatterCheck);
            check("without the map the scatter check is not run, with a reason", sc.State == MissionValidation.NotRun && sc.Executed == 0 && sc.Why.Contains("synthetic.bytes"), sc.State + " " + sc.Why);
            check("and the report names no map print it did not use", r.MapPrint == "" && r.NavFile == "", r.MapPrint);
            check("completion still ran without map data", Of(r, MissionValidation.CompletionCheck).Executed == 18, Say(r));
            check("a report with a check not run is never called a pass", r.Verdict != MissionValidation.Passed, r.Verdict);
        }
        {   // Nothing left to chance and every option combination tried: exhaustive, and green says what it covered and what it does not.
            string fine = Head + Triggers + Pace + "\"spawns\":[{\"pos\":[10,0,10]},{\"pos\":[20,0,20],\"tag\":\"officer\"}]}";
            ValidationReport r = MissionValidation.Run(fine, Ask(), Inputs(null), CancellationToken.None);
            ValidationCheck done = Of(r, MissionValidation.CompletionCheck);
            check("with nothing left to chance one roll covers every roll, so every combination is 2 x 3", done.Coverage == MissionValidation.Exhaustive && done.Executed == 6 && done.Planned == 6 && r.RollsAlike, done.Coverage + " " + done.Executed);
            check("the saved rehearsal passes and every one was run", Of(r, MissionValidation.RehearsalsCheck).State == MissionValidation.Passed && Of(r, MissionValidation.RehearsalsCheck).Executed == 1, Say(r));
            check("a scenario without scatter does not need the map", Of(r, MissionValidation.ScatterCheck).State == MissionValidation.NotNeeded, Say(r));
            check("so the verdict is a pass", r.Verdict == MissionValidation.Passed, Say(r));
            string json = MissionValidation.Json(r);
            check("a green report says it does not certify combat balance, navigation or multiplayer behaviour",
                  json.Contains("\"limits\":") && MissionValidation.Limits.Contains("combat balance") && MissionValidation.Limits.Contains("navigation") && MissionValidation.Limits.Contains("multiplayer"), "");
        }
        {   // More combinations of choices than the cap: a fixed sample, said so, with the number actually executed.
            var options = Enumerable.Range(1, 5).Select(k => "{\"id\":\"o" + k + "\",\"name\":\"O" + k + "\",\"choices\":[{\"name\":\"a\"},{\"name\":\"b\"},{\"name\":\"c\"},{\"name\":\"d\"}]}");
            string many = Head + Triggers + "\"options\":[" + string.Join(",", options) + "],\"spawns\":[{\"pos\":[10,0,10]},{\"pos\":[20,0,20],\"tag\":\"officer\"}]}";
            ValidationReport r = MissionValidation.Run(many, Ask(), Inputs(null), CancellationToken.None);
            ValidationCheck done = Of(r, MissionValidation.CompletionCheck);
            check("1024 combinations of choices are sampled down to the cap, and the count executed is the sample", done.Coverage == MissionValidation.Sampled && done.Executed == MissionValidation.MaxOptionCombinations * 3 && done.Executed == done.Planned, done.Coverage + " " + done.Executed);
            ValidationReport again = MissionValidation.Run(many, Ask(), Inputs(null), CancellationToken.None);
            check("the sample is the same on every run", MissionValidation.Json(again) == MissionValidation.Json(r), "");
        }
        {   // The work budget stops the run early and says so; what was not reached is not run, never passed.
            ValidationReport r = MissionValidation.Run(broken, Ask(4), Inputs(new FlatWalk()), CancellationToken.None);
            ValidationCheck done = Of(r, MissionValidation.CompletionCheck);
            check("a budget of 4 executes 4 combinations of the 18 and stops", r.BudgetHit && r.Executed == 4 && done.Executed == 4 && done.Planned == 18 && done.State == MissionValidation.Partial, Say(r));
            check("the stopped check says how far it got", done.Why.Contains("4 of 18"), done.Why);
            check("the rehearsals it did not reach are not run", Of(r, MissionValidation.RehearsalsCheck).State == MissionValidation.NotRun, Say(r));
            check("a run stopped by its budget is incomplete, not a pass", r.Verdict == MissionValidation.Incomplete, r.Verdict);
        }
        {   // Cancelling keeps what was found: the completion counterexample, and the one scatter seed that ran.
            var cancel = new CancellationTokenSource();
            ValidationReport r = MissionValidation.Run(Broken(",{\"pos\":[50,0,50],\"scatter\":5}"), Ask(), Inputs(new FlatWalk(cancel)), cancel.Token);
            ValidationCheck sc = Of(r, MissionValidation.ScatterCheck);
            check("the run says it was cancelled", r.Cancelled && !r.BudgetHit, Say(r));
            check("the completion results from before the cancel are kept", Of(r, MissionValidation.CompletionCheck).Counter != null && Of(r, MissionValidation.CompletionCheck).Executed == 18, Say(r));
            check("the scatter check ran one seed and is partial", sc.Executed == 1 && sc.Planned == 9 && sc.State == MissionValidation.Partial, Say(r));
            check("what was not reached is not run", Of(r, MissionValidation.RehearsalsCheck).State == MissionValidation.NotRun && Of(r, MissionValidation.RehearsalsCheck).Executed == 0, Say(r));
        }
        {   // Map-dependent scatter on the synthetic ground, and the saved rehearsals: a failing assertion becomes the rehearsal counterexample.
            string text = Broken(",{\"pos\":[50,0,50],\"scatter\":5}");
            ValidationInputs inputs = Inputs(new FlatWalk(), "fnv1a64:0123456789abcdef");
            inputs.Rehearsals.Add(new KeyValuePair<string, string>("officer_early.json", FailsEarly));
            ValidationReport r = MissionValidation.Run(text, Ask(), inputs, CancellationToken.None);
            ValidationCheck sc = Of(r, MissionValidation.ScatterCheck), rh = Of(r, MissionValidation.RehearsalsCheck);
            check("with the map here every seed and squad size is tried on its ground", sc.Executed == 9 && sc.Coverage == MissionValidation.Sampled && sc.State != MissionValidation.NotRun, Say(r));
            check("the report names the map file and its print where it used them", r.NavFile == "synthetic.bytes" && r.MapPrint == "fnv1a64:0123456789abcdef", r.NavFile + " " + r.MapPrint);
            check("one of the two saved rehearsals fails", rh.State == MissionValidation.Failed && rh.Failed == 1 && rh.Executed == 2, Say(r));
            ValidationCounter c = rh.Counter;
            check("its counterexample names the test, its roll and squad, and the trigger it expected", c != null && c.Rehearsal == "officer_early.json" && c.Seed == 1 && c.Players == 2 && c.Triggers.SequenceEqual(new[] { 2 }) && c.Trace.Count > 0,
                  c == null ? "none" : c.Rehearsal + " " + string.Join(",", c.Triggers));
            check("playing that rehearsal again fails the same way", c != null && MissionValidation.Reproduce(text, c, inputs)?.Expected == c.Expected, "");

            // Unchanged inputs, unchanged report; the time it was made is kept outside the compared part.
            ValidationReport twice = MissionValidation.Run(text, Ask(), inputs, CancellationToken.None);
            check("the same inputs give the same report, byte for byte", MissionValidation.Json(twice) == MissionValidation.Json(r), "");
            string stored = MissionValidation.File(r, "2026-01-01T00:00:00Z"), later = MissionValidation.File(twice, "2026-02-02T00:00:00Z");
            check("two runs differ only in when they were made", stored.Replace("2026-01-01T00:00:00Z", "") == later.Replace("2026-02-02T00:00:00Z", ""), "");
            check("a stored report speaks for the text and map it was made on", MissionValidation.Stale(stored, text, "fnv1a64:0123456789abcdef") == "" && MissionValidation.Stale(stored, text, "") == "", MissionValidation.Stale(stored, text, ""));
            check("a changed scenario text marks it stale", MissionValidation.Stale(stored, text.Replace("\"delay\":30", "\"delay\":45"), "") != "", "");
            check("a changed map file marks it stale", MissionValidation.Stale(stored, text, "fnv1a64:fedcba9876543210") != "", "");
            check("the counterexample file names the scenario it was found on", MissionValidation.CounterFile(r, "x").Contains(ScenarioFile.Hash(text)), "");
        }
        {   // A refused file is refused whatever else is asked: nothing else runs on it.
            ValidationReport r = MissionValidation.Run("{\"spawns\": 3}", Ask(), Inputs(new FlatWalk()), CancellationToken.None);
            check("the format check fails and every other check is not run", Of(r, MissionValidation.Format).State == MissionValidation.Failed &&
                  r.Checks.Where(c => c.Name != MissionValidation.Format).All(c => c.State == MissionValidation.NotRun && c.Executed == 0), Say(r));
        }
        {   // The request as the page sends it.
            string why = MissionValidation.Ask("{\"scenario\":\"x\",\"checks\":[\"completion\"],\"players\":[1,4],\"seeds\":3,\"budget\":50,\"choices\":{\"pace\":[1]}}",
                                               out string scenario, out ValidationAsk ask, out List<V3> insertions);
            check("a well-formed request is read with what it asks", why == "" && scenario == "x" && ask.Completion && !ask.Scatter && !ask.Rehearsals && ask.Players.SequenceEqual(new[] { 1, 4 }) &&
                  ask.Seeds.SequenceEqual(new long[] { 1, 2, 3 }) && ask.Budget == 50 && ask.Choices["pace"].SequenceEqual(new[] { 1 }), why);
            check("a squad size out of range is refused", MissionValidation.Ask("{\"scenario\":\"x\",\"players\":[0]}", out _, out _, out _) != "", "");
            ValidationAsk onlyFast = Ask(); onlyFast.Choices["pace"] = new List<int> { 1 };
            ValidationReport r = MissionValidation.Run(broken, onlyFast, Inputs(null), CancellationToken.None);
            check("asking for one choice of an option tries only that choice", Of(r, MissionValidation.CompletionCheck).Executed == 9 && r.Options[0].Contains("as asked"), Say(r));
        }
    }
}
