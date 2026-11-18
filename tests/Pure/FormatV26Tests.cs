using System;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// The v0.26 keys (ML-021 sealed, ML-022 options, ML-023 placement and scatter, ML-025 director): written in their fixed places
/// only when set, read back to the same text, bent into range by the lenient reader, and files without them unchanged.
/// </summary>
static class FormatV26Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        check("the fixtures for v0.26 are found", root != null, "");
        if (root == null) return;
        string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests", "fixtures", name));
        string v26 = Fixture("all_fields_v26.json");
        string[] newKeys = { "\"sealed\"", "\"placement\"", "\"options\"", "\"director\"", "\"scatter\"", "\"within\"", "\"fallback\"" };

        {   // Every v0.26 field survives reading and writing, twice over, as the same text.
            string once = MissionPlan.FromJson(v26).ToJson(), twice = MissionPlan.FromJson(once).ToJson();
            check("every v0.26 field: what is written reads back to the same text", once == twice, once);
            check("the new keys do not force missionlab-plan/2", once.StartsWith("{\"schema\":\"missionlab-plan/1\",", StringComparison.Ordinal), once.Substring(0, Math.Min(80, once.Length)));
        }
        {   // The strict reader takes the fixture and what ToJson makes of it.
            var findings = ScenarioFile.Read(v26, out MissionPlan p);
            check("the strict reader reads all_fields_v26.json without a finding", findings.Count == 0 && p != null, string.Join(" | ", findings.Select(f => f.Code + " " + f.Path)));
            var again = ScenarioFile.Read(MissionPlan.FromJson(v26).ToJson(), out MissionPlan back);
            check("the strict reader reads what ToJson wrote without a finding", again.Count == 0 && back != null, string.Join(" | ", again.Select(f => f.Code + " " + f.Path)));
        }
        {   // The model holds what the file says.
            var p = MissionPlan.FromJson(v26);
            check("sealed is read", p.Sealed, "");
            check("both options are read with their choices and picks", p.Options.Count == 2 && p.Options[0].Id == "garrison" && p.Options[0].Pick == 1 && p.Options[0].Choices.Count == 2 && p.Options[1].Choices.Count == 3 && p.Options[1].Pick == 0, "");
            ChoicePlan light = p.Options[0].Choices[0], full = p.Options[0].Choices[1];
            check("a choice keeps its drops, respawn times and skill", light.DropLots.SequenceEqual(new[] { 3, 4 }) && light.DropTriggers.SequenceEqual(new[] { 3 }) && light.RespawnTimes == 1 && light.Skill == "recruit", "");
            check("a choice that says nothing changes nothing", full.DropLots.Count == 0 && full.DropTriggers.Count == 0 && full.RespawnTimes == -1 && full.Skill == "", "");
            check("respawn times of 0 is kept apart from unchanged", p.Options[1].Choices[2].RespawnTimes == 0, "");
            check("both placements are read with their modes and areas", p.Placement.Count == 2 && p.Placement[0].Mode == PlacementPlan.Allow && p.Placement[1].Mode == PlacementPlan.Empty && p.Placement[1].Area != null && p.Placement[1].Area.Contains(new V3(20, 0, 20)), "");
            DirectorPlan d = p.Director;
            check("the director is read with its budget and actions", d != null && d.Budget == 10 && d.Regen == 1 && d.Actions.Count == 2, "");
            check("an action keeps every field", d != null && d.Actions[0].Id == "rear" && d.Actions[0].Cost == 3 && d.Actions[0].Cooldown == 60f && d.Actions[0].Fire == 2 && d.Actions[0].MinDistance == 30f && d.Actions[0].Unseen && d.Actions[0].Fallback == ActionPlan.Cancel, "");
            check("an action that says little has the defaults", d != null && d.Actions[1].Cooldown == 0f && d.Actions[1].MinDistance == 0f && !d.Actions[1].Unseen && d.Actions[1].Fallback == ActionPlan.Wait, "");
            check("spawns keep scatter, within and fallback", p.Spawns[0].Scatter == 4.5f && p.Spawns[0].Within == 1 && p.Spawns[1].Fallback == SpawnPlan.Skip && p.Spawns[2].Fallback == SpawnPlan.Stay && p.Spawns[3].Scatter == 0f && p.Spawns[3].Within == 0, "");
        }
        {   // The keys sit in their fixed places.
            string text = MissionPlan.FromJson(v26).ToJson();
            check("sealed comes after the metadata and before the seed", text.Contains("\"id\":\"sc-fixture26\",\"sealed\":true,\"seed\":26,"), text);
            int Where(string key) => text.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            check("placement, options and director come after things and places, before the settings and the spawns",
                  Where("doors") < Where("placement") && Where("placement") < Where("options") && Where("options") < Where("director") && Where("director") < Where("respawn") && Where("respawn") < Where("spawns"), text);
            check("placement is written with id, name, mode only when empty, and area",
                  text.Contains("\"placement\":[{\"id\":1,\"name\":\"Courtyard\",\"area\":{\"parts\":[{\"floor\":[-1,3],\"outline\":[[0,0],[40,0],[40,40],[0,40]]}]}},{\"id\":2,\"name\":\"Street\",\"mode\":\"empty\",\"area\":{\"parts\":[{\"floor\":[-1,3],\"outline\":[[18,18],[22,18],[22,22],[18,22]]}]}}],\"options\":"), text);
            check("options are written with id, name, pick, and choices with name, drop, respawnTimes and skill",
                  text.Contains("\"options\":[{\"id\":\"garrison\",\"name\":\"Garrison size\",\"pick\":1,\"choices\":[{\"name\":\"Light\",\"drop\":{\"lots\":[3,4],\"triggers\":[3]},\"respawnTimes\":1,\"skill\":\"recruit\"},{\"name\":\"Full\"}]},"
                              + "{\"id\":\"wave-only\",\"name\":\"Reinforcements\",\"choices\":[{\"name\":\"None\",\"drop\":{\"triggers\":[2]}},{\"name\":\"Some\",\"drop\":{\"lots\":[5]}},{\"name\":\"Hard\",\"respawnTimes\":0,\"skill\":\"veteran\"}]}],\"director\":"), text);
            check("the director is written with budget, regen and actions, defaults left out",
                  text.Contains("\"director\":{\"budget\":10,\"regen\":1,\"actions\":[{\"id\":\"rear\",\"name\":\"Rear reinforcements\",\"cost\":3,\"cooldown\":60,\"fire\":2,\"minDistance\":30,\"unseen\":true,\"fallback\":\"cancel\"},"
                              + "{\"id\":\"late_wave\",\"name\":\"Late wave now\",\"cost\":5,\"fire\":3}]},\"respawn\":"), text);
            check("spawn keys come after tag and before patrol; fallback only when skip",
                  text.Contains("\"hvt\":true,\"scatter\":4.5,\"within\":1,\"patrol\":[]}") && text.Contains("\"lot\":3,\"scatter\":6,\"within\":1,\"fallback\":\"skip\",\"patrol\":[]}") && text.Contains("\"lot\":4,\"scatter\":2,\"patrol\":[]}"), text);
        }
        {   // Inside the plan/2 envelope the body keeps the same order.
            var p = MissionPlan.FromJson(Fixture("all_fields_v2.json"));
            var extra = MissionPlan.FromJson(v26);
            // all_fields_v2.json has triggers 1 and 2 only: links to trigger 3 would be dropped when read back.
            extra.Director.Actions.RemoveAll(a => a.Fire > 2);
            foreach (OptionPlan o in extra.Options) foreach (ChoicePlan c in o.Choices) c.DropTriggers.RemoveAll(id => id > 2);
            p.Sealed = true; p.Placement = extra.Placement; p.Options = extra.Options; p.Director = extra.Director;
            string text = p.ToJson();
            int Where(string key) => text.IndexOf(",\"" + key + "\":", StringComparison.Ordinal);
            check("a shaped plan with the new keys is plan/2 with them in the body, in order",
                  text.StartsWith("{\"schema\":\"missionlab-plan/2\",\"mission\":{", StringComparison.Ordinal) && text.Contains("\"minutes\":25,\"sealed\":true,\"seed\":9,")
                  && Where("places") < Where("placement") && Where("placement") < Where("options") && Where("options") < Where("director") && Where("director") < text.IndexOf(",\"extraction\":{", StringComparison.Ordinal), text);
            check("and it reads back to the same text", MissionPlan.FromJson(text).ToJson() == text, text);
        }
        {   // Files without the new keys are written exactly as before.
            var plain = new System.Collections.Generic.List<string> { Fixture("all_fields.json"), Fixture("all_fields_v2.json"), Fixture("legacy_minimal.json"), Fixture("extraction_chance_defect.json") };
            // Examples that use a new key themselves (clean_house_sealed.json, ML-021) are not files without them.
            foreach (string file in Directory.GetFiles(Path.Combine(root, "examples"), "*.json"))
            {
                string json = File.ReadAllText(file);
                if (Array.TrueForAll(newKeys, k => !json.Contains(k))) plain.Add(json);
            }
            string found = "";
            foreach (string json in plain)
            {
                string text = MissionPlan.FromJson(json).ToJson();
                foreach (string key in newKeys) if (text.Contains(key)) found += key + " ";
            }
            check("no file without the new keys gains one when written", found == "", found);
            string now = MissionPlan.FromJson(Fixture("legacy_minimal.json")).ToJson(), then = Legacy023.MissionPlan.FromJson(Fixture("legacy_minimal.json")).ToJson();
            check("an old file is still the same text as v0.23 wrote", now == then, "now  " + now + "\n        then " + then);
            var bare = new MissionPlan { Scene = "S" };
            bare.Spawns.Add(new SpawnPlan());
            string text2 = bare.ToJson();
            check("a new plan with default values writes none of the new keys", newKeys.All(k => !text2.Contains(k)), text2);
            check("the defaults are stay, unchanged and none", new SpawnPlan().Fallback == SpawnPlan.Stay && new ChoicePlan().RespawnTimes == -1 && new PlacementPlan().Mode == PlacementPlan.Allow && new ActionPlan().Fallback == ActionPlan.Wait && bare.Director == null, "");
        }
        {   // The lenient reader bends what it can and drops what it cannot use.
            string odd = "{\"sealed\":1,\"triggers\":[{\"id\":1,\"when\":\"alert\"}],"
                + "\"options\":[{\"id\":\"x y\",\"name\":\"Bad id\",\"choices\":[{\"name\":\"a\"},{\"name\":\"b\"}]},{\"id\":\"one\",\"name\":\"One\",\"choices\":[{\"name\":\"a\"}]},"
                + "{\"id\":\"many\",\"name\":\"Many\",\"pick\":9,\"choices\":[{\"name\":\"1\",\"respawnTimes\":99,\"skill\":\"elite\",\"drop\":{\"lots\":[0,2,2],\"triggers\":[1,5]}},{\"name\":\"2\"},{\"name\":\"3\"},{\"name\":\"4\"},{\"name\":\"5\"},{\"name\":\"6\"},{\"name\":\"7\"}]},"
                + "{\"id\":\"many\",\"name\":\"Again\",\"choices\":[{\"name\":\"a\"},{\"name\":\"b\"}]}],"
                + "\"placement\":[{\"id\":1,\"name\":\"A\",\"mode\":\"odd\",\"area\":{\"parts\":[{\"floor\":[0,3],\"outline\":[[0,0],[10,0],[10,10],[0,10]]}]}},{\"id\":1,\"name\":\"Twice\",\"area\":{\"parts\":[{\"floor\":[0,3],\"outline\":[[0,0],[10,0],[10,10],[0,10]]}]}},"
                + "{\"id\":2,\"name\":\"No area\"},{\"id\":3,\"name\":\"C\",\"mode\":\"empty\",\"area\":{\"parts\":[{\"floor\":[0,3],\"outline\":[[20,0],[30,0],[30,10],[20,10]]}]}}],"
                + "\"director\":{\"budget\":5000,\"regen\":-3,\"actions\":[{\"id\":\"a\",\"name\":\"Act\",\"fire\":1,\"cost\":-1,\"cooldown\":9999,\"minDistance\":-5,\"fallback\":\"odd\"},{\"id\":\"b\",\"fire\":9},{\"id\":\"a\",\"fire\":1}]},"
                + "\"spawns\":[{\"pos\":[0,0,0],\"scatter\":99,\"within\":3,\"fallback\":\"skip\",\"hvt\":true},{\"pos\":[1,0,0],\"scatter\":-1,\"within\":1,\"fallback\":\"skip\"},{\"pos\":[2,0,0],\"within\":2,\"vehicle\":true,\"fallback\":\"skip\"}]}";
            var p = MissionPlan.FromJson(odd);
            check("sealed must be true to count", !p.Sealed, "");
            check("options with a bad or repeated id, or fewer than two choices, are dropped", p.Options.Count == 1 && p.Options[0].Id == "many", p.ToJson());
            check("an option keeps its first six choices and its pick is bent into range", p.Options.Count == 1 && p.Options[0].Choices.Count == 6 && p.Options[0].Pick == 5, p.ToJson());
            ChoicePlan c = p.Options.Count == 1 ? p.Options[0].Choices[0] : new ChoicePlan();
            check("a choice's respawn times are bent into range, an unknown skill and bad or missing numbers are dropped", c.RespawnTimes == 50 && c.Skill == "" && c.DropLots.SequenceEqual(new[] { 2 }) && c.DropTriggers.SequenceEqual(new[] { 1 }), p.ToJson());
            check("placements with a repeated id or no area are dropped, an unknown mode is allow", p.Placement.Count == 2 && p.Placement[0].Mode == PlacementPlan.Allow && p.Placement[1].Id == 3 && p.Placement[1].Mode == PlacementPlan.Empty, p.ToJson());
            DirectorPlan d = p.Director;
            check("the budget and regen are bent into range", d != null && d.Budget == 1000 && d.Regen == 0, p.ToJson());
            check("actions with a repeated id or a trigger that is not there are dropped, and their numbers bent into range",
                  d != null && d.Actions.Count == 1 && d.Actions[0].Cost == 0 && d.Actions[0].Cooldown == 3600f && d.Actions[0].MinDistance == 0f && d.Actions[0].Fallback == ActionPlan.Wait, p.ToJson());
            check("scatter is bent into 0 to 30 m", p.Spawns[0].Scatter == 30f && p.Spawns[1].Scatter == 0f, p.ToJson());
            check("within an empty placement or one that is not there is dropped; within an allow placement is kept", p.Spawns[0].Within == 0 && p.Spawns[1].Within == 1 && p.Spawns[2].Within == 0, p.ToJson());
            check("an hvt or a vehicle is never skipped", p.Spawns[0].Fallback == SpawnPlan.Stay && p.Spawns[2].Fallback == SpawnPlan.Stay && p.Spawns[1].Fallback == SpawnPlan.Skip, p.ToJson());
            check("what the lenient reader keeps passes the strict reader", !ScenarioFile.Blocks(ScenarioFile.Read(p.ToJson(), out _)), string.Join(" | ", ScenarioFile.Read(p.ToJson(), out _).Select(f => f.Code + " " + f.Path)));
        }
        {   // A placement without a shape is not written: it would be refused.
            var p = MissionPlan.FromJson(v26);
            p.Placement.Add(new PlacementPlan { Id = 9, Name = "Unshaped" });
            check("a placement without an area is left out of the file", !p.ToJson().Contains("Unshaped"), "");
            p.Placement.Clear(); p.Placement.Add(new PlacementPlan { Id = 9, Name = "Unshaped" });
            check("and with only such placements there is no placement key", !p.ToJson().Contains("\"placement\""), "");
        }
        {   // Capabilities name the new features.
            var caps = Capabilities.Of(MissionPlan.FromJson(v26));
            check("all_fields_v26.json lists director, options, scatter and sealed", caps.Contains("director") && caps.Contains("options") && caps.Contains("scatter") && caps.Contains("sealed"), string.Join(",", caps));
            check("all_fields.json lists none of them", Capabilities.Of(MissionPlan.FromJson(Fixture("all_fields.json"))).Intersect(new[] { "director", "options", "scatter", "sealed" }).Count() == 0, "");
        }
    }
}
