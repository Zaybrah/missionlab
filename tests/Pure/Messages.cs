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
    }
}
