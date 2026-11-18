using MissionLab.Core;

// The asset-free suite: nothing here reads a map file or needs the game. Run with ./test.sh
// One extra use: "dotnet run --project tests/Pure -- --read <scenario.json> ..." reads files the way the plugin does and says what it finds.
if (args.Length >= 2 && args[0] == "--read")
{
    int refused = 0;
    foreach (string path in args.Skip(1))
    {
        var findings = ScenarioFile.Read(File.ReadAllText(path), out MissionPlan read);
        var stops = read == null ? new List<Finding>() : Completion.Check(read, null, 0);
        Console.WriteLine(Path.GetFileName(path) + ": " + (read == null ? "REFUSED" : Completion.Blocks(stops) ? "read, but cannot be finished as written" : "ok, " + read.Spawns.Count + " enemies, " + read.Triggers.Count + " triggers"));
        foreach (Finding f in findings.Concat(stops)) Console.WriteLine("  " + f.Level + " " + f.Code + " " + f);
        if (read == null) refused++;
    }
    return refused == 0 ? 0 : 1;
}
// "--roundtrip <scenario.json> ..." writes each file back the way the plugin does (FromJson, then ToJson), for tests/editor/run.mjs.
// Each file starts with a line "#missionlab-roundtrip <path>"; no line of the JSON can begin with '#'.
if (args.Length >= 2 && args[0] == "--roundtrip")
{
    // Written as UTF-8 bytes: on Windows the console's code page would turn letters of other scripts into question marks.
    var utf8 = new System.Text.UTF8Encoding(false);
    int broken = 0;
    using (Stream stdout = Console.OpenStandardOutput())
    {
        foreach (string path in args.Skip(1))
        {
            string written;
            try { written = MissionPlan.FromJson(File.ReadAllText(path)).ToJson(); }
            catch (Exception e) { written = "not read: " + e.Message.Replace('\n', ' ').Replace('\r', ' '); broken++; }
            byte[] bytes = utf8.GetBytes("#missionlab-roundtrip " + path + "\n" + written + "\n");
            stdout.Write(bytes, 0, bytes.Length);
        }
    }
    return broken == 0 ? 0 : 1;
}
int failures = 0, checks = 0;
void Check(string what, bool ok, string more = "")
{
    checks++;
    if (ok) return;
    failures++;
    Console.WriteLine("  FAIL: " + what + (more == "" ? "" : " | " + more));
}
MissionPlan Plan(string json) => MissionPlan.FromJson(json);
string Fired(List<TriggerEvent> ev) => string.Join(",", ev.Where(e => e.Kind == TriggerEvent.Fire).Select(e => e.Id));
TriggerEngine Engine(MissionPlan p, float start = 0f) { var e = new TriggerEngine(); e.Reset(start); e.Sync(p.Triggers); return e; }
const string OneEnemy = "\"spawns\":[{\"pos\":[0,0,0]}]";

