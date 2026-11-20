// ------------------------------------------------------------------ moving an encounter together (ML-045)
// The selected enemies (one, or a squad from 28-squads.js) with what belongs to them: their routes, the triggers that bring
// them (anchor and painted area), the things that appear with those triggers, and the shaped places they stand in. All of
// it moves and turns around one pivot, the middle of the enemies, as one undo step. What cannot follow is listed and stays:
// the map's doors (a trigger keeps acting on the door where the map has it), triggers without a place of their own, a place
// that zones outside the encounter are linked to (ML-040: moving it would move their room too), and enemies the game has
// already spawned. A zone linked to a place moves its anchor only: its shape is the place's, and goes with the place or stays
// with it. The author sees the list before anything moves, and can untick a trigger, a thing or a place to leave it where it is.
const encounterUi = { dx: 0, dz: 0, turn: 0, skip: new Set(), of: '' };    // skip: keys the author unticked, for the selection in "of"

const encounterEnemyName = i => 'Enemy ' + (i + 1);
const encounterDoorName = d => 'Door' + (d && d.name ? ' "' + d.name + '"' : '');
// Every position of the encounter goes through this: turned around the pivot (positive degrees turn clockwise on the map,
// like facing and the squad turn), then shifted.
function encounterSpin(move, pivot) {
  const a = -move.turn * Math.PI / 180, cos = Math.cos(a), sin = Math.sin(a);
  return (x, z) => { const dx = x - pivot[0], dz = z - pivot[1]; return [pivot[0] + dx * cos - dz * sin + move.dx, pivot[1] + dx * sin + dz * cos + move.dz]; };
}
// What would move and what stays, without changing anything. list: enemy indices; move: { dx, dz, turn } in metres and
// degrees; skip: keys ('trigger:2', 'place:1', 'thing:0') left where they are. error is set when it cannot be done.
function encounterPlan(list, move, skip) {
  move = { dx: num(+(move && move.dx), 0), dz: num(+(move && move.dz), 0), turn: num(+(move && move.turn), 0) };
  skip = skip || new Set();
  const out = { error: '', moves: [], stays: [], enemies: [], triggers: [], things: [], places: [], pivot: null, move };
  const stay = (key, what, why) => { if (!out.stays.some(s => s.key === key)) out.stays.push({ key, what, why }); };
  for (const i of [...new Set(list || [])].sort((a, b) => a - b)) {
    const e = state.enemies[i]; if (!e) continue;
    if (live.on && e.live) stay('enemy:' + i, encounterEnemyName(i), 'stays: already in the running game');
    else { out.enemies.push(i); out.moves.push({ key: 'enemy:' + i, what: encounterEnemyName(i) + (e.patrol.length ? ' and its route of ' + e.patrol.length + ' stops' : ''), always: true }); }
  }
  if (!out.enemies.length) { out.error = out.stays.length ? 'the selected enemies are already in the running game' : 'select one or more enemies first'; return out; }
  const ids = new Set(out.enemies.map(i => state.enemies[i].trigger).filter(id => id > 0));
  state.zones.forEach((z, zi) => {
    if (!ids.has(z.id)) return;
    const key = 'trigger:' + z.id, what = trigTitle(z) + (z.name ? ' "' + z.name + '"' : '');
    const painted = !!(z.area && Array.isArray(z.area.parts) && z.area.parts.length) && !(z.place > 0);
    const linkedTo = z.place > 0 ? state.places.find(p => p.id === z.place) : null;
    if (z.when === 'door') { stay(key, what, 'stays: map-bound (it watches a door of the map)'); return; }
    if (!painted && !(isVec(z.pos) && (z.when === 'zone' || z.when === 'hold'))) { stay(key, what, 'stays: it has no place on the map'); return; }
    if (skip.has(key)) { out.moves.push({ key, what, off: true }); return; }
    out.triggers.push(zi); out.moves.push({ key, what: what + (painted ? ', its anchor and painted area' : linkedTo ? ', its anchor (its shape is place "' + linkedTo.name + '"\'s)' : ', its anchor') });
  });
  const moved = new Set(out.triggers.map(zi => state.zones[zi].id));
  state.things.forEach((t, ti) => {
    if (!(t.trigger > 0) || !ids.has(t.trigger)) return;
    const key = 'thing:' + ti, what = 'The ' + String(t.name).trim() + ' (appears with trigger ' + t.trigger + ')';
    if (!moved.has(t.trigger)) { stay(key, what, 'stays with trigger ' + t.trigger); return; }
    if (skip.has(key)) { out.moves.push({ key, what, off: true }); return; }
    out.things.push(ti); out.moves.push({ key, what });
  });
  const movedZones = new Set(out.triggers.map(zi => state.zones[zi]));
  state.places.forEach((p, pi) => {
    if (!(p.area && Array.isArray(p.area.parts) && p.area.parts.length) || !out.enemies.some(i => areaContains(p.area, state.enemies[i].pos))) return;
    const key = 'place:' + p.id, what = 'Place "' + p.name + '" and its shape';
    const sharers = state.zones.filter(z => z.place > 0 && z.place === p.id && !movedZones.has(z));
    if (sharers.length) { stay(key, what, 'stays: shared with ' + sharers.map(z => trigTitle(z).toLowerCase()).join(', ') + ', which would move too'); return; }
    if (skip.has(key)) { out.moves.push({ key, what, off: true }); return; }
    out.places.push(pi); out.moves.push({ key, what });
  });
  // The map's doors: inside what moves, or acted on by a trigger that moves. They are part of the map, so they never move.
  const areas = out.triggers.map(zi => state.zones[zi]).concat(out.places.map(pi => state.places[pi]));
  state.doors.forEach((d, di) => {
    const at = isVec(d.mid) ? d.mid : d.pos;
    const inside = isVec(at) && areas.some(h => (h.area && h.area.parts && h.area.parts.length ? areaContains(h.area, at) : membershipInside(h, at)));
    const users = out.triggers.map(zi => state.zones[zi]).filter(z => (z.doors || []).some(x => isVec(x.pos) && nearDoor(x.pos, [d]) === 0));
    if (inside || users.length) stay('door:' + di, encounterDoorName(d), 'stays: map-bound' + (users.length ? '; ' + users.map(z => trigTitle(z).toLowerCase()).join(' and ') + ' still acts on it where it is' : ''));
  });
  for (const zi of out.triggers) for (const x of state.zones[zi].doors || []) {
    if (isVec(x.pos) && nearDoor(x.pos, state.doors) < 0) stay('door@' + x.pos.join(','), encounterDoorName(x), 'stays: map-bound; ' + trigTitle(state.zones[zi]).toLowerCase() + ' still acts on it where it is');
  }
  out.pivot = [out.enemies.reduce((t, i) => t + state.enemies[i].pos[0], 0) / out.enemies.length, out.enemies.reduce((t, i) => t + state.enemies[i].pos[2], 0) / out.enemies.length];
  if (!move.dx && !move.dz && !(Math.abs(move.turn % 360) > 1e-9)) { out.error = 'give a distance or a turn first'; return out; }
  // Refused before anything changes: an enemy or a route stop that would end up where nobody can walk.
  if (state.map) {
    const spin = encounterSpin(move, out.pivot);
    const walkable = p => { const q = spin(p[0], p[2]); return levelsAt(q[0], q[1]).some(l => Math.abs(l - p[1]) <= 2.5); };
    const wrong = [];
    for (const i of out.enemies) {
      const e = state.enemies[i];
      if (!e.fixed && !walkable(e.pos)) wrong.push(encounterEnemyName(i).toLowerCase());
      else if (e.patrol.some(p => !walkable(p))) wrong.push('the route of ' + encounterEnemyName(i).toLowerCase());
    }
    if (wrong.length) out.error = wrong.slice(0, 3).join(', ') + (wrong.length > 3 ? ' and ' + (wrong.length - 3) + ' more' : '') + ' would end up where nobody can walk';
  }
  return out;
}
const encounterStepName = plan => {
  const m = plan.move, n = plan.enemies.length, verb = (m.dx || m.dz) && m.turn ? 'Move and turn ' : m.turn ? 'Turn ' : 'Move ';
  const rest = plan.triggers.length + plan.things.length + plan.places.length;
  return verb + (n === 1 ? 'enemy ' + (plan.enemies[0] + 1) : n + ' enemies') + (rest ? ' with ' + rest + (rest === 1 ? ' other part' : ' other parts') : '');
};
// Moves it all as one undo step. Returns the plan; done is true when something moved.
function encounterMove(list, move, skip) {
  const plan = encounterPlan(list, move, skip || encounterUi.skip);
  plan.done = false;
  if (plan.error) { say('Nothing was moved: ' + plan.error + '.'); return plan; }
  const m = plan.move, spin = encounterSpin(m, plan.pivot), turnYaw = y => (((num(y, 0) + m.turn) % 360) + 360) % 360;
  const at = p => { const q = spin(p[0], p[2]); return [r2(q[0]), p[1], r2(q[1])]; };
  const onFloor = p => { const q = at(p); return state.map ? [q[0], floorNear(q[0], q[2], p[1]), q[2]] : q; };
  const ring = r => r.map(p => spin(p[0], p[1]));
  const reshape = h => {
    if (isVec(h.pos)) h.pos = at(h.pos);
    if (h.area && Array.isArray(h.area.parts) && h.area.parts.length && !(h.place > 0)) {     // a linked zone's shape is its place's
      h.area.parts = h.area.parts.map(p => paintPart({ floor: p.floor, outline: ring(p.outline), holes: (p.holes || []).map(ring) }));
      areaPrepare(h.area);
    }
  };
  paintGestureDrop();
  willChange(encounterStepName(plan));
  for (const i of plan.enemies) { const e = state.enemies[i]; e.pos = onFloor(e.pos); e.patrol = e.patrol.map(onFloor); e.yaw = turnYaw(e.yaw); }
  for (const zi of plan.triggers) reshape(state.zones[zi]);
  for (const pi of plan.places) { reshape(state.places[pi]); markPlace(state.places[pi].id, 'moved'); }
  placeLinksResolve();          // zones linked to a place that moved take its new shape (29b-places.js)
  for (const ti of plan.things) { const t = state.things[ti]; t.pos = at(t.pos); t.yaw = turnYaw(t.yaw); }
  plan.done = true;
  encounterUi.dx = encounterUi.dz = encounterUi.turn = 0;
  changed();
  say(encounterStepName(plan) + '. ' + (plan.stays.length ? plan.stays.length + (plan.stays.length === 1 ? ' part stays' : ' parts stay') + ' where ' + (plan.stays.length === 1 ? 'it is' : 'they are') + ': ' + plan.stays.map(s => s.what).join(', ') + '. ' : '') + 'Undo puts it all back in one step.');
  return plan;
}
// The panel on the Enemies tab: the offset and turn, then every part with whether it moves or stays.
function syncEncounter() {
  const box = $('encounterBox'); if (!box) return;
  const list = picked(), of = list.join(',');
  box.hidden = !list.length;
  if (!list.length) return;
  if (encounterUi.of !== of) { encounterUi.of = of; encounterUi.skip.clear(); }
  for (const [id, k] of [['encDx', 'dx'], ['encDz', 'dz'], ['encTurn', 'turn']]) setVal($(id), String(encounterUi[k]));
  const plan = encounterPlan(list, encounterUi, encounterUi.skip), ul = $('encList');
  $('encCount').textContent = plan.moves.filter(r => !r.off).length + ' moving';
  if (!ul.contains(document.activeElement)) {
    ul.textContent = '';
    for (const r of plan.moves) {
      const li = document.createElement('li');
      if (r.always) li.textContent = r.what;
      else {
        const l = document.createElement('label'), c = document.createElement('input');
        c.type = 'checkbox'; c.checked = !r.off; c.setAttribute('aria-label', 'Move ' + r.what + ' too');
        c.addEventListener('change', () => { if (c.checked) encounterUi.skip.delete(r.key); else encounterUi.skip.add(r.key); syncEncounter(); });
        l.append(c, ' ' + r.what + (r.off ? ' (stays)' : '')); li.appendChild(l);
      }
      ul.appendChild(li);
    }
    for (const s of plan.stays) {
      const li = document.createElement('li'), chip = document.createElement('span');
      li.className = 'stays'; li.textContent = s.what + ' ';
      chip.className = 'chip warn'; chip.textContent = s.why; li.appendChild(chip);
      ul.appendChild(li);
    }
  }
  $('encGo').disabled = !!plan.error;
  $('encNote').textContent = plan.error ? 'Not possible yet: ' + plan.error + '.' : 'One step: Undo puts every part back together.';
}
const encounterField = (id, k) => $(id).addEventListener('input', ev => { encounterUi[k] = num(+ev.target.value, 0); syncEncounter(); });
encounterField('encDx', 'dx'); encounterField('encDz', 'dz'); encounterField('encTurn', 'turn');
$('encGo').addEventListener('click', () => encounterMove(picked(), encounterUi, encounterUi.skip));
$('histUndo').addEventListener('click', () => undo());
$('histRedo').addEventListener('click', () => redo());
expose({ txBegin, txCommit, txCancel, redo, historyList, encounterPlan, encounterMove });   // undo history and moving an encounter together (ML-045), for automated tests
