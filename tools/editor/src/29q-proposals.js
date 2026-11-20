// ------------------------------------------------------------------ proposals from a coding agent (ML-053)
// A coding agent (tools/agent, docs/agent-api.md) reads a scenario through the game's local link and proposes changes as
// structured operations. The plugin applies them to a copy, judges the result with the strict reader, Completion and the
// capability list, and keeps an accepted proposal in BepInEx/MissionLab/proposals: never in scenarios, never in the running
// mission. Here the author reviews one. It opens as a separate draft, with the differences from the scenario it was made for
// in words (the staged-change diff, plus objectives). Apply puts it on the author's own draft as ONE undo step, and only when
// the page still holds exactly the scenario it was made for; saving is then the normal Save. A proposal for a scenario that
// has changed since is stale and is never applied over the change.
const PROPOSAL_SCHEMA = 'missionlab-proposal/1';
state.proposals = state.proposals || { list: [], open: null, lines: [], note: '', trouble: '', busy: null };
const propStr = v => (typeof v === 'string' ? v : '');

// A proposal as the game answers it ({summary, proposal}), as its file holds it, or as this page keeps it. null: not one.
function proposalTake(d) {
  if (!d || typeof d !== 'object') return null;
  const rec = d.proposal && typeof d.proposal === 'object' ? d.proposal : d, sum = d.summary && typeof d.summary === 'object' ? d.summary : null;
  const base = rec.base && typeof rec.base === 'object' ? rec.base : null;
  if (rec.schema !== PROPOSAL_SCHEMA || typeof rec.id !== 'string' || !rec.id || typeof rec.scenario !== 'string' || !base || typeof base.text !== 'string') return null;
  return {
    schema: PROPOSAL_SCHEMA, id: rec.id, name: propStr(rec.name) || 'Proposal', file: propStr(rec.file), scenarioId: propStr(rec.scenarioId), made: propStr(rec.made),
    base: { hash: propStr(base.hash), revision: Number.isInteger(base.revision) ? base.revision : 0, text: base.text },
    scenario: rec.scenario, hash: propStr(rec.hash),
    changes: Array.isArray(rec.changes) ? rec.changes.filter(c => typeof c === 'string') : [],
    findings: Array.isArray(rec.findings) ? rec.findings.filter(f => f && typeof f.text === 'string') : [],
    stale: sum ? sum.stale === true : rec.stale === true,
    staleWhy: propStr(sum ? sum.staleWhy : rec.staleWhy),
  };
}
// Both texts as objects; null when either is not JSON.
function proposalObjects(q) {
  try { return { before: JSON.parse(q.base.text), after: JSON.parse(q.scenario) }; } catch (e) { return null; }
}

// ---- the differences, in words
// The staged-change diff does not speak of objectives; these lines do, in the same { kind, ref, text } form.
function proposalGoalLines(beforeObj, afterObj) {
  const goalsOf = o => { const b = planBody(o) || {}; return Array.isArray(b.goals) ? b.goals.filter(g => g && typeof g.id === 'string') : []; };
  const waits = o => { const b = planBody(o) || {}; return !!(b.extraction && typeof b.extraction === 'object' && b.extraction.goals === true); };
  const A = new Map(goalsOf(beforeObj).map(g => [g.id, g])), B = new Map(goalsOf(afterObj).map(g => [g.id, g])), out = [];
  const title = g => 'Objective "' + (propStr(g.name) || g.id) + '" (' + g.id + ')';
  const trig = id => (id > 0 ? 'trigger ' + id : 'none');
  const add = (ref, text) => out.push({ kind: 'goal', ref: 'goal:' + ref, text });
  for (const [id, b] of B) {
    const a = A.get(id);
    if (!a) {
      add(id, title(b) + ' added: completed by ' + trig(b.succeed | 0) + (Array.isArray(b.after) && b.after.length ? ', once ' + b.after.join(', ') + ' succeeded' : '')
        + ((b.activate | 0) > 0 ? ', active after ' + trig(b.activate | 0) : '') + ((b.fail | 0) > 0 ? ', failed by ' + trig(b.fail | 0) : '') + (b.required === false ? ', optional' : ', required'));
      continue;
    }
    const parts = [];
    if (propStr(a.name) !== propStr(b.name)) parts.push('name "' + propStr(a.name) + '" → "' + propStr(b.name) + '"');
    if ((a.required !== false) !== (b.required !== false)) parts.push(b.required === false ? 'now optional' : 'now required');
    if (JSON.stringify(a.after || []) !== JSON.stringify(b.after || [])) parts.push('waits for ' + stageWord(b.after || []) + ' (was ' + stageWord(a.after || []) + ')');
    for (const [k, words] of [['activate', 'active after'], ['succeed', 'completed by'], ['fail', 'failed by']]) if ((a[k] | 0) !== (b[k] | 0)) parts.push(words + ' ' + trig(b[k] | 0) + ' (was ' + trig(a[k] | 0) + ')');
    if ((a.notify !== false) !== (b.notify !== false)) parts.push(b.notify === false ? 'players are not told' : 'players are told');
    if (parts.length) add(id, title(b) + ': ' + parts.join('; '));
  }
  for (const [id, a] of A) if (!B.has(id)) add(id, title(a) + ' removed');
  if (waits(beforeObj) !== waits(afterObj)) out.push({ kind: 'goal', ref: 'extraction', text: waits(afterObj) ? 'Extraction now waits for every required objective' : 'Extraction no longer waits for the objectives' });
  return out;
}
// Everything the proposal changes in the scenario it was made for.
function proposalDiff(p) {
  const q = proposalTake(p), o = q && proposalObjects(q);
  if (!o) return [];
  return semanticDiff(o.before, o.after, null).concat(proposalGoalLines(o.before, o.after));
}

