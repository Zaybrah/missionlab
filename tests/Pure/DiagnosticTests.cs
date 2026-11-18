using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// The trigger diagnostic example (ML-015): played on a simulated clock against a scripted world, every state the trigger
/// inspector knows shows up in it (waiting, holding, counting, fired, cooling, exhausted, excluded by chance, waiting for an
/// excluded trigger, fired by hand, an action that failed, one that was not confirmed), with the sentences the page shows.
/// A circle is the one state it leaves out: shipped examples may not have one.
/// </summary>
static class DiagnosticTests
{
    private sealed class Script : ITriggerWorld
    {
        public bool Alert, Phone, Door;
        public int Gone, Alive = 2, In;
        public bool Alerted() => Alert;
        public bool IntelTaken(string item) => Phone;
        public bool Occupied(TriggerPlan t, out V3 where) { where = t.Pos; return t.Everyone ? Alive > 0 && In >= Alive : In > 0; }
        public bool Down(int count, string tag) => Gone >= count;
        public bool DoorOpen(V3 hinge) => Door;
        public int Inside(TriggerPlan t, out int alive) { alive = Alive; return Math.Min(In, Alive); }
    }

    private static string Example(string name)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "examples"))) root = Path.GetDirectoryName(root);
        return root == null ? null : File.ReadAllText(Path.Combine(root, "examples", name));
    }

    public static void Run(Action<string, bool, string> check)
    {
        string text = Example("clean_house_diagnostic.json");
        check("the diagnostic example is found", text != null, "");
        if (text == null) return;
        var findings = ScenarioFile.Read(text, out MissionPlan plan);
        check("the diagnostic example passes the strict reader", !ScenarioFile.Blocks(findings) && plan != null, string.Join(" | ", findings.Select(f => f.ToString())));
        if (plan == null) return;

        // This run's roll: the chance trigger sits the mission out, as on about half of all runs.
        plan.Triggers.Find(t => t.Id == 5).Off = true;
        check("the diagnostic example can be finished when the chance trigger sits out", !Completion.Blocks(Completion.Check(plan, null, 2)), "");
        var engine = new TriggerEngine();
        engine.Reset(0f); engine.Sync(plan.Triggers);
        var world = new Script();
        var seen = new SortedSet<string>();
        var said = new Dictionary<string, string>();
        void Look(float now, int id, string key)
        {
            TriggerView v = engine.Explain(plan.Triggers.Find(t => t.Id == id), now, world);
            said[key] = v.Why;
            if (key == "6") check("trigger 6 lists trigger 5 as excluded and still waited for", v.Excluded.SequenceEqual(new[] { 5 }) && v.Waiting.SequenceEqual(new[] { 5 }), string.Join(",", v.Excluded));
        }
        for (int step = 0; step <= 340; step++)
        {
            float now = step * 0.5f;
            if (now == 40f) world.In = 1;
            if (now == 50f) world.In = 2;
            if (now == 75f) world.In = 0;
            if (now == 100f) world.Door = true;
            if (now == 120f) world.Phone = true;
            if (now == 130f) world.Gone = 2;
            foreach (TriggerEvent ev in engine.Step(now, world))
            {
                if (ev.Kind != TriggerEvent.Fire) continue;
                // What the runtime tells after applying a firing (GameMaster.Fire): the back door's message and lock cannot be seen to take.
                if (ev.Id == 8) engine.Report(8, TriggerEngine.Unconfirmed, "asked of the game, not seen to take: a door, a message");
                else engine.Report(ev.Id, TriggerEngine.Applied, "");
            }
            if (now == 95f)
            {
                check("the by-hand trigger fires on Fire now", engine.FireNow(7, now, out _, out string why), why);
                engine.Report(7, TriggerEngine.Failed, "no door was found at (-4, -2)");     // its door setting points at the yard on purpose
            }
            foreach (TriggerPlan t in plan.Triggers) seen.Add(engine.Explain(t, now, world).Phase);
            if (now == 10f) { Look(now, 4, "phone"); Look(now, 6, "6"); Look(now, 9, "down"); }
            if (now == 18f) Look(now, 1, "clock");
            if (now == 45f) Look(now, 3, "one of two");
            if (now == 60f) Look(now, 3, "holding");
            if (now == 80f) Look(now, 2, "cooling");
            if (now == 96f) Look(now, 7, "failed");
            if (now == 101f) Look(now, 8, "unconfirmed");
            if (now == 160f) Look(now, 2, "exhausted");
            if (now == 160f) Look(now, 5, "excluded");
        }
        string all = string.Join(",", seen);
        foreach (string phase in new[] { TriggerEngine.Waiting, TriggerEngine.Holding, TriggerEngine.Counting, TriggerEngine.Fired, TriggerEngine.Cooling, TriggerEngine.Exhausted, TriggerEngine.Off })
            check("the diagnostic example shows the state '" + phase + "'", seen.Contains(phase), all);
        check("it never shows a circle (not allowed in an example)", !seen.Contains(TriggerEngine.Circle), all);
        void Says(string key, string want) => check("trigger inspector says: " + want, said.TryGetValue(key, out string got) && got == want, got);
        Says("phone", "Waiting for the cell phone to be picked up");
        Says("6", "Waiting for trigger 5, which sat this mission out (chance)");
        Says("down", "Waiting for 2 enemies to go down");
        Says("clock", "Timer running; 12 s left");
        Says("one of two", "1 of 2 living players inside; every one of them has to be");
        Says("holding", "2 of 2 living players inside; 10 s left of the 20 s hold");
        Says("cooling", "Fired 1 of 3 times; armed again in 25 s");
        Says("failed", "Fired by hand 95 s into the mission, but its actions failed: no door was found at (-4, -2)");
        Says("unconfirmed", "Fired 100 s into the mission; whether its actions took could not be seen: asked of the game, not seen to take: a door, a message");
        Says("exhausted", "Fired 3 of 3 times; it does not fire again");
        Says("excluded", "Sat this mission out (its chance is 50%); only Fire now sets it off");
    }
}
