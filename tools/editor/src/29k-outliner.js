// ------------------------------------------------------------------ floors and rooms (ML-041)
// A workspace for working on one floor or one room of a building without touching the others by mistake. A floor group is
// a height band ("Ground floor, 0.2 to 3.2 m"); a building is a named set of Places, each place one of its rooms. Every
// floor, building and room can be focused, isolated (only it is shown, everything else faint), hidden or locked. What is
// hidden, faint or locked cannot be clicked, boxed, dragged, deleted or dropped onto from the map, in 2D or 3D, so an
// enemy upstairs is never moved through a click meant for the floor below and the other way round.
// All of it is display. It is kept on this computer under the map file's fingerprint and never in the scenario, so export,
// saving, the mission check and the game always see every object. Only Apply changes geometry, as one undo step. Another
// map file never takes over these floors silently: the author is asked first.
const OUTLINER_SLACK = 0.4;    // metres: markers stand on the walkable surface a little under the feet a band is made for; the "Show up to" filter allows the same
const OUTLINER_KEY = 'missionlab-workspace:';
// Each floor: name, band bottom, band top, and the player-foot sample height the band is meant to hold. Clean House's feet
// are 0.68 on the ground floor, 3.68 upstairs and -2.32 in the basement, and its painted ground floor started at 0.2
// (docs/findings.md); the bands here keep that 0.48 m under the feet on every floor. The keys are the options of #olPreset
// in 10-side.html.
const OUTLINER_PRESETS = [
  { key: 'clean-house', floors: [['Basement', -2.8, 0.2, -2.32], ['Ground floor', 0.2, 3.2, 0.68], ['Upstairs', 3.2, 6.2, 3.68]] },
  { key: 'storeys', floors: [['Ground floor', -0.5, 2.5, 0], ['First floor', 2.5, 5.5, 3], ['Second floor', 5.5, 8.5, 6]] },
  { key: 'map', floors: null },
];
state.outliner = state.outliner || {
  print: null,         // the map fingerprint this workspace belongs to ('example' for the invented map, '' for a map without one); null before any map
  mapSeen: undefined,  // the map object last looked at, so another map is noticed
  preset: '',          // the preset the floors last came from
  floors: [],          // { id, name, band: [bottom, top], feet, hidden, locked }, in display order
  buildings: [],       // { id, name, places: [place ids], hidden, locked }, in display order
  rooms: {},           // place id -> { hidden, locked }
  isolate: null,       // { kind: 'floor' | 'building' | 'room', id }: display only, never stored
  chosen: [],          // areas ticked for Apply: 'trigger:<id>', 'place:<id>', 'placement:<id>'
  review: null,        // floors and buildings made on another map file, waiting for the author to keep or drop them
  section: null,       // { a: [x, z], b: [x, z] }: the line of the vertical section
  drawing: false, sectionStart: null,     // the next clicks on the map draw the section line
  look: '', storeFailed: false,
};

// ----- the workspace as stored
function outlinerData(ws) {
  return JSON.parse(JSON.stringify({ preset: ws.preset, floors: ws.floors, buildings: ws.buildings, rooms: ws.rooms }));
}
// The lenient reading of a stored workspace: anything malformed is left out rather than guessed at.
function outlinerRead(json) {
  if (!json || typeof json !== 'object') return null;
  const num = v => typeof v === 'number' && isFinite(v), flag = v => v === true;
  const floors = (Array.isArray(json.floors) ? json.floors : []).filter(f => f && Number.isInteger(f.id) && typeof f.name === 'string' && Array.isArray(f.band) && num(f.band[0]) && num(f.band[1]) && f.band[1] > f.band[0])
    .map(f => ({ id: f.id, name: f.name.slice(0, 40), band: [f.band[0], f.band[1]], feet: num(f.feet) ? f.feet : f.band[0] + 0.5, hidden: flag(f.hidden), locked: flag(f.locked) }));
  const buildings = (Array.isArray(json.buildings) ? json.buildings : []).filter(b => b && Number.isInteger(b.id) && typeof b.name === 'string')
    .map(b => ({ id: b.id, name: b.name.slice(0, 40), places: (Array.isArray(b.places) ? b.places : []).filter(Number.isInteger), hidden: flag(b.hidden), locked: flag(b.locked) }));
  const rooms = {};
  if (json.rooms && typeof json.rooms === 'object') for (const k of Object.keys(json.rooms)) { const r = json.rooms[k]; if (r && /^\d+$/.test(k)) rooms[k] = { hidden: flag(r.hidden), locked: flag(r.locked) }; }
  return { preset: typeof json.preset === 'string' ? json.preset : '', floors, buildings, rooms };
}
function outlinerSetData(ws, data) {
  const d = outlinerRead(data) || { preset: '', floors: [], buildings: [], rooms: {} };
  ws.preset = d.preset; ws.floors = d.floors; ws.buildings = d.buildings; ws.rooms = d.rooms;
}
const outlinerPrintOf = m => (!m ? null : m.isExample ? 'example' : m.print || '');
function outlinerLoad(print) {
  if (!print) return null;
  try { const text = localStorage.getItem(OUTLINER_KEY + print); return text ? outlinerRead(JSON.parse(text)) : null; } catch (e) { return null; }
}
// Kept only for a map with a fingerprint, and never while floors from another map wait for review.
function outlinerSave() {
  const ws = state.outliner;
  if (!ws.print || ws.review) return;
  try { localStorage.setItem(OUTLINER_KEY + ws.print, JSON.stringify(Object.assign({ schema: 'missionlab-workspace/1', print: ws.print }, outlinerData(ws)))); }
  catch (e) { if (!ws.storeFailed) { ws.storeFailed = true; say('This browser will not keep floors and rooms between visits.'); } }
}
const outlinerHas = ws => ws.floors.length > 0 || ws.buildings.length > 0;
// Another map is open: its own stored workspace comes back; without one, what was made on the map before waits for the
// author's word and is not used until then, so no floor is ever reassigned behind the author's back.
function outlinerCheckMap() {
  const ws = state.outliner, m = state.map;
  ws.mapSeen = m;
  if (!m) return;
  const print = outlinerPrintOf(m);
  if (print === ws.print) return;
  const before = ws.print !== null && outlinerHas(ws) ? Object.assign({ from: ws.print }, outlinerData(ws)) : ws.review;
  const stored = outlinerLoad(print);
  outlinerSetData(ws, stored);
  ws.print = print; ws.isolate = null; ws.chosen = []; ws.section = null; ws.drawing = false; ws.sectionStart = null;
  ws.review = !stored && before ? Object.assign({}, before, { to: print }) : null;
}
const olWs = () => { const ws = state.outliner; if (ws.mapSeen !== state.map) outlinerCheckMap(); return ws; };
function outlinerReviewKeep() {
  const ws = olWs(), r = ws.review;
  if (!r) return false;
  outlinerSetData(ws, r); ws.review = null;
  outlinerChanged();
  say('The floors and buildings are kept for this map file as they were. Check each band against this map before you rely on them.');
  return true;
}
function outlinerReviewDrop() {
  const ws = olWs();
  if (!ws.review) return false;
  ws.review = null; outlinerChanged();
  say('This map file starts with no floors or buildings.');
  return true;
}

