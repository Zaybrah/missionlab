using System;
using System.Collections.Generic;
using MissionLab.Core;

/// <summary>
/// The random layout under the determinism contract: Generator.Generate with its default input and Extras.Waves with its
/// default extras, for seeds 1 to 3 on three made-up maps, as fingerprints of the written plan. GenerateTests compares them
/// with fingerprints recorded from the generator before the quick mission form (ML-022) gave it a role mix, so a change
/// that adds, drops or reorders a draw on the default path fails here.
/// </summary>
static class GeneratorPin
{
    public static List<string> Prints()
    {
        var maps = new List<(string Name, NavMesh Nav, List<V3> Infils, List<V3> Authored)>();
        {   // A yard with a walled house in it: an opening on one side, so there is indoor ground.
            var b = new MapBuilder();
            b.Floor(0, 0, 200, 200, 5, 0, (x, z) => x >= 80 && x < 120 && z >= 80 && z < 120 && (x < 85 || x >= 115 || z < 85 || z >= 115) && !(z >= 95 && z < 105 && x < 85));
            maps.Add(("house", b.Build(), new List<V3> { new V3(10, 0, 100) }, new List<V3> { new V3(100, 0, 100), new V3(60, 0, 140) }));
        }
        {   // Two islands, only one reachable from the insertion point.
            var b = new MapBuilder();
            b.Floor(0, 0, 100, 100, 5, 0);
            b.Floor(120, 0, 260, 100, 5, 0);
            maps.Add(("islands", b.Build(), new List<V3> { new V3(10, 0, 50) }, new List<V3>()));
        }
        {   // Ground and a deck 4 m above it, joined by a ramp; no insertion point known.
            var b = new MapBuilder();
            b.Floor(0, 0, 160, 120, 5, 0, (x, z) => x >= 90 && x < 110 && z >= 50 && z < 70);
            b.Floor(30, 30, 90, 90, 5, 4);
            for (float x = 90; x < 110; x += 5)
                for (float z = 50; z < 70; z += 5)
                    b.Quad(x, z, x + 5, z + 5, (px, pz) => 4f - (px - 90f) / 5f);
            maps.Add(("deck", b.Build(), new List<V3>(), new List<V3> { new V3(60, 4, 60) }));
        }
        var prints = new List<string>();
        foreach (var m in maps)
            foreach (long seed in new long[] { 1, 2, 3 })
            {
                var input = new GeneratorInput { Nav = m.Nav, Seed = seed, Scene = m.Name, Enemies = 14, Infils = m.Infils, Authored = m.Authored };
                MissionPlan plan = Generator.Generate(input);
                var notes = new List<string>();
                Extras.Waves(plan, new Rng(seed ^ 0xE87A5), m.Infils, notes);
                prints.Add(m.Name + " " + seed + " " + plan.Spawns.Count + " " + ScenarioFile.Hash(plan.ToJson() + "|" + string.Join(";", notes)));
            }
        return prints;
    }
}
