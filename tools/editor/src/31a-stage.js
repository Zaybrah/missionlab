// ------------------------------------------------------------------ staged live changes and saved revisions (ML-014)
// Live mode: as before, an edit is carried out in the game at once. Draft mode: edits stay on this page and are listed in
// words until Apply changes sends them to the game as ONE batch with one command number. A retry carries the same number,
// so the game answers it from what it already did and never does it twice. Afterwards the page shows what the game did with
// each part and takes the game's own state over again: what is shown follows the game, never the request.
// Game Master actions (messages, lights, orders, Fire now, Spawn waiting, Remove all) stay immediate in both modes.
const STAGE_MODE_KEY = 'missionlab-stage-mode', DRAFTS_KEY = 'missionlab-editor-drafts-1', REVISIONS_KEY = 'missionlab-revisions-1';
const REVISION_SCHEMA = 'missionlab-revision/1';
const STAGE_LIMITS = { lines: 60, shown: 12, revisions: 30, revisionChars: 2000000, drafts: 6 };
const stage = {
  mode: 'live',        // 'live' | 'draft'
  base: null,          // the game's setup the draft started from, as stageSnapshot() writes it
  baseKey: '',         // its text, to tell whether the draft differs at all
  baseRev: null,       // the game's marker revision when base was taken
  baseSession: null,   // the mission it was taken in
  baseProblems: [],    // problems the running mission already had then: they do not block a batch
  seenRev: null,       // the game's marker revision at the last poll
  dirty: false,        // the draft differs from base
  preview: false,      // the preview is open
  out: null,           // the batch last sent: { command, key, lines, done }
  receipt: null,       // what the game did with it: { command, lines, result, error, retry, at }
  separate: null,      // a separate draft is open: { key, label, run }
  revisions: null,     // the list last shown: { id, list }
  diff: null,          // differences between a revision and the page: { n, lines }
  gameScenario: '',    // the running scenario's id and revision as last taken over from the game
  separateTimer: 0,
};
try { if (localStorage.getItem(STAGE_MODE_KEY) === 'draft') stage.mode = 'draft'; } catch (e) { /* storage unavailable */ }

