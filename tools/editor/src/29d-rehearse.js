// ------------------------------------------------------------------ rehearse: the triggers played through a written trace (ML-016)
// The author writes what happens (players stand here, the phone is picked up, an officer goes down) and what should follow
// (trigger 2 counts down, the extraction is held, five enemies). Run sends the scenario and the script to the plugin, which
// plays it with Core Rehearsal: the same TriggerEngine the live mission uses, the variants rolled as the Director rolls them.
// The page never judges the rules itself, so it cannot drift from the game; a browser-native evaluator is later work and
// needs parity fixtures first. Offline the page builds, checks and downloads scripts only.
// Every event is an assumption the author injects. Nothing here predicts spotting, pathfinding, physics, networking or combat.
const REHEARSAL_SCHEMA = 'missionlab-rehearsal/1';
const RH_PHASES = ['off', 'circle', 'waiting', 'holding', 'counting', 'fired', 'cooling', 'exhausted'];
const RH_EVENTS = { players: 'Players stand', alert: 'Alarm', intel: 'Intel taken', down: 'Enemies down', door: 'Door opened', fire: 'Fired by hand', outcome: 'Outcome reported' };
// What a step may report for the latest firing of a trigger (ML-047). A rehearsal never assumes an outcome by itself: an
// outcome trigger waits until a row reports one, and "timeout" is not reported but judged on the rehearsal's clock.
const RH_OUTCOMES = ['applied', 'failed', 'partial', 'unconfirmed'];
const RH_CHECKS = { fired: 'Check: fired', notFired: 'Check: not fired', phase: 'Check: phase', goal: 'Check: objective', extraction: 'Check: extraction', enemies: 'Check: enemies' };
// An objective's states as Core's GoalBook names them (ML-046); a check expects one of them at its moment.
const RH_GOALS = ['inactive', 'active', 'succeeded', 'failed'];
const RH_OFFLINE = 'Running needs the game: start OPERATOR with MissionLab and open http://localhost:8765. On its own this page checks the rows and saves the test as a file.';
state.rehearse = { rows: [], name: '', players: 2, roll: 0, pin: null, picking: null, focus: null, down: null, answer: null, failed: new Set(), saved: [], listedFor: null, choices: '',
  sources: null, prev: null, answerKey: '', answerSources: null, divergence: null, report: null, cursor: -1 };   // the run before (same steps), for the first difference (ML-043)

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
    case 'outcome': return row.id > 0 && RH_OUTCOMES.includes(row.result) ? { at, outcome: { trigger: row.id, result: row.result } } : null;
    case 'fired': case 'notFired': return row.id > 0 ? { at, expect: { [row.kind]: [row.id] } } : null;
    case 'phase': return row.id > 0 && RH_PHASES.includes(row.phase) ? { at, expect: { phase: { [row.id]: row.phase } } } : null;
    case 'goal': return typeof row.goal === 'string' && GOAL_ID.test(row.goal) && RH_GOALS.includes(row.state) ? { at, expect: { goals: { [row.goal]: row.state } } } : null;
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
    if (st.outcome && st.outcome.trigger > 0) o += ',"outcome":{"trigger":' + (st.outcome.trigger | 0) + ',"result":' + q(String(st.outcome.result || '')) + '}';
    const x = st.expect;
    if (x) {
      const parts = [];
      if (Array.isArray(x.fired) && x.fired.length) parts.push('"fired":[' + x.fired.join(',') + ']');
      if (Array.isArray(x.notFired) && x.notFired.length) parts.push('"notFired":[' + x.notFired.join(',') + ']');
      const ids = x.phase ? Object.keys(x.phase).map(Number).sort((a, b) => a - b) : [];
      if (ids.length) parts.push('"phase":{' + ids.map(id => q(String(id)) + ':' + q(x.phase[id])).join(',') + '}');
      // Goal ids in ordinal order, as Rehearsal.Write takes them from its sorted dictionary.
      const goals = x.goals && typeof x.goals === 'object' ? Object.keys(x.goals).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)) : [];
      if (goals.length) parts.push('"goals":{' + goals.map(id => q(id) + ':' + q(x.goals[id])).join(',') + '}');
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
    if (st.outcome && typeof st.outcome === 'object' && Number.isInteger(st.outcome.trigger) && st.outcome.trigger > 0) rows.push({ at, kind: 'outcome', id: st.outcome.trigger, result: typeof st.outcome.result === 'string' ? st.outcome.result : '' });
    const x = st.expect;
    if (!x || typeof x !== 'object') continue;
    for (const id of Array.isArray(x.fired) ? x.fired : []) rows.push({ at, kind: 'fired', id });
    for (const id of Array.isArray(x.notFired) ? x.notFired : []) rows.push({ at, kind: 'notFired', id });
    if (x.phase && typeof x.phase === 'object') for (const k of Object.keys(x.phase)) rows.push({ at, kind: 'phase', id: +k, phase: x.phase[k] });
    if (x.goals && typeof x.goals === 'object') for (const k of Object.keys(x.goals)) rows.push({ at, kind: 'goal', goal: k, state: x.goals[k] });
    if (x.extraction === 'open' || x.extraction === 'held') rows.push({ at, kind: 'extraction', value: x.extraction });
    if (Number.isInteger(x.enemies)) rows.push({ at, kind: 'enemies', count: x.enemies });
  }
  return rows;
}
// What stops a trace from being run or saved, row by row (numbers count from 1). ids: the scenario's trigger numbers;
// goals: its objective ids (the page's, when not given).
function rehearseProblems(rows, ids, goals) {
  const goalIds = Array.isArray(goals) ? goals : state.goals.map(g => g.id);
  const out = [], has = id => ids.includes(id);
  if (!rows.length) out.push({ row: 0, text: 'Add at least one row.' });
  rows.forEach((row, i) => {
    const bad = text => out.push({ row: i + 1, text: 'Row ' + (i + 1) + ': ' + text });
    if (!(typeof row.at === 'number' && isFinite(row.at)) || row.at < 0 || row.at > 86400) bad('the time is from 0 to 86400 seconds.');
    if (row.kind === 'players' && !Array.isArray(row.players)) bad('place the players: click the map or choose a place, or choose Nobody alive.');
    if (row.kind === 'door' && !isVec(row.pos)) bad('choose the door that is opened.');
    if (['fire', 'fired', 'notFired', 'phase', 'outcome'].includes(row.kind) && !has(row.id)) bad(row.id > 0 ? 'there is no trigger ' + row.id + ' in this scenario.' : 'choose a trigger.');
    if (row.kind === 'outcome' && !RH_OUTCOMES.includes(row.result)) bad('choose the outcome reported.');
    if (row.kind === 'phase' && !RH_PHASES.includes(row.phase)) bad('choose a phase.');
    if (row.kind === 'goal' && !goalIds.includes(row.goal)) bad(row.goal ? 'there is no objective "' + row.goal + '" in this scenario.' : 'choose an objective.');
    if (row.kind === 'goal' && !RH_GOALS.includes(row.state)) bad('choose the objective\'s state.');
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
// ----- the answer moment by moment (ML-043): Core sends, for every time at which a step happens, each trigger's phase just
// before the step and once the engine has looked, and how many living players stand inside each zone or hold.
const rhLooks = a => (a && Array.isArray(a.looks) ? a.looks.filter(l => l && typeof l.at === 'number') : []);
// The moment at a time, or null (an older plugin sends no moments).
function rehearseLook(a, at) { return rhLooks(a).find(l => Math.abs(l.at - at) < 0.005) || null; }
// What a moment changed, in words. name(id) names a trigger. { changed: ['trigger 1: waiting → fired (1 of 1 inside)'], same: ['trigger 2 waiting'] }
function rehearseLookWords(look, name) {
  const out = { changed: [], same: [] };
  if (!look) return out;
  const before = look.before || {}, after = look.after || {}, inside = look.inside || {};
  const ids = [...new Set([...Object.keys(before), ...Object.keys(after)])].map(Number).sort((p, q) => p - q);
  for (const id of ids) {
    const b = before[id], f = after[id], m = /^(\d+)\/(\d+)$/.exec(inside[id] || '');
    const who = m ? ' (' + m[1] + ' of ' + m[2] + (m[2] === '1' ? ' living player' : ' living players') + ' inside)' : '';
    if (b !== f) out.changed.push(name(id) + ': ' + (b || '?') + ' → ' + (f || '?') + who);
    else out.same.push(name(id) + ' ' + (f || '?') + who);
  }
  return out;
}
// The same for the objectives (ML-046), from the goal states just before the moment and once the goals were looked at.
// name(id) names an objective. An older plugin, or a scenario without objectives, sends none: both lists are then empty.
function rehearseGoalWords(look, name) {
  const out = { changed: [], same: [] };
  if (!look || !look.goalsAfter || typeof look.goalsAfter !== 'object') return out;
  const before = look.goalsBefore || {}, after = look.goalsAfter;
  const ids = [...new Set([...Object.keys(before), ...Object.keys(after)])].sort((p, q) => (p < q ? -1 : p > q ? 1 : 0));
  for (const id of ids) {
    if (before[id] !== after[id]) out.changed.push(name(id) + ': ' + (before[id] || '?') + ' → ' + (after[id] || '?'));
    else out.same.push(name(id) + ' ' + (after[id] || '?'));
  }
  return out;
}
// Every objective transition of an answer ({id, from, to, at}), in the order they happened; [] from an older plugin.
const rehearseGoalEvents = a => (a && Array.isArray(a.goals) ? a.goals.filter(e => e && typeof e.id === 'string' && typeof e.at === 'number') : []);
// An objective changed state at this moment.
const rhGoalMoved = (a, look, at) => rehearseGoalEvents(a).some(e => Math.abs(e.at - at) < 0.005) ||
  !!(look && look.goalsAfter && Object.keys(look.goalsAfter).some(id => (look.goalsBefore || {})[id] !== look.goalsAfter[id]));
// The steps of an answer worth stopping at: checks, injected events and moves after which a trigger's phase, an objective's
// state or the number of players inside changed. Plain moves that change nothing are passed over. Indexes into a.steps.
function rehearseEvents(a) {
  const steps = a && Array.isArray(a.steps) ? a.steps : [], looks = rhLooks(a), out = [];
  const moved = /: (?:\d+ living players? stands?|no player is alive)/;
  steps.forEach((x, i) => {
    if (x.expected || !moved.test(String(x.what || ''))) { out.push(i); return; }
    const k = looks.findIndex(l => Math.abs(l.at - x.at) < 0.005), l = looks[k];
    if (rhGoalMoved(a, l, x.at)) { out.push(i); return; }
    if (!l) { out.push(i); return; }
    const was = k > 0 ? looks[k - 1].inside || {} : {};
    const ids = Object.keys(l.after || {});
    if (ids.some(id => (l.before || {})[id] !== l.after[id] || (k > 0 && (l.inside || {})[id] !== was[id]))) out.push(i);
  });
  return out;
}
// Two runs of one script compare only when the steps, players and roll are the same; the name and fingerprint are not.
const rehearseKey = s => JSON.stringify([s.players, s.roll || 0, s.steps]);
// Where two answers to the same script first part ways (before and after an edit to a trigger or an area): the earliest
// moment at which a trigger's phase or an objective's state after the look differs, or the earliest step whose result
// differs, whichever comes first. null when they agree. { at, index (into later.steps), step (its number), text }.
// goalName(id) names an objective.
function rehearseDivergence(earlier, later, name, goalName) {
  name = name || (id => 'trigger ' + id);
  goalName = goalName || (id => 'objective ' + id);
  const secs = t => String(Math.round(t * 100) / 100) + ' s';
  const sa = earlier && Array.isArray(earlier.steps) ? earlier.steps : [], sb = later && Array.isArray(later.steps) ? later.steps : [];
  const number = x => { const m = /^step (\d+) /.exec(String(x && x.what || '')); return m ? +m[1] : 0; };
  let look = null;
  const la = rhLooks(earlier), lb = rhLooks(later);
  const times = [...new Set([...la.map(l => l.at), ...lb.map(l => l.at)])].sort((p, q) => p - q);
  for (const t of times) {
    const a = la.find(l => Math.abs(l.at - t) < 0.005), b = lb.find(l => Math.abs(l.at - t) < 0.005);
    if (!a || !b) { look = { at: t, what: 'only ' + (a ? 'the earlier' : 'the later') + ' run has a moment here' }; break; }
    const ids = [...new Set([...Object.keys(a.after || {}), ...Object.keys(b.after || {})])].map(Number).sort((p, q) => p - q);
    const id = ids.find(k => (a.after || {})[k] !== (b.after || {})[k]);
    if (id !== undefined) { look = { at: t, what: name(id) + ' was ' + ((a.after || {})[id] || 'not there') + ', now ' + ((b.after || {})[id] || 'not there') }; break; }
    const ga = a.goalsAfter || {}, gb = b.goalsAfter || {};
    const goal = [...new Set([...Object.keys(ga), ...Object.keys(gb)])].sort((p, q) => (p < q ? -1 : p > q ? 1 : 0)).find(k => ga[k] !== gb[k]);
    if (goal !== undefined) { look = { at: t, what: goalName(goal) + ' was ' + (ga[goal] || 'not there') + ', now ' + (gb[goal] || 'not there') }; break; }
  }
  let step = -1;
  for (let i = 0; i < Math.max(sa.length, sb.length); i++) {
    const a = sa[i], b = sb[i];
    if (!a || !b || a.what !== b.what || !!a.ok !== !!b.ok || a.actual !== b.actual) { step = i; break; }
  }
  const stepAt = step >= 0 ? num((sb[step] || sa[step]).at, 0) : Infinity;
  if (!look && step < 0) return null;
  if (look && look.at <= stepAt) {
    let index = sb.findIndex(x => Math.abs(num(x.at, -1) - look.at) < 0.005);
    if (index < 0) index = Math.max(0, sb.findIndex(x => num(x.at, 0) > look.at));
    const n = number(sb[index]);
    return { at: look.at, index, step: n, text: 'First difference at ' + secs(look.at) + (n ? ' (step ' + n + ')' : '') + ': ' + look.what + '.' };
  }
  const a = sa[step], b = sb[step], n = number(b || a);
  const said = x => (!x ? 'no such step' : (x.ok ? 'as expected' : 'not as expected') + (x.actual ? ' (' + x.actual + ')' : ''));
  return { at: stepAt, index: b ? step : Math.max(0, sb.length - 1), step: n, text: 'First difference at ' + secs(stepAt) + ': ' + String((b || a).what) + '; before: ' + said(a) + '; now: ' + said(b) + '.' };
}
// What a rehearsal models and what it does not, said beside every result.
const RH_MODELS = 'Modelled: which triggers fire and when, their phases, the objectives, the extraction and the enemy count, by the same rules the game runs. ' +
  'Not modelled: combat, navigation (test players walk the straight lines drawn, through walls if drawn so) and client presentation (messages, sounds, what players see).';
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
      case 'outcome':
        what.append(trig('id', 'Trigger whose latest firing gets the outcome'), rhSelect([['', 'choose an outcome'], ...RH_OUTCOMES.map(w => [w, w])], row.result || '', v => { row.result = v; rhEdited(); }, 'Outcome reported'),
          rhEl('span', '', 'for its latest firing at that moment'));
        break;
      case 'phase':
        what.append(trig('id', 'Trigger'), rhSelect([['', 'choose a phase'], ...RH_PHASES.map(p => [p, p])], row.phase || '', v => { row.phase = v; rhEdited(); }, 'Phase'));
        break;
      case 'goal': {
        const goals = state.goals.map(g => [g.id, g.name + ' (' + g.id + ')']);
        if (row.goal && !state.goals.some(g => g.id === row.goal)) goals.push([row.goal, row.goal + ' (not in this scenario)']);
        what.append(rhSelect([['', 'choose an objective'], ...goals], row.goal || '', v => { row.goal = v; rhEdited(); }, 'Objective'),
          rhSelect([['', 'choose a state'], ...RH_GOALS.map(s => [s, s])], row.state || '', v => { row.state = v; rhEdited(); }, 'State of the objective'));
        if (!state.goals.length) what.append(rhEl('span', '', 'This scenario has no objectives: add them on the Mission tab.'));
        break;
      }
      case 'extraction': what.append(rhSelect([['held', 'held'], ['open', 'open']], row.value === 'open' ? 'open' : 'held', v => { row.value = v; rhEdited(); }, 'Extraction')); break;
      case 'enemies': what.append(rhNumber(row.count | 0, 0, 10000, 1, v => { row.count = Math.max(0, v | 0); rhEdited(); }, 'Enemies in the mission'), rhEl('span', '', 'people in the mission by then (gun trucks are not counted)')); break;
    }
    li.append(rhEl('b', '', String(i + 1)), at, rhEl('span', 'kind', check ? RH_CHECKS[row.kind] : RH_EVENTS[row.kind] + ' (assumed)'), del, what);
    list.appendChild(li);
  });
  rhProblemsShow();
  const offline = !live.on;
  $('rhRun').disabled = offline; $('rhRerun').disabled = offline; $('rhExport').hidden = !offline;
  $('rhRun').title = offline ? RH_OFFLINE : 'Plays the trace in the plugin with the same trigger rules the game uses.';
  $('rhSave').textContent = offline ? 'Download test' : 'Save as test';
  r.choices = rhChoicesKey();
}
function rhProblemsShow() {
  const r = state.rehearse, found = rehearseProblems(r.rows, rhTriggers().map(z => z.id)).concat(walkProblems(state.walk, r.rows));
  const checks = r.rows.some(row => row.kind in RH_CHECKS);
  $('rhProblems').textContent = found.length ? found.map(f => f.text).join(' ') : r.rows.length && !checks ? 'This trace checks nothing yet: add a Check row.' : '';
  return found;
}
// What the rows' choices depend on; when it changes (a trigger, door, place, tag or objective was edited) the rows are drawn again.
const rhChoicesKey = () => JSON.stringify([state.zones.map(z => [z.id, z.name, z.when, z.item]), state.doors.length, state.places.map(p => [p.id, p.name]), tagsOf(), state.objectives.length, state.goals.map(g => [g.id, g.name]), live.on]);
function rhRefresh() { const r = state.rehearse; if (r.choices !== rhChoicesKey()) rhRender(); if (live.on && r.listedFor !== state.scenarioId) rhList(); }

