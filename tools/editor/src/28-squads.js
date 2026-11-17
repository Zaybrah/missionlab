// ------------------------------------------------------------------ squads
// Several enemies selected at once: moved, turned, copied and saved together.
const picked = () => { const all = state.squad.length ? state.squad : state.selected >= 0 ? [state.selected] : []; return all.filter(i => i < state.enemies.length); };
function setSquad(list, lead) {
  const all = [...new Set(list)].filter(i => i >= 0 && i < state.enemies.length).sort((a, b) => a - b);
  state.squad = all.length > 1 ? all : [];
  state.selected = all.length ? (all.includes(lead) ? lead : all[0]) : -1;
  if (all.length) { state.door = -1; showTab('enemies'); }
  view3d.syncEnemies(); syncSide(); invalidate();
}
// The nearest floor to a wanted height at a spot; the wanted height itself where nobody can walk.
function floorNear(x, z, want) { let y = want, best = 2.5; for (const l of levelsAt(x, z)) if (Math.abs(l - want) < best) { best = Math.abs(l - want); y = l; } return y; }
// Enemies as a file would hold them, but measured from the first one, so they can be put down anywhere.
function squadData(list) {
  const o = state.enemies[list[0]].pos, off = p => [p[0] - o[0], p[1] - o[1], p[2] - o[2]];
  return list.map(i => { const sp = spawnOf(state.enemies[i]); return Object.assign(sp, { pos: off(sp.pos), patrol: sp.patrol.map(off) }); });
}
function placeSquad(data, x, z) {
  const base = groundAt(x, z);
  if (base === null) return say('Nobody can walk there. Click on the shaded ground.');
  pushUndo();
  const first = state.enemies.length, lowest = Math.min(...data.map(d => d.group || 0)), free = state.enemies.reduce((m, e) => Math.max(m, e.group + 1), 0);
  const at = p => { const X = x + p[0], Z = z + p[2]; return [X, floorNear(X, Z, base + p[1]), Z]; };
  for (const d of data) state.enemies.push(enemyOf(Object.assign({}, d, { pos: at(d.pos), patrol: d.patrol.map(at), group: (d.group || 0) - lowest + free })));
  state.placing = null; canvas.style.cursor = 'default';
  setSquad(Array.from({ length: data.length }, (_, k) => first + k), first); changed();
  say(data.length + (data.length === 1 ? ' enemy placed.' : ' enemies placed, as a new group. Drag any of them to move all.'));
}
function turnSquad(deg) {
  const list = picked(); if (list.length < 2) return;
  pushUndo();
  const cx = list.reduce((t, i) => t + state.enemies[i].pos[0], 0) / list.length, cz = list.reduce((t, i) => t + state.enemies[i].pos[2], 0) / list.length;
  const a = -deg * Math.PI / 180, cos = Math.cos(a), sin = Math.sin(a);          // clockwise on the map for a positive turn, like facing
  const spin = p => { const dx = p[0] - cx, dz = p[2] - cz, X = cx + dx * cos - dz * sin, Z = cz + dx * sin + dz * cos; return [X, floorNear(X, Z, p[1]), Z]; };
  for (const i of list) { const e = state.enemies[i]; e.pos = spin(e.pos); e.patrol = e.patrol.map(spin); e.yaw = ((e.yaw + deg) % 360 + 360) % 360; }
  changed();
}
let clip = null, pointerAt = null;
function copySquad() { const list = picked(); if (!list.length) return false; clip = squadData(list); syncSide(); say(list.length + (list.length === 1 ? ' enemy copied.' : ' enemies copied.') + ' Ctrl+V puts them where the pointer is.'); return true; }
function startPlacing(data, what) { state.placing = data; canvas.style.cursor = 'crosshair'; say('Click the map where ' + what + ' should go. Esc cancels.'); }
function pasteSquad() { if (!clip) return false; if (pointerAt && state.mode !== '3d') placeSquad(clip, pointerAt[0], pointerAt[1]); else startPlacing(clip, 'the copied enemies'); return true; }
// Squads kept for later, on this computer. Two come with the editor.
const BUILT_IN_SQUADS = [
  { name: 'Pair of guards', fixed: true, data: [0, 2].map(x => Object.assign(spawnOf(newEnemy([x, 0, 0], 'Guard', 0)), { defensive: true })) },
  { name: 'Fire team of four', fixed: true, data: [[0, 0], [3, 2], [-3, 2], [0, 4]].map(p => Object.assign(spawnOf(newEnemy([p[0], 0, p[1]], 'Wander', 0)), { wanderDistance: 6 })) },
];
function savedSquads() { try { const list = JSON.parse(localStorage.getItem('missionlab-squads') || '[]'); return Array.isArray(list) ? list.filter(q => q && typeof q.name === 'string' && Array.isArray(q.data) && q.data.length) : []; } catch (e) { return []; } }
function storeSquads(list) { try { localStorage.setItem('missionlab-squads', JSON.stringify(list)); } catch (e) { say('This browser will not keep squads between visits.'); } }
const allSquads = () => BUILT_IN_SQUADS.concat(savedSquads());

