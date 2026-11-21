// ---------------------------------------------------------------- trigger inspector (ML-015): why every trigger waits, or is done
// Live, every word and number comes from the game's own trigger engine (/api/state, triggerState[].explain), so a page that is
// reloaded shows exactly the same: the page remembers nothing and works nothing out. Without the game it shows what can be
// known from the file alone, the same reasoning as the Mission check: which trigger can fire, which is left to chance.
const INSPECT_WORDS = { off: 'Excluded', circle: 'In a circle', waiting: 'Waiting', holding: 'Holding', counting: 'Counting down', fired: 'Fired', cooling: 'Cooling down', exhausted: 'Exhausted' };
const INSPECT_TIMED = ['counting', 'holding', 'cooling'];
// A zone or hold with a gate (ML-055) needs that trigger as an after trigger does.
const inspectNeeds = z => (z.when === 'after' || z.when === 'outcome' || z.when === 'zone' ? (z.after ? [z.after] : []) : z.when === 'all' || z.when === 'any' ? (z.of || []) : []);
// The words for what became of one firing of a trigger's actions (ML-047), as the engine reports them.
const INSPECT_OUTCOMES = { applied: 'Actions applied', failed: 'Action failed', partial: 'Action partly applied', unconfirmed: 'Action not confirmed' };
const INSPECT_WAITS = { applied: 'is seen to take', failed: 'fails', partial: 'only partly takes', unconfirmed: 'goes through unseen', timeout: 'has no answer within ' };
const inspectPlaced = z => z.when === 'zone' || z.when === 'door';
// Whole seconds rounded up, as the engine counts them ("1 s left" until it fires); a minute or more as m:ss.
function inspectClock(left) {
  const s = Math.max(0, Math.ceil((+left || 0) - 0.01));
  return s < 60 ? s + ' s' : Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
}
// The engine's sentence with the named place put in where it speaks of the place (29b-places.js).
function inspectWithPlace(z, text) {
  const name = inspectPlaced(z) ? placeName(z.pos) : '';
  if (!name || !text) return text;
  if (z.when === 'door') return text.replace(/\) to be opened/, ')' + placeWords(z.pos) + ' to be opened');
  return /\binside\b/.test(text) ? text.replace(/\binside\b/, 'inside ' + name) : text + ' (' + name + ')';
}

