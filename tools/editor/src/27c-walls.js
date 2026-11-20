// ------------------------------------------------------------------ tracing walls and suggesting rooms (ML-050)
// Help for painting a house: corners that snap to its walls, and a suggested outline for the room the author clicks in.
// Both work from the solid geometry Game Master mode exports (.mlgeo, read by loadGeo in 22-scenario.js), never from the
// walkable ground: where people can walk says nothing about where a room ends. Nothing here is part of the scenario, the
// undo history or the live mission until the author adds a suggested room, which is then one ordinary paint edit.
//
// Which geometry counts. A .mlgeo file carries no map print, so it is taken to belong to the open map only when its file
// name, without the extension, is the map file's (the plugin names the export after the map file it ran on), and when at
// least 80 % of the walkable ground lies over the geometry. Without geometry, on the example map, or with geometry of
// another map, snapping and suggestions are off with a line that says why; painting by hand is unchanged either way.
//
// The walls. Every triangle of the geometry (apart from the layer only the AI's sight meets) is cut by a level plane at
// waist height on the floor band being painted: 1.0 m above the floor (the band's bottom is 0.5 m under it), or the middle
// of the band when that is lower. What the plane cuts are the wall edges, as segments on the ground plan; this is how the
// walls of Clean House were found (docs/findings.md, "Painted trigger areas against recorded movement").
//
// Snapping, when switched on: a corner placed by Rectangle or Polygon, or dragged with Corners (27a-paint.js), moves to the
// nearest wall corner within the tolerance (where two wall edges meet at more than 20 degrees), else onto the nearest wall
// edge within it. The author sees where it goes before letting go.
//
// Suggest this room: the author clicks inside a room. A working region (30 m square around the click, 6 to 80 m) is cut
// into cells of 0.2 m on the paint grid; cells crossed by a wall edge or by an author-drawn barrier are closed, and cells
// count as floor where an almost level surface of the geometry lies between the band's bottom and the slice. The room is
// filled from the click across open cells. A fill that reaches the region's edge (an open door to outdoors) or ground with
// no floor (a stairwell, a drop) is a leak: nothing is proposed; the reached ground, the leak and the narrowest gap on the
// widest way out are shown, and the author draws a barrier there (or takes the shown gap as one). A bounded fill is traced
// exactly as the brush is (27a-paint.js: outlines and holes, straightened by at most 0.1 m, never across a gap), its corners
// are pulled onto wall edges within 0.35 m where that keeps every ring valid, and it is shown as a draft whose corners can
// be dragged. "Add the room" adds it as parts of the painted area: one undo step, within the same limits as any paint edit.
// "Drop it" changes nothing.
//
// Rooms it handles: rooms closed by walls at waist height on one floor, of any shape with straight walls (diagonal ones
// too); pillars and free-standing columns that reach above the slice, kept as holes; rooms whose doorways and stairwells
// the author closes with barriers; open-plan floors inside outer walls.
// Where it fails, and manual correction takes over: windows open at waist height and doorways without a door leak until a
// barrier is drawn; furniture taller than the slice (wardrobes, shelving, high counters) becomes holes or notches to erase;
// railings and low walls under the slice bound nothing; a stair going up is cut where its steps pass the slice, so its
// lower steps join the room; curved walls give many corners and may meet the 256-corner limit of a ring; a room larger
// than the working region leaks at its edge until the region is made larger; gaps between pieces of geometry narrower
// than a cell close, wider ones leak; outdoors, ground rising above the slice counts as a wall and a missing collider
// under a floor counts as a drop; a room spanning two floor bands (an atrium) needs one suggestion per band; a renamed
// export of another map with much the same footprint passes the match.
const WALL_SKIP = new Set(['AI_VisionBlock']);      // layers only the AI's sight meets: players walk through them
const WALL_WAIST = 1.0, WALL_GRID = 2, WALL_MATCH = 0.8;   // metres above the floor; metres per lookup cell; share of ground over geometry
const WALL_TOL_MIN = 0.05, WALL_TOL_MAX = 2, WALL_REGION_MIN = 6, WALL_REGION_MAX = 80, WALL_BARRIER_MAX = 20;
const WALL_CLEAR = 25;          // cells (5 m): how far from a wall the gap finder measures
const WALL_MAX_CELLS = 170000;  // cells of a working region: an 80 m square at 0.2 m, and never more
const WALL_DRAFT_SNAP = 0.35;   // metres: how far a corner of a suggestion may move onto a wall edge (cell and straightening)
// tool: 'room' | 'barrier' | null; barriers: [{ a, b, band }]; draft and leak: the last suggestion; at: where it was asked.
state.walls = { snap: false, tol: 0.5, region: 30, tool: null, barriers: [], draft: null, leak: null, at: null };
const wallsCache = { geo: null, map: null, match: null, slice: null };
let wallsGesture = null, wallsPointer = null;
PAINT_STEPS['That room'] = 'Add a suggested room to ';