// ---- the semantic diff: what a change does, in words, for the preview, the staged list and revision comparisons
const STAGE_KIND_ORDER = ['enemy', 'reinforcement', 'trigger', 'extraction', 'respawn', 'door', 'thing', 'intel', 'rule', 'place', 'more'];
const STAGE_WHEN = { zone: 'players reaching it', hold: 'players holding it', alert: 'the alarm', intel: 'intel being picked up', time: 'a set time', after: 'another trigger', down: 'enemies going down', all: 'all of several triggers', any: 'one of several triggers', door: 'a door opening' };
const STAGE_SPAWN_WORDS = { role: 'role', group: 'group', type: 'type', fixed: 'sentry', defensive: 'holds its ground', prone: 'prone', indoor: 'indoors', hvt: 'target person', vehicle: 'gun truck', chance: 'chance %', lot: 'lot', either: 'either/or set', option: 'option', team: 'team', minPlayers: 'players needed', skill: 'skill', nvg: 'night vision', tag: 'tag', detectionRange: 'sight range', hearingRange: 'hearing range', fov: 'field of view', wanderDistance: 'wander distance', patrolWaitSeconds: 'wait at stops' };
const STAGE_TRIGGER_WORDS = { name: 'name', radius: 'radius m', hold: 'hold s', delay: 'delay s', chance: 'chance %', repeat: 'repeats every s', times: 'times', rally: 'rally m', everyone: 'every player needed', warn: 'warns players', red: 'message in red', hunt: 'enemies hunt the players', item: 'intel item', count: 'enemies down needed', tag: 'tag', facePlayer: 'enemies face the players', power: 'lights', message: 'message' };
const stageWord = v => v === true ? 'yes' : v === false ? 'no' : v === undefined || v === null || v === '' ? 'not set' : Array.isArray(v) ? (v.join(', ') || 'none') : String(v);
const stageM = v => (Math.round(v * 10) / 10).toString();
const stageList = v => (Array.isArray(v) ? v.filter(x => x && typeof x === 'object') : []);
const stageSame = (a, b) => JSON.stringify(a) === JSON.stringify(b);
// A scenario file (plan/1 or the plan/2 envelope), a mission record, or a live batch as the page sends it.
function stagePlan(o) {
  const b = o && typeof o === 'object' ? (o.mission && typeof o.mission === 'object' ? o.mission : o.plan && typeof o.plan === 'object' ? o.plan : o) : {};
  const s = b.settings && typeof b.settings === 'object' ? b.settings : b;
  return { spawns: stageList(b.markers || b.spawns), triggers: stageList(b.triggers), doors: stageList(b.doors), things: stageList(b.things), objectives: stageList(b.objectives), places: stageList(b.places), settings: s };
}
// The name of the place a position is in, from the scenario's own places (MissionPlan.PlaceAt): '' when none.
function stagePlaceIn(places, p) {
  if (!isVec(p)) return '';
  for (const q of places) {
    if (typeof q.name !== 'string' || !q.name) continue;
    if (q.area) { try { if (areaContains(q.area, p)) return q.name; } catch (e) { /* a broken shape names nothing */ } continue; }
    if (!isVec(q.pos)) continue;
    const band = Array.isArray(q.floor) && q.floor.length === 2 ? p[1] >= q.floor[0] && p[1] <= q.floor[1] : Math.abs(p[1] - q.pos[1]) <= 2;
    if (band && Math.hypot(p[0] - q.pos[0], p[2] - q.pos[2]) <= 3) return q.name;
  }
  return '';
}
// Pairs two lists: by id where the game gave ids, otherwise identical entries first and the rest in order.
function stagePair(before, after, byId) {
  const pairs = [], usedA = new Set(), usedB = new Set();
  if (byId) {
    const ids = new Map(); before.forEach((s, i) => { if (s.id > 0) ids.set(s.id, i); });
    after.forEach((s, j) => { if (s.id > 0 && ids.has(s.id)) { pairs.push([ids.get(s.id), j]); usedA.add(ids.get(s.id)); usedB.add(j); } });
  } else {
    const texts = new Map(); before.forEach((s, i) => { const k = JSON.stringify(s); if (!texts.has(k)) texts.set(k, []); texts.get(k).push(i); });
    after.forEach((s, j) => { const l = texts.get(JSON.stringify(s)); if (l && l.length) { const i = l.shift(); pairs.push([i, j]); usedA.add(i); usedB.add(j); } });
    const restA = before.map((s, i) => i).filter(i => !usedA.has(i)), restB = after.map((s, j) => j).filter(j => !usedB.has(j));
    for (let k = 0; k < Math.min(restA.length, restB.length); k++) { pairs.push([restA[k], restB[k]]); usedA.add(restA[k]); usedB.add(restB[k]); }
  }
  return { pairs, removed: before.map((s, i) => i).filter(i => !usedA.has(i)), added: after.map((s, j) => j).filter(j => !usedB.has(j)) };
}
// What changes from one setup to another, as lines { kind, ref, text } a host can read. Pure: it reads only its arguments.
// ctx.fired: ids of triggers that have already fired in the running mission.
function semanticDiff(beforeObj, afterObj, ctx) {
  const A = stagePlan(beforeObj), B = stagePlan(afterObj), fired = new Set((ctx && Array.isArray(ctx.fired) ? ctx.fired : []).map(v => v | 0)), out = [];
  const places = B.places.length ? B.places : A.places;
  const where = p => { const n = stagePlaceIn(places, p); return n ? 'in ' + n : isVec(p) ? 'at ' + Math.round(p[0]) + ', ' + Math.round(p[2]) : 'without a place'; };
  const trigOf = id => B.triggers.find(t => (t.id | 0) === id) || A.triggers.find(t => (t.id | 0) === id) || null;
  const trigName = id => { const t = trigOf(id); return (t && (t.when === 'zone' || t.when === 'hold') ? 'zone ' : 'trigger ') + id + (t && t.name ? ' "' + t.name + '"' : ''); };
  const Cap = s => s.charAt(0).toUpperCase() + s.slice(1);
  const add = (kind, ref, text) => out.push({ kind, ref, text });
  const firedNote = id => !fired.has(id) ? '' : (trigOf(id) && (trigOf(id).repeat || 0) > 0 ? ' (it has fired already; this counts from its next firing)' : ' (it has fired already and will not fire again in this mission)');

  // Enemies, and the enemies a trigger brings (reinforcements).
  const enemyName = (s, i) => 'Enemy ' + (i + 1) + (s.tag ? ' (' + s.tag + ')' : s.hvt ? ' (target person)' : s.vehicle ? ' (gun truck)' : '');
  const kindOf = (a, b) => ((b && b.trigger > 0) || (a && a.trigger > 0) ? 'reinforcement' : 'enemy');
  const sp = stagePair(A.spawns, B.spawns, A.spawns.some(s => s.id > 0) || B.spawns.some(s => s.id > 0));
  for (const [i, j] of sp.pairs) {
    const a = A.spawns[i], b = B.spawns[j], parts = [];
    if (isVec(a.pos) && isVec(b.pos)) { const d = Math.hypot(b.pos[0] - a.pos[0], b.pos[1] - a.pos[1], b.pos[2] - a.pos[2]); if (d >= 0.05) parts.push('moved ' + stageM(d) + ' m, now ' + where(b.pos)); }
    if (Math.abs(((((b.yaw || 0) - (a.yaw || 0)) % 360) + 540) % 360 - 180) >= 1) parts.push('turned to ' + Math.round(b.yaw || 0) + '°');
    if (!stageSame(a.patrol || [], b.patrol || [])) parts.push('patrol route now ' + (b.patrol || []).length + ' stops');
    if ((a.trigger | 0) !== (b.trigger | 0)) parts.push(b.trigger > 0 ? 'now arrives with ' + trigName(b.trigger) + firedNote(b.trigger) : 'now there from the start (no longer waits for ' + trigName(a.trigger) + ')');
    const blank = (k, v) => v === undefined || v === null || v === 0 || v === false || v === '' || (k === 'chance' && v === 100);
    for (const k of Object.keys(STAGE_SPAWN_WORDS)) if (!stageSame(a[k], b[k]) && !(blank(k, a[k]) && blank(k, b[k]))) {
      const v = x => (x === undefined ? undefined : k === 'group' ? (x | 0) + 1 : x);
      parts.push(STAGE_SPAWN_WORDS[k] + ' ' + stageWord(v(a[k])) + ' → ' + stageWord(v(b[k])));
    }
    if (parts.length) add(kindOf(a, b), 'enemy:' + (b.id > 0 ? b.id : j + 1), enemyName(b, j) + ' ' + where(b.pos) + ': ' + parts.join('; '));
  }
  for (const j of sp.added) { const b = B.spawns[j]; add(kindOf(null, b), 'enemy:+' + (j + 1), enemyName(b, j) + ' added ' + where(b.pos) + (b.trigger > 0 ? ', arrives with ' + trigName(b.trigger) + firedNote(b.trigger) : ', there from the start')); }
  for (const i of sp.removed) { const a = A.spawns[i]; add(kindOf(a, null), 'enemy:-' + (a.id > 0 ? a.id : i + 1), enemyName(a, i) + ' removed (was ' + where(a.pos) + (a.trigger > 0 ? ', arriving with ' + trigName(a.trigger) : '') + ')' + (a.live ? '; its enemy in the game goes too' : '')); }
  // How many enemies each trigger brings, before and after.
  const brings = list => { const m = new Map(); for (const s of list) if (s.trigger > 0) m.set(s.trigger | 0, (m.get(s.trigger | 0) || 0) + 1); return m; };
  const nA = brings(A.spawns), nB = brings(B.spawns);
  for (const id of [...new Set([...nA.keys(), ...nB.keys()])].sort((p, q) => p - q)) {
    const x = nA.get(id) || 0, y = nB.get(id) || 0;
    if (x !== y) add('reinforcement', 'brings:' + id, Cap(trigName(id)) + ' now brings ' + y + (y === 1 ? ' enemy' : ' enemies') + ' (was ' + x + ')' + firedNote(id));
  }

  // Triggers.
  const tA = new Map(A.triggers.map(t => [t.id | 0, t])), tB = new Map(B.triggers.map(t => [t.id | 0, t]));
  for (const [id, b] of tB) {
    const a = tA.get(id);
    if (!a) {
      add('trigger', 'trigger:' + id, Cap(trigName(id)) + ' added: waits for ' + (STAGE_WHEN[b.when] || b.when) + (b.delay > 0 ? ', then ' + stageM(b.delay) + ' s' : ''));
      if (b.extraction === 'open') add('extraction', 'trigger:' + id, Cap(trigName(id)) + ' opens the extraction');
      if (b.end) add('rule', 'trigger:' + id, Cap(trigName(id)) + ' ends the mission as ' + b.end);
      if (b.respawn) add('respawn', 'trigger:' + id, Cap(trigName(id)) + ' calls a wave of dead players back');
      continue;
    }
    const parts = [];
    if (a.when !== b.when) parts.push('now waits for ' + (STAGE_WHEN[b.when] || b.when) + ' (was ' + (STAGE_WHEN[a.when] || a.when) + ')');
    if (isVec(a.pos) && isVec(b.pos)) { const d = Math.hypot(b.pos[0] - a.pos[0], b.pos[1] - a.pos[1], b.pos[2] - a.pos[2]); if (d >= 0.05) parts.push('moved ' + stageM(d) + ' m, now ' + where(b.pos)); }
    if (!stageSame(a.area || null, b.area || null)) parts.push(!a.area ? 'now has a painted area' : !b.area ? 'back to a circle' : 'its painted area changed');
    if ((a.after | 0) !== (b.after | 0)) parts.push(b.after ? 'now waits for ' + trigName(b.after) : 'no longer waits for another trigger');
    if (!stageSame(a.of || [], b.of || [])) parts.push('waits for triggers ' + stageWord(b.of || []) + ' (was ' + stageWord(a.of || []) + ')');
    if (!stageSame(a.doors || [], b.doors || [])) parts.push('its door actions changed (' + (b.doors || []).length + ' doors)');
    const blank = (k, v) => v === undefined || v === null || v === 0 || v === false || v === '' || (k === 'chance' && v === 100) || (k === 'facePlayer' && v === true);
    for (const k of Object.keys(STAGE_TRIGGER_WORDS)) if (!stageSame(a[k], b[k]) && !(blank(k, a[k]) && blank(k, b[k]))) parts.push(STAGE_TRIGGER_WORDS[k] + ' ' + stageWord(a[k]) + ' → ' + stageWord(b[k]));
    if (parts.length) add('trigger', 'trigger:' + id, Cap(trigName(id)) + ': ' + parts.join('; ') + firedNote(id));
    if ((a.extraction === 'open') !== (b.extraction === 'open')) add('extraction', 'trigger:' + id, Cap(trigName(id)) + (b.extraction === 'open' ? ' now opens the extraction' : ' no longer opens the extraction') + firedNote(id));
    if ((a.end || '') !== (b.end || '')) add('rule', 'trigger:' + id, Cap(trigName(id)) + (b.end ? ' now ends the mission as ' + b.end : ' no longer ends the mission') + firedNote(id));
    if (!!a.respawn !== !!b.respawn) add('respawn', 'trigger:' + id, Cap(trigName(id)) + (b.respawn ? ' now calls a wave of dead players back' : ' no longer calls dead players back') + firedNote(id));
  }
  for (const [id, a] of tA) if (!tB.has(id)) add('trigger', 'trigger:' + id, 'Trigger ' + id + (a.name ? ' "' + a.name + '"' : '') + ' removed' + (nA.get(id) ? '; the ' + nA.get(id) + (nA.get(id) === 1 ? ' enemy' : ' enemies') + ' it brought no longer wait for it' : '') + (a.extraction === 'open' ? '; it no longer opens the extraction' : ''));

  // Mission rules: extraction, reinforcement waves of dead players, the rest.
  const S = A.settings || {}, T = B.settings || {};
  const exA = S.extraction && S.extraction.after | 0, exB = T.extraction && T.extraction.after | 0;
  if ((exA || 0) !== (exB || 0)) add('extraction', 'extraction', exB ? 'Extraction now waits for ' + trigName(exB) + (exA ? ' (was ' + trigName(exA) + ')' : ' (was the mission\'s own rule)') : 'Extraction no longer waits for ' + trigName(exA) + ': the mission\'s own rule');
  const wave = r => r && r.after > 0 ? 'after ' + stageM(r.after) + ' s' + (r.times > 0 ? ', ' + r.times + ' times' : ', with no limit') : 'off';
  if (wave(S.respawn) !== wave(T.respawn)) add('respawn', 'respawn', 'Reinforcement waves of dead players: ' + wave(T.respawn) + ' (was ' + wave(S.respawn) + ')');
  const rules = [['insertion', 'Insertion for every player'], ['power', 'Lights at the start'], ['skill', 'Skill of every enemy'], ['nvg', 'Night vision for every enemy'], ['gameMaster', 'Run by a Game Master']];
  for (const [k, label] of rules) if (!stageSame(S[k], T[k])) add('rule', k, label + ': ' + stageWord(T[k]) + ' (was ' + stageWord(S[k]) + ')');
  const wA = S.weather || {}, wB = T.weather || {};
  for (const k of ['hour', 'rain', 'sandstorm']) if (!stageSame(wA[k], wB[k])) add('rule', 'weather', 'Weather ' + k + ': ' + stageWord(wB[k]) + ' (was ' + stageWord(wA[k]) + ')');
  if (!stageSame(S.briefing || [], T.briefing || [])) add('rule', 'briefing', 'Briefing now ' + (T.briefing || []).length + ' lines' + ((T.briefing || []).length ? ': "' + T.briefing[0] + '"' + (T.briefing.length > 1 ? ' …' : '') : ''));

  // Doors, by hinge.
  const doorKey = d => isVec(d.pos) ? d.pos.slice(0, 3).map(v => Math.round(v * 10)).join(',') : '';
  const dA = new Map(A.doors.map(d => [doorKey(d), d])), dB = new Map(B.doors.map(d => [doorKey(d), d]));
  const lockWord = v => v === true ? 'locked' : v === false ? 'unlocked' : 'left to the game';
  const doorName = d => 'Door' + (d.name ? ' "' + d.name + '"' : '') + ' ' + where(d.pos);
  for (const k of new Set([...dA.keys(), ...dB.keys()])) {
    const a = dA.get(k) || {}, b = dB.get(k) || {}, parts = [];
    if (a.locked !== b.locked) parts.push(lockWord(b.locked) + ' (was ' + lockWord(a.locked) + ')');
    if (a.open !== b.open) parts.push(b.open === true ? 'open' : b.open === false ? 'shut' : 'opening left to the game');
    if (a.enemiesCanOpen !== b.enemiesCanOpen) parts.push(b.enemiesCanOpen === false ? 'enemies kept out' : b.enemiesCanOpen === true ? 'enemies may open it' : 'enemies left to the game');
    if ((a.chance || 100) !== (b.chance || 100)) parts.push('chance ' + (b.chance || 100) + '%');
    if (parts.length) add('door', 'door:' + k, doorName(dB.has(k) ? b : a) + ': ' + parts.join(', '));
  }

  // Things, intel, places.
  const th = stagePair(A.things, B.things, false);
  for (const [i, j] of th.pairs) {
    const a = A.things[i], b = B.things[j], parts = [];
    if (isVec(a.pos) && isVec(b.pos) && Math.hypot(b.pos[0] - a.pos[0], b.pos[1] - a.pos[1], b.pos[2] - a.pos[2]) >= 0.05) parts.push('moved to ' + where(b.pos).replace(/^(in|at) /, ''));
    if (a.name !== b.name) parts.push('now ' + b.name);
    if ((a.trigger | 0) !== (b.trigger | 0)) parts.push(b.trigger ? 'appears with ' + trigName(b.trigger) : 'there from the start');
    if ((a.chance || 100) !== (b.chance || 100)) parts.push('chance ' + (b.chance || 100) + '%');
    if (parts.length) add('thing', 'thing:' + (j + 1), 'The ' + String(a.name).trim() + ': ' + parts.join(', '));
  }
  for (const j of th.added) add('thing', 'thing:+' + (j + 1), 'The ' + String(B.things[j].name).trim() + ' added ' + where(B.things[j].pos));
  for (const i of th.removed) add('thing', 'thing:-' + (i + 1), 'The ' + String(A.things[i].name).trim() + ' removed (was ' + where(A.things[i].pos) + ')');
  const oA = new Map(A.objectives.map(o => [o.name, o])), oB = new Map(B.objectives.map(o => [o.name, o]));
  for (const [n, b] of oB) { const a = oA.get(n); if (!a) add('intel', 'intel:' + n, 'Intel "' + n + '" placed ' + where(b.pos)); else if (isVec(a.pos) && isVec(b.pos) && Math.hypot(b.pos[0] - a.pos[0], b.pos[1] - a.pos[1], b.pos[2] - a.pos[2]) >= 0.05) add('intel', 'intel:' + n, 'Intel "' + n + '" moved to ' + where(b.pos).replace(/^(in|at) /, '')); }
  for (const n of oA.keys()) if (!oB.has(n)) add('intel', 'intel:' + n, 'Intel "' + n + '" back where the game puts it');
  const pA = new Map(A.places.map(p => [p.id | 0, p])), pB = new Map(B.places.map(p => [p.id | 0, p]));
  for (const [id, b] of pB) { const a = pA.get(id); if (!a) add('place', 'place:' + id, 'Place "' + b.name + '" added'); else if (!stageSame(a, b)) add('place', 'place:' + id, 'Place "' + b.name + '"' + (a.name !== b.name ? ' (was "' + a.name + '")' : '') + ' changed'); }
  for (const [id, a] of pA) if (!pB.has(id)) add('place', 'place:' + id, 'Place "' + a.name + '" removed');

  out.sort((p, q) => STAGE_KIND_ORDER.indexOf(p.kind) - STAGE_KIND_ORDER.indexOf(q.kind));      // stable: the order within a kind stays
  if (out.length > STAGE_LIMITS.lines) { const n = out.length - STAGE_LIMITS.lines; out.length = STAGE_LIMITS.lines; out.push({ kind: 'more', ref: '', text: n + ' more changes not listed' }); }
  return out;
}

