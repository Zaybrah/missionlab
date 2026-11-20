// ------------------------------------------------------------------ a guided recipe: Intel raid (ML-044)
// A first-time host makes a whole mission on the Map tab by answering five questions: where the squad goes in, which
// intel it takes, which place counts as the building, which kept encounters stand in which rooms, and how hard it is.
// "Make the mission" writes ordinary scenario data only, as one Undo step: named triggers, enemies, host options, a
// briefing, the extraction held until the intel is taken, an optional time limit that fails the mission, and the author's
// estimates. Nothing made here needs the recipe again: every part is changed where it would be changed by hand.
// A walkthrough test (in from the insertion, into the building, the intel taken, back out) goes into Rehearse in the
// rehearsal format, to run or save there.
// What the map does not tell (insertions, intel, a building, ground to stand on) and what the game has not been seen to
// do (enemies sent to hunt or rally: failing in the capability list) is said before anything is made; then nothing is made.
const RECIPE_NEVER = ['hunt', 'rally'];          // fresh enemies sent somewhere stood still in the game; patrol routes were walked
const RECIPE_GRID = 1.5, RECIPE_MARGIN = 0.6, RECIPE_SPACING = 1.5;   // metres: spots looked at, room round a spot, room between enemies
const RECIPE_GARRISON = ['light', 'standard', 'heavy'];               // 2 enemies always, 2 more (lot), 2 more (another lot)
const RECIPE_TEST = 'Intel raid: in, intel, out';
state.recipe = state.recipe || { encounters: [], key: '', made: null, note: '' };

// ----- what the map offers

