// ------------------------------------------------------------------ rehearse: the triggers played through a written trace (ML-016)
// The author writes what happens (players stand here, the phone is picked up, an officer goes down) and what should follow
// (trigger 2 counts down, the extraction is held, five enemies). Run sends the scenario and the script to the plugin, which
// plays it with Core Rehearsal: the same TriggerEngine the live mission uses, the variants rolled as the Director rolls them.
// The page never judges the rules itself, so it cannot drift from the game; a browser-native evaluator is later work and
// needs parity fixtures first. Offline the page builds, checks and downloads scripts only.
// Every event is an assumption the author injects. Nothing here predicts spotting, pathfinding, physics, networking or combat.
const REHEARSAL_SCHEMA = 'missionlab-rehearsal/1';
const RH_PHASES = ['off', 'circle', 'waiting', 'holding', 'counting', 'fired', 'cooling', 'exhausted'];
const RH_EVENTS = { players: 'Players stand', alert: 'Alarm', intel: 'Intel taken', down: 'Enemies down', door: 'Door opened', fire: 'Fired by hand' };
const RH_CHECKS = { fired: 'Check: fired', notFired: 'Check: not fired', phase: 'Check: phase', extraction: 'Check: extraction', enemies: 'Check: enemies' };
const RH_OFFLINE = 'Running needs the game: start OPERATOR with MissionLab and open http://localhost:8765. On its own this page checks the rows and saves the test as a file.';
state.rehearse = { rows: [], name: '', players: 2, roll: 0, pin: null, picking: null, focus: null, down: null, answer: null, failed: new Set(), saved: [], listedFor: null, choices: '' };

// ----- pure parts: rows to a script, the script as text, a script back to rows, the answer as a report

