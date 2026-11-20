// ------------------------------------------------------------------ editing
// Undo and redo (ML-045). Every finished command is one named entry holding the state just before it. Undo puts that state
// back and keeps the one it replaced for Redo; a new command after Undo drops what could have been redone. A gesture (a
// drag, a slider) is a transaction: txBegin when it starts, txCommit when it ends in a change, txCancel when it is given up,
// which puts the state back and leaves no entry. The history belongs to one scenario on one page: opening another one, or
// the game running another mission, clears it, so it is never applied to another mission. It is not kept anywhere; drafts
// and revisions are what survive a reload.
const HISTORY_LIMIT = 80;
const undoStack = [];       // done entries, newest last
const redoStack = [];       // undone entries, the next one to redo last
const hist = { open: null, session: null, note: '' };     // the transaction under way, whose history this is, why it last started afresh
const txOpen = () => !!hist.open;
// Everything an undo restores, as text. The selection is kept beside it, so a change of selection alone is no change.
// Areas are in it as written; their cached bounds are worked out again on the way back (historyRestore).
const undoSnapshot = () => JSON.stringify({ e: state.enemies, z: state.zones, o: state.objectives, d: doorsOut(), x: settingsOut(), t: thingsOut(), p: state.places,
  k: state.sealed, op: state.options, pl: state.placement, gm: state.director, sr: state.squadRange, mn: state.minutes });
