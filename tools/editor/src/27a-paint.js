// ------------------------------------------------------------------ painting areas (ML-027 B and C)
// Brush, eraser, rectangle, polygon and corner handles for the painted area of a trigger, a place or a placement region.
// Whatever the tool, the result is the one shape of 21a-area.js, and every ring is put in one order (outlines anticlockwise
// and holes clockwise with x to the right and z up, each from its lowest x, then lowest z, corner), so the same shape is
// saved the same whichever tool drew it.
//
// Brush and eraser work on cells of 0.2 m, numbered from the map's origin so that every stroke and every part share one grid.
// The parts of the chosen floor band that a stroke reaches are filled into cells (a cell counts when its middle is inside),
// the stroke adds or clears cells, and the cells are traced back into outlines and holes. The tolerance:
//   - a traced edge stays within 0.1 m of the middle of every cell side it replaces, so straight and 45 degree walls come
//     out as single edges, and no edge moves by more than 0.1 m;
//   - where straightening would make two rings touch, those rings keep their exact cell outline instead, so a gap of one
//     cell (0.2 m) or more between pieces, or between a hole and its outline, is never bridged;
//   - where two cells meet only at a corner, one of them is cleared, so pieces never hang together by a point (a ring
//     through such a corner touches itself, which the file refuses);
//   - a piece below the file's minimum (0.25 square metres for an outline, 0.05 for a hole) is left out, and say() tells so.
// Parts on other bands, and parts of this band the stroke does not reach, are kept exactly as they are.
// Nothing here talks to the game: a finished change goes through areaChanged() like any other edit.
const PAINT_CELL = 0.2, PAINT_SLACK = 0.1;                       // metres: the raster, and how far tracing may straighten
const PAINT_SIZE_MIN = 0.4, PAINT_SIZE_MAX = 30, PAINT_MAX_CELLS = 4000000;
const PAINT_OFF = 600000, PAINT_KEY = 1200000;                   // cell keys: (i + OFF) * KEY + j + OFF; the map is at most 100 km across
state.paint = { tool: null, size: 1.5, tip: 'round', bottom: -0.5, top: 2.6, target: null };
let paintGesture = null, paintPointer = null;                    // the stroke or shape under way; the pointer on the map

// ----- what is painted: { kind: 'trigger' | 'place' | 'placement', index }
const AREA_HOLDERS = { trigger: () => state.zones, place: () => state.places, placement: () => state.placement };
function areaHolder(target) {
  const list = target && AREA_HOLDERS[target.kind] ? AREA_HOLDERS[target.kind]() : null;
  return (list && list[target.index]) || null;
}
// The target's area, made when it has none yet. The writers leave an area without parts out, so this is never saved empty.
function areaOfTarget(target) {
  const h = areaHolder(target); if (!h) return null;
  if (!h.area || !Array.isArray(h.area.parts)) h.area = { parts: [] };
  return h.area;
}
const paintIs = (kind, index) => !!state.paint.target && state.paint.target.kind === kind && state.paint.target.index === index;
const paintSameBand = (part, band) => Math.abs(part.floor[0] - band[0]) < 0.005 && Math.abs(part.floor[1] - band[1]) < 0.005;
const paintBand = () => [state.paint.bottom, state.paint.top];

// The floor the author is looking at: the highest floor shown under the middle of the view, from just below it (stairs and
// thresholds) to just under the floor above. Without ground there, the height of what is painted, else 0.
function floorBand() {
  const at = state.mode === '3d' ? view3d.focus() : [state.view.cx, state.view.cz];
  let floor = state.map ? groundAt(at[0], at[1]) : null;
  if (floor === null) { const h = areaHolder(state.paint.target); floor = h && isVec(h.pos) ? h.pos[1] : 0; }
  return [r2(floor - 0.5), r2(floor + 2.6)];
}
function setPaintBand(bottom, top) {
  bottom = r2(+bottom); top = r2(+top);
  if (!isFinite(bottom) || !isFinite(top)) { say('A floor band is two heights in metres.'); return false; }
  if (top <= bottom) { say('The top of the floor band must be above its bottom.'); return false; }
  if (top - bottom > AREA_MAX_FLOOR) { say('A floor band covers at most ' + AREA_MAX_FLOOR + ' m of height; paint taller places as several parts.'); return false; }
  state.paint.bottom = bottom; state.paint.top = top;
  return true;
}

function areaEditStart(target, band) {
  if (!target || !areaHolder(target)) return false;
  const h = areaHolder(target);
  state.paint.target = { kind: target.kind, index: target.index, id: h.id };
  areaOfTarget(state.paint.target);
  const b = Array.isArray(band) && band.length === 2 ? band : floorBand();
  if (!setPaintBand(b[0], b[1])) { const f = floorBand(); setPaintBand(f[0], f[1]); }
  if (!state.paint.tool) state.paint.tool = 'brush';
  paintGestureDrop();
  if (target.kind === 'trigger') showTab('triggers');
  // A focused button inside the trigger list keeps the list from being rebuilt, and with it the painting controls.
  if (document.activeElement && typeof document.activeElement.blur === 'function') document.activeElement.blur();
  if (state.mode === '3d') say('Painting works on the flat map: switch to 2D. The 3D view shows the painted volumes.');
  syncSide(); invalidate(); view3d.syncEnemies();
  return true;
}
function areaEditStop() {
  paintGestureDrop(); state.paint.target = null; state.paint.tool = null;
  syncSide(); invalidate(); view3d.syncEnemies();
}
// Called once after every finished stroke, shape or corner edit. Other parts may wrap it.
function areaChanged(target) { changed(); }
// Back to a circle (triggers): the area goes, as one undo step.
function areaClear(target) {
  const h = areaHolder(target); if (!h) return;
  const had = !!(h.area && h.area.parts && h.area.parts.length);
  if (state.paint.target && state.paint.target.kind === target.kind && state.paint.target.index === target.index) { state.paint.target = null; state.paint.tool = null; paintGestureDrop(); }
  if (!had) { h.area = null; syncSide(); invalidate(); view3d.syncEnemies(); return; }
  willChange('Back to a circle: ' + areaTargetName(target)); h.area = null; areaChanged(target);
  say('The painted area is gone and the circle counts again. Undo brings the area back.');
}
// The painted thing in words, for the undo history: 'zone 2', 'place "Yard"', 'placement area "North"'.
function areaTargetName(target) {
  const h = areaHolder(target);
  if (!h) return 'the area';
  return target.kind === 'trigger' ? trigTitle(h).toLowerCase() : (target.kind === 'place' ? 'place "' : 'placement area "') + (h.name || h.id) + '"';
}
// What each finished paint edit is called in the history, by the word its refusal message starts with.
const PAINT_STEPS = { 'That stroke': 'Paint on ', 'That erasing': 'Erase from ', 'That rectangle': 'Add a rectangle to ', 'That polygon': 'Add a polygon to ',
  'That corner move': 'Move a corner of ', 'Removing that corner': 'Remove a corner of ', 'That corner': 'Add a corner to ', 'That move': 'Move the area of ',
  'That turn': 'Turn the area of ', 'Deleting that part': 'Delete a part of ', 'Adding a floor': 'Add a floor to ' };