// One row is one step, so "step N" in the plugin's answer is row N. Rows are kept in time order (stable).
function rehearseStep(row) {
  const at = r2(Math.max(0, num(row.at, 0)));
  switch (row.kind) {
    case 'players': return { at, players: (Array.isArray(row.players) ? row.players : []).filter(isVec).map(r3) };
    case 'alert': return { at, event: 'alert' };
    case 'intel': return { at, event: 'intel', ...(row.item ? { item: row.item } : {}) };
    case 'down': return { at, event: 'down', ...(row.tag ? { tag: row.tag } : {}), ...(row.count > 0 ? { count: row.count | 0 } : {}) };
    case 'door': return isVec(row.pos) ? { at, event: 'door', pos: r3(row.pos) } : null;
    case 'fire': return row.id > 0 ? { at, fire: row.id } : null;
    case 'fired': case 'notFired': return row.id > 0 ? { at, expect: { [row.kind]: [row.id] } } : null;
    case 'phase': return row.id > 0 && RH_PHASES.includes(row.phase) ? { at, expect: { phase: { [row.id]: row.phase } } } : null;
    case 'extraction': return { at, expect: { extraction: row.value === 'open' ? 'open' : 'held' } };
    case 'enemies': return { at, expect: { enemies: Math.max(0, row.count | 0) } };
  }
  return null;
}
// meta: { name, scenario (the fingerprint of the scenario text it is written for), revision, players, roll }.
function rehearseScript(rows, meta) {
  const steps = rows.map((row, i) => ({ row, i })).sort((a, b) => num(a.row.at, 0) - num(b.row.at, 0) || a.i - b.i).map(x => rehearseStep(x.row)).filter(Boolean);
  const players = Number.isInteger(meta.players) && meta.players >= 1 && meta.players <= 16 ? meta.players : 1, roll = Math.trunc(num(meta.roll, 0));
  return { schema: REHEARSAL_SCHEMA, ...(meta.name ? { name: meta.name } : {}), ...(meta.scenario ? { scenario: meta.scenario } : {}),
    ...(meta.revision > 0 ? { revision: meta.revision } : {}), players, ...(roll ? { roll } : {}), steps };
}
// The script as Rehearsal.Write has it: fixed key order, two decimals, defaults left out, one step per line.
function rehearseText(s) {
  const n = v => { const r = Math.round(v * 100) / 100; return String(r === 0 ? 0 : r); };
  const q = JSON.stringify, p = v => '[' + n(v[0]) + ',' + n(v[1]) + ',' + n(v[2]) + ']';
  const step = st => {
    let o = '{"at":' + n(st.at);
    if (Array.isArray(st.players)) o += ',"players":[' + st.players.map(p).join(',') + ']';
    if (st.event) {
      o += ',"event":' + q(st.event);
      if (st.event === 'intel' && st.item) o += ',"item":' + q(st.item);
      if (st.event === 'down' && st.tag) o += ',"tag":' + q(st.tag);
      if (st.event === 'down' && st.count > 0) o += ',"count":' + (st.count | 0);
      if (st.event === 'door') o += ',"pos":' + p(isVec(st.pos) ? st.pos : [0, 0, 0]);
    }
    if (st.fire > 0) o += ',"fire":' + (st.fire | 0);
    const x = st.expect;
    if (x) {
      const parts = [];
      if (Array.isArray(x.fired) && x.fired.length) parts.push('"fired":[' + x.fired.join(',') + ']');
      if (Array.isArray(x.notFired) && x.notFired.length) parts.push('"notFired":[' + x.notFired.join(',') + ']');
      const ids = x.phase ? Object.keys(x.phase).map(Number).sort((a, b) => a - b) : [];
      if (ids.length) parts.push('"phase":{' + ids.map(id => q(String(id)) + ':' + q(x.phase[id])).join(',') + '}');
      if (x.extraction) parts.push('"extraction":' + q(x.extraction));
      if (Number.isInteger(x.enemies) && x.enemies >= 0) parts.push('"enemies":' + x.enemies);
      o += ',"expect":{' + parts.join(',') + '}';
    }
    return o + '}';
  };
  let t = '{"schema":' + q(REHEARSAL_SCHEMA);
  if (s.name) t += ',"name":' + q(s.name);
  if (s.scenario) t += ',"scenario":' + q(s.scenario);
  if (s.revision > 0) t += ',"revision":' + (s.revision | 0);
  t += ',"players":' + (Number.isInteger(s.players) ? s.players : 1);
  if (s.roll) t += ',"roll":' + Math.trunc(s.roll);
  return t + ',"steps":[' + (s.steps || []).map((st, i) => (i ? ',' : '') + '\n' + step(st)).join('') + '\n]}';
}
// A script read back as rows: a step that does several things, or checks several, becomes several rows at its time.
function rehearseRows(s) {
  const rows = [];
  for (const st of s && Array.isArray(s.steps) ? s.steps : []) {
    if (!st || typeof st !== 'object') continue;
    const at = num(st.at, 0);
    if (Array.isArray(st.players)) rows.push({ at, kind: 'players', players: st.players.filter(isVec).map(v => v.slice(0, 3)) });
    if (st.event === 'alert') rows.push({ at, kind: 'alert' });
    else if (st.event === 'intel') rows.push({ at, kind: 'intel', item: typeof st.item === 'string' ? st.item : '' });
    else if (st.event === 'down') rows.push({ at, kind: 'down', tag: typeof st.tag === 'string' ? st.tag : '', count: Number.isInteger(st.count) ? st.count : 0 });
    else if (st.event === 'door') rows.push({ at, kind: 'door', pos: isVec(st.pos) ? st.pos.slice(0, 3) : null });
    if (Number.isInteger(st.fire) && st.fire > 0) rows.push({ at, kind: 'fire', id: st.fire });
    const x = st.expect;
    if (!x || typeof x !== 'object') continue;
    for (const id of Array.isArray(x.fired) ? x.fired : []) rows.push({ at, kind: 'fired', id });
    for (const id of Array.isArray(x.notFired) ? x.notFired : []) rows.push({ at, kind: 'notFired', id });
    if (x.phase && typeof x.phase === 'object') for (const k of Object.keys(x.phase)) rows.push({ at, kind: 'phase', id: +k, phase: x.phase[k] });
    if (x.extraction === 'open' || x.extraction === 'held') rows.push({ at, kind: 'extraction', value: x.extraction });
    if (Number.isInteger(x.enemies)) rows.push({ at, kind: 'enemies', count: x.enemies });
  }
  return rows;
}
// What stops a trace from being run or saved, row by row (numbers count from 1). ids: the scenario's trigger numbers.
function rehearseProblems(rows, ids) {
  const out = [], has = id => ids.includes(id);
  if (!rows.length) out.push({ row: 0, text: 'Add at least one row.' });
  rows.forEach((row, i) => {
    const bad = text => out.push({ row: i + 1, text: 'Row ' + (i + 1) + ': ' + text });
    if (!(typeof row.at === 'number' && isFinite(row.at)) || row.at < 0 || row.at > 86400) bad('the time is from 0 to 86400 seconds.');
    if (row.kind === 'players' && !Array.isArray(row.players)) bad('place the players: click the map or choose a place, or choose Nobody alive.');
    if (row.kind === 'door' && !isVec(row.pos)) bad('choose the door that is opened.');
    if (['fire', 'fired', 'notFired', 'phase'].includes(row.kind) && !has(row.id)) bad(row.id > 0 ? 'there is no trigger ' + row.id + ' in this scenario.' : 'choose a trigger.');
    if (row.kind === 'phase' && !RH_PHASES.includes(row.phase)) bad('choose a phase.');
  });
  return out;
}
// Whether a test was written for this scenario text; the same sentences as the plugin's RehearsalFile.Match.
function rehearseMatch(pin, hash, revision) {
  if (!pin || !pin.scenario) return { match: 'unknown', why: 'This test does not record which scenario text it was written for, so a change to the scenario cannot be told.' };
  if (pin.scenario === hash) return { match: 'same', why: 'Written for this scenario text (revision ' + revision + ').' };
  return { match: 'changed', why: 'The scenario changed since this test was written: the test was written for revision ' + (pin.revision | 0) + ', this is revision ' + revision +
    ((pin.revision | 0) === revision ? ' with other content' : '') + '. Its steps ran against the scenario as it is now.' };
}
// The plugin's answer, ready to show: a title, notes (refusal findings, a roll the Director refuses, a changed scenario),
// the assumptions, and every step with expected against actual. failedRows: row numbers (from 1) of failing steps.
function rehearseReport(a) {
  if (!a || typeof a !== 'object') return { level: 'refused', title: 'The game sent no answer that can be read.', notes: [], assumptions: '', steps: [], failedRows: [] };
  const findings = (Array.isArray(a.findings) ? a.findings : []).map(f => (f.path ? f.path + ': ' : '') + f.text);
  if (a.refused) return { level: 'refused', title: (a.refused === 'scenario' ? 'Not run: the scenario would be refused at a mission\'s start. ' : 'Not run: the test cannot be read. ') + (a.why || ''), notes: findings, assumptions: '', steps: [], failedRows: [] };
  const steps = (Array.isArray(a.steps) ? a.steps : []).map(x => ({ ok: !!x.ok, check: !!x.expected, at: x.at, what: String(x.what || ''), detail: x.expected ? 'expected ' + x.expected + '; got ' + x.actual : '' }));
  const checks = steps.filter(x => x.check), bad = checks.filter(x => !x.ok), notes = [];
  if (a.blocked) notes.push('The Director would refuse this roll, so the mission would not start: ' + a.blocked);
  if (a.match === 'changed' || a.match === 'unknown') notes.push(a.matchWhy);
  const failedRows = [...new Set(steps.filter(x => !x.ok).map(x => { const m = /^step (\d+) /.exec(x.what); return m ? +m[1] : 0; }).filter(n => n > 0))];
  const title = a.passed ? 'Passed: all ' + checks.length + (checks.length === 1 ? ' check was' : ' checks were') + ' as expected.'
    : bad.length ? 'Failed: ' + bad.length + ' of ' + checks.length + (checks.length === 1 ? ' check was' : ' checks were') + ' not as expected.'
      : 'Failed: the checks were as expected, but the Director would refuse this roll.';
  return { level: a.passed ? 'passed' : 'failed', title, notes, assumptions: a.assumptions || '', steps, failedRows };
}
// Kept tests rerun against the scenario as it is now: one line each, saying which fail and why (step and trigger).
function rehearseRerunReport(list) {
  let passed = 0;
  const lines = list.map(t => {
    const name = t.name || t.file;
    if (t.error) return { ok: false, text: name + ': not run: ' + t.error };
    const r = rehearseReport(t.answer);
    if (r.level === 'passed') passed++;
    const why = r.level === 'refused' ? r.title : r.steps.filter(x => !x.ok).slice(0, 3).map(x => x.what + (x.detail ? ' (' + x.detail + ')' : '')).join('; ');
    const changed = t.answer && t.answer.match === 'changed' ? ' ' + t.answer.matchWhy : '';
    return { ok: r.level === 'passed', text: name + ': ' + (r.level === 'passed' ? 'passed.' : 'failed: ' + (why || r.title)) + changed };
  });
  const title = !list.length ? 'No tests are kept for this scenario yet.' : passed === list.length ? 'All ' + list.length + ' kept tests pass.' : (list.length - passed) + ' of ' + list.length + ' kept tests fail.';
  return { title, lines };
}
// ----- the panel
const rhTriggers = () => state.zones.filter(z => z.id > 0).slice().sort((a, b) => a.id - b.id);
const rhTriggerLabel = z => z.id + ': ' + (z.name || trigText(z).replace(/ \(trigger \d+\)$/, ''));
// Where players can be put in one go: named places and zone triggers, each with a spot that is really inside.
function rhSpots() {
  const out = [];
  for (const p of state.places) if (isVec(p.pos)) out.push({ key: 'p' + p.id, label: 'Place: ' + p.name, spot: rhInside(p.pos, p.area, q => placeContains(p, q)) });
  for (const z of rhTriggers()) if ((z.when === 'zone' || z.when === 'hold') && isVec(z.pos)) out.push({ key: 'z' + z.id, label: 'Zone of trigger ' + rhTriggerLabel(z), spot: rhInside(z.pos, z.area, q => membershipInside(z, q)) });
  return out;
}
// The anchor when it is inside; for a shaped area whose anchor is outside, the middle of its first outline or a point
// between the middle and a corner, at a height inside the floor band. null when none of those is inside.
function rhInside(pos, area, inside) {
  const tries = [pos.slice(0, 3)], part = area && Array.isArray(area.parts) ? area.parts[0] : null;
  if (part && Array.isArray(part.outline) && part.outline.length >= 3) {
    const y = Array.isArray(part.floor) ? Math.min(part.floor[1], Math.max(part.floor[0], pos[1])) : pos[1];
    let cx = 0, cz = 0; for (const c of part.outline) { cx += c[0]; cz += c[1]; }
    cx /= part.outline.length; cz /= part.outline.length;
    tries.push([cx, y, cz]);
    for (const c of part.outline) tries.push([(c[0] + cx * 3) / 4, y, (c[1] + cz * 3) / 4], [(c[0] + cx) / 2, y, (c[1] + cz) / 2]);
  }
  return tries.find(inside) || null;
}
const rhEl = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined) e.textContent = text; return e; };
function rhSelect(options, value, onChange, label) {
  const s = document.createElement('select'); if (label) s.setAttribute('aria-label', label);
  for (const [v, t] of options) { const o = document.createElement('option'); o.value = v; o.textContent = t; s.appendChild(o); }
  s.value = value; s.addEventListener('change', () => onChange(s.value));
  return s;
}
function rhNumber(value, min, max, step, onChange, label) {
  const n = document.createElement('input'); n.type = 'number'; n.min = String(min); n.max = String(max); n.step = String(step); n.value = String(value);
  if (label) n.setAttribute('aria-label', label);
  n.addEventListener('change', () => onChange(Number(n.value)));
  return n;
}
const rhWhere = p => '(' + p[0].toFixed(1) + ', ' + p[2].toFixed(1) + ')' + placeWords(p);
function rhSort() { const r = state.rehearse; r.rows = r.rows.map((row, i) => ({ row, i })).sort((a, b) => num(a.row.at, 0) - num(b.row.at, 0) || a.i - b.i).map(x => x.row); }
// A change to the trace: the last answer no longer describes it.
function rhEdited() { const r = state.rehearse; r.failed.clear(); rhProblemsShow(); }

