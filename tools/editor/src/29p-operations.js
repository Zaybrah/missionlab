// ------------------------------------------------------------------ branching operations in Play (ML-051)
// A short chain of missions with consequences: how one mission ended, as its run record says (success or failed, and each
// objective's final state), decides which mission comes next. Each mission is pinned as a playlist entry is (file, id,
// revision and the text's fingerprint, with its options and dice), its branches are tried in the author's order and its
// fallback is taken when the record rules every branch out. The game keeps every operation in
// BepInEx/MissionLab/operations (Operations.cs, Core's Operation.cs), saved atomically with the previous version kept, so
// a reload of the page or a restart of the game keeps the progress and the next mission. This page shows and edits it.
// Only results carry over between missions: nothing inside a mission (enemies, inventory, positions, navigation,
// physics, networked objects) is saved or restored. An end the game did not say, a run whose end was not seen, or an
// objective the record does not list is never turned into a branch here or in the game: the host decides, and the
// decision is labelled as the host's. A scenario changed or gone since it was pinned is not set up until the host
// retargets it or restarts the operation; the runs counted so far stay either way.
state.operation = state.operation || { lists: [], file: '', data: null, pending: '', retry: '', problems: [], runs: [], note: '', trouble: '', key: '', redraw: false, busy: null };
const OP_SCHEMA = 'missionlab-operation/1';
const OP_END = 'end';
const OP_MOST = 6;
const OP_MOST_BRANCHES = 6;
const opStr = v => (typeof v === 'string' ? v : '');

// ---- what the game said
function operationTake(d) {
  const op = state.operation;
  if (!d || !Array.isArray(d.operations)) throw new Error('not an operation answer');
  op.lists = d.operations.filter(p => p && typeof p.file === 'string');
  op.file = opStr(d.selected);
  op.data = d.operation && typeof d.operation === 'object' && Array.isArray(d.operation.nodes) ? d.operation : null;
  if (op.data) {
    if (!Array.isArray(op.data.steps)) op.data.steps = [];
    for (const n of op.data.nodes) { if (!Array.isArray(n.branches)) n.branches = []; if (!n.setup || typeof n.setup !== 'object') n.setup = { file: '' }; }
  }
  op.pending = opStr(d.pending);
  op.retry = opStr(d.retry);
  op.problems = Array.isArray(d.problems) ? d.problems.filter(p => p && typeof p.text === 'string') : [];
  op.runs = Array.isArray(d.runs) ? d.runs.filter(r => r && typeof r.file === 'string') : [];
  op.trouble = opStr(d.trouble) ? 'The operation in use cannot be read: ' + d.trouble + '.' : '';
  if (opStr(d.said)) op.note = d.said;
  if (opStr(d.recovered) && op.note.indexOf(d.recovered) < 0) op.note = d.recovered + (op.note ? ' ' + op.note : '');
  return d;
}
// Asked whenever Play reads the catalogue, as the playlist is. A second call while one is on its way waits for that one.
function operationRefresh() {
  const op = state.operation;
  if (!live.on) return Promise.resolve();
  if (op.busy) return op.busy;
  op.busy = (async () => {
    try {
      const r = await fetch('/api/operations', { cache: 'no-store' });
      if (r.status === 404) op.trouble = 'The game runs an older MissionLab without operations.';
      else operationTake(await r.json());
    } catch (e) { op.trouble = 'The game did not hand over the operations. Is it still running?'; }
    op.busy = null;
    syncOperation();
  })();
  return op.busy;
}
// The files-only requests: answered by the game at once, in the lobby and during a mission alike.
async function operationPost(path, body) {
  const r = await fetch(path, { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify(body) });
  let d = null; try { d = await r.json(); } catch (e) { /* no body */ }
  if (r.status !== 200) throw new Error((d && d.error) || 'the game answered with status ' + r.status);
  return operationTake(d);
}

