// ---------------------------------------------------------------- mission check: what would go wrong, before a mission is spent on it
function missionCheck() {
  // A finding that a repair may fix carries an offer for it (see 34a-repairs.js): { kind, ... }.
  const out = [], add = (level, text, go, repair) => out.push(repair ? { level, text, go, repair } : { level, text, go });
  // A zone, an enemy or a thing is called by the named place it stands in, when there is one (29b-places.js).
  const at = z => (z.when === 'zone' ? placeWords(z.pos) : '');
  const byId = id => state.zones.find(z => z.id === id), name = z => trigTitle(z) + (z.name ? ' (' + z.name + ')' : '') + at(z), lower = z => trigTitle(z).toLowerCase() + (z.name ? ' (' + z.name + ')' : '') + at(z);
  const enemy = i => 'Enemy ' + (i + 1) + placeWords(state.enemies[i].pos);
  const toTriggers = () => showTab('triggers', true), toMission = () => showTab('mission', true);
  const toEnemy = i => () => { select(i); showTab('enemies', true); const e = state.enemies[i]; if (e) { state.view.cx = e.pos[0]; state.view.cz = e.pos[2]; invalidate(); } };
  // Can a trigger ever fire without the Game Master? One that waits for another needs that one to fire, and so on.
  const memo = new Map();
  const canFire = (z, trail) => {
    if (memo.has(z.id)) return memo.get(z.id);
    let ok = true;
    if (z.when === 'zone' || z.when === 'door') ok = isVec(z.pos);
    else if (z.when === 'after') { const p = byId(z.after); ok = !!p && p.id !== z.id && !trail.includes(p.id) && canFire(p, trail.concat(z.id)); }
    else if (z.when === 'all' || z.when === 'any') {
      const list = (z.of || []).map(byId).filter(q => q && q.id !== z.id), can = q => !trail.includes(q.id) && canFire(q, trail.concat(z.id));
      ok = list.length > 0 && list.length === (z.of || []).filter(id => id !== z.id).length ? (z.when === 'all' ? list.every(can) : list.some(can)) : (z.when === 'any' && list.some(can));
    }
    if (!trail.length) memo.set(z.id, ok);
    return ok;
  };
  // Does a trigger fire on EVERY run? '' when it does; otherwise the reason. Chance, a by-hand trigger and missing links break that.
  const sure = (z, trail) => {
    if (trail.includes(z.id)) return name(z) + ' waits for itself through other triggers';
    const on = trail.concat(z.id);
    if ((z.chance || 100) < 100) return name(z) + ' has a chance of ' + z.chance + '%';
    if (z.when === 'after') {
      if (!z.after) return state.gameMaster ? '' : name(z) + ' is fired by hand';
      const p = byId(z.after); if (!p) return name(z) + ' waits for a trigger that is gone';
      const w = sure(p, on); return w ? name(z) + ' waits for it: ' + w : '';
    }
    if (z.when === 'all') { if (!(z.of || []).length) return name(z) + ' has an empty list'; for (const id of z.of) { const p = byId(id); if (!p) return name(z) + ' waits for a trigger that is gone'; const w = sure(p, on); if (w) return name(z) + ' needs all of its list: ' + w; } return ''; }
    if (z.when === 'any') return (z.of || []).some(id => byId(id) && !sure(byId(id), on)) ? '' : name(z) + ' has no trigger in its list that fires on every run';
    if (z.when === 'down' && z.tag) {
      const tagged = state.enemies.filter(e => e.tag === z.tag);
      if (!tagged.length) return name(z) + ' waits for a tag no enemy has';
      const shaky = tagged.filter(e => (e.chance || 100) < 100 || e.either > 0 || e.minPlayers > 1 || (e.trigger && byId(e.trigger) && sure(byId(e.trigger), on)));
      if (shaky.length === tagged.length || ((z.count | 0) <= 0 && tagged.some(e => e.trigger && byId(e.trigger) && sure(byId(e.trigger), on)))) return name(z) + ' waits for enemies tagged "' + z.tag + '" that are not there on every run';
    }
    return '';
  };
  if (state.extractionAfter) {
    const gate = byId(state.extractionAfter), w = gate ? sure(gate, []) : '';
    if (w && !state.zones.some(z => z.opensExtraction && !sure(z, [])))
      add('problem', 'On some runs nobody could leave: the extraction waits for ' + name(gate) + ', and ' + w + '. The game refuses the scenario on such a run and plays its own mission.', toTriggers);
  }
  const targets = state.enemies.filter(e => e.hvt && !e.trigger);
  if (targets.length && targets.every(e => (e.chance || 100) < 100 || e.either > 0 || e.minPlayers > 1))
    add('problem', 'The target person can be left out on some runs (chance, either/or or players needed on every target). The game refuses the scenario on such a run.', () => showTab('enemies', true));
  // Triggers that wait for each other in a circle never fire.
  { const done = new Set(), told = new Set();
    const walk = (z, path) => {
      if (path.includes(z.id)) { const ring = path.slice(path.indexOf(z.id)), key = ring.slice().sort().join(','); if (!told.has(key)) { told.add(key); add('problem', ring.length === 1 ? name(z) + ' waits for itself: it can never fire.' : ring.map(id => name(byId(id))).join(', ') + ' wait for each other in a circle: none of them can ever fire.', toTriggers); } return; }
      if (done.has(z.id)) return;
      const needs = z.when === 'after' && z.after ? [z.after] : z.when === 'all' || z.when === 'any' ? (z.of || []) : [];
      for (const id of needs) { const p = byId(id); if (p) walk(p, path.concat(z.id)); }
      done.add(z.id);
    };
    for (const z of state.zones) walk(z, []); }
  if (state.refused) for (const f of state.refused.errors.slice(0, 12)) add('problem', state.refused.name + ' was not loaded. ' + (f.path ? f.path + ': ' : '') + f.text + '.', toMission);
  for (const b of moduleOpen()) if (b.required) add('problem', b.label + ' (copy ' + b.copy + ' of the module "' + b.module + '") is not bound to anything on this map yet. The scenario cannot be saved until it is.', toTriggers);
  for (const n of state.readNotes || []) add('note', n, toMission);
  if (state.respawnAfter > 0) add('note', 'Reinforcement wave: everyone who is dead comes back together, ' + Math.round(state.respawnAfter) + ' s after the first death. If the whole squad is dead at once the game may end the mission before the wave comes: that has not been tried.', toMission);
  const kinds = [...new Set(state.objectives.map(o => o.label || o.name).filter(Boolean))];
  // Two triggers with one number: the game refuses the file, and whatever waits for that number could mean either of them.
  { const count = new Map(); for (const z of state.zones) count.set(z.id, (count.get(z.id) || 0) + 1);
    for (const [id, k] of count) if (k > 1) add('problem', k + ' triggers have number ' + id + ' (' + state.zones.filter(z => z.id === id).map(name).join(', ') + '). The game refuses a file that uses a trigger number twice.', toTriggers, { kind: 'renumber', id }); }
  state.zones.forEach((z, zi) => {
    const n = state.enemies.filter(e => e.trigger === z.id).length;
    const does = n || state.things.some(t => t.trigger === z.id) || (z.doors || []).some(d => d.locked != null || d.open != null) || z.opensExtraction || z.power || z.message || z.end || z.rally > 0 || z.respawn || state.extractionAfter === z.id || state.zones.some(q => (q.when === 'after' && q.after === z.id) || ((q.when === 'all' || q.when === 'any') && (q.of || []).includes(z.id)));
    const byHand = z.when === 'after' && !z.after;
    if (z.when === 'door' && !isVec(z.pos)) add('problem', name(z) + ' waits for a door, but no door is chosen.', toTriggers);
    if (z.when === 'down' && z.tag && !state.enemies.some(e => e.tag === z.tag)) add('problem', name(z) + ' waits for enemies tagged "' + z.tag + '", but no enemy has that tag.', toTriggers);
    if (z.repeat > 0 && z.end) add('note', name(z) + ' repeats, but it also ends the mission: it will only fire once.', toTriggers);
    if (byHand) { if (does) add('note', name(z) + ' fires only when the Game Master presses Fire now.', toTriggers); }
    else if (!canFire(z, [])) add('problem', name(z) + ' can never fire: ' + (z.when === 'zone' ? 'it has no place on the map' : z.when === 'all' || z.when === 'any' ? ((z.of || []).length ? 'the triggers it waits for can never fire, or wait for it' : 'no triggers are ticked for it') : byId(z.after) ? 'it waits for a trigger that in turn waits for it' : 'the trigger it waits for is gone') + '.', toTriggers, z.when === 'after' && z.after && !byId(z.after) ? { kind: 'after', zi } : null);
    if ((z.when === 'all' || z.when === 'any') && (z.of || []).some(id => id === z.id || !byId(id))) add('warning', name(z) + ' lists ' + (z.of || []).filter(id => id === z.id || !byId(id)).map(id => (id === z.id ? 'itself' : 'trigger ' + id + ', which is gone')).join(' and ') + '. The saved file leaves that out.', toTriggers, { kind: 'unlist', zi });
    if (!does) add('warning', name(z) + ' does nothing: no enemy, door, message or other action belongs to it.', toTriggers);
    if (z.when === 'intel' && z.item && z.item !== 'all' && kinds.length && !kinds.includes(z.item)) add('warning', name(z) + ' waits for "' + z.item + '", which is not among this map\'s intel (' + kinds.join(', ') + ').', toTriggers);
    if (z.end && z.when === 'time' && !(z.delay > 0)) add('problem', name(z) + ' ends the mission the moment it starts. Give it a wait.', toTriggers);
    // A painted area (ML-027): what the strict reader would refuse, and an anchor left outside the shape.
    if (z.when === 'zone' && z.area && z.area.parts && z.area.parts.length) {
      const toArea = () => { toTriggers(); areaEditStart({ kind: 'trigger', index: zi }, z.area.parts[0].floor.slice(0, 2)); };
      for (const f of areaFindings(z.area)) add('problem', name(z) + ', painted area, ' + f.where.toLowerCase() + ': ' + f.text + '. The game refuses the file until that is put right.', toArea);
      if (isVec(z.pos) && !areaContains(z.area, z.pos)) add('warning', name(z) + ' has its anchor outside its painted area, so its label and the spot enemies rally to are outside too. Press "Move the anchor inside" while painting it.', toArea);
    }
  });
  if (state.power || state.zones.some(z => z.power)) add('note', 'Lights only change on maps with a breaker box. Clean House has one; other maps are not checked yet.', toMission);
  if (state.extractionAfter) {
    const z = byId(state.extractionAfter);
    if (!z) add('warning', 'The extraction waits for a trigger that is gone, so only the mission\'s own rule applies.', toMission, { kind: 'extraction' });
    else if (z.when === 'after' && !z.after) add('warning', 'The extraction opens only when the Game Master fires ' + lower(z) + '.', toMission);
    else if (!canFire(z, [])) add('problem', 'The extraction can never open: it waits for ' + lower(z) + ', which can never fire.', toMission);
  }
  if (state.insertion && state.insertions.length && !state.insertions.includes(state.insertion)) add('problem', 'The insertion "' + state.insertion + '" is not one of this map\'s (' + state.insertions.join(', ') + ').', toMission);
  state.enemies.forEach((e, i) => {
    const w = warningFor(e); if (w) add('warning', enemy(i) + ' is ' + w + '.', toEnemy(i), state.map && !e.fixed && !e.vehicle && !onGround(e.pos) ? { kind: 'snap', what: 'enemy', i } : null);
    if (state.map && e.role === 'Patrol' && !e.fixed && !e.vehicle) e.patrol.forEach((p, n) => { if (!onGround(p)) add('warning', 'Route stop ' + (n + 1) + ' of enemy ' + (i + 1) + ' is not on walkable ground, so the enemy may never get there.', toEnemy(i), { kind: 'snap', what: 'stop', i, n }); });
    if (e.trigger && !byId(e.trigger)) add('warning', 'Enemy ' + (i + 1) + ' waits for a trigger that is gone, so it is there from the start.', toEnemy(i), { kind: 'unlink', what: 'enemy', i });
    if (e.hvt && (e.chance < 100 || e.either)) add('warning', 'Enemy ' + (i + 1) + ' is the target person but is not there every time; a mission without its target cannot be finished.', toEnemy(i));
  });
  state.things.forEach((t, n) => {
    if (t.trigger && !byId(t.trigger)) add('warning', 'The ' + t.name.trim() + ' at ' + t.pos[0].toFixed(0) + ', ' + t.pos[2].toFixed(0) + placeWords(t.pos) + ' waits for a trigger that is gone, so it lies there from the start.', () => selectThing(n), { kind: 'unlink', what: 'thing', i: n });
    if (state.map && !onGround(t.pos)) add('note', 'The ' + t.name.trim() + ' at ' + t.pos[0].toFixed(0) + ', ' + t.pos[2].toFixed(0) + ' is more than 1 m from the walkable ground the enemies use: check that players can reach it.', () => selectThing(n), { kind: 'snap', what: 'thing', i: n });
  });
  if (state.things.some(t => !THING_GROUPS[0][1].includes(t.name))) add('note', 'Only weapons have been seen to work as placed things. Other things may do nothing or be hard to see.', () => showTab('doors', true));
  if (state.enemies.some(e => e.team === 1)) add('note', 'Enemies on the players\' side have not been tried in the game yet.', () => showTab('enemies', true));
  if (state.zones.some(z => z.end === 'failed')) add('note', 'A mission ended as failed shows "Mission failed." on every screen and takes everyone back to the lobby five seconds later. The game has no result screen for it.', toTriggers);
  for (let i = 0; i < state.enemies.length; i++) for (let j = i + 1; j < state.enemies.length; j++) {
    const a = state.enemies[i], b = state.enemies[j];
    if (a.either && b.either === a.either && a.option !== b.option) continue;            // never there together
    // Closer than 0.6 m they stand inside each other.
    if (Math.hypot(a.pos[0] - b.pos[0], a.pos[2] - b.pos[2]) < 0.6 && Math.abs(a.pos[1] - b.pos[1]) < 1.5) add('warning', 'Enemies ' + (i + 1) + ' and ' + (j + 1) + ' stand on the same spot' + placeWords(b.pos) + '.', toEnemy(j), { kind: 'apart', i, j });
  }
  // Enemies that a zone brings should not pop up in front of the players who set it off. For a painted area that means
  // anywhere in it, on its floors: it is looked at from spots on its edges and inside, which is a sample, not every point.
  for (const z of state.zones) {
    if (z.when !== 'zone' || !isVec(z.pos)) continue;
    const shaped = !!(z.area && z.area.parts && z.area.parts.length);
    let spots = null;
    state.enemies.forEach((e, i) => {
      if (e.trigger !== z.id || e.vehicle) return;
      if (shaped) {
        if (membershipInside(z, e.pos)) { add('warning', enemy(i) + ' appears inside the painted area of ' + lower(z) + ', where the players are when it fires.', toEnemy(i)); return; }
        if (!state.geo) return;
        if (!spots) spots = areaSamples(z.area, 48);
        const seen = spots.map(s => ({ s, d: Math.hypot(s[0] - e.pos[0], s[2] - e.pos[2]) })).filter(q => q.d < 60).sort((a, b) => a.d - b.d)
          .find(q => canSee([q.s[0], q.s[1] + 1.6, q.s[2]], [e.pos[0], e.pos[1] + 1.6, e.pos[2]]));
        if (seen) add('warning', 'Enemy ' + (i + 1) + ' appears in plain view of ' + lower(z) + ': it can be seen from ' + seen.s[0].toFixed(0) + ', ' + seen.s[2].toFixed(0) + ' in the painted area, ' + Math.round(seen.d) + ' m away. This is a sampled check: ' + spots.length + ' spots on the area\'s edges and inside, on its floors, were tried, not every point of it.', toEnemy(i));
        return;
      }
      const d = Math.hypot(e.pos[0] - z.pos[0], e.pos[2] - z.pos[2]), sameFloor = Math.abs(e.pos[1] - z.pos[1]) < 2;
      if (d < z.radius && sameFloor) add('warning', enemy(i) + ' appears inside ' + lower(z) + ', where the players are when it fires.', toEnemy(i));
      else if (state.geo && d < 60 && canSee([z.pos[0], z.pos[1] + 1.6, z.pos[2]], [e.pos[0], e.pos[1] + 1.6, e.pos[2]])) add('warning', 'Enemy ' + (i + 1) + ' appears in plain view of ' + lower(z) + ', ' + Math.round(d) + ' m from its middle.', toEnemy(i));
    });
  }
  if (state.enemies.some(e => e.skill || e.nvg)) add('note', 'Skill and night vision have not been tried in the game yet.', () => showTab('enemies', true));
  if (state.zones.some(z => z.chance < 100)) add('note', 'A trigger that is left to chance takes its enemies, things and actions with it when it sits a mission out.', toTriggers);
  for (const [k, o] of eitherSets()) if (o.size < 2) add('warning', 'Either/or set ' + k + ' has only one option, so it is there every time.', toEnemy(state.enemies.findIndex(e => e.either === k)));
  if (state.doors.some(d => d.known)) for (const d of state.doors) if (!d.known && doorSet(d)) add('warning', 'A door setting' + (d.name ? ' ("' + d.name + '")' : '') + ' fits no door of this map.', () => showTab('doors', true));
  const rank = { problem: 0, warning: 1, note: 2 };
  return out.sort((a, b) => rank[a.level] - rank[b.level]);
}
function syncCheck() {
  const list = $('checkList'); if (!list) return;
  const found = missionCheck(), hard = found.filter(f => f.level !== 'note').length;
  $('tabMission').textContent = hard || ''; $('checkCount').textContent = hard ? String(hard) : '';
  const empty = !state.enemies.length && !state.zones.length;
  $('checkSum').textContent = empty ? 'Nothing to check yet. Place an enemy or open a scenario.'
    : !found.length ? 'Nothing found. The check looks at triggers, the extraction, the insertion, enemies and doors.'
    : (hard ? hard + (hard === 1 ? ' thing' : ' things') + ' to look at' : 'Nothing that needs fixing') + (found.length > hard ? ', ' + (found.length - hard) + (found.length - hard === 1 ? ' note' : ' notes') : '') + '. Click one to go there.';
  list.textContent = '';
  for (const f of found) {
    const li = document.createElement('li'), b = document.createElement('button'), lv = document.createElement('span'), tx = document.createElement('span');
    b.type = 'button'; lv.className = 'lv ' + f.level; lv.textContent = f.level === 'problem' ? 'Problem' : f.level === 'warning' ? 'Check' : 'Note'; tx.textContent = f.text;
    b.append(lv, tx); if (f.go) b.addEventListener('click', f.go); else b.disabled = true;
    li.appendChild(b);
    if (f.repair) {                                  // ML-017: shows the repair on the map first; nothing changes before Apply
      const fx = document.createElement('button'); fx.type = 'button'; fx.className = 'btn fix'; fx.textContent = 'Preview repair';
      fx.setAttribute('aria-label', 'Preview a repair for: ' + f.text); fx.addEventListener('click', () => previewRepair(f));
      li.appendChild(fx);
    }
    list.appendChild(li);
  }
}

