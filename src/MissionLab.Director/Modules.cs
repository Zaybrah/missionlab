using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// Encounter modules (ML-019): reusable sets of enemies, patrol routes, triggers and messages, one file each under
    /// BepInEx/MissionLab/modules/&lt;name&gt;.module.json in the missionlab-module/1 format (docs/modules.md):
    ///   GET  /api/modules           the modules kept there: name, title and how much is in each
    ///   GET  /api/modules/&lt;name&gt;    one module's text
    ///   POST /api/modules           {"name", "text", "replace"}: kept after a shape check; never written over without replace
    /// Files only, answered on the browser link's worker thread: nothing here touches the game. The editor does the real
    /// work (inserting, remapping, binding); the plugin only keeps the files, so a module made on one computer can be reused.
    /// Modules have their own folder because the scenario scanner reads every *.json under scenarios/.
    /// </summary>
    internal static class Modules
    {
        internal const string Schema = "missionlab-module/1";
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "modules");
        private const string Suffix = ".module.json";
        private const int MaxBytes = 1 << 20;        // a module is a handful of enemies and triggers; a megabyte is far more than any needs
        private const int MaxListed = 200;
        private const int MaxItems = 500;            // enemies, triggers or bindings in one module
        private static readonly object Gate = new object();      // two saves of the same name at once must not interleave
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] Kinds = { "door", "objective", "place", "trigger", "extraction" };
        private static readonly JsonDocumentOptions Loose = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>The answer for a modules path, null for any other path.</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            bool one = r.Path.StartsWith("/api/modules/", StringComparison.Ordinal);
            if (r.Path != "/api/modules" && !one) return null;
            try
            {
                if (one)
                {
                    if (r.Method != "GET") return HttpResponse.Error(400, "GET expected");
                    string asked = Uri.UnescapeDataString(r.Path.Substring("/api/modules/".Length));
                    string file = FileName(asked);
                    // Only the names the list hands out: anything that needed cleaning is not one of them.
                    if (file == "" || (asked != file && asked + Suffix != file)) return HttpResponse.Error(400, "not a module name");
                    string full = Path.Combine(Dir, file);
                    return File.Exists(full) ? HttpResponse.Json(File.ReadAllText(full)) : HttpResponse.Error(404, "no such module");
                }
                if (r.Method == "POST") return Save(r.BodyText);
                if (r.Method != "GET") return HttpResponse.Error(400, "GET or POST expected");
                return ListAll();
            }
            catch (Exception e) { return HttpResponse.Error(500, "the modules could not be read or written: " + e.Message); }
        }

        /// <summary>
        /// The file name for a module: letters, digits, '_' and '-' only (CatalogueInfo.SafeName's rules), ".module.json" added.
        /// "" when nothing usable is left. Never a folder, so a save cannot land anywhere else.
        /// </summary>
        internal static string FileName(string name)
        {
            string n = (name ?? "").Trim();
            if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 5);
            if (n.EndsWith(".module", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 7);
            string safe = CatalogueInfo.SafeName(n);
            return safe == "" ? "" : safe.Substring(0, safe.Length - 5) + Suffix;
        }

        private static HttpResponse Save(string body)
        {
            string name = "", text = null; bool replace = false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return HttpResponse.Error(400, "a module is sent as {name, text, replace}");
                    if (root.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
                    if (root.TryGetProperty("text", out JsonElement t) && t.ValueKind == JsonValueKind.String) text = t.GetString();
                    if (root.TryGetProperty("replace", out JsonElement rp) && rp.ValueKind == JsonValueKind.True) replace = true;
                }
            }
            catch (JsonException) { return HttpResponse.Error(400, "the request is not JSON"); }
            string file = FileName(name);
            if (file == "") return HttpResponse.Error(400, "the module needs a name of letters, digits, '_' or '-'; nothing was saved");
            if (text == null || Encoding.UTF8.GetByteCount(text) > MaxBytes) return HttpResponse.Error(400, "the module text is missing or larger than 1 MB; nothing was saved");
            string why = Check(text, out string title, out int spawns, out int triggers, out int bindings, out int required);
            if (why != "") return HttpResponse.Error(400, "this is not a module MissionLab can use: " + why + "; nothing was saved");
            string path = Path.Combine(Dir, file);
            bool existed;
            lock (Gate)
            {
                existed = File.Exists(path);
                if (existed && !replace)
                {
                    // The same text again is the same save, sent twice: answer as saved and write nothing.
                    string was = "";
                    try { was = File.ReadAllText(path); } catch { }
                    if (was != text) return HttpResponse.Error(409, "a module named " + file + " is already there; it was left as it is. Save again with 'replace' to write over it");
                }
                else
                {
                    Directory.CreateDirectory(Dir);
                    // Written beside the file and then moved into place, so a failed write never leaves half a module under its name.
                    string temp = path + ".part";
                    File.WriteAllText(temp, text);
                    File.Move(temp, path, true);
                }
            }
            var sb = new StringBuilder(256);
            sb.Append("{\"saved\":true,\"replaced\":").Append(existed && replace ? "true" : "false").Append(",\"module\":");
            Summary(sb, file, title, spawns, triggers, bindings, required, "");
            return HttpResponse.Json(sb.Append('}').ToString());
        }

        private static HttpResponse ListAll()
        {
            var files = new List<string>();
            if (Directory.Exists(Dir)) files.AddRange(Directory.GetFiles(Dir, "*" + Suffix));
            files.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder(1024);
            sb.Append("{\"folder\":\"BepInEx/MissionLab/modules\",\"modules\":[");
            int k = 0;
            foreach (string f in files)
            {
                if (k >= MaxListed) break;
                string file = Path.GetFileName(f);
                if (FileName(file) != file) continue;     // a name the page could not ask for again
                string title = "", why;
                int spawns = 0, triggers = 0, bindings = 0, required = 0;
                try
                {
                    var info = new FileInfo(f);
                    why = info.Length > MaxBytes ? "larger than 1 MB" : Check(File.ReadAllText(f), out title, out spawns, out triggers, out bindings, out required);
                }
                catch (Exception e) { why = "could not be read: " + e.Message; }
                sb.Append(k++ == 0 ? "" : ",");
                Summary(sb, file, title, spawns, triggers, bindings, required, why);
            }
            return HttpResponse.Json(sb.Append("]}").ToString());
        }

        private static void Summary(StringBuilder sb, string file, string title, int spawns, int triggers, int bindings, int required, string problem)
        {
            sb.Append("{\"name\":\"").Append(Guard.Esc(file.Substring(0, file.Length - Suffix.Length)))
              .Append("\",\"file\":\"").Append(Guard.Esc(file))
              .Append("\",\"title\":\"").Append(Guard.Esc(title))
              .Append("\",\"spawns\":").Append(spawns.ToString(Inv))
              .Append(",\"triggers\":").Append(triggers.ToString(Inv))
              .Append(",\"bindings\":").Append(bindings.ToString(Inv))
              .Append(",\"required\":").Append(required.ToString(Inv));
            if (problem != "") sb.Append(",\"problem\":\"").Append(Guard.Esc(problem)).Append('"');
            sb.Append('}');
        }

        /// <summary>
        /// The shape of a missionlab-module/1 file, "" when it is fine, else what is wrong first. Only the shape: the editor
        /// checks the triggers and enemies against the scenario rules when it inserts a copy, as positions are relative here.
        /// </summary>
        internal static string Check(string text, out string title, out int spawns, out int triggers, out int bindings, out int required)
        {
            title = ""; spawns = triggers = bindings = required = 0;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text ?? "", Loose))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return "a module is a JSON object";
                    string schema = root.TryGetProperty("schema", out JsonElement sc) && sc.ValueKind == JsonValueKind.String ? sc.GetString() : "";
                    if (schema != Schema)
                        return schema.StartsWith("missionlab-module/", StringComparison.Ordinal) ? "it is " + schema + ", made by a newer MissionLab" : "its \"schema\" is not " + Schema;
                    if (!root.TryGetProperty("name", out JsonElement nm) || nm.ValueKind != JsonValueKind.String || nm.GetString().Trim() == "") return "it needs a \"name\"";
                    title = nm.GetString().Trim();
                    if (!root.TryGetProperty("anchor", out JsonElement an) || an.ValueKind != JsonValueKind.Object || !Vec(an, "pos")
                        || !an.TryGetProperty("yaw", out JsonElement yaw) || yaw.ValueKind != JsonValueKind.Number)
                        return "\"anchor\" is {\"pos\": [x, y, z], \"yaw\": degrees}";
                    if (!IsList(root, "spawns", out JsonElement sp) || !IsList(root, "triggers", out JsonElement tr) || !IsList(root, "bindings", out JsonElement bd))
                        return "\"spawns\", \"triggers\" and \"bindings\" are lists";
                    spawns = sp.GetArrayLength(); triggers = tr.GetArrayLength(); bindings = bd.GetArrayLength();
                    if (spawns + triggers == 0) return "it is empty: no enemies and no triggers";
                    if (spawns > MaxItems || triggers > MaxItems || bindings > MaxItems) return "it holds more than " + MaxItems.ToString(Inv) + " enemies, triggers or bindings";

                    var refs = new HashSet<string>(StringComparer.Ordinal);
                    var ids = new HashSet<int>();
                    int i = 0;
                    foreach (JsonElement t in tr.EnumerateArray())
                    {
                        string at = "triggers[" + (i++).ToString(Inv) + "]";
                        if (t.ValueKind != JsonValueKind.Object) return at + " is not an object";
                        if (!t.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int n) || n < 1) return at + " needs a whole number from 1 as its \"id\"";
                        if (!ids.Add(n)) return at + ": trigger number " + n.ToString(Inv) + " is used twice";
                    }
                    i = 0;
                    foreach (JsonElement b in bd.EnumerateArray())
                    {
                        string at = "bindings[" + (i++).ToString(Inv) + "]";
                        if (b.ValueKind != JsonValueKind.Object) return at + " is not an object";
                        string kind = b.TryGetProperty("kind", out JsonElement k) && k.ValueKind == JsonValueKind.String ? k.GetString() : "";
                        if (Array.IndexOf(Kinds, kind) < 0) return at + ": \"kind\" is one of door, objective, place, trigger, extraction";
                        string r = b.TryGetProperty("ref", out JsonElement rf) && rf.ValueKind == JsonValueKind.String ? rf.GetString() : "";
                        if (!GoodRef(r)) return at + ": \"ref\" is 1 to 32 letters, digits, '_' or '-'";
                        if (!refs.Add(r)) return at + ": \"" + r + "\" is listed twice";
                        bool must = kind != "extraction";
                        if (b.TryGetProperty("required", out JsonElement rq)) { if (rq.ValueKind != JsonValueKind.True && rq.ValueKind != JsonValueKind.False) return at + ": \"required\" is true or false"; must = rq.ValueKind == JsonValueKind.True; }
                        if (must) required++;
                        if (kind == "extraction" && !(b.TryGetProperty("trigger", out JsonElement bt) && bt.ValueKind == JsonValueKind.Number && bt.TryGetInt32(out int tn) && ids.Contains(tn)))
                            return at + ": an extraction binding names one of the module's triggers in \"trigger\"";
                    }
                    i = 0;
                    foreach (JsonElement s in sp.EnumerateArray())
                    {
                        string at = "spawns[" + (i++).ToString(Inv) + "]";
                        if (s.ValueKind != JsonValueKind.Object || !Vec(s, "pos")) return at + " needs \"pos\": [x, y, z]";
                        if (s.TryGetProperty("trigger", out JsonElement st) && st.ValueKind == JsonValueKind.Number && !(st.TryGetInt32(out int sn) && (sn == 0 || ids.Contains(sn))))
                            return at + ": \"trigger\" names no trigger of the module";
                    }
                    // Every outside reference ("@ref") names a listed binding: an unknown one could never be bound.
                    string missing = Refs(sp, refs, "spawns");
                    if (missing == "") missing = Refs(tr, refs, "triggers");
                    return missing;
                }
            }
            catch (JsonException e) { return "it is not JSON: " + e.Message; }
        }

        private static bool IsList(JsonElement o, string key, out JsonElement e) => o.TryGetProperty(key, out e) && e.ValueKind == JsonValueKind.Array;

        private static bool Vec(JsonElement o, string key)
        {
            if (!o.TryGetProperty(key, out JsonElement v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 3) return false;
            for (int i = 0; i < 3; i++) if (v[i].ValueKind != JsonValueKind.Number) return false;
            return true;
        }

        private static bool GoodRef(string r)
        {
            if (string.IsNullOrEmpty(r) || r.Length > 32) return false;
            foreach (char c in r)
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
            return true;
        }

        private static string Refs(JsonElement e, HashSet<string> refs, string at)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    string s = e.GetString();
                    return s.Length > 1 && s[0] == '@' && !refs.Contains(s.Substring(1)) ? at + ": \"" + s + "\" names no binding" : "";
                case JsonValueKind.Array:
                    int i = 0;
                    foreach (JsonElement x in e.EnumerateArray()) { string w = Refs(x, refs, at + "[" + (i++).ToString(Inv) + "]"); if (w != "") return w; }
                    return "";
                case JsonValueKind.Object:
                    foreach (JsonProperty p in e.EnumerateObject()) { string w = Refs(p.Value, refs, at + "." + p.Name); if (w != "") return w; }
                    return "";
                default:
                    return "";
            }
        }
    }
}
