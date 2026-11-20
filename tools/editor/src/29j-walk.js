// ------------------------------------------------------------------ walk it through: test players on timed paths, and the inside/outside probe (ML-043)
// Up to four test players walk timed paths across the map; Rehearse (29d-rehearse.js) plays them with the events and checks
// of its rows, through the same plugin endpoint and save path. The sampling policy, said on the page as well:
//   - between two waypoints a test player moves in a straight line at an even speed; before the first waypoint he stands
//     at it, after the last he stays there, and two waypoints at one time are a jump;
//   - positions are sampled at the engine's own looks, every 0.5 s from 0 (Core Rehearsal.Tick), and at the moment a test
//     player goes down; a sample is written into the script only when somebody moved since the one before;
//   - the engine sees nothing between two samples. A straight stretch that goes into a zone or hold and out again (or out
//     and back in) between two samples is found here by testing it against every edge and floor bound of the place, and
//     listed as a warning to add a waypoint. It is never assumed to count.
// Heights are explicit numbers. A click takes the height typed for clicks and lands on the nearest floor only when the
// author ticks that; "Snap heights" moves a test player's waypoints onto the nearest floor only when pressed.
const WALK_MAX = 4, WALK_TICK = 0.5, WALK_SPEED = 3;   // test players; the engine's look in seconds; metres a second for a clicked waypoint's suggested time
state.walk = { players: [], sel: 0, picking: null, down: null, clickY: 0, snap: false, probe: null, probeY: 0, warnings: [], at: null };

// ----- pure parts: positions, samples, the script, problems, crossings, the probe, recorded tracks

const walkHas = p => !!p && Array.isArray(p.points) && p.points.length > 0;
const walkDown = p => (p && typeof p.down === 'number' && isFinite(p.down) ? p.down : null);
const walkAlive = (p, t) => { const d = walkDown(p); return d === null || t < d; };

