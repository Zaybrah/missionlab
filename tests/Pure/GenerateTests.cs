using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// The quick mission form (ML-022): what a request may hold, that the same request gives the same layout, that the role mix
/// and the extras are honoured or named as left out, and that the answer's plan is a scenario the strict reader takes.
/// The map is made in memory (SyntheticMaps' MapBuilder); no game file is needed.
/// </summary>
static class GenerateTests
{
    private static NavMesh _yard;

    // An open yard, 200 m square: room for every group, all of it out of doors.
    private static NavMesh Yard()
    {
        if (_yard != null) return _yard;
        var b = new MapBuilder();
        b.Floor(0, 0, 200, 200, 5, 0);
        _yard = b.Build();
        return _yard;
    }

    private static string Body(string more) =>
        "{\"navFile\":\"yard.bytes\",\"enemies\":12,\"seed\":7,\"infils\":[[10,0,100]]" + (more.Length > 0 ? "," + more : "") + "}";

    private static MissionPlan Make(string body, List<string> leftOut)
    {
        QuickRequest q = QuickMission.Read(body, out string error);
        if (q == null) throw new InvalidOperationException(error);
        return QuickMission.Build(q, Yard(), leftOut);
    }

    // GeneratorPin.Prints() as the generator and extras gave it before the role mix and the extras flags were added (recorded
    // from that code). The default path must keep every draw and every position.
    private static readonly string[] Pinned =
    {
        "house 1 15 fnv1a64:e7695bf3f9f65257", "house 2 15 fnv1a64:1ff7d189e905d1bb", "house 3 15 fnv1a64:1f73ab673815d9f0",
        "islands 1 15 fnv1a64:f63728b9b2ea37a8", "islands 2 15 fnv1a64:243a70442906720c", "islands 3 15 fnv1a64:df454f44b52b7565",
        "deck 1 15 fnv1a64:f92533cfd136f8ee", "deck 2 15 fnv1a64:3b8067357a62fb75", "deck 3 15 fnv1a64:a3753e590ade0dba",
    };

