// ------------------------------------------------------------------ scenario data
// While connected to the game, a new enemy starts out looking at the nearest player.
function facePlayers(e) {
  let best = null;
  for (const p of live.players) { const d = Math.hypot(p.pos[0] - e.pos[0], p.pos[2] - e.pos[2]); if (!best || d < best.d) best = { d, p }; }
  if (live.on && best && best.d > 0.5) e.yaw = (Math.atan2(best.p.pos[0] - e.pos[0], best.p.pos[2] - e.pos[2]) * 180 / Math.PI + 360) % 360;
  return e;
}
function newEnemy(pos, role, group) {
  return { group: group || 0, role: role || 'Guard', pos: pos.slice(), yaw: 0, indoor: false, defensive: false, fixed: false, prone: false, type: '', trigger: 0, hvt: false, vehicle: false, chance: 100, lot: 0, either: 0, option: 0, team: 0, minPlayers: 0, skill: '', nvg: false, tag: '',
           detectionRange: 0, hearingRange: 0, fov: 0, wanderDistance: 0, patrolWaitSeconds: 4, patrol: [] };
}
const r2 = v => Math.round(v * 100) / 100;
const r3 = p => [r2(p[0]), r2(p[1]), r2(p[2])];
const num = (v, d) => (typeof v === 'number' && isFinite(v) ? v : d);
function fileName() { return (state.title.trim().toLowerCase().replace(/\.json$/, '').replace(/[^a-z0-9_-]+/g, '_').replace(/^_+|_+$/g, '') || 'scenario') + '.json'; }
function scenarioObject() {
  return {
    schema: 'missionlab-plan/1',
    name: state.title.trim(),
    scene: state.scene.trim(),
    navFile: state.navFile,
    seed: 0,
    notes: ['Made with the MissionLab scenario editor'],
    triggers: zonesOut(),
    objectives: objectivesOut(true),
    doors: doorsOut(),
    ...(state.things.length ? { things: thingsOut() } : {}),
    ...settingsOut(),
    spawns: state.enemies.map(spawnOf),
  };
}
function spawnOf(e) {
  return {
    group: e.group, role: e.role, pos: r3(e.pos), yaw: r2(e.yaw), indoor: !!e.indoor, defensive: !!e.defensive,
    detectionRange: r2(e.detectionRange), hearingRange: r2(e.hearingRange), fov: r2(e.fov),
    wanderDistance: Math.round(e.wanderDistance), patrolWaitSeconds: r2(e.patrolWaitSeconds), fixed: !!e.fixed,
    prone: !!e.prone, type: e.type || '', trigger: state.zones.some(z => z.id === e.trigger) ? e.trigger : 0, hvt: !!e.hvt, ...(e.vehicle ? { vehicle: true } : {}),
    ...(e.chance < 100 ? { chance: e.chance } : {}), ...(e.chance < 100 && e.lot ? { lot: e.lot } : {}), ...(e.either ? { either: e.either, option: e.option || 1 } : {}), ...(e.team ? { team: e.team } : {}),
    ...(e.minPlayers > 1 ? { minPlayers: e.minPlayers } : {}), ...(e.skill ? { skill: e.skill } : {}), ...(e.nvg ? { nvg: true } : {}), ...(e.tag ? { tag: e.tag } : {}),
    patrol: e.role === 'Patrol' && !e.fixed ? e.patrol.map(r3) : [],
  };
}
// A file only names the items somebody moved; the game goes on choosing the place of the others itself.
const objectivesOut = forFile => state.objectives.filter(o => !forFile || o.moved !== false).map(o => ({ name: o.name, pos: r3(o.pos), yaw: r2(o.yaw || 0) }));
const objectivesIn = list => (Array.isArray(list) ? list : []).filter(o => o && typeof o.name === 'string' && o.name && isVec(o.pos)).map(o => ({ name: o.name, label: typeof o.label === 'string' && o.label ? o.label : o.name.replace(/_\d+$/, ''), pos: o.pos.slice(0, 3), yaw: num(o.yaw, 0), obtained: !!o.obtained,
  home: isVec(o.home) ? o.home.slice(0, 3) : null, homeYaw: num(o.homeYaw, 0), moved: o.moved !== false }));
