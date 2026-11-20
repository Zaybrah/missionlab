using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using MissionLab.Core;
using UnityEngine;

namespace MissionLab
{
    // Inside the namespace, so it is found before any type of the game's own named Operation (the game has OperationsManager,
    // CerebusOperation and others in the global namespace, which a top-level using would lose to).
    using Operation = MissionLab.Core.Operation;

    /// <summary>
    /// Branching operations (ML-051), one file each under BepInEx/MissionLab/operations/ (missionlab-operation/1), the one in
    /// use named in operations/selected.txt. Every write goes through Core's OperationStore: a temporary file, the current
    /// file kept as &lt;name&gt;.prev.json, then a move into place; a damaged file is put back from its previous version and
    /// the page is told. An operation carries results from one mission to the next only: nothing inside a mission (enemies,
    /// inventory, positions, navigation, physics, networked objects) is saved or restored.
    ///   GET  /api/operations            the operations, the one in use (with the run records seen since) and recent runs
    ///   POST /api/operations/save       {file, operation}: its name, start and missions; progress stays the plugin's own
    ///   POST /api/operations/select     {file}: the operation in use ("" for none)
    ///   POST /api/operations/decide     {record, branch} or {record, again:true}: the host's labelled decision on a run
    ///   POST /api/operations/restart    {}: a new round from the first mission; earlier runs stay on record
    ///   POST /api/operations/delete     {file}
    ///   POST /api/operations/arm        {file, node, retry}: sets the next mission up (game thread: LiveLink queues it)
    /// Everything but arm is files only, answered on the browser link's worker thread. Setting up goes through the paths
    /// playlists use (Picker.Choose, the host's option picks, Director.SetRepeat); the map and mode stay the host's choice.
    /// </summary>
    internal static class Operations
    {
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "operations");
        internal const string Route = "/api/operations", ArmRoute = "/api/operations/arm";
        private const string SelectedFile = "selected.txt";
        private const int MostOperations = 40, RunsListed = 30;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // Worker threads and the game's thread (setting up, mission start) both read and write the files.
        private static readonly object Gate = new object();