// The map's insertions by name, with where each one is when a recon file or the game said so (else pos is null).
function recipeInsertions() {
  const names = [];
  for (const n of state.insertions.concat(state.insertionSpots.map(q => q.name))) if (typeof n === 'string' && n && !names.includes(n)) names.push(n);
  return names.map(name => { const q = state.insertionSpots.find(s => s.name === name && isVec(s.pos)); return { name, pos: q ? q.pos.slice(0, 3) : null }; });
}
// The intel kinds this map is known to have: the mission's own items (from the game or a scenario), then a recon file of
// this map, whose lists are complete. item is what the trigger waits for, as the game names the item where it is known.
function recipeKinds() {
  const out = [], seen = new Set(), add = item => { const kind = capKind(item); if (!kind || seen.has(kind)) return; seen.add(kind); out.push({ kind, item }); };
  for (const o of state.objectives) add(o.label || o.name);
  const facts = capFitting(state.reconFacts || null, { navFile: state.navFile, scene: state.scene }), group = THING_GROUPS.find(g => /^Intel/.test(g[0]));
  if (facts && Array.isArray(facts.items)) for (const k of facts.items) add((group && group[1].find(n => capKind(n) === k)) || k);
  return out;
}
// Spots on walkable ground inside a place, every 1.5 m, with room round each (0.6 m of the place and of that floor on every
// side), so nobody is put against a wall or in a doorway. In a fixed order, so the same map gives the same mission.
function recipeSpots(place) {
  if (!state.map || !place || !isVec(place.pos)) return [];
  const inside = p => placeContains(place, p), boxes = [], out = [], m = RECIPE_MARGIN, g = RECIPE_GRID;
  if (place.area && place.area.parts && place.area.parts.length) {
    for (const part of place.area.parts) {
      const b = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity };
      for (const c of part.outline) { b.minX = Math.min(b.minX, c[0]); b.maxX = Math.max(b.maxX, c[0]); b.minZ = Math.min(b.minZ, c[1]); b.maxZ = Math.max(b.maxZ, c[1]); }
      boxes.push(b);
    }
  } else boxes.push({ minX: place.pos[0] - PLACE_REACH, maxX: place.pos[0] + PLACE_REACH, minZ: place.pos[2] - PLACE_REACH, maxZ: place.pos[2] + PLACE_REACH });
  const roomy = (x, y, z) => [[m, 0], [-m, 0], [0, m], [0, -m]].every(([dx, dz]) => inside([x + dx, y, z + dz]) && levelsAt(x + dx, z + dz).some(v => Math.abs(v - y) <= 0.5));
  for (const b of boxes) {
    for (let i = Math.ceil(b.minX / g); i * g <= b.maxX; i++) {
      for (let k = Math.ceil(b.minZ / g); k * g <= b.maxZ; k++) {
        const x = r2(i * g), z = r2(k * g);
        for (const level of levelsAt(x, z)) {
          const p = [x, r2(level), z];
          if (!inside(p) || out.some(q => q[0] === p[0] && q[2] === p[2] && Math.abs(q[1] - p[1]) < 0.5) || !roomy(x, p[1], z)) continue;
          out.push(p);
        }
      }
    }
  }
  return out;
}
// count spots spread out: each the one farthest from those taken, the enemies already there and the insertion, and never
// nearer than 1.5 m to an enemy. Fewer when the place has no more room.
function recipeSpread(spots, count, enemies, from) {
  const taken = [], d = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
  while (taken.length < count) {
    let best = null, score = -1;
    for (const s of spots) {
      if (taken.includes(s)) continue;
      const close = taken.concat(enemies).reduce((v, q) => Math.min(v, d(s, q)), Infinity);
      if (close < RECIPE_SPACING) continue;
      const v = Math.min(close, from ? d(s, from) : Infinity);
      if (v > score) { score = v; best = s; }
    }
    if (!best) break;
    taken.push(best);
  }
  return taken;
}
// Where reinforcements can come from: the game's own spawn points of this map (on its ground by definition), outside the
// building, at least 25 m from it and from the insertion; the two nearest the building.
function recipeReinforcements(place, from) {
  const far = (a, b) => Math.hypot(a[0] - b[0], a[2] - b[2]) >= 25;
  return state.authored.filter(p => isVec(p) && onGround(p) && !placeContains(place, p) && far(p, place.pos) && (!from || far(p, from)))
    .map(p => ({ p: p.slice(0, 3), d: Math.hypot(p[0] - place.pos[0], p[2] - place.pos[2]) })).sort((a, b) => a.d - b.d).slice(0, 2).map(q => q.p);
}
// The zone the building becomes: its painted area, or for a point place the 3 m circle round its pin.
function recipeZoneShape(place) {
  const shaped = !!(place.area && place.area.parts && place.area.parts.length);
  return { pos: place.pos.slice(0, 3), radius: shaped ? 12 : PLACE_REACH, area: shaped ? areaRead(areaWrite(place.area)) : null };
}
// What a kept encounter cannot do in this recipe, as sentences: a broken file, something failing or unavailable in the
// game (hunt and rally above all), or an outside reference only the author can choose (a door, a place, a trigger).
function recipeModuleTrouble(mod) {
  const wrong = moduleProblems(mod);
  if (wrong.length) return [wrong[0]];
  const out = [];
  for (const word of capabilitiesOf(moduleStandIn(mod))) {
    const c = capJudge(word, '');
    if (RECIPE_NEVER.includes(word)) out.push('it sends enemies to ' + word + ' (' + (c.shown === 'failing' ? c.words : 'enemies sent somewhere just after they appeared stood still in the game') + '); the recipe brings reinforcements on patrol routes instead');
    else if (c.shown === 'failing' || c.shown === 'unavailable') out.push('it uses ' + word + ', which is ' + c.words);
  }
  const hand = mod.bindings.filter(b => moduleRequired(b) && b.kind !== 'objective');
  if (hand.length) out.push('it needs ' + andWords(hand.map(b => (typeof b.label === 'string' && b.label.trim()) || b.kind + ' ' + b.ref)) + ' chosen by hand: insert it under Modules on the Triggers tab once the mission is made');
  else if (live.on && mod.bindings.some(moduleRequired)) out.push('it waits for an intel item, and modules are only bound while editing offline: make the mission with the page opened as a file, then load it');
  return out;
}

