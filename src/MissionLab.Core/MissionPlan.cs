using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    public enum SpawnRole { Guard, Wander, Patrol }

    /// <summary>One enemy position and how the enemy behaves there.</summary>
    /// <summary>
    /// Something that happens during the mission, and what follows from it. When it fires, the enemies bound
    /// to it appear and its doors lock or unlock.
    /// </summary>
    public sealed class TriggerPlan
    {
        public const string OnZone = "zone";      // a player walks into the circle at Pos
        public const string OnAlert = "alert";    // an enemy notices the players
        public const string OnIntel = "intel";    // an intel item is picked up (Item: its kind, "" for any, "all" for every one)
        public const string OnTime = "time";      // Delay seconds after the mission started
        public const string OnAfter = "after";    // another trigger (After) has fired
        public const string OnHold = "hold";      // a player has stayed inside the zone for Hold seconds without leaving
        public const string OnDown = "down";      // Count enemies have gone down (0: every enemy there is, once there was one)
        public const string OnAll = "all";        // every trigger listed in Of has fired
        public const string OnAny = "any";        // one of the triggers listed in Of has fired

        public int Id;
        public string When = OnZone;
        public V3 Pos;
        public float Radius = 10f;
        public string Name;
        public bool FacePlayer = true;    // its enemies appear looking at the player who set it off, or the nearest one
        public float Delay;               // seconds between the thing happening and the trigger firing
        public string Item = "";
        public int After;
        public List<DoorPlan> Doors = new List<DoorPlan>();     // only Locked is used: the door locks or unlocks when the trigger fires
        public bool OpensExtraction;      // when it fires, the team may extract, whatever the mission's own rule says
        public string Power = "";         // "off" or "on": the map's breaker box, and the lights wired to it, when it fires
        public string Message = "";       // shown on every player's screen when it fires
        public string End = "";           // "success" or "failed": the mission ends when it fires

        public const int MessageLength = 120;
        public static string OnOff(string v) => v == "off" || v == "on" ? v : "";
        public static string Ending(string v) => v == "success" || v == "failed" ? v : "";
        public static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            return text.Length > MessageLength ? text.Substring(0, MessageLength) : text;
        }

        public static bool Known(string when) => when == OnZone || when == OnAlert || when == OnIntel || when == OnTime || when == OnAfter || when == OnHold || when == OnDown || when == OnAll || when == OnAny;
        /// <summary>Kinds that are a place on the map.</summary>
        public static bool Placed(string when) => when == OnZone || when == OnHold;
        public float Hold = 30f;          // seconds, for OnHold
        public int Count;                 // enemies, for OnDown
        public List<int> Of = new List<int>();   // triggers, for OnAll and OnAny
        public int Chance = 100;          // percent: how often this trigger is part of the mission at all (rolled at the start)
        public bool Warn;                 // players are told when one minute and when ten seconds are left of its wait
        public bool Off;                  // lost its roll this mission: only "Fire now" sets it off. Not written to the file.
    }

    /// <summary>Where one of the mission's own objective items (intel to pick up) should lie.</summary>
    public sealed class ObjectivePlan
    {
        public string Name;     // the item's object name in the map, e.g. "Cell Phone_1", or just its kind, "Cell Phone"
        public V3 Pos;
        public float Yaw;

        /// <summary>
        /// What an item is, whichever copy of it the mission uses: "Cell Phone_2" and "cell phone" are both "cell phone".
        /// A map holds several copies of each item in different places and keeps one of them per mission.
        /// </summary>
        public static string Kind(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1])) end--;
            if (end < name.Length && end > 1 && (name[end - 1] == '_' || name[end - 1] == ' ')) end--;
            else end = name.Length;                         // digits that are part of the name itself stay
            return name.Substring(0, end).Trim().ToLowerInvariant();
        }
    }

    /// <summary>Weather and time of day, on maps that have them. Left out, a setting stays with the game.</summary>
    public sealed class WeatherPlan
    {
        public float? Hour;         // 0 to 24
        public bool? Rain;
        public bool? Sandstorm;
        public bool HasWish => Hour.HasValue || Rain.HasValue || Sandstorm.HasValue;
    }

    /// <summary>
    /// What a scenario wants of one of the map's doors. A door is known by where its hinge is, which never moves.
    /// Left out, a setting stays with the game, which locks some doors by chance.
    /// </summary>
    /// <summary>Something the game can place for all players: a weapon on the ground, an intel item, a mine.</summary>
    public sealed class ThingPlan
    {
        public string Name = "";          // the game's name for it, as it registers the prefab with the network
        public V3 Pos;
        public float Yaw;
        public int Trigger;               // 0: there from the start. Otherwise it is placed when that trigger fires
        public int Chance = 100;          // percent: how often it is there at all
        public string Key => Name + "|" + System.Math.Round(Pos.X, 1) + "|" + System.Math.Round(Pos.Y, 1) + "|" + System.Math.Round(Pos.Z, 1) + "|" + System.Math.Round(Yaw) + "|" + Trigger;
    }

    public sealed class DoorPlan
    {
        public string Name;             // for the reader only
        public V3 Pos;
        public bool? Locked;
        public bool? EnemiesCanOpen;
        public bool HasWish => Locked.HasValue || EnemiesCanOpen.HasValue;
        public int Chance = 100;        // percent: how often this setting applies at all; otherwise the door is left to the game
    }

    public sealed class SpawnPlan
    {
        public int Group;
        public SpawnRole Role;
        public V3 Pos;
        public float Yaw;                 // degrees, Unity convention (0 = +Z, clockwise)
        public bool Indoor;
        public bool Defensive;            // false = pushes toward contact
        public float DetectionRange;
        public float HearingRange;
        public float Fov;
        public int Trigger;               // 0 = there from the start; otherwise the id of the zone that brings it in
        public bool Hvt;                  // the mission's target person stands here (missions that have one)
        public bool Vehicle;              // not a person: one of the game's gun trucks is parked here, facing Yaw
        public bool Prone;                // spawns lying down
        public int Chance = 100;          // percent: how often this enemy is there at all (rolled when the mission starts)
        public int Lot;                   // enemies with the same lot share one roll: the whole squad is there, or none of it
        public int Either;                // an either/or set: of all enemies with this number, only one option is there
        public int Option;                // which option of the set this enemy belongs to (1 = A, 2 = B ...)
        public int MinPlayers;            // only there when at least this many players are in the mission (0 or 1: always)
        public string Skill = "";         // "recruit" or "veteran": slower and wilder, or quicker and surer, than the game makes it ("": as the game makes it)
        public bool Nvg;                  // sees in the dark
        public int Team;                  // 0: the side the game gives it. Otherwise the game's team number (4 = a second enemy side, 1 = the players' side)
        public string Type;               // enemy type by name; empty = one of the mission's usual types
        public int WanderDistance;        // metres; only used by Wander
        public float PatrolWaitSeconds;
        /// <summary>Stays exactly here with navigation off (a fixed sentry, e.g. on a roof or balcony).</summary>
        public bool Fixed;
        public List<V3> Patrol = new List<V3>();
    }

    /// <summary>A complete enemy layout for one mission, reproducible from Seed.</summary>
    public sealed class MissionPlan
    {
        public const string Schema = "missionlab-plan/1";
        public long Seed;
        public string Scene;
        public string NavFile;
        public int RequestedEnemies;
        public int Groups;
        public List<SpawnPlan> Spawns = new List<SpawnPlan>();
        public List<TriggerPlan> Triggers = new List<TriggerPlan>();
        public List<ObjectivePlan> Objectives = new List<ObjectivePlan>();
        public List<DoorPlan> Doors = new List<DoorPlan>();
        public int ExtractionAfter;       // extraction stays shut until this trigger has fired (0: the mission's own rule alone)
        public string Insertion = "";     // the insertion every player gets, by name ("": each player's own choice)
        public WeatherPlan Weather = new WeatherPlan();
        public string Power = "";         // "off": the breaker box is off when the mission starts ("": left to the game)
        public List<ThingPlan> Things = new List<ThingPlan>();
        public string Title = "";         // the mission's name ("name" in the file), shown to the players when it starts
        public List<string> Briefing = new List<string>();   // up to four lines shown on every screen at the start, one after another
        public const int BriefingLines = 4;
        public List<string> Notes = new List<string>();

        public static string ThingsJson(List<ThingPlan> things)
        {
            var sb = new StringBuilder(128);
            var inv = CultureInfo.InvariantCulture;
            string F(float f) => System.Math.Round(f, 2).ToString(inv);
            sb.Append('[');
            for (int i = 0; i < things.Count; i++)
            {
                ThingPlan t = things[i];
                sb.Append(i > 0 ? "," : "").Append("{\"name\":\"").Append((t.Name ?? "").Replace("\\", "/").Replace("\"", "'")).Append("\",\"pos\":[")
                  .Append(F(t.Pos.X)).Append(',').Append(F(t.Pos.Y)).Append(',').Append(F(t.Pos.Z)).Append("],\"yaw\":").Append(F(t.Yaw));
                if (t.Trigger > 0) sb.Append(",\"trigger\":").Append(t.Trigger);
                if (t.Chance < 100) sb.Append(",\"chance\":").Append(System.Math.Max(1, t.Chance));
                sb.Append('}');
            }
            return sb.Append(']').ToString();
        }

        public static string DoorsJson(List<DoorPlan> doors)
        {
            var sb = new StringBuilder(128);
            var inv = CultureInfo.InvariantCulture;
            string F(float f) => System.Math.Round(f, 2).ToString(inv);
            sb.Append('[');
            bool first = true;
            foreach (DoorPlan d in doors)
            {
                if (!d.HasWish) continue;
                sb.Append(first ? "" : ",").Append("{\"name\":\"").Append((d.Name ?? "").Replace("\\", "/").Replace("\"", "'")).Append("\",\"pos\":[")
                  .Append(F(d.Pos.X)).Append(',').Append(F(d.Pos.Y)).Append(',').Append(F(d.Pos.Z)).Append(']');
                if (d.Locked.HasValue) sb.Append(",\"locked\":").Append(d.Locked.Value ? "true" : "false");
                if (d.EnemiesCanOpen.HasValue) sb.Append(",\"enemiesCanOpen\":").Append(d.EnemiesCanOpen.Value ? "true" : "false");
                if (d.Chance < 100) sb.Append(",\"chance\":").Append(System.Math.Max(1, d.Chance));
                sb.Append('}');
                first = false;
            }
            return sb.Append(']').ToString();
        }

        public static string ObjectivesJson(List<ObjectivePlan> items)
        {
            var sb = new StringBuilder(128);
            var inv = CultureInfo.InvariantCulture;
            string F(float f) => System.Math.Round(f, 2).ToString(inv);
            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                ObjectivePlan o = items[i];
                sb.Append(i > 0 ? "," : "").Append("{\"name\":\"").Append((o.Name ?? "").Replace("\\", "/").Replace("\"", "'")).Append("\",\"pos\":[")
                  .Append(F(o.Pos.X)).Append(',').Append(F(o.Pos.Y)).Append(',').Append(F(o.Pos.Z)).Append("],\"yaw\":").Append(F(o.Yaw)).Append('}');
            }
            return sb.Append(']').ToString();
        }

        public static string TriggersJson(List<TriggerPlan> zones)
        {
            var sb = new StringBuilder(128);
            var inv = CultureInfo.InvariantCulture;
            string F(float f) => System.Math.Round(f, 2).ToString(inv);
            sb.Append('[');
            for (int i = 0; i < zones.Count; i++)
            {
                TriggerPlan z = zones[i];
                string Text(string t) => (t ?? "").Replace("\\", "/").Replace("\"", "'");
                sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(z.Id).Append(",\"name\":\"").Append(Text(z.Name)).Append("\",\"when\":\"").Append(z.When).Append('"');
                if (TriggerPlan.Placed(z.When))
                    sb.Append(",\"pos\":[").Append(F(z.Pos.X)).Append(',').Append(F(z.Pos.Y)).Append(',').Append(F(z.Pos.Z)).Append("],\"radius\":").Append(F(z.Radius));
                if (z.When == TriggerPlan.OnIntel) sb.Append(",\"item\":\"").Append(Text(z.Item)).Append('"');
                if (z.When == TriggerPlan.OnAfter) sb.Append(",\"after\":").Append(z.After);
                if (z.When == TriggerPlan.OnHold) sb.Append(",\"hold\":").Append(F(z.Hold));
                if (z.When == TriggerPlan.OnDown) sb.Append(",\"count\":").Append(z.Count);
                if (z.When == TriggerPlan.OnAll || z.When == TriggerPlan.OnAny) sb.Append(",\"of\":[").Append(string.Join(",", z.Of)).Append(']');
                if (z.Chance < 100) sb.Append(",\"chance\":").Append(System.Math.Max(1, z.Chance));
                if (z.Warn) sb.Append(",\"warn\":true");
                if (z.Delay > 0f) sb.Append(",\"delay\":").Append(F(z.Delay));
                sb.Append(",\"facePlayer\":").Append(z.FacePlayer ? "true" : "false");
                if (z.Doors.Exists(d => d.Locked.HasValue)) sb.Append(",\"doors\":").Append(DoorsJson(z.Doors));
                if (z.OpensExtraction) sb.Append(",\"extraction\":\"open\"");
                if (TriggerPlan.OnOff(z.Power) != "") sb.Append(",\"power\":\"").Append(z.Power).Append('"');
                if (TriggerPlan.Clean(z.Message) != "") sb.Append(",\"message\":\"").Append(Text(TriggerPlan.Clean(z.Message))).Append('"');
                if (TriggerPlan.Ending(z.End) != "") sb.Append(",\"end\":\"").Append(z.End).Append('"');
                sb.Append('}');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>One enemy as a JSON object, as it appears in a mission file.</summary>
        public static string SpawnJson(SpawnPlan s)
        {
            var sb = new StringBuilder(256);
            var inv = CultureInfo.InvariantCulture;
            string F(float f) => System.Math.Round(f, 2).ToString(inv);
            string P(V3 v) => "[" + F(v.X) + "," + F(v.Y) + "," + F(v.Z) + "]";
            string S(string t) => t == null ? "null" : "\"" + t.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            sb.Append("{\"group\":").Append(s.Group);
            sb.Append(",\"role\":").Append(S(s.Role.ToString()));
            sb.Append(",\"pos\":").Append(P(s.Pos));
            sb.Append(",\"yaw\":").Append(F(s.Yaw));
            sb.Append(",\"indoor\":").Append(s.Indoor ? "true" : "false");
            sb.Append(",\"defensive\":").Append(s.Defensive ? "true" : "false");
            sb.Append(",\"detectionRange\":").Append(F(s.DetectionRange));
            sb.Append(",\"hearingRange\":").Append(F(s.HearingRange));
            sb.Append(",\"fov\":").Append(F(s.Fov));
            sb.Append(",\"wanderDistance\":").Append(s.WanderDistance);
            sb.Append(",\"patrolWaitSeconds\":").Append(F(s.PatrolWaitSeconds));
            sb.Append(",\"fixed\":").Append(s.Fixed ? "true" : "false");
            sb.Append(",\"prone\":").Append(s.Prone ? "true" : "false");
            sb.Append(",\"type\":").Append(S(string.IsNullOrEmpty(s.Type) ? "" : s.Type));
            sb.Append(",\"trigger\":").Append(s.Trigger);
            sb.Append(",\"hvt\":").Append(s.Hvt ? "true" : "false");
            if (s.Vehicle) sb.Append(",\"vehicle\":true");
            if (s.Chance < 100) sb.Append(",\"chance\":").Append(System.Math.Max(1, s.Chance));
            if (s.Lot > 0) sb.Append(",\"lot\":").Append(s.Lot);
            if (s.Either > 0) sb.Append(",\"either\":").Append(s.Either).Append(",\"option\":").Append(System.Math.Max(1, s.Option));
            if (s.Team > 0) sb.Append(",\"team\":").Append(s.Team);
            if (s.MinPlayers > 1) sb.Append(",\"minPlayers\":").Append(s.MinPlayers);
            if (s.Skill == "recruit" || s.Skill == "veteran") sb.Append(",\"skill\":\"").Append(s.Skill).Append('"');
            if (s.Nvg) sb.Append(",\"nvg\":true");
            sb.Append(",\"patrol\":[");
            for (int k = 0; k < s.Patrol.Count; k++) { if (k > 0) sb.Append(','); sb.Append(P(s.Patrol[k])); }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>The settings for the whole mission, as ",name:value" pairs; nothing for a setting left to the game.</summary>
        public static string SettingsJson(MissionPlan p)
        {
            var sb = new StringBuilder(96);
            var inv = CultureInfo.InvariantCulture;
            if (p.ExtractionAfter != 0) sb.Append(",\"extraction\":{\"after\":").Append(p.ExtractionAfter).Append('}');
            if (!string.IsNullOrEmpty(p.Insertion)) sb.Append(",\"insertion\":\"").Append(p.Insertion.Replace("\\", "/").Replace("\"", "'")).Append('"');
            if (TriggerPlan.OnOff(p.Power) != "") sb.Append(",\"power\":\"").Append(p.Power).Append('"');
            if (p.Weather.HasWish)
            {
                var parts = new List<string>();
                if (p.Weather.Hour.HasValue) parts.Add("\"hour\":" + System.Math.Round(p.Weather.Hour.Value, 2).ToString(inv));
                if (p.Weather.Rain.HasValue) parts.Add("\"rain\":" + (p.Weather.Rain.Value ? "true" : "false"));
                if (p.Weather.Sandstorm.HasValue) parts.Add("\"sandstorm\":" + (p.Weather.Sandstorm.Value ? "true" : "false"));
                sb.Append(",\"weather\":{").Append(string.Join(",", parts)).Append('}');
            }
            return sb.ToString();
        }

        public string ToJson()
        {
            var sb = new StringBuilder(4096);
            var inv = CultureInfo.InvariantCulture;
            string S(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            sb.Append("{\"schema\":").Append(S(Schema));
            if (!string.IsNullOrWhiteSpace(Title)) sb.Append(",\"name\":").Append(S(Title.Trim()));
            if (Briefing.Count > 0) { sb.Append(",\"briefing\":["); for (int i = 0; i < Briefing.Count; i++) sb.Append(i > 0 ? "," : "").Append(S(Briefing[i])); sb.Append(']'); }
            sb.Append(",\"seed\":").Append(Seed.ToString(inv));
            sb.Append(",\"scene\":").Append(S(Scene));
            sb.Append(",\"navFile\":").Append(S(NavFile));
            sb.Append(",\"requestedEnemies\":").Append(RequestedEnemies);
            sb.Append(",\"groups\":").Append(Groups);
            sb.Append(",\"notes\":[");
            for (int i = 0; i < Notes.Count; i++) { if (i > 0) sb.Append(','); sb.Append(S(Notes[i])); }
            sb.Append("],\"triggers\":").Append(TriggersJson(Triggers));
            sb.Append(",\"objectives\":").Append(ObjectivesJson(Objectives));
            sb.Append(",\"doors\":").Append(DoorsJson(Doors));
            if (Things.Count > 0) sb.Append(",\"things\":").Append(ThingsJson(Things));
            sb.Append(SettingsJson(this));
            sb.Append(",\"spawns\":[");
            for (int i = 0; i < Spawns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\n').Append(SpawnJson(Spawns[i]));
            }
            sb.Append("\n]}");
            return sb.ToString();
        }

        /// <summary>
        /// Reads a plan written by ToJson, or a mission record that contains one under "plan".
        /// Missing fields keep their defaults; sense values of 0 mean "use the map's usual value".
        /// </summary>
        public static MissionPlan FromJson(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("plan", out JsonElement inner) && inner.ValueKind == JsonValueKind.Object) root = inner;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("spawns", out JsonElement spawns) || spawns.ValueKind != JsonValueKind.Array)
                    throw new System.FormatException("not a mission plan: no \"spawns\" list");

                var plan = new MissionPlan
                {
                    Seed = root.TryGetProperty("seed", out var seed) && seed.ValueKind == JsonValueKind.Number ? seed.GetInt64() : 0,
                    Scene = Str(root, "scene"),
                    NavFile = Str(root, "navFile"),
                    Insertion = Str(root, "insertion") ?? "",
                    Power = TriggerPlan.OnOff(Str(root, "power")),
                    Title = (Str(root, "name") ?? "").Trim(),
                };
                if (root.TryGetProperty("briefing", out JsonElement br) && br.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement line in br.EnumerateArray())
                    {
                        string text = line.ValueKind == JsonValueKind.String ? TriggerPlan.Clean(line.GetString()) : "";
                        if (text != "" && plan.Briefing.Count < BriefingLines) plan.Briefing.Add(text);
                    }
                if (root.TryGetProperty("extraction", out JsonElement ex) && ex.ValueKind == JsonValueKind.Object) plan.ExtractionAfter = System.Math.Max(0, (int)Num(ex, "after", 0));
                if (root.TryGetProperty("weather", out JsonElement we) && we.ValueKind == JsonValueKind.Object)
                {
                    if (we.TryGetProperty("hour", out JsonElement wh) && wh.ValueKind == JsonValueKind.Number) plan.Weather.Hour = System.Math.Min(24f, System.Math.Max(0f, wh.GetSingle()));
                    plan.Weather.Rain = Flag(we, "rain");
                    plan.Weather.Sandstorm = Flag(we, "sandstorm");
                }
                int index = 0;
                foreach (JsonElement e in spawns.EnumerateArray())
                {
                    index++;
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    if (!TryVec(e, "pos", out V3 pos)) throw new System.FormatException("spawn " + index + " has no \"pos\": [x, y, z]");
                    var s = new SpawnPlan
                    {
                        Group = (int)Num(e, "group", 0),
                        Pos = pos,
                        Yaw = Num(e, "yaw", 0),
                        Indoor = Bool(e, "indoor"),
                        Defensive = Bool(e, "defensive"),
                        DetectionRange = Num(e, "detectionRange", 0),
                        HearingRange = Num(e, "hearingRange", 0),
                        Fov = Num(e, "fov", 0),
                        WanderDistance = (int)Num(e, "wanderDistance", 0),
                        PatrolWaitSeconds = Num(e, "patrolWaitSeconds", 4),
                        Fixed = Bool(e, "fixed"),
                        Prone = Bool(e, "prone"),
                        Type = Str(e, "type"),
                        Trigger = (int)Num(e, "trigger", 0),
                        Hvt = Bool(e, "hvt"),
                        Vehicle = Bool(e, "vehicle"),
                        Chance = System.Math.Max(1, System.Math.Min(100, (int)Num(e, "chance", 100))),
                        Lot = System.Math.Max(0, (int)Num(e, "lot", 0)),
                        Either = System.Math.Max(0, (int)Num(e, "either", 0)),
                        Option = System.Math.Max(0, (int)Num(e, "option", 0)),
                        Team = System.Math.Max(0, System.Math.Min(8, (int)Num(e, "team", 0))),
                        MinPlayers = System.Math.Max(0, System.Math.Min(16, (int)Num(e, "minPlayers", 0))),
                        Skill = Str(e, "skill") == "recruit" ? "recruit" : Str(e, "skill") == "veteran" ? "veteran" : "", Nvg = Bool(e, "nvg"),
                    };
                    if (s.Either > 0 && s.Option < 1) s.Option = 1;
                    string role = Str(e, "role") ?? "Guard";
                    if (!System.Enum.TryParse(role, true, out s.Role)) throw new System.FormatException("spawn " + index + ": unknown role \"" + role + "\" (Guard, Wander or Patrol)");
                    if (e.TryGetProperty("patrol", out JsonElement route) && route.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement p in route.EnumerateArray())
                            if (TryVec(p, out V3 wp)) s.Patrol.Add(wp);
                    if (s.Role == SpawnRole.Patrol && s.Patrol.Count < 2) s.Role = SpawnRole.Wander;
                    plan.Spawns.Add(s);
                    if (s.Group + 1 > plan.Groups) plan.Groups = s.Group + 1;
                }
                if (root.TryGetProperty("triggers", out JsonElement zones) && zones.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement z in zones.EnumerateArray())
                    {
                        if (z.ValueKind != JsonValueKind.Object) continue;
                        string when = Str(z, "when");
                        if (!TriggerPlan.Known(when)) when = TriggerPlan.OnZone;                 // files from before there were other kinds
                        bool placed = TryVec(z, "pos", out V3 zp);
                        int id = (int)Num(z, "id", 0);
                        if (id <= 0 || (TriggerPlan.Placed(when) && !placed)) continue;
                        var t = new TriggerPlan { Id = id, When = when, Pos = zp, Radius = System.Math.Max(1f, Num(z, "radius", 10)), Name = Str(z, "name"),
                                                  FacePlayer = !(z.TryGetProperty("facePlayer", out JsonElement fp) && fp.ValueKind == JsonValueKind.False),
                                                  Delay = System.Math.Min(7200f, System.Math.Max(0f, Num(z, "delay", 0))), Item = Str(z, "item") ?? "", After = (int)Num(z, "after", 0) };
                        if (z.TryGetProperty("doors", out JsonElement zd) && zd.ValueKind == JsonValueKind.Array) ReadDoors(zd, t.Doors);
                        t.OpensExtraction = Str(z, "extraction") == "open";
                        t.Hold = System.Math.Min(3600f, System.Math.Max(1f, Num(z, "hold", 30))); t.Count = System.Math.Max(0, (int)Num(z, "count", 0));
                        t.Chance = System.Math.Max(1, System.Math.Min(100, (int)Num(z, "chance", 100))); t.Warn = Bool(z, "warn");
                        if (z.TryGetProperty("of", out JsonElement of) && of.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement o in of.EnumerateArray())
                                if (o.ValueKind == JsonValueKind.Number && o.GetInt32() > 0 && o.GetInt32() != id && !t.Of.Contains(o.GetInt32())) t.Of.Add(o.GetInt32());
                        t.Power = TriggerPlan.OnOff(Str(z, "power")); t.Message = TriggerPlan.Clean(Str(z, "message")); t.End = TriggerPlan.Ending(Str(z, "end"));
                        t.Doors.RemoveAll(d => !d.Locked.HasValue);
                        plan.Triggers.Add(t);
                    }
                if (root.TryGetProperty("objectives", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement o in items.EnumerateArray())
                    {
                        string name = o.ValueKind == JsonValueKind.Object ? Str(o, "name") : null;
                        if (!string.IsNullOrEmpty(name) && TryVec(o, "pos", out V3 op)) plan.Objectives.Add(new ObjectivePlan { Name = name, Pos = op, Yaw = Num(o, "yaw", 0) });
                    }
                if (root.TryGetProperty("doors", out JsonElement doors) && doors.ValueKind == JsonValueKind.Array) ReadDoors(doors, plan.Doors);
                if (root.TryGetProperty("things", out JsonElement things) && things.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement t in things.EnumerateArray())
                    {
                        if (t.ValueKind != JsonValueKind.Object || !TryVec(t, "pos", out V3 tp)) continue;
                        string name = Str(t, "name");
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        plan.Things.Add(new ThingPlan { Name = name, Pos = tp, Yaw = Num(t, "yaw", 0), Trigger = System.Math.Max(0, (int)Num(t, "trigger", 0)),
                                                         Chance = System.Math.Max(1, System.Math.Min(100, (int)Num(t, "chance", 100))) });
                    }
                // An enemy that names a zone that does not exist is simply there from the start.
                foreach (SpawnPlan s in plan.Spawns) if (s.Trigger != 0 && !plan.Triggers.Exists(t => t.Id == s.Trigger)) s.Trigger = 0;
                plan.RequestedEnemies = plan.Spawns.Count;
                return plan;
            }
        }

        private static string Str(JsonElement o, string name) =>
            o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static float Num(JsonElement o, string name, float fallback) =>
            o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

        private static void ReadDoors(JsonElement list, List<DoorPlan> into)
        {
            foreach (JsonElement d in list.EnumerateArray())
            {
                if (d.ValueKind != JsonValueKind.Object || !TryVec(d, "pos", out V3 dp)) continue;
                var door = new DoorPlan { Name = Str(d, "name"), Pos = dp, Locked = Flag(d, "locked"), EnemiesCanOpen = Flag(d, "enemiesCanOpen"),
                                          Chance = System.Math.Max(1, System.Math.Min(100, (int)Num(d, "chance", 100))) };
                if (door.HasWish) into.Add(door);
            }
        }

        /// <summary>true, false, or nothing said.</summary>
        private static bool? Flag(JsonElement o, string name) =>
            !o.TryGetProperty(name, out JsonElement v) ? (bool?)null : v.ValueKind == JsonValueKind.True ? true : v.ValueKind == JsonValueKind.False ? false : (bool?)null;

        private static bool Bool(JsonElement o, string name) =>
            o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

        private static bool TryVec(JsonElement o, string name, out V3 v)
        {
            v = default(V3);
            return o.TryGetProperty(name, out JsonElement a) && TryVec(a, out v);
        }

        private static bool TryVec(JsonElement a, out V3 v)
        {
            v = default(V3);
            if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 3) return false;
            v = new V3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
            return true;
        }
    }
}