// ----- what is on which floor and in which room
const olFloor = id => state.outliner.floors.find(f => f.id === id) || null;
const olBuilding = id => state.outliner.buildings.find(b => b.id === id) || null;
const olRoom = id => state.outliner.rooms[id] || { hidden: false, locked: false };
// The floor a height belongs to: the band that holds it, the one starting highest when bands overlap (a marker at the top
// of the stairs stands on the upper floor).
function outlinerFloorAt(y) {
  let best = null;
  for (const f of state.outliner.floors) if (y >= f.band[0] - OUTLINER_SLACK && y <= f.band[1] && (!best || f.band[0] > best.band[0])) best = f;
  return best;
}
// A place holds a marker standing on the walkable surface when it holds the marker or the feet just above it.
const olHolds = (p, pos) => placeContains(p, pos) || placeContains(p, [pos[0], pos[1] + OUTLINER_SLACK, pos[2]]);
function outlinerInIsolated(pos) {
  const iso = state.outliner.isolate;
  if (!iso) return true;
  if (iso.kind === 'floor') { const f = outlinerFloorAt(pos[1]); return !!f && f.id === iso.id; }
  if (iso.kind === 'room') { const p = placeById(iso.id); return !p || olHolds(p, pos); }
  const b = olBuilding(iso.id);
  return !b || b.places.some(id => { const p = placeById(id); return !!p && olHolds(p, pos); });
}
// Whether anything is hidden, isolated or locked; nothing else needs looking at otherwise.
function outlinerActive() {
  const ws = olWs();
  if (ws.review) return false;
  if (ws.isolate) return true;
  for (const f of ws.floors) if (f.hidden || f.locked) return true;
  for (const b of ws.buildings) if (b.hidden || b.locked) return true;
  for (const k of Object.keys(ws.rooms)) if (ws.rooms[k].hidden || ws.rooms[k].locked) return true;
  return false;
}
// Why the map may not touch something at pos: 'hidden', 'ghost' (outside what is isolated), 'locked', or '' when it may.
function outlinerBlocks(pos) {
  if (!isVec(pos) || !outlinerActive()) return '';
  const ws = state.outliner, f = outlinerFloorAt(pos[1]);
  let hidden = !!(f && f.hidden), locked = !!(f && f.locked);
  for (const b of ws.buildings) {
    if (!b.hidden && !b.locked) continue;
    if (b.places.some(id => { const p = placeById(id); return !!p && olHolds(p, pos); })) { hidden = hidden || b.hidden; locked = locked || b.locked; }
  }
  for (const k of Object.keys(ws.rooms)) {
    const r = ws.rooms[k]; if (!r.hidden && !r.locked) continue;
    const p = placeById(+k);
    if (p && olHolds(p, pos)) { hidden = hidden || r.hidden; locked = locked || r.locked; }
  }
  if (hidden) return 'hidden';
  if (ws.isolate && !outlinerInIsolated(pos)) return 'ghost';
  return locked ? 'locked' : '';
}
// How strongly the map draws something at pos: 0 hidden, faint outside what is isolated, a little faint when locked.
function outlinerAlpha(pos) {
  const why = outlinerBlocks(pos);
  return why === 'hidden' ? 0 : why === 'ghost' ? 0.18 : why === 'locked' ? 0.6 : 1;
}
// The same for an area, through whatever holds it (a trigger, a place or a placement area) and its anchor.
function outlinerAreaAlpha(area) {
  if (!outlinerActive()) return 1;
  const h = state.zones.find(z => z.area === area) || state.places.find(p => p.area === area);
  return h && isVec(h.pos) ? outlinerAlpha(h.pos) : 1;
}
// The highest walkable floor at (x, z) that is shown and free: where a click or a drag puts something. Without anything
// hidden, isolated or locked it is exactly groundAt.
function outlinerGround(x, z) {
  if (!outlinerActive()) return groundAt(x, z);
  let best = null;
  for (const y of levelsAt(x, z)) if (y <= state.ceiling + 0.4 && !outlinerBlocks([x, y, z]) && (best === null || y > best)) best = y;
  return best;
}
// The flat map's ground: '' drawn as usual, 'hidden' left out, 'ghost' faint, for a triangle at height y.
function outlinerFloorLook(y) {
  const ws = state.outliner;
  if (ws.review) return '';
  const f = outlinerFloorAt(y);
  if (f && f.hidden) return 'hidden';
  if (ws.isolate && ws.isolate.kind === 'floor' && (!f || f.id !== ws.isolate.id)) return 'ghost';
  return '';
}
// What the ground's drawing depends on, so it is worked out again only when that changes.
function outlinerGroundKey() {
  const ws = olWs();
  if (ws.review) return '';
  const iso = ws.isolate && ws.isolate.kind === 'floor' ? ws.isolate.id : 0;
  if (!iso && !ws.floors.some(f => f.hidden)) return '';
  return JSON.stringify([iso, ws.floors.map(f => [f.id, f.band, f.hidden])]);
}
// Hidden, faint or locked objects leave the selection, so no key or drag can still reach them.
function outlinerPrune() {
  if (!outlinerActive()) return;
  const free = i => !!state.enemies[i] && !outlinerBlocks(state.enemies[i].pos);
  const keep = state.squad.filter(free);
  if (state.squad.length) { state.squad = keep.length > 1 ? keep : []; if (keep.length && !keep.includes(state.selected)) state.selected = keep[0]; }
  if (state.selected >= 0 && !free(state.selected)) state.selected = -1;
  if (state.thing >= 0 && state.things[state.thing] && outlinerBlocks(state.things[state.thing].pos)) state.thing = -1;
  if (state.door >= 0 && state.doors[state.door] && outlinerBlocks(state.doors[state.door].mid)) state.door = -1;
}
function outlinerChanged() {
  outlinerSave(); outlinerPrune();
  syncSide(); view3d.syncEnemies(); invalidate(); syncOutliner(true);
}

// Everything the outliner lists: { key, kind, index, id, name, pos (or null), area (or null), band (a point place's) }.
// A sealed mission that is not revealed lists only its places.
function outlinerItems() {
  const out = [], sealed = sealedHides(), shaped = a => !!(a && Array.isArray(a.parts) && a.parts.length);
  if (!sealed) {
    state.enemies.forEach((e, i) => out.push({ key: 'enemy:' + i, kind: 'enemy', index: i, name: 'Enemy ' + (i + 1), pos: e.pos, area: null }));
    state.zones.forEach((z, i) => { if (!isVec(z.pos) && !shaped(z.area)) return; out.push({ key: 'trigger:' + z.id, kind: 'trigger', index: i, id: z.id, name: 'Trigger ' + z.id + (z.name ? ' ' + z.name : ''), pos: isVec(z.pos) ? z.pos : null, area: shaped(z.area) ? z.area : null }); });
  }
  state.places.forEach((p, i) => out.push({ key: 'place:' + p.id, kind: 'place', index: i, id: p.id, name: p.name, pos: p.pos, area: shaped(p.area) ? p.area : null, band: p.floor }));
  if (!sealed) {
    state.placement.forEach((a, i) => { if (shaped(a.area)) out.push({ key: 'placement:' + a.id, kind: 'placement', index: i, id: a.id, name: a.name || 'Placement area ' + a.id, pos: null, area: a.area }); });
    state.things.forEach((t, i) => out.push({ key: 'thing:' + i, kind: 'thing', index: i, name: String(t.name).trim(), pos: t.pos, area: null }));
  }
  state.objectives.forEach((o, i) => out.push({ key: 'intel:' + i, kind: 'intel', index: i, name: 'Intel ' + (o.label || o.name), pos: o.pos, area: null }));
  return out;
}
const olBandHolds = (band, y) => band[0] <= y && y <= band[1];
// On a floor: a marker standing on it; an area or banded place whose band holds the floor's feet sample (an area reaching
// two floors is listed on both, which is how unintended coverage shows).
function outlinerOnFloor(f, items) {
  return (items || outlinerItems()).filter(it => {
    if (it.area) return it.area.parts.some(p => olBandHolds(p.floor, f.feet));
    if (it.band) return olBandHolds(it.band, f.feet);
    if (!isVec(it.pos)) return false;
    const on = outlinerFloorAt(it.pos[1]);
    return !!on && on.id === f.id;
  });
}
// In a room: markers it holds, and areas anchored in it.
function outlinerInRoom(place, items) {
  return (items || outlinerItems()).filter(it => !(it.kind === 'place' && it.id === place.id) && isVec(it.pos) && olHolds(place, it.pos));
}
const olBandable = it => !!it.area || it.kind === 'place';
function outlinerChosen() {
  const ws = olWs(), items = outlinerItems();
  return ws.chosen.map(k => items.find(it => it.key === k)).filter(it => it && olBandable(it));
}