// Where a test player stands at time t (waypoints { t, pos } in time order).
function walkPos(points, t) {
  if (!points.length) return null;
  if (t <= points[0].t) return points[0].pos.slice(0, 3);
  let i = 0;
  for (let k = 0; k < points.length; k++) if (points[k].t <= t) i = k;
  if (i === points.length - 1) return points[i].pos.slice(0, 3);
  const a = points[i], b = points[i + 1], f = (t - a.t) / (b.t - a.t);
  return [0, 1, 2].map(j => a.pos[j] + (b.pos[j] - a.pos[j]) * f);
}
// Every time a sample is taken: each half second from 0 up to the last waypoint (rounded up), and each moment a test player goes down.
function walkTimes(walk) {
  let end = 0;
  const extra = [];
  for (const p of walk.players) {
    if (!walkHas(p)) continue;
    for (const q of p.points) end = Math.max(end, num(q.t, 0));
    const d = walkDown(p);
    if (d !== null) { end = Math.max(end, d); extra.push(r2(d)); }
  }
  const n = Math.ceil(end / WALK_TICK - 1e-9), set = new Set(extra);
  for (let k = 0; k <= n; k++) set.add(k * WALK_TICK);
  return [...set].sort((a, b) => a - b);
}
// The living test players at time t: [{ k (which test player, from 0), pos }].
function walkAt(walk, t) {
  const out = [];
  if (!walk || typeof t !== 'number') return out;
  walk.players.forEach((p, k) => { if (walkHas(p) && walkAlive(p, t)) out.push({ k, pos: walkPos(p.points, t) }); });
  return out;
}
// The samples written into the script: [{ at, players: [[x, y, z], ...] }], one whenever somebody moved since the last.
function walkSamples(walk) {
  const out = [];
  if (!walk.players.some(walkHas)) return out;
  let last = null;
  for (const t of walkTimes(walk)) {
    const players = walkAt(walk, t).map(q => r3(q.pos)), key = JSON.stringify(players);
    if (key !== last) { out.push({ at: r2(t), players }); last = key; }
  }
  return out;
}
// The rows (events and checks) and the sampled paths as one missionlab-rehearsal/1 script, in time order: at one time the
// players' places come first, then the rows in their order. sources[i]: { row } (counted from 1) or { sample: true }.
function walkScript(walk, rows, meta) {
  const script = rehearseScript([], meta), entries = [];
  rows.forEach((row, i) => { const st = rehearseStep(row); if (st) entries.push({ st, src: { row: i + 1 }, kind: 1, n: entries.length }); });
  for (const s of walkSamples(walk)) entries.push({ st: { at: s.at, players: s.players }, src: { sample: true }, kind: 0, n: entries.length });
  entries.sort((a, b) => a.st.at - b.st.at || a.kind - b.kind || a.n - b.n);
  script.steps = entries.map(e => e.st);
  return { script, sources: entries.map(e => e.src) };
}
// What stops the walk from being run or saved, in the shape of rehearseProblems.
function walkProblems(walk, rows) {
  const out = [], bad = text => out.push({ row: 0, text });
  if (walk.players.length > WALK_MAX) bad('At most ' + WALK_MAX + ' test players can walk.');
  walk.players.forEach((p, k) => {
    const who = 'Test player ' + (k + 1);
    if (!walkHas(p)) { bad(who + ' has no waypoints yet: click the map to add some, or remove the player.'); return; }
    p.points.forEach((q, i) => {
      if (!(typeof q.t === 'number' && isFinite(q.t)) || q.t < 0 || q.t > 86400) bad(who + ', waypoint ' + (i + 1) + ': the time is from 0 to 86400 seconds.');
      else if (i && q.t < p.points[i - 1].t) bad(who + ', waypoint ' + (i + 1) + ': it comes before the waypoint above it.');
      if (!isVec(q.pos)) bad(who + ', waypoint ' + (i + 1) + ': the place needs x, height and z.');
    });
    if (p.down !== null && p.down !== undefined && (walkDown(p) === null || p.down < 0)) bad(who + ': "goes down at" is a time from 0 seconds, or empty.');
  });
  if (walk.players.length) rows.forEach((row, i) => { if (row.kind === 'players') bad('Row ' + (i + 1) + ' places the players by hand while test players walk paths: remove the row, or press "Turn player rows into paths".'); });
  return out;
}

