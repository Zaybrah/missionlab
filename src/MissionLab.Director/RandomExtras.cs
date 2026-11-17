using System;
using System.Collections.Generic;
using MissionLab.Core;
using UnityEngine;

namespace MissionLab
{
    /// <summary>
    /// What a random layout gets on top of its enemy positions, so that two missions differ in more than where
    /// people stand: a wave that comes when the enemy is alerted, an ambush where players walk into a building,
    /// a few doors locked, and (only with RandomIntel) the intel laid somewhere else. Everything is rolled from the mission's seed and
    /// written into the plan, so a mission record replays the same.
    /// </summary>
    internal static class RandomExtras
    {
        public static void Add(MissionPlan plan, RaidManager raid, NavMesh nav, Rng rng, List<V3> infils)
        {
            var notes = new List<string>();
            try { Extras.Waves(plan, rng, infils, notes); } catch (Exception e) { notes.Add("waves failed (" + e.Message + ")"); }
            try { Doors(plan, rng, notes); } catch (Exception e) { notes.Add("doors failed (" + e.Message + ")"); }
            // Moved intel is not yet confirmed on a client without the mod, so it waits for its own setting (off by default).
            if (DirectorPlugin.RandomIntel.Value)
            {
                try { Intel(plan, raid, nav, rng, notes); } catch (Exception e) { notes.Add("intel failed (" + e.Message + ")"); }
            }
            else notes.Add("intel left in its place (RandomIntel is off)");
            plan.Notes.Add("extras: " + (notes.Count > 0 ? string.Join("; ", notes) : "none fitted this layout"));
        }

        // ------------------------------------------------------------------ doors
        private static List<DoorV2> AllDoors()
        {
            var list = new List<DoorV2>();
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<DoorV2>(), false);
            for (int i = 0; found != null && i < found.Length; i++)
            {
                DoorV2 d = found[i] == null ? null : found[i].TryCast<DoorV2>();
                if (d != null) list.Add(d);
            }
            return list;
        }

        private static V3 HingeOf(DoorV2 d) { Vector3 h = GameMaster.Hinge(d); return new V3(h.x, h.y, h.z); }

        /// <summary>Doors the game itself may lock are left to it; of the others, about one in seven is locked, three at most.</summary>
        private static void Doors(MissionPlan plan, Rng rng, List<string> notes)
        {
            int locked = 0;
            foreach (DoorV2 d in AllDoors())
            {
                bool own = false;
                try { own = d.StartLocked; } catch { }
                if (own || locked >= 3 || !rng.Chance(0.15)) continue;
                plan.Doors.Add(new DoorPlan { Name = d.gameObject.name, Pos = HingeOf(d), Locked = true });
                locked++;
            }
            if (locked > 0) notes.Add(locked + (locked == 1 ? " door" : " doors") + " locked");
        }

