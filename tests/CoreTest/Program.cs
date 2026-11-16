using System.Diagnostics;
using System.Text.Json;
using MissionLab.Core;

// usage: CoreTest <navmesh.bytes> <recon.json|-> <outDir> <enemies> <seed>... [--infil x,y,z]...
//        CoreTest --check <scenario.json> <navmesh.bytes>     validate a hand-edited scenario file
if (args.Length == 2 && args[0] == "--http")
{
    // A stand-alone run of the plugin's little web server, to test it with curl.
    using var web = new MiniHttp(int.Parse(args[1]), r => r.Path == "/echo" ? HttpResponse.Json("{\"method\":\"" + r.Method + "\",\"bytes\":" + r.Body.Length + ",\"mark\":\"" + (r.Headers.TryGetValue("X-MissionLab", out var h) ? h : "") + "\"}")
                                                             : r.Path == "/boom" ? throw new InvalidOperationException("test") : HttpResponse.Error(404, "not found"));
    web.Start(); Console.WriteLine("listening on " + web.Port); Thread.Sleep(Timeout.Infinite);
}
if ((args.Length == 3 || args.Length == 4) && args[0] == "--check")
{
    MissionPlan sc;
    try { sc = MissionPlan.FromJson(File.ReadAllText(args[1])); }
    catch (Exception e) { Console.WriteLine("NOT A VALID SCENARIO: " + e.Message); return 1; }
    var map = NavMesh.Load(args[2]);
    // "The rest of the map": with a recon file, everything connected to an insertion point or to one of the
    // game's own spawn points (doors are not in the walkable-ground data, so houses count through those).
    bool[] mainArea = Generator.LargestComponent(map);
    if (args.Length == 4)
    {
        using var rec = JsonDocument.Parse(File.ReadAllText(args[3]));
        var seedTris = new List<int>();
        void Seed(JsonElement p, float tol) { int t = map.TriAt(p[0].GetSingle(), p[2].GetSingle(), p[1].GetSingle(), tol, out _); if (t >= 0) seedTris.Add(t); }
        foreach (var sp in rec.RootElement.GetProperty("raid").GetProperty("botSpawnPoints").EnumerateArray()) if (sp.ValueKind == JsonValueKind.Object) Seed(sp.GetProperty("pos"), 1.5f);
        foreach (var m in rec.RootElement.GetProperty("infiltration").GetProperty("infilMethods").EnumerateArray())
            if (m.TryGetProperty("children", out var kids)) foreach (var c in kids.EnumerateArray()) Seed(c.GetProperty("pos"), 2.5f);
        if (seedTris.Count > 0) mainArea = map.Reachable(seedTris);
        Console.WriteLine($"  reachability from {seedTris.Count} insertion and spawn points of the map");
    }
    int off = 0, num = 0;
    foreach (var s in sc.Spawns)
    {
        num++;
        bool walkable = map.TriAt(s.Pos.X, s.Pos.Z, s.Pos.Y, 1.0f, out _) >= 0;
        int tri = map.TriAt(s.Pos.X, s.Pos.Z, s.Pos.Y, 1.0f, out _);
        if (tri >= 0 && !mainArea[tri]) Console.WriteLine($"  enemy {num} ({s.Role}) at {s.Pos} stands on ground that is cut off from the rest of the map");
        if (!walkable && !s.Fixed) { off++; Console.WriteLine($"  enemy {num} ({s.Role}) at {s.Pos} is off the walkable surface: it will be a fixed sentry"); }
        int w = 0;
        foreach (var wp in s.Patrol) { w++; if (map.TriAt(wp.X, wp.Z, wp.Y, 1.0f, out _) < 0) Console.WriteLine($"  enemy {num}: patrol waypoint {w} at {wp} is off the walkable surface"); }
    }
    Console.WriteLine($"scenario for '{sc.Scene}': {sc.Spawns.Count} enemies in {sc.Groups} groups, {off} off the walkable surface");
    return 0;
}
var navPath = args[0]; var reconPath = args[1]; var outDir = args[2]; int enemies = int.Parse(args[3]);
var seeds = new List<long>(); var infils = new List<V3>();
for (int i = 4; i < args.Length; i++)
{
    if (args[i] == "--infil") { var p = args[++i].Split(','); infils.Add(new V3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]))); }
    else seeds.Add(long.Parse(args[i]));
}
var sw = Stopwatch.StartNew();
var nav = NavMesh.Load(navPath);
Console.WriteLine($"loaded {nav.SourceFile}: {nav.Count} triangles, tiles {nav.TilesX}x{nav.TilesZ}, x[{nav.MinX:0},{nav.MaxX:0}] z[{nav.MinZ:0},{nav.MaxZ:0}] in {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"bounds {(nav.HasBounds ? "center " + nav.BoundsCenter + " size " + nav.BoundsSize : "MISSING")}; ReadBounds ok={NavMesh.ReadBounds(navPath, out var rbc, out var rbs)} {rbc} {rbs}");

var authored = new List<V3>();
string scene = "test";
if (reconPath != "-")
{
    using var doc = JsonDocument.Parse(File.ReadAllText(reconPath));
    scene = doc.RootElement.GetProperty("scene").GetString();
    foreach (var s in doc.RootElement.GetProperty("raid").GetProperty("botSpawnPoints").EnumerateArray())
    {
        if (s.ValueKind != JsonValueKind.Object) continue;
        var p = s.GetProperty("pos");
        authored.Add(new V3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()));
    }
    if (infils.Count == 0)
        foreach (var m in doc.RootElement.GetProperty("infiltration").GetProperty("infilMethods").EnumerateArray())
        {
            if (!m.TryGetProperty("infilObject", out var o) || o.ValueKind != JsonValueKind.Object) continue;
            var p = o.GetProperty("pos");
            var v = new V3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
            if (Math.Abs(v.X) + Math.Abs(v.Z) > 0.01f) infils.Add(v);
            if (m.TryGetProperty("children", out var kids))
                foreach (var c in kids.EnumerateArray())
                {
                    var cp = c.GetProperty("pos");
                    var cv = new V3(cp[0].GetSingle(), cp[1].GetSingle(), cp[2].GetSingle());
                    if (Math.Abs(cv.X) + Math.Abs(cv.Y) + Math.Abs(cv.Z) > 0.01f && !infils.Any(q => Math.Abs(q.X - cv.X) + Math.Abs(q.Y - cv.Y) + Math.Abs(q.Z - cv.Z) < 1f)) infils.Add(cv);
                }
        }
}
int on = authored.Count(p => nav.TriAt(p.X, p.Z, p.Y, 1.5f, out _) >= 0);
Console.WriteLine($"scene '{scene}': {authored.Count} authored points, {on} on this mesh; {infils.Count} insertion points");