// A zone or hold with a place to be in: a painted area, or the old circle around its anchor.
const walkPlaced = z => z && z.id > 0 && (z.when === 'zone' || z.when === 'hold') && ((z.area && z.area.parts && z.area.parts.length > 0) || (isVec(z.pos) && z.radius > 0));
// A point of path (stretches { t, pos }) whose membership differs from `was`, or null. Only stretches that take time are
// walked; a jump is not. Each straight stretch is cut where it meets an edge or a floor bound of the place (or the circle
// and its 2 m height limit), and the middle of every piece is tested with membershipInside, the rule the plugin uses.
function walkStretch(z, path, was) {
  const area = z.area && z.area.parts && z.area.parts.length ? z.area : null, box = area ? areaPrepare(area) : null, e = 0.02;
  for (let i = 1; i < path.length; i++) {
    const a = path[i - 1], b = path[i];
    if (!(b.t > a.t)) continue;
    const A = a.pos, B = b.pos, cuts = [0, 1];
    if (area) {
      if (Math.max(A[0], B[0]) < box.minX - e || Math.min(A[0], B[0]) > box.maxX + e || Math.max(A[2], B[2]) < box.minZ - e || Math.min(A[2], B[2]) > box.maxZ + e) continue;
      cuts.push(...areaSegmentCrossings(area, A, B));
      if (B[1] !== A[1]) for (const part of area.parts) for (const h of part.floor) cuts.push((h - A[1]) / (B[1] - A[1]));
    } else {
      const c = z.pos, dx = B[0] - A[0], dz = B[2] - A[2], fx = A[0] - c[0], fz = A[2] - c[2];
      const qa = dx * dx + dz * dz, qb = 2 * (fx * dx + fz * dz), qc = fx * fx + fz * fz - z.radius * z.radius, disc = qb * qb - 4 * qa * qc;
      if (qa > 0 && disc >= 0) cuts.push((-qb - Math.sqrt(disc)) / (2 * qa), (-qb + Math.sqrt(disc)) / (2 * qa));
      if (B[1] !== A[1]) for (const h of [c[1] - 2, c[1] + 2]) cuts.push((h - A[1]) / (B[1] - A[1]));
    }
    const s = cuts.filter(v => v >= 0 && v <= 1).sort((p, q) => p - q);
    for (let j = 1; j < s.length; j++) {
      const f = (s[j - 1] + s[j]) / 2, p = [0, 1, 2].map(k => A[k] + (B[k] - A[k]) * f);
      if (membershipInside(z, p) !== was) return p;
    }
    if (i < path.length - 1 && membershipInside(z, B) !== was) return B.slice(0, 3);   // a waypoint between the two samples
  }
  return null;
}
// Places the engine would not see a test player go through, or leave and come back to, between two samples:
// [{ player (from 0), from, to, id, at: [x, y, z], leaves }]. A change that lasts until the next sample is seen, so not listed.
function walkCrossings(walk, zones) {
  const out = [], places = zones.filter(walkPlaced);
  if (!places.length) return out;
  const times = walkTimes(walk);
  walk.players.forEach((p, k) => {
    if (!walkHas(p)) return;
    for (let i = 1; i < times.length; i++) {
      const t0 = times[i - 1], t1 = times[i];
      if (!walkAlive(p, t1)) break;
      const path = [{ t: t0, pos: walkPos(p.points, t0) }];
      for (const q of p.points) if (q.t > t0 && q.t <= t1 && isVec(q.pos)) path.push(q);
      path.push({ t: t1, pos: walkPos(p.points, t1) });
      for (const z of places) {
        const was = membershipInside(z, path[0].pos);
        if (was !== membershipInside(z, path[path.length - 1].pos)) continue;
        const at = walkStretch(z, path, was);
        if (at) out.push({ player: k, from: t0, to: t1, id: z.id, at: r3(at), leaves: was });
      }
    }
  });
  return out;
}
function walkCrossingText(c, name) {
  const secs = t => String(r2(t)) + ' s', near = 'near (' + c.at[0].toFixed(1) + ', ' + c.at[2].toFixed(1) + ') at ' + c.at[1].toFixed(1) + ' m';
  return 'Test player ' + (c.player + 1) + (c.leaves
    ? ' leaves ' + name(c.id) + ' and comes back between ' + secs(c.from) + ' and ' + secs(c.to) + ', so the rehearsal does not see it leave. If it should count, add a waypoint ' + near + '.'
    : ' passes through ' + name(c.id) + ' between ' + secs(c.from) + ' and ' + secs(c.to) + ' without a sample inside it, so the rehearsal never sees it there. If it should count, add a waypoint ' + near + '.');
}
// The probe: for every zone and hold with a place, whether the point is inside and why (membershipExplain, 21a-area.js).
function walkProbe(pos, zones) {
  return zones.filter(walkPlaced).map(z => Object.assign({ id: z.id }, membershipExplain(z, pos)));
}