// ----- placing players by clicking the 2D map: one click per player, as many as the Players field says
function rhStartPicking(row) {
  const r = state.rehearse;
  walkStopPick();
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
// so a run says when the scenario has changed since; a new trace is written for the scenario as it is now. The test
// players' paths (29j-walk.js) are sampled into it; r.sources says which row, if any, each step came from.
function rhScript(text) {
  const r = state.rehearse, pin = r.pin || { scenario: textHash(text), revision: state.revision };   // textHash: ScenarioFile.Hash (31a-stage.js)
  const w = walkScript(state.walk, r.rows, { name: r.name.trim(), scenario: pin.scenario, revision: pin.revision, players: r.players, roll: r.roll });
  r.sources = w.sources;
  return w.script;
}
function rhReady() {
  const found = rhProblemsShow();
  if (found.length) { $('rhStatus').textContent = 'Not yet: ' + found[0].text; return false; }
  return true;
}
function rhDownload(name, text) {
  const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' })); a.download = name;
  document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(a.href), 4000);
}
async function rhRun() {
  const r = state.rehearse;
  if (!rhReady()) return;
  if (!live.on) { rhExport(); return; }
  const scenario = scenarioText(), s = rhScript(scenario), script = rehearseText(s), key = rehearseKey(s), sources = r.sources;
  $('rhStatus').textContent = 'Asked the game to rehearse it...';
  try {
    const { status, data } = await rhCall('/api/rehearse', { scenario, script });
    if (status !== 200 || !data) throw new Error((data && data.error) || 'status ' + status);
    // The run before is kept only when it played the same steps: then the two can be compared after an edit.
    r.prev = r.answer && r.answerKey === key ? r.answer : null;
    r.answer = data; r.answerKey = key; r.answerSources = sources; r.cursor = -1; $('rhStatus').textContent = '';
    rhShow();
  } catch (e) { $('rhStatus').textContent = 'The game did not rehearse it (' + e.message + '). Is OPERATOR with MissionLab still running?'; }
}
// Offline nothing can be run: the trace is downloaded as it would be sent, and the result says plainly that it did not run.
function rhExport() {
  const r = state.rehearse, script = rehearseText(rhScript(scenarioText()));
  const name = fileName().replace(/\.json$/, '') + '.' + rhStem(r.name.trim() || 'walkthrough') + '.trace.rehearsal.json';
  rhDownload(name, script);
  r.answer = null; r.prev = null; r.answerKey = ''; r.answerSources = null; r.divergence = null; r.failed.clear(); r.cursor = -1;
  r.report = { level: 'not-run', title: 'Not run: this page is not connected to the game, so nothing was rehearsed and nothing passed or failed. The trace was exported as ' + name +
    '; open the page from the game (http://localhost:8765) and press Run there.' };
  const box = $('rhResult');
  box.textContent = '';
  box.append(rhEl('p', 'notrun', r.report.title), rhEl('p', 'models', RH_MODELS));
  $('rhStatus').textContent = '';
  rhRender();
}
const rhTriggerName = id => { const z = state.zones.find(q => q.id === id); return z ? 'trigger ' + rhTriggerLabel(z) : 'trigger ' + id; };
const rhGoalName = id => { const g = state.goals.find(q => q.id === id); return 'objective "' + (g ? g.name : id) + '"'; };
function rhShow() {
  const r = state.rehearse, a = r.answer, rep = rehearseReport(a), box = $('rhResult'), src = r.answerSources;
  box.textContent = '';
  // "step N" is row N of a plain trace; the steps the test players' paths add belong to no row.
  r.failed = new Set(rep.failedRows.map(n => (src ? (src[n - 1] && src[n - 1].row) || 0 : n)).filter(n => n > 0));
  r.report = { level: rep.level, title: rep.title };
  if (!(r.cursor < rep.steps.length)) r.cursor = -1;
  box.append(rhEl('p', rep.level === 'passed' ? 'ok' : 'bad', rep.title));
  for (const n of rep.notes) box.append(rhEl('p', '', n));
  if (rep.assumptions) box.append(rhEl('p', 'assume', 'Assumed: ' + rep.assumptions));
  box.append(rhEl('p', 'models', RH_MODELS));
  r.divergence = r.prev ? rehearseDivergence(r.prev, a, rhTriggerName, rhGoalName) : null;
  if (r.prev) {
    const p = rhEl('p', 'diverge', r.divergence ? r.divergence.text : 'The same results as the run before, step for step.');
    if (r.divergence) { const go = rhEl('button', 'btn', 'Show it'); go.type = 'button'; go.addEventListener('click', () => rhPick(r.divergence.index)); p.append(' ', go); }
    box.append(p);
  }
  // Every objective transition, in the order it happened; each goes to its moment.
  const turns = rehearseGoalEvents(a);
  if (turns.length) {
    box.append(rhEl('p', 'sub', 'Objectives, as they changed:'));
    const ul = rhEl('ul', 'goals');
    for (const e of turns) {
      const li = rhEl('li'), go = rhEl('button', 'btn', String(Math.round(e.at * 100) / 100) + ' s: ' + rhGoalName(e.id) + ': ' + e.from + ' → ' + e.to); go.type = 'button';
      const i = rep.steps.findIndex(x => Math.abs(num(x.at, -1) - e.at) < 0.005);
      go.disabled = i < 0; go.addEventListener('click', () => rhPick(i));
      li.append(go); ul.append(li);
    }
    box.append(ul);
  } else if (Array.isArray(a && a.goals)) box.append(rhEl('p', 'got', 'No objective changed state.'));
  if (rep.steps.length) {
    const events = rehearseEvents(a), firstBad = rep.steps.findIndex(x => x.check && !x.ok), nav = rhEl('div', 'row');
    const button = (text, label, fn, off) => { const b = rhEl('button', 'btn', text); b.type = 'button'; b.setAttribute('aria-label', label); b.disabled = !!off; b.addEventListener('click', fn); nav.append(b); };
    button('◀ Previous event', 'Previous event', () => { const i = events.filter(k => k < r.cursor).pop(); if (i !== undefined) rhPick(i); }, !events.length);
    button('Next event ▶', 'Next event', () => { const i = events.find(k => k > r.cursor); if (i !== undefined) rhPick(i); }, !events.length);
    button('First failed check', 'Jump to the first failed check', () => rhPick(firstBad), firstBad < 0);
    const detail = rhEl('div', 'detail'); detail.id = 'rhDetail';
    box.append(nav, detail);
    const ol = rhEl('ol'); ol.id = 'rhSteps';
    rep.steps.forEach((x, i) => {
      const li = rhEl('li', i === r.cursor ? 'sel' : ''), body = rhEl('span', '', x.what);
      if (x.detail) { body.append(document.createElement('br'), rhEl('span', 'got', x.detail)); }
      li.append(rhEl('span', 'mark ' + (x.ok ? 'ok' : 'bad'), x.check ? (x.ok ? '✓' : '✗') : '·'), body);
      li.addEventListener('click', () => rhPick(i));
      ol.appendChild(li);
      if (i === r.cursor) setTimeout(() => li.scrollIntoView({ block: 'nearest' }), 0);
    });
    box.appendChild(ol);
    rhDetailShow(detail, rep);
  }
  rhRender();
}
// The selected step: what it did or found, and every trigger just before and just after its moment.
function rhDetailShow(el, rep) {
  const r = state.rehearse, x = rep.steps[r.cursor];
  el.textContent = '';
  if (!x) { el.append(rhEl('p', 'hint', 'Choose a step, or use Next event, to see the triggers just before and just after it.')); return; }
  el.append(rhEl('p', '', x.what), ...(x.detail ? [rhEl('p', 'got', x.detail)] : []));
  const look = rehearseLook(r.answer, x.at);
  if (!look) { el.append(rhEl('p', 'hint', 'The game sent no before and after state for this moment; a MissionLab older than this page does not.')); return; }
  const w = rehearseLookWords(look, rhTriggerName);
  el.append(rhEl('p', '', w.changed.length ? 'Changed at this moment: ' + w.changed.join('; ') + '.' : 'No trigger changed phase at this moment.'));
  if (w.same.length) el.append(rhEl('p', 'got', 'Unchanged: ' + w.same.join('; ') + '.'));
  const g = rehearseGoalWords(look, rhGoalName);
  if (g.changed.length) el.append(rhEl('p', '', 'Objectives changed at this moment: ' + g.changed.join('; ') + '.'));
  if (g.same.length) el.append(rhEl('p', 'got', 'Objectives unchanged: ' + g.same.join('; ') + '.'));
  const at = walkAt(state.walk, x.at);
  if (at.length) el.append(rhEl('p', 'got', 'Test players then: ' + at.map(p => 'T' + (p.k + 1) + ' ' + rhWhere(p.pos) + ' at ' + p.pos[1].toFixed(1) + ' m').join('; ') + '.'));
}
function rhPick(i) {
  const r = state.rehearse, rep = rehearseReport(r.answer), x = rep.steps[i];
  if (!x) return;
  r.cursor = i; state.walk.at = x.at;
  const m = /^step (\d+) /.exec(x.what), n = m ? +m[1] : 0, src = r.answerSources, row = src ? (src[n - 1] && src[n - 1].row) || 0 : n;
  if (row > 0 && r.rows[row - 1]) r.focus = r.rows[row - 1];
  rhShow(); invalidate();
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
    rhDownload(name, script);
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
  const walked = state.walk.players.length;    // the paths belonged to the trace this file replaces
  walkClear();
  r.rows = rehearseRows(o); rhSort();
  r.name = typeof o.name === 'string' ? o.name : '';
  r.players = Number.isInteger(o.players) && o.players >= 1 && o.players <= 16 ? o.players : 1;
  r.roll = Number.isInteger(o.roll) ? o.roll : 0;
  r.pin = { scenario: typeof o.scenario === 'string' ? o.scenario : '', revision: Number.isInteger(o.revision) ? o.revision : 0 };
  r.answer = null; r.prev = null; r.answerKey = ''; r.answerSources = null; r.divergence = null; r.report = null; r.cursor = -1;
  r.failed.clear(); r.focus = null; $('rhResult').textContent = '';
  $('rhName').value = r.name; $('rhPlayers').value = String(r.players); $('rhRoll').value = String(r.roll);
  $('rhStatus').textContent = 'Opened ' + from + ' (' + r.rows.length + ' rows).' + (walked ? ' The test players\' paths of the trace before were cleared with it.' : '') +
    (r.rows.some(row => row.kind === 'players') ? ' To edit its player places as paths, press "Turn player rows into paths" under Walk it through.' : '');
  rhPinShow(); rhRender(); invalidate();
}