// ---------------------------------------------------------------- trigger rules (ML-004)
Console.WriteLine("trigger rules");
{   // Arming a trigger again does not take back that it fired.
    var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"zone\",\"pos\":[0,0,0],\"radius\":5,\"repeat\":20},{\"id\":2,\"when\":\"alert\"},{\"id\":3,\"when\":\"all\",\"of\":[1,2]}]," + OneEnemy + "}");
    var e = Engine(p); var w = new World();
    w.Inside.Add(1);
    Check("a zone fires when someone is inside", Fired(e.Step(5, w)) == "1");
    w.Inside.Clear();
    Check("nothing more while it is latched", Fired(e.Step(10, w)) == "");
    Check("armed again after its pause, it waits for its condition", Fired(e.Step(26, w)) == "" && !e.State(1).Latched && e.EverFired(1));
    w.Alert = true;
    Check("all(A,B) fires when B comes later, although A is armed again", Fired(e.Step(30, w)) == "2,3", Fired(e.Step(30, w)));
    w.Inside.Add(1);
    var again = e.Step(31, w);
    Check("the repeating zone fires a second time, marked as again", Fired(again) == "1" && again[0].Again && e.Fires(1) == 2);
}
{   // A timer with a repeat and a cap: 1:00, 2:30, 4:00, as seen in the game on 16 November.
    var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"time\",\"delay\":60,\"repeat\":90,\"times\":3}]," + OneEnemy + "}");
    var e = Engine(p, 100f); var w = new World(); var at = new List<float>();
    for (float t = 100; t <= 600; t += 0.5f) if (Fired(e.Step(t, w)) == "1") at.Add(t - 100);
    Check("timer repeat fires at 60, 150 and 240 and then stops", string.Join(",", at) == "60,150,240", string.Join(",", at));
    Check("after its last firing it is exhausted", e.Phase(p.Triggers[0], 600, out _) == TriggerEngine.Exhausted);
}
{   // A condition that repeats waits for its condition and its delay again.
    var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"delay\":5,\"repeat\":10}]," + OneEnemy + "}");
    var e = Engine(p); var w = new World { Alert = true }; var at = new List<float>();
    for (float t = 0; t <= 40; t += 1f) if (Fired(e.Step(t, w)) == "1") at.Add(t);
    Check("condition repeat: delay, pause, condition again, delay again", string.Join(",", at) == "5,20,35", string.Join(",", at));
}
{   // The countdown is told at its start, at one minute and at ten seconds.
    var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"intel\"},{\"id\":2,\"when\":\"after\",\"after\":1,\"delay\":150,\"warn\":true},{\"id\":3,\"when\":\"after\",\"after\":1,\"delay\":400,\"warn\":true},{\"id\":4,\"when\":\"time\",\"delay\":30,\"warn\":true}]," + OneEnemy + "}");
    var e = Engine(p); var w = new World(); var told = new List<string>(); float fired2 = -1;
    w.Intel.Add("*");
    for (float t = 0; t <= 200; t += 0.5f) { var ev = e.Step(t, w); foreach (var x in ev) { if (x.Kind == TriggerEvent.Warn) told.Add(x.Id + ":" + x.Step + "@" + t); else if (x.Id == 2) fired2 = t; } }
    Check("countdown of 150 s: start at 4 s, one minute at 90 s, ten seconds at 140 s", told.Contains("2:4@4") && told.Contains("2:1@90") && told.Contains("2:2@140") && fired2 == 150, string.Join(" ", told));
    Check("a wait of 400 s also tells five minutes", told.Contains("3:3@100"));
    Check("a timer from the mission's start gives no start message, but its ten seconds", !told.Exists(s => s.StartsWith("4:4")) && told.Contains("4:2@20"));
    Check("every step is told once", told.Count == told.Distinct().Count());
}
{   // The order of the list does not change what happens.
    string a = "{\"id\":1,\"when\":\"time\",\"delay\":0}", b = "{\"id\":2,\"when\":\"after\",\"after\":1}", c = "{\"id\":3,\"when\":\"after\",\"after\":2}", d = "{\"id\":4,\"when\":\"any\",\"of\":[3,9]}";
    var forward = Engine(Plan("{\"triggers\":[" + a + "," + b + "," + c + "," + d + "]," + OneEnemy + "}"));
    var backward = Engine(Plan("{\"triggers\":[" + d + "," + c + "," + b + "," + a + "]," + OneEnemy + "}"));
    var w = new World();
    Check("a chain of four fires in one look, whatever the order in the file", Fired(forward.Step(0, w)) == "1,2,3,4" && Fired(backward.Step(0, w)) == "1,2,3,4", Fired(backward.Step(0, w)));
}
{   // Circles are named and never fire; the rest of the mission goes on.
    var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"after\",\"after\":2},{\"id\":2,\"when\":\"all\",\"of\":[1,3]},{\"id\":3,\"when\":\"time\"},{\"id\":4,\"when\":\"after\",\"after\":4},{\"id\":5,\"when\":\"after\",\"after\":1}]," + OneEnemy + "}");
    var e = Engine(p); var w = new World();
    Check("two circles are found and named", e.Problems.Count == 2 && e.Problems[0].Contains("1, 2") && e.Problems[1].Contains("trigger 4 waits for itself"), string.Join(" | ", e.Problems));
    string f = ""; for (float t = 0; t < 50; t++) f += Fired(e.Step(t, w));
    Check("only the trigger outside the circles fires", f == "3" && e.InCircle(1) && e.InCircle(2) && e.InCircle(4) && !e.InCircle(5), f);
    Check("the page can say why", e.Phase(p.Triggers[0], 10, out _) == TriggerEngine.Circle && e.Phase(p.Triggers[4], 10, out _) == TriggerEngine.Waiting);
}
{   // Deleting a trigger forgets it; the others keep their state. A mission restart forgets everything.
    var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"time\"},{\"id\":2,\"when\":\"hold\",\"pos\":[0,0,0],\"radius\":5,\"hold\":15},{\"id\":3,\"when\":\"alert\"}]," + OneEnemy + "}");
    var e = Engine(p); var w = new World(); w.Inside.Add(2); w.Alert = true;
    e.Step(0, w); e.Step(5, w);
    Check("before the edit: 1 and 3 fired, 2 is being held", e.EverFired(1) && e.EverFired(3) && e.State(2).InsideSince == 0f);
    var without = new List<TriggerPlan> { p.Triggers[1], p.Triggers[2] };
    e.Sync(without, 6f);
    Check("a deleted trigger is forgotten, the others are not", !e.Known(1) && !e.EverFired(1) && e.EverFired(3) && e.State(2).InsideSince == 0f);
    e.Sync(p.Triggers, 7f);
    Check("made again under the same number it starts clean, and a timer counts from then", e.Fires(1) == 0 && e.State(1).MetAt == 7f && Fired(e.Step(7, w)) == "1");
    p.Triggers[2].When = TriggerPlan.OnIntel; e.Sync(p.Triggers, 8f);
    Check("changed into another kind it starts clean", !e.EverFired(3));
    p.Triggers[1].When = TriggerPlan.OnZone; e.Sync(p.Triggers, 9f);
    Check("a hold made into a plain zone keeps what it knows", e.State(2).InsideSince == 0f);
    e.Reset(0f); e.Sync(p.Triggers);
    Check("a new mission begins with nothing remembered", !e.EverFired(1) && !e.EverFired(3) && e.State(2).InsideSince == null);
}
{   // Fire by hand.
    var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"intel\"},{\"id\":2,\"when\":\"after\",\"after\":1},{\"id\":3,\"when\":\"alert\",\"repeat\":30,\"chance\":50},{\"id\":4,\"when\":\"after\",\"after\":0}]," + OneEnemy + "}");
    p.Triggers[2].Off = true;
    var e = Engine(p); var w = new World();
    Check("nothing fires by itself", Fired(e.Step(1, w)) == "");
    Check("by hand it fires whatever its condition", e.FireNow(1, 2, out TriggerEvent ev, out _) && ev.ByHand && e.EverFired(1));
    Check("and what waits for it follows", Fired(e.Step(3, w)) == "2");
    Check("a second time is refused, with the reason", !e.FireNow(1, 4, out _, out string why1) && why1 == "it has already fired", "");
    Check("a trigger that sat out can still be fired by hand", e.FireNow(3, 5, out _, out _));
    Check("while it waits for its repeat the answer says when", !e.FireNow(3, 15, out _, out string why3) && why3.Contains("20 s"), "");
    Check("the by-hand trigger never fires by itself", Fired(e.Step(100, w)) == "" && e.FireNow(4, 101, out _, out _));
    Check("an unknown number is refused", !e.FireNow(99, 1, out _, out string why9) && why9 == "there is no trigger 99");
}
{   // Hold: leaving starts the count again.
    var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"hold\",\"pos\":[0,0,0],\"radius\":5,\"hold\":15}]," + OneEnemy + "}");
    var e = Engine(p); var w = new World(); var at = new List<float>();
    for (float t = 0; t <= 60; t++) { if (t == 0 || t == 20) w.Inside.Add(1); if (t == 10) w.Inside.Clear(); if (Fired(e.Step(t, w)) == "1") at.Add(t); if (t == 5) Check("held for 5 of 15 s", e.Phase(p.Triggers[0], t, out float left) == TriggerEngine.Holding && left == 10f); }
    Check("ten seconds inside, out, then fifteen seconds inside: fires at 35", string.Join(",", at) == "35", string.Join(",", at));
}
{   // What the page is told about each trigger.
    var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"delay\":30},{\"id\":2,\"when\":\"time\",\"delay\":40},{\"id\":3,\"when\":\"zone\",\"pos\":[0,0,0],\"radius\":5,\"repeat\":50},{\"id\":4,\"when\":\"intel\",\"chance\":10}]," + OneEnemy + "}");
    p.Triggers[3].Off = true;
    var e = Engine(p); var w = new World { Alert = true }; w.Inside.Add(3);
    e.Step(10, w);
    Check("counting down after its condition", e.Phase(p.Triggers[0], 15, out float l1) == TriggerEngine.Counting && l1 == 25f);
    Check("a timer counts down from the start", e.Phase(p.Triggers[1], 15, out float l2) == TriggerEngine.Counting && l2 == 25f);
    Check("cooling until its repeat", e.Phase(p.Triggers[2], 15, out float l3) == TriggerEngine.Cooling && l3 == 45f);
    Check("off when it lost its roll", e.Phase(p.Triggers[3], 15, out _) == TriggerEngine.Off);
    e.Step(45, w);
    Check("fired, for good", e.Phase(p.Triggers[0], 50, out _) == TriggerEngine.Fired && e.Phase(p.Triggers[1], 50, out _) == TriggerEngine.Fired);
}

