using System;
using System.Collections.Generic;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// Constrained scatter (ML-023): the same seed gives the same placement; a scattered enemy keeps to its "within" area, out of
/// "empty" areas, on its own floor and ground, clear of other enemies and of insertion points; and when no spot passes it
/// stays or is left out as declared, never a target person, with a skip that breaks the extraction caught by Completion.
/// </summary>
static class ScatterTests
{
    private static readonly long[] Seeds = Enumerable.Range(1, 40).Select(i => (long)i).ToArray();

    public static void Run(Action<string, bool, string> check)
    {
        {   // Same inputs and seed: the same placement, to the centimetre; another seed moves at least one enemy elsewhere.
            string a = Positions(Resolved(Open(), 7)), b = Positions(Resolved(Open(), 7)), c = Positions(Resolved(Open(), 8));
            check("the same scenario and seed give the same scattered placement", a == b, a + " | " + b);
            check("another seed gives another placement", a != c, a);
        }
        {   // Every scattered enemy draws exactly 24 candidates of two draws each, whatever happens to them.
            MissionPlan plan = Open();
            var rng = new Rng(5 ^ Scatter.Salt);
            Scatter.Resolve(plan, new FlatWalk(), rng, new List<V3>());
            var fresh = new Rng(5 ^ Scatter.Salt);
            for (int i = 0; i < 3 * Scatter.Attempts * 2; i++) fresh.NextDouble();
            check("three scattered enemies use exactly 144 draws", rng.NextU64() == fresh.NextU64(), "");
        }
        {   // Moved enemies are on the ground and within their scatter radius of the authored place.
            var far = new List<string>(); int moved = 0;
            foreach (long seed in Seeds)
            {
                MissionPlan plan = Open(); var authored = plan.Spawns.Select(s => s.Pos).ToList();
                ScatterOut o = Scatter.Resolve(plan, new FlatWalk(), new Rng(seed ^ Scatter.Salt), new List<V3>());
                moved += o.Moved;
                for (int i = 0; i < plan.Spawns.Count; i++) if (V3.DistXZ(plan.Spawns[i].Pos, authored[i]) > plan.Spawns[i].Scatter + 0.01f) far.Add(seed + ":" + i);
            }
            check("on open ground every scattered enemy moves", moved == Seeds.Length * 3, moved.ToString());
            check("and never further than its scatter radius", far.Count == 0, string.Join(" ", far));
        }
        {   // "within": a scattered enemy never leaves its allow placement, even with a radius that reaches well beyond it.
            var out_ = new List<string>(); int moved = 0;
            foreach (long seed in Seeds)
            {
                MissionPlan plan = One(new V3(50, 0, 50), 20f);
                plan.Placement.Add(new PlacementPlan { Id = 1, Name = "Courtyard", Area = Box(45, 45, 55, 55, -1, 3) });
                plan.Spawns[0].Within = 1;
                ScatterOut o = Scatter.Resolve(plan, new FlatWalk(), new Rng(seed ^ Scatter.Salt), new List<V3>());
                moved += o.Moved;
                if (!plan.Placement[0].Area.Contains(plan.Spawns[0].Pos)) out_.Add(seed + ":" + plan.Spawns[0].Pos);
            }
            check("a scattered enemy never escapes its within area", out_.Count == 0, string.Join(" ", out_));
            check("and the area still lets most rolls move it", moved > Seeds.Length / 2, moved.ToString());
        }
        {   // "empty": no scattered enemy ends up inside an empty placement.
            var inside = new List<string>(); int moved = 0;
            foreach (long seed in Seeds)
            {
                MissionPlan plan = One(new V3(50, 0, 50), 10f);
                plan.Placement.Add(new PlacementPlan { Id = 2, Name = "Street", Mode = PlacementPlan.Empty, Area = Box(50, 0, 100, 100, -1, 3) });
                ScatterOut o = Scatter.Resolve(plan, new FlatWalk(), new Rng(seed ^ Scatter.Salt), new List<V3>());
                moved += o.Moved;
                if (o.Moved == 1 && plan.Placement[0].Area.Contains(plan.Spawns[0].Pos)) inside.Add(seed + ":" + plan.Spawns[0].Pos);
            }
            check("a scattered enemy never lands in an empty area", inside.Count == 0, string.Join(" ", inside));
            check("it still moves into the half that is allowed", moved == Seeds.Length, moved.ToString());
        }
        {   // Two floors joined by a ramp: an enemy on the deck stays on the deck, one on the ground under it stays on the ground.
            NavMesh nav = TwoFloors();
            var wrong = new List<string>(); int moved = 0;
            foreach (long seed in Seeds)
            {
                MissionPlan plan = One(new V3(85, 4, 60), 20f);
                plan.Spawns.Add(new SpawnPlan { Pos = new V3(60, 0, 60), Scatter = 10f });
                ScatterOut o = Scatter.Resolve(plan, new NavWalk(nav), new Rng(seed ^ Scatter.Salt), new List<V3>());
                moved += o.Moved;
                if (Math.Abs(plan.Spawns[0].Pos.Y - 4f) > Scatter.SameFloor) wrong.Add(seed + ":deck " + plan.Spawns[0].Pos);
                if (Math.Abs(plan.Spawns[1].Pos.Y) > Scatter.SameFloor) wrong.Add(seed + ":ground " + plan.Spawns[1].Pos);
            }
            check("a scattered enemy never changes floor", wrong.Count == 0, string.Join(" ", wrong));
            check("both enemies are moved on their own floor", moved == Seeds.Length * 2, moved.ToString());
        }
        {   // Two islands 20 m apart: a radius that reaches across never puts an enemy on the other island.
            var b = new MapBuilder();
            b.Floor(0, 0, 100, 100, 5, 0);
            b.Floor(120, 0, 260, 100, 5, 0);
            var walk = new NavWalk(b.Build());
            var across = new List<string>();
            foreach (long seed in Seeds)
            {
                MissionPlan plan = One(new V3(98, 0, 50), 30f);
                Scatter.Resolve(plan, walk, new Rng(seed ^ Scatter.Salt), new List<V3>());
                if (plan.Spawns[0].Pos.X > 100.01f) across.Add(seed + ":" + plan.Spawns[0].Pos);
            }
            check("a scattered enemy stays on the ground its authored place is connected to", across.Count == 0, string.Join(" ", across));
            check("the islands have different numbers", walk.Island(new V3(50, 0, 50)) != walk.Island(new V3(200, 0, 50)) && walk.Island(new V3(50, 0, 50)) >= 0, "");
            check("a point off the ground is on no island", walk.Island(new V3(110, 0, 50)) == -1, "");
        }
        {   // Spacing: crowded enemies all move and none ends within 2 m of another, scattered or not.
            var near = new List<string>(); int moved = 0;
            foreach (long seed in Seeds)
            {
                var plan = new MissionPlan();
                for (int i = 0; i < 8; i++) plan.Spawns.Add(new SpawnPlan { Pos = new V3(50 + i * 0.5f, 0, 50), Scatter = 8f });
                plan.Spawns.Add(new SpawnPlan { Pos = new V3(51, 0, 51) });
                ScatterOut o = Scatter.Resolve(plan, new FlatWalk(), new Rng(seed ^ Scatter.Salt), new List<V3>());
                moved += o.Moved;
                for (int i = 0; i < plan.Spawns.Count; i++)
                    for (int j = i + 1; j < plan.Spawns.Count; j++)
                        if (Dist(plan.Spawns[i].Pos, plan.Spawns[j].Pos) < Scatter.Spacing) near.Add(seed + ":" + i + "/" + j);
            }
            check("crowded enemies all find a spot", moved == Seeds.Length * 8, moved.ToString());
            check("no resolved enemy is within 2 m of another", near.Count == 0, string.Join(" ", near));
        }
        {   // Insertion clearance: never closer than the generator's 20 m to an insertion point.
            var close = new List<string>(); int moved = 0;
            foreach (long seed in Seeds)
            {
                MissionPlan plan = One(new V3(50, 0, 25), 15f);
                ScatterOut o = Scatter.Resolve(plan, new FlatWalk(), new Rng(seed ^ Scatter.Salt), new List<V3> { new V3(50, 0, 0) });
                moved += o.Moved;
                if (V3.DistXZ(plan.Spawns[0].Pos, new V3(50, 0, 0)) < 20f) close.Add(seed + ":" + plan.Spawns[0].Pos);
            }
            check("the insertion clearance is the generator's", Scatter.Clearance == new GeneratorInput().InfilHardClearance && Scatter.Clearance == 20f, Scatter.Clearance.ToString());
            check("no scattered enemy comes within 20 m of an insertion point", close.Count == 0, string.Join(" ", close));
            check("and it still moves", moved > Seeds.Length / 2, moved.ToString());
        }
        {   // No spot passes: "stay" keeps the authored place and names the constraint; "skip" leaves the enemy out.
            MissionPlan plan = Blocked(SpawnPlan.Stay, false);
            V3 at = plan.Spawns[0].Pos;
            ScatterOut o = Scatter.Resolve(plan, new FlatWalk(), new Rng(3 ^ Scatter.Salt), new List<V3>());
            check("a failed search with stay keeps the authored place", o.Stayed == 1 && plan.Spawns.Count == 1 && plan.Spawns[0].Pos.X == at.X && plan.Spawns[0].Pos.Z == at.Z, o.Notes.FirstOrDefault());
            check("its note names the spawn and the constraint that stopped it", o.Notes.Exists(n => n.StartsWith("spawns[0] 'guard'") && n.Contains("empty 24")), string.Join(" | ", o.Notes));

            MissionPlan skip = Blocked(SpawnPlan.Skip, false);
            ScatterOut s = Scatter.Resolve(skip, new FlatWalk(), new Rng(3 ^ Scatter.Salt), new List<V3>());
            check("a failed search with skip leaves the enemy out", s.Skipped == 1 && skip.Spawns.Count == 0, string.Join(" | ", s.Notes));
            check("and says so", s.Notes.Exists(n => n.Contains("left out") && n.Contains("empty")), string.Join(" | ", s.Notes));
        }
        {   // A target person or a gun truck is never left out, whatever the spawn says.
            MissionPlan hvt = Blocked(SpawnPlan.Skip, true);
            ScatterOut o = Scatter.Resolve(hvt, new FlatWalk(), new Rng(3 ^ Scatter.Salt), new List<V3>());
            check("a target person with skip stays instead", o.Skipped == 0 && o.Stayed == 1 && hvt.Spawns.Count == 1, string.Join(" | ", o.Notes));
            MissionPlan truck = Blocked(SpawnPlan.Skip, false); truck.Spawns[0].Vehicle = true;
            ScatterOut t = Scatter.Resolve(truck, new FlatWalk(), new Rng(3 ^ Scatter.Salt), new List<V3>());
            check("a gun truck with skip stays instead", t.Skipped == 0 && truck.Spawns.Count == 1, string.Join(" | ", t.Notes));
        }
        {   // A skip the extraction depends on is caught: the setup is unfinishable, so the mission is refused.
            MissionPlan plan = Blocked(SpawnPlan.Skip, false);
            plan.Spawns[0].Tag = "boss";
            plan.Triggers.Add(new TriggerPlan { Id = 1, When = TriggerPlan.OnDown, Tag = "boss", Count = 1 });
            plan.ExtractionAfter = 1;
            check("before scatter the mission can be finished", !Completion.Blocks(Completion.Check(plan, null, 0)), "");
            Scatter.Resolve(plan, new FlatWalk(), new Rng(3 ^ Scatter.Salt), new List<V3>());
            List<Finding> found = Completion.Check(plan, null, 0);
            check("after the skip Completion finds it unfinishable", Completion.Blocks(found), string.Join(" | ", found));
        }
        {   // Spawns without scatter are not touched and draw nothing.
            MissionPlan plan = One(new V3(50, 0, 50), 0f);
            var rng = new Rng(9);
            ScatterOut o = Scatter.Resolve(plan, new FlatWalk(), rng, new List<V3>());
            check("a plan without scatter is left as it is and draws nothing", o.Moved + o.Stayed + o.Skipped == 0 && plan.Spawns[0].Pos.X == 50f && rng.NextU64() == new Rng(9).NextU64(), "");
        }
        {   // A moved spawn keeps where the author put it (for a Game Master save); one that stayed keeps none; ToJson ignores it.
            MissionPlan plan = Open(); var authored = plan.Spawns.Select(s => s.Pos).ToList();
            Scatter.Resolve(plan, new FlatWalk(), new Rng(7 ^ Scatter.Salt), new List<V3>());
            bool kept = plan.Spawns.Select((s, i) => s.Authored.HasValue && s.Authored.Value.X == authored[i].X && s.Authored.Value.Y == authored[i].Y && s.Authored.Value.Z == authored[i].Z).All(b => b);
            check("Resolve keeps the authored place of every moved spawn", kept, string.Join(" ", plan.Spawns.Select(s => s.Authored.ToString())));
            string withAuthored = plan.ToJson();
            foreach (SpawnPlan s in plan.Spawns) s.Authored = null;
            check("ToJson of a resolved plan is unchanged by the authored places", withAuthored == plan.ToJson(), withAuthored);
            MissionPlan again = Open();
            Scatter.Resolve(again, new FlatWalk(), new Rng(7 ^ Scatter.Salt), new List<V3>());
            Scatter.Resolve(again, new FlatWalk(), new Rng(8 ^ Scatter.Salt), new List<V3>());
            check("a second resolve keeps the first authored place", again.Spawns[0].Authored.HasValue && again.Spawns[0].Authored.Value.X == 20f && again.Spawns[0].Authored.Value.Z == 20f, again.Spawns[0].Authored.ToString());
            MissionPlan stayed = Blocked(SpawnPlan.Stay, false);
            Scatter.Resolve(stayed, new FlatWalk(), new Rng(3 ^ Scatter.Salt), new List<V3>());
            check("a spawn that stayed has no other authored place", !stayed.Spawns[0].Authored.HasValue, "");
        }
        {   // The seed sweep behind "Try seeds": the Director's order per seed, counterexamples, and never a claim of every roll.
            string text = Sweepable().ToJson();
            ScatterSweep.WalkFor flat = (string nav, out string why) => { why = ""; return nav == "flat.bytes" ? new FlatWalk() : null; };
            string body = "{\"scenario\":" + Json(text) + ",\"seeds\":[1,2,3,4,5],\"players\":2,\"insertions\":[[0,0,0]]}";
            string answer = ScatterSweep.Answer(body, flat, out int status);
            check("the sweep reads the scenario and tries every seed", status == 200 && answer.Contains("\"readable\":true") && answer.Contains("\"seeds\":5"), answer);
            check("it says how many seeds were tried and that they are not every roll", answer.Contains("5 seeds tried with 2 players; not every possible roll"), answer);
            check("the same request gives the same answer", answer == ScatterSweep.Answer(body, flat, out _), "");
            check("a skip that makes the mission unfinishable is a counterexample", answer.Contains("\"kind\":\"skip-unfinishable\"") && answer.Contains("leaving out spawns[1] 'boss'"), answer);
            check("an enemy that stayed because no spot passed is a counterexample", answer.Contains("\"kind\":\"stayed\",\"spawn\":2"), answer);
            check("notes name spawns by their number in the file", answer.Contains("spawns[1] 'boss' is left out") && answer.Contains("spawns[2] 'guard' stays") && !answer.Contains("spawns[0] 'boss'"), answer);
            check("each seed is refused as the Director would refuse it", !answer.Contains("\"refused\":false"), answer);
            check("moved enemies are reported with their spot", answer.Contains("\"spots\":[{\"spawn\":3,\"pos\":["), answer);

            // The spot is the one the Director's order gives: read, options at their defaults, Scatter.Resolve with the seed's salt.
            var counters = new List<ScatterSweep.Counter>();
            ScatterSweep.Run run = ScatterSweep.Try(text, 4, 2, new FlatWalk(), new List<V3> { new V3(0, 0, 0) }, counters);
            ScenarioFile.Read(text, out MissionPlan by);
            Options.Apply(by, null);
            Scatter.Resolve(by, new FlatWalk(), new Rng(4 ^ Scatter.Salt), new List<V3> { new V3(0, 0, 0) });
            SpawnPlan moved = by.Spawns.Find(s => s.Scatter > 0 && s.Authored.HasValue);
            check("the sweep places an enemy where the Director's order would", run.Spots.Count == 1 && moved != null && run.Spots[0].Value.X == moved.Pos.X && run.Spots[0].Value.Z == moved.Pos.Z,
                  run.Spots.Count + " " + (moved == null ? "none" : moved.Pos.ToString()));
            check("one seed gives one moved, one stayed, one left out", run.Moved == 1 && run.Stayed == 1 && run.Skipped == 1 && counters.Count == 2, run.Moved + "/" + run.Stayed + "/" + run.Skipped + " " + counters.Count);

            ScatterSweep.Answer("{\"scenario\":" + Json(text) + "}", flat, out int noSeeds);
            check("a sweep without seeds is refused", noSeeds == 400, "");
            string unread = ScatterSweep.Answer("{\"scenario\":\"{nope\",\"seeds\":[1]}", flat, out int s2);
            check("an unreadable scenario is answered with its reading findings", s2 == 200 && unread.Contains("\"readable\":false"), unread);
            MissionPlan elsewhere = Sweepable(); elsewhere.NavFile = "other.bytes";
            string missing = ScatterSweep.Answer("{\"scenario\":" + Json(elsewhere.ToJson()) + ",\"seeds\":[1]}", (string nav, out string why) => { why = "the map file " + nav + " is not in the game's NavMeshData folder"; return null; }, out int s3);
            check("a map file that is not there is said, and no seed is tried", s3 == 404 && missing.Contains("other.bytes is not in the game's NavMeshData"), missing);
            MissionPlan still = Sweepable(); still.Spawns.ForEach(s => s.Scatter = 0f);
            string none = ScatterSweep.Answer("{\"scenario\":" + Json(still.ToJson()) + ",\"seeds\":3}", flat, out int s4);
            check("a scenario without scatter tries nothing and says so", s4 == 200 && none.Contains("\"scatter\":false"), none);
        }
    }

