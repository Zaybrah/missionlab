using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// ML-052: the author-bounded pressure director. It only plays the director block's own cards, never past the band's spend,
/// the budget, the cap on living enemies or a card's placement rule, and says for every card it passes over which observation
/// and which bound ruled it out. It rests after a casualty, holds when an observation is unknown, decides nothing while a
/// spend is unsettled, tries a failed card once more at most, and gives the same decisions for the same observations; preview
/// decides alike and fires nothing. The format round-trips and the strict reader refuses bad bounds with the editor's codes.
/// </summary>
static class Ml052Tests
{
    private static string Root()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        return root;
    }

    private static string Codes(List<Finding> f) => string.Join(" | ", f.Where(x => x.Level == Finding.Error).Select(x => x.Code + " " + x.Path));

    // Three cards: a cheap flare (brings nobody), a rear group with a placement rule (3 enemies), a big push (5 enemies).
    private static DirectorPlan Cards(int budget, PressurePlan p) => new DirectorPlan
    {
        Budget = budget,
        Actions = new List<ActionPlan>
        {
            new ActionPlan { Id = "flare", Name = "Flare", Cost = 2, Fire = 11 },
            new ActionPlan { Id = "rear", Name = "Rear group", Cost = 4, Fire = 12, MinDistance = 30f, Unseen = true },
            new ActionPlan { Id = "push", Name = "Push", Cost = 6, Fire = 13 },
        },
        Pressure = p,
    };

    private static readonly Dictionary<int, int> Bringing = new Dictionary<int, int> { { 11, 0 }, { 12, 3 }, { 13, 5 } };

    private static PressurePlan Bands(string mode, params PressureBand[] bands) => new PressurePlan { Mode = mode, Bands = bands.ToList() };
    private static PressureBand Band(string name) => new PressureBand { Name = name };

    private static PressureLook Look(float t, int players = 4, int enemies = 2, params string[] goals) =>
        new PressureLook { Elapsed = t, Players = players, Enemies = enemies, ActiveGoals = goals.ToList() };

    private static (PressureDirector d, Budget b) Make(DirectorPlan plan, Func<int, string> triggerWhy = null)
    {
        var b = new Budget(plan, triggerWhy);
        var d = new PressureDirector(plan.Pressure, b) { Brings = a => Bringing.TryGetValue(a.Fire, out int n) ? n : -1, Placement = a => "" };
        return (d, b);
    }

    private static PressureReject Rejected(PressureDecision x, string id) => x.Rejected.FirstOrDefault(r => r.Action == id);
    private static string Said(PressureDecision x) => x.Kind + " " + x.Action + " [" + string.Join(", ", x.Rejected.Select(r => r.Action + ":" + r.Constraint)) + "] " + x.Why;

    // Plays a spend as the card path would: paid under the director's command id, then answered.
    private static bool Play(PressureDirector d, Budget b, PressureDecision x, float t, string status)
    {
        bool paid = b.Spend(x.Action, x.Command, t, out _);
        if (status == CommandLedger.Failed) b.Refund(x.Command);
        d.Settle(x.Command, status, status == CommandLedger.Failed ? "the spawner refused" : "", t);
        return paid;
    }

    public static void Run(Action<string, bool, string> check)
    {
        {   // Over a whole mission it never spends past the budget, never chooses a card the author did not approve, and picks the dearest that fits.
            var plan = Cards(20, Bands(PressurePlan.On, Band("All")));
            var (d, b) = Make(plan);
            int spent = 0; var chosen = new List<string>(); bool paidEach = true, approved = true; float lowest = b.Points;
            for (float t = 0f; t <= 1200f; t += 1f)
            {
                if (!d.Due(t)) continue;
                PressureDecision x = d.Decide(t, Look(t));
                if (x.Kind != PressureDecision.Spend) continue;
                approved &= plan.Actions.Exists(a => a.Id == x.Action);
                paidEach &= Play(d, b, x, t, CommandLedger.Applied);
                spent += x.Cost; chosen.Add(x.Action + "@" + t); lowest = Math.Min(lowest, b.Points);
            }
            check("the dearest card that fits is played each time, then the cheaper one that still fits", string.Join(",", chosen) == "push@60,push@120,push@180,flare@240", string.Join(",", chosen));
            check("it never spends more than the budget", spent == 20 && lowest >= 0f && b.Points == 0f, spent + " spent, " + b.Points + " left");
            check("every card it played was one of the author's and was paid for through the budget", approved && paidEach, "");
            PressureDecision broke = d.Decide(1300f, Look(1300f));
            check("with the budget spent it holds, and every card says the budget ruled it out",
                  broke.Kind == PressureDecision.Hold && broke.Constraint == PressureDirector.NoCard && broke.Rejected.Count == 3 && broke.Rejected.All(r => r.Constraint == PressureDirector.Points && r.Observed.StartsWith("the budget: 0 of 20")), Said(broke));
        }
        {   // The band's spend caps one decision; ties go to the card listed first.
            var (d, _) = Make(Cards(20, Bands(PressurePlan.On, new PressureBand { Name = "Probe", Spend = 4 })));
            PressureDecision x = d.Decide(60f, Look(60f));
            PressureReject push = Rejected(x, "push");
            check("a card dearer than the band's spend is passed over, naming the band and its cap", x.Action == "rear" && push != null && push.Constraint == PressureDirector.SpendCap && push.Observed == "band 'Probe'" && push.Why == "costs 6, the band spends at most 4 per decision", Said(x));
            var tie = new DirectorPlan { Budget = 10, Actions = new List<ActionPlan> { new ActionPlan { Id = "a", Name = "A", Cost = 3, Fire = 11 }, new ActionPlan { Id = "b", Name = "B", Cost = 3, Fire = 13 } }, Pressure = Bands(PressurePlan.On, Band("All")) };
            var (dt, _) = Make(tie);
            check("of two cards that cost the same, the one listed first is played", dt.Decide(60f, Look(60f)).Action == "a", "");
        }
        {   // Placement: the cards' own distance and view rules, checked as Budget.Placement checks them, and said to be an approximation.
            var p = Bands(PressurePlan.Preview, new PressureBand { Name = "Probe", Spend = 4 });     // preview: each look is a fresh decision, nothing waits
            var (d, _) = Make(Cards(20, p));
            var appear = new List<V3> { new V3(40f, 0f, 40f) };
            var players = new List<V3>(); var yaws = new List<float>();
            d.Placement = a => Budget.Placement(a, null, appear, players, yaws, null);
            players.Add(new V3(20f, 0f, 40f)); yaws.Add(90f);
            PressureDecision near = d.Decide(60f, Look(60f));
            PressureReject r = Rejected(near, "rear");
            check("a card whose enemies would appear too near a player is passed over for minDistance, with the distance",
                  near.Action == "flare" && r != null && r.Constraint == PressureDirector.MinDistance && r.Why.Contains("20 m from where an enemy would appear (needs 30 m)") && r.Why.Contains(PressureDirector.Approximation), Said(near));
            players[0] = new V3(100f, 0f, 40f); yaws[0] = -90f;     // 60 m away, facing the spot
            PressureDecision seen = d.Decide(120f, Look(120f));
            r = Rejected(seen, "rear");
            check("a card whose enemies a player faces is passed over for unseen", seen.Action == "flare" && r != null && r.Constraint == PressureDirector.Unseen && r.Why.StartsWith("a player can see where an enemy would appear"), Said(seen));
            yaws[0] = 90f;                                          // the same player, facing away
            PressureDecision clear = d.Decide(180f, Look(180f));
            check("once nobody is near or facing it, the card is played", clear.Action == "rear" && Rejected(clear, "rear") == null, Said(clear));
            var (blind, _) = Make(Cards(20, p)); blind.Placement = null;
            PressureDecision none = blind.Decide(60f, Look(60f));
            check("without a placement check a card with a placement rule is never played", none.Action == "flare" && Rejected(none, "rear").Why == "no placement check is available", Said(none));
        }
        {   // The cap on living enemies counts what a card brings.
            var p = Bands(PressurePlan.Preview, Band("All")); p.MaxEnemies = 8;
            var (d, _) = Make(Cards(20, p));
            PressureDecision x = d.Decide(60f, Look(60f, 4, 5));
            PressureReject push = Rejected(x, "push");
            check("a card that would take the living enemies over the cap is passed over, with the count it saw",
                  x.Action == "rear" && push.Constraint == PressureDirector.MaxEnemies && push.Observed == "living enemies 5" && push.Why == "5 living + 5 it brings = 10, over the cap of 8", Said(x));
            d.Brings = a => a.Id == "push" ? -1 : Bringing[a.Fire];
            PressureDecision unknown = d.Decide(120f, Look(120f, 4, 0));
            check("a card whose enemies cannot be counted is never played under a cap", Rejected(unknown, "push").Constraint == PressureDirector.MaxEnemies && Rejected(unknown, "push").Why.Contains("could not be seen") && unknown.Action == "rear", Said(unknown));
        }
        {   // Rest after a casualty: fewer living players than last seen stops spending for the rest time.
            var p = Bands(PressurePlan.On, Band("All")); p.Rest = 60f;
            var (d, b) = Make(Cards(20, p));
            d.Watch(10f, 4); d.Watch(40f, -1); d.Watch(50f, 4);
            check("an unknown count between two equal ones is no casualty", d.Log.Count == 0, string.Join(" / ", d.Log.Select(e => e.Words())));
            d.Watch(70f, 3);
            PressureDecision during = d.Decide(90f, Look(90f, 3));
            PressureDecision edge = d.Decide(129.5f, Look(129.5f, 3));
            check("a casualty is logged with the counts it saw", d.Log[0].Constraint == PressureDirector.Rest && d.Log[0].Observed == "living players 4 -> 3", d.Log[0].Words());
            check("for the rest time nothing is spent, and the hold says how long is left", during.Kind == PressureDecision.Hold && during.Constraint == PressureDirector.Rest && during.Why.Contains("40 s left") && edge.Constraint == PressureDirector.Rest && b.Points == 20f && d.Waiting == "", Said(during));
            PressureDecision after = d.Decide(130f, Look(130f, 3));
            check("when the rest is over it spends again", after.Kind == PressureDecision.Spend, Said(after));
        }
        {   // Unknown observations hold, spend nothing and try again after the shortest interval.
            var p = Bands(PressurePlan.On, new PressureBand { Name = "Phone", Goals = new List<string> { "phone" }, Every = 30f }, new PressureBand { Name = "Rest of it", Every = 90f });
            p.MaxEnemies = 20;
            var (d, b) = Make(Cards(20, p));
            var cases = new List<(string what, PressureLook look, string say)>
            {
                ("the time", new PressureLook { Elapsed = float.NaN, Players = 4, Enemies = 2, ActiveGoals = new List<string>() }, "the time since the start"),
                ("the players", new PressureLook { Elapsed = 60f, Players = -1, Enemies = 2, ActiveGoals = new List<string>() }, "the living players"),
                ("the goals", new PressureLook { Elapsed = 60f, Players = 4, Enemies = 2, ActiveGoals = null }, "the active goals"),
                ("the enemies under a cap", new PressureLook { Elapsed = 60f, Players = 4, Enemies = -1, ActiveGoals = new List<string>() }, "the living enemies"),
            };
            foreach (var c in cases)
            {
                PressureDecision x = d.Decide(60f, c.look);
                check("with " + c.what + " unknown it holds, names the observation and spends nothing",
                      x.Kind == PressureDecision.Hold && x.Constraint == PressureDirector.Unknown && x.Why.StartsWith(c.say + " could not be seen") && b.Points == 20f && d.Waiting == "" && d.NextAt == 90f, Said(x));
            }
            p.MaxEnemies = 0; p.Bands.RemoveAt(0);
            var (free, _) = Make(Cards(20, p));
            PressureDecision unneeded = free.Decide(60f, new PressureLook { Elapsed = 60f, Players = 4, Enemies = -1, ActiveGoals = null });
            check("an unknown it does not need (no cap, no band waits for goals) does not hold it", unneeded.Kind == PressureDecision.Spend, Said(unneeded));
        }
        {   // Bands in the author's order: the first whose time, goals and players all hold.
            var p = Bands(PressurePlan.Preview, new PressureBand { Name = "Late", From = 300f }, new PressureBand { Name = "Phone", Goals = new List<string> { "phone" }, MinPlayers = 3 },
                          new PressureBand { Name = "Quiet", Spend = 0 });
            var (d, _) = Make(Cards(20, p));
            PressureDecision quiet = d.Decide(60f, Look(60f, 4, 2));
            check("before its time and without its goal the quiet band applies, and it spends nothing", quiet.Band == "Quiet" && quiet.Constraint == PressureDirector.NoCard && quiet.Rejected.All(r => r.Constraint == PressureDirector.SpendCap) && quiet.Why.StartsWith("no approved card fits now"), Said(quiet));
            check("it says why the earlier bands did not apply", quiet.Why.Contains("'Late' starts at 300 s, 60 s have passed") && quiet.Why.Contains("'Phone' needs goal 'phone' active") && d.Decide(61f, Look(61f, 2, 2, "phone")).Band == "Quiet", quiet.Why);
            check("with the goal active and enough players the goal's band applies", d.Decide(62f, Look(62f, 3, 2, "phone")).Band == "Phone", "");
            check("from its time the first band applies whatever comes after", d.Decide(300f, Look(300f, 3, 2, "phone")).Band == "Late", "");
            var (none, _) = Make(Cards(20, Bands(PressurePlan.On, new PressureBand { Name = "Late", From = 300f })));
            PressureDecision early = none.Decide(60f, Look(60f));
            check("when no band applies it holds and names each band's reason", early.Constraint == PressureDirector.NoBand && early.Why.Contains("'Late' starts at 300 s, 60 s have passed"), early.Why);
        }
        {   // Preview decides exactly as "on" would, sends and spends nothing.
            var (on, bOn) = Make(Cards(12, Bands(PressurePlan.On, new PressureBand { Name = "All", Spend = 4 })));
            var (pre, bPre) = Make(Cards(12, Bands(PressurePlan.Preview, new PressureBand { Name = "All", Spend = 4 })));
            PressureDecision a = on.Decide(60f, Look(60f, 4, 2)), p = pre.Decide(60f, Look(60f, 4, 2));
            string Shape(PressureDecision x) => x.Band + "|" + x.Action + "|" + x.Cost + "|" + string.Join(",", x.Rejected.Select(r => r.Action + ":" + r.Constraint + ":" + r.Why));
            check("preview logs the same choice and the same reasons as on", Shape(a) == Shape(p) && a.Kind == PressureDecision.Spend && p.Kind == PressureDecision.Preview, Shape(a) + " / " + Shape(p));
            check("preview sends nothing and spends nothing", p.Command == "" && pre.Waiting == "" && bPre.Points == 12f && a.Command == "pressure-1" && on.Waiting == "pressure-1", p.Command + " " + bPre.Points);
            check("preview's line says it would spend", p.Words().StartsWith("preview: would spend 'rear'"), p.Words());
        }
        {   // While a spend is unsettled nothing more is decided; a failed card is refunded once, retried once, then left alone.
            var (d, b) = Make(Cards(20, Bands(PressurePlan.On, Band("All"))));
            PressureDecision first = d.Decide(60f, Look(60f));
            bool paid = b.Spend(first.Action, first.Command, 60f, out _);
            PressureDecision busy = d.Decide(61f, Look(61f), true);
            check("while its spend has not settled it decides nothing more, even when asked by hand", paid && busy.Kind == PressureDecision.Hold && busy.Constraint == PressureDirector.Pending && busy.Command == "" && b.Points == 14f, Said(busy));
            bool back = b.Refund(first.Command), twice = b.Refund(first.Command);
            bool replay = b.Spend(first.Action, first.Command, 62f, out string why);
            check("a failed card is refunded once, and its command cannot be paid again", back && !twice && !replay && b.Points == 20f, why);
            check("its result is taken once, and an unknown command is ignored", d.Settle(first.Command, CommandLedger.Failed, "the spawner refused", 62f) && !d.Settle(first.Command, CommandLedger.Failed, "again", 63f) && !d.Settle("page-7", CommandLedger.Failed, "", 63f), "");
            PressureDecision second = d.Decide(120f, Look(120f));
            check("a card that failed once may be tried once more, under a new command", second.Action == "push" && second.Command == "pressure-2", Said(second));
            Play(d, b, second, 120f, CommandLedger.Failed);
            PressureDecision third = d.Decide(180f, Look(180f));
            check("after failing twice it is not tried again, and says so", third.Action == "rear" && Rejected(third, "push").Constraint == PressureDirector.Failed && Rejected(third, "push").Why.Contains("failed 2 times"), Said(third));
            check("nothing was paid for the two failures", b.Points == 20f, b.Points.ToString());
        }
        {   // The budget's own refusals: a card resting, and a trigger that can no longer be fired.
            var plan = Cards(30, Bands(PressurePlan.On, Band("All")));
            plan.Actions[2].Cooldown = 120f;
            var fired = new HashSet<int>();
            var (d, b) = Make(plan, id => fired.Contains(id) ? "its trigger " + id + " has already fired" : "");
            PressureDecision x = d.Decide(60f, Look(60f)); Play(d, b, x, 60f, CommandLedger.Applied);
            PressureDecision y = d.Decide(120f, Look(120f));
            check("a card at rest is passed over for its cooldown", x.Action == "push" && Rejected(y, "push").Constraint == PressureDirector.Cooldown && Rejected(y, "push").Why == "cooldown: 60 s left" && y.Action == "rear", Said(y));
            Play(d, b, y, 120f, CommandLedger.Applied);
            fired.Add(13);
            PressureDecision z = d.Decide(240f, Look(240f));
            check("a card whose trigger cannot fire is passed over for its trigger", Rejected(z, "push").Constraint == PressureDirector.Trigger && Rejected(z, "push").Why == "its trigger 13 has already fired", Said(z));
        }
        {   // Manual: "spend now" skips the wait and the rest but no bound, "hold now" holds; both are labelled; off decides nothing.
            var p = Bands(PressurePlan.Off, new PressureBand { Name = "All", Spend = 4 }); p.Rest = 60f;
            var (d, b) = Make(Cards(20, p));
            check("off is never due and its decision is a hold", !d.Due(5000f) && d.Decide(60f, Look(60f)).Constraint == PressureDirector.Off, "");
            check("the host's mode switch is logged as manual", d.SetMode(PressurePlan.On, 70f, true) && d.Log.Last().Kind == PressureDecision.Mode && d.Log.Last().Manual && !d.SetMode("auto", 70f, true), d.Log.Last().Words());
            d.Watch(70f, 4); d.Watch(75f, 3);
            PressureDecision now = d.Decide(80f, Look(80f, 3), true);
            check("spend now during a rest plays a card within the band's cap, labelled manual", now.Kind == PressureDecision.Spend && now.Manual && now.Action == "rear" && now.Words().StartsWith("manual: spend 'rear'"), Said(now));
            Play(d, b, now, 80f, CommandLedger.Applied);
            float held = d.Hold(200f);
            PressureDecision hold = d.Decide(220f, Look(220f, 3));
            check("hold now stops automatic spending for the rest time, labelled manual", held == 60f && d.Log.Any(e => e.Manual && e.Kind == PressureDecision.Hold && e.Constraint == PressureDirector.Rest) && hold.Constraint == PressureDirector.Rest, Said(hold));
            PressureDecision off = d.Decide(400f, new PressureLook { Elapsed = 400f, Players = 3, Enemies = 2, ActiveGoals = new List<string>(), BudgetOff = true });
            check("with the budget rules off for the session it holds rather than spend outside the budget", off.Constraint == PressureDirector.BudgetOff && off.Kind == PressureDecision.Hold, Said(off));
        }
        {   // The same observations give the same decisions and the same log, byte for byte.
            (string json, int spends) Script()
            {
                var p = Bands(PressurePlan.On, new PressureBand { Name = "Phone", Goals = new List<string> { "phone" }, Every = 30f }, new PressureBand { Name = "Quiet", Spend = 2, Every = 45f });
                p.MaxEnemies = 9; p.Rest = 40f;
                var (d, b) = Make(Cards(16, p));
                int spends = 0;
                d.Noted = e => { if (e.Kind == PressureDecision.Spend) spends++; };
                int players = 4;
                for (float t = 0f; t <= 900f; t += 0.5f)
                {
                    if (t == 200f || t == 610f) players--;
                    d.Watch(t, players);
                    if (!d.Due(t)) continue;
                    PressureDecision x = d.Decide(t, Look(t, players, (int)(t / 100f), t >= 150f ? new[] { "phone" } : new string[0]));
                    if (x.Kind == PressureDecision.Spend) Play(d, b, x, t, t > 500f ? CommandLedger.Failed : CommandLedger.Applied);
                }
                return (d.Json(900f), spends);
            }
            var (one, many) = Script(); var (two, again) = Script();
            check("two runs on the same observations write the same decision log", one == two && many == again && many > 0 && one.Contains("\"constraint\":\"rest\""), one.Length + " / " + two.Length + ", " + many + " spends");
            using (JsonDocument doc = JsonDocument.Parse(one))
            {
                JsonElement log = doc.RootElement.GetProperty("log");
                bool keys = log.EnumerateArray().All(e => new[] { "at", "kind", "mode", "manual", "band", "action", "cost", "command", "constraint", "observed", "why", "words", "rejected" }.All(k => e.TryGetProperty(k, out _)));
                check("the snapshot is JSON and every entry carries every key", keys && log.GetArrayLength() > 0 && doc.RootElement.GetProperty("approximation").GetString() == PressureDirector.Approximation, "");
            }
        }

        string root = Root();
        check("the fixtures are found", root != null, AppContext.BaseDirectory);
        if (root == null) return;
        string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests", "fixtures", name));

        {   // The format: written in a fixed order, defaults left out, read back to the same text.
            string v27 = Fixture("all_fields_v27.json");
            var found = ScenarioFile.Read(v27, out MissionPlan strict);
            string once = MissionPlan.FromJson(v27).ToJson();
            check("all_fields_v27.json with its pressure bounds passes the strict reader", strict != null && strict.Director.Pressure != null && strict.Director.Pressure.Bands.Count == 2, Codes(found));
            check("and writes them back in the contract's key order with defaults left out",
                  once.Contains("\"pressure\":{\"mode\":\"preview\",\"bands\":[{\"name\":\"Upstairs push\",\"from\":120,\"minPlayers\":2,\"spend\":4,\"every\":45},{\"name\":\"Otherwise quiet\",\"spend\":0}],\"maxEnemies\":12,\"rest\":90}"), once);
            check("which reads back to the same text", MissionPlan.FromJson(once).ToJson() == once, "");
            // The same literal is checked against the editor's writer in tests/editor/cases/ml052-pressure.mjs.
            string written = MissionPlan.FromJson(Fixture("schema/valid/plan3_pressure.json")).ToJson();
            const string Director = "\"director\":{\"budget\":10,\"regen\":1,\"actions\":[{\"id\":\"rear\",\"name\":\"Rear reinforcements\",\"cost\":3,\"cooldown\":60,\"fire\":2,\"minDistance\":30,\"unseen\":true}],"
                                  + "\"pressure\":{\"mode\":\"on\",\"bands\":[{\"name\":\"Phone taken\",\"from\":120,\"goals\":[\"phone\"],\"minPlayers\":2,\"spend\":3,\"every\":30},{\"name\":\"Otherwise quiet\",\"spend\":0}],\"maxEnemies\":12,\"rest\":45}}";
            check("a band's goals are written as the editor writes them", written.Contains(Director) && MissionPlan.FromJson(written).ToJson() == written, written);
            var plain = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"alert\"}],\"director\":{\"budget\":5,\"actions\":[{\"id\":\"r\",\"name\":\"R\",\"cost\":1,\"fire\":1}],\"pressure\":{\"mode\":\"off\",\"bands\":[{\"name\":\" All \",\"from\":0,\"every\":60}],\"rest\":60}},\"spawns\":[{\"pos\":[0,0,0]}]}");
            check("every default is left out, and the name is trimmed", plain.ToJson().Contains("\"pressure\":{\"bands\":[{\"name\":\"All\"}]}}"), plain.ToJson());
            var none = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"alert\"}],\"director\":{\"budget\":5,\"actions\":[]},\"spawns\":[{\"pos\":[0,0,0]}]}");
            check("a director without pressure bounds writes no pressure key", none.Director.Pressure == null && !none.ToJson().Contains("pressure"), none.ToJson());
            check("any pressure bounds, even authored off, name the pressure capability, which this version knows; none do not",
                  Capabilities.Of(plain).Contains("pressure") && Pack.Known.Contains("pressure") && !Capabilities.Of(none).Contains("pressure") && Capabilities.Of(none).Contains("director"), string.Join(",", Capabilities.Of(plain)));
        }
        {   // The lenient reader clamps, keeps six bands, and leaves out a band it could not judge as meant.
            string Bands7 = string.Join(",", Enumerable.Range(1, 7).Select(i => "{\"name\":\"B" + i + "\",\"every\":5,\"minPlayers\":40,\"spend\":2000}"));
            var p = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"alert\"}],\"director\":{\"budget\":5,\"actions\":[],\"pressure\":{\"mode\":\"auto\",\"bands\":[" + Bands7 + "],\"maxEnemies\":900,\"rest\":-3}},\"spawns\":[{\"pos\":[0,0,0]}]}").Director.Pressure;
            check("values are clamped, the mode falls back to off and only six bands are kept",
                  p.Mode == PressurePlan.Off && p.Bands.Count == 6 && p.Bands.All(b => b.Every == 10f && b.MinPlayers == 16 && b.Spend == 1000) && p.MaxEnemies == 500 && p.Rest == 0f, MissionPlan.PressureJson(p));
            string clamped = "{\"bands\":[" + string.Join(",", Enumerable.Range(1, 6).Select(i => "{\"name\":\"B" + i + "\",\"minPlayers\":16,\"spend\":1000,\"every\":10}")) + "],\"maxEnemies\":500,\"rest\":0}";
            check("and are written as the editor writes the same reading (ml052-pressure.mjs)", MissionPlan.PressureJson(p) == clamped, MissionPlan.PressureJson(p));
            var lost = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"alert\"}],\"director\":{\"budget\":5,\"actions\":[],\"pressure\":{\"bands\":[{\"name\":\"Phone\",\"goals\":[\"phone\"]},{\"name\":\"Rest\"}]}},\"spawns\":[{\"pos\":[0,0,0]}]}");
            check("a band waiting for a goal the plan does not have is left out, not made to apply without it", lost.Director.Pressure.Bands.Count == 1 && lost.Director.Pressure.Bands[0].Name == "Rest", MissionPlan.PressureJson(lost.Director.Pressure));
            var empty = MissionPlan.FromJson("{\"triggers\":[{\"id\":1,\"when\":\"alert\"}],\"director\":{\"budget\":5,\"actions\":[],\"pressure\":{\"bands\":[]}},\"spawns\":[{\"pos\":[0,0,0]}]}");
            check("without a band there are no pressure bounds at all", empty.Director.Pressure == null, "");
        }
        {   // The strict reader's refusals; tests/editor/cases/ml052-pressure.mjs holds the editor to the same list.
            string Plan(string pressure) => "{\"schema\":\"missionlab-plan/1\",\"triggers\":[{\"id\":1,\"when\":\"alert\"}],\"director\":{\"budget\":5,\"actions\":[{\"id\":\"rear\",\"name\":\"Rear\",\"cost\":1,\"fire\":1}],\"pressure\":" + pressure + "},\"spawns\":[{\"pos\":[0,0,0]}]}";
            string seven = "[" + string.Join(",", Enumerable.Range(1, 7).Select(i => "{\"name\":\"B" + i + "\"}")) + "]";
            var cases = new List<(string what, string pressure, string want)>
            {
                ("pressure that is not an object", "3", "wrong-type director.pressure"),
                ("no bands", "{\"mode\":\"on\"}", "missing-field director.pressure.bands"),
                ("bands that are not a list", "{\"bands\":{}}", "wrong-type director.pressure.bands"),
                ("no band in the list", "{\"bands\":[]}", "out-of-range director.pressure.bands"),
                ("seven bands", "{\"bands\":" + seven + "}", "out-of-range director.pressure.bands"),
                ("a mode that is not text", "{\"mode\":true,\"bands\":[{\"name\":\"A\"}]}", "wrong-type director.pressure.mode"),
                ("an unknown mode", "{\"mode\":\"auto\",\"bands\":[{\"name\":\"A\"}]}", "unknown-value director.pressure.mode"),
                ("a goal in a plan without goals", "{\"bands\":[{\"name\":\"A\",\"goals\":[\"phone\"]}]}", "missing-ref director.pressure.bands[0].goals[0]"),
                ("a blank name", "{\"bands\":[{\"name\":\"  \"}]}", "missing-field director.pressure.bands[0].name"),
                ("a name that is not text", "{\"bands\":[{\"name\":3}]}", "wrong-type director.pressure.bands[0].name"),
                ("decisions too often", "{\"bands\":[{\"name\":\"A\",\"every\":9.5}]}", "out-of-range director.pressure.bands[0].every"),
                ("a cap that is not whole and a rest that is not a number", "{\"bands\":[{\"name\":\"A\"}],\"maxEnemies\":2.5,\"rest\":\"long\"}", "wrong-type director.pressure.maxEnemies | wrong-type director.pressure.rest"),
                ("every limit at its edge", "{\"mode\":\"preview\",\"bands\":[{\"name\":\"A\",\"from\":36000,\"minPlayers\":16,\"spend\":1000,\"every\":600},{\"name\":\"B\",\"from\":0,\"minPlayers\":0,\"spend\":0,\"every\":10}],\"maxEnemies\":500,\"rest\":600}", ""),
            };
            foreach (var c in cases)
            {
                string got = Codes(ScenarioFile.Read(Plan(c.pressure), out MissionPlan read));
                check((c.want == "" ? "accepted: " : "refused: ") + c.what, got == c.want && (c.want == "") == (read != null), got);
            }
            var bad = ScenarioFile.Read(Fixture("invalid/bad_pressure.json"), out MissionPlan notRead);
            check("invalid/bad_pressure.json names the unknown goal at its place", Codes(bad).Contains("missing-ref director.pressure.bands[0].goals[1]") && notRead == null, Codes(bad));
        }
        {   // The published schema names every key the writer writes, in the pressure block and in a band.
            var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "scenario.schema.json"))).RootElement;
            var defs = schema.GetProperty("$defs");
            var missing = new List<string>();
            var texts = new[] { MissionPlan.FromJson(Fixture("schema/valid/plan3_pressure.json")).ToJson(), MissionPlan.FromJson(Fixture("all_fields_v27.json")).ToJson() };
            foreach (string one in texts)
                using (JsonDocument doc = JsonDocument.Parse(one))
                {
                    JsonElement pressure = doc.RootElement.GetProperty("mission").GetProperty("director").GetProperty("pressure");
                    foreach (var k in pressure.EnumerateObject()) if (!defs.GetProperty("pressure").GetProperty("properties").TryGetProperty(k.Name, out _)) missing.Add("pressure." + k.Name);
                    foreach (var band in pressure.GetProperty("bands").EnumerateArray())
                        foreach (var k in band.EnumerateObject()) if (!defs.GetProperty("pressureBand").GetProperty("properties").TryGetProperty(k.Name, out _)) missing.Add("band." + k.Name);
                }
            check("the schema names every pressure key the plugin writes", missing.Count == 0 && schema.GetProperty("properties").GetProperty("director").GetProperty("properties").TryGetProperty("pressure", out _), string.Join(", ", missing));
        }
    }
}