// ---------------------------------------------------------------- can the mission be finished (ML-001)
Console.WriteLine("completion");
string Broken(MissionPlan p, Variants.Out gone = null, int players = 0) => string.Join(" || ", Completion.Check(p, gone, players).Where(f => f.Level == Finding.Error).Select(f => f.ToString()));
{
    var p = Plan("{\"extraction\":{\"after\":1},\"triggers\":[{\"id\":1,\"when\":\"intel\",\"chance\":40,\"name\":\"Phone\"}]," + OneEnemy + "}");
    Check("a healthy scenario passes", Broken(p) == "");
    p.Triggers[0].Off = true;
    string b = Broken(p);
    Check("extraction after a trigger that sat out is refused, with its name and chance", b.StartsWith("triggers[0]: nobody could leave") && b.Contains("'Phone'") && b.Contains("40%"), b);
}
{   // extraction -> A -> B
    var p = Plan("{\"extraction\":{\"after\":1},\"triggers\":[{\"id\":1,\"when\":\"after\",\"after\":2,\"name\":\"A\"},{\"id\":2,\"when\":\"alert\",\"chance\":50,\"name\":\"B\"}]," + OneEnemy + "}");
    p.Triggers[1].Off = true;
    string b = Broken(p);
    Check("the broken link two steps away is named", b.Contains("'A'") && b.Contains("'B' sits this mission out"), b);
}
{   // all needs every branch, any needs one that can fire
    string triggers = "\"triggers\":[{\"id\":1,\"when\":\"KIND\",\"of\":[2,3]},{\"id\":2,\"when\":\"alert\",\"chance\":50},{\"id\":3,\"when\":\"intel\",\"chance\":50}]";
    var all = Plan("{\"extraction\":{\"after\":1}," + triggers.Replace("KIND", "all") + "," + OneEnemy + "}");
    all.Triggers[1].Off = true;
    Check("all: one branch out breaks it", Broken(all).Contains("needs all of its list"), Broken(all));
    var any = Plan("{\"extraction\":{\"after\":1}," + triggers.Replace("KIND", "any") + "," + OneEnemy + "}");
    any.Triggers[1].Off = true;
    Check("any: one branch out is fine while another can fire", Broken(any) == "", Broken(any));
    any.Triggers[2].Off = true;
    Check("any: every branch out breaks it", Broken(any).Contains("has no trigger in its list that can fire"), Broken(any));
}
{   // another way out, by hand, missing, circle
    var p = Plan("{\"extraction\":{\"after\":1},\"triggers\":[{\"id\":1,\"when\":\"intel\",\"chance\":40},{\"id\":2,\"when\":\"time\",\"delay\":600,\"extraction\":\"open\"}]," + OneEnemy + "}");
    p.Triggers[0].Off = true;
    Check("another trigger that opens the extraction is a way out", Broken(p) == "", Broken(p));
    var hand = Plan("{\"extraction\":{\"after\":1},\"triggers\":[{\"id\":1,\"when\":\"after\",\"after\":0}]," + OneEnemy + "}");
    Check("a by-hand trigger on the way out is refused", Broken(hand).Contains("fired by hand"), Broken(hand));
    hand.GameMaster = true;
    Check("unless the scenario says it needs a Game Master", Broken(hand) == "" && MissionPlan.FromJson(hand.ToJson()).GameMaster && !Plan("{" + OneEnemy + "}").ToJson().Contains("gameMaster"));
    var missing = Plan("{\"extraction\":{\"after\":7},\"triggers\":[{\"id\":1,\"when\":\"alert\"}]," + OneEnemy + "}");
    var notes = Completion.Check(missing, null, 0);
    Check("a gate on a trigger that does not exist is a note: the game's own rule holds", notes.Count == 1 && notes[0].Level == Finding.Note && notes[0].Path == "extraction.after" && !Completion.Blocks(notes));
    var circle = Plan("{\"extraction\":{\"after\":1},\"triggers\":[{\"id\":1,\"when\":\"after\",\"after\":2},{\"id\":2,\"when\":\"after\",\"after\":1}]," + OneEnemy + "}");
    Check("a circle on the way out is refused", Broken(circle).Contains("waits for itself through other triggers"), Broken(circle));
}
{   // tagged enemies that can never all go down
    var p = Plan("{\"extraction\":{\"after\":1},\"triggers\":[{\"id\":1,\"when\":\"down\",\"tag\":\"officer\",\"count\":0},{\"id\":2,\"when\":\"alert\",\"chance\":30}],\"spawns\":[{\"pos\":[0,0,0],\"tag\":\"officer\"},{\"pos\":[1,0,0],\"tag\":\"Officer\",\"trigger\":2}]}");
    Check("tagged enemies: fine while each can appear", Broken(p) == "");
    p.Triggers[1].Off = true;
    Check("one tagged enemy waits for a trigger that sat out: 'all down' can never come", Broken(p).Contains("1 of them can never appear"), Broken(p));
    p.Triggers[0].Count = 1;
    Check("asking for one of them is still possible", Broken(p) == "", Broken(p));
    p.Spawns.Clear(); p.Spawns.Add(new SpawnPlan());
    Check("no enemy with the tag left", Broken(p).Contains("none is in this mission"), Broken(p));
}
{   // The dice: every roll that switches the gate's trigger off is refused, every other roll passes. The target person likewise.
    string src = "{\"extraction\":{\"after\":1},\"triggers\":[{\"id\":1,\"when\":\"intel\",\"chance\":50}]," +
                 "\"spawns\":[{\"pos\":[0,0,0]},{\"pos\":[1,0,0],\"hvt\":true,\"chance\":60},{\"pos\":[2,0,0],\"hvt\":true,\"either\":1,\"option\":1,\"minPlayers\":3},{\"pos\":[3,0,0],\"either\":1,\"option\":2}]}";
    int refusedGate = 0, refusedTarget = 0, passed = 0, wrong = 0; string sample = "";
    for (long seed = 1; seed <= 60; seed++)
    {
        var p = Plan(src);
        Variants.Out gone = Variants.Resolve(p, new Rng(seed), null, 2);
        var found = Completion.Check(p, gone, 2);
        bool gateOff = p.Triggers[0].Off, targetLeft = p.Spawns.Exists(s => s.Hvt);
        bool gateFound = found.Exists(f => f.Path == "triggers[0]"), targetFound = found.Exists(f => f.Path.StartsWith("spawns["));
        if (gateOff != gateFound || targetLeft == targetFound) wrong++;
        if (gateFound) refusedGate++; if (targetFound) { refusedTarget++; sample = Broken(p, gone, 2); } if (found.Count == 0) passed++;
    }
    Check("over 60 seeds the check agrees with the dice every time", wrong == 0 && refusedGate > 5 && refusedTarget > 5 && passed > 5, wrong + " wrong, " + refusedGate + "/" + refusedTarget + "/" + passed);
    Check("a lost target is named by its place in the file and the field that removed it", sample.Contains("spawns[1].chance: the target person at (1, 0)") && sample.Contains("spawns[2].minPlayers") && sample.Contains("needs 3 players and 2 are there"), sample);
    var same = Plan(src); var again = Plan(src);
    Variants.Resolve(same, new Rng(17), null, 2); Variants.Resolve(again, new Rng(17), null, 2);
    Check("the same seed and squad size give the same result", same.ToJson() == again.ToJson() && same.Triggers[0].Off == again.Triggers[0].Off);
}