// Without the game: can the trigger fire on its own, and on which runs? '' as text when nothing stands in its way.
function inspectStatic(z) {
  const byId = id => state.zones.find(q => q.id === id);
  // Why it can never fire by itself ('' when it can on some run): no place, a missing link, a circle.
  const never = (q, trail) => {
    if (trail.includes(q.id)) return 'it waits for itself through other triggers';
    if (inspectPlaced(q) && !isVec(q.pos)) return 'it has no place on the map';
    if (q.when === 'outcome' && !q.after) return 'it watches no trigger';
    if (outcomeNeverSeen(q)) return q.outcome === 'partial' ? 'trigger ' + q.after + ' brings fewer than two enemies, so it never partly takes' : 'not everything trigger ' + q.after + ' does can be seen, so it is never reported as applied';
    const on = trail.concat(q.id), list = inspectNeeds(q);
    if ((q.when === 'all' || q.when === 'any') && !list.length) return 'its list of triggers is empty';
    const why = list.map(id => { const p = byId(id); return !p ? 'trigger ' + id + ' does not exist' : p.when === 'after' && !p.after ? '' : never(p, on); });
    if (q.when === 'any') return why.some(w => w === '') ? '' : why[0];
    return why.find(w => w !== '') || '';
  };
  // The first trigger in its chain that is left to chance or fired by hand: then it fires on some runs only, or only by hand.
  // "any" hangs on that only when every one of its list does.
  const hangOne = (p, trail) => {
    if (!p || trail.includes(p.id)) return '';
    if ((p.chance || 100) < 100) return 'Waits for trigger ' + p.id + ', which is left to chance (' + p.chance + '%): on a run where it sits out, this one waits for ever';
    if (p.when === 'after' && !p.after) return 'Waits for trigger ' + p.id + ', which fires only by hand';
    return hangs(p, trail);
  };
  const hangs = (q, trail) => {
    const found = inspectNeeds(q).map(id => hangOne(byId(id), trail.concat(q.id)));
    if (q.when === 'any') return found.length && found.every(Boolean) ? found[0] : '';
    return found.find(Boolean) || '';
  };
  const blocked = z.when === 'after' && !z.after ? '' : never(z, []);
  if (blocked) return { word: 'Never fires', kind: 'never', text: 'It can never fire by itself: ' + blocked + '. Only Fire now sets it off' };
  const condition = inspectCondition(z);
  if ((z.chance || 100) < 100) return { word: 'Chance ' + z.chance + '%', kind: 'chance', text: 'Left to chance: on about ' + (100 - z.chance) + '% of runs it sits the mission out (excluded) and only Fire now sets it off. Otherwise: ' + condition };
  if (z.when === 'after' && !z.after) return { word: 'By hand', kind: 'byhand', text: 'Fires only when the Game Master presses Fire now' };
  const hang = hangs(z, []);
  if (hang) return { word: 'Depends', kind: 'depends', text: hang };
  return { word: 'Can fire', kind: 'can', text: condition };
}
// What a trigger waits for, in a sentence: for the static view.
function inspectCondition(z) {
  const kind = String(z.item || '').replace(/[ _]\d+$/, '').trim().toLowerCase();
  const tagged = z.tag ? ' tagged "' + z.tag + '"' : '';
  const list = (z.of || []).join(', ');
  let s = z.when === 'zone' ? (z.after ? 'Waits for trigger ' + z.after + ' before it watches its area, then f' : 'F') + (z.everyone ? 'ires when every living player is inside' : 'ires when a player steps inside') + (z.hold > 0 ? ' and stays ' + Math.round(z.hold) + ' s' : '')
    : z.when === 'alert' ? 'Fires when the enemy raises the alarm'
    : z.when === 'intel' ? (kind === '' || kind === 'any' ? 'Fires when any intel item is picked up' : kind === 'all' ? 'Fires when every intel item is picked up' : 'Fires when the ' + kind + ' is picked up')
    : z.when === 'time' ? 'Fires ' + Math.round(z.delay || 0) + ' s into the mission'
    : z.when === 'after' ? 'Fires after trigger ' + z.after
    : z.when === 'outcome' ? 'Fires when the latest firing of trigger ' + z.after + ' ' + (INSPECT_WAITS[z.outcome] || '') + (z.outcome === 'timeout' ? Math.round(z.within || OUTCOME_WITHIN) + ' s' : '')
    : z.when === 'all' ? 'Fires once triggers ' + list + ' have all fired'
    : z.when === 'any' ? 'Fires once one of triggers ' + list + ' has fired'
    : z.when === 'down' ? ((z.count | 0) <= 0 ? 'Fires once every enemy' + tagged + ' is down' : 'Fires once ' + z.count + ((z.count | 0) === 1 ? ' enemy' : ' enemies') + tagged + ' are down')
    : z.when === 'door' ? (isVec(z.pos) ? 'Fires when the door at (' + z.pos[0].toFixed(1) + ', ' + z.pos[2].toFixed(1) + ') is opened' : 'Fires when its door is opened')
    : 'Fires when its condition is met';
  s = inspectWithPlace(z, s);
  if (z.delay > 0 && z.when !== 'time') s += ', then waits ' + Math.round(z.delay) + ' s';
  if (z.repeat > 0) s += '; again every ' + Math.round(z.repeat) + ' s' + (z.times > 0 ? ', ' + z.times + ' times in all' : '');
  return s;
}

