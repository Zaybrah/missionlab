// ------------------------------------------------------------------ encounter modules (ML-019)
// A module is a whole encounter kept for reuse: enemies, their patrol routes, the triggers that bring them or that those
// wait for, and the triggers' messages. Everything is measured from an anchor, so a module can be put down anywhere and
// turned, on this map or another. The file is missionlab-module/1 (docs/modules.md):
//   { schema, name, anchor: { pos, yaw }, spawns: [...], triggers: [...], bindings: [{ kind, ref, label, required, hint, trigger }] }
// spawns and triggers are written as a scenario writes them, with positions relative to the anchor. What the encounter needs
// from outside itself (a door by its hinge, an intel item, a named place, another trigger, the extraction) is written as
// "@ref" where it is used and listed under bindings. A copy put into a scenario must have each required one bound to
// something on this map before the scenario can be saved: a guessed door or trigger would quietly do the wrong thing.
// Inserting makes an independent copy with fresh trigger ids and fresh group, lot and either numbers; nothing links back.
// Until a reference is bound, the enemy or trigger carries a mark ({ binds } on it, .bind on a trigger's door): the marks
// live in the editor only, travel with Undo, and are never written to a file.
const MODULE_SCHEMA = 'missionlab-module/1', MODULE_KINDS = ['door', 'objective', 'place', 'trigger', 'extraction'], MODULE_STORE = 'missionlab-modules';
const moduleRef = v => (typeof v === 'string' && v.length > 1 && v[0] === '@' ? v.slice(1) : '');
const moduleRequired = b => (typeof b.required === 'boolean' ? b.required : b.kind !== 'extraction');
const moduleWhere = p => p[0].toFixed(1) + ', ' + p[2].toFixed(1) + ' (height ' + p[1].toFixed(1) + ')';
const moduleServed = () => /^https?:$/.test(location.protocol) && /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
const moduleFileName = name => (String(name || '').toLowerCase().replace(/[^a-z0-9_-]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 60) || 'module') + '.module.json';
// Turning by deg is clockwise on the map, as facing is: yaw 0 looks along +z, 90 along +x (turnSquad does the same).
function moduleTurn(deg) { const a = deg * Math.PI / 180, c = Math.cos(a), s = Math.sin(a); return (x, z) => [x * c + z * s, z * c - x * s]; }

// What is wrong with a module, as plain sentences; empty when it can be inserted. The shape first (the plugin's
// Modules.Check has the same rules), then the scenario rules on its triggers and enemies with the outside references stood in.
function moduleProblems(obj) {
  const out = [], bad = t => out.push(t), isObj = v => v !== null && typeof v === 'object' && !Array.isArray(v);
  if (!isObj(obj)) return ['a module is a JSON object'];
  const m = /^missionlab-module\/(\d+)$/.exec(typeof obj.schema === 'string' ? obj.schema : '');
  if (!m) return ['its "schema" is not ' + MODULE_SCHEMA];
  if (+m[1] > 1) return ['it is ' + obj.schema + ', made by a newer MissionLab'];
  if (typeof obj.name !== 'string' || !obj.name.trim()) bad('it needs a "name"');
  if (!isObj(obj.anchor) || !isVec(obj.anchor.pos) || typeof obj.anchor.yaw !== 'number') bad('"anchor" is {"pos": [x, y, z], "yaw": degrees}');
  if (!Array.isArray(obj.spawns) || !Array.isArray(obj.triggers) || !Array.isArray(obj.bindings)) bad('"spawns", "triggers" and "bindings" are lists');
  if (out.length) return out;
  if (!obj.spawns.length && !obj.triggers.length) return ['it is empty: no enemies and no triggers'];
  const ids = new Set(obj.triggers.filter(t => isObj(t) && Number.isInteger(t.id)).map(t => t.id)), refs = new Set();
  obj.bindings.forEach((b, i) => {
    const at = 'bindings[' + i + ']';
    if (!isObj(b)) return bad(at + ' is not an object');
    if (!MODULE_KINDS.includes(b.kind)) bad(at + ': "kind" is one of ' + MODULE_KINDS.join(', '));
    if (typeof b.ref !== 'string' || !/^[A-Za-z0-9_-]{1,32}$/.test(b.ref)) bad(at + ': "ref" is 1 to 32 letters, digits, "_" or "-"');
    else if (refs.has(b.ref)) bad(at + ': "' + b.ref + '" is listed twice');
    else refs.add(b.ref);
    if (b.required !== undefined && typeof b.required !== 'boolean') bad(at + ': "required" is true or false');
    if (b.kind === 'extraction' && !ids.has(b.trigger)) bad(at + ': an extraction binding names one of the module\'s triggers in "trigger"');
  });
  obj.spawns.forEach((s, i) => { if (isObj(s) && typeof s.trigger === 'number' && s.trigger !== 0 && !ids.has(s.trigger)) bad('spawns[' + i + ']: "trigger" names no trigger of the module'); });
  // Every outside reference names a listed binding: an unknown one could never be bound.
  const walk = (v, at) => {
    if (moduleRef(v)) { if (!refs.has(moduleRef(v))) bad(at + ': "' + v + '" names no binding'); }
    else if (Array.isArray(v)) v.forEach((x, i) => walk(x, at + '[' + i + ']'));
    else if (isObj(v)) for (const k of Object.keys(v)) walk(v[k], at + '.' + k);
  };
  walk(obj.spawns, 'spawns'); walk(obj.triggers, 'triggers');
  for (const f of scenarioProblems(moduleStandIn(obj))) if (f.level === 'error') bad((f.path ? f.path + ': ' : '') + f.text);
  return out;
}
// The module as a plan/2 scenario body, every outside reference replaced by a harmless value, for the scenario rules.
function moduleStandIn(obj) {
  const c = JSON.parse(JSON.stringify({ spawns: obj.spawns, triggers: obj.triggers }));
  for (const s of c.spawns) if (s && moduleRef(s.trigger)) s.trigger = 0;
  for (const t of c.triggers) {
    if (!t || typeof t !== 'object') continue;
    if (moduleRef(t.pos)) t.pos = [0, 0, 0];
    if (moduleRef(t.after)) t.after = 0;
    if (Array.isArray(t.of)) t.of = t.of.filter(v => !moduleRef(v));
    if (moduleRef(t.item)) t.item = '';
    if (Array.isArray(t.doors)) for (const d of t.doors) if (d && moduleRef(d.pos)) d.pos = [0, 0, 0];
    delete t.place;
  }
  return { schema: 'missionlab-plan/2', mission: c };
}