// ---- staging rules (pure)
// What blocks a batch: a refusal of the scenario check, and Mission check problems the running mission did not already have.
// refusedName: a file that was not loaded (its problems are about that file, not about this draft).
function stageVerdict(findings, checks, already, refusedName) {
  const known = new Set(already || []), out = [];
  for (const f of findings || []) if (f.level === 'error') out.push('The scenario check refuses it: ' + (f.path ? f.path + ': ' : '') + f.text + '.');
  for (const f of checks || []) {
    if (f.level !== 'problem' || (refusedName && f.text.startsWith(refusedName + ' was not loaded.'))) continue;
    const text = 'Mission check: ' + f.text;
    if (!known.has(text)) out.push(text);
  }
  return out;
}
// The command number for a batch: the same batch keeps its number until the game has answered it, so a retry is never done twice.
function stageCommandFor(out, key, make) { return out && out.key === key && !out.done ? out : { command: make(), key, done: false }; }
// Whether a send that failed may still have been carried out (no answer, or begun and not finished): then the number is kept.
// Refused, expired, too busy or failed means it was not carried out, and the next try is a new batch.
function stageKeepsCommand(err) {
  const st = err && err.status, res = err && err.result;
  if (res === 'pending') return true;
  if (st === 409 || st === 429 || st === 503 || st === 500 || st === 400 || res === 'expired' || res === 'stale' || res === 'failed') return false;
  return true;
}
// Adds a revision to the page's own store (used when the page is not linked to the game). The same text twice is the same save;
// a different text under a number already taken is refused. Bounded: 30 per scenario, and the oldest of all go first when the
// store would pass about 2 million characters.
function stageRevisionAdd(all, entry, limits) {
  const lim = Object.assign({}, STAGE_LIMITS, limits || {}), store = JSON.parse(JSON.stringify(all && typeof all === 'object' ? all : {}));
  const list = Array.isArray(store[entry.id]) ? store[entry.id] : [];
  const was = list.find(r => r && r.n === entry.n);
  if (was) return was.hash === entry.hash ? { all: store, entry: was, again: true } : { all: store, error: 'revision ' + entry.n + ' of this scenario already exists with other content; nothing was overwritten' };
  list.push(entry); list.sort((p, q) => q.n - p.n); store[entry.id] = list.slice(0, lim.revisions);
  for (let guard = 0; JSON.stringify(store).length > lim.revisionChars && guard < 1000; guard++) {
    let oldest = null;
    for (const id of Object.keys(store)) for (const r of store[id]) if (r !== entry && (!oldest || String(r.saved) < String(oldest.r.saved))) oldest = { id, r };
    if (!oldest) break;
    store[oldest.id] = store[oldest.id].filter(r => r !== oldest.r); if (!store[oldest.id].length) delete store[oldest.id];
  }
  return { all: store, entry };
}
// A scenario text's fingerprint, as ScenarioFile.Hash: FNV-1a 64 over its UTF-8 bytes with line ends as \n.
const textHash = text => (text ? mapPrintOf(new TextEncoder().encode(String(text).replace(/\r\n/g, '\n'))) : '');