// ---------------------------------------------------------------- failures and commands (ML-005, ML-006)
Console.WriteLine("resilience");
{
    var g = new Guard("browser link"); var other = new Guard("mission rules");
    float t = 0; int logged = 0, ran = 0;
    for (int frame = 0; frame < 6000; frame++, t += 1f / 60f)          // 100 seconds of a part that always fails
    {
        if (!g.Due(t)) continue;
        ran++; g.Failed(t, "boom"); logged++;
    }
    Check("a part that always fails is tried a handful of times, not 6000", ran >= 8 && ran <= 12 && g.Degraded, ran + " tries");
    Check("its pauses grow to a minute at most", g.RetryAt - t <= 60f && g.Pauses >= 4);
    Check("another part is not touched by it", other.Due(t) && !other.Degraded && other.Failures == 0);
    Check("the page can read its state and last error", g.Json(t).Contains("\"paused\":true") && g.Json(t).Contains("\"lastError\":\"boom\""), g.Json(t));
    var few = new Guard("picker"); for (int i = 0; i < 4; i++) few.Failed(1, "x");
    Check("four failures in a row do not pause a part", !few.Degraded && few.Due(1));
    Check("one success sets a paused part right, and says so once", g.Ok() && !g.Degraded && g.Due(t) && !g.Ok());
    g.Failed(t, "a \"quoted\" line\nwith a break"); 
    Check("errors with quotes and line breaks stay valid JSON", System.Text.Json.JsonDocument.Parse(g.Json(t)).RootElement.GetProperty("lastError").GetString() == "a \"quoted\" line\nwith a break");
    g.Fresh(); Check("a new mission starts every part fresh", g.Failures == 0 && g.LastError == "");
}
{
    var led = new CommandLedger(8);
    Check("a new number is taken as pending", led.Begin("c1", "s1", "/api/command", 1, out var e1) && e1.Status == CommandLedger.Pending);
    Check("the same number again is not new, and says pending", !led.Begin("c1", "s1", "/api/command", 2, out var again) && again.Status == CommandLedger.Pending);
    led.Finish("c1", CommandLedger.Applied, 200, "{\"ok\":1}", "1 enemy appears");
    Check("once done, a repeat gets the first answer back", !led.Begin("c1", "s1", "/api/command", 3, out var done) && done.Status == CommandLedger.Applied && done.Body == "{\"ok\":1}" && done.Code == 200);
    for (int i = 2; i <= 12; i++) led.Begin("c" + i, "s1", "/api/markers", i, out _);
    Check("only the newest are kept", !led.TryGet("c1", out _) && led.TryGet("c12", out _) && led.TryGet("c5", out _));
    Check("the page gets the newest few, newest last", led.RecentJson(3).Contains("c10") && led.RecentJson(3).EndsWith("\"said\":\"\"}]") && !led.RecentJson(3).Contains("\"c9\""), led.RecentJson(3));
    Check("only plain numbers are accepted", CommandLedger.Clean("a-B_9") == "a-B_9" && CommandLedger.Clean("a b") == "" && CommandLedger.Clean("x\"y") == "" && CommandLedger.Clean(new string('a', 65)) == "" && CommandLedger.Clean(null) == "");
}
{   // A batch reports each part; queued spawns settle later; a refreshed page can read the parts back (ML-006).
    var led = new CommandLedger(8);
    led.Begin("b1", "s1", "/api/markers", 1, out _);
    var parts = new Outcome();
    parts.Add(new CommandOp("move", "3", 0, CommandLedger.Applied, ""));
    parts.Add(new CommandOp("add", "7", 1, CommandLedger.Pending, "its enemy is on its way"));
    parts.Add(new CommandOp("remove", "4", -1, CommandLedger.Failed, "it is \"being\" spawned"));
    led.Record("b1", parts);
    Check("a repeat before the game answered is told to wait", !led.Begin("b1", "s1", "/api/markers", 2, out var early) && !early.Done);
    led.Finish("b1", CommandLedger.Applied, 200, "{}", "");
    led.TryGet("b1", out var b1);
    Check("some failed, some did not: partial, not applied", b1.Done && b1.Status == CommandLedger.Partial, b1.Status);
    string read = led.JsonOf("b1");
    var doc = System.Text.Json.JsonDocument.Parse(read).RootElement;
    var ops = doc.GetProperty("ops");
    Check("the parts can be read back by number, each with its own outcome", ops.GetArrayLength() == 3 && ops[1].GetProperty("ref").GetString() == "7" && ops[1].GetProperty("at").GetInt32() == 1
          && ops[2].GetProperty("status").GetString() == "failed" && ops[2].GetProperty("said").GetString() == "it is \"being\" spawned" && doc.GetProperty("left").GetInt32() == 0, read);
    Check("a queued spawn hears what became of it", led.Settle("b1", "7", CommandLedger.Applied, "its enemy exists") && !led.Settle("b1", "7", CommandLedger.Failed, "again"));
    Check("still partial: the removal failed", led.TryGet("b1", out b1) && b1.Status == CommandLedger.Partial && led.JsonOf("b1").Contains("its enemy exists"));
    Check("the short list in every state answer leaves the parts out", !led.RecentJson(5).Contains("\"ops\"") && led.RecentJson(5).Contains("\"done\":true"), led.RecentJson(5));

    var waits = new Outcome();
    waits.Add(new CommandOp("add", "1", 0, CommandLedger.Applied, ""));
    waits.Add(new CommandOp("add", "2", 1, CommandLedger.Pending, ""));
    Check("applied and pending: pending", waits.Overall(CommandLedger.Applied) == CommandLedger.Pending);
    led.Begin("b2", "s1", "/api/markers", 3, out _); led.Record("b2", waits); led.Finish("b2", CommandLedger.Applied, 200, "{}", "");
    Check("a mission that ends fails what never came", led.Abandon("the mission ended") == 1 && led.TryGet("b2", out var b2) && b2.Status == CommandLedger.Partial);
    var asked = new Outcome(); asked.Add(new CommandOp("order", "5", 0, CommandLedger.Unconfirmed, "told to walk there"));
    Check("only asked for is unconfirmed, never applied", asked.Overall(CommandLedger.Applied) == CommandLedger.Unconfirmed);
    Check("nothing to report keeps the answer's own status", new Outcome().Overall(CommandLedger.Applied) == CommandLedger.Applied);
    var all = new Outcome(); all.Add(new CommandOp("save", "", -1, CommandLedger.Failed, "disk full"));
    Check("every part failed: failed", all.Overall(CommandLedger.Applied) == CommandLedger.Failed);
    led.Begin("b3", "s1", "/api/command", 4, out _); led.Record("b3", all); led.Finish("b3", CommandLedger.Failed, 500, "{}", "");
    Check("an answer that failed as a whole stays failed", led.TryGet("b3", out var b3) && b3.Status == CommandLedger.Failed);

    var huge = new Outcome();
    for (int i = 0; i < Outcome.Keep + 50; i++) huge.Add(new CommandOp("add", i.ToString(), i, CommandLedger.Applied, ""));
    huge.Add(new CommandOp("add", "late", 999, CommandLedger.Failed, "no room"));
    Check("a huge batch lists a bounded number of parts and counts the rest", huge.Ops.Count == Outcome.Keep && huge.LeftOut == 51, huge.Ops.Count + " listed, " + huge.LeftOut + " left out");
    Check("a failure is listed even when the list is full", huge.Ops.Exists(o => o.Ref == "late") && huge.Overall(CommandLedger.Applied) == CommandLedger.Partial);
    Check("the bounded list is valid JSON", System.Text.Json.JsonDocument.Parse("{" + huge.Json() + "}").RootElement.GetProperty("ops").GetArrayLength() == Outcome.Keep);
}