// ----- before anything is made
// choice: { insertion, item (null: none chosen, '': any intel), building (a place id), encounters: [{ module, name, room,
// pending }], garrison, skill, clock (minutes, 0: none), alarm, minutes, squad: [fewest, most] }.
// Returns { blockers, notes, uses, ctx }: blockers say why nothing can be made yet; ctx is what Make works from.
function recipeCheck(choice) {
  const blockers = [], notes = [], no = t => blockers.push(t);
  const ctx = { ins: null, item: '', place: null, zone: null, inside: null, spots: [], guards: [], reinforce: [] };
  if (live.on && live.mission) no('A mission is running and the page shows it. Make the mission after it.');
  if (!state.map) no('Open the map file first (Open files, above): the recipe only puts enemies on ground it can see.');
  else if (state.map.isExample) no('This is the example map. Open the map file of the mission you want to make (Open files, above).');
  // 1. Insertion
  const ins = recipeInsertions();
  if (!ins.length) no('The map\'s insertions are not known. Open the map\'s recon file (BepInEx/MissionLab/recon), or open this page from the game during a mission on this map.');
  else {
    ctx.ins = ins.find(q => q.name === choice.insertion) || null;
    if (!ctx.ins) no('Choose where the squad goes in.');
    else if (!ctx.ins.pos) no('Where "' + ctx.ins.name + '" is on the map is not known, so the walkthrough test cannot start there. Open the map\'s recon file, which has the place of every insertion.');
  }
  // 2. Objective
  const kinds = recipeKinds();
  if (!kinds.length) no('No intel is known for this map, so the recipe cannot say what to take. Open the map\'s recon file (it lists every intel item the map can have), or open this page from the game during an intel mission on this map.');
  else if (choice.item === '') ctx.item = '';
  else {
    const k = kinds.find(q => q.item === choice.item);
    if (!k) no('Choose the intel the squad takes.'); else ctx.item = k.item;
  }
  // 3. Building
  const place = state.places.find(p => p.id === choice.building) || null;
  if (!place) no(state.places.length ? 'Choose the place that counts as the building, or paint one (Paint a new place).' : 'This map has no named places yet. Paint the building first: Paint a new place, then paint its shape and give it a name.');
  else {
    ctx.place = place;
    const shaped = !!(place.area && place.area.parts && place.area.parts.length);
    const wrong = shaped ? areaFindings(place.area) : [];
    if (wrong.length) no(place.name + '\'s painted shape would be refused by the game (' + wrong[0].where.toLowerCase() + ': ' + wrong[0].text + '). Put it right under Places first.');
    else {
      ctx.zone = recipeZoneShape(place);
      const inZone = q => placeContains(place, q) && membershipInside(ctx.zone, q);
      ctx.spots = recipeSpots(place);
      // The test squad stands on a spot of the building's ground, the one nearest its pin, where both rules count it in.
      ctx.inside = ctx.spots.filter(inZone).sort((a, b) => Math.hypot(a[0] - place.pos[0], a[2] - place.pos[2]) - Math.hypot(b[0] - place.pos[0], b[2] - place.pos[2]))[0] || null;
      if (state.map && !ctx.inside) no('No spot of walkable ground with room round it is inside ' + place.name + '. Check that its shape and floor heights cover the floors of the building (Places, below).');
      if (!shaped) notes.push(place.name + ' is a point place: the building counts as 3 m round its pin. Painting its shape (Places, below) makes the whole building count.');
      if (ctx.ins && ctx.ins.pos && (placeContains(place, ctx.ins.pos) || membershipInside(ctx.zone, ctx.ins.pos))) no('The insertion "' + ctx.ins.name + '" is inside ' + place.name + ': the squad would start in the building. Choose another insertion or another place.');
      if (state.map) {
        ctx.guards = recipeSpread(ctx.spots, 6, state.enemies.map(e => e.pos), ctx.ins && ctx.ins.pos);
        if (ctx.inside && ctx.guards.length < 2) no(place.name + ' has room for fewer than two guards on its walkable ground. Paint a larger building.');
        else if (ctx.guards.length < 6 && choice.garrison === 'heavy') notes.push(place.name + ' has room for ' + ctx.guards.length + ' guards, so the largest garrison is ' + ctx.guards.length + '.');
      }
    }
  }
  // 4. Encounters
  (choice.encounters || []).forEach((enc, i) => {
    const which = 'Encounter ' + (i + 1);
    if (!enc.module && !enc.pending) return no(which + ': choose a kept module, or remove the row.');
    if (!state.places.some(p => p.id === enc.room)) no(which + ': choose the room it stands in.');
    if (enc.pending) return notes.push(which + ' ("' + enc.name + '") is kept by the game: it is checked when the mission is made.');
    for (const t of recipeModuleTrouble(enc.module)) no(which + ' ("' + (enc.module.name || 'module') + '") cannot go in: ' + t + '.');
  });
  // 5. Pressure and the author's estimates
  if (choice.alarm && ctx.place) {
    ctx.reinforce = recipeReinforcements(ctx.place, ctx.ins && ctx.ins.pos);
    if (!ctx.reinforce.length) no('Reinforcements need the game\'s own spawn points of this map, at least 25 m from the building and the insertion. Open the map\'s recon file, or untick the alarm.');
  }
  if (!(choice.minutes > 0 && choice.minutes <= 600)) no('Give your estimate of the mission\'s length: more than 0 and at most 600 minutes.');
  const sq = choice.squad || [];
  if (!(Number.isInteger(sq[0]) && Number.isInteger(sq[1]) && sq[0] >= 1 && sq[1] <= 16 && sq[0] <= sq[1])) no('Give the squad you have in mind: 1 to 16 players, the fewest first.');
  if (choice.clock > 0 && choice.minutes > choice.clock) notes.push('The time limit (' + choice.clock + ' minutes) is shorter than your estimate of the mission (' + choice.minutes + ' minutes).');
  if (state.enemies.length || state.zones.length) notes.push('The recipe adds to what is on the page (' + plural(state.enemies.length, 'enemy', 'enemies') + ', ' + plural(state.zones.length, 'trigger', 'triggers') + '). Its extraction, insertion, briefing and estimates replace any set before.');
  // What the mission will use, as recorded in the game. Experimental and untested parts are said, never hidden.
  const words = ['messages', 'extraction-hold', 'insertion', 'options', 'places', 'chance'];
  if (ctx.zone && ctx.zone.area) words.push('areas');
  if (choice.clock > 0) words.push('endings');
  for (const enc of choice.encounters || []) if (enc.module && !moduleProblems(enc.module).length) for (const w of capabilitiesOf(moduleStandIn(enc.module))) if (!words.includes(w)) words.push(w);
  const uses = words.sort().map(w => capJudge(w, ''));
  for (const c of uses) if (c.shown === 'experimental' || c.shown === 'untested' || c.shown === 'unknown') notes.push('It uses ' + c.name + ': ' + (c.words || capShort(c)) + '.');
  const skill = capList.records.find(r => r.id === 'skill-nvg');
  notes.push('The host\'s Enemy skill choice is ' + (skill && skill.status === 'supported' ? 'recorded as working' : 'not proven in the game yet') + '; Regulars leaves skill to the game.');
  return { blockers, notes, uses, ctx };
}