// ----- changing the workspace (never the scenario)
const olNextId = list => list.reduce((m, q) => Math.max(m, q.id), 0) + 1;
function outlinerAddFloor(name, band, feet) {
  const ws = olWs();
  if (ws.review) { say('Keep or drop the floors from the other map file first.'); return null; }
  const lo = +band[0], hi = +band[1];
  if (!isFinite(lo) || !isFinite(hi) || hi <= lo) { say('A floor is two heights in metres, the top above the bottom.'); return null; }
  const given = feet !== null && feet !== undefined && isFinite(+feet);
  const f = { id: olNextId(ws.floors), name: String(name || 'Floor').slice(0, 40), band: [r2(lo), r2(hi)], feet: r2(given ? +feet : lo + 0.5), hidden: false, locked: false };
  ws.floors.push(f); outlinerChanged();
  return f;
}
// The floors flat walkable ground is found at on the open map: heights with a lot of level ground, at least 2.2 m apart.
function outlinerMapFloors() {
  const m = state.map;
  if (!m) return [];
  const bins = new Map(), t = m.tris;
  for (let i = 0; i < m.n; i++) {
    const o = i * 9, y0 = t[o + 1], y1 = t[o + 4], y2 = t[o + 7];
    if (Math.max(y0, y1, y2) - Math.min(y0, y1, y2) > 0.3) continue;          // slopes and stairs are not a floor
    const a = Math.abs((t[o + 3] - t[o]) * (t[o + 8] - t[o + 2]) - (t[o + 6] - t[o]) * (t[o + 5] - t[o + 2])) / 2, k = Math.round(m.avgY[i] / 0.5);
    const b = bins.get(k) || { w: 0, y: 0 }; b.w += a; b.y += a * m.avgY[i]; bins.set(k, b);
  }
  const total = [...bins.values()].reduce((s, b) => s + b.w, 0), picked = [];
  for (const b of [...bins.values()].sort((p, q) => q.w - p.w)) {
    if (b.w < total * 0.02 || picked.length >= 8) break;
    const y = b.y / b.w;
    if (picked.every(q => Math.abs(q - y) >= 2.2)) picked.push(y);
  }
  return picked.sort((p, q) => p - q).map(y => r2(y));
}
// A preset replaces the floor groups of the workspace; areas keep their bands until Apply.
function outlinerSetPreset(key) {
  const ws = olWs(), pre = OUTLINER_PRESETS.find(p => p.key === key);
  if (!pre) return false;
  if (ws.review) { say('Keep or drop the floors from the other map file first.'); return false; }
  let floors;
  if (pre.floors) floors = pre.floors.map(([name, lo, hi, feet], i) => ({ id: i + 1, name, band: [lo, hi], feet, hidden: false, locked: false }));
  else {
    const ys = outlinerMapFloors();
    if (!ys.length) { say('No floors were found: open a map file first.'); return false; }
    floors = ys.map((y, i) => ({ id: i + 1, name: ys.length === 1 ? 'Ground' : i === 0 && y < -1 ? 'Basement' : 'Floor at ' + y.toFixed(1) + ' m', band: [r2(y - 0.5), r2(i + 1 < ys.length ? Math.min(y + 2.6, ys[i + 1] - 0.5) : y + 2.6)], feet: y, hidden: false, locked: false }));
  }
  ws.floors = floors; ws.preset = key;
  if (ws.isolate && ws.isolate.kind === 'floor') ws.isolate = null;
  outlinerChanged();
  say(floors.length + (floors.length === 1 ? ' floor' : ' floors') + ' set up for this page. Areas keep their own bands until you use Apply.');
  return true;
}
function outlinerSetFloor(id, fields) {
  const f = olFloor(id);
  if (!f) return false;
  if (typeof fields.name === 'string') f.name = fields.name.slice(0, 40) || f.name;
  if (Array.isArray(fields.band)) {
    const lo = +fields.band[0], hi = +fields.band[1];
    if (!isFinite(lo) || !isFinite(hi) || hi <= lo) { say('The top of a floor must be above its bottom.'); syncOutliner(true); return false; }
    f.band = [r2(lo), r2(hi)];
  }
  if (fields.feet !== undefined && isFinite(+fields.feet)) f.feet = r2(+fields.feet);
  outlinerChanged();
  return true;
}
function outlinerAddBuilding(name, places) {
  const ws = olWs();
  if (ws.review) { say('Keep or drop the buildings from the other map file first.'); return null; }
  const b = { id: olNextId(ws.buildings), name: String(name || 'Building ' + olNextId(ws.buildings)).slice(0, 40), places: (places || []).filter(Number.isInteger), hidden: false, locked: false };
  ws.buildings.push(b); outlinerChanged();
  return b;
}
function outlinerAddRoom(buildingId, placeId) {
  const b = olBuilding(buildingId);
  if (!b || !placeById(placeId) || b.places.includes(placeId)) return false;
  b.places.push(placeId); outlinerChanged();
  return true;
}
function outlinerRemoveRoom(buildingId, placeId) {
  const b = olBuilding(buildingId);
  if (!b || !b.places.includes(placeId)) return false;
  b.places = b.places.filter(id => id !== placeId);
  if (!state.outliner.buildings.some(q => q.places.includes(placeId))) delete state.outliner.rooms[placeId];
  const iso = state.outliner.isolate; if (iso && iso.kind === 'room' && iso.id === placeId) state.outliner.isolate = null;
  outlinerChanged();
  return true;
}
function outlinerDelete(kind, id) {
  const ws = olWs(), list = kind === 'floor' ? ws.floors : ws.buildings, i = list.findIndex(q => q.id === id);
  if (i < 0) return false;
  list.splice(i, 1);
  if (ws.isolate && ws.isolate.kind === kind && ws.isolate.id === id) ws.isolate = null;
  outlinerChanged();
  return true;
}
// Display order only: floors and buildings are listed in the order the author puts them.
function outlinerMove(kind, id, dir) {
  const ws = olWs(), list = kind === 'floor' ? ws.floors : ws.buildings, i = list.findIndex(q => q.id === id), j = i + (dir < 0 ? -1 : 1);
  if (i < 0 || j < 0 || j >= list.length) return false;
  const q = list[i]; list[i] = list[j]; list[j] = q;
  outlinerChanged();
  return true;
}
// flag: 'hidden' or 'locked'; kind 'floor', 'building' or 'room' (a place id).
function outlinerFlag(kind, id, flag, on) {
  if (flag !== 'hidden' && flag !== 'locked') return false;
  const ws = olWs();
  let target = kind === 'floor' ? olFloor(id) : kind === 'building' ? olBuilding(id) : null;
  if (kind === 'room') { if (!placeById(id)) return false; target = ws.rooms[id] || (ws.rooms[id] = { hidden: false, locked: false }); }
  if (!target) return false;
  target[flag] = on === undefined ? !target[flag] : !!on;
  if (kind === 'room' && !target.hidden && !target.locked) delete ws.rooms[id];
  outlinerChanged();
  return true;
}
// Shows only one floor, building or room; the rest stays on the map, faint, and cannot be picked. The same again ends it.
function outlinerIsolate(kind, id) {
  const ws = olWs(), iso = ws.isolate;
  ws.isolate = !kind || (iso && iso.kind === kind && iso.id === id) ? null : { kind, id };
  outlinerChanged();
  return ws.isolate;
}
function outlinerChoose(key, on) {
  const ws = olWs(), has = ws.chosen.includes(key);
  if (on && !has) ws.chosen.push(key); else if (!on && has) ws.chosen = ws.chosen.filter(k => k !== key);
  syncOutliner(true);
}
// The one change to the scenario this part makes: every ticked area (each of its parts) and point place takes the floor's
// band, as one undo step. A zone linked to a place (ML-040) has no shape of its own: its place takes the band, and every
// zone linked to that place follows it.
function outlinerApply(floorId) {
  const f = olFloor(floorId), items = outlinerItems(), list = [];
  for (const it of outlinerChosen()) {
    const z = it.kind === 'trigger' ? state.zones[it.index] : null, own = z && z.place > 0 ? items.find(q => q.kind === 'place' && q.id === z.place) : it;
    if (own && !list.some(q => q.key === own.key)) list.push(own);
  }
  if (!f) { say('Choose the floor whose band the areas should take.'); return 0; }
  if (!list.length) { say('Tick the areas to change first: every floor and room lists its areas with a box to tick.'); return 0; }
  willChange((list.length === 1 ? 'Set the floor band of ' + list[0].name : 'Set the floor band of ' + list.length + ' areas') + ' to ' + f.name);
  for (const it of list) {
    if (it.area) { for (const p of it.area.parts) p.floor = f.band.slice(); areaPrepare(it.area); }
    else state.places[it.index].floor = f.band.slice();
  }
  placeLinksResolve();
  changed();
  say((list.length === 1 ? list[0].name + ' now counts' : list.length + ' areas now count') + ' between ' + f.band[0] + ' and ' + f.band[1] + ' m (' + f.name + '). Undo takes it back.');
  return list.length;
}