let drag = null;
canvas.addEventListener('pointerdown', ev => {
  canvas.setPointerCapture(ev.pointerId);
  const r = canvas.getBoundingClientRect(), sx = ev.clientX - r.left, sy = ev.clientY - r.top;
  let target = ev.button === 0 ? hit(sx, sy) : null;
  if (target && 'enemy' in target && state.tool === 'route' && state.selected >= 0 && target.enemy !== state.selected) target = null;   // drawing a route past another enemy
  drag = { sx, sy, moved: false, target, pan: !target || ev.button !== 0 || 'door' in target, cx: state.view.cx, cz: state.view.cz, saved: false, button: ev.button };   // a door stays where it is
  const several = ev.shiftKey && ev.button === 0 && state.tool === 'select';
  if (several && !target) { drag.box = true; drag.pan = false; }                 // Shift and drag over empty ground: a box around enemies
  else if (several && 'enemy' in target) {                                         // Shift-click: in or out of the selection
    const list = picked(); setSquad(list.includes(target.enemy) ? list.filter(i => i !== target.enemy) : list.concat(target.enemy), target.enemy); drag.pan = true; drag.held = true;
  }
  else if (target && 'enemy' in target && picked().length > 1 && picked().includes(target.enemy)) {   // grabbing one of several: they all come along
    drag.squad = picked().map(i => ({ i, pos: state.enemies[i].pos.slice(), patrol: state.enemies[i].patrol.map(q => q.slice()) })); drag.grab = toWorld(sx, sy);
    state.selected = target.enemy; syncSide(); invalidate();
  }
  else if (target && 'enemy' in target) select(target.enemy);
  if (target && 'door' in target) selectDoor(target.door);
  if (target && 'thing' in target) selectThing(target.thing);
});
canvas.addEventListener('pointermove', ev => {
  const r = canvas.getBoundingClientRect(), sx = ev.clientX - r.left, sy = ev.clientY - r.top, [wx, wz] = toWorld(sx, sy), y = groundAt(wx, wz);
  const floorsHere = levelsAt(wx, wz).length;
  $('coords').textContent = 'x ' + wx.toFixed(1) + '  z ' + wz.toFixed(1) + (y === null ? '  no ground' : '  height ' + y.toFixed(1)) + (floorsHere > 1 ? '  (' + floorsHere + ' floors here)' : '');
  pointerAt = [wx, wz];
  if (!drag) { canvas.style.cursor = state.placing ? 'crosshair' : hit(sx, sy) ? 'grab' : (state.tool === 'select' ? 'default' : 'crosshair'); return; }
  if (!drag.moved && Math.hypot(sx - drag.sx, sy - drag.sy) < 4) return;
  drag.moved = true;
  if (drag.held) return;                                  // a Shift-click that slipped a little is still a click
  if (drag.box) { drag.bx = sx; drag.by = sy; invalidate(); return; }
  if (drag.squad) {
    if (!drag.saved) { pushUndo(); drag.saved = true; }
    const dx = wx - drag.grab[0], dz = wz - drag.grab[1], shift = q => { const X = q[0] + dx, Z = q[2] + dz; return [X, floorNear(X, Z, q[1]), Z]; };
    for (const m of drag.squad) { const e = state.enemies[m.i]; if (e) { e.pos = shift(m.pos); e.patrol = m.patrol.map(shift); } }
    invalidate(); syncPos(); return;
  }
  if (drag.pan) { state.view.cx = drag.cx - (sx - drag.sx) / state.view.scale; state.view.cz = drag.cz + (sy - drag.sy) / state.view.scale; invalidate(); return; }
  if (y === null) return;                                 // keep the last valid spot while over a wall
  if (!drag.saved) { pushUndo(); drag.saved = true; }
  if ('thing' in drag.target) { const t = state.things[drag.target.thing]; if (t) t.pos = [wx, y + 0.02, wz]; invalidate(); return; }
  if ('objective' in drag.target) { const o = state.objectives[drag.target.objective]; if (o) { o.pos = [wx, y + 0.02, wz]; o.moved = true; } invalidate(); return; }   // on the floor; use 3D for furniture
  const e = state.enemies[state.selected]; if (!e) return;
  if ('stop' in drag.target) e.patrol[drag.target.stop] = [wx, y, wz]; else e.pos = [wx, y, wz];
  invalidate(); syncPos();
});
canvas.addEventListener('pointerup', ev => {
  const d = drag; drag = null;
  if (!d) return;
  if (d.box) {
    if (d.moved) {
      const x0 = Math.min(d.sx, d.bx), x1 = Math.max(d.sx, d.bx), y0 = Math.min(d.sy, d.by), y1 = Math.max(d.sy, d.by);
      const inside = state.enemies.map((e, i) => { const [x, y] = toScreen(e.pos[0], e.pos[2]); return x >= x0 && x <= x1 && y >= y0 && y <= y1 && e.pos[1] <= state.ceiling + 0.4 ? i : -1; }).filter(i => i >= 0);
      setSquad(picked().concat(inside), inside[0]); say(picked().length + (picked().length === 1 ? ' enemy selected.' : ' enemies selected.'));
    }
    invalidate(); return;
  }
  if (d.held) return;
  if (d.squad && !d.moved) { select(d.target.enemy); return; }           // a plain click on one of several picks just that one
  if (d.moved) { if (d.saved) changed(); return; }
  if (state.placing && d.button === 0) { const [px, pz] = toWorld(d.sx, d.sy); placeSquad(state.placing, px, pz); return; }
  if (d.button === 2) { const [lx, lz] = toWorld(d.sx, d.sy); if (lookAt(lx, lz)) say('Enemy ' + (state.selected + 1) + ' now looks that way.'); return; }
  if (d.button !== 0) return;
  const [wx, wz] = toWorld(d.sx, d.sy), y = groundAt(wx, wz);
  if (state.order && !d.target) {
    const ids = state.order; state.order = null;
    if (y === null) return say('Nobody can walk there. Pick a spot on the shaded ground.');
    liveCommand('order', { ids, pos: [r2(wx), r2(y), r2(wz)] });
    return say(ids.length + (ids.length === 1 ? ' enemy is' : ' enemies are') + ' told to go there.');
  }
  if (state.tool === 'add' && !d.target) {
    if (y === null) return say('Nobody can walk there. Click on the shaded ground' + (isFinite(state.ceiling) ? ', or raise "Show up to" to reach higher floors.' : '.'));
    pushUndo();
    state.enemies.push(facePlayers(newEnemy([wx, y, wz], state.lastRole === 'Patrol' ? 'Guard' : state.lastRole, state.lastGroup)));
    state.selected = state.enemies.length - 1; changed(); say('Enemy ' + state.enemies.length + ' placed.');
  } else if (state.tool === 'route' && !d.target) {
    const e = state.enemies[state.selected];
    if (!e) return say('Select an enemy first, then click the stops of its route.');
    if (y === null) return say('Nobody can walk there. Route stops must be on the shaded ground.');
    pushUndo(); e.role = 'Patrol'; e.fixed = false; e.patrol.push([wx, y, wz]); changed();
    say('Stop ' + e.patrol.length + ' added' + (e.patrol.length < 2 ? '. Add at least one more.' : '.'));
  } else if (state.tool === 'zone' && !d.target) {
    if (y === null) return say('Place the zone on the shaded ground.');
    addZone(wx, y, wz);
  } else if (state.tool === 'thing' && !d.target) {
    if (y === null) return say('Lay it on the shaded ground.');
    pushUndo(); state.things.push({ name: state.thingName, pos: [wx, y + 0.02, wz], yaw: 0, trigger: 0, chance: 100 });
    selectThing(state.things.length - 1); changed(); say(state.thingName.trim() + ' placed.');
  } else if (state.tool === 'select' && !d.target) { select(-1); if (state.thing >= 0) { state.thing = -1; syncSide(); invalidate(); } }
});
// The selected enemy turns to look at a point.
function lookAt(x, z) {
  const e = state.enemies[state.selected];
  if (!e || Math.hypot(x - e.pos[0], z - e.pos[2]) < 0.3) return false;
  pushUndo(); e.yaw = (Math.atan2(x - e.pos[0], z - e.pos[2]) * 180 / Math.PI + 360) % 360; changed();
  return true;
}
canvas.addEventListener('pointercancel', () => { drag = null; });
canvas.addEventListener('contextmenu', ev => ev.preventDefault());
canvas.addEventListener('wheel', ev => { ev.preventDefault(); const r = canvas.getBoundingClientRect(); zoomAt(ev.clientX - r.left, ev.clientY - r.top, Math.exp(-ev.deltaY * 0.0015)); }, { passive: false });

