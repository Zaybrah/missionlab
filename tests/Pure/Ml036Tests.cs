using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// ML-036: one capability list (docs/capabilities.json) with build, map, mode and who saw it. A capability is confirmed only
/// on the build its evidence names, solo or host's-log evidence never reads as a client's, a failing capability shows as
/// failing rather than untested, and what a scenario needs of the map and mode is named before it is chosen.
/// </summary>
static class Ml036Tests
{
    private static string Registry(string rows) => "{\"schema\":\"missionlab-capabilities/1\",\"intro\":[],\"capabilities\":[" + rows + "]}";

    private static string Row(string id, string status, string uses, string evidence) =>
        "{\"id\":\"" + id + "\",\"label\":\"" + id + "\",\"status\":\"" + status + "\",\"where\":\"w\",\"summary\":\"s\",\"uses\":[" + uses + "],\"evidence\":[" + evidence + "]}";

    private static string Seen(string build, string scope, string result) =>
        "{\"build\":\"" + build + "\",\"date\":\"2026-11-18\",\"map\":\"Clean House\",\"mode\":\"PvE raid\",\"scope\":\"" + scope + "\",\"result\":\"" + result + "\",\"note\":\"n\"}";

    private const string Recon = "{\"schema\":\"missionlab-recon/1\",\"scene\":\"Suburb_Night\",\"navGraphs\":{\"cachedStartupFile\":\"suburbv2\"},"
        + "\"raid\":{\"objectiveSets\":[{\"name\":\"Cell Phone\",\"objectives\":[{\"name\":\"Cell Phone_1\",\"displayName\":\"Cell Phone\"},null]}],\"hvtSpawnPoints\":[]}}";

    private static string Intel(string item) =>
        "{\"schema\":\"missionlab-plan/1\",\"name\":\"Phone job\",\"navFile\":\"suburbv2.bytes\",\"spawns\":[{\"pos\":[0,0,0]}],"
        + "\"triggers\":[{\"id\":1,\"when\":\"intel\",\"item\":\"" + item + "\",\"message\":\"Got it.\"}]}";

    private static JsonElement Written(CatalogueEntry e)
    {
        var sb = new StringBuilder();
        CatalogueInfo.Write(e, sb, "tested", "Clean House");
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }

    private static JsonElement Capability(JsonElement entry, string name)
    {
        foreach (JsonElement c in entry.GetProperty("capabilities").EnumerateArray()) if (c.GetProperty("name").GetString() == name) return c;
        return default;
    }

