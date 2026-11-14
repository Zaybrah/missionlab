using System;
using System.Collections.Generic;

namespace MissionLab.Core
{
    public sealed class GeneratorInput
    {
        public NavMesh Nav;
        public long Seed;
        public string Scene;
        /// <summary>How many enemies the game is going to spawn.</summary>
        public int Enemies = 12;
        /// <summary>Where players can insert. Enemies are kept away from these.</summary>
        public List<V3> Infils = new List<V3>();
        /// <summary>The map's own enemy spawn points; used to find the playable part of the mesh.</summary>
        public List<V3> Authored = new List<V3>();
        /// <summary>No enemy within this distance of an insertion point if open ground lies between them.</summary>
        public float InfilClearance = 45f;
        /// <summary>No enemy this close to an insertion point, walls or not.</summary>
        public float InfilHardClearance = 20f;
        /// <summary>Patrol routes never come closer than this to an insertion point.</summary>
        public float PatrolInfilClearance = 30f;
        /// <summary>Rectangles (minX, maxX, minZ, maxZ) to keep clear, e.g. the extraction zone when insertion points are unknown.</summary>
        public List<float[]> AvoidBoxes = new List<float[]>();
        /// <summary>
        /// Spare spawn points beyond Enemies. The game spawns exactly Enemies bots from a
        /// random subset of the list, so every spare means one planned position goes unused.
        /// </summary>
        public int Spares = 1;
        /// <summary>Keep this far from the outer edge of the mesh (map borders are rarely playable).</summary>
        public float EdgeMargin = 15f;
        /// <summary>Radius of the play area around the map's own enemy positions; 0 = choose automatically.</summary>
        public float PlayRadius = 0f;
    }

    /// <summary>
    /// Turns a seed into an enemy layout: several groups spread over the reachable
    /// map, biased toward enclosed ground (rooms, compounds), with guards, wanderers
    /// and patrol routes. Pure code on plain data, so it runs identically in the
    /// game and in offline tools.
    /// </summary>
    public static class Generator
    {
        private const int Rays = 16;
        private const float RayMax = 14f;

        private sealed class Cand
        {
            public V3 P;
            public float Enc;          // 0..1, share of directions blocked within 7 m
            public float BestLen;      // longest free walk
            public double BestAngle;
            public int OpenRays;       // directions with at least 1 m of room
            public int Levels;
            public bool Used;
            public bool Indoor => Enc >= 0.6f || Levels > 1;
        }