// ----- saving a selection as a module
// The selected enemies, the triggers that bring them, and what those wait for: a trigger they wait for comes along when it
// brings no enemy outside the selection; otherwise it belongs to the rest of the mission and becomes a binding.
function moduleOfSelection(name) {
  const list = picked();
  if (!list.length) return { error: 'Select the enemies of the encounter first: click one, Shift-click the others.' };
  const title = String(name || '').replace(/\s+/g, ' ').trim().slice(0, 60);
  if (!title) return { error: 'Give the module a name first.' };
  const lead = state.enemies[list.includes(state.selected) ? state.selected : list[0]], A = lead.pos.slice(0, 3);
  const rel = p => [r2(p[0] - A[0]), r2(p[1] - A[1]), r2(p[2] - A[2])];
  const relRing = r => r.map(p => [p[0] - A[0], p[1] - A[2]]);
  const relArea = area => areaWrite({ parts: area.parts.map(part => ({ floor: [part.floor[0] - A[1], part.floor[1] - A[1]], outline: relRing(part.outline), holes: (part.holes || []).map(relRing) })) });
  const chosen = new Set(list), byId = id => state.zones.find(z => z.id === id);
  const bringsOnly = z => state.enemies.every((e, i) => e.trigger !== z.id || chosen.has(i));
  const needs = z => (z.when === 'after' ? [z.after] : z.when === 'all' || z.when === 'any' ? z.of || [] : []).filter(id => id && id !== z.id && byId(id));
  const inside = new Set(list.map(i => state.enemies[i].trigger).filter(id => id && byId(id))), outside = new Set();
  const follow = z => { for (const id of needs(z)) { if (inside.has(id) || outside.has(id)) continue; if (bringsOnly(byId(id))) { inside.add(id); follow(byId(id)); } else outside.add(id); } };
  [...inside].forEach(id => follow(byId(id)));
  const order = [...inside].sort((a, b) => a - b), modId = new Map(order.map((id, k) => [id, k + 1]));

  const bindings = [], seen = new Map();
  const bind = (what, kind, prefix, label, more) => {
    if (!seen.has(what)) { const ref = prefix + (bindings.filter(b => b.kind === kind).length + 1); seen.set(what, ref); bindings.push(Object.assign({ kind, ref, label }, more || {})); }
    return '@' + seen.get(what);
  };
  const trigRef = id => { const z = byId(id); return bind('t' + id, 'trigger', 't', trigText(z) + (z.name ? ': ' + z.name : '')); };
  const doorRef = (pos, name) => {
    const n = nearDoor(pos, state.doors), d = n >= 0 ? state.doors[n] : null, hinge = d ? d.pos : pos, label = d && d.name ? d.name : name || '';
    return bind(d ? 'd' + n : 'd' + r3(pos).join(','), 'door', 'd', 'Door ' + (label ? '"' + label + '" ' : '') + Math.round(Math.hypot(hinge[0] - A[0], hinge[2] - A[2])) + ' m from the anchor', { hint: rel(hinge) });
  };
  const samePlace = (p, z) => (p.area && z.area ? areaSignature(p.area) === areaSignature(z.area)
    : !p.area && !z.area && isVec(z.pos) && Math.hypot(p.pos[0] - z.pos[0], p.pos[2] - z.pos[2]) < 0.5 && Math.abs(p.pos[1] - z.pos[1]) <= 2);
  const written = zonesOut();
  const triggers = order.map(id => {
    const z = byId(id), o = JSON.parse(JSON.stringify(written.find(t => t.id === id)));
    o.id = modId.get(id);
    if (o.when === 'door') { if (isVec(o.pos)) o.pos = doorRef(o.pos, ''); }
    else if (isVec(o.pos)) o.pos = rel(o.pos);
    if (o.area && z.area) o.area = relArea(z.area);
    if (o.after) o.after = modId.get(o.after) || (outside.has(o.after) ? trigRef(o.after) : 0);
    if (Array.isArray(o.of)) o.of = o.of.map(v => modId.get(v) || trigRef(v));
    if (o.when === 'intel' && o.item && o.item !== 'all') o.item = bind('o' + o.item, 'objective', 'o', 'Intel item "' + o.item + '"', { hint: o.item });
    if (o.doors) for (const d of o.doors) d.pos = doorRef(d.pos, d.name);
    if (o.when === 'zone' || o.when === 'hold') { const p = state.places.find(q => samePlace(q, z)); if (p) o.place = bind('p' + p.id, 'place', 'p', 'Place "' + p.name + '"', { hint: p.name }); }
    return o;
  });
  if (modId.has(state.extractionAfter)) bindings.push({ kind: 'extraction', ref: 'x1', label: 'The extraction waits for trigger ' + modId.get(state.extractionAfter) + ' of this module', required: false, trigger: modId.get(state.extractionAfter) });
  const spawns = list.map(i => { const s = spawnOf(state.enemies[i]); s.pos = rel(s.pos); s.patrol = s.patrol.map(rel); s.trigger = modId.get(s.trigger) || 0; return s; });
  const module = { schema: MODULE_SCHEMA, name: title, anchor: { pos: r3(A), yaw: 0 }, spawns, triggers, bindings };
  const wrong = moduleProblems(module);
  return wrong.length ? { error: 'The selection cannot be kept as a module: ' + wrong[0] + '.' } : { module };
}
// One enemy per line, as scenario files have them.
function moduleText(mod) {
  const o = Object.assign({}, mod), mark = 'MISSIONLAB_MODULE_SPAWNS';
  o.spawns = mark;
  const list = mod.spawns.length ? '[\n' + mod.spawns.map(s => '  ' + JSON.stringify(s)).join(',\n') + '\n ]' : '[]';
  return JSON.stringify(o, null, 1).replace('"' + mark + '"', () => list) + '\n';
}

