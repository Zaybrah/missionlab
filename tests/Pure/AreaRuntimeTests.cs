using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// Painted areas in a running mission (ML-027 D): a live shape is checked with the file rules before it is taken, a refused
/// shape keeps the trigger's old place and its progress, a good one drops only unfinished progress, the circle rule is
/// unchanged, and the engine with the shared membership rule treats seams, holes, gaps and "everyone" as authored.
/// </summary>
static class AreaRuntimeTests
{
    /// <summary>Players standing where the script puts them; asked exactly as the plugin asks (GameMaster.PlayerInside/EveryoneInside).</summary>
    private sealed class Walkers : ITriggerWorld
    {
        public readonly List<V3> Alive = new List<V3>();
        public bool Alerted() => false;
        public bool IntelTaken(string item) => false;
        public bool Down(int count, string tag) => false;
        public bool DoorOpen(V3 hinge) => false;
        public bool Occupied(TriggerPlan t, out V3 where)
        {
            where = default;
            if (t.Everyone)
            {
                foreach (V3 p in Alive) { if (!Membership.Inside(t, p)) return false; where = p; }
                return Alive.Count > 0;
            }
            foreach (V3 p in Alive) if (Membership.Inside(t, p)) { where = p; return true; }
            return false;
        }
        public int Inside(TriggerPlan t, out int alive) { alive = Alive.Count; return Alive.Count(p => Membership.Inside(t, p)); }
    }

    private static string Square(float x0, float z0, float x1, float z1, float bottom, float top) =>
        "{\"floor\":[" + bottom + "," + top + "],\"outline\":[[" + x0 + "," + z0 + "],[" + x1 + "," + z0 + "],[" + x1 + "," + z1 + "],[" + x0 + "," + z1 + "]]}";

    private static string Area(params string[] parts) => "{\"parts\":[" + string.Join(",", parts) + "]}";

    private const string Bowtie = "{\"parts\":[{\"floor\":[0,3],\"outline\":[[0,0],[4,4],[4,0],[0,4]]}]}";

    private static TriggerEngine Engine(List<TriggerPlan> triggers)
    {
        var e = new TriggerEngine();
        e.Reset(0f);
        e.Sync(triggers);
        return e;
    }

    /// <summary>What Reconcile does with the page's list: the strict check on the text, the lenient reading, then the shapes taken over.</summary>
    private static List<KeyValuePair<int, string>> Live(string triggersJson, List<TriggerPlan> was, out List<TriggerPlan> now)
    {
        using var doc = JsonDocument.Parse(triggersJson);
        Dictionary<int, string> refused = AreaEdits.Refused(doc.RootElement);
        now = MissionPlan.FromJson("{\"triggers\":" + triggersJson + ",\"spawns\":[]}").Triggers;
        return AreaEdits.Apply(now, was, refused);
    }

