using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// ML-047: branches that wait for what a firing's actions came to. A firing whose actions failed never passes for applied;
/// every firing has its own number, and an answer for an older one, or one after the timeout, is kept as late and meets
/// nothing; nothing is retried; partial is told apart from failed; the mission's end fails what still waits, in a fixed
/// order; Completion refuses an extraction that waits for something nobody can see; the strict reader refuses broken
/// outcome triggers; files round-trip; and a rehearsal only knows an outcome when the script reports it.
/// </summary>
static class Ml047Tests
{
    private static string Root()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        return root;
    }

    private static string Codes(List<Finding> f) => string.Join(" | ", f.Where(x => x.Level == Finding.Error).Select(x => x.Code + " " + x.Path));

    private const string Enemies = "\"spawns\":[{\"pos\":[0,0,0]},{\"pos\":[4,0,4],\"trigger\":1},{\"pos\":[6,0,6],\"trigger\":1}]";

    /// <summary>Nobody anywhere, nothing seen, except an alarm when asked: only timers, the alarm and reports move the engine.</summary>
    private sealed class World : ITriggerWorld
    {
        public bool Alarm;
        public bool Alerted() => Alarm;
        public bool IntelTaken(string item) => false;
        public bool Occupied(TriggerPlan t, out V3 where) { where = default; return false; }
        public bool Down(int count, string tag) => false;
        public bool DoorOpen(V3 hinge) => false;
        public int Inside(TriggerPlan t, out int alive) { alive = 0; return 0; }
    }

    private static TriggerEngine Engine(MissionPlan p)
    {
        var e = new TriggerEngine();
        e.Reset(0f);
        e.Sync(p.Triggers);
        return e;
    }

    private static string Fired(List<TriggerEvent> events) => string.Join(",", events.Where(x => x.Kind == TriggerEvent.Fire).Select(x => x.Id + "#" + x.Invocation));

    public static void Run(Action<string, bool, string> check)
    {
        {   // A firing whose actions failed cannot pass for applied; the failure branch takes it.
            var p = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"time\"},{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"applied\"},{\"id\":3,\"when\":\"outcome\",\"after\":1,\"outcome\":\"failed\"}]," + Enemies + "}");
            var e = Engine(p); var w = new World();
            check("the watched trigger fires as its first firing", Fired(e.Step(0f, w)) == "1#1", "");
            check("before any report neither branch fires", Fired(e.Step(1f, w)) == "", "");
            e.Report(1, 1, TriggerEngine.Failed, "the spawner refused the enemies");
            string after = Fired(e.Step(1.5f, w)) + "|" + Fired(e.Step(30f, w));
            check("a failed firing sets off the failed branch and never the applied one", after == "3#1|" && !e.EverFired(2), after);
            check("the applied branch says what the firing came to", e.Explain(p.Triggers[1], 30f, w).Why == "Firing 1 of trigger 1 came to failed, not applied; waiting for its next firing", e.Explain(p.Triggers[1], 30f, w).Why);
        }
        {   // Each firing has its own number; an answer for the older one, after the newer began, satisfies nothing.
            var p = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"repeat\":10},{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"applied\"}]," + Enemies + "}");
            var e = Engine(p); var w = new World { Alarm = true };
            string first = Fired(e.Step(0f, w)), second = Fired(e.Step(10f, w));
            check("two firings are numbered 1 and 2", first == "1#1" && second == "1#2", first + " " + second);
            Invocation late = e.Report(1, 1, TriggerEngine.Applied, "2 enemies appeared");
            check("the answer for firing 1 is kept, as late", late != null && late.Late && e.State(1).Late == "late: firing 1 applied after firing 2 had begun", e.State(1).Late);
            check("the late answer does not stand for the latest firing", e.State(1).Outcome == "" && e.Explain(p.Triggers[0], 10.5f, w).Pending.SequenceEqual(new[] { 2 }), e.State(1).Outcome);
            check("the late answer does not set off the applied branch", Fired(e.Step(10.5f, w)) == "" && !e.EverFired(2), "");
            e.Report(1, 2, TriggerEngine.Applied, "2 enemies appeared");
            check("the answer for firing 2 does", Fired(e.Step(11f, w)) == "2#1", "");
            check("an answer for a firing that never happened is ignored", e.Report(1, 3, TriggerEngine.Applied, "") == null && e.Report(1, 0, TriggerEngine.Applied, "") == null, "");
        }
        {   // Timeout: no answer within the window fires the branch; a later answer is late, fires nothing, and nothing is tried again.
            var p = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"time\",\"delay\":2},{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"timeout\",\"within\":5},{\"id\":3,\"when\":\"outcome\",\"after\":1,\"outcome\":\"applied\"}]," + Enemies + "}");
            var e = Engine(p); var w = new World();
            var fired = new List<string>();
            for (float t = 0f; t < 7f; t += 0.5f) { string f = Fired(e.Step(t, w)); if (f != "") fired.Add(t.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + f); }
            check("before 2 + 5 s only the watched trigger has fired", string.Join(" ", fired) == "2:1#1", string.Join(" ", fired));
            string at7 = Fired(e.Step(7f, w));
            check("at 7 s (2 + 5) the timeout branch fires", at7 == "2#1" && e.EverFired(2), at7);
            Invocation told = e.Report(1, 1, TriggerEngine.Applied, "2 enemies appeared");
            check("an answer after the timeout is recorded, as late", told != null && told.Late && told.Outcome == TriggerEngine.Applied && e.State(1).Late == "late: applied after the timeout", e.State(1).Late);
            string later = "";
            for (float t = 7.5f; t <= 60f; t += 0.5f) later += Fired(e.Step(t, w));
            check("the late answer fires nothing, and neither the action nor the timeout branch runs again", later == "" && e.Fires(1) == 1 && e.Fires(2) == 1 && !e.EverFired(3), later);
            check("the inspector shows the late answer", e.Explain(p.Triggers[0], 60f, w).Late == "late: applied after the timeout", "");
        }
        {   // An answer of any kind in time, unconfirmed included, keeps the timeout branch from firing.
            var p = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"time\"},{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"timeout\",\"within\":5}]," + Enemies + "}");
            var e = Engine(p); var w = new World();
            e.Step(0f, w);
            e.Report(1, 1, TriggerEngine.Unconfirmed, "asked of the game, not seen to take: a message");
            for (float t = 0.5f; t <= 30f; t += 0.5f) e.Step(t, w);
            check("an unconfirmed answer in time is an answer: no timeout", !e.EverFired(2), "");
        }
        {   // Partial: some of one firing's enemies made, some not. Pure classification, as the plugin's Firing uses it.
            check("1 of 2 enemies made is partial", TriggerEngine.Classify(1, 1, false, false) == TriggerEngine.Partial, "");
            check("none of 2 made is failed", TriggerEngine.Classify(0, 2, false, false) == TriggerEngine.Failed, "");
            check("another failure wins over partial", TriggerEngine.Classify(1, 1, true, false) == TriggerEngine.Failed, "");
            check("partial wins over an unseen message", TriggerEngine.Classify(1, 1, false, true) == TriggerEngine.Partial, "");
            check("all made with an unseen part is unconfirmed", TriggerEngine.Classify(2, 0, false, true) == TriggerEngine.Unconfirmed, "");
            check("all made and nothing unseen is applied", TriggerEngine.Classify(2, 0, false, false) == TriggerEngine.Applied, "");
            var p = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"time\"},{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"partial\"},{\"id\":3,\"when\":\"outcome\",\"after\":1,\"outcome\":\"failed\"}]," + Enemies + "}");
            var e = Engine(p); var w = new World();
            e.Step(0f, w);
            e.Report(1, 1, TriggerEngine.Classify(1, 1, false, false), "1 of 2 enemies appeared");
            string f = Fired(e.Step(0.5f, w));
            check("a partial firing sets off the partial branch, not the failed one", f == "2#1", f);
        }
        {   // The mission's end fails every firing still waiting, in the same order whatever the order of the list.
            string Close(string triggers)
            {
                var p = MissionPlan.FromJson("{\"triggers\":[" + triggers + "]," + Enemies + "}");
                var e = Engine(p); var w = new World { Alarm = true };
                e.Step(0f, w); e.Step(10f, w);
                e.Report(3, 1, TriggerEngine.Applied, "");
                string closed = string.Join(",", e.Close().Select(x => x.Key + "#" + x.Value));
                bool all = new[] { 1, 2, 3 }.All(id => e.State(id).Invocations.All(i => i.Outcome != ""));
                return closed + (all ? "" : " (some still waiting)") + "|" + e.State(1).Outcome + ":" + e.State(1).OutcomeWhy + "|" + e.State(3).Outcome;
            }
            string a = Close("{\"id\":1,\"when\":\"alert\",\"repeat\":10},{\"id\":2,\"when\":\"time\"},{\"id\":3,\"when\":\"time\",\"delay\":5}");
            string b = Close("{\"id\":3,\"when\":\"time\",\"delay\":5},{\"id\":2,\"when\":\"time\"},{\"id\":1,\"when\":\"alert\",\"repeat\":10}");
            check("the mission's end fails what still waits, by trigger and then by firing", a == "1#1,1#2,2#1|failed:the mission ended|applied", a);
            check("the order of the list does not change it", a == b, b);
        }
        {   // Completion: waiting to see something nobody can see.
            string Plan(string watched, string outcome, bool extraction) =>
                "{\"triggers\":[" + watched + ",{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"" + outcome + "\"}]" + (extraction ? ",\"extraction\":{\"after\":2}" : "") + ",\"spawns\":[{\"pos\":[0,0,0]}" + "]}";
            const string message = "{\"id\":1,\"when\":\"alert\",\"message\":\"Contact.\"}";
            List<Finding> held = Completion.Check(MissionPlan.FromJson(Plan(message, "applied", true)), null, 0);
            check("an extraction that waits for a message to be seen to take is refused", Completion.Blocks(held), string.Join(" | ", held));
            List<Finding> aside = Completion.Check(MissionPlan.FromJson(Plan(message, "applied", false)), null, 0);
            check("the same branch, with nothing waiting for it, is a note at its outcome", !Completion.Blocks(aside) && aside.Any(f => f.Level == Finding.Note && f.Code == Completion.UnseenOutcome && f.Path == "triggers[1].outcome"), string.Join(" | ", aside));
            check("the refusal is not said twice as a note", !held.Any(f => f.Code == Completion.UnseenOutcome), string.Join(" | ", held));
            List<Finding> failed = Completion.Check(MissionPlan.FromJson(Plan(message, "failed", true)), null, 0);
            check("waiting for a message trigger to fail is possible", !Completion.Blocks(failed) && failed.Count == 0, string.Join(" | ", failed));
            const string enemy = "{\"id\":1,\"when\":\"alert\"}";
            string withEnemy = "{\"triggers\":[" + enemy + ",{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"applied\"},{\"id\":3,\"when\":\"outcome\",\"after\":1,\"outcome\":\"partial\"}],\"extraction\":{\"after\":2},\"spawns\":[{\"pos\":[0,0,0]},{\"pos\":[1,0,1],\"trigger\":1}]}";
            List<Finding> seen = Completion.Check(MissionPlan.FromJson(withEnemy), null, 0);
            check("an extraction that waits for an enemy to be seen is fine", !Completion.Blocks(seen), string.Join(" | ", seen));
            check("partial with one enemy is a note: one enemy cannot be partly made", seen.Count(f => f.Code == Completion.UnseenOutcome && f.Path == "triggers[2].outcome") == 1, string.Join(" | ", seen));
            check("an outcome edge counts for circles like after", TriggerEngine.Circles(MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"after\",\"after\":2},{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"applied\"}]," + Enemies + "}").Triggers).SetEquals(new[] { 1, 2 }), "");
        }
        {   // The strict reader: links an outcome trigger must have.
            List<Finding> Read(string trigger) => ScenarioFile.Read("{\"schema\":\"missionlab-plan/1\",\"triggers\":[{\"id\":1,\"when\":\"alert\"}," + trigger + "]," + Enemies + "}", out _);
            check("watching trigger 0 names no trigger", Codes(Read("{\"id\":2,\"when\":\"outcome\",\"after\":0,\"outcome\":\"failed\"}")) == "missing-ref triggers[1].after", Codes(Read("{\"id\":2,\"when\":\"outcome\",\"after\":0,\"outcome\":\"failed\"}")));
            check("watching itself is refused", Codes(Read("{\"id\":2,\"when\":\"outcome\",\"after\":2,\"outcome\":\"failed\"}")) == "self-ref triggers[1].after", Codes(Read("{\"id\":2,\"when\":\"outcome\",\"after\":2,\"outcome\":\"failed\"}")));
            check("within at the edges is read", Codes(Read("{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"timeout\",\"within\":3600}")) == "" && Codes(Read("{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"timeout\",\"within\":1}")) == "", "");
            check("within above an hour is refused", Codes(Read("{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"timeout\",\"within\":3601}")) == "out-of-range triggers[1].within", "");
            check("an after trigger may still name 0: fired by hand", Codes(Read("{\"id\":2,\"when\":\"after\",\"after\":0}")) == "", "");
        }
        string root = Root();
        if (root == null) return;
        string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests", "fixtures", name));
        {   // Writing: outcome and within right after "after", within only when it is not 30; older kinds unchanged.
            var p = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"alert\"},{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"failed\"},{\"id\":3,\"when\":\"outcome\",\"after\":1,\"outcome\":\"timeout\",\"within\":45,\"message\":\"Nobody came.\"},{\"id\":4,\"when\":\"after\",\"after\":1,\"outcome\":\"failed\",\"within\":9}]," + Enemies + "}");
            string once = p.ToJson();
            check("an outcome trigger writes after and outcome together, and no within at 30", once.Contains("\"when\":\"outcome\",\"after\":1,\"outcome\":\"failed\",\"facePlayer\""), once);
            check("a timeout writes its within", once.Contains("\"when\":\"outcome\",\"after\":1,\"outcome\":\"timeout\",\"within\":45,"), once);
            check("an after trigger writes neither outcome nor within", once.Contains("{\"id\":4,\"name\":\"\",\"when\":\"after\",\"after\":1,\"facePlayer\""), once);
            check("it reads back to the same text", MissionPlan.FromJson(once).ToJson() == once, once);
            string v27 = Fixture("all_fields_v27.json");
            var strict = ScenarioFile.Read(v27, out MissionPlan read);
            string first = MissionPlan.FromJson(v27).ToJson();
            check("all_fields_v27.json, with its outcome trigger, passes the strict reader", read != null && !ScenarioFile.Blocks(strict), Codes(strict));
            check("all_fields_v27.json writes back to the same text, outcome keys and all", first == MissionPlan.FromJson(first).ToJson() && first.Contains("\"outcome\":\"timeout\",\"within\":45"), first);
            check("a plan with an outcome trigger names the outcomes capability, which this version knows", Capabilities.Of(read).Contains("outcomes") && Pack.Known.Contains("outcomes"), string.Join(",", Capabilities.Of(read)));
            var olds = new List<string> { "all_fields.json", "all_fields_v2.json", "all_fields_v26.json", "legacy_minimal.json" }.Select(Fixture).ToList();
            olds.AddRange(Directory.GetFiles(Path.Combine(root, "examples"), "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText));
            check("files without outcome triggers write no outcome key and read back to the same text",
                  olds.All(t => { string a = MissionPlan.FromJson(t).ToJson(); return a == MissionPlan.FromJson(a).ToJson() && !a.Contains("\"outcome\""); }), "");
        }
        {   // Rehearsal: an outcome is only known when the script reports it, for the latest firing; timeouts run on its clock.
            var plan = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"time\",\"delay\":1},{\"id\":2,\"when\":\"outcome\",\"after\":1,\"outcome\":\"applied\"},{\"id\":3,\"when\":\"outcome\",\"after\":1,\"outcome\":\"timeout\",\"within\":5}]," + Enemies + "}");
            const string told = "{\"schema\":\"missionlab-rehearsal/1\",\"steps\":[{\"at\":2,\"outcome\":{\"trigger\":1,\"result\":\"applied\"}},{\"at\":3,\"expect\":{\"fired\":[2],\"notFired\":[3]}},{\"at\":10,\"expect\":{\"notFired\":[3]}}]}";
            var found = Rehearsal.Read(told, out RehearsalScript s, plan);
            check("a script with an outcome reads", s != null, Codes(found));
            if (s != null)
            {
                RehearsalResult r = Rehearsal.Run(plan, s);
                check("a reported applied firing sets off the applied branch and no timeout", r.Passed, string.Join(" | ", r.Steps.Where(x => !x.Ok).Select(x => x.What + ": " + x.Actual)));
                StepResult report = r.Steps.FirstOrDefault(x => x.What.Contains("is reported applied"));
                check("the step names the firing and its watchers", report != null && report.Trigger == 1 && report.Invocation == 1 && report.What.Contains("watched by trigger 2 (waits for applied), trigger 3 (waits for timeout)"), report == null ? "" : report.What);
                string written = Rehearsal.Write(s);
                check("the script writes the outcome back and reads the same", written.Contains("\"outcome\":{\"trigger\":1,\"result\":\"applied\"}") && Rehearsal.Read(written, out RehearsalScript again, plan).Count == 0 && Rehearsal.Write(again) == written, written);
            }
            const string silent = "{\"schema\":\"missionlab-rehearsal/1\",\"steps\":[{\"at\":5.5,\"expect\":{\"notFired\":[2,3]}},{\"at\":6,\"expect\":{\"fired\":[3],\"notFired\":[2]}}]}";
            Rehearsal.Read(silent, out RehearsalScript quiet, plan);
            RehearsalResult q = Rehearsal.Run(plan, quiet);
            check("without a report the rehearsal assumes nothing: the timeout fires at 1 + 5 s", q.Passed, string.Join(" | ", q.Steps.Where(x => !x.Ok).Select(x => x.What + ": " + x.Actual)));
            const string early = "{\"schema\":\"missionlab-rehearsal/1\",\"steps\":[{\"at\":0.5,\"outcome\":{\"trigger\":1,\"result\":\"applied\"}}]}";
            Rehearsal.Read(early, out RehearsalScript before, plan);
            RehearsalResult n = Rehearsal.Run(plan, before);
            check("a report before the trigger fired is a failed step, not an assumed firing", !n.Passed && n.Steps.Any(x => !x.Ok && x.Trigger == 1 && x.Invocation == 0), "");
            var bad = Rehearsal.Read("{\"schema\":\"missionlab-rehearsal/1\",\"steps\":[{\"at\":1,\"outcome\":{\"trigger\":9,\"result\":\"timeout\"}}]}", out _, plan);
            check("a script refuses an unknown trigger and timeout as a reported result", Codes(bad) == "missing-ref steps[0].outcome.trigger | unknown-value steps[0].outcome.result", Codes(bad));
        }
    }
}