// Interior waypoints that lie on the straight line (in place and time) between their neighbours, within tol metres, are left out.
function walkSimplify(points, tol) {
  if (points.length <= 2) return points.slice();
  const keep = [points[0]];
  let a = 0;
  for (let i = 2; i < points.length; i++) {
    const A = points[a], C = points[i];
    let fits = C.t > A.t;
    for (let j = a + 1; j < i && fits; j++) {
      const B = points[j], f = (B.t - A.t) / (C.t - A.t);
      fits = [0, 1, 2].every(k => Math.abs(A.pos[k] + (C.pos[k] - A.pos[k]) * f - B.pos[k]) <= tol);
    }
    if (!fits) { keep.push(points[i - 1]); a = i - 1; }
  }
  keep.push(points[points.length - 1]);
  return keep;
}
// "Players stand" rows back into paths: possible when every such row places the same number of players (1 to 4). Between
// two rows on the half-second grid a player walks the last half second, as a walk turned into samples did; between rows
// off the grid he jumps at the later row's time, as the rehearsal plays them. null when the rows cannot be paths.
function walkFromRows(rows) {
  const placed = rows.filter(r => r.kind === 'players' && Array.isArray(r.players)).map(r => ({ t: num(r.at, 0), players: r.players }));
  if (!placed.length) return null;
  const n = placed[0].players.length;
  if (n < 1 || n > WALK_MAX || placed.some(r => r.players.length !== n || !r.players.every(isVec))) return null;
  const grid = t => Math.abs(t / WALK_TICK - Math.round(t / WALK_TICK)) < 1e-6;
  const players = [];
  for (let k = 0; k < n; k++) {
    const points = [];
    placed.forEach((r, i) => {
      const prev = placed[i - 1];
      if (prev && r.t - prev.t > WALK_TICK + 1e-6) points.push({ t: grid(prev.t) && grid(r.t) ? r2(r.t - WALK_TICK) : r.t, pos: prev.players[k].slice(0, 3) });
      points.push({ t: r.t, pos: r.players[k].slice(0, 3) });
    });
    players.push({ points: walkSimplify(points, 0.02), down: null });
  }
  return { players, rest: rows.filter(r => r.kind !== 'players') };
}
// The players' tracks of a missionlab-replay/1 recording, at most four, as paths. Positions only: a recording is no evidence
// of intel, doors, enemies going down, the alarm or deaths, so those stay for the author to add as rows.
// { players: [{ id, name, points, from, to }], end, more (players recorded beyond four) }
function walkTracks(obj) {
  if (!obj || obj.schema !== 'missionlab-replay/1' || !Array.isArray(obj.frames)) throw new Error('it is not a MissionLab replay (missionlab-replay/1)');
  const names = Array.isArray(obj.players) ? obj.players : [], tracks = new Map();
  let end = 0;
  for (const f of obj.frames) {
    if (!Array.isArray(f) || typeof f[0] !== 'number' || !isFinite(f[0]) || !Array.isArray(f[1])) continue;
    end = Math.max(end, f[0]);
    for (const q of f[1]) {
      if (!Array.isArray(q) || q.length < 4 || ![1, 2, 3].every(k => typeof q[k] === 'number' && isFinite(q[k]))) continue;
      if (!tracks.has(q[0])) tracks.set(q[0], []);
      tracks.get(q[0]).push({ t: r2(f[0]), pos: r3([q[1], q[2], q[3]]) });
    }
  }
  const players = [...tracks.entries()].slice(0, WALK_MAX).map(([id, pts]) => {
    pts.sort((a, b) => a.t - b.t);
    return { id, name: typeof names[id] === 'string' && names[id] ? names[id] : 'Recorded player ' + id, points: walkSimplify(pts, 0.05), from: pts[0].t, to: pts[pts.length - 1].t };
  });
  return { players, end, more: Math.max(0, tracks.size - WALK_MAX) };
}

// ----- floors
// The floor heights under a point of the ground plan, lowest first (none without a map).
function walkFloors(x, z) { return state.map ? [...new Set(levelsAt(x, z))].sort((a, b) => a - b) : []; }
function walkNearestFloor(x, z, y) { const f = walkFloors(x, z); return f.length ? f.reduce((b, v) => (Math.abs(v - y) < Math.abs(b - y) ? v : b)) : null; }
// '' when a place stands on a floor (within 1 m, as onGround has it) or there is no map; else what the author should know.
function walkFloorNote(pos) {
  if (!state.map || !isVec(pos)) return '';
  const f = walkFloors(pos[0], pos[2]);
  if (f.some(y => Math.abs(y - pos[1]) <= 1)) return '';
  return f.length ? 'No floor within 1 m of this height; floors here are at ' + [...new Set(f.map(y => y.toFixed(1)))].join(', ') + ' m.' : 'Nobody can stand here: there is no floor under it.';
}

