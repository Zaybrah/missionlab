// ------------------------------------------------------------------ painted areas (ML-027)
// The plugin's src/MissionLab.Core/Area.cs in JavaScript, rule for rule: both are held to tests/fixtures/area_points.json.
// An area here has the file's shape: { parts: [{ floor: [bottom, top], outline: [[x, z], ...], holes: [[[x, z], ...], ...] }] }.
// The union of the parts counts, so parts may touch or overlap and walking across a seam never leaves the area.
const AREA_MAX_PARTS = 32, AREA_MAX_RING = 256, AREA_MAX_POINTS = 2048, AREA_MAX_FLOOR = 60, AREA_MAX_COORD = 100000;
const AREA_MIN_OUTLINE = 0.25, AREA_MIN_HOLE = 0.05, AREA_NEAR = 0.01;    // metres: a point this close to an edge is on it
const AREA_BOXES = new WeakMap();    // area -> what areaContains needs, worked out once by areaPrepare

const areaPair = v => Array.isArray(v) && v.length === 2 && typeof v[0] === 'number' && typeof v[1] === 'number';

// Rings as x0, z0, x1, z1, ... so the geometry below reads like the plugin's.
const areaFlat = ring => { const f = []; for (const p of ring) f.push(p[0], p[1]); return f; };

// The lenient reading: points that are not two numbers are skipped, a ring left with fewer than 3 points is dropped, an
// upside-down floor is turned round, a part without a floor or a usable outline is dropped. null when no part is left.
function areaRead(json) {
  if (!json || typeof json !== 'object' || Array.isArray(json) || !Array.isArray(json.parts)) return null;
  const loose = ring => { if (!Array.isArray(ring)) return null; const list = ring.filter(p => areaPair(p) && isFinite(p[0]) && isFinite(p[1])).map(p => [p[0], p[1]]); return list.length < 3 ? null : list; };
  const parts = [];
  for (const one of json.parts) {
    if (!one || typeof one !== 'object' || Array.isArray(one) || !areaPair(one.floor)) continue;
    const outline = loose(one.outline);
    if (!outline) continue;
    const holes = Array.isArray(one.holes) ? one.holes.map(loose).filter(h => h) : [];
    parts.push({ floor: [Math.min(one.floor[0], one.floor[1]), Math.max(one.floor[0], one.floor[1])], outline, holes });
  }
  return parts.length ? { parts } : null;
}

// The canonical object for the file, 2 decimals; holes left out when there are none.
function areaWrite(area) {
  const n = v => r2(v) || 0;    // no -0
  const ring = r => r.map(p => [n(p[0]), n(p[1])]);
  return { parts: area.parts.map(p => Object.assign({ floor: [n(p.floor[0]), n(p.floor[1])], outline: ring(p.outline) }, p.holes && p.holes.length ? { holes: p.holes.map(ring) } : {})) };
}

// The canonical text of the shape: two areas with the same signature are the same shape once written.
const areaSignature = area => JSON.stringify(areaWrite(area));

// Works out the bounds areaContains checks first. Call it again after changing an area in place.
function areaPrepare(area) {
  const box = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity, bottom: Infinity, top: -Infinity, pieces: [] };
  for (const part of area.parts) {
    const piece = { outline: areaFlat(part.outline), holes: (part.holes || []).map(areaFlat), bottom: part.floor[0], top: part.floor[1], minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity };
    for (let k = 0; k < piece.outline.length; k += 2) {
      piece.minX = Math.min(piece.minX, piece.outline[k]); piece.maxX = Math.max(piece.maxX, piece.outline[k]);
      piece.minZ = Math.min(piece.minZ, piece.outline[k + 1]); piece.maxZ = Math.max(piece.maxZ, piece.outline[k + 1]);
    }
    box.minX = Math.min(box.minX, piece.minX); box.maxX = Math.max(box.maxX, piece.maxX); box.minZ = Math.min(box.minZ, piece.minZ); box.maxZ = Math.max(box.maxZ, piece.maxZ);
    box.bottom = Math.min(box.bottom, piece.bottom); box.top = Math.max(box.top, piece.top);
    box.pieces.push(piece);
  }
  AREA_BOXES.set(area, box);
  return box;
}