// One row per trigger. s: the game's /api/state answer (its triggerState carries the engine's explain objects), or null
// without the game. Row: { id, title, word, kind, text, count, clock, outcome, outcomeText, late, invocation, pending, watch,
// waiting, excluded, live }. Live, every firing is numbered (invocation, the latest); outcome is what became of that one, late
// a result that came for an older firing or after a timeout had already been taken (kept apart: it never changes the branch),
// pending the firings still without a result, and watch, for an outcome trigger, which firing of its trigger it waits on.
function inspectRows(s) {
  const running = !!(s && Array.isArray(s.triggerState));
  const byId = new Map(running ? s.triggerState.filter(t => t && t.id > 0).map(t => [t.id, t]) : []);
  return state.zones.map(z => {
    const title = trigTitle(z) + (z.name ? ': ' + z.name : '');
    if (!running) {
      const st = inspectStatic(z);
      return { id: z.id, title, word: st.word, kind: st.kind, text: st.text, count: z.repeat > 0 ? (z.times > 0 ? 'up to ' + z.times + ' times' : 'repeats') : '', clock: '', outcome: '', outcomeText: '', late: '', invocation: 0, pending: [], watch: '', waiting: [], excluded: [], live: false };
    }
    const t = byId.get(z.id);
    if (!t) return { id: z.id, title, word: 'Not in the game', kind: 'unknown', text: 'The running mission does not have this trigger: the change has not reached the game.', count: '', clock: '', outcome: '', outcomeText: '', late: '', invocation: 0, pending: [], watch: '', waiting: [], excluded: [], live: true };
    const ex = t.explain || { phase: t.phase, left: t.left, why: '', fires: t.fires | 0, times: z.times | 0, waiting: [], excluded: [], inside: -1, alive: -1, outcome: '', outcomeWhy: '' };
    const fires = ex.fires | 0, times = ex.times | 0, repeats = times > 0 || z.repeat > 0;
    const outcome = INSPECT_OUTCOMES[ex.outcome] ? ex.outcome : '';
    const why = ex.outcomeWhy || '', invocation = Math.max(0, ex.invocation | 0), pending = Array.isArray(ex.pending) ? ex.pending.filter(n => Number.isInteger(n) && n > 0) : [];
    // An outcome trigger names the firing it waits on: the latest of the trigger it watches, as the game numbers it.
    const w = z.when === 'outcome' ? byId.get(z.after) : null, wn = w && w.explain ? Math.max(0, w.explain.invocation | 0) : 0;
    const watch = z.when !== 'outcome' ? '' : wn > 0 ? 'waits on firing ' + wn + ' of trigger ' + z.after : 'trigger ' + z.after + ' has not fired yet';
    return {
      id: z.id, title, word: INSPECT_WORDS[ex.phase] || String(ex.phase || ''), kind: ex.phase || 'waiting',
      text: t.explain ? inspectWithPlace(z, ex.why || '') : 'This version of the plugin does not explain its triggers.',
      count: repeats ? 'fired ' + fires + (times > 0 ? ' of ' + times : ', no limit') : fires > 1 ? 'fired ' + fires + ' times' : '',
      clock: INSPECT_TIMED.includes(ex.phase) ? inspectClock(ex.left) : '',
      outcome, late: typeof ex.late === 'string' ? ex.late : '', invocation, pending, watch,
      outcomeText: outcome ? (invocation > 0 ? 'Firing ' + invocation + ': ' : '') + INSPECT_OUTCOMES[outcome] + (why ? ': ' + why : '') : pending.includes(invocation) && invocation > 0 ? 'Firing ' + invocation + ': no result yet' : '',
      waiting: Array.isArray(ex.waiting) ? ex.waiting : [], excluded: Array.isArray(ex.excluded) ? ex.excluded : [], live: true,
    };
  });
}

// What a trigger hangs on and what it brings: its prerequisites all the way back (after/all/any), the enemies and things
// bound to it, the doors it sets, the door it watches, and the extraction when it opens it or the extraction waits for it.
// Enemies and things are indices into state.enemies/state.things; doors carry their position and the index of the map's
// door at that place (-1: no door of the map is known there).
function inspectLinks(id) {
  const out = { id, before: [], enemies: [], things: [], doors: [], watched: null, extraction: '' };
  const byId = q => state.zones.find(z => z.id === q), z = byId(id);
  if (!z) return out;
  const seen = new Set([id]);
  const walk = q => { for (const n of inspectNeeds(q)) if (!seen.has(n)) { seen.add(n); out.before.push(n); const p = byId(n); if (p) walk(p); } };
  walk(z);
  state.enemies.forEach((e, i) => { if (e.trigger === id) out.enemies.push(i); });
  state.things.forEach((t, i) => { if (t.trigger === id) out.things.push(i); });
  for (const d of z.doors || []) if (isVec(d.pos) && (d.locked != null || d.open != null)) out.doors.push({ name: d.name || '', pos: d.pos.slice(0, 3), index: nearDoor(d.pos, state.doors) });
  if (z.when === 'door' && isVec(z.pos)) out.watched = { pos: z.pos.slice(0, 3), index: nearDoor(z.pos, state.doors) };
  out.extraction = z.opensExtraction ? 'opens' : state.extractionAfter === id ? 'holds' : '';
  return out;
}
// The same in words, under the selected row.
function inspectLinkText(l) {
  const parts = [];
  if (l.before.length) parts.push('Needs ' + (l.before.length === 1 ? 'trigger ' : 'triggers ') + l.before.join(', '));
  if (l.enemies.length) parts.push('brings ' + (l.enemies.length === 1 ? 'enemy ' : 'enemies ') + l.enemies.map(i => i + 1).join(', '));
  if (l.things.length) parts.push('places ' + l.things.length + (l.things.length === 1 ? ' thing' : ' things'));
  if (l.doors.length) parts.push('sets ' + l.doors.map(d => d.name || 'a door').join(', '));
  if (l.watched) parts.push('watches the door at ' + l.watched.pos[0].toFixed(0) + ', ' + l.watched.pos[2].toFixed(0) + placeWords(l.watched.pos));
  if (l.extraction) parts.push(l.extraction === 'opens' ? 'opens the extraction' : 'the extraction waits for it');
  const s = parts.join('; ');
  return s ? s.charAt(0).toUpperCase() + s.slice(1) + '.' : 'Nothing on the map belongs to it, and it waits for no other trigger.';
}

