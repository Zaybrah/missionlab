using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// Rehearsal through the browser link (ML-016): what POST /api/rehearse answers, how a kept test is named and checked,
/// and the editor's trace fixture, which the page's script builder must write byte for byte (tests/editor/run.mjs).
/// The plugin's Rehearsals.cs only moves files and calls RehearsalFile, so these are the endpoint's rules.
/// </summary>
static class RehearsalsTests
{
    private static string Fixture(string name)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        return root == null ? null : File.ReadAllText(Path.Combine(root, "tests", "fixtures", "rehearsal", name)).Replace("\r\n", "\n");
    }

    private static string Body(string scenario, string script) =>
        "{\"scenario\":" + JsonSerializer.Serialize(scenario) + ",\"script\":" + JsonSerializer.Serialize(script) + "}";

    private static JsonElement Answer(string body, out int status)
    {
        string json = RehearsalFile.Rehearse(body, out status);
        using (JsonDocument doc = JsonDocument.Parse(json)) return doc.RootElement.Clone();
    }

    public static void Run(Action<string, bool, string> check)
    {
        string scenario = Fixture("phone_countdown.json"), trace = Fixture("editor_trace.rehearsal.json");

        {   // The editor's trace fixture reads against its scenario and is already canonical: the page writes the same text.
            ScenarioFile.Read(scenario, out MissionPlan plan);
            var found = Rehearsal.Read(trace, out RehearsalScript s, plan);
            check("the editor's trace fixture reads with nothing found", s != null && found.Count == 0, string.Join(" | ", found));
            check("the editor's trace fixture is written canonically", s != null && Rehearsal.Write(s) == trace.Trim(), s == null ? "" : Rehearsal.Write(s));
        }
        {   // The answer: passed, every step with expected and actual, the scenario's fingerprint and revision, and the assumptions.
            JsonElement a = Answer(Body(scenario, trace), out int status);
            check("a rehearsal is answered with 200", status == 200, status.ToString());
            check("the editor's trace passes against its scenario", a.GetProperty("passed").GetBoolean(),
                string.Join(" | ", a.GetProperty("steps").EnumerateArray().Where(x => !x.GetProperty("ok").GetBoolean()).Select(x => x.GetProperty("what").GetString() + " -> " + x.GetProperty("actual").GetString())));
            check("it names the scenario's fingerprint and revision", a.GetProperty("scenarioHash").GetString() == ScenarioFile.Hash(scenario) && a.GetProperty("revision").GetInt32() == 0, a.GetProperty("scenarioHash").GetString());
            check("it says the injected events are assumptions", a.GetProperty("assumptions").GetString().Contains("spotting, pathfinding, physics, networking and combat are not predicted"), a.GetProperty("assumptions").GetString());
            JsonElement[] steps = a.GetProperty("steps").EnumerateArray().ToArray();
            JsonElement fire = steps.FirstOrDefault(x => x.GetProperty("what").GetString().StartsWith("step 9 at 6 s: trigger 2 is fired by hand"));
            check("each step is named by number and time, with expected and actual", fire.ValueKind == JsonValueKind.Object && fire.GetProperty("expected").GetString() == "it fires" && fire.GetProperty("actual").GetString() == "it fired",
                string.Join(" | ", steps.Select(x => x.GetProperty("what").GetString())));
            JsonElement held = steps.FirstOrDefault(x => x.GetProperty("what").GetString().Contains("trigger 3 'Pick-up' is holding"));
            check("a phase step names the trigger by number and name", held.ValueKind == JsonValueKind.Object && held.GetProperty("ok").GetBoolean() && held.GetProperty("at").GetDouble() == 10, held.ToString());
            check("a script written for other text is reported as changed, with both revisions", a.GetProperty("match").GetString() == RehearsalFile.Changed &&
                a.GetProperty("matchWhy").GetString().Contains("written for revision 3, this is revision 0"), a.GetProperty("matchWhy").GetString());
        }
        {   // The same script pinned to this scenario's text matches; one with no fingerprint says a change cannot be told.
            Rehearsal.Read(trace, out RehearsalScript s);
            s.Scenario = ScenarioFile.Hash(scenario); s.Revision = 0;
            JsonElement same = Answer(Body(scenario, Rehearsal.Write(s)), out _);
            check("a script written for this very text matches it", same.GetProperty("match").GetString() == RehearsalFile.Same, same.GetProperty("matchWhy").GetString());
            JsonElement crlf = Answer(Body(scenario.Replace("\n", "\r\n"), Rehearsal.Write(s)), out _);
            check("line endings alone do not make a scenario changed", crlf.GetProperty("match").GetString() == RehearsalFile.Same, crlf.GetProperty("matchWhy").GetString());
            s.Scenario = "";
            JsonElement none = Answer(Body(scenario, Rehearsal.Write(s)), out _);
            check("a script without a fingerprint says so", none.GetProperty("match").GetString() == RehearsalFile.Unknown, none.GetProperty("matchWhy").GetString());
        }
        {   // A failing expectation is reported by step and trigger, and the whole rehearsal fails.
            string wrong = trace.Replace("{\"at\":7,\"expect\":{\"enemies\":5}}", "{\"at\":7,\"expect\":{\"enemies\":9}}");
            JsonElement a = Answer(Body(scenario, wrong), out _);
            JsonElement bad = a.GetProperty("steps").EnumerateArray().FirstOrDefault(x => !x.GetProperty("ok").GetBoolean());
            check("a wrong enemy count fails the rehearsal", !a.GetProperty("passed").GetBoolean(), a.ToString());
            check("the failing step is named with expected and actual", bad.ValueKind == JsonValueKind.Object && bad.GetProperty("what").GetString().StartsWith("step 12 at 7 s: 9 enemies") &&
                bad.GetProperty("expected").GetString() == "9" && bad.GetProperty("actual").GetString().StartsWith("5 "), bad.ToString());
        }
        {   // A scenario the Director would refuse is not rehearsed: the answer says so, with the findings.
            JsonElement a = Answer(Body("{\"schema\":\"missionlab-plan/1\",\"triggers\":[{\"id\":1,\"when\":\"teleport\"}],\"spawns\":[{\"pos\":[0,0,0]}]}", trace), out int status);
            check("a refused scenario is an answer, not a broken request", status == 200, status.ToString());
            check("it says the scenario was refused, with its findings", a.GetProperty("refused").GetString() == "scenario" && !a.GetProperty("passed").GetBoolean() &&
                a.GetProperty("findings").EnumerateArray().Any(f => f.GetProperty("code").GetString() == "unknown-kind"), a.ToString());
        }
        {   // A script that names a trigger the scenario lacks is refused at that step.
            string bad = trace.Replace("{\"at\":6,\"fire\":2}", "{\"at\":6,\"fire\":9}");
            JsonElement a = Answer(Body(scenario, bad), out _);
            check("a script naming a missing trigger is refused with the step's place", a.GetProperty("refused").GetString() == "script" &&
                a.GetProperty("findings").EnumerateArray().Any(f => f.GetProperty("code").GetString() == "missing-ref" && f.GetProperty("path").GetString() == "steps[8].fire"), a.ToString());
        }
        {   // A request that is not usable at all is a 400 with nothing run.
            RehearsalFile.Rehearse("not json", out int s1);
            RehearsalFile.Rehearse("{\"scenario\":\"{}\"}", out int s2);
            RehearsalFile.Rehearse("[1,2]", out int s3);
            check("a request without JSON, without a script or of the wrong shape is refused", s1 == 400 && s2 == 400 && s3 == 400, s1 + " " + s2 + " " + s3);
        }
        {   // Keeping a test: the name gives a safe file name, the text is kept canonically, nothing reaches another folder.
            string why = RehearsalFile.Prepare(trace, "Phone starts the countdown!", out string canonical, out string file, out _);
            check("a test is ready to keep under a safe file name", why == "" && file == "Phone_starts_the_countdown.json", why + " " + file);
            check("the kept text is canonical and carries the name it was saved under", canonical.Contains("\"name\":\"Phone starts the countdown!\"") && canonical.Contains("\"scenario\":\"fnv1a64:0123456789abcdef\"") && canonical.Contains("\"revision\":3"), canonical);
            RehearsalFile.Prepare(trace, "", out _, out string kept, out _);
            check("without a name the script's own name is used", kept == "editor_trace_one_row_of_every_kind.json", kept);
            string none = RehearsalFile.Prepare(trace.Replace("\"name\":\"editor trace: one row of every kind\",", ""), "  ", out _, out _, out _);
            check("a test with no usable name is not kept", none != "", none);
            string broken = RehearsalFile.Prepare("{\"schema\":\"missionlab-rehearsal/9\",\"steps\":[]}", "x", out _, out _, out List<Finding> found);
            check("a test from a newer version is not kept, with the reason", broken.Contains("missionlab-rehearsal/1") && found.Any(f => f.Code == "future-version"), broken);
            check("only names the page could have made are files", RehearsalFile.GoodFile("a_b-1.json") && !RehearsalFile.GoodFile("../a.json") && !RehearsalFile.GoodFile("a/b.json") &&
                !RehearsalFile.GoodFile("a.txt") && !RehearsalFile.GoodFile("con.json") && !RehearsalFile.GoodFile(""), "");
        }
        {   // The list shows a kept test's name, fingerprint and revision, and a broken file with its reason.
            var sb = new StringBuilder("[");
            RehearsalFile.Summary(sb, "t.json", trace, "2026-11-17T10:00:00Z");
            sb.Append(',');
            RehearsalFile.Summary(sb, "broken.json", "{", "");
            JsonElement[] list;
            using (JsonDocument doc = JsonDocument.Parse(sb.Append(']').ToString())) list = doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
            check("a kept test is listed with its name, scenario fingerprint, revision and steps", list[0].GetProperty("name").GetString() == "editor trace: one row of every kind" &&
                list[0].GetProperty("scenario").GetString() == "fnv1a64:0123456789abcdef" && list[0].GetProperty("revision").GetInt32() == 3 && list[0].GetProperty("steps").GetInt32() == 16 && list[0].GetProperty("error").GetString() == "", list[0].ToString());
            check("a broken kept file is listed with its reason", list[1].GetProperty("error").GetString().Contains("not a JSON file"), list[1].ToString());
        }
    }
}
