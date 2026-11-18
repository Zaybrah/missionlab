using System;
using System.Collections.Generic;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// Trigger explanation and outcomes (ML-015): Explain says where a trigger stands in one sentence with the real numbers and
/// changes nothing; Report keeps "fired, but its actions failed" apart from "its condition is not met"; a zone or hold whose
/// place changes during a mission loses the progress under way and keeps its firings.
/// </summary>
static class EngineTests
{
    private const string OneEnemy = "\"spawns\":[{\"pos\":[0,0,0]}]";

    private static MissionPlan Plan(string json) => MissionPlan.FromJson(json);

    private static TriggerEngine Engine(MissionPlan p)
    {
        var e = new TriggerEngine();
        e.Reset(0f);
        e.Sync(p.Triggers);
        return e;
    }

    private static string Fired(List<TriggerEvent> ev) => string.Join(",", ev.Where(x => x.Kind == TriggerEvent.Fire).Select(x => x.Id));

    /// <summary>A world that says how many players are alive and how many of them stand inside every zone.</summary>
    private sealed class CountWorld : ITriggerWorld
    {
        public bool Alert;
        public int Alive, In;
        public bool Alerted() => Alert;
        public bool IntelTaken(string item) => false;
        public bool Occupied(TriggerPlan t, out V3 where) { where = default; return t.Everyone ? Alive > 0 && In >= Alive : In > 0; }
        public bool Down(int count, string tag) => false;
        public bool DoorOpen(V3 hinge) => false;
        public int Inside(TriggerPlan t, out int alive) { alive = Alive; return Math.Min(In, Alive); }
    }

