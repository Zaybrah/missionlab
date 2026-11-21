// ---------------------------------------------------------------- mission check: what would go wrong, before a mission is spent on it
// An outcome trigger (ML-047) that waits for a result its trigger's action can never report, as TriggerPlan.NeverSeen
// judges it from what the Director observes: only enemies it makes are seen (not targets or vehicles), and any part that
// cannot be seen makes the whole firing "unconfirmed". So "applied" never comes from a trigger that sets doors, calls a
// rally, brings players back or ends the mission, nor from one that shows a message or sends its enemies hunting, unless
// it repeats with enemies and nothing beyond them (a repeat that brings nobody back leaves the message out). "partial"
// needs at least two enemies, one made and one not. Completion, the check and the inspector judge such a trigger as never firing.
function outcomeNeverSeen(z) {
  const y = z && z.when === 'outcome' ? state.zones.find(q => q.id === z.after) : null;
  if (!y || (z.outcome !== 'applied' && z.outcome !== 'partial')) return false;
  const made = state.enemies.filter(e => e.trigger === y.id && !e.hvt && !e.vehicle).length;
  if (z.outcome === 'partial') return made < 2;
  const doors = (y.doors || []).some(d => d.locked != null || d.open != null), ends = y.end === 'success' || y.end === 'failed';
  const beyond = doors || y.rally > 0 || !!y.respawn || y.power === 'on' || y.power === 'off' || ends || !!y.opensExtraction;
  return doors || y.rally > 0 || !!y.respawn || ends || ((!!cleanMessage(y.message) || (!!y.hunt && made > 0)) && !(made > 0 && y.repeat > 0 && !beyond));
}
function missionCheck() {
  // A finding that a repair may fix carries an offer for it (see 34a-repairs.js): { kind, ... }. One about particular
  // triggers or the extraction names them in about ({ triggers: [ids], why } or { extraction: true, why }), so the mission
  // graph (29i-graph.js) marks exactly those without reading the sentence. why: circle, never, missing, self, unsure, byhand, blind.
  const out = [], add = (level, text, go, repair, about) => { const f = repair ? { level, text, go, repair } : { level, text, go }; if (about) f.about = about; out.push(f); };
  // A zone, an enemy or a thing is called by the named place it stands in, when there is one (29b-places.js).
  const at = z => (z.when === 'zone' ? placeWords(z.pos) : '');
  const byId = id => state.zones.find(z => z.id === id), name = z => trigTitle(z) + (z.name ? ' (' + z.name + ')' : '') + at(z), lower = z => trigTitle(z).toLowerCase() + (z.name ? ' (' + z.name + ')' : '') + at(z);
  const enemy = i => 'Enemy ' + (i + 1) + placeWords(state.enemies[i].pos);
  const toTriggers = () => showTab('triggers', true), toMission = () => showTab('mission', true);
  const toEnemy = i => () => { select(i); showTab('enemies', true); const e = state.enemies[i]; if (e) { state.view.cx = e.pos[0]; state.view.cz = e.pos[2]; invalidate(); } };
  // Triggers that wait for each other in a circle, found the way the plugin finds them (TriggerEngine.Rings): the strongly
  // connected groups over after/all/any, the same for every order of the list. One in a circle never fires, even when its
  // "any" list has another way in. A trigger listing itself is left out, as the saved file leaves it out.
  const rings = checkRings(state.zones), inRing = new Map();
  for (const ring of rings) for (const id of ring) inRing.set(id, ring);
  const blind = outcomeNeverSeen;
  // Can a trigger ever fire without the Game Master? One that waits for another needs that one to fire, and so on.
  const memo = new Map();
  const canFire = (z, trail) => {
    if (memo.has(z.id)) return memo.get(z.id);
    let ok = true;
    if (inRing.has(z.id)) ok = false;
    // A zone or hold with a gate (ML-055) needs its place and, like an after trigger, the gate to fire.
    else if (z.when === 'zone' || z.when === 'door') { ok = isVec(z.pos); if (ok && z.when === 'zone' && z.after) { const p = byId(z.after); ok = !!p && p.id !== z.id && !trail.includes(p.id) && canFire(p, trail.concat(z.id)); } }
    else if (z.when === 'after' || z.when === 'outcome') { const p = byId(z.after); ok = !!p && p.id !== z.id && !trail.includes(p.id) && !blind(z) && canFire(p, trail.concat(z.id)); }
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
    if (inRing.has(z.id)) return inRing.get(z.id).length === 1 ? name(z) + ' waits for itself' : name(z) + ' waits for itself through other triggers (' + inRing.get(z.id).map(id => name(byId(id))).join(', ') + ' wait for each other in a circle and never fire)';
    const on = trail.concat(z.id);
    if ((z.chance || 100) < 100) return name(z) + ' has a chance of ' + z.chance + '%';
    if (z.when === 'after') {
      if (!z.after) return state.gameMaster ? '' : name(z) + ' is fired by hand';
      const p = byId(z.after); if (!p) return name(z) + ' waits for a trigger that is gone';
      const w = sure(p, on); return w ? name(z) + ' waits for it: ' + w : '';
    }
    if (z.when === 'zone' && z.after) {
      const p = byId(z.after); if (!p) return name(z) + ' waits for a trigger that is gone';
      const w = sure(p, on); if (w) return name(z) + ' waits for it: ' + w;
    }
    if (z.when === 'outcome') {
      const p = byId(z.after); if (!p) return name(z) + ' watches a trigger that is gone';
      if (blind(z)) return name(z) + ' waits for ' + lower(p) + ' to be seen to take, but nothing it does can be seen';
      const w = sure(p, on); return w ? name(z) + ' watches it: ' + w : '';
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
      add('problem', 'On some runs nobody could leave: the extraction waits for ' + name(gate) + ', and ' + w + '. The game refuses the scenario on such a run and plays its own mission.', toTriggers, null, { extraction: true, why: 'unsure' });
  }
  const targets = state.enemies.filter(e => e.hvt && !e.trigger);
  if (targets.length && targets.every(e => (e.chance || 100) < 100 || e.either > 0 || e.minPlayers > 1))
    add('problem', 'The target person can be left out on some runs (chance, either/or or players needed on every target). The game refuses the scenario on such a run.', () => showTab('enemies', true));
  // Triggers that wait for each other in a circle never fire.
  for (const ring of rings) add('problem', ring.length === 1 ? name(byId(ring[0])) + ' waits for itself: it can never fire.' : ring.map(id => name(byId(id))).join(', ') + ' wait for each other in a circle: none of them can ever fire.', toTriggers, null, { triggers: ring.slice(), why: 'circle' });
  if (state.refused) for (const f of state.refused.errors.slice(0, 12)) add('problem', state.refused.name + ' was not loaded. ' + (f.path ? f.path + ': ' : '') + f.text + '.', toMission);
  for (const b of moduleOpen()) if (b.required) add('problem', b.label + ' (copy ' + b.copy + ' of the module "' + b.module + '") is not bound to anything on this map yet. The scenario cannot be saved until it is.', toTriggers);
  for (const n of state.readNotes || []) add('note', n, toMission);
  if (state.respawnAfter > 0) add('note', 'Reinforcement wave: everyone who is dead comes back together, ' + Math.round(state.respawnAfter) + ' s after the first death. If the whole squad is dead at once the game may end the mission before the wave comes: that has not been tried.', toMission);
  const kinds = [...new Set(state.objectives.map(o => o.label || o.name).filter(Boolean))];
  // Two triggers with one number: the game refuses the file, and whatever waits for that number could mean either of them.
  { const count = new Map(); for (const z of state.zones) count.set(z.id, (count.get(z.id) || 0) + 1);
    for (const [id, k] of count) if (k > 1) add('problem', k + ' triggers have number ' + id + ' (' + state.zones.filter(z => z.id === id).map(name).join(', ') + '). The game refuses a file that uses a trigger number twice.', toTriggers, { kind: 'renumber', id }); }
  // Whether the extraction can never open (said below), and every trigger its gate waits for, through after, outcome,
  // all and any: a blind outcome trigger among them is why nobody could leave, so it is a problem rather than a note.
  const gate = state.extractionAfter ? byId(state.extractionAfter) : null;
  const exitNever = !!gate && !(gate.when === 'after' && !gate.after) && !canFire(gate, []) && !state.zones.some(q => q.opensExtraction && canFire(q, []));
  const exitNeeds = new Set();
  if (exitNever) {
    const walk = q => { if (!q || exitNeeds.has(q.id)) return; exitNeeds.add(q.id);
      for (const id of (q.when === 'after' || q.when === 'outcome' || q.when === 'zone') && q.after ? [q.after] : q.when === 'all' || q.when === 'any' ? q.of || [] : []) walk(byId(id)); };
    walk(gate);
  }
  state.zones.forEach((z, zi) => {
    const n = state.enemies.filter(e => e.trigger === z.id).length;
    const does = n || state.things.some(t => t.trigger === z.id) || (z.doors || []).some(d => d.locked != null || d.open != null) || z.opensExtraction || z.power || z.message || z.end || z.rally > 0 || z.respawn || state.extractionAfter === z.id || state.zones.some(q => ((q.when === 'after' || q.when === 'outcome' || q.when === 'zone') && q.after === z.id) || ((q.when === 'all' || q.when === 'any') && (q.of || []).includes(z.id)));
    const byHand = z.when === 'after' && !z.after;
    if (z.when === 'door' && !isVec(z.pos)) add('problem', name(z) + ' waits for a door, but no door is chosen.', toTriggers);
    if (z.when === 'down' && z.tag && !state.enemies.some(e => e.tag === z.tag)) add('problem', name(z) + ' waits for enemies tagged "' + z.tag + '", but no enemy has that tag.', toTriggers);
    if (z.repeat > 0 && z.end) add('note', name(z) + ' repeats, but it also ends the mission: it will only fire once.', toTriggers);
    if (byHand) { if (does) add('note', name(z) + ' fires only when the Game Master presses Fire now.', toTriggers); }
    else if (blind(z) && !inRing.has(z.id) && canFire(byId(z.after), [])) {
      // Blocking only when the extraction can never open because of it; otherwise the branch just never runs.
      const held = exitNever && exitNeeds.has(z.id), w = byId(z.after);
      add(held ? 'problem' : 'note', name(z) + (z.outcome === 'partial'
        ? ' waits for ' + lower(w) + ' to partly take, but it brings fewer than two enemies, so it is never reported as partly applied'
        : ' waits for ' + lower(w) + ' to be seen to take, but nothing it does can be seen in full, so it is never reported as applied') + ': ' + lower(z) + ' never fires.'
        + (held ? ' The extraction waits for it, so nobody could leave.' : ''), toTriggers, null, Object.assign({ triggers: [z.id], why: 'blind' }, held ? { extraction: true } : {}));
    }
    else if (!canFire(z, [])) {
      const gone = (z.when === 'after' || z.when === 'outcome' || z.when === 'zone') && z.after && !byId(z.after);
      const gateWhy = () => { const p = byId(z.after); return !p ? 'the trigger it waits for is gone' : inRing.has(z.id) ? 'it waits for a trigger that in turn waits for it' : 'it waits for ' + lower(p) + ', which can never fire'; };
      add('problem', name(z) + ' can never fire: ' + (z.when === 'zone' ? (!isVec(z.pos) ? 'it has no place on the map' : gateWhy()) : z.when === 'all' || z.when === 'any' ? ((z.of || []).length ? 'the triggers it waits for can never fire, or wait for it' : 'no triggers are ticked for it') : z.when === 'outcome' && !z.after ? 'it watches no trigger' : byId(z.after) ? 'it waits for a trigger that in turn waits for it' : 'the trigger it waits for is gone') + '.', toTriggers, gone ? { kind: 'after', zi } : null, { triggers: [z.id], why: gone ? 'missing' : 'never' });
    }
    if ((z.when === 'all' || z.when === 'any') && (z.of || []).some(id => id === z.id || !byId(id))) add('warning', name(z) + ' lists ' + (z.of || []).filter(id => id === z.id || !byId(id)).map(id => (id === z.id ? 'itself' : 'trigger ' + id + ', which is gone')).join(' and ') + '. The saved file leaves that out.', toTriggers, { kind: 'unlist', zi }, { triggers: [z.id], why: (z.of || []).some(id => id !== z.id && !byId(id)) ? 'missing' : 'self' });
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
    if (!z) add('warning', 'The extraction waits for a trigger that is gone, so only the mission\'s own rule applies.', toMission, { kind: 'extraction' }, { extraction: true, why: 'missing' });
    else if (z.when === 'after' && !z.after) add('warning', 'The extraction opens only when the Game Master fires ' + lower(z) + '.', toMission, null, { extraction: true, why: 'byhand' });
    else if (!canFire(z, []) && !state.zones.some(q => q.opensExtraction && canFire(q, []))) add('problem', 'The extraction can never open: it waits for ' + lower(z) + ', which can never fire. No other trigger opens the extraction.', toMission, null, { extraction: true, why: 'never' });
  }
  // Objectives (ML-046), judged as the plugin's Completion judges them: a circle among "after" lists, and a required
  // objective that can never succeed, hold the extraction for ever when it waits for objectives and no trigger that can
  // fire opens it anyway; otherwise they are notes. An optional objective never holds anything, so it is not judged.
  if (state.goals.length || state.extractionGoals) {
    const gates = state.extractionGoals && !state.zones.some(z => z.opensExtraction && canFire(z, []));
    const level = gates ? 'problem' : 'note', goal = id => state.goals.find(g => g.id === id);
    const gname = g => 'Objective "' + g.name + '"', held = gates ? ' The extraction waits for every required objective, so nobody could leave.' : '';
    const ROLE = [['activate', 'becomes active after'], ['succeed', 'is completed by'], ['fail', 'fails by']];
    for (const g of state.goals) for (const [key, words] of ROLE) if ((key === 'succeed' || g[key] > 0) && !byId(g[key]))
      add('problem', gname(g) + ' ' + words + ' ' + (g[key] > 0 ? 'trigger ' + g[key] + ', which is gone' : 'no trigger') + '. The game refuses the file until another trigger is chosen.', toMission, null, { goals: [g.id], why: 'missing' });
    const rings = goalRings(state.goals), ringed = new Set([].concat(...rings));
    for (const ring of rings) add(level, (ring.length === 1 ? gname(goal(ring[0])) + ' waits for itself' : ring.map(id => gname(goal(id))).join(', ') + ' wait for each other in a circle') + ': ' + (ring.length === 1 ? 'it' : 'none of them') + ' can ever become active.' + held,
      toMission, null, Object.assign({ goals: ring.slice(), why: 'circle' }, gates ? { extraction: true } : {}));
    // Why an objective can never succeed, with the trigger at fault (0 when it is another objective): '' when it can. The
    // first reason wins in Completion's order: an objective it waits for, then the trigger that makes it active, then the
    // one that completes it. A trigger fired by hand counts only when the mission says it needs a Game Master.
    const byHand = z => z.when === 'after' && !z.after, fires = z => (byHand(z) ? state.gameMaster : canFire(z, []));
    const cannot = z => (byHand(z) ? 'is fired only by hand, and the mission does not say it needs a Game Master' : 'can never fire');
    const gmemo = new Map();
    const never = (g, trail) => {
      if (gmemo.has(g.id)) return gmemo.get(g.id);
      let w = ['', 0];
      const s = byId(g.succeed), a = g.activate > 0 ? byId(g.activate) : null;
      if (ringed.has(g.id) || trail.includes(g.id)) w = ['it waits for itself through other objectives', 0];
      else {
        for (const id of g.after) {
          const p = goal(id);
          if (!p) { w = ['it waits for objective "' + id + '", which is gone', 0]; break; }
          if (never(p, trail.concat(g.id))[0]) { w = ['it waits for objective "' + p.name + '", which can never be completed', 0]; break; }
        }
        if (!w[0]) {
          if (g.activate > 0 && !a) w = ['the trigger that makes it active is gone', 0];
          else if (a && !fires(a)) w = [lower(a) + ', which makes it active, ' + cannot(a), a.id];
          else if (!s) w = ['the trigger that completes it is gone', 0];
          else if (!fires(s)) w = [lower(s) + ', which completes it, ' + cannot(s), s.id];
        }
      }
      if (!trail.length) gmemo.set(g.id, w);
      return w;
    };
    for (const g of state.goals) {
      if (!g.required || ringed.has(g.id) || !byId(g.succeed) || (g.activate > 0 && !byId(g.activate))) continue;     // gone triggers are said above
      const [w, z] = never(g, []);
      if (!w) continue;
      add(level, gname(g) + ' can never be completed: ' + w + '.' + held, toMission, null,
        Object.assign({ goals: [g.id], why: 'never' }, z ? { triggers: [z] } : {}, gates ? { extraction: true } : {}));
    }
    const needed = state.goals.filter(g => g.required);
    if (gates) add('note', needed.length ? 'The extraction stays shut until every required objective has succeeded: ' + needed.map(g => '"' + g.name + '"').join(', ') + '.'
      : 'The extraction waits for every required objective, but there is none, so objectives do not hold it.', toMission);
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


// The circles among triggers, as TriggerEngine.Rings finds them: each group of triggers that wait for each other (one that
// waits for itself through "after" is a group of one), sorted by number, in an order that does not depend on the list.
// A second trigger with the same number is ignored, as the plugin ignores it.
function checkRings(zones) {
  const byId = new Map();
  for (const z of zones) if (!byId.has(z.id)) byId.set(z.id, z);
  // A zone or hold's gate (ML-055) is such a link too; one naming itself is dropped on reading, so it is no circle.
  const needs = z => ((z.when === 'after' || z.when === 'outcome') && z.after ? [z.after] : z.when === 'zone' && z.after && z.after !== z.id ? [z.after] : z.when === 'all' || z.when === 'any' ? (z.of || []).filter(id => id !== z.id) : [])
    .filter(id => byId.has(id)).sort((a, b) => a - b);
  const index = new Map(), low = new Map(), on = new Set(), stack = [], rings = [];
  let next = 0;
  const visit = id => {
    index.set(id, next); low.set(id, next); next++; stack.push(id); on.add(id);
    let self = false;
    for (const n of needs(byId.get(id))) {
      if (n === id) self = true;
      if (!index.has(n)) { visit(n); low.set(id, Math.min(low.get(id), low.get(n))); }
      else if (on.has(n)) low.set(id, Math.min(low.get(id), index.get(n)));
    }
    if (low.get(id) !== index.get(id)) return;
    const ring = [];
    let p;
    do { p = stack.pop(); on.delete(p); ring.push(p); } while (p !== id);
    if (ring.length > 1 || self) rings.push(ring.sort((a, b) => a - b));
  };
  for (const id of [...byId.keys()].sort((a, b) => a - b)) if (!index.has(id)) visit(id);
  return rings.sort((a, b) => a[0] - b[0]);
}
// The circles among objectives' "after" lists (ML-046), found the same way: each group of goals that wait for each other,
// a goal naming itself being a group of one. Goals in each group, and the groups, come in the order of the goal list.
function goalRings(goals) {
  const pos = new Map();
  goals.forEach((g, i) => { if (!pos.has(g.id)) pos.set(g.id, i); });
  const byPos = (a, b) => pos.get(a) - pos.get(b);
  const index = new Map(), low = new Map(), on = new Set(), stack = [], rings = [];
  let next = 0;
  const visit = id => {
    index.set(id, next); low.set(id, next); next++; stack.push(id); on.add(id);
    let self = false;
    for (const n of goals[pos.get(id)].after || []) {
      if (!pos.has(n)) continue;
      if (n === id) self = true;
      if (!index.has(n)) { visit(n); low.set(id, Math.min(low.get(id), low.get(n))); }
      else if (on.has(n)) low.set(id, Math.min(low.get(id), index.get(n)));
    }
    if (low.get(id) !== index.get(id)) return;
    const ring = [];
    let p;
    do { p = stack.pop(); on.delete(p); ring.push(p); } while (p !== id);
    if (ring.length > 1 || self) rings.push(ring.sort(byPos));
  };
  for (const id of pos.keys()) if (!index.has(id)) visit(id);
  return rings.sort((a, b) => byPos(a[0], b[0]));
}
expose({ checkRings, goalRings });
