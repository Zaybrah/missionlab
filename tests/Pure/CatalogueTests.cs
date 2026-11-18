using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// The Play view's catalogue (ML-013): an entry built from a scenario's text and the run records. What the author says
/// (squad range, estimate) stays apart from what was measured (the median of runs seen to end); unknown stays unknown;
/// runs are linked by scenario id across renames and by file name for older records; save names cannot leave the folder.
/// </summary>
static class CatalogueTests
{
    private const string Scenario = "{\"schema\":\"missionlab-plan/1\",\"name\":\"Clean House\",\"id\":\"sc-cleanhouse\",\"revision\":3,\"briefing\":[\"Find the phone.\"],"
                                  + "\"squad\":[2,4],\"minutes\":25,\"navFile\":\"suburbv2.bytes\",\"doors\":[{\"name\":\"Front\",\"pos\":[1,0,1],\"locked\":true}],"
                                  + "\"spawns\":[{\"pos\":[1,0,2]},{\"pos\":[3,0,4],\"chance\":50}]}";

    private static RunSummary Run(string file, string json) => RunSummary.Read(file, json);

    private static string Record(string scenario, string id, int revision, string outcome, string end) =>
        "{\"schema\":\"missionlab-run/1\",\"startedUtc\":\"2026-11-17T10:00:00Z\",\"session\":\"m1-1\",\"scenario\":\"" + scenario + "\",\"authoredHash\":\"\",\"scenarioId\":\"" + id
        + "\",\"revision\":" + revision + ",\"outcome\":\"" + outcome + "\",\"reason\":\"\",\"end\":" + end + ",\"resolved\":null}";

    private static string Ended(int seconds, string result) => "{\"utc\":\"2026-11-17T10:30:00Z\",\"seconds\":" + seconds + ",\"result\":\"" + result + "\"}";

    private static JsonElement Json(CatalogueEntry e, string compat, ICollection<string> seen)
    {
        var sb = new StringBuilder();
        CatalogueInfo.Write(e, sb, compat, seen, "Clean House");
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }

