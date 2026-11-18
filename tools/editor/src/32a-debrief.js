// ------------------------------------------------------------------ debrief and unused content (ML-024)
// The page's copy of Core's Debrief (src/MissionLab.Core/Debrief.cs): a short, factual debrief of one recording, where every number
// names the event or record field it was counted from, and the same plain text the plugin writes (tests/fixtures/debrief holds the
// expected text for both). Health, shots fired and players' use of doors are not recorded, so they are named as not recorded.
// Connected to the game, the Replay tab asks the game for the debrief, which also reads the run record; offline it is worked out here
// from the recording alone. The Mission tab's 'Unused content' asks the game for the scenario's comparable runs, or, offline, looks
// at one recording the author opens. Its items are suggestions for review, never faults.
const DEBRIEF_NOT_RECORDED = 'health, shots fired, door use by players';
const DB_WHO = 3, DB_FIRE = 4;        // recording semantics from which rows say who an enemy is, and from which alerts and firings are events
const DB_TRIGGER_KINDS = ['zone', 'alert', 'intel', 'time', 'after', 'hold', 'down', 'all', 'any', 'door'];
const dbMmss = t => mmss(Math.max(0, t));
const dbNum = v => typeof v === 'number' && isFinite(v);
const dbStr = (o, k) => (o && typeof o[k] === 'string' ? o[k] : '');

// The parts of a plan the debrief needs, read as MissionPlan.FromJson reads them; null when it would not read.
function debriefPlan(p) {
  if (p && typeof p === 'object' && p.plan && typeof p.plan === 'object' && !Array.isArray(p.plan)) p = p.plan;
  if (p && typeof p === 'object' && p.mission && typeof p.mission === 'object' && !Array.isArray(p.mission)) p = p.mission;
  if (!p || typeof p !== 'object' || !Array.isArray(p.spawns)) return null;
  const spawns = [];
  for (const e of p.spawns) {
    if (!e || typeof e !== 'object' || Array.isArray(e)) continue;
    if (!isVec(e.pos)) return null;
    if (typeof e.role === 'string' && !/^(guard|wander|patrol)$/i.test(e.role)) return null;
    spawns.push({ tag: cleanTag(typeof e.tag === 'string' ? e.tag : ''), trigger: dbNum(e.trigger) ? Math.trunc(e.trigger) : 0 });
  }
  const triggers = [];
  for (const z of Array.isArray(p.triggers) ? p.triggers : []) {
    if (!z || typeof z !== 'object' || Array.isArray(z)) continue;
    const when = DB_TRIGGER_KINDS.includes(z.when) ? z.when : 'zone', id = dbNum(z.id) ? Math.trunc(z.id) : 0;
    if (id <= 0 || ((when === 'zone' || when === 'hold' || when === 'door') && !isVec(z.pos))) continue;
    triggers.push({ id, name: typeof z.name === 'string' ? z.name : '' });
  }
  const sid = typeof p.id === 'string' ? p.id.trim() : '';
  return { id: /^[A-Za-z0-9_-]{1,64}$/.test(sid) ? sid : '', triggers, spawns, places: placesIn(p.places) };
}

// A timeline line of an older recording that says a trigger fired: "zone 'Gate' reached: ..." or "trigger 3 (time): ...".
function debriefFiredWords(text) {
  const rest = text.startsWith('zone ') ? text.slice(5) : text.startsWith('trigger ') ? text.slice(8) : null;
  if (rest === null || !(text.includes(' enemy appears') || text.includes(' enemies appear'))) return null;
  if (rest.startsWith("'")) { const close = rest.indexOf("' ", 1); return close < 1 ? null : { id: 0, name: rest.slice(1, close) }; }
  const m = /^\d+/.exec(rest), id = m ? parseInt(m[0], 10) : 0;
  return id > 0 ? { id, name: '' } : null;
}