// ----- making it
// Everything is made in one transaction, so one Undo takes all of it back. Returns { made, why } or the made parts.
function recipeMake(choice) {
  const check = recipeCheck(choice);
  if (check.blockers.length) { state.recipe.note = 'Nothing was made. ' + check.blockers.join(' '); return { made: false, why: check.blockers }; }
  const x = check.ctx, place = x.place, building = place.name, ins = x.ins, intelWord = x.item ? x.item : 'intel';
  const towards = (from, to) => r2((Math.atan2(to[0] - from[0], to[2] - from[2]) * 180 / Math.PI + 360) % 360);
  const made = { zones: {}, enemies: [], options: [], copies: [] };
  txBegin('Make the Intel raid');
  try {
    // Encounters first, so the garrison keeps clear of their enemies. An intel item they wait for is the mission's.
    for (const enc of choice.encounters || []) {
      const room = state.places.find(p => p.id === enc.room);
      const near = s => Math.hypot(s[0] - room.pos[0], s[2] - room.pos[2]), ground = recipeSpots(room).sort((a, b) => near(a) - near(b))[0];
      const at = ground || rhInside(room.pos, room.area, q => placeContains(room, q)) || room.pos.slice(0, 3);
      const r = moduleInsert(enc.module, at, 0);
      if (r.error) throw new Error(r.error);
      for (const b of moduleOpen().filter(q => q.copy === r.copy && q.kind === 'objective')) moduleBind(b.key, x.item);
      made.copies.push(r.copy);
    }
    let top = state.zones.reduce((m, z) => Math.max(m, z.id), 0);
    const add = (when, more) => { const z = newTrigger(++top, when, more); state.zones.push(z); return z; };
    const enter = add('zone', { name: 'Into ' + building, pos: x.zone.pos, radius: x.zone.radius, area: x.zone.area, message: cleanMessage('You are in ' + building + '. Find the ' + intelWord + '.') });
    const intel = add('intel', { name: (x.item || 'Intel') + ' taken', item: x.item, message: cleanMessage((x.item || 'Intel') + ' secured. The extraction is open: get out.') });
    const alarm = choice.alarm ? add('alert', { name: 'Alarm', delay: 10, red: true, message: cleanMessage('They know you are here. More of them are on their way to ' + building + '.') }) : null;
    const clock = choice.clock > 0 ? add('time', { name: 'Out of time', delay: choice.clock * 60, warn: true, red: true, end: 'failed', message: cleanMessage('Out of time: the mission has failed.') }) : null;
    Object.assign(made.zones, { enter: enter.id, intel: intel.id, alarm: alarm ? alarm.id : 0, clock: clock ? clock.id : 0 });

    // The garrison: two guards always, two more in one lot, two more in another; the host's Garrison option drops lots.
    const guards = recipeSpread(x.spots, 6, state.enemies.map(e => e.pos), ins.pos);
    if (guards.length < 2) throw new Error(building + ' has no room left for the garrison once the encounters are in');
    const group = state.enemies.reduce((m, e) => Math.max(m, (e.group | 0) + 1), 0), lotTop = state.enemies.reduce((m, e) => Math.max(m, e.lot | 0), 0);
    const lotA = guards.length > 2 ? lotTop + 1 : 0, lotB = guards.length > 4 ? lotTop + 2 : 0;
    guards.forEach((p, i) => {
      const e = Object.assign(newEnemy(p, 'Guard', group), { yaw: towards(p, ins.pos), indoor: true, defensive: i % 2 === 0, lot: i < 2 ? 0 : i < 4 ? lotA : lotB });
      state.enemies.push(e); made.enemies.push(state.enemies.length - 1);
    });
    // Reinforcements walk a route: from outside into the building and across it. They never hunt or rally.
    if (alarm) {
      const stops = [x.inside, x.spots.reduce((b, s) => (Math.hypot(s[0] - x.inside[0], s[2] - x.inside[2]) > Math.hypot(b[0] - x.inside[0], b[2] - x.inside[2]) ? s : b), x.inside)];
      for (const p of x.reinforce) {
        const e = Object.assign(newEnemy(p, 'Patrol', group + 1), { yaw: towards(p, place.pos), trigger: alarm.id, patrolWaitSeconds: 2, patrol: stops.map(s => s.slice(0, 3)) });
        state.enemies.push(e); made.enemies.push(state.enemies.length - 1);
      }
    }

    // Host options: the author's choices are the defaults; a choice's name says what it is, never that it is easier.
    const freeId = base => { let id = base, n = 2; while (state.options.some(o => o.id === id)) id = base + '-' + n++; return id; };
    const choiceOf = (name, more) => Object.assign({ name, lots: [], triggers: [], respawnTimes: -1, skill: '' }, more || {});
    const option = (base, name, pick, choices) => { const id = freeId(base); state.options.push({ id, name, pick, choices }); made.options.push(id); };
    if (lotA) {
      const want = Math.max(0, RECIPE_GARRISON.indexOf(choice.garrison));
      if (lotB) option('garrison', 'Garrison', want, [choiceOf('Light', { lots: [lotA, lotB] }), choiceOf('Standard', { lots: [lotB] }), choiceOf('Heavy')]);
      else option('garrison', 'Garrison', want > 0 ? 1 : 0, [choiceOf('Light', { lots: [lotA] }), choiceOf('Heavy')]);
    }
    option('skill', 'Enemy skill', choice.skill === 'recruit' ? 0 : choice.skill === 'veteran' ? 2 : 1, [choiceOf('Recruits', { skill: 'recruit' }), choiceOf('Regulars'), choiceOf('Veterans', { skill: 'veteran' })]);
    if (clock) option('clock', 'Time limit', 0, [choiceOf(choice.clock + ' minutes'), choiceOf('None', { triggers: [clock.id] })]);

    // The way through: in from the insertion, the extraction shut until the intel is taken; the author's estimates.
    state.extractionAfter = intel.id; state.insertion = ins.name;
    const pressure = clock && alarm ? 'You have ' + choice.clock + ' minutes, and an alarm brings more of them.' : clock ? 'You have ' + choice.clock + ' minutes.' : alarm ? 'If they raise the alarm, more of them come.' : '';
    state.briefing = ['Intel raid. Go in from ' + ins.name + ', get into ' + building + ' and take the ' + intelWord + '.', 'The way out stays shut until you have it.', pressure].map(cleanMessage).filter(Boolean);
    state.minutes = choice.minutes; state.squadRange = [choice.squad[0], choice.squad[1]];
    state.selected = -1; state.squad = [];
    changed();
  } catch (err) {
    txCancel();
    state.recipe.note = 'Nothing was made: ' + (err && err.message ? err.message : err) + '.';
    return { made: false, why: [state.recipe.note] };
  }
  txCommit();

  // The walkthrough test, written for the scenario as it is now. The test players jump between the rows' places; the
  // building's check comes a second later, after the engine's half-second look.
  const players = Math.max(1, Math.min(4, choice.squad[0])), at = p => Array.from({ length: players }, () => p.slice(0, 3));
  const walk = (a, b) => Math.max(10, Math.ceil(Math.hypot(b[0] - a[0], b[2] - a[2]) / WALK_SPEED / 5) * 5);
  const tIn = walk(ins.pos, x.inside), tIntel = tIn + 30, tOut = tIntel + 1 + walk(x.inside, ins.pos);
  const rows = [
    { at: 0, kind: 'players', players: at(ins.pos) }, { at: 0, kind: 'notFired', id: made.zones.enter }, { at: 0, kind: 'extraction', value: 'held' },
    { at: tIn, kind: 'players', players: at(x.inside) }, { at: tIn + 1, kind: 'fired', id: made.zones.enter }, { at: tIn + 1, kind: 'extraction', value: 'held' },
    { at: tIntel, kind: 'intel', item: x.item }, { at: tIntel + 1, kind: 'fired', id: made.zones.intel }, { at: tIntel + 1, kind: 'extraction', value: 'open' },
    { at: tOut, kind: 'players', players: at(ins.pos) }, { at: tOut + 1, kind: 'extraction', value: 'open' },
  ];
  if (made.zones.clock && tOut + 1 < choice.clock * 60) rows.push({ at: tOut + 1, kind: 'notFired', id: made.zones.clock });
  const text = scenarioText();
  rhLoad(rehearseText(rehearseScript(rows, { name: RECIPE_TEST, scenario: textHash(text), revision: state.revision, players })), 'the Intel raid recipe');
  made.rows = state.rehearse.rows;
  state.recipe.made = { zones: made.zones };
  state.recipe.note = 'Intel raid made as one step (Undo takes all of it back): ' + plural(2 + (made.zones.alarm ? 1 : 0) + (made.zones.clock ? 1 : 0), 'trigger', 'triggers') + ' named after what they do, '
    + plural(made.enemies.length, 'enemy', 'enemies') + ' of its own' + (made.copies.length ? ' and ' + plural(made.copies.length, 'encounter', 'encounters') : '') + ', host options ' + andWords(made.options.map(id => state.options.find(o => o.id === id).name))
    + ', a briefing and the extraction held until the ' + intelWord + ' is taken. ' + choice.minutes + ' minutes and ' + choice.squad[0] + ' to ' + choice.squad[1] + ' players are your estimates: nobody has measured them. '
    + 'The walkthrough test "' + RECIPE_TEST + '" is in Rehearse (Mission tab): run it from the game, or keep it with Save the walkthrough test.';
  say(state.recipe.note);
  return Object.assign({ made: true }, made);
}