    public static void Run(Action<string, bool, string> check)
    {
        {   // The page's list is checked with the file rules; only zones and holds count, and only the first finding of each.
            string list = "[{\"id\":1,\"when\":\"hold\",\"pos\":[0,0,0],\"hold\":5,\"area\":" + Area(Square(0, 0, 10, 10, 0, 3)) + "}," +
                          "{\"id\":2,\"when\":\"zone\",\"pos\":[0,0,0],\"area\":" + Bowtie + "}," +
                          "{\"id\":3,\"when\":\"zone\",\"pos\":[0,0,0],\"radius\":5}," +
                          "{\"id\":4,\"when\":\"time\",\"delay\":5,\"area\":\"nonsense\"}," +
                          "{\"id\":5,\"when\":\"zone\",\"pos\":[0,0,0],\"area\":{\"parts\":[]}}," +
                          "{\"id\":6,\"when\":\"zone\",\"pos\":[0,0,0],\"area\":null}]";
            using var doc = JsonDocument.Parse(list);
            Dictionary<int, string> refused = AreaEdits.Refused(doc.RootElement);
            check("live: only the broken shapes are refused", string.Join(",", refused.Keys.OrderBy(k => k)) == "2,5", string.Join(",", refused.Keys));
            check("live: a crossing outline is named by its path", refused.TryGetValue(2, out string two) && two.StartsWith("triggers[1].area.parts[0].outline: "), two);
            check("live: an empty area is named by its path", refused.TryGetValue(5, out string five) && five.StartsWith("triggers[4].area.parts: "), five);
        }

        {   // A refused shape keeps the old place and the hold under way; a new trigger with a broken shape is not added;
            // a trigger that was another kind stays as it was. A good new shape drops only the unfinished progress.
            var was = MissionPlan.FromJson("{\"triggers\":[" +
                "{\"id\":1,\"when\":\"hold\",\"pos\":[5,0,5],\"hold\":30,\"area\":" + Area(Square(0, 0, 10, 10, 0, 3)) + "}," +
                "{\"id\":2,\"when\":\"zone\",\"pos\":[5,0,5],\"repeat\":60,\"area\":" + Area(Square(0, 0, 10, 10, 0, 3)) + "}," +
                "{\"id\":6,\"when\":\"time\",\"delay\":600}],\"spawns\":[]}").Triggers;
            AreaEdits.Prepare(was);
            var e = Engine(was); var w = new Walkers(); w.Alive.Add(new V3(5, 1, 5));
            e.Step(0, w); e.Step(4, w);
            check("live: before the edit the hold runs and the zone has fired", e.State(1).InsideSince == 0f && e.Fires(2) == 1 && e.State(2).Latched, "");
            string oldShape = was[0].Area.Signature();

            List<KeyValuePair<int, string>> said = Live("[" +
                "{\"id\":1,\"when\":\"hold\",\"pos\":[50,0,50],\"radius\":3,\"hold\":30,\"area\":" + Bowtie + "}," +
                "{\"id\":2,\"when\":\"zone\",\"pos\":[5,0,5],\"repeat\":60,\"area\":" + Area(Square(0, 0, 10, 10, 0, 3)) + "}," +
                "{\"id\":6,\"when\":\"zone\",\"pos\":[0,0,0],\"area\":" + Bowtie + "}," +
                "{\"id\":7,\"when\":\"zone\",\"pos\":[0,0,0],\"area\":" + Bowtie + "}]", was, out List<TriggerPlan> now);
            check("live: one failed line per refused shape, in list order", string.Join(",", said.Select(s => s.Key)) == "1,6,7", string.Join(" | ", said.Select(s => s.Key + " " + s.Value)));
            check("live: the line says the rule and what was kept", said.Count == 3 && said[0].Value.Contains("triggers[0].area.parts[0].outline") && said[0].Value.EndsWith("it keeps the place it had")
                                                                    && said[1].Value.EndsWith("it stays as it was") && said[2].Value.EndsWith("it was not added"), said.Count > 0 ? said[0].Value : "");
            TriggerPlan one = now.Find(t => t.Id == 1);
            check("live: the refused trigger keeps its anchor, radius and area", one != null && one.Pos.X == 5 && one.Pos.Z == 5 && one.Radius == was[0].Radius && one.Area.Signature() == oldShape, "");
            check("live: the trigger that was a timer is still that timer", now.Exists(t => t.Id == 6 && t.When == TriggerPlan.OnTime && t.Delay == 600), "");
            check("live: the new trigger with a broken shape is left out", !now.Exists(t => t.Id == 7), "");
            e.Sync(now);
            check("live: after a refused shape the hold still runs from where it was", e.State(1).InsideSince == 0f, "");

            said = Live("[" +
                "{\"id\":1,\"when\":\"hold\",\"pos\":[5,0,5],\"hold\":30,\"area\":" + Area(Square(0, 0, 20, 10, 0, 3)) + "}," +
                "{\"id\":2,\"when\":\"zone\",\"pos\":[5,0,5],\"repeat\":60,\"area\":" + Area(Square(0, 0, 20, 10, 0, 3)) + "}]", now, out List<TriggerPlan> after);
            e.Sync(after);
            check("live: a good shape is one change with nothing refused", said.Count == 0 && after[0].Area.Signature() != oldShape, "");
            check("live: the new shape drops the hold under way", e.State(1).InsideSince == null && e.State(1).Fires == 0, "");
            check("live: the new shape keeps what has fired", e.Fires(2) == 1 && e.State(2).Latched && e.State(2).LastFiredAt == 0f && e.State(2).RearmAt == 60f, "");
            e.Step(5, w);
            check("live: the hold starts again in the new shape", e.State(1).InsideSince == 5f, "");
        }

        {   // Bounds are worked out once by Prepare: an outline changed in place with the same number of parts answers by its new
            // shape only after it, which is why the plugin prepares every list it reads.
            var t = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"zone\",\"pos\":[0,0,0],\"area\":" + Area(Square(0, 0, 10, 10, 0, 3)) + "}],\"spawns\":[]}").Triggers;
            var far = new V3(15, 1, 5);
            check("prepare: outside before the change", !Membership.Inside(t[0], far), "");
            t[0].Area.Parts[0].Outline = new List<P2> { new P2(0, 0), new P2(20, 0), new P2(20, 10), new P2(0, 10) };
            AreaEdits.Prepare(t);
            check("prepare: inside the changed outline once prepared", Membership.Inside(t[0], far), "");
        }