// ----- which geometry counts
const wallsStem = name => String(name || '').replace(/^.*[\\/]/, '').replace(/\.[^.]*$/, '').toLowerCase();
// Every usable triangle of the geometry as three offsets into its positions.
function wallsEachTriangle(fn) {
  const geo = state.geo;
  for (const g of geo.groups) {
    if (WALL_SKIP.has(layerName(g))) continue;
    for (let t = g.start; t + 2 < g.start + g.count; t += 3) if (!geo.bad[t / 3 | 0]) fn(geo.all[t] * 3, geo.all[t + 1] * 3, geo.all[t + 2] * 3);
  }
}
// The share of the walkable ground (a sample of its triangles) that lies over some piece of the geometry, on the ground plan.
function wallsCover(m) {
  const b = m.bounds, span = Math.max(b.maxX - b.minX, b.maxZ - b.minZ, 1), cs = Math.max(2, span / 512);
  const gw = Math.floor((b.maxX - b.minX) / cs) + 1, gh = Math.floor((b.maxZ - b.minZ) / cs) + 1, on = new Uint8Array(gw * gh), p = state.geo.positions;
  wallsEachTriangle((a, c, d) => {
    const x0 = Math.min(p[a], p[c], p[d]), x1 = Math.max(p[a], p[c], p[d]), z0 = Math.min(p[a + 2], p[c + 2], p[d + 2]), z1 = Math.max(p[a + 2], p[c + 2], p[d + 2]);
    if (x1 < b.minX || x0 > b.maxX || z1 < b.minZ || z0 > b.maxZ) return;
    const i0 = Math.max(0, Math.floor((x0 - b.minX) / cs)), i1 = Math.min(gw - 1, Math.floor((x1 - b.minX) / cs));
    const j0 = Math.max(0, Math.floor((z0 - b.minZ) / cs)), j1 = Math.min(gh - 1, Math.floor((z1 - b.minZ) / cs));
    for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) on[j * gw + i] = 1;
  });
  const t = m.tris, step = Math.max(1, Math.floor(m.n / 3000));
  let n = 0, over = 0;
  for (let k = 0; k < m.n; k += step) {
    const o = k * 9, i = Math.floor(((t[o] + t[o + 3] + t[o + 6]) / 3 - b.minX) / cs), j = Math.floor(((t[o + 2] + t[o + 5] + t[o + 8]) / 3 - b.minZ) / cs);
    n++;
    if (i >= 0 && j >= 0 && i < gw && j < gh && on[j * gw + i]) over++;
  }
  return n ? over / n : 0;
}
// { ok, why }: whether the open geometry belongs to the open map. Worked out once per pair of files.
function wallsMatch() {
  const m = state.map, geo = state.geo;
  if (!m || m.isExample) return { ok: false, why: 'Wall tracing needs the map file and the 3D geometry exported for it (.mlgeo): open both.' };
  if (!geo) return { ok: false, why: 'No 3D geometry is open: open the .mlgeo file Game Master mode exports for this map (press M there) to snap to walls and suggest rooms.' };
  if (wallsCache.geo === geo && wallsCache.map === m && wallsCache.match) return wallsCache.match;
  wallsCache.geo = geo; wallsCache.map = m; wallsCache.slice = null;
  let match;
  if (wallsStem(geo.name) !== wallsStem(m.name)) match = { ok: false, why: 'The 3D geometry ' + geo.name + ' was exported on another map than the open one (' + m.name + '), so wall tracing is off.' };
  else {
    const cover = wallsCover(m);
    match = cover >= WALL_MATCH ? { ok: true, why: '' }
      : { ok: false, why: 'The 3D geometry ' + geo.name + ' does not line up with the walkable ground of ' + m.name + ' (' + Math.round(cover * 100) + ' % of it lies over the geometry), so wall tracing is off. Export it again on this map.' };
  }
  wallsCache.match = match;
  return match;
}

// ----- the wall edges of the floor band
// The height of the slice: waist height above the band's floor, or the middle of the band when that is lower.
const wallsWaist = () => r2(Math.min(state.paint.bottom + 0.5 + WALL_WAIST, (state.paint.bottom + state.paint.top) / 2));
const wallsKey = (i, j) => (i + 100000) * 200000 + j + 100000;
// { y, segs (ax, az, bx, bz each), n, grid: lookup cell -> segment numbers }, or null when wall tracing is off.
function wallsSlice() {
  if (!wallsMatch().ok) return null;
  const y = wallsWaist();
  if (wallsCache.slice && wallsCache.slice.y === y) return wallsCache.slice;
  const p = state.geo.positions, out = [];
  wallsEachTriangle((a, b, c) => {
    const ya = p[a + 1], yb = p[b + 1], yc = p[c + 1];
    if ((ya >= y) === (yb >= y) && (yb >= y) === (yc >= y)) return;
    const cut = [];
    for (const [u, v] of [[a, b], [b, c], [c, a]]) {
      const yu = p[u + 1], yv = p[v + 1];
      if ((yu >= y) === (yv >= y)) continue;
      const t = (y - yu) / (yv - yu);
      cut.push(p[u] + (p[v] - p[u]) * t, p[u + 2] + (p[v + 2] - p[u + 2]) * t);
    }
    if (cut.length === 4 && Math.hypot(cut[2] - cut[0], cut[3] - cut[1]) >= 0.01) out.push(cut[0], cut[1], cut[2], cut[3]);
  });
  // Each edge goes into the lookup cells it passes through (not every cell of its box, which for a long diagonal edge would
  // be thousands), walking it a quarter cell at a time and adding both side cells where it steps diagonally.
  const segs = new Float64Array(out), n = segs.length / 4, grid = new Map(), add = (i, j, k) => { const key = wallsKey(i, j), list = grid.get(key); if (!list) grid.set(key, [k]); else if (list[list.length - 1] !== k) list.push(k); };
  for (let k = 0; k < n; k++) {
    const o = k * 4, steps = Math.max(1, Math.ceil(Math.hypot(segs[o + 2] - segs[o], segs[o + 3] - segs[o + 1]) / (WALL_GRID / 4)));
    let pi = null, pj = null;
    for (let s = 0; s <= steps; s++) {
      const i = Math.floor((segs[o] + (segs[o + 2] - segs[o]) * s / steps) / WALL_GRID), j = Math.floor((segs[o + 1] + (segs[o + 3] - segs[o + 1]) * s / steps) / WALL_GRID);
      if (i === pi && j === pj) continue;
      add(i, j, k);
      if (pi !== null && i !== pi && j !== pj) { add(pi, j, k); add(i, pj, k); }
      pi = i; pj = j;
    }
  }
  wallsCache.slice = { y, segs, n, grid };
  return wallsCache.slice;
}
// The nearest point of segment a-b to p, and how far it is: [x, z, distance].
function wallsNearest(px, pz, ax, az, bx, bz) {
  const dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
  const t = len > 0 ? Math.max(0, Math.min(1, ((px - ax) * dx + (pz - az) * dz) / len)) : 0, x = ax + dx * t, z = az + dz * t;
  return [x, z, Math.hypot(px - x, pz - z)];
}
// Wall edges within tol of p = [x, z], nearest first: [{ k, x, z, d }].
function wallsNear(p, tol) {
  const s = wallsSlice();
  if (!s) return [];
  const out = [], seen = new Set(), segs = s.segs;
  for (let i = Math.floor((p[0] - tol) / WALL_GRID); i <= Math.floor((p[0] + tol) / WALL_GRID); i++)
    for (let j = Math.floor((p[1] - tol) / WALL_GRID); j <= Math.floor((p[1] + tol) / WALL_GRID); j++) {
      for (const k of s.grid.get(wallsKey(i, j)) || []) {
        if (seen.has(k)) continue;
        seen.add(k);
        const o = k * 4, q = wallsNearest(p[0], p[1], segs[o], segs[o + 1], segs[o + 2], segs[o + 3]);
        if (q[2] <= tol) out.push({ k, x: q[0], z: q[1], d: q[2] });
      }
    }
  return out.sort((a, b) => a.d - b.d);
}
// Where p = [x, z] snaps to within tol: { p, corner } (a wall corner, else the nearest point of a wall edge), or null.
function wallsSnapPoint(p, tol) {
  const near = wallsNear(p, tol);
  if (!near.length) return null;
  const segs = wallsSlice().segs, list = near.slice(0, 12), slack = Math.min(tol, 0.1);
  let best = null, bestD = Infinity;
  for (let a = 0; a < list.length; a++) for (let b = a + 1; b < list.length; b++) {
    const o = list[a].k * 4, q = list[b].k * 4;
    const ux = segs[o + 2] - segs[o], uz = segs[o + 3] - segs[o + 1], vx = segs[q + 2] - segs[q], vz = segs[q + 3] - segs[q + 1];
    const lu = Math.hypot(ux, uz), lv = Math.hypot(vx, vz), cross = ux * vz - uz * vx;
    if (!lu || !lv || Math.abs(cross) / (lu * lv) < 0.34) continue;           // nearly parallel: the two faces of one wall
    const s = ((segs[q] - segs[o]) * vz - (segs[q + 1] - segs[o + 1]) * vx) / cross, x = segs[o] + ux * s, z = segs[o + 1] + uz * s;
    const d = Math.hypot(x - p[0], z - p[1]);
    if (d > tol || d >= bestD) continue;
    if (wallsNearest(x, z, segs[o], segs[o + 1], segs[o + 2], segs[o + 3])[2] > slack || wallsNearest(x, z, segs[q], segs[q + 1], segs[q + 2], segs[q + 3])[2] > slack) continue;
    best = [x, z]; bestD = d;
  }
  return best ? { p: best, corner: true } : { p: [near[0].x, near[0].z], corner: false };
}
// A corner the author places or drags, snapped when snapping is on and the geometry matches; else unchanged. 27a-paint.js
// sends every corner of Rectangle, Polygon and Corners through this.
function wallsSnap(w) {
  if (!w || !state.walls.snap || !state.paint.target) return w;
  const got = wallsSnapPoint(w, state.walls.tol);
  return got ? [r2(got.p[0]), r2(got.p[1])] : w;
}
function wallsSet(o) {
  o = o || {};
  if (o.snap !== undefined) state.walls.snap = !!o.snap;
  if (o.tol !== undefined && isFinite(+o.tol)) state.walls.tol = Math.max(WALL_TOL_MIN, Math.min(WALL_TOL_MAX, r2(+o.tol)));
  if (o.region !== undefined && isFinite(+o.region)) state.walls.region = Math.max(WALL_REGION_MIN, Math.min(WALL_REGION_MAX, Math.round(+o.region)));
  wallsSync(); invalidate();
}