// ----- the one order every tool writes
function paintRing(ring, outline) {
  const out = [];
  for (const p of ring) { const q = [r2(p[0]) || 0, r2(p[1]) || 0], last = out[out.length - 1]; if (!last || last[0] !== q[0] || last[1] !== q[1]) out.push(q); }
  while (out.length > 1 && out[0][0] === out[out.length - 1][0] && out[0][1] === out[out.length - 1][1]) out.pop();
  if (out.length > 2 && (areaShoelace(areaFlat(out)) > 0) !== outline) out.reverse();
  let k = 0;
  for (let i = 1; i < out.length; i++) if (out[i][0] < out[k][0] || (out[i][0] === out[k][0] && out[i][1] < out[k][1])) k = i;
  return out.slice(k).concat(out.slice(0, k));
}
function paintPart(part) {
  return { floor: [r2(part.floor[0]) || 0, r2(part.floor[1]) || 0], outline: paintRing(part.outline, true),
    holes: (part.holes || []).map(h => paintRing(h, false)).filter(h => h.length).sort((a, b) => a[0][0] - b[0][0] || a[0][1] - b[0][1]) };
}
const paintCopy = parts => JSON.parse(JSON.stringify(parts));
function paintBox(ring) {
  const b = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity };
  for (const p of ring) { b.minX = Math.min(b.minX, p[0]); b.maxX = Math.max(b.maxX, p[0]); b.minZ = Math.min(b.minZ, p[1]); b.maxZ = Math.max(b.maxZ, p[1]); }
  return b;
}
// '' when the parts are within the file's limits, else why not.
function paintLimits(parts) {
  if (parts.length > AREA_MAX_PARTS) return 'an area has at most ' + AREA_MAX_PARTS + ' parts, and this would make ' + parts.length;
  let total = 0;
  for (const p of parts) for (const r of [p.outline, ...(p.holes || [])]) {
    if (r.length > AREA_MAX_RING) return 'a ring has at most ' + AREA_MAX_RING + ' corners, and this would make one of ' + r.length + '; paint it as several parts or with fewer details';
    total += r.length;
  }
  if (total > AREA_MAX_POINTS) return 'an area has at most ' + AREA_MAX_POINTS + ' corners, and this would make ' + total;
  return '';
}
// One finished edit: refused with a message when it breaks a limit, else one undo step and then areaChanged.
function paintCommit(target, parts, what, more) {
  const holder = areaHolder(target);
  if (!holder) return false;
  const why = paintLimits(parts);
  if (why) { say(what + ' was not made: ' + why + '.'); return false; }
  willChange((PAINT_STEPS[what] || 'Change the area of ') + areaTargetName(target));
  holder.area = { parts };
  if (more) more(holder);
  areaPrepare(holder.area);
  areaChanged(target);
  return true;
}
// The anchor (label, rally centre, fallback destination) of a freshly painted area may lie outside it: offer to move it.
const paintAnchorOutside = target => { const h = areaHolder(target), a = h && h.area; return !!(a && a.parts && a.parts.length && isVec(h.pos) && !areaContains(a, h.pos)); };
function paintTell(target, before, said) {
  const text = [said];
  if (!before && paintAnchorOutside(target)) text.push('The anchor (its label, the rally point and where orders fall back to) lies outside the area: press "Move the anchor inside" to put it on a point inside.');
  const s = text.filter(Boolean).join(' ');
  if (s) say(s);
}

