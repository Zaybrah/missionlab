using System;
using System.Collections.Generic;
using MissionLab.Core;

/// <summary>
/// The on-screen message queue on a made-up clock (ML-010): spacing, red messages, the cap and a new mission.
/// The game shows a message and then reports it with Shown, so the tests do the same.
/// </summary>
static class Messages
{
    public static void Run(Action<string, bool, string> check)
    {
        {   // Two yellow messages at the same moment: the first goes now, the second 4.5 s later, in order.
            var q = new MessageQueue(); var shown = new List<string>(); var at = new List<float>();
            if (q.Offer("one", false, 10f) == MessageTurn.Now) { shown.Add("one"); at.Add(10f); q.Shown(10f); }
            check("a second message at the same moment waits", q.Offer("two", false, 10f) == MessageTurn.Waits, "");
            for (float t = 10f; t <= 20f; t += 0.1f)
            {
                string due = q.Due(t);
                if (due != null) { shown.Add(due); at.Add(t); q.Shown(t); }
            }
            check("both messages are shown in order", string.Join(",", shown) == "one,two", string.Join(",", shown));
            check("the second comes 4.5 s after the first", at.Count == 2 && Math.Abs(at[1] - at[0] - MessageQueue.Gap) < 0.11f, string.Join(",", at));
        }
        {   // A red message goes at once even while yellow ones wait.
            var q = new MessageQueue();
            q.Offer("one", false, 0f); q.Shown(0f);
            q.Offer("two", false, 1f);
            check("a red message is shown at once", q.Offer("alarm", true, 1f) == MessageTurn.Now, "");
            q.Shown(1f);
            check("the yellow one still waits", q.Waiting == 1 && q.Due(2f) == null, q.Waiting.ToString());
            check("the yellow one keeps its gap after the red one", q.Due(5.5f) == "two", "");
        }
        {   // At most eight wait; the ninth is dropped and the eight keep their order.
            var q = new MessageQueue();
            q.Offer("first", false, 0f); q.Shown(0f);
            for (int i = 0; i < 8; i++) q.Offer("m" + i, false, 0f);
            check("the ninth waiting message is dropped", q.Offer("extra", false, 0f) == MessageTurn.Dropped, "");
            check("eight are waiting", q.Waiting == MessageQueue.MaxWaiting, q.Waiting.ToString());
            check("the oldest waiting comes first", q.Due(4.5f) == "m0", "");
        }
        {   // A new mission drops what was waiting and the next message goes at once.
            var q = new MessageQueue();
            q.Offer("one", false, 0f); q.Shown(0f);
            q.Offer("two", false, 0f); q.Offer("three", false, 0f);
            q.Reset();
            check("nothing waits after a reset", q.Waiting == 0 && q.Due(100f) == null, q.Waiting.ToString());
            check("the first message of the new mission goes at once", q.Offer("hello", false, 0.5f) == MessageTurn.Now, "");
        }
        {   // A mission ending waits until the messages fired with it have been on screen (the acceptance run, 18 November).
            var q = new MessageQueue();
            check("with nothing waiting the mission ends at once", q.EndAt(5f) == 5f, q.EndAt(5f).ToString());
            q.Offer("phone secured", false, 237.9f); q.Shown(237.9f);
            q.Offer("the extraction is open", false, 237.9f); q.Offer("mission accomplished", false, 237.9f);
            float end = q.EndAt(237.9f), lastShown = -1f;
            for (float t = 237.9f; t < end; t += 0.1f) { if (q.Due(t) != null) { lastShown = t; q.Shown(t); } }
            check("both waiting messages are shown before the ending", q.Waiting == 0 && lastShown > 0f, q.Waiting + " still waiting");
            check("the last one stays on screen for the linger time first", end - lastShown >= MessageQueue.Linger - 0.11f, end + " vs " + lastShown);
            var full = new MessageQueue();
            full.Offer("first", false, 0f); full.Shown(0f);
            for (int i = 0; i < 8; i++) full.Offer("m" + i, false, 0f);
            check("an ending never waits longer than the cap", full.EndAt(0f) == MessageQueue.MostWait, full.EndAt(0f).ToString());
        }
        {   // A repeat that brings nobody back and does nothing else shows no message (the co-op run, 19 November).
            MissionPlan p = MissionPlan.FromJson("{\"triggers\":[{\"id\":7,\"when\":\"time\",\"repeat\":60,\"message\":\"More of them.\"}," +
                "{\"id\":8,\"when\":\"time\",\"repeat\":60,\"power\":\"off\",\"message\":\"The power is cut again.\"}," +
                "{\"id\":9,\"when\":\"time\",\"repeat\":60,\"doors\":[{\"pos\":[1,0,1],\"locked\":true}],\"message\":\"The door is locked again.\"}]," +
                "\"spawns\":[{\"pos\":[0,0,0],\"trigger\":7}]}");
            TriggerPlan waves = p.Triggers[0], power = p.Triggers[1], door = p.Triggers[2];
            check("a repeat with nobody back and nothing else to do leaves its message out", TriggerPlan.MessageLeftOut(waves, true, true, 0), "");
            check("a repeat that brings one back shows it", !TriggerPlan.MessageLeftOut(waves, true, true, 1), "");
            check("the first firing always shows it", !TriggerPlan.MessageLeftOut(waves, false, true, 0), "");
            check("a repeat of a trigger without enemies of its own (a reminder) shows it", !TriggerPlan.MessageLeftOut(waves, true, false, 0), "");
            check("a repeat that cuts the power shows it, enemies or not", !TriggerPlan.MessageLeftOut(power, true, true, 0), "");
            check("a repeat that sets a door shows it, enemies or not", !TriggerPlan.MessageLeftOut(door, true, true, 0), "");
        }
    }
}
