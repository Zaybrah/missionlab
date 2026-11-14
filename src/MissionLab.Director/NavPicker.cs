using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>What the running game reports about its own navigation graph.</summary>
    internal sealed class LiveGraph
    {
        public V3 Center, Size;
        public int TilesX, TilesZ;
        public int Nodes = -1;
    }

    /// <summary>
    /// Finds which shipped navmesh file describes the scene that is loading.
    /// A file is accepted only if its stored bounds equal the live graph's bounds
    /// (several maps share similar ground, so "the spawn points stand on it" alone
    /// is not proof). The scene's own spawn points are then used as a second check.
    /// </summary>
    internal static class NavPicker
    {
        private static readonly Dictionary<string, NavMesh> Cache = new Dictionary<string, NavMesh>();

        public static NavMesh Pick(string scene, List<V3> authored, LiveGraph live, out string info)
        {
            if (Cache.TryGetValue(scene, out var cached)) { info = cached.SourceFile + " (cached)"; return cached; }

            string dir = Path.Combine(Paths.GameDataPath, "NavMeshData");
            if (!Directory.Exists(dir)) { info = "folder not found: " + dir; return null; }
            if (live == null) { info = "the game's navigation graph could not be read, so the map file cannot be verified"; return null; }

            NavMesh best = null; float bestScore = -1f; int bestDiff = int.MaxValue; int matched = 0, files = 0;
            foreach (string path in Directory.GetFiles(dir, "*.bytes"))
            {
                files++;
                try
                {
                    if (!NavMesh.ReadBounds(path, out V3 c, out V3 s)) continue;
                    if (!Close(c, live.Center) || !Close(s, live.Size)) continue;
                    NavMesh nav = NavMesh.Load(path);
                    if (nav.TilesX != live.TilesX || nav.TilesZ != live.TilesZ) continue;
                    matched++;
                    int on = 0;
                    foreach (var p in authored)
                        if (nav.TriAt(p.X, p.Z, p.Y, 1.5f, out _) >= 0) on++;
                    float score = authored.Count == 0 ? 0f : on / (float)authored.Count;
                    int diff = live.Nodes > 0 ? Math.Abs(nav.Count - live.Nodes) : 0;
                    // Same bounds can exist in two versions of a map: prefer the one whose
                    // triangle count is closest to the live graph, then the better spawn fit.
                    if (diff < bestDiff || (diff == bestDiff && score > bestScore)) { best = nav; bestScore = score; bestDiff = diff; }
                }
                catch (Exception e)
                {
                    DirectorPlugin.L.LogWarning("could not read " + Path.GetFileName(path) + ": " + e.Message);
                }
            }
            if (best == null) { info = "none of " + files + " navmesh files has this scene's graph bounds"; return null; }
            if (authored.Count >= 5 && bestScore < 0.5f)
            {
                info = best.SourceFile + " has the right bounds but only " + Math.Round(bestScore * 100) + "% of the scene's spawn points stand on it";
                return null;
            }
            Cache[scene] = best;
            info = best.SourceFile + " (bounds match, " + matched + " candidate file(s), triangle difference " + (live.Nodes > 0 ? bestDiff.ToString() : "n/a") +
                   ", " + Math.Round(bestScore * 100) + "% of authored points on mesh)";
            return best;
        }

        private static bool Close(V3 a, V3 b) =>
            Math.Abs(a.X - b.X) < 0.05f && Math.Abs(a.Y - b.Y) < 0.05f && Math.Abs(a.Z - b.Z) < 0.05f;
    }
}