// ---------------------------------------------------------------- shared reinforcement waves (ML-002)
Console.WriteLine("reinforcement waves");
{
    // after = 20: two deaths 19 seconds apart come back in ONE wave, 20 s after the first death.
    var r = new WaveRule { After = 20, Times = 2 };
    var log = new List<string>(); int dead = 0, asks = 0;
    for (float t = 0; t <= 200; t += 0.5f)
    {
        if (t == 10) dead = 1; if (t == 29) dead = 2;
        int back = r.State == WaveRule.Pending && t >= 31 ? 2 : 0;         // the game brings both back a second after it is asked
        if (back > 0) dead = 0;
        var act = r.Step(t, dead, back);
        if (act == WaveRule.Act.Ask) { asks++; r.Asked(t, true, ""); }
        if (act != WaveRule.Act.None) log.Add(act + "@" + t);
        if (t == 20) Check("ten seconds after the first death the page can say ten seconds are left", r.State == WaveRule.Counting && r.SecondsLeft(t) == 10f);
    }
    Check("two deaths 19 s apart: one announcement, one wave at 30 s, seen at 31 s", string.Join(" ", log) == "Announce@10 Ask@30 Observed@31", string.Join(" ", log));
    Check("one wave is counted, one is left", r.Done == 1 && r.WavesLeft == 1 && r.State == WaveRule.Idle && asks == 1);
}
{
    // The game is asked, and nobody comes back: the wave is not counted, and it is tried again a bounded number of times.
    var r = new WaveRule { After = 5, Times = 1 };
    int asks = 0, gaveUp = 0; var askedAt = new List<float>();
    for (float t = 0; t <= 300; t += 0.5f)
    {
        var act = r.Step(t, 1, 0);
        if (act == WaveRule.Act.Ask) { asks++; askedAt.Add(t); r.Asked(t, true, ""); }
        if (act == WaveRule.Act.GaveUp) gaveUp++;
        if (t == 7) Check("while a wave is pending the game is not asked again", r.State == WaveRule.Pending && asks == 1);
    }
    Check("a wave that nobody saw is not counted", r.Done == 0 && r.WavesLeft == 1 && r.State == WaveRule.Failed && r.Error.Contains("nobody was back"), r.State + " " + r.Error);
    Check("it is tried three times in all, then left alone", asks == 3 && gaveUp == 3, asks + " asks at " + string.Join(",", askedAt));
    Check("when everyone is alive again the rule is ready for the next death", r.Step(301, 0, 0) == WaveRule.Act.None && r.State == WaveRule.Idle && r.Step(302, 1, 0) == WaveRule.Act.Announce);
}
{
    // The call itself fails (no game manager, an exception): not counted either. A second wave after the cap never comes.
    var r = new WaveRule { After = 5, Times = 1 };
    r.Step(0, 1, 0); Check("the first look starts the clock", r.State == WaveRule.Counting);
    Check("after the wait the game is to be asked", r.Step(5, 1, 0) == WaveRule.Act.Ask);
    r.Asked(5, false, "the game's mission keeper was not found");
    Check("a call that fails leaves the wave unspent and says why", r.State == WaveRule.Failed && r.Done == 0 && r.Error.Contains("mission keeper"));
    Check("it is asked again ten seconds later", r.Step(14, 1, 0) == WaveRule.Act.None && r.Step(15, 1, 0) == WaveRule.Act.Ask);
    r.Asked(15, true, ""); Check("this time someone is back: counted", r.Step(16, 0, 1) == WaveRule.Act.Observed && r.Done == 1 && r.Spent);
    Check("the cap is reached: a later death starts no clock", r.Step(50, 1, 0) == WaveRule.Act.None && r.State == WaveRule.Idle);
    var off = new WaveRule(); Check("without a rule nothing happens", off.Step(1, 3, 0) == WaveRule.Act.None);
    var endless = new WaveRule { After = 5 }; Check("no cap: waves left is reported as unlimited", endless.WavesLeft == -1 && !endless.Spent);
    // One player back and another newly dead at the same moment: still a wave that happened; the clock starts again for the other.
    var mix = new WaveRule { After = 5 }; mix.Step(0, 1, 0); mix.Step(5, 1, 0); mix.Asked(5, true, "");
    Check("someone is back while another just died: counted, and the clock runs for the other", mix.Step(6, 1, 1) == WaveRule.Act.Observed && mix.Done == 1 && mix.State == WaveRule.Counting && mix.Since == 6f);
    r.Reset(); Check("a new mission starts with no wave spent", r.Done == 0 && r.State == WaveRule.Idle);
}