// ---- putting a scenario's content on the page, as opening its file does (loadScenario), without its name, identity or
// map: only the parts an undo step restores, so Apply and its undo are one exact step.
function proposalSet(obj) {
  const root = planBody(obj);
  state.zones = zonesIn(root.triggers); state.objectives = objectivesIn(root.objectives); setDoorWishes(root.doors); settingsIn(root);
  goalsRead(root);
  state.things = thingsIn(root.things); state.thing = -1;
  state.enemies = (Array.isArray(root.spawns) ? root.spawns : []).map((s, i) => enemyOf(s, i));
  state.places = placesIn(root.places);
  placeLinksResolve();
  state.squadRange = Array.isArray(root.squad) && root.squad.length === 2 ? [root.squad[0], root.squad[1]] : null;
  state.minutes = num(root.minutes, 0) > 0 ? root.minutes : 0;
  state.sealed = root.sealed === true; state.options = optionsIn(root.options); state.placement = placementIn(root.placement); state.director = directorIn(root.director);
  for (const e of state.enemies) { if (e.within && !state.placement.some(p => p.id === e.within && p.mode === 'allow')) e.within = 0; if (e.hvt || e.vehicle) e.fallback = 'stay'; }
  state.selected = Math.min(state.selected, state.enemies.length - 1);
}