// One recording's debrief. run: its run record (an object), or null when there is none. Throws when it is not a recording.
function debriefOf(obj, run, name) {
  if (!obj || typeof obj !== 'object' || obj.schema !== 'missionlab-replay/1') throw new Error('This is not a MissionLab recording (missionlab-replay/1).');
  const vec = a => (isVec(a) ? a.slice(0, 3) : null);
  const frames = [];
  for (const f of Array.isArray(obj.frames) ? obj.frames : []) {
    if (!Array.isArray(f) || f.length < 3 || !dbNum(f[0]) || !Array.isArray(f[1]) || !Array.isArray(f[2])) continue;
    const rows = f[2].filter(q => Array.isArray(q) && q.length >= 4 && [0, 1, 2, 3].every(k => dbNum(q[k])))
      .map(q => ({ id: Math.trunc(q[0]), pos: q.slice(1, 4), state: q.length > 5 && typeof q[5] === 'string' ? q[5] : '', who: q.length > 6 && typeof q[6] === 'string' && whoName(q[6]) ? q[6] : '' }));
    frames.push({ t: f[0], rows });
  }
  if (!frames.length) throw new Error('It is a recording without any readable moments.');
  const events = (Array.isArray(obj.events) ? obj.events : []).filter(e => Array.isArray(e) && e.length >= 3 && dbNum(e[0]))
    .map(e => ({ t: e[0], kind: typeof e[1] === 'string' ? e[1] : '', text: typeof e[2] === 'string' ? e[2] : '', at: e.length > 3 ? vec(e[3]) : null, id: e.length > 4 && Number.isInteger(e[4]) ? e[4] : 0 }));
  const semantics = Number.isInteger(obj.semantics) && obj.semantics >= 1 ? obj.semantics : 1;
  const d = { replay: name || '', run: dbStr(obj, 'run'), scene: dbStr(obj, 'scene'), plugin: dbStr(obj, 'plugin'), scenario: dbStr(obj, 'scenario'), scenarioId: '', scenarioHash: dbStr(obj, 'scenarioHash'),
    semantics, hasRun: false, plan: null, facts: [], milestones: [], players: (Array.isArray(obj.players) ? obj.players : []).filter(p => typeof p === 'string'), notes: [],
    off: [], leftOut: (Array.isArray(obj.leftOut) ? obj.leftOut : []).filter(x => Number.isInteger(x) && x >= 0), fired: new Set(), enemies: new Map() };
  let runPlayers = -1, runSeconds = -1, runResult = '';
  if (run && typeof run === 'object' && !Array.isArray(run)) {
    d.hasRun = true;
    if (dbStr(run, 'authoredHash')) d.scenarioHash = dbStr(run, 'authoredHash');
    if (dbStr(run, 'scenario')) d.scenario = dbStr(run, 'scenario');
    d.scenarioId = dbStr(run, 'scenarioId');
    if (Number.isInteger(run.players)) runPlayers = run.players;
    if (run.end && typeof run.end === 'object' && !Array.isArray(run.end)) { runResult = dbStr(run.end, 'result'); runSeconds = dbNum(run.end.seconds) && run.end.seconds >= 0 ? run.end.seconds : -2; }
    const lo = run.leftOut && typeof run.leftOut === 'object' ? run.leftOut : null;
    if (lo && Array.isArray(lo.triggersOff)) d.off = lo.triggersOff.filter(x => Number.isInteger(x));
    if (lo && !d.leftOut.length && Array.isArray(lo.spawns)) d.leftOut = lo.spawns.filter(s => s && Number.isInteger(s.index) && s.index >= 0).map(s => s.index);
    if (lo) d.leftOut.sort((a, b) => a - b);
    if (run.resolved && typeof run.resolved === 'object' && !Array.isArray(run.resolved)) d.plan = debriefPlan(run.resolved);
  }
  if (!d.plan && obj.plan && typeof obj.plan === 'object' && !Array.isArray(obj.plan)) d.plan = debriefPlan(obj.plan);
  if (!d.scenarioId && d.plan) d.scenarioId = d.plan.id;
  debriefWork(d, frames, events, runPlayers, runSeconds, runResult);
  return d;
}