$('rhAddBtn').addEventListener('click', () => {
  const r = state.rehearse, kind = $('rhAdd').value, last = r.rows.length ? num(r.rows[r.rows.length - 1].at, 0) : 0;
  const first = rhTriggers()[0], intel = state.zones.find(z => z.when === 'intel' && z.item), goal = state.goals[0];
  const row = { at: last, kind, ...({ players: { players: null }, intel: { item: intel ? intel.item : '' }, down: { tag: '', count: 1 }, door: { pos: null }, fire: { id: first ? first.id : 0 }, outcome: { id: first ? first.id : 0, result: 'applied' },
    fired: { id: first ? first.id : 0 }, notFired: { id: first ? first.id : 0 }, phase: { id: first ? first.id : 0, phase: 'waiting' }, goal: { goal: goal ? goal.id : '', state: 'succeeded' },
    extraction: { value: 'held' }, enemies: { count: 0 } }[kind] || {}) };
  r.rows.push(row); r.focus = row;
  rhEdited(); rhRender();
  if (kind === 'players') rhStartPicking(row);
});
$('rhName').addEventListener('input', () => { state.rehearse.name = $('rhName').value; });
$('rhPlayers').addEventListener('change', () => { const v = Math.round(Number($('rhPlayers').value)); state.rehearse.players = v >= 1 && v <= 16 ? v : 1; $('rhPlayers').value = String(state.rehearse.players); rhEdited(); });
$('rhRoll').addEventListener('change', () => { const v = Math.trunc(Number($('rhRoll').value)); state.rehearse.roll = isFinite(v) ? v : 0; $('rhRoll').value = String(state.rehearse.roll); rhEdited(); });
$('rhRun').addEventListener('click', rhRun);
$('rhExport').addEventListener('click', () => { if (rhReady()) rhExport(); });
$('rhSave').addEventListener('click', rhSave);
$('rhRerun').addEventListener('click', rhRerun);
$('rhOpen').addEventListener('click', () => $('rhFile').click());
$('rhFile').addEventListener('change', async ev => { const f = ev.target.files && ev.target.files[0]; ev.target.value = ''; if (f) rhLoad(await f.text(), f.name); });
$('rhClear').addEventListener('click', () => {
  const r = state.rehearse;
  rhStopPicking(); walkClear(); r.rows = []; r.pin = null; r.answer = null; r.prev = null; r.answerKey = ''; r.answerSources = null; r.divergence = null; r.report = null; r.cursor = -1;
  r.failed.clear(); r.focus = null; r.name = ''; $('rhName').value = '';
  $('rhResult').textContent = ''; $('rhStatus').textContent = ''; rhPinShow(); rhRender(); invalidate();
});
$('rhBox').addEventListener('pointerenter', rhRefresh);
$('rhBox').addEventListener('focusin', rhRefresh);

expose({ rhRun, rhExport, rhScript, rhShow, rhPick, rehearseLook, rehearseLookWords, rehearseEvents, rehearseKey, rehearseDivergence });   // the result view (ML-043), for automated tests
expose({ rehearseStep, rehearseGoalWords, rehearseGoalEvents, RH_GOALS });   // objective checks and transitions (ML-046), for automated tests
expose({ RH_OUTCOMES });   // outcome rows (ML-047), for automated tests