// ----- brush and eraser
function paintStrokeNew(erase) {
  return { erase: !!erase, size: Math.max(PAINT_SIZE_MIN, Math.min(PAINT_SIZE_MAX, +state.paint.size || 1.5)), tip: state.paint.tip === 'square' ? 'square' : 'round',
    cells: new Set(), minI: Infinity, maxI: -Infinity, minJ: Infinity, maxJ: -Infinity, path: [] };
}
// Whether the segment a-b meets the square of half size h around (cx, cz): the square tip swept along the stroke.
function paintSegBox(a, b, cx, cz, h) {
  let t0 = 0, t1 = 1;
  const d = [b[0] - a[0], b[1] - a[1]], lo = [cx - h - a[0] - 1e-9, cz - h - a[1] - 1e-9], hi = [cx + h - a[0] + 1e-9, cz + h - a[1] + 1e-9];
  for (let k = 0; k < 2; k++) {
    if (Math.abs(d[k]) < 1e-12) { if (lo[k] > 0 || hi[k] < 0) return false; continue; }
    let u = lo[k] / d[k], v = hi[k] / d[k];
    if (u > v) { const s = u; u = v; v = s; }
    t0 = Math.max(t0, u); t1 = Math.min(t1, v);
    if (t0 > t1) return false;
  }
  return true;
}
// Every cell whose middle the tip covers on its way from a to b.
function paintSweep(stroke, a, b) {
  const h = stroke.size / 2, C = PAINT_CELL;
  stroke.path.push(b);
  const i0 = Math.floor((Math.min(a[0], b[0]) - h) / C), i1 = Math.floor((Math.max(a[0], b[0]) + h) / C);
  const j0 = Math.floor((Math.min(a[1], b[1]) - h) / C), j1 = Math.floor((Math.max(a[1], b[1]) + h) / C);
  if (Math.abs(i0) > PAINT_OFF - 10 || Math.abs(i1) > PAINT_OFF - 10 || Math.abs(j0) > PAINT_OFF - 10 || Math.abs(j1) > PAINT_OFF - 10) return;
  for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
    const cx = (i + 0.5) * C, cz = (j + 0.5) * C;
    if (stroke.tip === 'square' ? !paintSegBox(a, b, cx, cz, h) : areaDistance2(cx, cz, a[0], a[1], b[0], b[1]) > h * h + 1e-9) continue;
    stroke.cells.add((i + PAINT_OFF) * PAINT_KEY + j + PAINT_OFF);
    if (i < stroke.minI) stroke.minI = i; if (i > stroke.maxI) stroke.maxI = i;
    if (j < stroke.minJ) stroke.minJ = j; if (j > stroke.maxJ) stroke.maxJ = j;
  }
}
// Fills a part into the cells, row by row: even-odd over its outline and holes, so the holes stay empty.
function paintFill(r, part) {
  const rings = [part.outline, ...(part.holes || [])];
  for (let j = 0; j < r.h; j++) {
    const z = (r.j0 + j + 0.5) * PAINT_CELL, xs = [];
    for (const ring of rings) for (let k = 0, n = ring.length, prev = n - 1; k < n; prev = k++) {
      const a = ring[prev], b = ring[k];
      if ((a[1] > z) !== (b[1] > z)) xs.push(a[0] + (z - a[1]) * (b[0] - a[0]) / (b[1] - a[1]));
    }
    xs.sort((p, q) => p - q);
    for (let s = 0; s + 1 < xs.length; s += 2) {
      const from = Math.max(0, Math.ceil(xs[s] / PAINT_CELL - 0.5) - r.i0), to = Math.min(r.w - 1, Math.floor(xs[s + 1] / PAINT_CELL - 0.5) - r.i0);
      for (let i = from; i <= to; i++) r.on[j * r.w + i] = 1;
    }
  }
}
// The parts after a stroke: { parts, dropped } (parts null when an eraser met nothing), or { error }.
function paintStrokeParts(area, stroke, band) {
  const C = PAINT_CELL, keep = [], touched = [];
  const x0 = (stroke.minI - 1) * C, x1 = (stroke.maxI + 2) * C, z0 = (stroke.minJ - 1) * C, z1 = (stroke.maxJ + 2) * C;
  for (const part of area.parts) {
    const b = paintBox(part.outline);
    if (paintSameBand(part, band) && b.maxX >= x0 && b.minX <= x1 && b.maxZ >= z0 && b.minZ <= z1) touched.push(part); else keep.push(part);
  }
  if (stroke.erase && !touched.length) return { parts: null, dropped: 0 };
  let i0 = stroke.minI, i1 = stroke.maxI, j0 = stroke.minJ, j1 = stroke.maxJ;
  for (const part of touched) {
    const b = paintBox(part.outline);
    i0 = Math.min(i0, Math.floor(b.minX / C)); i1 = Math.max(i1, Math.floor(b.maxX / C)); j0 = Math.min(j0, Math.floor(b.minZ / C)); j1 = Math.max(j1, Math.floor(b.maxZ / C));
  }
  i0 -= 2; j0 -= 2; i1 += 2; j1 += 2;
  if ((i1 - i0 + 1) * (j1 - j0 + 1) > PAINT_MAX_CELLS) return { error: 'That stroke reaches over too much ground at once for cells of 0.2 m. Paint in smaller pieces, or use the rectangle or the polygon.' };
  const r = { i0, j0, w: i1 - i0 + 1, h: j1 - j0 + 1 };
  r.on = new Uint8Array(r.w * r.h);
  for (const part of touched) paintFill(r, part);
  const set = stroke.erase ? 0 : 1;
  for (const key of stroke.cells) { const i = Math.floor(key / PAINT_KEY) - PAINT_OFF, j = key % PAINT_KEY - PAINT_OFF; r.on[(j - j0) * r.w + (i - i0)] = set; }
  const traced = paintTrace(r, band, keep.filter(p => paintSameBand(p, band)));
  return { parts: keep.concat(traced.parts), dropped: traced.dropped };
}
// Cells back to parts: outlines with their holes, straightened within the tolerance at the top of this file.
function paintTrace(r, band, neighbours) {
  const { w, h, on } = r, C = PAINT_CELL;
  for (let again = true; again;) {          // two cells meeting only at a corner: clear one, so nothing hangs together by a point
    again = false;
    for (let j = 0; j + 1 < h; j++) for (let i = 0; i + 1 < w; i++) {
      const k = j * w + i, a = on[k], b = on[k + 1], c = on[k + w], d = on[k + w + 1];
      if (a === d && b === c && a !== b) { if (a) on[k] = 0; else on[k + 1] = 0; again = true; }
    }
  }
  // Cell sides between painted and empty, each running with the painted cell on its left: outlines anticlockwise, holes clockwise.
  // Without corner-only meetings every grid corner has at most one side leaving it, so the sides link up into rings.
  const W = w + 1, next = new Int32Array(W * (h + 1)).fill(-1), cell = (i, j) => i >= 0 && j >= 0 && i < w && j < h && on[j * w + i] === 1;
  for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) {
    if (!on[j * w + i]) continue;
    if (!cell(i, j - 1)) next[j * W + i] = j * W + i + 1;
    if (!cell(i + 1, j)) next[j * W + i + 1] = (j + 1) * W + i + 1;
    if (!cell(i, j + 1)) next[(j + 1) * W + i + 1] = (j + 1) * W + i;
    if (!cell(i - 1, j)) next[(j + 1) * W + i] = j * W + i;
  }
  const outlines = [], holes = [];
  let dropped = 0;
  for (let v0 = 0; v0 < next.length; v0++) {
    if (next[v0] < 0) continue;
    const P = [];
    let v = v0;
    do { P.push([v % W, Math.floor(v / W)]); const n = next[v]; next[v] = -1; v = n; } while (v !== v0 && v >= 0);
    const lattice = areaShoelace(areaFlat(P)) / 2, ring = paintStraighten(P);
    const metres = list => list.map(p => [r2((r.i0 + p[0]) * C) || 0, r2((r.j0 + p[1]) * C) || 0]);
    const one = { exact: metres(ring.exact), simple: metres(ring.simple), area: Math.abs(lattice) * C * C, lattice: P, use: 'simple' };
    if (lattice > 0) { if (one.area < AREA_MIN_OUTLINE) dropped++; else outlines.push(one); }
    else if (one.area < AREA_MIN_HOLE) dropped++;
    else holes.push(one);
  }
  // Each hole belongs to the smallest outline around it (an island inside a courtyard is a part of its own).
  for (const o of outlines) { o.flat = areaFlat(o.lattice); o.holes = []; }
  for (const hole of holes) {
    const p = hole.lattice[0];
    let best = null;
    for (const o of outlines) if (areaOdd(o.flat, p[0], p[1]) && (!best || o.area < best.area)) best = o;
    if (best) best.holes.push(hole); else dropped++;
  }
  // Straightened rings that break the shape rules, or come to touch another ring, keep their exact cell outline.
  const rings = [];
  for (const o of outlines) { rings.push(o); for (const hole of o.holes) rings.push(hole); }
  for (const one of rings) {
    one.min = outlines.includes(one) ? AREA_MIN_OUTLINE : AREA_MIN_HOLE;
    if (areaShape(areaFlat(one.simple), one.min)) one.use = 'exact';
  }
  const fixed = [];
  for (const p of neighbours) for (const ring of [p.outline, ...(p.holes || [])]) fixed.push(ring);
  const now = one => (one.use === 'simple' ? one.simple : one.exact);
  for (let again = true; again;) {
    again = false;
    for (let a = 0; a < rings.length; a++) {
      for (let b = a + 1; b < rings.length; b++) {
        if ((rings[a].use === 'exact' && rings[b].use === 'exact') || !paintTouch(now(rings[a]), now(rings[b]))) continue;
        rings[a].use = rings[b].use = 'exact'; again = true;
      }
      if (rings[a].use === 'simple' && fixed.some(f => paintTouch(rings[a].simple, f))) { rings[a].use = 'exact'; again = true; }
    }
  }
  const parts = outlines.map(o => paintPart({ floor: band.slice(), outline: now(o), holes: o.holes.map(now) }))
    .sort((a, b) => a.outline[0][0] - b.outline[0][0] || a.outline[0][1] - b.outline[0][1]);
  return { parts, dropped };
}
// A ring of cell corners (grid units, every corner of every cell side) as its turning corners (exact) and straightened
// (simple): an edge may replace a run of sides when the middle of each of those sides is within 0.1 m (half a cell) of it.
function paintStraighten(P) {
  const n = P.length, corners = [];
  for (let k = 0; k < n; k++) {
    const a = P[(k + n - 1) % n], b = P[k], c = P[(k + 1) % n];
    if ((b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]) !== 0) corners.push(k);
  }
  const exact = corners.map(k => P[k]);
  if (corners.length < 4) return { exact, simple: exact };
  const at = k => P[k % n], limit = Math.pow(PAINT_SLACK / PAINT_CELL, 2) + 1e-9, all = corners.concat(corners.map(k => k + n)), keep = new Set();
  let start = corners[0];
  for (const k of corners) if (P[k][0] < P[start][0] || (P[k][0] === P[start][0] && P[k][1] < P[start][1])) start = k;
  let far = start, farD = -1;
  for (const k of corners) { const d = Math.pow(P[k][0] - P[start][0], 2) + Math.pow(P[k][1] - P[start][1], 2); if (d > farD) { farD = d; far = k; } }
  const split = (a, b) => {
    const A = at(a), B = at(b);
    let worst = 0;
    for (let k = a; k < b && worst <= limit; k++) { const p = at(k), q = at(k + 1); worst = Math.max(worst, areaDistance2((p[0] + q[0]) / 2, (p[1] + q[1]) / 2, A[0], A[1], B[0], B[1])); }
    if (worst <= limit) return;
    let best = -1, bestD = -1;
    for (const c of all) if (c > a && c < b) { const d = areaDistance2(at(c)[0], at(c)[1], A[0], A[1], B[0], B[1]); if (d > bestD) { bestD = d; best = c; } }
    if (best < 0) return;
    keep.add(best % n); split(a, best); split(best, b);
  };
  const end = far > start ? far : far + n;
  keep.add(start); keep.add(far);
  split(start, end); split(end, start + n);
  return { exact, simple: [...keep].sort((a, b) => a - b).map(k => P[k]) };
}
// Whether two rings share any point.
function paintTouch(a, b) {
  const ba = paintBox(a), bb = paintBox(b), e = AREA_NEAR;
  if (ba.maxX < bb.minX - e || bb.maxX < ba.minX - e || ba.maxZ < bb.minZ - e || bb.maxZ < ba.minZ - e) return false;
  const fa = areaFlat(a), fb = areaFlat(b);
  for (let i = 0; i < a.length; i++) for (let j = 0; j < b.length; j++) if (areaTouch(fa, i, fb, j)) return true;
  return false;
}
function paintStrokeCommit(stroke) {
  const t = state.paint.target, area = areaOfTarget(t);
  if (!area || !stroke.cells.size) return false;
  const got = paintStrokeParts(area, stroke, paintBand());
  if (got.error) { say(got.error); return false; }
  if (!got.parts) { say('Nothing painted on this floor band there to erase.'); return false; }
  const before = area.parts.length;
  if (!paintCommit(t, got.parts, stroke.erase ? 'That erasing' : 'That stroke')) return false;
  paintTell(t, before, got.dropped ? got.dropped + (got.dropped === 1 ? ' piece was' : ' pieces were') + ' smaller than an area may be (0.25 square metres, or 0.05 for a hole) and left out.' : '');
  return true;
}
// A whole stroke at once, through world points [x, z], with the brush size and tip set: one undo step. For scripts and tests.
function paintStroke(points, erase) {
  if (!areaHolder(state.paint.target)) { say('Choose what to paint first: a zone\'s "Painted area", or a place\'s shape.'); return false; }
  const list = (points || []).filter(p => Array.isArray(p) && isFinite(p[0]) && isFinite(p[1]));
  if (!list.length) return false;
  const stroke = paintStrokeNew(erase);
  list.forEach((p, k) => paintSweep(stroke, list[Math.max(0, k - 1)], p));
  return paintStrokeCommit(stroke);
}

