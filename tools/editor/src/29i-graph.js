// ---------------------------------------------------------------- mission graph (ML-042): the whole mission in one picture, read only
// What each trigger waits for, what it brings, which ones open the extraction and which end the mission. Every box and
// line is read from the scenario model (state.zones, the enemies and things bound to a trigger, state.extractionAfter);
// nothing here works out whether a trigger can fire. What is wrong is marked from the mission check's own findings (their
// about field, 34-check.js) and the circles TriggerEngine.Rings finds (checkRings); live state is what the inspector shows
// (inspectRows, 29c-inspect.js) and a rehearsal is its last answer (29d-rehearse.js). So the picture cannot disagree with
// the check, the inspector or the game. The layout is for the eye only: it is kept in state.graph, never in the scenario,
// so arranging the picture cannot change what the mission does.
// mark: triggers another part of the page points the author at (29l-validate.js: the ones a validation counterexample
// names), set only through graphMark. Display only, like the layout: a dashed box and a line of words, never colour alone.
state.graph = { dir: 'down', shape: '', look: '', base: null, model: null, mark: { ids: [], label: '' } };
const GRAPH_SVG = 'http://www.w3.org/2000/svg';
const GRAPH_W = 172, GRAPH_LINE = 14, GRAPH_GAP = 14, GRAPH_LAYER_GAP = 40, GRAPH_PAD = 8, GRAPH_ROOM = 36;   // ROOM: space for lines that go round the boxes
const GRAPH_EDGES = ['start', 'hand', 'after', 'outcome', 'all', 'any', 'holds', 'opens', 'end'];
const GRAPH_FLAG_WORDS = {
  circle: 'In a circle: never fires', never: 'Can never fire', missing: 'Waits for a trigger that is gone', self: 'Lists itself (left out on saving)',
  blind: 'Waits for a result that can never come',
};
// The word an outcome line (ML-047) carries: what the watching trigger waits to hear of the latest firing of the other.
const GRAPH_OUTCOME_WORDS = { applied: 'applied', failed: 'failed', partial: 'partial', unconfirmed: 'unconfirmed', timeout: 'timeout' };
const GRAPH_EXIT_WORDS = { never: 'Can never open', missing: 'Waits for a trigger that is gone', unsure: 'May stay shut on some runs', byhand: 'Opens only by hand' };

// ----- the model: boxes and lines, from the scenario alone