// ----- barriers: short lines the author draws across doorways and stairwells, on the band they were drawn on
const wallsBandOf = b => paintSameBand({ floor: b.band }, paintBand());
const wallsBarriersHere = () => state.walls.barriers.filter(wallsBandOf);
function wallsBarrierAdd(a, b) {
  if (!areaHolder(state.paint.target)) { say('Choose what to paint first.'); return false; }
  if (!Array.isArray(a) || !Array.isArray(b) || ![a[0], a[1], b[0], b[1]].every(v => typeof v === 'number' && isFinite(v))) return false;
  const len = Math.hypot(b[0] - a[0], b[1] - a[1]);
  if (len < 0.3) { say('Drag a barrier right across the doorway: at least 0.3 m.'); return false; }
  if (len > WALL_BARRIER_MAX) { say('A barrier is at most ' + WALL_BARRIER_MAX + ' m long: draw a long opening as several.'); return false; }
  state.walls.barriers.push({ a: [r2(a[0]), r2(a[1])], b: [r2(b[0]), r2(b[1])], band: paintBand() });
  // A leak may be closed now: ask again at the same point. A draft stays as the author may have corrected it.
  if (state.walls.leak && state.walls.at) wallsSuggest(state.walls.at);
  else { wallsSync(); invalidate(); }
  return true;
}
function wallsBarrierRemove(k) {
  const here = wallsBarriersHere()[k];
  if (!here) return false;
  state.walls.barriers.splice(state.walls.barriers.indexOf(here), 1);
  wallsSync(); invalidate();
  return true;
}
function wallsBarrierClear() {
  state.walls.barriers = state.walls.barriers.filter(b => !wallsBandOf(b));
  wallsSync(); invalidate();
}
function wallsBarrierAt(sx, sy) {
  let best = -1, bd = 7;
  wallsBarriersHere().forEach((b, k) => {
    const [ax, ay] = toScreen(b.a[0], b.a[1]), [bx, by] = toScreen(b.b[0], b.b[1]), d = wallsNearest(sx, sy, ax, ay, bx, by)[2];
    if (d < bd) { bd = d; best = k; }
  });
  return best;
}