// ---- staging on the page
// The setup as the page sends it to the game (the /api/markers batch), plus the places that name where things are.
function stageSnapshot() {
  return { markers: state.enemies.map(e => Object.assign({ id: e.id || 0 }, spawnOf(e), e.live ? { live: true } : {})), triggers: zonesOut(), objectives: objectivesOut(), doors: doorsOut(), things: thingsOut(), settings: settingsOut(), places: placesOut() };
}
function stageBody() {
  const snap = stageSnapshot();
  return { spawnNow: $('spawnNow').checked === true, triggers: snap.triggers, objectives: snap.objectives, doors: snap.doors, things: snap.things, settings: snap.settings, markers: snap.markers.map(m => { const o = Object.assign({}, m); delete o.live; return o; }) };
}
function stageCurrentProblems() {
  let obj = null, checks = [];
  try { obj = scenarioObject(); } catch (e) { return ['The draft could not be written as a scenario: ' + (e && e.message ? e.message : e)]; }
  try { checks = missionCheck(); } catch (e) { /* the scenario check still counts */ }
  return stageVerdict(scenarioProblems(obj), checks, [], state.refused ? state.refused.name : '');
}
function stageBlockers() { return stageCurrentProblems().filter(t => stage.baseProblems.indexOf(t) < 0); }
function stageHolds() { return live.on && stage.mode === 'draft'; }
// The game's own state has just been taken over: it is what a draft starts from.
function stageFollowGame() {
  stage.base = live.mission ? stageSnapshot() : null; stage.baseKey = stage.base ? JSON.stringify(stage.base) : '';
  stage.baseRev = live.rev; stage.baseSession = live.mission ? live.session : null; stage.dirty = false; live.staged = [];
  if (stage.mode === 'draft' && stage.base) stage.baseProblems = stageCurrentProblems();
  stageSync();
}
// An edit in Draft mode: listed, held on the page, not sent.
function stageChanged() {
  if (!stage.base) { live.staged = []; stage.dirty = false; stageSync(); return; }
  const snap = stageSnapshot();
  stage.dirty = JSON.stringify(snap) !== stage.baseKey;
  live.staged = stage.dirty ? semanticDiff(stage.base, snap, { fired: state.fired }) : [];
  if (stage.dirty && !live.staged.length) live.staged = [{ kind: 'enemy', ref: '', text: 'Small changes (rounding, facing) not listed' }];
  live.pending = stage.dirty;          // a staged change is a change waiting to be sent: the game's list must not overwrite it
  stageSync();
}
// Every poll of the game's state. A draft made in a mission that has ended is kept as a separate draft, never sent to the next one.
function stageSeen(s) {
  const session = s && s.mission && typeof s.session === 'string' ? s.session : null;
  stage.seenRev = s && s.mission ? s.rev : null;
  // The running scenario's identity, so revisions saved here file under it. Taken over when it changes, not on every poll,
  // so a revision number saved on this page is not set back by the mission's older one.
  const ident = s && s.mission && typeof s.scenarioId === 'string' && s.scenarioId ? s.scenarioId + ':' + (s.revision | 0) : '';
  if (ident && ident !== stage.gameScenario && !stage.separate) { stage.gameScenario = ident; state.scenarioId = s.scenarioId; state.revision = Math.max(0, s.revision | 0); stageSyncRevisions(); }
  if (stage.dirty && stage.baseSession && session !== stage.baseSession) {
    stageKeepAside('ended:' + stage.baseSession, 'Staged changes for a mission that has ended', null);
    stage.dirty = false; live.staged = []; live.pending = false; live.rev = null; stage.out = null; stage.preview = false;
    say('The mission changed, so the staged changes were not applied. They are kept as a separate draft under Revisions (Mission tab).');
    stageSync(); return;
  }
  if (stageHolds() && stage.dirty) live.pending = true;
}
function stageSetMode(mode) {
  if (mode === stage.mode) return;
  if (mode === 'live' && stage.dirty) return say('Apply or discard the ' + live.staged.length + ' staged changes first: Live mode would send them at once.');
  stage.mode = mode; stage.preview = false;
  try { localStorage.setItem(STAGE_MODE_KEY, mode); } catch (e) { /* storage unavailable */ }
  if (mode === 'draft') { if (live.mission && !live.pending && !live.sending) stageFollowGame(); }
  else { live.pending = false; live.staged = []; stage.dirty = false; live.rev = null; }
  showRepair();
  stageSync();
  say(mode === 'draft' ? 'Draft mode: edits stay on this page until you press Apply changes.' : 'Live mode: every edit is carried out in the game at once.');
}
function stageDiscard() {
  if (!stage.dirty) return;
  stage.dirty = false; live.staged = []; live.pending = false; live.rev = null; stage.preview = false;    // the next poll brings the game's state back
  if (stage.separate) { stage.separate = null; stageSyncRevisions(); }
  stageSync(); say('The draft was discarded. The page shows the game again.');
}
// Ask the game what became of a batch whose answer never arrived. null when it does not know the number.
async function stageLookup(command) {
  try { const r = await fetch('/api/command/' + encodeURIComponent(command), { cache: 'no-store' }); if (r.status === 404) return null; const k = await r.json(); return k && k.status && k.status !== 'unknown' ? k : null; }
  catch (e) { return undefined; }
}
async function applyStaged() {
  if (!live.on || !live.mission) { say('No mission is running, so there is nothing to apply the draft to.'); return false; }
  if (live.sending) { say('A change is on its way to the game. Wait for its answer.'); return false; }
  // An earlier try that got no answer: ask the game first. If it was carried out, it is not sent again.
  if (stage.out && !stage.out.done) {
    const k = await stageLookup(stage.out.command);
    if (k === undefined) { say('The game is not answering, so nothing was sent. Try again in a moment.'); return false; }
    if (k && !k.done) { say('The game has begun the last batch and has not finished. It was not sent again.'); return false; }
    if (k && k.done) { stage.out.done = true; await stageAnswered(stage.out, k, null); return true; }
  }
  if (!stage.dirty) { say('Nothing is staged.'); return false; }
  if (stage.baseSession && live.session !== stage.baseSession) { say('This draft was made for an earlier mission, so it was not applied.'); return false; }
  const blockers = stageBlockers();
  if (blockers.length) { stage.preview = true; stageSync(); say('Not applied: ' + blockers[0] + (blockers.length > 1 ? ' (and ' + (blockers.length - 1) + ' more; see the preview)' : '')); return false; }
  const body = stageBody(), key = JSON.stringify(body);
  stage.out = Object.assign(stageCommandFor(stage.out, key, newCommand), { lines: live.staged.slice() });
  const out = stage.out;
  live.sending = true; stageSync();
  try {
    const s = await api('/api/markers', body, out.command);
    out.done = true; live.sending = false;
    if (!s || s.mission === false) { stage.receipt = { command: out.command, lines: out.lines, result: null, error: 'No mission is running any more. Nothing was applied.', retry: false, at: Date.now() }; stageSync(); return false; }
    let res = s.result || null;
    if (!res) { const k = await stageLookup(out.command); res = k || null; }
    await stageAnswered(out, res, s);
    return true;
  } catch (err) {
    live.sending = false;
    const keep = stageKeepsCommand(err);
    out.done = !keep;
    stage.receipt = { command: out.command, lines: out.lines, result: null, error: liveTrouble(err), retry: keep, at: Date.now() };
    say(liveTrouble(err) + (keep ? ' Apply changes asks the game about the same batch again; it is never carried out twice.' : ''));
    stageSync();
    return false;
  }
}
// The game has answered a batch: show what became of each part and take the game's state over.
async function stageAnswered(out, res, s) {
  stage.receipt = { command: out.command, lines: out.lines || [], result: res, error: '', retry: false, at: Date.now() };
  stage.dirty = false; live.staged = []; live.pending = false; live.rev = null; stage.preview = false;
  if (stage.separate) { stage.separate = null; stageSyncRevisions(); }
  if (res && res.command && Array.isArray(res.ops) && res.ops.some(o => o.status === 'pending')) followCommand(res.command);
  try { await applyLive(s && s.mission ? s : await api('/api/state')); } catch (e) { /* the next poll takes the game's state over */ }
  stageSync();
  say(res && Array.isArray(res.ops) && res.ops.length ? 'The game answered: ' + opsSummary(res) + '. The page shows the game\'s own state now.' : 'The batch was sent. The page shows the game\'s own state now.');
}
// Later news of a batch (spawns that were on their way).
function stageReceipt(command, k) {
  if (!stage.receipt || stage.receipt.command !== command || !k || !Array.isArray(k.ops)) return;
  stage.receipt.result = k; stageSync();
}
const STAGE_STATUS = { applied: 'applied', failed: 'failed', pending: 'on its way', unconfirmed: 'requested' };
function stageOpText(o) {
  const what = OP_WORDS[o.op] || o.op;
  const who = MARKER_OPS.indexOf(o.op) >= 0 ? (o.at >= 0 ? 'enemy ' + (o.at + 1) : o.ref ? 'marker ' + o.ref : 'an enemy') : o.ref ? o.op + ' ' + o.ref : o.op;
  return what.charAt(0).toUpperCase() + what.slice(1) + ': ' + who + (o.said ? ' — ' + o.said : '');
}
function stageRows(ul, rows) {
  ul.textContent = '';
  for (const r of rows) {
    const li = document.createElement('li'), k = document.createElement('span'), t = document.createElement('span');
    k.className = 'kind ' + (r.cls || ''); k.textContent = r.kind; t.textContent = r.text;
    li.append(k, t); ul.appendChild(li);
  }
}
function stagePara(box, text) { const p = document.createElement('p'); p.textContent = text; box.appendChild(p); return p; }
function stageSync() {
  if (!$('stageBox')) return;
  const draft = stage.mode === 'draft';
  $('stageLive').setAttribute('aria-pressed', String(!draft)); $('stageDraft').setAttribute('aria-pressed', String(draft));
  $('stageLive').disabled = draft && stage.dirty;
  $('stageCount').textContent = draft && stage.dirty ? live.staged.length + ' staged' : '';
  $('stageMode').textContent = !draft ? 'Live: every edit is carried out in the game at once.'
    : !live.mission ? 'Draft: no mission is running yet.'
    : live.sending ? 'Draft: the batch is on its way to the game...'
    : stage.dirty ? 'Draft: these changes are on this page only. The game has not changed.'
    : 'Draft: the page follows the game until you edit something; edits then wait here until you apply them.';
  const lines = draft && stage.dirty ? live.staged : [];
  const shown = lines.slice(0, STAGE_LIMITS.shown).map(l => ({ kind: l.kind, text: l.text }));
  if (lines.length > STAGE_LIMITS.shown) shown.push({ kind: 'more', text: (lines.length - STAGE_LIMITS.shown) + ' more: Preview changes lists them all' });
  stageRows($('stageList'), shown);
  $('stageButtons').hidden = !draft || (!stage.dirty && !(stage.out && !stage.out.done));
  $('stageApply').disabled = live.sending || !live.mission;
  $('stageApply').textContent = stage.out && !stage.out.done && !stage.dirty ? 'Ask about the last batch' : 'Apply changes';
  $('stageDiscard').disabled = !stage.dirty;
  // The preview: every line, then what blocks the batch and what cannot be taken back.
  const check = $('stageCheck'); check.textContent = '';
  if (draft && stage.preview && stage.dirty) {
    stagePara(check, 'Preview: what Apply changes would do in the running game.');
    const ul = document.createElement('ul'); check.appendChild(ul);
    const rows = live.staged.map(l => ({ kind: l.kind, text: l.text }));
    const blockers = stageBlockers();
    for (const b of blockers) rows.push({ kind: 'blocked', cls: 'block', text: b });
    for (const b of stage.baseProblems) rows.push({ kind: 'already', cls: 'warn', text: b + ' (the running mission has this already; it does not block)' });
    if (stage.seenRev !== null && stage.baseRev !== null && stage.seenRev !== stage.baseRev) rows.push({ kind: 'note', cls: 'warn', text: 'The game\'s list has changed since this draft began (enemies spawned, or another page edited it). Apply sends this whole draft: a marker the game has and the draft does not is removed.' });
    stageRows(ul, rows);
    stagePara(check, blockers.length ? 'Apply is blocked until these are fixed.' : 'Once applied, the game cannot undo it: Undo changes the draft only, and never brings back a death, a finished mission or collected intel.');
  }
  // The receipt of the last batch, part by part; failed parts stay listed until the next batch.
  const rec = $('stageReceipt'); rec.textContent = '';
  const r = stage.receipt;
  if (r) {
    const res = r.result, ops = res && Array.isArray(res.ops) ? res.ops : [];
    stagePara(rec, 'Last batch (' + r.command + ', ' + new Date(r.at).toLocaleTimeString() + '): ' + (r.error ? r.error : res && res.status ? res.status + (ops.length ? ': ' + opsSummary(res) : '') : 'sent; the game gave no part-by-part answer') + '.');
    if (ops.length) { const ul = document.createElement('ul'); rec.appendChild(ul); stageRows(ul, ops.map(o => ({ kind: STAGE_STATUS[o.status] || o.status, cls: o.status, text: stageOpText(o) }))); }
    if (ops.some(o => o.status === 'failed')) stagePara(rec, 'Failed parts are not in the game. The page shows what the game has; make them again to stage them once more.');
    if (r.retry) stagePara(rec, 'Its outcome is not known yet. Apply changes asks the game about this same batch; it is never carried out twice.');
  }
}
$('stageLive').addEventListener('click', () => stageSetMode('live'));
$('stageDraft').addEventListener('click', () => stageSetMode('draft'));
$('stagePreview').addEventListener('click', () => { stage.preview = !stage.preview; stageSync(); });
$('stageApply').addEventListener('click', () => applyStaged());
$('stageDiscard').addEventListener('click', stageDiscard);