// ----- the panel
const walkName = id => { const z = state.zones.find(q => q.id === id); return z ? 'trigger ' + rhTriggerLabel(z) : 'trigger ' + id; };
// A change to the walk: the last answer no longer describes it, and the crossings are worked out again.
function walkEdited() {
  const w = state.walk;
  state.rehearse.failed.clear();
  w.warnings = walkCrossings(w, rhTriggers());
  rhProblemsShow(); walkRender(); invalidate();
}
function walkSortPoints(p) { p.points = p.points.map((q, i) => ({ q, i })).sort((a, b) => num(a.q.t, 0) - num(b.q.t, 0) || a.i - b.i).map(x => x.q); }
function walkRender() {
  const w = state.walk, tabs = $('walkTabs'), list = $('walkPoints');
  if (w.sel >= w.players.length) w.sel = Math.max(0, w.players.length - 1);
  const p = w.players[w.sel];
  tabs.textContent = '';
  w.players.forEach((q, k) => {
    const b = rhEl('button', 'btn' + (k === w.sel ? ' on' : ''), 'Test player ' + (k + 1)); b.type = 'button';
    b.setAttribute('aria-pressed', k === w.sel ? 'true' : 'false');
    b.addEventListener('click', () => { w.sel = k; walkStopPick(); walkRender(); invalidate(); });
    tabs.append(b);
  });
  $('walkAdd').disabled = w.players.length >= WALK_MAX;
  $('walkRemove').disabled = !p; $('walkPick').disabled = !p; $('walkSnapAll').disabled = !p || !state.map; $('walkDown').disabled = !p;
  $('walkPick').textContent = w.picking === 'path' ? 'Stop adding waypoints' : 'Add waypoints by clicking';
  $('walkProbePick').textContent = w.picking === 'probe' ? 'Stop probing' : 'Click the map';
  $('walkDown').value = p && walkDown(p) !== null ? String(p.down) : '';
  list.textContent = '';
  if (p) p.points.forEach((q, i) => {
    const li = rhEl('li'), set = (k, v) => { if (k === 't') q.t = isFinite(v) ? Math.max(0, r2(v)) : 0; else { q.pos = q.pos.slice(0, 3); q.pos[k] = isFinite(v) ? r2(v) : 0; } walkSortPoints(p); walkEdited(); };
    const del = rhEl('button', 'btn', '×'); del.type = 'button'; del.title = 'Remove this waypoint'; del.setAttribute('aria-label', 'Remove waypoint ' + (i + 1));
    del.addEventListener('click', () => { p.points.splice(i, 1); walkEdited(); });
    const who = 'Waypoint ' + (i + 1) + ': ';
    li.append(rhEl('b', '', String(i + 1)), rhNumber(num(q.t, 0), 0, 86400, 0.5, v => set('t', v), who + 'seconds from the mission\'s start'),
      rhNumber(q.pos[0], -100000, 100000, 0.1, v => set(0, v), who + 'x'), rhNumber(q.pos[1], -1000, 1000, 0.1, v => set(1, v), who + 'height'),
      rhNumber(q.pos[2], -100000, 100000, 0.1, v => set(2, v), who + 'z'), del);
    const note = walkFloorNote(q.pos);
    if (note) li.append(rhEl('span', 'note', note));
    list.append(li);
  });
  if (p && !p.points.length) list.append(rhEl('li', 'empty', 'No waypoints yet: press "Add waypoints by clicking" and click the map, in walking order.'));
  const n = w.players.filter(walkHas).length, rolled = state.rehearse.players;
  $('walkNote').textContent = !n ? '' : 'The mission is rolled for ' + rolled + (rolled === 1 ? ' player' : ' players') + ' (Players, in Rehearse); ' + n + (n === 1 ? ' test player walks' : ' test players walk') + '. ' +
    (n < rolled ? 'Players without a path count as not alive, which matters for holds that need everyone.' : n > rolled ? 'More test players walk than the mission is rolled for; raise Players if they should count when the dice decide which enemies take part.' : '');
  const warn = $('walkWarn');
  warn.textContent = '';
  for (const c of w.warnings) {
    const li = rhEl('li', '', walkCrossingText(c, walkName));
    li.addEventListener('click', () => { w.sel = c.player; w.at = c.from; walkRender(); invalidate(); });
    warn.append(li);
  }
  walkProbeShow();
}
function walkProbeShow() {
  const w = state.walk, out = $('walkProbeOut');
  out.textContent = '';
  if (!w.probe) return;
  const answers = walkProbe(w.probe, rhTriggers());
  if (!answers.length) { out.append(rhEl('li', '', 'There is no zone or hold trigger to be inside yet.')); return; }
  for (const a of answers) {
    const li = rhEl('li');
    li.append(rhEl('span', 'mark ' + (a.inside ? 'ok' : 'bad'), a.inside ? 'in' : 'out'), rhEl('span', '', walkName(a.id) + ': ' + a.why));
    out.append(li);
  }
  const note = walkFloorNote(w.probe);
  if (note) out.append(rhEl('li', 'note', note));
}
function walkProbeFields() { const p = state.walk.probe; for (const [id, k] of [['walkProbeX', 0], ['walkProbeY', 1], ['walkProbeZ', 2]]) $(id).value = p ? String(p[k]) : ''; }
function walkProbeTyped() {
  const v = ['walkProbeX', 'walkProbeY', 'walkProbeZ'].map(id => ($(id).value === '' ? NaN : Number($(id).value)));
  state.walk.probe = v.every(isFinite) ? r3(v) : null;
  if (isFinite(v[1])) state.walk.probeY = v[1];
  walkProbeShow(); invalidate();
}
function walkProbeAt(x, z) {
  const w = state.walk;
  let y = w.probe ? w.probe[1] : w.probeY;
  if (w.snap) { const f = walkNearestFloor(x, z, y); if (f !== null) y = f; }
  w.probe = r3([x, y, z]); w.probeY = w.probe[1];
  walkProbeFields(); walkProbeShow(); invalidate();
}
const walkStride = (a, b) => Math.max(WALK_TICK, Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]) / WALK_SPEED / WALK_TICK - 1e-9) * WALK_TICK);
function walkAddPoint(x, z) {
  const w = state.walk, p = w.players[w.sel];
  if (!p) { walkStopPick(); return; }
  let y = w.clickY;
  if (w.snap) {
    const f = walkNearestFloor(x, z, y);
    if (f === null) { say('There is no floor here to land on. Untick "Clicks land on the nearest floor" to put the waypoint at the typed height.'); return; }
    y = f;
  }
  const last = p.points[p.points.length - 1], pos = r3([x, y, z]), t = last ? r2(last.t + walkStride(last.pos, pos)) : 0;
  p.points.push({ t, pos });
  w.clickY = pos[1]; $('walkY').value = String(w.clickY);
  say('Waypoint ' + p.points.length + ' of test player ' + (w.sel + 1) + ' at ' + t + ' s and ' + pos[1] + ' m up (the time assumes walking at ' + WALK_SPEED + ' m/s; change it in the list). Click the next one; Esc stops.');
  walkEdited();
}
function walkStartPick(kind) {
  const w = state.walk;
  if (state.mode === '3d') { say('Switch to the 2D map to click places.'); return; }
  rhStopPicking();
  w.picking = kind; w.down = null;
  say(kind === 'probe' ? 'Click the map to probe a point at the height in the probe. Esc stops.' : 'Click the map where test player ' + (w.sel + 1) + ' walks, in order. Esc stops.');
  walkRender(); invalidate();
}
function walkStopPick() { const w = state.walk; if (!w.picking) return; w.picking = null; w.down = null; walkRender(); invalidate(); }
// The paths belong to the trace: Clear and Open test file in Rehearse take them away with it.
function walkClear() { const w = state.walk; walkStopPick(); w.players = []; w.sel = 0; w.warnings = []; w.at = null; walkRender(); }
function walkAddPlayer() {
  const w = state.walk;
  if (w.players.length >= WALK_MAX) return;
  w.players.push({ points: [], down: null }); w.sel = w.players.length - 1;
  walkEdited(); walkStartPick('path');
}
function walkRemovePlayer() {
  const w = state.walk;
  if (!w.players[w.sel]) return;
  w.players.splice(w.sel, 1); w.sel = Math.max(0, w.sel - 1); walkStopPick(); walkEdited();
}
function walkSnapAll() {
  const w = state.walk, p = w.players[w.sel];
  if (!p) return;
  let moved = 0, none = 0;
  for (const q of p.points) {
    const f = walkNearestFloor(q.pos[0], q.pos[2], q.pos[1]);
    if (f === null) { none++; continue; }
    if (Math.abs(f - q.pos[1]) >= 0.005) { q.pos = [q.pos[0], r2(f), q.pos[2]]; moved++; }
  }
  $('walkStatus').textContent = moved + (moved === 1 ? ' waypoint was' : ' waypoints were') + ' moved onto the nearest floor.' + (none ? ' ' + none + ' stand where there is no floor and were left as they are.' : '');
  walkEdited();
}
async function walkTrackOpen(f) {
  const w = state.walk;
  try {
    const got = walkTracks(JSON.parse(await f.text()));
    if (!got.players.length) throw new Error('it has no recorded player positions');
    walkStopPick();
    w.players = got.players.map(q => ({ points: q.points, down: null })); w.sel = 0;
    const gone = got.players.filter(q => got.end - q.to > 2).map(q => q.name + ' is not recorded after ' + mmss(q.to));
    $('walkStatus').textContent = 'Took the tracks of ' + got.players.map((q, k) => 'test player ' + (k + 1) + ': ' + q.name + ' (' + q.points.length + ' waypoints)').join(', ') + ' from ' + f.name +
      '. Times are the recording\'s own seconds from its start. The recording shows only where they walked: it is no evidence of intel picked up, doors opened, enemies going down, the alarm or deaths, so add those yourself as rows in Rehearse.' +
      (gone.length ? ' ' + gone.join('; ') + ': the recording does not say whether he went down or left, so set "Goes down at" if he went down.' : '') +
      (got.more ? ' ' + got.more + ' more ' + (got.more === 1 ? 'player was' : 'players were') + ' recorded; a walkthrough has at most ' + WALK_MAX + '.' : '');
  } catch (e) { $('walkStatus').textContent = f.name + ' could not be used: ' + e.message + '. Nothing was changed.'; }
  walkEdited();
}
function walkTakeRows() {
  const r = state.rehearse, got = walkFromRows(r.rows);
  if (!got) { $('walkStatus').textContent = 'Only "Players stand" rows that each place the same number of players, 1 to ' + WALK_MAX + ', can become paths.'; return; }
  walkStopPick(); rhStopPicking();
  state.walk.players = got.players; state.walk.sel = 0;
  const before = r.rows.length;
  r.rows = got.rest; r.focus = null;
  $('walkStatus').textContent = (before - got.rest.length) + ' "Players stand" rows became the paths of ' + got.players.length + (got.players.length === 1 ? ' test player.' : ' test players.');
  rhEdited(); rhRender(); walkEdited();
}

