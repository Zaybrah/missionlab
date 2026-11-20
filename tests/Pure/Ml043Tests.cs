using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// ML-043: walkthrough tests saved in examples/rehearsals/sc-painted-house, played by the Core rehearsal against the
/// painted house in examples/rehearsals/painted_house.json (made-up coordinates). The house is one area: a ground floor
/// and an upper floor round a courtyard (a hole in both), joined by a stair part that spans both bands. The garden is
/// everything outside. Each saved test is paired with a changed plan to show the test notices what it is about.
/// </summary>
static class Ml043Tests
{
    private static string Root()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "examples", "rehearsals", "painted_house.json"))) root = Path.GetDirectoryName(root);
        return root;
    }

    private static string Text(string root, string rel) => File.ReadAllText(Path.Combine(root, "examples", "rehearsals", rel)).Replace("\r\n", "\n");

    private static string Failures(RehearsalResult r) =>
        (r.Blocked == "" ? "" : "blocked: " + r.Blocked + " | ") + string.Join(" | ", r.Steps.Where(x => !x.Ok).Select(x => x.What + " -> expected " + x.Expected + ", got " + x.Actual));

    private static RehearsalLook LookAt(RehearsalResult r, float at) => r.Looks.FirstOrDefault(l => Math.Abs(l.At - at) < 0.001f);

    private static string Phase(RehearsalResult r, float at, int id, bool after = true)
    {
        RehearsalLook l = LookAt(r, at);
        if (l == null) return "no look at " + at;
        return (after ? l.After : l.Before).TryGetValue(id, out string p) ? p : "no trigger " + id;
    }

    // Every part of trigger id loses the part with the given floor band and outline start (e.g. the stairs).
    private static void DropParts(MissionPlan p, Func<AreaPart, bool> which)
    {
        foreach (TriggerPlan t in p.Triggers)
        {
            if (t.Area == null) continue;
            t.Area.Parts.RemoveAll(x => which(x));
            t.Area.Prepare();
        }
    }

    public static void Run(Action<string, bool, string> check)
    {
        string root = Root();
        check("the painted house example is found", root != null, "");
        if (root == null) return;
        string scenario = Text(root, "painted_house.json");
        ScenarioFile.Read(scenario, out MissionPlan house);
        check("the painted house passes the strict reader", house != null, "");
        if (house == null) return;
        MissionPlan Fresh() { ScenarioFile.Read(scenario, out MissionPlan p); return p; }
        RehearsalScript Script(string name, MissionPlan plan)
        {
            var found = Rehearsal.Read(Text(root, "sc-painted-house/" + name + ".json"), out RehearsalScript s, plan);
            check(name + " reads with nothing found", s != null && found.Count == 0, string.Join(" | ", found));
            return s;
        }
        string[] names = { "front_door_counts", "courtyard_and_garden_do_not_count", "stairs_keep_the_hold", "two_players_in_one_house" };

        {   // The saved tests are written exactly as Rehearsal.Write writes them, so saving one again does not change it.
            foreach (string name in names)
            {
                string text = Text(root, "sc-painted-house/" + name + ".json");
                Rehearsal.Read(text, out RehearsalScript s, house);
                check(name + " is in Rehearsal.Write's own form", s != null && Rehearsal.Write(s) == text.TrimEnd('\n'), s == null ? "" : Rehearsal.Write(s));
            }
        }
        {   // Walking in through the doorway fires the house zone the moment the sample lands on the wall line, not before.
            RehearsalScript s = Script("front_door_counts", house);
            if (s != null)
            {
                RehearsalResult r = Rehearsal.Run(house, s);
                check("the walk from the garden through the front door passes", r.Passed, Failures(r));
                check("the zone was waiting in the garden half a second before the doorway", Phase(r, 2.5f, 1) == TriggerEngine.Waiting, Phase(r, 2.5f, 1));
                check("at the doorway the zone goes from waiting to fired", Phase(r, 3f, 1, false) == TriggerEngine.Waiting && Phase(r, 3f, 1) == TriggerEngine.Fired, Phase(r, 3f, 1, false) + " -> " + Phase(r, 3f, 1));
                RehearsalLook door = LookAt(r, 3f);
                check("at the doorway one of one living player counts as inside", door != null && door.Inside.TryGetValue(1, out string n) && n == "1/1", door == null ? "" : string.Join(",", door.Inside));
                MissionPlan moved = Fresh();
                foreach (TriggerPlan t in moved.Triggers) if (t.Area != null) { foreach (AreaPart part in t.Area.Parts) for (int k = 0; k < part.Outline.Count; k++) if (part.Outline[k].Z == 0) part.Outline[k] = new P2(part.Outline[k].X, 1.5f); t.Area.Prepare(); }
                RehearsalResult later = Rehearsal.Run(moved, s);
                check("with the front wall 1.5 m further in, the same walk fails at the doorway step", !later.Passed && later.Steps.Any(x => !x.Ok && x.What.StartsWith("step 10 at 3 s: trigger 1 'House' has fired")), Failures(later));
            }
        }
        {   // Standing in the courtyard (a hole) and walking along the garden never counts; without the hole the courtyard would.
            RehearsalScript s = Script("courtyard_and_garden_do_not_count", house);
            if (s != null)
            {
                RehearsalResult r = Rehearsal.Run(house, s);
                check("the courtyard and the garden leave the zone and the hold unfired", r.Passed, Failures(r));
                MissionPlan solid = Fresh();
                foreach (TriggerPlan t in solid.Triggers) if (t.Area != null) { foreach (AreaPart part in t.Area.Parts) part.Holes.Clear(); t.Area.Prepare(); }
                RehearsalResult filled = Rehearsal.Run(solid, s);
                check("with the courtyard filled in, the courtyard step fires the zone and the test fails", !filled.Passed && filled.Steps.Any(x => !x.Ok && x.What.StartsWith("step 2 at 2.5 s: trigger 1 'House' has not fired")), Failures(filled));
            }
        }
        {   // Climbing the stairs keeps the hold running through the gap between the floor bands; without the stair part it starts again.
            RehearsalScript s = Script("stairs_keep_the_hold", house);
            if (s != null)
            {
                RehearsalResult r = Rehearsal.Run(house, s);
                check("the stair climb holds the house for 6 s and fires the hold", r.Passed, Failures(r));
                check("on the landing at 2.9 m (between the bands) the hold is still holding", Phase(r, 2f, 2) == TriggerEngine.Holding, Phase(r, 2f, 2));
                MissionPlan noStairs = Fresh();
                DropParts(noStairs, part => part.Outline.Count == 4 && part.Outline[0].X == 16);
                RehearsalResult gap = Rehearsal.Run(noStairs, s);
                check("without the stair part the landing is outside, so the hold falls back to waiting", Phase(gap, 2f, 2) == TriggerEngine.Waiting, Phase(gap, 2f, 2));
                check("and the hold has not fired at 6 s, so the test fails there", !gap.Passed && gap.Steps.Any(x => !x.Ok && x.What.StartsWith("step 13 at 6 s: trigger 2 'Hold the house' has fired")), Failures(gap));
            }
        }
        {   // Two living players in different parts of the one union (ground floor, stairs) satisfy an everyone-hold.
            RehearsalScript s = Script("two_players_in_one_house", house);
            if (s != null)
            {
                RehearsalResult r = Rehearsal.Run(house, s);
                check("one player downstairs and one on the stairs and upstairs hold the house together", r.Passed, Failures(r));
                RehearsalLook mid = LookAt(r, 2f);
                check("on the landing both living players count as inside the house", mid != null && mid.Inside.TryGetValue(2, out string n) && n == "2/2", mid == null ? "" : string.Join(",", mid.Inside));
                check("the injected alarm fires the alarm trigger at its own time", Phase(r, 1.25f, 3, false) == TriggerEngine.Waiting && Phase(r, 1.25f, 3) == TriggerEngine.Fired, Phase(r, 1.25f, 3));
                // The second player in the garden instead: an everyone-hold needs every living player inside.
                foreach (RehearsalStep st in s.Steps) if (st.Players != null && st.Players.Count == 2) st.Players[1] = new V3(10, 0, -6);
                RehearsalResult apart = Rehearsal.Run(house, s);
                RehearsalLook split = LookAt(apart, 2f);
                check("with the second player in the garden only one of two is inside", split != null && split.Inside.TryGetValue(2, out string m) && m == "1/2", split == null ? "" : string.Join(",", split.Inside));
                check("and the everyone-hold never fires", !apart.Passed && apart.Steps.Any(x => !x.Ok && x.What.StartsWith("step 15 at 6 s: trigger 2 'Hold the house' has fired")), Failures(apart));
            }
        }
        {   // The plugin's answer carries the moments, with phases before and after, in the shape the page reads.
            string script = Text(root, "sc-painted-house/front_door_counts.json");
            string body = "{\"scenario\":" + JsonSerializer.Serialize(scenario) + ",\"script\":" + JsonSerializer.Serialize(script) + "}";
            string answer = RehearsalFile.Rehearse(body, out int status);
            bool shaped = false; int looks = 0; string at3 = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(answer))
                {
                    JsonElement list = doc.RootElement.GetProperty("looks");
                    looks = list.GetArrayLength();
                    foreach (JsonElement l in list.EnumerateArray())
                        if (l.GetProperty("at").GetDouble() == 3)
                        {
                            at3 = l.GetProperty("before").GetProperty("1").GetString() + " -> " + l.GetProperty("after").GetProperty("1").GetString() + " " + l.GetProperty("inside").GetProperty("1").GetString();
                            shaped = at3 == "waiting -> fired 1/1" && l.GetProperty("after").GetProperty("3").GetString() == TriggerEngine.Waiting;
                        }
                }
            }
            catch (Exception e) { at3 = e.Message; }
            check("the answer is 200 with one look per moment a step happens (11 moments)", status == 200 && looks == 11, status + " " + looks);
            check("the look at the doorway says the zone went from waiting to fired with 1 of 1 inside", shaped, at3);
        }
    }
}