// ----- suggest this room
// The working region around `at`, on the paint grid: closed cells (wall edges and barriers) and floor cells. null when it
// would hold more than WALL_MAX_CELLS cells, so every pass over it below stays bounded.
function wallsRegion(at) {
  const C = PAINT_CELL, half = state.walls.region / 2;
  const i0 = Math.floor((at[0] - half) / C), j0 = Math.floor((at[1] - half) / C), i1 = Math.floor((at[0] + half) / C), j1 = Math.floor((at[1] + half) / C);
  const w = i1 - i0 + 1, h = j1 - j0 + 1;
  if (!(w > 2 && h > 2 && w * h <= WALL_MAX_CELLS)) return null;
  return { i0, j0, w, h, x0: i0 * C, z0: j0 * C, x1: (i1 + 1) * C, z1: (j1 + 1) * C, closed: new Uint8Array(w * h), floor: new Uint8Array(w * h) };
}
// Closes every cell the segment passes through. Samples half a cell apart land in the same or a neighbouring cell, and two
// closed cells meeting at a corner already stop a fill that moves between sides only, so no line has a gap.
function wallsClose(g, ax, az, bx, bz) {
  let t0 = 0, t1 = 1;
  const d = [bx - ax, bz - az], lo = [g.x0 - ax, g.z0 - az], hi = [g.x1 - ax, g.z1 - az];
  for (let k = 0; k < 2; k++) {                    // only the piece inside the region
    if (Math.abs(d[k]) < 1e-12) { if (lo[k] > 0 || hi[k] < 0) return; continue; }
    let u = lo[k] / d[k], v = hi[k] / d[k];
    if (u > v) { const s = u; u = v; v = s; }
    t0 = Math.max(t0, u); t1 = Math.min(t1, v);
    if (t0 > t1) return;
  }
  const C = PAINT_CELL, steps = Math.max(1, Math.ceil(Math.hypot(d[0], d[1]) * (t1 - t0) / (C / 2)));
  for (let s = 0; s <= steps; s++) {
    const t = t0 + (t1 - t0) * s / steps, i = Math.floor((ax + d[0] * t) / C) - g.i0, j = Math.floor((az + d[1] * t) / C) - g.j0;
    if (i >= 0 && j >= 0 && i < g.w && j < g.h) g.closed[j * g.w + i] = 1;
  }
}
// Floor: a cell whose middle lies over an almost level surface between the band's bottom and the slice. A crack of one
// cell between two floor pieces is floor too, so only a real opening (a stairwell, a drop) counts as missing floor.
function wallsFloor(g, bottom, top) {
  const p = state.geo.positions, C = PAINT_CELL, f = g.floor;
  wallsEachTriangle((a, b, c) => {
    const ay = p[a + 1], by = p[b + 1], cy = p[c + 1];
    if (Math.min(ay, by, cy) >= top || Math.max(ay, by, cy) < bottom) return;
    const ax = p[a], az = p[a + 2], bx = p[b], bz = p[b + 2], cx = p[c], cz = p[c + 2];
    if (Math.max(ax, bx, cx) < g.x0 || Math.min(ax, bx, cx) > g.x1 || Math.max(az, bz, cz) < g.z0 || Math.min(az, bz, cz) > g.z1) return;
    const ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
    const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
    if (Math.abs(ny) < 0.7 * Math.hypot(nx, ny, nz)) return;          // steeper than about 45 degrees: not something to stand on
    const den = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
    if (Math.abs(den) < 1e-12) return;
    const i0 = Math.max(0, Math.floor(Math.min(ax, bx, cx) / C - 0.5) - g.i0), i1 = Math.min(g.w - 1, Math.ceil(Math.max(ax, bx, cx) / C - 0.5) - g.i0);
    const j0 = Math.max(0, Math.floor(Math.min(az, bz, cz) / C - 0.5) - g.j0), j1 = Math.min(g.h - 1, Math.ceil(Math.max(az, bz, cz) / C - 0.5) - g.j0);
    for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
      const k = j * g.w + i;
      if (f[k]) continue;
      const x = (g.i0 + i + 0.5) * C, z = (g.j0 + j + 0.5) * C;
      const l1 = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / den, l2 = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / den, l3 = 1 - l1 - l2;
      if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
      const y = l1 * ay + l2 * by + l3 * cy;
      if (y >= bottom && y < top) f[k] = 1;
    }
  });
  const w = g.w, out = new Uint8Array(f.length);
  for (let j = 0; j < g.h; j++) for (let i = 0; i < w; i++) {
    const k = j * w + i;
    out[k] = f[k] || (i > 0 && i < w - 1 && f[k - 1] && f[k + 1]) || (j > 0 && j < g.h - 1 && f[k - w] && f[k + w]) ? 1 : 0;
  }
  g.floor = out;
}
// The way out of a leaking fill: along the widest route from the click to the leak, the narrowest point nearest the room,
// and the line across it between the walls on either side. { a, b, width, mid } or null when no wall is near enough.
function wallsGap(g, start, leaks) {
  // Clearance: how far each cell is from the nearest closed cell, in tenths of a cell, up to 5 m: two raster passes with
  // steps of 1 cell sideways and 1.41 diagonally, so every cell is visited exactly twice. Close to straight-line distance,
  // a doorway comes out narrower than the floor just in front of it, which is where the gap is looked for.
  const { w, h, closed } = g, N = w * h, CAP = WALL_CLEAR * 10, D = new Float64Array(N), R2 = Math.SQRT2;
  for (let k = 0; k < N; k++) D[k] = closed[k] ? 0 : WALL_CLEAR;
  for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) {
    const k = j * w + i;
    if (!D[k]) continue;
    let v = D[k];
    if (i > 0) v = Math.min(v, D[k - 1] + 1);
    if (j > 0) { v = Math.min(v, D[k - w] + 1); if (i > 0) v = Math.min(v, D[k - w - 1] + R2); if (i < w - 1) v = Math.min(v, D[k - w + 1] + R2); }
    D[k] = v;
  }
  for (let j = h - 1; j >= 0; j--) for (let i = w - 1; i >= 0; i--) {
    const k = j * w + i;
    if (!D[k]) continue;
    let v = D[k];
    if (i < w - 1) v = Math.min(v, D[k + 1] + 1);
    if (j < h - 1) { v = Math.min(v, D[k + w] + 1); if (i < w - 1) v = Math.min(v, D[k + w + 1] + R2); if (i > 0) v = Math.min(v, D[k + w - 1] + R2); }
    D[k] = v;
  }
  const clear = new Int16Array(N);
  for (let k = 0; k < N; k++) clear[k] = Math.min(CAP, Math.floor(D[k] * 10 + 1e-6));
  const best = new Int16Array(N).fill(-1), from = new Int32Array(N).fill(-1), done = new Uint8Array(N), buckets = [];
  for (let b = 0; b <= CAP; b++) buckets.push([]);
  const leaking = new Uint8Array(N);
  for (const k of leaks) leaking[k] = 1;
  best[start] = clear[start]; buckets[clear[start]].push(start);
  // The widest route to every cell, its narrowest point as large as can be: buckets from the widest down. A cell is taken
  // out once (done), and only a taken-out cell pushes its four neighbours, so there are at most 4 pushes per cell.
  for (let b = CAP; b >= 0; b--) {
    const list = buckets[b];
    while (list.length) {
      const k = list.pop();
      if (done[k] || best[k] !== b) continue;
      done[k] = 1;
      const i = k % w, j = (k - i) / w;
      if (leaking[k] || i === 0 || j === 0 || i === w - 1 || j === h - 1) continue;
      for (const n of [k - 1, k + 1, k - w, k + w]) {
        if (closed[n]) continue;
        const nb = Math.min(b, clear[n]);
        if (nb > best[n]) { best[n] = nb; from[n] = k; buckets[nb].push(n); }
      }
    }
  }
  let out = -1;
  for (const k of leaks) if (out < 0 || best[k] > best[out]) out = k;
  if (out < 0 || best[out] < 0 || best[out] >= CAP) return null;
  const path = [];
  for (let k = out, steps = 0; k >= 0 && steps <= N; k = from[k], steps++) path.push(k);
  path.reverse();
  const neck = path.find(k => clear[k] === best[out]);
  if (neck === undefined) return null;
  const ci = neck % w, cj = (neck - ci) / w, reach = Math.ceil(best[out] / 10) + 2, C = PAINT_CELL;
  let A = null, ad = Infinity;
  for (let j = Math.max(0, cj - reach); j <= Math.min(h - 1, cj + reach); j++) for (let i = Math.max(0, ci - reach); i <= Math.min(w - 1, ci + reach); i++) {
    const d = Math.hypot(i - ci, j - cj);
    if (closed[j * w + i] && d < ad) { ad = d; A = [i, j]; }
  }
  if (!A) return null;
  let B = null, bd = Infinity;
  for (let j = Math.max(0, cj - reach); j <= Math.min(h - 1, cj + reach); j++) for (let i = Math.max(0, ci - reach); i <= Math.min(w - 1, ci + reach); i++) {
    const d = Math.hypot(i - ci, j - cj);
    if (!closed[j * w + i] || d >= bd || !d) continue;
    const cos = ((i - ci) * (A[0] - ci) + (j - cj) * (A[1] - cj)) / (d * (Math.hypot(A[0] - ci, A[1] - cj) || 1));
    if (cos < -0.5) { bd = d; B = [i, j]; }
  }
  if (!B) return null;
  const at = c => [r2((g.i0 + c[0] + 0.5) * C), r2((g.j0 + c[1] + 0.5) * C)], a = at(A), b = at(B);
  return { a, b, width: r2(Math.max(0, Math.hypot(b[0] - a[0], b[1] - a[1]) - C)), mid: [r2((a[0] + b[0]) / 2), r2((a[1] + b[1]) / 2)] };
}
// A copy of each ring with its corners pulled onto wall edges within reach; a part that would break a rule keeps its corners.
function wallsSnapParts(parts) {
  const pull = ring => ring.map(p => { const got = wallsSnapPoint(p, WALL_DRAFT_SNAP); return got ? [r2(got.p[0]), r2(got.p[1])] : p; });
  return parts.map(part => {
    const moved = paintPart({ floor: part.floor, outline: pull(part.outline), holes: (part.holes || []).map(pull) });
    return wallsFindings([moved]).length || moved.outline.length < 3 || moved.holes.length !== (part.holes || []).length ? part : moved;
  });
}
const wallsFindings = parts => areaFindings({ parts });
const wallsTargetIs = t => !!t && !!state.paint.target && t.kind === state.paint.target.kind && t.index === state.paint.target.index && t.id === state.paint.target.id;
// The suggestion for the room around `at` = [x, z]: { draft } when the fill is bounded, { leak } when it is not, or
// { error }. Never changes the scenario, the undo history or the live mission.
function wallsSuggest(at) {
  wallsGesture = null; state.walls.draft = null; state.walls.leak = null;
  const t = state.paint.target, area = areaHolder(t) ? areaOfTarget(t) : null;
  const fail = text => { say(text); wallsSync(); invalidate(); return { error: text }; };
  if (!area) return fail('Choose what to paint first: a zone\'s "Painted area", or a place\'s shape.');
  if (!Array.isArray(at) || !isFinite(at[0]) || !isFinite(at[1])) return fail('Click inside a room.');
  const s = wallsSlice();
  if (!s) return fail('No room can be suggested. ' + wallsMatch().why + ' Painting by hand works as before.');
  state.walls.at = [r2(at[0]), r2(at[1])];
  const band = paintBand(), g = wallsRegion(at), segs = s.segs;
  if (!g) return fail('The working region of ' + state.walls.region + ' m is too large to fill: narrow the region (at most ' + WALL_REGION_MAX + ' m).');
  for (let k = 0; k < s.n; k++) wallsClose(g, segs[k * 4], segs[k * 4 + 1], segs[k * 4 + 2], segs[k * 4 + 3]);
  const barriers = wallsBarriersHere();
  for (const b of barriers) wallsClose(g, b.a[0], b.a[1], b.b[0], b.b[1]);
  wallsFloor(g, band[0], s.y);
  const { w, h, closed, floor } = g, start = (Math.floor(at[1] / PAINT_CELL) - g.j0) * w + Math.floor(at[0] / PAINT_CELL) - g.i0;
  if (closed[start]) return fail('That point is on a wall edge or a barrier: click further inside the room.');
  if (!floor[start]) return fail('There is no floor on this floor band (' + band[0] + ' to ' + band[1] + ' m) at that point: click on the floor of the room, or choose its band.');
  const reached = new Uint8Array(w * h), order = new Int32Array(w * h), leaks = [];
  let qh = 0, qt = 0, edge = 0, drop = 0;
  reached[start] = 1; order[qt++] = start;
  while (qh < qt) {                               // across open cells, sideways only, never on from a leak
    const k = order[qh++], i = k % w, j = (k - i) / w;
    if (!floor[k]) { leaks.push(k); drop++; continue; }
    if (i === 0 || j === 0 || i === w - 1 || j === h - 1) { leaks.push(k); edge++; continue; }
    for (const n of [k - 1, k + 1, k - w, k + w]) if (!closed[n] && !reached[n]) { reached[n] = 1; order[qt++] = n; }
  }
  const on = new Uint8Array(w * h);
  for (let k = 0; k < w * h; k++) on[k] = reached[k] && floor[k] ? 1 : 0;
  const box = { x0: g.x0, z0: g.z0, x1: g.x1, z1: g.z1 }, C = PAINT_CELL, cellAt = k => { const i = k % w; return [r2((g.i0 + i + 0.5) * C), r2((g.j0 + (k - i) / w + 0.5) * C)]; };
  if (leaks.length) {
    const shown = paintTrace({ i0: g.i0, j0: g.j0, w, h, on }, band, []).parts, every = Math.max(1, Math.floor(leaks.length / 80));
    const leak = { box, band, parts: shown, marks: leaks.filter((k, n) => n % every === 0).map(cellAt), edge, drop, gap: wallsGap(g, start, leaks) };
    state.walls.leak = leak;
    say(wallsLeakWords(leak));
    wallsSync(); invalidate();
    return { leak };
  }
  const neighbours = area.parts.filter(p => paintSameBand(p, band)), traced = paintTrace({ i0: g.i0, j0: g.j0, w, h, on }, band, neighbours);
  if (!traced.parts.length) return fail('The room there is smaller than an area may be (0.25 square metres).');
  const draft = { parts: wallsSnapParts(traced.parts), box, band, target: { kind: t.kind, index: t.index, id: t.id }, dropped: traced.dropped };
  state.walls.draft = draft;
  wallsDraftCheck();
  say(wallsDraftWords(draft));
  wallsSync(); invalidate();
  return { draft };
}
function wallsLeakWords(leak) {
  const where = [leak.edge ? 'reached the edge of the ' + state.walls.region + ' m working region' : '', leak.drop ? 'reached ground with no floor on this band (a stairwell or a drop)' : ''].filter(Boolean).join(' and ');
  return 'No room suggested: the fill ' + where + ', so the room is not closed. ' + (leak.gap
    ? 'It got out through a gap about ' + leak.gap.width + ' m wide at ' + leak.gap.mid[0] + ', ' + leak.gap.mid[1] + ' (marked red): draw a barrier across it, or press "Close the gap".'
    : 'No narrow gap leads out: draw barriers across the openings, or click inside a room with walls around it.');
}
function wallsDraftCheck() {
  const d = state.walls.draft, h = areaHolder(state.paint.target);
  if (!d) return;
  d.findings = wallsFindings(d.parts);
  d.limit = paintLimits(((h && h.area && h.area.parts) || []).concat(d.parts));
}
function wallsDraftWords(d) {
  const holes = d.parts.reduce((n, p) => n + (p.holes || []).length, 0), corners = d.parts.reduce((n, p) => n + p.outline.length + (p.holes || []).reduce((m, r) => m + r.length, 0), 0);
  return 'Draft room: ' + plural(d.parts.length, 'part', 'parts') + ', ' + plural(corners, 'corner', 'corners') + (holes ? ', ' + plural(holes, 'hole', 'holes') + ' kept' : '')
    + (d.dropped ? ', ' + plural(d.dropped, 'piece', 'pieces') + ' too small to keep' : '') + '. It is not part of the scenario until you press "Add the room"; drag its corners to correct it first, or drop it.';
}
// Adds the draft to the painted area: one undo step, refused like any paint edit that breaks a rule or a limit.
function wallsAccept() {
  const d = state.walls.draft, t = state.paint.target;
  if (!d) { say('There is no suggested room to add.'); return false; }
  if (!wallsTargetIs(d.target)) { wallsReject(); say('The suggestion was for something else: ask again.'); return false; }
  wallsDraftCheck();
  if (d.findings.length) { say('The draft cannot be added yet: ' + d.findings[0].where + ': ' + d.findings[0].text + '. Move its corners, or drop it.'); return false; }
  const area = areaOfTarget(t), before = area.parts.length;
  if (!paintCommit(t, area.parts.concat(d.parts.map(paintPart)), 'That room')) return false;
  state.walls.draft = null; state.walls.leak = null; wallsGesture = null;
  paintTell(t, before, 'The room was added to ' + areaTargetName(t) + '. Correct it with Corners, Brush and Erase like any painted part; Undo takes it out again.');
  wallsSync(); invalidate();
  return true;
}
function wallsReject() {
  const had = !!(state.walls.draft || state.walls.leak);
  state.walls.draft = null; state.walls.leak = null; wallsGesture = null;
  if (had) say('Suggestion dropped: nothing changed.');
  wallsSync(); invalidate();
  return had;
}
const wallsCloseGap = () => { const l = state.walls.leak; return !!(l && l.gap) && wallsBarrierAdd(l.gap.a, l.gap.b); };

