// ------------------------------------------------------------------ validate: every check in one run, before the squad is invited (ML-048)
// One report that ties the checks to the scenario text it was made for. With the page served by the game, the plugin runs
// Core MissionValidation on a thread of its own (POST /api/validate): the strict format, Completion for every combination
// of the host's options with each squad size and seed asked, the scatter seeds on the map's own navmesh (only with the
// matching map file on that computer) and every saved rehearsal; a work budget caps it and Cancel stops it with what was
// found so far. The game keeps the last report beside the scenario (validations/<scenario id>/) and the page shows it,
// marked stale when the scenario text or the map has changed since. Offline the page runs only what it has itself: the
// format check and its own mission check; everything else is listed as not run. The first failing combination is shown
// with the seed, options and squad size that play it again, and Show selects the triggers it is about and marks them in
// the mission graph (29i-graph.js). A pass says what it covered; it never claims combat balance, navigation or co-op.
const VALIDATE_EVALUATOR = 1;                  // Core MissionValidation.Evaluator: a report from other checks is stale
const VALIDATE_LIMITS = 'A pass covers only the checks above, on the combinations they executed. It does not certify combat balance, navigation, spotting, physics or multiplayer behaviour, and a roll or squad size that was not tried may still differ.';
const VALIDATE_TITLES = { format: 'Strict format', check: 'Mission check (this page)', completion: 'Completion across options, squad sizes and seeds', scatter: 'Scatter seeds on the map', rehearsals: 'Saved rehearsals' };
const VALIDATE_STATES = { passed: 'Passed', failed: 'Failed', 'not-run': 'Not run', 'not-needed': 'Not needed', partial: 'Stopped early' };
const VALIDATE_PLUGIN = 'not run: needs the game\'s plugin. Start OPERATOR with MissionLab and open the editor at http://localhost:8765';
// shown: { report, source: offline|live|stored, made }; run: the plugin's run id while one is going; marking: the mission
// graph's marks (graphMark, 29i-graph.js) are this section's counterexample; keptFor: the scenario id whose kept report was last asked for.
state.validate = state.validate || { players: [1, 2, 4], seeds: 10, budget: 2000, checks: { completion: true, scatter: true, rehearsals: true },
  run: '', busy: false, cancelling: false, note: '', shown: null, marking: false, keptFor: null, key: '' };
const VALIDATE_MARK = 'In the counterexample';

// ----- what is asked

// "1, 2, 4" as squad sizes from 1 to 16, each once, in order; the default when nothing usable is left.
function validatePlayers(text) {
  const out = [...new Set(String(text).split(/[^0-9]+/).filter(Boolean).map(n => parseInt(n, 10)).filter(n => n >= 1 && n <= 16))].sort((a, b) => a - b);
  return out.length ? out : [1, 2, 4];
}
// The body of POST /api/validate. The insertion points go along as the scatter preview sends them (29g-scatter.js).
function validateBody() {
  const v = state.validate, checks = Object.keys(v.checks).filter(k => v.checks[k]);
  const insertions = state.infils.filter(isVec).concat(state.insertionSpots.map(s => s.pos).filter(isVec)).slice(0, 64).map(p => p.slice(0, 3).map(r2));
  return { scenario: scenarioText(), checks, players: v.players.slice(), seeds: Math.max(1, Math.min(200, v.seeds | 0)), budget: Math.max(1, Math.min(20000, v.budget | 0)), insertions };
}

// ----- offline: only what the page has