const inspectRunning = () => live.on && live.mission;
const inspectShown = () => typeof viewIs !== 'function' || viewIs('direct');
function inspectSelect(id) {
  state.inspectSel = state.inspectSel === id ? 0 : id;
  syncInspect(true); invalidate();
}
// The list. Rebuilt only when the triggers change; otherwise the words and numbers are updated where they stand, so a
// countdown ticking does not take the focus off a button.
function syncInspect(force) {
  const list = $('inspectList'); if (!list) return;
  // A sealed mission, not revealed (20b-sealed.js): what triggers wait for, what was rolled and what they will do stay out.
  if (sealedHides()) {
    $('inspectNote').textContent = 'This mission is sealed, so the inspector does not say what triggers wait for, what was rolled or what they will do. Reveal the mission to see it (spoiler avoidance: the file stays readable).';
    $('inspectEmpty').hidden = true; list.hidden = true; list.textContent = ''; list.dataset.shape = ''; state.inspectSel = 0;
    return;
  }
  list.hidden = false;
  const running = inspectRunning();
  const rows = inspectRows(running ? { triggerState: [...live.trig.values()] } : null);
  $('inspectNote').textContent = running ? 'Live from the game. Every value is the game\'s own, so a reloaded page shows the same.'
    : (live.on ? 'No mission is running. ' : 'Without the game. ') + 'This is what the file says each trigger can do. Live values (who is inside, time left, how often it fired, whether its actions took) need a running mission.';
  $('inspectEmpty').hidden = rows.length > 0;
  if (state.inspectSel && !rows.some(r => r.id === state.inspectSel)) state.inspectSel = 0;
  const links = state.inspectSel ? inspectLinks(state.inspectSel) : null;
  const before = new Set(links ? links.before : []);
  const shape = rows.map(r => r.id).join(',') + '|' + running;
  if (force || list.dataset.shape !== shape) {
    list.dataset.shape = shape; list.textContent = '';
    for (const r of rows) {
      const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
      const li = mk('li'), pick = mk('button', { type: 'button', className: 'pick' });
      li.dataset.id = String(r.id);
      pick.addEventListener('click', () => inspectSelect(r.id));
      pick.append(mk('b', { className: 'title' }), mk('span', { className: 'word' }), mk('span', { className: 'num' }), mk('span', { className: 'text' }), mk('span', { className: 'outcome' }), mk('span', { className: 'late' }));
      const fire = mk('button', { type: 'button', className: 'btn fire', textContent: 'Fire now', hidden: !running });
      fire.addEventListener('click', () => liveCommand('fire', { id: r.id }));
      li.append(pick, fire, mk('p', { className: 'links hint' }));
      list.appendChild(li);
    }
  }
  const items = list.children || [];
  rows.forEach((r, i) => {
    const li = items[i]; if (!li || !li.querySelector) return;
    const sel = r.id === state.inspectSel;
    li.className = 'k-' + r.kind + (r.outcome ? ' o-' + r.outcome : '') + (sel ? ' sel' : '') + (before.has(r.id) ? ' pre' : '') + (r.excluded.length ? ' blocked' : '');
    const pick = li.querySelector('.pick'); pick.setAttribute('aria-pressed', String(sel));
    li.querySelector('.title').textContent = r.title + (before.has(r.id) ? ' · needed by the selected one' : '');
    li.querySelector('.word').textContent = r.word;
    li.querySelector('.num').textContent = [r.count, r.clock && r.clock + ' left', r.watch].filter(Boolean).join(' · ');
    li.querySelector('.text').textContent = r.text;
    const oc = li.querySelector('.outcome'); oc.textContent = r.outcomeText; oc.hidden = !r.outcomeText || r.outcome === 'applied' && !sel;
    const lt = li.querySelector('.late'); lt.textContent = r.late ? 'Late: ' + r.late : ''; lt.hidden = !r.late;
    const fire = li.querySelector('.fire'); fire.hidden = !running;
    fire.disabled = r.kind === 'fired' || r.kind === 'exhausted' || r.kind === 'cooling';     // latched: the engine refuses Fire now (TriggerEngine.FireNow)
    fire.title = fire.disabled ? r.title + ' has fired and is not armed now, so Fire now would be refused.' : 'Set ' + r.title + ' off now, whatever it waits for. This is not part of the inspection: it changes the mission.';
    const lk = li.querySelector('.links'); lk.hidden = !sel; lk.textContent = sel && links ? inspectLinkText(links) : '';
  });
}
// Twice a second, so a countdown follows the game's (the state is asked for every 0.6 s); only while Direct is shown.
setInterval(() => { const shown = inspectShown(); $('inspectBox').hidden = !shown; if (shown) syncInspect(false); }, 500);