// Doors. A door is known by where its hinge is. Only doors somebody set are written; null means "left to the game".
const tri = v => v === true ? true : v === false ? false : null;
const doorSet = d => d.locked !== null || d.enemiesCanOpen !== null || d.open != null;
const doorsOut = () => state.doors.filter(doorSet).map(d => {
  const o = { name: d.name || '', pos: r3(d.pos) };
  if (d.locked !== null) o.locked = d.locked;
  if (d.enemiesCanOpen !== null) o.enemiesCanOpen = d.enemiesCanOpen;
  if (d.open != null) o.open = d.open;
  if (d.chance < 100) o.chance = d.chance;
  return o;
});
// The map's doors as the game or a recon file lists them.
const doorsIn = list => (Array.isArray(list) ? list : []).filter(d => d && d.active !== false && (isVec(d.hinge) || isVec(d.pos))).map(d => {
  const pos = (isVec(d.hinge) ? d.hinge : d.pos).slice(0, 3);
  return { name: typeof d.name === 'string' ? d.name : '', pos, mid: isVec(d.mid) ? d.mid.slice(0, 3) : pos.slice(), locked: tri(d.locked), enemiesCanOpen: tri(d.enemiesCanOpen), open: tri(d.open), isOpen: tri(d.isOpen), chance: Math.max(1, Math.min(100, Math.round(num(d.chance, 100)))),
    isLocked: tri(d.isLocked), enemiesKeptOut: tri(d.enemiesKeptOut), broken: !!d.broken, known: true };
});
function nearDoor(pos, list) {
  let best = -1, bd = 0.75;
  list.forEach((d, i) => { const dist = Math.hypot(d.pos[0] - pos[0], d.pos[1] - pos[1], d.pos[2] - pos[2]); if (dist < bd) { bd = dist; best = i; } });
  return best;
}
// Settings go onto the doors we know. One for a door we have not seen is kept as it is, so nothing is lost before the map's doors are known.
function applyDoorWishes(wishes) {
  for (const w of (Array.isArray(wishes) ? wishes : [])) {
    if (!w || !isVec(w.pos) || (tri(w.locked) === null && tri(w.enemiesCanOpen) === null && tri(w.open) === null)) continue;
    const i = nearDoor(w.pos, state.doors);
    const ch = Math.max(1, Math.min(100, Math.round(num(w.chance, 100))));
    if (i >= 0) { state.doors[i].locked = tri(w.locked); state.doors[i].enemiesCanOpen = tri(w.enemiesCanOpen); state.doors[i].open = tri(w.open); state.doors[i].chance = ch; }
    else state.doors.push({ name: typeof w.name === 'string' ? w.name : '', pos: w.pos.slice(0, 3), mid: w.pos.slice(0, 3), locked: tri(w.locked), enemiesCanOpen: tri(w.enemiesCanOpen), open: tri(w.open), chance: ch, isLocked: null, enemiesKeptOut: null, broken: false, known: false });
  }
}
function setDoorWishes(wishes) {
  state.doors = state.doors.filter(d => d.known);
  for (const d of state.doors) { d.locked = null; d.enemiesCanOpen = null; d.open = null; d.chance = 100; }
  applyDoorWishes(wishes); state.door = -1;
}
function setKnownDoors(list) { const wishes = doorsOut(); state.doors = list; applyDoorWishes(wishes); state.door = -1; }
// Settings for the whole mission. Only what somebody set is written.
function settingsOut() {
  const o = {}, w = {};
  if (state.extractionAfter && state.zones.some(z => z.id === state.extractionAfter)) o.extraction = { after: state.extractionAfter };
  if (state.insertion) o.insertion = state.insertion;
  if (state.power) o.power = state.power;
  if (state.allSkill) o.skill = state.allSkill;
  if (state.allNvg) o.nvg = true;
  if (state.gameMaster) o.gameMaster = true;
  if (state.respawnAfter > 0) o.respawn = { after: r2(state.respawnAfter), ...(state.respawnTimes > 0 ? { times: state.respawnTimes } : {}) };
  if (state.briefing.length) o.briefing = state.briefing.slice(0, 4);
  if (state.weather.hour !== null) w.hour = r2(state.weather.hour);
  if (state.weather.rain !== null) w.rain = state.weather.rain;
  if (state.weather.sandstorm !== null) w.sandstorm = state.weather.sandstorm;
  if (Object.keys(w).length) o.weather = w;
  return o;
}
function settingsIn(root) {
  const ex = root && root.extraction, w = (root && root.weather) || {};
  state.extractionAfter = ex && typeof ex === 'object' ? Math.max(0, num(ex.after, 0) | 0) : 0;
  state.insertion = root && typeof root.insertion === 'string' ? root.insertion : '';
  state.power = root && (root.power === 'off' || root.power === 'on') ? root.power : '';
  state.briefing = briefingIn(root && root.briefing);
  state.allSkill = root && (root.skill === 'recruit' || root.skill === 'veteran') ? root.skill : ''; state.allNvg = !!(root && root.nvg === true); state.gameMaster = !!(root && root.gameMaster === true);
  const rs = root && root.respawn && typeof root.respawn === 'object' ? root.respawn : {};
  state.respawnAfter = num(rs.after, 0) > 0 ? Math.max(3, Math.min(600, num(rs.after, 0))) : 0; state.respawnTimes = Math.max(0, Math.min(50, num(rs.times, 0) | 0));
  state.weather = { hour: Number.isFinite(w.hour) ? Math.max(0, Math.min(24, w.hour)) : null, rain: tri(w.rain), sandstorm: tri(w.sandstorm) };
}
const WHENS = ['zone', 'alert', 'intel', 'time', 'after', 'down', 'all', 'any', 'door'];
const cleanTag = t => String(t || '').replace(/[^\p{L}\p{N} _-]/gu, '').trim().slice(0, 24);
const tagsOf = () => [...new Set(state.enemies.map(e => e.tag).filter(Boolean))].sort();
// Which known door a door trigger points at: its place in the list, or '' when none is near enough.
const doorOf = z => { if (!isVec(z.pos)) return ''; let best = '', bd = 0.6; state.doors.forEach((d, i) => { const dd = Math.hypot(d.pos[0] - z.pos[0], d.pos[1] - z.pos[1], d.pos[2] - z.pos[2]); if (dd < bd) { bd = dd; best = String(i); } }); return best; };
// What the game can place, by its own names. Rifles were seen to work; the rest is offered as found in the game's list.
const THING_GROUPS = [['Weapons', ["AK-74M", "AKM", "B&T APC9", "BLACKHAWK DE-MS", "Benelli M2", "F2000", "FN FNX-45", "Glock 17", "Glock 18C", "Glock 19x", "Glock 45 Legacy", "HK416A5", "HK416D", "M&P 9 2.0", "M320", "M45A1", "M4A1", "M72 LAW", "M9A1", "MCX", "MCX SPEAR", "MK16", "MK17", "MK46 MOD 2", "MP5", "MP7A1", "MP7A2", "MP9-N", "Noveske N4", "R700", "Remington M870", "SIG M17", "SIG MG338", "SIG P226", "SIG P320x", "SPR300", "SR-25", "STACCATO 2011", "SVD", "TT 2011 PitViper", "USP 45", "USP45 Tactical", "VKS"]], ['Intel items (not tried yet)', ["CIA Contacts Phone", "Cell Phone", "Encrypted Walkie Talkie", "Golden Watch", "HVI'S Classified Document", "HVT's Log Book", "Hard Drive", "Mexico_Contents", "Mexico_Document", "Mexico_Document Logs", "Mexico_Phone Numbers", "Mexico_Routes Document", "RFID Card", "Sim cards", "Tactical Encryption Unit", "Thumbdrive"]], ['Explosives (not tried yet)', ["Landmine", "Proximity Explosive_IED ", "Slap Charge"]], ['Other (not tried yet)', ["Chair_Leather_Interactable"]]];
const pct = v => Math.max(1, Math.min(100, Math.round(num(v, 100))));
const thingsIn = list => (Array.isArray(list) ? list : []).filter(t => t && typeof t.name === 'string' && t.name && isVec(t.pos)).map(t => ({ name: t.name, pos: t.pos.slice(0, 3), yaw: num(t.yaw, 0), trigger: Math.max(0, num(t.trigger, 0) | 0), chance: pct(t.chance) }));
const thingsOut = () => state.things.map(t => ({ name: t.name, pos: r3(t.pos), yaw: r2(t.yaw), ...(t.trigger && state.zones.some(z => z.id === t.trigger) ? { trigger: t.trigger } : {}), ...(t.chance < 100 ? { chance: t.chance } : {}) }));
const briefingIn = list => (Array.isArray(list) ? list : []).map(cleanMessage).filter(Boolean).slice(0, 4);
const zonesOut = () => state.zones.map(z => {
  const o = { id: z.id, name: z.name || '', when: z.when || 'zone' };
  if (o.when === 'zone') { o.pos = r3(z.pos); o.radius = r2(z.radius); if (z.hold > 0) { o.when = 'hold'; o.hold = r2(z.hold); } if (z.everyone) o.everyone = true; }
  if (o.when === 'door' && isVec(z.pos)) o.pos = r3(z.pos);
  if (o.when === 'down') { o.count = Math.max(0, z.count | 0); if (z.tag) o.tag = z.tag; }
  if (o.when === 'all' || o.when === 'any') o.of = (z.of || []).filter(id => id !== z.id && state.zones.some(q => q.id === id));
  if (z.chance < 100) o.chance = Math.max(1, z.chance | 0);
  if (z.warn) o.warn = true;
  if (z.repeat > 0) { o.repeat = r2(z.repeat); if (z.times > 0) o.times = z.times | 0; }
  if (z.rally > 0) o.rally = r2(z.rally);
  if (z.red && cleanMessage(z.message)) o.red = true;
  if (z.hunt) o.hunt = true;
  if (z.respawn) o.respawn = true;
  if (o.when === 'intel') o.item = z.item || '';
  if (o.when === 'after') o.after = z.after | 0;
  if (z.delay > 0) o.delay = r2(z.delay);
  o.facePlayer = z.facePlayer !== false;
  const doors = (z.doors || []).filter(d => d.locked != null || d.open != null).map(d => ({ name: d.name || '', pos: r3(d.pos), ...(d.locked != null ? { locked: d.locked } : {}), ...(d.open != null ? { open: d.open } : {}) }));
  if (doors.length) o.doors = doors;
  if (z.opensExtraction) o.extraction = 'open';
  if (z.power === 'off' || z.power === 'on') o.power = z.power;
  if (cleanMessage(z.message)) o.message = cleanMessage(z.message);
  if (z.end === 'success' || z.end === 'failed') o.end = z.end;
  return o;
});
const newTrigger = (id, when, more) => Object.assign({ id, name: '', when, pos: null, radius: 12, facePlayer: true, delay: 0, item: '', after: 0, doors: [], opensExtraction: false, power: '', message: '', end: '', hold: 0, count: 0, of: [], chance: 100, warn: false, everyone: false, tag: '', repeat: 0, times: 0, rally: 0, red: false, hunt: false, respawn: false }, more || {});
const cleanMessage = t => String(t || '').replace(/\s+/g, ' ').trim().slice(0, 120);
// What a trigger waits for, in a few words.
const trigTitle = z => (z.when === 'zone' ? 'Zone ' : 'Trigger ') + z.id;
const trigText = z => z.when === 'zone' ? (z.hold > 0 ? 'When players hold zone ' : 'When players reach zone ') + z.id : (z.when === 'door' ? 'When a door is opened' : z.when === 'down' ? 'When enemies are down' : z.when === 'all' ? 'When all of these have fired' : z.when === 'any' ? 'When one of these has fired' : z.when === 'alert' ? 'When the enemy is alerted' : z.when === 'intel' ? 'When intel is picked up' : z.when === 'time' ? 'After a set time' : 'After another trigger') + ' (trigger ' + z.id + ')';
const trigTag = id => { const z = state.zones.find(q => q.id === id); return (z && z.when !== 'zone' ? 'trigger ' : 'zone ') + id; };
// A file from before there were other kinds has only zones; a zone needs a place, the other kinds do not.
const zonesIn = list => (Array.isArray(list) ? list : []).filter(z => z && z.id > 0 && (isVec(z.pos) || (WHENS.includes(z.when) && z.when !== 'zone'))).map(z => newTrigger(z.id | 0, WHENS.includes(z.when) ? z.when : 'zone', {
  hold: z.when === 'hold' ? Math.max(1, Math.min(3600, num(z.hold, 30))) : 0, count: Math.max(0, num(z.count, 0) | 0),
  of: (Array.isArray(z.of) ? z.of : []).map(v => v | 0).filter(v => v > 0), chance: Math.max(1, Math.min(100, Math.round(num(z.chance, 100)))), warn: z.warn === true,
  everyone: z.everyone === true, tag: cleanTag(z.tag), repeat: Math.max(0, Math.min(3600, num(z.repeat, 0))), times: Math.max(0, Math.min(100, num(z.times, 0) | 0)), rally: Math.max(0, Math.min(300, num(z.rally, 0))), red: z.red === true, hunt: z.hunt === true, respawn: z.respawn === true,
  name: typeof z.name === 'string' ? z.name : '', pos: isVec(z.pos) ? z.pos.slice(0, 3) : null, radius: Math.max(1, num(z.radius, 10)), facePlayer: z.facePlayer !== false,
  delay: Math.max(0, Math.min(7200, num(z.delay, 0))), item: typeof z.item === 'string' ? z.item : '', after: num(z.after, 0) | 0, opensExtraction: z.extraction === 'open',
  power: z.power === 'off' || z.power === 'on' ? z.power : '', message: cleanMessage(z.message), end: z.end === 'success' || z.end === 'failed' ? z.end : '',
  doors: (Array.isArray(z.doors) ? z.doors : []).filter(d => d && isVec(d.pos) && (tri(d.locked) !== null || tri(d.open) !== null)).map(d => ({ name: typeof d.name === 'string' ? d.name : '', pos: d.pos.slice(0, 3), locked: tri(d.locked), open: tri(d.open) })) }));
