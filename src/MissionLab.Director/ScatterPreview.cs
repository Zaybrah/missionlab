using System;
using System.IO;
using BepInEx;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// The editor's "Try seeds" for constrained scatter (ML-023):
    ///   POST /api/scatter/preview {"scenario": text, "seeds": [1, 2, ...], "players": n, "insertions": [[x, y, z], ...]}
    /// Answered on the browser link's worker thread with Core only (ScatterSweep: strict read, the dice, the default options,
    /// Scatter.Resolve on NavWalk, Completion.Check per seed): no game objects, so it works in the lobby and between missions.
    /// The walkable surface is the scenario's own navFile, read from the game's NavMeshData folder at the moment it is asked
    /// for; nothing of it is written anywhere. The answer says how many seeds were tried and that they are not every roll.
    /// </summary>
    internal static class ScatterPreview
    {
        private const string Route = "/api/scatter/preview";
        private static readonly object Gate = new object();      // one map in memory at a time; two sweeps at once share it
        private static string _loadedPath = "";
        private static DateTime _loadedAt;
        private static NavWalk _loaded;

        /// <summary>The answer for the scatter preview path, null for any other path.</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            if (r.Path != Route) return null;
            if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
            try
            {
                string answer = ScatterSweep.Answer(r.BodyText, WalkOf, out int status);
                return HttpResponse.Json(answer, status);
            }
            catch (Exception e) { return HttpResponse.Error(500, "the seeds could not be tried: " + e.Message); }
        }

        // The game's own navmesh file of that name, loaded once and kept until the file changes. Names only: no folders.
        private static IWalk WalkOf(string navFile, out string why)
        {
            why = "";
            string name = navFile ?? "";
            if (name.Length == 0 || name.Length > 120 || name != Path.GetFileName(name) || name.Contains("..") || !name.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
            {
                why = "the scenario's navFile '" + name + "' is not a map file name; no seeds were tried";
                return null;
            }
            string path = Path.Combine(Paths.GameDataPath, "NavMeshData", name);
            if (!File.Exists(path))
            {
                why = "the map file " + name + " is not in the game's NavMeshData folder; no seeds were tried";
                return null;
            }
            lock (Gate)
            {
                DateTime at = File.GetLastWriteTimeUtc(path);
                if (_loaded == null || _loadedPath != path || _loadedAt != at)
                {
                    try { _loaded = new NavWalk(NavMesh.Load(path)); _loadedPath = path; _loadedAt = at; }
                    catch (Exception e)
                    {
                        _loaded = null; _loadedPath = "";
                        why = "the map file " + name + " could not be read: " + e.Message;
                        return null;
                    }
                }
                return _loaded;
            }
        }
    }
}