    public static void Run(Action<string, bool, string> check)
    {
        {   // A zone for everyone counts the living players, with the real numbers.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"zone\",\"pos\":[0,0,0],\"radius\":5,\"everyone\":true}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld { Alive = 2, In = 1 };
            e.Step(1, w);
            TriggerView v = e.Explain(p.Triggers[0], 1, w);
            check("a zone for everyone says 1 of 2 living players inside", v.Phase == TriggerEngine.Waiting && v.Why == "1 of 2 living players inside; every one of them has to be" && v.Inside == 1 && v.Alive == 2, v.Why);
            w.Alive = 0; w.In = 0;
            check("with nobody alive it says so", e.Explain(p.Triggers[0], 1, w).Why == "No living player to step inside", e.Explain(p.Triggers[0], 1, w).Why);
            check("without a world it does not guess the numbers", e.Explain(p.Triggers[0], 1, null).Inside == -1, "");
        }
        {   // A hold under way says who is inside and how long is left.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"hold\",\"pos\":[0,0,0],\"radius\":5,\"hold\":30}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld { Alive = 2, In = 1 };
            e.Step(0, w); e.Step(8, w);
            TriggerView v = e.Explain(p.Triggers[0], 8, w);
            check("a hold under way says 1 of 2 inside and 22 s left", v.Phase == TriggerEngine.Holding && v.Why == "1 of 2 living players inside; 22 s left of the 30 s hold" && v.Left == 22f, v.Why);
        }
        {   // A wait under way says how many seconds are left.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"delay\":20},{\"id\":2,\"when\":\"time\",\"delay\":30}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld { Alert = true };
            e.Step(8, w); e.Step(16, w);
            check("an alert with a 20 s wait, met at 8 s, says 12 s left at 16 s", e.Explain(p.Triggers[0], 16, w).Why == "Condition met; 12 s left", e.Explain(p.Triggers[0], 16, w).Why);
            check("a timer of 30 s says 14 s left at 16 s", e.Explain(p.Triggers[1], 16, w).Why == "Timer running; 14 s left", e.Explain(p.Triggers[1], 16, w).Why);
        }
        {   // Waiting for an item names the item as the player knows it.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"intel\",\"item\":\"Cell Phone\"},{\"id\":2,\"when\":\"intel\",\"item\":\"all\"},{\"id\":3,\"when\":\"alert\"},{\"id\":4,\"when\":\"down\",\"count\":0,\"tag\":\"officer\"}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld();
            e.Step(1, w);
            check("an intel trigger waits for the cell phone to be picked up", e.Explain(p.Triggers[0], 1, w).Why == "Waiting for the cell phone to be picked up", e.Explain(p.Triggers[0], 1, w).Why);
            check("\"all\" waits for every intel item", e.Explain(p.Triggers[1], 1, w).Why == "Waiting for every intel item to be picked up", e.Explain(p.Triggers[1], 1, w).Why);
            check("an alert trigger waits for the alarm", e.Explain(p.Triggers[2], 1, w).Why == "Waiting for the enemy to raise the alarm", e.Explain(p.Triggers[2], 1, w).Why);
            check("a down trigger names its tag", e.Explain(p.Triggers[3], 1, w).Why == "Waiting for every enemy tagged 'officer' to go down", e.Explain(p.Triggers[3], 1, w).Why);
        }
        {   // A trigger that waits for one that sat this mission out says so, and lists it as excluded.
            var p = Plan("{\"triggers\":[{\"id\":2,\"when\":\"alert\",\"chance\":50},{\"id\":3,\"when\":\"after\",\"after\":2},{\"id\":4,\"when\":\"any\",\"of\":[2,3]}]," + OneEnemy + "}");
            p.Triggers[0].Off = true;
            var e = Engine(p); var w = new CountWorld { Alert = true };
            e.Step(1, w);
            TriggerView v = e.Explain(p.Triggers[1], 1, w);
            check("it waits for trigger 2, which sat this mission out (chance)", v.Why == "Waiting for trigger 2, which sat this mission out (chance)" && v.Excluded.SequenceEqual(new[] { 2 }) && v.Waiting.SequenceEqual(new[] { 2 }), v.Why);
            check("the excluded trigger itself says that only Fire now sets it off", e.Explain(p.Triggers[0], 1, w).Phase == TriggerEngine.Off && e.Explain(p.Triggers[0], 1, w).Why.StartsWith("Sat this mission out (its chance is 50%)"), e.Explain(p.Triggers[0], 1, w).Why);
            TriggerView any = e.Explain(p.Triggers[2], 1, w);
            check("\"any\" of one that sat out and one still waiting names both", any.Why == "Waiting for any of triggers 2, 3" && any.Excluded.SequenceEqual(new[] { 2 }), any.Why);
            e.FireNow(2, 2, out _, out _);
            e.Step(2, w);
            check("fired by hand, the trigger that waited for it fires too", e.EverFired(3) && e.Explain(p.Triggers[1], 2, w).Waiting.Count == 0, "");
        }
        {   // A repeating trigger says how often it fired of how often it may, and when it is armed again.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"repeat\":20,\"times\":3}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld { Alert = true };
            for (float t = 0; t <= 25; t += 0.5f) e.Step(t, w);
            TriggerView v = e.Explain(p.Triggers[0], 25, w);
            check("it fired 2 of 3 times and is armed again in 15 s", v.Phase == TriggerEngine.Cooling && v.Why == "Fired 2 of 3 times; armed again in 15 s" && v.Fires == 2 && v.Times == 3, v.Why);
            for (float t = 25.5f; t <= 60; t += 0.5f) e.Step(t, w);
            check("after the third it says it does not fire again", e.Explain(p.Triggers[0], 60, w).Why == "Fired 3 of 3 times; it does not fire again", e.Explain(p.Triggers[0], 60, w).Why);
        }
        {   // Explaining only reads: a hold that has not started is not started by being asked about.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"hold\",\"pos\":[0,0,0],\"radius\":5,\"hold\":10},{\"id\":2,\"when\":\"alert\",\"delay\":5}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld { Alive = 1, In = 1, Alert = true };
            for (int k = 0; k < 5; k++) { e.Explain(p.Triggers[0], k, w); e.Explain(p.Triggers[1], k, w); }
            check("asking about a hold does not start it", e.State(1).InsideSince == null && e.State(1).MetAt == null && e.State(2).MetAt == null, "");
            check("asking says the hold starts once a look sees the player", e.Explain(p.Triggers[0], 5, w).Why == "1 of 1 living player inside", e.Explain(p.Triggers[0], 5, w).Why);
            check("the first look after all that asking still starts the clocks at its own time", Fired(e.Step(5, w)) == "" && e.State(1).InsideSince == 5f && e.State(2).MetAt == 5f, "");
        }
        {   // Report tells "fired, but its actions failed" apart from "its condition is not met".
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"time\",\"delay\":3},{\"id\":2,\"when\":\"alert\"}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld();
            for (float t = 0; t <= 4; t += 0.5f) e.Step(t, w);
            e.Report(1, TriggerEngine.Failed, "the spawner refused the enemies");
            TriggerView failed = e.Explain(p.Triggers[0], 4, w), unmet = e.Explain(p.Triggers[1], 4, w);
            check("a firing whose actions failed says so, with why", failed.Phase == TriggerEngine.Fired && failed.Outcome == TriggerEngine.Failed && failed.Why == "Fired 3 s into the mission, but its actions failed: the spawner refused the enemies", failed.Why);
            check("a trigger whose condition is not met has no outcome and says what it waits for", unmet.Phase == TriggerEngine.Waiting && unmet.Outcome == "" && unmet.Why == "Waiting for the enemy to raise the alarm", unmet.Why);
            e.Report(1, TriggerEngine.Applied, "");
            check("an applied firing says only that it fired", e.Explain(p.Triggers[0], 4, w).Why == "Fired 3 s into the mission" && e.State(1).Outcome == TriggerEngine.Applied, e.Explain(p.Triggers[0], 4, w).Why);
            e.Report(99, TriggerEngine.Failed, "no such trigger");
            check("a report for a trigger the engine does not know is ignored", e.State(99) == null, "");
        }
        {   // A new firing waits for its own report: the last one's outcome does not stick to it.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"repeat\":10}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld { Alert = true };
            e.Step(0, w);
            e.Report(1, TriggerEngine.Unconfirmed, "the door did not answer");
            check("an unconfirmed outcome is said with why", e.Explain(p.Triggers[0], 1, w).Why == "Fired once; armed again in 9 s; whether its actions took could not be seen: the door did not answer", e.Explain(p.Triggers[0], 1, w).Why);
            e.Step(10, w);
            check("the second firing starts with no outcome", e.Fires(1) == 2 && e.State(1).Outcome == "" && e.State(1).OutcomeWhy == "", e.State(1).Outcome);
        }
        {   // A hold that is moved during a mission starts its count again, and keeps that it fired before.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"hold\",\"pos\":[0,0,0],\"radius\":5,\"hold\":5,\"repeat\":10}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld { Alive = 1, In = 1 };
            for (float t = 0; t <= 18; t += 0.5f) e.Step(t, w);
            check("before the move it fired once and holds again since 15 s", e.Fires(1) == 1 && e.State(1).InsideSince == 15f && e.Phase(p.Triggers[0], 18, out _) == TriggerEngine.Holding, "");
            e.Sync(p.Triggers);
            check("reading the same list again changes nothing", e.State(1).InsideSince == 15f, "");
            p.Triggers[0].Pos = new V3(50, 0, 50);
            e.Sync(p.Triggers);
            TriggerState s = e.State(1);
            check("moved, the hold in progress is gone", s.InsideSince == null && s.MetAt == null && !s.HasWhere && e.Phase(p.Triggers[0], 18, out _) == TriggerEngine.Waiting, "");
            check("moved, its firing is kept", s.Fires == 1 && s.LastFiredAt == 5f && e.EverFired(1), "");
            e.Step(18.5f, w);
            check("the hold starts again at the next look", e.State(1).InsideSince == 18.5f, "");
        }
        {   // A latched zone that is resized keeps its latch and its wait; switching "everyone" counts as a change.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"zone\",\"pos\":[0,0,0],\"radius\":5,\"repeat\":30},{\"id\":2,\"when\":\"zone\",\"pos\":[0,0,0],\"radius\":5,\"delay\":20}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld { Alive = 2, In = 1 };
            e.Step(1, w);
            p.Triggers[0].Radius = 8; p.Triggers[1].Everyone = true;
            e.Sync(p.Triggers);
            check("the fired zone stays latched with its pause", e.State(1).Latched && e.State(1).RearmAt == 31f && e.State(1).MetAt == 1f, "");
            check("the zone counting down starts over once everyone is asked for", e.State(2).MetAt == null && !e.State(2).Latched, "");
        }
        {   // Giving a zone a shaped area counts as a change of place.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"hold\",\"pos\":[0,0,0],\"radius\":5,\"hold\":30}]," + OneEnemy + "}");
            var e = Engine(p); var w = new CountWorld { Alive = 1, In = 1 };
            e.Step(0, w); e.Step(4, w);
            var part = new AreaPart { Bottom = -1, Top = 3 };
            part.Outline.Add(new P2(-5, -5)); part.Outline.Add(new P2(5, -5)); part.Outline.Add(new P2(5, 5)); part.Outline.Add(new P2(-5, 5));
            var area = new AreaPlan(); area.Parts.Add(part); area.Prepare();
            p.Triggers[0].Area = area;
            e.Sync(p.Triggers);
            check("a hold given an area starts its count again", e.State(1).InsideSince == null && TriggerEngine.ShapeOf(p.Triggers[0]).EndsWith(area.Signature()), TriggerEngine.ShapeOf(p.Triggers[0]));
        }
    }
}