// ----- correcting the draft: its corners move, appear and go without touching the scenario
function wallsDraftHandles() {
  const d = state.walls.draft, out = [];
  if (!d) return out;
  d.parts.forEach((part, j) => [part.outline, ...(part.holes || [])].forEach((ring, hh) => ring.forEach((p, k) => {
    const q = ring[(k + 1) % ring.length];
    out.push({ part: j, ring: hh - 1, k, p, mid: false }, { part: j, ring: hh - 1, k, p: [(p[0] + q[0]) / 2, (p[1] + q[1]) / 2], mid: true });
  })));
  return out;
}
function wallsDraftHandleAt(sx, sy) {
  let best = null, bd = 9;
  for (const hd of wallsDraftHandles()) {
    const [x, y] = toScreen(hd.p[0], hd.p[1]), d = Math.hypot(x - sx, y - sy) + (hd.mid ? 2 : 0);
    if (d < bd) { bd = d; best = hd; }
  }
  return best;
}
// Moves corner k of a draft ring to p = [x, z] (snapped like any corner), or removes it when p is null.
function wallsDraftVertex(part, ring, k, p) {
  const d = state.walls.draft, r = d && paintRingOf(d.parts, part, ring);
  if (!r || !r[k]) return false;
  if (p) { const at = wallsSnap(p); r[k] = [r2(at[0]), r2(at[1])]; }
  else if (r.length > 3) r.splice(k, 1);
  else if (ring >= 0) d.parts[part].holes.splice(ring, 1);
  else { say('An outline needs at least three corners.'); return false; }
  d.parts = d.parts.map(paintPart);
  wallsDraftCheck(); wallsSync(); invalidate();
  return true;
}

