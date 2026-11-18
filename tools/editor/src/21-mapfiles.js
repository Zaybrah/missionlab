// ------------------------------------------------------------------ map files
// The game ships each map's walkable surface as a zip (A* Pathfinding Project cache).
async function inflateRaw(bytes) {
  const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}
async function unzip(buffer) {
  const u8 = new Uint8Array(buffer), dv = new DataView(buffer), out = new Map();
  let eocd = -1;
  for (let i = u8.length - 22; i >= Math.max(0, u8.length - 66000); i--) if (dv.getUint32(i, true) === 0x06054b50) { eocd = i; break; }
  if (eocd < 0) throw new Error('This is not a map file from NavMeshData.');
  const count = dv.getUint16(eocd + 10, true);
  let p = dv.getUint32(eocd + 16, true);
  for (let n = 0; n < count; n++) {
    if (dv.getUint32(p, true) !== 0x02014b50) break;
    const method = dv.getUint16(p + 10, true), csize = dv.getUint32(p + 20, true);
    const nameLen = dv.getUint16(p + 28, true), extraLen = dv.getUint16(p + 30, true), commentLen = dv.getUint16(p + 32, true);
    const local = dv.getUint32(p + 42, true);
    const name = new TextDecoder().decode(u8.subarray(p + 46, p + 46 + nameLen));
    const start = local + 30 + dv.getUint16(local + 26, true) + dv.getUint16(local + 28, true);
    const data = u8.subarray(start, start + csize);
    out.set(name, method === 0 ? data.slice() : await inflateRaw(data));
    p += 46 + nameLen + extraLen + commentLen;
  }
  return out;
}
// graph0_extra.binary, per tile: x, z, width, depth, tris[], verts[] (mm), verts in graph space,
// one flag byte (if set: a pre-cut copy to skip), then 20 bytes per triangle node.
function parseTiles(bytes) {
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let off = 0;
  const i32 = () => { const v = dv.getInt32(off, true); off += 4; return v; };
  const tilesX = i32(), tilesZ = i32();
  if (tilesX <= 0 || tilesZ <= 0 || tilesX > 4096 || tilesZ > 4096) throw new Error('This map file has no walkable surface in it.');
  const parts = []; let total = 0;
  for (let z = 0; z < tilesZ; z++) for (let x = 0; x < tilesX; x++) {
    const tx = i32(), tz = i32();
    if (tx !== x || tz !== z) continue;                  // slot belongs to a larger tile already read
    off += 8;
    const nIdx = i32(), idxAt = off; off += 4 * nIdx;
    const nVerts = i32(), vertAt = off; off += 12 * nVerts;
    const skip = size => { const count = i32(); off += size * count; };   // read the count first, then jump
    skip(12);
    const cut = dv.getUint8(off); off += 1;
    if (cut) { skip(4); skip(12); skip(4); }
    skip(20);
    const t = new Float32Array(nIdx * 3);
    for (let k = 0; k < nIdx; k++) {
      const v = vertAt + 12 * dv.getInt32(idxAt + 4 * k, true);
      t[k * 3] = dv.getInt32(v, true) / 1000; t[k * 3 + 1] = dv.getInt32(v + 4, true) / 1000; t[k * 3 + 2] = dv.getInt32(v + 8, true) / 1000;
    }
    parts.push(t); total += t.length;
  }
  if (off !== bytes.byteLength) throw new Error('This map file has a layout this editor does not know (game update?).');
  const tris = new Float32Array(total); let at = 0;
  for (const t of parts) { tris.set(t, at); at += t.length; }
  return tris;
}
// The map file's fingerprint, as ScenarioFile.MapPrint has it: FNV-1a 64 over the raw bytes. The 64-bit sum is carried in
// four 16-bit pieces because BigInt per byte is too slow for a map file of many megabytes; the result is the same.
function mapPrintOf(buffer) {
  const bytes = new Uint8Array(buffer);
  let h0 = 0x2325, h1 = 0x8422, h2 = 0x9ce4, h3 = 0xcbf2;
  for (let i = 0; i < bytes.length; i++) {
    h0 ^= bytes[i];
    // times 0x100000001b3: each piece by 0x1b3, plus the pieces two lower shifted by 8 bits
    const a0 = h0 * 0x1b3, a1 = h1 * 0x1b3 + (a0 >>> 16), a2 = h2 * 0x1b3 + h0 * 0x100 + (a1 >>> 16), a3 = h3 * 0x1b3 + h1 * 0x100 + (a2 >>> 16);
    h0 = a0 & 0xffff; h1 = a1 & 0xffff; h2 = a2 & 0xffff; h3 = a3 & 0xffff;
  }
  const hex = v => v.toString(16).padStart(4, '0');
  return 'fnv1a64:' + hex(h3) + hex(h2) + hex(h1) + hex(h0);
}
async function readMapFile(buffer) {
  const files = await unzip(buffer);
  const extra = files.get('graph0_extra.binary');
  if (!extra) throw new Error('This zip has no walkable surface in it (graph0_extra.binary is missing).');
  return parseTiles(extra);
}

