using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// ML-040: zones and holds linked to a named place. A link is read into the place's one shape by both readers, written as
/// missionlab-plan/3 only when there is a link, refused when it points at nothing, flattened on request into exact copies,
/// kept honest when the Game Master saves, and applied live as one snapshot: every dependant loses its unfinished hold and
/// keeps what it fired, while a zone with its own copy of the same shape goes on as it was.
/// </summary>
static class Ml040Tests
{
    private const string Square = "{\"parts\":[{\"floor\":[-1,3],\"outline\":[[-10,-10],[10,-10],[10,10],[-10,10]]}]}";
    private const string Bigger = "{\"parts\":[{\"floor\":[-1,3],\"outline\":[[-12,-12],[12,-12],[12,12],[-12,12]]}]}";

    private static string Root()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures"))) root = Path.GetDirectoryName(root);
        return root;
    }

    private static string Codes(List<Finding> f) => string.Join(" | ", f.Where(x => x.Level == Finding.Error).Select(x => x.Code + " " + x.Path));

    /// <summary>Players standing still at given spots; a zone is occupied when one of them (or each, for everyone) is inside it.</summary>
    private sealed class Walkers : ITriggerWorld
    {
        public List<V3> At = new List<V3>();
        public bool Alerted() => false;
        public bool IntelTaken(string item) => false;
        public bool Occupied(TriggerPlan t, out V3 where)
        {
            where = default;
            int inside = At.Count(p => Membership.Inside(t, p));
            if (inside == 0 || (t.Everyone && inside < At.Count)) return false;
            where = At.First(p => Membership.Inside(t, p));
            return true;
        }
        public bool Down(int count, string tag) => false;
        public bool DoorOpen(V3 hinge) => false;
        public int Inside(TriggerPlan t, out int alive) { alive = At.Count; return At.Count(p => Membership.Inside(t, p)); }
    }

    // A house painted once and used three times, and a fourth hold with its own copy of the same shape.
    private static string Linked(string houseArea) =>
        "{\"schema\":\"missionlab-plan/3\",\"mission\":{\"places\":[{\"id\":1,\"name\":\"House\",\"pos\":[0,0,0],\"area\":" + houseArea + "}]," +
        "\"triggers\":[{\"id\":1,\"when\":\"zone\",\"pos\":[0,0,0],\"place\":1,\"repeat\":10}," +
        "{\"id\":2,\"when\":\"hold\",\"pos\":[0,0,0],\"place\":1,\"hold\":30}," +
        "{\"id\":3,\"when\":\"hold\",\"pos\":[1,0,1],\"place\":1,\"hold\":30}," +
        "{\"id\":4,\"when\":\"hold\",\"pos\":[0,0,0],\"area\":" + Square + ",\"hold\":30}]," +
        "\"spawns\":[{\"pos\":[30,0,30]}]}}";

    public static void Run(Action<string, bool, string> check)
    {
        string root = Root();
        check("the fixtures are found", root != null, AppContext.BaseDirectory);
        if (root == null) return;
        string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests", "fixtures", name));
        string v27 = Fixture("all_fields_v27.json");

        {   // Both readers resolve a link into the place's one shape.
            var found = ScenarioFile.Read(v27, out MissionPlan strict);
            check("the strict reader reads all_fields_v27.json without a finding", strict != null && !found.Any(f => f.Level == Finding.Error), Codes(found));
            var lenient = MissionPlan.FromJson(v27);
            foreach (MissionPlan p in new[] { strict, lenient })
            {
                if (p == null) continue;
                PlacePlan house = p.Places.First(q => q.Id == 1);
                var linked = p.Triggers.Where(t => t.Place == 1).ToList();
                check("three triggers link the house", linked.Count == 3 && linked.All(t => t.Linked), string.Join(",", linked.Select(t => t.Id)));
                check("each linked trigger has the house's shape", linked.All(t => t.Area != null && t.Area.Signature() == house.Area.Signature()), "");
                check("they share one shape: no trigger can drift from the others", linked.All(t => ReferenceEquals(t.Area, house.Area)), "");
                check("the copy keeps its own shape and no link", p.Triggers.First(t => t.Id == 4).Place == 0 && p.Triggers.First(t => t.Id == 4).Area != null, "");
                check("a point inside the house's upper floor is inside every linked trigger", linked.All(t => Membership.Inside(t, new V3(15, 4, 0))), "");
                check("a point in the house's courtyard hole is inside none of them", linked.All(t => !Membership.Inside(t, new V3(0, 1, 0))), "");
            }
        }
        {   // Save and reload keep the link, as plan/3; plan/3 only when there is a link.
            var p = MissionPlan.FromJson(v27);
            string once = p.ToJson(), twice = MissionPlan.FromJson(once).ToJson();
            check("a linked plan reads back to the same text", once == twice, once);
            check("a linked plan is written as the plan/3 envelope", once.StartsWith("{\"schema\":\"missionlab-plan/3\",\"mission\":{", StringComparison.Ordinal), once.Substring(0, Math.Min(80, once.Length)));
            using (JsonDocument doc = JsonDocument.Parse(once))
            {
                JsonElement t1 = doc.RootElement.GetProperty("mission").GetProperty("triggers")[0];
                check("a linked trigger writes the link and no area of its own", t1.GetProperty("place").GetInt32() == 1 && !t1.TryGetProperty("area", out _), t1.GetRawText());
            }
            check("the written plan/3 passes the strict reader", !ScenarioFile.Blocks(ScenarioFile.Read(once, out _)), Codes(ScenarioFile.Read(once, out _)));
            check("v0.23's reader refuses plan/3 (it finds no root spawns)", OldReaderThrows(once), "");
            string Format(string text) => JsonDocument.Parse(text).RootElement.GetProperty("schema").GetString();
            check("a plan whose only shapes are copies stays plan/2", Format(MissionPlan.FromJson(Fixture("all_fields_v2.json")).ToJson()) == MissionPlan.Schema2, "");
            check("a plan without shapes stays plan/1", Format(MissionPlan.FromJson(Fixture("all_fields.json")).ToJson()) == MissionPlan.Schema, "");
            var unlinked = MissionPlan.FromJson(v27);
            foreach (TriggerPlan t in unlinked.Triggers) t.Place = 0;
            unlinked.Goals.Clear(); unlinked.ExtractionGoals = false;      // goals alone keep plan/3 (ML-046)
            check("dropping every link drops the format to plan/2", Format(unlinked.ToJson()) == MissionPlan.Schema2, "");
            // Every file that existed before reads back to the text it was written as; it is plan/3 exactly when it links a
            // place or has goals (clean_house_objectives.json, ML-046) and no zone or hold waits for a trigger (that is plan/4, ML-055).
            var olds = new List<string> { "all_fields.json", "all_fields_v2.json", "all_fields_v26.json", "legacy_minimal.json", "extraction_chance_defect.json" }.Select(n => Path.Combine(root, "tests", "fixtures", n)).ToList();
            olds.AddRange(Directory.GetFiles(Path.Combine(root, "examples"), "*.json"));
            foreach (string file in olds)
            {
                MissionPlan plan = MissionPlan.FromJson(File.ReadAllText(file));
                string first = plan.ToJson(), second = MissionPlan.FromJson(first).ToJson();
                bool four = plan.Triggers.Any(t => t.Gated);
                bool needs3 = !four && (plan.Goals.Count > 0 || plan.ExtractionGoals || plan.Triggers.Any(t => t.Place != 0));
                check(Path.GetFileName(file) + " is written the same way twice, as plan/4 exactly when a zone or hold waits, else as plan/3 exactly when it has place links or goals",
                      first == second && (Format(first) == MissionPlan.Schema3) == needs3 && (Format(first) == MissionPlan.Schema4) == four, first.Substring(0, Math.Min(60, first.Length)));
            }
        }
        {   // Renaming a place keeps its links: they name its id.
            var p = MissionPlan.FromJson(v27);
            p.Places.First(q => q.Id == 1).Name = "Farmhouse";
            var back = MissionPlan.FromJson(p.ToJson());
            check("after a rename the three links still resolve to the renamed place", back.Triggers.Count(t => t.Place == 1 && t.Area != null) == 3 && back.Places.First(q => q.Id == 1).Name == "Farmhouse", "");
            check("and a player in the house is still in them", back.Triggers.Where(t => t.Place == 1).All(t => Membership.Inside(t, new V3(-5, 0, -5))), "");
        }
        {   // A dangling link: both readers decide alike. The strict one refuses it at the trigger's path.
            var found = ScenarioFile.Read(Fixture("invalid/dangling_place_link.json"), out MissionPlan none);
            check("a link to a place that is not there is refused as missing-ref at the trigger", Codes(found) == "missing-ref triggers[0].place" && none == null, Codes(found));
            var lenient = MissionPlan.FromJson(Fixture("invalid/dangling_place_link.json"));
            check("the lenient reader drops the dangling link and keeps the circle", lenient.Triggers[0].Place == 0 && lenient.Triggers[0].Area == null, "");
            check("a link from a plan/2 file is refused: plan/2 readers would read a circle", Codes(ScenarioFile.Read(Fixture("invalid/place_in_plan2.json"), out _)) == "bad-area triggers[0].place", "");
            var future = ScenarioFile.Read("{\"schema\":\"missionlab-plan/5\",\"mission\":{\"spawns\":[{\"pos\":[0,0,0]}]}}", out _);
            check("a format newer than plan/4 is still refused as future-version", Codes(future) == "future-version schema", Codes(future));
        }
        {   // The flattened export: no links, plan/2, and the exact effective shape for every trigger.
            var original = MissionPlan.FromJson(v27);
            var flat = MissionPlan.FromJson(v27); flat.Flatten();
            flat.Goals.Clear(); flat.ExtractionGoals = false;              // goals alone keep plan/3 (ML-046); Ml046Tests covers that
            string text = flat.ToJson();
            check("the flattened copy is plan/2 and names no place link", text.StartsWith("{\"schema\":\"missionlab-plan/2\",", StringComparison.Ordinal) && !text.Contains("\"place\":"), text.Substring(0, Math.Min(80, text.Length)));
            check("the flattened copy passes the strict reader", !ScenarioFile.Blocks(ScenarioFile.Read(text, out _)), Codes(ScenarioFile.Read(text, out _)));
            var back = MissionPlan.FromJson(text);
            int probes = 0, differ = 0;
            for (float x = -15f; x <= 60f; x += 1.25f)
                for (float z = -15f; z <= 60f; z += 1.25f)
                    foreach (float y in new[] { -0.5f, 0.5f, 2.5f, 4f, 5.5f, 7f })
                        for (int i = 0; i < original.Triggers.Count; i++)
                        {
                            var p = new V3(x, y, z);
                            probes++;
                            if (Membership.Inside(original.Triggers[i], p) != Membership.Inside(back.Triggers[i], p)) differ++;
                        }
            check("over a grid of probe points every trigger holds the same points flattened as linked", differ == 0 && probes > 100000, differ + " of " + probes + " differ");
            check("flattened copies are independent: each trigger has its own shape", back.Triggers.Where(t => t.Area != null).Select(t => t.Area).Distinct().Count() == back.Triggers.Count(t => t.Area != null), "");
            var mine = MissionPlan.FromJson(v27); mine.Flatten();
            mine.Triggers[0].Area.Parts[0].Bottom = 50f;
            check("changing one flattened copy changes no other trigger and not the place", mine.Triggers[1].Area.Parts[0].Bottom != 50f && mine.Places[0].Area.Parts[0].Bottom != 50f, "");
        }
        {   // A Game Master save keeps the links only where the shape is still the place's.
            MissionPlan Authored() => MissionPlan.FromJson(Linked(Square));
            var kept = MissionPlan.FromJson(Linked(Square));
            MissionPlan.CarryAuthored(Authored(), kept);
            check("unchanged linked triggers stay linked when saved", kept.Triggers.Count(t => t.Place == 1) == 3, "");
            var moved = MissionPlan.FromJson(Linked(Bigger));     // the page edited the house live: every dependant has the new shape
            MissionPlan.CarryAuthored(Authored(), moved);
            check("a place edited live for all its dependants is saved with the new shape, links kept", moved.Triggers.Count(t => t.Place == 1) == 3 && moved.Places[0].Area.Signature() == MissionPlan.FromJson(Linked(Bigger)).Places[0].Area.Signature(), moved.ToJson());
            var split = MissionPlan.FromJson(Linked(Square));
            split.Triggers[1].Area = MissionPlan.FromJson(Linked(Bigger)).Places[0].Area;      // one dependant reshaped on its own
            MissionPlan.CarryAuthored(Authored(), split);
            check("a dependant reshaped on its own is saved as its own copy, the others stay linked", split.Triggers[1].Place == 0 && split.Triggers[1].Area != null && split.Triggers[0].Place == 1 && split.Triggers[2].Place == 1, split.ToJson());
            check("such a save is a valid scenario", !ScenarioFile.Blocks(ScenarioFile.Read(split.ToJson(), out _)), Codes(ScenarioFile.Read(split.ToJson(), out _)));
            var gone = MissionPlan.FromJson(Linked(Square));
            var noPlaces = Authored(); noPlaces.Places.Clear();
            MissionPlan.CarryAuthored(noPlaces, gone);
            check("links to a place the authored file lacks are saved as copies, never dangling", gone.Triggers.All(t => t.Place == 0) && !ScenarioFile.Blocks(ScenarioFile.Read(gone.ToJson(), out _)), Codes(ScenarioFile.Read(gone.ToJson(), out _)));
        }
        {   // The live path refuses a link whose place is missing or broken, and the trigger keeps what it had.
            var was = MissionPlan.FromJson(Linked(Square)).Triggers;
            string triggers = MissionPlan.TriggersJson(was);
            Dictionary<int, string> Refused(string places)
            {
                using (JsonDocument t = JsonDocument.Parse(triggers)) using (JsonDocument p = JsonDocument.Parse(places)) return AreaEdits.Refused(t.RootElement, p.RootElement);
            }
            check("the live triggers carry the links", triggers.Contains("\"place\":1"), triggers);
            check("a good place refuses nothing", Refused("[{\"id\":1,\"name\":\"House\",\"pos\":[0,0,0],\"area\":" + Bigger + "}]").Count == 0, "");
            var none = Refused("[]");
            check("no place: each linked trigger is refused, the copy is not", none.Keys.OrderBy(k => k).SequenceEqual(new[] { 1, 2, 3 }) && none[1].StartsWith("triggers[0].place", StringComparison.Ordinal), string.Join(" | ", none.Select(k => k.Key + ": " + k.Value)));
            var crossed = Refused("[{\"id\":1,\"name\":\"House\",\"pos\":[0,0,0],\"area\":{\"parts\":[{\"floor\":[-1,3],\"outline\":[[0,0],[10,10],[10,0],[0,10]]}]}}]");
            check("a place whose shape breaks the rules refuses its dependants", crossed.Count == 3 && crossed[2].StartsWith("places[0].area", StringComparison.Ordinal), string.Join(" | ", crossed.Select(k => k.Key + ": " + k.Value)));
            var sent = MissionPlan.FromJson("{\"places\":[],\"triggers\":" + triggers + ",\"spawns\":[]}");
            AreaEdits.Apply(sent.Triggers, was, none);
            check("a refused link keeps the trigger's place, shape and link", sent.Triggers.Where(t => t.Id <= 3).All(t => t.Place == 1 && t.Area != null && t.Area.Signature() == was[0].Area.Signature()), "");
        }
        {   // A live edit of the place: one snapshot for all dependants, their holds start again, their firings stay.
            var plan = MissionPlan.FromJson(Linked(Square));
            var engine = new TriggerEngine(); engine.Reset(0f); engine.Sync(plan.Triggers);
            var world = new Walkers(); world.At.Add(new V3(-5, 1, -5));
            for (float t = 0; t <= 12f; t += 0.5f) engine.Step(t, world);
            int zoneFires = engine.Fires(1); float? zoneLast = engine.State(1).LastFiredAt;
            check("before the edit the zone has fired and all three holds are under way", zoneFires >= 1 && engine.State(2).InsideSince == 0f && engine.State(3).InsideSince == 0f && engine.State(4).InsideSince == 0f, zoneFires + " " + engine.State(2).InsideSince);
            string places = "[{\"id\":1,\"name\":\"House\",\"pos\":[0,0,0],\"area\":" + Bigger + "}]", triggers = MissionPlan.TriggersJson(plan.Triggers);
            Dictionary<int, string> refused;
            using (JsonDocument t = JsonDocument.Parse(triggers)) using (JsonDocument p = JsonDocument.Parse(places)) refused = AreaEdits.Refused(t.RootElement, p.RootElement);
            var sent = MissionPlan.FromJson("{\"places\":" + places + ",\"triggers\":" + triggers + ",\"spawns\":[]}");
            AreaEdits.Apply(sent.Triggers, plan.Triggers, refused);
            check("every dependant gets the one new shape", ReferenceEquals(sent.Triggers[0].Area, sent.Triggers[1].Area) && ReferenceEquals(sent.Triggers[1].Area, sent.Triggers[2].Area) && sent.Triggers[0].Area.Signature() != plan.Triggers[0].Area.Signature(), "");
            engine.Sync(sent.Triggers, 12f);
            check("both linked holds lose the hold under way", engine.State(2).InsideSince == null && engine.State(3).InsideSince == null, engine.State(2).InsideSince + " " + engine.State(3).InsideSince);
            check("the linked zone keeps its firings", engine.Fires(1) == zoneFires && engine.State(1).LastFiredAt == zoneLast, engine.Fires(1) + " was " + zoneFires);
            check("the hold with its own copy of the old shape goes on", engine.State(4).InsideSince == 0f, engine.State(4).InsideSince.ToString());
            var fired = new Dictionary<int, float>();
            for (float t = 12.5f; t <= 60f; t += 0.5f)
                foreach (TriggerEvent e in engine.Step(t, world))
                    if (e.Kind == TriggerEvent.Fire && e.Id >= 2 && !fired.ContainsKey(e.Id)) fired[e.Id] = t;
            check("the copy fires 30 s after it began, the linked holds 30 s after the edit", fired.TryGetValue(4, out float f4) && f4 == 30f && fired.TryGetValue(2, out float f2) && f2 == 42.5f && fired.TryGetValue(3, out float f3) && f3 == 42.5f,
                string.Join(", ", fired.Select(k => k.Key + "@" + k.Value)));
        }
        {   // A run record's resolved plan keeps the links and the places, and the repeat comparison still matches.
            var plan = MissionPlan.FromJson(v27);
            Variants.Resolve(plan, new Rng(27 ^ 0x5EED), new List<string>(), 1);
            string resolved = plan.ToJson();
            check("the resolved plan keeps the links and the place they name", resolved.Contains("\"place\":1") && resolved.Contains("\"House interior\""), "");
            check("a fresh write of the same plan matches the recorded text byte for byte", MissionPlan.FromJson(resolved).ToJson() == resolved, "");
        }
    }

    private static bool OldReaderThrows(string json) { try { Legacy023.MissionPlan.FromJson(json); return false; } catch (Exception) { return true; } }
}