// ----- rectangle and polygon: a part of their own on the band
function paintAddPart(outline, what) {
  const t = state.paint.target, area = areaOfTarget(t);
  if (!area) { say('Choose what to paint first: a zone\'s "Painted area", or a place\'s shape.'); return false; }
  const part = paintPart({ floor: paintBand(), outline, holes: [] });
  if (part.outline.length < 3) { say(what + ' needs at least three different corners.'); return false; }
  const before = area.parts.length;
  if (!paintCommit(t, area.parts.concat([part]), what)) return false;
  const wrong = areaShape(areaFlat(part.outline), AREA_MIN_OUTLINE);
  paintTell(t, before, wrong ? what + ' was added, but ' + wrong + '. The file is refused until that is put right: undo it, or move its corners with Corners.' : '');
  return true;
}
function paintRect(a, b) {
  const x0 = Math.min(a[0], b[0]), x1 = Math.max(a[0], b[0]), z0 = Math.min(a[1], b[1]), z1 = Math.max(a[1], b[1]);
  return paintAddPart([[x0, z0], [x1, z0], [x1, z1], [x0, z1]], 'That rectangle');
}
function paintPoly(points) { return paintAddPart((points || []).map(p => [p[0], p[1]]), 'That polygon'); }

// ----- corners, moving and turning
function paintRingOf(parts, part, ring) { const p = parts[part]; return !p ? null : ring < 0 ? p.outline : (p.holes || [])[ring] || null; }
// Moves corner k of a ring (ring -1: the outline, else that hole) to p = [x, z], or removes it when p is null.
function areaVertex(part, ring, k, p) {
  const t = state.paint.target, area = areaHolder(t) && areaHolder(t).area;
  if (!area || !paintRingOf(area.parts, part, ring) || !paintRingOf(area.parts, part, ring)[k]) return false;
  const parts = paintCopy(area.parts), r = paintRingOf(parts, part, ring);
  if (p) r[k] = [r2(p[0]), r2(p[1])];
  else if (r.length > 3) r.splice(k, 1);
  else if (ring >= 0) { parts[part].holes.splice(ring, 1); say('A hole needs three corners, so that hole is gone.'); }
  else { say('An outline needs at least three corners. To remove it, delete the part in the list.'); return false; }
  return paintCommit(t, parts.map(paintPart), p ? 'That corner move' : 'Removing that corner');
}
// Adds a corner p = [x, z] after corner k of a ring.
function areaInsertVertex(part, ring, k, p) {
  const t = state.paint.target, area = areaHolder(t) && areaHolder(t).area;
  if (!area || !paintRingOf(area.parts, part, ring)) return false;
  const parts = paintCopy(area.parts);
  paintRingOf(parts, part, ring).splice(k + 1, 0, [r2(p[0]), r2(p[1])]);
  return paintCommit(t, parts.map(paintPart), 'That corner');
}
function paintBounds(parts) {
  const b = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity };
  for (const p of parts) { const q = paintBox(p.outline); b.minX = Math.min(b.minX, q.minX); b.maxX = Math.max(b.maxX, q.maxX); b.minZ = Math.min(b.minZ, q.minZ); b.maxZ = Math.max(b.maxZ, q.maxZ); }
  return b;
}
// The whole area, and its anchor with it, moved or turned (positive degrees turn clockwise on the map, like facing).
function paintTransform(fn, what) {
  const t = state.paint.target, area = areaHolder(t) && areaHolder(t).area;
  if (!area || !area.parts.length) { say('Paint the area first.'); return false; }
  const ring = r => r.map(fn);
  const parts = area.parts.map(p => paintPart({ floor: p.floor, outline: ring(p.outline), holes: (p.holes || []).map(ring) }));
  return paintCommit(t, parts, what, h => { if (isVec(h.pos)) { const q = fn([h.pos[0], h.pos[2]]); h.pos = [r2(q[0]), h.pos[1], r2(q[1])]; } });
}
const areaMove = (dx, dz) => paintTransform(p => [p[0] + dx, p[1] + dz], 'That move');
function areaTurn(deg) {
  const area = areaHolder(state.paint.target) && areaHolder(state.paint.target).area;
  if (!area || !area.parts.length) { say('Paint the area first.'); return false; }
  const b = paintBounds(area.parts), cx = (b.minX + b.maxX) / 2, cz = (b.minZ + b.maxZ) / 2, a = -deg * Math.PI / 180, cos = Math.cos(a), sin = Math.sin(a);
  return paintTransform(p => { const dx = p[0] - cx, dz = p[1] - cz; return [cx + dx * cos - dz * sin, cz + dx * sin + dz * cos]; }, 'That turn');
}
function paintDeletePart(j) {
  const t = state.paint.target, area = areaHolder(t) && areaHolder(t).area;
  if (!area || !area.parts[j]) return false;
  const ok = paintCommit(t, area.parts.filter((p, k) => k !== j), 'Deleting that part');
  if (ok && !area.parts.length) say('That was the last part' + (t.kind === 'trigger' ? ': the circle counts again until you paint.' : '.'));
  return ok;
}