// ---- review and apply
// Opens the proposal as a separate draft, with its differences listed in the panel. Returns true when the draft opened;
// the differences are shown either way (linked to a game between missions there is no draft to open).
function proposalOpen(p) {
  const q = proposalTake(p), ui = state.proposals;
  if (!q || !proposalObjects(q)) { say('That is not a MissionLab proposal, or its scenario texts cannot be read.'); return false; }
  ui.open = q; ui.lines = proposalDiff(q); ui.note = '';
  const opened = openSeparate('proposal:' + q.id, 'the proposal "' + q.name + '"', proposalObjects(q).after, null);
  if (opened && q.stale) say('Opened the proposal "' + q.name + '" as a separate draft. It is stale: ' + (q.staleWhy || 'the scenario has changed since it was proposed') + ', so it cannot be applied.');
  syncProposals();
  return opened;
}
// Puts the proposal on the author's own draft as one undo step. Refused, with nothing changed, when it is stale, when the
// page does not hold exactly the scenario it was made for, or when the page's own check refuses the result.
function proposalApply(p) {
  const q = proposalTake(p || state.proposals.open), ui = state.proposals;
  const refuse = text => { ui.note = text; say(text); syncProposals(); return false; };
  if (!q) return refuse('Open a proposal first.');
  if (q.stale) return refuse('Not applied: ' + (q.staleWhy || 'the scenario has changed since this was proposed') + '. Ask the agent for a new proposal against the scenario as it is now.');
  const o = proposalObjects(q);
  if (!o) return refuse('Not applied: the proposal\'s scenario texts cannot be read.');
  const wrong = scenarioProblems(o.after).filter(f => f.level === 'error');
  if (wrong.length) return refuse('Not applied: the scenario check refuses it: ' + (wrong[0].path ? wrong[0].path + ': ' : '') + wrong[0].text + '.');
  if (stage.separate) {
    if (live.on) return refuse('Go back to the running game first (Back to my draft), then Apply.');
    backToMine();                 // Apply goes on the author's own draft, never on the separate one
  }
  if (live.on && live.mission && stage.mode !== 'draft') stageSetMode('draft');      // nothing reaches the game before Apply changes
  const name = 'Apply proposal "' + q.name + '"';
  txBegin(name);
  // The page must hold the scenario the proposal was made for, exactly: otherwise Apply would quietly undo the author's
  // own edits made since. The comparison is the undo snapshot, so it covers everything the step would replace.
  let same = false;
  try { proposalSet(o.before); same = undoSnapshot() === hist.open.snap; } catch (e) { same = false; }
  if (!same) {
    txCancel();
    return refuse('Not applied: the page does not hold the scenario this proposal was made for (' + (q.file || 'its file') + ', revision ' + q.base.revision
      + ') as it was then. Open that scenario unchanged, or ask the agent for a new proposal against what you have now.');
  }
  try { proposalSet(o.after); } catch (e) { txCancel(); return refuse('Not applied: ' + (e && e.message ? e.message : 'the proposal cannot be put on the page') + '.'); }
  txCommit(name);
  changed();
  ui.open = null; ui.lines = [];
  ui.note = 'Applied "' + q.name + '" as one step: Undo takes it back. Save the scenario as usual to keep it.';
  say(ui.note);
  syncProposals();
  return true;
}

// ---- the game's list (live only)
async function proposalRefresh() {
  const ui = state.proposals;
  if (!live.on) { ui.trouble = ''; syncProposals(); return; }
  if (ui.busy) return ui.busy;
  ui.busy = (async () => {
    try {
      const r = await fetch('/api/agent/proposals', { cache: 'no-store' });
      if (r.status === 404) ui.trouble = 'The game runs an older MissionLab without agent proposals.';
      else { const d = await r.json(); ui.list = Array.isArray(d.proposals) ? d.proposals.filter(x => x && typeof x.id === 'string') : []; ui.trouble = ''; }
    } catch (e) { ui.trouble = 'The game did not hand over the proposals. Is it still running?'; }
    ui.busy = null;
    syncProposals();
  })();
  return ui.busy;
}
async function proposalReview(id) {
  let d = null;
  try { const r = await fetch('/api/agent/proposal/' + encodeURIComponent(id), { cache: 'no-store' }); d = await r.json(); } catch (e) { /* reported below */ }
  if (!proposalTake(d)) return say('The game did not hand over that proposal' + (d && d.error ? ': ' + d.error : '') + '.');
  return proposalOpen(d);
}
async function proposalDiscard(id) {
  try { await api('/api/agent/proposal/' + encodeURIComponent(id) + '/discard', {}); }
  catch (e) { return say('The proposal was not discarded: ' + (e && e.message ? e.message : 'the game did not answer') + '.'); }
  if (state.proposals.open && state.proposals.open.id === id) { state.proposals.open = null; state.proposals.lines = []; }
  say('The proposal was discarded. Your scenario was not changed.');
  return proposalRefresh();
}
// A proposal file from BepInEx/MissionLab/proposals, for the page opened without the game.
async function proposalFile(f) {
  let d = null;
  try { d = JSON.parse(await f.text()); } catch (e) { /* reported below */ }
  if (!proposalTake(d)) return say(f.name + ' is not a MissionLab proposal.');
  return proposalOpen(d);
}

