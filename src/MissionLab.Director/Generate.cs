using System;
using System.IO;
using BepInEx;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// The quick mission form's layout (ML-022): POST /api/generate {navFile, enemies, mix, extras, seed}. Runs on the HTTP worker
    /// thread: it reads the game's own map file from OPERATOR_Data/NavMeshData (read only, never copied or written anywhere) and
    /// runs Core's generator and extras on it, so no game object is touched. The extras that need the running game (moved intel)
    /// are left out and the answer says so.
    /// </summary>
    internal static class Generate
    {
        private static readonly object Gate = new object();     // one layout at a time: the map's lookup grid is built lazily
        private static string _mapName = "";
        private static NavMesh _map;                             // the map file read last, kept for rerolls on the same map

        public static HttpResponse Handle(HttpRequest r)
        {
            if (r.Path != "/api/generate") return null;
            if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
            try
            {
                lock (Gate)
                {
                    string answer = QuickMission.Answer(r.BodyText, Load, out int status);
                    return HttpResponse.Json(answer, status);
                }
            }
            catch (Exception e)
            {
                DirectorPlugin.L.LogWarning("quick mission failed: " + e.Message);
                return HttpResponse.Error(500, "the layout could not be made: " + e.Message);
            }
        }

        // Under Gate. The name has passed QuickMission.NavFileOk: a file name only. Null when the game has no such file.
        private static NavMesh Load(string navFile)
        {
            if (_map != null && string.Equals(navFile, _mapName, StringComparison.OrdinalIgnoreCase)) return _map;
            string path = Path.Combine(Paths.GameDataPath, "NavMeshData", navFile);
            if (!File.Exists(path)) return null;
            NavMesh nav = NavMesh.Load(path);
            _map = nav; _mapName = navFile;
            return nav;
        }
    }
}