// What a trigger waits for, in a few words. The delay, the chance and repeating are said apart (graphTimings).
function graphCondition(z) {
  const list = (z.of || []).join(', '), item = String(z.item || '').replace(/[ _]\d+$/, '').trim();
  switch (z.when) {
    case 'zone': return (z.hold > 0 ? 'Players hold the zone ' + Math.round(z.hold) + ' s' : 'Players reach the zone') + (z.everyone ? ', all of them' : '') + (z.after ? ', once trigger ' + z.after + ' has fired' : '');
    case 'alert': return 'The enemy raises the alarm';
    case 'intel': return item === '' || item.toLowerCase() === 'any' ? 'Any intel is picked up' : item === 'all' ? 'Every intel item is picked up' : item + ' is picked up';
    case 'time': return Math.round(z.delay || 0) + ' s into the mission';
    case 'after': return z.after ? 'After trigger ' + z.after : 'Only by hand (Fire now)';
    case 'outcome': return z.after ? 'Trigger ' + z.after + ' ' + (z.outcome === 'timeout' ? 'gives no answer in ' + Math.round(z.within || OUTCOME_WITHIN) + ' s' : 'reports ' + (GRAPH_OUTCOME_WORDS[z.outcome] || z.outcome)) : 'Watches no trigger';
    case 'all': return list ? 'All of ' + list : 'All of: none ticked';
    case 'any': return list ? 'One of ' + list : 'One of: none ticked';
    case 'down': return ((z.count | 0) > 0 ? z.count + ((z.count | 0) === 1 ? ' enemy down' : ' enemies down') : 'Every enemy down') + (z.tag ? ', tagged "' + z.tag + '"' : '');
    case 'door': return 'A door is opened';
  }
  return 'Its condition is met';
}
function graphTimings(z) {
  const out = [];
  if (z.delay > 0 && z.when !== 'time') out.push({ kind: 'delay', text: 'then waits ' + Math.round(z.delay) + ' s' });
  if ((z.chance || 100) < 100) out.push({ kind: 'chance', text: z.chance + '% chance, else sits out' });
  if (z.repeat > 0) out.push({ kind: 'repeat', text: 'again every ' + Math.round(z.repeat) + ' s' + (z.times > 0 ? ', ' + z.times + ' times' : '') });
  return out;
}
// What firing does. short goes in the box, text in its tooltip and the detail line.
function graphEffects(z) {
  const out = [], put = (kind, short, text, n) => out.push(n === undefined ? { kind, short, text } : { kind, short, text, n });
  const enemies = state.enemies.filter(e => e.trigger === z.id).length, things = state.things.filter(t => t.trigger === z.id).length;
  const doors = (z.doors || []).filter(d => d.locked != null || d.open != null).length, message = cleanMessage(z.message);
  if (enemies) put('enemies', enemies + (enemies === 1 ? ' enemy' : ' enemies'), 'brings ' + enemies + (enemies === 1 ? ' enemy' : ' enemies'), enemies);
  if (things) put('things', things + (things === 1 ? ' thing' : ' things'), 'places ' + things + (things === 1 ? ' thing' : ' things'), things);
  if (doors) put('doors', doors + (doors === 1 ? ' door' : ' doors'), 'sets ' + doors + (doors === 1 ? ' door' : ' doors'), doors);
  if (z.power === 'off' || z.power === 'on') put('power', z.power === 'off' ? 'lights out' : 'lights on', z.power === 'off' ? 'switches the lights off' : 'switches the lights on');
  if (message) put('message', z.red ? 'red message' : 'message', (z.red ? 'shows in red: ' : 'shows: ') + message);
  if (z.respawn) put('respawn', 'dead come back', 'dead players come back');
  if (z.rally > 0) put('rally', 'rally ' + Math.round(z.rally) + ' m', 'enemies within ' + Math.round(z.rally) + ' m walk there');
  if (z.hunt) put('hunt', 'hunt', 'its enemies come to the players');
  if (z.opensExtraction) put('extraction', 'opens extraction', 'opens the extraction');
  if (z.end === 'success' || z.end === 'failed') put('end', 'ends: ' + z.end, 'ends the mission: ' + z.end);
  return out;
}

