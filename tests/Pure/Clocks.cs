using System;
using System.Collections.Generic;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// Simulated-clock checks (ML-010): repeats with a cap, the countdown messages around a firing, reinforcement waves over a
/// whole mission, and what a mission reset forgets. The clock is a plain number the test advances; nothing here reads a
/// file or needs the game. Message queueing itself (one yellow message waits for the other) lives in the plugin
/// (TryOuts.Message, on the game's clock), so only the engine's part of it is checked here.
/// </summary>
static class Clocks
{
    private const string OneEnemy = "\"spawns\":[{\"pos\":[0,0,0]}]";

    private static MissionPlan Plan(string json) => MissionPlan.FromJson(json);

    private static TriggerEngine Engine(MissionPlan p, float start)
    {
        var e = new TriggerEngine();
        e.Reset(start);
        e.Sync(p.Triggers);
        return e;
    }

    private static string Fired(List<TriggerEvent> ev) => string.Join(",", ev.Where(x => x.Kind == TriggerEvent.Fire).Select(x => x.Id));

    // Everything one look decided, written down with the time since the mission's start, so that two missions can be compared.
    private static void Note(List<string> log, List<TriggerEvent> ev, float sinceStart)
    {
        foreach (TriggerEvent x in ev)
            log.Add((x.Kind == TriggerEvent.Fire ? "fire " + x.Id : "warn " + x.Id + "." + x.Step) + "@" + sinceStart);
    }