// ----- the anchor and the floors
// A point well inside the area (never just the middle of its box, which may be outside or in a courtyard): along rows
// across each part, the middle of the span that lies farthest from every edge, on a walkable floor of the part's band.
function areaInteriorPoint(area) {
  const found = [];
  for (const part of (area && area.parts) || []) {
    const rings = [part.outline, ...(part.holes || [])], flats = rings.map(areaFlat), b = paintBox(part.outline);
    for (let row = 1; row < 32; row++) {
      const z = b.minZ + (b.maxZ - b.minZ) * row / 32, xs = [];
      for (const ring of rings) for (let k = 0, n = ring.length, prev = n - 1; k < n; prev = k++) {
        const p = ring[prev], q = ring[k];
        if ((p[1] > z) !== (q[1] > z)) xs.push(p[0] + (z - p[1]) * (q[0] - p[0]) / (q[1] - p[1]));
      }
      xs.sort((p, q) => p - q);
      for (let s = 0; s + 1 < xs.length; s += 2) {
        const x = (xs[s] + xs[s + 1]) / 2;
        let d = Infinity;
        for (const f of flats) for (let k = 0, n = f.length / 2; k < n; k++) { const m = (k + 1) % n; d = Math.min(d, areaDistance2(x, z, f[2 * k], f[2 * k + 1], f[2 * m], f[2 * m + 1])); }
        found.push({ x, z, d, part });
      }
    }
  }
  found.sort((a, b) => b.d - a.d);
  for (const one of found.slice(0, 40)) {
    const [lo, hi] = one.part.floor, want = Math.min(hi, lo + 0.5);
    const levels = state.map ? levelsAt(one.x, one.z).filter(y => y >= lo && y <= hi) : [];
    const y = levels.length ? levels.reduce((p, q) => (Math.abs(q - want) < Math.abs(p - want) ? q : p)) : want;
    const p = [r2(one.x), r2(y), r2(one.z)];
    if (areaContains(area, p)) return p;
  }
  return null;
}
function areaAnchorInside() {
  const t = state.paint.target, h = areaHolder(t), area = h && h.area;
  if (!area || !area.parts.length) { say('Paint the area first.'); return false; }
  const p = areaInteriorPoint(area);
  if (!p) { say('No point inside the area was found. Check its findings.'); return false; }
  willChange('Anchor inside the area of ' + areaTargetName(t)); h.pos = p; areaChanged(t);
  say('The anchor now stands inside the area, at ' + p[0].toFixed(1) + ', ' + p[2].toFixed(1) + ', height ' + p[1].toFixed(1) + '.');
  return true;
}
// Copies this band's parts to the floor above as parts of their own, and paints there from now on: the start of a whole house.
function paintAddFloor() {
  const t = state.paint.target, area = areaOfTarget(t), band = paintBand();
  if (!area) return false;
  const mine = area.parts.filter(p => paintSameBand(p, band));
  if (!mine.length) { say('Paint this floor first. Add floor copies its outline to the floor above, as a part of its own.'); return false; }
  const floor = band[0] + 0.5, span = band[1] - band[0], spot = areaInteriorPoint({ parts: mine });
  const above = spot && state.map ? levelsAt(spot[0], spot[2]).filter(y => y > floor + 1.2).sort((a, b) => a - b)[0] : undefined;
  const next = above !== undefined ? above : floor + span, nb = [r2(next - 0.5), r2(next - 0.5 + span)];
  if (area.parts.some(p => paintSameBand(p, nb))) { setPaintBand(nb[0], nb[1]); syncSide(); invalidate(); say('That floor has its own parts already; painting now goes there.'); return true; }
  const copies = mine.map(p => paintPart({ floor: nb, outline: p.outline, holes: p.holes || [] }));
  if (!paintCommit(t, area.parts.concat(copies), 'Adding a floor', () => { state.paint.bottom = nb[0]; state.paint.top = nb[1]; })) return false;
  say('This floor\'s outline is copied to the floor at ' + next.toFixed(1) + ' m (band ' + nb[0] + ' to ' + nb[1] + ' m), and painting now goes there. Erase what the upper floor does not have; for the stairs, paint a part whose band reaches both floors.');
  return true;
}

