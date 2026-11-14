using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MissionLab.Core
{
    public struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static float DistXZ(V3 a, V3 b) { float dx = a.X - b.X, dz = a.Z - b.Z; return (float)Math.Sqrt(dx * dx + dz * dz); }
        public override string ToString() => "(" + X.ToString("0.00") + ", " + Y.ToString("0.00") + ", " + Z.ToString("0.00") + ")";
    }

    /// <summary>
    /// The walkable surface of one map, read from the recast-graph caches the game
    /// ships in OPERATOR_Data/NavMeshData (A* Pathfinding Project 5.x, a zip per map).
    /// Read-only: nothing here touches the game.
    /// </summary>
    public sealed class NavMesh
    {
        // Per triangle: three corners (world metres), owning tile, neighbours.
        public int Count;
        public float[] Ax, Ay, Az, Bx, By, Bz, Cx, Cy, Cz;
        public int[] Tile;
        public float[] Area;
        public List<int>[] Neighbours;
        public int TilesX, TilesZ;
        public float MinX, MaxX, MinZ, MaxZ;
        public string SourceFile;
        /// <summary>Graph bounds as stored in the file (graph0.json); used to match a file to a live scene.</summary>
        public bool HasBounds;
        public V3 BoundsCenter, BoundsSize;

        private const float HashCell = 4f;
        private Dictionary<long, List<int>> _hash;

        private static long Key(int a, int b) => ((long)a << 32) ^ (uint)b;

        public static NavMesh Load(string path)
        {
            byte[] extra;
            string settings = null;
            using (var fs = File.OpenRead(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var entry = zip.GetEntry("graph0_extra.binary");
                if (entry == null) throw new InvalidDataException("no graph0_extra.binary in " + path);
                using (var s = entry.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    extra = ms.ToArray();
                }
                var js = zip.GetEntry("graph0.json");
                if (js != null)
                    using (var sr = new StreamReader(js.Open())) settings = sr.ReadToEnd();
            }
            var nm = Parse(extra);
            nm.SourceFile = Path.GetFileName(path);
            if (settings != null && TryVec(settings, "forcedBoundsCenter", out nm.BoundsCenter) && TryVec(settings, "forcedBoundsSize", out nm.BoundsSize))
                nm.HasBounds = true;
            return nm;
        }

        /// <summary>Reads only the graph bounds from a file: cheap, for finding the right file first.</summary>
        public static bool ReadBounds(string path, out V3 center, out V3 size)
        {
            center = default(V3); size = default(V3);
            using (var fs = File.OpenRead(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var js = zip.GetEntry("graph0.json");
                if (js == null) return false;
                string settings;
                using (var sr = new StreamReader(js.Open())) settings = sr.ReadToEnd();
                return TryVec(settings, "forcedBoundsCenter", out center) && TryVec(settings, "forcedBoundsSize", out size);
            }
        }

        // Finds  "key": { "x": 1, "y": 2, "z": 3 }  without a JSON library.
        private static bool TryVec(string json, string key, out V3 v)
        {
            v = default(V3);
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return false;
            int end = json.IndexOf('}', k);
            if (end < 0) return false;
            string body = json.Substring(k, end - k);
            return TryNum(body, "x", out v.X) && TryNum(body, "y", out v.Y) && TryNum(body, "z", out v.Z);
        }

        private static bool TryNum(string body, string axis, out float value)
        {
            value = 0f;
            int k = body.IndexOf("\"" + axis + "\"", StringComparison.Ordinal);
            if (k < 0) return false;
            int colon = body.IndexOf(':', k);
            if (colon < 0) return false;
            int i = colon + 1;
            while (i < body.Length && body[i] == ' ') i++;
            int j = i;
            while (j < body.Length && (char.IsDigit(body[j]) || body[j] == '-' || body[j] == '+' || body[j] == '.' || body[j] == 'e' || body[j] == 'E')) j++;
            return float.TryParse(body.Substring(i, j - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        public static NavMesh Parse(byte[] d)
        {
            var r = new BinaryReader(new MemoryStream(d));
            int tx = r.ReadInt32();
            if (tx < 0) throw new InvalidDataException("graph has no tiles");
            int tz = r.ReadInt32();

            var ax = new List<float>(); var ay = new List<float>(); var az = new List<float>();
            var bx = new List<float>(); var by = new List<float>(); var bz = new List<float>();
            var cx = new List<float>(); var cy = new List<float>(); var cz = new List<float>();
            var tile = new List<int>();
            var localIdx = new List<int>();   // 3 tile-local vertex indices per triangle

            for (int z = 0; z < tz; z++)
                for (int x = 0; x < tx; x++)
                {
                    int ttx = r.ReadInt32(), ttz = r.ReadInt32();
                    if (ttx != x || ttz != z) continue;          // slot covered by a larger tile
                    r.ReadInt32(); r.ReadInt32();                 // tile width, depth
                    int nt = r.ReadInt32();
                    var tris = new int[nt];
                    for (int i = 0; i < nt; i++) tris[i] = r.ReadInt32();
                    int nv = r.ReadInt32();
                    var v = new float[nv * 3];
                    for (int i = 0; i < nv * 3; i++) v[i] = r.ReadInt32() / 1000f;   // Int3, millimetres
                    int nvg = r.ReadInt32();
                    r.BaseStream.Seek(12L * nvg, SeekOrigin.Current);               // same verts in graph space
                    if (r.ReadByte() != 0)                                           // pre-cut copy of the tile
                    {
                        int n = r.ReadInt32(); r.BaseStream.Seek(4L * n, SeekOrigin.Current);
                        n = r.ReadInt32(); r.BaseStream.Seek(12L * n, SeekOrigin.Current);
                        n = r.ReadInt32(); r.BaseStream.Seek(4L * n, SeekOrigin.Current);
                    }
                    int nn = r.ReadInt32();
                    if (nn * 3 != nt) throw new InvalidDataException("tile " + x + "," + z + ": node count mismatch");
                    r.BaseStream.Seek(20L * nn, SeekOrigin.Current);                // penalty, flags, v0, v1, v2
                    int tileId = x + z * tx;
                    for (int i = 0; i < nt; i += 3)
                    {
                        int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                        ax.Add(v[a * 3]); ay.Add(v[a * 3 + 1]); az.Add(v[a * 3 + 2]);
                        bx.Add(v[b * 3]); by.Add(v[b * 3 + 1]); bz.Add(v[b * 3 + 2]);
                        cx.Add(v[c * 3]); cy.Add(v[c * 3 + 1]); cz.Add(v[c * 3 + 2]);
                        tile.Add(tileId);
                        localIdx.Add(a); localIdx.Add(b); localIdx.Add(c);
                    }
                }
            if (r.BaseStream.Position != r.BaseStream.Length)
                throw new InvalidDataException("unexpected trailing data (" + (r.BaseStream.Length - r.BaseStream.Position) + " bytes)");

            var nm = new NavMesh
            {
                Count = tile.Count, TilesX = tx, TilesZ = tz,
                Ax = ax.ToArray(), Ay = ay.ToArray(), Az = az.ToArray(),
                Bx = bx.ToArray(), By = by.ToArray(), Bz = bz.ToArray(),
                Cx = cx.ToArray(), Cy = cy.ToArray(), Cz = cz.ToArray(),
                Tile = tile.ToArray(),
            };
            nm.Finish(localIdx);
            return nm;
        }

        private void Finish(List<int> localIdx)
        {
            Area = new float[Count];
            Neighbours = new List<int>[Count];
            _hash = new Dictionary<long, List<int>>();
            MinX = MinZ = float.MaxValue; MaxX = MaxZ = float.MinValue;
            for (int i = 0; i < Count; i++)
            {
                Neighbours[i] = new List<int>(3);
                float ux = Bx[i] - Ax[i], uy = By[i] - Ay[i], uz = Bz[i] - Az[i];
                float vx = Cx[i] - Ax[i], vy = Cy[i] - Ay[i], vz = Cz[i] - Az[i];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                Area[i] = 0.5f * (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                float lox = Math.Min(Ax[i], Math.Min(Bx[i], Cx[i])), hix = Math.Max(Ax[i], Math.Max(Bx[i], Cx[i]));
                float loz = Math.Min(Az[i], Math.Min(Bz[i], Cz[i])), hiz = Math.Max(Az[i], Math.Max(Bz[i], Cz[i]));
                MinX = Math.Min(MinX, lox); MaxX = Math.Max(MaxX, hix);
                MinZ = Math.Min(MinZ, loz); MaxZ = Math.Max(MaxZ, hiz);
                for (int gx = (int)Math.Floor(lox / HashCell); gx <= (int)Math.Floor(hix / HashCell); gx++)
                    for (int gz = (int)Math.Floor(loz / HashCell); gz <= (int)Math.Floor(hiz / HashCell); gz++)
                    {
                        long k = Key(gx, gz);
                        if (!_hash.TryGetValue(k, out var list)) _hash[k] = list = new List<int>();
                        list.Add(i);
                    }
            }
            Connect(localIdx);
        }

        private struct BorderEdge { public int Tri; public float Lo, Hi, YLo, YHi; }

        private void Link(int a, int b)
        {
            if (a == b) return;
            if (!Neighbours[a].Contains(b)) Neighbours[a].Add(b);
            if (!Neighbours[b].Contains(a)) Neighbours[b].Add(a);
        }

        /// <summary>
        /// Inside a tile, triangles sharing two vertex indices are neighbours. Across
        /// tiles the vertices differ, so open edges lying on the same axis-aligned
        /// line are joined where they overlap at similar height (what the game's
        /// pathfinder does when it stitches tiles).
        /// </summary>
        private void Connect(List<int> localIdx)
        {
            int curTile = -1;
            var openEdges = new List<(int tri, int e)>();
            var seen = new Dictionary<long, (int tri, int e)>();
            for (int i = 0; i < Count; i++)
            {
                if (Tile[i] != curTile)
                {
                    foreach (var kv in seen) openEdges.Add(kv.Value);
                    seen.Clear();
                    curTile = Tile[i];
                }
                for (int e = 0; e < 3; e++)
                {
                    int v0 = localIdx[i * 3 + e], v1 = localIdx[i * 3 + (e + 1) % 3];
                    long k = Key(Math.Min(v0, v1), Math.Max(v0, v1));
                    if (seen.TryGetValue(k, out var other))
                    {
                        Link(i, other.tri);
                        seen.Remove(k);
                    }
                    else seen[k] = (i, e);
                }
            }
            foreach (var kv in seen) openEdges.Add(kv.Value);

            // Cross-tile stitching.
            var buckets = new Dictionary<long, List<BorderEdge>>();
            foreach (var (tri, e) in openEdges)
            {
                Corner(tri, e, out float x0, out float y0, out float z0);
                Corner(tri, (e + 1) % 3, out float x1, out float y1, out float z1);
                bool alongZ = Math.Abs(x0 - x1) < 0.002f;      // edge runs along Z at constant X
                bool alongX = Math.Abs(z0 - z1) < 0.002f;
                if (alongZ == alongX) continue;
                var be = new BorderEdge { Tri = tri };
                long key;
                if (alongZ)
                {
                    key = Key(0, (int)Math.Round(x0 * 100f));
                    if (z0 <= z1) { be.Lo = z0; be.Hi = z1; be.YLo = y0; be.YHi = y1; } else { be.Lo = z1; be.Hi = z0; be.YLo = y1; be.YHi = y0; }
                }
                else
                {
                    key = Key(1, (int)Math.Round(z0 * 100f));
                    if (x0 <= x1) { be.Lo = x0; be.Hi = x1; be.YLo = y0; be.YHi = y1; } else { be.Lo = x1; be.Hi = x0; be.YLo = y1; be.YHi = y0; }
                }
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<BorderEdge>();
                list.Add(be);
            }
            foreach (var list in buckets.Values)
            {
                if (list.Count < 2) continue;
                list.Sort((p, q) => p.Lo.CompareTo(q.Lo));
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count && list[j].Lo < list[i].Hi - 0.05f; j++)
                    {
                        var p = list[i]; var q = list[j];
                        if (Tile[p.Tri] == Tile[q.Tri]) continue;
                        float mid = 0.5f * (Math.Max(p.Lo, q.Lo) + Math.Min(p.Hi, q.Hi));
                        float yp = p.YLo + (p.YHi - p.YLo) * ((mid - p.Lo) / Math.Max(1e-4f, p.Hi - p.Lo));
                        float yq = q.YLo + (q.YHi - q.YLo) * ((mid - q.Lo) / Math.Max(1e-4f, q.Hi - q.Lo));
                        if (Math.Abs(yp - yq) < 0.6f) Link(p.Tri, q.Tri);
                    }
            }
        }

        private void Corner(int tri, int c, out float x, out float y, out float z)
        {
            if (c == 0) { x = Ax[tri]; y = Ay[tri]; z = Az[tri]; }
            else if (c == 1) { x = Bx[tri]; y = By[tri]; z = Bz[tri]; }
            else { x = Cx[tri]; y = Cy[tri]; z = Cz[tri]; }
        }

        /// <summary>
        /// Triangle under (x, z) whose surface height is closest to y, within tol.
        /// Returns -1 if the point is off the mesh at that height.
        /// </summary>
        public int TriAt(float x, float z, float y, float tol, out float height, bool[] mask = null)
        {
            height = 0f;
            if (!_hash.TryGetValue(Key((int)Math.Floor(x / HashCell), (int)Math.Floor(z / HashCell)), out var list)) return -1;
            int best = -1; float bestDy = tol;
            for (int n = 0; n < list.Count; n++)
            {
                int i = list[n];
                if (mask != null && !mask[i]) continue;
                float d = (Bz[i] - Cz[i]) * (Ax[i] - Cx[i]) + (Cx[i] - Bx[i]) * (Az[i] - Cz[i]);
                if (Math.Abs(d) < 1e-9f) continue;
                float l1 = ((Bz[i] - Cz[i]) * (x - Cx[i]) + (Cx[i] - Bx[i]) * (z - Cz[i])) / d;
                float l2 = ((Cz[i] - Az[i]) * (x - Cx[i]) + (Ax[i] - Cx[i]) * (z - Cz[i])) / d;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float h = l1 * Ay[i] + l2 * By[i] + l3 * Cy[i];
                float dy = Math.Abs(h - y);
                if (dy <= bestDy) { bestDy = dy; best = i; height = h; }
            }
            return best;
        }

        /// <summary>How many distinct walkable levels exist above/below (x, z).</summary>
        public int LevelsAt(float x, float z)
        {
            if (!_hash.TryGetValue(Key((int)Math.Floor(x / HashCell), (int)Math.Floor(z / HashCell)), out var list)) return 0;
            var hs = new List<float>();
            for (int n = 0; n < list.Count; n++)
            {
                int i = list[n];
                float d = (Bz[i] - Cz[i]) * (Ax[i] - Cx[i]) + (Cx[i] - Bx[i]) * (Az[i] - Cz[i]);
                if (Math.Abs(d) < 1e-9f) continue;
                float l1 = ((Bz[i] - Cz[i]) * (x - Cx[i]) + (Cx[i] - Bx[i]) * (z - Cz[i])) / d;
                float l2 = ((Cz[i] - Az[i]) * (x - Cx[i]) + (Ax[i] - Cx[i]) * (z - Cz[i])) / d;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float h = l1 * Ay[i] + l2 * By[i] + l3 * Cy[i];
                bool dup = false;
                for (int k = 0; k < hs.Count; k++) if (Math.Abs(hs[k] - h) < 1.5f) { dup = true; break; }
                if (!dup) hs.Add(h);
            }
            return hs.Count;
        }

        /// <summary>All triangles connected to the seed triangles.</summary>
        public bool[] Reachable(IEnumerable<int> seedTris)
        {
            var mark = new bool[Count];
            var stack = new Stack<int>();
            foreach (int s in seedTris)
                if (s >= 0 && !mark[s]) { mark[s] = true; stack.Push(s); }
            while (stack.Count > 0)
            {
                int t = stack.Pop();
                var nb = Neighbours[t];
                for (int i = 0; i < nb.Count; i++)
                    if (!mark[nb[i]]) { mark[nb[i]] = true; stack.Push(nb[i]); }
            }
            return mark;
        }

        /// <summary>
        /// Walk in a straight line across the mesh from p and return how far you get
        /// before leaving walkable ground (capped at maxDist). The end point is returned too.
        /// </summary>
        public float Walk(V3 p, double angle, float maxDist, bool[] mask, out V3 end, float step = 0.3f)
        {
            float dx = (float)Math.Cos(angle), dz = (float)Math.Sin(angle);
            float d = 0f, y = p.Y;
            end = p;
            while (d < maxDist)
            {
                float nd = d + step;
                float x = p.X + dx * nd, z = p.Z + dz * nd;
                if (TriAt(x, z, y, 0.6f, out float h, mask) < 0) break;
                d = nd; y = h;
                end = new V3(x, h, z);
            }
            return d;
        }

        /// <summary>Uniform random point inside triangle i.</summary>
        public V3 PointIn(int i, Rng rng)
        {
            double r1 = rng.NextDouble(), r2 = rng.NextDouble();
            if (r1 + r2 > 1) { r1 = 1 - r1; r2 = 1 - r2; }
            return new V3(
                (float)(Ax[i] + r1 * (Bx[i] - Ax[i]) + r2 * (Cx[i] - Ax[i])),
                (float)(Ay[i] + r1 * (By[i] - Ay[i]) + r2 * (Cy[i] - Ay[i])),
                (float)(Az[i] + r1 * (Bz[i] - Az[i]) + r2 * (Cz[i] - Az[i])));
        }
    }
}