function rhRender() {
  const r = state.rehearse, list = $('rhRows');
  list.textContent = '';
  const trigOptions = [['0', 'choose a trigger'], ...rhTriggers().map(z => [String(z.id), rhTriggerLabel(z)])];
  r.rows.forEach((row, i) => {
    const check = row.kind in RH_CHECKS, li = rhEl('li', (check ? 'check' : 'event') + (r.failed.has(i + 1) ? ' failed' : ''));
    li.addEventListener('pointerdown', () => { if (r.focus !== row) { r.focus = row; invalidate(); } });
    const at = rhNumber(num(row.at, 0), 0, 86400, 0.5, v => { row.at = isFinite(v) ? Math.max(0, v) : 0; rhSort(); rhEdited(); rhRender(); }, 'Row ' + (i + 1) + ': seconds from the mission\'s start');
    const del = rhEl('button', 'btn', '×'); del.type = 'button'; del.title = 'Remove this row'; del.setAttribute('aria-label', 'Remove row ' + (i + 1));
    del.addEventListener('click', () => { r.rows.splice(i, 1); if (r.picking === row) rhStopPicking(); if (r.focus === row) r.focus = null; rhEdited(); rhRender(); invalidate(); });
    const what = rhEl('div', 'what');
    const trig = (key, label) => rhSelect(row[key] > 0 && !rhTriggers().some(z => z.id === row[key]) ? trigOptions.concat([[String(row[key]), 'trigger ' + row[key] + ' (not in this scenario)']]) : trigOptions,
      String(row[key] || 0), v => { row[key] = +v; rhEdited(); }, label);
    switch (row.kind) {
      case 'players': {
        const placed = Array.isArray(row.players);
        what.append(rhEl('span', '', !placed ? 'Not placed yet.' : row.players.length ? row.players.map((p, k) => 'P' + (k + 1) + ' ' + rhWhere(p)).join(', ') : 'Nobody is alive.'));
        const click = rhEl('button', 'btn', r.picking === row ? 'Stop placing' : 'Click the map'); click.type = 'button';
        click.addEventListener('click', () => (r.picking === row ? rhStopPicking() : rhStartPicking(row)));
        const spots = rhSpots();
        const at2 = rhSelect([['', 'Put them all at…'], ...spots.map(s => [s.key, s.label])], '', v => {
          const s = spots.find(x => x.key === v); if (!s) return;
          if (!s.spot) return say('No spot inside ' + s.label + ' could be found; click the map instead.');
          row.players = Array.from({ length: r.players }, () => r3(s.spot)); r.focus = row; rhEdited(); rhRender(); invalidate();
        }, 'Put the players at a place or zone');
        const none = rhEl('button', 'btn', 'Nobody alive'); none.type = 'button';
        none.addEventListener('click', () => { row.players = []; if (r.picking === row) rhStopPicking(); rhEdited(); rhRender(); invalidate(); });
        what.append(click, at2, none);
        break;
      }
      case 'intel': {
        const items = [...new Set([...state.zones.filter(z => z.when === 'intel' && z.item).map(z => z.item), ...state.objectives.map(o => o.label || o.name)])];
        what.append(rhSelect([['', 'any item'], ...items.map(t => [t, t]), ...(row.item && !items.includes(row.item) ? [[row.item, row.item]] : [])], row.item || '', v => { row.item = v; rhEdited(); }, 'Item picked up'));
        break;
      }
      case 'down': {
        const tags = tagsOf();
        what.append(rhNumber(row.count | 0, 0, 1000, 1, v => { row.count = Math.max(0, v | 0); rhEdited(); }, 'How many go down (0: all of them)'),
          rhSelect([['', 'enemies of any tag'], ...tags.map(t => [t, 'tagged ' + t]), ...(row.tag && !tags.includes(row.tag) ? [[row.tag, 'tagged ' + row.tag]] : [])], row.tag || '', v => { row.tag = v; rhEdited(); }, 'Which enemies'),
          rhEl('span', '', '0 means every one there is.'));
        break;
      }
      case 'door': {
        const doors = [...state.doors.map((d, k) => ['d' + k, (d.name || 'Door') + ' ' + (k + 1), d.pos]),
          ...rhTriggers().filter(z => z.when === 'door' && isVec(z.pos)).map(z => ['t' + z.id, 'The door of trigger ' + rhTriggerLabel(z), z.pos])];
        const near = isVec(row.pos) ? doors.find(d => Math.hypot(d[2][0] - row.pos[0], d[2][1] - row.pos[1], d[2][2] - row.pos[2]) <= 0.5) : null;
        const opts = [['', 'choose a door'], ...doors.map(d => [d[0], d[1]]), ...(isVec(row.pos) && !near ? [['f', 'the door at ' + rhWhere(row.pos)]] : [])];
        what.append(rhSelect(opts, near ? near[0] : isVec(row.pos) ? 'f' : '', v => { const d = doors.find(x => x[0] === v); if (d) { row.pos = d[2].slice(0, 3); rhEdited(); } }, 'Door opened'));
        if (!doors.length) what.append(rhEl('span', '', 'Doors are listed once the game has sent them, on the World tab.'));
        break;
      }
      case 'fire': case 'fired': case 'notFired': what.append(trig('id', 'Trigger')); break;
      case 'phase':
        what.append(trig('id', 'Trigger'), rhSelect([['', 'choose a phase'], ...RH_PHASES.map(p => [p, p])], row.phase || '', v => { row.phase = v; rhEdited(); }, 'Phase'));
        break;
      case 'extraction': what.append(rhSelect([['held', 'held'], ['open', 'open']], row.value === 'open' ? 'open' : 'held', v => { row.value = v; rhEdited(); }, 'Extraction')); break;
      case 'enemies': what.append(rhNumber(row.count | 0, 0, 10000, 1, v => { row.count = Math.max(0, v | 0); rhEdited(); }, 'Enemies in the mission'), rhEl('span', '', 'people in the mission by then (gun trucks are not counted)')); break;
    }
    li.append(rhEl('b', '', String(i + 1)), at, rhEl('span', 'kind', check ? RH_CHECKS[row.kind] : RH_EVENTS[row.kind] + ' (assumed)'), del, what);
    list.appendChild(li);
  });
  rhProblemsShow();
  const offline = !live.on;
  $('rhRun').disabled = offline; $('rhRerun').disabled = offline;
  $('rhRun').title = offline ? RH_OFFLINE : 'Plays the trace in the plugin with the same trigger rules the game uses.';
  $('rhSave').textContent = offline ? 'Download test' : 'Save as test';
  r.choices = rhChoicesKey();
}
function rhProblemsShow() {
  const r = state.rehearse, found = rehearseProblems(r.rows, rhTriggers().map(z => z.id));
  const checks = r.rows.some(row => row.kind in RH_CHECKS);
  $('rhProblems').textContent = found.length ? found.map(f => f.text).join(' ') : r.rows.length && !checks ? 'This trace checks nothing yet: add a Check row.' : '';
  return found;
}
// What the rows' choices depend on; when it changes (a trigger, door, place or tag was edited) the rows are drawn again.
const rhChoicesKey = () => JSON.stringify([state.zones.map(z => [z.id, z.name, z.when, z.item]), state.doors.length, state.places.map(p => [p.id, p.name]), tagsOf(), state.objectives.length, live.on]);
function rhRefresh() { const r = state.rehearse; if (r.choices !== rhChoicesKey()) rhRender(); if (live.on && r.listedFor !== state.scenarioId) rhList(); }

