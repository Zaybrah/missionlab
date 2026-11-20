using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// The completion check and the trigger engine agree on circles (ML-029): a trigger that waits for itself through other
/// triggers never fires, even when its "any" list has another way in, so an extraction held behind it is refused.
/// Both ask TriggerEngine.Rings, which gives the same answer for every order of the list.
/// </summary>
static class Ml029Tests
{
    private const string Circle = "{\"id\":1,\"when\":\"any\",\"of\":[2,3]}", Echo = "{\"id\":2,\"when\":\"after\",\"after\":1}",
                         Clock = "{\"id\":3,\"when\":\"time\",\"delay\":0}", Opener = "{\"id\":4,\"when\":\"time\",\"delay\":0,\"extraction\":\"open\"}";

    /// <summary>A world in which everything a trigger can wait for has happened, so only the rules decide what fires.</summary>
    private sealed class YesWorld : ITriggerWorld
    {
        public bool Alerted() => true;
        public bool IntelTaken(string item) => true;
        public bool Occupied(TriggerPlan t, out V3 where) { where = default; return true; }
        public bool Down(int count, string tag) => true;
        public bool DoorOpen(V3 hinge) => true;
        public int Inside(TriggerPlan t, out int alive) { alive = 1; return 1; }
    }

    private static MissionPlan Plan(IEnumerable<string> triggers) =>
        MissionPlan.FromJson("{\"extraction\":{\"after\":1},\"triggers\":[" + string.Join(",", triggers) + "],\"spawns\":[{\"pos\":[0,0,0]}]}");

    // The blocking findings without their path: the path says where in the file (triggers[0] or triggers[2]), which
    // rightly moves with the order of the list; what is wrong must not.
    private static string Errors(MissionPlan p) => string.Join(" || ", Completion.Check(p, null, 0).Where(f => f.Level == Finding.Error).Select(f => f.Text));

    // What fires on each look of a made-up clock, a look per second for 12 s; within one look sorted by number,
    // since triggers that do not wait for each other may fire in the order of the list.
    private static string Run(MissionPlan p)
    {
        var e = new TriggerEngine();
        e.Reset(0f);
        e.Sync(p.Triggers);
        var w = new YesWorld();
        var looks = new List<string>();
        for (int s = 0; s <= 12; s++)
            looks.Add(string.Join(",", e.Step(s, w).Where(x => x.Kind == TriggerEvent.Fire).Select(x => x.Id).OrderBy(id => id)));
        return string.Join("|", looks);
    }

    private static IEnumerable<List<string>> Orders(List<string> items)
    {
        if (items.Count <= 1) { yield return new List<string>(items); yield break; }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = new List<string>(items); rest.RemoveAt(i);
            foreach (List<string> tail in Orders(rest)) { tail.Insert(0, items[i]); yield return tail; }
        }
    }

    public static void Run(Action<string, bool, string> check)
    {
        {   // The fixture: the extraction waits for an "any" in a circle whose other way in is a timer that fires.
            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "tests", "fixtures", "any_cycle_extraction.json"))) root = Path.GetDirectoryName(root);
            check("the fixture with an any-circle is found", root != null, AppContext.BaseDirectory);
            if (root != null)
            {
                List<Finding> read = ScenarioFile.Read(File.ReadAllText(Path.Combine(root, "tests", "fixtures", "any_cycle_extraction.json")), out MissionPlan p);
                string broken = p == null ? "not read" : Errors(p);
                check("the fixture is refused: nobody could leave, and the circle of triggers 1 and 2 is named",
                      p != null && broken.Contains("nobody could leave") && broken.Contains("triggers 1, 2 wait for each other in a circle"), broken + " " + string.Join(" | ", read));
            }
        }
        {   // Every order of the list gives the same findings and the same firings: only the timer fires.
            var orders = Orders(new List<string> { Circle, Echo, Clock }).ToList();
            string findings = Errors(Plan(orders[0])), firings = Run(Plan(orders[0]));
            bool same = orders.All(o => Errors(Plan(o)) == findings && Run(Plan(o)) == firings);
            check("all six orders of the any-circle are refused alike and fire alike", orders.Count == 6 && same && findings != "" && firings.StartsWith("3|", StringComparison.Ordinal) && !firings.Contains("1") && !firings.Contains("2"), findings + " / " + firings);
            check("the engine names the same circle for every order",
                  orders.All(o => string.Join(";", TriggerEngine.Rings(Plan(o).Triggers).Select(r => string.Join(",", r))) == "1,2"), "");
        }
        {   // Another trigger that opens the extraction and can fire is a way out, in every order.
            var orders = Orders(new List<string> { Circle, Echo, Clock, Opener }).ToList();
            string firings = Run(Plan(orders[0]));
            check("with a timer that opens the extraction the any-circle is accepted in all 24 orders",
                  orders.Count == 24 && orders.All(o => Errors(Plan(o)) == "" && Run(Plan(o)) == firings) && firings.StartsWith("3,4|", StringComparison.Ordinal), Errors(Plan(orders[0])) + " / " + firings);
        }
        {   // What the check calls reachable is what the engine fires, on rolled missions with a chance branch and a repeating prerequisite.
            const string Rolled = "{\"triggers\":["
                + "{\"id\":1,\"when\":\"time\",\"delay\":0,\"repeat\":3},"           // repeating prerequisite
                + "{\"id\":2,\"when\":\"after\",\"after\":1},"
                + "{\"id\":3,\"when\":\"alert\",\"chance\":50},"                    // the chance branch
                + "{\"id\":4,\"when\":\"after\",\"after\":3},"
                + "{\"id\":5,\"when\":\"any\",\"of\":[4,6]},"                       // in a circle with 6, with 4 as another way in
                + "{\"id\":6,\"when\":\"after\",\"after\":5},"
                + "{\"id\":7,\"when\":\"all\",\"of\":[1,2]},"
                + "{\"id\":8,\"when\":\"after\",\"after\":0},"                      // fired by hand, and no Game Master
                + "{\"id\":9,\"when\":\"any\",\"of\":[4,7]},"
                + "{\"id\":10,\"when\":\"all\",\"of\":[4,9]}"
                + "],\"spawns\":[{\"pos\":[0,0,0]}]}";
            int[] ids = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            bool agree = true, sawOn = false, sawOff = false; string where = "";
            for (long seed = 1; seed <= 24; seed++)
            {
                MissionPlan p = MissionPlan.FromJson(Rolled);
                Variants.Out gone = Variants.Resolve(p, new Rng(seed), new List<string>(), 0);
                TriggerPlan chance = p.Triggers.First(t => t.Id == 3);
                if (chance.Off) sawOff = true; else sawOn = true;
                var e = new TriggerEngine();
                e.Reset(0f);
                e.Sync(p.Triggers);
                var w = new YesWorld();
                for (int s = 0; s <= 12; s++) e.Step(s, w);
                foreach (int id in ids)
                {
                    p.ExtractionAfter = id;
                    bool reachable = !Completion.Blocks(Completion.Check(p, gone, 0));
                    if (reachable != e.EverFired(id)) { agree = false; where += " seed " + seed + " trigger " + id + (reachable ? " reachable, not fired" : " fired, called unreachable"); }
                }
            }
            check("on every rolled mission the check calls reachable exactly the triggers the engine fires", agree && sawOn && sawOff, where + (sawOn && sawOff ? "" : " (the chance never went both ways)"));
        }
    }
}