// ---------------------------------------------------------------- files: fixtures and the examples that ship (ML-010)
Console.WriteLine("files");
{
    string root = AppContext.BaseDirectory;
    while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
    Check("the repository's fixtures are found", root != null);
    if (root != null)
    {
        string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests", "fixtures", name));
        // Every field survives reading and writing, twice over.
        var all = Plan(Fixture("all_fields.json"));
        string once = all.ToJson(), twice = Plan(once).ToJson();
        Check("every field: what is written reads back to the same text", once == twice);
        foreach (string key in new[] { "\"gameMaster\":true", "\"power\":\"off\"", "\"skill\":\"veteran\"", "\"nvg\":true", "\"respawn\":{\"after\":20,\"times\":2}", "\"everyone\":true", "\"red\":true",
                                       "\"hold\":15", "\"rally\":30", "\"hunt\":true", "\"extraction\":\"open\"", "\"repeat\":30", "\"times\":3", "\"open\":false", "\"warn\":true", "\"end\":\"failed\"",
                                       "\"respawn\":true", "\"tag\":\"officer\"", "\"when\":\"door\"", "\"enemiesCanOpen\":false", "\"chance\":25", "\"lot\":3", "\"either\":1", "\"minPlayers\":2", "\"team\":4" })
            Check("every field: " + key + " is kept", once.Contains(key), key);
        Check("every field: eleven triggers, five enemies, a door and a thing", all.Triggers.Count == 11 && all.Spawns.Count == 5 && all.Doors.Count == 1 && all.Things.Count == 1);
        Check("every field: the scenario can be finished as written", !Completion.Blocks(Completion.Check(all, null, 0)), Broken(all));
        // A file from the first version: only a scene and positions.
        var old = Plan(Fixture("legacy_minimal.json"));
        Check("a file of the first version reads with plain defaults", old.Spawns.Count == 2 && old.Triggers.Count == 0 && old.ExtractionAfter == 0 && !old.GameMaster && old.RespawnAfter == 0f && old.Spawns[0].Chance == 100 && old.Spawns[0].Tag == "");
        Check("and gains none of the newer keys when written", !old.ToJson().Contains("respawn") && !old.ToJson().Contains("gameMaster") && !old.ToJson().Contains("tag"));
        // The defect of ML-001, as a file: half the rolls must be refused, the other half must pass.
        int refused = 0, passed = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            var p = Plan(Fixture("extraction_chance_defect.json"));
            var gone = Variants.Resolve(p, new Rng(seed), null, 1);
            if (Completion.Blocks(Completion.Check(p, gone, 1))) refused++; else passed++;
        }
        Check("the extraction-and-chance fixture is refused on the rolls that break it, and only on those", refused > 8 && passed > 8, refused + " refused, " + passed + " passed");
        // The contract (ML-007): files that must be refused, each with the place in the file. The editor is held to the same list.
        var expected = System.Text.Json.JsonDocument.Parse(Fixture("expected.json")).RootElement;
        foreach (string file in Directory.GetFiles(Path.Combine(root, "tests", "fixtures", "invalid"), "*.json"))
        {
            string name = Path.GetFileName(file), before = File.ReadAllText(file);
            var findings = ScenarioFile.Read(before, out MissionPlan notRead);
            string got = string.Join(" | ", findings.Where(f => f.Level == Finding.Error).Select(f => f.Code + " " + f.Path));
            string want = expected.TryGetProperty(name, out var list) ? string.Join(" | ", list.EnumerateArray().Select(x => x.GetString())) : "(not listed in expected.json)";
            Check("invalid/" + name + " is refused with the listed places", got == want && notRead == null, "got  " + got + "\n        want " + want);
            Check("invalid/" + name + " is left as it was", File.ReadAllText(file) == before);
        }
        foreach (string good in new[] { "all_fields.json", "all_fields_v2.json", "extraction_chance_defect.json" })
            Check(good + " passes the strict reader", !ScenarioFile.Blocks(ScenarioFile.Read(Fixture(good), out MissionPlan ok)) && ok != null, string.Join(" | ", ScenarioFile.Read(Fixture(good), out _).Select(f => f.ToString())));
        var legacy = ScenarioFile.Read(Fixture("legacy_minimal.json"), out MissionPlan oldPlan);
        Check("a file without a format line is read, with a note", oldPlan != null && legacy.Count == 1 && legacy[0].Level == Finding.Note && legacy[0].Code == "no-version");
        // Text with quotes, backslashes and a control character is written as valid JSON and reads back the same.
        var odd = Plan("{\"name\":\"The \\\"quiet\\\" house \\\\ north\",\"insertion\":\"Alpha \\\"2\\\"\",\"triggers\":[{\"id\":1,\"when\":\"alert\",\"name\":\"Say \\\"hi\\\"\",\"message\":\"C:\\\\path and a \\\"quote\\\"\"}],\"things\":[{\"name\":\"A\\\\K\",\"pos\":[0,0,0]}]," + OneEnemy + "}");
        string oddOut = odd.ToJson(); var oddBack = Plan(oddOut);
        Check("quotes and backslashes survive writing and reading", oddBack.Title == "The \"quiet\" house \\ north" && oddBack.Insertion == "Alpha \"2\"" && oddBack.Triggers[0].Name == "Say \"hi\"" && oddBack.Triggers[0].Message == "C:\\path and a \"quote\"" && oddBack.Things[0].Name == "A\\K" && oddBack.ToJson() == oddOut, oddOut);

        Check("the fingerprint of a text is stable, ignores line endings and tells texts apart", ScenarioFile.Hash("a\r\nb") == ScenarioFile.Hash("a\nb") && ScenarioFile.Hash("a\nb") != ScenarioFile.Hash("a\nc") && ScenarioFile.Hash("abc") == "fnv1a64:e71fa2190541574b" && ScenarioFile.Hash("") == "", ScenarioFile.Hash("abc"));
        // docs/scenario.schema.json must name every key the plugin writes: the schema and the code cannot drift apart unnoticed.
        var schema = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "scenario.schema.json"))).RootElement;
        var missing = new SortedSet<string>();
        void Keys(System.Text.Json.JsonElement obj, System.Text.Json.JsonElement props, string where) { foreach (var p in obj.EnumerateObject()) if (!props.TryGetProperty(p.Name, out _)) missing.Add(where + "." + p.Name); }
        void Area(System.Text.Json.JsonElement holder)
        {
            if (!holder.TryGetProperty("area", out var area)) return;
            Keys(area, schema.GetProperty("$defs").GetProperty("area").GetProperty("properties"), "area");
            foreach (var part in area.GetProperty("parts").EnumerateArray()) Keys(part, schema.GetProperty("$defs").GetProperty("part").GetProperty("properties"), "part");
        }
        void Listed(System.Text.Json.JsonElement rootObj, string list, string def)
        {
            if (!rootObj.TryGetProperty(list, out var arr)) return;
            foreach (var one in arr.EnumerateArray())
            {
                Keys(one, schema.GetProperty("$defs").GetProperty(def).GetProperty("properties"), def);
                if (def == "trigger" && one.TryGetProperty("doors", out var td)) foreach (var door in td.EnumerateArray()) Keys(door, schema.GetProperty("$defs").GetProperty("door").GetProperty("properties"), "door");
                if (def == "trigger" || def == "place") Area(one);
            }
        }
        var written = new List<string> { Plan(Fixture("all_fields.json")).ToJson(), Plan(Fixture("all_fields_v2.json")).ToJson() };
        foreach (string file in Directory.GetFiles(Path.Combine(root, "examples"), "*.json")) written.Add(Plan(File.ReadAllText(file)).ToJson());
        foreach (string text in written)
        {
            var w = System.Text.Json.JsonDocument.Parse(text).RootElement;
            Keys(w, schema.GetProperty("properties"), "scenario");
            // missionlab-plan/2: the body under "mission" has the same keys as a plan/1 file.
            if (w.TryGetProperty("mission", out var body)) { w = body; Keys(w, schema.GetProperty("properties"), "mission"); }
            Listed(w, "triggers", "trigger"); Listed(w, "spawns", "spawn"); Listed(w, "doors", "door"); Listed(w, "things", "thing"); Listed(w, "places", "place");
        }
        Check("the schema names every key the plugin writes", missing.Count == 0, string.Join(", ", missing));

        // What ships in examples/ must read, write back to the same text, and be possible to finish as written.
        foreach (string file in Directory.GetFiles(Path.Combine(root, "examples"), "*.json"))
        {
            string name = Path.GetFileName(file);
            try
            {
                var p = Plan(File.ReadAllText(file));
                string first = p.ToJson();
                Check("example " + name + " writes back to the same text", Plan(first).ToJson() == first);
                Check("example " + name + " can be finished as written", !Completion.Blocks(Completion.Check(p, null, 0)), Broken(p));
                var e = new TriggerEngine(); e.Reset(0); e.Sync(p.Triggers);
                Check("example " + name + " has no triggers in a circle", e.Problems.Count == 0, string.Join(" | ", e.Problems));
            }
            catch (Exception ex) { Check("example " + name + " reads", false, ex.Message); }
        }
    }
}