// ----- placing players by clicking the 2D map: one click per player, as many as the Players field says
function rhStartPicking(row) {
  const r = state.rehearse;
  if (state.mode === '3d') return say('Switch to the 2D map to place players by clicking.');
  r.picking = row; r.focus = row; row.players = [];
  say('Click the map where player 1 of ' + r.players + ' stands. Esc stops.');
  rhEdited(); rhRender(); invalidate();
}
function rhStopPicking() { const r = state.rehearse; if (!r.picking) return; r.picking = null; r.down = null; rhRender(); invalidate(); }
// Ahead of the map's own handler (capture), so a click while placing neither selects nor drags anything.
canvas.addEventListener('pointerdown', ev => {
  const r = state.rehearse;
  if (!r.picking || ev.button !== 0) return;
  ev.stopImmediatePropagation(); r.down = [ev.clientX, ev.clientY];
}, true);
canvas.addEventListener('pointerup', ev => {
  const r = state.rehearse, down = r.down;
  if (!r.picking || !down || ev.button !== 0) return;
  r.down = null;
  if (Math.hypot(ev.clientX - down[0], ev.clientY - down[1]) >= 4) return;
  const row = r.picking;
  if (!r.rows.includes(row) || row.kind !== 'players') return rhStopPicking();
  const b = canvas.getBoundingClientRect(), [wx, wz] = toWorld(ev.clientX - b.left, ev.clientY - b.top), y = groundAt(wx, wz);
  if (y === null && state.map) return say('Nobody can stand there. Click the shaded ground' + (isFinite(state.ceiling) ? ', or raise "Show up to" to reach higher floors.' : '.'));
  if (!Array.isArray(row.players)) row.players = [];
  row.players.push(r3([wx, y === null ? 0 : y, wz]));
  const k = row.players.length;
  if (k >= r.players) { say('All ' + r.players + (r.players === 1 ? ' player is' : ' players are') + ' placed.'); r.picking = null; }
  else say('Player ' + k + ' stands at ' + rhWhere(row.players[k - 1]) + '. Click where player ' + (k + 1) + ' of ' + r.players + ' stands. Esc stops.');
  rhEdited(); rhRender(); invalidate();
});
window.addEventListener('keydown', ev => { if (ev.key === 'Escape' && state.rehearse.picking) rhStopPicking(); });
// The players of the row being edited, drawn on the 2D map (24-drawing.js calls this).
function drawRehearsal() {
  const r = state.rehearse, row = r && (r.picking || r.focus);
  if (!row || row.kind !== 'players' || !Array.isArray(row.players)) return;
  const font = getComputedStyle(document.body).getPropertyValue('--mono');
  row.players.forEach((p, k) => {
    const [x, y] = toScreen(p[0], p[2]);
    ctx.beginPath(); ctx.arc(x, y, 6.5, 0, 6.2832); ctx.fillStyle = css.friend; ctx.globalAlpha = 0.55; ctx.fill(); ctx.globalAlpha = 1;
    ctx.setLineDash([3, 3]); ctx.lineWidth = 1.5; ctx.strokeStyle = css.friend; ctx.beginPath(); ctx.arc(x, y, 10, 0, 6.2832); ctx.stroke(); ctx.setLineDash([]);
    ctx.font = '600 11px ' + font; ctx.textAlign = 'left'; ctx.textBaseline = 'middle'; ctx.fillStyle = css.friend; ctx.fillText('P' + (k + 1), x + 12, y);
  });
}