// The boxes (nodes) and lines (edges) of the scenario on the page, with the mission check's marks; nothing when the view is
// sealed (20b-sealed.js), as the inspector leaves out what triggers wait for and what they do.
// Node: { key, kind: start|trigger|gone|extraction|end, id, title, cond, timings, effects, flags, findings }.
// Edge: { from, to, kind: start|hand|after|outcome|all|any|holds|opens|end } between node keys; an outcome line also has
// word, the outcome it waits for, written on the line.
function graphBase() {
  if (sealedHides()) return { sealed: true, nodes: [], edges: [], rings: [] };
  const nodes = [], edges = [], byKey = new Map();
  const add = n => { nodes.push(Object.assign({ cond: '', timings: [], effects: [], flags: [], findings: [] }, n)); byKey.set(n.key, nodes[nodes.length - 1]); };
  add({ key: 'start', kind: 'start', id: 0, title: 'Mission start' });
  // A second trigger with the same number gets a box of its own; what waits for that number means the first, as in the plugin.
  const keys = state.zones.map((z, i) => (state.zones.findIndex(q => q.id === z.id) === i ? 't:' + z.id : 't:' + z.id + '/' + i));
  const ref = id => {
    if (state.zones.some(z => z.id === id)) return 't:' + id;
    if (!byKey.has('gone:' + id)) add({ key: 'gone:' + id, kind: 'gone', id, title: 'Trigger ' + id, cond: 'Not in this scenario' });
    return 'gone:' + id;
  };
  state.zones.forEach((z, i) => add({ key: keys[i], kind: 'trigger', id: z.id, title: trigTitle(z) + (z.name ? ': ' + z.name : ''), cond: graphCondition(z), timings: graphTimings(z), effects: graphEffects(z) }));
  state.zones.forEach((z, i) => {
    const to = keys[i];
    if (z.when === 'after') edges.push(z.after ? { from: ref(z.after), to, kind: 'after' } : { from: 'start', to, kind: 'hand' });
    else if (z.when === 'outcome') { if (z.after) edges.push({ from: ref(z.after), to, kind: 'outcome', word: GRAPH_OUTCOME_WORDS[z.outcome] || String(z.outcome || '') }); }
    else if (z.when === 'all' || z.when === 'any') { for (const id of z.of || []) if (id !== z.id) edges.push({ from: ref(id), to, kind: z.when }); }
    // A zone or hold with a gate (ML-055) is watched only once the gate has fired: an after line, told apart in its sentence.
    else if (z.when === 'zone' && z.after) edges.push({ from: ref(z.after), to, kind: 'after', gate: true });
    else edges.push({ from: 'start', to, kind: 'start' });
  });
  const openers = state.zones.filter(z => z.opensExtraction).map(z => z.id);
  if (state.extractionAfter || openers.length) {
    const held = state.extractionAfter ? ref(state.extractionAfter) : '';
    add({ key: 'extraction', kind: 'extraction', id: 0, title: 'Extraction',
      cond: state.extractionAfter ? 'Held until trigger ' + state.extractionAfter + ' fires' : 'Not held by the scenario',
      timings: openers.length ? [{ kind: 'openers', text: 'opened by ' + (openers.length === 1 ? 'trigger ' : 'triggers ') + openers.join(', ') }] : [] });
    if (held) edges.push({ from: held, to: 'extraction', kind: 'holds' });
    state.zones.forEach((z, i) => { if (z.opensExtraction) edges.push({ from: keys[i], to: 'extraction', kind: 'opens' }); });
  }
  for (const [n, end] of [[1, 'success'], [2, 'failed']]) {
    const by = state.zones.map((z, i) => (z.end === end ? i : -1)).filter(i => i >= 0);
    if (!by.length) continue;
    add({ key: 'end:' + end, kind: 'end', id: n, title: end === 'success' ? 'Mission success' : 'Mission failed', cond: 'Ended by ' + (by.length === 1 ? 'trigger ' : 'triggers ') + by.map(i => state.zones[i].id).join(', ') });
    for (const i of by) edges.push({ from: keys[i], to: 'end:' + end, kind: 'end' });
  }
  // The marks: only findings that name their triggers or the extraction (about), never a sentence read back.
  for (const f of missionCheck()) {
    const a = f.about; if (!a) continue;
    const mark = n => { if (!n) return; if (!n.flags.includes(a.why)) n.flags.push(a.why); if (!n.findings.includes(f.text)) n.findings.push(f.text); };
    if (a.extraction) mark(byKey.get('extraction'));
    for (const id of a.triggers || []) for (const n of nodes) if (n.kind === 'trigger' && n.id === id) mark(n);
  }
  return { sealed: false, nodes, edges, rings: checkRings(state.zones) };
}

// ----- live and rehearsal state, as the inspector and the rehearsal panel have it

// By trigger number: the inspector's row (where it stands now) and how often the game says it has fired. Fired so far is
// history; the phase is now; the outcome is whether its actions took. They are kept apart so the picture shows them apart.
function graphLive() {
  if (!inspectRunning()) return null;
  const out = new Map();
  for (const r of inspectRows({ triggerState: [...live.trig.values()] })) {
    const t = live.trig.get(r.id), ex = t && t.explain;
    out.set(r.id, { phase: r.kind, word: r.word, clock: r.clock, fires: t ? ((ex ? ex.fires : t.fires) | 0) : 0, outcome: r.outcome, outcomeText: r.outcomeText, late: r.late });
  }
  return out;
}
// The last rehearsal's checks, by node key, when that answer was given for the scenario text on the page: an answer about
// an earlier version says nothing about this one. Each step names its trigger (trigger, 0 for none) and whether it checks
// the extraction; an answer from a plugin older than those fields gives no overlay (graphRehearsalOld), as reading the
// number back out of the step's words could pick the wrong box. A failed check wins.
function graphRehearsalOld() {
  const a = state.rehearse && state.rehearse.answer;
  return !!(a && typeof a === 'object' && Array.isArray(a.steps) && a.steps.some(x => x && x.expected && typeof x.trigger !== 'number'));
}
function graphRehearsal() {
  const a = state.rehearse && state.rehearse.answer;
  if (!a || typeof a !== 'object' || a.refused || !Array.isArray(a.steps) || !a.scenarioHash) return null;
  if (a.scenarioHash !== textHash(scenarioText())) return null;
  if (graphRehearsalOld()) return null;
  const out = new Map();
  for (const x of a.steps) {
    if (!x || !x.expected) continue;
    const key = x.trigger > 0 ? 't:' + x.trigger : x.extraction === true ? 'extraction' : '';
    if (!key) continue;
    const was = out.get(key);
    if (was && !was.ok) continue;
    out.set(key, { ok: !!x.ok, text: String(x.what || '').replace(/^step \d+ at [^:]*: /, '') + (x.ok ? '' : ', got ' + (x.actual || 'nothing')) });
  }
  return out;
}