// The page's own report, in the plugin's shape: the strict format (scenarioProblems, the same rules as the plugin's reader)
// and the mission check run; completion, scatter and the saved rehearsals need the plugin and are said to be not run.
function validateOffline() {
  const text = scenarioText(), read = scenarioProblems(text), errors = read.filter(f => f.level === 'error');
  const checks = [], words = f => (f.path ? f.path + ': ' : '') + f.text;
  checks.push({ name: 'format', state: errors.length ? 'failed' : 'passed', coverage: 'exhaustive', planned: 1, executed: 1, failed: errors.length ? 1 : 0,
    why: errors.length ? 'the game\'s reader refuses this scenario: ' + words(errors[0]) : '', notes: read.filter(f => f.level !== 'error').map(f => 'note: ' + words(f)), counterexample: null });
  const problems = errors.length ? [] : missionCheck().filter(f => f.level === 'problem');
  const about = problems.find(f => f.about && Array.isArray(f.about.triggers) && f.about.triggers.length);
  checks.push({ name: 'check', state: errors.length ? 'not-run' : problems.length ? 'failed' : 'passed', coverage: errors.length ? '' : 'exhaustive', planned: 1, executed: errors.length ? 0 : 1,
    failed: problems.length, why: errors.length ? 'not run: the scenario must pass the format check first' : problems.length ? problems.length + (problems.length === 1 ? ' problem to fix' : ' problems to fix') : '',
    notes: problems.map(f => f.text), counterexample: about ? { check: 'check', expected: 'the mission check finds nothing to fix', found: about.text, triggers: about.about.triggers.slice(), spawns: [], trace: [] } : null });
  const scatters = state.enemies.some(e => e.scatter > 0);
  checks.push({ name: 'completion', state: 'not-run', coverage: '', planned: 0, executed: 0, failed: 0, why: VALIDATE_PLUGIN + '. The page\'s mission check above is no substitute: it does not try the option combinations, squad sizes or seeds.', notes: [], counterexample: null });
  checks.push({ name: 'scatter', state: scatters ? 'not-run' : 'not-needed', coverage: '', planned: 0, executed: 0, failed: 0,
    why: !scatters ? 'no enemy has a scatter radius' : 'not run: needs the game\'s plugin and the matching map file ' + (state.navFile || '(the scenario names none)') + ' on that computer, as scatter is placed on the map\'s own navmesh', notes: [], counterexample: null });
  checks.push({ name: 'rehearsals', state: 'not-run', coverage: '', planned: 0, executed: 0, failed: 0, why: VALIDATE_PLUGIN + '; saved rehearsals are kept by the game.', notes: [], counterexample: null });
  const failed = checks.some(c => c.state === 'failed'), first = checks.map(c => c.counterexample).find(Boolean) || null;
  return { schema: 'missionlab-validation/1', evaluator: 0, offline: true, scenarioHash: textHash(text), scenarioId: state.scenarioId, revision: state.revision,
    navFile: '', mapPrint: '', players: [], seeds: [], options: [], budget: 0, executed: 0, cancelled: false, budgetHit: false,
    verdict: failed ? 'failed' : 'incomplete', covers: 'Checked on this page: the strict format and the mission check. Completion across options, squad sizes and seeds, scatter and the saved rehearsals were not run.',
    limits: VALIDATE_LIMITS, checks, first };
}

// ----- the report as the page shows it