function debriefWork(d, frames, events, runPlayers, runSeconds, runResult) {
  const guessed = d.semantics < DB_WHO, fires = d.semantics >= DB_FIRE;
  const add = (key, label, value, source, count, at, guess) => d.facts.push({ key, label, value, source, count, at, guess });
  const placeAt = p => { if (!d.plan || !p) return ''; for (const pl of d.plan.places) if (placeContains(pl, p)) return pl.name; return ''; };
  const where = p => { const n = placeAt(p); return n ? ' in ' + n : ''; };
  const who = new Map();
  for (const f of frames) for (const q of f.rows) {
    if (q.who && !who.has(q.id)) who.set(q.id, q.who);
    if (d.semantics >= DB_WHO && q.who.startsWith('s')) d.enemies.set(q.who, !!d.enemies.get(q.who) || (q.state !== '' && q.state !== 'I'));
  }
  const call = id => (who.has(id) && whoName(who.get(id)) ? whoName(who.get(id)) : 'enemy ' + id);
  const n = { recorded: Math.max(0, frames[frames.length - 1].t - frames[0].t), contact: -1, down: 0, edits: 0, gone: 0, cards: 0, fired: 0, triggers: -1 };
  d.counts = n;
  add('recorded', 'Recorded', dbMmss(n.recorded), 'frames', n.recorded, -1, false);
  if (!d.hasRun) add('elapsed', 'Setup to end', 'not known without the run record', 'run.end', -1, -1, false);
  else if (runSeconds >= 0) add('elapsed', 'Setup to end', dbMmss(runSeconds), 'run.end', runSeconds, -1, false);
  else add('elapsed', 'Setup to end', runSeconds === -2 ? 'not known: the run record has an end without its length' : 'not known: the end was not seen', 'run.end', -1, -1, false);
  if (runResult) add('result', 'How it ended', runResult, 'run.end', -1, -1, false);

  if (fires) {
    const e = events.find(x => x.kind === 'alert');
    if (!e) add('contact', 'First recorded contact', 'none recorded', 'alert', -1, -1, false);
    else { n.contact = e.t; add('contact', 'First recorded contact', dbMmss(e.t) + ', ' + call(e.id) + (e.at ? where(e.at) : ''), 'alert', -1, e.t, false); }
  } else {
    let found = false;
    for (const f of frames) {
      const q = f.rows.find(r => r.state !== '' && r.state !== 'I');
      if (!q) continue;
      n.contact = f.t; found = true;
      add('contact', 'First recorded contact', dbMmss(f.t) + ', ' + call(q.id) + where(q.pos), 'frames', -1, f.t, guessed);
      break;
    }
    if (!found) add('contact', 'First recorded contact', 'none recorded', 'frames', -1, -1, guessed);
  }

  let firings = 0;
  const trig = id => (id > 0 && d.plan ? d.plan.triggers.find(x => x.id === id) || null : null);
  for (const e of events) {
    if (fires && e.kind === 'fire') {
      const t = trig(e.id);
      d.milestones.push({ at: e.t, trigger: e.id, name: t ? t.name : '', place: e.at ? placeAt(e.at) : '', source: 'fire', guess: false });
      if (e.id > 0) d.fired.add(e.id);
      firings++;
    } else if (!fires && e.kind === 'note') {
      const w = debriefFiredWords(e.text);
      if (!w) continue;
      let id = w.id;
      if (!id && w.name && d.plan && d.plan.triggers.filter(x => x.name === w.name).length === 1) id = d.plan.triggers.find(x => x.name === w.name).id;
      const t = trig(id);
      d.milestones.push({ at: e.t, trigger: id, name: t ? t.name : w.name, place: '', source: 'note', guess: true });
      if (id > 0) d.fired.add(id);
      firings++;
    }
  }
  if (d.plan) n.triggers = d.plan.triggers.filter(t => !d.off.includes(t.id)).length;
  const distinct = new Set(d.milestones.map(m => (m.trigger > 0 ? '#' + m.trigger : "'" + m.name))).size;
  add('fired', 'Triggers fired', distinct + (n.triggers >= 0 ? ' of ' + n.triggers : '') + (firings > distinct ? ', ' + firings + ' firings' : ''), fires ? 'fire' : 'note', distinct, -1, !fires);
  n.fired = distinct;

  const count = kind => events.filter(e => e.kind === kind).length;
  const firstAt = kind => { const e = events.find(x => x.kind === kind); return e ? e.t : -1; };
  n.down = count('down'); n.gone = count('gone');
  const removed = count('removed'), remade = count('remade');
  n.edits = removed + remade;
  if (!guessed) add('down', 'Enemies down, as the game reported', String(n.down), 'down', n.down, firstAt('down'), false);
  else add('down', 'Enemies down or gone', String(n.down), 'down', n.down, firstAt('down'), true);
  if (d.semantics >= 2) add('removed', 'Taken out by an edit, not counted as down', String(removed), 'removed', removed, -1, false);
  if (!guessed) {
    add('remade', 'Made again after an edit', String(remade), 'remade', remade, -1, false);
    add('gone', 'Gone for a reason not known', String(n.gone), 'gone', n.gone, firstAt('gone'), false);
  }
  const intel = events.filter(e => e.kind === 'intel').pop();
  if (intel) {
    const m = /^\d+/.exec(intel.text), k = intel.text.startsWith('an intel item') ? 1 : m ? parseInt(m[0], 10) : -1;
    if (k >= 0) add('intel', 'Intel items taken', String(k), 'intel', k, intel.t, false);
  }
  const asked = count('command');
  if (asked > 0) add('command', 'Game Master requests', String(asked), 'command', asked, -1, false);
  // The Game Master's action cards (ML-025), counted by the word each "budget" event starts with.
  const verbs = ['request', 'spend', 'free', 'fire', 'wait', 'refused', 'cancel', 'refund'], words = ['asked', 'paid', 'free', 'fired', 'waited', 'refused', 'cancelled', 'refunded'], c = verbs.map(() => 0);
  for (const e of events) { if (e.kind !== 'budget') continue; const sp = e.text.indexOf(' '), k = verbs.indexOf(sp > 0 ? e.text.slice(0, sp) : e.text); if (k >= 0) c[k]++; }
  n.cards = c[3];
  const cards = verbs.map((v, k) => (c[k] > 0 ? c[k] + ' ' + words[k] : '')).filter(Boolean).join(', ');
  if (cards) add('budget', 'Game Master action cards', cards, 'budget', c[3], -1, false);
  const ended = events.filter(e => e.kind === 'end').pop();
  if (ended) add('end', 'Recording ended', dbMmss(ended.t) + ', ' + ended.text, 'end', -1, ended.t, false);
  add('players', 'Players seen', d.players.length + (d.players.length ? ' (' + d.players.join(', ') + ')' : ''), 'players', d.players.length, -1, false);
  if (runPlayers >= 0) add('squad', 'Squad size rolled for', String(runPlayers), 'run.players', runPlayers, -1, false);
  add('notRecorded', 'Not recorded', DEBRIEF_NOT_RECORDED, '', -1, -1, false);

  if (guessed) d.notes.push('An older recording (semantics ' + d.semantics + '): enemies are linked by a guess and every enemy that left the game\'s list counts as down'
    + (d.semantics < 2 ? ', even one an edit took out' : '') + '. Its states may show an enemy\'s first moments or a gun truck\'s gunner as alerted. Numbers marked (a guess) come from it.');
  if (!fires) d.notes.push('Made before trigger firings and alerts were recorded as events of their own (MissionLab 0.26): milestones are read from the timeline\'s wording, and first contact from the enemies\' recorded states.');
  if (!d.hasRun) d.notes.push('No run record was found for this recording, so the time from setup to end, how it ended and the squad size are not known.');
  d.text = debriefText(d); d.message = debriefMessage(d);
}