// ----- inserting a copy
// at: the anchor's place on this map; deg: how far the copy is turned. Returns { copy, triggers, enemies, off, open }, or { error }.
function moduleInsert(mod, at, deg) {
  const wrong = moduleProblems(mod);
  if (wrong.length) return { error: 'The module was not inserted: ' + wrong[0] + (wrong.length > 1 ? ' (and ' + (wrong.length - 1) + ' more)' : '') + '.' };
  if (!isVec(at)) return { error: 'Choose where the module goes first.' };
  if (live.on && mod.bindings.some(moduleRequired)) return { error: 'This module has outside references to bind, and edits now go straight into the running mission. Insert it while editing offline, bind it, then load the scenario.' };
  deg = ((num(+deg, 0) % 360) + 360) % 360;
  pushUndo();
  const copy = state.moduleCopies = (state.moduleCopies || 0) + 1, key = ref => 'm' + copy + ':' + ref;
  const turn = moduleTurn(deg), put = p => { const q = turn(p[0], p[2]); return [r2(at[0] + q[0]), r2(at[1] + p[1]), r2(at[2] + q[1])]; };
  const ring = r => r.map(p => { const q = turn(p[0], p[1]); return [r2(at[0] + q[0]), r2(at[2] + q[1])]; });
  const shape = area => ({ parts: area.parts.map(part => ({ floor: [r2(part.floor[0] + at[1]), r2(part.floor[1] + at[1])], outline: ring(part.outline), holes: (part.holes || []).map(ring) })) });
  const binds = new Map(mod.bindings.map(b => [b.ref, b])), hintAt = ref => { const b = binds.get(ref); return b && isVec(b.hint) ? put(b.hint) : at.slice(0, 3); };
  // Fresh numbers, above every one in use: trigger ids, groups, lots and either-sets. Two copies never share one.
  let top = state.zones.reduce((m, z) => Math.max(m, z.id), 0);
  const ids = new Map(mod.triggers.map(t => [t.id, ++top]));
  const renumber = (field, from) => {
    const used = mod.spawns.map(s => num(s[field], 0) | 0).filter(v => v >= from);
    if (!used.length) return v => num(v, 0) | 0;
    const low = Math.min(...used), free = state.enemies.reduce((m, e) => Math.max(m, (e[field] | 0) + 1), from);
    return v => { v = num(v, 0) | 0; return v >= from ? v - low + free : v; };
  };
  const group = renumber('group', 0), lot = renumber('lot', 1), either = renumber('either', 1);
  // A tag the module's own "enemies down" triggers count is made unique, or a second copy would count the first copy's enemies.
  const usedTags = new Set([...state.enemies.map(e => e.tag), ...state.zones.map(z => z.tag)].filter(Boolean)), tags = new Map();
  for (const t of mod.triggers) {
    const tag = t.when === 'down' ? cleanTag(t.tag) : '';
    if (!tag || tags.has(tag) || !usedTags.has(tag)) continue;
    let n = 2, name; do { name = cleanTag(tag.slice(0, 20) + ' ' + n++); } while (usedTags.has(name));
    usedTags.add(name); tags.set(tag, name);
  }
  const retag = v => (v && tags.has(cleanTag(v)) ? tags.get(cleanTag(v)) : v);

  const marks = new Map(), doorMarks = new Map();
  const raw = mod.triggers.map(t => {
    const o = JSON.parse(JSON.stringify(t)), mark = {}, doorKeys = [];
    o.id = ids.get(t.id);
    if (moduleRef(o.pos)) { mark.pos = key(moduleRef(o.pos)); o.pos = hintAt(moduleRef(o.pos)); } else if (isVec(o.pos)) o.pos = put(o.pos);
    if (o.area) { const a = areaRead(o.area); if (a) o.area = areaWrite(shape(a)); else delete o.area; }
    if (o.after !== undefined) { if (moduleRef(o.after)) { mark.after = key(moduleRef(o.after)); o.after = 0; } else o.after = ids.get(o.after) || 0; }
    if (Array.isArray(o.of)) { const of = []; for (const v of o.of) { if (moduleRef(v)) (mark.of = mark.of || []).push(key(moduleRef(v))); else if (ids.has(v)) of.push(ids.get(v)); } o.of = of; }
    if (moduleRef(o.item)) { const b = binds.get(moduleRef(o.item)); mark.item = key(moduleRef(o.item)); o.item = b && typeof b.hint === 'string' ? b.hint : ''; }
    if (moduleRef(o.place)) mark.place = key(moduleRef(o.place));
    delete o.place;
    if (o.tag) o.tag = retag(o.tag);
    if (Array.isArray(o.doors)) {
      // The same filter zonesIn uses, so the marks below line up with the doors it keeps.
      o.doors = o.doors.filter(d => d && (isVec(d.pos) || moduleRef(d.pos)) && (tri(d.locked) !== null || tri(d.open) !== null));
      for (const d of o.doors) { if (moduleRef(d.pos)) { doorKeys.push(key(moduleRef(d.pos))); d.pos = hintAt(moduleRef(d.pos)); } else { doorKeys.push(''); d.pos = put(d.pos); } }
    }
    for (const b of mod.bindings) if (b.kind === 'extraction' && b.trigger === t.id) mark.extraction = key(b.ref);
    marks.set(o.id, mark); doorMarks.set(o.id, doorKeys);
    return o;
  });
  const zones = zonesIn(raw);
  for (const z of zones) {
    const mark = marks.get(z.id);
    if (mark && Object.keys(mark).length) z.binds = mark;
    (doorMarks.get(z.id) || []).forEach((k, n) => { if (k && z.doors[n]) z.doors[n].bind = k; });
    z.moduleCopy = copy;
  }
  const enemies = mod.spawns.map((s, i) => {
    const o = JSON.parse(JSON.stringify(s));
    let mark = '';
    o.pos = put(o.pos); o.patrol = (Array.isArray(o.patrol) ? o.patrol.filter(isVec) : []).map(put); o.yaw = num(o.yaw, 0) + deg;
    if (moduleRef(o.trigger)) { mark = key(moduleRef(o.trigger)); o.trigger = 0; } else o.trigger = ids.get(o.trigger) || 0;
    o.group = group(o.group); o.lot = lot(o.lot); o.either = either(o.either); if (o.tag) o.tag = retag(o.tag);
    const e = enemyOf(o, i);
    e.yaw = r2(e.yaw) % 360; e.moduleCopy = copy;
    if (mark) e.binds = { trigger: mark };
    return e;
  });
  for (const b of mod.bindings) state.moduleBindings.push({ key: key(b.ref), copy, module: mod.name, kind: b.kind, ref: b.ref, label: typeof b.label === 'string' && b.label.trim() ? b.label.trim() : b.kind + ' ' + b.ref,
    required: moduleRequired(b), hint: b.kind === 'door' ? (isVec(b.hint) ? put(b.hint) : null) : typeof b.hint === 'string' ? b.hint : '' });

  const first = state.enemies.length;
  state.enemies.push(...enemies); state.zones.push(...zones);
  state.squad = enemies.length > 1 ? enemies.map((_, k) => first + k) : []; state.selected = enemies.length ? first : -1; state.door = -1;
  const off = modulePlacement(copy), open = moduleOpen().filter(b => b.copy === copy && b.required);
  state.moduleReport = { copy, name: mod.name, off, checked: !!state.map };
  changed();
  say('"' + mod.name + '" is in as copy ' + copy + ': ' + enemies.length + (enemies.length === 1 ? ' enemy, ' : ' enemies, ') + zones.length + (zones.length === 1 ? ' trigger' : ' triggers') + (zones.length ? ' (number ' + zones.map(z => z.id).join(', ') + ')' : '') + '.'
    + (open.length ? ' ' + open.length + (open.length === 1 ? ' outside reference needs' : ' outside references need') + ' binding before the scenario can be saved: see Modules on the Triggers tab.' : '')
    + ' ' + modulePlacementWords(state.moduleReport));
  return { copy, triggers: zones.map(z => z.id), enemies: enemies.map((_, k) => first + k), off, open };
}

