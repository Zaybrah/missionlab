using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// ML-046: player goals. Each transition happens once however often its trigger fires; an optional goal's failure holds
/// nothing up unless a goal lists it in "after"; failure wins over success in one look, whatever the order of the list;
/// broken references are refused by the strict reader and circles by Completion; an extraction with "goals": true is held
/// until every required goal has succeeded, explained by the same GoalBook in preflight and in the run; and goals make the
/// file missionlab-plan/3 and nothing else does it besides links.
/// </summary>
static class Ml046Tests
{
    private static string Root()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        return root;
    }

    private static string Codes(List<Finding> f) => string.Join(" | ", f.Where(x => x.Level == Finding.Error).Select(x => x.Code + " " + x.Path));

    private static GoalPlan G(string id, int succeed, int fail = 0, bool required = true, string after = null) =>
        new GoalPlan { Id = id, Name = "Goal " + id, Succeed = succeed, Fail = fail, Required = required, After = after == null ? new List<string>() : new List<string> { after } };

    /// <summary>Nobody anywhere, nothing seen: only timers and hand firings move the engine.</summary>
    private sealed class Quiet : ITriggerWorld
    {
        public bool Alerted() => false;
        public bool IntelTaken(string item) => false;
        public bool Occupied(TriggerPlan t, out V3 where) { where = default; return false; }
        public bool Down(int count, string tag) => false;
        public bool DoorOpen(V3 hinge) => false;
        public int Inside(TriggerPlan t, out int alive) { alive = 0; return 0; }
    }

    private static IEnumerable<List<T>> Orders<T>(List<T> items)
    {
        if (items.Count <= 1) { yield return new List<T>(items); yield break; }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = new List<T>(items); rest.RemoveAt(i);
            foreach (List<T> tail in Orders(rest)) { tail.Insert(0, items[i]); yield return tail; }
        }
    }

    // Recover the phone (trigger 1), then secure the upper floor (trigger 2), with an optional power cut (trigger 3, lost by 4).
    private const string Mission =
        "{\"schema\":\"missionlab-plan/3\",\"mission\":{\"triggers\":[" +
        "{\"id\":1,\"name\":\"Phone taken\",\"when\":\"time\",\"delay\":1}," +
        "{\"id\":2,\"name\":\"Upper floor clear\",\"when\":\"time\",\"delay\":5}," +
        "{\"id\":3,\"name\":\"Power cut\",\"when\":\"time\",\"delay\":3}]," +
        "\"goals\":[{\"id\":\"phone\",\"name\":\"Recover the phone\",\"succeed\":1}," +
        "{\"id\":\"upper\",\"name\":\"Secure the upper floor\",\"after\":[\"phone\"],\"succeed\":2}," +
        "{\"id\":\"power\",\"name\":\"Cut the power\",\"required\":false,\"succeed\":3}]," +
        "\"extraction\":{\"goals\":true}," +
        "\"spawns\":[{\"pos\":[30,0,30]}]}}";

    public static void Run(Action<string, bool, string> check)
    {
        string root = Root();
        check("the fixtures are found", root != null, AppContext.BaseDirectory);
        if (root == null) return;
        string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests", "fixtures", name));

        {   // The phone, then the next goal, exactly once, however often the phone's trigger fires.
            var plan = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"time\",\"delay\":2,\"repeat\":5},{\"id\":2,\"when\":\"time\",\"delay\":600}]," +
                                            "\"goals\":[{\"id\":\"phone\",\"name\":\"Recover the phone\",\"succeed\":1},{\"id\":\"upper\",\"name\":\"Secure the upper floor\",\"after\":[\"phone\"],\"succeed\":2}]," +
                                            "\"spawns\":[{\"pos\":[0,0,0]}]}");
            var engine = new TriggerEngine(); engine.Reset(0f); engine.Sync(plan.Triggers);
            var book = new GoalBook(); book.Sync(plan.Goals);
            var all = new List<GoalEvent>(); var notices = new List<string>();
            for (int k = 0; k <= 120; k++)
            {
                float now = k * 0.5f;
                engine.Step(now, new Quiet());
                foreach (GoalEvent e in book.Step(now, engine.EverFired))
                {
                    all.Add(e);
                    string n = GoalBook.Notice(book.Find(e.Id), e, out _);
                    if (n != null) notices.Add(n);
                }
            }
            check("the phone's trigger fired again and again", engine.Fires(1) >= 2, engine.Fires(1).ToString());
            check("the phone succeeded once", all.Count(e => e.Id == "phone" && e.To == GoalBook.Succeeded) == 1, string.Join(", ", all.Select(e => e.Id + ":" + e.To)));
            check("the next goal became active exactly once, when the phone succeeded", all.Count(e => e.Id == "upper" && e.To == GoalBook.Active) == 1 && all.First(e => e.Id == "upper").At == all.First(e => e.Id == "phone" && e.To == GoalBook.Succeeded).At, string.Join(", ", all.Select(e => e.Id + ":" + e.To + "@" + e.At)));
            check("three transitions in all: phone active, phone succeeded, upper active", all.Count == 3, all.Count.ToString());
            check("players are told twice: the phone is complete, and the new objective (the first goal was in the briefing)",
                  notices.SequenceEqual(new[] { "Objective complete: Recover the phone", "New objective: Secure the upper floor" }), string.Join(" / ", notices));
        }

        {   // An optional goal's failure blocks nothing, unless a goal lists it in "after".
            var goals = new List<GoalPlan> { G("phone", 1), G("power", 2, fail: 3, required: false), G("upper", 4, after: "phone") };
            var fired = new HashSet<int> { 3, 1, 4 };
            var book = new GoalBook(); book.Sync(goals);
            book.Step(10f, fired.Contains);
            check("the optional goal failed", book.State("power") == GoalBook.Failed, book.State("power"));
            check("the required goals still succeeded", book.State("phone") == GoalBook.Succeeded && book.State("upper") == GoalBook.Succeeded, book.StatesJson());
            check("nothing required remains, so a goals extraction opens", book.Remaining(true).Count == 0 && book.Holding() == "", book.Holding());

            var strict = new List<GoalPlan> { G("phone", 1), G("power", 2, fail: 3, required: false), G("escape", 4, after: "power") };
            var held = new GoalBook(); held.Sync(strict);
            held.Step(10f, fired.Contains);
            held.Step(20f, new HashSet<int> { 1, 2, 3, 4 }.Contains);
            check("a required goal that lists the failed goal in \"after\" never becomes active", held.State("escape") == GoalBook.Inactive, held.StatesJson());
            check("a terminal goal never changes: the failed one stays failed when its succeed trigger fires later", held.State("power") == GoalBook.Failed, held.StatesJson());
            check("the extraction is explained as waiting for it", held.Remaining(true).Select(g => g.Id).SequenceEqual(new[] { "escape" }) && held.Holding().Contains("'Goal escape' (not yet active)"), held.Holding());
        }

        {   // Success and failure in one look: failure wins, for every order of the list.
            var baseGoals = new List<GoalPlan> { G("a", 1, fail: 2), G("b", 3, after: "a"), G("c", 4, fail: 5, after: "b"), G("d", 6) };
            string want = null; bool same = true; int orders = 0;
            foreach (List<GoalPlan> order in Orders(baseGoals))
            {
                orders++;
                var book = new GoalBook(); book.Sync(order);
                book.Step(0f, new HashSet<int>().Contains);
                book.Step(1f, new HashSet<int> { 1, 2, 3, 6 }.Contains);
                string got = string.Join(",", baseGoals.Select(g => g.Id + "=" + book.State(g.Id)));
                if (want == null) want = got; else if (got != want) same = false;
            }
            check("every one of the 24 orders ends the same", orders == 24 && same, want);
            check("the goal whose success and failure both fired in one look failed; what waited for it stays inactive", want == "a=failed,b=inactive,c=inactive,d=succeeded", want);
            var chain = new List<GoalPlan> { G("c", 3, after: "b"), G("b", 2, after: "a"), G("a", 1) };
            var cb = new GoalBook(); cb.Sync(chain);
            List<GoalEvent> one = cb.Step(0f, new HashSet<int> { 1, 2, 3 }.Contains);
            check("a chain listed backwards completes in one step, each transition once", one.Count == 6 && chain.All(g => cb.State(g.Id) == GoalBook.Succeeded), string.Join(", ", one.Select(e => e.Id + ":" + e.To)));
        }

        {   // The strict reader refuses broken goals, as the contract lists them.
            var expected = JsonDocument.Parse(Fixture("expected.json")).RootElement;
            foreach (string name in new[] { "bad_goals.json", "goals_in_plan2.json" })
            {
                var found = ScenarioFile.Read(Fixture(Path.Combine("invalid", name)), out MissionPlan none);
                string want = string.Join(" | ", expected.GetProperty(name).EnumerateArray().Select(x => x.GetString()));
                check("invalid/" + name + " is refused with the listed codes and paths", Codes(found) == want && none == null, Codes(found));
            }
            var oldFile = ScenarioFile.Read("{\"triggers\":[{\"id\":1,\"when\":\"alert\"}],\"goals\":[{\"id\":\"a\",\"name\":\"A\",\"succeed\":1}],\"spawns\":[{\"pos\":[0,0,0]}]}", out _);
            check("goals in a file that names no format are refused as a format older versions read without them", Codes(oldFile) == "unknown-format goals", Codes(oldFile));
            var lenient = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"alert\"}],\"goals\":[{\"id\":\"a\",\"name\":\"A\",\"succeed\":9},{\"id\":\"b\",\"name\":\"B\",\"succeed\":1,\"after\":[\"a\",\"b\"],\"fail\":7}],\"spawns\":[{\"pos\":[0,0,0]}]}");
            check("the lenient reader drops a goal whose succeed trigger is missing, and the links to it", lenient.Goals.Count == 1 && lenient.Goals[0].Id == "b" && lenient.Goals[0].After.Count == 0 && lenient.Goals[0].Fail == 0, lenient.ToJson());
        }

        {   // Circles and required goals that can never succeed: blocking only when the extraction waits for goals.
            string Circle(bool waits, string extra) =>
                "{\"schema\":\"missionlab-plan/3\",\"mission\":{\"triggers\":[{\"id\":1,\"when\":\"time\",\"delay\":5},{\"id\":2,\"when\":\"after\",\"after\":3},{\"id\":3,\"when\":\"after\",\"after\":2}" + extra + "]," +
                "\"goals\":[{\"id\":\"x\",\"name\":\"X\",\"after\":[\"y\"],\"succeed\":1},{\"id\":\"y\",\"name\":\"Y\",\"after\":[\"x\"],\"succeed\":1},{\"id\":\"z\",\"name\":\"Z\",\"succeed\":2},{\"id\":\"w\",\"name\":\"W\",\"after\":[\"x\"],\"succeed\":1}]" +
                (waits ? ",\"extraction\":{\"goals\":true}" : "") + ",\"spawns\":[{\"pos\":[0,0,0]}]}}";
            var read = ScenarioFile.Read(Circle(true, ""), out MissionPlan p);
            check("a goal circle passes the strict reader (it is Completion's to judge)", p != null, Codes(read));
            if (p != null)
            {
                List<Finding> found = Completion.Check(p, null, 0);
                string errors = string.Join(" | ", found.Where(f => f.Level == Finding.Error).Select(f => f.Path));
                check("with the extraction waiting for goals, the circle, the unreachable goal and the goal behind the circle block",
                      errors == "goals[0].after | goals[2].succeed | goals[3].after[0]", errors);
                check("the circle names both goals", found.Any(f => f.Path == "goals[0].after" && f.Text.Contains("x, y")), string.Join(" | ", found.Select(f => f.ToString())));
            }
            ScenarioFile.Read(Circle(false, ""), out MissionPlan q);
            check("without an extraction that waits for goals, they are notes only", q != null && !Completion.Blocks(Completion.Check(q, null, 0)) && Completion.Check(q, null, 0).Count(f => f.Path.StartsWith("goals[", StringComparison.Ordinal)) == 3,
                  q == null ? "" : string.Join(" | ", Completion.Check(q, null, 0).Select(f => f.Level + " " + f.Path)));
            ScenarioFile.Read(Circle(true, ",{\"id\":4,\"when\":\"time\",\"delay\":60,\"extraction\":\"open\"}"), out MissionPlan opener);
            check("a trigger that can open the extraction another way keeps them notes", opener != null && !Completion.Check(opener, null, 0).Any(f => f.Level == Finding.Error && f.Path.StartsWith("goals[", StringComparison.Ordinal)), "");
            var rings = GoalBook.Rings(new List<GoalPlan> { G("x", 1, after: "y"), G("y", 1, after: "x"), G("s", 1, after: "s"), G("w", 1, after: "x") });
            check("Rings finds the two-goal circle and the goal that waits for itself, and not the goal behind them", rings.Count == 2 && rings[0].SequenceEqual(new[] { "x", "y" }) && rings[1].SequenceEqual(new[] { "s" }), string.Join(" / ", rings.Select(r => string.Join(",", r))));
            var stuck = new GoalBook(); stuck.Sync(new List<GoalPlan> { G("x", 1, after: "y"), G("y", 1, after: "x") });
            check("goals in a circle never become active", stuck.Step(0f, id => true).Count == 0, stuck.StatesJson());
        }

        {   // The extraction waits for the required goals; preflight and the run explain it with the same words.
            var found = ScenarioFile.Read(Mission, out MissionPlan plan);
            check("the phone mission reads strictly", plan != null, Codes(found));
            if (plan == null) return;
            Finding note = Completion.Check(plan, null, 0).FirstOrDefault(f => f.Path == "extraction.goals");
            var book = new GoalBook(); book.Sync(plan.Goals);
            check("Completion names the required goals that remain, as the run's GoalBook does, and not the optional one",
                  note != null && note.Level == Finding.Note && note.Text == book.Holding() && note.Text.Contains("'Recover the phone'") && note.Text.Contains("'Secure the upper floor'") && !note.Text.Contains("power"), note == null ? "(none)" : note.Text);
            book.Step(2f, new HashSet<int> { 1 }.Contains);
            check("once the phone is recovered, only the upper floor remains", book.Remaining(true).Select(g => g.Id).SequenceEqual(new[] { "upper" }), book.Holding());

            string Script(string steps) => "{\"schema\":\"missionlab-rehearsal/1\",\"players\":1,\"steps\":[" + steps + "]}";
            string good = Script("{\"at\":1,\"expect\":{\"goals\":{\"phone\":\"succeeded\",\"upper\":\"active\",\"power\":\"active\"},\"extraction\":\"held\"}}," +
                                 "{\"at\":5,\"expect\":{\"goals\":{\"upper\":\"succeeded\",\"power\":\"succeeded\"},\"extraction\":\"open\"}}");
            var heard = Rehearsal.Read(good, out RehearsalScript s, plan);
            check("a rehearsal with goal expectations reads", s != null, Codes(heard));
            if (s != null)
            {
                RehearsalResult r = Rehearsal.Run(plan, s);
                check("it passes: the extraction is held until the last required goal succeeds", r.Passed, string.Join(" | ", r.Steps.Where(x => !x.Ok).Select(x => x.What + " -> " + x.Actual)));
                check("the run lists each transition once", r.Goals.Count(e => e.Id == "upper" && e.To == GoalBook.Succeeded) == 1 && r.Goals.Count == 6, string.Join(", ", r.Goals.Select(e => e.Id + ":" + e.To + "@" + e.At)));
                RehearsalLook first = r.Looks.FirstOrDefault(l => l.At == 1f);
                check("the look at 1 s shows the phone go from active to succeeded", first != null && first.GoalsBefore["phone"] == GoalBook.Active && first.GoalsAfter["phone"] == GoalBook.Succeeded, "");
                string written = Rehearsal.Write(s);
                Rehearsal.Read(written, out RehearsalScript again, plan);
                check("a script with goal expectations writes back the same", again != null && Rehearsal.Write(again) == written && written.Contains("\"goals\":{\"phone\":\"succeeded\",\"power\":\"active\",\"upper\":\"active\"}"), written);
                string answer = RehearsalFile.Run(Mission, good);
                check("the page's answer carries the transitions", answer.Contains("\"goals\":[{\"id\":\"phone\",\"from\":\"inactive\",\"to\":\"active\"") && answer.Contains("\"goalsAfter\":{"), answer.Length > 400 ? answer.Substring(0, 400) : answer);
            }
            string bad = Script("{\"at\":2,\"expect\":{\"goals\":{\"upper\":\"succeeded\"},\"extraction\":\"open\"}}");
            Rehearsal.Read(bad, out RehearsalScript b, plan);
            RehearsalResult rb = b == null ? null : Rehearsal.Run(plan, b);
            check("a wrong goal expectation fails, and the extraction is not open yet", rb != null && !rb.Passed && rb.Steps.Count(x => !x.Ok) == 2, rb == null ? "" : string.Join(" | ", rb.Steps.Select(x => x.Ok + " " + x.Actual)));
            var unknown = Rehearsal.Read(Script("{\"at\":1,\"expect\":{\"goals\":{\"ghost\":\"active\",\"phone\":\"done\"}}}"), out _, plan);
            check("a script naming an unknown goal or state is refused", Codes(unknown) == "missing-ref steps[0].expect.goals.ghost | unknown-value steps[0].expect.goals.phone", Codes(unknown));
        }

        {   // plan/3 only with goals or links; all_fields_v27 round-trips; flattening keeps goals and plan/3.
            string Format(string text) => JsonDocument.Parse(text).RootElement.GetProperty("schema").GetString();
            var plain = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"alert\"}],\"spawns\":[{\"pos\":[0,0,0]}]}");
            check("a plan without goals or links stays plan/1 and writes no goals key", Format(plain.ToJson()) == MissionPlan.Schema && !plain.ToJson().Contains("\"goals\""), plain.ToJson());
            check("a plan without goals needs no goals capability and holds no extraction", !Capabilities.Of(plain).Contains("goals") && !Capabilities.Of(plain).Contains("extraction-hold"), string.Join(",", Capabilities.Of(plain)));
            plain.Goals.Add(G("a", 1));
            check("one goal makes it plan/3", Format(plain.ToJson()) == MissionPlan.Schema3, plain.ToJson());
            check("a plan with a goal names the goals capability, which this version knows", Capabilities.Of(plain).Contains("goals") && Pack.Known.Contains("goals"), string.Join(",", Capabilities.Of(plain)));
            plain.Goals.Clear(); plain.ExtractionGoals = true;
            check("an extraction that waits for goals makes it plan/3", Format(plain.ToJson()) == MissionPlan.Schema3 && plain.ToJson().Contains("\"extraction\":{\"goals\":true}"), plain.ToJson());
            check("an extraction that waits for goals counts as an extraction hold", Capabilities.Of(plain).Contains("extraction-hold") && !Capabilities.Of(plain).Contains("goals"), string.Join(",", Capabilities.Of(plain)));

            string v27 = Fixture("all_fields_v27.json");
            var found = ScenarioFile.Read(v27, out MissionPlan strict);
            check("all_fields_v27.json, goals and all, passes the strict reader", strict != null, Codes(found));
            var p = MissionPlan.FromJson(v27);
            string once = p.ToJson(), twice = MissionPlan.FromJson(once).ToJson();
            check("all_fields_v27.json writes back to the same text", once == twice, once);
            check("every goal field survives", p.Goals.Count == 3 && p.Goals[1].After.SequenceEqual(new[] { "phone" }) && p.Goals[1].Activate == 2 && p.Goals[0].Fail == 6 && !p.Goals[2].Required && !p.Goals[2].Notify && p.ExtractionGoals && p.ExtractionAfter == 3, once);
            check("goals are written in the fixed key order", once.Contains("\"goals\":[{\"id\":\"phone\",\"name\":\"Recover the phone\",\"succeed\":1,\"fail\":6},{\"id\":\"upper\",\"name\":\"Secure the upper floor\",\"after\":[\"phone\"],\"activate\":2,\"succeed\":3},{\"id\":\"power-cut\",\"name\":\"Cut the power\",\"required\":false,\"succeed\":5,\"notify\":false}]") && once.Contains("\"extraction\":{\"after\":3,\"goals\":true}"), once);
            var flat = MissionPlan.FromJson(v27); flat.Flatten();
            string ft = flat.ToJson();
            check("flattening a plan with goals keeps plan/3 and keeps the goals: only links are dropped", Format(ft) == MissionPlan.Schema3 && !ft.Contains("\"place\":") && MissionPlan.FromJson(ft).Goals.Count == 3, ft.Substring(0, Math.Min(80, ft.Length)));
            // The live state's "goalPlan" is GoalsJson of the running goals: read back beside the same triggers, it is the same goals.
            string goalPlan = MissionPlan.GoalsJson(p.Goals);
            var fromPage = MissionPlan.FromJson("{\"triggers\":" + MissionPlan.TriggersJson(p.Triggers) + ",\"goals\":" + goalPlan + ",\"spawns\":[]}");
            check("the live goalPlan reads back to the same goals", MissionPlan.GoalsJson(fromPage.Goals) == goalPlan && fromPage.Goals.Count == 3, goalPlan);

            // A Game Master save keeps the author's goals, but none that names a trigger the save no longer has. The live state
            // is always read through FromJson, which already unbinds enemies from a deleted trigger; the save is built the same way.
            var live = MissionPlan.FromJson(v27);
            live.Triggers.RemoveAll(t => t.Id == 5 || t.Id == 6);
            live.Goals.Clear(); live.ExtractionGoals = false;
            var saved = MissionPlan.FromJson(live.ToJson());
            MissionPlan.CarryAuthored(MissionPlan.FromJson(v27), saved);
            check("a save keeps the goals whose triggers remain, and drops a fail trigger that is gone", saved.ExtractionGoals && saved.Goals.Select(g => g.Id).SequenceEqual(new[] { "phone", "upper" }) && saved.Goals[0].Fail == 0, saved.ToJson());
            check("such a save passes the strict reader", !ScenarioFile.Blocks(ScenarioFile.Read(saved.ToJson(), out _)), Codes(ScenarioFile.Read(saved.ToJson(), out _)));
        }
    }
}