    public static void Run(Action<string, bool, string> check)
    {
        {   // With no run on record, the author's words are there and nothing is measured: no duration is made up from the estimate.
            var e = CatalogueInfo.Build("clean_house.json", Scenario, new List<RunSummary>());
            check("a good scenario is readable and usable", e.Readable && e.Usable && e.Why == "", e.Why);
            check("title, id, revision, briefing and map come from the file", e.Title == "Clean House" && e.Id == "sc-cleanhouse" && e.Revision == 3 && e.Briefing.Count == 1 && e.NavFile == "suburbv2.bytes" && e.Value == "clean_house", e.Title + " " + e.Id);
            check("the author's squad range and estimate are kept", e.SquadMin == 2 && e.SquadMax == 4 && e.Minutes == 25f, e.SquadMin + "-" + e.SquadMax + " " + e.Minutes);
            check("nothing measured without runs", e.Measured == null && e.Runs == 0 && e.Finished == 0 && e.LastRun == null, "");
            JsonElement j = Json(e, "tested", new HashSet<string> { "doors" });
            check("the author's numbers are written under author", j.GetProperty("author").GetProperty("minutes").GetDouble() == 25 && j.GetProperty("author").GetProperty("squad")[1].GetInt32() == 4, j.GetRawText());
            check("an unmeasured duration is written as null, not as the estimate", j.GetProperty("measured").GetProperty("minutes").ValueKind == JsonValueKind.Null, j.GetRawText());
            check("no last run is null", j.GetProperty("lastRun").ValueKind == JsonValueKind.Null, j.GetRawText());
            bool doorsTested = false, chanceUntested = false;
            foreach (JsonElement c in j.GetProperty("capabilities").EnumerateArray())
            {
                if (c.GetProperty("name").GetString() == "doors") doorsTested = c.GetProperty("tested").GetBoolean();
                if (c.GetProperty("name").GetString() == "chance") chanceUntested = !c.GetProperty("tested").GetBoolean();
            }
            check("capabilities are marked tested only when listed as seen", doorsTested && chanceUntested, j.GetProperty("capabilities").GetRawText());
            check("the compatibility word is passed through", j.GetProperty("compat").GetString() == "tested" && j.GetProperty("mode").GetString() == "pve", j.GetRawText());
        }

        {   // A scenario with no squad range and no estimate writes null for both: unknown stays unknown.
            var e = CatalogueInfo.Build("bare.json", "{\"schema\":\"missionlab-plan/1\",\"spawns\":[{\"pos\":[0,0,0]}]}", null);
            JsonElement j = Json(e, "", null);
            check("no author squad range is null", j.GetProperty("author").GetProperty("squad").ValueKind == JsonValueKind.Null, j.GetRawText());
            check("no author estimate is null", j.GetProperty("author").GetProperty("minutes").ValueKind == JsonValueKind.Null, j.GetRawText());
            check("a scenario naming no map has unknown compatibility", j.GetProperty("compat").GetString() == "" && e.NavFile == "", j.GetRawText());
        }

        {   // Measured minutes are the median of used runs seen to end; refused runs and runs whose end was never seen do not count.
            var runs = new List<RunSummary>
            {
                Run("20261101-100000_a.run.json", Record("clean_house", "sc-cleanhouse", 1, "used", Ended(1200, "over"))),     // 20 min
                Run("20261102-100000_a.run.json", Record("clean_house", "sc-cleanhouse", 2, "used", Ended(1800, "success"))),  // 30 min
                Run("20261103-100000_a.run.json", Record("clean_house", "sc-cleanhouse", 3, "used", Ended(3000, "failed"))),   // 50 min
                Run("20261104-100000_a.run.json", Record("clean_house", "sc-cleanhouse", 3, "used", "null")),                   // never seen to end
                Run("20261105-100000_a.run.json", Record("clean_house", "sc-cleanhouse", 3, "refused", Ended(60, "over"))),
            };
            var e = CatalogueInfo.Build("clean_house.json", Scenario, runs);
            check("every run of the scenario is counted", e.Runs == 5, e.Runs.ToString());
            check("only used runs seen to end are finished", e.Finished == 3, e.Finished.ToString());
            check("the measured duration is the median, not the mean", e.Measured.HasValue && Math.Abs(e.Measured.Value - 30) < 1e-9, e.Measured.ToString());
            check("the last run is the newest record", e.LastRun != null && e.LastRun.File == "20261105-100000_a.run.json" && e.LastRun.Outcome == "refused", e.LastRun?.File);
            check("the author's estimate is not changed by measurements", e.Minutes == 25f, e.Minutes.ToString());
            JsonElement j = Json(e, "tested", null);
            check("the measured block says how many runs it rests on", j.GetProperty("measured").GetProperty("finished").GetInt32() == 3 && j.GetProperty("measured").GetProperty("minutes").GetDouble() == 30, j.GetRawText());
            var open = Run("x.run.json", Record("clean_house", "sc-cleanhouse", 3, "used", "null"));
            check("a run without an end has no duration", open.Seconds < 0 && open.Result == "" && open.Ended == "", open.Seconds.ToString());
            check("an older record without end or id reads", Run("old.run.json", "{\"schema\":\"missionlab-run/1\",\"scenario\":\"clean_house\",\"outcome\":\"used\"}") is RunSummary o && o.Seconds < 0 && o.ScenarioId == "" && o.Revision == 0, "");
            check("a record that is not JSON is skipped, not thrown", Run("bad.run.json", "{not json") == null, "");
        }

        {   // Median of an even count is the mean of the middle two; empty is null.
            check("median of two", CatalogueInfo.Median(new List<double> { 10, 20 }) == 15, "");
            check("median of none is null", CatalogueInfo.Median(new List<double>()) == null, "");
        }

        {   // Ids decide when both sides have one: a renamed file keeps its runs, and a new file under an old name does not inherit them.
            var renamed = CatalogueInfo.Build("clean_house_v2.json", Scenario, new List<RunSummary> { Run("1.run.json", Record("clean_house", "sc-cleanhouse", 2, "used", Ended(600, "over"))) });
            check("a renamed scenario keeps its runs through its id", renamed.Runs == 1 && renamed.Finished == 1, renamed.Runs.ToString());
            var other = CatalogueInfo.Build("clean_house.json", Scenario.Replace("sc-cleanhouse", "sc-different"), new List<RunSummary> { Run("1.run.json", Record("clean_house", "sc-cleanhouse", 2, "used", Ended(600, "over"))) });
            check("another scenario under the same file name does not inherit runs", other.Runs == 0 && other.Measured == null, other.Runs.ToString());
            var older = CatalogueInfo.Build("clean_house.json", Scenario, new List<RunSummary> { Run("1.run.json", Record("Clean_House", "", 0, "used", Ended(600, "over"))) });
            check("a record without an id is linked by file name, ignoring case", older.Runs == 1, older.Runs.ToString());
        }

        {   // A sealed mission (ML-021): the entry keeps the briefing, requirements and the author's numbers, and leaves out
            // the enemy count; the catalogue never carries positions, rolls or trigger outcomes. The shipped example is used.
            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "examples", "clean_house_sealed.json"))) root = Path.GetDirectoryName(root);
            check("the sealed example is found", root != null, "");
            if (root != null)
            {
                string text = File.ReadAllText(Path.Combine(root, "examples", "clean_house_sealed.json"));
                var e = CatalogueInfo.Build("clean_house_sealed.json", text, new List<RunSummary>());
                check("the sealed example is readable, usable and sealed", e.Readable && e.Usable && e.Sealed && e.Why == "", e.Why);
                JsonElement j = Json(e, "tested", null);
                check("a sealed entry says so", j.GetProperty("sealed").ValueKind == JsonValueKind.True, j.GetRawText());
                check("a sealed entry leaves the enemy count out", j.GetProperty("enemies").ValueKind == JsonValueKind.Null, j.GetRawText());
                check("a sealed entry keeps the briefing and the author's squad and minutes", j.GetProperty("briefing").GetArrayLength() == 3
                      && j.GetProperty("author").GetProperty("squad")[1].GetInt32() == 4 && j.GetProperty("author").GetProperty("minutes").GetDouble() == 15, j.GetRawText());
                bool sealedCap = false;
                foreach (JsonElement c in j.GetProperty("capabilities").EnumerateArray()) if (c.GetProperty("name").GetString() == "sealed") sealedCap = true;
                check("a sealed entry keeps its requirements: map, mode and capabilities", sealedCap && j.GetProperty("navFile").GetString() == "suburbv2.bytes" && j.GetProperty("mode").GetString() == "pve", j.GetRawText());
                string raw = j.GetRawText();
                check("no position or route of the sealed example is in its entry", !raw.Contains("-15") && !raw.Contains("\"pos\"") && !raw.Contains("patrol"), raw);
            }
            JsonElement open = Json(CatalogueInfo.Build("clean_house.json", Scenario, null), "", null);
            check("an unsealed entry says so and keeps its enemy count", open.GetProperty("sealed").ValueKind == JsonValueKind.False && open.GetProperty("enemies").GetInt32() == 2, open.GetRawText());
            check("an entry without options writes an empty list", open.GetProperty("options").GetArrayLength() == 0, open.GetRawText());
        }

        {   // Errors of a sealed mission stay visible: what is wrong and where in the file, not where on the map.
            check("a place in brackets is left out", CatalogueInfo.Unplaced("the target person at (12.5, -4) is not in this mission: lost its roll") == "the target person is not in this mission: lost its roll",
                  CatalogueInfo.Unplaced("the target person at (12.5, -4) is not in this mission: lost its roll"));
            check("a place after 'from' is left out", CatalogueInfo.Unplaced("it can be seen from 12, -34 in the painted area") == "it can be seen in the painted area", CatalogueInfo.Unplaced("it can be seen from 12, -34 in the painted area"));
            check("lists of trigger ids are kept", CatalogueInfo.Unplaced("needs triggers 3, 4 and 5") == "needs triggers 3, 4 and 5", CatalogueInfo.Unplaced("needs triggers 3, 4 and 5"));
            string broken = "{\"schema\":\"missionlab-plan/1\",\"id\":\"sc-sealed-broken\",\"sealed\":true,\"spawns\":[{\"pos\":[0,0,0],\"trigger\":7}]}";
            string refused = "{\"schema\":\"missionlab-run/1\",\"startedUtc\":\"2026-11-17T10:00:00Z\",\"session\":\"m1-1\",\"scenario\":\"broken\",\"authoredHash\":\"\",\"scenarioId\":\"sc-sealed-broken\","
                           + "\"revision\":1,\"outcome\":\"refused\",\"reason\":\"the target person at (3, 4) is not in this mission\",\"end\":null,\"resolved\":null}";
            var e = CatalogueInfo.Build("broken.json", broken, new List<RunSummary> { Run("1.run.json", refused) });
            check("a refused file that says sealed is treated as sealed", !e.Readable && e.Sealed && e.Findings.Exists(f => f.Level == Finding.Error), e.Why);
            JsonElement j = Json(e, "", null);
            check("a sealed entry's refused run keeps its reason without the place", j.GetProperty("lastRun").GetProperty("reason").GetString() == "the target person is not in this mission", j.GetRawText());
            check("sealed is read under mission in plan/2", CatalogueInfo.SealedIn("{\"schema\":\"missionlab-plan/2\",\"mission\":{\"sealed\":true}}") && !CatalogueInfo.SealedIn("{\"sealed\":1}") && !CatalogueInfo.SealedIn("not json"), "");
        }

        {   // A file the strict reader refuses is listed with its findings and is not usable; its id still links refused runs.
            string broken = "{\"schema\":\"missionlab-plan/1\",\"id\":\"sc-broken\",\"revision\":2,\"spawns\":[{\"pos\":[0,0,0],\"trigger\":7}]}";
            var e = CatalogueInfo.Build("broken.json", broken, new List<RunSummary> { Run("1.run.json", Record("broken", "sc-broken", 2, "refused", "null")) });
            check("a refused file is not readable and not usable", !e.Readable && !e.Usable && e.Why.StartsWith("MissionLab would refuse this file", StringComparison.Ordinal), e.Why);
            check("its findings are listed", e.Findings.Exists(f => f.Level == Finding.Error), "");
            check("its id is still taken from the text", e.Id == "sc-broken" && e.Revision == 2 && e.Runs == 1, e.Id);
            var none = CatalogueInfo.Build("empty.json", "{\"schema\":\"missionlab-plan/1\",\"spawns\":[]}", null);
            check("a scenario without enemies is readable but not usable", none.Readable && !none.Usable && none.Why.Contains("no enemies"), none.Why);
            CatalogueInfo.Identity("not json", out string id, out int rev);
            check("identity of text that is not JSON is empty", id == "" && rev == 0, id);
            CatalogueInfo.Identity("{\"schema\":\"missionlab-plan/2\",\"mission\":{\"id\":\"sc-two\",\"revision\":4}}", out id, out rev);
            check("identity is found under mission in plan/2", id == "sc-two" && rev == 4, id);
        }

        {   // Save names stay inside the scenarios folder and never become device names.
            check("a plain name gets .json", CatalogueInfo.SafeName("clean_house") == "clean_house.json", CatalogueInfo.SafeName("clean_house"));
            check(".json is not doubled", CatalogueInfo.SafeName("Clean House.json") == "Clean_House.json", CatalogueInfo.SafeName("Clean House.json"));
            check("folders are flattened", CatalogueInfo.SafeName("../../BepInEx/config/x") == "BepInEx_config_x.json", CatalogueInfo.SafeName("../../BepInEx/config/x"));
            check("nothing usable gives no name", CatalogueInfo.SafeName("../..") == "" && CatalogueInfo.SafeName("  ") == "", CatalogueInfo.SafeName("../.."));
            check("device names are changed", CatalogueInfo.SafeName("con") == "con_.json" && CatalogueInfo.SafeName("LPT1") == "LPT1_.json", CatalogueInfo.SafeName("con"));
            check("long names are cut to 60", CatalogueInfo.SafeName(new string('a', 100)).Length == 65, "");
        }
    }
}