// ---------------------------------------------------------------- simulated clock: repeats, messages, waves, reset (ML-010)
Console.WriteLine("simulated clock");
Clocks.Run(Check);

// ---------------------------------------------------------------- synthetic maps: islands, floors, insertion clearance (ML-010)
Console.WriteLine("synthetic maps");
SyntheticMaps.Run(Check);

// ---------------------------------------------------------------- message queue: spacing, red at once, cap, reset (ML-010)
Console.WriteLine("message queue");
Messages.Run(Check);

// ---------------------------------------------------------------- trigger explanation, outcomes, moved places (ML-015)
Console.WriteLine("trigger explanation");
EngineTests.Run(Check);

// ---------------------------------------------------------------- rehearsal: scripted events against a scenario (ML-016)
Console.WriteLine("rehearsal");
RehearsalTests.Run(Check);

// ---------------------------------------------------------------- format v2: envelope, areas in files, places, metadata (ML-027, ML-018, ML-013)
Console.WriteLine("format v2");
FormatV2Tests.Run(Check);

// ---------------------------------------------------------------- painted areas: the shared point fixture, refusals, canonical text, the old circle (ML-027)
Console.WriteLine("painted areas");
AreaTests.Run(Check);

// ---------------------------------------------------------------- painted areas live: refused shapes, kept history, seams, gaps, everyone (ML-027 D)
Console.WriteLine("painted areas live");
AreaRuntimeTests.Run(Check);

