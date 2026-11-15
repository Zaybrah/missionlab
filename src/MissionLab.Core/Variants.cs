using System.Collections.Generic;

namespace MissionLab.Core
{
    /// <summary>
    /// The parts of a hand-made scenario that are left to chance: an enemy or a squad that is only there some of the
    /// time, and either/or sets of which one option is there. Rolled once, when the mission starts.
    /// </summary>
    public static class Variants
    {
        public static bool Any(MissionPlan plan) => plan.Spawns.Exists(s => s.Chance < 100 || s.Either > 0);

        public static string Letter(int option) => option >= 1 && option <= 26 ? ((char)('A' + option - 1)).ToString() : option.ToString();

        /// <summary>Takes the enemies that lose their roll out of the plan and returns them, in the order they stood in.</summary>
        public static List<SpawnPlan> Resolve(MissionPlan plan, Rng rng, List<string> notes)
        {
            var gone = new List<SpawnPlan>();
            if (!Any(plan)) return gone;

            // Either/or: one option of every set.
            var sets = new SortedDictionary<int, SortedSet<int>>();
            foreach (SpawnPlan s in plan.Spawns)
            {
                if (s.Either <= 0) continue;
                if (!sets.TryGetValue(s.Either, out SortedSet<int> options)) sets[s.Either] = options = new SortedSet<int>();
                options.Add(s.Option);
            }
            var chosen = new Dictionary<int, int>();
            foreach (KeyValuePair<int, SortedSet<int>> set in sets)
            {
                var options = new List<int>(set.Value);
                int pick = options[rng.Range(0, options.Count)];
                chosen[set.Key] = pick;
                notes?.Add("either/or " + set.Key + ": option " + Letter(pick) + " of " + options.Count);
            }

            // Chance: one roll for every lot, one for every enemy that is in no lot.
            var lots = new Dictionary<int, bool>();
            int rolled = 0, lost = 0;
            foreach (SpawnPlan s in plan.Spawns)
            {
                bool stays = s.Either <= 0 || chosen[s.Either] == s.Option;
                if (stays && s.Chance < 100)
                {
                    if (s.Lot > 0)
                    {
                        if (!lots.TryGetValue(s.Lot, out bool win)) { win = rng.Range(0, 100) < s.Chance; lots[s.Lot] = win; rolled++; if (!win) lost++; }
                        stays = win;
                    }
                    else { stays = rng.Range(0, 100) < s.Chance; rolled++; if (!stays) lost++; }
                }
                if (!stays) gone.Add(s);
            }
            if (rolled > 0) notes?.Add("chance: " + (rolled - lost) + " of " + rolled + " rolls came up");
            if (gone.Count > 0)
            {
                var set = new HashSet<SpawnPlan>(gone);
                plan.Spawns.RemoveAll(set.Contains);
            }
            return gone;
        }
    }
}
