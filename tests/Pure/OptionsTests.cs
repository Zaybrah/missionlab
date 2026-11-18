using System;
using System.Collections.Generic;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// Host options (ML-022): what a choice takes out or changes, how picks fall back to the author's default, that options never
/// move the dice, and that Validate finds a combination that makes the mission unfinishable and says how to play it again.
/// </summary>
static class OptionsTests
{
    public static void Run(Action<string, bool, string> check)
    {
        {   // A choice drops its lots and triggers and sets respawns and skill; the other choice changes nothing.
            MissionPlan light = Garrison();
            Options.Apply(light, new Dictionary<string, int> { ["garrison"] = 0 });
            check("the light garrison leaves out lots 3 and 4", light.Spawns.Count == 2 && light.Spawns.All(s => s.Lot != 3 && s.Lot != 4), light.Spawns.Count.ToString());
            check("and switches trigger 7 off", light.Triggers.Single(t => t.Id == 7).Off && !light.Triggers.Single(t => t.Id == 8).Off, "");
            check("and sets respawns and skill", light.RespawnTimes == 1 && light.Skill == "recruit", light.RespawnTimes + " " + light.Skill);

            MissionPlan full = Garrison();
            Options.Apply(full, new Dictionary<string, int> { ["garrison"] = 1 });
            check("the full garrison keeps every enemy and trigger", full.Spawns.Count == 5 && !full.Triggers.Any(t => t.Off), full.Spawns.Count.ToString());
            check("and leaves respawns and skill as the scenario has them", full.RespawnTimes == 0 && full.Skill == "", full.RespawnTimes + " " + full.Skill);
        }
        {   // An unknown option id, a pick out of range, a negative pick or no picks at all: the author's default (pick 0 here).
            foreach (var picks in new[] { null, new Dictionary<string, int>(), new Dictionary<string, int> { ["nope"] = 1 },
                                          new Dictionary<string, int> { ["garrison"] = 9 }, new Dictionary<string, int> { ["garrison"] = -1 } })
            {
                MissionPlan plan = Garrison();
                Options.Apply(plan, picks);
                string said = picks == null ? "null" : string.Join(",", picks.Select(p => p.Key + "=" + p.Value));
                check("picks " + said + " fall back to the default choice", plan.Spawns.Count == 2 && plan.RespawnTimes == 1, plan.Spawns.Count.ToString());
            }
            MissionPlan other = Garrison(); other.Options[0].Pick = 1;
            check("the default is the option's own pick", Options.Chosen(other.Options[0], new Dictionary<string, int> { ["garrison"] = 7 }) == 1, "");
        }
        {   // Options draw nothing: the same seed rolls the same dice whichever choice is picked.
            string Rolled(int pick)
            {
                MissionPlan plan = Garrison();
                for (int i = 0; i < plan.Spawns.Count; i++) plan.Spawns[i].Chance = 50;
                plan.Triggers.ForEach(t => t.Chance = 50);
                var rng = new Rng(42 ^ 0x5EED);
                Variants.Out gone = Variants.Resolve(plan, rng, null, 0);
                Options.Apply(plan, new Dictionary<string, int> { ["garrison"] = pick });
                return string.Join(",", gone.SpawnIndex) + "|" + string.Join(",", plan.Triggers.Where(t => t.Id != 7).Select(t => t.Off)) + "|" + rng.NextU64();
            }
            check("the same seed rolls the same dice whatever the picks", Rolled(0) == Rolled(1), Rolled(0) + " / " + Rolled(1));
        }
        {   // A choice that removes what the extraction waits for is found, with the combination, squad size and roll that show it.
            MissionPlan plan = Extraction();
            List<Finding> found = Options.Validate(plan, 5);
            List<Finding> errors = found.Where(f => f.Level == Finding.Error).ToList();
            check("Validate finds the two combinations that leave nobody able to leave", errors.Count == 2 && errors.All(e => e.Code == Options.Unfinishable), string.Join(" | ", found));
            string text = errors.Count > 0 ? errors[0].Text : "";
            check("it names the combination", text.Contains("boss=1 'Without the officer'") && text.Contains("doors=0 'Locked'"), text);
            check("it names the squad size and the roll", text.Contains("1 player and roll 1"), text);
            check("it points at the trigger the extraction waits for", errors.Count > 0 && errors[0].Path == "triggers[0]", errors.Count > 0 ? errors[0].Path : "");
            check("the combinations that keep the officer pass", !errors.Exists(e => e.Text.Contains("boss=0")), string.Join(" | ", errors));
            check("a note says how much was tried and that it is a sample", found.Exists(f => f.Level == Finding.Note && f.Text.Contains("tried 4 combinations") && f.Text.Contains("squad sizes 1 to 4") && f.Text.Contains("rolls 1 to 5") && f.Text.Contains("not proof")), string.Join(" | ", found));
            check("Validate leaves the plan as it was", plan.Spawns.Count == 4 && !plan.Triggers.Any(t => t.Off), "");
            plan.SquadMin = 2; plan.SquadMax = 3;
            check("the author's squad range is the one tried", Options.Validate(plan, 5).Exists(f => f.Text.Contains("squad sizes 2 to 3") && f.Text.Contains("tried")), "");
        }
        {   // A choice that leaves out the target person is an error too.
            MissionPlan plan = Garrison();
            plan.Spawns.First(s => s.Lot == 3).Hvt = true;
            List<Finding> found = Options.Validate(plan, 3);
            check("leaving out the target person is unfinishable", found.Exists(f => f.Level == Finding.Error && f.Code == Options.Unfinishable && f.Text.Contains("garrison=0") && f.Text.Contains("target person")), string.Join(" | ", found));
        }
        {   // A scenario whose choices are all fine has no error.
            List<Finding> found = Options.Validate(Garrison(), 3);
            check("harmless options validate without an error", !Completion.Blocks(found), string.Join(" | ", found));
            check("without dice one roll is said to cover every roll", found.Exists(f => f.Text.Contains("one roll covers every roll")), string.Join(" | ", found));
        }
        {   // More than 64 combinations: 64 are tried and a note says the coverage is sampled.
            var plan = new MissionPlan();
            plan.Spawns.Add(new SpawnPlan { Pos = new V3(1, 0, 1) });
            for (int i = 0; i < 4; i++)
            {
                var o = new OptionPlan { Id = "o" + i, Name = "Option " + i };
                for (int c = 0; c < 3; c++) o.Choices.Add(new ChoicePlan { Name = "C" + c });
                plan.Options.Add(o);
            }
            List<Finding> found = Options.Validate(plan, 2);
            check("81 combinations give the sampled note", found.Exists(f => f.Level == Finding.Note && f.Text.Contains("sampled") && f.Text.Contains("81 combinations") && f.Text.Contains("64 of them")), string.Join(" | ", found));
            check("and 64 combinations are tried", found.Exists(f => f.Text.Contains("tried 64 combinations")), string.Join(" | ", found));
        }
        {   // The browser link's check reads the text strictly and validates it.
            string text = "{\"schema\":\"missionlab-plan/1\",\"name\":\"t\",\"extraction\":{\"after\":1}," +
                          "\"options\":[{\"id\":\"boss\",\"name\":\"Officer\",\"choices\":[{\"name\":\"With\"},{\"name\":\"Without\",\"drop\":{\"lots\":[2]}}]}]," +
                          "\"triggers\":[{\"id\":1,\"when\":\"down\",\"tag\":\"officer\",\"count\":1}]," +
                          "\"spawns\":[{\"pos\":[1,0,1],\"role\":\"guard\"},{\"pos\":[5,0,5],\"role\":\"guard\",\"lot\":2,\"tag\":\"officer\"}]}";
            string answer = Options.Answer("{\"scenario\":" + Json(text) + ",\"seeds\":2}", out int status);
            check("the options check answers a readable scenario", status == 200 && answer.Contains("\"readable\":true"), answer);
            check("and reports the unfinishable combination", answer.Contains("\"code\":\"unfinishable\"") && answer.Contains("boss=1"), answer);
            string bad = Options.Answer("{\"scenario\":\"{nope\"}", out int s2);
            check("an unreadable scenario is answered with its reading findings", s2 == 200 && bad.Contains("\"readable\":false") && bad.Contains("not-json"), bad);
            Options.Answer("{}", out int s3);
            check("a request without a scenario is refused", s3 == 400, "");
        }
        {   // ML-023 save fix: what a choice overrides is kept as the author had it, and never reaches the file by itself.
            MissionPlan plan = Garrison(); plan.Skill = "veteran"; plan.RespawnTimes = 3; plan.RespawnAfter = 30f;
            Options.Apply(plan, new Dictionary<string, int> { ["garrison"] = 0 });
            check("Apply keeps the authored skill and respawns it overrides", plan.SkillAuthored == "veteran" && plan.RespawnTimesAuthored == 3 && plan.Skill == "recruit" && plan.RespawnTimes == 1,
                  plan.SkillAuthored + " " + plan.RespawnTimesAuthored + " " + plan.Skill + " " + plan.RespawnTimes);
            string withAuthored = plan.ToJson();
            plan.SkillAuthored = null; plan.RespawnTimesAuthored = null;
            check("ToJson of an applied plan is unchanged by the authored values", withAuthored == plan.ToJson(), withAuthored);
            MissionPlan twice = Garrison(); twice.Skill = "veteran";
            Options.Apply(twice, null); Options.Apply(twice, null);
            check("applying twice still keeps the author's own skill", twice.SkillAuthored == "veteran", twice.SkillAuthored);
            MissionPlan full = Garrison();
            Options.Apply(full, new Dictionary<string, int> { ["garrison"] = 1 });
            check("a choice that overrides nothing keeps no authored values", full.SkillAuthored == null && !full.RespawnTimesAuthored.HasValue, "");
        }
    }

