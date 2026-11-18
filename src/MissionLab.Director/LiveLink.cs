using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// Lets the scenario editor talk to the running game. The plugin serves the editor page
    /// at http://localhost:PORT on this computer only. The page reads the live mission
    /// (players, enemies, markers) and sends back edits, which Game Master mode carries out.
    ///
    /// Web requests arrive on worker threads. Anything that touches the game is queued and
    /// answered from the game's own thread in Pump(); files are served directly.
    /// </summary>
    internal static class LiveLink
    {
        private sealed class Job
        {
            public HttpRequest Req; public HttpResponse Res; public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public int Phase;              // 0: waiting its turn, 1: the game has begun it, 2: given up before it began (it never runs)
            public string Command = "";    // the page's own number for this change ("": none)
            public string Session;         // the mission it was made for (null: not said)
        }

        internal static readonly CommandLedger Ledger = new CommandLedger(200);
        private const int MaxWaiting = 64;             // more than this and the page is told to wait: a stuck page cannot pile up work
        private static int _waiting;
        private static volatile string _status = "{\"mission\":false}";
        private static string _sessionSeen = "";        // game thread only: the mission Pump last saw

        private static readonly ConcurrentQueue<Job> Jobs = new ConcurrentQueue<Job>();
        private static MiniHttp _web;
        private static string _webDir, _navDir, _mapsDir;
        private static volatile string _navFile;        // map file of the mission in progress
        internal static int Port;
        internal static string StartError = "";       // why the browser link is not there ("": it is)
        internal const string TestedGame = "0.09.5";  // the version of OPERATOR this build was tried with
        internal static bool Running;
        internal static float LastRequestAt = -100f;    // game time of the last request from the browser
        internal static volatile string PageStamp = "";  // changes whenever the editor page on disk changes

        // How far a map, mode and seat has been proven with this build: the values the page shows beside "Is everything ready?".
        internal const string Tested = "tested", Experimental = "experimental", Untested = "untested";
        internal const string AnyPve = "pve";           // in the table below: any of the PvE raid modes

        // The combinations played through with this build, in one place. A row is added only after its acceptance run has
        // passed (docs/backlog-status.md); anything not listed is reported as untested, so a host can tell before relying on it.
        private static readonly (string Map, string Mode, string Role, string Level)[] Compat =
        {
            ("suburbv2.bytes", AnyPve, "host", Tested),   // Clean House, hosted with the mod: the demonstrated baseline
        };

        /// <summary>The PvE raids MissionLab was written for. Everything else (PvP, wave survival, modes a later game adds) is left alone.</summary>
        internal static bool PveRaid(OperationsManager.GameMode mode)
        {
            return mode == OperationsManager.GameMode.StandardPVE || mode == OperationsManager.GameMode.PVE_HVTKILL ||
                   mode == OperationsManager.GameMode.PVE_FINDINTEL || mode == OperationsManager.GameMode.PVE_ASSETDESTROY;
        }

        /// <summary>The table's word for this map, mode and seat; "" while no map is in use, as there is nothing to judge.</summary>
        internal static string CompatFor(string map, string mode, bool pve, string role)
        {
            if (string.IsNullOrEmpty(map)) return "";
            foreach (var row in Compat)
            {
                bool modeFits = row.Mode == AnyPve ? pve : string.Equals(row.Mode, mode, StringComparison.Ordinal);
                if (modeFits && row.Role == role && string.Equals(row.Map, map, StringComparison.OrdinalIgnoreCase)) return row.Level;
            }
            return Untested;
        }

        private static string Stamp(string file)
        {
            try { var info = new FileInfo(file); return info.Exists ? info.Length + "-" + info.LastWriteTimeUtc.Ticks : ""; } catch { return ""; }
        }

        public static void Start(int port)
        {
            Port = port;
            _webDir = Path.Combine(DirectorPlugin.BaseDir, "web");
            _mapsDir = Path.Combine(DirectorPlugin.BaseDir, "maps");
            _navDir = Path.Combine(Paths.GameDataPath, "NavMeshData");
            Directory.CreateDirectory(_webDir);
            try
            {
                _web = new MiniHttp(port, Handle);
                _web.Start();
                Running = true;
                DirectorPlugin.L.LogInfo("browser Game Master: http://localhost:" + port + " (this computer only)");
            }
            catch (Exception e)
            {
                // The usual reasons: a second copy of the game or of the mod is already running, or another program holds the port.
                StartError = "The browser link could not start on port " + port + ": " + e.Message + ". Either a second copy of the game or of MissionLab is running, or another program uses that port. Close the other one, or change [LiveLink] Port in BepInEx/config/missionlab.director.cfg.";
                Director.Banner = "MissionLab: " + StartError; Director.BannerUntil = UnityEngine.Time.unscaledTime + 30f;
                DirectorPlugin.L.LogWarning(StartError); }
        }

        // Worker thread. No game objects here.
        private static HttpResponse Handle(HttpRequest r)
        {
            // Only the page this server hands out may talk to it; another website open in the browser may not.
            if (r.Headers.TryGetValue("Origin", out string origin) && origin != "http://localhost:" + Port && origin != "http://127.0.0.1:" + Port)
                return HttpResponse.Error(403, "this address only answers its own page");
            if (r.Path == "/three.min.js")
            {
                // The page's 3D library, kept beside the page so that 3D works without the internet.
                string lib = Path.Combine(_webDir, "three.min.js");
                return File.Exists(lib) ? new HttpResponse { ContentType = "application/javascript", Body = File.ReadAllBytes(lib) } : HttpResponse.Error(404, "not installed");
            }
            if (r.Path == "/" || r.Path == "/index.html")
            {
                string page = Path.Combine(_webDir, "index.html");
                if (File.Exists(page))
                {
                    // The page learns which copy it is, so a tab left open reloads itself after an update.
                    string stamp = Stamp(page); PageStamp = stamp;
                    return new HttpResponse { ContentType = "text/html; charset=utf-8", Body = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(page).Replace("__ML_PAGE_STAMP__", stamp)) };
                }
                return File.Exists(page) ? HttpResponse.File(page, "text/html; charset=utf-8")
                                         : new HttpResponse { ContentType = "text/plain; charset=utf-8", Body = System.Text.Encoding.UTF8.GetBytes("The editor page is missing. Copy tools/editor/index.html to BepInEx/MissionLab/web/index.html.") };
            }
            if (r.Path == "/api/map" || r.Path == "/api/geo")
            {
                string nav = _navFile;
                if (string.IsNullOrEmpty(nav)) return HttpResponse.Error(404, "no mission is running");
                return r.Path == "/api/map" ? HttpResponse.File(Path.Combine(_navDir, Path.GetFileName(nav)), "application/octet-stream")
                                            : HttpResponse.File(Path.Combine(_mapsDir, Path.GetFileNameWithoutExtension(nav) + ".mlgeo"), "application/octet-stream");
            }
            // Recordings, run records and map files by name, so a mission can be played back or reported after it has ended. Names only: no folders.
            if (r.Path == "/api/replays") return HttpResponse.Json(Replay.ListJson());
            foreach (var kind in new[] { ("/api/replay/", Replay.Dir, ".json", "application/json"), ("/api/runfile/", RunRecord.Dir, ".run.json", "application/json"),
                                         ("/api/mapfile/", _navDir, ".bytes", "application/octet-stream"), ("/api/geofile/", _mapsDir, ".mlgeo", "application/octet-stream"),
                                         ("/api/scenariofile/", Catalogue.Dir, ".json", "application/json") })
            {
                if (!r.Path.StartsWith(kind.Item1, StringComparison.Ordinal)) continue;
                string name = Uri.UnescapeDataString(r.Path.Substring(kind.Item1.Length));
                if (name.Length == 0 || name.Length > 120 || name != Path.GetFileName(name) || name.Contains("..") || !name.EndsWith(kind.Item3, StringComparison.OrdinalIgnoreCase)) return HttpResponse.Error(400, "not a file name");
                string full = Path.Combine(kind.Item2, name);
                return File.Exists(full) ? HttpResponse.File(full, kind.Item4) : HttpResponse.Error(404, "no such file");
            }
            if (!r.Path.StartsWith("/api/", StringComparison.Ordinal)) return HttpResponse.Error(404, "not found");
            bool change = r.Method != "GET";
            if (change && !r.Headers.ContainsKey("X-MissionLab")) return HttpResponse.Error(403, "not from the editor page");

            // Answered here, without the game's thread: it works when the game is busy, in the lobby and between missions.
            if (r.Path == "/api/status") return HttpResponse.Json(_status);
            { HttpResponse saved = Revisions.Handle(r); if (saved != null) return saved; }     // revisions: files only (ML-014)
            { HttpResponse listed = Catalogue.Handle(r); if (listed != null) return listed; }  // mission catalogue and saving a scenario: files only (ML-013)
            { HttpResponse rehearsed = Rehearsals.Handle(r); if (rehearsed != null) return rehearsed; }   // rehearsal and kept tests: Core only, no game objects (ML-016)
            { HttpResponse kept = Modules.Handle(r); if (kept != null) return kept; }         // encounter modules: files only (ML-019)
            { HttpResponse told = Debriefs.Handle(r); if (told != null) return told; }        // debriefs and unused content: recordings and run records only (ML-024)
            { HttpResponse packed = Packs.Handle(r); if (packed != null) return packed; }     // installing a scenario pack: files only (ML-026)
            { HttpResponse swept = ScatterPreview.Handle(r); if (swept != null) return swept; }   // scatter seeds tried: Core and the game's navmesh file only (ML-023)
            if (r.Path == "/api/options/validate")                                             // host options checked: Core only, no game objects (ML-022)
            {
                if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
                try { string answer = MissionLab.Core.Options.Answer(r.BodyText, out int status); return HttpResponse.Json(answer, status); }
                catch (Exception e) { return HttpResponse.Error(500, "the options could not be checked: " + e.Message); }
            }
            { HttpResponse made = Generate.Handle(r); if (made != null) return made; }       // quick mission: the game's own map file read, Core only (ML-022)
            if (r.Path.StartsWith("/api/command/", StringComparison.Ordinal) && !change)
            {
                string asked = CommandLedger.Clean(Uri.UnescapeDataString(r.Path.Substring("/api/command/".Length)));
                string known = asked != "" ? Ledger.JsonOf(asked) : null;
                return known != null ? HttpResponse.Json(known) : HttpResponse.Json("{\"command\":\"" + asked + "\",\"status\":\"unknown\"}", 404);
            }

            var job = new Job { Req = r };
            if (change)
            {
                job.Session = r.Headers.TryGetValue("X-MissionLab-Session", out string session) ? session : null;
                job.Command = r.Headers.TryGetValue("X-MissionLab-Command", out string number) ? CommandLedger.Clean(number) : "";
                // The same number twice is carried out once: the second time gets what the first one got.
                if (job.Command != "" && !Ledger.Begin(job.Command, job.Session, r.Path, 0f, out CommandLedger.Entry seen))
                    return !seen.Done ? HttpResponse.Json(Ledger.JsonOf(job.Command) ?? CommandLedger.Json(seen), 202)
                                      : new HttpResponse { Status = seen.Code, Body = System.Text.Encoding.UTF8.GetBytes(seen.Body) };
            }
            if (Volatile.Read(ref _waiting) >= MaxWaiting)
            {
                string busy = "{\"status\":\"" + CommandLedger.Expired + "\",\"command\":\"" + job.Command + "\",\"error\":\"the game has too much waiting; this was not carried out\"}";
                if (job.Command != "") Ledger.Finish(job.Command, CommandLedger.Expired, 429, busy, "");
                return HttpResponse.Json(busy, 429);
            }
            Interlocked.Increment(ref _waiting);
            Jobs.Enqueue(job);
            if (job.Done.Wait(4000)) return job.Res;
            if (Interlocked.CompareExchange(ref job.Phase, 2, 0) == 0)
            {
                // The game never got to it. It is struck off: it will not run later, and not in the next mission either.
                string late = "{\"status\":\"" + CommandLedger.Expired + "\",\"command\":\"" + job.Command + "\",\"error\":\"the game did not take this in time; it was not carried out and will not run later\"}";
                if (job.Command != "") Ledger.Finish(job.Command, CommandLedger.Expired, 503, late, "");
                return HttpResponse.Json(late, 503);
            }
            // The game has begun it and is not done: neither carried out for certain nor cancelled.
            return HttpResponse.Json("{\"status\":\"" + CommandLedger.Pending + "\",\"command\":\"" + job.Command + "\",\"error\":\"the game has begun this and has not finished; ask for its result, do not send it again\"}", 202);
        }

        /// <summary>Game thread, every frame.</summary>
        public static void Pump()
        {
            if (!Running) return;
            _navFile = GameMaster.LiveNavFile();
            if (UnityEngine.Time.frameCount % 240 == 0) PageStamp = Stamp(Path.Combine(_webDir, "index.html"));
            if (UnityEngine.Time.frameCount % 30 == 0)
            {
                string role = "menu";
                try { role = Mirror.NetworkServer.active ? "host" : Mirror.NetworkClient.active ? "client" : "menu"; } catch { }
                string game = ""; try { game = UnityEngine.Application.version ?? ""; } catch { }
                // The game mode as the game names it; unreadable in some menus, which leaves it empty rather than guessed.
                string mode = ""; bool pve = false;
                try
                {
                    OperationsManager om = OperationsManager.singleton;
                    if (om != null) { OperationsManager.GameMode m = om.CurrentGameMode; mode = m.ToString(); pve = PveRaid(m); }
                }
                catch { mode = ""; pve = false; }
                string map = _navFile ?? "";
                var sb = new StringBuilder(1024);
                sb.Append("{\"mission\":").Append(map.Length > 0 ? "true" : "false");
                sb.Append(",\"role\":\"").Append(role).Append("\",\"game\":\"").Append(Guard.Esc(game)).Append("\",\"testedGame\":\"").Append(TestedGame).Append('"');
                sb.Append(",\"mode\":\"").Append(Guard.Esc(mode)).Append("\",\"pve\":").Append(pve ? "true" : "false");
                sb.Append(",\"map\":\"").Append(Guard.Esc(Path.GetFileName(map))).Append("\",\"compat\":\"").Append(CompatFor(map, mode, pve, role)).Append('"');
                sb.Append(",\"port\":").Append(Port.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"library\":").Append(File.Exists(Path.Combine(_webDir, "three.min.js")) ? "true" : "false");
                sb.Append(GameMaster.Standing()).Append('}');
                _status = sb.ToString();
            }
            if (Director.Session != _sessionSeen)
            {
                // A mission ended or began: spawns still queued for the old one were dropped with it, so they will never be made.
                _sessionSeen = Director.Session;
                try { int lost = Ledger.Abandon("the mission ended before the game made it"); if (lost > 0) DirectorPlugin.L.LogInfo("browser changes: " + lost + " queued part(s) ended with the mission"); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("browser change results could not be closed: " + e.Message); }
            }
            for (int n = 0; n < 8 && Jobs.TryDequeue(out Job job); n++)
            {
                Interlocked.Decrement(ref _waiting);
                if (Interlocked.CompareExchange(ref job.Phase, 1, 0) != 0) continue;       // the page stopped waiting before its turn: it never runs
                LastRequestAt = UnityEngine.Time.unscaledTime;
                bool change = job.Req.Method != "GET";
                string status = CommandLedger.Applied; int said = GameMaster.SaidCount;
                if (change && job.Session != null && job.Session != Director.Session)
                {
                    // Made for another mission: nothing is done.
                    status = CommandLedger.Stale;
                    job.Res = HttpResponse.Json("{\"status\":\"stale\",\"command\":\"" + job.Command + "\",\"session\":\"" + Director.Session + "\",\"error\":\"this was meant for another mission; nothing was done\"}", 409);
                }
                else
                    try { job.Res = Answer(job.Req, job.Command); if (job.Res.Status >= 400) status = CommandLedger.Failed; }
                    catch (Exception e)
                    {
                        status = CommandLedger.Failed;
                        DirectorPlugin.L.LogWarning("browser request " + job.Req.Path + " failed: " + e);
                        job.Res = HttpResponse.Error(500, e.GetType().Name + ": " + e.Message);
                    }
                if (job.Command != "")
                    Ledger.Finish(job.Command, status, job.Res.Status, System.Text.Encoding.UTF8.GetString(job.Res.Body), GameMaster.SaidCount != said ? GameMaster.LastSaid : "");
                job.Done.Set();
            }
        }

        private static HttpResponse Answer(HttpRequest r, string command)
        {
            switch (r.Path)
            {
                case "/api/state": return HttpResponse.Json(GameMaster.StateJson());
                case "/api/markers": return r.Method == "POST" ? HttpResponse.Json(GameMaster.Reconcile(r.BodyText, command)) : HttpResponse.Error(400, "POST expected");
                case "/api/command": return r.Method == "POST" ? HttpResponse.Json(GameMaster.Command(r.BodyText, command)) : HttpResponse.Error(400, "POST expected");
                case "/api/runs":
                    return HttpResponse.Json("{\"runs\":" + RunRecord.ListJson(12) + ",\"repeat\":\"" + Director.RepeatFile + "\"}");
                case "/api/repeat":
                    if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
                    {
                        string file = "";
                        using (var doc = System.Text.Json.JsonDocument.Parse(r.BodyText))
                            if (doc.RootElement.TryGetProperty("file", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.String) file = f.GetString();
                        GameMaster.Say(Director.SetRepeat(file));
                        return HttpResponse.Json(GameMaster.StateJson());
                    }
                case "/api/recover":
                    if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
                    DirectorPlugin.TryAgainNow();
                    return HttpResponse.Json(GameMaster.StateJson());
                case "/api/choose":
                    if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
                    using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(r.BodyText)) Picker.Choose(doc.RootElement.GetProperty("value").GetString());
                    return HttpResponse.Json(GameMaster.StateJson());
                case "/api/options":
                    // The host's picks for the next mission; kept on the game's thread, where the mission's start reads them.
                    if (r.Method == "POST")
                        using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(r.BodyText)) Picker.SetPicks(doc.RootElement);
                    return HttpResponse.Json("{\"picks\":" + Picker.PicksJson(Picker.Picks()) + "}");
                default: return HttpResponse.Error(404, "not found");
            }
        }
    }
}