// connectivity summary
var all = nav.Reachable(authored.Select(p => nav.TriAt(p.X, p.Z, p.Y, 1.5f, out _)).Where(t => t >= 0));
double reach = 0, total = 0; for (int i = 0; i < nav.Count; i++) { total += nav.Area[i]; if (all[i]) reach += nav.Area[i]; }
Console.WriteLine($"reachable from authored points: {reach:0} of {total:0} m2 ({100 * reach / total:0.0}%)");

Directory.CreateDirectory(outDir);
File.WriteAllText(Path.Combine(outDir, "reachable.txt"), string.Join("\n", Enumerable.Range(0, nav.Count).Select(i =>
    FormattableString.Invariant($"{(all[i] ? 1 : 0)} {nav.Ax[i]} {nav.Az[i]} {nav.Bx[i]} {nav.Bz[i]} {nav.Cx[i]} {nav.Cz[i]} {nav.Ay[i]}"))));
int failures = 0;
// An intel item goes by its kind: the map has several copies of each and uses one per mission.
foreach (var (name, kind) in new[] { ("Cell Phone_1", "cell phone"), ("Cell Phone_2", "cell phone"), ("Cell Phone", "cell phone"), ("Sim cards 12", "sim cards"), ("AK47", "ak47"), ("  Laptop_3", "laptop"), ("_7", "_7"), ("", "") })
    if (ObjectivePlan.Kind(name) != kind) { Console.WriteLine("  FAIL: kind of '" + name + "' is '" + ObjectivePlan.Kind(name) + "', expected '" + kind + "'"); failures++; }