    // Garrison size: Light (default) drops lots 3 and 4 and trigger 7, one respawn, recruits; Full changes nothing.
    private static MissionPlan Garrison()
    {
        var plan = new MissionPlan();
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(1, 0, 1) });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(2, 0, 2), Lot = 3 });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(3, 0, 3), Lot = 3 });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(4, 0, 4), Lot = 4 });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(5, 0, 5), Lot = 5 });
        plan.Triggers.Add(new TriggerPlan { Id = 7, When = TriggerPlan.OnTime, Delay = 60 });
        plan.Triggers.Add(new TriggerPlan { Id = 8, When = TriggerPlan.OnTime, Delay = 90 });
        var light = new ChoicePlan { Name = "Light", RespawnTimes = 1, Skill = "recruit" };
        light.DropLots.Add(3); light.DropLots.Add(4); light.DropTriggers.Add(7);
        var o = new OptionPlan { Id = "garrison", Name = "Garrison size" };
        o.Choices.Add(light); o.Choices.Add(new ChoicePlan { Name = "Full" });
        plan.Options.Add(o);
        return plan;
    }

    // The extraction waits for the officer (lot 2) to go down; one choice leaves the officer out. A second option is harmless.
    // One enemy is left to chance, so rolls matter.
    private static MissionPlan Extraction()
    {
        var plan = new MissionPlan { ExtractionAfter = 1 };
        plan.Triggers.Add(new TriggerPlan { Id = 1, Name = "Officer down", When = TriggerPlan.OnDown, Tag = "officer", Count = 1 });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(1, 0, 1) });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(5, 0, 5), Lot = 2, Tag = "officer" });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(9, 0, 9), Chance = 50 });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(12, 0, 9), Lot = 6 });
        var without = new ChoicePlan { Name = "Without the officer" };
        without.DropLots.Add(2);
        var boss = new OptionPlan { Id = "boss", Name = "Officer" };
        boss.Choices.Add(new ChoicePlan { Name = "With the officer" }); boss.Choices.Add(without);
        var doors = new OptionPlan { Id = "doors", Name = "Doors" };
        doors.Choices.Add(new ChoicePlan { Name = "Locked" }); doors.Choices.Add(new ChoicePlan { Name = "Open", RespawnTimes = 2 });
        plan.Options.Add(boss); plan.Options.Add(doors);
        return plan;
    }

    private static string Json(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