function enemyOf(s, i) {
  if (!s || !isVec(s.pos)) throw new Error('Enemy ' + (i + 1) + ' has no "pos": [x, y, z].');
  const role = ROLES.find(r => r.toLowerCase() === String(s.role || 'Guard').toLowerCase());
  if (!role) throw new Error('Enemy ' + (i + 1) + ' has an unknown role "' + s.role + '".');
  return Object.assign(newEnemy(s.pos.slice(0, 3), role, Math.max(0, num(s.group, 0) | 0)), {
    yaw: ((num(s.yaw, 0) % 360) + 360) % 360, indoor: !!s.indoor, defensive: !!s.defensive, fixed: !!s.fixed,
    prone: !!s.prone, type: typeof s.type === 'string' ? s.type : '', trigger: num(s.trigger, 0) | 0, hvt: !!s.hvt, vehicle: !!s.vehicle,
    chance: Math.max(1, Math.min(100, Math.round(num(s.chance, 100)))), lot: Math.max(0, num(s.lot, 0) | 0), either: Math.max(0, num(s.either, 0) | 0), option: num(s.either, 0) > 0 ? Math.max(1, num(s.option, 1) | 0) : 0,
    team: Math.max(0, Math.min(8, num(s.team, 0) | 0)),
    minPlayers: Math.max(0, Math.min(16, num(s.minPlayers, 0) | 0)), skill: s.skill === 'recruit' || s.skill === 'veteran' ? s.skill : '', nvg: s.nvg === true, tag: cleanTag(s.tag),
    detectionRange: num(s.detectionRange, 0), hearingRange: num(s.hearingRange, 0), fov: num(s.fov, 0),
    wanderDistance: num(s.wanderDistance, 0), patrolWaitSeconds: num(s.patrolWaitSeconds, 4),
    patrol: Array.isArray(s.patrol) ? s.patrol.filter(isVec).map(p => p.slice(0, 3)) : [],
  });
}
// Solid map geometry exported by the mod. Returns the number of triangles kept.
function loadGeo(buf, name) {
  const dv = new DataView(buf), v2 = new TextDecoder().decode(new Uint8Array(buf, 0, 6)) === 'MLGEO2';
  const nv = dv.getInt32(8, true), ni = dv.getInt32(12, true), ng = v2 ? dv.getInt32(16, true) : 0, head = v2 ? 20 : 16;
  if (nv <= 0 || ni <= 0 || ng < 0 || head + nv * 12 + ni * 4 + ng * 16 > buf.byteLength) throw new Error('The 3D map file is cut off or damaged. Export it again.');
  const pos = new Float32Array(buf.slice(head, head + nv * 12)), all = new Uint32Array(buf.slice(head + nv * 12, head + nv * 12 + ni * 4));
  let groups = [], layers = [], o = head + nv * 12 + ni * 4;
  if (v2) {
    const g = new Int32Array(buf.slice(o, o + ng * 16)); o += ng * 16;
    for (let i = 0; i < ng; i++) groups.push({ start: g[i * 4], count: g[i * 4 + 1], layer: g[i * 4 + 2], flags: g[i * 4 + 3] });
    for (let l = 0; l < 32 && o + 4 <= buf.byteLength; l++) { const len = dv.getInt32(o, true); o += 4; layers.push(new TextDecoder().decode(new Uint8Array(buf, o, len)) || 'Layer ' + l); o += len; }
  }
  if (!groups.length) groups = [{ start: 0, count: all.length, layer: -1, flags: 1 }];
  // Maps are wrapped in invisible boxes many kilometres wide. Drawn, they would hide everything else.
  const bad = new Uint8Array(Math.ceil(all.length / 3)); let good = 0;
  for (let t = 0; t + 2 < all.length; t += 3) {
    const a = all[t] * 3, b = all[t + 1] * 3, c = all[t + 2] * 3;
    const far = (p, q) => Math.abs(pos[p] - pos[q]) > 1000 || Math.abs(pos[p + 1] - pos[q + 1]) > 1000 || Math.abs(pos[p + 2] - pos[q + 2]) > 1000;
    if (a >= pos.length || b >= pos.length || c >= pos.length || far(a, b) || far(b, c) || far(c, a)) bad[t / 3] = 1; else good++;
  }
  state.geo = { positions: pos, all, bad, groups, layers, name };
  view3d.dropGeo(); syncLayers();
  return good;
}
// Which layers of the 3D map are switched off (remembered between visits).
const hidden = { layers: new Set(['AI_VisionBlock']), unmodelled: false };        // only the AI "sees" those shapes; the game never draws them
const GHOST = new Set(['PlayerBlock']);                                            // invisible walls players cannot pass: drawn see-through
try { const h = JSON.parse(localStorage.getItem('missionlab-hidden-layers') || 'null'); if (h) { hidden.layers = new Set(h.layers || []); hidden.unmodelled = !!h.unmodelled; } } catch (e) { /* no storage */ }
const layerName = g => g.layer < 0 ? 'Everything' : (state.geo.layers[g.layer] || 'Layer ' + g.layer);
function geoIndices(ghost) {
  const geo = state.geo, out = new Uint32Array(geo.all.length); let n = 0;
  for (const g of geo.groups) {
    if (hidden.layers.has(layerName(g)) || (hidden.unmodelled && !(g.flags & 1)) || GHOST.has(layerName(g)) !== !!ghost) continue;
    for (let t = g.start; t + 2 < g.start + g.count; t += 3) { if (geo.bad[t / 3 | 0]) continue; out[n++] = geo.all[t]; out[n++] = geo.all[t + 1]; out[n++] = geo.all[t + 2]; }
  }
  return out.slice(0, n);
}
function syncLayers() {
  const geo = state.geo, box = $('layerBox'), has = !!(geo && geo.layers.length);
  $('layersBtn').hidden = !(has && state.mode === '3d'); if (!has || state.mode !== '3d') box.hidden = true;
  if (!has) return;
  const sums = new Map(); let bare = 0;
  for (const g of geo.groups) { const k = layerName(g); sums.set(k, (sums.get(k) || 0) + g.count / 3); if (!(g.flags & 1)) bare += g.count / 3; }
  box.textContent = '';
  const row = (text, n, on, change) => {
    const l = document.createElement('label'), c = document.createElement('input'), t = document.createElement('span'), k = document.createElement('span');
    c.type = 'checkbox'; c.checked = on; c.addEventListener('change', () => { change(c.checked); try { localStorage.setItem('missionlab-hidden-layers', JSON.stringify({ layers: [...hidden.layers], unmodelled: hidden.unmodelled })); } catch (e) { /* no storage */ } view3d.dropGeo(); });
    t.textContent = text; k.className = 'n'; k.textContent = Math.round(n).toLocaleString('en'); l.append(c, t, k); box.appendChild(l);
  };
  const title = document.createElement('b'); title.textContent = 'Show in 3D (triangles)'; box.appendChild(title);
  [...sums.entries()].sort((p, q) => q[1] - p[1]).forEach(([name, n]) => row(name === 'PlayerBlock' ? 'PlayerBlock (invisible walls, shown see-through red)' : name === 'AI_VisionBlock' ? 'AI_VisionBlock (blocks only what enemies see)' : name, n, !hidden.layers.has(name), on => { if (on) hidden.layers.delete(name); else hidden.layers.add(name); }));
  row('Shapes without a visible model (often invisible walls)', bare, !hidden.unmodelled, on => { hidden.unmodelled = !on; });
}
function scenarioText() {
  const o = scenarioObject(), head = Object.assign({}, o); delete head.spawns;
  return JSON.stringify(head, null, 1).replace(/\n}$/, ',\n "spawns": [\n' + o.spawns.map(s => '  ' + JSON.stringify(s)).join(',\n') + '\n ]\n}\n');
}
const isVec = p => Array.isArray(p) && p.length >= 3 && p.slice(0, 3).every(v => typeof v === 'number' && isFinite(v));
// The scenario file's contract, missionlab-plan/1, as the plugin reads it (ScenarioFile.cs holds the same rules, and
// tests/fixtures/invalid holds files both must judge alike). Returns findings: level, a short fixed code, the place in the
// file and a sentence. Nothing is quietly dropped or bent into shape: an error means the file is not loaded.
const PLAN_VERSION = 1, PLAN_KINDS = ['zone', 'hold', 'alert', 'intel', 'time', 'after', 'down', 'all', 'any', 'door'];
const SPAWN_FIELDS = ['n:yaw', 'n:detectionRange', 'n:hearingRange', 'n:fov', 'n:wanderDistance', 'n:patrolWaitSeconds', 'n:chance', 'i:group', 'i:trigger', 'i:lot', 'i:either', 'i:option', 'i:team', 'i:minPlayers',
  'b:indoor', 'b:defensive', 'b:fixed', 'b:prone', 'b:hvt', 'b:vehicle', 'b:nvg', 's:role', 's:type', 's:skill', 's:tag'];