// In some part's height band (no tolerance), in its outline (within 0.01 m of an edge counts), not well inside one of its holes.
function areaContains(area, p) {
  let box = AREA_BOXES.get(area);
  if (!box || box.pieces.length !== area.parts.length) box = areaPrepare(area);
  const x = p[0], y = p[1], z = p[2], e = AREA_NEAR;
  if (!box.pieces.length || y < box.bottom || y > box.top || x < box.minX - e || x > box.maxX + e || z < box.minZ - e || z > box.maxZ + e) return false;
  for (const piece of box.pieces) {
    if (y < piece.bottom || y > piece.top || x < piece.minX - e || x > piece.maxX + e || z < piece.minZ - e || z > piece.maxZ + e) continue;
    if (!areaInRing(piece.outline, x, z)) continue;
    if (!piece.holes.some(h => areaWellInside(h, x, z))) return true;
  }
  return false;
}

// Whether a player at p stands in a trigger's place: its area when it has one (the radius is then ignored), else the
// circle exactly as the plugin has always had it: within the radius across, and the floor above or below does not count.
function membershipInside(trigger, p) {
  if (!trigger) return false;
  if (trigger.area && trigger.area.parts && trigger.area.parts.length) return areaContains(trigger.area, p);
  if (!trigger.pos) return false;
  const dx = p[0] - trigger.pos[0], dz = p[2] - trigger.pos[2];
  return dx * dx + dz * dz <= trigger.radius * trigger.radius && Math.abs(p[1] - trigger.pos[1]) <= 2;
}

// Why a point is inside or outside a trigger's place (ML-043's probe), worked out by the same rules as membershipInside
// and areaContains, part by part, so the answer can name what decided it. A point is inside through the first part that
// takes it; outside, every part says what it failed: below or above its floor band (no tolerance), off its outline (an
// edge within 0.01 m counts), or well inside one of its holes (a hole's edge still counts).
// { inside, kind: 'area' | 'circle' | 'none', part, hole, edge, parts: [{ part, ok, fail: 'below' | 'above' | 'outline' | 'hole' | '', hole, bottom, top }],
//   distance, radius, rise (circle only), why }
function membershipExplain(trigger, p) {
  const m = n => String(Math.round(n * 100) / 100) + ' m';
  if (!trigger || !isVec(p)) return { inside: false, kind: 'none', part: -1, hole: -1, edge: false, parts: [], why: 'There is no place to be inside.' };
  const x = p[0], y = p[1], z = p[2];
  if (trigger.area && trigger.area.parts && trigger.area.parts.length) {
    const parts = [];
    for (let i = 0; i < trigger.area.parts.length; i++) {
      const part = trigger.area.parts[i], bottom = part.floor[0], top = part.floor[1], outline = areaFlat(part.outline);
      const one = { part: i, ok: false, fail: '', hole: -1, bottom, top };
      parts.push(one);
      if (y < bottom) { one.fail = 'below'; continue; }
      if (y > top) { one.fail = 'above'; continue; }
      if (!areaInRing(outline, x, z)) { one.fail = 'outline'; continue; }
      const holes = (part.holes || []).map(areaFlat);
      const cut = holes.findIndex(h => areaWellInside(h, x, z));
      if (cut >= 0) { one.fail = 'hole'; one.hole = cut; continue; }
      one.ok = true;
      const edge = areaOnEdge(outline, x, z) ? 'on the edge of its outline (within 0.01 m counts as on it)'
        : holes.some(h => areaOnEdge(h, x, z)) ? 'on the edge of one of its holes (a hole\'s edge belongs to the area)' : '';
      return { inside: true, kind: 'area', part: i, hole: -1, edge: !!edge, parts,
        why: 'Inside: part ' + (i + 1) + ' takes it, its floor band ' + m(bottom) + ' to ' + m(top) + ' holds the height ' + m(y) + (edge ? ', and it stands ' + edge : '') + '.' };
    }
    const said = parts.map(q => 'part ' + (q.part + 1) + (q.fail === 'below' ? ' starts at ' + m(q.bottom) + ', above this height'
      : q.fail === 'above' ? ' ends at ' + m(q.top) + ', below this height'
        : q.fail === 'hole' ? ': it is in hole ' + (q.hole + 1) + ', which is left out'
          : ': it is off the outline'));
    const hole = parts.find(q => q.fail === 'hole');
    return { inside: false, kind: 'area', part: -1, hole: hole ? hole.hole : -1, edge: false, parts, why: 'Outside at height ' + m(y) + ': ' + said.join('; ') + '.' };
  }
  if (!isVec(trigger.pos)) return { inside: false, kind: 'none', part: -1, hole: -1, edge: false, parts: [], why: 'This trigger has no place yet.' };
  const dx = x - trigger.pos[0], dz = z - trigger.pos[2], distance = Math.sqrt(dx * dx + dz * dz), rise = y - trigger.pos[1];
  const across = dx * dx + dz * dz <= trigger.radius * trigger.radius, level = Math.abs(rise) <= 2;
  const words = m(distance) + ' from the anchor across, the radius is ' + m(trigger.radius) + '; ' + m(Math.abs(rise)) + (rise < 0 ? ' below' : ' above') + ' it, and 2 m up or down counts';
  return { inside: across && level, kind: 'circle', part: -1, hole: -1, edge: false, parts: [], distance, radius: trigger.radius, rise,
    why: (across && level ? 'Inside the circle: ' : !across ? 'Outside the circle: ' : 'Outside: on another floor: ') + words + '.' };
}

