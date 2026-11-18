using System;
using System.Collections.Generic;
using System.Globalization;

namespace MissionLab.Core
{
    /// <summary>The walkable ground as scatter asks about it: the navmesh in the game (NavWalk), a made-up one in tests.</summary>
    public interface IWalk
    {
        /// <summary>The walkable surface under p closest to p's height, at most maxRise above or below it. False: none there.</summary>
        bool Snap(V3 p, float maxRise, out V3 onGround);

        /// <summary>Which connected part of the ground p stands on; -1 when p is off the ground.</summary>
        int Island(V3 p);
    }

    /// <summary>What scatter did: how many enemies moved, stayed at their authored place or were left out, and why.</summary>
    public sealed class ScatterOut
    {
        public List<string> Notes = new List<string>();
        public int Moved, Stayed, Skipped;
    }

    /// <summary>
    /// Constrained scatter (ML-023): an enemy with a scatter radius is placed at random within that distance of its authored
    /// place when the mission starts, on the same floor and the same connected ground, inside its "within" placement, out of
    /// every "empty" placement, clear of the other enemies and of the insertion points. When no spot passes, it stays where
    /// the author put it or is left out, as the spawn says; a target person or a gun truck is never left out.
    /// </summary>
    public static class Scatter
    {
        public const long Salt = 0x5CA77E;            // fixed: new Rng(seed ^ Salt) gives the same placement for the same seed
        public const int Attempts = 24;
        public const float SameFloor = 1.5f;           // metres up or down from the authored height: further is another floor
        public const float Spacing = 2f;               // metres from every other enemy
        public static readonly float Clearance = new GeneratorInput().InfilHardClearance;    // the generator's own "never this close to an insertion"

        // The constraints in the order they are asked; a note names the ones that stopped a spawn.
        public const string Floor = "floor", Island = "island", Within = "within", Empty = "empty", Spaced = "spacing", Insertion = "insertion";
        private static readonly string[] Order = { Floor, Island, Within, Empty, Spaced, Insertion };

        public static bool Any(MissionPlan plan) => plan != null && plan.Spawns.Exists(s => s.Scatter > 0f);

        /// <summary>
        /// Moves every spawn with a scatter radius, in list order. rng: new Rng(seed ^ Salt). Every scattered spawn draws exactly
        /// Attempts candidates (two draws each: angle, then radius r * sqrt(u)) whether or not an early one passes, so one spawn's
        /// luck never shifts the next one's candidates. The first candidate that passes is used. Skipped spawns are taken out of
        /// the plan, so Completion.Check sees a skip that would make the mission unfinishable. A moved spawn keeps where the
        /// author put it in Authored, so a Game Master save writes the authored place back rather than this roll's.
        /// </summary>
        public static ScatterOut Resolve(MissionPlan plan, IWalk walk, Rng rng, List<V3> insertions)
        {
            var result = new ScatterOut();
            if (plan == null || walk == null || rng == null || !Any(plan)) return result;
            var inv = CultureInfo.InvariantCulture;

            // Where every enemy stands so far: the authored place, replaced by the resolved one once a spawn is settled.
            var at = new List<V3>(plan.Spawns.Count);
            foreach (SpawnPlan s in plan.Spawns) at.Add(s.Pos);
            var skipped = new HashSet<SpawnPlan>();
            var xs = new double[Attempts];
            var zs = new double[Attempts];

            for (int i = 0; i < plan.Spawns.Count; i++)
            {
                SpawnPlan s = plan.Spawns[i];
                if (s.Scatter <= 0f) continue;
                float radius = Math.Min(s.Scatter, SpawnPlan.ScatterLimit);
                for (int a = 0; a < Attempts; a++)
                {
                    double angle = rng.NextDouble() * 2.0 * Math.PI;
                    double r = radius * Math.Sqrt(rng.NextDouble());
                    xs[a] = s.Pos.X + r * Math.Cos(angle);
                    zs[a] = s.Pos.Z + r * Math.Sin(angle);
                }

                PlacementPlan inside = null;
                if (s.Within > 0) inside = plan.Placement.Find(p => p.Id == s.Within && p.Mode == PlacementPlan.Allow && p.Area != null && p.Area.Parts.Count > 0);
                int home = walk.Island(s.Pos);
                var stopped = new int[Order.Length];
                bool found = false; V3 spot = s.Pos;
                for (int a = 0; a < Attempts && !found; a++)
                {
                    int why = Judge(plan, walk, s, i, inside, home, new V3((float)xs[a], s.Pos.Y, (float)zs[a]), at, skipped, insertions, out V3 ground);
                    if (why < 0) { found = true; spot = ground; }
                    else stopped[why]++;
                }

                string name = "spawns[" + i + "]" + (string.IsNullOrEmpty(s.Tag) ? "" : " '" + s.Tag + "'");
                if (found)
                {
                    if (!s.Authored.HasValue) s.Authored = s.Pos;
                    s.Pos = spot; at[i] = spot; result.Moved++;
                    continue;
                }
                var reasons = new List<string>();
                for (int k = 0; k < Order.Length; k++) if (stopped[k] > 0) reasons.Add(Order[k] + " " + stopped[k]);
                string because = "no spot passed in " + Attempts + " tries (" + string.Join(", ", reasons) + ")";
                bool required = s.Hvt || s.Vehicle;
                if (s.Fallback == SpawnPlan.Skip && !required)
                {
                    skipped.Add(s); result.Skipped++;
                    result.Notes.Add(name + " is left out: " + because);
                }
                else
                {
                    result.Stayed++;
                    result.Notes.Add(name + " stays at its authored place (" + s.Pos.X.ToString("0.#", inv) + ", " + s.Pos.Z.ToString("0.#", inv) + "): " + because +
                                     (s.Fallback == SpawnPlan.Skip ? "; a required target is never left out" : ""));
                }
            }
            if (skipped.Count > 0) plan.Spawns.RemoveAll(skipped.Contains);
            result.Notes.Add("scatter: " + result.Moved + " moved, " + result.Stayed + " stayed, " + result.Skipped + " left out");
            return result;
        }

        // -1 when the candidate passes, otherwise the index in Order of the first constraint it fails.
        private static int Judge(MissionPlan plan, IWalk walk, SpawnPlan s, int self, PlacementPlan inside, int home, V3 candidate,
                                 List<V3> at, HashSet<SpawnPlan> skipped, List<V3> insertions, out V3 ground)
        {
            if (!walk.Snap(candidate, SameFloor, out ground) || Math.Abs(ground.Y - s.Pos.Y) > SameFloor) return 0;
            if (home < 0 || walk.Island(ground) != home) return 1;
            if (s.Within > 0 && (inside == null || !inside.Area.Contains(ground))) return 2;
            foreach (PlacementPlan p in plan.Placement)
                if (p.Mode == PlacementPlan.Empty && p.Area != null && p.Area.Parts.Count > 0 && p.Area.Contains(ground)) return 3;
            for (int j = 0; j < at.Count; j++)
            {
                // Spawns later in the list that scatter are not settled yet: their authored place is not where they will stand.
                if (j == self || skipped.Contains(plan.Spawns[j]) || (j > self && plan.Spawns[j].Scatter > 0f)) continue;
                float dx = at[j].X - ground.X, dy = at[j].Y - ground.Y, dz = at[j].Z - ground.Z;
                if (dx * dx + dy * dy + dz * dz < Spacing * Spacing) return 4;
            }
            if (insertions != null)
                foreach (V3 p in insertions) if (V3.DistXZ(p, ground) < Clearance) return 5;
            return -1;
        }
    }
}