// Doors: a setting that is left out stays left out, through save and load.
{
    var dp = MissionPlan.FromJson("{\"spawns\":[],\"doors\":[{\"name\":\"A\",\"pos\":[1,2,3],\"locked\":true},{\"pos\":[4,5,6.126],\"enemiesCanOpen\":false,\"locked\":false},{\"pos\":[7,8,9]},{\"name\":\"no place\",\"locked\":true}]}");
    bool ok = dp.Doors.Count == 2 && dp.Doors[0].Locked == true && dp.Doors[0].EnemiesCanOpen == null && dp.Doors[1].Locked == false && dp.Doors[1].EnemiesCanOpen == false;
    string text = MissionPlan.DoorsJson(dp.Doors);
    var back = MissionPlan.FromJson("{\"spawns\":[],\"doors\":" + text + "}");
    ok = ok && back.Doors.Count == 2 && MissionPlan.DoorsJson(back.Doors) == text && text.Contains("6.13") && !text.Contains("null");
    ok = ok && MissionPlan.FromJson(dp.ToJson()).Doors.Count == 2;
    if (!ok) { Console.WriteLine("  FAIL: doors do not survive save and load: " + text); failures++; }
}
// Triggers: every kind survives save and load, an old file's zone is still a zone, and nonsense is dropped.
{
    string src = "{\"spawns\":[{\"pos\":[0,0,0],\"trigger\":2}],\"triggers\":[" +
        "{\"id\":1,\"pos\":[1,2,3],\"radius\":5}," +
        "{\"id\":2,\"when\":\"alert\",\"delay\":20,\"name\":\"QRF\"}," +
        "{\"id\":3,\"when\":\"intel\",\"item\":\"Cell Phone\",\"doors\":[{\"pos\":[4,5,6],\"locked\":true},{\"pos\":[7,8,9]}]}," +
        "{\"id\":4,\"when\":\"after\",\"after\":3,\"delay\":99999}," +
        "{\"id\":5,\"when\":\"time\",\"delay\":600,\"facePlayer\":false}," +
        "{\"id\":6,\"when\":\"zone\"}," +
        "{\"id\":7,\"when\":\"nonsense\",\"pos\":[0,0,0]}]}";
    var tp = MissionPlan.FromJson(src);
    var t = tp.Triggers;
    bool ok = t.Count == 6 && t[0].When == TriggerPlan.OnZone && t[0].Radius == 5 && t[1].When == TriggerPlan.OnAlert && t[1].Delay == 20 && t[1].Name == "QRF"
        && t[2].When == TriggerPlan.OnIntel && t[2].Item == "Cell Phone" && t[2].Doors.Count == 1 && t[2].Doors[0].Locked == true
        && t[3].When == TriggerPlan.OnAfter && t[3].After == 3 && t[3].Delay == 7200 && t[4].When == TriggerPlan.OnTime && t[4].Delay == 600 && !t[4].FacePlayer
        && t[5].Id == 7 && t[5].When == TriggerPlan.OnZone && tp.Spawns.Count == 1 && tp.Spawns[0].Trigger == 2;
    string text = MissionPlan.TriggersJson(t);
    var back = MissionPlan.FromJson("{\"spawns\":[],\"triggers\":" + text + "}");
    ok = ok && MissionPlan.TriggersJson(back.Triggers) == text && MissionPlan.TriggersJson(MissionPlan.FromJson(tp.ToJson()).Triggers) == text;
    if (!ok) { Console.WriteLine("  FAIL: triggers do not survive save and load: " + text); failures++; }
}
// Mission-wide settings and the gun truck flag survive save and load; left out, they stay left out.
{
    var sp = MissionPlan.FromJson("{\"extraction\":{\"after\":3},\"insertion\":\"Alpha 2\",\"weather\":{\"hour\":5.5,\"rain\":true},\"triggers\":[{\"id\":3,\"when\":\"time\",\"delay\":60,\"extraction\":\"open\"}],\"spawns\":[{\"pos\":[1,2,3],\"vehicle\":true},{\"pos\":[4,5,6]}]}");
    bool ok = sp.ExtractionAfter == 3 && sp.Insertion == "Alpha 2" && sp.Weather.Hour == 5.5f && sp.Weather.Rain == true && sp.Weather.Sandstorm == null
        && sp.Triggers.Count == 1 && sp.Triggers[0].OpensExtraction && sp.Spawns.Count == 2 && sp.Spawns[0].Vehicle && !sp.Spawns[1].Vehicle;
    var again = MissionPlan.FromJson(sp.ToJson());
    ok = ok && again.ToJson() == sp.ToJson() && again.ExtractionAfter == 3 && again.Weather.Rain == true && again.Spawns[0].Vehicle && again.Triggers[0].OpensExtraction;
    var plain = MissionPlan.FromJson("{\"spawns\":[{\"pos\":[1,2,3]}]}");
    ok = ok && MissionPlan.SettingsJson(plain) == "" && !plain.ToJson().Contains("vehicle") && !plain.ToJson().Contains("weather");
    if (!ok) { Console.WriteLine("  FAIL: mission settings do not survive save and load: " + sp.ToJson()); failures++; }
}
{   // what a scenario leaves to chance, and what a trigger can do besides bringing enemies
    string src = "{\"power\":\"off\",\"triggers\":[{\"id\":1,\"when\":\"time\",\"delay\":600,\"power\":\"on\",\"message\":\"Hold  the\\nhouse\",\"end\":\"success\"},{\"id\":2,\"when\":\"alert\",\"end\":\"failed\",\"power\":\"dim\",\"message\":\"" + new string('x', 200) + "\"}]," +
                 "\"spawns\":[{\"pos\":[0,0,0]},{\"pos\":[1,0,0],\"chance\":40},{\"pos\":[2,0,0],\"chance\":50,\"lot\":3},{\"pos\":[3,0,0],\"chance\":50,\"lot\":3}," +
                 "{\"pos\":[4,0,0],\"either\":1,\"option\":1},{\"pos\":[5,0,0],\"either\":1,\"option\":1},{\"pos\":[6,0,0],\"either\":1,\"option\":2},{\"pos\":[7,0,0],\"either\":1,\"option\":3},{\"pos\":[8,0,0],\"chance\":0},{\"pos\":[9,0,0],\"chance\":250}]}";
    var vp = MissionPlan.FromJson(src);
    var again = MissionPlan.FromJson(vp.ToJson());
    bool ok = vp.Power == "off" && again.Power == "off"
        && again.Triggers.Count == 2 && again.Triggers[0].Power == "on" && again.Triggers[0].Message == "Hold  the house" && again.Triggers[0].End == "success"
        && again.Triggers[1].Power == "" && again.Triggers[1].End == "failed" && again.Triggers[1].Message.Length == TriggerPlan.MessageLength
        && again.Spawns.Count == 10 && again.Spawns[0].Chance == 100 && again.Spawns[1].Chance == 40 && again.Spawns[2].Lot == 3 && again.Spawns[6].Either == 1 && again.Spawns[6].Option == 2
        && again.Spawns[8].Chance == 1 && again.Spawns[9].Chance == 100
        && again.ToJson() == vp.ToJson() && !MissionPlan.SpawnJson(again.Spawns[0]).Contains("chance") && !MissionPlan.SpawnJson(again.Spawns[0]).Contains("either");
    if (!ok) { Console.WriteLine("  FAIL: chance, either/or and trigger actions do not survive save and load: " + again.ToJson()); failures++; }

    var seen = new Dictionary<string, int>(); int lone = 0, squad = 0; const int runs = 4000;
    for (int r = 0; r < runs; r++)
    {
        var p = MissionPlan.FromJson(src);
        var notes = new List<string>();
        var gone = Variants.Resolve(p, new Rng(1000 + r), notes);
        var q = MissionPlan.FromJson(src);
        Variants.Resolve(q, new Rng(1000 + r), null);
        if (q.ToJson() != p.ToJson()) { Console.WriteLine("  FAIL: the same roll gives another mission"); failures++; break; }
        if (gone.Count + p.Spawns.Count != 10 || !p.Spawns.Exists(s => s.Pos.X == 0) || !p.Spawns.Exists(s => s.Pos.X == 9)) { Console.WriteLine("  FAIL: an enemy without chance was dropped, or one was lost"); failures++; break; }
        bool a = p.Spawns.Exists(s => s.Pos.X == 4), a2 = p.Spawns.Exists(s => s.Pos.X == 5), b = p.Spawns.Exists(s => s.Pos.X == 6), c = p.Spawns.Exists(s => s.Pos.X == 7);
        if (a != a2 || (a ? 1 : 0) + (b ? 1 : 0) + (c ? 1 : 0) != 1) { Console.WriteLine("  FAIL: an either/or set did not keep exactly one option"); failures++; break; }
        string which = a ? "A" : b ? "B" : "C"; seen[which] = seen.TryGetValue(which, out int n) ? n + 1 : 1;
        bool l2 = p.Spawns.Exists(s => s.Pos.X == 2), l3 = p.Spawns.Exists(s => s.Pos.X == 3);
        if (l2 != l3) { Console.WriteLine("  FAIL: a lot was split"); failures++; break; }
        if (l2) squad++;
        if (p.Spawns.Exists(s => s.Pos.X == 1)) lone++;
    }
    bool fair = seen.Count == 3 && Math.Abs(seen["A"] - runs / 3) < runs / 20 && Math.Abs(seen["B"] - runs / 3) < runs / 20
             && Math.Abs(lone - runs * 0.4) < runs / 20 && Math.Abs(squad - runs * 0.5) < runs / 20;
    if (!fair) { Console.WriteLine("  FAIL: the rolls are off: options " + string.Join(",", seen) + ", 40% enemy " + lone + ", 50% squad " + squad + " of " + runs); failures++; }
    else Console.WriteLine("  chance and either/or: options " + string.Join(" ", seen) + " | 40% enemy there " + lone + ", 50% squad there " + squad + " of " + runs);
    var none = MissionPlan.FromJson("{\"spawns\":[{\"pos\":[1,2,3]}]}");
    if (Variants.Any(none) || Variants.Resolve(none, new Rng(1), null).Count != 0 || none.Spawns.Count != 1) { Console.WriteLine("  FAIL: a plain scenario was touched by the rolls"); failures++; }
}
{   // things, sides, and the triggers that wait for a held zone or for enemies to go down
    string src = "{\"things\":[{\"name\":\"AKM\",\"pos\":[1,2,3],\"yaw\":90},{\"name\":\"Hard Drive\",\"pos\":[4,5,6],\"trigger\":2},{\"name\":\"\",\"pos\":[0,0,0]},{\"name\":\"MP5\"}]," +
                 "\"triggers\":[{\"id\":1,\"when\":\"hold\",\"pos\":[1,0,1],\"radius\":6,\"hold\":45},{\"id\":2,\"when\":\"down\",\"count\":3},{\"id\":3,\"when\":\"hold\"},{\"id\":4,\"when\":\"down\"}]," +
                 "\"spawns\":[{\"pos\":[0,0,0],\"team\":4},{\"pos\":[1,0,0],\"team\":1},{\"pos\":[2,0,0]},{\"pos\":[3,0,0],\"team\":99}]}";
    var tp = MissionPlan.FromJson(src);
    var back = MissionPlan.FromJson(tp.ToJson());
    bool ok = back.Things.Count == 2 && back.Things[0].Name == "AKM" && back.Things[0].Yaw == 90 && back.Things[0].Trigger == 0 && back.Things[1].Name == "Hard Drive" && back.Things[1].Trigger == 2 && back.Things[1].Pos.Z == 6
        && back.Triggers.Count == 3 && back.Triggers[0].When == TriggerPlan.OnHold && back.Triggers[0].Hold == 45 && back.Triggers[0].Radius == 6 && back.Triggers[0].Pos.X == 1
        && back.Triggers[1].When == TriggerPlan.OnDown && back.Triggers[1].Count == 3 && back.Triggers[2].When == TriggerPlan.OnDown && back.Triggers[2].Count == 0
        && back.Spawns[0].Team == 4 && back.Spawns[1].Team == 1 && back.Spawns[2].Team == 0 && back.Spawns[3].Team == 8
        && back.ToJson() == tp.ToJson() && !MissionPlan.SpawnJson(back.Spawns[2]).Contains("team") && !MissionPlan.FromJson("{\"spawns\":[{\"pos\":[0,0,0]}]}").ToJson().Contains("things")
        && back.Things[0].Key != back.Things[1].Key;
    if (!ok) { Console.WriteLine("  FAIL: things, sides or the hold and down triggers do not survive save and load: " + back.ToJson()); failures++; }
    else Console.WriteLine("  things, sides, hold and down triggers: survive save and load");
}
{   // 0.23: doors open or closed, hunters, respawn by trigger and by rule
    string src = "{\"respawn\":{\"after\":1,\"times\":2}," +
                 "\"doors\":[{\"name\":\"A\",\"pos\":[1,0,1],\"open\":true},{\"name\":\"B\",\"pos\":[2,0,2]}]," +
                 "\"triggers\":[{\"id\":1,\"when\":\"time\",\"hunt\":true,\"respawn\":true,\"doors\":[{\"name\":\"A\",\"pos\":[1,0,1],\"open\":false,\"locked\":true},{\"name\":\"B\",\"pos\":[2,0,2],\"open\":true},{\"name\":\"C\",\"pos\":[3,0,3],\"enemiesCanOpen\":false}]},{\"id\":2,\"when\":\"alert\"}]," +
                 "\"spawns\":[{\"pos\":[0,0,0]}]}";
    var np = MissionPlan.FromJson(src);
    string js = np.ToJson();
    var back = MissionPlan.FromJson(js);
    bool ok = back.RespawnAfter == 3f && back.RespawnTimes == 2 && back.Doors.Count == 1 && back.Doors[0].Open == true && !back.Doors[0].Locked.HasValue
        && back.Triggers[0].Hunt && back.Triggers[0].Respawn && !back.Triggers[1].Hunt && !back.Triggers[1].Respawn
        && back.Triggers[0].Doors.Count == 2 && back.Triggers[0].Doors[0].Open == false && back.Triggers[0].Doors[0].Locked == true && back.Triggers[0].Doors[1].Open == true && !back.Triggers[0].Doors[1].Locked.HasValue
        && back.ToJson() == js && MissionPlan.FromJson("{\"spawns\":[]}").RespawnAfter == 0f && !MissionPlan.FromJson("{\"spawns\":[]}").ToJson().Contains("respawn");
    if (!ok) { Console.WriteLine("  FAIL: the 0.23 fields do not survive save and load: " + js); failures++; }
}
{   // 0.22: tags, door triggers, every player, repeats, rally, red messages, the scenario's own skill
    string src = "{\"skill\":\"veteran\",\"nvg\":true," +
                 "\"triggers\":[{\"id\":1,\"when\":\"down\",\"tag\":\" off!icer \",\"count\":0,\"repeat\":2,\"times\":3,\"rally\":40,\"message\":\"x\",\"red\":true}," +
                 "{\"id\":2,\"when\":\"door\",\"pos\":[1,2,3]},{\"id\":3,\"when\":\"door\"},{\"id\":4,\"when\":\"zone\",\"pos\":[0,0,0],\"radius\":5,\"everyone\":true},{\"id\":5,\"when\":\"time\",\"everyone\":true,\"tag\":\"a\",\"times\":4}]," +
                 "\"spawns\":[{\"pos\":[0,0,0],\"tag\":\"Officer\"},{\"pos\":[1,0,0]}]}";
    var np = MissionPlan.FromJson(src);
    var back = MissionPlan.FromJson(np.ToJson());
    string js = np.ToJson();
    bool ok = back.Skill == "veteran" && back.Nvg && back.Triggers.Count == 4                                    // the door trigger without a door is dropped
        && back.Triggers[0].Tag == "officer" && back.Triggers[0].Repeat == 5f && back.Triggers[0].Times == 3 && back.Triggers[0].Rally == 40f && back.Triggers[0].Red
        && back.Triggers[1].When == TriggerPlan.OnDoor && back.Triggers[1].Pos.X == 1f && back.Triggers[2].Everyone && !back.Triggers[3].Everyone
        && back.Spawns[0].Tag == "Officer" && back.Spawns[1].Tag == "" && back.ToJson() == js
        && !MissionPlan.SpawnJson(back.Spawns[1]).Contains("tag") && MissionPlan.FromJson("{\"skill\":\"hero\",\"spawns\":[]}").Skill == "" && SpawnPlan.CleanTag("  a<b>c  ") == "abc" && SpawnPlan.CleanTag(new string('x', 40)).Length == 24;
    // The fifth trigger is a timer: "everyone" and "tag" are not written for it, "times" is not written without a repeat.
    string fifth = js.Substring(js.IndexOf("\"id\":5")); fifth = fifth.Substring(0, fifth.IndexOf('}'));
    ok = ok && !fifth.Contains("everyone") && !fifth.Contains("tag") && !fifth.Contains("times");
    if (!ok) { Console.WriteLine("  FAIL: the 0.22 fields do not survive save and load: " + js); failures++; }
}
{   // 0.21: trigger logic, chance on doors, things and triggers, players needed, skill, name and briefing
    string src = "{\"name\":\" Night raid \",\"briefing\":[\"Get  the phone.\",\"\",\"Leave by the east gate.\",\"3\",\"4\",\"5\"]," +
                 "\"doors\":[{\"name\":\"A\",\"pos\":[1,0,1],\"locked\":true,\"chance\":50},{\"name\":\"B\",\"pos\":[2,0,2],\"locked\":true}]," +
                 "\"things\":[{\"name\":\"AKM\",\"pos\":[1,2,3],\"chance\":50},{\"name\":\"MP5\",\"pos\":[4,5,6]}]," +
                 "\"triggers\":[{\"id\":1,\"when\":\"time\",\"delay\":120,\"warn\":true},{\"id\":2,\"when\":\"alert\",\"chance\":50},{\"id\":3,\"when\":\"all\",\"of\":[1,2,3,2,0]},{\"id\":4,\"when\":\"any\",\"of\":[1,2]}]," +
                 "\"spawns\":[{\"pos\":[0,0,0]},{\"pos\":[1,0,0],\"minPlayers\":3,\"skill\":\"veteran\",\"nvg\":true},{\"pos\":[2,0,0],\"skill\":\"hero\",\"minPlayers\":2}]}";
    var np = MissionPlan.FromJson(src);
    var back = MissionPlan.FromJson(np.ToJson());
    bool ok = back.Title == "Night raid" && back.Briefing.Count == 4 && back.Briefing[0] == "Get  the phone." && back.Briefing[1] == "Leave by the east gate."
        && back.Doors.Count == 2 && back.Doors[0].Chance == 50 && back.Doors[1].Chance == 100 && back.Things[0].Chance == 50 && back.Things[1].Chance == 100
        && back.Triggers.Count == 4 && back.Triggers[0].Warn && !back.Triggers[1].Warn && back.Triggers[1].Chance == 50
        && back.Triggers[2].When == TriggerPlan.OnAll && back.Triggers[2].Of.Count == 2 && back.Triggers[2].Of[0] == 1 && back.Triggers[2].Of[1] == 2 && back.Triggers[3].When == TriggerPlan.OnAny
        && back.Spawns[1].MinPlayers == 3 && back.Spawns[1].Skill == "veteran" && back.Spawns[1].Nvg && back.Spawns[2].Skill == "" && back.Spawns[2].MinPlayers == 2 && !back.Spawns[0].Nvg
        && back.ToJson() == np.ToJson() && !MissionPlan.SpawnJson(back.Spawns[0]).Contains("minPlayers") && !MissionPlan.SpawnJson(back.Spawns[0]).Contains("skill");
    if (!ok) { Console.WriteLine("  FAIL: the 0.21 fields do not survive save and load: " + back.ToJson()); failures++; }

    int doorIn = 0, thingIn = 0, trigOn = 0; const int runs = 3000; bool sound = true;
    for (int r = 0; r < runs && sound; r++)
    {
        var p = MissionPlan.FromJson(src);
        var notes = new List<string>();
        Variants.Out gone = Variants.Resolve(p, new Rng(500 + r), notes, 2);
        sound = p.Spawns.Count == 2 && gone.Spawns.Count == 1 && gone.Spawns[0].MinPlayers == 3            // two players: the one who needs three stays home
             && p.Doors.Exists(d => d.Name == "B") && p.Things.Exists(t => t.Name == "MP5") && p.Triggers.Count == 4 && !p.Triggers[0].Off && !p.Triggers[2].Off
             && p.Doors.Count + gone.Doors.Count == 2 && p.Things.Count + gone.Things.Count == 2;
        if (p.Doors.Count == 2) doorIn++;
        if (p.Things.Count == 2) thingIn++;
        if (!p.Triggers[1].Off) trigOn++;
    }
    var solo = MissionPlan.FromJson(src); Variants.Resolve(solo, new Rng(1), null, 1);
    var crowd = MissionPlan.FromJson(src); Variants.Resolve(crowd, new Rng(1), null, 4);
    var unknown = MissionPlan.FromJson(src); Variants.Resolve(unknown, new Rng(1), null, 0);
    bool fair = Math.Abs(doorIn - runs / 2) < runs / 16 && Math.Abs(thingIn - runs / 2) < runs / 16 && Math.Abs(trigOn - runs / 2) < runs / 16;
    if (!sound || !fair || solo.Spawns.Count != 1 || crowd.Spawns.Count != 3 || unknown.Spawns.Count != 3)
    { Console.WriteLine("  FAIL: rolls for doors, things, triggers or players: sound " + sound + ", door " + doorIn + ", thing " + thingIn + ", trigger " + trigOn + " of " + runs + "; enemies with 1/4/unknown players " + solo.Spawns.Count + "/" + crowd.Spawns.Count + "/" + unknown.Spawns.Count); failures++; }
    else Console.WriteLine("  0.21: door there " + doorIn + ", thing there " + thingIn + ", trigger on " + trigOn + " of " + runs + " | enemies with 1, 2, 4 players: 1, 2, 3");
}
var seenPlans = new HashSet<string>();
foreach (var seed in seeds)
{
    GeneratorInput In() => new GeneratorInput { Nav = nav, Seed = seed, Scene = scene, Enemies = enemies, Infils = infils, Authored = authored };
    sw.Restart();
    var plan = Generator.Generate(In());
    long ms = sw.ElapsedMilliseconds;
    var json = plan.ToJson();
    if (Generator.Generate(In()).ToJson() != json) { Console.WriteLine("  FAIL: not deterministic"); failures++; }
    JsonDocument.Parse(json).Dispose();   // must be valid JSON
    string reloaded = MissionPlan.FromJson(json).ToJson();   // enemy positions and settings must survive save and load
    if (reloaded.Substring(reloaded.IndexOf("\"spawns\"")) != json.Substring(json.IndexOf("\"spawns\""))) { Console.WriteLine("  FAIL: plan does not survive save and load"); failures++; }
    if (MissionPlan.FromJson("{\"plan\":" + json + "}").Spawns.Count != plan.Spawns.Count) { Console.WriteLine("  FAIL: mission record wrapper not read"); failures++; }
    string body = json.Substring(json.IndexOf("\"spawns\""));
    if (!seenPlans.Add(body)) { Console.WriteLine("  FAIL: same layout as an earlier seed"); failures++; }
    File.WriteAllText(Path.Combine(outDir, $"plan_seed{seed}.json"), json);

    // The extras for random layouts: the same roll twice gives the same result, most enemies are still there at the start,
    // and nobody waits for a trigger that does not exist.
    {
        var a = Generator.Generate(In()); var b = Generator.Generate(In());
        var na = new List<string>(); var nb = new List<string>();
        Extras.Waves(a, new Rng(seed ^ 0xE87A5), infils, na); Extras.Waves(b, new Rng(seed ^ 0xE87A5), infils, nb);
        int held = a.Spawns.Count(s => s.Trigger != 0);
        bool ok = a.ToJson() == b.ToJson() && held <= Math.Max(1, a.Spawns.Count / 3) && a.Spawns.Count - held >= 3
            && a.Spawns.All(s => s.Trigger == 0 || a.Triggers.Exists(t => t.Id == s.Trigger))
            && a.Triggers.Select(t => t.Id).Distinct().Count() == a.Triggers.Count
            && a.Triggers.All(t => t.When != TriggerPlan.OnZone || nav.TriAt(t.Pos.X, t.Pos.Z, t.Pos.Y, 2f, out _) >= 0 || a.Spawns.Exists(s => V3.DistXZ(s.Pos, t.Pos) < 30f))
            && MissionPlan.FromJson(a.ToJson()).Triggers.Count == a.Triggers.Count;
        if (!ok) { Console.WriteLine("  FAIL: random extras are off for seed " + seed + ": " + string.Join("; ", na)); failures++; }
        else if (seed <= 2) Console.WriteLine("  extras, seed " + seed + ": " + (na.Count > 0 ? string.Join("; ", na) : "none") + " (" + held + " of " + a.Spawns.Count + " held back)");
    }

    var seedTris = infils.Select(p => nav.TriAt(p.X, p.Z, p.Y, 2.5f, out _)).Concat(authored.Select(p => nav.TriAt(p.X, p.Z, p.Y, 1.5f, out _))).Where(t => t >= 0).ToList();
    var mask = seedTris.Count > 0 ? nav.Reachable(seedTris) : Generator.LargestComponent(nav);
    int offMesh = 0, unreachable = 0, nearInfil = 0, tooClose = 0, badLeg = 0;
    for (int i = 0; i < plan.Spawns.Count; i++)
    {
        var s = plan.Spawns[i];
        int t = nav.TriAt(s.Pos.X, s.Pos.Z, s.Pos.Y, 0.1f, out _);
        if (t < 0) offMesh++; else if (!mask[t]) unreachable++;
        if (infils.Any(f => V3.DistXZ(f, s.Pos) < 20f)) nearInfil++;
        if (s.Patrol.Any(w => infils.Any(f => V3.DistXZ(f, w) < 30f))) nearInfil++;
        foreach (var f in infils)
        {
            float d = V3.DistXZ(f, s.Pos);
            if (d >= 45f || nav.TriAt(f.X, f.Z, f.Y, 2.5f, out float fh, mask) < 0) continue;
            if (nav.Walk(new V3(f.X, fh, f.Z), Math.Atan2(s.Pos.Z - f.Z, s.Pos.X - f.X), d, mask, out _, 0.5f) >= d - 1.0f) nearInfil++;
        }
        for (int k = i + 1; k < plan.Spawns.Count; k++)
        {
            var o = plan.Spawns[k].Pos;
            if (Math.Sqrt(Math.Pow(o.X - s.Pos.X, 2) + Math.Pow(o.Y - s.Pos.Y, 2) + Math.Pow(o.Z - s.Pos.Z, 2)) < 1.79) tooClose++;
        }
        for (int k = 0; k + 1 < s.Patrol.Count; k++)
        {
            var a = s.Patrol[k]; var b = s.Patrol[k + 1];
            double ang = Math.Atan2(b.Z - a.Z, b.X - a.X); float len = V3.DistXZ(a, b);
            if (nav.Walk(a, ang, len, mask, out _) < len - 0.7f) badLeg++;
        }
        if (s.Role == SpawnRole.Patrol && s.Patrol.Count < 2) badLeg++;
    }
    int bad = offMesh + unreachable + nearInfil + tooClose + badLeg + (plan.Spawns.Count < enemies ? 1 : 0);
    failures += bad;
    var roles = string.Join(" ", plan.Spawns.GroupBy(s => s.Role).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"));
    Console.WriteLine($"  seed {seed}: {plan.Spawns.Count} spawns in {plan.Groups} groups, indoor {plan.Spawns.Count(s => s.Indoor)}, {roles}, {ms} ms" +
        (bad == 0 ? "  OK" : $"  PROBLEMS offMesh={offMesh} unreachable={unreachable} nearInfil={nearInfil} tooClose={tooClose} badPatrolLeg={badLeg}"));
    foreach (var n in plan.Notes.Skip(seed == seeds[0] ? 0 : 99)) Console.WriteLine("     note: " + n);
}
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} PROBLEM(S)");
return failures == 0 ? 0 : 1;
