using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// What a generated layout is made from besides its seed, as written into its run record ("inputs"). Null means "not recorded":
    /// records from before 0.27 have no inputs at all, and a later version may leave one out. The player count is not here: the
    /// generator and its extras never look at it.
    /// </summary>
    public sealed class RepeatInputs
    {
        public int? Generator;              // RepeatSetup.GeneratorVersion of the code that made it
        public string Scene, NavFile;
        public string MapPrint;             // ScenarioFile.MapPrint of the navmesh file ("": it could not be read)
        public string ScenePrint;           // RepeatSetup.ScenePrint of the scene's own points and senses
        public int? Enemies;                // the enemy count handed to the generator
        public bool? RandomExtras, RandomIntel, MoveTarget;
        public bool? Target;                // the mission has a target person (only then can MoveTarget move one)
    }

    /// <summary>What the run record chosen for a repeat says about that run.</summary>
    public sealed class RepeatSource
    {
        public bool Readable = true;
        public string Outcome = "", Scenario = "";      // Scenario "": a generated layout
        public long Roll;
        public RepeatInputs Inputs;                     // null: the record holds none
    }

    /// <summary>Exact: the repeat may go ahead. Otherwise Reasons say why not, and Keys name the input or fact behind each reason.</summary>
    public sealed class RepeatVerdict
    {
        public readonly List<string> Keys = new List<string>();
        public readonly List<string> Reasons = new List<string>();
        public bool Exact => Reasons.Count == 0;
        public string Text => string.Join("; ", Reasons);
        internal void No(string key, string reason) { Keys.Add(key); Reasons.Add(reason); }
    }

    /// <summary>
    /// "Repeat this run" for a generated layout means the same effective setup: the same positions, roles, routes, target pick and
    /// random extras (waves, ambushes, locked doors, moved intel). What players, the game's AI and physics do afterwards is outside
    /// that promise. The setup can only come out the same from the same seed and the same inputs, so a repeat is refused before
    /// anything of the mission is changed when an input differs or is unknown; playing a new setup instead is the host's own choice.
    /// </summary>
    public static class RepeatSetup
    {
        /// <summary>
        /// Raise this only when the order or number of draws in Generator, Extras or the plugin's RandomExtras changes, or when
        /// what they place for the same seed and inputs changes in any other way. A run made by another version is not repeated.
        /// </summary>
        public const int GeneratorVersion = 1;

        // The keys of the record's "inputs" object; a verdict names them, plus the three facts about the record itself.
        public const string KeyRecord = "record", KeyOutcome = "outcome", KeyKind = "kind", KeyInputs = "inputs";
        public const string KeyGenerator = "generator", KeyScene = "scene", KeyNavFile = "navFile", KeyMapPrint = "mapPrint", KeyScenePrint = "scenePrint",
                            KeyEnemies = "enemies", KeyRandomExtras = "randomExtras", KeyRandomIntel = "randomIntel", KeyMoveTarget = "moveTarget", KeyTarget = "target";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// The seed of a layout: a chosen run's recorded roll comes first, even over a configured seed that says otherwise, because the
        /// host picked that run on purpose; then the configured seed (0: none); then a fresh one.
        /// </summary>
        public static long Seed(long? repeatRoll, long configured, Func<long> fresh)
        {
            if (repeatRoll.HasValue) return repeatRoll.Value;
            return configured != 0 ? configured : fresh();
        }

        /// <summary>May this generated mission repeat the chosen run exactly? now: the inputs this mission would generate from.</summary>
        public static RepeatVerdict ForGenerated(RepeatSource chosen, RepeatInputs now)
        {
            var v = new RepeatVerdict();
            if (chosen == null || !chosen.Readable) { v.No(KeyRecord, "the run record could not be read"); return v; }
            if (chosen.Outcome != "used") { v.No(KeyOutcome, "that run was refused, so it has no setup to repeat"); return v; }
            if (!string.IsNullOrEmpty(chosen.Scenario)) { v.No(KeyKind, "that run played scenario '" + chosen.Scenario + "', and this mission makes a generated layout"); return v; }
            RepeatInputs then = chosen.Inputs;
            if (then == null) { v.No(KeyInputs, "the run record comes from a MissionLab version that did not record the map and settings a generated layout is made from"); return v; }
            now = now ?? new RepeatInputs();

            if (!then.Generator.HasValue) Missing(v, KeyGenerator, "the generator version");
            else if (then.Generator != now.Generator) v.No(KeyGenerator, "generator version " + then.Generator.Value.ToString(Inv) + " then, " + (now.Generator.HasValue ? now.Generator.Value.ToString(Inv) : "unknown") + " now");
            if (then.Scene == null) Missing(v, KeyScene, "the scene");
            else if (then.Scene != (now.Scene ?? "")) v.No(KeyScene, "scene '" + then.Scene + "' then, '" + (now.Scene ?? "") + "' now");
            if (then.NavFile == null) Missing(v, KeyNavFile, "the map file");
            else if (then.NavFile != (now.NavFile ?? "")) v.No(KeyNavFile, "map file " + then.NavFile + " then, " + (now.NavFile ?? "none") + " now");
            // A fingerprint that could not be taken proves nothing, even when both sides lack one.
            if (then.MapPrint == null) Missing(v, KeyMapPrint, "the map file's fingerprint");
            else if (then.MapPrint == "") v.No(KeyMapPrint, "the map file could not be read when that run was made, so its map data cannot be compared");
            else if (string.IsNullOrEmpty(now.MapPrint)) v.No(KeyMapPrint, "the map file cannot be read now, so its map data cannot be compared");
            else if (then.MapPrint != now.MapPrint) v.No(KeyMapPrint, "the map data has changed since that run");
            if (then.ScenePrint == null) Missing(v, KeyScenePrint, "the scene's own points");
            else if (then.ScenePrint == "" || then.ScenePrint != (now.ScenePrint ?? "")) v.No(KeyScenePrint, "the scene's own enemy, insertion or target points, extraction zones or usual senses have changed since that run");
            if (!then.Enemies.HasValue) Missing(v, KeyEnemies, "the enemy count");
            else if (then.Enemies != now.Enemies) v.No(KeyEnemies, then.Enemies.Value.ToString(Inv) + " enemies then, " + (now.Enemies.HasValue ? now.Enemies.Value.ToString(Inv) : "unknown") + " now (General/EnemyCount or the game's own count)");
            Flag(v, KeyRandomExtras, "RandomExtras", then.RandomExtras, now.RandomExtras);
            Flag(v, KeyRandomIntel, "RandomIntel", then.RandomIntel, now.RandomIntel);
            Flag(v, KeyMoveTarget, "MoveTarget", then.MoveTarget, now.MoveTarget);
            if (!then.Target.HasValue) Missing(v, KeyTarget, "whether the mission had a target person");
            else if (then.Target != now.Target) v.No(KeyTarget, (then.Target.Value ? "a mission with a target person then, " : "a mission without a target person then, ") + (now.Target == true ? "one with a target now" : "one without now"));
            return v;
        }

        /// <summary>
        /// May this scenario mission repeat the chosen run? Only a run of a scenario can be; whether the same setup came out is
        /// said after the setup, as before 0.27 (the dice, options and squad are compared there).
        /// </summary>
        public static RepeatVerdict ForScenario(RepeatSource chosen, string scenario)
        {
            var v = new RepeatVerdict();
            if (chosen == null || !chosen.Readable) v.No(KeyRecord, "the run record could not be read");
            else if (string.IsNullOrEmpty(chosen.Scenario)) v.No(KeyKind, "that run was a generated layout, and this mission plays scenario '" + (scenario ?? "") + "'");
            return v;
        }

        private static void Missing(RepeatVerdict v, string key, string what) => v.No(key, what + " was not recorded with that run");

        private static void Flag(RepeatVerdict v, string key, string setting, bool? then, bool? now)
        {
            if (!then.HasValue) Missing(v, key, "the " + setting + " setting");
            else if (then != now) v.No(key, setting + " " + (then.Value ? "on" : "off") + " then, " + (now == true ? "on" : now == false ? "off" : "unknown") + " now");
        }

        /// <summary>
        /// A fingerprint of what the scene itself hands the generator and the target pick: its own enemy spawn points, the insertion
        /// points, the extraction boxes kept clear, the target spots and the usual senses. Positions count to the centimetre and
        /// each list is sorted first, because the game hands some of them over in no fixed order.
        /// </summary>
        public static string ScenePrint(List<V3> infils, List<V3> authored, List<float[]> avoid, List<V3> targets, float detect, float hear, float fov)
        {
            var sb = new StringBuilder(1024);
            Points(sb, "authored", authored);
            Points(sb, "infils", infils);
            Points(sb, "targets", targets);
            var boxes = new List<string>();
            if (avoid != null)
                foreach (float[] b in avoid)
                {
                    if (b == null) continue;
                    var parts = new List<string>();
                    foreach (float f in b) parts.Add(Cm(f));
                    boxes.Add(string.Join(",", parts));
                }
            boxes.Sort(StringComparer.Ordinal);
            sb.Append("|avoid:").Append(string.Join(";", boxes));
            sb.Append("|senses:").Append(Cm(detect)).Append(',').Append(Cm(hear)).Append(',').Append(Cm(fov));
            return ScenarioFile.Hash(sb.ToString());
        }

        private static void Points(StringBuilder sb, string name, List<V3> points)
        {
            var all = new List<string>();
            if (points != null) foreach (V3 p in points) all.Add(Cm(p.X) + "," + Cm(p.Y) + "," + Cm(p.Z));
            all.Sort(StringComparer.Ordinal);
            sb.Append('|').Append(name).Append(':').Append(string.Join(";", all));
        }

        private static string Cm(float f) => ((long)Math.Round(f * 100.0)).ToString(Inv);

        // ----- the record's "inputs" object

        /// <summary>The "inputs" object of a run record. Every key is written, false and 0 included, because a missing key means "not recorded".</summary>
        public static string InputsJson(RepeatInputs i)
        {
            var sb = new StringBuilder(320);
            sb.Append("{\"generator\":").Append((i.Generator ?? 0).ToString(Inv))
              .Append(",\"scene\":").Append(Str(i.Scene)).Append(",\"navFile\":").Append(Str(i.NavFile))
              .Append(",\"mapPrint\":").Append(Str(i.MapPrint)).Append(",\"scenePrint\":").Append(Str(i.ScenePrint))
              .Append(",\"enemies\":").Append((i.Enemies ?? 0).ToString(Inv))
              .Append(",\"randomExtras\":").Append(Bool(i.RandomExtras)).Append(",\"randomIntel\":").Append(Bool(i.RandomIntel))
              .Append(",\"moveTarget\":").Append(Bool(i.MoveTarget)).Append(",\"target\":").Append(Bool(i.Target)).Append('}');
            return sb.ToString();
        }

        private static string Str(string s) => "\"" + Guard.Esc(s ?? "") + "\"";
        private static string Bool(bool? b) => b == true ? "true" : "false";

        /// <summary>The inputs written into a run record's text, or null when it has none (records before 0.27, or text that is not JSON).</summary>
        public static RepeatInputs ReadInputs(string recordText)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(recordText ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("inputs", out JsonElement o) || o.ValueKind != JsonValueKind.Object) return null;
                    return new RepeatInputs
                    {
                        Generator = Int(o, KeyGenerator), Scene = Text(o, KeyScene), NavFile = Text(o, KeyNavFile), MapPrint = Text(o, KeyMapPrint), ScenePrint = Text(o, KeyScenePrint),
                        Enemies = Int(o, KeyEnemies), RandomExtras = Flag(o, KeyRandomExtras), RandomIntel = Flag(o, KeyRandomIntel), MoveTarget = Flag(o, KeyMoveTarget), Target = Flag(o, KeyTarget)
                    };
                }
            }
            catch (JsonException) { return null; }
        }

        private static string Text(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static int? Int(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : (int?)null;
        private static bool? Flag(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : (bool?)null;
    }
}