        /// <summary>Worker thread. null: not an operation request, or setting up, which the game's thread answers (Arm).</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            if (r.Path != Route && !r.Path.StartsWith(Route + "/", StringComparison.Ordinal)) return null;
            if (r.Path == ArmRoute) return null;
            try
            {
                lock (Gate)
                {
                    if (r.Path == Route) return r.Method == "GET" ? HttpResponse.Json(Answer("")) : HttpResponse.Error(400, "GET expected");
                    if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(r.BodyText ?? ""); }
                    catch (JsonException) { return HttpResponse.Error(400, "the request is not JSON"); }
                    using (doc)
                    {
                        JsonElement body = doc.RootElement;
                        if (body.ValueKind != JsonValueKind.Object) return HttpResponse.Error(400, "the request is not a JSON object");
                        switch (r.Path)
                        {
                            case Route + "/save": return Save(body);
                            case Route + "/select":
                                {
                                    string file = Name(Str(body, "file"));
                                    if (file != "" && !File.Exists(Path.Combine(Dir, file))) return HttpResponse.Error(404, "there is no operation " + file);
                                    Directory.CreateDirectory(Dir);
                                    File.WriteAllText(Path.Combine(Dir, SelectedFile), file);
                                    return HttpResponse.Json(Answer(file == "" ? "No operation in use." : ""));
                                }
                            case Route + "/decide":
                                {
                                    Operation op = Selected(out string file, out string why, out string recovered);
                                    if (op == null) return HttpResponse.Error(409, why);
                                    Reconcile(file, op, Runs());
                                    int choice = Operation.Undecided;
                                    if (body.TryGetProperty("again", out JsonElement again) && again.ValueKind == JsonValueKind.True) choice = Operation.Again;
                                    else if (body.TryGetProperty("branch", out JsonElement b) && b.ValueKind == JsonValueKind.Number && b.TryGetInt32(out int at)) choice = at;
                                    if (choice == Operation.Undecided) return HttpResponse.Error(400, "a decision names a branch (-1 for the fallback) or again");
                                    if (!op.Decide(Str(body, "record"), choice, out string said)) return HttpResponse.Error(409, said);
                                    Write(file, op);
                                    return HttpResponse.Json(Answer(said + "."));
                                }
                            case Route + "/restart":
                                {
                                    Operation op = Selected(out string file, out string why, out string recovered);
                                    if (op == null) return HttpResponse.Error(409, why);
                                    Reconcile(file, op, Runs());
                                    string said = op.Restart();
                                    Write(file, op);
                                    return HttpResponse.Json(Answer(said + "."));
                                }
                            case Route + "/delete":
                                {
                                    string file = Name(Str(body, "file"));
                                    if (file == "") return HttpResponse.Error(400, "not an operation file name");
                                    string path = Path.Combine(Dir, file);
                                    foreach (string gone in new[] { path, OperationStore.PrevPath(path), path + ".damaged" }) if (File.Exists(gone)) File.Delete(gone);
                                    if (SelectedName() == file) File.WriteAllText(Path.Combine(Dir, SelectedFile), "");
                                    return HttpResponse.Json(Answer("Operation " + file + " deleted; its run records stay."));
                                }
                            default: return HttpResponse.Error(404, "not found");
                        }
                    }
                }
            }
            catch (Exception e) { return HttpResponse.Error(500, "the operations could not be read or written: " + e.Message); }
        }

        // ----- files

        // An operation file name: letters, digits, '_' and '-' with ".json" (CatalogueInfo.SafeName), never a folder; "" when
        // not one. That also leaves out the kept previous versions (<name>.prev.json) when the folder is listed.
        private static string Name(string file)
        {
            string f = (file ?? "").Trim();
            if (f == "") return "";
            string safe = CatalogueInfo.SafeName(f);
            return safe == f ? f : "";
        }

        private static string Str(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static string SelectedName()
        {
            string path = Path.Combine(Dir, SelectedFile);
            return File.Exists(path) ? Name(File.ReadAllText(path)) : "";
        }

        private static Operation Load(string file, out string why, out string recovered)
        {
            why = ""; recovered = "";
            if (file == "") { why = "no operation is named"; return null; }
            Operation op = OperationStore.Load(Path.Combine(Dir, file), out why, out recovered);
            // Logged, not shown as a banner: this runs on the browser link's threads too, where the game's clock cannot be read.
            if (recovered != "") DirectorPlugin.L.LogWarning("operation: " + recovered);
            return op;
        }

        private static Operation Selected(out string file, out string why, out string recovered)
        {
            file = SelectedName(); recovered = "";
            if (file == "") { why = "no operation is in use"; return null; }
            return Load(file, out why, out recovered);
        }

        private static void Write(string file, Operation op)
        {
            Directory.CreateDirectory(Dir);
            OperationStore.Save(Path.Combine(Dir, file), op);
        }

        // The run records on disk, oldest first, with their objectives. A record being written at this moment is skipped this time.
        private static List<OperationRun> Runs()
        {
            var list = new List<OperationRun>();
            try
            {
                if (!Directory.Exists(RunRecord.Dir)) return list;
                var files = new List<string>(Directory.GetFiles(RunRecord.Dir, "*.run.json"));
                files.Sort(StringComparer.Ordinal);
                foreach (string f in files)
                {
                    try { OperationRun s = OperationRun.Read(Path.GetFileName(f), File.ReadAllText(f)); if (s != null) list.Add(s); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return list;
        }

        private static List<RunSummary> Summaries(List<OperationRun> runs)
        {
            var list = new List<RunSummary>(runs.Count);
            foreach (OperationRun r in runs) list.Add(r.Summary);
            return list;
        }

        /// <summary>Counts the run records seen since; written back only when something changed (a mission boundary). Returns what changed.</summary>
        private static string Reconcile(string file, Operation op, List<OperationRun> runs)
        {
            string said = "";
            foreach (OperationRun r in runs)
            {
                string one = op.Take(r, Director.Session ?? "");
                if (one != "") said = one;
            }
            if (said != "") Write(file, op);
            return said;
        }

        private static string ScenarioText(string value)
        {
            string path = Path.Combine(Catalogue.Dir, value + ".json");
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception) { return null; }
        }

        // ----- answers

        private static string Q(string s) => "\"" + Guard.Esc(s ?? "") + "\"";

        /// <summary>The page's view: every operation, the one in use as it stands after the runs seen, and the recent runs.</summary>
        private static string Answer(string said)
        {
            var sb = new StringBuilder(4096);
            string selected = SelectedName(), trouble = "", recovered = "";
            List<OperationRun> runs = Runs();
            Operation op = null;
            if (selected != "")
            {
                op = Load(selected, out trouble, out recovered);
                if (op != null) { string seen = Reconcile(selected, op, runs); if (said == "") said = seen; }
            }
            sb.Append("{\"selected\":").Append(Q(op != null ? selected : "")).Append(",\"operations\":[");
            int n = 0;
            if (Directory.Exists(Dir))
            {
                var files = new List<string>(Directory.GetFiles(Dir, "*.json", SearchOption.TopDirectoryOnly));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string path in files)
                {
                    if (n >= MostOperations) break;
                    string file = Path.GetFileName(path);
                    if (Name(file) != file) continue;
                    string bad = "", restored = "";
                    Operation one = file == selected && op != null ? op : OperationStore.Load(path, out bad, out restored);
                    if (restored != "") DirectorPlugin.L.LogWarning("operation: " + restored);
                    sb.Append(n++ > 0 ? "," : "").Append("{\"file\":").Append(Q(file)).Append(",\"name\":").Append(Q(one != null ? one.Name : ""))
                      .Append(",\"nodes\":").Append(one != null ? one.Nodes.Count.ToString(Inv) : "0").Append(",\"why\":").Append(Q(one != null ? "" : bad)).Append('}');
                }
            }
            OperationStep pending = op != null ? op.Pending() : null;
            sb.Append("],\"operation\":").Append(op != null ? op.ToJson() : "null").Append(",\"trouble\":").Append(Q(trouble)).Append(",\"recovered\":").Append(Q(recovered))
              .Append(",\"pending\":").Append(Q(pending != null ? pending.Record : "")).Append(",\"retry\":").Append(Q(op != null ? op.RetryRecord() : "")).Append(",\"problems\":[");
            List<Finding> problems = op != null ? op.Check() : new List<Finding>();
            for (int i = 0; i < problems.Count; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"code\":").Append(Q(problems[i].Code)).Append(",\"path\":").Append(Q(problems[i].Path)).Append(",\"text\":").Append(Q(problems[i].Text)).Append('}');
            sb.Append("],\"runs\":[");
            for (int i = runs.Count - 1, k = 0; i >= 0 && k < RunsListed; i--, k++)
            {
                RunSummary r = runs[i].Summary;
                sb.Append(k > 0 ? "," : "").Append("{\"file\":").Append(Q(r.File)).Append(",\"scenario\":").Append(Q(r.Scenario)).Append(",\"scenarioId\":").Append(Q(r.ScenarioId))
                  .Append(",\"outcome\":").Append(Q(r.Outcome)).Append(",\"result\":").Append(Q(r.Result)).Append(",\"started\":").Append(Q(r.Started)).Append(",\"hash\":").Append(Q(r.Hash))
                  .Append(",\"goals\":").Append(Operation.GoalsJson(runs[i].Goals)).Append('}');
            }
            if (said == "" && recovered != "") said = recovered;
            return sb.Append("],\"said\":").Append(Q(said)).Append('}').ToString();
        }

        /// <summary>
        /// {file, operation}: the page's name, start and missions. A new file is named after the operation and never written
        /// over another. Progress (the next mission, the runs counted, the choices, the round) stays the plugin's own; the next
        /// mission stays set up only while its pinned setup is unchanged, and it cannot be removed while the operation is on it.
        /// </summary>
        private static HttpResponse Save(JsonElement body)
        {
            if (!body.TryGetProperty("operation", out JsonElement sent) || sent.ValueKind != JsonValueKind.Object) return HttpResponse.Error(400, "an operation is sent as {file, operation}");
            Operation op = Operation.Read(sent.GetRawText(), out string why);
            if (op == null) return HttpResponse.Error(400, "the operation " + why + "; nothing was saved");
            if (op.Name == "") return HttpResponse.Error(400, "an operation needs a name; nothing was saved");
            string file = Name(Str(body, "file"));
            if (Str(body, "file") != "" && file == "") return HttpResponse.Error(400, "not an operation file name; nothing was saved");
            Operation was = null;
            if (file != "" && File.Exists(Path.Combine(Dir, file)))
            {
                was = Load(file, out string bad, out string recovered);
                if (was == null) return HttpResponse.Error(409, bad + "; nothing was written over it");
            }
            else if (file == "")
            {
                string stem = CatalogueInfo.SafeName(op.Name);
                if (stem == "") stem = "operation.json";
                stem = stem.Substring(0, stem.Length - 5);
                file = stem + ".json";
                for (int i = 2; File.Exists(Path.Combine(Dir, file)); i++) file = stem + "-" + i.ToString(Inv) + ".json";
            }
            string changed = "";
            if (was != null)
            {
                op.Id = was.Id; op.Round = was.Round; op.Steps = was.Steps; op.Next = was.Next;
                if (!op.Done && op.Node(op.Next) == null)
                    return HttpResponse.Error(409, was.NodeName(was.Next) + " is the next mission, so it cannot be taken out while the operation is on it. Restart the operation or keep it; nothing was saved");
                OperationNode before = was.Current, now = op.Current;
                bool same = was.Armed && before != null && now != null && Playlist.EntryText(before.Setup) == Playlist.EntryText(now.Setup);
                op.Armed = same; op.ArmedAfter = same ? was.ArmedAfter : ""; op.Repeat = same ? was.Repeat : "";
                if (was.Armed && !same) changed = "Saved. The next mission changed, so set it up again before you host it.";
            }
            else
            {
                if (op.Id == "") op.Id = "op-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", Inv);
                op.Round = 1; op.Steps = new List<OperationStep>(); op.Next = op.Start; op.Armed = false; op.ArmedAfter = ""; op.Repeat = "";
            }
            List<Finding> problems = op.Check();
            if (problems.Count > 0) return HttpResponse.Error(400, "the operation cannot be used: " + problems[0] + (problems.Count > 1 ? " (and " + (problems.Count - 1).ToString(Inv) + " more)" : "") + "; nothing was saved");
            Write(file, op);
            if (was == null || SelectedName() == "") File.WriteAllText(Path.Combine(Dir, SelectedFile), file);     // a new operation is the one in use
            return HttpResponse.Json(Answer(was == null ? "Operation " + op.Name + " saved as " + file + "." : changed));
        }

        // ----- setting the next mission up (game thread)

        /// <summary>
        /// Game thread (a LiveLink job): {file, node, retry}. Refused, with nothing changed in the game, when the operation
        /// has moved on from the node the page saw, its scenario is missing or changed since it was pinned (the host chooses
        /// to retarget or restart first), or its dice cannot be set. retry: play the next mission's last run again with its
        /// exact setup (an interrupted or undecided run), without counting anything twice.
        /// </summary>
        internal static HttpResponse Arm(string bodyText)
        {
            string file = "", node = "";
            bool retry = false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(bodyText ?? ""))
                {
                    JsonElement b = doc.RootElement;
                    if (b.ValueKind == JsonValueKind.Object)
                    {
                        file = Name(Str(b, "file")); node = Str(b, "node");
                        retry = b.TryGetProperty("retry", out JsonElement t) && t.ValueKind == JsonValueKind.True;
                    }
                }
            }
            catch (JsonException) { return HttpResponse.Error(400, "the request is not JSON"); }
            lock (Gate)
            {
                if (file == "") return HttpResponse.Error(400, "no operation named");
                Operation op = Load(file, out string why, out string recovered);
                if (op == null) return HttpResponse.Error(404, why);
                List<OperationRun> runs = Runs();
                List<RunSummary> summaries = Summaries(runs);
                Reconcile(file, op, runs);        // a run of the mission set up before is counted for it first, not for this one
                if (op.Done) return HttpResponse.Error(409, "the operation is over; restart it to play it again");
                OperationNode n = op.Current;
                if (n == null) return HttpResponse.Error(409, "the operation's next mission is not one of its missions; nothing was changed");
                if (node != "" && node != n.Id) return HttpResponse.Json("{\"error\":" + Q("the operation has moved on to " + op.NodeName(n.Id) + "; look at it again before setting anything up") + "}", 409);
                List<Finding> problems = op.Check();
                if (problems.Count > 0) return HttpResponse.Error(409, "the operation cannot be used: " + problems[0] + "; nothing was changed");
                string identity = Playlist.Identity(n.Setup, ScenarioText(n.Setup.File), out string changed);
                if (identity != Playlist.Same)
                    return HttpResponse.Json("{\"error\":" + Q(op.NodeName(n.Id) + " was not set up: " + changed + ". Choose to use the file as it is now, use another mission, or restart the operation; its progress is kept either way.") + ",\"identity\":" + Q(identity) + "}", 409);
                string repeat;
                if (retry)
                {
                    repeat = op.RetryRecord();
                    RunSummary was = summaries.Find(x => x.File == repeat);
                    if (repeat == "" || was == null || was.Outcome != "used")
                        return HttpResponse.Error(409, "there is no run of " + op.NodeName(n.Id) + " to retry" + (repeat != "" ? " (run record " + repeat + " is gone or was refused)" : "") + "; set it up as it is instead");
                }
                else if (!new Playlist().SeedFor(n.Setup, summaries, out repeat, out string seedWhy))
                    return HttpResponse.Error(409, op.NodeName(n.Id) + " was not set up: " + seedWhy + ". Change its dice or pick another mission.");
                if (!Picker.Choose(n.Setup.File)) return HttpResponse.Error(409, "the game does not list scenarios/" + n.Setup.File + ".json; nothing was changed");
                Picker.UsePicks(n.Setup.Picks);
                string dice = Director.SetRepeat(repeat);
                op.ArmNext(summaries, repeat);
                Write(file, op);
                File.WriteAllText(Path.Combine(Dir, SelectedFile), file);
                string said = "Next mission of " + op.Name + ": " + op.NodeName(n.Id) + (retry ? " again, with the same setup" : "") + ". Host " + MapWords(n.Setup) + " in the game; " + dice + ".";
                Tell(said);
                return HttpResponse.Json(Answer(said));
            }
        }

        private static string MapWords(PlaylistEntry e)
        {
            string map = e.Map != "" ? e.Map : e.NavFile != "" ? e.NavFile : "the scenario's map";
            return map + " as a PvE raid";
        }

        private static void Tell(string text)
        {
            DirectorPlugin.L.LogInfo("operation: " + text);
            try { Director.Banner = "MissionLab operation: " + text; Director.BannerUntil = Time.unscaledTime + 8f; } catch { }
        }

        /// <summary>
        /// Game thread, at a mission's start, before its setup is read. Runs seen since are counted first. When the operation's
        /// next mission is still the next-mission choice and still the pinned content, it puts back the options and the repeat
        /// a restart of the game lost; a choice the host changed by hand wins, and changed content disarms it rather than
        /// playing it under the mission's name.
        /// </summary>
        internal static void BeforeMission()
        {
            lock (Gate)
            {
                Operation op = Selected(out string file, out string why, out string recovered);
                if (op == null || !op.Armed) return;
                List<OperationRun> runs = Runs();
                Reconcile(file, op, runs);
                OperationNode n = op.Current;
                if (!op.Armed || n == null) return;
                string now = Picker.Current();
                if (!string.Equals(now, n.Setup.File, StringComparison.OrdinalIgnoreCase))
                {
                    DirectorPlugin.L.LogInfo("operation: the next-mission choice is '" + now + "', not " + n.Setup.File + ", so this mission does not count for " + op.NodeName(n.Id));
                    return;
                }
                if (Playlist.Identity(n.Setup, ScenarioText(n.Setup.File), out string changed) != Playlist.Same)
                {
                    op.Armed = false; op.Repeat = ""; Write(file, op);
                    Tell(op.NodeName(n.Id) + " is no longer set up: " + changed + ". This mission uses the file as it is now and does not count for the operation");
                    return;
                }
                if (Picker.Picks().Count == 0 && n.Setup.Picks.Count > 0) Picker.UsePicks(n.Setup.Picks);
                if (op.Repeat != "" && Director.RepeatFile == "")
                {
                    // A repeat that cannot be set is not played as a new roll under the mission's name.
                    if (Summaries(runs).Exists(x => x.File == op.Repeat && x.Outcome == "used")) DirectorPlugin.L.LogInfo("operation: " + Director.SetRepeat(op.Repeat));
                    else
                    {
                        string gone = op.Repeat;
                        op.Armed = false; op.Repeat = ""; Write(file, op);
                        Tell(op.NodeName(n.Id) + " is no longer set up: run record " + gone + " is gone. This mission gets a new roll and does not count for the operation");
                    }
                }
            }
        }
    }
}