// ----- drawing
// The area as painted, or, while a stroke is under way, as it will be when the stroke ends.
function areaShown(area) {
  const g = paintGesture, h = areaHolder(state.paint.target);
  return g && g.preview && h && h.area === area ? { parts: g.preview } : area;
}
// The floor height whose parts are drawn in full: the band being painted, else the floor shown under the middle of the view.
// null draws every part in full (no map, or every floor shown).
function paintShownFloor() {
  if (state.paint.target) return state.paint.bottom + 0.5;
  if (!state.map || !isFinite(state.ceiling)) return null;
  const y = groundAt(state.view.cx, state.view.cz);
  return y === null ? state.ceiling - 1.5 : y;
}
// Fill, outline and holes of each part; parts whose band misses `floor` are faint and dashed.
// style: { colour, fill (opacity), line (opacity), width, floor }.
function drawArea(c, area, style) {
  area = areaShown(area);
  if (!area || !Array.isArray(area.parts)) return;
  const s = style || {}, colour = s.colour || css.friend, y = typeof s.floor === 'number' ? s.floor : null, base = c.globalAlpha;
  for (const part of area.parts) {
    const dim = y !== null && (y < part.floor[0] || y > part.floor[1]);
    c.beginPath();
    for (const ring of [part.outline, ...(part.holes || [])]) {
      ring.forEach((p, k) => { const [x, z] = toScreen(p[0], p[1]); if (k) c.lineTo(x, z); else c.moveTo(x, z); });
      c.closePath();
    }
    c.globalAlpha = base * (s.fill === undefined ? 0.14 : s.fill) * (dim ? 0.35 : 1); c.fillStyle = colour; c.fill('evenodd');
    c.globalAlpha = base * (s.line === undefined ? 0.9 : s.line) * (dim ? 0.4 : 1); c.lineWidth = s.width || 1.6; c.strokeStyle = colour;
    c.setLineDash(dim ? [4, 4] : []); c.stroke(); c.setLineDash([]);
  }
  c.globalAlpha = base;
}
// The tool on top of everything: the brush tip, the rectangle or polygon being drawn, and the corner handles.
function drawPaint(c) {
  const t = state.paint.target, tool = state.paint.tool, area = areaHolder(t) && areaHolder(t).area;
  if (!t || !tool || state.mode === '3d') return;
  const g = paintGesture, v = state.view;
  c.save();
  c.strokeStyle = css.ink; c.fillStyle = css.friend; c.lineWidth = 1.5;
  if ((tool === 'brush' || tool === 'erase') && paintPointer) {
    const [x, y] = toScreen(paintPointer[0], paintPointer[1]), r = state.paint.size / 2 * v.scale;
    c.beginPath(); if (state.paint.tip === 'square') c.rect(x - r, y - r, 2 * r, 2 * r); else c.arc(x, y, r, 0, Math.PI * 2);
    c.setLineDash(tool === 'erase' ? [4, 3] : []); c.stroke(); c.setLineDash([]);
  }
  if (g && g.kind === 'stroke' && !g.preview && g.stroke.path.length) {    // until the first traced preview: the swept path
    c.lineCap = state.paint.tip === 'square' ? 'square' : 'round'; c.lineJoin = 'round'; c.lineWidth = state.paint.size * v.scale;
    c.globalAlpha = 0.3; c.strokeStyle = g.stroke.erase ? css.paper : css.friend;
    c.beginPath(); g.stroke.path.forEach((p, k) => { const [x, y] = toScreen(p[0], p[1]); if (k) c.lineTo(x, y); else c.moveTo(x, y); }); c.stroke();
    c.globalAlpha = 1;
  }
  if (g && g.kind === 'rect') {
    const [ax, ay] = toScreen(g.a[0], g.a[1]), [bx, by] = toScreen(g.b[0], g.b[1]);
    c.globalAlpha = 0.15; c.fillRect(ax, ay, bx - ax, by - ay); c.globalAlpha = 1; c.setLineDash([5, 4]); c.strokeRect(ax, ay, bx - ax, by - ay); c.setLineDash([]);
  }
  if (g && g.kind === 'poly' && g.pts.length) {
    c.beginPath(); g.pts.forEach((p, k) => { const [x, y] = toScreen(p[0], p[1]); if (k) c.lineTo(x, y); else c.moveTo(x, y); });
    if (paintPointer) { const p = wallsSnap(paintPointer), [x, y] = toScreen(p[0], p[1]); c.lineTo(x, y); }
    c.setLineDash([5, 4]); c.stroke(); c.setLineDash([]);
    g.pts.forEach((p, k) => { const [x, y] = toScreen(p[0], p[1]); c.beginPath(); c.arc(x, y, k ? 3.5 : 6, 0, Math.PI * 2); c.fillStyle = css.panel; c.fill(); c.stroke(); });
  }
  if (tool === 'vertex' && area) for (const hd of paintHandles(area)) {
    const [x, y] = toScreen(hd.p[0], hd.p[1]);
    c.beginPath(); if (hd.mid) c.arc(x, y, 3.5, 0, Math.PI * 2); else c.rect(x - 4.5, y - 4.5, 9, 9);
    c.fillStyle = hd.mid ? css.paper : css.panel; c.fill(); c.lineWidth = 1.5; c.strokeStyle = css.ink; c.stroke();
  }
  c.restore();
}
// Corner and edge-middle handles of the parts on the band being painted.
function paintHandles(area) {
  const out = [], band = paintBand();
  area.parts.forEach((part, j) => {
    if (!paintSameBand(part, band)) return;
    [part.outline, ...(part.holes || [])].forEach((ring, h) => ring.forEach((p, k) => {
      const q = ring[(k + 1) % ring.length];
      out.push({ part: j, ring: h - 1, k, p, mid: false }, { part: j, ring: h - 1, k, p: [(p[0] + q[0]) / 2, (p[1] + q[1]) / 2], mid: true });
    }));
  });
  return out;
}
function paintHandleAt(sx, sy) {
  const area = areaHolder(state.paint.target) && areaHolder(state.paint.target).area;
  if (!area) return null;
  let best = null, bd = 9;
  for (const hd of paintHandles(area)) {
    const [x, y] = toScreen(hd.p[0], hd.p[1]), d = Math.hypot(x - sx, y - sy) + (hd.mid ? 2 : 0);    // corners win over edge middles
    if (d < bd) { bd = d; best = hd; }
  }
  return best;
}
// Whether a map point [x, z] is on the painted area (any band): there a drag with Corners moves the whole area.
function paintOnArea(w) {
  const area = areaHolder(state.paint.target) && areaHolder(state.paint.target).area;
  return !!area && area.parts.some(p => areaInRing(areaFlat(p.outline), w[0], w[1]) && !(p.holes || []).some(h => areaWellInside(areaFlat(h), w[0], w[1])));
}
// 3D: every painted trigger area as see-through volumes between its floor bands. 29b and others may wrap this to add theirs.
function areaVolumes() {
  const t = state.paint.target, out = [];
  state.zones.forEach((z, i) => {
    if (z.when !== 'zone' || !z.area || !z.area.parts || !z.area.parts.length) return;
    out.push({ area: z.area, colour: 'friend', strong: !!t && t.kind === 'trigger' && t.index === i, faint: state.fired.includes(z.id) });
  });
  return out;
}

// ----- corner, edge-middle and whole-area drags. The drag changes the area in place so it can be drawn, but it is one
// change only when it ends: it is a transaction from its first movement (27-editing.js), so until then nothing is staged or
// saved and no undo entry exists, and giving it up by any route (Esc, pointercancel, another tool, Done) puts back exactly
// what was there before it started.
function paintDragStart(kind, fields, sx, sy) {
  const t = state.paint.target, h = areaHolder(t);
  paintGesture = Object.assign({ kind, sx, sy, saved: false, holder: h, target: { kind: t.kind, index: t.index, id: t.id },
    was: JSON.stringify(h.area), sig: areaSignature(h.area), pos: isVec(h.pos) ? h.pos.slice() : null }, fields);
}
// Puts the area and its anchor back as they were before the drag. In place, so anything holding the area sees it, and its
// cached bounds are worked out again: the drag left them describing the dragged shape.
function paintDragRestore(g) {
  const h = g.holder, was = JSON.parse(g.was);
  if (h.area && was) h.area.parts = was.parts; else h.area = was;
  if (g.pos) h.pos = g.pos.slice();
  if (h.area) areaPrepare(h.area);
}
const paintIsDrag = g => !!g && (g.kind === 'vertex' || g.kind === 'move');
// Gives up whatever gesture is under way; a drag is undone first. True when there was a drag to give up.
function paintGestureDrop() {
  const g = paintGesture;
  paintGesture = null;
  if (!paintIsDrag(g)) return false;
  if (g.saved) { paintDragRestore(g); txCancel(); }
  invalidate();
  return true;
}
// The end of a drag: one undo entry (the state before it started) and one change notice, or nothing when it moved nothing.
function paintDragEnd(g) {
  paintGesture = null;
  const h = g.holder, area = h.area;
  if (!area) { if (g.saved) txCancel(); return; }
  if (!g.saved) {           // a click on an edge middle adds a corner there; a click on a corner or the area changes nothing
    if (g.kind === 'vertex' && g.hd.mid) areaInsertVertex(g.hd.part, g.hd.ring, g.hd.k, g.hd.p);
    return;
  }
  const parts = area.parts.map(paintPart), pos = isVec(h.pos) ? [r2(h.pos[0]), h.pos[1], r2(h.pos[2])] : null;
  if (areaSignature({ parts }) === g.sig && JSON.stringify(pos) === JSON.stringify(g.pos)) { paintDragRestore(g); txCancel(); invalidate(); return; }
  area.parts = parts; if (pos) h.pos = pos;
  areaPrepare(area);
  txCommit();
  areaChanged(g.target);
}
function paintToolSet(tool) { paintGestureDrop(); state.paint.tool = tool; syncSide(); invalidate(); }