// ----- the mouse: Suggest room and Barrier are chosen in place of a painting tool, so 27a-paint.js leaves the map alone
const wallsActive = () => !!(state.walls.tool && !state.paint.tool && state.mode !== '3d' && areaHolder(state.paint.target));
function wallsToolSet(tool) {
  tool = tool === 'room' || tool === 'barrier' ? tool : null;
  if (tool && !wallsMatch().ok) { say(wallsMatch().why + ' Painting by hand works as before.'); return false; }
  wallsGesture = null;
  if (tool) { paintGestureDrop(); state.paint.tool = null; }
  state.walls.tool = tool;
  syncSide(); invalidate();
  return true;
}
function wallsPointerDown(ev) {
  if (!wallsActive()) return;
  const { sx, sy, w } = paintAt(ev);
  if (state.walls.tool === 'barrier') {
    if (ev.button === 2) { const k = wallsBarrierAt(sx, sy); if (k >= 0) { ev.stopImmediatePropagation(); wallsBarrierRemove(k); } return; }
    if (ev.button !== 0) return;
    ev.stopImmediatePropagation(); canvas.setPointerCapture(ev.pointerId);
    const at = wallsSnap(w);
    wallsGesture = { kind: 'barrier', a: at, b: at };
    invalidate();
    return;
  }
  const hd = state.walls.draft ? wallsDraftHandleAt(sx, sy) : null;
  if (ev.button === 2) { if (hd && !hd.mid) { ev.stopImmediatePropagation(); wallsDraftVertex(hd.part, hd.ring, hd.k, null); } return; }
  if (ev.button !== 0) return;
  ev.stopImmediatePropagation(); canvas.setPointerCapture(ev.pointerId);
  wallsGesture = hd ? { kind: 'corner', hd, sx, sy, was: null } : { kind: 'click', w, sx, sy };
}
function wallsPointerMove(ev) {
  if (!wallsActive()) { wallsPointer = null; return; }
  const { sx, sy, w } = paintAt(ev), g = wallsGesture, d = state.walls.draft;
  wallsPointer = w;
  if (!g) { if (state.walls.snap) invalidate(); return; }
  ev.stopImmediatePropagation();
  if (g.kind === 'barrier') g.b = wallsSnap(w);
  else if (g.kind === 'corner' && d) {
    if (!g.was && Math.hypot(sx - g.sx, sy - g.sy) < 4) return;
    const at = wallsSnap(w);
    if (!g.was) {
      g.was = paintCopy(d.parts);
      const ring = paintRingOf(d.parts, g.hd.part, g.hd.ring);
      if (g.hd.mid) { ring.splice(g.hd.k + 1, 0, [r2(at[0]), r2(at[1])]); g.hd = { part: g.hd.part, ring: g.hd.ring, k: g.hd.k + 1, mid: false }; }
    }
    paintRingOf(d.parts, g.hd.part, g.hd.ring)[g.hd.k] = [r2(at[0]), r2(at[1])];
  }
  invalidate();
}
function wallsPointerUp(ev) {
  const g = wallsGesture;
  if (!g || !wallsActive()) return;
  ev.stopImmediatePropagation();
  wallsGesture = null;
  const { sx, sy, w } = paintAt(ev), d = state.walls.draft;
  if (g.kind === 'barrier') { wallsBarrierAdd(g.a, wallsSnap(w)); invalidate(); return; }
  if (g.kind === 'corner' && d) {
    if (g.was) { d.parts = d.parts.map(paintPart); wallsDraftCheck(); wallsSync(); }
    else if (g.hd.mid) { const ring = paintRingOf(d.parts, g.hd.part, g.hd.ring); ring.splice(g.hd.k + 1, 0, [r2(g.hd.p[0]), r2(g.hd.p[1])]); d.parts = d.parts.map(paintPart); wallsDraftCheck(); wallsSync(); }
    invalidate();
    return;
  }
  if (g.kind === 'click' && Math.hypot(sx - g.sx, sy - g.sy) < 4) wallsSuggest(w);
}
// The browser took the pointer away: a half-made barrier goes, a dragged draft corner goes back.
function wallsPointerCancel() {
  const g = wallsGesture, d = state.walls.draft;
  wallsGesture = null;
  if (g && g.kind === 'corner' && g.was && d) { d.parts = g.was; wallsDraftCheck(); }
  invalidate();
}
function wallsKeyDown(ev) {
  if (!wallsActive() || /^(INPUT|TEXTAREA|SELECT)$/.test(ev.target.tagName)) return;
  const key = ev.key;
  if (key === 'Escape') {
    ev.stopImmediatePropagation();
    if (wallsGesture) wallsPointerCancel();
    else if (state.walls.draft || state.walls.leak) wallsReject();
    else areaEditStop();
  } else if (key === 'Enter' && state.walls.draft && !wallsGesture) { ev.preventDefault(); ev.stopImmediatePropagation(); wallsAccept(); }
  else if (key === 'Delete' || key === 'Backspace') {
    ev.preventDefault(); ev.stopImmediatePropagation();       // never the selected enemy while painting
    if (!wallsPointer || !state.walls.draft) return;
    const [sx, sy] = toScreen(wallsPointer[0], wallsPointer[1]), hd = wallsDraftHandleAt(sx, sy);
    if (hd && !hd.mid) wallsDraftVertex(hd.part, hd.ring, hd.k, null);
  }
}
canvas.addEventListener('pointerdown', wallsPointerDown);
canvas.addEventListener('pointermove', wallsPointerMove);
canvas.addEventListener('pointerup', wallsPointerUp);
canvas.addEventListener('pointercancel', wallsPointerCancel);
window.addEventListener('keydown', wallsKeyDown);

