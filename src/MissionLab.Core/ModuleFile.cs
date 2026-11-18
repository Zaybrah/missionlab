using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// The shape of an encounter module, missionlab-module/1 (docs/modules.md). Pure, so the plugin's modules folder and a
    /// scenario pack (Pack) judge a module by the same rules. Only the shape: the editor checks the triggers and enemies
    /// against the scenario rules when it inserts a copy, as positions in a module are relative to its anchor.
    /// The editor's moduleProblems has the same rules.
    /// </summary>
    public static class ModuleFile
    {
        public const string Schema = "missionlab-module/1";
        public const int MaxItems = 500;            // enemies, triggers or bindings in one module
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] Kinds = { "door", "objective", "place", "trigger", "extraction" };
        private static readonly JsonDocumentOptions Loose = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>"" when the text is a module MissionLab can use, else what is wrong first; with its title and counts.</summary>
        public static string Check(string text, out string title, out int spawns, out int triggers, out int bindings, out int required)
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