const TRIGGER_FIELDS = ['n:radius', 'n:delay', 'n:hold', 'n:chance', 'n:repeat', 'n:rally', 'i:count', 'i:times', 'i:after', 'b:facePlayer', 'b:warn', 'b:everyone', 'b:red', 'b:hunt', 'b:respawn',
  's:name', 's:when', 's:item', 's:message', 's:power', 's:end', 's:extraction', 's:tag'];
const THING_FIELDS = ['s:name', 'n:yaw', 'n:chance', 'i:trigger'], DOOR_FIELDS = ['s:name', 'b:locked', 'b:enemiesCanOpen', 'b:open', 'n:chance'];
function scenarioProblems(obj) {
  const found = [], bad = (code, path, text) => found.push({ level: 'error', code, path, text }), note = (code, path, text) => found.push({ level: 'note', code, path, text });
  const isObj = v => v !== null && typeof v === 'object' && !Array.isArray(v), has = (o, k) => o[k] !== undefined && o[k] !== null;
  const whole = v => typeof v === 'number' && Number.isInteger(v), spot = v => Array.isArray(v) && v.length >= 3 && v.slice(0, 3).every(n => typeof n === 'number');
  const join = (at, key) => at === '' ? key : at + '.' + key;
  const types = (o, at, fields) => { for (const f of fields) { const key = f.slice(2); if (!has(o, key)) continue; const v = o[key];
    const ok = f[0] === 'n' ? typeof v === 'number' : f[0] === 'i' ? whole(v) : f[0] === 'b' ? typeof v === 'boolean' : typeof v === 'string';
    if (!ok) bad('wrong-type', join(at, key), '"' + key + '" must be ' + (f[0] === 'n' ? 'a number' : f[0] === 'i' ? 'a whole number' : f[0] === 'b' ? 'true or false' : 'text')); } };
  const value = (o, at, key, allowed) => { if (has(o, key) && typeof o[key] === 'string' && !allowed.includes(o[key])) bad('unknown-value', join(at, key), '"' + o[key] + '" is not a value this version knows for "' + key + '"'); };
  const doorsOf = (list, at) => { if (!Array.isArray(list)) return bad('wrong-type', at, '"doors" is a list');
    list.forEach((d, i) => { const one = at + '[' + i + ']'; if (!isObj(d)) return bad('wrong-type', one, 'a door is an object');
      if (!(has(d, 'pos') && spot(d.pos))) bad('missing-field', one + '.pos', 'a door needs "pos": [x, y, z], the place of its hinge'); types(d, one, DOOR_FIELDS); }); };
  let root = obj;
  if (isObj(root) && isObj(root.plan)) root = root.plan;
  if (!isObj(root)) { bad('wrong-type', '', 'a scenario is a JSON object'); return found; }
  if (!has(root, 'schema')) note('no-version', 'schema', 'the file names no format; it is read as missionlab-plan/' + PLAN_VERSION);
  else {
    const sv = typeof root.schema === 'string' ? root.schema : '', m = /^missionlab-plan\/(\d+)$/.exec(sv), v = m ? +m[1] : 0;
    if (v <= 0) { bad('unknown-format', 'schema', '"' + sv + '" is not a MissionLab scenario format'); return found; }
    if (v > PLAN_VERSION) { bad('future-version', 'schema', 'this file is missionlab-plan/' + v + '; this version of MissionLab reads missionlab-plan/' + PLAN_VERSION + '. Update MissionLab to use it. The file was not changed'); return found; }
  }
  const ids = new Set(), link = (o, at) => { if (has(o, 'trigger') && whole(o.trigger) && o.trigger !== 0 && !ids.has(o.trigger)) bad('missing-ref', at + '.trigger', 'there is no trigger ' + o.trigger); };
  if (has(root, 'triggers')) {
    if (!Array.isArray(root.triggers)) bad('wrong-type', 'triggers', '"triggers" is a list');
    else {
      for (const t of root.triggers) if (isObj(t) && has(t, 'id') && whole(t.id)) ids.add(t.id);
      const seen = new Set();
      root.triggers.forEach((t, i) => {
        const at = 'triggers[' + i + ']';
        if (!isObj(t)) return bad('wrong-type', at, 'a trigger is an object');
        let me = 0;
        if (!has(t, 'id') || !whole(t.id) || t.id < 1) bad('bad-id', at + '.id', 'a trigger needs a whole number from 1 as its "id"');
        else { me = t.id; if (seen.has(me)) bad('duplicate-id', at + '.id', 'trigger number ' + me + ' is used twice'); seen.add(me); }
        types(t, at, TRIGGER_FIELDS);
        const when = has(t, 'when') && typeof t.when === 'string' ? t.when : 'zone';
        if (!PLAN_KINDS.includes(when)) return bad('unknown-kind', at + '.when', '"' + when + '" is not a kind of trigger this version knows');
        if ((when === 'zone' || when === 'hold' || when === 'door') && !(has(t, 'pos') && spot(t.pos))) bad('missing-field', at + '.pos', 'a ' + when + ' trigger needs "pos": [x, y, z]');
        if (when === 'after' && has(t, 'after') && whole(t.after)) {
          if (t.after !== 0 && t.after === me) bad('self-ref', at + '.after', 'the trigger waits for itself');
          else if (t.after !== 0 && !ids.has(t.after)) bad('missing-ref', at + '.after', 'there is no trigger ' + t.after);
        }
        if ((when === 'all' || when === 'any') && has(t, 'of')) {
          if (!Array.isArray(t.of)) bad('wrong-type', at + '.of', '"of" is a list of trigger numbers');
          else t.of.forEach((one, j) => { const op = at + '.of[' + j + ']'; if (!whole(one)) bad('wrong-type', op, 'a trigger number is a whole number'); else if (!ids.has(one)) bad('missing-ref', op, 'there is no trigger ' + one); });
        }
        value(t, at, 'power', ['', 'on', 'off']); value(t, at, 'end', ['', 'success', 'failed']); value(t, at, 'extraction', ['', 'open']);
        if (has(t, 'doors')) doorsOf(t.doors, at + '.doors');
      });
    }
  }
  if (!has(root, 'spawns') || !Array.isArray(root.spawns)) bad('wrong-type', 'spawns', 'a scenario needs "spawns": a list of enemies');
  else root.spawns.forEach((e, i) => {
    const at = 'spawns[' + i + ']';
    if (!isObj(e)) return bad('wrong-type', at, 'an enemy is an object');
    if (!(has(e, 'pos') && spot(e.pos))) bad('missing-field', at + '.pos', 'an enemy needs "pos": [x, y, z]');
    types(e, at, SPAWN_FIELDS); value(e, at, 'skill', ['', 'recruit', 'veteran']); link(e, at);
  });
  if (has(root, 'extraction')) {
    if (!isObj(root.extraction)) bad('wrong-type', 'extraction', '"extraction" is an object');
    else if (has(root.extraction, 'after')) { const a = root.extraction.after; if (!whole(a)) bad('wrong-type', 'extraction.after', 'a trigger number is a whole number'); else if (a !== 0 && !ids.has(a)) bad('missing-ref', 'extraction.after', 'there is no trigger ' + a); }
  }
  if (has(root, 'things')) {
    if (!Array.isArray(root.things)) bad('wrong-type', 'things', '"things" is a list');
    else root.things.forEach((th, i) => { const at = 'things[' + i + ']'; if (!isObj(th)) return bad('wrong-type', at, 'a thing is an object');
      if (!(has(th, 'pos') && spot(th.pos))) bad('missing-field', at + '.pos', 'a thing needs "pos": [x, y, z]'); types(th, at, THING_FIELDS); link(th, at); });
  }
  if (has(root, 'doors')) doorsOf(root.doors, 'doors');
  if (has(root, 'respawn')) { if (!isObj(root.respawn)) bad('wrong-type', 'respawn', '"respawn" is an object: {"after": seconds, "times": waves}'); else types(root.respawn, 'respawn', ['n:after', 'i:times']); }
  types(root, '', ['s:name', 's:scene', 's:navFile', 's:insertion', 's:power', 's:skill', 'b:nvg', 'b:gameMaster', 'i:seed']);
  value(root, '', 'power', ['', 'on', 'off']); value(root, '', 'skill', ['', 'recruit', 'veteran']);
  return found;
}
// Accepts a scenario, or a mission record written by the Director (the plan sits under "plan").
function loadScenario(obj, name) {
  // Read strictly. A file with something wrong in it is not loaded: what is on the page stays, and the file itself is never touched.
  const found = scenarioProblems(obj), wrong = found.filter(f => f.level === 'error');
  if (wrong.length) {
    state.refused = { name, errors: wrong };
    return name + ' was not loaded: ' + wrong.length + (wrong.length === 1 ? ' problem' : ' problems') + '. First: ' + (wrong[0].path ? wrong[0].path + ': ' : '') + wrong[0].text + '. The file was not changed; the Mission tab lists them all.';
  }
  state.refused = null; state.readNotes = found.filter(f => f.level === 'note').map(f => name + ': ' + f.text + '. Saving writes the current format; Undo brings back what was here before.');
  const record = obj && obj.plan && Array.isArray(obj.plan.spawns);
  const root = record ? obj.plan : obj;
  const list = [];
  root.spawns.forEach((s, i) => list.push(enemyOf(s, i)));
  pushUndo();
  state.zones = zonesIn(root.triggers); state.objectives = objectivesIn(root.objectives); setDoorWishes(root.doors); settingsIn(root);
  state.things = thingsIn(root.things); state.thing = -1;
  state.enemies = list; state.selected = list.length ? 0 : -1;
  state.title = (typeof root.name === 'string' && root.name.trim()) || name.replace(/\.json$/i, '');
  // A generated mission names one scene (day or night). Matching by map file covers both.
  state.scene = record ? '' : (typeof root.scene === 'string' ? root.scene : '');
  const nav = typeof root.navFile === 'string' ? root.navFile : '';
  if (nav) state.navFile = nav;
  if (state.map && state.map.isExample) { state.map = null; state.infils = []; state.authored = []; state.doors = state.doors.filter(d => !d.known); state.door = -1; }
  state.example = false;
  return list.length + ' enemies loaded from ' + name + '.';
}
// A recon file from the MissionLab Recon plugin: insertion points and the game's own spawn points.
function loadRecon(obj, name) {
  const pts = [], add = p => { if (isVec(p) && Math.abs(p[0]) + Math.abs(p[2]) > 0.01 && !pts.some(q => Math.hypot(q[0] - p[0], q[2] - p[2]) < 1)) pts.push(p.slice(0, 3)); };
  const walk = node => { if (!node || typeof node !== 'object') return; if (node.pos) add(node.pos); (node.children || []).forEach(walk); };
  ((obj.infiltration || {}).infilMethods || []).forEach(m => { if (!m) return; walk(m.infilObject); (m.children || []).forEach(walk); });
  const spots = ((obj.infiltration || {}).infilMethods || []).filter(m => m && typeof m.infilName === 'string' && m.infilObject && isVec(m.infilObject.pos)).map(m => ({ name: m.infilName, pos: m.infilObject.pos.slice(0, 3) }));
  if (spots.length) state.insertionSpots = spots;
  const sceneName = (obj.operations && obj.operations.currentScene) || obj.scene || '';
  if (typeof sceneName === 'string' && sceneName && Array.isArray(obj.doors)) state.night = /night/i.test(sceneName);
  state.infils = pts;
  const spawns = (obj.raid || {}).botSpawnPoints;
  state.authored = (Array.isArray(spawns) ? spawns : []).filter(s => s && isVec(s.pos)).map(s => s.pos.slice(0, 3));
  const doors = doorsIn(obj.doors);
  if (Array.isArray(obj.doors)) setKnownDoors(doors);
  // The game's enemy types, so one can be chosen without the game running (the suicide bomber, for one).
  const ways = (((obj.infiltration || {}).infilMethods) || []).map(m => m && m.infilName).filter(n => typeof n === 'string' && n);
  if (ways.length && !live.on) state.insertions = ways;
  const kinds = ((obj.gameManager || {}).allAITypes || []).filter(t => typeof t === 'string' && t);
  if (kinds.length && !live.on) live.types = kinds;
  const trucks = ((obj.raid || {}).staticVehicleAI || []).filter(t => typeof t === 'string' && t);
  if (trucks.length && !live.on) live.vehicleTypes = trucks;
  return pts.length + ' insertion points, ' + (doors.length ? doors.length + ' doors' : 'no door list') + ' and ' + state.authored.length + ' of the game\'s own spawn points loaded from ' + name + (obj.scene ? ' (' + obj.scene + ').' : '.');
}
async function openFiles(files) {
  const notes = [];
  for (const f of files) {
    try {
      const buf = await f.arrayBuffer(), head = new Uint8Array(buf, 0, Math.min(2, buf.byteLength));
      if (buf.byteLength > 16 && /^MLGEO[12]$/.test(new TextDecoder().decode(new Uint8Array(buf, 0, 6)))) {
        const n = loadGeo(buf, f.name);
        notes.push(f.name + ': ' + Math.round(n).toLocaleString('en') + ' triangles of walls, floors and furniture for the 3D view.');
        if (state.mode !== '3d') setMode('3d');
      } else if (head[0] === 0x50 && head[1] === 0x4b) {
        const tris = await readMapFile(buf);
        const wasExample = state.example;
        state.map = makeMap(tris, f.name, false); state.navFile = f.name; state.example = false;
        if (wasExample) { state.enemies = []; state.selected = -1; state.infils = []; state.authored = []; state.doors = state.doors.filter(d => !d.known); state.door = -1; undoStack.length = 0; }
        state.ceiling = Infinity; view3d.dropMap(); fit();
        notes.push(f.name + ': ' + state.map.n.toLocaleString('en') + ' walkable triangles.');
      } else {
        const obj = JSON.parse(new TextDecoder().decode(buf));
        if (obj && obj.schema === 'missionlab-replay/1') {
          // A recording carries the plan it was played with; offline that is shown too, so zones and doors are on the map.
          if (!live.on && obj.plan && Array.isArray(obj.plan.spawns) && obj.plan.spawns.length) { loadScenario(obj.plan, f.name); if (!state.map) fit(); }
          notes.push(loadReplay(obj, f.name));
        }
        else if (obj && (Array.isArray(obj.spawns) || (obj.plan && Array.isArray(obj.plan.spawns)))) { notes.push(loadScenario(obj, f.name)); if (!state.map) fit(); }
        else if (obj && (obj.infiltration || obj.raid)) notes.push(loadRecon(obj, f.name));
        else throw new Error('It is not a scenario, a mission record, a replay or a recon file.');
      }
    } catch (err) { notes.push(f.name + ' could not be opened. ' + (err && err.message ? err.message : err)); }
  }
  say(notes.join(' '));
  changed();
}