// ----- drawing: on top of the painting tool
function wallsDrawEdges(c, s) {
  if (state.view.scale < 4) return;              // too far out to tell one wall from the next
  const { w, h } = size(), a = toWorld(0, 0), b = toWorld(w, h), seen = new Set(), segs = s.segs;
  c.beginPath();
  for (let i = Math.floor(Math.min(a[0], b[0]) / WALL_GRID); i <= Math.floor(Math.max(a[0], b[0]) / WALL_GRID); i++)
    for (let j = Math.floor(Math.min(a[1], b[1]) / WALL_GRID); j <= Math.floor(Math.max(a[1], b[1]) / WALL_GRID); j++)
      for (const k of s.grid.get(wallsKey(i, j)) || []) {
        if (seen.has(k)) continue;
        seen.add(k);
        const [x0, y0] = toScreen(segs[k * 4], segs[k * 4 + 1]), [x1, y1] = toScreen(segs[k * 4 + 2], segs[k * 4 + 3]);
        c.moveTo(x0, y0); c.lineTo(x1, y1);
      }
  c.globalAlpha = 0.6; c.lineWidth = 1; c.strokeStyle = css['accent-text']; c.stroke(); c.globalAlpha = 1;
}
function wallsDrawLine(c, a, b, colour, width, dash) {
  const [x0, y0] = toScreen(a[0], a[1]), [x1, y1] = toScreen(b[0], b[1]);
  c.beginPath(); c.moveTo(x0, y0); c.lineTo(x1, y1); c.lineWidth = width; c.strokeStyle = colour; c.setLineDash(dash || []); c.stroke(); c.setLineDash([]);
  for (const [x, y] of [[x0, y0], [x1, y1]]) { c.beginPath(); c.arc(x, y, 3, 0, Math.PI * 2); c.fillStyle = colour; c.fill(); }
}
function wallsDrawBox(c, box) {
  const [x0, y0] = toScreen(box.x0, box.z1), [x1, y1] = toScreen(box.x1, box.z0);
  c.lineWidth = 1; c.strokeStyle = css['ink-2']; c.setLineDash([3, 5]); c.strokeRect(x0, y0, x1 - x0, y1 - y0); c.setLineDash([]);
}
function wallsDraw(c) {
  const t = state.paint.target;
  if (!t || state.mode === '3d' || !areaHolder(t)) return;
  const tool = state.paint.tool, s = (state.walls.snap || state.walls.tool) && wallsMatch().ok ? wallsSlice() : null, d = state.walls.draft, l = state.walls.leak;
  c.save();
  if (s) wallsDrawEdges(c, s);
  for (const b of wallsBarriersHere()) wallsDrawLine(c, b.a, b.b, css.warn, 3, [6, 3]);
  if (wallsGesture && wallsGesture.kind === 'barrier') wallsDrawLine(c, wallsGesture.a, wallsGesture.b, css.warn, 3, [6, 3]);
  if (l) {
    wallsDrawBox(c, l.box);
    drawArea(c, { parts: l.parts }, { colour: css.enemy, fill: 0.08, line: 0.5, width: 1.2 });
    c.strokeStyle = css.enemy; c.lineWidth = 1.5;
    for (const p of l.marks) { const [x, y] = toScreen(p[0], p[1]); c.beginPath(); c.moveTo(x - 4, y - 4); c.lineTo(x + 4, y + 4); c.moveTo(x + 4, y - 4); c.lineTo(x - 4, y + 4); c.stroke(); }
    if (l.gap) wallsDrawLine(c, l.gap.a, l.gap.b, css.enemy, 4);
  }
  if (d) {
    wallsDrawBox(c, d.box);
    const colour = css['accent-text'];
    for (const part of d.parts) {
      c.beginPath();
      for (const ring of [part.outline, ...(part.holes || [])]) { ring.forEach((p, k) => { const [x, y] = toScreen(p[0], p[1]); if (k) c.lineTo(x, y); else c.moveTo(x, y); }); c.closePath(); }
      c.globalAlpha = 0.16; c.fillStyle = colour; c.fill('evenodd'); c.globalAlpha = 1;
      c.lineWidth = 2; c.strokeStyle = colour; c.setLineDash([7, 4]); c.stroke(); c.setLineDash([]);
    }
    const first = d.parts[0] && d.parts[0].outline[0];
    if (first) { const [x, y] = toScreen(first[0], first[1]); c.fillStyle = colour; c.font = '600 11px ' + (getComputedStyle(document.body).getPropertyValue('--mono') || 'monospace'); c.textAlign = 'left'; c.textBaseline = 'bottom'; c.fillText('DRAFT', x + 4, y - 4); }
    if (state.walls.tool === 'room') for (const hd of wallsDraftHandles()) {
      const [x, y] = toScreen(hd.p[0], hd.p[1]);
      c.beginPath(); if (hd.mid) c.arc(x, y, 3.5, 0, Math.PI * 2); else c.rect(x - 4.5, y - 4.5, 9, 9);
      c.fillStyle = hd.mid ? css.paper : css.panel; c.fill(); c.lineWidth = 1.5; c.strokeStyle = colour; c.stroke();
    }
  }
  // Where the corner under the pointer would land with snapping on.
  const pointer = state.walls.tool ? wallsPointer : (tool === 'rect' || tool === 'poly' || tool === 'vertex') ? paintPointer : null;
  if (s && state.walls.snap && pointer) {
    const at = wallsSnap(pointer);
    if (at !== pointer) {
      const [x, y] = toScreen(at[0], at[1]);
      c.beginPath(); c.moveTo(x, y - 6); c.lineTo(x + 6, y); c.lineTo(x, y + 6); c.lineTo(x - 6, y); c.closePath();
      c.lineWidth = 2; c.strokeStyle = css['accent-text']; c.stroke();
    }
  }
  c.restore();
}
const wallsDrawPaint = drawPaint;
drawPaint = c => { wallsDrawPaint(c); wallsDraw(c); };