function removeSelected() {
  const list = picked(); if (!list.length) return;
  pushUndo();
  for (const i of list.slice().sort((a, b) => b - a)) state.enemies.splice(i, 1);
  state.squad = []; state.selected = Math.min(list[0], state.enemies.length - 1); changed();
}
window.addEventListener('keydown', ev => {
  if (/^(INPUT|TEXTAREA|SELECT)$/.test(ev.target.tagName) && ev.target.type !== 'range' && ev.target.type !== 'checkbox') return;
  const key = ev.key.toLowerCase();
  if (state.mode === '3d' && !ev.ctrlKey && !ev.metaKey && view3d.key(key, true)) { ev.preventDefault(); return; }   // flying keys
  const plain = !window.getSelection || window.getSelection().isCollapsed;           // not while text on the page is selected for copying
  if ((ev.ctrlKey || ev.metaKey) && key === 'z') { ev.preventDefault(); undo(); }
  else if ((ev.ctrlKey || ev.metaKey) && key === 'c' && plain) { if (copySquad()) ev.preventDefault(); }
  else if ((ev.ctrlKey || ev.metaKey) && key === 'v') { if (pasteSquad()) ev.preventDefault(); }
  else if ((ev.ctrlKey || ev.metaKey) && key === 'd') { if (picked().length) { ev.preventDefault(); const e0 = state.enemies[picked()[0]]; placeSquad(squadData(picked()), e0.pos[0] + 2, e0.pos[2] + 2); } }
  else if ((ev.ctrlKey || ev.metaKey) && key === 'a' && state.enemies.length) { ev.preventDefault(); setSquad(state.enemies.map((e, i) => i), state.selected); }
  else if (ev.ctrlKey || ev.metaKey || ev.altKey) return;
  else if (key === 'v') setTool('select'); else if (key === 'a') setTool('add'); else if (key === 'r') setTool('route'); else if (key === 'z') setTool('zone');
  else if (key === 'escape') { state.placing = null; setTool('select'); select(-1); }
  else if (key === 'delete') removeSelected();
  else if (key === 'pagedown') { ev.preventDefault(); stepFloor(-1); } else if (key === 'pageup') { ev.preventDefault(); stepFloor(1); }
  else if (key === 'backspace' && state.tool === 'route') { const e = state.enemies[state.selected]; if (e && e.patrol.length) { ev.preventDefault(); pushUndo(); e.patrol.pop(); changed(); } }
});