// Lines of text no wider than max characters, at most rows of them; what does not fit ends in an ellipsis (the tooltip
// and the detail line have it all).
function graphWrap(text, max, rows) {
  const out = [];
  let line = '';
  for (const w of String(text).split(' ')) {
    if (!line) line = w;
    else if ((line + ' ' + w).length <= max) line += ' ' + w;
    else { out.push(line); line = w; }
  }
  if (line) out.push(line);
  if (out.length > rows) { out.length = rows; out[rows - 1] = out[rows - 1].slice(0, max - 1) + '…'; }
  return out.map(l => (l.length > max ? l.slice(0, max - 1) + '…' : l));
}
function graphFlagWords(n) {
  const words = n.kind === 'extraction' ? GRAPH_EXIT_WORDS : GRAPH_FLAG_WORDS;
  return n.flags.filter(f => words[f] && !(f === 'never' && n.flags.includes('circle'))).map(f => words[f]);
}
// The rows of text in a box, each with its class.
function graphRows(n) {
  const rows = [], put = (cls, text, max, most) => { for (const t of graphWrap(text, max, most)) rows.push({ cls, text: t }); };
  put('g-title', n.title, 24, 1);
  if (n.cond) put('g-cond', n.cond, 26, 2);
  for (const t of n.timings) put('g-time g-' + t.kind, t.text, 24, 1);
  if (n.effects.length) put('g-fx', n.effects.map(e => e.short).join(' · '), 24, 2);
  for (const w of graphFlagWords(n)) put('g-flag', w, 26, 2);
  if (n.live) {
    put('g-now', 'Now: ' + n.live.word + (n.live.clock ? ', ' + n.live.clock + ' left' : ''), 26, 1);
    put(n.live.fires > 0 ? 'g-hist' : 'g-hist none', n.live.fires > 0 ? 'Fired ' + n.live.fires + (n.live.fires === 1 ? ' time' : ' times') + ' so far' : 'Not fired yet', 26, 1);
    if (n.live.outcomeText) put('g-out', n.live.outcomeText, 26, 2);
    if (n.live.late) put('g-late', 'Late: ' + n.live.late, 26, 2);
  }
  if (n.rehearsal) put('g-rh', 'Rehearsal ' + (n.rehearsal.ok ? '✓ ' : '✗ ') + n.rehearsal.text, 26, 2);
  if (graphMarked(n)) put('g-flag g-markrow', state.graph.mark.label, 26, 1);
  return rows;
}
// Everything a box says, in full: its tooltip and what a screen reader reads.
function graphNodeText(n) {
  return [n.title, n.cond, ...n.timings.map(t => t.text), ...n.effects.map(e => e.text), ...n.findings,
    n.live ? 'Now: ' + n.live.word + (n.live.clock ? ', ' + n.live.clock + ' left' : '') + '. ' + (n.live.fires > 0 ? 'Fired ' + n.live.fires + ' so far' : 'Not fired yet') : '',
    n.live && n.live.outcomeText, n.live && n.live.late && 'Late: ' + n.live.late, n.rehearsal && 'Rehearsal: ' + n.rehearsal.text, graphMarked(n) && state.graph.mark.label].filter(Boolean).join('\n');
}

// The model with live and rehearsal state put on, and the rows each box shows.
function graphDress(base) {
  const now = base.sealed ? null : graphLive(), rh = base.sealed ? null : graphRehearsal();
  const nodes = base.nodes.map(b => {
    const n = Object.assign({}, b, { live: null, rehearsal: null });
    if (now && n.kind === 'trigger' && now.has(n.id)) n.live = now.get(n.id);
    if (rh && (n.kind === 'trigger' || n.kind === 'extraction') && rh.has(n.kind === 'trigger' ? 't:' + n.id : 'extraction')) n.rehearsal = rh.get(n.kind === 'trigger' ? 't:' + n.id : 'extraction');
    n.rows = graphRows(n);
    return n;
  });
  return { sealed: base.sealed, nodes, edges: base.edges, rings: base.rings, live: !!now, rehearsal: !!rh };
}
function graphModel() { return graphDress(graphBase()); }

