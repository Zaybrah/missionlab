using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// Generator checks on small maps made in memory (ML-010): disconnected areas, stacked floors and insertion clearance.
/// The maps are written in the navmesh tile layout the game ships and read back through NavMesh.Parse, so no map file is
/// needed. Each map is generated with a few seeds, and every rule is asked of every position and patrol point.
/// </summary>
static class SyntheticMaps
{
    private static readonly long[] Seeds = { 1, 2, 3 };
    private const float OnSurface = 0.05f;

    public static void Run(Action<string, bool, string> check)
    {
        {   // Two islands: the generator only uses the part that the insertion point can reach, even when the other part is larger.
            var b = new MapBuilder();
            b.Floor(0, 0, 100, 100, 5, 0);                      // island A, with the insertion point
            b.Floor(120, 0, 260, 100, 5, 0);                    // island B, 20 m away and larger, with no way across
            NavMesh nav = b.Build();
            double areaA = 0, areaB = 0;
            for (int i = 0; i < nav.Count; i++) { if (nav.Ax[i] < 110) areaA += nav.Area[i]; else areaB += nav.Area[i]; }
            check("the cut-off island is the larger part of the map", areaB > areaA, areaA + " against " + areaB);
            check("the two islands are not joined", !nav.Reachable(new[] { 0 }).Where((reached, i) => reached && nav.Ax[i] > 110).Any(), "");

            var off = new List<string>(); int placed = int.MaxValue; bool seeded = true;
            foreach (long seed in Seeds)
            {
                var input = new GeneratorInput { Nav = nav, Seed = seed, Scene = "islands" };
                input.Infils.Add(new V3(10, 0, 50));
                MissionPlan plan = Generator.Generate(input);
                placed = Math.Min(placed, plan.Spawns.Count);
                seeded &= plan.Notes.Contains("reachability seeds: 1 insertion, 0 authored");
                foreach (V3 p in Points(plan)) if (p.X > 100.01f) off.Add(seed + ":" + p);
            }
            check("the insertion point on island A seeds what is reachable", seeded, "");
            check("no enemy and no patrol point is placed on the island the insertion point cannot reach", off.Count == 0, string.Join(" ", off));
            check("island A still gets its enemies", placed >= 12, placed + " placed at least");

            var wrong = new List<string>(); bool fellBack = true; placed = int.MaxValue;
            foreach (long seed in Seeds)
            {
                MissionPlan plan = Generator.Generate(new GeneratorInput { Nav = nav, Seed = seed, Scene = "islands" });
                placed = Math.Min(placed, plan.Spawns.Count);
                fellBack &= plan.Notes.Exists(n => n.Contains("largest connected part"));
                foreach (V3 p in Points(plan)) if (p.X < 119.99f) wrong.Add(seed + ":" + p);
            }
            check("with no insertion or authored point on the mesh the generator says it uses the largest part", fellBack, "");
            check("and then it places everything on the larger island only", wrong.Count == 0 && placed >= 12, placed + " placed at least; " + string.Join(" ", wrong));
        }
        {   // Two floors: a deck 4 m above open ground, reached by a ramp. Positions stay on a floor's surface, never between floors.
            var b = new MapBuilder();
            b.Floor(0, 0, 160, 120, 5, 0, (x, z) => x >= 90 && x < 110 && z >= 50 && z < 70);   // ground, open where the ramp stands
            b.Floor(30, 30, 90, 90, 5, 4);                                                    // the deck
            for (float x = 90; x < 110; x += 5)
                for (float z = 50; z < 70; z += 5)
                    b.Quad(x, z, x + 5, z + 5, (px, pz) => 4f - (px - 90f) / 5f);              // the ramp: 4 m down over 20 m
            NavMesh nav = b.Build();
            int deckTri = nav.TriAt(60, 60, 4, 0.1f, out _), groundTri = nav.TriAt(150, 60, 0, 0.1f, out _);
            check("the ramp joins the deck to the ground", deckTri >= 0 && groundTri >= 0 && nav.Reachable(new[] { groundTri })[deckTri], "");
            check("under the deck the map has two levels", nav.LevelsAt(60, 60) == 2 && nav.LevelsAt(150, 60) == 1, "");

            var floating = new List<string>(); var between = new List<string>(); var outdoors = new List<string>();
            int onDeck = 0, underDeck = 0, placed = int.MaxValue;
            foreach (long seed in Seeds)
            {
                var input = new GeneratorInput { Nav = nav, Seed = seed, Scene = "floors" };
                input.Infils.Add(new V3(150, 0, 60));
                MissionPlan plan = Generator.Generate(input);
                placed = Math.Min(placed, plan.Spawns.Count);
                foreach (V3 p in Points(plan))
                {
                    if (nav.TriAt(p.X, p.Z, p.Y, OnSurface, out _) < 0) floating.Add(seed + ":" + p);
                    bool overDeck = p.X > 30 && p.X < 90 && p.Z > 30 && p.Z < 90;
                    if (overDeck && Math.Abs(p.Y) > OnSurface && Math.Abs(p.Y - 4f) > OnSurface) between.Add(seed + ":" + p);
                }
                foreach (SpawnPlan s in plan.Spawns)
                {
                    bool overDeck = s.Pos.X > 30 && s.Pos.X < 90 && s.Pos.Z > 30 && s.Pos.Z < 90;
                    if (!overDeck) continue;
                    if (!s.Indoor) outdoors.Add(seed + ":" + s.Pos);
                    if (Math.Abs(s.Pos.Y - 4f) <= OnSurface) onDeck++; else underDeck++;
                }
            }
            check("every position and patrol point lies on a walkable surface, at its height", floating.Count == 0, string.Join(" ", floating));
            check("where the floors overlap nothing is placed between them", between.Count == 0, string.Join(" ", between));
            check("a position on or under the deck counts as indoors (more than one level there)", outdoors.Count == 0, string.Join(" ", outdoors));
            check("the map is a fair test: enemies stand on the deck and on the ground below it", onDeck > 0 && underDeck > 0, onDeck + " on the deck, " + underDeck + " below");
            check("every seed places its enemies", placed >= 12, placed + " placed at least");
        }
        {   // Insertion clearance: 20 m kept clear whatever is in between, 45 m over open ground, 30 m for patrol routes, and kept-clear boxes.
            var b = new MapBuilder();
            b.Floor(0, 0, 160, 120, 5, 0, (x, z) => x >= 30 && x < 35 && z >= 15 && z < 105);   // a wall 10 m from the insertion point, open at both ends
            NavMesh nav = b.Build();
            var infil = new V3(20, 0, 60);
            var box = new float[] { 100, 145, 15, 50 };
            check("the walled map is one connected part", nav.Reachable(new[] { 0 }).All(r => r), "");

            var hard = new List<string>(); var open = new List<string>(); var patrol = new List<string>(); var boxed = new List<string>();
            int placed = int.MaxValue;
            foreach (long seed in Seeds)
            {
                var input = new GeneratorInput { Nav = nav, Seed = seed, Scene = "clearance" };
                input.Infils.Add(infil);
                input.AvoidBoxes.Add(box);
                MissionPlan plan = Generator.Generate(input);
                placed = Math.Min(placed, plan.Spawns.Count);
                foreach (SpawnPlan s in plan.Spawns)
                {
                    float d = V3.DistXZ(infil, s.Pos);
                    if (d < input.InfilHardClearance) hard.Add(seed + ":" + s.Pos);
                    // Within 45 m only a position whose straight line from the insertion point is cut may stay (the generator's own test).
                    if (d < input.InfilClearance && nav.Walk(infil, Math.Atan2(s.Pos.Z - infil.Z, s.Pos.X - infil.X), d, null, out _, 0.5f) >= d - 1f) open.Add(seed + ":" + s.Pos);
                    if (s.Pos.X >= box[0] && s.Pos.X <= box[1] && s.Pos.Z >= box[2] && s.Pos.Z <= box[3]) boxed.Add(seed + ":" + s.Pos);
                    foreach (V3 q in s.Patrol) if (V3.DistXZ(infil, q) < input.PatrolInfilClearance) patrol.Add(seed + ":" + q);
                }
            }
            check("no enemy within 20 m of the insertion point, wall or not", hard.Count == 0, string.Join(" ", hard));
            check("no enemy within 45 m of the insertion point with open ground between", open.Count == 0, string.Join(" ", open));
            check("no patrol point within 30 m of the insertion point", patrol.Count == 0, string.Join(" ", patrol));
            check("no enemy in a kept-clear box", boxed.Count == 0, string.Join(" ", boxed));
            check("every seed places its enemies", placed >= 12, placed + " placed at least");
        }
    }