// ----- running, keeping and rerunning (through the plugin), opening and downloading (anywhere)
async function rhCall(path, body) {
  const init = body === undefined ? { cache: 'no-store' } : { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify(body) };
  const res = await fetch(path, init);
  let data = null; try { data = await res.json(); } catch (e) { /* no body */ }
  return { status: res.status, data };
}
// The script for the scenario text given. A test opened from a file or the game keeps the fingerprint it was written for,
// so a run says when the scenario has changed since; a new trace is written for the scenario as it is now.
function rhScript(text) {
  const r = state.rehearse, pin = r.pin || { scenario: textHash(text), revision: state.revision };   // textHash: ScenarioFile.Hash (31a-stage.js)
  return rehearseScript(r.rows, { name: r.name.trim(), scenario: pin.scenario, revision: pin.revision, players: r.players, roll: r.roll });
}
function rhReady() {
  const found = rhProblemsShow();
  if (found.length) { $('rhStatus').textContent = 'Not yet: ' + found[0].text; return false; }
  return true;
}
async function rhRun() {
  const r = state.rehearse;
  if (!live.on) { $('rhStatus').textContent = RH_OFFLINE; return; }
  if (!rhReady()) return;
  const scenario = scenarioText(), script = rehearseText(rhScript(scenario));
  $('rhStatus').textContent = 'Asked the game to rehearse it...';
  try {
    const { status, data } = await rhCall('/api/rehearse', { scenario, script });
    if (status !== 200 || !data) throw new Error((data && data.error) || 'status ' + status);
    r.answer = data; $('rhStatus').textContent = '';
    rhShow();
  } catch (e) { $('rhStatus').textContent = 'The game did not rehearse it (' + e.message + '). Is OPERATOR with MissionLab still running?'; }
}
function rhShow() {
  const r = state.rehearse, rep = rehearseReport(r.answer), box = $('rhResult');
  box.textContent = '';
  r.failed = new Set(rep.failedRows);
  box.append(rhEl('p', rep.level === 'passed' ? 'ok' : 'bad', rep.title));
  for (const n of rep.notes) box.append(rhEl('p', '', n));
  if (rep.assumptions) box.append(rhEl('p', 'assume', 'Assumed: ' + rep.assumptions));
  if (rep.steps.length) {
    const ol = rhEl('ol'); ol.id = 'rhSteps';
    for (const x of rep.steps) {
      const li = rhEl('li'), body = rhEl('span', '', x.what);
      if (x.detail) { body.append(document.createElement('br'), rhEl('span', 'got', x.detail)); }
      li.append(rhEl('span', 'mark ' + (x.ok ? 'ok' : 'bad'), x.check ? (x.ok ? '✓' : '✗') : '·'), body);
      ol.appendChild(li);
    }
    box.appendChild(ol);
  }
  rhRender();
}
function rhPinShow() {
  const r = state.rehearse, el = $('rhPin');
  if (!r.pin) { el.textContent = ''; el.className = 'hint'; return; }
  const m = rehearseMatch(r.pin, textHash(scenarioText()), state.revision);
  el.textContent = m.why; el.className = 'hint' + (m.match === 'same' ? '' : ' changed');
}
const rhStem = name => name.trim().replace(/[^A-Za-z0-9_-]+/g, '_').replace(/^[_-]+|[_-]+$/g, '').slice(0, 60) || 'test';
async function rhSave() {
  const r = state.rehearse;
  if (!r.name.trim()) { $('rhStatus').textContent = 'Give the test a name first.'; $('rhName').focus(); return; }
  if (!rhReady()) return;
  const hadId = !!state.scenarioId, text = scenarioText();
  r.pin = { scenario: textHash(text), revision: state.revision };    // a kept test is written for the scenario as it is now
  const script = rehearseText(rhScript(text));
  rhPinShow();
  if (!live.on) {
    const name = fileName().replace(/\.json$/, '') + '.' + rhStem(r.name) + '.rehearsal.json';
    const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([script], { type: 'application/json' })); a.download = name;
    document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(a.href), 4000);
    $('rhStatus').textContent = 'Downloaded as ' + name + ', written for revision ' + state.revision + '. Keep it beside the scenario file; to run it, open the page from the game.';
    return;
  }
  const path = '/api/rehearsals/' + encodeURIComponent(state.scenarioId);
  try {
    let res = await rhCall(path, { name: r.name.trim(), text: script, replace: false });
    if (res.status === 409 && res.data && res.data.exists && confirm('A test named ' + res.data.file + ' is already kept for this scenario. Replace it?')) res = await rhCall(path, { name: r.name.trim(), text: script, replace: true });
    const d = res.data || {};
    if (res.status === 200 && d.saved) {
      $('rhStatus').textContent = (d.replaced ? 'Replaced ' : 'Kept as ') + 'rehearsals/' + state.scenarioId + '/' + d.test.file + ', written for revision ' + state.revision + '.' +
        (hadId ? '' : ' The scenario got its id just now: save the scenario too, so the test stays with it.');
      rhList();
    } else $('rhStatus').textContent = 'Not kept: ' + (d.why || d.error || 'status ' + res.status) + (Array.isArray(d.findings) && d.findings.length ? ' ' + d.findings.map(f => (f.path ? f.path + ': ' : '') + f.text).join('; ') : '');
  } catch (e) { $('rhStatus').textContent = 'The game did not keep it (' + e.message + '). Is OPERATOR with MissionLab still running?'; }
}
async function rhList() {
  const r = state.rehearse, id = state.scenarioId;
  r.listedFor = id;
  if (!live.on) return;
  if (!id) { r.saved = []; rhSavedShow('This scenario has no id yet. Save it once; then tests can be kept beside it.'); return; }
  try {
    const { status, data } = await rhCall('/api/rehearsals/' + encodeURIComponent(id));
    if (status !== 200 || !data) throw new Error((data && data.error) || 'status ' + status);
    r.saved = Array.isArray(data.rehearsals) ? data.rehearsals : [];
    rhSavedShow('');
  } catch (e) { rhSavedShow('The kept tests could not be listed (' + e.message + ').'); }
}
function rhSavedShow(note) {
  const r = state.rehearse, ul = $('rhSaved');
  ul.textContent = '';
  if (note) { ul.append(rhEl('li', '', note)); return; }
  for (const t of r.saved) {
    const li = rhEl('li'), body = rhEl('div');
    const mark = t.answer || t.failure ? rhEl('span', 'mark ' + (t.passed ? 'ok' : 'bad'), t.passed ? '✓' : '✗') : rhEl('span', 'mark', '·');
    body.append(rhEl('b', '', t.name || t.file), rhEl('div', 'meta', t.error ? 'cannot be read: ' + t.error : 'revision ' + (t.revision | 0) + ' · ' + (t.steps | 0) + ' steps · ' + (t.players | 0) + (t.players === 1 ? ' player' : ' players') + (t.saved ? ' · ' + t.saved : '')));
    if (t.line) body.append(rhEl('div', t.passed ? 'meta' : 'meta bad', t.line));
    const acts = rhEl('div', 'row'), open = rhEl('button', 'btn', 'Open'); open.type = 'button';
    open.addEventListener('click', () => rhOpenKept(t));
    acts.append(open); body.append(acts);
    li.append(mark, body); ul.appendChild(li);
  }
}
async function rhKeptText(t) {
  const res = await fetch('/api/rehearsals/' + encodeURIComponent(state.scenarioId) + '/' + encodeURIComponent(t.file), { cache: 'no-store' });
  if (res.status !== 200) throw new Error('status ' + res.status);
  return res.text();
}
async function rhOpenKept(t) {
  try { rhLoad(await rhKeptText(t), t.file); } catch (e) { $('rhStatus').textContent = t.file + ' could not be read (' + e.message + ').'; }
}
// Every kept test against the scenario as it is now, one after another; each says whether it passes and, if not, why.
async function rhRerun() {
  const r = state.rehearse;
  if (!live.on) { $('rhStatus').textContent = RH_OFFLINE; return; }
  await rhList();
  if (!r.saved.length) { $('rhStatus').textContent = state.scenarioId ? 'No tests are kept for this scenario yet.' : 'This scenario has no id yet, so it has no kept tests.'; return; }
  const scenario = scenarioText(), done = [];
  for (const t of r.saved) {
    $('rhStatus').textContent = 'Rerunning ' + (done.length + 1) + ' of ' + r.saved.length + '...';
    try {
      const script = await rhKeptText(t);
      const { status, data } = await rhCall('/api/rehearse', { scenario, script });
      if (status !== 200 || !data) throw new Error((data && data.error) || 'status ' + status);
      done.push({ file: t.file, name: t.name, answer: data });
    } catch (e) { done.push({ file: t.file, name: t.name, error: e.message }); }
  }
  const rep = rehearseRerunReport(done);
  done.forEach((d, k) => { const t = r.saved[k]; t.answer = d.answer || null; t.failure = d.error || ''; t.passed = rep.lines[k].ok; t.line = rep.lines[k].text; });
  $('rhStatus').textContent = rep.title;
  rhSavedShow('');
}
function rhLoad(text, from) {
  const r = state.rehearse;
  let o = null; try { o = JSON.parse(text); } catch (e) { /* said below */ }
  if (!o || typeof o !== 'object' || typeof o.schema !== 'string' || !o.schema.startsWith('missionlab-rehearsal/')) { $('rhStatus').textContent = from + ' is not a MissionLab rehearsal; nothing was loaded.'; return; }
  if (o.schema !== REHEARSAL_SCHEMA) { $('rhStatus').textContent = from + ' was written by a newer MissionLab (' + o.schema + '); this page reads ' + REHEARSAL_SCHEMA + '. Nothing was loaded.'; return; }
  rhStopPicking();
  r.rows = rehearseRows(o); rhSort();
  r.name = typeof o.name === 'string' ? o.name : '';
  r.players = Number.isInteger(o.players) && o.players >= 1 && o.players <= 16 ? o.players : 1;
  r.roll = Number.isInteger(o.roll) ? o.roll : 0;
  r.pin = { scenario: typeof o.scenario === 'string' ? o.scenario : '', revision: Number.isInteger(o.revision) ? o.revision : 0 };
  r.answer = null; r.failed.clear(); r.focus = null; $('rhResult').textContent = '';
  $('rhName').value = r.name; $('rhPlayers').value = String(r.players); $('rhRoll').value = String(r.roll);
  $('rhStatus').textContent = 'Opened ' + from + ' (' + r.rows.length + ' rows).';
  rhPinShow(); rhRender(); invalidate();
}

