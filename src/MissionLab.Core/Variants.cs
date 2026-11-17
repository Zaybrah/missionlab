using System.Collections.Generic;

namespace MissionLab.Core
{
    /// <summary>
    /// The parts of a hand-made scenario that differ from mission to mission: enemies, squads, doors, things and
    /// triggers that are only there some of the time, either/or sets of which one option is there, and enemies that
    /// only come when enough players do. Settled once, when the mission starts.
    /// </summary>
    public static class Variants
    {
        public static bool Any(MissionPlan plan) =>
            plan.Spawns.Exists(s => s.Chance < 100 || s.Either > 0 || s.MinPlayers > 1) || plan.Doors.Exists(d => d.Chance < 100) ||
            plan.Things.Exists(t => t.Chance < 100) || plan.Triggers.Exists(t => t.Chance < 100);

        public static string Letter(int option) => option >= 1 && option <= 26 ? ((char)('A' + option - 1)).ToString() : option.ToString();

        /// <summary>What a roll took out of the plan for this mission. A save puts it back.</summary>
        public sealed class Out
        {
            public readonly List<SpawnPlan> Spawns = new List<SpawnPlan>();
            public readonly List<int> SpawnIndex = new List<int>();          // where each of those stood in the scenario's own list
            public readonly List<DoorPlan> Doors = new List<DoorPlan>();
            public readonly List<ThingPlan> Things = new List<ThingPlan>();
            public int Count => Spawns.Count + Doors.Count + Things.Count;
        }

        public static List<SpawnPlan> Resolve(MissionPlan plan, Rng rng, List<string> notes) => Resolve(plan, rng, notes, 0).Spawns;

        /// <summary>
        /// Takes what loses its roll out of the plan and returns it. Triggers that lose stay in the plan, switched off.
        /// players: how many are in the mission (0: not known, so nobody is left out for want of players).
        /// </summary>
        public static Out Resolve(MissionPlan plan, Rng rng, List<string> notes, int players)
        {
            var gone = new Out();
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

            // Chance: one roll for every lot, one for every enemy that is in no lot. Too few players: not there.
            var lots = new Dictionary<int, bool>();
            int rolled = 0, lost = 0, few = 0;
            int place = -1;
            foreach (SpawnPlan s in plan.Spawns)
            {
                place++;
                bool stays = s.Either <= 0 || chosen[s.Either] == s.Option;
                if (stays && players > 0 && s.MinPlayers > players) { stays = false; few++; }
                if (stays && s.Chance < 100)
                {
                    if (s.Lot > 0)
                    {
                        if (!lots.TryGetValue(s.Lot, out bool win)) { win = rng.Range(0, 100) < s.Chance; lots[s.Lot] = win; rolled++; if (!win) lost++; }
                        stays = win;
                    }
                    else { stays = rng.Range(0, 100) < s.Chance; rolled++; if (!stays) lost++; }
                }
                if (!stays) { gone.Spawns.Add(s); gone.SpawnIndex.Add(place); }
            }
            foreach (DoorPlan d in plan.Doors)
                if (d.Chance < 100) { rolled++; if (rng.Range(0, 100) >= d.Chance) { lost++; gone.Doors.Add(d); } }
            foreach (ThingPlan t in plan.Things)
                if (t.Chance < 100) { rolled++; if (rng.Range(0, 100) >= t.Chance) { lost++; gone.Things.Add(t); } }
            int off = 0;
            foreach (TriggerPlan t in plan.Triggers)
            {
                t.Off = false;
                if (t.Chance < 100) { rolled++; if (rng.Range(0, 100) >= t.Chance) { lost++; off++; t.Off = true; } }
            }
            if (rolled > 0) notes?.Add("chance: " + (rolled - lost) + " of " + rolled + " rolls came up");
            if (off > 0) notes?.Add(off + (off == 1 ? " trigger sits" : " triggers sit") + " this one out");
            if (few > 0) notes?.Add(few + (few == 1 ? " enemy needs" : " enemies need") + " more players than the " + players + " there");
            if (gone.Spawns.Count > 0) { var set = new HashSet<SpawnPlan>(gone.Spawns); plan.Spawns.RemoveAll(set.Contains); }
            if (gone.Doors.Count > 0) { var set = new HashSet<DoorPlan>(gone.Doors); plan.Doors.RemoveAll(set.Contains); }
            if (gone.Things.Count > 0) { var set = new HashSet<ThingPlan>(gone.Things); plan.Things.RemoveAll(set.Contains); }
            return gone;
        }
    }
}