function historyRestore(snap, sel) {
  paintGestureDrop();                  // a half-drawn rectangle or polygon belongs to the state being left
  const o = JSON.parse(snap);
  state.enemies = o.e; state.zones = o.z || []; state.objectives = o.o || state.objectives; setDoorWishes(o.d || []); settingsIn(o.x || {}); state.things = thingsIn(o.t); state.thing = -1; state.squad = [];
  state.selected = Math.min(sel, o.e.length - 1);
  if (Array.isArray(o.p)) state.places = o.p;
  state.sealed = o.k === true; state.options = Array.isArray(o.op) ? o.op : []; state.placement = Array.isArray(o.pl) ? o.pl : []; state.director = o.gm || null;
  state.squadRange = Array.isArray(o.sr) ? o.sr : null; state.minutes = o.mn > 0 ? o.mn : 0;
  // Fresh objects: their bounds are worked out now, so every inside test agrees with the restored shapes straight away.
  for (const h of state.zones.concat(state.places, state.placement)) if (h && h.area && Array.isArray(h.area.parts)) areaPrepare(h.area);
}
// Whose history it is: this page on its own, the lobby, or one running mission of the game.
const historySession = () => (!live.on ? 'page' : live.mission ? 'mission ' + (live.session || '') : 'lobby');
// Where a step was made: '' on the page alone, 'game' when Live mode sends it at once, 'staged' when Draft mode holds it.
const historyWhere = () => (live.on && live.mission ? (stageHolds() ? 'staged' : 'game') : '');
// Clears the history with a reason the author can read. Returns that sentence, '' when there was nothing to clear.
function historyReset(why) {
  const had = undoStack.length + redoStack.length > 0;
  undoStack.length = 0; redoStack.length = 0; hist.open = null; hist.session = historySession();
  hist.note = had ? why + ' Undo history starts afresh: the earlier steps belonged to what was here before, and are never applied to this.' : '';
  if (had) say(hist.note);
  syncHistory();
  return hist.note;
}
// The page now holds another mission's state than the steps were made on: they go.
function historyCheck() {
  const now = historySession(), was = hist.session;
  if (was === now) return;
  hist.session = now;
  if (was !== null && undoStack.length + redoStack.length > 0) historyReset(now === 'lobby' ? 'The mission has ended.' : was === 'page' ? 'The page is linked to the game now.' : 'The game is running another mission.');
}
// The step on top changed nothing (an edit that was refused or chose the value it had): it goes, and what it dropped from
// Redo comes back. Only a step recorded by historyPush dropped anything; while it is on top Redo is empty.
function historyDropEmpty(now) {
  while (undoStack.length && undoStack[undoStack.length - 1].snap === now) { const e = undoStack.pop(); if (e.kept) { redoStack.length = 0; redoStack.push(...e.kept); } }
}
function historyPush(entry) {
  historyDropEmpty(entry.snap);
  entry.at = Date.now(); entry.where = historyWhere(); entry.kept = redoStack.splice(0);
  undoStack.push(entry); if (undoStack.length > HISTORY_LIMIT) undoStack.shift();
  syncHistory();
}
// Call just before a one-step edit, with what it does in a few words ('Move enemy 3'). Inside a transaction it adds nothing:
// the transaction is the step.
function willChange(name) {
  historyCheck();
  if (hist.open) return;
  historyPush({ name: String(name || 'Change'), snap: undoSnapshot(), sel: state.selected });
}
// A change made of many small steps (a drag): nothing is recorded until txCommit, and txCancel takes it all back.
function txBegin(name) {
  historyCheck();
  if (hist.open) txCommit();
  hist.open = { name: String(name || 'Change'), snap: undoSnapshot(), sel: state.selected };
  return true;
}
// One entry when the state differs from where the transaction began, none when it does not. name replaces the first one.
function txCommit(name) {
  const t = hist.open; if (!t) return false;
  hist.open = null;
  if (undoSnapshot() === t.snap) { syncHistory(); return false; }
  historyPush({ name: name || t.name, snap: t.snap, sel: t.sel });
  return true;
}
// Gives the transaction up: the state goes back to where it began, with no entry. A gesture that already put its own state
// back in place (27a-paint.js) leaves nothing to restore here.
function txCancel() {
  const t = hist.open; if (!t) return false;
  hist.open = null;
  if (undoSnapshot() !== t.snap) { historyRestore(t.snap, t.sel); syncSide(); view3d.syncEnemies(); invalidate(); }
  return true;
}
// Whether the step may already be in the running game: sent at once in Live mode, or part of a batch the game answered since.
function historyApplied(entry) {
  if (!live.on || !live.mission) return false;
  if (entry.where === 'game') return true;
  return entry.where === 'staged' && !!stage.receipt && !stage.receipt.error && stage.receipt.at >= entry.at;
}
// Undo and Redo of a step the game already has cannot take it out of the game: the page changes, and the difference is
// staged for review like any other edit. Fired triggers, spawned enemies and what players did are never rewound.
function historyStep(from, to, word) {
  historyCheck();
  if (hist.open) { say('Let go first: a change is still being made.'); return false; }
  const now = undoSnapshot();
  historyDropEmpty(now);
  const entry = from[from.length - 1];
  if (!entry) { say(from === undoStack ? 'Nothing to undo.' : 'Nothing to redo.'); syncHistory(); return false; }
  const applied = historyApplied(entry);
  if (applied && (live.sending || (!stageHolds() && live.pending))) { say('The last change is still on its way to the game. ' + word + ' again once the game has it.'); return false; }
  const switched = applied && !stageHolds();
  if (switched) stageSetMode('draft');
  from.pop();
  to.push({ name: entry.name, snap: now, sel: state.selected, at: Date.now(), where: applied ? 'staged' : historyWhere() });
  if (to === undoStack && undoStack.length > HISTORY_LIMIT) undoStack.shift();
  historyRestore(entry.snap, entry.sel);
  changed();
  say(applied ? word + ': "' + entry.name + '", on this page only. The game keeps it as it is until you apply the staged change in Direct' + (switched ? ' (Draft mode is on now, so nothing reaches the game before you review it)' : '')
    + '. Triggers that fired, enemies that spawned and what players did stay as they are.' : word + ': ' + entry.name + '.');
  syncHistory();
  return true;
}
function undo() { return historyStep(undoStack, redoStack, 'Undone'); }
function redo() { return historyStep(redoStack, undoStack, 'Redone'); }
// A scenario takes the page's place (a file, a revision, a pack, a generated layout): the steps so far belong to the one that
// was here, so they go. Unsaved work on the page is first kept as a separate draft under Revisions, where it opens again.
function historyReplaced(name) {
  let unsaved = false;
  try {
    const anything = state.enemies.length + state.zones.length + state.things.length + state.places.length > 0;
    unsaved = !live.on && !state.example && !stage.separate && (catalogue.savedText === null ? anything : catalogue.savedText !== scenarioText());
  } catch (e) { /* a page that cannot be written has nothing to keep */ }
  if (unsaved) stageKeepAside('replaced', 'What was on the page before ' + name, null);
  const kept = unsaved ? 'What was on the page before is kept under Revisions (Mission tab) as a separate draft.' : '';
  const reset = historyReset('You opened ' + name + '.');
  if (reset && kept) { hist.note = reset + ' ' + kept; say(hist.note); syncHistory(); return hist.note; }
  if (kept) { hist.note = kept; syncHistory(); }
  return reset || kept;
}
// The history in the side panel: what Undo and Redo would do next, and the steps, newest first.
function syncHistory() {
  historyCheck();
  const u = undoStack[undoStack.length - 1], r = redoStack[redoStack.length - 1];
  if (!$('histList')) return;
  $('histUndo').disabled = !u; $('histRedo').disabled = !r;
  $('histUndo').title = u ? 'Undo: ' + u.name + ' (Ctrl+Z)' : 'Nothing to undo';
  $('histRedo').title = r ? 'Redo: ' + r.name + ' (Ctrl+Shift+Z or Ctrl+Y)' : 'Nothing to redo';
  $('undo').title = u ? 'Undo: ' + u.name + ' (Ctrl+Z)' : 'Undo (Ctrl+Z)';
  $('histCount').textContent = undoStack.length + redoStack.length ? plural(undoStack.length, 'step', 'steps') + (redoStack.length ? ', ' + redoStack.length + ' undone' : '') : '';
  const list = $('histList'); list.textContent = '';
  const row = (entry, undone) => {
    const li = document.createElement('li');
    li.className = undone ? 'undone' : ''; li.textContent = entry.name + (undone ? ' (undone)' : '') + (!undone && historyApplied(entry) ? ' (in the game)' : '');
    list.appendChild(li);
  };
  for (const e of redoStack) row(e, true);
  const shown = undoStack.slice(-24).reverse();
  for (const e of shown) row(e, false);
  if (undoStack.length > shown.length) { const li = document.createElement('li'); li.className = 'more'; li.textContent = (undoStack.length - shown.length) + ' earlier'; list.appendChild(li); }
  $('histEmpty').hidden = undoStack.length + redoStack.length > 0;
  $('histNote').textContent = hist.note;
}
const historyList = () => ({ done: undoStack.map(e => e.name), undone: redoStack.map(e => e.name).reverse(), open: hist.open ? hist.open.name : '', note: hist.note });
expose({ undoDepth: () => undoStack.length });   // gestures leave no entry until they end (ML-032), for automated tests
// Brings a thing into view. In 3D the floors above it are hidden, or the roof would be all there is to see.
function showAt(p, top) {
  if (state.mode !== '3d') { state.view.cx = p[0]; state.view.cz = p[2]; state.view.scale = Math.max(state.view.scale, 14); invalidate(); return; }
  if (state.ceiling > top) { state.ceiling = top; const c = $('ceiling'); c.value = Math.min(+c.max, Math.max(+c.min, top)); syncSide(); }
  view3d.lookFrom(p);
}
// One part of the side panel at a time. A test page may ask for all of them at once.
function showTab(name, byHand) {
  const side = $('side');
  if (side.dataset.show === 'all' && !byHand && name !== 'all') return;
  side.dataset.show = name;
  for (const b of document.querySelectorAll('#tabs button')) b.setAttribute('aria-selected', b.dataset.tab === name ? 'true' : 'false');
  if (name !== 'all' && name !== 'replay') { try { localStorage.setItem('missionlab-tab', name); } catch (e) { /* storage unavailable */ } }
}
document.querySelectorAll('#tabs button').forEach(b => b.addEventListener('click', () => showTab(b.dataset.tab, true)));
function selectDoor(n) { if (n >= 0) showTab('doors'); state.door = n; if (n >= 0) state.selected = -1; view3d.syncEnemies(); syncSide(); invalidate(); }
function select(i) { if (i >= 0) showTab('enemies'); state.selected = i; state.squad = []; if (i >= 0) state.door = -1; view3d.syncEnemies(); if (i >= 0) { state.lastRole = state.enemies[i].role; state.lastGroup = state.enemies[i].group; } syncSide(); invalidate(); }
function setMode(mode) {
  if (mode === '3d' && !view3d.available()) return say('The 3D view could not start. It needs an internet connection to load its 3D library, and a browser with WebGL.');
  state.mode = mode;
  $('map').hidden = mode === '3d'; $('view3d').hidden = mode !== '3d';
  $('mode2d').setAttribute('aria-pressed', String(mode === '2d')); $('mode3d').setAttribute('aria-pressed', String(mode === '3d'));
  $('zoomIn').hidden = $('zoomOut').hidden = mode === '3d';
  if (mode === '3d') view3d.open();
  syncLayers(); setTool(state.tool); invalidate();
}
function setTool(t) {
  state.tool = t;
  if (t === 'add' || t === 'route') showTab('enemies'); else if (t === 'zone') showTab('triggers');
  document.querySelectorAll('#tools .tool').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.tool === t)));
  toolHint();
}
// The line at the foot of the map: what a click does with the tool in hand. In Play the map only shows.
function toolHint() {
  const t = state.tool;
  if (viewIs('play')) { $('hintLine').textContent = 'The map shows the scenario. To change it, switch to Create.'; return; }
  if (state.mode === '3d') { $('hintLine').textContent = 'Drag to look, W A S D to fly, Q and E for down and up, Shift is faster, scroll moves forward. ' + { select: 'Click an enemy to select it, drag it to move it. Right click: the selected enemy looks there. ', add: 'Click to place an enemy. ', route: 'Click to add route stops. ', zone: 'Click to place a zone. ' }[t]; return; }
  $('hintLine').textContent = { select: 'Click an enemy to select it, drag to move it. Drag empty ground to pan, scroll to zoom. ',
    add: 'Click walkable ground to place an enemy. ', route: 'Click to add stops to the selected enemy\'s patrol route. ', zone: 'Click to place a zone that brings enemies in when players reach it. ' }[t];
}
// What a press at (sx, sy) on the flat map takes hold of. Anything on a floor or room that Floors and rooms hides, locks
// or leaves faint outside what is isolated is passed over (29k-outliner.js), so the press reaches what is free under it.
function hit(sx, sy) {
  const sel = state.enemies[state.selected], free = p => !outlinerBlocks(p);
  if (sel && sel.role === 'Patrol' && free(sel.pos)) for (let n = 0; n < sel.patrol.length; n++) { const [x, y] = toScreen(sel.patrol[n][0], sel.patrol[n][2]); if (Math.hypot(x - sx, y - sy) <= 10 && free(sel.patrol[n])) return { stop: n }; }
  for (let n = 0; n < state.objectives.length; n++) { const [x, y] = toScreen(state.objectives[n].pos[0], state.objectives[n].pos[2]); if (Math.hypot(x - sx, y - sy) <= 10 && free(state.objectives[n].pos)) return { objective: n }; }
  const sealed = sealedHides();            // hidden markers cannot be clicked either (20b-sealed.js)
  for (let n = 0; n < (sealed ? 0 : state.things.length); n++) { const [x, y] = toScreen(state.things[n].pos[0], state.things[n].pos[2]); if (Math.hypot(x - sx, y - sy) <= 9 && free(state.things[n].pos)) return { thing: n }; }
  let best = -1, bestD = 13;
  if (!sealed) state.enemies.forEach((e, i) => { const [x, y] = toScreen(e.pos[0], e.pos[2]), d = Math.hypot(x - sx, y - sy); if (d < bestD && free(e.pos)) { bestD = d; best = i; } });
  if (best >= 0) return { enemy: best };
  const door = state.tool === 'select' ? doorAtScreen(sx, sy) : -1;
  return door >= 0 ? { door } : null;
}
// Where a door's leaf is on screen: from the hinge through the middle, as far again.
function doorLine(d) {
  const [hx, hy] = toScreen(d.pos[0], d.pos[2]), [mx, my] = toScreen(d.mid[0], d.mid[2]);
  if (Math.hypot(mx - hx, my - hy) < 2) return [mx - 6, my, mx + 6, my];          // only a place is known
  return [hx, hy, 2 * mx - hx, 2 * my - hy];
}
function doorAtScreen(sx, sy) {
  let best = -1, bd = 8;
  state.doors.forEach((d, n) => {
    if (d.mid[1] - 1 > state.ceiling + 0.4 || outlinerBlocks(d.mid)) return;
    const g = doorLine(d), dx = g[2] - g[0], dy = g[3] - g[1], t = Math.max(0, Math.min(1, ((sx - g[0]) * dx + (sy - g[1]) * dy) / (dx * dx + dy * dy || 1)));
    const dist = Math.hypot(sx - g[0] - t * dx, sy - g[1] - t * dy);
    if (dist < bd) { bd = dist; best = n; }
  });
  return best;
}