// Why a report no longer speaks for the page ('' when it does): the scenario text, the map it used, or the checks changed.
function validateStale(r) {
  if (!r || typeof r !== 'object') return '';
  if (r.scenarioHash !== textHash(scenarioText())) return 'The scenario has changed since this report was made: validate again before relying on it.';
  const m = state.map;
  if (r.mapPrint && m && !m.isExample && m.print && String(r.navFile || '').toLowerCase() === String(m.name || '').toLowerCase() && m.print !== r.mapPrint)
    return 'The map file ' + r.navFile + ' open here is not the one this report used: validate again.';
  if (!r.offline && r.evaluator !== VALIDATE_EVALUATOR) return 'This report was made by another version of MissionLab\'s checks: validate again.';
  return '';
}
// The counterexample in words: what plays it again and what went wrong.
function validateCounterWords(c) {
  if (!c) return null;
  const lines = [];
  if (c.check === 'check') lines.push('The page\'s mission check: ' + c.found);
  else if (c.check === 'rehearsals') {
    lines.push('Saved rehearsal ' + c.rehearsal + ' (its roll ' + c.seed + ', ' + c.players + (c.players === 1 ? ' player' : ' players') + ').');
    lines.push('Expected: ' + c.expected + '. Found: ' + (c.found || 'nothing') + '.');
  } else {
    lines.push('Seed ' + c.seed + ' with ' + c.players + (c.players === 1 ? ' player' : ' players') + (c.options ? ', options ' + c.options : ', no host options') + '.');
    lines.push('Expected: ' + c.expected + '. Found: ' + c.found + '.');
    lines.push('To play it: set Seed = ' + c.seed + ' in the MissionLab settings, ' + (c.options ? 'pick those options in Play, ' : '') + 'and start the mission with ' + c.players + (c.players === 1 ? ' player.' : ' players.'));
  }
  const ids = (c.triggers || []).filter(id => state.zones.some(z => z.id === id)), gone = (c.triggers || []).filter(id => !ids.includes(id));
  if (ids.length) lines.push('About ' + (ids.length === 1 ? 'trigger ' : 'triggers ') + ids.join(', ') + '.');
  if (gone.length) lines.push((gone.length === 1 ? 'Trigger ' : 'Triggers ') + gone.join(', ') + ' named here ' + (gone.length === 1 ? 'is' : 'are') + ' no longer in the scenario.');
  if ((c.spawns || []).length) lines.push('About ' + c.spawns.map(i => 'enemy ' + (i + 1)).join(', ') + '.');
  return lines;
}
// Everything the section shows, worked out from the report and the page: the automated tests read this.
function validateModel() {
  const v = state.validate, s = v.shown;
  if (!s || !s.report) return null;
  const r = s.report, checks = Array.isArray(r.checks) ? r.checks : [];
  return {
    source: s.source, made: s.made || '', stale: validateStale(r), verdict: r.verdict || 'incomplete', offline: !!r.offline,
    checks: checks.map(c => ({ name: c.name, title: VALIDATE_TITLES[c.name] || c.name, state: c.state, stateWords: VALIDATE_STATES[c.state] || c.state, coverage: c.coverage || '',
      planned: c.planned | 0, executed: c.executed | 0, failed: c.failed | 0, why: c.why || '', notes: Array.isArray(c.notes) ? c.notes : [] })),
    counter: r.first || null, counterWords: validateCounterWords(r.first), covers: r.covers || '', limits: r.limits || VALIDATE_LIMITS,
    seeds: r.seeds || [], players: r.players || [], options: r.options || [], cancelled: !!r.cancelled, budgetHit: !!r.budgetHit,
  };
}
// A report to show. A new report lets go of the marks the last counterexample put on the graph.
function validateTake(report, source, made) {
  const v = state.validate;
  v.shown = report && typeof report === 'object' ? { report, source, made: made || '' } : null;
  v.key = '';
  if (v.marking) validateClearMarks();
  validateSync();
  return validateModel();
}

// ----- the counterexample on the page

// Selects what the counterexample is about: its first trigger in the inspector with the enemies it brings, the map turned to
// its place, and every trigger it names marked in the mission graph; an enemy when it names no trigger. Nothing changes.
function validateShowCounter() {
  const m = validateModel(), c = m && m.counter, v = state.validate;
  if (!c) return false;
  if (sealedHides()) { v.note = 'This mission is sealed: reveal it first to see what the counterexample is about.'; validateSync(); return false; }
  const ids = (c.triggers || []).filter(id => state.zones.some(z => z.id === id));
  v.marking = true;
  if (ids.length) {
    const id = ids[0], z = state.zones.find(q => q.id === id), bound = [];
    state.enemies.forEach((e, i) => { if (e.trigger === id) bound.push(i); });
    state.inspectSel = id;
    state.selected = bound.length ? bound[0] : -1; state.squad = bound.length > 1 ? bound : [];
    if (bound.length) state.door = -1;
    const spot = isVec(z.pos) ? z.pos : bound.length ? state.enemies[bound[0]].pos : null;
    if (spot) { state.view.cx = spot[0]; state.view.cz = spot[2]; }
    showTab('triggers', true);
    view3d.syncEnemies(); syncSide(); invalidate();
    graphMark(ids, VALIDATE_MARK);
    say('Selected ' + (ids.length === 1 ? 'trigger ' : 'triggers ') + ids.join(', ') + ' from the counterexample; the mission graph marks ' + (ids.length === 1 ? 'it.' : 'them.'));
    return true;
  }
  graphMark([], '');                               // nothing of the graph is named: earlier marks go
  const spawn = (c.spawns || []).find(i => i >= 0 && i < state.enemies.length);
  if (spawn !== undefined) {
    select(spawn);
    const e = state.enemies[spawn]; state.view.cx = e.pos[0]; state.view.cz = e.pos[2]; invalidate();
    say('Selected enemy ' + (spawn + 1) + ' from the counterexample.');
    return true;
  }
  v.note = 'The counterexample names no trigger or enemy that is still in the scenario.'; validateSync();
  return false;
}
// Takes away only marks this section put on the graph; another part's marks are its own to clear.
function validateClearMarks() { const v = state.validate; if (v.marking) { v.marking = false; graphMark([], ''); } validateSync(); }