// Ahead of the map's own handler (capture), so a click while placing neither selects nor drags anything.
canvas.addEventListener('pointerdown', ev => {
  const w = state.walk;
  if (!w.picking || ev.button !== 0) return;
  ev.stopImmediatePropagation(); w.down = [ev.clientX, ev.clientY];
}, true);
canvas.addEventListener('pointerup', ev => {
  const w = state.walk, down = w.down;
  if (!w.picking || !down || ev.button !== 0) return;
  w.down = null;
  if (Math.hypot(ev.clientX - down[0], ev.clientY - down[1]) >= 4) return;
  const b = canvas.getBoundingClientRect(), [x, z] = toWorld(ev.clientX - b.left, ev.clientY - b.top);
  if (w.picking === 'probe') walkProbeAt(x, z); else walkAddPoint(x, z);
});
window.addEventListener('keydown', ev => { if (ev.key === 'Escape' && state.walk.picking) walkStopPick(); });

$('walkAdd').addEventListener('click', walkAddPlayer);
$('walkRemove').addEventListener('click', walkRemovePlayer);
$('walkPick').addEventListener('click', () => (state.walk.picking === 'path' ? walkStopPick() : walkStartPick('path')));
$('walkSnapAll').addEventListener('click', walkSnapAll);
$('walkY').addEventListener('change', () => { const v = Number($('walkY').value); state.walk.clickY = isFinite(v) ? r2(v) : 0; $('walkY').value = String(state.walk.clickY); });
$('walkSnap').addEventListener('change', () => { state.walk.snap = !!$('walkSnap').checked; });
$('walkDown').addEventListener('change', () => {
  const p = state.walk.players[state.walk.sel], v = $('walkDown').value;
  if (!p) return;
  p.down = v === '' ? null : Math.max(0, r2(Number(v)));
  walkEdited();
});
$('walkProbePick').addEventListener('click', () => (state.walk.picking === 'probe' ? walkStopPick() : walkStartPick('probe')));
for (const id of ['walkProbeX', 'walkProbeY', 'walkProbeZ']) $(id).addEventListener('change', walkProbeTyped);
$('walkProbeFloor').addEventListener('click', () => {
  const w = state.walk;
  if (!w.probe) { $('walkStatus').textContent = 'Probe a point first.'; return; }
  const f = walkNearestFloor(w.probe[0], w.probe[2], w.probe[1]);
  if (f === null) { $('walkStatus').textContent = 'There is no floor under the probe' + (state.map ? '.' : ': open a map first.'); return; }
  w.probe = r3([w.probe[0], f, w.probe[2]]); w.probeY = w.probe[1];
  walkProbeFields(); walkProbeShow(); invalidate();
});
$('walkProbeClear').addEventListener('click', () => { state.walk.probe = null; walkProbeFields(); walkProbeShow(); invalidate(); });
$('walkTrack').addEventListener('click', () => $('walkTrackFile').click());
$('walkTrackFile').addEventListener('change', async ev => { const f = ev.target.files && ev.target.files[0]; ev.target.value = ''; if (f) await walkTrackOpen(f); });
$('walkRows').addEventListener('click', walkTakeRows);
// The crossings depend on the triggers' places, which are edited elsewhere: worked out again when the panel is used.
$('walkBox').addEventListener('pointerenter', () => { const w = state.walk; if (w.players.length) w.warnings = walkCrossings(w, rhTriggers()); walkRender(); invalidate(); });

walkRender();
expose({ walkPos, walkTimes, walkAt, walkSamples, walkScript, walkProblems, walkCrossings, walkCrossingText, walkProbe, walkSimplify, walkFromRows, walkTracks, walkAddPlayer, walkAddPoint, walkTakeRows, walkClear, walkRender, walkEdited, WALK_TICK });   // the walkthrough (ML-043), for automated tests