// The strict rules for the "area" of a trigger or a place at `at` ('triggers[3]'; paths read 'triggers[3].area.parts[0].outline').
// At most one finding per ring: the first rule it breaks. bad(code, path, text) as in scenarioProblems.
function areaProblems(json, at, bad) {
  const a = at === '' ? 'area' : at + '.area';
  const isObj = v => v !== null && typeof v === 'object' && !Array.isArray(v), has = (o, k) => o[k] !== undefined && o[k] !== null;
  if (!isObj(json)) return bad('wrong-type', a, '"area" is an object: {"parts": [...]}');
  if (!Array.isArray(json.parts)) return bad('wrong-type', a + '.parts', '"parts" is a list of floor pieces');
  const count = json.parts.length;
  if (count === 0) return bad('bad-area', a + '.parts', 'an area needs at least one part');
  if (count > AREA_MAX_PARTS) return bad('bad-area', a + '.parts', 'an area has at most ' + AREA_MAX_PARTS + ' parts; this one has ' + count);
  const strict = (ring, path) => { const flat = []; for (let k = 0; k < ring.length; k++) { if (!areaPair(ring[k])) { bad('wrong-type', path + '[' + k + ']', 'a point is [x, z]: two numbers'); return null; } flat.push(ring[k][0], ring[k][1]); } return flat; };
  let points = 0;
  json.parts.forEach((part, j) => {
    const pa = a + '.parts[' + j + ']';
    if (!isObj(part)) return bad('wrong-type', pa, 'a part is an object: {"floor": [bottom, top], "outline": [[x, z], ...]}');
    if (!has(part, 'floor') || !areaPair(part.floor)) bad('wrong-type', pa + '.floor', '"floor" is [bottom, top]: two heights in metres');
    else if (part.floor[1] <= part.floor[0]) bad('bad-area', pa + '.floor', 'the top of "floor" must be above its bottom');
    else if (part.floor[1] - part.floor[0] > AREA_MAX_FLOOR) bad('bad-area', pa + '.floor', 'a part covers at most ' + AREA_MAX_FLOOR + ' m of height; split taller places into parts');

    let outline = null;
    if (!has(part, 'outline') || !Array.isArray(part.outline)) bad('wrong-type', pa + '.outline', '"outline" is a list of points [x, z]');
    else { points += part.outline.length; outline = strict(part.outline, pa + '.outline'); }

    const holes = [], holePaths = [];
    if (has(part, 'holes')) {
      if (!Array.isArray(part.holes)) bad('wrong-type', pa + '.holes', '"holes" is a list of rings');
      else part.holes.forEach((hole, h) => {
        const hp = pa + '.holes[' + h + ']';
        if (!Array.isArray(hole)) return bad('wrong-type', hp, 'a hole is a list of points [x, z]');
        points += hole.length;
        const cut = strict(hole, hp);
        if (cut) { holes.push(cut); holePaths.push(hp); }
      });
    }

    // Geometry: the outline, then each hole on its own, then each hole against the outline and the holes before it.
    if (outline) { const wrong = areaShape(outline, AREA_MIN_OUTLINE); if (wrong) { bad('bad-area', pa + '.outline', wrong); outline = null; } }
    const good = [];
    holes.forEach((hole, h) => {
      let wrong = areaShape(hole, AREA_MIN_HOLE);
      if (!wrong && outline && !areaHoleWithin(hole, outline)) wrong = 'a hole must lie inside its outline';
      for (let g = 0; !wrong && g < good.length; g++) if (areaOverlap(hole, good[g])) wrong = 'this hole overlaps an earlier hole; draw them as one';
      if (wrong) bad('bad-area', holePaths[h], wrong); else good.push(hole);
    });
  });
  if (points > AREA_MAX_POINTS) bad('bad-area', a, 'an area has at most ' + AREA_MAX_POINTS + ' points; this one has ' + points);
}

