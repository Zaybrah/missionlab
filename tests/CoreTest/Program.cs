using System.Diagnostics;
using System.Text.Json;
using MissionLab.Core;

// usage: CoreTest <navmesh.bytes> <recon.json|-> <outDir> <enemies> <seed>... [--infil x,y,z]...
var navPath = args[0]; var reconPath = args[1]; var outDir = args[2]; int enemies = int.Parse(args[3]);
var seeds = new List<long>(); var infils = new List<V3>();
for (int i = 4; i < args.Length; i++)
{
    if (args[i] == "--infil") { var p = args[++i].Split(','); infils.Add(new V3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]))); }
    else seeds.Add(long.Parse(args[i]));
}
var sw = Stopwatch.StartNew();
var nav = NavMesh.Load(navPath);
Console.WriteLine($"loaded {nav.SourceFile}: {nav.Count} triangles, tiles {nav.TilesX}x{nav.TilesZ}, x[{nav.MinX:0},{nav.MaxX:0}] z[{nav.MinZ:0},{nav.MaxZ:0}] in {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"bounds {(nav.HasBounds ? "center " + nav.BoundsCenter + " size " + nav.BoundsSize : "MISSING")}; ReadBounds ok={NavMesh.ReadBounds(navPath, out var rbc, out var rbs)} {rbc} {rbs}");

var authored = new List<V3>();
string scene = "test";
if (reconPath != "-")
{
    using var doc = JsonDocument.Parse(File.ReadAllText(reconPath));
    scene = doc.RootElement.GetProperty("scene").GetString();
    foreach (var s in doc.RootElement.GetProperty("raid").GetProperty("botSpawnPoints").EnumerateArray())
    {
        if (s.ValueKind != JsonValueKind.Object) continue;
        var p = s.GetProperty("pos");
        authored.Add(new V3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()));
    }
    if (infils.Count == 0)
        foreach (var m in doc.RootElement.GetProperty("infiltration").GetProperty("infilMethods").EnumerateArray())
        {
            if (!m.TryGetProperty("infilObject", out var o) || o.ValueKind != JsonValueKind.Object) continue;
            var p = o.GetProperty("pos");
            var v = new V3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
            if (Math.Abs(v.X) + Math.Abs(v.Z) > 0.01f) infils.Add(v);
            if (m.TryGetProperty("children", out var kids))
                foreach (var c in kids.EnumerateArray())
                {
                    var cp = c.GetProperty("pos");
                    var cv = new V3(cp[0].GetSingle(), cp[1].GetSingle(), cp[2].GetSingle());
                    if (Math.Abs(cv.X) + Math.Abs(cv.Y) + Math.Abs(cv.Z) > 0.01f && !infils.Any(q => Math.Abs(q.X - cv.X) + Math.Abs(q.Y - cv.Y) + Math.Abs(q.Z - cv.Z) < 1f)) infils.Add(cv);
                }
        }
}
int on = authored.Count(p => nav.TriAt(p.X, p.Z, p.Y, 1.5f, out _) >= 0);
Console.WriteLine($"scene '{scene}': {authored.Count} authored points, {on} on this mesh; {infils.Count} insertion points");

// connectivity summary
var all = nav.Reachable(authored.Select(p => nav.TriAt(p.X, p.Z, p.Y, 1.5f, out _)).Where(t => t >= 0));
double reach = 0, total = 0; for (int i = 0; i < nav.Count; i++) { total += nav.Area[i]; if (all[i]) reach += nav.Area[i]; }
Console.WriteLine($"reachable from authored points: {reach:0} of {total:0} m2 ({100 * reach / total:0.0}%)");

Directory.CreateDirectory(outDir);
File.WriteAllText(Path.Combine(outDir, "reachable.txt"), string.Join("\n", Enumerable.Range(0, nav.Count).Select(i =>
    FormattableString.Invariant($"{(all[i] ? 1 : 0)} {nav.Ax[i]} {nav.Az[i]} {nav.Bx[i]} {nav.Bz[i]} {nav.Cx[i]} {nav.Cz[i]} {nav.Ay[i]}"))));
