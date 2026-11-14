using System;
using System.Collections.Concurrent;
using System.IO;
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
        private sealed class Job { public HttpRequest Req; public HttpResponse Res; public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false); }

        private static readonly ConcurrentQueue<Job> Jobs = new ConcurrentQueue<Job>();
        private static MiniHttp _web;
        private static string _webDir, _navDir, _mapsDir;
        private static volatile string _navFile;        // map file of the mission in progress
        internal static int Port;
        internal static bool Running;
        internal static float LastRequestAt = -100f;    // game time of the last request from the browser
        internal static volatile string PageStamp = "";  // changes whenever the editor page on disk changes

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
            catch (Exception e) { DirectorPlugin.L.LogWarning("the browser link could not start on port " + port + " (" + e.Message + "). Change [LiveLink] Port in the settings if another program uses it."); }
        }

        // Worker thread. No game objects here.
        private static HttpResponse Handle(HttpRequest r)
        {
            // Only the page this server hands out may talk to it; another website open in the browser may not.
            if (r.Headers.TryGetValue("Origin", out string origin) && origin != "http://localhost:" + Port && origin != "http://127.0.0.1:" + Port)
                return HttpResponse.Error(403, "this address only answers its own page");
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
            if (!r.Path.StartsWith("/api/", StringComparison.Ordinal)) return HttpResponse.Error(404, "not found");
            if (r.Method != "GET" && !r.Headers.ContainsKey("X-MissionLab")) return HttpResponse.Error(403, "not from the editor page");

            var job = new Job { Req = r };
            Jobs.Enqueue(job);
            return job.Done.Wait(4000) ? job.Res : HttpResponse.Error(503, "the game did not answer in time");
        }

        /// <summary>Game thread, every frame.</summary>
        public static void Pump()
        {
            if (!Running) return;
            _navFile = GameMaster.LiveNavFile();
            if (UnityEngine.Time.frameCount % 240 == 0) PageStamp = Stamp(Path.Combine(_webDir, "index.html"));
            for (int n = 0; n < 8 && Jobs.TryDequeue(out Job job); n++)
            {
                LastRequestAt = UnityEngine.Time.unscaledTime;
                try { job.Res = Answer(job.Req); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("browser request " + job.Req.Path + " failed: " + e); job.Res = HttpResponse.Error(500, e.GetType().Name + ": " + e.Message); }
                job.Done.Set();
            }
        }

        private static HttpResponse Answer(HttpRequest r)
        {
            switch (r.Path)
            {
                case "/api/state": return HttpResponse.Json(GameMaster.StateJson());
                case "/api/markers": return r.Method == "POST" ? HttpResponse.Json(GameMaster.Reconcile(r.BodyText)) : HttpResponse.Error(400, "POST expected");
                case "/api/command": return r.Method == "POST" ? HttpResponse.Json(GameMaster.Command(r.BodyText)) : HttpResponse.Error(400, "POST expected");
                default: return HttpResponse.Error(404, "not found");
            }
        }
    }
}