    // Every place a plan puts someone: the spawn positions and their patrol points.
    private static IEnumerable<V3> Points(MissionPlan plan)
    {
        foreach (SpawnPlan s in plan.Spawns)
        {
            yield return s.Pos;
            foreach (V3 q in s.Patrol) yield return q;
        }
    }
}

/// <summary>
/// Writes a one-tile navmesh in the layout NavMesh.Parse reads (tile header, triangles, Int3 millimetre vertices, an empty
/// graph-space copy, no pre-cut copy, blank node records). Corners at the same spot are shared, so triangles that meet
/// at an edge are neighbours; surfaces at different heights never share corners and stay apart unless a ramp joins them.
/// If the tile layout ever changes, this writer changes with the three parsers.
/// </summary>
sealed class MapBuilder
{
    private readonly List<int> _verts = new List<int>();
    private readonly List<int> _tris = new List<int>();
    private readonly Dictionary<(int, int, int), int> _index = new Dictionary<(int, int, int), int>();

    private int Vertex(float x, float y, float z)
    {
        var key = ((int)Math.Round(x * 1000f), (int)Math.Round(y * 1000f), (int)Math.Round(z * 1000f));
        if (_index.TryGetValue(key, out int i)) return i;
        i = _verts.Count / 3;
        _verts.Add(key.Item1); _verts.Add(key.Item2); _verts.Add(key.Item3);
        _index[key] = i;
        return i;
    }