        // ------------------------------------------------------------------ intel
        private static bool Down(Vector3 from, float reach, out RaycastHit hit) =>
            Physics.Raycast(from, Vector3.down, out hit, reach, ~0, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// A place to lay something down: a level surface at table height above a walkable spot, under a roof,
        /// wide enough to hold the item and with room above it. Found by probing the map's colliders.
        /// </summary>
        private static bool TableTop(V3 p, out Vector3 top)
        {
            top = Vector3.zero;
            var feet = new Vector3(p.X, p.Y, p.Z);
            if (!Down(feet + Vector3.up * 1.6f, 1.7f, out RaycastHit hit)) return false;
            float rise = hit.point.y - p.Y;
            if (rise < 0.5f || rise > 1.2f || hit.normal.y < 0.97f) return false;
            string layer = LayerMask.LayerToName(hit.collider.gameObject.layer);
            if (layer == "PlayerBlock" || layer == "AI_VisionBlock" || layer == "Water") return false;
            // Wide enough: the same height a hand's breadth away on four sides.
            foreach (Vector3 off in new[] { new Vector3(0.12f, 0, 0), new Vector3(-0.12f, 0, 0), new Vector3(0, 0, 0.12f), new Vector3(0, 0, -0.12f) })
                if (!Down(feet + off + Vector3.up * 1.6f, 1.7f, out RaycastHit side) || Mathf.Abs(side.point.y - hit.point.y) > 0.03f) return false;
            // Room above it, so it does not end up inside a shelf, and a roof somewhere over the floor.
            if (Physics.Raycast(hit.point + Vector3.up * 0.03f, Vector3.up, 0.45f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (!Physics.Raycast(feet + Vector3.up * 0.2f, Vector3.up, 6f, ~0, QueryTriggerInteraction.Ignore)) return false;
            top = hit.point + Vector3.up * 0.02f;
            return true;
        }

        private static void Intel(MissionPlan plan, RaidManager raid, NavMesh nav, Rng rng, List<string> notes)
        {
            var sets = raid.objectives;
            if (sets == null || nav == null) return;
            int moved = 0;
            var used = new List<Vector3>();
            for (int i = 0; i < sets.Length; i++)
            {
                var items = sets[i] == null ? null : sets[i].objectives;
                ObjectiveObject any = null;
                for (int k = 0; items != null && k < items.Length && any == null; k++) if (items[k] != null && items[k].gameObject.activeInHierarchy) any = items[k];
                if (any == null) continue;
                Vector3 home = any.transform.position;
                // Walkable triangles in the same building, more or less: within 14 m and a storey up or down.
                var near = new List<int>();
                for (int t = 0; t < nav.Count; t++)
                {
                    float x = (nav.Ax[t] + nav.Bx[t] + nav.Cx[t]) / 3f, y = (nav.Ay[t] + nav.By[t] + nav.Cy[t]) / 3f, z = (nav.Az[t] + nav.Bz[t] + nav.Cz[t]) / 3f;
                    if ((x - home.x) * (x - home.x) + (z - home.z) * (z - home.z) < 14f * 14f && Mathf.Abs(y - home.y) < 4.5f) near.Add(t);
                }
                if (near.Count == 0) continue;
                for (int tries = 0; tries < 500; tries++)
                {
                    V3 p = nav.PointIn(near[rng.Range(0, near.Count)], rng);
                    if (!TableTop(p, out Vector3 top)) continue;
                    if ((top - home).sqrMagnitude < 1f) continue;                       // somewhere else, not the same table
                    if (used.Exists(u => (u - top).sqrMagnitude < 0.36f)) continue;      // not on top of another item
                    used.Add(top);
                    plan.Objectives.Add(new ObjectivePlan { Name = sets[i].name, Pos = new V3(top.x, top.y, top.z), Yaw = rng.Range(0f, 360f) });
                    moved++;
                    break;
                }
            }
            if (moved > 0) notes.Add(moved + (moved == 1 ? " intel item" : " intel items") + " laid somewhere else");

            // Taking the intel may shut a door nearby.
            if (moved == 0 || !rng.Chance(0.5)) return;
            DoorV2 pick = null; float best = 12f * 12f;
            foreach (DoorV2 d in AllDoors())
            {
                V3 h = HingeOf(d);
                if (plan.Doors.Exists(x => V3.DistXZ(x.Pos, h) < 0.75f && Math.Abs(x.Pos.Y - h.Y) < 0.75f)) continue;
                float sq = (GameMaster.Hinge(d) - used[0]).sqrMagnitude;
                if (sq < best) { best = sq; pick = d; }
            }
            if (pick == null) return;
            int id = 1;
            foreach (TriggerPlan t in plan.Triggers) id = Math.Max(id, t.Id + 1);
            var trap = new TriggerPlan { Id = id, When = TriggerPlan.OnIntel, Name = "Intel taken", Item = "" };
            trap.Doors.Add(new DoorPlan { Name = pick.gameObject.name, Pos = HingeOf(pick), Locked = true });
            plan.Triggers.Add(trap);
            notes.Add("a door near the intel locks when an item is taken");
        }
    }
}