// ---------------------------------------------------------------- saved revisions: identity, file, run links (ML-014)
Console.WriteLine("saved revisions");
RevisionsTests.Run(Check);

// ---------------------------------------------------------------- mission catalogue: author's words apart from measurements, run links, safe names (ML-013)
Console.WriteLine("mission catalogue");
CatalogueTests.Run(Check);

// ---------------------------------------------------------------- rehearsal through the browser link: the answer, kept tests, the editor's trace (ML-016)
Console.WriteLine("rehearsal endpoint");
RehearsalsTests.Run(Check);

// ---------------------------------------------------------------- the trigger diagnostic example: every inspector state, with its sentences (ML-015)
Console.WriteLine("trigger diagnostic example");
DiagnosticTests.Run(Check);

Console.WriteLine(failures == 0 ? "ALL " + checks + " CHECKS PASSED" : failures + " of " + checks + " CHECKS FAILED");
return failures == 0 ? 0 : 1;

/// <summary>A world that answers from a script.</summary>
sealed class World : ITriggerWorld
{
    public bool Alert, AllDown, Door;
    public int DownCount;
    public readonly HashSet<string> Intel = new HashSet<string>();
    public readonly HashSet<int> Inside = new HashSet<int>();
    public bool Alerted() => Alert;
    public bool IntelTaken(string item) => Intel.Contains("*") || Intel.Contains(item ?? "");
    public bool Occupied(TriggerPlan t, out V3 where) { where = new V3(1, 2, 3); return Inside.Contains(t.Id); }
    public bool Down(int count, string tag) => count <= 0 ? AllDown : DownCount >= count;
    public bool DoorOpen(V3 hinge) => Door;
    // The set above says who is inside: one living player, inside or not. EngineTests has a world that counts more players.
    int ITriggerWorld.Inside(TriggerPlan t, out int alive) { alive = 1; return Inside.Contains(t.Id) ? 1 : 0; }
}