// What of a copy is off the walkable ground of the open map (the same 1 m rule as the Mission check). On another map than the
// module was made on, this and the bindings are what decide whether it fits.
function modulePlacement(copy) {
  const off = [];
  if (!state.map) return off;
  state.enemies.forEach((e, i) => {
    if (e.moduleCopy !== copy || e.fixed || e.vehicle) return;
    if (!onGround(e.pos)) off.push('Enemy ' + (i + 1) + ' at ' + moduleWhere(e.pos));
    if (e.role === 'Patrol') e.patrol.forEach((p, n) => { if (!onGround(p)) off.push('Route stop ' + (n + 1) + ' of enemy ' + (i + 1) + ' at ' + moduleWhere(p)); });
  });
  for (const z of state.zones) if (z.moduleCopy === copy && z.when === 'zone' && isVec(z.pos) && !onGround(z.pos)) off.push(trigTitle(z) + ', its anchor at ' + moduleWhere(z.pos));
  return off;
}
const modulePlacementWords = r => (!r.checked ? 'No map is open, so its placement was not checked: open the map file, then choose Check placement.'
  : r.off.length ? r.off.length + (r.off.length === 1 ? ' position is' : ' positions are') + ' off the walkable ground (listed under Modules); move them, or let the Mission check put them on the ground.'
  : 'Every position is on walkable ground.');