// ---- the panel on the Mission tab
function syncProposals() {
  if (!$('propBox')) return;
  const ui = state.proposals, mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  $('propWhere').textContent = live.on ? 'Proposals the game keeps in BepInEx/MissionLab/proposals. Nothing in them reaches your scenario or the game until you apply and save.'
    : 'Open a proposal file from BepInEx/MissionLab/proposals. Opened from the game, the page lists them.';
  $('propRefresh').hidden = !live.on;
  $('propCount').textContent = live.on && ui.list.length ? String(ui.list.length) : '';
  $('propTrouble').textContent = ui.trouble; $('propTrouble').hidden = !ui.trouble;
  const list = $('propList'); list.textContent = '';
  for (const p of live.on ? ui.list : []) {
    const li = mk('li', { className: p.stale ? 'stale' : '' }), row = mk('div', { className: 'row' });
    const head = mk('b', { textContent: propStr(p.name) || p.id });
    const meta = mk('span', { className: 'meta', textContent: propStr(p.file) + (p.base ? ', revision ' + (p.base.revision | 0) : '') + (p.made ? ' · ' + new Date(p.made).toLocaleString() : '')
      + ' · ' + plural(Array.isArray(p.changes) ? p.changes.length : 0, 'change', 'changes') });
    li.append(head, meta);
    if (p.stale) li.appendChild(mk('span', { className: 'chip warn', textContent: 'Stale: ' + (propStr(p.staleWhy) || 'the scenario has changed') }));
    const open = mk('button', { type: 'button', className: 'btn', textContent: 'Review' }); open.addEventListener('click', () => proposalReview(p.id));
    const drop = mk('button', { type: 'button', className: 'btn danger', textContent: 'Discard' }); drop.addEventListener('click', () => proposalDiscard(p.id));
    row.append(open, drop); li.appendChild(row); list.appendChild(li);
  }
  $('propEmpty').hidden = !live.on || ui.list.length > 0 || !!ui.trouble;
  const q = ui.open;
  $('propOpen').hidden = !q;
  if (q) {
    $('propTitle').textContent = q.name;
    $('propMeta').textContent = 'For ' + (q.file || 'a scenario') + ', revision ' + q.base.revision + (q.base.hash ? ' (' + q.base.hash.replace(/^[a-z0-9]+:/, '').slice(0, 8) + ')' : '') + (q.made ? ', proposed ' + new Date(q.made).toLocaleString() : '') + '.';
    $('propStale').textContent = q.stale ? 'Stale: ' + (q.staleWhy || 'the scenario has changed since this was proposed') + '. It cannot be applied; ask for a new proposal.' : '';
    $('propStale').hidden = !q.stale;
    const said = $('propChanges'); said.textContent = '';
    for (const c of q.changes) said.appendChild(mk('li', { textContent: c }));
    for (const f of q.findings) said.appendChild(mk('li', { className: 'note', textContent: 'Note: ' + (f.path ? f.path + ': ' : '') + f.text }));
    $('propDiffNote').textContent = ui.lines.length ? 'What changes in the scenario: ' + plural(ui.lines.length, 'difference', 'differences') + '.' : 'The proposal makes no difference to the scenario.';
    stageRows($('propDiff'), ui.lines.map(l => ({ kind: l.kind, text: l.text })));
    $('propApply').disabled = q.stale;
    $('propBack').hidden = !(stage.separate && stage.separate.key === 'proposal:' + q.id);
  }
  $('propNote').textContent = ui.note;
}
$('propRefresh').addEventListener('click', () => proposalRefresh());
$('propOpenFile').addEventListener('click', () => $('propFile').click());
$('propFile').addEventListener('change', ev => { const f = ev.target.files && ev.target.files[0]; ev.target.value = ''; if (f) proposalFile(f); });
$('propApply').addEventListener('click', () => proposalApply());
$('propBack').addEventListener('click', () => { backToMine(); syncProposals(); });
$('propClose').addEventListener('click', () => { state.proposals.open = null; state.proposals.lines = []; state.proposals.note = ''; syncProposals(); });
// Linked to the game, the list follows the folder: an agent's new proposal shows up without a reload. The first look waits
// for the rest of the page: live and stage are declared in parts further down.
setInterval(() => { if (live.on) proposalRefresh(); }, 10000);
setTimeout(() => { if (live.on) proposalRefresh(); else syncProposals(); }, 0);
expose({ proposalTake, proposalDiff, proposalGoalLines, proposalOpen, proposalApply, proposalRefresh, syncProposals });   // agent proposals (ML-053), for automated tests
