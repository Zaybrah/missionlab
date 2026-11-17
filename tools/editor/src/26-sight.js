// ------------------------------------------------------------------ checks
// ------------------------------------------------------------------ sight
// Who can see whom, with the walls of the 3D map file in the way. The triangles are sorted into a grid of
// 4 m squares once, so one look only tests the few that lie under it.
const sight = { made: null, grid: null, stamp: null, tick: 0, cache: null, key: '' };
const SEE_THROUGH = new Set(['PlayerBlock', 'Water']);         // invisible walls for players only, and water: neither stops a look
function sightGrid() {
  const g = state.geo; if (!g) return null;
  if (sight.made === g) return sight.grid;
  const tris = [];
  for (const grp of g.groups) {
    if (SEE_THROUGH.has(g.layers[grp.layer] || '')) continue;
    for (let t = grp.start; t + 2 < grp.start + grp.count; t += 3) if (!g.bad[t / 3]) tris.push(t);
  }
  const P = g.positions, I = g.all;
  let minX = Infinity, minZ = Infinity, maxX = -Infinity, maxZ = -Infinity;
  for (const t of tris) for (let k = 0; k < 3; k++) { const v = I[t + k] * 3; if (P[v] < minX) minX = P[v]; if (P[v] > maxX) maxX = P[v]; if (P[v + 2] < minZ) minZ = P[v + 2]; if (P[v + 2] > maxZ) maxZ = P[v + 2]; }
  const cell = Math.max(4, Math.max(maxX - minX, maxZ - minZ) / 400), nx = Math.max(1, Math.ceil((maxX - minX) / cell)), nz = Math.max(1, Math.ceil((maxZ - minZ) / cell));
  const span = t => {
    let ax = Infinity, az = Infinity, bx = -Infinity, bz = -Infinity;
    for (let k = 0; k < 3; k++) { const v = I[t + k] * 3; ax = Math.min(ax, P[v]); bx = Math.max(bx, P[v]); az = Math.min(az, P[v + 2]); bz = Math.max(bz, P[v + 2]); }
    return [Math.max(0, Math.floor((ax - minX) / cell)), Math.min(nx - 1, Math.floor((bx - minX) / cell)), Math.max(0, Math.floor((az - minZ) / cell)), Math.min(nz - 1, Math.floor((bz - minZ) / cell))];
  };
  const start = new Uint32Array(nx * nz + 1);
  for (const t of tris) { const [x0, x1, z0, z1] = span(t); for (let z = z0; z <= z1; z++) for (let x = x0; x <= x1; x++) start[z * nx + x + 1]++; }
  for (let c = 0; c < nx * nz; c++) start[c + 1] += start[c];
  const fill = start.slice(0, nx * nz), list = new Uint32Array(start[nx * nz]);
  for (const t of tris) { const [x0, x1, z0, z1] = span(t); for (let z = z0; z <= z1; z++) for (let x = x0; x <= x1; x++) list[fill[z * nx + x]++] = t; }
  sight.made = g; sight.stamp = new Uint32Array(Math.ceil(I.length / 3)); sight.tick = 0;
  return sight.grid = { minX, minZ, nx, nz, cell, start, list };
}
// Is the straight line from a to b free of walls? Both are eye positions, [x, y, z]. Without a 3D map nothing is in the way.
function canSee(a, b) {
  const G = sightGrid(); if (!G) return true;
  const P = state.geo.positions, I = state.geo.all, dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
  const len = Math.hypot(dx, dz), steps = Math.max(1, Math.ceil(len / (G.cell * 0.5)));
  const tick = ++sight.tick; let lastCell = -1;
  for (let n = 0; n <= steps; n++) {
    const f = n / steps, cx = Math.floor((a[0] + dx * f - G.minX) / G.cell), cz = Math.floor((a[2] + dz * f - G.minZ) / G.cell);
    // The line is thin but a square is not: look in the square and its neighbours, so nothing is skipped at a corner.
    for (let oz = -1; oz <= 1; oz++) for (let ox = -1; ox <= 1; ox++) {
      const x = cx + ox, z = cz + oz; if (x < 0 || z < 0 || x >= G.nx || z >= G.nz) continue;
      const c = z * G.nx + x; if (c === lastCell) continue;
      for (let k = G.start[c]; k < G.start[c + 1]; k++) {
        const t = G.list[k]; if (sight.stamp[t / 3] === tick) continue; sight.stamp[t / 3] = tick;
        // Where the line meets the triangle's plane, and whether that point lies inside the triangle.
        const p0 = I[t] * 3, p1 = I[t + 1] * 3, p2 = I[t + 2] * 3;
        const e1x = P[p1] - P[p0], e1y = P[p1 + 1] - P[p0 + 1], e1z = P[p1 + 2] - P[p0 + 2], e2x = P[p2] - P[p0], e2y = P[p2 + 1] - P[p0 + 1], e2z = P[p2 + 2] - P[p0 + 2];
        const hx = dy * e2z - dz * e2y, hy = dz * e2x - dx * e2z, hz = dx * e2y - dy * e2x, det = e1x * hx + e1y * hy + e1z * hz;
        if (det > -1e-9 && det < 1e-9) continue;
        const inv = 1 / det, sx = a[0] - P[p0], sy = a[1] - P[p0 + 1], sz = a[2] - P[p0 + 2], u = inv * (sx * hx + sy * hy + sz * hz);
        if (u < 0 || u > 1) continue;
        const qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x, v = inv * (dx * qx + dy * qy + dz * qz);
        if (v < 0 || u + v > 1) continue;
        const along = inv * (e2x * qx + e2y * qy + e2z * qz);
        if (along > 0.01 && along < 0.99) return false;          // something stands between the two, not at either end
      }
    }
    lastCell = -1;
  }
  return true;
}
// Insertion points that belong together (one insertion is a dozen spots a few metres apart).
function insertionGroups() {
  const pts = state.infils, group = new Array(pts.length).fill(-1); let n = 0;
  for (let i = 0; i < pts.length; i++) {
    if (group[i] >= 0) continue;
    const todo = [i]; group[i] = n;
    while (todo.length) { const a = pts[todo.pop()]; for (let j = 0; j < pts.length; j++) if (group[j] < 0 && Math.hypot(pts[j][0] - a[0], pts[j][2] - a[2]) < 12) { group[j] = n; todo.push(j); } }
    n++;
  }
  return Array.from({ length: n }, (_, g) => pts.filter((p, i) => group[i] === g));
}
// For every enemy that is there from the start: can it see players standing on an insertion point?
// Returns one entry per enemy: null, or { dist, at, facing } for the nearest insertion spot in view and in range.
const USUAL_SIGHT = 50, NIGHT_SIGHT = 25, USUAL_FOV = 100;
function dangers() {
  if (!state.geo || !state.infils.length) return state.enemies.map(() => null);
  const key = state.geo.name + '|' + state.infils.length + '|' + state.night + '|' + state.insertion + '|' + state.insertionSpots.length + '|' + state.enemies.map(e => [e.pos, e.yaw, e.prone, e.vehicle, e.trigger, e.detectionRange, e.fov].join(',')).join(';');
  if (sight.key === key) return sight.cache;
  // Only the insertion the scenario chose counts, when it chose one and the editor knows where that is.
  const spot = state.insertion && state.insertionSpots.find(q => q.name === state.insertion);
  const groups = insertionGroups().filter(g => !spot || g.some(p => Math.hypot(p[0] - spot.pos[0], p[2] - spot.pos[2]) < 15));
  sight.key = key;
  return sight.cache = state.enemies.map(e => {
    if (e.trigger) return null;                                   // comes later, when the players are elsewhere
    const range = e.detectionRange > 0 ? e.detectionRange : (state.night && !e.nvg ? NIGHT_SIGHT : USUAL_SIGHT), eye = [e.pos[0], e.pos[1] + (e.vehicle ? 2.2 : e.prone ? 0.35 : 1.6), e.pos[2]];
    let best = null;
    for (const g of groups) for (const p of g) {
      const dist = Math.hypot(p[0] - e.pos[0], p[1] - e.pos[1], p[2] - e.pos[2]);
      if (dist > range || (best && dist >= best.dist)) continue;
      if (!canSee(eye, [p[0], p[1] + 1.6, p[2]])) continue;
      const turn = Math.abs(((Math.atan2(p[0] - e.pos[0], p[2] - e.pos[2]) * 180 / Math.PI - e.yaw) % 360 + 540) % 360 - 180);
      best = { dist, at: p, facing: turn <= (e.fov > 0 ? e.fov : USUAL_FOV) / 2 };
    }
    return best;
  });
}
function warningFor(e) {
  if (state.map && !e.fixed && !e.vehicle && !onGround(e.pos)) return 'not on walkable ground; the game will treat it as a fixed sentry';
  if (e.role === 'Patrol' && !e.fixed && e.patrol.length < 2) return 'needs at least 2 route stops, otherwise it wanders';
  if (!e.trigger && state.infils.some(p => Math.hypot(p[0] - e.pos[0], p[2] - e.pos[2]) < 20)) return 'within 20 m of an insertion point';
  const d = dangers()[state.enemies.indexOf(e)];
  if (d) return (d.facing ? 'looking straight at an insertion point ' : 'in view of an insertion point ') + Math.round(d.dist) + ' m away: players may be noticed as soon as they arrive'
    + (/bomber/i.test(e.type || '') ? ', and a bomber runs at whoever it sees' : '');
  return '';
}

