using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>One file in a pack: a scenario or an encounter module, with what the manifest says about it.</summary>
    public sealed class PackEntry
    {
        public const string Scenario = "scenario", Module = "module";
        public string Name = "", Kind = "", Id = "", Hash = "", Text = "", NavFile = "", MapPrint = "";
        public int Bytes, Revision;
        public List<string> Capabilities = new List<string>();
    }

    /// <summary>A pack as read: its words, its files in manifest order and what it says it needs.</summary>
    public sealed class PackInfo
    {
        public string Name = "", Made = "", By = "", Mod = "";
        public List<PackEntry> Files = new List<PackEntry>();
        public List<string> Maps = new List<string>(), Modes = new List<string>(), Capabilities = new List<string>();
    }

    /// <summary>A file already in the scenarios or modules folder: its name, and for a scenario its id and revision.</summary>
    public sealed class PackLocal
    {
        public string Name = "", Id = "", Hash = "";
        public int Revision;
    }

    /// <summary>What installing one pack file does. Text is what is written under File; nothing is written for skip and same.</summary>
    public sealed class PackStep
    {
        public const string Install = "install", Copy = "copy", NewRevision = "revision", Skip = "skip", Same = "same";
        public string Name = "", Kind = "", Action = "", File = "", Id = "", Text = "", Hash = "", Why = "";
        public int Revision;
    }

    /// <summary>
    /// Scenario packs, missionlab-pack/1 (ML-026): one JSON file carrying scenarios and encounter modules from one player to
    /// another, with a manifest that lists every file and what it needs (map files, mode, MissionLab version, features).
    /// File texts are kept as JSON strings, so the manifest's hash is over the exact text and an installed file is the
    /// author's file byte for byte. Only scenario JSON and missionlab-module/1 go in: never navigation data, map geometry,
    /// replays or anything else from the game. Read checks a pack; Plan decides, without touching any file, how its files
    /// go into the local folders without ever replacing one. The editor has the same rules in 30a-packs.js.
    /// </summary>
    public static class Pack
    {
        public const string Schema = "missionlab-pack/1", Pve = "pve", Suffix = ".mlpack.json", ModuleSuffix = ".module.json";
        public const int MaxFiles = 64;
        public const int MaxScenarioBytes = 4 * 1024 * 1024, MaxModuleBytes = 1 << 20;   // as the catalogue and the modules folder take them

        /// <summary>Every feature word this version knows (Capabilities.Of). A pack needing another one was made by a newer MissionLab.</summary>
        public static readonly string[] Known = { "areas", "chance", "director", "door-trigger", "doors", "endings", "extraction-hold", "hunt", "hvt",
            "insertion", "intel-move", "messages", "options", "places", "power", "rally", "repeat", "respawn", "scatter", "sealed", "teams", "things",
            "vehicles", "weather" };

        // What the game or MissionLab makes from the game: none of it may travel in a pack.
        private static readonly string[] Forbidden = { ".bytes", ".binary", ".mlgeo", ".dll", ".zip", ".assets", ".bundle", ".exe" };
        private static readonly string[] ForbiddenSchemas = { "missionlab-replay/", "missionlab-recon/", "missionlab-movement/" };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly JsonDocumentOptions Loose = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>
        /// The file name a pack file must have: a scenario's as the catalogue saves it (letters, digits, '_' and '-', ".json"),
        /// a module's as the modules folder keeps it (".module.json"). "" when nothing usable is left. Never a folder.
        /// </summary>
        public static string FileName(string name, string kind)
        {
            string n = (name ?? "").Trim();
            if (kind == PackEntry.Module)
            {
                if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 5);
                if (n.EndsWith(".module", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 7);
                string safe = CatalogueInfo.SafeName(n);
                return safe == "" ? "" : safe.Substring(0, safe.Length - 5) + ModuleSuffix;
            }
            return CatalogueInfo.SafeName(n);
        }

        /// <summary>Whether version a is later than version b ("0.27.0" against "0.26.0"); false when either is not dotted numbers.</summary>
        public static bool Newer(string a, string b)
        {
            int[] x = Parts(a), y = Parts(b);
            if (x == null || y == null) return false;
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p > q;
            }
            return false;
        }

        private static int[] Parts(string v)
        {
            string[] s = (v ?? "").Split('.');
            var r = new int[s.Length];
            for (int i = 0; i < s.Length; i++) if (!int.TryParse(s[i], NumberStyles.None, Inv, out r[i])) return null;
            return r;
        }

        private static bool ForbiddenName(string name)
        {
            foreach (string end in Forbidden) if ((name ?? "").EndsWith(end, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Navigation data, map geometry, a replay or a recon file, judged by the text itself.</summary>
        private static bool ForbiddenText(string text)
        {
            string t = (text ?? "").TrimStart();
            if (t.StartsWith("PK", StringComparison.Ordinal) || t.StartsWith("MLGEO", StringComparison.Ordinal)) return true;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(t, Loose))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("schema", out JsonElement s) || s.ValueKind != JsonValueKind.String) return false;
                    foreach (string f in ForbiddenSchemas) if (s.GetString().StartsWith(f, StringComparison.Ordinal)) return true;
                    return false;
                }
            }
            catch (JsonException) { return false; }
        }

        /// <summary>
        /// Reads a pack strictly. pack is null when an error was found. Paths name the place: "manifest.files[0].hash",
        /// "files.raid.json" and, inside a scenario, "files.raid.json.triggers[2].after". version: this MissionLab's.
        /// </summary>
        public static List<Finding> Read(string text, string version, out PackInfo pack)
        {
            pack = null;
            var found = new List<Finding>();
            void Bad(string code, string path, string why) => found.Add(new Finding { Code = code, Path = path, Text = why });
            void Note(string code, string path, string why) => found.Add(new Finding { Level = Finding.Note, Code = code, Path = path, Text = why });

            JsonDocument doc;
            try { doc = JsonDocument.Parse(text ?? "", Loose); }
            catch (JsonException e) { Bad("not-json", "", "this is not a JSON file: " + e.Message); return found; }
            var p = new PackInfo();
            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { Bad("wrong-type", "", "a pack is a JSON object"); return found; }
                if (!root.TryGetProperty("schema", out JsonElement schema)) { Bad("missing-field", "schema", "a pack names its format: \"schema\": \"" + Schema + "\""); return found; }
                string sv = schema.ValueKind == JsonValueKind.String ? schema.GetString() : "";
                if (sv != Schema)
                {
                    if (sv.StartsWith("missionlab-pack/", StringComparison.Ordinal) && int.TryParse(sv.Substring(16), NumberStyles.None, Inv, out int v) && v > 1)
                        Bad("future-version", "schema", "this pack is " + sv + "; this version of MissionLab reads " + Schema + ". Update MissionLab to use it");
                    else Bad("unknown-format", "schema", "\"" + sv + "\" is not a MissionLab pack format");
                    return found;
                }
                p.Name = Str(root, "name", "name", true, "the pack needs a \"name\"", Bad);
                p.Made = Str(root, "made", "made", false, "", Bad);
                p.By = Str(root, "by", "by", false, "", Bad);

                if (!root.TryGetProperty("manifest", out JsonElement man) || man.ValueKind != JsonValueKind.Object) { Bad("missing-field", "manifest", "a pack needs a \"manifest\" object"); return found; }
                if (!root.TryGetProperty("files", out JsonElement texts) || texts.ValueKind != JsonValueKind.Object) { Bad("missing-field", "files", "a pack needs \"files\": an object of file texts"); return found; }
                if (!man.TryGetProperty("files", out JsonElement list) || list.ValueKind != JsonValueKind.Array) { Bad("missing-field", "manifest.files", "the manifest lists every file under \"files\""); return found; }
                if (list.GetArrayLength() == 0) Bad("missing-field", "manifest.files", "the pack holds no files");
                if (list.GetArrayLength() > MaxFiles) { Bad("out-of-range", "manifest.files", "a pack holds at most " + MaxFiles.ToString(Inv) + " files"); return found; }

                // What the pack says it needs.
                if (!man.TryGetProperty("requires", out JsonElement req) || req.ValueKind != JsonValueKind.Object) Bad("missing-field", "manifest.requires", "the manifest says what the pack needs under \"requires\"");
                else
                {
                    p.Maps = Words(req, "maps", "manifest.requires.maps", Bad);
                    for (int k = 0; k < p.Maps.Count; k++)
                        if (p.Maps[k] == "" || p.Maps[k].IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || p.Maps[k].Contains(".."))
                            Bad("bad-name", "manifest.requires.maps[" + k.ToString(Inv) + "]", "a map is named by its file name only, such as suburbv2.bytes");
                    p.Modes = Words(req, "modes", "manifest.requires.modes", Bad);
                    if (req.TryGetProperty("modes", out JsonElement _) && p.Modes.Count == 0) Bad("missing-field", "manifest.requires.modes", "the pack names its mode: [\"pve\"]");
                    for (int k = 0; k < p.Modes.Count; k++)
                        if (p.Modes[k] != Pve) Bad("unknown-value", "manifest.requires.modes[" + k.ToString(Inv) + "]", "\"" + p.Modes[k] + "\" is not a mode MissionLab plays; it runs only in PvE");
                    p.Mod = Str(req, "mod", "manifest.requires.mod", true, "the pack names the MissionLab version it was made with", Bad);
                    if (p.Mod != "" && Parts(p.Mod) == null) Bad("unknown-value", "manifest.requires.mod", "\"" + p.Mod + "\" is not a version number such as 0.26.0");
                    else if (p.Mod != "" && Newer(p.Mod, version)) Note("newer-mod", "manifest.requires.mod", "made with MissionLab " + p.Mod + "; this is " + version + ". Each file below is checked by this version's rules");
                    p.Capabilities = Words(req, "capabilities", "manifest.requires.capabilities", Bad);
                    for (int k = 0; k < p.Capabilities.Count; k++)
                        if (Array.IndexOf(Known, p.Capabilities[k]) < 0)
                            Bad("unsupported", "manifest.requires.capabilities[" + k.ToString(Inv) + "]", "\"" + p.Capabilities[k] + "\" is a feature this version of MissionLab does not have");
                }

                // Every listed file: a clean name, an allowed kind, its text, and the hash and size the manifest gives.
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ids = new Dictionary<string, string>(StringComparer.Ordinal);
                int i = 0;
                foreach (JsonElement f in list.EnumerateArray())
                {
                    string at = "manifest.files[" + (i++).ToString(Inv) + "]";
                    if (f.ValueKind != JsonValueKind.Object) { Bad("wrong-type", at, "a manifest entry is an object"); continue; }
                    var e = new PackEntry
                    {
                        Name = Str(f, "name", at + ".name", true, "every file needs its \"name\"", Bad),
                        Kind = Str(f, "kind", at + ".kind", true, "every file needs its \"kind\": scenario or module", Bad),
                    };
                    if (e.Name == "" || e.Kind == "") continue;
                    if (e.Kind != PackEntry.Scenario && e.Kind != PackEntry.Module) { Bad("not-allowed", at + ".kind", "\"" + e.Kind + "\" files cannot go in a pack: only scenarios and modules do, never navigation data, map geometry, replays or game files"); continue; }
                    if (ForbiddenName(e.Name)) { Bad("not-allowed", at + ".name", e.Name + " is not a scenario or a module: navigation data, map geometry, replays and game files never go in a pack"); continue; }
                    if (FileName(e.Name, e.Kind) != e.Name)
                    {
                        Bad("bad-name", at + ".name", "\"" + e.Name + "\" is not a plain file name: " + (e.Kind == PackEntry.Module ? "letters, digits, '_' or '-' and \".module.json\"" : "letters, digits, '_' or '-' and \".json\"") + ", no folders");
                        continue;
                    }
                    if (!names.Add(e.Name)) { Bad("duplicate-id", at + ".name", e.Name + " is listed twice"); continue; }
                    string file = "files." + e.Name;
                    if (!texts.TryGetProperty(e.Name, out JsonElement body)) { Bad("missing-ref", at + ".name", "the pack lists " + e.Name + " but does not hold its text"); continue; }
                    if (body.ValueKind != JsonValueKind.String) { Bad("wrong-type", file, "each file is held as its text, a JSON string"); continue; }
                    e.Text = body.GetString();
                    e.Bytes = Encoding.UTF8.GetByteCount(e.Text);
                    if (ForbiddenText(e.Text)) { Bad("not-allowed", file, "this is navigation data, map geometry, a replay or a recon file: none of those go in a pack"); continue; }
                    if (e.Bytes > (e.Kind == PackEntry.Module ? MaxModuleBytes : MaxScenarioBytes)) { Bad("out-of-range", file, "the file is larger than MissionLab takes (" + (e.Kind == PackEntry.Module ? "1 MB" : "4 MB") + ")"); continue; }

                    string hash = Str(f, "hash", at + ".hash", true, "every file needs its \"hash\"", Bad);
                    if (hash != "" && hash != ScenarioFile.Hash(e.Text)) Bad("hash-mismatch", at + ".hash", "the text of " + e.Name + " is not the text the manifest describes: it was changed after the pack was made");
                    e.Hash = ScenarioFile.Hash(e.Text);
                    if (!f.TryGetProperty("bytes", out JsonElement by) || by.ValueKind != JsonValueKind.Number || !by.TryGetInt32(out int n)) Bad("missing-field", at + ".bytes", "every file needs its size in \"bytes\"");
                    else if (n != e.Bytes) Bad("hash-mismatch", at + ".bytes", e.Name + " has " + e.Bytes.ToString(Inv) + " bytes; the manifest says " + n.ToString(Inv));

                    if (e.Kind == PackEntry.Module)
                    {
                        string why = ModuleFile.Check(e.Text, out _, out _, out _, out _, out _);
                        if (why != "") Bad("bad-module", file, "not a module MissionLab can use: " + why);
                        else p.Files.Add(e);
                        continue;
                    }
                    List<Finding> inner = ScenarioFile.Read(e.Text, out MissionPlan plan);
                    foreach (Finding x in inner) found.Add(new Finding { Level = x.Level, Code = x.Code, Path = x.Path == "" ? file : file + "." + x.Path, Text = x.Text });
                    if (plan == null) continue;
                    e.Id = plan.Id ?? ""; e.Revision = plan.Revision; e.NavFile = System.IO.Path.GetFileName((plan.NavFile ?? "").Trim()); e.MapPrint = plan.MapPrint ?? "";
                    e.Capabilities = MissionLab.Core.Capabilities.Of(plan);
                    if (plan.Spawns.Count == 0) Bad("no-enemies", file + ".spawns", "it has no enemies, so MissionLab would leave the game's own mission");
                    string said = f.TryGetProperty("id", out JsonElement mid) && mid.ValueKind == JsonValueKind.String ? mid.GetString() : null;
                    if (said != null && said != e.Id) Bad("id-mismatch", at + ".id", "the manifest says " + e.Name + " is " + said + "; the file says " + (e.Id == "" ? "nothing" : e.Id));
                    if (e.Id != "")
                    {
                        if (ids.TryGetValue(e.Id, out string first)) Bad("duplicate-id", file + ".id", e.Name + " has the same id as " + first + ": two scenarios in one pack are two different missions");
                        else ids[e.Id] = e.Name;
                    }
                    // The manifest names everything each scenario needs.
                    foreach (string c in e.Capabilities)
                        if (!p.Capabilities.Contains(c)) Bad("missing-ref", "manifest.requires.capabilities", e.Name + " uses " + c + ", which the manifest does not list");
                    if (e.NavFile != "" && !p.Maps.Contains(e.NavFile)) Bad("missing-ref", "manifest.requires.maps", e.Name + " is made for " + e.NavFile + ", which the manifest does not list");
                    p.Files.Add(e);
                }
                // A file the manifest does not list would arrive unannounced.
                foreach (JsonProperty t in texts.EnumerateObject())
                    if (!names.Contains(t.Name))
                        Bad(ForbiddenName(t.Name) ? "not-allowed" : "unlisted", "files." + t.Name, ForbiddenName(t.Name) ? t.Name + " is not a scenario or a module: it cannot go in a pack" : "the manifest does not list " + t.Name);
            }
            if (!ScenarioFile.Blocks(found)) pack = p;
            return found;
        }

        private static string Str(JsonElement o, string key, string path, bool required, string missing, Action<string, string, string> bad)
        {
            if (!o.TryGetProperty(key, out JsonElement v) || v.ValueKind == JsonValueKind.Null) { if (required) bad("missing-field", path, missing); return ""; }
            if (v.ValueKind != JsonValueKind.String) { bad("wrong-type", path, "\"" + key + "\" is text"); return ""; }
            string s = v.GetString().Trim();
            if (required && s == "") bad("missing-field", path, missing);
            return s;
        }

        private static List<string> Words(JsonElement o, string key, string path, Action<string, string, string> bad)
        {
            var r = new List<string>();
            if (!o.TryGetProperty(key, out JsonElement v)) { bad("missing-field", path, "the manifest lists \"" + key + "\" (an empty list when there are none)"); return r; }
            if (v.ValueKind != JsonValueKind.Array) { bad("wrong-type", path, "\"" + key + "\" is a list of text"); return r; }
            int i = 0;
            foreach (JsonElement w in v.EnumerateArray())
            {
                if (w.ValueKind != JsonValueKind.String) bad("wrong-type", path + "[" + i.ToString(Inv) + "]", "\"" + key + "\" is a list of text");
                else r.Add(w.GetString());
                i++;
            }
            return r;
        }

        /// <summary>
        /// A pack from file texts (name, text), in that order: scenarios end in ".json", modules in ".module.json". The manifest
        /// is made from the files themselves: ids, hashes, sizes, the map files and features the scenarios use, PvE and mod.
        /// The result is read back with Read; text is "" when that finds an error, and the findings say why.
        /// </summary>
        public static List<Finding> Write(string name, string made, string by, string mod, IList<KeyValuePair<string, string>> files, out string text)
        {
            var maps = new SortedSet<string>(StringComparer.Ordinal);
            var caps = new SortedSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder(4096);
            string Q(string s) => "\"" + Guard.Esc(s ?? "") + "\"";
            sb.Append("{\"schema\":").Append(Q(Schema)).Append(",\"name\":").Append(Q(name)).Append(",\"made\":").Append(Q(made)).Append(",\"by\":").Append(Q(by))
              .Append(",\n \"manifest\":{\"files\":[");
            for (int i = 0; i < files.Count; i++)
            {
                string n = files[i].Key ?? "", t = files[i].Value ?? "";
                bool module = n.EndsWith(ModuleSuffix, StringComparison.OrdinalIgnoreCase);
                sb.Append(i > 0 ? "," : "").Append("\n  {\"name\":").Append(Q(n)).Append(",\"kind\":").Append(Q(module ? PackEntry.Module : PackEntry.Scenario));
                if (!module)
                {
                    CatalogueInfo.Identity(t, out string id, out _);
                    if (id != "") sb.Append(",\"id\":").Append(Q(id));
                    ScenarioFile.Read(t, out MissionPlan plan);
                    if (plan != null)
                    {
                        string nav = System.IO.Path.GetFileName((plan.NavFile ?? "").Trim());
                        if (nav != "") maps.Add(nav);
                        foreach (string c in MissionLab.Core.Capabilities.Of(plan)) caps.Add(c);
                    }
                }
                sb.Append(",\"hash\":").Append(Q(ScenarioFile.Hash(t))).Append(",\"bytes\":").Append(Encoding.UTF8.GetByteCount(t).ToString(Inv)).Append('}');
            }
            sb.Append("\n ],\n \"requires\":{\"maps\":[");
            int k = 0;
            foreach (string m in maps) sb.Append(k++ > 0 ? "," : "").Append(Q(m));
            sb.Append("],\"modes\":[").Append(Q(Pve)).Append("],\"mod\":").Append(Q(mod)).Append(",\"capabilities\":[");
            k = 0;
            foreach (string c in caps) sb.Append(k++ > 0 ? "," : "").Append(Q(c));
            sb.Append("]}},\n \"files\":{");
            for (int i = 0; i < files.Count; i++) sb.Append(i > 0 ? "," : "").Append("\n  ").Append(Q(files[i].Key)).Append(':').Append(Q(files[i].Value));
            sb.Append("\n }}\n");
            text = sb.ToString();
            List<Finding> found = Read(text, mod, out PackInfo back);
            if (back == null) text = "";
            return found;
        }

        /// <summary>
        /// How each file of a checked pack goes into the local folders, never replacing anything. choices: per pack file name
        /// "copy" (the default), "revision" or "skip", used only when the file collides. A scenario collides when a local file
        /// has its name or a local scenario has its id; a module when a local module has its name.
        ///   same:     the identical text is already there (same name or, for a scenario, same id): nothing to do.
        ///   install:  nothing collides: written as it is.
        ///   copy:     a new file name when the name is taken, and a new id ("-copy") when the id is taken, so it stands apart.
        ///   revision: keeps its id, takes the next revision after both the local and the pack's, under a new file name.
        ///   skip:     nothing is written.
        /// "revision" for a module, or for a scenario whose id nothing local has, is a copy: there is no local work to follow.
        /// </summary>
        public static List<PackStep> Plan(PackInfo pack, IDictionary<string, string> choices, IList<PackLocal> scenarios, IList<PackLocal> modules)
        {
            var steps = new List<PackStep>();
            var scenarioNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var moduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var revisions = new Dictionary<string, int>(StringComparer.Ordinal);      // id -> highest revision there
            foreach (PackLocal l in scenarios ?? new List<PackLocal>())
            {
                scenarioNames.Add(l.Name);
                if (l.Id != "") revisions[l.Id] = Math.Max(l.Revision, revisions.TryGetValue(l.Id, out int r) ? r : 0);
            }
            foreach (PackLocal l in modules ?? new List<PackLocal>()) moduleNames.Add(l.Name);

            foreach (PackEntry e in pack.Files)
            {
                bool module = e.Kind == PackEntry.Module;
                var s = new PackStep { Name = e.Name, Kind = e.Kind, Id = e.Id, Revision = e.Revision, Hash = e.Hash };
                steps.Add(s);
                IList<PackLocal> here = (module ? modules : scenarios) ?? new List<PackLocal>();
                PackLocal same = null;
                foreach (PackLocal l in here)
                    if (l.Hash == e.Hash && (string.Equals(l.Name, e.Name, StringComparison.OrdinalIgnoreCase) || (!module && e.Id != "" && l.Id == e.Id))) { same = l; break; }
                if (same != null) { s.Action = PackStep.Same; s.File = same.Name; s.Why = "the same text is already there as " + same.Name; continue; }

                HashSet<string> taken = module ? moduleNames : scenarioNames;
                bool nameHit = taken.Contains(e.Name), idHit = !module && e.Id != "" && revisions.ContainsKey(e.Id);
                string choice = choices != null && choices.TryGetValue(e.Name, out string c) && !string.IsNullOrEmpty(c) ? c : PackStep.Copy;
                if (!nameHit && !idHit) { s.Action = PackStep.Install; s.File = e.Name; s.Text = e.Text; }
                else if (choice == PackStep.Skip) { s.Action = PackStep.Skip; s.Why = Clash(e, nameHit, idHit) + "; skipped as asked"; continue; }
                else if (choice != PackStep.Copy && choice != PackStep.NewRevision) { s.Action = PackStep.Skip; s.Why = "\"" + choice + "\" is not copy, revision or skip; nothing was written"; continue; }
                else if (choice == PackStep.NewRevision && idHit)
                {
                    s.Action = PackStep.NewRevision;
                    s.Revision = Math.Max(revisions[e.Id], e.Revision) + 1;
                    s.File = FreeName(e.Name, "-r" + s.Revision.ToString(Inv), taken, false);
                    s.Text = Rewrite(e.Text, e.Id, s.Revision);
                    s.Why = Clash(e, nameHit, idHit) + "; installed as revision " + s.Revision.ToString(Inv) + " of " + e.Id;
                }
                else
                {
                    s.Action = PackStep.Copy;
                    s.File = nameHit ? FreeName(e.Name, "-copy", taken, module) : e.Name;
                    if (idHit)
                    {
                        s.Id = FreeId(e.Id, revisions);
                        s.Text = Rewrite(e.Text, s.Id, e.Revision);
                    }
                    else s.Text = e.Text;
                    s.Why = Clash(e, nameHit, idHit) + "; installed as a copy" + (idHit ? " with the id " + s.Id : "") + (s.File != e.Name ? " named " + s.File : "");
                }
                if (s.Text == null) { s.Action = PackStep.Skip; s.File = ""; s.Text = ""; s.Why = "the changed scenario could not be written; nothing was written"; continue; }
                s.Hash = ScenarioFile.Hash(s.Text);
                taken.Add(s.File);
                if (!module && s.Id != "") revisions[s.Id] = Math.Max(s.Revision, revisions.TryGetValue(s.Id, out int was) ? was : 0);
            }
            return steps;
        }

        private static string Clash(PackEntry e, bool nameHit, bool idHit) =>
            nameHit && idHit ? "a file named " + e.Name + " and a scenario with its id are already there"
            : nameHit ? "a file named " + e.Name + " is already there" : "a scenario with the id " + e.Id + " is already there";

        /// <summary>name with the ending added before ".json" / ".module.json", then numbered, until no local file has it.</summary>
        private static string FreeName(string name, string ending, HashSet<string> taken, bool module)
        {
            string suffix = module ? ModuleSuffix : ".json", stem = name.Substring(0, name.Length - suffix.Length);
            for (int n = 1; ; n++)
            {
                string tail = ending + (n == 1 ? "" : n.ToString(Inv));
                string cut = stem.Length + tail.Length > 60 ? stem.Substring(0, 60 - tail.Length) : stem;
                string candidate = cut + tail + suffix;
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        private static string FreeId(string id, Dictionary<string, int> ids)
        {
            for (int n = 1; ; n++)
            {
                string tail = "-copy" + (n == 1 ? "" : n.ToString(Inv));
                string cut = id.Length + tail.Length > MissionPlan.IdLength ? id.Substring(0, MissionPlan.IdLength - tail.Length) : id;
                if (!ids.ContainsKey(cut + tail)) return cut + tail;
            }
        }

        /// <summary>The scenario with another id or revision, as the plugin writes scenarios; null when it cannot be read.</summary>
        private static string Rewrite(string text, string id, int revision)
        {
            ScenarioFile.Read(text, out MissionPlan plan);
            if (plan == null) return null;
            plan.Id = id; plan.Revision = revision;
            return plan.ToJson();
        }
    }
}