const debriefMilestoneWords = m => (m.trigger > 0 ? 'trigger ' + m.trigger + (m.name ? " '" + m.name + "'" : '') : m.name ? "'" + m.name + "'" : 'a trigger') + (m.place ? ' in ' + m.place : '');

// The same text as Debrief.Text().
function debriefText(d) {
  const lines = ['MissionLab debrief: ' + (d.replay || 'a recording')];
  if (d.scene) lines.push('Map: ' + d.scene);
  if (d.scenario || d.scenarioId) lines.push('Scenario: ' + (d.scenario || d.scenarioId) + (d.scenario && d.scenarioId ? ' (' + d.scenarioId + ')' : ''));
  lines.push('Recording semantics ' + d.semantics + (d.plugin ? ', plugin ' + d.plugin : ''));
  for (const f of d.facts) {
    lines.push(f.label + ': ' + f.value + (f.source ? ' [' + f.source + ']' : '') + (f.guess ? ' (a guess)' : ''));
    if (f.key === 'fired') for (const m of d.milestones) lines.push('  ' + dbMmss(m.at) + ' ' + debriefMilestoneWords(m) + ' [' + m.source + ']' + (m.guess ? ' (a guess)' : ''));
  }
  for (const note of d.notes) lines.push('Note: ' + note);
  return lines.join('\n');
}