// ----- bindings
// The outside references still to bind: those some enemy or trigger of a copy still waits on. A deleted copy waits on nothing.
function moduleOpen() {
  if (!state.moduleBindings) return [];
  const used = new Set();
  for (const e of state.enemies) if (e.binds && e.binds.trigger) used.add(e.binds.trigger);
  for (const z of state.zones) {
    const m = z.binds;
    if (m) { for (const k of ['pos', 'after', 'item', 'place', 'extraction']) if (m[k]) used.add(m[k]); for (const k of m.of || []) used.add(k); }
    for (const d of z.doors || []) if (d.bind) used.add(d.bind);
  }
  return state.moduleBindings.filter(b => used.has(b.key));
}
// Why the scenario cannot be saved yet, or '' when it can.
function moduleBlock() {
  const open = moduleOpen().filter(b => b.required);
  if (!open.length) return '';
  return 'Not saved: ' + open.length + (open.length === 1 ? ' outside reference' : ' outside references') + ' of an inserted module must be bound first (' + open.slice(0, 3).map(b => b.label).join('; ')
    + (open.length > 3 ? '; and more' : '') + '). Bind them under Modules on the Triggers tab.';
}
// value: a door's place in state.doors, an intel item's name ('' for any), a place's id, a trigger's id, or true/false for the extraction.
function moduleBind(key, value) {
  const b = moduleOpen().find(x => x.key === key), fail = text => { say(text); return false; };
  if (!b) return fail('That reference is bound already, or its module copy is gone.');
  let door = null, place = null;
  if (b.kind === 'door') { door = Number.isInteger(value) ? state.doors[value] : null; if (!door) return fail('Choose one of the map\'s doors for ' + b.label + '.'); }
  else if (b.kind === 'place') { place = state.places.find(p => p.id === value); if (!place) return fail('Choose one of the named places for ' + b.label + '.'); }
  else if (b.kind === 'trigger') { const z = state.zones.find(q => q.id === value); if (!z || z.moduleCopy === b.copy) return fail('Choose a trigger of the mission for ' + b.label + ', not one of the copy\'s own.'); }
  else if (b.kind === 'objective') { if (typeof value !== 'string') return fail('Name the intel item for ' + b.label + '.'); value = value.trim(); }
  else value = value === true;
  pushUndo();
  for (const e of state.enemies) if (e.binds && e.binds.trigger === key) { e.trigger = value; delete e.binds; }
  for (const z of state.zones) {
    const m = z.binds || {};
    if (m.pos === key) { z.pos = door.pos.slice(0, 3); delete m.pos; }
    for (const d of z.doors || []) if (d.bind === key) { d.pos = door.pos.slice(0, 3); if (door.name) d.name = door.name; delete d.bind; }
    if (m.after === key) { z.after = value; delete m.after; }
    if (m.of && m.of.includes(key)) { if (!z.of.includes(value)) z.of.push(value); m.of = m.of.filter(k => k !== key); if (!m.of.length) delete m.of; }
    if (m.item === key) { z.item = value; delete m.item; }
    // A zone that is a named place takes the place's shape on this map; a point place moves only the anchor.
    if (m.place === key) { z.pos = place.pos.slice(0, 3); if (place.area) z.area = JSON.parse(JSON.stringify(place.area)); delete m.place; }
    if (m.extraction === key) { if (value) state.extractionAfter = z.id; delete m.extraction; }
    if (z.binds && !Object.keys(z.binds).length) delete z.binds;
  }
  changed();
  const left = moduleBlock();
  say('Bound: ' + b.label + '.' + (left ? '' : ' Nothing is left to bind; the scenario can be saved.'));
  return true;
}