// The map: the selected trigger's place, what it needs (zones and doors with a place) and what it brings. Called from draw().
function drawInspect() {
  if (!state.inspectSel || !inspectShown() || sealedHides()) return;
  const z = state.zones.find(q => q.id === state.inspectSel); if (!z) return;
  const l = inspectLinks(z.id), v = state.view, mono = getComputedStyle(document.body).getPropertyValue('--mono');
  const ring = (p, r, colour, dash) => { const [x, y] = toScreen(p[0], p[2]); ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.lineWidth = 3; ctx.strokeStyle = colour; ctx.setLineDash(dash || []); ctx.stroke(); ctx.setLineDash([]); };
  const door = (d, colour) => {
    if (d.index >= 0 && state.doors[d.index]) { const g = doorLine(state.doors[d.index]); ctx.beginPath(); ctx.moveTo(g[0], g[1]); ctx.lineTo(g[2], g[3]); ctx.lineWidth = 15; ctx.lineCap = 'round'; ctx.globalAlpha = 0.45; ctx.strokeStyle = colour; ctx.stroke(); ctx.globalAlpha = 1; ctx.lineCap = 'butt'; }
    else { const [x, y] = toScreen(d.pos[0], d.pos[2]); ctx.beginPath(); ctx.moveTo(x - 7, y - 7); ctx.lineTo(x + 7, y + 7); ctx.moveTo(x + 7, y - 7); ctx.lineTo(x - 7, y + 7); ctx.lineWidth = 3; ctx.strokeStyle = colour; ctx.stroke(); }   // no door of the map is known there
  };
  for (const id of l.before) { const p = state.zones.find(q => q.id === id); if (p && p.when === 'zone' && isVec(p.pos)) ring(p.pos, Math.max(8, p.radius * v.scale), css['ink-2'], [6, 4]); else if (p && p.when === 'door' && isVec(p.pos)) door({ pos: p.pos, index: nearDoor(p.pos, state.doors) }, css['ink-2']); }
  if (z.when === 'zone' && isVec(z.pos)) ring(z.pos, Math.max(8, z.radius * v.scale), css.warn);
  if (l.watched) door(l.watched, css.warn);
  for (const d of l.doors) door(d, css.warn);
  for (const i of l.enemies) { const e = state.enemies[i]; if (e) ring(e.pos, 14, css.warn); }
  for (const i of l.things) { const t = state.things[i]; if (t) ring(t.pos, 11, css.warn, [3, 3]); }
  // A caption, since the extraction and triggers without a place have nowhere on the map to be drawn.
  const said = trigTitle(z) + ': ' + inspectLinkText(l);
  ctx.font = '500 12px ' + mono; ctx.textAlign = 'left'; ctx.textBaseline = 'top';
  const { w: cw, h: ch } = size(), w = Math.min(ctx.measureText(said).width + 16, cw - 24), top = ch - 80;    // above the status line
  ctx.fillStyle = css.panel; ctx.globalAlpha = 0.92; ctx.fillRect(12, top, w, 22); ctx.globalAlpha = 1;
  ctx.strokeStyle = css.warn; ctx.lineWidth = 1.5; ctx.strokeRect(12, top, w, 22);
  ctx.fillStyle = css.ink; ctx.fillText(said, 20, top + 5, w - 16);
}
