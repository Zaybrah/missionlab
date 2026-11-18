using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// Scenario packs (ML-026): the fixtures in tests/fixtures/packs judged as expected.json says (the editor checks the same
/// files), the writer giving the same text as the editor's, nothing but scenarios and modules going in, plain file names,
/// and how colliding names and ids are installed without ever replacing a local file.
/// </summary>
static class PackTests
{
    public static void Run(Action<string, bool, string> check)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "tests", "fixtures", "packs"))) root = Path.GetDirectoryName(root);
        check("the pack fixtures are found", root != null, "");
        if (root == null) return;
        string Lf(string p) => File.ReadAllText(Path.Combine(root, p)).Replace("\r\n", "\n");
        string dir = Path.Combine(root, "tests", "fixtures", "packs");
        const string Version = "0.26.0", S = "clean_house_quiet_raid.json", M = "two_guard_alarm.module.json";

        {   // Every fixture is judged exactly as expected.json says: the same codes at the same places as the editor gives.
            using (JsonDocument expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "expected.json"))))
            {
                int n = 0;
                foreach (JsonProperty fx in expected.RootElement.EnumerateObject())
                {
                    n++;
                    var want = fx.Value.EnumerateArray().Select(v => v.GetString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
                    List<Finding> found = Pack.Read(File.ReadAllText(Path.Combine(dir, fx.Name)), Version, out PackInfo pack);
                    var got = found.Where(f => f.Level == Finding.Error).Select(f => f.Code + " " + f.Path).OrderBy(s => s, StringComparer.Ordinal).ToList();
                    check(fx.Name + " gives " + (want.Count == 0 ? "no errors" : string.Join(", ", want)), got.SequenceEqual(want), string.Join(" | ", found));
                    check(fx.Name + (want.Count == 0 ? " is read" : " is refused whole"), (pack != null) == (want.Count == 0), "");
                }
                check("every fixture file is in expected.json", n == Directory.GetFiles(dir, "*.mlpack.json").Length, n.ToString());
            }
        }
        string raid = Lf("examples/clean_house_quiet_raid.json"), alarm = Lf("examples/modules/two_guard_alarm.module.json");
        var both = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(S, raid), new KeyValuePair<string, string>(M, alarm) };
        {   // The writer makes the manifest from the files, gives the editor's text line for line, and reads back.
            List<Finding> found = Pack.Write("Clean House quiet raid", "2026-11-18", "MissionLab 0.26.0", Version, both, out string text);
            check("a scenario and a module make a pack", text != "" && !ScenarioFile.Blocks(found), string.Join(" | ", found));
            check("the same text as the editor's packWrite (valid.mlpack.json)", text == Lf("tests/fixtures/packs/valid.mlpack.json"), text.Length.ToString());
            Pack.Read(text, Version, out PackInfo pack);
            PackEntry sc = pack.Files[0], md = pack.Files[1];
            check("the manifest lists both files with their kinds", pack.Files.Count == 2 && sc.Kind == PackEntry.Scenario && md.Kind == PackEntry.Module, "");
            check("each file is kept as its exact text", sc.Text == raid && md.Text == alarm, "");
            check("the hash is ScenarioFile.Hash of that text", sc.Hash == ScenarioFile.Hash(raid) && md.Hash == ScenarioFile.Hash(alarm), sc.Hash);
            check("it needs the scenario's map file, PvE and this version", pack.Maps.SequenceEqual(new[] { "suburbv2.bytes" }) && pack.Modes.SequenceEqual(new[] { "pve" }) && pack.Mod == Version, string.Join(",", pack.Maps));
            ScenarioFile.Read(raid, out MissionPlan plan);
            check("and every feature the scenario uses", pack.Capabilities.SequenceEqual(Capabilities.Of(plan)), string.Join(",", pack.Capabilities));
            check("every feature word Capabilities.Of gives is one this version knows", pack.Capabilities.All(c => Pack.Known.Contains(c)), "");
            check("the scenario's id travels in the manifest", sc.Id == "sc-cleanhouse-quiet", sc.Id);
        }
        {   // Only scenarios and modules: navigation data, geometry and replays are refused, also when the writer is handed them.
            var nav = new List<KeyValuePair<string, string>>(both) { new KeyValuePair<string, string>("suburbv2.bytes", "PK\u0003\u0004") };
            List<Finding> found = Pack.Write("x", "", "", Version, nav, out string text);
            check("a navmesh file is refused and no pack is made", text == "" && found.Any(f => f.Code == "not-allowed"), string.Join(" | ", found));
            var geo = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("suburbv2_mlgeo.json", "MLGEO2\n\0") };
            check("map geometry under a scenario name is refused by its content", Pack.Write("x", "", "", Version, geo, out _).Any(f => f.Code == "not-allowed"), "");
            var rep = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("run.json", "{\"schema\":\"missionlab-replay/1\",\"rows\":[]}") };
            check("a replay is refused", Pack.Write("x", "", "", Version, rep, out _).Any(f => f.Code == "not-allowed" && f.Path == "files.run.json"), "");
            var folder = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("scenarios/raid.json", raid) };
            check("a file name with a folder is refused", Pack.Write("x", "", "", Version, folder, out string t2).Any(f => f.Code == "bad-name") && t2 == "", "");
        }
        {   // File names: plain names only, of the right ending for the kind; versions compared by number.
            check("raid.json is a scenario name", Pack.FileName("raid.json", PackEntry.Scenario) == "raid.json", "");
            check("../raid.json is not", Pack.FileName("../raid.json", PackEntry.Scenario) != "../raid.json", "");
            check("a module name ends in .module.json", Pack.FileName("a.module.json", PackEntry.Module) == "a.module.json" && Pack.FileName("a.json", PackEntry.Module) == "a.module.json", "");
            check("a module name is not a scenario name", Pack.FileName("a.module.json", PackEntry.Scenario) != "a.module.json", "");
            check("0.27.0 is newer than 0.26.0", Pack.Newer("0.27.0", "0.26.0") && !Pack.Newer("0.26.0", "0.26.0") && !Pack.Newer("0.26", "0.26.0") && !Pack.Newer("next", "0.26.0"), "");
        }
        {   // A newer MissionLab's pack is read by this version's rules, with a note saying so.
            Pack.Write("x", "", "", "0.30.0", both, out string newer);
            List<Finding> found = Pack.Read(newer, Version, out PackInfo pack);
            check("a pack from a newer version is read with a note", pack != null && found.Any(f => f.Code == "newer-mod" && f.Level == Finding.Note), string.Join(" | ", found));
        }

        // ---- installing: what goes where, never over a local file
        Pack.Write("x", "", "", Version, both, out string packText);
        Pack.Read(packText, Version, out PackInfo p);
        PackLocal Local(string name, string id, int rev, string text) => new PackLocal { Name = name, Id = id, Revision = rev, Hash = ScenarioFile.Hash(text) };
        List<PackStep> Plan(Dictionary<string, string> choices, List<PackLocal> sc, List<PackLocal> md) => Pack.Plan(p, choices, sc, md);
        bool NeverOver(List<PackStep> steps, List<PackLocal> sc, List<PackLocal> md) =>
            steps.All(s => s.File == "" || s.Action == PackStep.Same || !(s.Kind == PackEntry.Module ? md : sc).Any(l => string.Equals(l.Name, s.File, StringComparison.OrdinalIgnoreCase)));
        string Id(string text) { ScenarioFile.Read(text, out MissionPlan plan); return plan == null ? "(refused)" : plan.Id + " r" + plan.Revision; }
        {   // Nothing there: each file under its own name, byte for byte.
            List<PackStep> steps = Plan(null, new List<PackLocal>(), new List<PackLocal>());
            check("nothing local: both install under their own names", steps.Count == 2 && steps.All(s => s.Action == PackStep.Install && s.File == s.Name), string.Join(",", steps.Select(s => s.Action + ":" + s.File)));
            check("and as the author's exact text", steps[0].Text == raid && steps[1].Text == alarm && steps[0].Hash == ScenarioFile.Hash(raid), "");
        }
        {   // The same text already there, by name or by id: nothing to do.
            var sc = new List<PackLocal> { Local("renamed.json", "sc-cleanhouse-quiet", 0, raid) };
            var md = new List<PackLocal> { Local(M, "", 0, alarm) };
            List<PackStep> steps = Plan(null, sc, md);
            check("the same scenario under another name is already there", steps[0].Action == PackStep.Same && steps[0].File == "renamed.json", steps[0].Action + " " + steps[0].File);
            check("the same module is already there", steps[1].Action == PackStep.Same, steps[1].Action);
        }
        {   // Name and id taken by other work: a copy gets a new name and a new id; it never replaces the local file.
            var sc = new List<PackLocal> { Local(S, "sc-cleanhouse-quiet", 2, "{\"other\":1}") };
            var md = new List<PackLocal> { Local(M, "", 0, "{\"other\":2}"), Local("two_guard_alarm-copy.module.json", "", 0, "{\"other\":3}") };
            List<PackStep> steps = Plan(new Dictionary<string, string>(), sc, md);
            check("a colliding scenario is copied under a new name", steps[0].Action == PackStep.Copy && steps[0].File == "clean_house_quiet_raid-copy.json", steps[0].File);
            check("with a new id, so the two stay apart", steps[0].Id == "sc-cleanhouse-quiet-copy" && Id(steps[0].Text) == "sc-cleanhouse-quiet-copy r0", Id(steps[0].Text));
            check("a colliding module is copied under the next free name", steps[1].Action == PackStep.Copy && steps[1].File == "two_guard_alarm-copy2.module.json" && steps[1].Text == alarm, steps[1].File);
            check("nothing is written over", NeverOver(steps, sc, md), "");
        }
        {   // A new revision: same id, the next revision after both, a new file name.
            var sc = new List<PackLocal> { Local("my_raid.json", "sc-cleanhouse-quiet", 3, "{\"mine\":1}"), Local(S, "", 0, "{\"mine\":2}") };
            List<PackStep> steps = Plan(new Dictionary<string, string> { [S] = "revision", [M] = "revision" }, sc, new List<PackLocal> { Local(M, "", 0, "{}") });
            check("installed as revision 4 of the same id", steps[0].Action == PackStep.NewRevision && steps[0].Revision == 4 && Id(steps[0].Text) == "sc-cleanhouse-quiet r4", Id(steps[0].Text));
            check("under a new file name", steps[0].File == "clean_house_quiet_raid-r4.json", steps[0].File);
            check("a module has no revisions: it is copied", steps[1].Action == PackStep.Copy && steps[1].File == "two_guard_alarm-copy.module.json", steps[1].Action + " " + steps[1].File);
            check("nothing is written over", NeverOver(steps, sc, new List<PackLocal> { Local(M, "", 0, "{}") }), "");
        }
        {   // Only the id is taken: a copy keeps the name and takes a new id; "revision" with only the name taken is a copy.
            var idOnly = new List<PackLocal> { Local("mine.json", "sc-cleanhouse-quiet", 1, "{\"mine\":1}") };
            PackStep a = Plan(null, idOnly, new List<PackLocal>())[0];
            check("an id clash alone: same file name, new id", a.Action == PackStep.Copy && a.File == S && a.Id == "sc-cleanhouse-quiet-copy", a.File + " " + a.Id);
            var nameOnly = new List<PackLocal> { Local(S, "sc-other", 1, "{\"mine\":1}") };
            PackStep b = Plan(new Dictionary<string, string> { [S] = "revision" }, nameOnly, new List<PackLocal>())[0];
            check("a name clash alone asked as revision: a copy with its own id and text", b.Action == PackStep.Copy && b.File == "clean_house_quiet_raid-copy.json" && b.Id == "sc-cleanhouse-quiet" && b.Text == raid, b.Action + " " + b.File);
        }
        {   // Skip, and an unknown choice, write nothing.
            var sc = new List<PackLocal> { Local(S, "", 0, "{}") };
            List<PackStep> steps = Plan(new Dictionary<string, string> { [S] = "skip" }, sc, new List<PackLocal>());
            check("skip writes nothing", steps[0].Action == PackStep.Skip && steps[0].File == "" && steps[0].Text == "", steps[0].Action);
            check("a file without a clash is installed whatever was chosen", steps[1].Action == PackStep.Install, steps[1].Action);
            PackStep odd = Plan(new Dictionary<string, string> { [S] = "replace" }, sc, new List<PackLocal>())[0];
            check("\"replace\" is not a choice: nothing written", odd.Action == PackStep.Skip && odd.Why.Contains("replace"), odd.Why);
        }
        {   // The module shape check now lives in Core, with the plugin's wording.
            check("both example modules pass the shape check", ModuleFile.Check(alarm, out string title, out _, out _, out _, out _) == "" && title == "Two-guard alarm response"
                && ModuleFile.Check(Lf("examples/modules/hold_zone.module.json"), out _, out _, out _, out _, out _) == "", title);
            check("a module without an anchor is named so", ModuleFile.Check("{\"schema\":\"missionlab-module/1\",\"name\":\"a\",\"spawns\":[],\"triggers\":[],\"bindings\":[]}", out _, out _, out _, out _, out _)
                == "\"anchor\" is {\"pos\": [x, y, z], \"yaw\": degrees}", "");
        }
    }
}
