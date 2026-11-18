using System;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// missionlab-plan/2 (ML-027 format boundary, ML-018 places, ML-013 metadata): the envelope is written only when a trigger has a
/// shaped area, v0.23's own reader (tests/Pure/Legacy) refuses such a file instead of reading the shape as a circle, and
/// the new keys survive save and load.
/// </summary>
static class FormatV2Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        check("the fixtures for format v2 are found", root != null, "");
        if (root == null) return;
        string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests", "fixtures", name));
        string v2 = Fixture("all_fields_v2.json"), v1 = Fixture("all_fields.json");
        const string Envelope = "{\"schema\":\"missionlab-plan/2\",\"mission\":{";
        bool OldReaderThrows(string json) { try { Legacy023.MissionPlan.FromJson(json); return false; } catch (Exception) { return true; } }

        {   // Every plan/2 field survives reading and writing, twice over, as the same text.
            var p = MissionPlan.FromJson(v2);
            string once = p.ToJson(), twice = MissionPlan.FromJson(once).ToJson();
            check("every plan/2 field: what is written reads back to the same text", once == twice, once);
            check("a plan with a shaped area is written as the plan/2 envelope", once.StartsWith(Envelope, StringComparison.Ordinal) && once.EndsWith("]}}", StringComparison.Ordinal), once.Substring(0, Math.Min(80, once.Length)));
            check("the body under mission has no schema key of its own", !once.Substring(Envelope.Length).Contains("\"schema\""), once);
            check("the shaped area is written right after the radius, with its hole", once.Contains("\"radius\":6,\"area\":{\"parts\":[{\"floor\":[-1,3],\"outline\":[[-10,-10],[10,-10],[10,10],[-10,10]],\"holes\":[[[-2,-2],[2,-2],[2,2],[-2,2]]]},{\"floor\":[3,6],"), once);
            check("both triggers keep their areas and the hold keeps everyone", p.Triggers.Count == 2 && p.Triggers[0].Area != null && p.Triggers[0].Area.Parts.Count == 2 && p.Triggers[0].Area.Parts[0].Holes.Count == 1
                  && p.Triggers[1].Area != null && p.Triggers[1].Everyone && p.Spawns.Count == 2, once);
        }
        {   // The strict reader takes plan/2 and reads the body under mission.
            var findings = ScenarioFile.Read(v2, out MissionPlan p);
            check("the strict reader reads all_fields_v2.json without a finding", findings.Count == 0 && p != null, string.Join(" | ", findings.Select(f => f.Code + " " + f.Path)));
            check("and it reads the areas, places and metadata", p != null && p.Triggers[0].Area != null && p.Places.Count == 2 && p.Id == "sc-fixture01", "");
            var again = ScenarioFile.Read(MissionPlan.FromJson(v2).ToJson(), out MissionPlan back);
            check("the strict reader reads a plan/2 file that ToJson wrote", !ScenarioFile.Blocks(again) && back != null && back.Triggers[0].Area != null, string.Join(" | ", again.Select(f => f.Code + " " + f.Path)));
        }
        {   // The format boundary: v0.23 skips a plan/2 file rather than reading its shapes as circles.
            check("v0.23's reader refuses all_fields_v2.json", OldReaderThrows(v2), "");
            check("v0.23's reader refuses the plan/2 text ToJson writes", OldReaderThrows(MissionPlan.FromJson(v2).ToJson()), "");
            var p = MissionPlan.FromJson(v1);
            p.Triggers[0].Area = MissionPlan.FromJson(v2).Triggers[0].Area;
            string shaped = p.ToJson();
            check("an old plan given one shaped zone becomes plan/2, and v0.23 refuses it", shaped.StartsWith(Envelope, StringComparison.Ordinal) && OldReaderThrows(shaped), shaped.Substring(0, Math.Min(80, shaped.Length)));
            string plain = MissionPlan.FromJson(v1).ToJson();
            check("v0.23's reader still reads a plan/1 file written today", !OldReaderThrows(plain) && Legacy023.MissionPlan.FromJson(plain).Spawns.Count == 5, "");
        }
        {   // A plan without new keys is written exactly as v0.23 wrote it.
            foreach (string json in new[] { Fixture("legacy_minimal.json"), "{\"triggers\":[{\"id\":1,\"when\":\"zone\",\"pos\":[1,2,3],\"radius\":5},{\"id\":2,\"when\":\"alert\",\"delay\":4}],\"spawns\":[{\"pos\":[0,0,0]},{\"pos\":[1,0,0],\"trigger\":1}]}" })
            {
                string now = MissionPlan.FromJson(json).ToJson(), then = Legacy023.MissionPlan.FromJson(json).ToJson();
                check("a plan without new keys is the same text as v0.23 wrote", now == then, "now  " + now + "\n        then " + then);
            }
        }
        {   // Only a shaped zone or hold makes the file plan/2.
            check("all_fields.json is still written as plan/1", MissionPlan.FromJson(v1).ToJson().StartsWith("{\"schema\":\"missionlab-plan/1\",", StringComparison.Ordinal), "");
            var p = MissionPlan.FromJson(v2);
            p.Triggers[0].Area = null; p.Triggers[1].Area = null;
            string text = p.ToJson();
            check("without its areas the same plan is plan/1, and keeps its places", text.StartsWith("{\"schema\":\"missionlab-plan/1\",", StringComparison.Ordinal) && !text.Contains("\"mission\"") && text.Contains("\"places\":["), text);
            var alert = MissionPlan.FromJson("{\"schema\":\"missionlab-plan/2\",\"mission\":{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"area\":{\"parts\":[{\"floor\":[0,3],\"outline\":[[0,0],[10,0],[10,10],[0,10]]}]}}],\"spawns\":[{\"pos\":[0,0,0]}]}}");
            check("an area on an alert trigger is dropped by the lenient reader and does not make plan/2", alert.Triggers[0].Area == null && alert.ToJson().StartsWith("{\"schema\":\"missionlab-plan/1\",", StringComparison.Ordinal), alert.ToJson());
            var empty = MissionPlan.FromJson(v1);
            empty.Triggers[0].Area = new AreaPlan();
            check("an area with no parts does not make plan/2", empty.ToJson() == MissionPlan.FromJson(v1).ToJson(), "");
        }
        {   // A record holding a plan/2 envelope under "plan" still reads.
            var rec = MissionPlan.FromJson("{\"schema\":\"missionlab-mission/1\",\"plan\":" + MissionPlan.FromJson(v2).ToJson() + "}");
            check("a record that holds a plan/2 envelope reads", rec.Triggers.Count == 2 && rec.Triggers[0].Area != null, "");
        }
        {   // Metadata keys are written in their fixed places and survive save and load.
            var p = MissionPlan.FromJson("{\"name\":\"M\",\"id\":\"sc-abc\",\"revision\":4,\"briefing\":[\"Hi.\"],\"squad\":[1,3],\"minutes\":12.5,\"scene\":\"S\",\"mapPrint\":\"fnv1a64:00112233445566ff\",\"spawns\":[{\"pos\":[0,0,0]}]}");
            string text = p.ToJson();
            check("metadata comes in the order schema, name, id, revision, briefing, squad, minutes, seed, scene, navFile, mapPrint",
                  text.StartsWith("{\"schema\":\"missionlab-plan/1\",\"name\":\"M\",\"id\":\"sc-abc\",\"revision\":4,\"briefing\":[\"Hi.\"],\"squad\":[1,3],\"minutes\":12.5,\"seed\":0,\"scene\":\"S\",\"navFile\":null,\"mapPrint\":\"fnv1a64:00112233445566ff\",\"requestedEnemies\":1,", StringComparison.Ordinal), text);
            check("metadata reads back to the same text", MissionPlan.FromJson(text).ToJson() == text, text);
            var v = MissionPlan.FromJson(v2);
            check("all_fields_v2.json's metadata is read", v.Id == "sc-fixture01" && v.Revision == 3 && v.SquadMin == 2 && v.SquadMax == 4 && v.Minutes == 25f && v.MapPrint == "fnv1a64:0123456789abcdef", "");
            var odd = MissionPlan.FromJson("{\"id\":\"has space\",\"revision\":-2,\"squad\":[5,2],\"minutes\":900,\"mapPrint\":\"md5:1\",\"spawns\":[{\"pos\":[0,0,0]}]}");
            check("the lenient reader drops a bad id and map print, and bends the numbers into range", odd.Id == "" && odd.Revision == 0 && odd.SquadMin == 2 && odd.SquadMax == 5 && odd.Minutes == 600f && odd.MapPrint == "", odd.ToJson());
            check("unset metadata writes no keys", !MissionPlan.FromJson(v1).ToJson().Contains("\"revision\"") && !MissionPlan.FromJson(v1).ToJson().Contains("\"squad\""), "");
        }
        {   // Places survive save and load, after things and before the settings.
            var p = MissionPlan.FromJson(v2);
            string text = p.ToJson();
            check("places are written with id, name, pos, floor and area",
                  text.Contains("\"places\":[{\"id\":1,\"name\":\"Rear stairs\",\"pos\":[1,0,1],\"floor\":[0,3]},{\"id\":2,\"name\":\"Courtyard\",\"pos\":[50,0,50],\"area\":{\"parts\":[{\"floor\":[-1,2],\"outline\":[[45,45],[55,45],[55,55],[45,55]]}]}}],\"extraction\":"), text);
            check("places read back with their floor band and area", p.Places.Count == 2 && p.Places[0].Bottom == 0f && p.Places[0].Top == 3f && p.Places[0].Area == null && p.Places[1].Area != null && !p.Places[1].Bottom.HasValue, "");
            var flipped = MissionPlan.FromJson("{\"places\":[{\"id\":1,\"name\":\"Up\",\"pos\":[0,0,0],\"floor\":[4,1]},{\"id\":1,\"name\":\"Twice\",\"pos\":[0,0,0]},{\"id\":2,\"name\":\" \",\"pos\":[0,0,0]}],\"spawns\":[{\"pos\":[0,0,0]}]}");
            check("the lenient reader swaps an inverted floor and drops repeated ids and empty names", flipped.Places.Count == 1 && flipped.Places[0].Bottom == 1f && flipped.Places[0].Top == 4f, flipped.ToJson());
        }
        {   // PlaceAt names the first place that holds a point.
            var p = MissionPlan.FromJson(v2);
            check("a point within 3 m of a point place, inside its floor band, is there", p.PlaceAt(new V3(2, 1, 1)) == "Rear stairs", p.PlaceAt(new V3(2, 1, 1)));
            check("above its floor band it is not", p.PlaceAt(new V3(1, 5, 1)) == "", p.PlaceAt(new V3(1, 5, 1)));
            check("more than 3 m away it is not", p.PlaceAt(new V3(4.5f, 1, 1)) == "", "");
            check("inside an area place's outline and floor it is there", p.PlaceAt(new V3(50, 0, 50)) == "Courtyard", "");
            check("above an area place's floor it is not", p.PlaceAt(new V3(50, 5, 50)) == "", "");
            var bare = new MissionPlan();
            bare.Places.Add(new PlacePlan { Id = 1, Name = "Gate", Pos = new V3(0, 10, 0) });
            check("a point place without a band uses the circle's 2 m height rule", bare.PlaceAt(new V3(0, 12, 0)) == "Gate" && bare.PlaceAt(new V3(0, 12.5f, 0)) == "", "");
        }
        {   // The map fingerprint is FNV-1a 64 over the raw bytes, worked out here from the published constants.
            byte[] bytes = { 0, 1, 2, 255, 128, 0x61 };
            ulong h = 14695981039346656037UL;
            foreach (byte b in bytes) { h ^= b; h = unchecked(h * 1099511628211UL); }
            string want = "fnv1a64:" + h.ToString("x16");
            check("the map fingerprint of fixed bytes matches FNV-1a 64 worked out by hand", ScenarioFile.MapPrint(bytes) == want, ScenarioFile.MapPrint(bytes) + " want " + want);
            check("the map fingerprint of \"abc\" is the known value", ScenarioFile.MapPrint(new byte[] { 0x61, 0x62, 0x63 }) == "fnv1a64:e71fa2190541574b", "");
            check("no bytes give the FNV offset basis", ScenarioFile.MapPrint(new byte[0]) == "fnv1a64:cbf29ce484222325", "");
            check("a map fingerprint passes its own format check", MissionPlan.PrintOk(ScenarioFile.MapPrint(bytes)), "");
        }
        {   // Capabilities: a sorted list of the features a scenario uses.
            var caps = Capabilities.Of(MissionPlan.FromJson(v2));
            check("all_fields_v2.json uses areas, an extraction hold, messages and places", string.Join(",", caps) == "areas,extraction-hold,messages,places", string.Join(",", caps));
            var many = Capabilities.Of(MissionPlan.FromJson(v1));
            check("all_fields.json lists its features sorted, each once", many.SequenceEqual(many.OrderBy(c => c, StringComparer.Ordinal).Distinct()) && many.Contains("door-trigger") && many.Contains("chance") && many.Contains("teams") && !many.Contains("areas"), string.Join(",", many));
        }
    }
}