// ---- separate drafts: an earlier revision, a run's resolved plan, or staged changes of a mission that ended
function separateDrafts() { try { const l = JSON.parse(localStorage.getItem(DRAFTS_KEY) || '[]'); return Array.isArray(l) ? l.filter(d => d && d.key && d.scenario) : []; } catch (e) { return []; } }
function stageKeepAside(key, label, run) {
  let scenario; try { scenario = Object.assign(scenarioObject(), { name: state.title }); } catch (e) { return; }
  const list = [{ key, label, run: run || null, saved: new Date().toISOString(), scenario }].concat(separateDrafts().filter(d => d.key !== key)).slice(0, STAGE_LIMITS.drafts);
  try { localStorage.setItem(DRAFTS_KEY, JSON.stringify(list)); } catch (e) { /* storage full: the draft stays on the page */ }
}
// changed(): offline, a separate draft is saved under its own key, so the author's own draft is never overwritten by it.
function stageKeepSeparate() {
  if (!stage.separate) return false;
  clearTimeout(stage.separateTimer);
  const s = stage.separate;
  stage.separateTimer = setTimeout(() => { if (stage.separate === s) stageKeepAside(s.key, s.label, s.run); }, 400);
  return true;
}
function stageKeepMine() {
  clearTimeout(draftTimer);          // an edit still waiting to be saved belongs to the author's own draft
  try { if (state.example) localStorage.removeItem(DRAFT_KEY); else localStorage.setItem(DRAFT_KEY, JSON.stringify(Object.assign(scenarioObject(), { name: state.title }))); } catch (e) { /* storage unavailable */ }
}
function openSeparate(key, label, obj, run) {
  if (live.on && stage.dirty) { say('Apply or discard the staged changes first, so that they are not lost.'); return false; }
  // Between missions the linked page shows nothing and is cleared on the next look at the game.
  if (live.on && !live.mission) { say('No mission is running, so the linked page has nothing to hold a draft against. Host a mission, or open tools/editor/index.html without the game.'); return false; }
  const kept = separateDrafts().find(d => d.key === key);
  if (!stage.separate && !live.on) stageKeepMine();
  if (live.on && stage.mode !== 'draft') stageSetMode('draft');      // linked to a game it can only be a draft: nothing reaches the game before Apply
  const text = loadScenario(kept ? kept.scenario : obj, label);
  if (state.refused) { say(text); return false; }
  stage.separate = { key, label, run: run || (kept && kept.run) || null };
  if (!live.on) stageKeepAside(key, label, stage.separate.run);
  changed(); stageSyncRevisions();
  say('Opened ' + label + ' as a separate draft' + (kept ? ' (with your edits to it)' : '') + '. ' + (live.on ? 'Nothing reaches the game before Apply changes.' : 'Your own draft is kept; Back to my draft returns to it.'));
  return true;
}
function backToMine() {
  if (!stage.separate) return;
  stage.separate = null;
  if (live.on) { stage.dirty = false; live.staged = []; live.pending = false; live.rev = null; stage.preview = false; stageSync(); stageSyncRevisions(); say('Back to the running game: the page follows it again. The other draft can be opened again from Revisions.'); return; }
  let o = null; try { o = JSON.parse(localStorage.getItem(DRAFT_KEY) || 'null'); } catch (e) { /* storage unavailable */ }
  const body = o && (o.mission || o);
  if (body && Array.isArray(body.spawns)) say(loadScenario(o, o.name || body.name || 'draft') + ' Back to your own draft.');
  else say('You had no draft of your own. This one stays on the page and is your draft from now on.');
  changed(); stageSyncRevisions();
}