// The classes of a box: kind, the check's marks (f-), then the three live things kept apart: history (h-fired, it has fired
// at least once), the phase now (p-), and the outcome of its actions (o-); and a rehearsal check (r-).
function graphNodeClass(n) {
  const c = ['g-node', 'g-' + n.kind];
  if (n.kind === 'end') c.push('g-end-' + (n.id === 1 ? 'success' : 'failed'));
  for (const f of n.flags) c.push('f-' + f);
  if (n.live) { if (n.live.fires > 0) c.push('h-fired'); c.push('p-' + n.live.phase); if (n.live.outcome) c.push('o-' + n.live.outcome); if (n.live.late) c.push('l-late'); }
  if (n.rehearsal) c.push(n.rehearsal.ok ? 'r-ok' : 'r-bad');
  if (graphMarked(n)) c.push('g-mark');
  if (n.kind === 'trigger' && state.inspectSel && n.id === state.inspectSel) c.push('sel');
  return c.join(' ');
}
// Marks these triggers with these words (shown in the box and its tooltip) and draws the graph again; graphMark([]) lets
// them go. One set of marks at a time: the last caller's.
function graphMark(ids, label) {
  state.graph.mark = { ids: Array.isArray(ids) ? ids.filter(id => Number.isInteger(id)) : [], label: String(label || 'Marked') };
  graphSync(true);
}
const graphMarked = n => n.kind === 'trigger' && state.graph.mark.ids.includes(n.id);

// ----- the layout: display only

