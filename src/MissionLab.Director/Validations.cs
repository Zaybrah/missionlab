using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// The validation matrix through the browser link (ML-048):
    ///   POST /api/validate                       {"scenario", "checks", "players", "seeds", "budget", "choices", "insertions"}: starts a run
    ///   GET  /api/validate/&lt;run&gt;               how far it has got, and the report once it is done
    ///   POST /api/validate/&lt;run&gt;/cancel        stops it between two combinations; the report keeps what was found
    ///   GET  /api/validations/&lt;scenario id&gt;     the last report kept for a scenario, and its counterexample
    /// A run can take many seconds, so it runs on a thread of its own, never on the game's thread and never holding a browser
    /// link worker: Core only (MissionValidation, the TriggerEngine, the scatter on NavWalk), the game's navmesh file and the
    /// saved rehearsals read from disk. No game objects. One run at a time. The finished report is kept under
    /// validations/&lt;scenario id&gt;/last.json, and the first failing combination beside it as counterexample.json.
    /// </summary>
    internal static class Validations
    {
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "validations");
        private const string Route = "/api/validate", Kept = "/api/validations/";
        private const string Running = "running", Done = "done", Broke = "error";
        private static readonly object Gate = new object();
        private static Run _run;                       // the run in progress or the last one; guarded by Gate
        private static int _count;

        private sealed class Run
        {
            public string Id = "";
            public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            public readonly ValidationProgress Progress = new ValidationProgress();
            public volatile string State = Running;
            public volatile string Answer = "";        // the finished answer's JSON, once State is not Running
        }

        /// <summary>The answer for a validation path, null for any other path.</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            bool start = r.Path == Route, one = r.Path.StartsWith(Route + "/", StringComparison.Ordinal), kept = r.Path.StartsWith(Kept, StringComparison.Ordinal);
            if (!start && !one && !kept) return null;
            try
            {
                if (start) return r.Method == "POST" ? Start(r.BodyText) : HttpResponse.Error(400, "POST expected");
                if (kept)
                {
                    string id = Uri.UnescapeDataString(r.Path.Substring(Kept.Length));
                    if (!RevisionFile.GoodId(id)) return HttpResponse.Error(400, "not a scenario id");
                    return r.Method == "GET" ? Last(id) : HttpResponse.Error(400, "GET expected");
                }
                string[] parts = r.Path.Substring(Route.Length + 1).Split('/');
                Run run;
                lock (Gate) run = _run != null && _run.Id == parts[0] ? _run : null;
                if (run == null) return HttpResponse.Error(404, "no such validation run; it may have been replaced by a newer one");
                if (parts.Length == 2 && parts[1] == "cancel")
                {
                    if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
                    run.Cancel.Cancel();
                    return HttpResponse.Json("{\"id\":\"" + run.Id + "\",\"state\":\"" + (run.State == Running ? "cancelling" : run.State) + "\"}");
                }
                if (parts.Length != 1 || r.Method != "GET") return HttpResponse.Error(400, "not a validation path");
                return HttpResponse.Json(Status(run));
            }
            catch (Exception e) { return HttpResponse.Error(500, "the validation could not be started or read: " + e.Message); }
        }

        private static HttpResponse Start(string body)
        {
            string why = MissionValidation.Ask(body, out string scenario, out ValidationAsk ask, out List<V3> insertions);
            if (why != "") return HttpResponse.Error(400, why);
            Run run;
            lock (Gate)
            {
                if (_run != null && _run.State == Running)
                    return HttpResponse.Json("{\"error\":\"a validation is already running; cancel it or wait for it\",\"id\":\"" + _run.Id + "\"}", 409);
                run = new Run { Id = "v" + (++_count).ToString(CultureInfo.InvariantCulture) };
                _run = run;
            }
            var thread = new Thread(() => Work(run, scenario, ask, insertions)) { IsBackground = true, Name = "MissionLab validation", Priority = ThreadPriority.BelowNormal };
            thread.Start();
            return HttpResponse.Json("{\"id\":\"" + run.Id + "\",\"state\":\"" + Running + "\"}", 202);
        }

        // The run's own thread. Nothing of the game is touched here; every failure becomes the answer, never an exception.
        private static void Work(Run run, string scenario, ValidationAsk ask, List<V3> insertions)
        {
            try
            {
                ScenarioFile.Read(scenario, out MissionPlan plan);
                var inputs = new ValidationInputs { Insertions = insertions };
                if (plan != null && ask.Scatter && MissionLab.Core.Scatter.Any(plan) && !string.IsNullOrEmpty(plan.NavFile)) Map(plan.NavFile, inputs);
                string id = plan != null && RevisionFile.GoodId(plan.Id) ? plan.Id : "";
                if (plan != null && ask.Rehearsals) Saved(id, inputs);
                ValidationReport report = MissionValidation.Run(scenario, ask, inputs, run.Cancel.Token, run.Progress);
                string made = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                string keptAs = "", keptWhy = "";
                if (id == "") keptWhy = "the scenario has no id yet, so the report was not kept; save the scenario once and validate again";
                else
                {
                    try { keptAs = Keep(id, report, made); }
                    catch (Exception e) { keptWhy = "the report could not be kept: " + e.Message; }
                }
                var sb = new StringBuilder(4096);
                sb.Append("{\"id\":\"").Append(run.Id).Append("\",\"state\":\"").Append(Done).Append("\",\"made\":\"").Append(made)
                  .Append("\",\"kept\":\"").Append(Guard.Esc(keptAs)).Append("\",\"keptWhy\":\"").Append(Guard.Esc(keptWhy)).Append("\",\"report\":")
                  .Append(MissionValidation.Json(report)).Append('}');
                run.Answer = sb.ToString();
                run.State = Done;
            }
            catch (Exception e)
            {
                run.Answer = "{\"id\":\"" + run.Id + "\",\"state\":\"" + Broke + "\",\"error\":\"" + Guard.Esc("the validation stopped: " + e.Message) + "\"}";
                run.State = Broke;
            }
        }

        private static string Status(Run run)
        {
            if (run.State != Running) return run.Answer;
            ValidationProgress p = run.Progress;
            return "{\"id\":\"" + run.Id + "\",\"state\":\"" + (run.Cancel.IsCancellationRequested ? "cancelling" : Running) + "\",\"check\":\"" + Guard.Esc(p.Check ?? "") +
                   "\",\"executed\":" + p.Executed.ToString(CultureInfo.InvariantCulture) + ",\"planned\":" + p.Planned.ToString(CultureInfo.InvariantCulture) + "}";
        }

        // The game's own navmesh file of that name, with its print, so the report can say which map it was tried on. Names only.
        private static void Map(string navFile, ValidationInputs inputs)
        {
            string name = navFile ?? "";
            if (name.Length == 0 || name.Length > 120 || name != Path.GetFileName(name) || name.Contains("..") || !name.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
            {
                inputs.MapWhy = "'" + name + "' is not a map file name";
                return;
            }
            string path = Path.Combine(Paths.GameDataPath, "NavMeshData", name);
            if (!File.Exists(path)) { inputs.MapWhy = "it is not in the game's NavMeshData folder"; return; }
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                inputs.Walk = new NavWalk(NavMesh.Load(path));
                inputs.NavFile = name;
                inputs.MapPrint = ScenarioFile.MapPrint(bytes);
            }
            catch (Exception e) { inputs.Walk = null; inputs.MapWhy = "it could not be read: " + e.Message; }
        }

        // The rehearsals kept for the scenario (Rehearsals.cs keeps them), by file name.
        private static void Saved(string id, ValidationInputs inputs)
        {
            if (id == "") { inputs.RehearsalsWhy = "not run: the scenario has no id yet, so no rehearsal can be kept for it"; return; }
            var list = new List<KeyValuePair<string, string>>();
            string folder = Path.Combine(Rehearsals.Dir, id);
            try
            {
                if (Directory.Exists(folder))
                    foreach (string f in Directory.GetFiles(folder, "*.json"))
                    {
                        string file = Path.GetFileName(f);
                        if (RehearsalFile.GoodFile(file)) list.Add(new KeyValuePair<string, string>(file, File.ReadAllText(f)));
                    }
                inputs.Rehearsals = list;
            }
            catch (Exception e) { inputs.Rehearsals = null; inputs.RehearsalsWhy = "not run: the saved rehearsals could not be read: " + e.Message; }
        }

        // Written beside the file and then moved into place, so a crash never leaves half a report under its name. A report
        // with no counterexample takes the old one away, as it would no longer belong to the report beside it.
        private static string Keep(string id, ValidationReport report, string made)
        {
            string folder = Path.Combine(Dir, id);
            lock (Gate)
            {
                Directory.CreateDirectory(folder);
                Write(Path.Combine(folder, "last.json"), MissionValidation.File(report, made));
                string counter = Path.Combine(folder, "counterexample.json");
                if (report.First != null) Write(counter, MissionValidation.CounterFile(report, made));
                else if (File.Exists(counter)) File.Delete(counter);
            }
            return "validations/" + id + "/last.json";
        }

        private static void Write(string full, string text)
        {
            string temp = full + ".part";
            File.WriteAllText(temp, text);
            File.Move(temp, full, true);
        }

        private static HttpResponse Last(string id)
        {
            string folder = Path.Combine(Dir, id), last = Path.Combine(folder, "last.json"), counter = Path.Combine(folder, "counterexample.json");
            string report, example;
            lock (Gate)
            {
                report = File.Exists(last) ? File.ReadAllText(last) : "";
                example = File.Exists(counter) ? File.ReadAllText(counter) : "";
            }
            if (report.Trim().Length == 0) return HttpResponse.Json("{\"id\":\"" + id + "\",\"kept\":null}");
            return HttpResponse.Json("{\"id\":\"" + id + "\",\"kept\":" + report.Trim() + ",\"counterexample\":" + (example.Trim().Length > 0 ? example.Trim() : "null") + "}");
        }
    }
}