    public static void Run(Action<string, bool, string> check)
    {
        {   // Determinism contract: with no mix and the default extras a random layout is byte for byte what it was.
            List<string> now = GeneratorPin.Prints();
            check("the random layout with default input is unchanged for seeds 1 to 3 on three maps", now.SequenceEqual(Pinned), string.Join(" | ", now));
        }
        {   // Map names: a file name of the game only, never a path.
            check("a shipped map name is accepted", QuickMission.NavFileOk("suburbv2.bytes") && QuickMission.NavFileOk("depotraid1.bytes"), "");
            var bad = new[] { "", "../suburbv2.bytes", "maps/suburbv2.bytes", "maps\\suburbv2.bytes", "suburbv2.json", "C:suburbv2.bytes", "a..bytes" };
            check("a path, another kind of file or nothing is refused", bad.All(n => !QuickMission.NavFileOk(n)), string.Join(" ", bad.Where(QuickMission.NavFileOk)));
        }
        {   // What a request may hold.
            string Why(string body) { QuickMission.Read(body, out string e); return e; }
            check("a request without a map is refused", Why("{\"enemies\":12,\"seed\":1}").StartsWith("navFile"), Why("{\"enemies\":12,\"seed\":1}"));
            check("too few or too many enemies are refused", Why("{\"navFile\":\"a.bytes\",\"enemies\":1,\"seed\":1}").StartsWith("enemies") && Why("{\"navFile\":\"a.bytes\",\"enemies\":61,\"seed\":1}").StartsWith("enemies"), "");
            check("a request without a seed is refused", Why("{\"navFile\":\"a.bytes\",\"enemies\":6}").StartsWith("seed"), "");
            check("a negative share is refused", Why(Body("\"mix\":{\"guard\":-1}")).StartsWith("mix.guard"), Why(Body("\"mix\":{\"guard\":-1}")));
            check("moved intel is never offered, and the reason says it needs the game", Why(Body("\"extras\":[\"intel\"]")).Contains("needs the game itself"), Why(Body("\"extras\":[\"intel\"]")));
            check("an unknown extra is named", Why(Body("\"extras\":[\"weather\"]")).Contains("\"weather\" is not an extra"), Why(Body("\"extras\":[\"weather\"]")));
            check("text that is not JSON is refused", Why("enemies=12") == "the request is not JSON", Why("enemies=12"));

            QuickRequest q = QuickMission.Read(Body("\"mix\":{\"guard\":2,\"wander\":1,\"patrol\":1},\"extras\":[\"alert\",\"doors\"],\"authored\":[[50,0,50],[1,2]],\"doors\":[{\"name\":\"Door_1\",\"pos\":[5,0,5],\"locked\":true},{\"pos\":[6,0,6]}]"), out string none);
            check("a full request is read", q != null && none == "" && q.NavFile == "yard.bytes" && q.Enemies == 12 && q.Seed == 7, none);
            check("its mix, extras and points are kept", q != null && q.Mix != null && q.Mix[0] == 2 && q.Alert && !q.Ambush && q.Doors && q.Infils.Count == 1 && q.Authored.Count == 1, "");
            check("doors keep their name and whether the game locks them", q != null && q.MapDoors.Count == 2 && q.MapDoors[0].Locked == true && q.MapDoors[0].Name == "Door_1" && q.MapDoors[1].Locked == false, "");
            QuickRequest zero = QuickMission.Read(Body("\"mix\":{\"guard\":0,\"wander\":0,\"patrol\":0}"), out _);
            check("an all-zero mix means the generator's own mix", zero != null && zero.Mix == null, "");
            double[] shares = Generator.Shares(new float[] { 2, 1, 1 });
            check("shares become fractions of the whole", shares != null && Math.Abs(shares[0] - 0.5) < 1e-9 && Math.Abs(shares[2] - 0.25) < 1e-9, "");
        }
        {   // Same request, same layout; another seed, another layout; exactly the enemies asked for.
            var leftA = new List<string>(); var leftB = new List<string>();
            string a = Make(Body("\"extras\":[\"alert\",\"ambush\"]"), leftA).ToJson(), b = Make(Body("\"extras\":[\"alert\",\"ambush\"]"), leftB).ToJson();
            check("the same request gives the same layout", a == b && leftA.SequenceEqual(leftB), "");
            string c = Make(Body("\"extras\":[\"alert\",\"ambush\"]").Replace("\"seed\":7", "\"seed\":8"), new List<string>()).ToJson();
            check("another seed gives another layout", a != c, "");
            MissionPlan plan = Make(Body(""), new List<string>());
            check("exactly the enemies asked for, no spares", plan.Spawns.Count == 12, plan.Spawns.Count.ToString());
            check("the layout names its map and its seed", plan.NavFile == "yard.bytes" && plan.Seed == 7 && plan.Title == "Quick mission 7", plan.NavFile + " " + plan.Title);
        }
        {   // The role mix.
            MissionPlan guards = Make(Body("\"mix\":{\"guard\":1}"), new List<string>());
            check("a mix of guards only gives guards only", guards.Spawns.All(s => s.Role == SpawnRole.Guard), string.Join(",", guards.Spawns.Select(s => s.Role)));
            MissionPlan still = Make(Body("\"mix\":{\"guard\":1,\"wander\":1}"), new List<string>());
            check("a mix without patrols gets no patrol added", still.Spawns.All(s => s.Role != SpawnRole.Patrol) && still.Spawns.Any(s => s.Role == SpawnRole.Wander), string.Join(",", still.Spawns.Select(s => s.Role)));
            MissionPlan moving = Make(Body("\"mix\":{\"patrol\":1}"), new List<string>());
            check("a mix of patrols only has no guard, and every patrol has a route", moving.Spawns.All(s => s.Role != SpawnRole.Guard) && moving.Spawns.Where(s => s.Role == SpawnRole.Patrol).All(s => s.Patrol.Count >= 2)
                  && moving.Spawns.Count(s => s.Role == SpawnRole.Patrol) >= 6, string.Join(",", moving.Spawns.Select(s => s.Role)));
        }
        {   // Extras: what fits is made, what does not is named.
            var left = new List<string>();
            MissionPlan plan = Make(Body("\"extras\":[\"alert\",\"ambush\"]"), left);
            TriggerPlan alert = plan.Triggers.FirstOrDefault(t => t.Id == 1);
            check("the alert wave is a trigger on alert with its enemies held back", alert != null && alert.When == TriggerPlan.OnAlert && plan.Spawns.Any(s => s.Trigger == 1), "");
            check("an open yard has no building for an ambush, and that is said", !plan.Triggers.Any(t => t.Id == 2) && left.Any(l => l.StartsWith("ambush:")), string.Join(" | ", left));
            check("moved intel is always named as left out", left.Any(l => l.StartsWith("moved intel:")), "");
            check("the extras are noted in the plan", plan.Notes.Any(n => n.StartsWith("extras: ") && n.Contains("alerted")), string.Join(" | ", plan.Notes));
            var none = new List<string>();
            MissionPlan plain = Make(Body(""), none);
            check("without extras nobody is held back", plain.Triggers.Count == 0 && plain.Spawns.All(s => s.Trigger == 0), "");
            check("without insertion points the page is told enemies are not kept clear of them", Make("{\"navFile\":\"yard.bytes\",\"enemies\":6,\"seed\":3}", none).Spawns.Count > 0 && none.Any(l => l.StartsWith("insertion points:")), "");
            var few = new List<string>();
            Make(Body("\"extras\":[\"alert\"]").Replace("\"enemies\":12", "\"enemies\":4"), few);
            check("fewer than 6 enemies get no wave, and that is said", few.Any(l => l.StartsWith("alert wave and ambush:")), string.Join(" | ", few));
        }
        {   // Locked doors: from the door list the page sends; doors the game locks itself are left to it; three at most.
            var doors = new List<string>();
            for (int i = 0; i < 40; i++) doors.Add("{\"name\":\"D" + i + "\",\"pos\":[" + (i * 4) + ",0,20],\"locked\":" + (i % 2 == 0 ? "true" : "false") + "}");
            var left = new List<string>();
            MissionPlan plan = Make(Body("\"extras\":[\"doors\"],\"doors\":[" + string.Join(",", doors) + "]"), left);
            check("some doors are locked, three at most", plan.Doors.Count >= 1 && plan.Doors.Count <= 3 && plan.Doors.All(d => d.Locked == true), plan.Doors.Count.ToString());
            check("never one the game locks itself", plan.Doors.All(d => int.Parse(d.Name.Substring(1)) % 2 == 1), string.Join(",", plan.Doors.Select(d => d.Name)));
            var noList = new List<string>();
            MissionPlan bare = Make(Body("\"extras\":[\"doors\"]"), noList);
            check("without a door list no door is locked, and that is said", bare.Doors.Count == 0 && noList.Any(l => l.StartsWith("locked doors:")), string.Join(" | ", noList));
            var rule = new List<DoorPlan> { new DoorPlan { Name = "a", Locked = true }, new DoorPlan { Name = "b", Locked = false } };
            var into = new MissionPlan(); var said = new List<string>();
            var rng = new Rng(1);
            Extras.LockDoors(into, rule, rng, said);
            var fresh = new Rng(1); fresh.NextDouble();
            check("the door rule draws once per door the game leaves alone", rng.NextDouble() == fresh.NextDouble(), "");
        }
        {   // The answer the page reads.
            string answer = QuickMission.Answer(Body("\"extras\":[\"alert\"]"), name => name == "yard.bytes" ? Yard() : null, out int status);
            check("a request on a known map is answered", status == 200, answer.Length > 300 ? answer.Substring(0, 300) : answer);
            using (JsonDocument doc = JsonDocument.Parse(answer))
            {
                JsonElement root = doc.RootElement;
                check("the answer counts what was placed and by role", root.GetProperty("placed").GetInt32() == 12 && root.GetProperty("roles").GetProperty("guard").GetInt32() + root.GetProperty("roles").GetProperty("wander").GetInt32() + root.GetProperty("roles").GetProperty("patrol").GetInt32() == 12, "");
                check("it says what is left out", root.GetProperty("leftOut").EnumerateArray().Any(e => e.GetString().StartsWith("moved intel:")), "");
                string text = root.GetProperty("plan").GetRawText();
                List<Finding> read = ScenarioFile.Read(text, out MissionPlan kept);
                check("its plan is a scenario the strict reader takes", kept != null && !ScenarioFile.Blocks(read), string.Join("; ", read.Select(f => f.ToString())));
                check("with the map, the enemies and the alert wave in it", kept != null && kept.NavFile == "yard.bytes" && kept.Spawns.Count == 12 && kept.Triggers.Any(t => t.Id == 1), "");
            }
            QuickMission.Answer(Body(""), name => null, out int missing);
            check("a map file the game does not have is a 404", missing == 404, missing.ToString());
            string refused = QuickMission.Answer("{\"navFile\":\"../x.bytes\",\"enemies\":5,\"seed\":1}", name => Yard(), out int bad);
            check("a bad request is a 400 with the reason", bad == 400 && refused.Contains("navFile"), refused);
        }
    }
}