// Layers by how deep a trigger sits behind what it waits for, ties by number; the same for every order of the list.
// Triggers in a circle share one layer. The extraction and the endings come last. dir: 'down' (layers are rows) or
// 'across' (layers are columns). Returns { width, height, at: Map(key -> { x, y, w, h, layer }) }.
function graphLayout(model, dir) {
  const across = dir === 'across', ringOf = new Map();
  for (const r of model.rings) for (const id of r) ringOf.set('t:' + id, 'ring:' + r[0]);
  const comp = k => ringOf.get(k) || k, preds = new Map();
  for (const e of model.edges) {
    if (e.to === 'extraction' || e.to.startsWith('end:')) continue;
    const a = comp(e.from), b = comp(e.to);
    if (a === b) continue;
    if (!preds.has(b)) preds.set(b, []);
    preds.get(b).push(a);
  }
  const depth = new Map(), busy = new Set();
  const deep = c => {
    if (depth.has(c)) return depth.get(c);
    if (busy.has(c)) return 0;          // only an unforeseen circle: the circles above are already one each
    busy.add(c);
    let d = 0;
    for (const p of preds.get(c) || []) d = Math.max(d, deep(p) + 1);
    busy.delete(c); depth.set(c, d);
    return d;
  };
  const layerOf = new Map(), last = n => n.kind === 'extraction' || n.kind === 'end';
  let deepest = 0;
  for (const n of model.nodes) if (!last(n)) { const d = deep(comp(n.key)); layerOf.set(n.key, d); deepest = Math.max(deepest, d); }
  for (const n of model.nodes) if (last(n)) layerOf.set(n.key, deepest + 1);
  const rank = { start: 0, gone: 1, trigger: 2, extraction: 3, end: 4 }, layers = [];
  for (const n of model.nodes) { const l = layerOf.get(n.key); (layers[l] = layers[l] || []).push(n); }
  const at = new Map();
  let offset = GRAPH_PAD, width = 0, height = 0;
  layers.forEach((list, layer) => {
    if (!list) return;
    list.sort((a, b) => rank[a.kind] - rank[b.kind] || a.id - b.id || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
    const hs = list.map(n => (n.rows ? n.rows.length : 1) * GRAPH_LINE + 10), thick = across ? GRAPH_W : Math.max(...hs);
    let along = GRAPH_PAD;
    list.forEach((n, i) => {
      at.set(n.key, across ? { x: offset, y: along, w: GRAPH_W, h: hs[i], layer } : { x: along, y: offset, w: GRAPH_W, h: hs[i], layer });
      along += (across ? hs[i] : GRAPH_W) + GRAPH_GAP;
    });
    width = Math.max(width, across ? offset + GRAPH_W : along - GRAPH_GAP);
    height = Math.max(height, across ? along - GRAPH_GAP : offset + thick);
    offset += thick + GRAPH_LAYER_GAP;
  });
  return { width: Math.ceil(width + GRAPH_PAD + GRAPH_ROOM), height: Math.ceil(height + GRAPH_PAD + GRAPH_ROOM), at };
}
// A line from box a to box b. shift parts several lines between the same two boxes. Forward lines run from the end of one
// layer to the start of the next; lines within a layer bow out past the boxes; lines back (a circle) go round the side.
function graphPath(a, b, shift, across) {
  const p = v => Math.round(v * 10) / 10, c = (x1, y1, x2, y2, x3, y3, x4, y4) => 'M' + p(x1) + ',' + p(y1) + ' C' + p(x2) + ',' + p(y2) + ' ' + p(x3) + ',' + p(y3) + ' ' + p(x4) + ',' + p(y4);
  if (a === b) { const x = a.x + a.w, y = a.y + a.h / 2; return c(x, y - 6, x + 26, y - 20, x + 26, y + 20, x, y + 6); }
  if (!across) {
    const x1 = a.x + a.w / 2 + shift, x2 = b.x + b.w / 2 + shift;
    if (b.layer > a.layer) { const y1 = a.y + a.h, y2 = b.y, d = Math.max(16, (y2 - y1) / 2); return c(x1, y1, x1, y1 + d, x2, y2 - d, x2, y2); }
    if (b.layer === a.layer) { const y1 = a.y + a.h, y2 = b.y + b.h, low = Math.max(y1, y2) + 22; return c(x1, y1, x1, low, x2, low, x2, y2); }
    const y1 = a.y + a.h / 2 + shift, y2 = b.y + b.h / 2 + shift, xa = a.x + a.w, xb = b.x + b.w, out = Math.max(xa, xb) + 28;
    return c(xa, y1, out, y1, out, y2, xb, y2);
  }
  const y1 = a.y + a.h / 2 + shift, y2 = b.y + b.h / 2 + shift;
  if (b.layer > a.layer) { const x1 = a.x + a.w, x2 = b.x, d = Math.max(16, (x2 - x1) / 2); return c(x1, y1, x1 + d, y1, x2 - d, y2, x2, y2); }
  if (b.layer === a.layer) { const x1 = a.x + a.w, x2 = b.x + b.w, far = Math.max(x1, x2) + 22; return c(x1, y1, far, y1, far, y2, x2, y2); }
  const x1 = a.x + a.w / 2 + shift, x2 = b.x + b.w / 2 + shift, ya = a.y + a.h, yb = b.y + b.h, out = Math.max(ya, yb) + 28;
  return c(x1, ya, x1, out, x2, out, x2, yb);
}
function graphEdgeText(e, byKey) {
  const name = k => { const n = byKey.get(k); return n ? n.title : k; };
  switch (e.kind) {
    case 'start': return name(e.to) + ' is armed from the start';
    case 'hand': return name(e.to) + ' fires only when the Game Master presses Fire now';
    case 'after': return e.gate ? name(e.to) + ' watches its area only once ' + name(e.from) + ' has fired' : name(e.to) + ' waits for ' + name(e.from);
    case 'outcome': return name(e.to) + (e.word === 'timeout' ? ' fires when ' + name(e.from) + ' gives no answer in time' : ' fires when the latest firing of ' + name(e.from) + ' is reported ' + e.word);
    case 'all': return name(e.to) + ' needs ' + name(e.from) + ', with every other one of its list';
    case 'any': return name(e.to) + ' needs ' + name(e.from) + ' or another one of its list';
    case 'holds': return 'The extraction stays shut until ' + name(e.from) + ' fires';
    case 'opens': return name(e.from) + ' opens the extraction';
    case 'end': return name(e.from) + ' ends the mission: ' + (e.to === 'end:success' ? 'success' : 'failed');
  }
  return '';
}

// ----- drawing and picking

function graphDraw() {
  const svg = $('graphSvg'), m = state.graph.model;
  if (!svg || !m) return;
  const focused = document.activeElement && typeof document.activeElement.getAttribute === 'function' ? document.activeElement.getAttribute('data-key') : null;
  svg.textContent = '';
  if (m.sealed || !m.nodes.some(n => n.kind === 'trigger')) { svg.setAttribute('width', '0'); svg.setAttribute('height', '0'); return; }
  const box = graphLayout(m, state.graph.dir), across = state.graph.dir === 'across', byKey = new Map(m.nodes.map(n => [n.key, n]));
  const el = (tag, attrs, parent) => { const e = document.createElementNS(GRAPH_SVG, tag); for (const k of Object.keys(attrs)) e.setAttribute(k, String(attrs[k])); if (parent) parent.appendChild(e); return e; };
  svg.setAttribute('width', String(box.width)); svg.setAttribute('height', String(box.height)); svg.setAttribute('viewBox', '0 0 ' + box.width + ' ' + box.height);
  const defs = el('defs', {}, svg);
  for (const k of GRAPH_EDGES) el('path', { d: 'M0,0 L8,4 L0,8 z', class: 'g-head e-' + k }, el('marker', { id: 'gArrow-' + k, viewBox: '0 0 8 8', refX: 7, refY: 4, markerWidth: 7, markerHeight: 7, orient: 'auto' }, defs));
  const lines = el('g', { class: 'g-edges' }, svg), pairs = new Map();
  for (const e of m.edges) { const k = e.from + '>' + e.to; if (!pairs.has(k)) pairs.set(k, []); pairs.get(k).push(e); }
  for (const list of pairs.values()) list.forEach((e, i) => {
    const a = box.at.get(e.from), b = box.at.get(e.to);
    if (!a || !b) return;
    const path = el('path', { d: graphPath(a, b, (i - (list.length - 1) / 2) * 10, across), class: 'g-edge e-' + e.kind + (e.kind === 'end' ? ' ' + e.to.replace(':', '-') : ''), 'marker-end': 'url(#gArrow-' + e.kind + ')' }, lines);
    el('title', {}, path).textContent = graphEdgeText(e, byKey);
    // An outcome line says which outcome it waits for, halfway along, so it is told from an after line without colour.
    if (e.kind === 'outcome') {
      const mid = across ? { x: (a.x + a.w + b.x) / 2, y: (a.y + a.h / 2 + b.y + b.h / 2) / 2 } : { x: (a.x + a.w / 2 + b.x + b.w / 2) / 2, y: (a.y + a.h + b.y) / 2 };
      el('text', { class: 'g-elabel', x: Math.round(mid.x), y: Math.round(mid.y) + 3, 'text-anchor': 'middle' }, lines).textContent = e.word;
    }
  });
  const boxes = el('g', { class: 'g-nodes' }, svg);
  for (const n of m.nodes) {
    const b = box.at.get(n.key);
    if (!b) continue;
    const g = el('g', { class: graphNodeClass(n), transform: 'translate(' + b.x + ',' + b.y + ')', 'data-key': n.key }, boxes);
    el('rect', { class: 'g-box', width: b.w, height: b.h, rx: 4 }, g);
    // Fired so far, as a tab on the corner: history, never mixed up with the border (the phase now) or the outcome row.
    if (n.live && n.live.fires > 0) { el('rect', { class: 'g-tab', x: b.w - 30, y: -7, width: 30, height: 14, rx: 7 }, g); el('text', { class: 'g-tabtext', x: b.w - 15, y: 3.5, 'text-anchor': 'middle' }, g).textContent = '✓' + n.live.fires; }
    n.rows.forEach((r, i) => { el('text', { class: r.cls, x: 8, y: 16 + i * GRAPH_LINE }, g).textContent = r.text; });
    el('title', {}, g).textContent = graphNodeText(n);
    if (n.kind !== 'trigger') continue;
    g.setAttribute('tabindex', '0'); g.setAttribute('role', 'button');
    g.setAttribute('aria-pressed', String(n.id === state.inspectSel)); g.setAttribute('aria-label', graphNodeText(n));
    g.addEventListener('click', () => graphPick(n.id));
    g.addEventListener('keydown', ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); graphPick(n.id); } });
  }
  if (typeof focused === 'string' && focused) { const again = svg.querySelector('[data-key="' + focused + '"]'); if (again && typeof again.focus === 'function') again.focus(); }
}

