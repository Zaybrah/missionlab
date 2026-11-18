using System;
using System.Collections.Generic;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// The Game Master budget (ML-025) on a simulated clock: what an action costs, how long it rests, the points that come back,
/// refunds and retries, and the placement rule that keeps reinforcements away from and out of sight of the players. The clock
/// is a plain number; the trigger check is a dictionary the test fills in.
/// </summary>
static class BudgetTests
{
    private static DirectorPlan Plan(int budget, int regen) => new DirectorPlan
    {
        Budget = budget, Regen = regen,
        Actions = new List<ActionPlan>
        {
            new ActionPlan { Id = "rear", Name = "Rear reinforcements", Cost = 3, Cooldown = 60f, Fire = 12, MinDistance = 30f, Unseen = true },
            new ActionPlan { Id = "flare", Name = "Flare", Cost = 8, Fire = 13, Fallback = ActionPlan.Cancel },
        },
    };

    private static readonly IList<float> Ahead = new List<float> { 0f };     // one player facing +Z

    public static void Run(Action<string, bool, string> check)
    {
        {   // Spending takes the cost off and starts the action's rest; once the rest is over it may be used again.
            var b = new Budget(Plan(10, 0));
            bool first = b.Spend("rear", "c1", 0f, out string why1);
            bool resting = b.Can("rear", 30f, out string why2);
            bool later = b.Spend("rear", "c2", 60f, out _);
            check("a spend takes the cost off", first && Math.Abs(b.Points - 4f) < 0.001f, b.Points + " " + why1);
            check("an action that rests says how long is left", !resting && why2 == "cooldown: 30 s left", why2);
            check("after its rest the action may be used again", later, b.Points.ToString());
        }
        {   // A retry with the same command id is paid for once; a new id pays again.
            var b = new Budget(Plan(10, 0));
            b.Spend("flare", "c1", 0f, out _);
            bool again = b.Spend("flare", "c1", 1f, out string why);
            check("a retry with the same command id never pays twice", again && Math.Abs(b.Points - 2f) < 0.001f && why == "already paid for by this command", b.Points + " " + why);
            check("the budget knows the command it has seen", b.Has("c1") && !b.Has("c2"), "");
            check("the same id used for another action is refused", !b.Spend("rear", "c1", 2f, out string other) && other.Contains("'flare'"), other);
        }
        {   // A refund gives the points back and lifts the rest; it happens once, and a refunded command is not paid for again by a retry.
            var b = new Budget(Plan(10, 0));
            b.Spend("rear", "c1", 0f, out _);
            bool back = b.Refund("c1"), twice = b.Refund("c1");
            check("a refund restores the points", back && Math.Abs(b.Points - 10f) < 0.001f, b.Points.ToString());
            check("a refund happens once", !twice, "");
            check("a refund lifts the action's rest", b.Can("rear", 1f, out string why), why);
            check("a retry of a refunded command is not paid again", !b.Spend("rear", "c1", 2f, out string again) && Math.Abs(b.Points - 10f) < 0.001f, again);
            check("an unknown command cannot be refunded", !b.Refund("nope"), "");
        }
        {   // A refused action costs nothing, whatever the reason.
            var triggers = new Dictionary<int, string> { { 12, "" }, { 13, "its trigger 13 has already fired" } };
            var b = new Budget(Plan(5, 0), id => triggers.TryGetValue(id, out string w) ? w : "its trigger " + id + " does not exist");
            bool unknown = b.Spend("nuke", "c1", 0f, out string w1);
            bool poor = b.Spend("flare", "c2", 0f, out string w2);
            b.Spend("rear", "c3", 0f, out _);
            bool rest = b.Spend("rear", "c4", 10f, out string w3);
            var rich = new Budget(Plan(20, 0), id => triggers.TryGetValue(id, out string w) ? w : "");
            bool latched = rich.Spend("flare", "c5", 0f, out string w4);
            check("an unknown action is refused and named", !unknown && w1 == "there is no action 'nuke'", w1);
            check("too few points are refused with the numbers", !poor && w2 == "not enough points (needs 8, has 5)", w2);
            check("an action that rests is refused", !rest && w3 == "cooldown: 50 s left", w3);
            check("an action whose trigger cannot fire is refused with the trigger's reason", !latched && w4 == "its trigger 13 has already fired", w4);
            check("no refused action took any points", Math.Abs(b.Points - 2f) < 0.001f && Math.Abs(rich.Points - 20f) < 0.001f && !b.Has("c1") && !b.Has("c2") && !b.Has("c4"), b.Points + " " + rich.Points);
        }
        {   // Points come back by the minute and never above the starting budget, refunds included.
            var b = new Budget(Plan(10, 2));
            b.Spend("flare", "c1", 0f, out _);
            b.Can("flare", 30f, out _);
            float half = b.Points;
            b.Can("flare", 600f, out _);
            float capped = b.Points;
            var c = new Budget(Plan(10, 60));
            c.Spend("rear", "c1", 0f, out _);
            c.Can("rear", 2f, out _);
            c.Refund("c1");
            check("regen adds points by the minute", Math.Abs(half - 3f) < 0.001f, half.ToString());
            check("regen stops at the starting budget", Math.Abs(capped - 10f) < 0.001f, capped.ToString());
            check("a refund does not lift the points above the starting budget", Math.Abs(c.Points - 10f) < 0.001f, c.Points.ToString());
        }
        {   // Reset brings back the full budget and forgets every spend and rest.
            var b = new Budget(Plan(10, 0));
            b.Spend("rear", "c1", 0f, out _);
            b.Reset();
            check("a reset gives the full budget back", Math.Abs(b.Points - 10f) < 0.001f, b.Points.ToString());
            check("a reset forgets the spends and the rests", !b.Has("c1") && b.Can("rear", 1f, out _), "");
        }
        {   // The snapshot is JSON the page can read, with every action's state and why it cannot be used.
            var b = new Budget(Plan(10, 1));
            b.Spend("rear", "c\"1", 5f, out _);
            string json = b.Json(15f);
            bool parsed = true; string flareWhy = "", rearWhy = ""; bool rearOff = false; int spent = 0; double points = 0; string command = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement r = doc.RootElement;
                    points = r.GetProperty("points").GetDouble();
                    foreach (JsonElement a in r.GetProperty("actions").EnumerateArray())
                    {
                        if (a.GetProperty("id").GetString() == "flare") flareWhy = a.GetProperty("why").GetString();
                        if (a.GetProperty("id").GetString() == "rear") { rearWhy = a.GetProperty("why").GetString(); rearOff = !a.GetProperty("available").GetBoolean() && a.GetProperty("cooldownLeft").GetDouble() == 50; }
                    }
                    foreach (JsonElement s in r.GetProperty("spent").EnumerateArray()) { spent++; command = s.GetProperty("command").GetString(); }
                    parsed = r.GetProperty("budget").GetInt32() == 10 && r.GetProperty("regen").GetInt32() == 1;
                }
            }
            catch (Exception) { parsed = false; }
            check("the budget snapshot parses as JSON with its totals", parsed && Math.Abs(points - 7.17) < 0.001, json);
            check("the snapshot says why each action is unavailable", flareWhy == "not enough points (needs 8, has 7.17)" && rearWhy == "cooldown: 50 s left" && rearOff, json);
            check("the snapshot lists the spend with its command id as sent", spent == 1 && command == "c\"1", json);
            check("the snapshot keys come in the documented order", json.StartsWith("{\"points\":7.17,\"budget\":10,\"regen\":1,\"on\":true,\"actions\":[{\"id\":\"rear\",\"name\":\"Rear reinforcements\",\"cost\":3,\"cooldownLeft\":50,\"available\":false,\"why\":", StringComparison.Ordinal), json);
        }
        {   // Free directing (the host turned the budget rules off): a card needs only a trigger that can fire; points and rests do not count.
            var triggers = new Dictionary<int, string> { { 13, "its trigger 13 has already fired" } };
            var b = new Budget(Plan(2, 0), id => triggers.TryGetValue(id, out string w) ? w : "");
            bool freeRear = b.CanFree("rear", out string rearWhy);
            bool freeFlare = b.CanFree("flare", out string flareWhy);
            bool none = b.CanFree("nope", out string noneWhy);
            check("free directing ignores points the budget lacks", freeRear && rearWhy == "" && !b.Can("rear", 0f, out _), rearWhy);
            check("free directing still refuses a trigger that cannot fire", !freeFlare && flareWhy == "its trigger 13 has already fired", flareWhy);
            check("free directing refuses an unknown action", !none && noneWhy == "there is no action 'nope'", noneWhy);
            string off = b.Json(0f, false);
            bool parsed = true, on = true, rearOk = false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(off))
                {
                    on = doc.RootElement.GetProperty("on").GetBoolean();
                    foreach (JsonElement a in doc.RootElement.GetProperty("actions").EnumerateArray()) if (a.GetProperty("id").GetString() == "rear") rearOk = a.GetProperty("available").GetBoolean();
                }
            }
            catch (Exception) { parsed = false; }
            check("the snapshot says when the budget rules are off, and the cards follow their triggers only", parsed && !on && rearOk && Math.Abs(b.Points - 2f) < 0.001f, off);
            check("each card carries its trigger and placement rule for the page", off.Contains("\"why\":\"\",\"fire\":12,\"cooldown\":60,\"minDistance\":30,\"unseen\":true,\"fallback\":\"wait\"}") && off.Contains("\"fire\":13,\"cooldown\":0,\"minDistance\":0,\"unseen\":false,\"fallback\":\"cancel\"}"), off);
        }
        {   // A painted area is measured to its nearest edge, and is 0 inside; a circle trigger to its anchor.
            var area = new AreaPlan();
            area.Parts.Add(new AreaPart { Outline = new List<P2> { new P2(0, 0), new P2(10, 0), new P2(10, 10), new P2(0, 10) }, Bottom = 0, Top = 3 });
            var shaped = new TriggerPlan { Id = 1, Pos = new V3(5, 0, 5), Area = area };
            var circle = new TriggerPlan { Id = 2, Pos = new V3(5, 0, 5) };
            check("a point inside a painted area is 0 m from it", Budget.Distance(shaped, new V3(2, 0, 2)) == 0f, "");
            check("a point outside a painted area is measured to its nearest edge", Math.Abs(Budget.Distance(shaped, new V3(25, 0, 5)) - 15f) < 0.001f, Budget.Distance(shaped, new V3(25, 0, 5)).ToString());
            check("a circle trigger is measured to its anchor", Math.Abs(Budget.Distance(circle, new V3(5, 0, 25)) - 20f) < 0.001f, "");
        }
        {   // Placement: too close, in view, out of view behind the player, and in view but behind a wall.
            var a = new ActionPlan { Id = "rear", Cost = 3, Fire = 12, MinDistance = 30f, Unseen = true };
            var zone = new TriggerPlan { Id = 12, Pos = new V3(0, 0, -300) };     // far behind, beyond the sight range: only the spawn points matter here
            var player = new List<V3> { new V3(0, 0, 0) };
            string near = Budget.Placement(a, zone, new List<V3> { new V3(0, 0, 20) }, player, Ahead, null);
            string seen = Budget.Placement(a, zone, new List<V3> { new V3(0, 0, 50) }, player, Ahead, null);
            string behind = Budget.Placement(a, zone, new List<V3> { new V3(0, 0, -50) }, player, Ahead, null);
            string walled = Budget.Placement(a, zone, new List<V3> { new V3(0, 0, 50) }, player, Ahead, (eye, p) => false);
            string unknownFacing = Budget.Placement(a, zone, new List<V3> { new V3(0, 0, -50) }, player, new List<float> { float.NaN }, null);
            check("an enemy too close to a player is not safe", near.StartsWith("a player is 20 m from where an enemy would appear", StringComparison.Ordinal), near);
            check("an enemy in a player's view is not safe without a sight test", seen == "a player can see where an enemy would appear", seen);
            check("an enemy behind the player, far enough away, is safe", behind == "", behind);
            check("an enemy in view but behind a wall is safe", walled == "", walled);
            check("a player whose facing is unknown is taken to face the spawn", unknownFacing != "", unknownFacing);
            check("a time trigger's anchor is never measured, only its enemies", zone.When == TriggerPlan.OnZone && Budget.Placement(a, new TriggerPlan { Id = 12, When = TriggerPlan.OnTime }, new List<V3>(), player, Ahead, null) == "", "");
        }
    }
}
