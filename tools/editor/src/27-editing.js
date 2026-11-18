// ------------------------------------------------------------------ editing
function pushUndo() { undoStack.push(JSON.stringify({ e: state.enemies, s: state.selected, z: state.zones, o: state.objectives, d: doorsOut(), x: settingsOut(), t: thingsOut(), p: state.places,
  k: state.sealed, op: state.options, pl: state.placement, gm: state.director })); if (undoStack.length > 80) undoStack.shift(); }
function undo() {
  const snap = undoStack.pop(); if (!snap) return say('Nothing to undo.');
  const o = JSON.parse(snap); state.enemies = o.e; state.zones = o.z || []; state.objectives = o.o || state.objectives; setDoorWishes(o.d || []); settingsIn(o.x || {}); state.things = thingsIn(o.t); state.thing = -1; state.squad = []; state.selected = Math.min(o.s, o.e.length - 1);
  if (Array.isArray(o.p)) state.places = o.p;
  state.sealed = o.k === true; state.options = Array.isArray(o.op) ? o.op : []; state.placement = Array.isArray(o.pl) ? o.pl : []; state.director = o.gm || null;
  changed();
}
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
function hit(sx, sy) {
  const sel = state.enemies[state.selected];
  if (sel && sel.role === 'Patrol') for (let n = 0; n < sel.patrol.length; n++) { const [x, y] = toScreen(sel.patrol[n][0], sel.patrol[n][2]); if (Math.hypot(x - sx, y - sy) <= 10) return { stop: n }; }
  for (let n = 0; n < state.objectives.length; n++) { const [x, y] = toScreen(state.objectives[n].pos[0], state.objectives[n].pos[2]); if (Math.hypot(x - sx, y - sy) <= 10) return { objective: n }; }
  const sealed = sealedHides();            // hidden markers cannot be clicked either (20b-sealed.js)
  for (let n = 0; n < (sealed ? 0 : state.things.length); n++) { const [x, y] = toScreen(state.things[n].pos[0], state.things[n].pos[2]); if (Math.hypot(x - sx, y - sy) <= 9) return { thing: n }; }
  let best = -1, bestD = 13;
  if (!sealed) state.enemies.forEach((e, i) => { const [x, y] = toScreen(e.pos[0], e.pos[2]), d = Math.hypot(x - sx, y - sy); if (d < bestD) { bestD = d; best = i; } });
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
    if (d.mid[1] - 1 > state.ceiling + 0.4) return;
    const g = doorLine(d), dx = g[2] - g[0], dy = g[3] - g[1], t = Math.max(0, Math.min(1, ((sx - g[0]) * dx + (sy - g[1]) * dy) / (dx * dx + dy * dy || 1)));
    const dist = Math.hypot(sx - g[0] - t * dx, sy - g[1] - t * dy);
    if (dist < bd) { bd = dist; best = n; }
  });
  return best;
}