// Clicking a trigger opens it in the inspector. In Direct that is the inspector's own selection: its row, and its place,
// its enemies and its doors on the map. In Create, where the inspector is not shown, its enemies become the selection and
// the map turns to its place, and Direct opens on it. Clicking it again lets it go. Nothing in the scenario changes.
function graphPick(id) {
  if (sealedHides()) return;
  const z = state.zones.find(q => q.id === id);
  if (!z) return;
  if (viewIs('direct')) inspectSelect(id);
  else if (state.inspectSel === id) state.inspectSel = 0;
  else {
    state.inspectSel = id;
    const bound = [];
    state.enemies.forEach((e, i) => { if (e.trigger === id) bound.push(i); });
    state.selected = bound.length ? bound[0] : -1; state.squad = bound.length > 1 ? bound : [];
    if (bound.length) state.door = -1;
    const spot = isVec(z.pos) ? z.pos : bound.length ? state.enemies[bound[0]].pos : null;
    if (spot) { state.view.cx = spot[0]; state.view.cz = spot[2]; }
    view3d.syncEnemies(); syncSide(); invalidate();
  }
  graphSync(true);
}
function graphSetDir(dir) {
  state.graph.dir = dir === 'across' ? 'across' : 'down';
  document.querySelectorAll('#graphBox [data-graph-dir]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.graphDir === state.graph.dir)));
  graphSync(true);
}
// The selected trigger in the inspector's words: why it waits or fires, and what it hangs on and brings.
function graphDetail() {
  const id = state.inspectSel;
  if (!id || sealedHides() || !state.zones.some(z => z.id === id)) return '';
  const row = inspectRows(inspectRunning() ? { triggerState: [...live.trig.values()] } : null).find(r => r.id === id);
  const why = row && row.text ? row.title + ': ' + row.text.replace(/\.$/, '') + '. ' : '';
  return why + inspectLinkText(inspectLinks(id));
}
// What the mission check marks, and the circles, need the structure only; live and rehearsal state are put on each time.
function graphShape() {
  return JSON.stringify([sealedHides(), state.extractionAfter, state.gameMaster, state.enemies.map(e => e.trigger | 0), state.things.map(t => t.trigger | 0),
    state.zones.map(z => [z.id, z.name, z.when, z.after, z.outcome, z.within, z.of, z.delay, z.chance, z.repeat, z.times, z.opensExtraction, z.power, cleanMessage(z.message), z.red, z.end,
      z.respawn, z.rally, z.hunt, z.hold, z.everyone, z.item, z.count, z.tag, isVec(z.pos), (z.doors || []).filter(d => d.locked != null || d.open != null).length]), state.enemies.map(e => !!(e.hvt || e.vehicle))]);
}
function graphSync(force) {
  const svg = $('graphSvg');
  if (!svg) return;
  const g = state.graph, shape = graphShape();
  if (force || shape !== g.shape || !g.base) { g.shape = shape; g.base = graphBase(); }
  const model = graphDress(g.base);
  const look = JSON.stringify([g.dir, state.inspectSel, viewIs('direct'), model.nodes.map(n => [n.key, graphNodeClass(n), n.rows.map(r => r.text)])]);
  if (!force && look === g.look) return;
  g.look = look; g.model = model;
  const triggers = model.nodes.filter(n => n.kind === 'trigger').length;
  $('graphCount').textContent = model.sealed || !triggers ? '' : String(triggers);
  $('graphNote').textContent = model.sealed ? 'This mission is sealed, so the graph does not show what triggers wait for or what they do. Reveal the mission to see it (spoiler avoidance: the file stays readable).'
    : !triggers ? 'This scenario has no triggers yet.'
    : (model.live ? 'Live from the game: "Now" is where each trigger stands, the tick on a corner how often it has fired so far, and an action that failed or was not confirmed says so.'
      : 'From the file. A red border is a problem the Mission check found.') + (model.rehearsal ? ' The last rehearsal\'s checks are shown on the triggers they checked.' : graphRehearsalOld() ? ' The last rehearsal came from an older plugin that does not name the trigger each check is about, so its checks are not shown here; update the plugin and rehearse again.' : '')
      + (viewIs('direct') ? ' Click a trigger to open it in the inspector.' : ' Click a trigger to select its enemies and read the inspector\'s words on it below.');
  $('graphDirRow').hidden = model.sealed || !triggers;
  $('graphDetail').textContent = model.sealed ? '' : graphDetail();
  graphDraw();
}
const graphShown = () => viewIs('direct') || (viewIs('create') && ['triggers', 'all'].includes($('side').dataset.show));
document.querySelectorAll('#graphBox [data-graph-dir]').forEach(b => b.addEventListener('click', () => graphSetDir(b.dataset.graphDir)));
// Twice a second while it is on screen, as the inspector: drawn again only when something it shows has changed.
const graphTick = () => { if (graphShown()) graphSync(false); };
setInterval(graphTick, 500); onPaneChange(graphTick);
expose({ graphModel, graphLayout, graphNodeClass, graphPick, graphSetDir, graphSync, graphDetail, graphMark });