// ----- the mouse on the flat map. These listeners come before 28-squads.js's, so a painting click stops there; the right
// button and empty ground under Corners still pan as usual. Every corner a Rectangle, a Polygon or a Corners drag places
// goes through wallsSnap (27c-walls.js), which leaves it where it is unless snapping to walls is on and the geometry matches.
const paintAt = ev => { const r = canvas.getBoundingClientRect(), sx = ev.clientX - r.left, sy = ev.clientY - r.top; return { sx, sy, w: toWorld(sx, sy) }; };
const paintActive = () => !!(state.paint.target && state.paint.tool && state.mode !== '3d' && areaHolder(state.paint.target));
function paintPointerDown(ev) {
  if (!paintActive()) return;
  const { sx, sy, w } = paintAt(ev), tool = state.paint.tool;
  if (tool === 'vertex') {
    const hd = paintHandleAt(sx, sy);
    if (ev.button === 2 && hd && !hd.mid) { ev.stopImmediatePropagation(); paintGestureDrop(); areaVertex(hd.part, hd.ring, hd.k, null); return; }
    if (ev.button !== 0) return;
    if (hd) { ev.stopImmediatePropagation(); canvas.setPointerCapture(ev.pointerId); paintGestureDrop(); paintDragStart('vertex', { hd }, sx, sy); return; }
    if (paintOnArea(w)) { ev.stopImmediatePropagation(); canvas.setPointerCapture(ev.pointerId); paintGestureDrop(); paintDragStart('move', { from: w }, sx, sy); }
    return;
  }
  if (ev.button !== 0) return;
  ev.stopImmediatePropagation(); canvas.setPointerCapture(ev.pointerId);
  if (tool === 'brush' || tool === 'erase') {
    paintGesture = { kind: 'stroke', stroke: paintStrokeNew(tool === 'erase'), last: w, at: 0, preview: null };
    paintSweep(paintGesture.stroke, w, w);
  } else if (tool === 'rect') { const at = wallsSnap(w); paintGesture = { kind: 'rect', a: at, b: at }; }
  else if (tool === 'poly') { if (!paintGesture || paintGesture.kind !== 'poly') paintGesture = { kind: 'poly', pts: [] }; paintGesture.down = true; }
  invalidate();
}
function paintPointerMove(ev) {
  if (!paintActive()) { paintPointer = null; return; }
  const { sx, sy, w } = paintAt(ev), g = paintGesture;
  paintPointer = w;
  if (!g) { invalidate(); return; }
  if (g.kind === 'stroke') {
    paintSweep(g.stroke, g.last, w); g.last = w;
    const now = Date.now();
    if (now - g.at > 80) {                 // the traced result, a few times a second, so the final outline shows while painting
      g.at = now;
      const area = areaOfTarget(state.paint.target), got = area ? paintStrokeParts(area, g.stroke, paintBand()) : null;
      g.preview = got && got.parts ? got.parts : null;
    }
  } else if (g.kind === 'rect') g.b = wallsSnap(w);
  else if (paintIsDrag(g)) {
    if (!g.saved && Math.hypot(sx - g.sx, sy - g.sy) < 4) return;
    const h = g.holder, area = h.area;
    if (!area) return;
    const at = g.kind === 'vertex' ? wallsSnap(w) : w;
    if (!g.saved) {
      const ring = g.kind === 'vertex' && g.hd.mid ? paintRingOf(area.parts, g.hd.part, g.hd.ring) : null;
      if (ring && ring.length >= AREA_MAX_RING) { paintGesture = null; return say('A ring has at most ' + AREA_MAX_RING + ' corners.'); }
      txBegin((g.kind === 'move' ? 'Move the area of ' : 'Move a corner of ') + areaTargetName(g.target));
      if (ring) { ring.splice(g.hd.k + 1, 0, [r2(at[0]), r2(at[1])]); g.hd = { part: g.hd.part, ring: g.hd.ring, k: g.hd.k + 1, mid: false }; }
      g.saved = true;
    }
    if (g.kind === 'vertex') paintRingOf(area.parts, g.hd.part, g.hd.ring)[g.hd.k] = [r2(at[0]), r2(at[1])];
    else {
      const dx = w[0] - g.from[0], dz = w[1] - g.from[1], shift = p => [p[0] + dx, p[1] + dz];
      for (const part of area.parts) { part.outline = part.outline.map(shift); part.holes = (part.holes || []).map(r => r.map(shift)); }
      if (isVec(h.pos)) h.pos = [h.pos[0] + dx, h.pos[1], h.pos[2] + dz];
      g.from = w;
    }
    areaPrepare(area);    // the shape on screen and the one membership checks see stay the same while dragging
  }
  invalidate();
}
function paintPointerUp(ev) {
  const g = paintGesture;
  if (!g || !paintActive()) return;
  const { sx, sy, w } = paintAt(ev);
  if (g.kind === 'stroke') { paintGesture = null; paintStrokeCommit(g.stroke); invalidate(); return; }
  if (g.kind === 'rect') {
    paintGesture = null;
    if (Math.abs(g.b[0] - g.a[0]) < 0.5 || Math.abs(g.b[1] - g.a[1]) < 0.5) { invalidate(); return say('Drag to draw a rectangle at least half a metre across.'); }
    paintRect(g.a, g.b); return;
  }
  if (g.kind === 'poly') {
    if (!g.down) return;
    g.down = false;
    const near = (p, px) => { const [x, y] = toScreen(p[0], p[1]); return Math.hypot(x - sx, y - sy) <= px; };
    if (g.pts.length >= 3 && (near(g.pts[0], 9) || near(g.pts[g.pts.length - 1], 5))) { paintGesture = null; paintPoly(g.pts); return; }
    if (g.pts.length && near(g.pts[g.pts.length - 1], 5)) return;
    if (g.pts.length >= AREA_MAX_RING) return say('A ring has at most ' + AREA_MAX_RING + ' corners. Close this one first.');
    const at = wallsSnap(w);
    g.pts.push([r2(at[0]), r2(at[1])]); invalidate(); return;
  }
  paintDragEnd(g);
  invalidate();
}
// The browser took the pointer away (a touch turned into a scroll, a window took focus): the drag did not finish.
function paintPointerCancel() { if (paintGesture && paintGesture.kind !== 'poly') { paintGestureDrop(); invalidate(); } }
function paintKey(ev) {
  if (!paintActive() || /^(INPUT|TEXTAREA|SELECT)$/.test(ev.target.tagName)) return;
  const g = paintGesture, key = ev.key;
  if (key === 'Enter' && g && g.kind === 'poly' && g.pts.length >= 3) { ev.preventDefault(); ev.stopImmediatePropagation(); paintGesture = null; paintPoly(g.pts); }
  else if (key === 'Backspace' && g && g.kind === 'poly') { ev.preventDefault(); ev.stopImmediatePropagation(); g.pts.pop(); invalidate(); }
  else if (key === 'Escape' && g) { ev.stopImmediatePropagation(); paintGestureDrop(); invalidate(); say('Dropped.'); }
  else if (key === 'Escape') areaEditStop();
  else if ((key === 'Delete' || key === 'Backspace') && state.paint.tool === 'vertex' && paintPointer) {
    ev.preventDefault(); ev.stopImmediatePropagation();          // never the selected enemy while painting
    const [sx, sy] = toScreen(paintPointer[0], paintPointer[1]), hd = paintHandleAt(sx, sy);
    if (hd && !hd.mid) { paintGestureDrop(); areaVertex(hd.part, hd.ring, hd.k, null); }
  }
}
canvas.addEventListener('pointerdown', paintPointerDown);
canvas.addEventListener('pointermove', paintPointerMove);
canvas.addEventListener('pointerup', paintPointerUp);
canvas.addEventListener('pointercancel', paintPointerCancel);
window.addEventListener('keydown', paintKey);
expose({ paintPointerDown, paintPointerMove, paintPointerUp, paintPointerCancel, paintKey, paintToolSet });   // gestures through the canvas's own handlers (ML-032), for automated tests

