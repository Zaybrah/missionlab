using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// Rehearsal (ML-016): a script of told events played against a scenario, rolled the way the Director rolls it. The
/// scenarios and scripts live in tests/fixtures/rehearsal and use made-up coordinates.
/// </summary>
static class RehearsalTests
{
    private static string Fixture(string name)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        return root == null ? null : File.ReadAllText(Path.Combine(root, "tests", "fixtures", "rehearsal", name));
    }

    private static MissionPlan Scenario(string name)
    {
        ScenarioFile.Read(Fixture(name), out MissionPlan plan);
        return plan;
    }

    private static RehearsalScript Script(string name, MissionPlan plan)
    {
        Rehearsal.Read(Fixture(name), out RehearsalScript s, plan);
        return s;
    }

    private static string Failures(RehearsalResult r) =>
        (r.Blocked == "" ? "" : "blocked: " + r.Blocked + " | ") + string.Join(" | ", r.Steps.Where(x => !x.Ok).Select(x => x.What + " -> expected " + x.Expected + ", got " + x.Actual));

    // Enemies a roll leaves at the start, counted the way the rehearsal counts them: people only, there from the start.
    private static int AtStart(MissionPlan p) => p.Spawns.Count(s => !s.Vehicle && s.Trigger == 0);

    public static void Run(Action<string, bool, string> check)
    {
        {   // The phone starts the countdown: the shipped script passes against its scenario.
            MissionPlan plan = Scenario("phone_countdown.json");
            check("the countdown scenario passes the strict reader", plan != null, "");
            if (plan == null) return;
            var found = Rehearsal.Read(Fixture("phone_countdown.rehearsal.json"), out RehearsalScript s, plan);
            check("its script reads with nothing found", s != null && found.Count == 0, string.Join(" | ", found));
            RehearsalResult r = Rehearsal.Run(plan, s);
            check("picking up the phone starts the countdown, the extraction opens when it ends, and the pick-up holds", r.Passed, Failures(r));
            StepResult counting = r.Steps.FirstOrDefault(x => x.What.StartsWith("step 3 at 10 s: trigger 2 'Countdown' is counting"));
            check("at 10 s the countdown is counting with 55 s left", counting != null && counting.Ok && counting.Actual == "counting: Condition met; 55 s left", counting == null ? "no such step" : counting.Actual);
            check("the assumptions say what is not predicted", r.Assumptions.Contains("spotting, pathfinding, physics, networking and combat are not predicted") && r.Assumptions.Contains("roll 12345 for 2 players"), r.Assumptions);
            check("running it did not change the scenario", plan.Spawns.Count == 7 && plan.Triggers.All(t => !t.Off), "");
        }
        {   // The variant for the number of players is the one the Director would pick: minPlayers 3 leaves out one enemy for 1 player, not for 3.
            MissionPlan plan = Scenario("phone_countdown.json");
            foreach (int players in new[] { 1, 3 })
            {
                RehearsalScript s = Script("phone_countdown.rehearsal.json", plan);
                s.Players = players;
                int start = players >= 3 ? 4 : 3;
                s.Steps[2].Expect.Enemies = start; s.Steps[3].Expect.Enemies = start + 2;
                RehearsalResult r = Rehearsal.Run(plan, s);
                MissionPlan same = Scenario("phone_countdown.json");
                Variants.Resolve(same, new Rng(s.Roll ^ 0x5EED), null, players);
                check("with " + players + " player(s) the rehearsal has " + start + " enemies at the start, as the Director's roll has", r.Passed && AtStart(same) == start, Failures(r));
            }
        }
        {   // An optional branch cannot take the extraction away: a roll that leaves out what the extraction waits for is named as blocked.
            MissionPlan plan = Scenario("optional_branch.json");
            check("the optional-branch scenario passes the strict reader", plan != null, "");
            if (plan == null) return;
            int blocked = 0, open = 0; bool alike = true, named = true, failedStep = true;
            for (long roll = 1; roll <= 40; roll++)
            {
                RehearsalScript s = Script("optional_branch.rehearsal.json", plan);
                s.Roll = roll;
                RehearsalResult r = Rehearsal.Run(plan, s);
                MissionPlan same = Scenario("optional_branch.json");
                Variants.Out gone = Variants.Resolve(same, new Rng(roll ^ 0x5EED), null, 1);
                bool director = Completion.Blocks(Completion.Check(same, gone, 1));
                alike &= director == (r.Blocked != "") && same.Triggers[1].Off == (r.Blocked != "");
                if (r.Blocked == "") { open++; if (!r.Passed) failedStep = false; continue; }
                blocked++;
                named &= r.Blocked.Contains("trigger 2 'Ambush'") && !r.Passed;
                StepResult step = r.Steps.FirstOrDefault(x => !x.Ok);
                failedStep &= step != null && step.What == "step 3 at 6 s: trigger 2 'Ambush' has fired" && step.Actual.StartsWith("not fired: Sat this mission out (its chance is 50%)");
            }
            check("some rolls leave the ambush out and some keep it", blocked > 8 && open > 8, blocked + " blocked, " + open + " open");
            check("a roll is blocked exactly when the Director would refuse it", alike, "");
            check("a blocked roll names the trigger the extraction waits for and does not pass", named, "");
            check("on a blocked roll the step that expected the ambush fails, and says it sat out", failedStep, "");
            plan.ExtractionAfter = 1;
            bool never = true;
            for (long roll = 1; roll <= 40; roll++)
            {
                RehearsalScript s = Script("optional_branch.rehearsal.json", plan);
                s.Roll = roll;
                never &= Rehearsal.Run(plan, s).Blocked == "";
            }
            check("with the extraction waiting for the phone instead, no roll is blocked", never, "");
        }
        {   // A changed scenario makes a saved expectation fail, and the failure names the step and the trigger.
            MissionPlan plan = Scenario("phone_countdown.json");
            RehearsalScript s = Script("phone_countdown.rehearsal.json", plan);
            plan.Triggers[1].Delay = 120;
            RehearsalResult r = Rehearsal.Run(plan, s);
            StepResult first = r.Steps.FirstOrDefault(x => !x.Ok);
            check("with a longer countdown the rehearsal fails", !r.Passed, "");
            check("the first failure is step 4, naming trigger 2 and how long is left", first != null && first.What == "step 4 at 66 s: trigger 2 'Countdown' has fired" && first.Expected == "fired" && first.Actual == "not fired: Condition met; 59 s left", first == null ? "" : first.What + " | " + first.Actual);
            check("the extraction check of that step says what it waits for", r.Steps.Any(x => x.What == "step 4 at 66 s: the extraction is open" && !x.Ok && x.Actual == "held: it waits for trigger 2 'Countdown'"), Failures(r));
        }
        {   // Enemies going down and a door opening are told by the script, and set off the triggers that wait for them.
            MissionPlan plan = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"down\",\"tag\":\"officer\",\"count\":0},{\"id\":2,\"when\":\"door\",\"pos\":[3,0,4]},{\"id\":3,\"when\":\"down\",\"count\":3},{\"id\":4,\"when\":\"zone\",\"pos\":[50,0,50],\"radius\":4}]," +
                                                    "\"spawns\":[{\"pos\":[0,0,0],\"tag\":\"officer\"},{\"pos\":[1,0,0],\"tag\":\"officer\"},{\"pos\":[2,0,0]}]}");
            string json = "{\"schema\":\"missionlab-rehearsal/1\",\"players\":1,\"steps\":[" +
                          "{\"at\":0,\"players\":[[0,0,0]]},{\"at\":1,\"event\":\"down\",\"tag\":\"officer\"},{\"at\":1.5,\"expect\":{\"fired\":[1],\"notFired\":[2,3]}}," +
                          "{\"at\":2,\"event\":\"door\",\"pos\":[5,0,4]},{\"at\":2.5,\"expect\":{\"notFired\":[2]}},{\"at\":3,\"event\":\"door\",\"pos\":[3.3,0,4.2]}," +
                          "{\"at\":4,\"event\":\"down\",\"count\":1},{\"at\":4,\"players\":[[52,0,50]]},{\"at\":4,\"expect\":{\"fired\":[2,3,4]}}]}";
            var found = Rehearsal.Read(json, out RehearsalScript s, plan);
            check("a script with every kind of event reads", s != null, string.Join(" | ", found));
            if (s != null)
            {
                RehearsalResult r = Rehearsal.Run(plan, s);
                check("every officer down, a door opened within half a metre, a third enemy down and a player in the zone set off their triggers", r.Passed, Failures(r));
            }
        }
        {   // Write then Read gives the same script back, and the same text.
            MissionPlan plan = Scenario("phone_countdown.json");
            RehearsalScript s = Script("phone_countdown.rehearsal.json", plan);
            s.Scenario = "fnv1a64:0123456789abcdef"; s.Revision = 3;
            s.Steps.Add(new RehearsalStep { At = 90.25f, Event = Rehearsal.Door, Pos = new V3(1.5f, -2, 3.25f) });
            s.Steps.Add(new RehearsalStep { At = 91, Event = Rehearsal.Down, Tag = "officer", Count = 2, Fire = 4 });
            s.Steps.Add(new RehearsalStep { At = 92, Event = Rehearsal.Alert, Players = new List<V3>() });
            s.Steps.Add(new RehearsalStep { At = 93, Expect = new RehearsalExpect { Extraction = Rehearsal.Held, Enemies = 0 } });
            string once = Rehearsal.Write(s);
            var found = Rehearsal.Read(once, out RehearsalScript back);
            string twice = back == null ? "" : Rehearsal.Write(back);
            check("a written script reads back with nothing found", back != null && found.Count == 0, string.Join(" | ", found));
            check("and writes again to the same text", once == twice, once + "\n" + twice);
            check("its fields survive", back != null && back.Name == "phone starts the countdown" && back.Players == 2 && back.Roll == 12345 && back.Revision == 3 && back.Steps.Count == 11
                                         && back.Steps[7].Pos.Z == 3.25f && back.Steps[8].Fire == 4 && back.Steps[8].Count == 2 && back.Steps[9].Players.Count == 0 && back.Steps[10].Expect.Enemies == 0
                                         && back.Steps[2].Expect.Phase[2] == TriggerEngine.Counting, once);
            check("the text starts with its format and keeps keys in order", once.StartsWith("{\"schema\":\"missionlab-rehearsal/1\",\"name\":\"phone starts the countdown\",\"scenario\":\"fnv1a64:0123456789abcdef\",\"revision\":3,\"players\":2,\"roll\":12345,\"steps\":["), once);
            check("an expectation is written in its fixed order", once.Contains("{\"at\":10,\"expect\":{\"fired\":[1],\"notFired\":[2,3],\"phase\":{\"2\":\"counting\"},\"extraction\":\"held\",\"enemies\":3}}"), once);
        }
        {   // A bad script is named place by place, the way a bad scenario is.
            MissionPlan plan = Scenario("phone_countdown.json");
            string json = "{\"schema\":\"missionlab-rehearsal/1\",\"players\":2,\"steps\":[{\"at\":0,\"players\":[[0,0]]},{\"at\":1,\"event\":\"sneeze\"},{\"at\":2,\"event\":\"door\"}," +
                          "{\"at\":3,\"fire\":9},{\"at\":4,\"expect\":{\"fired\":[\"x\"],\"phase\":{\"2\":\"asleep\",\"7\":\"waiting\"}}},{\"event\":\"alert\"},{\"at\":5},{\"at\":1,\"event\":\"alert\"}]}";
            var found = Rehearsal.Read(json, out RehearsalScript s, plan);
            string got = string.Join(" | ", found.Select(f => f.Code + " " + f.Path));
            string want = "wrong-type steps[0].players[0] | unknown-value steps[1].event | missing-field steps[2].pos | missing-ref steps[3].fire | wrong-type steps[4].expect.fired[0] | " +
                          "unknown-value steps[4].expect.phase.2 | missing-ref steps[4].expect.phase.7 | missing-field steps[5].at | missing-field steps[6] | out-of-range steps[7].at";
            check("every fault is named with its place, in order, and nothing is read", got == want && s == null, got);
            check("without the scenario a trigger number is not looked up", !Rehearsal.Read(json, out _, null).Any(f => f.Code == "missing-ref"), "");
            check("a newer rehearsal format is refused", Rehearsal.Read("{\"schema\":\"missionlab-rehearsal/2\",\"steps\":[]}", out _).Select(f => f.Code + " " + f.Path).SequenceEqual(new[] { "future-version schema" }), "");
            check("steps that are not a list are refused", Rehearsal.Read("{\"schema\":\"missionlab-rehearsal/1\",\"steps\":{}}", out _).Select(f => f.Code + " " + f.Path).SequenceEqual(new[] { "wrong-type steps" }), "");
            check("text that is not JSON is refused", Rehearsal.Read("not json", out RehearsalScript none)[0].Code == "not-json" && none == null, "");
        }
    }
}