// ----- geometry, following Area.cs line for line

// Why a ring cannot be used, or '' when it can.
function areaShape(r, smallest) {
  const n = r.length / 2;
  if (n < 3) return 'a ring needs at least 3 points';
  if (n > AREA_MAX_RING) return 'a ring has at most ' + AREA_MAX_RING + ' points; this one has ' + n;
  for (let k = 0; k < r.length; k++) if (!(Math.abs(r[k]) <= AREA_MAX_COORD)) return 'a point lies more than ' + AREA_MAX_COORD + ' m from the middle of the map';
  for (let k = 0; k < n; k++) {
    const next = (k + 1) % n;
    if (r[2 * k] === r[2 * next] && r[2 * k + 1] === r[2 * next + 1]) return 'point ' + next + ' is the same as the one before it; leave out repeated points, including a closing one';
  }
  for (let i = 0; i < n; i++)
    for (let j = i + 2; j < n; j++) {
      if (i === 0 && j === n - 1) continue;    // neighbours share a corner
      if (areaTouch(r, i, r, j)) return 'the edges of this ring cross or touch each other';
    }
  if (Math.abs(areaShoelace(r)) / 2 < smallest) return 'the ring encloses less than ' + smallest + ' square metres';
  return '';
}
function areaShoelace(r) {
  const n = r.length / 2;
  let sum = 0;
  for (let k = 0; k < n; k++) { const next = (k + 1) % n; sum += r[2 * k] * r[2 * next + 1] - r[2 * next] * r[2 * k + 1]; }
  return sum;
}
const areaOrient = (ax, az, bx, bz, cx, cz) => (bx - ax) * (cz - az) - (bz - az) * (cx - ax);
const areaBetween = (ax, az, bx, bz, px, pz) => Math.min(ax, bx) <= px && px <= Math.max(ax, bx) && Math.min(az, bz) <= pz && pz <= Math.max(az, bz);
// Edge i of ring r against edge j of ring s: [d1, d2, d3, d4, and the eight coordinates].
function areaEdges(r, i, s, j) {
  const ni = (i + 1) % (r.length / 2), nj = (j + 1) % (s.length / 2);
  const ax = r[2 * i], az = r[2 * i + 1], bx = r[2 * ni], bz = r[2 * ni + 1], cx = s[2 * j], cz = s[2 * j + 1], dx = s[2 * nj], dz = s[2 * nj + 1];
  return [areaOrient(cx, cz, dx, dz, ax, az), areaOrient(cx, cz, dx, dz, bx, bz), areaOrient(ax, az, bx, bz, cx, cz), areaOrient(ax, az, bx, bz, dx, dz), ax, az, bx, bz, cx, cz, dx, dz];
}
const areaProper = (d1, d2, d3, d4) => ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
// Whether the two edges share any point: a crossing, a touch at an end, or a collinear overlap.
function areaTouch(r, i, s, j) {
  const [d1, d2, d3, d4, ax, az, bx, bz, cx, cz, dx, dz] = areaEdges(r, i, s, j);
  if (areaProper(d1, d2, d3, d4)) return true;
  return (d1 === 0 && areaBetween(cx, cz, dx, dz, ax, az)) || (d2 === 0 && areaBetween(cx, cz, dx, dz, bx, bz))
    || (d3 === 0 && areaBetween(ax, az, bx, bz, cx, cz)) || (d4 === 0 && areaBetween(ax, az, bx, bz, dx, dz));
}
// Whether the two edges cross at a point inside both; touching does not count.
function areaCrossing(r, s) {
  for (let i = 0; i < r.length / 2; i++)
    for (let j = 0; j < s.length / 2; j++) { const d = areaEdges(r, i, s, j); if (areaProper(d[0], d[1], d[2], d[3])) return true; }
  return false;
}
function areaDistance2(px, pz, ax, az, bx, bz) {
  const dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
  let t = len > 0 ? ((px - ax) * dx + (pz - az) * dz) / len : 0;
  if (t < 0) t = 0; else if (t > 1) t = 1;
  const qx = ax + t * dx - px, qz = az + t * dz - pz;
  return qx * qx + qz * qz;
}
function areaOnEdge(r, x, z) {
  const n = r.length / 2;
  for (let k = 0; k < n; k++) { const next = (k + 1) % n; if (areaDistance2(x, z, r[2 * k], r[2 * k + 1], r[2 * next], r[2 * next + 1]) <= AREA_NEAR * AREA_NEAR) return true; }
  return false;
}
// Even-odd: a ray towards +X crosses the ring's edges an odd number of times from inside.
function areaOdd(r, x, z) {
  const n = r.length / 2;
  let inside = false;
  for (let k = 0, prev = n - 1; k < n; prev = k++) {
    const xi = r[2 * k], zi = r[2 * k + 1], xj = r[2 * prev], zj = r[2 * prev + 1];
    if ((zi > z) !== (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
  }
  return inside;
}
// In the ring or on its edge: an outline's edge belongs to the area.
const areaInRing = (r, x, z) => areaOnEdge(r, x, z) || areaOdd(r, x, z);
// Inside and clear of the edge: a hole's edge still belongs to the area.
const areaWellInside = (r, x, z) => !areaOnEdge(r, x, z) && areaOdd(r, x, z);
function areaHoleWithin(hole, outline) {
  for (let k = 0; k < hole.length; k += 2) if (!areaInRing(outline, hole[k], hole[k + 1])) return false;
  return !areaCrossing(hole, outline);
}
function areaOverlap(a, b) {
  for (let k = 0; k < a.length; k += 2) if (areaWellInside(b, a[k], a[k + 1])) return true;
  for (let k = 0; k < b.length; k += 2) if (areaWellInside(a, b[k], b[k + 1])) return true;
  return areaCrossing(a, b);
}
// Where the straight ground-plan segment from a to b ([x, y, z] each) meets an edge of any outline or hole of the area, as
// fractions along it from 0 to 1, in order. The walkthrough (29j-walk.js) checks membership just either side of each.
function areaSegmentCrossings(area, a, b) {
  const out = [], ax = a[0], az = a[2], dx = b[0] - a[0], dz = b[2] - a[2];
  const ring = r => {
    const n = r.length / 2;
    for (let k = 0; k < n; k++) {
      const next = (k + 1) % n, ex = r[2 * next] - r[2 * k], ez = r[2 * next + 1] - r[2 * k + 1];
      const d = dx * ez - dz * ex;
      if (Math.abs(d) < 1e-12) continue;      // parallel: the ends' own membership says what happens along it
      const qx = r[2 * k] - ax, qz = r[2 * k + 1] - az, s = (qx * ez - qz * ex) / d, u = (qx * dz - qz * dx) / d;
      if (s >= 0 && s <= 1 && u >= 0 && u <= 1) out.push(s);
    }
  };
  for (const part of area.parts) { ring(areaFlat(part.outline)); for (const h of part.holes || []) ring(areaFlat(h)); }
  return out.sort((p, q) => p - q);
}

expose({ membershipExplain, areaSegmentCrossings });   // the probe and the walkthrough's crossing check (ML-043), for automated tests
