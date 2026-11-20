using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using MissionLab.Core;

/// <summary>
/// Repeating a generated setup (ML-031): the chosen run's roll comes before the configured seed, a repeat goes ahead only when
/// every recorded input matches the mission's own, and the same roll and inputs give the same plan byte for byte. The game side
/// (reading the map file, the scene's points and the settings, and leaving the mission alone on a refusal) is not run here.
/// </summary>
static class Ml031Tests
{
    private static RepeatInputs Now() => new RepeatInputs
    {
        Generator = RepeatSetup.GeneratorVersion, Scene = "Suburb", NavFile = "suburbv2.bytes", MapPrint = "fnv1a64:0123456789abcdef", ScenePrint = "fnv1a64:fedcba9876543210",
        Enemies = 14, RandomExtras = true, RandomIntel = false, MoveTarget = true, Target = true
    };

    // The inputs as a later mission reads them back from the record it was written into.
    private static RepeatInputs RoundTrip(RepeatInputs i) => RepeatSetup.ReadInputs("{\"schema\":\"missionlab-run/1\",\"roll\":4242,\"inputs\":" + RepeatSetup.InputsJson(i) + "}");

    private static RepeatSource Generated(RepeatInputs then) => new RepeatSource { Outcome = "used", Scenario = "", Roll = 4242, Inputs = then };