// ---- words, as Core's Operation says them
const opNode = (d, id) => (d && Array.isArray(d.nodes) ? d.nodes.find(n => n.id === id) || null : null);
function opNodeName(d, id) {
  if (id === OP_END) return 'the end of the operation';
  const n = opNode(d, id);
  return n ? playlistName(n.setup) || n.id : String(id || '');
}
function opCondWords(b) {
  const parts = [];
  if (b.result === 'success') parts.push('a success'); else if (b.result === 'failed') parts.push('failed');
  if (b.goal) parts.push('objective ' + b.goal + ' ' + (b.state || 'succeeded'));
  return parts.join(' and ');
}
function opWayWords(n, branch) {
  if (branch === -1) return 'no branch matched, so the fallback';
  const b = n && n.branches[branch];
  return b ? 'branch ' + (branch + 1) + ' (' + opCondWords(b) + ')' : '';
}
function opResultWords(s) {
  if (!s) return '';
  let how = s.result === 'success' ? 'a success' : s.result === 'failed' ? 'failed' : s.result === 'refused' ? 'refused, so it was not played'
    : s.result === 'incomplete' ? 'incomplete: its end was not seen' : 'unknown: the game did not say how it ended';
  if (s.again) how += '; set aside by the host';
  else if (s.manual) how += '; the way on was decided by the host';
  else if (s.advanced) how += ', as the game recorded it';
  return how;
}
const opRound = d => Math.max(1, (d && d.round) | 0 || 1);

// ---- the view of the operation in use
// The rows Play lists: each mission's pin, the map and mode to host, its branches and fallback, and what stands in the
// way of setting it up. A changed mission offers retarget (as it is now, or the picked mission) or restart; a missing
// one cannot be used as it is now.
function operationRows() {
  const d = state.operation.data; if (!d) return [];
  const list = live.on ? catalogue.list : null;
  return d.nodes.map((n, i) => {
    const id = playlistIdentity(n.setup, list), bad = id.state === 'missing' || id.state === 'changed';
    return {
      index: i, id: n.id, label: playlistName(n.setup) || n.id, start: n.id === d.start, next: n.id === d.next, armed: n.id === d.next && d.armed === true,
      pinned: playlistPinWords(n.setup), instruction: playlistMapWords(n.setup), options: playlistPickWords(n.setup), seed: playlistSeedWords(n.setup),
      branches: n.branches.map(b => 'If ' + (opCondWords(b) || 'nothing is said') + ': ' + opNodeName(d, b.to)),
      fallback: 'Otherwise: ' + opNodeName(d, n.fallback),
      identity: id.state, problem: bad ? id.text : '', choices: id.state === 'changed' ? ['current', 'other', 'restart'] : id.state === 'missing' ? ['other', 'restart'] : [],
      canSetUp: !bad && n.id === d.next && !state.operation.problems.length,
    };
  });
}
// The runs counted in this round, oldest first, each with where it led and who decided.
function operationProgress() {
  const d = state.operation.data; if (!d) return [];
  const round = opRound(d);
  return d.steps.filter(s => (s.round | 0 || 1) === round).map(s => {
    const n = opNode(d, s.node), head = opNodeName(d, s.node) + ': ' + opResultWords(s);
    return s.advanced ? head + '. ' + opWayWords(n, s.branch | 0).replace(/^./, c => c.toUpperCase()) + ', on to ' + opNodeName(d, s.to) + '.' : head + '.';
  });
}
// The run the operation waits on, with the host's choices: each branch, the fallback, or setting it aside to play again.
function operationPending() {
  const op = state.operation, d = op.data;
  if (!d || !op.pending) return null;
  const s = d.steps.find(x => x.record === op.pending), n = s && opNode(d, s.node);
  if (!s || !n) return null;
  const choices = n.branches.map((b, i) => ({ choice: i, words: 'Branch ' + (i + 1) + ' (' + opCondWords(b) + '): ' + opNodeName(d, b.to) }));
  choices.push({ choice: -1, words: 'Fallback: ' + opNodeName(d, n.fallback) });
  choices.push({ choice: 'again', words: 'It does not count: play it again' });
  return {
    record: s.record, node: s.node, choices,
    text: opNodeName(d, s.node) + ' waits for your decision. Its run was ' + opResultWords(s) + '. Whichever way you choose is recorded as decided by the host, not by the game.',
  };
}
function operationOutcome() {
  const op = state.operation, d = op.data; if (!d) return '';
  const round = opRound(d), last = d.steps.filter(s => (s.round | 0 || 1) === round).slice(-1)[0];
  const lastWords = last ? 'Last run, ' + opNodeName(d, last.node) + ': ' + opResultWords(last) + '. ' : '';
  if (d.next === OP_END) return lastWords + 'The operation is over (round ' + round + '). Restart it to play it again.';
  const n = opNode(d, d.next);
  if (!n) return lastWords + 'The next mission is not one of the operation\'s missions any more. Restart the operation.';
  return (round > 1 ? 'Round ' + round + '. ' : '') + lastWords + 'Next: ' + opNodeName(d, d.next)
    + (d.armed ? ', set up in the game. ' : '. Choose Set up the next mission when you are ready. ') + playlistMapWords(n.setup);
}