// ----- focus
// The ground-plan box and top height of a floor, building or room, from what is in it.
function outlinerBounds(kind, id) {
  const box = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity, y: 0, top: Infinity };
  const add = (x, z) => { box.minX = Math.min(box.minX, x); box.maxX = Math.max(box.maxX, x); box.minZ = Math.min(box.minZ, z); box.maxZ = Math.max(box.maxZ, z); };
  const addPlace = p => {
    if (p.area && p.area.parts && p.area.parts.length) for (const part of p.area.parts) for (const q of part.outline) add(q[0], q[1]);
    else { add(p.pos[0] - PLACE_REACH, p.pos[2] - PLACE_REACH); add(p.pos[0] + PLACE_REACH, p.pos[2] + PLACE_REACH); }
  };
  const top = p => (p.area && p.area.parts && p.area.parts.length ? Math.max(...p.area.parts.map(q => q.floor[1])) : p.floor ? p.floor[1] : p.pos[1] + PLACE_ABOVE);
  if (kind === 'floor') {
    const f = olFloor(id); if (!f) return null;
    for (const it of outlinerOnFloor(f)) { if (isVec(it.pos)) add(it.pos[0], it.pos[2]); if (it.area) for (const part of it.area.parts) for (const q of part.outline) add(q[0], q[1]); }
    if (!isFinite(box.minX) && state.map) { const b = state.map.bounds; add(b.minX, b.minZ); add(b.maxX, b.maxZ); }
    box.y = f.feet; box.top = f.band[1];
  } else {
    const places = kind === 'room' ? [placeById(id)] : ((olBuilding(id) || { places: [] }).places.map(placeById));
    const list = places.filter(Boolean);
    if (!list.length) return null;
    list.forEach(addPlace);
    box.y = Math.min(...list.map(p => p.pos[1])); box.top = Math.max(...list.map(top));
  }
  return isFinite(box.minX) ? box : null;
}
// Brings it into view with the floors above it left out, as showAt does in 3D, where it is seen from above.
function outlinerFocus(kind, id) {
  const box = outlinerBounds(kind, id);
  if (!box) { say('There is nothing to show there yet.'); return false; }
  const c = [(box.minX + box.maxX) / 2, box.y, (box.minZ + box.maxZ) / 2], hi = state.map ? state.map.bounds.maxY : Infinity;
  state.ceiling = box.top >= hi ? Infinity : box.top;
  const el = $('ceiling'); el.value = isFinite(state.ceiling) ? Math.min(+el.max, Math.max(+el.min, state.ceiling)) : el.max;
  if (state.mode === '3d') { view3d.lookFrom(c); syncSide(); return true; }
  const { w, h } = size();
  state.view.cx = c[0]; state.view.cz = c[2];
  state.view.scale = Math.max(0.15, Math.min(80, Math.min((w - 48) / Math.max(4, box.maxX - box.minX + 6), (h - 130) / Math.max(4, box.maxZ - box.minZ + 6))));
  syncSide(); invalidate();
  return true;
}