    public static void Run(Action<string, bool, string> check)
    {
        CapabilityRegistry shipped = CapabilityRegistry.Embedded();

        {   // The list the plugin carries reads without a problem, so no row has lost evidence or fallen back to untested on the way.
            check("docs/capabilities.json is built in and reads without problems", shipped.Problems.Count == 0 && shipped.Records.Count >= 30, string.Join("; ", shipped.Problems));
            bool named = shipped.Records.All(r => r.Evidence.All(e => e.Build != "" && e.Date != "" && e.Map != "" && e.Mode != "" && CapabilityRegistry.Scopes.Contains(e.Scope)));
            check("every evidence record names its build, date, map, mode and scope", named, "");
        }

        {   // Every feature word a scenario can use has exactly one row, so no capability is silently shown as unknown.
            var missing = Pack.Known.Where(w => shipped.For(w) == null).ToList();
            check("every capability word belongs to a row of the list", missing.Count == 0, string.Join(",", missing));
        }

        {   // A scenario with fresh hunters shows the recorded failure, with its build, not "untested"; it stays playable as an experiment.
            string text = "{\"schema\":\"missionlab-plan/1\",\"name\":\"Hunt\",\"navFile\":\"suburbv2.bytes\",\"spawns\":[{\"pos\":[0,0,0]},{\"pos\":[5,0,5],\"trigger\":1}],"
                        + "\"triggers\":[{\"id\":1,\"when\":\"time\",\"delay\":30,\"hunt\":true}]}";
            var e = CatalogueInfo.Build("hunt.json", text, null);
            CatalogueInfo.Check(e, shipped, "0.27.0", null);
            JsonElement hunt = Capability(Written(e), "hunt");
            check("the hunt scenario is readable", e.Readable && e.Capabilities.Contains("hunt"), e.Why);
            check("hunters are shown as failing, not untested", hunt.ValueKind == JsonValueKind.Object && hunt.GetProperty("shown").GetString() == "failing"
                  && hunt.GetProperty("status").GetString() == "failing" && hunt.GetProperty("build").GetString() == "0.26.1", hunt.ValueKind == JsonValueKind.Object ? hunt.GetRawText() : "no hunt");
            check("the failure is a note that labels the choice an experiment, not a block", e.Usable && e.Needs.Any(f => f.Code == "capability-failing" && f.Level == Finding.Note && f.Text.Contains("experiment")),
                  string.Join(" | ", e.Needs.Select(f => f.Level + " " + f.Code + " " + f.Text)));
        }

        {   // Solo and host's-log evidence never reads as a client's; only co-op-client evidence does.
            var reg = CapabilityRegistry.Read(Registry(Row("solo", "supported", "\"a\"", Seen("0.26.0", "solo", "worked")) + ","
                + Row("log", "supported", "\"b\"", Seen("0.26.0", "two-player-host-log", "worked")) + ","
                + Row("client", "supported", "\"c\"", Seen("0.26.0", "co-op-client", "worked"))));
            CapabilityState a = Capabilities.Judge(reg, "a", "0.26.0"), b = Capabilities.Judge(reg, "b", "0.26.0"), c = Capabilities.Judge(reg, "c", "0.26.0");
            check("solo evidence is not a client's", !a.Client && a.Words.Contains("no client has seen it") && !a.Words.Contains("seen by a client"), a.Words);
            check("two players by the host's log is not a client's either", !b.Client && b.Words.Contains("host's log") && !b.Words.Contains("seen by a client"), b.Words);
            check("evidence from a client's screen says so", c.Client && c.Words.Contains("seen by a client") && !c.Words.Contains("no client"), c.Words);
        }

        {   // Evidence confirms only the build it names: a newer build is "seen on X, not yet on Y", and an unknown build confirms nothing.
            var reg = CapabilityRegistry.Read(Registry(Row("doors", "supported", "\"doors\"", Seen("0.9.0", "solo", "worked") + "," + Seen("0.10.0", "solo", "worked")) + ","
                + Row("old", "supported", "\"old\"", "{\"build\":\"unrecorded\",\"date\":\"unrecorded\",\"map\":\"Suburb\",\"mode\":\"PvE raid\",\"scope\":\"solo\",\"result\":\"worked\",\"note\":\"\"}")));
            CapabilityState same = Capabilities.Judge(reg, "doors", "0.10"), later = Capabilities.Judge(reg, "doors", "0.27.0"), offline = Capabilities.Judge(reg, "doors", "");
            check("the newest evidence is found by version number, not by text", same.Build == "0.10.0", same.Build);
            check("the build the evidence names is confirmed (0.10 is 0.10.0)", same.Confirmed && same.Shown == Capabilities.ShownSupported, same.Shown);
            check("a newer build is told it has not been seen yet, and is not confirmed", !later.Confirmed && later.Shown == Capabilities.ShownEarlier && later.Words.Contains("seen working on 0.10.0") && later.Words.Contains("not yet on 0.27.0"), later.Words);
            check("with the running build unknown nothing is confirmed", !offline.Confirmed && offline.Shown == Capabilities.ShownEarlier, offline.Shown);
            CapabilityState unrec = Capabilities.Judge(reg, "old", "0.27.0");
            check("evidence from an unrecorded build never confirms", !unrec.Confirmed && unrec.Words.Contains("an unrecorded build"), unrec.Words);
        }

        {   // The shipped list confirms nothing on a build no evidence names yet.
            var confirmed = Pack.Known.Where(w => Capabilities.Judge(shipped, w, "0.27.0").Confirmed).ToList();
            check("a build without evidence of its own inherits no confirmation", confirmed.Count == 0, string.Join(",", confirmed));
        }

        {   // A broken list can only claim less: bad evidence is left out and an unknown status reads as untested.
            var reg = CapabilityRegistry.Read(Registry(Row("x", "supported", "\"x\"", "{\"build\":\"latest\",\"date\":\"2026-11-18\",\"map\":\"m\",\"mode\":\"m\",\"scope\":\"solo\",\"result\":\"worked\"}") + ","
                + Row("y", "great", "\"y\"", Seen("0.26.0", "solo", "worked")) + ","
                + Row("z", "supported", "\"x\"", Seen("0.26.0", "nobody", "worked"))));
            CapabilityState x = Capabilities.Judge(reg, "x", "0.26.0"), y = Capabilities.Judge(reg, "y", "0.26.0");
            check("evidence naming no proper build is left out, so nothing is confirmed", !x.Confirmed && x.Shown == Capabilities.ShownUntested && reg.For("x").Evidence.Count == 0, x.Shown);
            check("an unknown status reads as untested", !y.Confirmed && y.Status == CapabilityRegistry.Untested, y.Status);
            check("each problem is listed: bad build, unknown status, a word used twice, an unknown scope", reg.Problems.Count == 4, string.Join("; ", reg.Problems));
            check("a file that is not the list is refused without throwing", CapabilityRegistry.Read("{").Records.Count == 0 && CapabilityRegistry.Read("[]").Problems.Count == 1
                  && CapabilityRegistry.Read("{\"schema\":\"other\"}").Problems.Count == 1, "");
            check("a word in no row is unknown, never confirmed", Capabilities.Judge(reg, "nothing", "0.26.0").Shown == Capabilities.ShownUnknown && !Capabilities.Judge(null, "x", "0.26.0").Confirmed, "");
        }

        {   // Intel items: a kind the map's full list lacks blocks; one missing from a single mission's items only warns.
            MissionPlan sim = MissionPlan.FromJson(Intel("Sim cards")), phone = MissionPlan.FromJson(Intel("cell phone"));
            var full = new MapFacts { Items = new List<string> { "Cell Phone_1" }, ItemsComplete = true, Source = "recon/x.json" };
            var once = new MapFacts { Items = new List<string> { "Cell Phone_1" }, ItemsComplete = false };
            Finding miss = Capabilities.Needs(sim, full, null, "").Find(f => f.Code == "missing-item");
            check("an intel trigger for an item the map does not have is a known incompatibility", miss != null && miss.Level == Finding.Error && miss.Path == "triggers[0].item" && miss.Text.Contains("sim cards"), miss?.Text);
            check("an item missing from one mission's items is a warning", Capabilities.Needs(sim, once, null, "").Exists(f => f.Code == "missing-item" && f.Level == Finding.Note), "");
            check("an item the map has, named by another copy or case, needs nothing", !Capabilities.Needs(phone, full, null, "").Exists(f => f.Code == "missing-item"), "");
            MissionPlan any = MissionPlan.FromJson(Intel(""));
            check("waiting for any intel on a map with none is named", Capabilities.Needs(any, new MapFacts { Items = new List<string>(), ItemsComplete = true }, null, "").Exists(f => f.Code == "missing-item" && f.Level == Finding.Error), "");
            MissionPlan moved = MissionPlan.FromJson("{\"spawns\":[{\"pos\":[0,0,0]}],\"objectives\":[{\"name\":\"Sim cards_2\",\"pos\":[1,0,1]}]}");
            check("moving an item the map does not have is named", Capabilities.Needs(moved, full, null, "").Exists(f => f.Code == "missing-item" && f.Path == "objectives[0].name"), "");
            check("nothing known of the map judges nothing", Capabilities.Needs(sim, null, null, "").Count == 0 && Capabilities.Needs(sim, new MapFacts(), null, "").Count == 0, "");
        }

        {   // Mode, scene and map file: known mismatches block; unknown ones say nothing.
            MissionPlan scened = MissionPlan.FromJson("{\"scene\":\"Suburb_Night\",\"spawns\":[{\"pos\":[0,0,0]}]}"), anyScene = MissionPlan.FromJson("{\"scene\":\"*\",\"spawns\":[{\"pos\":[0,0,0]}]}");
            MissionPlan mapped = MissionPlan.FromJson(Intel("cell phone"));
            check("a mode other than a PvE raid blocks", Capabilities.Needs(mapped, new MapFacts { Mode = "PVP_TDM", Pve = false }, null, "").Exists(f => f.Code == "wrong-mode" && f.Level == Finding.Error), "");
            check("a PvE raid passes", !Capabilities.Needs(mapped, new MapFacts { Mode = "PVE_RAID", Pve = true }, null, "").Exists(f => f.Code == "wrong-mode"), "");
            check("another scene blocks", Capabilities.Needs(scened, new MapFacts { Scene = "Georgia" }, null, "").Exists(f => f.Code == "wrong-scene" && f.Level == Finding.Error), "");
            check("the same scene in other case and '*' pass", Capabilities.Needs(scened, new MapFacts { Scene = "suburb_night" }, null, "").Count == 0 && Capabilities.Needs(anyScene, new MapFacts { Scene = "Georgia" }, null, "").Count == 0, "");
            check("another map file blocks", Capabilities.Needs(mapped, new MapFacts { MapFile = "depotraid1.bytes" }, null, "").Exists(f => f.Code == "wrong-map"), "");
            MissionPlan target = MissionPlan.FromJson("{\"spawns\":[{\"pos\":[0,0,0],\"hvt\":true}]}");
            Finding nt = Capabilities.Needs(target, new MapFacts { TargetKnown = true, HasTarget = false }, null, "").Find(f => f.Code == "no-target");
            check("a target person on a mission without one is a warning", nt != null && nt.Level == Finding.Note, nt?.Text);
        }

        {   // A recon file of the scenario's map supplies its items; the catalogue then names the missing item before the mission is chosen.
            MapFacts recon = MapFacts.FromRecon("recon/Suburb_Night.late.json", Recon);
            check("a recon file gives the map's full item list, even when copies are gone", recon != null && recon.ItemsComplete && recon.Items.Contains("cell phone") && recon.TargetKnown && !recon.HasTarget,
                  recon == null ? "null" : string.Join(",", recon.Items));
            check("something that is not a recon file gives no facts", MapFacts.FromRecon("x", "{\"schema\":\"missionlab-run/1\"}") == null && MapFacts.FromRecon("x", "{") == null, "");
            var maps = new List<MapFacts> { recon };
            var bad = CatalogueInfo.Build("sim.json", Intel("Sim cards"), null);
            CatalogueInfo.Check(bad, shipped, "0.27.0", maps);
            JsonElement j = Written(bad);
            bool listed = j.GetProperty("needs").EnumerateArray().Any(f => f.GetProperty("code").GetString() == "missing-item" && f.GetProperty("level").GetString() == "error");
            check("a scenario needing an item its map lacks is not offered, and says why", !bad.Usable && bad.Why.Contains("sim cards") && listed && !j.GetProperty("usable").GetBoolean(), bad.Why);
            var good = CatalogueInfo.Build("phone.json", Intel("Cell Phone"), null);
            CatalogueInfo.Check(good, shipped, "0.27.0", maps);
            check("the same scenario waiting for an item the map has stays usable", good.Usable && !good.Needs.Exists(f => f.Level == Finding.Error), string.Join(" | ", good.Needs.Select(f => f.Text)));
            var elsewhere = CatalogueInfo.Build("other.json", Intel("Sim cards").Replace("suburbv2.bytes", "georgia1.bytes"), null);
            CatalogueInfo.Check(elsewhere, shipped, "0.27.0", maps);
            check("a recon file of another map is not used to judge it", elsewhere.Usable && !elsewhere.Needs.Exists(f => f.Code == "missing-item"), "");
        }
    }
}
