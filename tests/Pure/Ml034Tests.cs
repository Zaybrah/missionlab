using System;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// ML-034: the strict reader judges the structural cases in tests/fixtures/schema as docs/scenario.schema.json does
/// (tools/schema_check.py runs the same files through a real JSON Schema validator), and it reads the format line by
/// the same rule as the editor.
/// </summary>
static class Ml034Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures", "schema"))) root = Path.GetDirectoryName(root);
        check("the structural schema fixtures are found", root != null, AppContext.BaseDirectory);
        if (root == null) return;
        string dir = Path.Combine(root, "tests", "fixtures", "schema");
        string Codes(System.Collections.Generic.List<Finding> found) => string.Join(" | ", found.Select(f => f.Code + " " + f.Path));

        {   // What the schema accepts the reader loads, shaped areas included.
            string[] files = Directory.GetFiles(Path.Combine(dir, "valid"), "*.json");
            check("there are structural cases the schema must accept", files.Length >= 3, files.Length.ToString());
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                var found = ScenarioFile.Read(text, out MissionPlan plan);
                check("schema/valid/" + Path.GetFileName(file) + " is read by the strict reader", !ScenarioFile.Blocks(found) && plan != null, Codes(found));
                if (plan != null && text.Contains("\"area\"")) check("schema/valid/" + Path.GetFileName(file) + " keeps its shaped areas", plan.Triggers.Count > 0 && plan.Triggers.All(t => t.Area != null), plan.ToJson());
            }
        }
        {   // What the schema refuses the reader refuses too, and nothing of it is loaded.
            string[] files = Directory.GetFiles(Path.Combine(dir, "invalid"), "*.json");
            check("there are structural cases the schema must refuse", files.Length >= 3, files.Length.ToString());
            foreach (string file in files)
            {
                var found = ScenarioFile.Read(File.ReadAllText(file), out MissionPlan plan);
                check("schema/invalid/" + Path.GetFileName(file) + " is refused by the strict reader", ScenarioFile.Blocks(found) && plan == null, Codes(found));
            }
        }
        {   // The format line is digits after the slash and nothing else, so the plugin and the editor agree on every file.
            const string Body = ",\"mission\":{\"spawns\":[{\"pos\":[0,0,0]}]}}";
            string First(string schema) { var f = ScenarioFile.Read("{\"schema\":\"" + schema + "\"" + Body, out _); return f.Count == 0 ? "" : f[0].Code + " " + f[0].Path; }
            foreach (string odd in new[] { "missionlab-plan/+2", "missionlab-plan/ 2", "missionlab-plan/2 ", "missionlab-plan/", "missionlab-plan/2.0" })
                check("\"" + odd + "\" is not a scenario format", First(odd) == "unknown-format schema", First(odd));
            var lead = ScenarioFile.Read("{\"schema\":\"missionlab-plan/02\"" + Body, out MissionPlan two);
            check("a leading zero still names missionlab-plan/2", !ScenarioFile.Blocks(lead) && two != null, Codes(lead));
            check("a number too big to hold is a newer format, not an unknown one", First("missionlab-plan/99999999999") == "future-version schema", First("missionlab-plan/99999999999"));
        }
    }
}
