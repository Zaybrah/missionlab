using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace MissionLab
{
    /// <summary>
    /// Writes the solid geometry of the map that is loaded right now (everything with a
    /// collider: ground, walls, stairs, furniture) to a file in the player's own game
    /// folder, so the scenario editor can show the map in 3D. Nothing is uploaded and the
    /// file is not meant to be shared: it is derived from the game's own assets.
    ///
    /// File: "MLGEO1\n\0", int32 vertexCount, int32 indexCount, float32 x,y,z per vertex
    /// (world metres), uint32 per index (triangles).
    /// </summary>
    internal static class MapExport
    {
        private sealed class MeshData { public Vector3[] V; public int[] T; }
        private const int MaxTriangles = 9000000;

        public static string Run(string path)
        {
            var sw = Stopwatch.StartNew();
            var cache = new Dictionary<IntPtr, MeshData>();
            var pos = new List<float>(1 << 20);
            var idx = new List<int>(1 << 21);
            int meshColliders = 0, boxes = 0, others = 0, skipped = 0, readable = 0, gpu = 0, unreadable = 0, gpuErrors = 0;
            string firstGpuError = null;
            bool gpuAllowed = true;

            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Collider>(), false);
            int total = found == null ? 0 : found.Length;
            for (int i = 0; i < total && idx.Count / 3 < MaxTriangles; i++)
            {
                Collider c;
                try { c = found[i] == null ? null : found[i].TryCast<Collider>(); } catch { c = null; }
                if (c == null) continue;
                try
                {
                    if (!c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) { skipped++; continue; }
                    if (c.GetComponentInParent<Health>() != null) { skipped++; continue; }       // players and enemies
                    Matrix4x4 m = c.transform.localToWorldMatrix;

                    MeshCollider mc = c.TryCast<MeshCollider>();
                    if (mc != null)
                    {
                        Mesh mesh = mc.sharedMesh;
                        if (mesh == null) { skipped++; continue; }
                        if (!cache.TryGetValue(mesh.Pointer, out MeshData data))
                        {
                            data = null;
                            if (mesh.isReadable) { data = ReadCpu(mesh); if (data != null) readable++; }
                            else if (gpuAllowed)
                            {
                                try { data = ReadGpu(mesh); if (data != null) gpu++; }
                                catch (Exception e)
                                {
                                    gpuErrors++;
                                    if (firstGpuError == null) firstGpuError = e.GetType().Name + ": " + e.Message;
                                    if (gpuErrors >= 5 && gpu == 0) gpuAllowed = false;     // this route does not work here: stop trying
                                }
                            }
                            if (data == null) unreadable++;
                            cache[mesh.Pointer] = data;
                        }
                        if (data == null) { AddBox(pos, idx, c.bounds); continue; }           // at least show where it is
                        meshColliders++;
                        int at = pos.Count / 3;
                        for (int k = 0; k < data.V.Length; k++) { Vector3 w = m.MultiplyPoint3x4(data.V[k]); pos.Add(w.x); pos.Add(w.y); pos.Add(w.z); }
                        for (int k = 0; k < data.T.Length; k++) idx.Add(at + data.T[k]);
                        continue;
                    }

                    BoxCollider bc = c.TryCast<BoxCollider>();
                    if (bc != null)
                    {
                        boxes++;
                        Vector3 ce = bc.center, h = bc.size * 0.5f;
                        int at = pos.Count / 3;
                        for (int k = 0; k < 8; k++)
                        {
                            Vector3 w = m.MultiplyPoint3x4(new Vector3(ce.x + ((k & 1) == 0 ? -h.x : h.x), ce.y + ((k & 2) == 0 ? -h.y : h.y), ce.z + ((k & 4) == 0 ? -h.z : h.z)));
                            pos.Add(w.x); pos.Add(w.y); pos.Add(w.z);
                        }
                        foreach (int q in BoxTris) idx.Add(at + q);
                        continue;
                    }

                    others++;                                                                 // capsules, spheres, wheels: their bounding box
                    AddBox(pos, idx, c.bounds);
                }
                catch (Exception) { skipped++; }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(Encoding.ASCII.GetBytes("MLGEO1\n\0"));
                w.Write(pos.Count / 3);
                w.Write(idx.Count);
                var fb = new byte[pos.Count * 4];
                Buffer.BlockCopy(pos.ToArray(), 0, fb, 0, fb.Length);
                w.Write(fb);
                var ib = new byte[idx.Count * 4];
                Buffer.BlockCopy(idx.ToArray(), 0, ib, 0, ib.Length);
                w.Write(ib);
            }
            sw.Stop();
            long bytes = new FileInfo(path).Length;
            return (idx.Count / 3) + " triangles from " + total + " colliders (" + meshColliders + " mesh shapes, " + boxes + " boxes, " + others + " other, " + skipped + " skipped) | " +
                   "distinct meshes: " + readable + " readable, " + gpu + " read from the graphics card, " + unreadable + " unreadable" +
                   (firstGpuError != null ? " (" + gpuErrors + " errors, first: " + firstGpuError + ")" : "") +
                   " | " + (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB in " + sw.ElapsedMilliseconds + " ms";
        }

        private static readonly int[] BoxTris = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };

        private static void AddBox(List<float> pos, List<int> idx, Bounds b)
        {
            Vector3 lo = b.min, hi = b.max;
            if ((hi - lo).sqrMagnitude < 1e-6f) return;
            int at = pos.Count / 3;
            for (int k = 0; k < 8; k++) { pos.Add((k & 1) == 0 ? lo.x : hi.x); pos.Add((k & 2) == 0 ? lo.y : hi.y); pos.Add((k & 4) == 0 ? lo.z : hi.z); }
            foreach (int q in BoxTris) idx.Add(at + q);
        }

        private static MeshData ReadCpu(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            if (v == null || t == null || v.Length == 0 || t.Length < 3) return null;
            return new MeshData { V = v, T = t };
        }

        /// <summary>
        /// Most meshes in a built game keep no copy in main memory; their vertices live on the
        /// graphics card only. Read the vertex and index buffers back from there.
        /// </summary>
        private static MeshData ReadGpu(Mesh mesh)
        {
            int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
            int offset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            VertexAttributeFormat format = mesh.GetVertexAttributeFormat(VertexAttribute.Position);
            if (stream < 0 || (format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16)) return null;

            byte[] vb; int stride, count;
            GraphicsBuffer vbuf = mesh.GetVertexBuffer(stream);
            try
            {
                stride = vbuf.stride; count = vbuf.count;
                if (stride <= 0 || count <= 0 || (long)stride * count > 400000000L) return null;
                var raw = new Il2CppStructArray<byte>((long)stride * count);
                vbuf.GetData(raw.Cast<Il2CppSystem.Array>());
                vb = raw;
            }
            finally { vbuf.Dispose(); }

            var v = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int o = i * stride + offset;
                v[i] = format == VertexAttributeFormat.Float32
                    ? new Vector3(BitConverter.ToSingle(vb, o), BitConverter.ToSingle(vb, o + 4), BitConverter.ToSingle(vb, o + 8))
                    : new Vector3(Mathf.HalfToFloat(BitConverter.ToUInt16(vb, o)), Mathf.HalfToFloat(BitConverter.ToUInt16(vb, o + 2)), Mathf.HalfToFloat(BitConverter.ToUInt16(vb, o + 4)));
            }

            byte[] ib; int istride, icount;
            GraphicsBuffer ibuf = mesh.GetIndexBuffer();
            try
            {
                istride = ibuf.stride; icount = ibuf.count;
                if ((istride != 2 && istride != 4) || icount < 3) return null;
                var raw = new Il2CppStructArray<byte>((long)istride * icount);
                ibuf.GetData(raw.Cast<Il2CppSystem.Array>());
                ib = raw;
            }
            finally { ibuf.Dispose(); }

            var t = new List<int>(icount);
            int subs = mesh.subMeshCount;
            for (int s = 0; s < subs; s++)
            {
                int start = (int)mesh.GetIndexStart(s), n = (int)mesh.GetIndexCount(s), baseVertex = (int)mesh.GetBaseVertex(s);
                n -= n % 3;
                for (int k = start; k < start + n && k < icount; k++)
                {
                    int ix = (istride == 2 ? BitConverter.ToUInt16(ib, k * 2) : (int)BitConverter.ToUInt32(ib, k * 4)) + baseVertex;
                    if (ix < 0 || ix >= count) return null;
                    t.Add(ix);
                }
            }
            return t.Count < 3 ? null : new MeshData { V = v, T = t.ToArray() };
        }
    }
}