int failures = 0;
var seenPlans = new HashSet<string>();
foreach (var seed in seeds)
{
    GeneratorInput In() => new GeneratorInput { Nav = nav, Seed = seed, Scene = scene, Enemies = enemies, Infils = infils, Authored = authored };
    sw.Restart();
    var plan = Generator.Generate(In());
    long ms = sw.ElapsedMilliseconds;
    var json = plan.ToJson();
    if (Generator.Generate(In()).ToJson() != json) { Console.WriteLine("  FAIL: not deterministic"); failures++; }
    JsonDocument.Parse(json).Dispose();   // must be valid JSON
    string body = json.Substring(json.IndexOf("\"spawns\""));
    if (!seenPlans.Add(body)) { Console.WriteLine("  FAIL: same layout as an earlier seed"); failures++; }
    File.WriteAllText(Path.Combine(outDir, $"plan_seed{seed}.json"), json);

    var seedTris = infils.Select(p => nav.TriAt(p.X, p.Z, p.Y, 2.5f, out _)).Concat(authored.Select(p => nav.TriAt(p.X, p.Z, p.Y, 1.5f, out _))).Where(t => t >= 0).ToList();
    var mask = seedTris.Count > 0 ? nav.Reachable(seedTris) : Generator.LargestComponent(nav);
    int offMesh = 0, unreachable = 0, nearInfil = 0, tooClose = 0, badLeg = 0;
    for (int i = 0; i < plan.Spawns.Count; i++)
    {
        var s = plan.Spawns[i];
        int t = nav.TriAt(s.Pos.X, s.Pos.Z, s.Pos.Y, 0.1f, out _);
        if (t < 0) offMesh++; else if (!mask[t]) unreachable++;
        if (infils.Any(f => V3.DistXZ(f, s.Pos) < 20f)) nearInfil++;
        if (s.Patrol.Any(w => infils.Any(f => V3.DistXZ(f, w) < 30f))) nearInfil++;
        foreach (var f in infils)
        {
            float d = V3.DistXZ(f, s.Pos);
            if (d >= 45f || nav.TriAt(f.X, f.Z, f.Y, 2.5f, out float fh, mask) < 0) continue;
            if (nav.Walk(new V3(f.X, fh, f.Z), Math.Atan2(s.Pos.Z - f.Z, s.Pos.X - f.X), d, mask, out _, 0.5f) >= d - 1.0f) nearInfil++;
        }
        for (int k = i + 1; k < plan.Spawns.Count; k++)
        {
            var o = plan.Spawns[k].Pos;
            if (Math.Sqrt(Math.Pow(o.X - s.Pos.X, 2) + Math.Pow(o.Y - s.Pos.Y, 2) + Math.Pow(o.Z - s.Pos.Z, 2)) < 1.79) tooClose++;
        }
        for (int k = 0; k + 1 < s.Patrol.Count; k++)
        {
            var a = s.Patrol[k]; var b = s.Patrol[k + 1];
            double ang = Math.Atan2(b.Z - a.Z, b.X - a.X); float len = V3.DistXZ(a, b);
            if (nav.Walk(a, ang, len, mask, out _) < len - 0.7f) badLeg++;
        }
        if (s.Role == SpawnRole.Patrol && s.Patrol.Count < 2) badLeg++;
    }
    int bad = offMesh + unreachable + nearInfil + tooClose + badLeg + (plan.Spawns.Count < enemies ? 1 : 0);
    failures += bad;
    var roles = string.Join(" ", plan.Spawns.GroupBy(s => s.Role).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"));
    Console.WriteLine($"  seed {seed}: {plan.Spawns.Count} spawns in {plan.Groups} groups, indoor {plan.Spawns.Count(s => s.Indoor)}, {roles}, {ms} ms" +
        (bad == 0 ? "  OK" : $"  PROBLEMS offMesh={offMesh} unreachable={unreachable} nearInfil={nearInfil} tooClose={tooClose} badPatrolLeg={badLeg}"));
    foreach (var n in plan.Notes.Skip(seed == seeds[0] ? 0 : 99)) Console.WriteLine("     note: " + n);
}
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} PROBLEM(S)");
return failures == 0 ? 0 : 1;