// ----- running through the game

async function validateCall(path, body) {
  const init = body === undefined ? { cache: 'no-store' } : { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify(body) };
  const res = await fetch(path, init);
  let data = null; try { data = await res.json(); } catch (e) { /* no body */ }
  return { status: res.status, data };
}
const validateWait = ms => new Promise(done => setTimeout(done, ms));
// Starts a run (offline: the page's own checks at once) and follows it until it is done, cancelled or lost.
async function validateRun() {
  const v = state.validate;
  if (v.busy) return validateModel();
  if (!live.on) { validateTake(validateOffline(), 'offline', ''); v.note = 'Offline: only the format and the mission check ran. Open the editor from the game for the rest.'; validateSync(); return validateModel(); }
  v.busy = true; v.cancelling = false; v.note = 'Asking the game to validate...'; validateSync();
  try {
    let { status, data } = await validateCall('/api/validate', validateBody());
    if (status === 409 && data && data.id) { v.note = 'A validation was already running in the game; following it.'; }
    else if (status === 404 && !(data && data.id)) throw new Error('the game runs an older MissionLab that cannot validate; update the plugin');
    else if (!(status === 202 || status === 200) || !data || !data.id) throw new Error((data && data.error) || 'status ' + status);
    v.run = data.id;
    for (;;) {
      await validateWait(500);
      ({ status, data } = await validateCall('/api/validate/' + encodeURIComponent(v.run)));
      if (status !== 200 || !data) throw new Error((data && data.error) || 'status ' + status);
      if (data.state === 'running' || data.state === 'cancelling') {
        v.note = (data.state === 'cancelling' ? 'Cancelling... ' : 'Validating: ') + (VALIDATE_TITLES[data.check] || 'starting') + ', ' + (data.executed | 0) + ' of about ' + (data.planned | 0) + ' combinations.';
        validateSync(); continue;
      }
      if (data.state !== 'done' || !data.report) throw new Error(data.error || 'the game stopped the run');
      validateTake(data.report, 'live', data.made);
      v.keptFor = state.scenarioId;
      v.note = (data.report.cancelled ? 'Cancelled: the report keeps what was found until then. ' : '') + (data.kept ? 'Kept as ' + data.kept + '.' : data.keptWhy ? 'Not kept: ' + data.keptWhy + '.' : '');
      break;
    }
  } catch (e) { v.note = 'The game did not validate it (' + (e && e.message ? e.message : e) + ').'; }
  v.busy = false; v.cancelling = false; v.run = '';
  validateSync();
  return validateModel();
}
async function validateCancel() {
  const v = state.validate;
  if (!v.busy || !v.run || v.cancelling) return;
  v.cancelling = true; validateSync();
  try { await validateCall('/api/validate/' + encodeURIComponent(v.run) + '/cancel', {}); }
  catch (e) { v.note = 'The game did not take the cancel (' + e.message + ').'; v.cancelling = false; validateSync(); }
}
// The last report the game kept for this scenario, shown (and marked stale when it no longer fits) until a new run.
async function validateFetchKept() {
  const v = state.validate, id = state.scenarioId;
  v.keptFor = id;
  if (!live.on || !id) return;
  try {
    const { status, data } = await validateCall('/api/validations/' + encodeURIComponent(id));
    if (status !== 200 || !data || !data.kept || !data.kept.report) return;
    if (state.scenarioId !== id || (v.shown && v.shown.source === 'live')) return;
    validateTake(data.kept.report, 'stored', data.kept.made);
  } catch (e) { /* no kept report is shown; a run makes one */ }
}

// ----- the section