// ----- the panel
// Kept modules by the Modules picker's keys: 'l:' this browser, 'g:' the game's folder, 'f:' the file opened there.
const recipeModuleList = () => [...savedModules().map((m, i) => ['l:' + i, m.name]), ...moduleGame.map(m => ['g:' + m.name, (m.title || m.name) + ' (the game\'s)']), ...(moduleFile ? [['f:', moduleFile.name + ' (file)']] : [])];
async function recipeModuleOf(key) {
  if (key.startsWith('l:')) return savedModules()[+key.slice(2)] || null;
  if (key === 'f:') return moduleFile;
  if (key.startsWith('g:')) {
    try { const r = await fetch('/api/modules/' + encodeURIComponent(key.slice(2)), { cache: 'no-store' }); return r.ok ? await r.json() : null; } catch (e) { return null; }
  }
  return null;
}
// The choices on the panel. A module kept by the game is only fetched when the mission is made (pending until then).
function recipeForm() {
  const v = id => String($(id).value || ''), n = (id, d) => { const f = parseFloat($(id).value); return isFinite(f) ? f : d; }, item = v('rcIntel');
  const list = recipeModuleList(), kept = key => (key.startsWith('l:') ? savedModules()[+key.slice(2)] || null : key === 'f:' ? moduleFile : null);
  return { insertion: v('rcInsertion'), item: item === 'any' ? '' : item.startsWith('k:') ? item.slice(2) : null, building: Math.trunc(n('rcBuilding', 0)),
    encounters: state.recipe.encounters.map(e => { const known = list.find(q => q[0] === e.key), pending = !!known && e.key.startsWith('g:');
      return { key: e.key, room: e.room, name: known ? known[1] : '', pending, module: pending ? null : kept(e.key) }; }),
    garrison: v('rcGarrison') || 'standard', skill: v('rcSkill'), clock: Math.max(0, Math.trunc(n('rcClock', 0))), alarm: !!$('rcAlarm').checked,
    minutes: n('rcMinutes', 0), squad: [Math.trunc(n('rcSquadMin', 0)), Math.trunc(n('rcSquadMax', 0))] };
}
function recipeFill(sel, options) {
  const was = String(sel.value || '');
  sel.textContent = '';
  for (const [value, text] of options) sel.appendChild(optionEl(text, value));
  if (options.some(o => o[0] === was)) sel.value = was;
}
function recipeEncounters() {
  const ul = $('rcEncList'), mods = [['', 'choose a module'], ...recipeModuleList()], rooms = [['0', 'choose a room'], ...state.places.map(p => [String(p.id), p.name])];
  ul.textContent = '';
  state.recipe.encounters.forEach((enc, i) => {
    const li = document.createElement('li'), mod = document.createElement('select'), room = document.createElement('select'), word = document.createElement('span'), del = document.createElement('button');
    mod.setAttribute('aria-label', 'Module of encounter ' + (i + 1)); room.setAttribute('aria-label', 'Room of encounter ' + (i + 1));
    recipeFill(mod, mods); recipeFill(room, rooms); mod.value = enc.key; room.value = String(enc.room || 0);
    mod.addEventListener('change', () => { enc.key = String(mod.value || ''); recipeShow(); });
    room.addEventListener('change', () => { enc.room = Math.trunc(+room.value || 0); recipeShow(); });
    word.className = 'hint'; word.textContent = 'in';
    del.type = 'button'; del.className = 'btn'; del.textContent = 'Remove';
    del.addEventListener('click', () => { state.recipe.encounters.splice(i, 1); recipeSync(true); });
    li.append(mod, word, room, del); ul.appendChild(li);
  });
}
// What stops the mission being made, and what to know first, as the panel stands.
function recipeShow() {
  const c = recipeCheck(recipeForm()), ul = $('rcBefore');
  ul.textContent = '';
  for (const t of c.blockers) { const li = document.createElement('li'); li.className = 'bad'; li.textContent = t; ul.appendChild(li); }
  for (const t of c.notes) { const li = document.createElement('li'); li.textContent = t; ul.appendChild(li); }
  $('rcReady').textContent = c.blockers.length ? 'Before the mission can be made (' + c.blockers.length + '):' : 'Ready. Make the mission writes everything below as one step; Undo takes it all back.';
  $('rcGo').disabled = c.blockers.length > 0;
  $('rcSaveTest').hidden = !state.recipe.made || !state.rehearse.rows.length;
  $('rcNote').textContent = state.recipe.note;
}
// The lists follow the map: insertions, intel, places and kept modules. Drawn again only when one of them changed.
function recipeSync(force) {
  const ins = recipeInsertions(), kinds = recipeKinds(), mods = recipeModuleList();
  const key = JSON.stringify([ins, kinds, state.places.map(p => [p.id, p.name]), mods]);
  if (force || key !== state.recipe.key) {
    state.recipe.key = key;
    recipeFill($('rcInsertion'), [['', ins.length ? 'choose an insertion' : 'none known'], ...ins.map(q => [q.name, q.name + (q.pos ? '' : ' (where it is not known)')])]);
    recipeFill($('rcIntel'), [['', kinds.length ? 'choose the intel' : 'none known'], ...kinds.map(k => ['k:' + k.item, k.item]), ...(kinds.length ? [['any', 'Any intel item']] : [])]);
    recipeFill($('rcBuilding'), [['0', state.places.length ? 'choose a place' : 'no places yet'], ...state.places.map(p => [String(p.id), p.name + (p.area && p.area.parts.length ? '' : ' (a point)')])]);
    recipeEncounters();
  }
  recipeShow();
}
async function recipeGo() {
  const form = recipeForm();
  for (const enc of form.encounters) if (enc.pending) { enc.module = await recipeModuleOf(enc.key); enc.pending = false; if (!enc.module) { state.recipe.note = 'Nothing was made: the game did not send the module "' + enc.name + '".'; recipeShow(); return; } }
  recipeMake(form);
  recipeSync(true);
}
$('rcGo').addEventListener('click', recipeGo);
$('rcPaint').addEventListener('click', () => { say('Click where the building\'s name should stand, then paint its shape and name it under Places. Then choose it here.'); waitForPlace('area'); });
$('rcEncAdd').addEventListener('click', () => { state.recipe.encounters.push({ key: '', room: 0 }); recipeSync(true); });
$('rcSaveTest').addEventListener('click', () => { const r = state.rehearse; if (!r.name.trim()) { r.name = RECIPE_TEST; $('rhName').value = r.name; } rhSave(); });
for (const id of ['rcInsertion', 'rcIntel', 'rcBuilding', 'rcGarrison', 'rcSkill', 'rcClock', 'rcAlarm', 'rcMinutes', 'rcSquadMin', 'rcSquadMax']) $(id).addEventListener('change', recipeShow);
// The page's state is not all there while the parts start (the game link comes later), so the panel is drawn when used.
$('recipeBox').addEventListener('pointerenter', () => recipeSync(false));
$('recipeBox').addEventListener('focusin', () => recipeSync(false));

expose({ recipeInsertions, recipeKinds, recipeSpots, recipeSpread, recipeReinforcements, recipeModuleTrouble, recipeCheck, recipeMake, recipeForm, recipeSync, RECIPE_TEST });   // the Intel raid recipe (ML-044), for automated tests
