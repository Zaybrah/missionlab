"""Reader for the A* Pathfinding Project 5.x recast-graph caches that OPERATOR
ships in OPERATOR_Data/NavMeshData/*.bytes (zip: meta.json, graph0.json,
graph0_extra.binary, ...). Read-only; returns world-space walkable triangles."""
import json
import struct
import zipfile
from collections import defaultdict

import numpy as np


class NavMesh:
    def __init__(self, verts, tris, flags, settings):
        self.verts = verts          # (V,3) float metres, world space (x, y, z)
        self.tris = tris            # (T,3) int indices into verts
        self.flags = flags          # (T,) uint32 node flags
        self.settings = settings    # graph0.json
        a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
        self.centroids = (a + b + c) / 3.0
        cr = np.cross(b - a, c - a)
        self.areas = 0.5 * np.linalg.norm(cr, axis=1)
        self._components = None

    @property
    def components(self):
        """Connected-component id per triangle (shared edges)."""
        if self._components is None:
            edge_owner = defaultdict(list)
            for ti, (i, j, k) in enumerate(self.tris):
                for u, v in ((i, j), (j, k), (k, i)):
                    edge_owner[(min(u, v), max(u, v))].append(ti)
            parent = list(range(len(self.tris)))

            def find(x):
                while parent[x] != x:
                    parent[x] = parent[parent[x]]
                    x = parent[x]
                return x

            self.adj = defaultdict(set)
            for owners in edge_owner.values():
                for o in owners[1:]:
                    self.adj[owners[0]].add(o)
                    self.adj[o].add(owners[0])
                    ra, rb = find(owners[0]), find(o)
                    if ra != rb:
                        parent[ra] = rb
            self._components = np.array([find(i) for i in range(len(self.tris))])
        return self._components


def load(path):
    z = zipfile.ZipFile(path)
    meta = z.read("meta.json").decode()
    settings = json.loads(z.read("graph0.json"))
    d = z.read("graph0_extra.binary")
    off = 0

    def i32(n=1):
        nonlocal off
        vals = struct.unpack_from("<%di" % n, d, off)
        off += 4 * n
        return vals if n > 1 else vals[0]

    tx, tz = i32(2)
    if tx < 0:
        raise ValueError("graph has no tiles")
    vert_index = {}
    verts, tris, flags = [], [], []
    for z_ in range(tz):
        for x_ in range(tx):
            x, zc = i32(2)
            if x != x_ or zc != z_:
                continue  # slot belongs to a larger tile already read
            i32(2)  # tile width, depth (in tiles)
            nt = i32()
            t = np.frombuffer(d, "<i4", nt, off).reshape(-1, 3)
            off += 4 * nt
            nv = i32()
            v = np.frombuffer(d, "<i4", nv * 3, off).reshape(-1, 3) / 1000.0
            off += 12 * nv
            nvg = i32()
            off += 12 * nvg  # same verts in graph space
            is_cut = d[off]  # A* 5.3: one flag byte per tile (navmesh-cut state)
            off += 1
            if is_cut:  # pre-cut copy of the tile: tags, verts (tile space), tris
                n = i32(); off += 4 * n
                n = i32(); off += 12 * n
                n = i32(); off += 4 * n
            nn = i32()
            node = np.frombuffer(d, "<u4", nn * 5, off).reshape(-1, 5)
            off += 20 * nn  # penalty, flags, v0, v1, v2
            assert nn == len(t), (nn, len(t))
            # weld vertices across tiles by quantised position
            local = []
            for p in v:
                key = (round(p[0], 3), round(p[1], 3), round(p[2], 3))
                idx = vert_index.get(key)
                if idx is None:
                    idx = vert_index[key] = len(verts)
                    verts.append(p)
                local.append(idx)
            local = np.array(local, dtype=np.int64)
            if len(t):
                tris.append(local[t])
                flags.append(node[:, 1])
    assert off == len(d), "trailing bytes: %d of %d" % (off, len(d))
    nm = NavMesh(np.array(verts), np.concatenate(tris), np.concatenate(flags), settings)
    nm.meta = meta
    nm.tiles = (tx, tz)
    return nm


if __name__ == "__main__":
    import sys
    for p in sys.argv[1:]:
        nm = load(p)
        comp = nm.components
        ids, counts = np.unique(comp, return_counts=True)
        area_by = {i: nm.areas[comp == i].sum() for i in ids}
        big = max(area_by, key=area_by.get)
        lo, hi = nm.verts.min(0), nm.verts.max(0)
        print("%-22s tiles=%s tris=%6d verts=%6d area=%8.0f m2 comps=%4d main=%4.1f%%  x[%.0f,%.0f] y[%.1f,%.1f] z[%.0f,%.0f] walkable_flag=%.2f" % (
            p.split("/")[-1], nm.tiles, len(nm.tris), len(nm.verts), nm.areas.sum(), len(ids),
            100 * area_by[big] / nm.areas.sum(), lo[0], hi[0], lo[1], hi[1], lo[2], hi[2], (nm.flags & 1).mean()))