// ----- the controls, inside the painting box (10-side.html)
const wallsUi = { box: $('wallsBox'), tools: [...document.querySelectorAll('#wallsBox [data-walls]')] };
const WALLS_HINTS = {
  room: 'Click inside a room on this floor band: its outline is suggested as a draft. Drag the draft\'s corners to correct it, right-click one to remove it; Enter adds the room, Esc drops it.',
  barrier: 'Drag a barrier across a doorway, a window or a stairwell: suggestions stop there. Right-click a barrier to remove it. Barriers stay on this page and are never saved.',
};
function wallsSync() {
  const t = state.paint.target;
  if (!t || !areaHolder(t)) { state.walls.draft = null; state.walls.leak = null; state.walls.tool = null; wallsGesture = null; return; }
  if (state.walls.draft && !wallsTargetIs(state.walls.draft.target)) state.walls.draft = null;
  if (state.paint.tool) state.walls.tool = null;
  const match = wallsMatch(), s = match.ok ? wallsSlice() : null, d = state.walls.draft, l = state.walls.leak;
  if (!match.ok) state.walls.tool = null;
  wallsUi.box.dataset.off = String(!match.ok);
  $('wallsStatus').textContent = match.ok
    ? 'Walls of ' + state.geo.name + ', cut at ' + s.y + ' m (waist height on this floor band): ' + s.n.toLocaleString('en') + ' wall edges.'
    : match.why + ' Painting by hand works as before.';
  const snap = $('wallsSnap');
  snap.checked = state.walls.snap; snap.disabled = !match.ok;
  setVal($('wallsTol'), state.walls.tol); $('wallsTol').disabled = !match.ok;
  setVal($('wallsRegion'), state.walls.region); $('wallsRegion').disabled = !match.ok;
  for (const b of wallsUi.tools) { b.disabled = !match.ok; b.setAttribute('aria-pressed', String(b.dataset.walls === state.walls.tool)); }
  const barriers = wallsBarriersHere().length;
  $('wallsClearBarriers').hidden = !barriers;
  $('wallsNote').textContent = !match.ok ? '' : (l ? wallsLeakWords(l) : d ? wallsDraftWords(d) : 'Choose Suggest room and click inside a room.')
    + (barriers ? ' ' + plural(barriers, 'barrier', 'barriers') + ' on this floor band.' : '');
  const fl = $('wallsFindings'); fl.textContent = '';
  if (d) for (const f of (d.findings || []).concat(d.limit ? [{ where: 'With the area', text: d.limit }] : [])) { const li = document.createElement('li'); li.textContent = f.where + ': ' + f.text + '.'; fl.appendChild(li); }
  $('wallsAccept').hidden = !d; $('wallsReject').hidden = !(d || l); $('wallsGap').hidden = !(l && l.gap);
  if (wallsActive()) $('paintHint').textContent = WALLS_HINTS[state.walls.tool];
}
const wallsSyncPaint = syncPaint;
syncPaint = () => { wallsSyncPaint(); wallsSync(); };
$('wallsSnap').addEventListener('change', ev => wallsSet({ snap: ev.target.checked }));
$('wallsTol').addEventListener('change', ev => wallsSet({ tol: ev.target.value }));
$('wallsRegion').addEventListener('change', ev => wallsSet({ region: ev.target.value }));
for (const b of wallsUi.tools) b.addEventListener('click', () => wallsToolSet(state.walls.tool === b.dataset.walls ? null : b.dataset.walls));
$('wallsClearBarriers').addEventListener('click', wallsBarrierClear);
$('wallsGap').addEventListener('click', wallsCloseGap);
$('wallsAccept').addEventListener('click', wallsAccept);
$('wallsReject').addEventListener('click', wallsReject);
expose({ wallsMatch, wallsSlice, wallsSnapPoint, wallsSnap, wallsSet, wallsToolSet, wallsSuggest, wallsAccept, wallsReject, wallsBarrierAdd, wallsBarrierRemove, wallsBarrierClear,
  wallsCloseGap, wallsDraftVertex, wallsPointerDown, wallsPointerMove, wallsPointerUp });   // wall tracing and room suggestions (ML-050), for automated tests
