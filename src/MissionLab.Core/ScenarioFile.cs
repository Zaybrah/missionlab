using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// The scenario file's contract, missionlab-plan/1 and /2. Reads a file strictly: a wrong type, a number used twice, a link to
    /// something that is not there, a kind this version does not know and a file written for a newer version are each named
    /// with the place in the file ("triggers[2].after") and a short code. Nothing is quietly dropped or bent into shape.
    /// missionlab-plan/2 holds the same body under "mission"; it exists so that older versions, which would read a shaped
    /// area as its anchor circle, refuse the file instead. Paths in findings are relative to the body.
    /// The editor carries the same rules in JavaScript; tests/fixtures/invalid holds files both must judge alike.
    /// docs/scenario.schema.json describes the same format for other tools.
    /// </summary>
    public static class ScenarioFile
    {
        public const int Version = 2;
        public static readonly string[] Kinds = { "zone", "hold", "alert", "intel", "time", "after", "down", "all", "any", "door" };

        // Field tables: n number, i whole number, b yes/no, s text. The editor has the same tables.
        private static readonly string[] SpawnFields = { "n:yaw", "n:detectionRange", "n:hearingRange", "n:fov", "n:wanderDistance", "n:patrolWaitSeconds", "n:chance", "n:scatter",
            "i:group", "i:trigger", "i:lot", "i:either", "i:option", "i:team", "i:minPlayers", "i:within", "b:indoor", "b:defensive", "b:fixed", "b:prone", "b:hvt", "b:vehicle", "b:nvg",
            "s:role", "s:type", "s:skill", "s:tag", "s:fallback" };
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

        /// <summary>
        /// A fingerprint of the navmesh file a scenario's coordinates were authored on: FNV-1a 64 over its raw bytes, so a
        /// changed map can be told from the one the author used. Not a protection against tampering.
        /// </summary>
        public static string MapPrint(byte[] bytes)
        {
            if (bytes == null) return "";
            ulong h = 14695981039346656037UL;
            foreach (byte b in bytes) { h ^= b; h *= 1099511628211UL; }
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
                int version = 1;
                if (!Has(root, "schema", out JsonElement schema)) Note("no-version", "schema", "the file names no format; it is read as missionlab-plan/1");
                else
                {
                    string s = schema.ValueKind == JsonValueKind.String ? schema.GetString() : "";
                    int v = s.StartsWith("missionlab-plan/", StringComparison.Ordinal) && int.TryParse(s.Substring(16), out int n) ? n : 0;
                    if (v <= 0) { Bad("unknown-format", "schema", "\"" + s + "\" is not a MissionLab scenario format"); return found; }
                    if (v > Version) { Bad("future-version", "schema", "this file is missionlab-plan/" + v + "; this version of MissionLab reads missionlab-plan/" + Version + ". Update MissionLab to use it. The file was not changed"); return found; }
                    version = v;
                }
                if (version >= 2)
                {
                    if (!Has(root, "mission", out JsonElement body) || body.ValueKind != JsonValueKind.Object) { Bad("wrong-type", "mission", "a missionlab-plan/2 file holds its scenario as an object under \"mission\""); return found; }
                    root = body;
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
                            if (Has(t, "area", out JsonElement area))
                            {
                                if (when != "zone" && when != "hold") Bad("bad-area", at + ".area", "only zone and hold triggers have an area");
                                else if (version < 2) Bad("bad-area", at + ".area", "a shaped area needs missionlab-plan/2: save the file again with this MissionLab");
                                else AreaPlan.Check(area, at, Bad);
                            }
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

                // Placements an enemy may scatter into, collected first like the trigger numbers.
                var allow = new HashSet<long>();
                if (Has(root, "placement", out JsonElement allowList) && allowList.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement pl in allowList.EnumerateArray())
                        if (pl.ValueKind == JsonValueKind.Object && Has(pl, "id", out JsonElement pid) && Whole(pid) && pid.GetInt64() >= 1 && (!Has(pl, "mode", out JsonElement pm) || (pm.ValueKind == JsonValueKind.String && pm.GetString() == PlacementPlan.Allow))) allow.Add(pid.GetInt64());
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
                        if (Has(s, "scatter", out JsonElement scatter) && scatter.ValueKind == JsonValueKind.Number && (scatter.GetDouble() <= 0 || scatter.GetDouble() > SpawnPlan.ScatterLimit)) Bad("out-of-range", at + ".scatter", "\"scatter\" is more than 0 and at most " + SpawnPlan.ScatterLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) + " metres");
                        if (Has(s, "within", out JsonElement within) && Whole(within) && within.GetInt64() != 0 && !allow.Contains(within.GetInt64())) Bad("missing-ref", at + ".within", "there is no placement " + within.GetInt64() + " that allows enemies");
                        Value(s, at, "fallback", new[] { SpawnPlan.Stay, SpawnPlan.Skip }, Bad);
                        if (Text(s, "fallback") == SpawnPlan.Skip && (Is(s, "hvt") || Is(s, "vehicle"))) Bad("unknown-value", at + ".fallback", "a required target cannot be skipped: an hvt or a vehicle stays where it was put");
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
                if (Has(root, "places", out JsonElement places))
                {
                    if (places.ValueKind != JsonValueKind.Array) Bad("wrong-type", "places", "\"places\" is a list");
                    else
                    {
                        var placeIds = new HashSet<long>(); int i = 0;
                        foreach (JsonElement pl in places.EnumerateArray())
                        {
                            string at = "places[" + i++ + "]";
                            if (pl.ValueKind != JsonValueKind.Object) { Bad("wrong-type", at, "a place is an object"); continue; }
                            if (!Has(pl, "id", out JsonElement id) || !Whole(id) || id.GetInt64() < 1) Bad("bad-id", at + ".id", "a place needs a whole number from 1 as its \"id\"");
                            else if (!placeIds.Add(id.GetInt64())) Bad("duplicate-id", at + ".id", "place number " + id.GetInt64() + " is used twice");
                            if (!Has(pl, "name", out JsonElement name) || (name.ValueKind == JsonValueKind.String && name.GetString().Trim() == "")) Bad("missing-field", at + ".name", "a place needs a \"name\" for people to read");
                            if (!(Has(pl, "pos", out JsonElement pos) && Spot(pos))) Bad("missing-field", at + ".pos", "a place needs \"pos\": [x, y, z]");
                            Types(pl, at, new[] { "i:id", "s:name" }, Bad);
                            if (Has(pl, "floor", out JsonElement floor))
                            {
                                if (floor.ValueKind != JsonValueKind.Array || floor.GetArrayLength() != 2 || floor[0].ValueKind != JsonValueKind.Number || floor[1].ValueKind != JsonValueKind.Number) Bad("wrong-type", at + ".floor", "\"floor\" is [bottom, top] in metres");
                                else if (floor[1].GetDouble() < floor[0].GetDouble()) Bad("bad-area", at + ".floor", "the top of the floor band is below its bottom");
                            }
                            if (Has(pl, "area", out JsonElement area)) AreaPlan.Check(area, at, Bad);
                        }
                    }
                }
                if (Has(root, "placement", out JsonElement placement))
                {
                    if (placement.ValueKind != JsonValueKind.Array) Bad("wrong-type", "placement", "\"placement\" is a list");
                    else
                    {
                        var seen = new HashSet<long>(); int i = 0;
                        foreach (JsonElement pl in placement.EnumerateArray())
                        {
                            string at = "placement[" + i++ + "]";
                            if (pl.ValueKind != JsonValueKind.Object) { Bad("wrong-type", at, "a placement is an object"); continue; }
                            if (!Has(pl, "id", out JsonElement id) || !Whole(id) || id.GetInt64() < 1) Bad("bad-id", at + ".id", "a placement needs a whole number from 1 as its \"id\"");
                            else if (!seen.Add(id.GetInt64())) Bad("duplicate-id", at + ".id", "placement number " + id.GetInt64() + " is used twice");
                            Named(pl, at, "a placement", Bad);
                            Choice(pl, at, "mode", new[] { PlacementPlan.Allow, PlacementPlan.Empty }, Bad);
                            if (!Has(pl, "area", out JsonElement area)) Bad("missing-field", at + ".area", "a placement needs an \"area\": it is the area");
                            else AreaPlan.Check(area, at, Bad);
                        }
                    }
                }
                if (Has(root, "options", out JsonElement options))
                {
                    if (options.ValueKind != JsonValueKind.Array) Bad("wrong-type", "options", "\"options\" is a list");
                    else
                    {
                        var seen = new HashSet<string>(StringComparer.Ordinal); int i = 0;
                        foreach (JsonElement op in options.EnumerateArray())
                        {
                            string at = "options[" + i++ + "]";
                            if (op.ValueKind != JsonValueKind.Object) { Bad("wrong-type", at, "an option is an object"); continue; }
                            Label(op, at, seen, "option", Bad);
                            Named(op, at, "an option", Bad);
                            int count = -1;
                            if (!Has(op, "choices", out JsonElement choices) || choices.ValueKind != JsonValueKind.Array) Bad("wrong-type", at + ".choices", "\"choices\" is a list of " + OptionPlan.FewestChoices + " to " + OptionPlan.MostChoices + " choices");
                            else
                            {
                                count = choices.GetArrayLength();
                                if (count < OptionPlan.FewestChoices || count > OptionPlan.MostChoices) Bad("out-of-range", at + ".choices", "an option has " + OptionPlan.FewestChoices + " to " + OptionPlan.MostChoices + " choices");
                                int j = 0;
                                foreach (JsonElement ch in choices.EnumerateArray())
                                {
                                    string cat = at + ".choices[" + j++ + "]";
                                    if (ch.ValueKind != JsonValueKind.Object) { Bad("wrong-type", cat, "a choice is an object"); continue; }
                                    Named(ch, cat, "a choice", Bad);
                                    if (Has(ch, "drop", out JsonElement drop))
                                    {
                                        if (drop.ValueKind != JsonValueKind.Object) Bad("wrong-type", cat + ".drop", "\"drop\" is an object: {\"lots\": [...], \"triggers\": [...]}");
                                        else
                                        {
                                            Numbers(drop, cat + ".drop", "lots", null, Bad);
                                            Numbers(drop, cat + ".drop", "triggers", ids, Bad);
                                        }
                                    }
                                    Range(ch, cat, "respawnTimes", true, 0, ChoicePlan.RespawnLimit, Bad);
                                    Choice(ch, cat, "skill", new[] { "", "recruit", "veteran" }, Bad);
                                }
                            }
                            if (Has(op, "pick", out JsonElement pick))
                            {
                                if (!Whole(pick)) Bad("wrong-type", at + ".pick", "\"pick\" must be a whole number");
                                else if (count >= 0 && (pick.GetInt64() < 0 || pick.GetInt64() >= count)) Bad("out-of-range", at + ".pick", "\"pick\" counts the choices from 0 and must name one of them");
                            }
                        }
                    }
                }
                if (Has(root, "director", out JsonElement director))
                {
                    if (director.ValueKind != JsonValueKind.Object) Bad("wrong-type", "director", "\"director\" is an object: {\"budget\": points, \"regen\": points a minute, \"actions\": [...]}");
                    else
                    {
                        Range(director, "director", "budget", true, 0, DirectorPlan.BudgetLimit, Bad);
                        Range(director, "director", "regen", true, 0, DirectorPlan.RegenLimit, Bad);
                        if (Has(director, "actions", out JsonElement actions))
                        {
                            if (actions.ValueKind != JsonValueKind.Array) Bad("wrong-type", "director.actions", "\"actions\" is a list");
                            else
                            {
                                var seen = new HashSet<string>(StringComparer.Ordinal); int i = 0;
                                foreach (JsonElement a in actions.EnumerateArray())
                                {
                                    string at = "director.actions[" + i++ + "]";
                                    if (a.ValueKind != JsonValueKind.Object) { Bad("wrong-type", at, "an action is an object"); continue; }
                                    Label(a, at, seen, "action", Bad);
                                    Named(a, at, "an action", Bad);
                                    Range(a, at, "cost", true, 0, ActionPlan.CostLimit, Bad);
                                    Range(a, at, "cooldown", false, 0, ActionPlan.CooldownLimit, Bad);
                                    if (!Has(a, "fire", out JsonElement fire)) Bad("missing-field", at + ".fire", "an action needs \"fire\": the trigger it fires");
                                    else if (!Whole(fire)) Bad("wrong-type", at + ".fire", "\"fire\" must be a whole number");
                                    else if (!ids.Contains(fire.GetInt64())) Bad("missing-ref", at + ".fire", "there is no trigger " + fire.GetInt64());
                                    Range(a, at, "minDistance", false, 0, ActionPlan.DistanceLimit, Bad);
                                    Types(a, at, new[] { "b:unseen" }, Bad);
                                    Choice(a, at, "fallback", new[] { ActionPlan.Wait, ActionPlan.Cancel }, Bad);
                                }
                            }
                        }
                    }
                }
                Types(root, "", new[] { "s:name", "s:scene", "s:navFile", "s:insertion", "s:power", "s:skill", "b:nvg", "b:gameMaster", "i:seed", "s:id", "i:revision", "n:minutes", "s:mapPrint", "b:sealed" }, Bad);
                Value(root, "", "power", new[] { "", "on", "off" }, Bad);
                Value(root, "", "skill", new[] { "", "recruit", "veteran" }, Bad);
                // Metadata the author keeps: only checked for sense, never needed to run the mission.
                string sid = Text(root, "id");
                if (sid != null && !MissionPlan.IdOk(sid)) Bad("bad-id", "id", "a scenario id is 1 to " + MissionPlan.IdLength + " letters, digits, '_' or '-'");
                if (Has(root, "revision", out JsonElement rev) && Whole(rev) && rev.GetInt64() < 0) Bad("out-of-range", "revision", "\"revision\" counts saves from 0");
                if (Has(root, "squad", out JsonElement squad))
                {
                    if (squad.ValueKind != JsonValueKind.Array || squad.GetArrayLength() != 2 || !Whole(squad[0]) || !Whole(squad[1])) Bad("wrong-type", "squad", "\"squad\" is [fewest, most] players, whole numbers");
                    else if (squad[0].GetInt64() < 1 || squad[1].GetInt64() > MissionPlan.SquadLimit || squad[0].GetInt64() > squad[1].GetInt64()) Bad("out-of-range", "squad", "\"squad\" is [fewest, most] players from 1 to " + MissionPlan.SquadLimit + ", fewest first");
                }
                if (Has(root, "minutes", out JsonElement minutes) && minutes.ValueKind == JsonValueKind.Number && (minutes.GetDouble() <= 0 || minutes.GetDouble() > MissionPlan.MinutesLimit)) Bad("out-of-range", "minutes", "\"minutes\" is more than 0 and at most " + MissionPlan.MinutesLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
                string print = Text(root, "mapPrint");
                if (print != null && !MissionPlan.PrintOk(print)) Bad("unknown-value", "mapPrint", "\"mapPrint\" is \"fnv1a64:\" and 16 lowercase hex digits, as MissionLab writes it");
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
        private static bool Is(JsonElement o, string key) => Has(o, key, out JsonElement v) && v.ValueKind == JsonValueKind.True;

        /// <summary>A name for people to read: required, and text.</summary>
        private static void Named(JsonElement o, string at, string what, Action<string, string, string> bad)
        {
            if (!Has(o, "name", out JsonElement name) || (name.ValueKind == JsonValueKind.String && name.GetString().Trim() == "")) bad("missing-field", at + ".name", what + " needs a \"name\" for people to read");
            else if (name.ValueKind != JsonValueKind.String) bad("wrong-type", at + ".name", "\"name\" must be text");
        }

        /// <summary>A text id of an option or an action: 1 to 32 letters, digits, '_' or '-', each once.</summary>
        private static void Label(JsonElement o, string at, HashSet<string> seen, string what, Action<string, string, string> bad)
        {
            string id = Text(o, "id");
            if (id == null || !OptionPlan.IdOk(id)) bad("bad-id", at + ".id", "an " + what + " id is 1 to " + OptionPlan.IdLength + " letters, digits, '_' or '-'");
            else if (!seen.Add(id)) bad("duplicate-id", at + ".id", "the " + what + " id \"" + id + "\" is used twice");
        }

        /// <summary>A text key with fixed values: first its type, then its value.</summary>
        private static void Choice(JsonElement o, string at, string key, string[] allowed, Action<string, string, string> bad)
        {
            if (!Has(o, key, out JsonElement v)) return;
            if (v.ValueKind != JsonValueKind.String) bad("wrong-type", Join(at, key), "\"" + key + "\" must be text");
            else Value(o, at, key, allowed, bad);
        }

        /// <summary>A number with bounds, inclusive: first its type, then its range.</summary>
        private static void Range(JsonElement o, string at, string key, bool whole, double lo, double hi, Action<string, string, string> bad)
        {
            if (!Has(o, key, out JsonElement v)) return;
            if (whole ? !Whole(v) : v.ValueKind != JsonValueKind.Number) { bad("wrong-type", Join(at, key), "\"" + key + "\" must be " + (whole ? "a whole number" : "a number")); return; }
            double d = v.GetDouble();
            if (d < lo || d > hi) bad("out-of-range", Join(at, key), "\"" + key + "\" is from " + lo.ToString(System.Globalization.CultureInfo.InvariantCulture) + " to " + hi.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>A list of whole numbers: lot numbers from 1, or (with ids) trigger numbers that exist.</summary>
        private static void Numbers(JsonElement o, string at, string key, HashSet<long> ids, Action<string, string, string> bad)
        {
            if (!Has(o, key, out JsonElement list)) return;
            if (list.ValueKind != JsonValueKind.Array) { bad("wrong-type", at + "." + key, "\"" + key + "\" is a list of whole numbers"); return; }
            int k = 0;
            foreach (JsonElement n in list.EnumerateArray())
            {
                string one = at + "." + key + "[" + k++ + "]";
                if (!Whole(n)) bad("wrong-type", one, "a " + (ids == null ? "lot" : "trigger") + " number is a whole number");
                else if (ids != null && !ids.Contains(n.GetInt64())) bad("missing-ref", one, "there is no trigger " + n.GetInt64());
                else if (ids == null && n.GetInt64() < 1) bad("out-of-range", one, "lot numbers start at 1");
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