// ----- the vertical section
// A side view along the line from a to b ([x, z] each): the walkable levels under it, where it crosses each area part and
// that part's height band, the floors with their player-foot samples, stairs, and where bands of different areas overlap
// or touch or reach a floor they were not anchored on (a ground-floor area reaching the basement, say).
function sectionModel(a, b) {
  const dx = b[0] - a[0], dz = b[1] - a[1], length = Math.hypot(dx, dz);
  const model = { a, b, length, step: 0, samples: [], floors: [], feet: [], stairs: [], parts: [], notes: [] };
  if (!(length > 0.1)) return model;
  const n = Math.min(400, Math.max(2, Math.ceil(length / 0.25))), step = length / n;
  model.step = step;
  for (let k = 0; k <= n; k++) {
    const t = k * step, x = a[0] + dx * t / length, z = a[1] + dz * t / length;
    model.samples.push({ t, x, z, levels: state.map ? levelsAt(x, z).slice().sort((p, q) => p - q) : [] });
  }
  // Floors: heights with level ground along at least a metre of the line. Stairs add only a sample or two to each height.
  const bins = new Map();
  model.samples.forEach((s, k) => s.levels.forEach(y => { const key = Math.round(y / 0.2), bin = bins.get(key) || { ks: new Set(), ys: [] }; bin.ks.add(k); bin.ys.push(y); bins.set(key, bin); }));
  const enough = Math.max(4, Math.ceil(1 / step));
  let run = null;
  const close = () => { if (run) { const ys = run.sort((p, q) => p - q); model.floors.push(r2(ys[Math.floor(ys.length / 2)])); } run = null; };
  for (const key of [...bins.keys()].sort((p, q) => p - q)) {
    const bin = bins.get(key);
    if (bin.ks.size < enough) { close(); continue; }
    if (run && !bins.has(key - 1)) close();
    run = (run || []).concat(bin.ys);
  }
  close();
  // Stairs: walkable samples between floors, joined while they climb steadily from one sample to the next.
  const off = y => model.floors.every(f => Math.abs(f - y) > 0.35);
  const runs = [];
  model.samples.forEach((s, k) => s.levels.filter(off).forEach(y => {
    const r = runs.find(q => q.k === k - 1 && Math.abs(q.y - y) <= 0.8);
    if (r) { r.k = k; r.y = y; r.to = s.t; r.low = Math.min(r.low, y); r.high = Math.max(r.high, y); r.n++; r.path.push([s.t, y]); }
    else runs.push({ k, y, from: s.t, to: s.t, low: y, high: y, n: 1, path: [[s.t, y]] });
  }));
  model.stairs = runs.filter(r => r.n >= 2 && r.high - r.low >= 0.5).map(r => ({ from: r2(r.from), to: r2(r.to), low: r2(r.low), high: r2(r.high), path: r.path }));
  // Player-foot samples: the author's floors when there are any, else the floors found under the line.
  const ws = olWs();
  model.feet = ws.floors.length && !ws.review ? ws.floors.map(f => ({ name: f.name, y: f.feet, band: f.band.slice(), map: false }))
    : model.floors.map(y => ({ name: 'Floor at ' + y.toFixed(1) + ' m', y, band: null, map: true }));
  // The area parts the line crosses, with where along it.
  for (const it of outlinerItems()) {
    const parts = it.area ? it.area.parts : it.kind === 'place' && it.band ? [{ floor: it.band, circle: it.pos }] : [];
    parts.forEach((part, j) => {
      const ring = part.circle ? null : areaFlat(part.outline), holes = part.circle ? [] : (part.holes || []).map(areaFlat), spans = [];
      let open = null;
      for (const s of model.samples) {
        const inside = part.circle ? Math.hypot(s.x - part.circle[0], s.z - part.circle[2]) <= PLACE_REACH : areaInRing(ring, s.x, s.z) && !holes.some(hh => areaWellInside(hh, s.x, s.z));
        if (inside) { if (open) open.to = s.t; else open = { from: s.t, to: s.t }; }
        else if (open) { spans.push(open); open = null; }
      }
      if (open) spans.push(open);
      if (!spans.length) return;
      model.parts.push({ key: it.key, kind: it.kind, name: it.name + (parts.length > 1 ? ' part ' + (j + 1) : ''), owner: it.name, bottom: part.floor[0], top: part.floor[1], spans, anchor: isVec(it.pos) ? it.pos[1] : null });
    });
  }
  // Bands of different areas over the same stretch of the line.
  const meet = (p, q) => p.spans.some(u => q.spans.some(v => u.from <= v.to && v.from <= u.to));
  for (let i = 0; i < model.parts.length; i++) for (let j = i + 1; j < model.parts.length; j++) {
    const p = model.parts[i], q = model.parts[j];
    if (p.key === q.key || p.kind !== q.kind || !meet(p, q)) continue;     // a place over a trigger only names where it is; two triggers or two places on one spot are what can surprise
    const lo = Math.max(p.bottom, q.bottom), hi = Math.min(p.top, q.top);
    if (hi - lo > 0.01) model.notes.push({ kind: 'overlap', a: p.key, b: q.key, pa: i, pb: j, bottom: r2(lo), top: r2(hi), text: p.name + ' and ' + q.name + ' overlap from ' + r2(lo) + ' to ' + r2(hi) + ' m: a player there is inside both.' });
    else if (hi - lo >= -0.01) model.notes.push({ kind: 'touch', a: p.key, b: q.key, pa: i, pb: j, bottom: r2(lo), top: r2(lo), text: p.name + ' and ' + q.name + ' touch at ' + r2(lo) + ' m: a player whose feet are exactly there is inside both.' });
  }
  // A band holding the feet of a floor other than the one its area is anchored on.
  const lowest = model.feet.length ? Math.min(...model.feet.map(f => f.y)) : null;
  for (const p of model.parts) {
    const here = model.feet.filter(f => !f.map || model.samples.some(s => p.spans.some(u => s.t >= u.from && s.t <= u.to) && s.levels.some(y => Math.abs(y - f.y) <= 0.3)));
    if (!here.length) continue;
    const want = p.anchor !== null ? p.anchor : p.bottom + 0.5;
    const main = here.reduce((m, f) => (Math.abs(f.y - want) < Math.abs(m.y - want) ? f : m));
    for (const f of here) {
      if (f === main || !olBandHolds([p.bottom, p.top], f.y)) continue;
      const below = f.y < main.y, basement = below && f.y === lowest && f.y < 0;
      model.notes.push({ kind: below ? 'below' : 'above', a: p.key, floor: f.name, y: f.y, basement,
        text: p.name + ' (band ' + p.bottom + ' to ' + p.top + ' m) also holds the feet of ' + f.name + ' (' + f.y + ' m), ' + (below ? 'below' : 'above') + ' the floor it is anchored on'
          + (basement ? ': players in the basement count as inside.' : ': players there count as inside.') });
    }
  }
  for (const s of model.stairs) model.notes.push({ kind: 'stairs', bottom: s.low, top: s.high, text: 'Stairs from ' + s.low + ' to ' + s.high + ' m, ' + s.from + ' to ' + s.to + ' m along the line.' });
  return model;
}
// A section across a room (a place): along its longer side, through its middle, 2 m past each end.
function outlinerSectionThrough(placeId) {
  const box = outlinerBounds('room', placeId);
  if (!box) { say('Choose a room first: isolate one, or select a place in Places.'); return null; }
  const cx = (box.minX + box.maxX) / 2, cz = (box.minZ + box.maxZ) / 2, wide = box.maxX - box.minX >= box.maxZ - box.minZ;
  state.outliner.section = wide ? { a: [r2(box.minX - 2), r2(cz)], b: [r2(box.maxX + 2), r2(cz)] } : { a: [r2(cx), r2(box.minZ - 2)], b: [r2(cx), r2(box.maxZ + 2)] };
  syncOutliner(true); invalidate();
  return state.outliner.section;
}
function outlinerSectionClick(w) {
  const ws = state.outliner;
  if (!ws.sectionStart) { ws.sectionStart = w; say('Now click where the section line ends. Esc cancels.'); invalidate(); return; }
  ws.section = { a: ws.sectionStart.map(r2), b: w.map(r2) }; ws.sectionStart = null; ws.drawing = false;
  syncOutliner(true); invalidate();
  say('The section is drawn in Floors and rooms.');
}
canvas.addEventListener('pointerdown', ev => {
  if (!state.outliner.drawing || ev.button !== 0) return;
  ev.stopImmediatePropagation(); ev.preventDefault();
  const r = canvas.getBoundingClientRect();
  outlinerSectionClick(toWorld(ev.clientX - r.left, ev.clientY - r.top));
}, true);
window.addEventListener('keydown', ev => { if (ev.key === 'Escape' && state.outliner.drawing) { state.outliner.drawing = false; state.outliner.sectionStart = null; invalidate(); say('No section line drawn.'); } });

