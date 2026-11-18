using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>What the quick mission form asks for (ML-022): a map, how many enemies in what mix, which extras, and a seed.</summary>
    public sealed class QuickRequest
    {
        public string NavFile = "";
        public int Enemies;
        public float[] Mix;                                      // guard, wander, patrol shares; null: the generator's own mix
        public bool Alert, Ambush, Doors;                        // the extras asked for
        public long Seed;
        public List<V3> Infils = new List<V3>();                 // where players arrive, as the page knows them
        public List<V3> Authored = new List<V3>();               // the map's own enemy spawn points, as the page knows them
        public List<DoorPlan> MapDoors = new List<DoorPlan>();   // the map's doors as the page knows them; Locked = true: the game locks it itself
    }

    /// <summary>
    /// A quick mission (ML-022): the navigation-aware generator over a map file, with the extras that need nothing from the
    /// running game (a wave on alert, an ambush, a few locked doors from a door list the page sends). Moved intel needs the
    /// game's furniture, so it is never part of a quick mission. The same request gives the same layout.
    /// </summary>
    public static class QuickMission
    {
        public const int FewestEnemies = 2, MostEnemies = 60, MostPoints = 500;
        public const long ExtrasSalt = 0xE87A5;                  // the salt the Director rolls a random layout's extras with
        public const string Alert = "alert", Ambush = "ambush", LockedDoors = "doors";
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        private static readonly string[] MixKeys = { "guard", "wander", "patrol" };

        /// <summary>A map file name as the game ships them (suburbv2.bytes): letters, digits, '_', '-' and '.', no folders.</summary>
        public static bool NavFileOk(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 120 || name.Contains("..")) return false;
            if (!name.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (char c in name)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')) return false;
            return true;
        }

        /// <summary>
        /// Reads {"navFile", "enemies", "mix": {"guard", "wander", "patrol"}, "extras": ["alert", "ambush", "doors"], "seed"}, with
        /// the optional "infils" and "authored" ([[x, y, z], ...]) and "doors" ([{"name", "pos", "locked"}]) the page knows for
        /// that map. Null, with the reason, when something is missing or wrong.
        /// </summary>
        public static QuickRequest Read(string body, out string error)
        {
            error = "";
            var q = new QuickRequest();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body ?? "", Lenient))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) { error = "a quick mission is asked for as {navFile, enemies, mix, extras, seed}"; return null; }
                    if (!root.TryGetProperty("navFile", out JsonElement nf) || nf.ValueKind != JsonValueKind.String || !NavFileOk(nf.GetString()))
                    { error = "navFile names one of the game's map files, such as suburbv2.bytes (a name only, no folders)"; return null; }
                    q.NavFile = nf.GetString();
                    if (!root.TryGetProperty("enemies", out JsonElement en) || en.ValueKind != JsonValueKind.Number || !en.TryGetInt32(out int n) || n < FewestEnemies || n > MostEnemies)
                    { error = "enemies is a whole number from " + FewestEnemies + " to " + MostEnemies; return null; }
                    q.Enemies = n;
                    if (!root.TryGetProperty("seed", out JsonElement sd) || sd.ValueKind != JsonValueKind.Number || !sd.TryGetInt64(out long seed))
                    { error = "seed is a whole number"; return null; }
                    q.Seed = seed;
                    if (root.TryGetProperty("mix", out JsonElement mx) && mx.ValueKind != JsonValueKind.Null)
                    {
                        if (mx.ValueKind != JsonValueKind.Object) { error = "mix is {guard, wander, patrol}, each a share"; return null; }
                        var shares = new float[3];
                        for (int i = 0; i < MixKeys.Length; i++)
                        {
                            if (!mx.TryGetProperty(MixKeys[i], out JsonElement v)) continue;
                            if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d) || d < 0 || d > 1000) { error = "mix." + MixKeys[i] + " is a share from 0 to 1000"; return null; }
                            shares[i] = (float)d;
                        }
                        q.Mix = Generator.Shares(shares) != null ? shares : null;
                    }
                    if (root.TryGetProperty("extras", out JsonElement ex) && ex.ValueKind != JsonValueKind.Null)
                    {
                        if (ex.ValueKind != JsonValueKind.Array) { error = "extras is a list: alert, ambush, doors"; return null; }
                        foreach (JsonElement e in ex.EnumerateArray())
                        {
                            string name = e.ValueKind == JsonValueKind.String ? e.GetString() : "";
                            if (name == Alert) q.Alert = true;
                            else if (name == Ambush) q.Ambush = true;
                            else if (name == LockedDoors) q.Doors = true;
                            else if (name == "intel") { error = "moved intel is not offered here: it needs the game itself (a table among the map's furniture) and the RandomIntel setting"; return null; }
                            else { error = "\"" + name + "\" is not an extra; the extras are alert, ambush and doors"; return null; }
                        }
                    }
                    Points(root, "infils", q.Infils);
                    Points(root, "authored", q.Authored);
                    if (root.TryGetProperty("doors", out JsonElement ds) && ds.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement d in ds.EnumerateArray())
                        {
                            if (q.MapDoors.Count >= MostPoints) break;
                            if (d.ValueKind != JsonValueKind.Object || !d.TryGetProperty("pos", out JsonElement p) || !Point(p, out V3 at)) continue;
                            string name = d.TryGetProperty("name", out JsonElement nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() : "";
                            if (name.Length > 120) name = name.Substring(0, 120);
                            bool own = d.TryGetProperty("locked", out JsonElement lk) && lk.ValueKind == JsonValueKind.True;
                            q.MapDoors.Add(new DoorPlan { Name = name, Pos = at, Locked = own });
                        }
                }
            }
            catch (JsonException) { error = "the request is not JSON"; return null; }
            return q;
        }

        /// <summary>
        /// The layout for a request on a map: exactly the enemies asked for (no spares), roles in the asked mix, then the extras,
        /// rolled with the Director's extras salt. leftOut gets, in the host's words, each part that was asked for or that a
        /// mission in the game would have but this layout has not, and why.
        /// </summary>
        public static MissionPlan Build(QuickRequest q, NavMesh nav, List<string> leftOut)
        {
            var input = new GeneratorInput { Nav = nav, Seed = q.Seed, Scene = "", Enemies = q.Enemies, Infils = q.Infils, Authored = q.Authored, Spares = 0, RoleMix = q.Mix };
            MissionPlan plan = Generator.Generate(input);
            plan.NavFile = q.NavFile;
            plan.Title = "Quick mission " + q.Seed.ToString(CultureInfo.InvariantCulture);
            if (q.Infils.Count == 0)
                leftOut.Add("insertion points: the page knows none for this map, so enemies are not kept clear of where players arrive (they come from a running mission on this map or a recon file)");
            var rng = new Rng(q.Seed ^ ExtrasSalt);
            var notes = new List<string>();
            if (q.Alert || q.Ambush)
            {
                if (plan.Spawns.Count < 6) leftOut.Add("alert wave and ambush: a layout of fewer than 6 enemies has neither");
                else
                {
                    Extras.Waves(plan, rng, q.Infils, notes, q.Alert, q.Ambush);
                    if (q.Alert && !plan.Triggers.Exists(t => t.Id == 1)) leftOut.Add("alert wave: no enemy out of doors and away from the insertion points was free for it");
                    if (q.Ambush && !plan.Triggers.Exists(t => t.Id == 2)) leftOut.Add("ambush: no building holds two or more enemies with another enemy free to come");
                }
            }
            if (q.Doors)
            {
                if (q.MapDoors.Count == 0) leftOut.Add("locked doors: the page knows none of this map's doors (they come from a running mission on this map or a recon file), so none are locked");
                else Extras.LockDoors(plan, q.MapDoors, rng, notes);
            }
            leftOut.Add("moved intel: never part of a quick mission; it needs the game itself (a table among the map's furniture), so the intel stays where the game puts it");
            plan.Notes.Add("extras: " + (notes.Count > 0 ? string.Join("; ", notes) : "none"));
            return plan;
        }

        /// <summary>
        /// The browser link's quick mission: the request read, the map loaded through load (null: the game has no such file),
        /// the layout built. Answered with {"navFile", "seed", "enemies", "placed", "roles", "later", "doorsLocked", "notes",
        /// "leftOut", "plan"}; "plan" is the layout as a scenario.
        /// </summary>
        public static string Answer(string body, Func<string, NavMesh> load, out int status)
        {
            status = 400;
            QuickRequest q = Read(body, out string error);
            if (q == null) return Error(error);
            NavMesh nav = load(q.NavFile);
            if (nav == null) { status = 404; return Error("the game has no map file " + q.NavFile + " in OPERATOR_Data/NavMeshData"); }
            var leftOut = new List<string>();
            MissionPlan plan = Build(q, nav, leftOut);
            status = 200;
            int guards = 0, wanderers = 0, patrols = 0, later = 0;
            foreach (SpawnPlan s in plan.Spawns)
            {
                if (s.Role == SpawnRole.Guard) guards++; else if (s.Role == SpawnRole.Wander) wanderers++; else patrols++;
                if (s.Trigger != 0) later++;
            }
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(8192);
            sb.Append("{\"navFile\":\"").Append(Guard.Esc(q.NavFile)).Append("\",\"seed\":").Append(q.Seed.ToString(inv))
              .Append(",\"enemies\":").Append(q.Enemies.ToString(inv)).Append(",\"placed\":").Append(plan.Spawns.Count.ToString(inv))
              .Append(",\"roles\":{\"guard\":").Append(guards.ToString(inv)).Append(",\"wander\":").Append(wanderers.ToString(inv)).Append(",\"patrol\":").Append(patrols.ToString(inv))
              .Append("},\"later\":").Append(later.ToString(inv)).Append(",\"doorsLocked\":").Append(plan.Doors.Count.ToString(inv));
            sb.Append(",\"notes\":"); Strings(sb, plan.Notes);
            sb.Append(",\"leftOut\":"); Strings(sb, leftOut);
            sb.Append(",\"plan\":").Append(plan.ToJson()).Append('}');
            return sb.ToString();
        }

        private static void Strings(StringBuilder sb, List<string> list)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++) sb.Append(i > 0 ? "," : "").Append('"').Append(Guard.Esc(list[i])).Append('"');
            sb.Append(']');
        }

        private static string Error(string why) => "{\"error\":\"" + Guard.Esc(why) + "\"}";

        private static void Points(JsonElement root, string key, List<V3> into)
        {
            if (!root.TryGetProperty(key, out JsonElement list) || list.ValueKind != JsonValueKind.Array) return;
            foreach (JsonElement p in list.EnumerateArray())
            {
                if (into.Count >= MostPoints) return;
                if (Point(p, out V3 at)) into.Add(at);
            }
        }

        private static bool Point(JsonElement p, out V3 at)
        {
            at = new V3(0, 0, 0);
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 3) return false;
            var xyz = new float[3];
            for (int i = 0; i < 3; i++)
            {
                JsonElement v = p[i];
                if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d) || double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 100000) return false;
                xyz[i] = (float)d;
            }
            at = new V3(xyz[0], xyz[1], xyz[2]);
            return true;
        }
    }
}