// tris: 9 floats per triangle (x, y, z of each corner), world metres.
function makeMap(tris, name, isExample) {
  const n = tris.length / 9, minY = new Float32Array(n), avgY = new Float32Array(n);
  const b = { minX: Infinity, maxX: -Infinity, minY: Infinity, maxY: -Infinity, minZ: Infinity, maxZ: -Infinity };
  for (let i = 0; i < n; i++) {
    const o = i * 9, y0 = tris[o + 1], y1 = tris[o + 4], y2 = tris[o + 7];
    minY[i] = Math.min(y0, y1, y2); avgY[i] = (y0 + y1 + y2) / 3;
    for (let k = 0; k < 9; k += 3) {
      b.minX = Math.min(b.minX, tris[o + k]); b.maxX = Math.max(b.maxX, tris[o + k]);
      b.minZ = Math.min(b.minZ, tris[o + k + 2]); b.maxZ = Math.max(b.maxZ, tris[o + k + 2]);
    }
    b.minY = Math.min(b.minY, minY[i]); b.maxY = Math.max(b.maxY, y0, y1, y2);
  }
  const order = Array.from({ length: n }, (_, i) => i).sort((p, q) => avgY[p] - avgY[q]);
  const cell = Math.max(4, Math.max(b.maxX - b.minX, b.maxZ - b.minZ) / 220);
  const gw = Math.floor((b.maxX - b.minX) / cell) + 1, gh = Math.floor((b.maxZ - b.minZ) / cell) + 1;
  const cells = new Array(gw * gh);
  for (let i = 0; i < n; i++) {
    const o = i * 9;
    const x0 = Math.floor((Math.min(tris[o], tris[o + 3], tris[o + 6]) - b.minX) / cell), x1 = Math.floor((Math.max(tris[o], tris[o + 3], tris[o + 6]) - b.minX) / cell);
    const z0 = Math.floor((Math.min(tris[o + 2], tris[o + 5], tris[o + 8]) - b.minZ) / cell), z1 = Math.floor((Math.max(tris[o + 2], tris[o + 5], tris[o + 8]) - b.minZ) / cell);
    for (let z = z0; z <= z1; z++) for (let x = x0; x <= x1; x++) (cells[z * gw + x] || (cells[z * gw + x] = [])).push(i);
  }
  const m = { tris, n, minY, avgY, order, bounds: b, cell, gw, gh, cells, name, isExample: !!isExample };
  m.borders = findBorders(m);
  return m;
}
// Edges with walkable ground on one side only: walls, fences, furniture, ledges. Nobody gets past these.
function findBorders(m) {
  const t = m.tris, seen = new Map(), q = v => Math.round(v * 1000);
  for (let i = 0; i < m.n; i++) for (let e = 0; e < 3; e++) {
    const a = i * 9 + e * 3, b = i * 9 + ((e + 1) % 3) * 3;
    const ka = q(t[a]) + ',' + q(t[a + 1]) + ',' + q(t[a + 2]), kb = q(t[b]) + ',' + q(t[b + 1]) + ',' + q(t[b + 2]);
    const key = ka < kb ? ka + '|' + kb : kb + '|' + ka, hit = seen.get(key);
    if (hit) hit.n++; else seen.set(key, { n: 1, a, b, c: i * 9 + ((e + 2) % 3) * 3 });
  }
  const out = [];
  for (const e of seen.values()) {
    if (e.n !== 1) continue;
    const ax = t[e.a], az = t[e.a + 2], bx = t[e.b], bz = t[e.b + 2], y = (t[e.a + 1] + t[e.b + 1]) / 2;
    let nx = bz - az, nz = ax - bx; const len = Math.hypot(nx, nz);
    if (len < 0.02) continue;
    nx /= len; nz /= len;
    if (nx * (t[e.c] - ax) + nz * (t[e.c + 2] - az) > 0) { nx = -nx; nz = -nz; }      // point away from the triangle
    // The mesh is stored in square tiles; where two tiles meet, the ground simply continues.
    if (levelsIn(m, (ax + bx) / 2 + nx * 0.06, (az + bz) / 2 + nz * 0.06).some(v => Math.abs(v - y) < 0.6)) continue;
    out.push(ax, az, bx, bz, Math.min(t[e.a + 1], t[e.b + 1]));
  }
  return new Float32Array(out);
}
// Heights of every walkable level at (x, z).
const levelsAt = (x, z) => levelsIn(state.map, x, z);
function levelsIn(m, x, z) {
  const out = [];
  if (!m) return out;
  const cx = Math.floor((x - m.bounds.minX) / m.cell), cz = Math.floor((z - m.bounds.minZ) / m.cell);
  if (cx < 0 || cz < 0 || cx >= m.gw || cz >= m.gh) return out;
  const list = m.cells[cz * m.gw + cx], t = m.tris;
  if (!list) return out;
  for (const i of list) {
    const o = i * 9, x0 = t[o], z0 = t[o + 2], x1 = t[o + 3], z1 = t[o + 5], x2 = t[o + 6], z2 = t[o + 8];
    const d = (z1 - z2) * (x0 - x2) + (x2 - x1) * (z0 - z2);
    if (Math.abs(d) < 1e-9) continue;
    const a = ((z1 - z2) * (x - x2) + (x2 - x1) * (z - z2)) / d, b = ((z2 - z0) * (x - x2) + (x0 - x2) * (z - z2)) / d, c = 1 - a - b;
    if (a < -1e-4 || b < -1e-4 || c < -1e-4) continue;
    out.push(a * t[o + 1] + b * t[o + 4] + c * t[o + 7]);
  }
  return out;
}
// The highest visible floor under the cursor, or null where nobody can walk.
function groundAt(x, z) {
  let best = null;
  for (const y of levelsAt(x, z)) if (y <= state.ceiling + 0.4 && (best === null || y > best)) best = y;
  return best;
}
const onGround = p => !state.map || levelsAt(p[0], p[2]).some(y => Math.abs(y - p[1]) <= 1.0);