        {   // The old circle: the same answer as the two checks GameMaster had, on and around every edge.
            var c = new TriggerPlan { Id = 1, When = TriggerPlan.OnZone, Pos = new V3(3, 10, -2), Radius = 5 };
            int differ = 0, n = 0;
            foreach (float y in new[] { 7.5f, 8f, 9f, 10f, 12f, 12.01f })
                for (float x = -3f; x <= 9f; x += 0.25f)
                    for (float z = -8f; z <= 4f; z += 0.25f)
                    {
                        n++;
                        float dx = x - c.Pos.X, dz = z - c.Pos.Z;
                        bool old = dx * dx + dz * dz <= c.Radius * c.Radius && Math.Abs(y - c.Pos.Y) <= 2f;
                        if (old != Membership.Inside(c, new V3(x, y, z))) differ++;
                    }
            check("circle: membership answers as the old check did at " + n + " points", differ == 0, differ + " differ");
        }

        {   // A hold over a house of two touching parts: walking across the seam keeps the count; a real gap starts it again.
            var house = MissionPlan.FromJson("{\"triggers\":[" +
                "{\"id\":1,\"when\":\"hold\",\"pos\":[5,0,5],\"hold\":5,\"area\":" + Area(Square(0, 0, 10, 10, 0, 3), Square(10, 0, 20, 10, 0, 3)) + "}," +
                "{\"id\":2,\"when\":\"hold\",\"pos\":[5,0,5],\"hold\":5,\"area\":" + Area(Square(0, 0, 10, 10, 0, 3), Square(14, 0, 24, 10, 0, 3)) + "}],\"spawns\":[]}").Triggers;
            AreaEdits.Prepare(house);
            var e = Engine(house); var w = new Walkers(); w.Alive.Add(new V3(0, 0, 0));
            float[] xs = { 6, 8, 10, 12, 15, 18 };
            var reset = new List<float>();
            for (int i = 0; i < xs.Length; i++)
            {
                w.Alive[0] = new V3(xs[i], 1, 5);
                e.Step(i, w);
                if (e.State(2).InsideSince == null) reset.Add(xs[i]);
            }
            check("seam: the hold across touching parts fired at 5 s", e.Fires(1) == 1, e.State(1).InsideSince + "");
            check("seam: the hold across a gap started again where the gap is", e.Fires(2) == 0 && string.Join(",", reset) == "12", string.Join(",", reset));
        }

        {   // Everyone: one player in each wing is everyone in; a player in the courtyard hole or upstairs is not; nobody alive is no.
            var courtyard = "{\"floor\":[0,3],\"outline\":[[0,0],[20,0],[20,20],[0,20]],\"holes\":[[[8,8],[12,8],[12,12],[8,12]]]}";
            var t = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"zone\",\"pos\":[1,0,1],\"everyone\":true,\"area\":" + Area(courtyard, Square(30, 0, 40, 10, 0, 3)) + "}],\"spawns\":[]}").Triggers;
            AreaEdits.Prepare(t);
            var w = new Walkers();
            check("everyone: nobody alive is not everyone", !w.Occupied(t[0], out _), "");
            w.Alive.Add(new V3(2, 1, 2)); w.Alive.Add(new V3(35, 1, 5));
            check("everyone: one in each part of the union is everyone", w.Occupied(t[0], out V3 where) && where.X == 35, "");
            w.Alive.Add(new V3(10, 1, 10));
            check("everyone: a player in the courtyard hole is out", !w.Occupied(t[0], out _), "");
            w.Alive[2] = new V3(12, 1, 10);
            check("everyone: the hole's edge belongs to the area", w.Occupied(t[0], out _), "");
            w.Alive[2] = new V3(2, 3.5f, 2);
            check("everyone: a player on the floor above is out", !w.Occupied(t[0], out _), "");
        }
    }
}
