using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// ML-055: a zone or hold that waits for another trigger ("after", missionlab-plan/4). In the in-game test of 21 November
/// the upper-floor hold ran out while the player looked for the phone, so its goal succeeded the moment the phone activated
/// it. A gated hold counts only from the look in which its gate has fired; a gate that cannot fire keeps it shut, and says why.
/// </summary>
static class Ml055Tests
{
    private static string Root()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        return root;
    }

    private static string Codes(List<Finding> f) => string.Join(" | ", f.Where(x => x.Level == Finding.Error).Select(x => x.Code + " " + x.Path));
    private static string Format(string json) => json.StartsWith("{\"schema\":\"", StringComparison.Ordinal) ? json.Substring(11, json.IndexOf('"', 11) - 11) : "";

    private static TriggerPlan Hold(int id, int after, float hold = 30f) => new TriggerPlan { Id = id, When = TriggerPlan.OnHold, Pos = new V3(0, 0, 0), Radius = 5f, Hold = hold, After = after };
    private static TriggerPlan Time(int id, float delay) => new TriggerPlan { Id = id, When = TriggerPlan.OnTime, Delay = delay };

    // Steps the engine every half second from..to and returns when the trigger first fired (-1: never).
    private static float FiredAt(TriggerEngine engine, World world, int id, float from, float to)
    {
        for (float now = from; now <= to + 0.001f; now += 0.5f)
            foreach (TriggerEvent e in engine.Step(now, world))
                if (e.Kind == TriggerEvent.Fire && e.Id == id) return now;
        return -1f;
    }

    public static void Run(Action<string, bool, string> check)
    {
        string root = Root();
        check("the fixtures are found", root != null, AppContext.BaseDirectory);
        if (root == null) return;
        string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests", "fixtures", name));

        {   // The in-game bug: the player holds the floor before the phone (trigger 1, by hand) is taken.
            var triggers = new List<TriggerPlan> { new TriggerPlan { Id = 1, When = TriggerPlan.OnAfter, After = 0 }, Hold(2, 1) };
            var engine = new TriggerEngine(); engine.Reset(0f); engine.Sync(triggers);
            var world = new World(); world.Inside.Add(2);
            check("a gated hold does not count while its gate has not fired", FiredAt(engine, world, 2, 0f, 60f) < 0f && engine.Phase(triggers[1], 60f, out _) == TriggerEngine.Waiting, "");
            string why = engine.Explain(triggers[1], 60f, world).Why;
            check("and Explain says it waits for trigger 1 before it watches its area", why == "Waiting for trigger 1 to fire; until then it does not watch its area", why);
            check("the gate is fired by hand", engine.FireNow(1, 60f, out _, out string no), no);
            float at = FiredAt(engine, world, 2, 60.5f, 120f);
            check("with the player already inside, the hold needs its full 30 s after the gate fires", at >= 90f && at <= 91f, at.ToString());
        }
        {   // A gated zone fires only after its gate.
            var triggers = new List<TriggerPlan> { Time(1, 10f), new TriggerPlan { Id = 2, When = TriggerPlan.OnZone, Pos = new V3(0, 0, 0), Radius = 5f, After = 1 } };
            var engine = new TriggerEngine(); engine.Reset(0f); engine.Sync(triggers);
            var world = new World(); world.Inside.Add(2);
            float at = FiredAt(engine, world, 2, 0f, 30f);
            check("a gated zone with a player inside fires in the look after its gate, not before", at >= 10f && at <= 11f, at.ToString());
        }
        {   // A gate that sat the mission out: the hold never fires by itself, and both the engine and Completion say why.
            var plan = new MissionPlan { ExtractionAfter = 2 };
            plan.Triggers.Add(Time(1, 5f)); plan.Triggers[0].Off = true; plan.Triggers[0].Chance = 50;
            plan.Triggers.Add(Hold(2, 1));
            plan.Spawns.Add(new SpawnPlan { Pos = new V3(30, 0, 30) });
            var engine = new TriggerEngine(); engine.Reset(0f); engine.Sync(plan.Triggers);
            var world = new World(); world.Inside.Add(2);
            check("a hold whose gate sat the mission out never fires by itself", FiredAt(engine, world, 2, 0f, 120f) < 0f, "");
            string why = engine.Explain(plan.Triggers[1], 120f, world).Why;
            check("Explain names the gate that sat out", why == "Waiting for trigger 1, which sat this mission out (chance); until then it does not watch its area", why);
            var found = Completion.Check(plan, null, 0);
            string text = string.Join(" | ", found.Select(f => f.Text));
            check("Completion says nobody could leave because the hold's gate sits out", Completion.Blocks(found) && text.Contains("trigger 2 waits for it: trigger 1 sits this mission out"), text);
        }
        {   // A gate in a circle, through the zone's gate: found as a circle; the zone never fires and Completion says so.
            var plan = new MissionPlan { ExtractionAfter = 2 };
            plan.Triggers.Add(new TriggerPlan { Id = 1, When = TriggerPlan.OnAfter, After = 2 });
            plan.Triggers.Add(new TriggerPlan { Id = 2, When = TriggerPlan.OnZone, Pos = new V3(0, 0, 0), Radius = 5f, After = 1 });
            plan.Spawns.Add(new SpawnPlan { Pos = new V3(30, 0, 30) });
            var rings = TriggerEngine.Rings(plan.Triggers);
            check("a circle through a zone's gate is found", rings.Count == 1 && rings[0].OrderBy(x => x).SequenceEqual(new[] { 1, 2 }), string.Join(";", rings.Select(r => string.Join(",", r))));
            var engine = new TriggerEngine(); engine.Reset(0f); engine.Sync(plan.Triggers);
            var world = new World(); world.Inside.Add(2);
            check("the zone in the circle never fires", FiredAt(engine, world, 2, 0f, 60f) < 0f && engine.InCircle(2), "");
            var found = Completion.Check(plan, null, 0);
            string text = string.Join(" | ", found.Select(f => f.Text));
            check("Completion says the zone waits for itself through the circle", Completion.Blocks(found) && text.Contains("waits for itself through other triggers"), text);
            // A gate outside the circle that waits for a trigger in one: the engine explains it as for an after trigger.
            plan.Triggers.Add(Hold(3, 1));
            engine.Sync(plan.Triggers);
            string why = engine.Explain(plan.Triggers[2], 10f, world).Why;
            check("a hold gated by a trigger in a circle is explained as such", why == "Waiting for trigger 1, which stands in a circle and can never fire; until then it does not watch its area", why);
        }
        {   // Completion: a gate that names itself or no trigger.
            var plan = new MissionPlan { ExtractionAfter = 2 };
            plan.Triggers.Add(Time(1, 5f)); plan.Triggers.Add(Hold(2, 9));
            plan.Spawns.Add(new SpawnPlan { Pos = new V3(30, 0, 30) });
            string text = string.Join(" | ", Completion.Check(plan, null, 0).Select(f => f.Text));
            check("Completion says a hold's gate does not exist", text.Contains("trigger 2 waits for trigger 9, which does not exist"), text);
        }
        {   // Writing: plan/4 only with a gate, byte for byte both ways.
            var plan = new MissionPlan();
            plan.Triggers.Add(Time(1, 5f)); plan.Triggers.Add(Hold(2, 0));
            plan.Spawns.Add(new SpawnPlan { Pos = new V3(30, 0, 30) });
            string plain = plan.ToJson();
            check("a hold without a gate is not plan/4 and writes no after", Format(plain) != MissionPlan.Schema4 && !plain.Contains("\"after\""), plain);
            plan.Triggers[1].After = 1;
            string gated = MissionPlan.FromJson(plan.ToJson()).ToJson();      // once through the reader, so the counts the reader fills in are there
            check("a gated hold makes the file plan/4", Format(gated) == MissionPlan.Schema4 && gated.Contains("\"after\":1,\"hold\":30"), gated);
            check("and it reads back to the same text", MissionPlan.FromJson(gated).ToJson() == gated, gated);
            check("the strict reader takes it and lists zone-after", !ScenarioFile.Blocks(ScenarioFile.Read(gated, out MissionPlan read)) && read != null && Capabilities.Of(read).Contains("zone-after"), Codes(ScenarioFile.Read(gated, out _)));
            check("without the gate, no zone-after", !Capabilities.Of(MissionPlan.FromJson(plain)).Contains("zone-after"), "");
            check("Pack knows zone-after", Pack.Known.Contains("zone-after"), "");
        }
        {   // all_fields_v4.json: strict reader, round trip, capability, plan/4.
            string text = Fixture("all_fields_v4.json");
            var found = ScenarioFile.Read(text, out MissionPlan plan);
            check("all_fields_v4.json passes the strict reader", !ScenarioFile.Blocks(found) && plan != null, Codes(found));
            if (plan != null)
            {
                string once = plan.ToJson(), twice = MissionPlan.FromJson(once).ToJson();
                check("all_fields_v4.json round-trips byte for byte, as plan/4", once == twice && Format(once) == MissionPlan.Schema4, once.Substring(0, Math.Min(80, once.Length)));
                check("all_fields_v4.json keeps both gates", plan.Triggers.First(t => t.Id == 3).After == 1 && plan.Triggers.First(t => t.Id == 5).After == 6, "");
                check("all_fields_v4.json lists zone-after", Capabilities.Of(plan).Contains("zone-after"), string.Join(",", Capabilities.Of(plan)));
            }
        }
        {   // The lenient reader drops a gate that names itself or nothing.
            var plan = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"hold\",\"pos\":[0,0,0],\"after\":1},{\"id\":2,\"when\":\"zone\",\"pos\":[0,0,0],\"after\":9},{\"id\":3,\"when\":\"zone\",\"pos\":[0,0,0],\"after\":1}],\"spawns\":[{\"pos\":[0,0,0]}]}");
            check("the lenient reader drops a self gate and a dangling gate, and keeps a good one", plan.Triggers[0].After == 0 && plan.Triggers[1].After == 0 && plan.Triggers[2].After == 1, "");
        }
        {   // A live change of the gate loses a hold under way.
            var triggers = new List<TriggerPlan> { Time(1, 1f), Hold(2, 1) };
            var engine = new TriggerEngine(); engine.Reset(0f); engine.Sync(triggers);
            var world = new World(); world.Inside.Add(2);
            FiredAt(engine, world, 2, 0f, 20f);
            check("a hold is under way after its gate fired", engine.Phase(triggers[1], 20f, out _) == TriggerEngine.Holding, "");
            check("ShapeOf includes the gate", TriggerEngine.ShapeOf(Hold(2, 1)) != TriggerEngine.ShapeOf(Hold(2, 0)), "");
            var changed = new List<TriggerPlan> { Time(1, 1f), Hold(2, 0) };
            engine.Sync(changed);
            check("removing its gate live loses the unfinished hold", engine.Phase(changed[1], 20f, out _) == TriggerEngine.Waiting, "");
            float at = FiredAt(engine, world, 2, 20.5f, 80f);
            check("and the hold counts its full 30 s again", at >= 50f && at <= 51f, at.ToString());
        }
        {   // GoalBook and engine: goal activated by trigger 1 succeeds on a hold gated by trigger 1.
            var plan = MissionPlan.FromJson("{\"schema\":\"missionlab-plan/4\",\"mission\":{\"triggers\":[{\"id\":1,\"name\":\"Phone taken\",\"when\":\"time\",\"delay\":40}," +
                                            "{\"id\":2,\"name\":\"Upper floor\",\"when\":\"hold\",\"pos\":[0,0,0],\"radius\":5,\"hold\":30,\"after\":1}]," +
                                            "\"goals\":[{\"id\":\"upper\",\"name\":\"Secure the upper floor\",\"activate\":1,\"succeed\":2}],\"spawns\":[{\"pos\":[30,0,30]}]}}");
            var engine = new TriggerEngine(); engine.Reset(0f); engine.Sync(plan.Triggers);
            var book = new GoalBook(); book.Sync(plan.Goals);
            var world = new World(); world.Inside.Add(2);
            float activeAt = -1f, doneAt = -1f;
            for (float now = 0f; now <= 120f; now += 0.5f)
            {
                engine.Step(now, world);
                foreach (GoalEvent e in book.Step(now, engine.EverFired))
                {
                    if (e.To == GoalBook.Active) activeAt = now;
                    if (e.To == GoalBook.Succeeded) doneAt = now;
                }
            }
            check("a hold reached before the phone no longer completes the goal the moment it activates", activeAt >= 40f && doneAt >= activeAt + 30f, activeAt + " / " + doneAt);
        }
    }
}