// The same three lines as Debrief.Message(), which the game can show at a mission's end.
function debriefMessage(d) {
  const n = d.counts, guessed = d.semantics < DB_WHO;
  return [
    'Debrief: ' + dbMmss(n.recorded) + ' recorded' + (n.contact >= 0 ? ', first contact at ' + dbMmss(n.contact) : ', no contact recorded') + '.',
    'Enemies down' + (guessed ? ' (a guess)' : '') + ': ' + n.down + (n.edits > 0 ? '; ' + n.edits + ' taken out by edits, not counted' : '') + (n.gone > 0 ? '; ' + n.gone + ' gone, reason not known' : '') + '.',
    'Triggers fired: ' + n.fired + (n.triggers >= 0 ? ' of ' + n.triggers : '') + (n.cards > 0 ? '; Game Master cards fired: ' + n.cards : '') + '.',
  ].map(l => l.slice(0, 120));
}

// The scenario's spawn behind an authored identity ("s4"), in the plan as played; null when not known.
function debriefSpawn(d, who) {
  const m = /^s(\d+)/.exec(who || '');
  if (!d.plan || !m) return null;
  const authored = +m[1];
  if (d.leftOut.includes(authored)) return null;
  const played = authored - d.leftOut.filter(x => x < authored).length;
  return d.plan.spawns[played] || null;
}

// UnusedReport.Compare: suggestions from runs already known to be comparable (offline: one recording on its own).
function unusedOf(runs) {
  const g = { runs: [], triggerRuns: 0, enemyRuns: 0, suggestions: [], notes: [] };
  if (!runs.length) return g;
  const first = runs[0], fired = new Set(), seen = new Set(), active = new Set();
  let allRuns = true;
  for (const d of runs) {
    g.runs.push(d.run || d.replay);
    if (!d.hasRun) allRuns = false;
    if (d.semantics >= DB_FIRE) { g.triggerRuns++; for (const id of d.fired) fired.add(id); }
    if (d.semantics >= DB_WHO) { g.enemyRuns++; for (const [who, on] of d.enemies) { seen.add(who); if (on) active.add(who); } }
  }
  const runsWords = k => (k === 1 ? 'the one run looked at' : 'any of the ' + k + ' runs looked at');
  const upper = s => s.charAt(0).toUpperCase() + s.slice(1);
  if (first.plan && g.triggerRuns > 0)
    for (const t of first.plan.triggers) {
      if (first.off.includes(t.id) || fired.has(t.id)) continue;
      const name = 'trigger ' + t.id + (t.name ? " '" + t.name + "'" : '');
      g.suggestions.push({ kind: 'trigger', ref: String(t.id), name, text: upper(name) + ' did not fire in ' + runsWords(g.triggerRuns) + '. Worth a look, not a fault: it may be a backup, or out of the way.' });
    }
  const index = w => { const m = /^.(\d+)/.exec(w); return m ? +m[1] : Infinity; };
  const quiet = [...seen].filter(w => !active.has(w)).sort((a, b) => index(a) - index(b) || (a < b ? -1 : a > b ? 1 : 0));
  for (const who of quiet) {
    const s = debriefSpawn(first, who);
    let name = whoName(who);
    if (s && s.tag) name += ", tagged '" + s.tag + "'";
    if (s && s.trigger > 0) name += ', brought in by trigger ' + s.trigger;
    g.suggestions.push({ kind: 'enemy', ref: who, name, text: upper(name) + ' never left its idle state in ' + runsWords(g.enemyRuns) + '. Worth a look, not a fault: a guard can add tension without ever engaging.' });
  }
  if (!g.triggerRuns) g.notes.push('None of these recordings note when triggers fire (made before MissionLab 0.26), so triggers are not judged.');
  if (!g.enemyRuns) g.notes.push('None of these recordings say which enemy is which (older recordings), so enemies are not judged.');
  if (!first.plan) g.notes.push('The mission as it was played could not be read, so its triggers are not known.');
  if (!allRuns) g.notes.push('Without the run record, a trigger that lost its roll in this run is listed too, and the setup cannot be compared with other runs.');
  if (runs.length === 1) g.notes.push('One run says little: look again after a few more runs of the same setup.');
  return g;
}