// A small invented compound so the editor can be tried before opening a real map.
function exampleMap() {
  const t = [];
  const plate = (x0, z0, x1, z1, y) => {
    const nx = Math.max(1, Math.round((x1 - x0) / 8)), nz = Math.max(1, Math.round((z1 - z0) / 8));
    for (let i = 0; i < nx; i++) for (let k = 0; k < nz; k++) {
      const a = x0 + (x1 - x0) * i / nx, b = x0 + (x1 - x0) * (i + 1) / nx, c = z0 + (z1 - z0) * k / nz, d = z0 + (z1 - z0) * (k + 1) / nz;
      t.push(a, y, c, b, y, c, b, y, d, a, y, c, b, y, d, a, y, d);
    }
  };
  plate(-64, -5, 64, 5, 0); plate(-5, -46, 5, 46, 0);             // two roads
  plate(-54, 9, -9, 42, 0); plate(-46, 15, -20, 35, 0.3); plate(-46, 15, -20, 35, 3.6);   // yard, house, upper floor
  plate(9, -42, 56, -9, 0); plate(20, -36, 46, -17, 0.3);         // walled compound and its building
  plate(9, 9, 40, 30, 0); plate(-40, -30, -9, -9, 0);             // two open lots
  return new Float32Array(t);
}
function loadExample() {
  state.map = makeMap(exampleMap(), 'example', true);
  state.example = true; state.navFile = ''; state.title = 'my_scenario'; state.scene = '';
  state.infils = [[-60, 0, 0], [0, 0, -44]]; state.authored = [];
  const e = (group, role, x, y, z, yaw, extra) => Object.assign(newEnemy([x, y, z], role, group), { yaw }, extra || {});
  state.enemies = [
    e(0, 'Guard', -40, 3.6, 30, 135, { defensive: true }), e(0, 'Guard', -26, 0.3, 19, 200, { defensive: true }), e(0, 'Wander', -15, 0, 38, 90),
    e(1, 'Guard', 30, 0.3, -30, 300, { defensive: true }), e(1, 'Wander', 50, 0, -14, 0),
    e(2, 'Patrol', 20, 0, 2, 270, { patrol: [[-30, 0, 2], [-30, 0, -2], [50, 0, -2], [50, 0, 2]] }),
  ];
  state.selected = 5;
}

