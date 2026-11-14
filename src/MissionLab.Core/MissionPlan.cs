using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    public enum SpawnRole { Guard, Wander, Patrol }

    /// <summary>One enemy position and how the enemy behaves there.</summary>
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
        public bool Prone;                // spawns lying down
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
        public List<string> Notes = new List<string>();

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
            sb.Append(",\"patrol\":[");
            for (int k = 0; k < s.Patrol.Count; k++) { if (k > 0) sb.Append(','); sb.Append(P(s.Patrol[k])); }
            sb.Append("]}");
            return sb.ToString();
        }

        public string ToJson()
        {
            var sb = new StringBuilder(4096);
            var inv = CultureInfo.InvariantCulture;
            string S(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            sb.Append("{\"schema\":").Append(S(Schema));
            sb.Append(",\"seed\":").Append(Seed.ToString(inv));
            sb.Append(",\"scene\":").Append(S(Scene));
            sb.Append(",\"navFile\":").Append(S(NavFile));
            sb.Append(",\"requestedEnemies\":").Append(RequestedEnemies);
            sb.Append(",\"groups\":").Append(Groups);
            sb.Append(",\"notes\":[");
            for (int i = 0; i < Notes.Count; i++) { if (i > 0) sb.Append(','); sb.Append(S(Notes[i])); }
            sb.Append("],\"spawns\":[");
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
                };
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
                    };
                    string role = Str(e, "role") ?? "Guard";
                    if (!System.Enum.TryParse(role, true, out s.Role)) throw new System.FormatException("spawn " + index + ": unknown role \"" + role + "\" (Guard, Wander or Patrol)");
                    if (e.TryGetProperty("patrol", out JsonElement route) && route.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement p in route.EnumerateArray())
                            if (TryVec(p, out V3 wp)) s.Patrol.Add(wp);
                    if (s.Role == SpawnRole.Patrol && s.Patrol.Count < 2) s.Role = SpawnRole.Wander;
                    plan.Spawns.Add(s);
                    if (s.Group + 1 > plan.Groups) plan.Groups = s.Group + 1;
                }
                plan.RequestedEnemies = plan.Spawns.Count;
                return plan;
            }
        }

        private static string Str(JsonElement o, string name) =>
            o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static float Num(JsonElement o, string name, float fallback) =>
            o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

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