// ---- changes, saved by the game
// The operation as the page edits it: its name, start and missions. Progress is the game's own and is never sent.
function operationCopy() {
  const d = state.operation.data;
  return d ? JSON.parse(JSON.stringify({ schema: OP_SCHEMA, id: d.id || '', name: d.name, start: d.start, nodes: d.nodes })) : null;
}
// A refused change leaves the operation as it was, so its rows look unchanged to syncOperation, while the control the
// host changed still shows the refused value. The rows are drawn again from the saved operation, focus or not.
function operationRefused(note) {
  const op = state.operation;
  op.note = note; op.redraw = true;
  syncOperation();
}
async function operationSave(p, done) {
  const op = state.operation;
  try { const d = await operationPost('/api/operations/save', { file: op.file, operation: p }); op.note = opStr(d.said) || done; }
  catch (err) { operationRefused('Not saved: ' + (err && err.message ? err.message : err) + '. The operation is as it was.'); return; }
  syncOperation();
}
// A mission id the game accepts (a-z, 0-9 and '-', at most 32), made from the file name and free in this operation.
function opNodeId(p, e) {
  const base = String(e.value || 'mission').toLowerCase().replace(/[^a-z0-9-]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 28) || 'mission';
  let id = base === OP_END ? 'end-1' : base;
  for (let k = 2; p.nodes.some(n => n.id === id); k++) id = base + '-' + k;
  return id;
}
function opPicked() {
  const op = state.operation, e = pickedEntry();
  if (!e || e.page) { op.note = 'Pick a mission in the list above first.'; syncOperation(); return null; }
  if (!e.usable) { op.note = (e.title || e.value) + ' cannot be used: ' + (e.why || 'MissionLab would not use it') + '.'; syncOperation(); return null; }
  return e;
}
async function operationNew() {
  if (!live.on) return;
  const e = opPicked(); if (!e) return;
  const name = String($('opName').value || '').trim() || 'Operation';
  const p = { schema: OP_SCHEMA, id: '', name, start: '', nodes: [] };
  const id = opNodeId(p, e);
  p.nodes.push({ id, setup: playlistEntryOf(e), branches: [], fallback: OP_END });
  p.start = id;
  state.operation.file = '';
  await operationSave(p, 'Operation ' + name + ' started with ' + (e.title || e.value) + '.');
}
async function operationAdd() {
  const op = state.operation;
  if (!live.on) return;
  if (!op.data) { await operationNew(); return; }
  const e = opPicked(); if (!e) return;
  const p = operationCopy();
  if (p.nodes.length >= OP_MOST) { op.note = 'An operation holds at most ' + OP_MOST + ' missions.'; syncOperation(); return; }
  p.nodes.push({ id: opNodeId(p, e), setup: playlistEntryOf(e), branches: [], fallback: OP_END });
  await operationSave(p, 'Added ' + (e.title || e.value) + '. Lead to it from a branch or a fallback of another mission.');
}
async function operationSetNode(id, change, done) {
  const p = operationCopy(), n = p && p.nodes.find(x => x.id === id); if (!n) return;
  change(n, p);
  await operationSave(p, done);
}
async function operationAddBranch(id) {
  const p = operationCopy(), n = p && p.nodes.find(x => x.id === id); if (!n) return;
  if (n.branches.length >= OP_MOST_BRANCHES) { operationRefused('A mission has at most ' + OP_MOST_BRANCHES + ' branches.'); return; }
  const other = p.nodes.find(x => x.id !== id);
  n.branches.push({ result: 'success', to: other ? other.id : OP_END });
  await operationSave(p, opNodeName(p, id) + ': branch ' + n.branches.length + ' added.');
}
async function operationSetBranch(id, i, change, done) {
  await operationSetNode(id, n => { if (n.branches[i]) change(n.branches[i], n); }, done);
}
async function operationRemoveBranch(id, i) {
  await operationSetNode(id, n => { n.branches.splice(i, 1); }, opNodeName(state.operation.data, id) + ': branch ' + (i + 1) + ' removed.');
}
async function operationRemoveNode(id) {
  const p = operationCopy(); if (!p) return;
  const from = p.nodes.filter(n => n.id !== id && (n.fallback === id || n.branches.some(b => b.to === id)));
  if (from.length) { operationRefused(opNodeName(p, id) + ' is still where ' + from.map(n => opNodeName(p, n.id)).join(', ') + ' can lead. Change those first.'); return; }
  if (p.start === id) { operationRefused(opNodeName(p, id) + ' is where the operation starts. Start with another mission first.'); return; }
  const name = opNodeName(p, id);
  p.nodes = p.nodes.filter(n => n.id !== id);
  await operationSave(p, 'Removed ' + name + ' from the operation.');
}
// A changed or missing scenario: the host's explicit choice. 'current' pins the file as it is now, 'other' the mission
// picked in the list above, 'restart' begins a new round. The host's name, squad size, dice, branches and fallback stay,
// and so do the runs counted; an option keeps its pick only where the scenario still offers it.
async function operationResolve(id, how) {
  const op = state.operation, d = op.data, n = opNode(d, id);
  if (!live.on || !n) return;
  if (how === 'restart') { await operationRestart(); return; }
  const was = n.setup, idn = playlistIdentity(was, catalogue.list);
  const e = how === 'current' ? idn.now : pickedEntry();
  if (!e || e.page) { operationRefused(how === 'current' ? 'The file is not there any more: use the picked mission instead, or restart the operation.' : 'Pick a mission in the list above first.'); return; }
  if (!e.usable) { operationRefused((e.title || e.value) + ' cannot be used: ' + (e.why || 'MissionLab would not use it') + '.'); return; }
  const fresh = playlistEntryOf(e), same = String(e.value).toLowerCase() === String(was.file).toLowerCase();
  if (was.label && same) fresh.label = was.label;
  if (was.squad > 0) fresh.squad = was.squad;
  fresh.seed = was.seed || 'new';
  fresh.record = same ? was.record || '' : '';
  for (const o of e.options || []) {
    const at = was.picks && Object.prototype.hasOwnProperty.call(was.picks, o.id) ? was.picks[o.id] | 0 : -1;
    if (at >= 0 && at < (o.choices || []).length) fresh.picks[o.id] = at;
  }
  await operationSetNode(id, x => { x.setup = fresh; }, opNodeName(d, id) + ' now uses ' + (e.title || e.value) + ', revision ' + (e.revision | 0) + ', as it is now. The runs counted so far stay.');
}
// Setting the next mission up: refused here, before asking, when its scenario changed or is gone; the game checks again.
async function operationArm(retry) {
  const op = state.operation, d = op.data;
  if (!live.on || !d) return false;
  const n = opNode(d, d.next);
  if (!n) { op.note = d.next === OP_END ? 'The operation is over. Restart it to play it again.' : 'The next mission is not one of the operation\'s missions.'; syncOperation(); return false; }
  if (op.problems.length) { op.note = 'The operation cannot be used yet: ' + op.problems[0].text + '.'; syncOperation(); return false; }
  const id = playlistIdentity(n.setup, catalogue.list);
  if (id.state === 'missing' || id.state === 'changed') {
    op.note = opNodeName(d, n.id) + ' was not set up: ' + id.text + '. Choose Use it as it is now, Use the picked mission or Restart the operation first; the progress is kept.';
    syncOperation(); return false;
  }
  if (retry && !op.retry) { op.note = 'There is no run of ' + opNodeName(d, n.id) + ' to retry.'; syncOperation(); return false; }
  try {
    const answer = await api('/api/operations/arm', { file: op.file, node: n.id, retry: !!retry });
    operationTake(answer);
    op.note = opStr(answer.said) || 'Set up: ' + opNodeName(d, n.id) + '.';
    live.runsFor = null; opts.picksFor = '';      // the Dice list and the Options box ask the game again
    syncOperation(); return true;
  } catch (err) {
    op.note = err && err.status === 409 && err.result !== 'stale' ? err.message : liveTrouble(err);
    syncOperation(); return false;
  }
}
// The host's decision on the run the operation waits on: a branch by number, -1 for the fallback, or 'again'.
async function operationDecide(choice) {
  const op = state.operation, p = operationPending();
  if (!live.on || !p) return;
  const body = choice === 'again' ? { record: p.record, again: true } : { record: p.record, branch: choice | 0 };
  try { const answer = await operationPost('/api/operations/decide', body); op.note = opStr(answer.said); }
  catch (err) { op.note = 'Not decided: ' + (err && err.message ? err.message : err) + '.'; }
  syncOperation();
}
async function operationRestart() {
  const op = state.operation; if (!live.on || !op.data) return;
  try { const answer = await operationPost('/api/operations/restart', {}); op.note = opStr(answer.said); }
  catch (err) { op.note = 'Not restarted: ' + (err && err.message ? err.message : err) + '.'; }
  syncOperation();
}
async function operationSelect(file) {
  try { await operationPost('/api/operations/select', { file }); state.operation.note = ''; }
  catch (err) { state.operation.note = 'The game did not change the operation in use: ' + (err && err.message ? err.message : err) + '.'; }
  syncOperation();
}
async function operationDelete() {
  const op = state.operation; if (!live.on || !op.file) return;
  const d = op.data;
  if (!confirm('Delete the operation ' + ((d && d.name) || op.file) + '? Its run records stay.')) return;
  try { await operationPost('/api/operations/delete', { file: op.file }); }
  catch (err) { op.note = 'Not deleted: ' + (err && err.message ? err.message : err) + '.'; }
  syncOperation();
}

