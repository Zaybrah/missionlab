using System.Collections.Generic;

namespace MissionLab.Core
{
    /// <summary>
    /// Small deterministic generator (SplitMix64). The same seed gives the same
    /// mission in the game plugin and in any offline tool, on any .NET runtime.
    /// </summary>
    public sealed class Rng
    {
        private ulong _s;

        public Rng(long seed)
        {
            // Hash the seed first. Seeding with seed * gamma would make seed N+1 the
            // same stream as seed N shifted by one draw, and nearby seeds would give
            // nearly identical missions.
            unchecked
            {
                ulong z = (ulong)seed ^ 0xD1B54A32D192ED03UL;
                z = (z ^ (z >> 33)) * 0xFF51AFD7ED558CCDUL;
                z = (z ^ (z >> 33)) * 0xC4CEB9FE1A85EC53UL;
                _s = z ^ (z >> 33);
            }
        }

        public ulong NextU64()
        {
            unchecked
            {
                ulong z = (_s += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Uniform integer in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextDouble() * (maxExclusive - min));
        }

        public float Range(float a, float b) => a + (float)NextDouble() * (b - a);

        public bool Chance(double p) => NextDouble() < p;

        /// <summary>Index drawn proportionally to the weights, or -1 if all weights are zero.</summary>
        public int Weighted(IList<double> weights)
        {
            double total = 0;
            for (int i = 0; i < weights.Count; i++) if (weights[i] > 0) total += weights[i];
            if (total <= 0) return -1;
            double r = NextDouble() * total;
            int last = -1;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0) continue;
                last = i;
                r -= weights[i];
                if (r < 0) return i;
            }
            return last;
        }
    }
}