function validateRender(model) {
  const box = $('vReport');
  box.textContent = '';
  if (!model) return;
  const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined) e.textContent = text; return e; };
  const verdict = { passed: 'Passed: every check that ran found nothing wrong.', failed: 'Failed: a check found a combination that breaks the mission.', incomplete: 'Incomplete: some checks did not run or stopped early, so this is not a pass.' };
  box.append(el('p', 'verdict ' + model.verdict, verdict[model.verdict] || model.verdict));
  if (model.stale) box.append(el('p', 'stale', 'Stale: ' + model.stale));
  box.append(el('p', 'hint', model.source === 'offline' ? 'Run on this page, offline.' : model.source === 'stored' ? 'The report the game kept' + (model.made ? ', made ' + model.made : '') + '.' : 'Made by the game' + (model.made ? ' at ' + model.made : '') + '.'));
  const ul = el('ul', 'checks');
  for (const c of model.checks) {
    const li = el('li', 's-' + c.state), head = el('div', 'row');
    head.append(el('b', '', c.title), el('span', 'chip state', c.stateWords));
    li.append(head);
    if (c.executed || c.planned) li.append(el('div', 'meta', c.executed + ' of ' + c.planned + (c.planned === 1 ? ' combination' : ' combinations') + ' executed' + (c.coverage ? ' · ' + c.coverage : '') + (c.failed ? ' · ' + c.failed + ' failed' : '')));
    if (c.why) li.append(el('div', 'why', c.why));
    for (const n of c.notes.slice(0, 6)) li.append(el('div', 'meta', n));
    ul.append(li);
  }
  box.append(ul);
  if (model.covers) box.append(el('p', 'covers', model.covers));
  box.append(el('p', 'limits', model.limits));
  const words = model.counterWords;
  $('vCounterBox').hidden = !words;
  $('vCounter').textContent = words ? words.join(' ') : '';
}
function validateSync() {
  const v = state.validate;
  if (!$('vReport')) return;
  setVal($('vPlayers'), v.players.join(', ')); setVal($('vSeeds'), String(v.seeds)); setVal($('vBudget'), String(v.budget));
  $('vDoCompletion').checked = v.checks.completion; $('vDoScatter').checked = v.checks.scatter; $('vDoRehearsals').checked = v.checks.rehearsals;
  $('vRun').disabled = v.busy; $('vCancel').hidden = !v.busy; $('vCancel').disabled = v.cancelling;
  $('vClear').hidden = !(v.marking && state.graph.mark.ids.length);
  $('vStatus').textContent = v.note;
  $('vOffline').hidden = live.on;
  if (live.on && state.scenarioId && v.keptFor !== state.scenarioId && !v.busy) validateFetchKept();
  const model = validateModel(), key = JSON.stringify(model);
  if (key === v.key) return;
  v.key = key;
  validateRender(model);
}
// The section follows every change the side column shows (29-controls.js syncSide).
{ const syncSideBase = syncSide; syncSide = function () { syncSideBase(); validateSync(); }; }
$('vPlayers').addEventListener('change', () => { state.validate.players = validatePlayers($('vPlayers').value); validateSync(); });
$('vSeeds').addEventListener('change', () => { state.validate.seeds = Math.max(1, Math.min(200, parseInt($('vSeeds').value, 10) || 10)); validateSync(); });
$('vBudget').addEventListener('change', () => { state.validate.budget = Math.max(1, Math.min(20000, parseInt($('vBudget').value, 10) || 2000)); validateSync(); });
$('vDoCompletion').addEventListener('change', () => { state.validate.checks.completion = !!$('vDoCompletion').checked; });
$('vDoScatter').addEventListener('change', () => { state.validate.checks.scatter = !!$('vDoScatter').checked; });
$('vDoRehearsals').addEventListener('change', () => { state.validate.checks.rehearsals = !!$('vDoRehearsals').checked; });
$('vRun').addEventListener('click', () => { validateRun(); });
$('vCancel').addEventListener('click', () => { validateCancel(); });
$('vShow').addEventListener('click', () => { validateShowCounter(); });
$('vClear').addEventListener('click', validateClearMarks);
expose({ validateRun, validateOffline, validateTake, validateModel, validateShowCounter, validateBody, validatePlayers, validateStale });   // the validation run (ML-048), for automated tests