// ---- revisions: the author's explicit saves, with a note, tied to the scenario text's fingerprint
function localRevisions() { try { const o = JSON.parse(localStorage.getItem(REVISIONS_KEY) || '{}'); return o && typeof o === 'object' && !Array.isArray(o) ? o : {}; } catch (e) { return {}; } }
async function listRevisions() {
  const id = state.scenarioId;
  let list = [];
  if (id && live.on) {
    try { const o = await api('/api/revisions/' + encodeURIComponent(id)); list = o && Array.isArray(o.revisions) ? o.revisions : []; }
    catch (e) { say('The game did not answer, so the revisions are not listed.'); }
  } else if (id) list = (localRevisions()[id] || []).map(r => ({ n: r.n, note: r.note, saved: r.saved, hash: r.hash, runs: [] }));
  stage.revisions = { id, list };
  stageSyncRevisions();
  return list;
}
async function saveRevision() {
  if (state.example) return say('Open a map or a scenario first: the example compound is not saved as a revision.');
  if (stage.separate) return say('This is a separate draft. Go back to your own draft first, or save it as a scenario file.');
  if (!state.scenarioId) state.scenarioId = newScenarioId();
  const list = await listRevisions();
  const was = state.revision, n = Math.max(was, ...list.map(r => r.n | 0)) + 1;
  const note = String($('revNote').value || '').replace(/\s+/g, ' ').trim().slice(0, 200);
  state.revision = n;
  const text = scenarioText(), entry = { schema: REVISION_SCHEMA, id: state.scenarioId, n, note, saved: new Date().toISOString().replace(/\.\d+Z$/, 'Z'), hash: textHash(text), scenario: text };
  try {
    if (live.on) await api('/api/revisions', { text, note });
    else {
      const got = stageRevisionAdd(localRevisions(), entry);
      if (got.error) throw new Error(got.error);
      localStorage.setItem(REVISIONS_KEY, JSON.stringify(got.all));
    }
  } catch (e) { state.revision = was; say('The revision was not saved: ' + (e && e.message ? e.message : 'the page could not store it') + '.'); return false; }
  $('revNote').value = '';
  if (!live.on) stageKeepMine();       // the draft remembers its revision number
  syncSide();
  say('Saved revision ' + n + (note ? ' ("' + note + '")' : '') + (live.on ? ' in BepInEx/MissionLab/revisions.' : ' in this browser. Opened from the game, the page keeps revisions in BepInEx/MissionLab/revisions.'));
  await listRevisions();
  return true;
}
async function revisionEntry(n) {
  const id = stage.revisions && stage.revisions.id;
  if (!id) return null;
  if (!live.on) return (localRevisions()[id] || []).find(r => r.n === n) || null;
  try { const o = await api('/api/revision/' + encodeURIComponent(id) + '/' + n); return o && o.schema === REVISION_SCHEMA ? o : null; } catch (e) { return null; }
}
async function openRevision(n) {
  const e = await revisionEntry(n); let obj = null;
  try { obj = e ? JSON.parse(e.scenario) : null; } catch (err) { /* reported below */ }
  if (!obj) return say('Revision ' + n + ' could not be read.');
  return openSeparate('rev:' + e.id + ':' + n, 'revision ' + n + (e.note ? ' ("' + e.note + '")' : ''), obj, null);
}
async function compareRevision(n) {
  const e = await revisionEntry(n); let obj = null;
  try { obj = e ? JSON.parse(e.scenario) : null; } catch (err) { /* reported below */ }
  if (!obj) return say('Revision ' + n + ' could not be read.');
  stage.diff = { n, lines: semanticDiff(obj, scenarioObject(), null) }; stageSyncRevisions();
}
// A run record: its resolved plan opens as a separate draft, and its inputs (roll, players) and versions stay with it.
async function openRun(file) {
  let rec = null;
  try { rec = await (await fetch('/api/runfile/' + encodeURIComponent(file), { cache: 'no-store' })).json(); } catch (e) { /* reported below */ }
  if (!rec || !rec.resolved || typeof rec.resolved !== 'object') return say('That run record holds no resolved plan: the mission was refused, or the record is from an older version.');
  const run = { file, startedUtc: rec.startedUtc || '', roll: rec.roll, players: rec.players, scenario: rec.scenario || '', authoredHash: rec.authoredHash || '', scenarioId: rec.scenarioId || '', revision: rec.revision || 0,
    plugin: rec.plugin || '', game: rec.game || '', scene: rec.scene || '', navFile: rec.navFile || '', outcome: rec.outcome || '', reason: rec.reason || '', leftOut: rec.leftOut || null, end: rec.end || null };
  return openSeparate('run:' + file, 'the run of ' + (run.startedUtc ? new Date(run.startedUtc).toLocaleString() : file), rec.resolved, run);
}
function stageRunRows(run) {
  const lo = run.leftOut || {}, rows = [];
  rows.push('Resolved plan: the scenario as it stood after the dice. Roll ' + run.roll + ', ' + run.players + (run.players === 1 ? ' player' : ' players') + '.');
  rows.push('Scenario ' + (run.scenario || '?') + (run.revision ? ', revision ' + run.revision : '') + (run.authoredHash ? ' (' + run.authoredHash.replace(/^[a-z0-9]+:/, '').slice(0, 8) + ')' : ''));
  rows.push('Map ' + (run.navFile || '?') + (run.scene ? ', ' + run.scene : '') + '. MissionLab ' + (run.plugin || '?') + (run.plugin && run.plugin !== EDITOR_VERSION ? ' (this page is ' + EDITOR_VERSION + ')' : '') + ', game ' + (run.game || '?') + '.');
  const gone = (Array.isArray(lo.spawns) ? lo.spawns.length : 0), trig = Array.isArray(lo.triggersOff) ? lo.triggersOff.length : 0;
  if (gone || trig || lo.doors || lo.things) rows.push('Left out by the dice: ' + gone + ' enemies, ' + trig + ' triggers, ' + (lo.doors | 0) + ' doors, ' + (lo.things | 0) + ' things.');
  rows.push('Outcome: ' + (run.outcome || '?') + (run.end && run.end.result ? ', ended ' + run.end.result : '') + (run.reason ? ' (' + run.reason + ')' : '') + '.');
  return rows;
}
function stageSyncRevisions() {
  if (!$('revBox')) return;
  const id = state.scenarioId, r = stage.revisions && stage.revisions.id === id ? stage.revisions : null;
  $('revWhere').textContent = (live.on ? 'Saved in BepInEx/MissionLab/revisions, with the run recordings that played each one.' : 'Saved in this browser (the newest ' + STAGE_LIMITS.revisions + ' of each scenario). Run recordings are linked when the page is opened from the game.')
    + (id ? '' : ' A scenario gets its identity on its first save.');
  $('revCount').textContent = r && r.list.length ? String(r.list.length) : '';
  const open = stage.separate;
  $('revOpen').hidden = !open;
  $('revOpenText').textContent = open ? 'Open as a separate draft: ' + open.label + '.' + (live.on ? ' Nothing reaches the game before Apply changes.' : ' Your own draft is kept.') : '';
  const runBox = $('revRun'); runBox.textContent = ''; runBox.hidden = !(open && open.run);
  if (open && open.run) for (const t of stageRunRows(open.run)) { const li = document.createElement('li'); li.textContent = t; runBox.appendChild(li); }
  $('revRepeat').hidden = !(open && open.run && live.on);
  $('revSave').disabled = !!open;
  const ul = $('revList'); ul.textContent = '';
  const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  for (const rev of r ? r.list : []) {
    const li = mk('li'), head = mk('b', { textContent: 'Revision ' + rev.n + (rev.note ? ': ' + rev.note : '') });
    const meta = mk('span', { className: 'meta', textContent: (rev.saved ? new Date(rev.saved).toLocaleString() : '') + (rev.hash ? ' · ' + rev.hash.replace(/^[a-z0-9]+:/, '').slice(0, 8) : '') + (rev.n === state.revision ? ' · the page' : '') });
    const row = mk('div', { className: 'row' });
    const openB = mk('button', { type: 'button', className: 'btn', textContent: 'Open as a separate draft' }); openB.addEventListener('click', () => openRevision(rev.n));
    const cmpB = mk('button', { type: 'button', className: 'btn', textContent: 'Differences from the page' }); cmpB.addEventListener('click', () => compareRevision(rev.n));
    row.append(openB, cmpB); li.append(head, meta, row);
    const runs = Array.isArray(rev.runs) ? rev.runs : [];
    if (runs.length) {
      const rl = mk('ul', { className: 'runs' });
      for (const run of runs) {
        const rli = mk('li'), b = mk('button', { type: 'button', className: 'btn', textContent: 'Open run' });
        b.addEventListener('click', () => openRun(run.file));
        rli.append(mk('span', { className: 'meta', textContent: (run.startedUtc ? new Date(run.startedUtc).toLocaleString() : run.file) + ' · ' + run.players + (run.players === 1 ? ' player' : ' players') + ' · roll ' + run.roll + ' · ' + (run.result || run.outcome || '?') + ' ' }), b);
        rl.appendChild(rli);
      }
      li.appendChild(rl);
    } else if (live.on) li.appendChild(mk('span', { className: 'meta', textContent: 'No run recording has played it yet.' }));
    ul.appendChild(li);
  }
  for (const d of separateDrafts()) {
    if (open && open.key === d.key) continue;
    const li = mk('li'), row = mk('div', { className: 'row' });
    const b = mk('button', { type: 'button', className: 'btn', textContent: 'Open' }); b.addEventListener('click', () => openSeparate(d.key, d.label, d.scenario, d.run));
    const x = mk('button', { type: 'button', className: 'btn danger', textContent: 'Forget' }); x.addEventListener('click', () => { try { localStorage.setItem(DRAFTS_KEY, JSON.stringify(separateDrafts().filter(q => q.key !== d.key))); } catch (e) { /* storage unavailable */ } stageSyncRevisions(); });
    row.append(b, x); li.append(mk('b', { textContent: 'Separate draft: ' + d.label }), mk('span', { className: 'meta', textContent: d.saved ? new Date(d.saved).toLocaleString() : '' }), row); ul.appendChild(li);
  }
  const diff = $('revDiff'); diff.textContent = '';
  if (stage.diff) {
    stagePara(diff, 'From revision ' + stage.diff.n + ' to the page: ' + (stage.diff.lines.length ? stage.diff.lines.length + ' changes.' : 'no differences.'));
    const dl = mk('ul'); diff.appendChild(dl); stageRows(dl, stage.diff.lines.map(l => ({ kind: l.kind, text: l.text })));
  }
}
$('revSave').addEventListener('click', () => saveRevision());
$('revLoad').addEventListener('click', () => { stage.diff = null; listRevisions(); });
$('revBack').addEventListener('click', backToMine);
$('revRepeat').addEventListener('click', () => { const run = stage.separate && stage.separate.run; if (!run) return; api('/api/repeat', { file: run.file }).then(s => { live.rev = null; live.runsFor = null; say('The next mission you host repeats that run: same roll. It needs the same scenario and number of players to come out the same.'); return applyLive(s); }).catch(e => say(liveTrouble(e))); });
stageSync(); stageSyncRevisions();