    public static void Run(Action<string, bool, string> check)
    {
        {   // A condition with a repeat and a cap fires that many times; what waits for it fires once, not once per repeat.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"repeat\":10,\"times\":2},{\"id\":2,\"when\":\"after\",\"after\":1}]," + OneEnemy + "}");
            var e = Engine(p, 0f); var w = new World { Alert = true };
            var ones = new List<float>(); int twos = 0;
            for (float t = 0; t <= 100; t += 0.5f)
                foreach (TriggerEvent x in e.Step(t, w))
                {
                    if (x.Kind != TriggerEvent.Fire) continue;
                    if (x.Id == 1) ones.Add(t); else if (x.Id == 2) twos++;
                }
            check("an alert trigger with repeat 10 and times 2 fires at 0 and 10, then never again", string.Join(",", ones) == "0,10", string.Join(",", ones));
            check("the trigger that waits for it fires once, not once per repeat", twos == 1, twos + " firings");
            check("the capped trigger is exhausted and refuses a firing by hand", e.Fires(1) == 2 && e.Phase(p.Triggers[0], 100, out _) == TriggerEngine.Exhausted && !e.FireNow(1, 100, out _, out string why) && why == "it has already fired", "");
        }
        {   // A timer with a repeat and no cap keeps firing; times 1 means once, repeat or not.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"time\",\"repeat\":30},{\"id\":2,\"when\":\"time\",\"delay\":5,\"repeat\":5,\"times\":1}]," + OneEnemy + "}");
            var e = Engine(p, 0f); var w = new World();
            var ones = new List<float>(); var twos = new List<float>();
            for (float t = 0; t <= 100; t += 0.5f)
                foreach (TriggerEvent x in e.Step(t, w))
                {
                    if (x.Kind != TriggerEvent.Fire) continue;
                    if (x.Id == 1) ones.Add(t); else if (x.Id == 2) twos.Add(t);
                }
            check("a timer with repeat 30 and no cap fires at 0, 30, 60 and 90", string.Join(",", ones) == "0,30,60,90", string.Join(",", ones));
            check("between firings it says how long until the next", e.Phase(p.Triggers[0], 100, out float left) == TriggerEngine.Cooling && left == 20f, left + " s");
            check("times 1 with a repeat fires once only", string.Join(",", twos) == "5" && e.Phase(p.Triggers[1], 100, out _) == TriggerEngine.Exhausted, string.Join(",", twos));
        }
        {   // Two triggers that fire on the same look come out of it together; the countdown they start is told later, not on top of them.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\"},{\"id\":2,\"when\":\"after\",\"after\":1,\"delay\":30,\"warn\":true},{\"id\":3,\"when\":\"alert\"}]," + OneEnemy + "}");
            var e = Engine(p, 0f); var w = new World();
            string before = "";
            for (float t = 0; t < 10; t += 0.5f) before += Fired(e.Step(t, w));
            w.Alert = true;
            var same = e.Step(10, w);
            check("nothing fires before the alert", before == "", before);
            check("two triggers met on the same look both fire on it, in list order", Fired(same) == "1,3", Fired(same));
            check("the countdown started by that look says nothing on the same look", !same.Exists(x => x.Kind == TriggerEvent.Warn), "");
            var log = new List<string>();
            for (float t = 10.5f; t <= 50; t += 0.5f) Note(log, e.Step(t, w), t);
            check("the countdown is told 4 s later, again at ten seconds, then it fires", string.Join(" ", log) == "warn 2.4@14 warn 2.2@30 fire 2@40", string.Join(" ", log));
        }
        {   // A mission reset forgets every trigger state: the same mission played again gives the same events, shifted in time.
            var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"repeat\":10,\"times\":2},{\"id\":2,\"when\":\"after\",\"after\":1,\"delay\":60,\"warn\":true},"
                       + "{\"id\":3,\"when\":\"hold\",\"pos\":[0,0,0],\"radius\":5,\"hold\":15},{\"id\":4,\"when\":\"after\",\"after\":5},{\"id\":5,\"when\":\"after\",\"after\":4}]," + OneEnemy + "}");
            var w = new World { Alert = true };
            List<string> Play(TriggerEngine engine, float start)
            {
                var log = new List<string>();
                w.Inside.Clear();
                for (float t = start; t <= start + 200; t += 0.5f)
                {
                    if (t - start == 190) w.Inside.Add(3);      // someone walks into the hold zone shortly before the reset
                    Note(log, engine.Step(t, w), t - start);
                }
                return log;
            }
            var e = Engine(p, 0f);
            var first = Play(e, 0f);
            check("before the reset: the repeat is spent, the countdown was told, the hold runs, the circle is named",
                  e.Fires(1) == 2 && e.EverFired(2) && e.State(2).Told != 0 && e.State(3).InsideSince == 190f && e.InCircle(4) && e.Problems.Count == 1,
                  string.Join(" ", first));
            e.Reset(1000f);
            check("after the reset nothing is known: no triggers, no states, no firings, no circles, no problems",
                  !e.Known(1) && e.State(1) == null && e.State(3) == null && !e.FiredIds().Any() && !e.InCircle(4) && e.Problems.Count == 0 && e.Start == 1000f, "");
            e.Sync(p.Triggers);
            check("read again, every trigger starts clean", e.Fires(1) == 0 && !e.State(1).Latched && e.State(1).RearmAt == null && e.State(2).Told == 0 && e.State(3).InsideSince == null && e.Problems.Count == 1, "");
            var second = Play(e, 1000f);
            check("the mission played again gives the same events at the same times", first.Count > 0 && string.Join(" ", first) == string.Join(" ", second), string.Join(" ", second));
        }
        {   // One mission of reinforcement waves on a simulated clock: a failed call and a wave nobody saw use up nothing.
            var r = new WaveRule { After = 10, Times = 2 };
            var log = new List<string>();
            int dead = 0, asks = 0, doneAtAsk = 0; float askedAt = -1f; bool countedWhilePending = false;
            for (float t = 0; t <= 120; t += 0.5f)
            {
                if (t == 5 || t == 40 || t == 100) dead = 1;
                int back = 0;
                bool gameBrings = asks != 3;                 // the third request goes through, but nobody comes back
                if (r.State == WaveRule.Pending && gameBrings && t >= askedAt + 1f) { back = dead; dead = 0; }
                var act = r.Step(t, dead, back);
                if (act != WaveRule.Act.None) log.Add(act + "@" + t);
                if (act == WaveRule.Act.Ask)
                {
                    asks++; askedAt = t; doneAtAsk = r.Done;
                    r.Asked(t, asks != 1, asks == 1 ? "the game's mission keeper was not found" : "");   // the first call fails outright
                }
                if (r.State == WaveRule.Pending && r.Done != doneAtAsk) countedWhilePending = true;
            }
            check("two waves: a failed call, a retry that works; then a wave nobody saw, a retry that works; a third death after the cap starts no clock",
                  string.Join(" ", log) == "Announce@5 Ask@15 Ask@25 Observed@26 Announce@40 Ask@50 GaveUp@58.5 Ask@68.5 Observed@69.5", string.Join(" ", log));
            check("a wave is never counted while it is pending", !countedWhilePending, "");
            check("the failed call and the unseen wave used up nothing: two waves took four requests", r.Done == 2 && r.Spent && r.WavesLeft == 0 && asks == 4 && r.State == WaveRule.Idle, r.Done + " done, " + asks + " asks");
            r.Reset();
            check("a mission reset gives every wave back and keeps the rule itself", r.Done == 0 && r.WavesLeft == 2 && !r.Spent && r.State == WaveRule.Idle && r.After == 10f && r.Times == 2, "");
        }
        {   // A mission reset in the middle of a failed or pending wave forgets it, and a late answer from the old mission is not counted.
            var failed = new WaveRule { After = 5, Times = 1 };
            failed.Step(0, 1, 0); failed.Step(5, 1, 0); failed.Asked(5, false, "boom");
            check("before the reset the wave has failed with its reason", failed.State == WaveRule.Failed && failed.Error == "boom" && failed.Tries == 1, "");
            failed.Reset();
            check("after the reset: idle, no error, no tries, no clock", failed.State == WaveRule.Idle && failed.Error == "" && failed.Tries == 0 && failed.Since == 0f && failed.Done == 0, "");
            check("the first death of the new mission starts the full wait again", failed.Step(100, 1, 0) == WaveRule.Act.Announce && failed.SecondsLeft(100) == 5f && failed.WavesLeft == 1, "");
            var pending = new WaveRule { After = 5, Times = 1 };
            pending.Step(0, 1, 0); pending.Step(5, 1, 0); pending.Asked(5, true, "");
            check("before the reset the wave is pending", pending.State == WaveRule.Pending, "");
            pending.Reset();
            check("a player back after the reset is not counted as a wave of the new mission", pending.Step(6, 0, 1) == WaveRule.Act.None && pending.Done == 0 && pending.State == WaveRule.Idle, "");
        }
    }
}