    /// <summary>One rectangle as two triangles; height(x, z) gives each corner's height.</summary>
    public void Quad(float x0, float z0, float x1, float z1, Func<float, float, float> height)
    {
        int a = Vertex(x0, height(x0, z0), z0), b = Vertex(x1, height(x1, z0), z0);
        int c = Vertex(x1, height(x1, z1), z1), d = Vertex(x0, height(x0, z1), z1);
        _tris.Add(a); _tris.Add(b); _tris.Add(c);
        _tris.Add(a); _tris.Add(c); _tris.Add(d);
    }

    /// <summary>A flat grid of cell-sized squares at height y; hole(x, z) leaves out the square whose low corner is (x, z).</summary>
    public void Floor(float x0, float z0, float x1, float z1, float cell, float y, Func<float, float, bool> hole = null)
    {
        for (float x = x0; x < x1; x += cell)
            for (float z = z0; z < z1; z += cell)
                if (hole == null || !hole(x, z)) Quad(x, z, x + cell, z + cell, (px, pz) => y);
    }

    public NavMesh Build()
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            int nodes = _tris.Count / 3;
            w.Write(1); w.Write(1);                     // tiles across, tiles deep
            w.Write(0); w.Write(0);                     // this tile's own slot
            w.Write(1); w.Write(1);                     // tile width, depth
            w.Write(_tris.Count); foreach (int t in _tris) w.Write(t);
            w.Write(_verts.Count / 3); foreach (int v in _verts) w.Write(v);
            w.Write(0);                                 // no graph-space copy of the vertices
            w.Write((byte)0);                           // no pre-cut copy of the tile
            w.Write(nodes); w.Write(new byte[20 * nodes]);
            w.Flush();
            return NavMesh.Parse(ms.ToArray());
        }
    }
}