// ----- on the flat map
// Outside an isolated building or room, a veil of the map's own ground colour, so what is isolated stands out.
function drawOutlinerVeil() {
  const ws = state.outliner, iso = ws.isolate;
  if (!iso || iso.kind === 'floor' || ws.review) return;
  const places = (iso.kind === 'room' ? [placeById(iso.id)] : ((olBuilding(iso.id) || { places: [] }).places.map(placeById))).filter(Boolean);
  if (!places.length) return;
  const { w, h } = size();
  ctx.save(); ctx.beginPath(); ctx.rect(0, 0, w, h);
  for (const p of places) {
    if (p.area && p.area.parts && p.area.parts.length) for (const part of p.area.parts) part.outline.forEach((q, k) => { const [x, y] = toScreen(q[0], q[1]); if (k) ctx.lineTo(x, y); else ctx.moveTo(x, y); if (k === part.outline.length - 1) ctx.closePath(); });
    else { const [x, y] = toScreen(p.pos[0], p.pos[2]); ctx.moveTo(x + PLACE_REACH * state.view.scale, y); ctx.arc(x, y, PLACE_REACH * state.view.scale, 0, Math.PI * 2); ctx.closePath(); }
  }
  ctx.globalAlpha = 0.6; ctx.fillStyle = css.paper; ctx.fill('evenodd');
  ctx.restore();
}
// The section line, A to B, and its first end while it is being drawn.
function drawSectionLine() {
  const ws = state.outliner, s = ws.section, font = '600 11px ' + getComputedStyle(document.body).getPropertyValue('--mono');
  ctx.save();
  ctx.strokeStyle = css['accent-text']; ctx.fillStyle = css['accent-text']; ctx.lineWidth = 2; ctx.font = font; ctx.textBaseline = 'bottom'; ctx.textAlign = 'center';
  if (s) {
    const [ax, ay] = toScreen(s.a[0], s.a[1]), [bx, by] = toScreen(s.b[0], s.b[1]);
    ctx.setLineDash([7, 4]); ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke(); ctx.setLineDash([]);
    for (const [x, y, t] of [[ax, ay, 'A'], [bx, by, 'B']]) { ctx.beginPath(); ctx.arc(x, y, 4, 0, Math.PI * 2); ctx.fill(); ctx.lineWidth = 3; ctx.strokeStyle = css.paper; ctx.strokeText(t, x, y - 6); ctx.fillText(t, x, y - 6); }
  }
  if (ws.drawing && ws.sectionStart) { const [x, y] = toScreen(ws.sectionStart[0], ws.sectionStart[1]); ctx.beginPath(); ctx.arc(x, y, 5, 0, Math.PI * 2); ctx.fill(); }
  ctx.restore();
}