// ----- kept modules: in this browser, in the game's modules folder (when the page comes from the game), or opened from a file
function savedModules() { try { const list = JSON.parse(localStorage.getItem(MODULE_STORE) || '[]'); return Array.isArray(list) ? list.filter(m => m && typeof m.name === 'string' && Array.isArray(m.spawns)) : []; } catch (e) { return []; } }
function storeModules(list) { try { localStorage.setItem(MODULE_STORE, JSON.stringify(list)); return true; } catch (e) { return false; } }
let moduleGame = [], moduleFile = null;
async function moduleLoadGame() {
  try { const got = await (await fetch('/api/modules', { cache: 'no-store' })).json(); moduleGame = Array.isArray(got.modules) ? got.modules.filter(m => !m.problem) : []; }
  catch (e) { moduleGame = []; }
  syncModules();
}
async function moduleChosen() {
  const v = String($('mdlPick').value || ''), kind = v.slice(0, 2), rest = v.slice(2);
  if (kind === 'l:') return savedModules()[+rest] || null;
  if (kind === 'f:') return moduleFile;
  if (kind === 'g:') {
    try { const r = await fetch('/api/modules/' + encodeURIComponent(rest), { cache: 'no-store' }); return r.ok ? await r.json() : null; } catch (e) { return null; }
  }
  return null;
}
const moduleNote = text => { $('mdlMsg').textContent = text; return text; };
async function moduleSaveSelection() {
  const got = moduleOfSelection($('mdlName').value);
  if (got.error) return moduleNote(got.error);
  const mod = got.module, text = moduleText(mod), file = moduleFileName(mod.name);
  const kept = storeModules(savedModules().filter(m => m.name !== mod.name).concat([mod]));
  const what = mod.spawns.length + (mod.spawns.length === 1 ? ' enemy, ' : ' enemies, ') + mod.triggers.length + (mod.triggers.length === 1 ? ' trigger' : ' triggers')
    + (mod.bindings.length ? ' and ' + mod.bindings.length + (mod.bindings.length === 1 ? ' outside reference' : ' outside references') + ' to bind on insert' : '');
  let where = kept ? 'kept in this browser' : 'not kept in this browser (it will not store it)';
  if (moduleServed()) {
    const send = async replace => fetch('/api/modules', { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify({ name: file, text, replace }) });
    try {
      let r = await send(false);
      if (r.status === 409 && confirm('The game already keeps a module named ' + file + '. Replace it?')) r = await send(true);
      const a = await r.json().catch(() => ({}));
      where += r.ok ? ' and in BepInEx/MissionLab/modules as ' + file : '; the game did not keep it (' + (a.error || 'status ' + r.status) + ')';
    } catch (e) { where += '; the game did not answer'; }
    moduleLoadGame();
  } else if (saver) {
    try { await saver(file, text); where += ' and saved as ' + file; } catch (e) { /* declined: still kept in the browser */ }
  }
  syncModules();
  return moduleNote('"' + mod.name + '": ' + what + ', measured from enemy ' + (state.selected + 1) + '; ' + where + '.');
}
// Where the anchor goes: the middle of the map view, the selected enemy, or a typed spot; on the floor there when there is one.
function moduleAnchor() {
  const how = String($('mdlAnchor').value || 'view'), e = state.enemies[state.selected];
  if (how === 'selected') return e ? e.pos.slice(0, 3) : null;
  const x = how === 'typed' ? parseFloat($('mdlX').value) : state.view.cx, z = how === 'typed' ? parseFloat($('mdlZ').value) : state.view.cz;
  if (!isFinite(x) || !isFinite(z)) return null;
  const y = groundAt(x, z), typedY = parseFloat($('mdlY').value);
  return [x, y !== null ? y : how === 'typed' && isFinite(typedY) ? typedY : 0, z];
}
async function moduleInsertChosen() {
  const mod = await moduleChosen();
  if (!mod) return moduleNote('Choose a module to insert, or open a module file.');
  const at = moduleAnchor();
  if (!at) return moduleNote($('mdlAnchor').value === 'selected' ? 'Select an enemy to put the anchor on first.' : 'Type the x and z of the spot first.');
  const r = moduleInsert(mod, at, parseFloat($('mdlTurn').value) || 0);
  return moduleNote(r.error || 'Copy ' + r.copy + ' of "' + mod.name + '" inserted.');
}