$('rhAddBtn').addEventListener('click', () => {
  const r = state.rehearse, kind = $('rhAdd').value, last = r.rows.length ? num(r.rows[r.rows.length - 1].at, 0) : 0;
  const first = rhTriggers()[0], intel = state.zones.find(z => z.when === 'intel' && z.item);
  const row = { at: last, kind, ...({ players: { players: null }, intel: { item: intel ? intel.item : '' }, down: { tag: '', count: 1 }, door: { pos: null }, fire: { id: first ? first.id : 0 },
    fired: { id: first ? first.id : 0 }, notFired: { id: first ? first.id : 0 }, phase: { id: first ? first.id : 0, phase: 'waiting' }, extraction: { value: 'held' }, enemies: { count: 0 } }[kind] || {}) };
  r.rows.push(row); r.focus = row;
  rhEdited(); rhRender();
  if (kind === 'players') rhStartPicking(row);
});
$('rhName').addEventListener('input', () => { state.rehearse.name = $('rhName').value; });
$('rhPlayers').addEventListener('change', () => { const v = Math.round(Number($('rhPlayers').value)); state.rehearse.players = v >= 1 && v <= 16 ? v : 1; $('rhPlayers').value = String(state.rehearse.players); rhEdited(); });
$('rhRoll').addEventListener('change', () => { const v = Math.trunc(Number($('rhRoll').value)); state.rehearse.roll = isFinite(v) ? v : 0; $('rhRoll').value = String(state.rehearse.roll); rhEdited(); });
$('rhRun').addEventListener('click', rhRun);
$('rhSave').addEventListener('click', rhSave);
$('rhRerun').addEventListener('click', rhRerun);
$('rhOpen').addEventListener('click', () => $('rhFile').click());
$('rhFile').addEventListener('change', async ev => { const f = ev.target.files && ev.target.files[0]; ev.target.value = ''; if (f) rhLoad(await f.text(), f.name); });
$('rhClear').addEventListener('click', () => {
  const r = state.rehearse;
  rhStopPicking(); r.rows = []; r.pin = null; r.answer = null; r.failed.clear(); r.focus = null; r.name = ''; $('rhName').value = '';
  $('rhResult').textContent = ''; $('rhStatus').textContent = ''; rhPinShow(); rhRender(); invalidate();
});
$('rhBox').addEventListener('pointerenter', rhRefresh);
$('rhBox').addEventListener('focusin', rhRefresh);