        public static MissionPlan Generate(GeneratorInput input)
        {
            var nav = input.Nav;
            var rng = new Rng(input.Seed);
            var plan = new MissionPlan { Seed = input.Seed, Scene = input.Scene, NavFile = nav.SourceFile, RequestedEnemies = input.Enemies };

            // 1. Which part of the mesh can players actually get to?
            var seeds = new List<int>();
            foreach (var p in input.Infils) { int t = nav.TriAt(p.X, p.Z, p.Y, 2.5f, out _); if (t >= 0) seeds.Add(t); }
            int infilSeeds = seeds.Count;
            foreach (var p in input.Authored) { int t = nav.TriAt(p.X, p.Z, p.Y, 1.5f, out _); if (t >= 0) seeds.Add(t); }
            bool[] mask;
            if (seeds.Count > 0) mask = nav.Reachable(seeds);
            else { mask = LargestComponent(nav); plan.Notes.Add("no insertion or authored point lies on the mesh; using its largest connected part"); }
            plan.Notes.Add("reachability seeds: " + infilSeeds + " insertion, " + (seeds.Count - infilSeeds) + " authored");

            var tris = new List<int>();
            var cum = new List<double>();
            double area = 0;
            for (int i = 0; i < nav.Count; i++)
                if (mask[i] && nav.Area[i] >= 0.3f) { area += nav.Area[i]; tris.Add(i); cum.Add(area); }
            if (tris.Count == 0) { plan.Notes.Add("no usable walkable area"); return plan; }
            plan.Notes.Add("reachable area " + Math.Round(area) + " m2 in " + tris.Count + " triangles");

            int PickTri()
            {
                double r = rng.NextDouble() * area;
                int lo = 0, hi = cum.Count - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] > r) hi = mid; else lo = mid + 1; }
                return tris[lo];
            }

            // Play area: a disc around the map's own enemy positions, away from the mesh border.
            bool hasCentre = input.Authored.Count > 0;
            float pcx = 0, pcz = 0, playRadius = input.PlayRadius;
            if (hasCentre)
            {
                float lox = float.MaxValue, hix = float.MinValue, loz = float.MaxValue, hiz = float.MinValue;
                foreach (var a in input.Authored)
                {
                    pcx += a.X; pcz += a.Z;
                    lox = Math.Min(lox, a.X); hix = Math.Max(hix, a.X); loz = Math.Min(loz, a.Z); hiz = Math.Max(hiz, a.Z);
                }
                pcx /= input.Authored.Count; pcz /= input.Authored.Count;
                if (playRadius <= 0)
                {
                    float diag = (float)Math.Sqrt((hix - lox) * (hix - lox) + (hiz - loz) * (hiz - loz));
                    playRadius = Math.Max(60f, Math.Min(180f, 1.2f * diag + 35f));
                }
                plan.Notes.Add("play area: " + Math.Round(playRadius) + " m around (" + Math.Round(pcx) + ", " + Math.Round(pcz) + ")");
            }
            bool InPlay(V3 p)
            {
                if (p.X < nav.MinX + input.EdgeMargin || p.X > nav.MaxX - input.EdgeMargin) return false;
                if (p.Z < nav.MinZ + input.EdgeMargin || p.Z > nav.MaxZ - input.EdgeMargin) return false;
                if (!hasCentre) return true;
                float dx = p.X - pcx, dz = p.Z - pcz;
                return dx * dx + dz * dz <= playRadius * playRadius;
            }

            // Insertion points snapped onto the mesh, for the "open ground between" test.
            var infilOnMesh = new List<V3>();
            foreach (var f in input.Infils)
                if (nav.TriAt(f.X, f.Z, f.Y, 2.5f, out float fh, mask) >= 0) infilOnMesh.Add(new V3(f.X, fh, f.Z));
            bool Exposed(V3 p)
            {
                foreach (var b in input.AvoidBoxes)
                    if (p.X >= b[0] && p.X <= b[1] && p.Z >= b[2] && p.Z <= b[3]) return true;
                foreach (var f in input.Infils)
                    if (V3.DistXZ(f, p) < input.InfilHardClearance) return true;
                foreach (var f in infilOnMesh)
                {
                    float d = V3.DistXZ(f, p);
                    if (d >= input.InfilClearance) continue;
                    double ang = Math.Atan2(p.Z - f.Z, p.X - f.X);
                    if (nav.Walk(f, ang, d, mask, out _, 0.5f) >= d - 1.0f) return true;   // nothing in the way
                }
                return false;
            }

            bool PatrolBlocked(V3 p)
            {
                foreach (var f in input.Infils) if (V3.DistXZ(f, p) < input.PatrolInfilClearance) return true;
                return false;
            }

            // 2. Candidate positions, each scored for how enclosed it is.
            int want = Math.Max(400, Math.Min(1200, (int)(area / 12)));
            var cands = new List<Cand>(want);
            for (int n = 0; n < want * 6 && cands.Count < want; n++)
            {
                var p = nav.PointIn(PickTri(), rng);
                if (!InPlay(p) || Exposed(p)) continue;
                var c = Score(nav, p, mask);
                if (c.OpenRays < 4) continue;      // wedged against walls or clutter
                cands.Add(c);
            }
            if (cands.Count == 0) { plan.Notes.Add("no candidate positions away from the insertion points"); return plan; }

            // 3. Groups: how many, how big, and where.
            int total = input.Enemies + Math.Max(0, input.Spares);
            int groups = Math.Max(2, Math.Min(7, (int)Math.Round(input.Enemies / 3.5)));
            groups = Math.Max(1, Math.Min(groups, total / 2));
            var sizes = new int[groups];
            for (int g = 0; g < groups; g++) sizes[g] = 2;
            for (int left = total - 2 * groups, guard = 0; left > 0 && guard < 1000; guard++)
            {
                int g = rng.Range(0, groups);
                if (sizes[g] >= 6) continue;
                sizes[g]++; left--;
            }

            float radius = (float)Math.Sqrt(area / Math.PI);
            float sep = Math.Max(15f, Math.Min(70f, 0.25f * radius));
            var centres = new List<Cand>();
            var w = new double[cands.Count];
            for (int g = 0; g < groups; g++)
            {
                Cand pick = null;
                for (float s = sep; pick == null && s > 3f; s *= 0.7f)
                {
                    for (int i = 0; i < cands.Count; i++)
                    {
                        var c = cands[i];
                        bool ok = !c.Used;
                        for (int k = 0; ok && k < centres.Count; k++) if (V3.DistXZ(c.P, centres[k].P) < s) ok = false;
                        w[i] = ok ? 0.004 + c.Enc * c.Enc * c.Enc + (c.Levels > 1 ? 0.25 : 0) : 0;
                    }
                    int idx = rng.Weighted(w);
                    if (idx >= 0) pick = cands[idx];
                    // The first group is the strongpoint: take the most enclosed of several draws.
                    for (int extra = 0; g == 0 && idx >= 0 && extra < 24; extra++)
                    {
                        int alt = rng.Weighted(w);
                        if (alt >= 0 && cands[alt].Enc > pick.Enc) pick = cands[alt];
                    }
                }
                if (pick == null) break;
                pick.Used = true;
                centres.Add(pick);
            }
            plan.Groups = centres.Count;

            // 4. Members around each centre.
            var perGroup = new List<List<SpawnPlan>>();
            for (int g = 0; g < centres.Count; g++)
            {
                var centre = centres[g];
                var pool = new List<Cand>();
                foreach (var c in cands)
                    if (!c.Used && V3.DistXZ(c.P, centre.P) < 13f && Math.Abs(c.P.Y - centre.P.Y) < 4.5f) pool.Add(c);
                if (pool.Count < sizes[g] * 3)
                    foreach (var extra in LocalCandidates(nav, mask, centre.P, rng, 40))
                        if (InPlay(extra.P) && !Exposed(extra.P)) pool.Add(extra);

                var chosen = new List<Cand> { centre };
                var pw = new double[pool.Count];
                while (chosen.Count < sizes[g])
                {
                    for (int i = 0; i < pool.Count; i++)
                    {
                        var c = pool[i];
                        bool ok = !c.Used;
                        for (int k = 0; ok && k < chosen.Count; k++) if (Dist(c.P, chosen[k].P) < 1.8f) ok = false;
                        pw[i] = ok ? 0.2 + c.Enc : 0;
                    }
                    int idx = rng.Weighted(pw);
                    if (idx < 0) break;
                    pool[idx].Used = true;
                    chosen.Add(pool[idx]);
                }

                var list = new List<SpawnPlan>();
                foreach (var c in chosen) list.Add(MakeSpawn(nav, mask, c, g, rng, PatrolBlocked));
                perGroup.Add(list);
            }

            // Make sure the mission has at least one patrol if any position allows it.
            bool anyPatrol = false;
            foreach (var l in perGroup) foreach (var s in l) if (s.Role == SpawnRole.Patrol) anyPatrol = true;
            if (!anyPatrol)
            {
                SpawnPlan best = null; float bestLen = 8f;
                foreach (var l in perGroup)
                    foreach (var s in l)
                    {
                        if (s.Indoor) continue;
                        var route = PatrolRoute(nav, mask, s.Pos, rng, PatrolBlocked);
                        if (route.Count < 2) continue;
                        float len = V3.DistXZ(route[0], route[1]);
                        if (len > bestLen) { bestLen = len; best = s; best.Patrol = route; }
                    }
                if (best != null) { best.Role = SpawnRole.Patrol; best.PatrolWaitSeconds = rng.Range(2, 9); }
                foreach (var l in perGroup) foreach (var s in l) if (s.Role != SpawnRole.Patrol) s.Patrol.Clear();
            }

            // Interleave groups so any subset the game draws still covers every group.
            for (int k = 0; ; k++)
            {
                bool any = false;
                foreach (var l in perGroup) if (k < l.Count) { plan.Spawns.Add(l[k]); any = true; }
                if (!any) break;
            }
            if (plan.Spawns.Count < input.Enemies)
                plan.Notes.Add("only " + plan.Spawns.Count + " positions found for " + input.Enemies + " enemies");
            return plan;
        }

        private static float Dist(V3 a, V3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static Cand Score(NavMesh nav, V3 p, bool[] mask)
        {
            var c = new Cand { P = p, Levels = nav.LevelsAt(p.X, p.Z) };
            int blocked = 0;
            for (int k = 0; k < Rays; k++)
            {
                double a = 2 * Math.PI * k / Rays;
                float d = nav.Walk(p, a, RayMax, mask, out _);
                if (d < 7f) blocked++;
                if (d >= 1f) c.OpenRays++;
                if (d > c.BestLen) { c.BestLen = d; c.BestAngle = a; }
            }
            c.Enc = blocked / (float)Rays;
            return c;
        }

        private static List<Cand> LocalCandidates(NavMesh nav, bool[] mask, V3 centre, Rng rng, int count)
        {
            var near = new List<int>(); var cum = new List<double>(); double area = 0;
            for (int i = 0; i < nav.Count; i++)
            {
                if (!mask[i] || nav.Area[i] < 0.2f) continue;
                float cx = (nav.Ax[i] + nav.Bx[i] + nav.Cx[i]) / 3f, cz = (nav.Az[i] + nav.Bz[i] + nav.Cz[i]) / 3f, cy = (nav.Ay[i] + nav.By[i] + nav.Cy[i]) / 3f;
                float dx = cx - centre.X, dz = cz - centre.Z;
                if (dx * dx + dz * dz > 11f * 11f || Math.Abs(cy - centre.Y) > 4.5f) continue;
                area += nav.Area[i]; near.Add(i); cum.Add(area);
            }
            var res = new List<Cand>();
            if (near.Count == 0) return res;
            for (int n = 0; n < count * 3 && res.Count < count; n++)
            {
                double r = rng.NextDouble() * area;
                int lo = 0, hi = cum.Count - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] > r) hi = mid; else lo = mid + 1; }
                var p = nav.PointIn(near[lo], rng);
                var c = Score(nav, p, mask);
                if (c.OpenRays >= 4) res.Add(c);
            }
            return res;
        }

        private static SpawnPlan MakeSpawn(NavMesh nav, bool[] mask, Cand c, int group, Rng rng, Func<V3, bool> patrolBlocked)
        {
            var s = new SpawnPlan { Group = group, Pos = c.P, Indoor = c.Indoor };
            // Face the longest open line: usually the approach an enemy would watch.
            double a = c.BestAngle;
            s.Yaw = (float)(Math.Atan2(Math.Cos(a), Math.Sin(a)) * 180.0 / Math.PI);
            if (s.Yaw < 0) s.Yaw += 360f;

            double roll = rng.NextDouble();
            if (c.Indoor) s.Role = roll < 0.65 ? SpawnRole.Guard : SpawnRole.Wander;
            else s.Role = roll < 0.15 ? SpawnRole.Guard : roll < 0.70 ? SpawnRole.Wander : SpawnRole.Patrol;   // in the open, keep moving

            s.Defensive = rng.Chance(c.Indoor && s.Role == SpawnRole.Guard ? 0.6 : 0.3);
            s.DetectionRange = c.Indoor ? 25f : 45f;
            s.HearingRange = c.Indoor ? 20f : 25f;
            s.Fov = c.Indoor ? 100f : 120f;
            s.WanderDistance = c.Indoor ? 6 : 14;
            s.PatrolWaitSeconds = rng.Range(2, 9);
            if (s.Role == SpawnRole.Patrol)
            {
                s.Patrol = PatrolRoute(nav, mask, c.P, rng, patrolBlocked);
                if (s.Patrol.Count < 2) { s.Role = SpawnRole.Wander; s.Patrol.Clear(); }
            }
            return s;
        }

        /// <summary>
        /// A route of straight, walkable legs: start, out along the longest free line,
        /// then a second leg in another direction. The game loops it.
        /// </summary>
        private static List<V3> PatrolRoute(NavMesh nav, bool[] mask, V3 start, Rng rng, Func<V3, bool> blocked)
        {
            var route = new List<V3>();
            if (blocked(start)) return route;
            double a1 = 0; float best = 0;
            for (int k = 0; k < Rays; k++)
            {
                double a = 2 * Math.PI * k / Rays;
                float d = nav.Walk(start, a, 30f, mask, out _);
                if (d > best) { best = d; a1 = a; }
            }
            if (best < 6f) return route;
            nav.Walk(start, a1, best * rng.Range(0.7f, 0.9f), mask, out V3 wp1);
            if (blocked(wp1)) return route;             // would walk toward the players' start
            route.Add(start);
            route.Add(wp1);

            double a2 = 0; float best2 = 0;
            for (int k = 0; k < Rays; k++)
            {
                double a = 2 * Math.PI * k / Rays;
                double back = Math.Abs(Math.IEEERemainder(a - (a1 + Math.PI), 2 * Math.PI));
                if (back < Math.PI / 3) continue;          // do not simply walk back
                float d = nav.Walk(wp1, a, 25f, mask, out _);
                if (d > best2) { best2 = d; a2 = a; }
            }
            if (best2 >= 5f)
            {
                nav.Walk(wp1, a2, best2 * rng.Range(0.7f, 0.9f), mask, out V3 wp2);
                if (!blocked(wp2)) route.Add(wp2);
            }
            return route;
        }

        public static bool[] LargestComponent(NavMesh nav)
        {
            var comp = new int[nav.Count];
            for (int i = 0; i < comp.Length; i++) comp[i] = -1;
            int bestId = -1; double bestArea = -1; int id = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < nav.Count; i++)
            {
                if (comp[i] >= 0) continue;
                double a = 0;
                comp[i] = id; stack.Push(i);
                while (stack.Count > 0)
                {
                    int t = stack.Pop(); a += nav.Area[t];
                    foreach (int nb in nav.Neighbours[t]) if (comp[nb] < 0) { comp[nb] = id; stack.Push(nb); }
                }
                if (a > bestArea) { bestArea = a; bestId = id; }
                id++;
            }
            var mask = new bool[nav.Count];
            for (int i = 0; i < nav.Count; i++) mask[i] = comp[i] == bestId;
            return mask;
        }
    }
}
