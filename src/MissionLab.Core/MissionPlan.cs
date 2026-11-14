using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
        public int WanderDistance;        // metres; only used by Wander
        public float PatrolWaitSeconds;
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

        public string ToJson()
        {
            var sb = new StringBuilder(4096);
            var inv = CultureInfo.InvariantCulture;
            string F(float f) => System.Math.Round(f, 2).ToString(inv);
            string P(V3 v) => "[" + F(v.X) + "," + F(v.Y) + "," + F(v.Z) + "]";
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
                var s = Spawns[i];
                if (i > 0) sb.Append(',');
                sb.Append("\n{\"group\":").Append(s.Group);
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
                sb.Append(",\"patrol\":[");
                for (int k = 0; k < s.Patrol.Count; k++) { if (k > 0) sb.Append(','); sb.Append(P(s.Patrol[k])); }
                sb.Append("]}");
            }
            sb.Append("\n]}");
            return sb.ToString();
        }
    }
}