    public static void Run(Action<string, bool, string> check)
    {
        {   // The same inputs, written into a record and read back, let the repeat go ahead.
            RepeatVerdict v = RepeatSetup.ForGenerated(Generated(RoundTrip(Now())), Now());
            check("a generated run with unchanged inputs is repeated exactly", v.Exact, v.Text);
        }
        {   // Every input that differs stops the repeat, and the reason names that input.
            var changes = new List<(string Key, Action<RepeatInputs> Change)>
            {
                (RepeatSetup.KeyGenerator, i => i.Generator = RepeatSetup.GeneratorVersion + 1),
                (RepeatSetup.KeyScene, i => i.Scene = "Depot"),
                (RepeatSetup.KeyNavFile, i => i.NavFile = "depotraid1.bytes"),
                (RepeatSetup.KeyMapPrint, i => i.MapPrint = "fnv1a64:1111111111111111"),
                (RepeatSetup.KeyScenePrint, i => i.ScenePrint = "fnv1a64:2222222222222222"),
                (RepeatSetup.KeyEnemies, i => i.Enemies = 15),
                (RepeatSetup.KeyRandomExtras, i => i.RandomExtras = false),
                (RepeatSetup.KeyRandomIntel, i => i.RandomIntel = true),
                (RepeatSetup.KeyMoveTarget, i => i.MoveTarget = false),
                (RepeatSetup.KeyTarget, i => i.Target = false),
            };
            var wrong = new List<string>();
            foreach (var c in changes)
            {
                RepeatInputs now = Now(); c.Change(now);
                RepeatVerdict v = RepeatSetup.ForGenerated(Generated(RoundTrip(Now())), now);
                if (v.Exact || !v.Keys.SequenceEqual(new[] { c.Key })) wrong.Add(c.Key + " -> " + string.Join(",", v.Keys));
            }
            check("each changed input refuses the repeat and names only that input", wrong.Count == 0, string.Join(" | ", wrong));
        }
        {   // A record that lacks one input (a later version left it out, or the file was edited) is refused for that input.
            string[] keys = { RepeatSetup.KeyGenerator, RepeatSetup.KeyScene, RepeatSetup.KeyNavFile, RepeatSetup.KeyMapPrint, RepeatSetup.KeyScenePrint,
                              RepeatSetup.KeyEnemies, RepeatSetup.KeyRandomExtras, RepeatSetup.KeyRandomIntel, RepeatSetup.KeyMoveTarget, RepeatSetup.KeyTarget };
            var wrong = new List<string>();
            foreach (string key in keys)
            {
                JsonObject inputs = JsonNode.Parse(RepeatSetup.InputsJson(Now())).AsObject();
                inputs.Remove(key);
                RepeatInputs then = RepeatSetup.ReadInputs("{\"roll\":4242,\"inputs\":" + inputs.ToJsonString() + "}");
                RepeatVerdict v = RepeatSetup.ForGenerated(Generated(then), Now());
                if (then == null || v.Exact || !v.Keys.SequenceEqual(new[] { key })) wrong.Add(key + " -> " + (then == null ? "unread" : string.Join(",", v.Keys)));
            }
            check("each missing input refuses the repeat and names that input", wrong.Count == 0, string.Join(" | ", wrong));
        }
        {   // A record from before the inputs were kept.
            string legacy = "{\"schema\":\"missionlab-run/1\",\"plugin\":\"0.26.2\",\"scene\":\"Suburb\",\"navFile\":\"suburbv2.bytes\",\"scenario\":\"\",\"roll\":4242,\"players\":2,\"outcome\":\"used\",\"reason\":\"\",\"end\":null,\"resolved\":{}}";
            RepeatInputs then = RepeatSetup.ReadInputs(legacy);
            RepeatVerdict v = RepeatSetup.ForGenerated(Generated(then), Now());
            check("a legacy record has no inputs", then == null, "");
            check("a legacy generated run is refused for its missing inputs", !v.Exact && v.Keys.Contains(RepeatSetup.KeyInputs), string.Join(",", v.Keys));
        }
        {   // A map fingerprint that could not be taken proves nothing, even when it is missing on both sides.
            RepeatInputs then = Now(); then.MapPrint = "";
            RepeatInputs now = Now(); now.MapPrint = "";
            RepeatVerdict v = RepeatSetup.ForGenerated(Generated(RoundTrip(then)), now);
            check("an unread map file on both sides refuses the repeat", !v.Exact && v.Keys.Contains(RepeatSetup.KeyMapPrint), string.Join(",", v.Keys));
            RepeatVerdict later = RepeatSetup.ForGenerated(Generated(RoundTrip(Now())), now);
            check("a map file that cannot be read now refuses the repeat", !later.Exact && later.Keys.Contains(RepeatSetup.KeyMapPrint), string.Join(",", later.Keys));
        }
        {   // Unreadable, refused and scenario records never start a generated layout under a repeat label.
            RepeatVerdict unread = RepeatSetup.ForGenerated(new RepeatSource { Readable = false }, Now());
            check("an unreadable record is refused", !unread.Exact && unread.Keys.Contains(RepeatSetup.KeyRecord), string.Join(",", unread.Keys));
            RepeatSource refused = Generated(RoundTrip(Now())); refused.Outcome = "refused";
            RepeatVerdict no = RepeatSetup.ForGenerated(refused, Now());
            check("a refused run is not repeated", !no.Exact && no.Keys.Contains(RepeatSetup.KeyOutcome), string.Join(",", no.Keys));
            RepeatVerdict other = RepeatSetup.ForGenerated(new RepeatSource { Outcome = "used", Scenario = "Raid.json", Roll = 7 }, Now());
            check("a scenario's run is not repeated by a generated layout", !other.Exact && other.Keys.Contains(RepeatSetup.KeyKind), string.Join(",", other.Keys));
        }
        {   // A scenario mission keeps its earlier repeat behaviour for a scenario's run, but never repeats a generated one.
            RepeatVerdict same = RepeatSetup.ForScenario(new RepeatSource { Outcome = "refused", Scenario = "Raid.json", Roll = 7 }, "Raid.json");
            check("a scenario's run, even a refused one, may still be repeated by a scenario", same.Exact, same.Text);
            RepeatVerdict gen = RepeatSetup.ForScenario(Generated(RoundTrip(Now())), "Raid.json");
            check("a generated run is not repeated by a scenario", !gen.Exact && gen.Keys.Contains(RepeatSetup.KeyKind), string.Join(",", gen.Keys));
            RepeatVerdict unread = RepeatSetup.ForScenario(new RepeatSource { Readable = false }, "Raid.json");
            check("an unreadable record is refused by a scenario too", !unread.Exact && unread.Keys.Contains(RepeatSetup.KeyRecord), string.Join(",", unread.Keys));
        }
        {   // The seed: the chosen run, then the configured one, then a fresh one.
            int freshCalls = 0;
            long Fresh() { freshCalls++; return 555555; }
            check("the chosen run's roll wins over a conflicting configured seed", RepeatSetup.Seed(4242, 99, Fresh) == 4242, "");
            check("the chosen run's roll is used with seed 0 configured", RepeatSetup.Seed(4242, 0, Fresh) == 4242 && freshCalls == 0, freshCalls.ToString());
            check("without a repeat the configured seed is used", RepeatSetup.Seed(null, 99, Fresh) == 99 && freshCalls == 0, "");
            check("without a repeat or a configured seed a fresh one is drawn", RepeatSetup.Seed(null, 0, Fresh) == 555555 && freshCalls == 1, "");
        }
        {   // The scene's own points: their order does not count, a move of a few centimetres does.
            var authored = new List<V3> { new V3(100, 0, 100), new V3(60, 0, 140) };
            var infils = new List<V3> { new V3(10, 0, 100), new V3(12, 0, 104) };
            var boxes = new List<float[]> { new float[] { 0, 10, 0, 10 } };
            var targets = new List<V3> { new V3(90, 0, 90) };
            string a = RepeatSetup.ScenePrint(infils, authored, boxes, targets, 30, 20, 90);
            string b = RepeatSetup.ScenePrint(new List<V3> { infils[1], infils[0] }, new List<V3> { authored[1], authored[0] }, boxes, targets, 30, 20, 90);
            check("the scene fingerprint does not depend on the order the game lists its points in", a == b, a + " " + b);
            string moved = RepeatSetup.ScenePrint(infils, authored, boxes, new List<V3> { new V3(90.05f, 0, 90) }, 30, 20, 90);
            string senses = RepeatSetup.ScenePrint(infils, authored, boxes, targets, 31, 20, 90);
            check("a moved target spot or other usual senses change the scene fingerprint", moved != a && senses != a, "");
        }
        {   // Generating twice: a run made with seed 0 configured, then repeated with a conflicting seed configured, gives the same plan.
            var b = new MapBuilder();
            b.Floor(0, 0, 200, 200, 5, 0, (x, z) => x >= 80 && x < 120 && z >= 80 && z < 120 && (x < 85 || x >= 115 || z < 85 || z >= 115) && !(z >= 95 && z < 105 && x < 85));
            NavMesh nav = b.Build();
            var infils = new List<V3> { new V3(10, 0, 100) };
            var authored = new List<V3> { new V3(100, 0, 100), new V3(60, 0, 140) };
            string Make(long seed)
            {
                var input = new GeneratorInput { Nav = nav, Seed = seed, Scene = "house", Enemies = 14, Infils = infils, Authored = authored };
                MissionPlan plan = Generator.Generate(input);
                Extras.Waves(plan, new Rng(seed ^ 0xE87A5), infils, new List<string>());
                return plan.ToJson();
            }
            long first = RepeatSetup.Seed(null, 0, () => 731904);
            string made = Make(first);
            RepeatInputs recorded = RoundTrip(Now());
            RepeatVerdict v = RepeatSetup.ForGenerated(new RepeatSource { Outcome = "used", Roll = first, Inputs = recorded }, Now());
            long again = RepeatSetup.Seed(first, 99, () => 123456);
            string repeated = Make(again);
            check("the repeat goes ahead and uses the recorded roll", v.Exact && again == first, v.Text + " " + again);
            check("the repeated plan, extras included, is byte for byte the first one", repeated == made, "");
            check("the conflicting configured seed would have given another plan", Make(99) != made, "");
        }
    }
}