// ----- the side panel
const olMk = (tag, props) => Object.assign(document.createElement(tag), props || {});
function olButton(text, title, fn, pressed) {
  const b = olMk('button', { type: 'button', className: 'btn ol-btn', textContent: text, title: title || '' });
  if (pressed !== undefined) b.setAttribute('aria-pressed', String(!!pressed));
  b.addEventListener('click', fn);
  return b;
}
const olWhere = it => (isVec(it.pos) ? it.pos[0].toFixed(0) + ', ' + it.pos[2].toFixed(0) + ' · height ' + it.pos[1].toFixed(1) : '');
const olBandText = band => band[0] + ' to ' + band[1] + ' m';
// One listed object: a click shows it (and selects an enemy that may be picked); an area has a box to tick for Apply.
function olItemRow(it) {
  const li = olMk('li', { className: 'ol-item' }), why = isVec(it.pos) ? outlinerBlocks(it.pos) : '';
  if (olBandable(it)) {
    const c = olMk('input', { type: 'checkbox', checked: state.outliner.chosen.includes(it.key), title: 'Tick to give it a floor\'s band with Apply' });
    c.setAttribute('aria-label', 'Choose ' + it.name + ' for Apply');
    c.addEventListener('change', () => outlinerChoose(it.key, c.checked));
    li.appendChild(c);
  }
  const b = olMk('button', { type: 'button', className: 'ol-show' });
  const bands = it.area ? [...new Set(it.area.parts.map(p => olBandText(p.floor)))].join(', ') : it.band ? olBandText(it.band) : '';
  b.append(olMk('span', { textContent: it.name }), olMk('span', { className: 'ol-meta', textContent: (bands ? 'band ' + bands : olWhere(it)) + (why ? ' · ' + (why === 'ghost' ? 'faint' : why) : '') }));
  b.addEventListener('click', () => {
    if (it.kind === 'enemy' && !why) { state.selected = it.index; state.squad = []; state.door = -1; view3d.syncEnemies(); syncSide(); }
    const p = isVec(it.pos) ? it.pos : it.area ? [it.area.parts[0].outline[0][0], it.area.parts[0].floor[0] + 0.5, it.area.parts[0].outline[0][1]] : null;
    if (p) showAt(p, it.area ? Math.max(...it.area.parts.map(q => q.floor[1])) : p[1] + PLACE_ABOVE);
  });
  li.appendChild(b);
  return li;
}
function olItemList(items, empty) {
  const ul = olMk('ul', { className: 'ol-items' });
  if (!items.length) ul.appendChild(olMk('li', { className: 'hint', textContent: empty }));
  for (const it of items) ul.appendChild(olItemRow(it));
  return ul;
}
// The tools every entry has: Focus, Isolate, Hide, Lock; with order and delete for floors and buildings.
function olTools(kind, id, flags, extra) {
  const iso = state.outliner.isolate, isolated = !!iso && iso.kind === kind && iso.id === id, box = olMk('span', { className: 'ol-tools' });
  box.append(olButton('Focus', 'Show it on the map, with the floors above it left out', () => outlinerFocus(kind, id)),
    olButton(isolated ? 'Isolated' : 'Isolate', 'Show only this; the rest stays on the map, faint, and cannot be picked', () => outlinerIsolate(kind, id), isolated),
    olButton(flags.hidden ? 'Hidden' : 'Hide', 'Leave it off the map; it stays in the scenario', () => outlinerFlag(kind, id, 'hidden'), flags.hidden),
    olButton(flags.locked ? 'Locked' : 'Lock', 'Nothing on it can be clicked, boxed or dragged on the map', () => outlinerFlag(kind, id, 'locked'), flags.locked));
  for (const b of extra || []) box.appendChild(b);
  return box;
}
function olNameInput(value, label, fn) {
  const i = olMk('input', { type: 'text', className: 'ol-name', value, maxLength: 40, spellcheck: false });
  i.setAttribute('aria-label', label);
  i.addEventListener('change', () => fn(i.value.trim()));
  return i;
}
function olFloorRow(f, items, n, count) {
  const li = olMk('li', { className: 'ol-entry' }), head = olMk('div', { className: 'ol-head' });
  li.dataset.look = f.hidden ? 'hidden' : f.locked ? 'locked' : '';
  head.append(olNameInput(f.name, 'Name of the floor', v => { if (v) outlinerSetFloor(f.id, { name: v }); }), olMk('span', { className: 'ol-meta', textContent: olBandText(f.band) + ' · feet ' + f.feet }));
  const up = olButton('\u2191', 'Earlier in the list', () => outlinerMove('floor', f.id, -1)), down = olButton('\u2193', 'Later in the list', () => outlinerMove('floor', f.id, 1));
  up.disabled = n === 0; down.disabled = n === count - 1;
  li.append(head, olTools('floor', f.id, f, [up, down, olButton('Remove', 'Remove this floor from the list; the areas keep their bands', () => outlinerDelete('floor', f.id))]));
  const band = olMk('div', { className: 'ol-band' });
  const num = (label, value, fn) => { const l = olMk('label'), i = olMk('input', { type: 'number', step: '0.1', value: String(value) }); l.append(olMk('span', { textContent: label }), i); i.addEventListener('change', () => fn(+i.value)); return l; };
  band.append(num('From', f.band[0], v => outlinerSetFloor(f.id, { band: [v, f.band[1]] })), num('Up to', f.band[1], v => outlinerSetFloor(f.id, { band: [f.band[0], v] })), num('Feet', f.feet, v => outlinerSetFloor(f.id, { feet: v })));
  li.append(band, olItemList(outlinerOnFloor(f, items), 'Nothing on this floor yet.'));
  return li;
}
function olRoomRow(b, id, items) {
  const p = placeById(id), li = olMk('li', { className: 'ol-entry ol-room' }), head = olMk('div', { className: 'ol-head' });
  if (!p) {
    head.append(olMk('span', { textContent: 'Place ' + id + ' (not in this scenario)' }));
    li.append(head, olMk('span', { className: 'ol-tools' }));
    li.lastChild.appendChild(olButton('Remove', 'Take it out of the building', () => outlinerRemoveRoom(b.id, id)));
    return li;
  }
  const r = olRoom(id);
  li.dataset.look = r.hidden ? 'hidden' : r.locked ? 'locked' : '';
  head.append(olMk('b', { textContent: p.name }), olMk('span', { className: 'ol-meta', textContent: p.area && p.area.parts.length ? 'area' : 'point' }));
  li.append(head, olTools('room', id, r, [olButton('Remove', 'Take this room out of the building; the place stays', () => outlinerRemoveRoom(b.id, id))]), olItemList(outlinerInRoom(p, items), 'Nothing in this room yet.'));
  return li;
}
function olBuildingRow(b, items) {
  const li = olMk('li', { className: 'ol-entry' }), head = olMk('div', { className: 'ol-head' });
  li.dataset.look = b.hidden ? 'hidden' : b.locked ? 'locked' : '';
  head.append(olNameInput(b.name, 'Name of the building', v => { if (v) { b.name = v; outlinerChanged(); } }), olMk('span', { className: 'ol-meta', textContent: b.places.length + (b.places.length === 1 ? ' room' : ' rooms') }));
  li.append(head, olTools('building', b.id, b, [
    olButton('\u2191', 'Earlier in the list', () => outlinerMove('building', b.id, -1)), olButton('\u2193', 'Later in the list', () => outlinerMove('building', b.id, 1)),
    olButton('Remove', 'Remove this building from the list; its places stay', () => outlinerDelete('building', b.id))]));
  const rooms = olMk('ul', { className: 'ol-rooms' });
  for (const id of b.places) rooms.appendChild(olRoomRow(b, id, items));
  li.appendChild(rooms);
  const free = state.places.filter(p => !b.places.includes(p.id));
  if (free.length) {
    const row = olMk('div', { className: 'row' }), pick = olMk('select');
    pick.setAttribute('aria-label', 'Place to add to ' + b.name);
    for (const p of free) pick.appendChild(olMk('option', { value: String(p.id), textContent: p.name }));
    row.append(pick, olButton('Add room', 'Add the chosen place to this building as a room', () => outlinerAddRoom(b.id, +pick.value)));
    li.appendChild(row);
  } else if (!state.places.length) li.appendChild(olMk('p', { className: 'hint', textContent: 'Rooms are Places: add some under Places below, then add them here.' }));
  return li;
}
const svgNS = 'http://www.w3.org/2000/svg';
function olSvg(tag, attrs, text) {
  const el = document.createElementNS(svgNS, tag);
  for (const k of Object.keys(attrs)) el.setAttribute(k, String(attrs[k]));
  if (text !== undefined) el.textContent = text;
  return el;
}
// The section as a picture: distance along the line across, height up. Bands are boxes, overlaps hatched in amber, the
// ground under the line dots, stairs a line, and every player-foot sample a dashed level with its height written on it.
function sectionDraw(model) {
  const svg = $('olSection');
  svg.textContent = '';
  if (!model || !model.length) { svg.setAttribute('width', '0'); svg.setAttribute('height', '0'); return; }
  const W = 340, H = 230, L = 36, R = 8, T = 10, B = 22;
  const ys = [];
  for (const s of model.samples) ys.push(...s.levels);
  for (const p of model.parts) ys.push(Math.max(p.bottom, -20), Math.min(p.top, 30));
  for (const f of model.feet) ys.push(f.y);
  const lo = ys.length ? Math.min(...ys) - 0.5 : -1, hi = ys.length ? Math.max(...ys) + 0.5 : 4;
  const X = t => L + t / model.length * (W - L - R), Y = y => T + (hi - Math.max(lo, Math.min(hi, y))) / (hi - lo || 1) * (H - T - B);
  svg.setAttribute('width', String(W)); svg.setAttribute('height', String(H)); svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
  svg.appendChild(olSvg('rect', { class: 'sec-frame', x: L, y: T, width: W - L - R, height: H - T - B }));
  for (let k = Math.ceil(lo); k <= Math.floor(hi); k++) if (k % (hi - lo > 12 ? 5 : hi - lo > 6 ? 2 : 1) === 0) {
    svg.appendChild(olSvg('line', { class: 'sec-grid', x1: L, x2: W - R, y1: Y(k), y2: Y(k) }));
    svg.appendChild(olSvg('text', { class: 'sec-axis', x: L - 4, y: Y(k) + 3.5, 'text-anchor': 'end' }, String(k)));
  }
  svg.appendChild(olSvg('text', { class: 'sec-axis', x: L, y: H - 6 }, 'A'));
  svg.appendChild(olSvg('text', { class: 'sec-axis', x: W - R, y: H - 6, 'text-anchor': 'end' }, 'B · ' + model.length.toFixed(1) + ' m'));
  for (const p of model.parts) for (const u of p.spans) {
    const x0 = X(u.from - model.step / 2 < 0 ? 0 : u.from - model.step / 2), x1 = X(Math.min(model.length, u.to + model.step / 2));
    const r = olSvg('rect', { class: 'sec-band sec-' + p.kind, x: x0, y: Y(p.top), width: Math.max(1, x1 - x0), height: Math.max(1, Y(p.bottom) - Y(p.top)) });
    r.appendChild(olSvg('title', {}, p.name + ': ' + p.bottom + ' to ' + p.top + ' m'));
    svg.appendChild(r);
  }
  for (const n of model.notes) {
    if (n.kind !== 'overlap' && n.kind !== 'touch') continue;
    const p = model.parts[n.pa], q = model.parts[n.pb];
    for (const u of p.spans) for (const v of q.spans) {
      const from = Math.max(u.from, v.from), to = Math.min(u.to, v.to);
      if (from > to) continue;
      svg.appendChild(olSvg('rect', { class: 'sec-' + n.kind, x: X(from), y: Y(n.top) - (n.kind === 'touch' ? 1.5 : 0), width: Math.max(2, X(to) - X(from)), height: n.kind === 'touch' ? 3 : Math.max(2, Y(n.bottom) - Y(n.top)) }));
    }
  }
  for (const s of model.samples) for (const y of s.levels) svg.appendChild(olSvg('rect', { class: 'sec-ground', x: X(s.t) - 1, y: Y(y) - 1, width: 2, height: 2 }));
  for (const s of model.stairs) svg.appendChild(olSvg('polyline', { class: 'sec-stairs', points: s.path.map(q => X(q[0]).toFixed(1) + ',' + Y(q[1]).toFixed(1)).join(' ') }));
  for (const f of model.feet) {
    if (f.y < lo || f.y > hi) continue;
    svg.appendChild(olSvg('line', { class: 'sec-feet', x1: L, x2: W - R, y1: Y(f.y), y2: Y(f.y) }));
    svg.appendChild(olSvg('text', { class: 'sec-feet-text', x: W - R - 2, y: Y(f.y) - 3, 'text-anchor': 'end' }, f.name + ' · feet ' + f.y));
  }
}
const outlinerShown = () => viewIs('create') && ['map', 'all'].includes($('side').dataset.show);
// The panel, drawn again only when something it shows has changed (force: always).
function syncOutliner(force) {
  const ws = olWs(), items = outlinerItems();
  const look = JSON.stringify([outlinerData(ws), ws.isolate, ws.chosen, ws.review && [ws.review.from, ws.review.to], ws.section, ws.drawing,
    items.map(it => [it.key, it.name, isVec(it.pos) ? it.pos.map(r2) : 0, it.area ? areaSignature(it.area) : it.band || 0]), state.places.map(p => p.id), sealedHides()]);
  if (!force && look === ws.look) return;
  ws.look = look;
  $('olCount').textContent = ws.floors.length || ws.buildings.length ? ws.floors.length + ' · ' + ws.buildings.length : '';
  const r = ws.review;
  $('olReview').hidden = !r;
  if (r) $('olReviewWhy').textContent = 'This map file is not the one these floors and buildings were made on ('
    + (r.from === 'example' ? 'the example map' : r.from || 'a map without a fingerprint') + '). They are not used here until you say so: '
    + r.floors.map(f => f.name + ' ' + olBandText(f.band)).concat(r.buildings.map(b => b.name)).join('; ') + '.';
  $('olPreset').value = ws.preset || '';
  const floors = $('olFloors'); floors.textContent = '';
  ws.floors.forEach((f, n) => floors.appendChild(olFloorRow(f, items, n, ws.floors.length)));
  $('olFloorsEmpty').hidden = ws.floors.length > 0;
  const buildings = $('olBuildings'); buildings.textContent = '';
  for (const b of ws.buildings) buildings.appendChild(olBuildingRow(b, items));
  $('olBuildingsEmpty').hidden = ws.buildings.length > 0;
  const target = $('olApplyFloor'), was = target.value; target.textContent = '';
  for (const f of ws.floors) target.appendChild(olMk('option', { value: String(f.id), textContent: f.name + ' (' + olBandText(f.band) + ')' }));
  if (ws.floors.some(f => String(f.id) === was)) target.value = was;
  const chosen = outlinerChosen();
  $('olChosen').textContent = chosen.length ? 'Ticked: ' + chosen.map(it => it.name).join(', ') + '.' : 'No areas ticked. Tick them in the lists above.';
  $('olApply').disabled = !chosen.length || !ws.floors.length;
  $('olSectionDraw').textContent = ws.drawing ? 'Click two points on the map' : 'Draw a section line';
  $('olSectionDraw').setAttribute('aria-pressed', String(!!ws.drawing));
  const model = ws.section ? sectionModel(ws.section.a, ws.section.b) : null;
  sectionDraw(model);
  $('olSectionNote').textContent = !model ? 'Draw a line across the building, or take one through a room, to see its floors from the side.'
    : !model.parts.length ? 'No area or banded place crosses this line.' : model.parts.length + (model.parts.length === 1 ? ' band crosses' : ' bands cross') + ' the line.';
  const notes = $('olSectionNotes'); notes.textContent = '';
  for (const n of model ? model.notes : []) { const li = olMk('li', { textContent: n.text }); li.dataset.kind = n.kind; notes.appendChild(li); }
}
$('olPreset').addEventListener('change', ev => { if (ev.target.value) outlinerSetPreset(ev.target.value); });
$('olAddFloor').addEventListener('click', () => {
  const at = state.mode === '3d' ? view3d.focus() : [state.view.cx, state.view.cz], y = state.map ? groundAt(at[0], at[1]) : null, base = y === null ? 0 : y;
  outlinerAddFloor('Floor ' + (state.outliner.floors.length + 1), [base - 0.5, base + 2.6], base);
});
$('olAddBuilding').addEventListener('click', () => outlinerAddBuilding('Building ' + olNextId(state.outliner.buildings), placeUi.selected >= 0 && state.places[placeUi.selected] ? [state.places[placeUi.selected].id] : []));
$('olApply').addEventListener('click', () => outlinerApply(+$('olApplyFloor').value));
$('olReviewKeep').addEventListener('click', outlinerReviewKeep);
$('olReviewDrop').addEventListener('click', outlinerReviewDrop);
$('olSectionDraw').addEventListener('click', () => {
  const ws = state.outliner;
  ws.drawing = !ws.drawing; ws.sectionStart = null;
  if (ws.drawing) { if (state.mode === '3d') setMode('2d'); say('Click where the section line starts. Esc cancels.'); }
  syncOutliner(true); invalidate();
});
$('olSectionRoom').addEventListener('click', () => {
  const iso = state.outliner.isolate, p = iso && iso.kind === 'room' ? placeById(iso.id) : state.places[placeUi.selected];
  if (!p) { say('Isolate a room first, or select a place under Places.'); return; }
  outlinerSectionThrough(p.id);
});
$('olSectionClear').addEventListener('click', () => { state.outliner.section = null; syncOutliner(true); invalidate(); });
// Twice a second or so while it is on screen, like the mission graph.
setInterval(() => { if (outlinerShown()) syncOutliner(false); }, 700);
expose({ outlinerCheckMap, outlinerBlocks, outlinerGround, outlinerActive, outlinerItems, outlinerOnFloor, outlinerInRoom, outlinerAddFloor, outlinerSetFloor, outlinerSetPreset,
  outlinerMapFloors, outlinerAddBuilding, outlinerAddRoom, outlinerRemoveRoom, outlinerDelete, outlinerMove, outlinerFlag, outlinerIsolate, outlinerChoose, outlinerApply,
  outlinerFocus, outlinerReviewKeep, outlinerReviewDrop, outlinerSectionThrough, sectionModel, syncOutliner, outlinerWorkspace: () => Object.assign({ print: olWs().print, review: state.outliner.review }, outlinerData(state.outliner)) });