// ----- the Modules part of the Triggers tab
function syncModules() {
  if (!state.moduleBindings) return;      // syncSide can run before this part has started
  const pick = $('mdlPick'), local = savedModules();
  const options = [...local.map((m, i) => ['l:' + i, m.name + ' (' + m.spawns.length + ' + ' + m.triggers.length + ')']), ...moduleGame.map(m => ['g:' + m.name, (m.title || m.name) + ' (the game\'s)']),
    ...(moduleFile ? [['f:', moduleFile.name + ' (file)']] : [])];
  const key = options.map(o => o.join('=')).join('|');
  if (pick.dataset.key !== key && document.activeElement !== pick) {
    const was = pick.value; pick.dataset.key = key; pick.textContent = '';
    for (const [v, t] of options) { const o = document.createElement('option'); o.value = v; o.textContent = t; pick.appendChild(o); }
    if (options.some(o => o[0] === was)) pick.value = was;
  }
  $('mdlForget').hidden = !String(pick.value || '').startsWith('l:');
  $('mdlTyped').hidden = $('mdlAnchor').value !== 'typed';
  $('mdlSave').disabled = !picked().length;

  // One row per reference still to bind, with what on this map it could be.
  const open = moduleOpen(), list = $('mdlBindings');
  const bkey = open.map(b => b.key).join('|') + '#' + state.doors.length + '#' + state.zones.map(z => z.id).join(',') + '#' + state.places.map(p => p.id).join(',') + '#' + state.objectives.length;
  if (list.dataset.key !== bkey) {
    list.dataset.key = bkey; list.textContent = '';
    for (const b of open) {
      const li = document.createElement('li'), what = document.createElement('span'), go = document.createElement('button');
      what.textContent = b.label + ' · copy ' + b.copy + ' of "' + b.module + '"' + (b.required ? '' : ' (optional)');
      li.appendChild(what);
      let control = null, read = null;
      const choose = (items, nothing) => {
        if (!items.length) { const p = document.createElement('span'); p.className = 'hint'; p.textContent = nothing; li.appendChild(p); return; }
        control = document.createElement('select');
        for (const [v, t] of items) { const o = document.createElement('option'); o.value = v; o.textContent = t; control.appendChild(o); }
      };
      if (b.kind === 'door') {
        const far = d => (b.hint ? Math.hypot(d.pos[0] - b.hint[0], d.pos[1] - b.hint[1], d.pos[2] - b.hint[2]) : 0);
        choose(state.doors.map((d, i) => [i, far(d)]).sort((p, q) => p[1] - q[1]).map(([i, dist]) => [i, (state.doors[i].name || 'Door ' + (i + 1)) + (b.hint ? ' · ' + dist.toFixed(0) + ' m from where it would be' : '')]),
          'No doors are known on this map yet: connect to the game during a mission, or open a recon file.');
        read = () => parseInt(control.value, 10);
      } else if (b.kind === 'trigger') {
        choose(state.zones.filter(z => z.moduleCopy !== b.copy).map(z => [z.id, trigText(z) + (z.name ? ': ' + z.name : '')]), 'The mission has no other trigger yet: add the one it should wait for first.');
        read = () => parseInt(control.value, 10);
      } else if (b.kind === 'place') {
        choose(state.places.map(p => [p.id, p.name]), 'This map has no named places yet: name the place first.');
        read = () => parseInt(control.value, 10);
      } else if (b.kind === 'objective') {
        const names = [...new Set([b.hint, ...state.objectives.map(o => o.label)].filter(Boolean))];
        choose([...names.map(n => [n, n]), ['', 'Any intel item'], ['all', 'All intel items']], '');
        read = () => control.value;
      } else {
        choose([['yes', 'Yes: the extraction waits for it'], ['no', 'No: leave the extraction as it is']], '');
        read = () => control.value === 'yes';
      }
      if (control) {
        control.setAttribute('aria-label', 'What ' + b.label + ' is on this map');
        go.type = 'button'; go.className = 'btn'; go.textContent = 'Bind';
        go.addEventListener('click', () => moduleBind(b.key, read()));
        li.append(control, go);
      }
      list.appendChild(li);
    }
  }
  $('mdlBindHint').hidden = open.length > 0;
  const rep = state.moduleReport, out = $('mdlReport');
  const rkey = rep ? rep.copy + '#' + rep.checked + '#' + rep.off.join('|') : '';
  if (out.dataset.key !== rkey) {
    out.dataset.key = rkey; out.textContent = '';
    if (rep) {
      const head = document.createElement('li'); head.className = 'sum'; head.textContent = 'Copy ' + rep.copy + ' of "' + rep.name + '": ' + modulePlacementWords(rep); out.appendChild(head);
      for (const line of rep.off.slice(0, 20)) { const li = document.createElement('li'); li.textContent = line; out.appendChild(li); }
    }
  }
  $('mdlCheck').hidden = !rep;
}
$('mdlSave').addEventListener('click', () => { moduleSaveSelection(); });
$('mdlInsert').addEventListener('click', () => { moduleInsertChosen(); });
$('mdlAnchor').addEventListener('change', syncModules);
$('mdlPick').addEventListener('change', syncModules);
$('mdlOpen').addEventListener('click', () => $('mdlFile').click());
$('mdlFile').addEventListener('change', async ev => {
  const f = ev.target.files && ev.target.files[0]; ev.target.value = '';
  if (!f) return;
  try {
    const obj = JSON.parse(await f.text()), wrong = moduleProblems(obj);
    if (wrong.length) { moduleNote(f.name + ' is not a module MissionLab can use: ' + wrong[0] + '.'); return; }
    moduleFile = obj; syncModules(); $('mdlPick').value = 'f:'; syncModules();
    moduleNote(f.name + ' is open: "' + obj.name + '". Choose where it goes and Insert module.');
  } catch (err) { moduleNote(f.name + ' could not be read: ' + (err && err.message ? err.message : err)); }
});
$('mdlForget').addEventListener('click', () => {
  const v = String($('mdlPick').value || '');
  if (!v.startsWith('l:')) return;
  const list = savedModules(), gone = list.splice(+v.slice(2), 1)[0];
  storeModules(list); syncModules();
  if (gone) moduleNote('"' + gone.name + '" is no longer kept in this browser.');
});
$('mdlCheck').addEventListener('click', () => {
  const rep = state.moduleReport; if (!rep) return;
  rep.off = modulePlacement(rep.copy); rep.checked = !!state.map;
  syncModules(); say(modulePlacementWords(rep));
});
// Last, so syncModules (called from syncSide) does nothing until everything above exists.
Object.assign(state, { moduleBindings: [], moduleCopies: 0, moduleReport: null });
if (moduleServed()) moduleLoadGame();
