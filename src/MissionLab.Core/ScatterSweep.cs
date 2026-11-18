using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// The editor's "Try seeds" for constrained scatter (ML-023, POST /api/scatter/preview): the scenario is started the way
    /// the Director starts it (the dice with the Director's salt, the options at their defaults, scatter on the map's walkable
    /// surface, then Completion.Check), once for every seed asked. Each seed reports how many scattered enemies moved, stayed or
    /// were left out, with the notes, and every counterexample is listed: a seed whose dice or options alone make the mission
    /// unfinishable, a skip that makes it unfinishable, and an enemy that stayed at its authored place because no spot passed.
    /// The seeds tried are a sample; the answer says so and never claims every possible roll.
    /// </summary>
    public static class ScatterSweep
    {
        public const int MaxSeeds = 200, MaxPlayers = 16, MaxInsertions = 64;
        public const string Unfinishable = "unfinishable", SkipBreaks = "skip-unfinishable", Stayed = "stayed";
        private const long VariantSalt = 0x5EED;       // the Director's dice salt, so a counterexample reproduces in the game with that Seed
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>One seed's start: the counts, the notes (spawn numbers as in the file) and where moved enemies stand.</summary>
        public sealed class Run
        {
            public long Seed;
            public int Moved, Stayed, Skipped;
            public bool Refused;                                   // Completion.Check refused the setup: the game's own mission would run
            public List<string> Notes = new List<string>();
            public List<KeyValuePair<int, V3>> Spots = new List<KeyValuePair<int, V3>>();   // file spawn index -> resolved place
        }

        /// <summary>Something a player could meet with this seed that the author should look at.</summary>
        public sealed class Counter
        {
            public long Seed;
            public string Kind = "";                               // Unfinishable, SkipBreaks or Stayed
            public int Spawn = -1;                                 // the spawn's number in the file, -1 when the whole setup is meant
            public string Text = "";
        }

        /// <summary>
        /// Starts the scenario once with this seed and squad size on the given ground. text: the scenario as written (read
        /// afresh, so seeds never share state). insertions: the map's insertion points (none: no clearance is asked).
        /// </summary>
        public static Run Try(string text, long seed, int players, IWalk walk, List<V3> insertions, List<Counter> counters)
        {
            var run = new Run { Seed = seed };
            ScenarioFile.Read(text, out MissionPlan plan);
            if (plan == null) { run.Refused = true; return run; }
            var number = new Dictionary<SpawnPlan, int>();
            for (int i = 0; i < plan.Spawns.Count; i++) number[plan.Spawns[i]] = i;

            Variants.Out gone = null; int squad = 0;
            if (Variants.Any(plan)) { gone = Variants.Resolve(plan, new Rng(seed ^ VariantSalt), null, players); squad = players; }
            bool target = plan.Spawns.Exists(s => s.Hvt && s.Trigger == 0);
            Options.Apply(plan, null);
            bool tookTarget = target && !plan.Spawns.Exists(s => s.Hvt && s.Trigger == 0);
            List<Finding> before = Completion.Check(plan, gone, squad);
            bool brokenBefore = tookTarget || Completion.Blocks(before);
            if (brokenBefore)
                counters.Add(new Counter { Seed = seed, Kind = Unfinishable,
                    Text = "the dice and the default options alone make the mission unfinishable: " + (tookTarget ? "the target person is left out by the options" : Errors(before)) });

            var order = new List<SpawnPlan>(plan.Spawns);          // the order Scatter.Resolve visits, and names its notes by
            ScatterOut got = Scatter.Resolve(plan, walk, new Rng(seed ^ Scatter.Salt), insertions);
            var still = new HashSet<SpawnPlan>(plan.Spawns);
            var skipped = new List<string>();
            int note = 0;
            for (int j = 0; j < order.Count; j++)
            {
                SpawnPlan s = order[j];
                if (s.Scatter <= 0f) continue;
                int k = number.TryGetValue(s, out int n) ? n : -1;
                if (still.Contains(s) && s.Authored.HasValue) { run.Spots.Add(new KeyValuePair<int, V3>(k, s.Pos)); continue; }
                // A spawn that did not move has one note, in list order; its name there counts the resolved list, so it is
                // renamed with the spawn's number in the file, which is what the author sees.
                string said = note < got.Notes.Count ? got.Notes[note++] : "";
                string resolvedName = "spawns[" + j + "]" + (string.IsNullOrEmpty(s.Tag) ? "" : " '" + s.Tag + "'");
                string fileName = "spawns[" + k + "]" + (string.IsNullOrEmpty(s.Tag) ? "" : " '" + s.Tag + "'");
                string renamed = said.StartsWith(resolvedName, StringComparison.Ordinal) ? fileName + said.Substring(resolvedName.Length) : said;
                run.Notes.Add(renamed);
                if (!still.Contains(s)) skipped.Add(fileName);
                else counters.Add(new Counter { Seed = seed, Kind = Stayed, Spawn = k, Text = renamed });
            }
            if (got.Notes.Count > 0) run.Notes.Add(got.Notes[got.Notes.Count - 1]);      // the summary line
            run.Moved = got.Moved; run.Stayed = got.Stayed; run.Skipped = got.Skipped;

            List<Finding> after = Completion.Check(plan, gone, squad);
            run.Refused = brokenBefore || Completion.Blocks(after);
            if (!brokenBefore && Completion.Blocks(after))
                counters.Add(new Counter { Seed = seed, Kind = SkipBreaks,
                    Text = "leaving out " + string.Join(", ", skipped) + " makes the mission unfinishable, so the scenario would be refused: " + Errors(after) });
            return run;
        }

        /// <summary>The walkable ground of a map file by name, or null with why it cannot be had.</summary>
        public delegate IWalk WalkFor(string navFile, out string why);

        /// <summary>
        /// The answer to POST /api/scatter/preview {"scenario": text, "seeds": [1, 2, ...] or a count, "players": n,
        /// "insertions": [[x, y, z], ...]}: strict read first, then every seed on the scenario's own map (walkFor).
        /// </summary>
        public static string Answer(string body, WalkFor walkFor, out int status)
        {
            status = 400;
            string scenario = null; var seeds = new List<long>(); int players = 0; var insertions = new List<V3>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body ?? "", Lenient))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return Error("a seed sweep is sent as {scenario, seeds, players}");
                    if (root.TryGetProperty("scenario", out JsonElement sc) && sc.ValueKind == JsonValueKind.String) scenario = sc.GetString();
                    if (root.TryGetProperty("seeds", out JsonElement sd))
                    {
                        if (sd.ValueKind == JsonValueKind.Number && sd.TryGetInt32(out int count) && count >= 1)
                            for (int i = 1; i <= Math.Min(count, MaxSeeds); i++) seeds.Add(i);
                        else if (sd.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in sd.EnumerateArray())
                            {
                                if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out long one)) return Error("every seed is a whole number");
                                if (seeds.Count >= MaxSeeds) return Error("at most " + MaxSeeds + " seeds are tried at once");
                                if (!seeds.Contains(one)) seeds.Add(one);
                            }
                    }
                    if (root.TryGetProperty("players", out JsonElement pl) && pl.ValueKind == JsonValueKind.Number && pl.TryGetInt32(out int p)) players = p;
                    if (root.TryGetProperty("insertions", out JsonElement ins) && ins.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in ins.EnumerateArray())
                        {
                            if (insertions.Count >= MaxInsertions) break;
                            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 3) return Error("an insertion point is [x, y, z]");
                            var xyz = new float[3];
                            for (int k = 0; k < 3; k++)
                            {
                                if (e[k].ValueKind != JsonValueKind.Number) return Error("an insertion point is [x, y, z]");
                                xyz[k] = e[k].GetSingle();
                            }
                            insertions.Add(new V3(xyz[0], xyz[1], xyz[2]));
                        }
                }
            }
            catch (JsonException) { return Error("the request is not JSON"); }
            if (string.IsNullOrEmpty(scenario)) return Error("the scenario text is missing; no seeds were tried");
            if (scenario.Length > RevisionFile.MaxText) return Error("the scenario text is too long to be a scenario; no seeds were tried");
            if (seeds.Count == 0) return Error("no seeds were asked for: send \"seeds\": [1, 2, ...]");

            List<Finding> read = ScenarioFile.Read(scenario, out MissionPlan plan);
            if (plan == null)
            {
                status = 200;
                var refused = new StringBuilder(512).Append("{\"readable\":false,\"findings\":");
                CatalogueInfo.WriteFindings(refused, read);
                return refused.Append('}').ToString();
            }
            if (players < 1) players = plan.SquadMin > 0 ? plan.SquadMin : 1;
            players = Math.Min(players, MaxPlayers);
            if (!Scatter.Any(plan)) { status = 200; return "{\"readable\":true,\"scatter\":false,\"tried\":\"No enemy has a scatter radius, so no seeds were tried.\",\"runs\":[],\"counterexamples\":[]}"; }
            if (string.IsNullOrEmpty(plan.NavFile)) { status = 422; return Error("the scenario names no map file (navFile), so its walkable surface is unknown; no seeds were tried"); }
            IWalk walk = walkFor(plan.NavFile, out string why);
            if (walk == null) { status = 404; return Error(why == "" ? "the map file " + plan.NavFile + " could not be read; no seeds were tried" : why); }

            var counters = new List<Counter>();
            var runs = new List<Run>();
            foreach (long seed in seeds) runs.Add(Try(scenario, seed, players, walk, insertions, counters));
            status = 200;
            return Json(plan.NavFile, players, runs, counters);
        }

        /// <summary>The words every answer carries: how many seeds were tried, and that they are not every possible roll.</summary>
        public static string Tried(int seeds, int players, int counters) =>
            seeds + (seeds == 1 ? " seed" : " seeds") + " tried with " + players + (players == 1 ? " player" : " players") + "; not every possible roll. " +
            (counters == 0 ? "No counterexample among them." : counters + (counters == 1 ? " counterexample" : " counterexamples") + " found.");

        public static string Json(string navFile, int players, List<Run> runs, List<Counter> counters)
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\"readable\":true,\"scatter\":true,\"navFile\":\"").Append(Guard.Esc(navFile)).Append("\",\"players\":").Append(players)
              .Append(",\"seeds\":").Append(runs.Count).Append(",\"tried\":\"").Append(Guard.Esc(Tried(runs.Count, players, counters.Count))).Append("\",\"runs\":[");
            for (int i = 0; i < runs.Count; i++)
            {
                Run r = runs[i];
                sb.Append(i > 0 ? "," : "").Append("{\"seed\":").Append(r.Seed.ToString(Inv)).Append(",\"moved\":").Append(r.Moved).Append(",\"stayed\":").Append(r.Stayed)
                  .Append(",\"skipped\":").Append(r.Skipped).Append(",\"refused\":").Append(r.Refused ? "true" : "false").Append(",\"notes\":[");
                for (int k = 0; k < r.Notes.Count; k++) sb.Append(k > 0 ? "," : "").Append('"').Append(Guard.Esc(r.Notes[k])).Append('"');
                sb.Append("],\"spots\":[");
                for (int k = 0; k < r.Spots.Count; k++)
                {
                    V3 p = r.Spots[k].Value;
                    sb.Append(k > 0 ? "," : "").Append("{\"spawn\":").Append(r.Spots[k].Key).Append(",\"pos\":[").Append(Num(p.X)).Append(',').Append(Num(p.Y)).Append(',').Append(Num(p.Z)).Append("]}");
                }
                sb.Append("]}");
            }
            sb.Append("],\"counterexamples\":[");
            for (int i = 0; i < counters.Count; i++)
            {
                Counter c = counters[i];
                sb.Append(i > 0 ? "," : "").Append("{\"seed\":").Append(c.Seed.ToString(Inv)).Append(",\"kind\":\"").Append(c.Kind).Append("\",\"spawn\":").Append(c.Spawn)
                  .Append(",\"text\":\"").Append(Guard.Esc(c.Text)).Append("\"}");
            }
            return sb.Append("]}").ToString();
        }

        private static string Num(float v) => Math.Round(v, 2).ToString("0.##", Inv);

        private static string Errors(List<Finding> found) => string.Join("; ", found.FindAll(f => f.Level == Finding.Error).ConvertAll(f => f.ToString()));

        private static string Error(string why) => "{\"error\":\"" + Guard.Esc(why) + "\"}";
    }
}
