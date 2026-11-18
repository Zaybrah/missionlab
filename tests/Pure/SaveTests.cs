using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// A Game Master save of an authored scenario (ML-008): the plan it builds from its own markers and settings gets everything
/// else of the authored file back through MissionPlan.CarryAuthored, key by key, with the revision one higher.
/// </summary>
static class SaveTests
{
    // What the Game Master edits live: these keys are written as it has them, never as the author had them.
    private static readonly string[] GmOwned = { "name", "briefing", "spawns", "requestedEnemies", "groups", "triggers", "objectives", "doors", "things",
                                                 "extraction", "insertion", "weather", "power", "skill", "nvg", "gameMaster", "respawn" };

    public static void Run(Action<string, bool, string> check)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        check("the fixtures for saving are found", root != null, "");
        if (root == null) return;
        var carriedSeen = new HashSet<string>();
        foreach (string name in new[] { "all_fields_v26.json", "all_fields_v2.json" })
        {
            MissionPlan authored = MissionPlan.FromJson(File.ReadAllText(Path.Combine(root, "tests", "fixtures", name)));
            authored.Notes.Add("The author's own note");
            if (name == "all_fields_v2.json") authored.NavFile = "fixture_nav.json";
            string authoredJson = authored.ToJson();

            // Built the way the Game Master saves: its own enemies, triggers and settings, seed 0, the map it is on.
            var saved = new MissionPlan { Seed = 0, Scene = authored.Scene, NavFile = "live_map.json" };
            saved.Spawns.AddRange(authored.Spawns.Take(authored.Spawns.Count - 1));
            foreach (SpawnPlan s in saved.Spawns) saved.Groups = Math.Max(saved.Groups, s.Group + 1);
            saved.Triggers.AddRange(authored.Triggers);
            saved.Doors.AddRange(authored.Doors);
            saved.Things.AddRange(authored.Things);
            saved.Title = "Edited live"; saved.Briefing = new List<string>(authored.Briefing);
            saved.ExtractionAfter = authored.ExtractionAfter; saved.Insertion = authored.Insertion; saved.Weather = authored.Weather; saved.Power = authored.Power;
            saved.Skill = authored.Skill; saved.Nvg = authored.Nvg; saved.GameMaster = authored.GameMaster; saved.RespawnAfter = authored.RespawnAfter; saved.RespawnTimes = authored.RespawnTimes;
            saved.RequestedEnemies = saved.Spawns.Count;
            saved.Notes.Add("Saved from Game Master mode");
            string gmJson = saved.ToJson();

            MissionPlan.CarryAuthored(authored, saved);
            string savedJson = saved.ToJson();

            using JsonDocument a = JsonDocument.Parse(authoredJson), g = JsonDocument.Parse(gmJson), s2 = JsonDocument.Parse(savedJson);
            JsonElement ar = Body(a.RootElement), gr = Body(g.RootElement), sr = Body(s2.RootElement);
            check(name + ": the saved file has the authored format line", a.RootElement.GetProperty("schema").GetString() == s2.RootElement.GetProperty("schema").GetString(), "");
            foreach (JsonProperty p in ar.EnumerateObject())
            {
                string key = p.Name;
                bool has = sr.TryGetProperty(key, out JsonElement got);
                if (key == "revision")
                {
                    check(name + ": the revision is the authored one plus one", has && got.GetInt32() == p.Value.GetInt32() + 1, has ? got.ToString() : "missing");
                    continue;
                }
                if (key == "notes")
                {
                    var want = authored.Notes.Concat(new[] { "Saved from Game Master mode" }).ToList();
                    var gotNotes = has ? got.EnumerateArray().Select(n => n.GetString()).ToList() : new List<string>();
                    check(name + ": the author's notes come first, then the save's", gotNotes.SequenceEqual(want), string.Join(" | ", gotNotes));
                    continue;
                }
                if (GmOwned.Contains(key))
                {
                    bool gmHas = gr.TryGetProperty(key, out JsonElement gmValue);
                    check(name + ": " + key + " is written as the Game Master has it", has == gmHas && (!has || got.GetRawText() == gmValue.GetRawText()), has ? got.GetRawText() : "missing");
                    continue;
                }
                carriedSeen.Add(key);
                if (key == "navFile" && p.Value.ValueKind == JsonValueKind.Null)
                {
                    check(name + ": with no map file of the author's, the live one is written", has && got.GetString() == "live_map.json", has ? got.GetRawText() : "missing");
                    continue;
                }
                check(name + ": " + key + " is kept as the author wrote it", has && got.GetRawText() == p.Value.GetRawText(), has ? got.GetRawText() : "missing");
            }
            if (!ar.TryGetProperty("revision", out _))
                check(name + ": a file without a revision is saved as revision 1", sr.TryGetProperty("revision", out JsonElement r1) && r1.GetInt32() == 1, "");
            check(name + ": the enemy the Game Master removed stays removed", saved.Spawns.Count == authored.Spawns.Count - 1 && sr.GetProperty("spawns").GetArrayLength() == authored.Spawns.Count - 1, "");
            check(name + ": the live title is kept", saved.Title == "Edited live", saved.Title);
        }
        {   // A file that names no map file keeps the one the Game Master was on.
            var authored = new MissionPlan { Scene = "Fixture", Id = "sc-plain", Revision = 4 };
            var saved = new MissionPlan { Scene = "Fixture", NavFile = "live_map.json" };
            MissionPlan.CarryAuthored(authored, saved);
            check("a scenario naming no map file keeps the live map file", saved.NavFile == "live_map.json" && saved.Id == "sc-plain" && saved.Revision == 5, saved.NavFile);
        }
        string[] mustSee = { "id", "squad", "minutes", "sealed", "seed", "scene", "navFile", "mapPrint", "places", "placement", "options", "director" };
        var unseen = mustSee.Where(k => !carriedSeen.Contains(k)).ToList();
        check("the fixtures between them carry every authored key", unseen.Count == 0, string.Join(", ", unseen));
    }

    private static JsonElement Body(JsonElement root) =>
        root.TryGetProperty("mission", out JsonElement body) && body.ValueKind == JsonValueKind.Object ? body : root;
}