    // Three enemies with scatter on open ground, far apart.
    private static MissionPlan Open()
    {
        var plan = new MissionPlan();
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(20, 0, 20), Scatter = 6f });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(50, 0, 50), Scatter = 10f });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(80, 0, 30), Scatter = 3f });
        return plan;
    }

    private static MissionPlan One(V3 at, float scatter)
    {
        var plan = new MissionPlan();
        plan.Spawns.Add(new SpawnPlan { Pos = at, Scatter = scatter });
        return plan;
    }

    // One enemy whose whole scatter circle is an empty placement: no candidate can pass.
    private static MissionPlan Blocked(string fallback, bool hvt)
    {
        MissionPlan plan = One(new V3(50, 0, 50), 5f);
        plan.Spawns[0].Fallback = fallback; plan.Spawns[0].Hvt = hvt; plan.Spawns[0].Tag = "guard";
        plan.Placement.Add(new PlacementPlan { Id = 1, Name = "Kill zone", Mode = PlacementPlan.Empty, Area = Box(40, 40, 60, 60, -1, 3) });
        return plan;
    }

    private static MissionPlan Resolved(MissionPlan plan, long seed)
    {
        Scatter.Resolve(plan, new FlatWalk(), new Rng(seed ^ Scatter.Salt), new List<V3>());
        return plan;
    }

    private static string Positions(MissionPlan plan) => string.Join(" ", plan.Spawns.Select(s => s.Pos.X.ToString("0.00") + "," + s.Pos.Z.ToString("0.00")));

    private static float Dist(V3 a, V3 b) { float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z; return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz); }

    // On flat.bytes: an option leaves out spawn 0 by default (so file and resolved numbers differ); 'boss' (skip) and 'guard'
    // (stay) stand in an empty area that covers their whole circle; the extraction waits for 'boss'; spawn 3 scatters freely.
    private static MissionPlan Sweepable()
    {
        var plan = new MissionPlan { NavFile = "flat.bytes", ExtractionAfter = 1 };
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(20, 0, 80), Lot = 1 });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(50, 0, 50), Scatter = 5f, Fallback = SpawnPlan.Skip, Tag = "boss" });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(53, 0, 50), Scatter = 4f, Tag = "guard" });
        plan.Spawns.Add(new SpawnPlan { Pos = new V3(80, 0, 80), Scatter = 4f });
        plan.Placement.Add(new PlacementPlan { Id = 1, Name = "Kill zone", Mode = PlacementPlan.Empty, Area = Box(40, 40, 62, 60, -1, 3) });
        plan.Triggers.Add(new TriggerPlan { Id = 1, When = TriggerPlan.OnDown, Tag = "boss", Count = 1 });
        var few = new ChoicePlan { Name = "Few" };
        few.DropLots.Add(1);
        var o = new OptionPlan { Id = "size", Name = "Size" };
        o.Choices.Add(few); o.Choices.Add(new ChoicePlan { Name = "All" });
        plan.Options.Add(o);
        return plan;
    }

    private static string Json(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";

    private static AreaPlan Box(float x0, float z0, float x1, float z1, float bottom, float top)
    {
        var part = new AreaPart { Bottom = bottom, Top = top };
        part.Outline.Add(new P2(x0, z0)); part.Outline.Add(new P2(x1, z0)); part.Outline.Add(new P2(x1, z1)); part.Outline.Add(new P2(x0, z1));
        var area = new AreaPlan();
        area.Parts.Add(part);
        return area;
    }

    // The deck-and-ramp map of the generator's two-floor case: ground at 0, a deck at 4 m over part of it, a ramp between.
    private static NavMesh TwoFloors()
    {
        var b = new MapBuilder();
        b.Floor(0, 0, 160, 120, 5, 0, (x, z) => x >= 90 && x < 110 && z >= 50 && z < 70);
        b.Floor(30, 30, 90, 90, 5, 4);
        for (float x = 90; x < 110; x += 5)
            for (float z = 50; z < 70; z += 5)
                b.Quad(x, z, x + 5, z + 5, (px, pz) => 4f - (px - 90f) / 5f);
        return b.Build();
    }

    // Flat open ground at height 0 over 0..100 on both axes, all one island.
    private sealed class FlatWalk : IWalk
    {
        public bool Snap(V3 p, float maxRise, out V3 onGround)
        {
            onGround = new V3(p.X, 0f, p.Z);
            return p.X >= 0 && p.X <= 100 && p.Z >= 0 && p.Z <= 100 && Math.Abs(p.Y) <= maxRise;
        }

        public int Island(V3 p) => p.X >= 0 && p.X <= 100 && p.Z >= 0 && p.Z <= 100 && Math.Abs(p.Y) <= Scatter.SameFloor ? 0 : -1;
    }
}