// ----- the controls: one box, shown in the row of whatever is painted (the trigger list, or 29b's place panel)
const paintUi = { box: $('paintBox'), home: $('paintHome') };
// The painting controls for that row, or null when it is not being painted.
function paintBoxFor(kind, index) { return paintIs(kind, index) && areaHolder(state.paint.target) ? paintUi.box : null; }
const PAINT_HINTS = {
  brush: 'Drag to paint on this floor band. Cells are 0.2 m; edges are straightened by at most 0.1 m and never across a gap.',
  erase: 'Drag to erase on this floor band. Erasing the middle leaves a hole, such as a courtyard; erasing right across splits the part in two.',
  rect: 'Drag a rectangle: it is added as a part on this floor band.',
  poly: 'Click the corners; click the first one, click the last one again or press Enter to close. Backspace takes the last corner back, Esc drops it.',
  vertex: 'Drag a corner to move it, drag a small circle to add a corner, right-click a corner (or point at it and press Delete) to remove it. Drag inside the area to move all of it.',
};
// Findings in words: 'Part 2, hole 1: ...'.
function paintFindingWhere(path) {
  const m = /parts\[(\d+)\](?:\.(outline|floor)|\.holes\[(\d+)\])?/.exec(path);
  if (!m) return 'The area';
  return 'Part ' + (+m[1] + 1) + (m[2] === 'outline' ? ', outline' : m[2] === 'floor' ? ', floor band' : m[3] !== undefined ? ', hole ' + (+m[3] + 1) : '');
}
function areaFindings(area) {
  const out = [];
  if (area && area.parts && area.parts.length) areaProblems(areaWrite(area), '', (code, path, text) => out.push({ code, path, text, where: paintFindingWhere(path) }));
  return out;
}
function syncPaint() {
  const t = state.paint.target;
  let h = areaHolder(t);
  if (t && h && t.id !== undefined && h.id !== t.id) {            // the list changed under it: follow the same thing, or stop
    const list = AREA_HOLDERS[t.kind](), i = list.findIndex(q => q && q.id === t.id);
    if (i >= 0) { t.index = i; h = list[i]; } else h = null;
  }
  if (!h) {
    if (t) { state.paint.target = null; state.paint.tool = null; paintGestureDrop(); }
    if (paintUi.box.parentNode !== paintUi.home) paintUi.home.appendChild(paintUi.box);
    return;
  }
  const area = areaOfTarget(t), mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  $('paintTitle').textContent = 'Painting ' + (t.kind === 'trigger' ? trigTitle(h).toLowerCase() : t.kind === 'place' ? (h.name || 'the place') : (h.name || 'the placement area'));
  for (const b of document.querySelectorAll('#paintBox [data-paint]')) b.setAttribute('aria-pressed', String(b.dataset.paint === state.paint.tool));
  setVal($('paintSize'), state.paint.size); setVal($('paintTip'), state.paint.tip);
  setVal($('paintBottom'), state.paint.bottom); setVal($('paintTop'), state.paint.top);
  $('paintSizeRow').hidden = !(state.paint.tool === 'brush' || state.paint.tool === 'erase');
  const pl = $('paintParts'); pl.textContent = '';
  let points = 0, biggest = 0;
  area.parts.forEach((part, j) => {
    const rings = [part.outline, ...(part.holes || [])], here = paintSameBand(part, paintBand());
    for (const r of rings) { points += r.length; biggest = Math.max(biggest, r.length); }
    const li = mk('li'), go = mk('button', { type: 'button', className: 'btn', textContent: here ? 'Painting here' : 'Paint here', disabled: here });
    const what = mk('span', { textContent: 'Part ' + (j + 1) + ' · floor ' + part.floor[0] + ' to ' + part.floor[1] + ' m · ' + part.outline.length + ' corners' + (part.holes && part.holes.length ? ' · ' + part.holes.length + (part.holes.length === 1 ? ' hole' : ' holes') : '') });
    go.title = 'Paint on this part\'s floor band'; go.addEventListener('click', () => { setPaintBand(part.floor[0], part.floor[1]); syncSide(); invalidate(); });
    const del = mk('button', { type: 'button', className: 'btn danger', textContent: 'Delete' }); del.addEventListener('click', () => paintDeletePart(j));
    li.append(what, go, del); pl.appendChild(li);
  });
  $('paintCount').textContent = area.parts.length
    ? area.parts.length + ' of ' + AREA_MAX_PARTS + ' parts · ' + points + ' of ' + AREA_MAX_POINTS + ' corners · largest ring ' + biggest + ' of ' + AREA_MAX_RING
    : 'Nothing painted yet. ' + (t.kind === 'trigger' ? 'Until then the circle counts.' : '');
  const fl = $('paintFindings'); fl.textContent = '';
  for (const f of areaFindings(area)) fl.appendChild(mk('li', { textContent: f.where + ': ' + f.text + '.' }));
  $('paintAnchor').hidden = !paintAnchorOutside(t);
  $('paintHint').textContent = (PAINT_HINTS[state.paint.tool] || 'Choose a tool.') + ' The game gets the whole shape at once, the next time the ' + (t.kind === 'trigger' ? 'triggers are' : 'scenario is') + ' sent.';
}
for (const b of document.querySelectorAll('#paintBox [data-paint]')) b.addEventListener('click', () => paintToolSet(b.dataset.paint));
$('paintSize').addEventListener('change', ev => { state.paint.size = Math.max(PAINT_SIZE_MIN, Math.min(PAINT_SIZE_MAX, r2(+ev.target.value || 1.5))); syncSide(); invalidate(); });
$('paintTip').addEventListener('change', ev => { state.paint.tip = ev.target.value === 'square' ? 'square' : 'round'; invalidate(); });
const paintBandEdit = () => { if (!setPaintBand($('paintBottom').value, $('paintTop').value)) { $('paintBottom').value = state.paint.bottom; $('paintTop').value = state.paint.top; } syncSide(); invalidate(); };
$('paintBottom').addEventListener('change', paintBandEdit); $('paintTop').addEventListener('change', paintBandEdit);
$('paintThisFloor').addEventListener('click', () => { const b = floorBand(); setPaintBand(b[0], b[1]); syncSide(); invalidate(); say('Painting on the floor band ' + b[0] + ' to ' + b[1] + ' m.'); });
$('paintAddFloor').addEventListener('click', paintAddFloor);
$('paintTurnL').addEventListener('click', () => areaTurn(-15));
$('paintTurnR').addEventListener('click', () => areaTurn(15));
$('paintAnchor').addEventListener('click', areaAnchorInside);
$('paintDone').addEventListener('click', areaEditStop);
