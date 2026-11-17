using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// The scenario file's contract, missionlab-plan/1. Reads a file strictly: a wrong type, a number used twice, a link to
    /// something that is not there, a kind this version does not know and a file written for a newer version are each named
    /// with the place in the file ("triggers[2].after") and a short code. Nothing is quietly dropped or bent into shape.
    /// The editor carries the same rules in JavaScript; tests/fixtures/invalid holds files both must judge alike.
    /// docs/scenario.schema.json describes the same format for other tools.
    /// </summary>
    public static class ScenarioFile
    {
        public const int Version = 1;
        public static readonly string[] Kinds = { "zone", "hold", "alert", "intel", "time", "after", "down", "all", "any", "door" };

        // Field tables: n number, i whole number, b yes/no, s text. The editor has the same tables.
        private static readonly string[] SpawnFields = { "n:yaw", "n:detectionRange", "n:hearingRange", "n:fov", "n:wanderDistance", "n:patrolWaitSeconds", "n:chance",
            "i:group", "i:trigger", "i:lot", "i:either", "i:option", "i:team", "i:minPlayers", "b:indoor", "b:defensive", "b:fixed", "b:prone", "b:hvt", "b:vehicle", "b:nvg",
            "s:role", "s:type", "s:skill", "s:tag" };
        private static readonly string[] TriggerFields = { "n:radius", "n:delay", "n:hold", "n:chance", "n:repeat", "n:rally", "i:count", "i:times", "i:after",
            "b:facePlayer", "b:warn", "b:everyone", "b:red", "b:hunt", "b:respawn", "s:name", "s:when", "s:item", "s:message", "s:power", "s:end", "s:extraction", "s:tag" };
        private static readonly string[] ThingFields = { "s:name", "n:yaw", "n:chance", "i:trigger" };
        private static readonly string[] DoorFields = { "s:name", "b:locked", "b:enemiesCanOpen", "b:open", "n:chance" };

        public static bool Blocks(List<Finding> findings) => findings.Exists(f => f.Level == Finding.Error);

        /// <summary>
        /// A fingerprint of a scenario's text, to tell whether two runs used the same file. Line endings do not count.
        /// It is FNV-1a over the UTF-8 bytes: good for "is this the same text", not a protection against tampering.
        /// </summary>
        public static string Hash(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            ulong h = 14695981039346656037UL;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))) { h ^= b; h *= 1099511628211UL; }
            return "fnv1a64:" + h.ToString("x16");
        }

        /// <summary>Reads a scenario. plan is null when an error was found. The text itself is never changed.</summary>
        public static List<Finding> Read(string json, out MissionPlan plan)
        {
            plan = null;
            var found = new List<Finding>();
            void Bad(string code, string path, string text) => found.Add(new Finding { Code = code, Path = path, Text = text });
            void Note(string code, string path, string text) => found.Add(new Finding { Level = Finding.Note, Code = code, Path = path, Text = text });

            JsonDocument doc;
            try { doc = JsonDocument.Parse(json ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (JsonException e) { Bad("not-json", "", "this is not a JSON file: " + e.Message); return found; }
            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("plan", out JsonElement inner) && inner.ValueKind == JsonValueKind.Object) root = inner;
                if (root.ValueKind != JsonValueKind.Object) { Bad("wrong-type", "", "a scenario is a JSON object"); return found; }

                // The version comes first: a newer file is not looked at any further.
                if (!Has(root, "schema", out JsonElement schema)) Note("no-version", "schema", "the file names no format; it is read as missionlab-plan/" + Version);
                else
                {
                    string s = schema.ValueKind == JsonValueKind.String ? schema.GetString() : "";
                    int v = s.StartsWith("missionlab-plan/", StringComparison.Ordinal) && int.TryParse(s.Substring(16), out int n) ? n : 0;
                    if (v <= 0) { Bad("unknown-format", "schema", "\"" + s + "\" is not a MissionLab scenario format"); return found; }
                    if (v > Version) { Bad("future-version", "schema", "this file is missionlab-plan/" + v + "; this version of MissionLab reads missionlab-plan/" + Version + ". Update MissionLab to use it. The file was not changed"); return found; }
                }

                var ids = new HashSet<long>();
                if (Has(root, "triggers", out JsonElement triggers))
                {
                    if (triggers.ValueKind != JsonValueKind.Array) Bad("wrong-type", "triggers", "\"triggers\" is a list");
                    else
                    {
                        // The numbers first, so that links can be checked whatever the order in the file.
                        foreach (JsonElement t in triggers.EnumerateArray()) if (t.ValueKind == JsonValueKind.Object && Has(t, "id", out JsonElement id) && Whole(id)) ids.Add(id.GetInt64());
                        var seen = new HashSet<long>(); int i = 0;
                        foreach (JsonElement t in triggers.EnumerateArray())
                        {
                            string at = "triggers[" + i++ + "]";
                            if (t.ValueKind != JsonValueKind.Object) { Bad("wrong-type", at, "a trigger is an object"); continue; }
                            long me = 0;
                            if (!Has(t, "id", out JsonElement id) || !Whole(id) || id.GetInt64() < 1) Bad("bad-id", at + ".id", "a trigger needs a whole number from 1 as its \"id\"");
                            else { me = id.GetInt64(); if (!seen.Add(me)) Bad("duplicate-id", at + ".id", "trigger number " + me + " is used twice"); }
                            Types(t, at, TriggerFields, Bad);
                            string when = Text(t, "when") ?? "zone";
                            if (Array.IndexOf(Kinds, when) < 0) { Bad("unknown-kind", at + ".when", "\"" + when + "\" is not a kind of trigger this version knows"); continue; }
                            if ((when == "zone" || when == "hold" || when == "door") && !(Has(t, "pos", out JsonElement pos) && Spot(pos))) Bad("missing-field", at + ".pos", "a " + when + " trigger needs \"pos\": [x, y, z]");
                            if (when == "after" && Has(t, "after", out JsonElement after) && Whole(after))
                            {
                                long a = after.GetInt64();
                                if (a != 0 && a == me) Bad("self-ref", at + ".after", "the trigger waits for itself");
                                else if (a != 0 && !ids.Contains(a)) Bad("missing-ref", at + ".after", "there is no trigger " + a);
                            }
                            if ((when == "all" || when == "any") && Has(t, "of", out JsonElement of))
                            {
                                if (of.ValueKind != JsonValueKind.Array) Bad("wrong-type", at + ".of", "\"of\" is a list of trigger numbers");
                                else { int j = 0; foreach (JsonElement one in of.EnumerateArray()) { string op = at + ".of[" + j++ + "]"; if (!Whole(one)) Bad("wrong-type", op, "a trigger number is a whole number"); else if (!ids.Contains(one.GetInt64())) Bad("missing-ref", op, "there is no trigger " + one.GetInt64()); } }
                            }
                            Value(t, at, "power", new[] { "", "on", "off" }, Bad);
                            Value(t, at, "end", new[] { "", "success", "failed" }, Bad);
                            Value(t, at, "extraction", new[] { "", "open" }, Bad);
                            if (Has(t, "doors", out JsonElement td)) Doors(td, at + ".doors", Bad);
                        }
                    }
                }

                if (!Has(root, "spawns", out JsonElement spawns) || spawns.ValueKind != JsonValueKind.Array) Bad("wrong-type", "spawns", "a scenario needs \"spawns\": a list of enemies");
                else
                {
                    int i = 0;
                    foreach (JsonElement s in spawns.EnumerateArray())
                    {
                        string at = "spawns[" + i++ + "]";
                        if (s.ValueKind != JsonValueKind.Object) { Bad("wrong-type", at, "an enemy is an object"); continue; }
                        if (!(Has(s, "pos", out JsonElement pos) && Spot(pos))) Bad("missing-field", at + ".pos", "an enemy needs \"pos\": [x, y, z]");
                        Types(s, at, SpawnFields, Bad);
                        Value(s, at, "skill", new[] { "", "recruit", "veteran" }, Bad);
                        Link(s, at, ids, Bad);
                    }
                }

                if (Has(root, "extraction", out JsonElement ex))
                {
                    if (ex.ValueKind != JsonValueKind.Object) Bad("wrong-type", "extraction", "\"extraction\" is an object");
                    else if (Has(ex, "after", out JsonElement after))
                    {
                        if (!Whole(after)) Bad("wrong-type", "extraction.after", "a trigger number is a whole number");
                        else if (after.GetInt64() != 0 && !ids.Contains(after.GetInt64())) Bad("missing-ref", "extraction.after", "there is no trigger " + after.GetInt64());
                    }
                }
                if (Has(root, "things", out JsonElement things))
                {
                    if (things.ValueKind != JsonValueKind.Array) Bad("wrong-type", "things", "\"things\" is a list");
                    else
                    {
                        int i = 0;
                        foreach (JsonElement th in things.EnumerateArray())
                        {
                            string at = "things[" + i++ + "]";
                            if (th.ValueKind != JsonValueKind.Object) { Bad("wrong-type", at, "a thing is an object"); continue; }
                            if (!(Has(th, "pos", out JsonElement pos) && Spot(pos))) Bad("missing-field", at + ".pos", "a thing needs \"pos\": [x, y, z]");
                            Types(th, at, ThingFields, Bad);
                            Link(th, at, ids, Bad);
                        }
                    }
                }
                if (Has(root, "doors", out JsonElement doors)) Doors(doors, "doors", Bad);
                if (Has(root, "respawn", out JsonElement rs))
                {
                    if (rs.ValueKind != JsonValueKind.Object) Bad("wrong-type", "respawn", "\"respawn\" is an object: {\"after\": seconds, \"times\": waves}");
                    else Types(rs, "respawn", new[] { "n:after", "i:times" }, Bad);
                }
                Types(root, "", new[] { "s:name", "s:scene", "s:navFile", "s:insertion", "s:power", "s:skill", "b:nvg", "b:gameMaster", "i:seed" }, Bad);
                Value(root, "", "power", new[] { "", "on", "off" }, Bad);
                Value(root, "", "skill", new[] { "", "recruit", "veteran" }, Bad);
            }
            if (Blocks(found)) return found;
            try { plan = MissionPlan.FromJson(json); }
            catch (Exception e) { Bad("unreadable", "", e.Message); }
            return found;
        }

        private static bool Has(JsonElement o, string key, out JsonElement value) => o.TryGetProperty(key, out value) && value.ValueKind != JsonValueKind.Null;
        private static bool Whole(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out _);
        private static bool Spot(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 3) return false;
            for (int i = 0; i < 3; i++) if (e[i].ValueKind != JsonValueKind.Number) return false;
            return true;
        }
        private static string Text(JsonElement o, string key) => Has(o, key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static string Join(string at, string key) => at == "" ? key : at + "." + key;

        private static void Types(JsonElement o, string at, string[] fields, Action<string, string, string> bad)
        {
            foreach (string f in fields)
            {
                string key = f.Substring(2);
                if (!Has(o, key, out JsonElement v)) continue;
                bool ok = f[0] == 'n' ? v.ValueKind == JsonValueKind.Number : f[0] == 'i' ? Whole(v) : f[0] == 'b' ? v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False : v.ValueKind == JsonValueKind.String;
                if (!ok) bad("wrong-type", Join(at, key), "\"" + key + "\" must be " + (f[0] == 'n' ? "a number" : f[0] == 'i' ? "a whole number" : f[0] == 'b' ? "true or false" : "text"));
            }
        }
        private static void Value(JsonElement o, string at, string key, string[] allowed, Action<string, string, string> bad)
        {
            string v = Text(o, key);
            if (v != null && Array.IndexOf(allowed, v) < 0) bad("unknown-value", Join(at, key), "\"" + v + "\" is not a value this version knows for \"" + key + "\"");
        }
        private static void Link(JsonElement o, string at, HashSet<long> ids, Action<string, string, string> bad)
        {
            if (Has(o, "trigger", out JsonElement t) && Whole(t) && t.GetInt64() != 0 && !ids.Contains(t.GetInt64())) bad("missing-ref", at + ".trigger", "there is no trigger " + t.GetInt64());
        }
        private static void Doors(JsonElement list, string at, Action<string, string, string> bad)
        {
            if (list.ValueKind != JsonValueKind.Array) { bad("wrong-type", at, "\"doors\" is a list"); return; }
            int i = 0;
            foreach (JsonElement d in list.EnumerateArray())
            {
                string one = at + "[" + i++ + "]";
                if (d.ValueKind != JsonValueKind.Object) { bad("wrong-type", one, "a door is an object"); continue; }
                if (!(Has(d, "pos", out JsonElement pos) && Spot(pos))) bad("missing-field", one + ".pos", "a door needs \"pos\": [x, y, z], the place of its hinge");
                Types(d, one, DoorFields, bad);
            }
        }
    }
}
