using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// The debrief and the unused-content report (ML-024), from the synthetic recordings in tests/fixtures/debrief: one of
/// semantics 4 with its run record, one of semantics 2 without. The page's copy of the debrief (32a-debrief.js) must write
/// the same text for both (tests/editor/run.mjs reads the same expected files).
/// </summary>
static class DebriefTests
{
    private const string RaidReplay = "20261118-120000_Suburb.json", RaidRun = "20261118-120000_Suburb.run.json";

    private static string Fixture(string name)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        return root == null ? null : File.ReadAllText(Path.Combine(root, "tests", "fixtures", "debrief", name)).Replace("\r\n", "\n");
    }

    private static DebriefFact Fact(Debrief d, string key) => d.Facts.Find(f => f.Key == key);

    public static void Run(Action<string, bool, string> checkWith)
    {
        void check(string what, bool ok, string more = "") => checkWith(what, ok, more);
        string replay = Fixture("raid.replay.json"), run = Fixture("raid.run.json"), old = Fixture("old.replay.json");

        {   // Semantics 4 with its run record: every number from a named source, edits apart from kills.
            string asked = "";
            Debrief d = Debrief.Read(RaidReplay, replay, n => { asked = n; return n == RaidRun ? run : null; }, out string error);
            check("a recording of semantics 4 reads", d != null && error == "", error);
            if (d != null)
            {
                check("the run record named in the recording's header is the one read", asked == RaidRun && d.HasRun);
                check("the debrief text is the expected one", d.Text() == Fixture("raid.expected.txt").TrimEnd(), d.Text());
                check("every count says which event or record it came from",
                    d.Facts.TrueForAll(f => f.Count < 0 || f.Source != ""), string.Join(" | ", d.Facts.ConvertAll(f => f.Key + "=" + f.Source)));
                check("nothing in it is a guess", d.Facts.TrueForAll(f => !f.Guess) && d.Milestones.TrueForAll(m => !m.Guess));
                check("an edit's removal and remaking are not counted as enemies down",
                    Fact(d, "down").Count == 1 && Fact(d, "removed").Count == 1 && Fact(d, "remade").Count == 1 && Fact(d, "gone").Count == 1);
                check("first contact is the first alert event, at its moment", Fact(d, "contact").Source == Debrief.FromAlert && Fact(d, "contact").At == 30.5);
                check("elapsed time is the run record's, the recording's length the frames'", Fact(d, "elapsed").Count == 610 && Fact(d, "elapsed").Source == Debrief.FromRunEnd && Fact(d, "recorded").Count == 116);
                check("health, shots and door use are named as not recorded, never as numbers",
                    Fact(d, "notRecorded") != null && Fact(d, "notRecorded").Count < 0 && !d.Facts.Exists(f => f.Key == "health" || f.Key == "shots"));
                check("the milestones name trigger, name and place", d.Milestones.Count == 2 && d.Milestones[0].Trigger == 1 && d.Milestones[0].Name == "Gate" && d.Milestones[0].Place == "Courtyard");
                check("the hashes for comparing runs are the scenario's and the resolved setup's",
                    d.ScenarioHash == "fnv1a64:00000000000000aa" && d.ResolvedHash.StartsWith("fnv1a64:", StringComparison.Ordinal) && d.ScenarioId == "raid-1");
                List<string> message = d.Message();
                check("the message is three lines, each within a game message",
                    message.Count == Debrief.MessageLines && message.TrueForAll(l => l.Length > 0 && l.Length <= TriggerPlan.MessageLength), string.Join(" / ", message));
                check("the message says what it counts and what it leaves out",
                    message[0] == "Debrief: 1:56 recorded, first contact at 0:30." && message[1] == "Enemies down: 1; 2 taken out by edits, not counted; 1 gone, reason not known."
                    && message[2] == "Triggers fired: 1 of 2; Game Master cards fired: 1.", string.Join(" / ", message));
                using (JsonDocument doc = JsonDocument.Parse(d.ToJson()))
                {
                    JsonElement r = doc.RootElement;
                    check("the JSON answer carries the facts, milestones, message and text",
                        r.GetProperty("schema").GetString() == Debrief.Schema && r.GetProperty("facts").GetArrayLength() == d.Facts.Count && r.GetProperty("milestones").GetArrayLength() == 2
                        && r.GetProperty("message").GetArrayLength() == 3 && r.GetProperty("text").GetString() == d.Text() && r.GetProperty("notRecorded").GetArrayLength() == 3);
                }
            }
        }

        {   // Semantics 2, no run record: the guesses say so, and nothing is filled in from elsewhere.
            Debrief d = Debrief.Read("old.replay.json", old, n => { throw new InvalidOperationException("a recording without a run must not ask for one"); }, out string error);
            check("a recording of semantics 2 reads", d != null, error);
            if (d != null)
            {
                check("the older debrief text is the expected one", d.Text() == Fixture("old.expected.txt").TrimEnd(), d.Text());
                check("down, first contact and milestones are marked as guesses", Fact(d, "down").Guess && Fact(d, "contact").Guess && Fact(d, "fired").Guess && d.Milestones.TrueForAll(m => m.Guess));
                check("a removal by an edit is still counted apart", Fact(d, "removed") != null && !Fact(d, "removed").Guess && Fact(d, "removed").Count == 1);
                check("no gone or remade numbers are made up for it", Fact(d, "gone") == null && Fact(d, "remade") == null);
                check("without a run record the elapsed time is not known", Fact(d, "elapsed").Count < 0 && Fact(d, "result") == null && Fact(d, "squad") == null && !d.HasRun);
                check("the door note is not taken for a trigger", d.Milestones.Count == 2 && d.Fired.Count == 2);
                check("the older message marks the guess", d.Message()[1].StartsWith("Enemies down (a guess): 1", StringComparison.Ordinal), d.Message()[1]);
            }
        }

        {   // What is not a recording is refused with a reason.
            check("a scenario is not a recording", Debrief.Read("x.json", "{\"schema\":\"missionlab-plan/1\",\"spawns\":[]}", null, out string e1) == null && e1.Contains("not a MissionLab recording"), e1);
            check("a recording without moments is refused", Debrief.Read("x.json", "{\"schema\":\"missionlab-replay/1\",\"frames\":[]}", null, out string e2) == null && e2.Contains("moments"), e2);
            check("text that is not JSON is refused", Debrief.Read("x.json", "{oops", null, out string e3) == null && e3 != "", e3);
            string open = run.Replace("\"end\":{\"utc\":\"2026-11-18T12:10:10.0000000Z\",\"seconds\":610,\"result\":\"success\"}", "\"end\":null");
            Debrief d = Debrief.Read(RaidReplay, replay, n => open, out _);
            check("a run whose end was not seen leaves the elapsed time unknown", d != null && Fact(d, "elapsed").Count < 0 && Fact(d, "elapsed").Value == "not known: the end was not seen" && Fact(d, "result") == null);
        }

        {   // Unused content across runs: grouped only when the scenario and the resolved setup match.
            string Rename(string text, string runName) => text.Replace(RaidRun, runName);
            string replayB = Rename(replay, "b.run.json").Replace("[3,20,0,20,0,\"I\",\"s3\"]", "[3,20,0,20,0,\"S\",\"s3\"]");     // the officer joins in, in run B
            string runB = run.Replace(RaidReplay, "b.json"), runC = run.Replace(RaidReplay, "c.json").Replace("\"seed\":42,\"scene\"", "\"seed\":43,\"scene\"");
            string runD = run.Replace("\"outcome\":\"used\"", "\"outcome\":\"refused\"").Replace("\"replay\":\"" + RaidReplay + "\"", "\"replay\":\"\"");
            runD = runD.Substring(0, runD.IndexOf(",\"resolved\":", StringComparison.Ordinal)) + ",\"resolved\":null}";
            string runE = run.Replace("\"scenarioId\":\"raid-1\"", "\"scenarioId\":\"other\""), runF = run.Replace(RaidReplay, "f.json");
            var replays = new Dictionary<string, string> { [RaidReplay] = replay, ["b.json"] = replayB, ["c.json"] = Rename(replay, "c.run.json") };
            var runs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("f.run.json", runF), new KeyValuePair<string, string>("e.run.json", runE), new KeyValuePair<string, string>("d.run.json", runD),
                new KeyValuePair<string, string>("c.run.json", runC), new KeyValuePair<string, string>("b.run.json", runB), new KeyValuePair<string, string>(RaidRun, run),
            };
            UnusedReport report = UnusedReport.Build("raid-1", runs, n => replays.TryGetValue(n, out string t) ? t : null);
            check("only the scenario's own runs are looked at", report.Looked == 5, report.Text());
            check("a different setup after the dice is not grouped with the others", report.Groups.Count == 2 && report.Groups[0].Runs.Count == 2 && report.Groups[1].Runs.Count == 1
                  && report.Groups[0].ResolvedHash != report.Groups[1].ResolvedHash && report.Groups[0].ScenarioHash == report.Groups[1].ScenarioHash, report.Text());
            check("a refused setup and a recording no longer kept are named, not compared",
                report.Skipped.Count == 2 && report.Skipped.Exists(s => s.Key == "d.run.json" && s.Value.Contains("refused")) && report.Skipped.Exists(s => s.Key == "f.run.json" && s.Value.Contains("no longer there")), report.Text());
            UnusedGroup g = report.Groups[0];
            var refs = g.Suggestions.ConvertAll(s => s.Kind + ":" + s.Ref);
            check("the trigger that never fired is suggested; the one that lost its roll and the one that fired are not",
                refs.Contains("trigger:3") && !refs.Contains("trigger:1") && !refs.Contains("trigger:2"), string.Join(", ", refs));
            check("enemies that never left idle in any run are suggested, in spawn order; one active in either run is not",
                string.Join(",", refs.FindAll(r => r.StartsWith("enemy:", StringComparison.Ordinal))) == "enemy:s0,enemy:s4", string.Join(", ", refs));
            check("markers placed by the Game Master are never suggested", !refs.Exists(r => r.Contains(":m")));
            UnusedSuggestion late = g.Suggestions.Find(s => s.Ref == "s4");
            check("an enemy is named with what brings it in", late != null && late.Name == "spawn #5, brought in by trigger 1", late == null ? "" : late.Name);
            check("every item is a suggestion with the count of runs looked at",
                g.Suggestions.TrueForAll(s => s.Text.Contains("Worth a look, not a fault") && s.Text.Contains("any of the 2 runs looked at")), string.Join(" / ", g.Suggestions.ConvertAll(s => s.Text)));
            UnusedGroup lone = report.Groups[1];
            check("the officer is suggested in the setup where it stayed idle", lone.Suggestions.Exists(s => s.Ref == "s3") && lone.Notes.Exists(n => n.StartsWith("One run says little", StringComparison.Ordinal)));
            check("a recording no longer kept is not taken for a run where nothing happened", !g.Runs.Contains("f.run.json"));
            using (JsonDocument doc = JsonDocument.Parse(report.ToJson()))
                check("the report's JSON lists groups, suggestions and the runs not compared",
                    doc.RootElement.GetProperty("schema").GetString() == UnusedReport.Schema && doc.RootElement.GetProperty("groups").GetArrayLength() == 2
                    && doc.RootElement.GetProperty("skipped").GetArrayLength() == 2 && doc.RootElement.GetProperty("looked").GetInt32() == 5);
            check("the report's text starts with the number of runs looked at", report.Text().StartsWith("Unused content in raid-1: suggestions for review, from 5 runs looked at", StringComparison.Ordinal), report.Text());

            Debrief one = Debrief.Read(RaidReplay, replay, null, out _);
            UnusedGroup offline = UnusedReport.Compare(new List<Debrief> { one });
            check("one recording on its own (the page offline) is compared with nothing and says so",
                offline.Runs.Count == 1 && offline.Notes.Exists(n => n.StartsWith("Without the run record", StringComparison.Ordinal)) && offline.Suggestions.Exists(s => s.Ref == "2"),
                string.Join(" / ", offline.Notes));
            string offlineText = "Unused content in " + one.ScenarioId + ": suggestions for review, from 1 run looked at\n" + string.Join("\n", UnusedReport.GroupLines(offline, "Setup 1"));
            check("the one-run report is the text the page writes offline", offlineText == Fixture("raid.unused.txt").TrimEnd(), offlineText);
            Debrief older = Debrief.Read("old.replay.json", old, null, out _);
            UnusedGroup olderGroup = UnusedReport.Compare(new List<Debrief> { older });
            check("a recording without fire events or identities judges neither triggers nor enemies",
                olderGroup.Suggestions.Count == 0 && olderGroup.TriggerRuns == 0 && olderGroup.EnemyRuns == 0 && olderGroup.Notes.Count >= 2, string.Join(" / ", olderGroup.Notes));
        }
    }
}