// ---- drawing the section
function opTargetSelect(d, value, label, fn) {
  const sel = plEl('select'); sel.setAttribute('aria-label', label);
  for (const n of d.nodes) sel.append(optionEl(opNodeName(d, n.id), n.id));
  sel.append(optionEl('The end of the operation', OP_END));
  sel.value = value;
  sel.addEventListener('change', () => fn(sel.value));
  return sel;
}
function operationBranchEl(d, n, b, j) {
  const row = plEl('div', 'row'), at = opNodeName(d, n.id) + ' branch ' + (j + 1);
  row.append(plEl('span', 'meta', (j + 1) + '. If'));
  const res = plEl('select'); res.setAttribute('aria-label', 'Result for ' + at);
  for (const [v, t] of [['', 'any result'], ['success', 'a success'], ['failed', 'failed']]) res.append(optionEl(t, v));
  res.value = b.result || '';
  res.addEventListener('change', () => operationSetBranch(n.id, j, x => { if (res.value) x.result = res.value; else delete x.result; }, at + ': ' + (res.value || 'any result') + '.'));
  const goal = plEl('input'); goal.type = 'text'; goal.maxLength = 32; goal.placeholder = 'objective id'; goal.value = b.goal || '';
  goal.setAttribute('aria-label', 'Objective for ' + at);
  goal.addEventListener('change', () => operationSetBranch(n.id, j, x => { const g = String(goal.value || '').trim(); if (g) { x.goal = g; x.state = x.state || 'succeeded'; } else { delete x.goal; delete x.state; } }, at + ': objective ' + (String(goal.value || '').trim() || 'none') + '.'));
  const st = plEl('select'); st.setAttribute('aria-label', 'Objective state for ' + at);
  for (const [v, t] of [['succeeded', 'succeeded'], ['failed', 'failed']]) st.append(optionEl(t, v));
  st.value = b.state || 'succeeded'; st.disabled = !b.goal;
  st.addEventListener('change', () => operationSetBranch(n.id, j, x => { x.state = st.value; }, at + ': objective ' + st.value + '.'));
  row.append(res, goal, st, plEl('span', 'meta', 'then'), opTargetSelect(d, b.to, 'Where ' + at + ' leads', v => operationSetBranch(n.id, j, x => { x.to = v; }, at + ' leads to ' + opNodeName(d, v) + '.')));
  row.append(plButton('Remove', 'danger', 'Remove this branch', () => operationRemoveBranch(n.id, j)));
  return row;
}
function operationNodeEl(row, n) {
  const d = state.operation.data, li = plEl('li', (row.next ? 'next' : '') + (row.problem ? ' problem' : ''));
  const head = plEl('div', 'head'); head.append(plEl('span', '', row.label));
  if (row.start) head.append(plEl('span', 'chip', 'start'));
  if (row.next) head.append(plEl('span', 'chip info', row.armed ? 'next · set up in the game' : 'next'));
  li.append(head, plEl('div', 'meta', row.pinned), plEl('div', '', row.instruction), plEl('div', 'meta', 'Options: ' + row.options + '. Dice: ' + row.seed + '.'));
  n.branches.forEach((b, j) => li.append(operationBranchEl(d, n, b, j)));
  const fb = plEl('div', 'row');
  fb.append(plEl('span', 'meta', 'Otherwise (fallback):'), opTargetSelect(d, n.fallback, 'Fallback of ' + row.label, v => operationSetNode(n.id, x => { x.fallback = v; }, row.label + ': otherwise ' + opNodeName(d, v) + '.')));
  li.append(fb);
  if (row.problem) {
    li.append(plEl('div', 'warn', (row.identity === 'missing' ? 'Missing: ' : 'Changed since it was added: ') + row.problem + '. It is not set up until you choose; the runs counted so far stay:'));
    const fix = plEl('div', 'row');
    if (row.choices.includes('current')) fix.append(plButton('Use it as it is now', '', 'Pin the file as it is now', () => operationResolve(n.id, 'current')));
    fix.append(plButton('Use the picked mission', '', 'Put the mission picked in the list above in its place', () => operationResolve(n.id, 'other')));
    fix.append(plButton('Restart the operation', '', 'Begin a new round from the first mission; earlier runs stay on record', () => operationResolve(n.id, 'restart')));
    li.append(fix);
  }
  const acts = plEl('div', 'row');
  const add = plButton('Add a branch', '', 'Lead somewhere else on a result or an objective', () => operationAddBranch(n.id)); add.disabled = n.branches.length >= OP_MOST_BRANCHES;
  const start = plButton('Start here', '', 'The operation begins with this mission (from its next round)', () => operationSetNode(n.id, (x, p) => { p.start = n.id; }, row.label + ' is now where the operation starts.')); start.disabled = row.start;
  acts.append(add, start, plButton('Remove', 'danger', 'Remove this mission from the operation', () => operationRemoveNode(n.id)));
  li.append(acts);
  return li;
}
function syncOperation() {
  if (!viewIs('play')) return;
  const op = state.operation, d = op.data;
  $('opLive').hidden = !live.on;
  if (!live.on) return;
  $('opCount').textContent = d ? String(d.nodes.length) : '';
  const sel = $('opPick'), listKey = JSON.stringify([op.lists, op.file]);
  if (sel.dataset.key !== listKey && document.activeElement !== sel) {
    sel.dataset.key = listKey; sel.textContent = '';
    sel.append(optionEl(op.lists.length ? 'None' : 'None yet: pick a mission, name it and choose New operation', ''));
    for (const p of op.lists) sel.append(optionEl((p.name || p.file) + (p.why ? ' (cannot be read)' : ' · ' + plural(p.nodes | 0, 'mission', 'missions')), p.file));
    sel.value = op.file;
  }
  $('opDelete').disabled = !op.file;
  $('opOutcome').textContent = op.trouble || operationOutcome();
  const pending = operationPending(), box = $('opDecide');
  box.hidden = !pending;
  $('opDecideText').textContent = pending ? pending.text : '';
  const choices = $('opDecideRow'), choiceKey = JSON.stringify(pending);
  if (choices.dataset.key !== choiceKey) {
    choices.dataset.key = choiceKey; choices.textContent = '';
    if (pending) for (const c of pending.choices) choices.append(plButton(c.words, c.choice === 'again' ? '' : 'primary', 'Recorded as decided by the host', () => operationDecide(c.choice)));
  }
  const next = d && opNode(d, d.next), rows = operationRows(), nextRow = rows.find(r => r.next);
  $('opArm').disabled = !d || !nextRow || !nextRow.canSetUp;
  $('opArm').textContent = d && d.armed ? 'Set up the next mission again' : 'Set up the next mission';
  $('opRetry').hidden = !(d && next && op.retry);
  $('opRetry').disabled = !nextRow || !nextRow.canSetUp;
  $('opRestart').disabled = !d;
  $('opNote').textContent = [op.problems.length ? 'The operation cannot be used yet: ' + op.problems.map(p => p.text).join('; ') + '.' : '', op.note].filter(Boolean).join(' ');
  const progress = $('opProgress'), steps = operationProgress(), progressKey = JSON.stringify(steps);
  if (progress.dataset.key !== progressKey) { progress.dataset.key = progressKey; progress.textContent = ''; steps.forEach(t => progress.append(plEl('li', '', t))); }
  const key = JSON.stringify([rows, d && d.nodes]);
  const f = document.activeElement, ul = $('opNodes'), held = !!f && /^(INPUT|SELECT)$/.test(String(f.tagName || '')) && !!ul.contains(f);
  // A control in use is not drawn over, so typing is kept; after a refusal it must be, as it shows what was refused.
  if (!op.redraw && (key === op.key || held)) return;
  const label = held ? String(f.getAttribute('aria-label') || '') : '';
  op.redraw = false; op.key = key; ul.textContent = '';
  rows.forEach(row => ul.appendChild(operationNodeEl(row, d.nodes[row.index])));
  const again = label ? [...ul.querySelectorAll('[aria-label]')].find(x => x.getAttribute('aria-label') === label) : null;
  if (again) again.focus();
}
$('opAdd').addEventListener('click', () => operationAdd());
$('opNew').addEventListener('click', () => operationNew());
$('opDelete').addEventListener('click', () => operationDelete());
$('opArm').addEventListener('click', () => operationArm(false));
$('opRetry').addEventListener('click', () => operationArm(true));
$('opRestart').addEventListener('click', () => operationRestart());
$('opPick').addEventListener('change', ev => operationSelect(ev.target.value));
// Play's catalogue read brings the operations along; Play's refresh draws them, as for the playlist (29n-playlist.js).
{ const refreshCatalogueBase = refreshCatalogue; refreshCatalogue = async function () { await refreshCatalogueBase(); await operationRefresh(); }; }
{ const syncPlayBase = syncPlay; syncPlay = function () { syncPlayBase(); syncOperation(); }; }
expose({ operationRefresh, operationRows, operationProgress, operationPending, operationOutcome, operationNew, operationAdd, operationAddBranch, operationSetBranch, operationSetNode, operationRemoveNode, operationResolve, operationArm, operationDecide, operationRestart, syncOperation });