// UnusedReport.GroupLines, for the page's one-run report.
function unusedLines(g, title) {
  const lines = [title + ': ' + g.runs.length + (g.runs.length === 1 ? ' run' : ' comparable runs')];
  if (!g.suggestions.length) lines.push('- Nothing stood unused.');
  for (const s of g.suggestions) lines.push('- ' + s.text);
  for (const note of g.notes) lines.push('  Note: ' + note);
  return lines;
}

// ---- the Debrief block of the Replay tab
const debriefUi = { of: null, text: '', from: '' };
function debriefOrNull(obj, name) {
  try { return debriefOf(obj, null, name); } catch (e) { return null; }
}
function syncDebrief() {
  const box = $('debriefBox'), d = replay.data ? replay.debrief : null;
  box.hidden = !replay.data;
  if (!replay.data) { debriefUi.of = null; return; }
  if (debriefUi.of === replay.debrief && debriefUi.text) return;
  const asked = debriefUi.of = replay.debrief;
  showDebrief(d ? d.text : '', d ? 'Worked out here from the recording alone.' : 'This recording has no debrief: it could not be read.');
  if (!live.on || !replay.name) return;
  // The game also reads the run record that belongs to the recording: the time from setup to end, how it ended, the squad size.
  fetch('/api/debrief/' + encodeURIComponent(replay.name), { cache: 'no-store' }).then(res => (res.ok ? res.json() : null)).then(o => {
    if (debriefUi.of === asked && o && typeof o.text === 'string') showDebrief(o.text, o.hasRun ? 'From the recording and its run record, as the game read them.' : 'From the recording, as the game read it; its run record was not found.');
  }).catch(() => { /* an older game side has no debrief: the page's own stays */ });
}
function showDebrief(text, from) {
  debriefUi.text = text; debriefUi.from = from;
  $('dbText').textContent = text; $('dbStatus').textContent = from;
}
$('dbCopy').addEventListener('click', () => {
  const text = debriefUi.text;
  if (!text) return;
  (navigator.clipboard ? navigator.clipboard.writeText(text) : Promise.reject()).then(() => say('The debrief is on the clipboard as plain text.'))
    .catch(() => { $('dbStatus').textContent = 'It could not be copied here: select the text above and copy it by hand.'; });
});

// ---- the Mission tab's unused-content report
function showUnused(text, status) {
  $('unusedText').hidden = !text; $('unusedText').textContent = text || ''; $('unusedStatus').textContent = status || '';
}
async function unusedFromGame() {
  const id = state.scenarioId;
  if (!id) { showUnused('', 'This scenario has no id yet, so its runs cannot be found: save it once, then play it.'); return; }
  showUnused('', 'Asking the game for the runs of ' + id + '...');
  try {
    const res = await fetch('/api/debrief/unused/' + encodeURIComponent(id), { cache: 'no-store' }), o = await res.json();
    if (!res.ok || !o || typeof o.text !== 'string') { showUnused('', 'The game could not make the report: ' + ((o && o.error) || 'status ' + res.status) + '.'); return; }
    showUnused(o.text, o.looked ? 'Runs are compared only when the scenario and its setup after the dice are the same.' : 'No runs of this scenario were found in the game folder yet.');
  } catch (e) { showUnused('', 'The game did not answer.'); }
}
// Offline: one recording the author opens, compared with nothing.
function unusedFromRecording(obj, name) {
  const d = debriefOf(obj, null, name), g = unusedOf([d]);
  const other = d.scenarioId && state.scenarioId && d.scenarioId !== state.scenarioId;
  const text = ['Unused content in ' + (d.scenarioId || d.scenario || 'this recording') + ': suggestions for review, from 1 run looked at'].concat(unusedLines(g, 'Setup 1')).join('\n');
  showUnused(text, other ? 'This recording is of scenario ' + d.scenarioId + ', not of the open one (' + state.scenarioId + ').' : 'From ' + name + ' alone.');
  return { debrief: d, group: g, text };
}
$('unusedRuns').addEventListener('click', unusedFromGame);
$('unusedOpen').addEventListener('click', () => $('unusedFile').click());
$('unusedFile').addEventListener('change', async ev => {
  const f = ev.target.files && ev.target.files[0];
  ev.target.value = '';
  if (!f) return;
  try { unusedFromRecording(JSON.parse(await f.text()), f.name); }
  catch (e) { showUnused('', f.name + ' could not be used: ' + (e && e.message ? e.message : 'it is not readable JSON') + '.'); }
});
