using System;
using System.Collections.Generic;

namespace MissionLab.Core
{
    /// <summary>What a random layout gets on top of its positions, as far as it needs nothing from the game itself.</summary>
    public static class Extras
    {
        private static bool FarFromInsertions(SpawnPlan s, List<V3> infils, float min)
        {
            foreach (V3 p in infils) if (V3.DistXZ(p, s.Pos) < min) return false;
            return true;
        }

        // ------------------------------------------------------------------ enemies that come later
        /// <summary>
        /// Holds some enemies back: a few come once the enemy is alerted, a few when players walk into the busiest building.
        /// alert and ambush say which of the two to make (a random layout makes both).
        /// </summary>
        public static void Waves(MissionPlan plan, Rng rng, List<V3> infils, List<string> notes, bool alert = true, bool ambush = true)
        {
            int n = plan.Spawns.Count;
            if (n < 6) return;
            float cx = 0, cz = 0;
            foreach (SpawnPlan s in plan.Spawns) { cx += s.Pos.X; cz += s.Pos.Z; }
            var centre = new V3(cx / n, 0, cz / n);
            int budget = Math.Max(1, n / 3);                          // at most a third of the enemies come later

            // 1. Alerted: the outermost enemies out of doors are not there at first; they come once the shooting starts.
            int want = Math.Max(1, Math.Min(4, (int)Math.Round(n * 0.15)));
            var outer = plan.Spawns.FindAll(s => !s.Indoor && s.Trigger == 0 && !s.Hvt && FarFromInsertions(s, infils, 35f));
            outer.Sort((a, b) => V3.DistXZ(b.Pos, centre).CompareTo(V3.DistXZ(a.Pos, centre)));
            int took = 0;
            for (int i = 0; alert && i < outer.Count && took < want && budget > 0; i++, took++, budget--)
            {
                outer[i].Trigger = 1; outer[i].Role = SpawnRole.Wander; outer[i].Defensive = false; outer[i].Patrol.Clear();
            }
            if (took > 0)
            {
                int wait = 5 * rng.Range(3, 8);                        // 15 to 35 seconds
                plan.Triggers.Add(new TriggerPlan { Id = 1, When = TriggerPlan.OnAlert, Name = "Alerted", Delay = wait });
                notes.Add(took + (took == 1 ? " more enemy comes " : " more enemies come ") + wait + " s after the enemy is alerted");
            }

            // 2. Ambush: walking into the busiest building brings in enemies from near it.
            var indoor = new Dictionary<int, List<SpawnPlan>>();
            foreach (SpawnPlan s in plan.Spawns)
            {
                if (!s.Indoor || s.Trigger != 0) continue;
                if (!indoor.TryGetValue(s.Group, out List<SpawnPlan> list)) indoor[s.Group] = list = new List<SpawnPlan>();
                list.Add(s);
            }
            List<SpawnPlan> house = null;
            foreach (List<SpawnPlan> list in indoor.Values) if (list.Count >= 2 && (house == null || list.Count > house.Count)) house = list;
            if (!ambush || house == null || budget <= 0) return;
            float hx = 0, hz = 0;
            foreach (SpawnPlan s in house) { hx += s.Pos.X; hz += s.Pos.Z; }
            hx /= house.Count; hz /= house.Count;
            SpawnPlan floor = house[0];
            foreach (SpawnPlan s in house) if (V3.DistXZ(s.Pos, new V3(hx, 0, hz)) < V3.DistXZ(floor.Pos, new V3(hx, 0, hz))) floor = s;
            var spot = new V3(hx, floor.Pos.Y, hz);                    // on the floor of whoever stands nearest the middle
            var near = plan.Spawns.FindAll(s => s.Trigger == 0 && !s.Hvt && !house.Contains(s) && V3.DistXZ(s.Pos, spot) > 12f && FarFromInsertions(s, infils, 30f));
            near.Sort((a, b) => V3.DistXZ(a.Pos, spot).CompareTo(V3.DistXZ(b.Pos, spot)));
            want = Math.Max(1, Math.Min(3, (int)Math.Round(n * 0.1)));
            took = 0;
            for (int i = 0; i < near.Count && took < want && budget > 0; i++, took++, budget--)
            {
                near[i].Trigger = 2; near[i].Defensive = false;
                if (near[i].Role == SpawnRole.Patrol) { near[i].Role = SpawnRole.Wander; near[i].Patrol.Clear(); }
            }
            if (took > 0)
            {
                plan.Triggers.Add(new TriggerPlan { Id = 2, When = TriggerPlan.OnZone, Name = "Ambush", Pos = spot, Radius = 7f });
                notes.Add(took + (took == 1 ? " more enemy comes" : " more enemies come") + " when players reach " + spot);
            }
        }

        // ------------------------------------------------------------------ doors
        /// <summary>
        /// Locks a few of the map's doors: those the game locks itself (Locked = true in the list) are left to it; of the others,
        /// about one in seven is locked, three at most. Draws once per door considered, in list order.
        /// </summary>
        public static void LockDoors(MissionPlan plan, List<DoorPlan> doors, Rng rng, List<string> notes)
        {
            int locked = 0;
            foreach (DoorPlan d in doors)
            {
                if (d.Locked == true || locked >= 3 || !rng.Chance(0.15)) continue;
                plan.Doors.Add(new DoorPlan { Name = d.Name, Pos = d.Pos, Locked = true });
                locked++;
            }
            if (locked > 0) notes.Add(locked + (locked == 1 ? " door" : " doors") + " locked");
        }
    }
}
