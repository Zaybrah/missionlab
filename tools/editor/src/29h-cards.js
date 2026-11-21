// ------------------------------------------------------------------ Game Master budget and action cards (ML-025)
// Create: the author's director block (Mission tab): starting points, points back a minute, and the actions, each firing
// one of the scenario's triggers at a price and with a rest. The file rules (scenarioProblems) check it like the rest.
// Direct: one card per action from the game's own budget snapshot (/api/state "budget", Core Budget.Json). The page works
// nothing out: points, rests, whether a card may be played and why not are the game's, so every page shows the same. A
// press sends {"cmd":"action","id":...} with a command number; when no answer came, pressing again sends the same number,
// and the game answers it from what it already did, so a card is never paid for twice.
const CARD_FOLLOW = 90, CARD_FOLLOW_MS = 800;   // about 72 s: the game waits up to 30 s for a safe moment, then up to 30 s for the trigger's answer
// budget: the last snapshot from the game (null: none); seen: the state it came from; ops: per action id, the last press
// and what became of it; session: the mission those presses belong to; pressure: the pressure director's snapshot (ML-052).
const cards = { budget: null, seen: null, ops: new Map(), session: null, shape: '', authorShape: '', pressure: null, pressureShape: '', prAuthorShape: '' };

// The game's answers. Every state carries the snapshot when the mission has a budget; a state without one means none.
function cardTake(s) {
  if (!s || typeof s !== 'object') return;
  if (typeof s.session === 'string' && typeof cards.session === 'string' && s.session !== cards.session) cards.ops.clear();
  if (typeof s.session === 'string') cards.session = s.session;
  cards.budget = s.mission && s.budget && typeof s.budget === 'object' && Array.isArray(s.budget.actions) ? s.budget : null;
  cards.pressure = s.mission && s.pressure && typeof s.pressure === 'object' && Array.isArray(s.pressure.log) ? s.pressure : null;
}

// ---------------------------------------------------------------- what a card says
const cardPlural = (n, one, many) => n + ' ' + (n === 1 ? one : many);
const cardTime = sec => { const s = Math.max(0, Math.round(+sec || 0)); return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0'); };
const cardPoints = p => String(Math.round((+p || 0) * 100) / 100);
// The trigger the action fires, and what it brings: the enemies still waiting for it (all bound to it when offline),
// the doors it sets, its message, the lights and the extraction.
function cardEffect(fire) {
  const z = state.zones.find(q => q.id === fire);
  if (!z) return 'Fires trigger ' + fire + ', which this page does not know yet';
  const bound = state.enemies.filter(e => e.trigger === fire), coming = live.on ? bound.filter(e => e.waiting) : bound;
  const parts = [];
  if (coming.length) parts.push('brings ' + cardPlural(coming.length, 'enemy', 'enemies'));
  else if (bound.length) parts.push('its enemies are already out');
  const doors = (z.doors || []).length;
  if (doors) parts.push('sets ' + cardPlural(doors, 'door', 'doors'));
  if (z.message) parts.push('says "' + z.message + '"');
  if (z.power) parts.push('turns the lights ' + z.power);
  if (z.opensExtraction) parts.push('opens the extraction');
  return 'Fires trigger ' + fire + (z.name ? ' "' + z.name + '"' : '') + (parts.length ? ': ' + parts.join(', ') : ': nothing on the map is bound to it');
}
// The placement rule the game checks before firing, in words. The sight test is the plugin's own (a view cone and a ray),
// not the game's: it is strict, but it is not proof that nobody could see the spot.
function cardRule(a) {
  const need = [];
  if (+a.minDistance > 0) need.push('at least ' + cardPoints(a.minDistance) + ' m from every player');
  if (a.unseen) need.push('out of every player\'s view (the plugin\'s strict view test, not the game\'s own sight)');
  if (!need.length) return 'Fires at once; no placement rule.';
  return 'Only ' + need.join(' and ') + '; otherwise ' + (a.fallback === 'cancel' ? 'cancelled and refunded.' : 'it waits up to 30 s for a safe moment, then is cancelled and refunded.');
}
// What became of the last press, as the game reported it.
function cardOpText(op) {
  if (!op) return '';
  const said = String(op.said || '');
  switch (op.status) {
    case 'sending': return op.tries > 1 ? 'Asking again with the same number...' : 'Sending...';
    case 'pending': return /^waiting: /.test(said) ? 'Waiting for a safe spot: ' + said.slice(9) : said ? 'Pending: ' + said : 'Pending.';
    case 'applied': return 'Applied' + (said ? ': ' + said : '.');
    case 'unconfirmed': return 'Requested; the game could not confirm the effect' + (said ? ': ' + said : '.');
    case 'cancelled': return 'Cancelled: ' + said.replace(/^cancelled: /, '');
    case 'lost': return 'No answer from the game. Press Ask again: it carries the same number, so it is never paid for twice.';
    default: return 'Failed: ' + (said || 'no reason given');
  }
}
// One row per card, from a snapshot (the last one by default).
function cardRows(b) {
  b = b === undefined ? cards.budget : b;
  if (!b || !Array.isArray(b.actions)) return [];
  const on = b.on !== false;
  return b.actions.map(a => {
    const op = cards.ops.get(a.id) || null, left = +a.cooldownLeft || 0, rest = +a.cooldown || 0;
    const busy = !!op && (op.status === 'sending' || op.status === 'pending'), retry = !!op && op.status === 'lost';
    return {
      id: a.id, name: a.name || a.id, effect: cardEffect(a.fire | 0), rule: cardRule(a),
      cost: on ? cardPlural(+a.cost || 0, 'point', 'points') : 'free: budget rules are off',
      rest: left > 0 ? cardTime(left) + ' rest left' : rest > 0 ? 'rests ' + cardTime(rest) + ' after use' : 'no rest',
      available: !!a.available, why: a.available ? '' : a.why || 'the game gives no reason',
      op, opText: cardOpText(op), busy, retry,
      // A retry is always allowed: the first try may have been paid for, which is exactly what the same number settles.
      enabled: !busy && (retry || !!a.available), label: retry ? 'Ask again' : busy ? 'Waiting...' : 'Play',
    };
  });
}
function cardMeter(b) {
  b = b === undefined ? cards.budget : b;
  return b ? cardPoints(b.points) + ' of ' + (b.budget | 0) + ' points' + ((b.regen | 0) > 0 ? ', ' + (b.regen | 0) + ' back a minute' : '') : '';
}
// The newest spends first, by action name, with the time since the mission was laid out and whether it was given back.
function cardSpent(b) {
  b = b === undefined ? cards.budget : b;
  if (!b || !Array.isArray(b.spent)) return [];
  const names = new Map((b.actions || []).map(a => [a.id, a.name || a.id]));
  return b.spent.slice().reverse().map(s => ({ command: s.command, refunded: !!s.refunded, text: cardTime(s.at) + '  ' + (names.get(s.id) || s.id) + (s.refunded ? ' · refunded' : '') }));
}

// ---------------------------------------------------------------- pressing a card
const cardIo = {
  send: (body, command) => api('/api/command', body, command).then(async s => { live.rev = null; await applyLive(s); return s; }),
  ask: async command => (await fetch('/api/command/' + encodeURIComponent(command), { cache: 'no-store' })).json(),
  wait: ms => new Promise(done => setTimeout(done, ms)),
};
const cardPart = k => (k && Array.isArray(k.ops) ? k.ops.find(o => o && o.op === 'action') || null : null);
function cardSet(op, part, none) {
  if (!part) { op.status = 'failed'; op.said = none || 'the game gave no result for the card'; return; }
  op.said = String(part.said || '');
  op.status = part.status === 'failed' && /^cancelled: /.test(op.said) ? 'cancelled' : ['pending', 'applied', 'unconfirmed', 'failed'].includes(part.status) ? part.status : 'failed';
}
async function cardPress(id, io) {
  io = io || cardIo;
  const was = cards.ops.get(id);
  if (was && (was.status === 'sending' || was.status === 'pending')) return was;      // still out: never sent twice
  const again = !!was && was.status === 'lost';
  const op = { command: again ? was.command : newCommand(), status: 'sending', said: '', tries: again ? was.tries + 1 : 1 };
  cards.ops.set(id, op); syncCards();
  try {
    const s = await io.send({ cmd: 'action', id }, op.command);
    cardTake(s);
    let part = cardPart(s && s.result);
    if (!part && s && s.mission !== false) { try { part = cardPart(await io.ask(op.command)); } catch (e) { part = null; } }
    cardSet(op, part, s && s.mission === false ? 'no mission is running' : '');
  } catch (err) {
    const res = err && err.result, st = err && err.status;
    if (res === 'pending' || res === 'failed') {
      // The game took it; its ledger has the card's own answer (a "cancelled: ..." is told apart there).
      let part = null; try { part = cardPart(await io.ask(op.command)); } catch (e) { part = null; }
      if (part) cardSet(op, part); else { op.status = res; op.said = res === 'pending' ? 'the game has begun it and has not finished' : (err && err.message) || ''; }
    } else if (st || res) { op.status = 'failed'; op.said = liveTrouble(err); }   // refused before it ran (another mission, busy, expired): nothing was paid
    else op.status = 'lost';                                                       // no answer at all: it may or may not have reached the game
  }
  syncCards();
  if (op.status === 'pending') await cardFollow(op, io);
  return op;
}
// Waiting for a safe spot or for the trigger's answer: ask the game's ledger until the card is settled.
async function cardFollow(op, io) {
  for (let i = 0; i < CARD_FOLLOW && op.status === 'pending'; i++) {
    await io.wait(CARD_FOLLOW_MS);
    let k = null; try { k = await io.ask(op.command); } catch (e) { continue; }
    if (k && k.status === 'unknown') { op.status = 'unconfirmed'; op.said = 'the game no longer knows this request; look at the map'; break; }
    const part = cardPart(k);
    if (part) cardSet(op, part);
    syncCards();
  }
  if (op.status === 'pending') op.said = 'still not settled after ' + Math.round(CARD_FOLLOW * CARD_FOLLOW_MS / 1000) + ' s; the replay records what came of it';
  syncCards();
  return op;
}
function cardMode(on) { return liveCommand('budgetMode', { on: !!on }); }

// ---------------------------------------------------------------- the Direct section
function cardNote(b) {
  if (!live.on) return 'Cards need the game: host a mission with MissionLab and open http://localhost:8765.';
  if (!live.mission) return 'No mission is running.';
  if (!b) return 'This mission has no Game Master budget. Add one in Create, Mission tab.';
  if (b.on === false) return 'Free directing: the budget rules are off for this session. Cards fire without spending; the replay records that.';
  return 'Points, rests and why a card cannot be played come from the game, whichever page plays the card.';
}
function syncCards() {
  const list = $('cardsList'); if (!list) return;
  const b = cards.budget, rows = cardRows(b);
  $('cardsNote').textContent = cardNote(b);
  $('cardsMeterRow').hidden = !b; $('cardsModeRow').hidden = !b;
  if (b) {
    const m = $('cardsMeter'); m.max = Math.max(1, b.budget | 0); m.value = Math.max(0, +b.points || 0);
    $('cardsPoints').textContent = cardMeter(b);
    $('cardsMode').checked = b.on !== false;      // the game's mode, not the last click: a refused change shows as not made
  }
  const shape = rows.map(r => r.id).join(',');
  if (cards.shape !== shape) {
    cards.shape = shape; list.textContent = '';
    for (const r of rows) {
      const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
      const li = mk('li'), play = mk('button', { type: 'button', className: 'btn primary play' });
      li.dataset.id = r.id;
      play.addEventListener('click', () => cardPress(r.id));
      li.append(mk('b', { className: 'name' }), mk('span', { className: 'cost' }), mk('span', { className: 'effect' }), mk('span', { className: 'rest' }),
        mk('span', { className: 'rule hint' }), mk('span', { className: 'why' }), mk('span', { className: 'op', role: 'status' }), play);
      list.appendChild(li);
    }
  }
  const items = list.children || [];
  rows.forEach((r, i) => {
    const li = items[i]; if (!li || !li.querySelector) return;
    li.className = (r.available ? 'can' : 'cannot') + (r.op ? ' o-' + r.op.status : '');
    li.querySelector('.name').textContent = r.name;
    li.querySelector('.cost').textContent = r.cost;
    li.querySelector('.effect').textContent = r.effect;
    li.querySelector('.rest').textContent = r.rest;
    li.querySelector('.rule').textContent = r.rule;
    const why = li.querySelector('.why'); why.textContent = r.why ? 'Not now: ' + r.why : ''; why.hidden = !r.why;
    const op = li.querySelector('.op'); op.textContent = r.opText; op.hidden = !r.opText;
    const play = li.querySelector('.play'); play.textContent = r.label; play.disabled = !r.enabled;
    play.title = r.retry ? 'Sends the same request again under its first number: the game pays for it once.' : r.available ? 'Play ' + r.name + ' (' + r.cost + ').' : 'Not now: ' + r.why;
  });
  const spent = $('cardsSpent'), was = cardSpent(b);
  spent.textContent = '';
  for (const s of was) { const li = document.createElement('li'); li.textContent = s.text; if (s.refunded) li.className = 'refunded'; spent.appendChild(li); }
  $('cardsSpentHead').hidden = !was.length;
  syncPressure();
}
$('cardsMode').addEventListener('change', ev => cardMode(ev.target.checked));
// Twice a second, like the inspector: the state is read every 0.6 s; only while Direct is shown.
const cardsTick = () => {
  if (catalogue.s !== cards.seen) { cards.seen = catalogue.s; cardTake(catalogue.s); }
  const shown = viewIs('direct'); $('cardsBox').hidden = !shown; if (shown) syncCards();
};
setInterval(cardsTick, 500); onPaneChange(cardsTick);

// ---------------------------------------------------------------- the Create section: authoring the director block
// A trigger for a new action: one fired only by hand and not yet on a card, else any not yet on a card, else the first.
function cardPickTrigger() {
  const used = new Set((state.director ? state.director.actions : []).map(a => a.fire));
  const free = state.zones.filter(z => !used.has(z.id));
  const byHand = free.find(z => z.when === 'after' && !z.after);
  return (byHand || free[0] || state.zones[0] || { id: 0 }).id;
}
function cardAuthorOn(on) {
  if (!!on === !!state.director) return;
  willChange(on ? 'Give the Game Master a budget' : 'Take the Game Master budget away'); state.director = on ? { budget: 10, regen: 0, actions: [] } : null; changed();
}
function cardAuthorAdd() {
  const d = state.director;
  if (!d) return false;
  if (!state.zones.length) { say('Add a trigger first: every action fires one.'); return false; }
  willChange('Add a Game Master action');
  let n = d.actions.length + 1; while (d.actions.some(a => a.id === 'action' + n)) n++;
  d.actions.push({ id: 'action' + n, name: 'Action ' + n, cost: 1, cooldown: 0, fire: cardPickTrigger(), minDistance: 0, unseen: false, fallback: 'wait' });
  changed(); return true;
}
function cardAuthorDelete(i) {
  const d = state.director; if (!d || !d.actions[i]) return false;
  willChange('Delete the Game Master action "' + (d.actions[i].name || d.actions[i].id) + '"'); d.actions.splice(i, 1); changed(); return true;
}
// One value of the block (i < 0) or of action i. Types are kept; ranges are left to the file rules, which name the field.
const CARD_WHOLE = ['budget', 'regen', 'cost', 'fire'], CARD_NUMBER = ['cooldown', 'minDistance'];
function cardAuthorSet(i, key, value) {
  const d = state.director; if (!d) return false;
  const target = i < 0 ? d : d.actions[i]; if (!target || !(key in target)) return false;
  let v = value;
  if (CARD_WHOLE.includes(key)) v = Math.round(+value || 0);
  else if (CARD_NUMBER.includes(key)) v = Math.round((+value || 0) * 100) / 100;
  else if (key === 'unseen') v = value === true || value === 'true';
  else if (key === 'fallback') v = value === 'cancel' ? 'cancel' : 'wait';
  else v = String(value === undefined || value === null ? '' : value).trim();
  if (target[key] === v) return false;
  willChange(i < 0 ? 'Change the Game Master budget' : 'Change the Game Master action "' + (target.name || target.id) + '"'); target[key] = v; changed(); return true;
}
// The file rules' findings for the block, then what the page can add: cards that can never be played, triggers on two
// cards, and triggers that also fire by themselves (the card then races the trigger's own rule).
function cardAuthorProblems() {
  const d = state.director; if (!d) return [];
  const found = scenarioProblems(scenarioObject()).filter(f => f.path === 'director' || String(f.path || '').startsWith('director.'))
    .map(f => ({ level: f.level, path: f.path, text: f.text }));
  const seen = new Map();
  d.actions.forEach((a, i) => {
    const at = 'director.actions[' + i + ']', z = state.zones.find(q => q.id === a.fire);
    if (a.cost > d.budget) found.push({ level: 'note', path: at + '.cost', text: 'it costs more than the starting budget' + (d.regen > 0 ? ', so it waits for points to come back' : ' and no points come back: it can never be played') });
    if (seen.has(a.fire)) found.push({ level: 'note', path: at + '.fire', text: 'trigger ' + a.fire + ' is on another card too (' + seen.get(a.fire) + ')' });
    else seen.set(a.fire, a.name || a.id);
    if (z && !(z.when === 'after' && !z.after)) found.push({ level: 'note', path: at + '.fire', text: 'trigger ' + a.fire + ' also fires by itself (' + trigText(z) + '); the card can fire it first, and under budget rules Fire now refuses it' });
  });
  return found.concat(pressureAuthorNotes());
}
const cardAuthorWhere = path => {
  const pr = /^director\.pressure(?:\.bands\[(\d+)\])?\.?(.*)$/.exec(path || ''), p = state.director && state.director.pressure;
  if (pr) {
    const b = pr[1] !== undefined && p ? p.bands[+pr[1]] : null;
    return 'Pressure' + (pr[1] !== undefined ? ' band ' + (+pr[1] + 1) + (b && b.name ? ' "' + b.name + '"' : '') : '') + (pr[2] ? ', ' + pr[2] : '');
  }
  const m = /^director\.actions\[(\d+)\]\.?(.*)$/.exec(path || ''), d = state.director;
  if (!m) return path === 'director' ? 'Budget' : 'Budget ' + String(path || '').replace(/^director\.?/, '');
  const a = d && d.actions[+m[1]];
  return 'Action ' + (+m[1] + 1) + (a && a.name ? ' "' + a.name + '"' : '') + (m[2] ? ', ' + m[2] : '');
};
function syncCardsAuthor() {
  const list = $('gbActions'); if (!list) return;
  const d = state.director;
  if (document.activeElement !== $('gbOn')) $('gbOn').checked = !!d;
  $('gbBody').hidden = !d;
  syncPressureAuthor();
  if (!d) { cards.authorShape = ''; return; }
  setVal($('gbBudget'), d.budget); setVal($('gbRegen'), d.regen);
  $('gbAdd').disabled = !state.zones.length; $('gbNoTrig').hidden = state.zones.length > 0;
  const shape = d.actions.length + '|' + state.zones.map(z => z.id + ':' + z.name + ':' + z.when).join(',');
  const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  if (cards.authorShape !== shape) {
    cards.authorShape = shape; list.textContent = '';
    d.actions.forEach((a, i) => {
      const li = mk('li'), uid = 'gb' + i;
      const field = (label, k, input) => {
        const row = mk('div', { className: 'field' }); input.id = uid + k; input.dataset.k = k;
        input.addEventListener('change', ev => cardAuthorSet(i, k, ev.target.type === 'checkbox' ? ev.target.checked : ev.target.value));
        row.append(mk('label', { htmlFor: input.id, textContent: label }), input); return row;
      };
      const num = (min, max, step, title) => mk('input', { type: 'number', min, max, step, title });
      const fire = mk('select'), fb = mk('select');
      for (const z of state.zones) fire.appendChild(mk('option', { value: String(z.id), textContent: 'Trigger ' + z.id + (z.name ? ' "' + z.name + '"' : '') + ': ' + trigText(z) }));
      fb.append(mk('option', { value: 'wait', textContent: 'Wait up to 30 s for a safe moment, then cancel' }), mk('option', { value: 'cancel', textContent: 'Cancel at once' }));
      const unseen = mk('input', { type: 'checkbox' });
      const del = mk('button', { type: 'button', className: 'btn danger', textContent: 'Delete action' });
      del.addEventListener('click', () => cardAuthorDelete(i));
      const ur = mk('label', { className: 'check' }); unseen.id = uid + 'unseen'; unseen.dataset.k = 'unseen';
      unseen.addEventListener('change', ev => cardAuthorSet(i, 'unseen', ev.target.checked));
      ur.append(unseen, mk('span', { textContent: 'Only out of every player\'s view (a strict view test of the plugin\'s own, not the game\'s sight)' }));
      li.append(mk('b', { className: 'title', textContent: 'Action ' + (i + 1) }),
        field('Name', 'name', mk('input', { type: 'text', maxLength: 60 })),
        field('Id', 'id', mk('input', { type: 'text', maxLength: 32, spellcheck: false, title: 'Letters, digits, "-" and "_"; unique. Commands and the replay name the action by it.' })),
        field('Fires', 'fire', fire),
        field('Cost', 'cost', num(0, 1000, 1, 'Points the card costs, 0 to 1000.')),
        field('Rest (s)', 'cooldown', num(0, 3600, 5, 'Seconds before the card may be played again, 0 to 3600.')),
        field('Players at least (m)', 'minDistance', num(0, 500, 5, 'No player may be nearer than this to the trigger\'s place or to where its enemies appear. 0: no distance rule.')),
        ur, field('Otherwise', 'fallback', fb), mk('div', { className: 'row' }));
      li.lastChild.appendChild(del);
      list.appendChild(li);
    });
  }
  const items = list.children || [];
  d.actions.forEach((a, i) => {
    const li = items[i]; if (!li || !li.querySelector) return;
    for (const k of ['name', 'id', 'cost', 'cooldown', 'minDistance', 'fallback']) { const el = li.querySelector('[data-k="' + k + '"]'); if (el) setVal(el, a[k]); }
    const fire = li.querySelector('[data-k="fire"]'); if (fire) setVal(fire, String(a.fire));
    const unseen = li.querySelector('[data-k="unseen"]'); if (unseen && document.activeElement !== unseen) unseen.checked = !!a.unseen;
    const title = li.querySelector('.title'); if (title) title.textContent = 'Action ' + (i + 1) + (a.name ? ': ' + a.name : '');
  });
  const probs = $('gbProblems'), found = cardAuthorProblems();
  probs.textContent = '';
  for (const f of found) probs.appendChild(mk('li', { className: f.level === 'error' ? 'error' : 'note', textContent: cardAuthorWhere(f.path) + ': ' + f.text }));
}
$('gbOn').addEventListener('change', ev => cardAuthorOn(ev.target.checked));
$('gbBudget').addEventListener('change', ev => cardAuthorSet(-1, 'budget', ev.target.value));
$('gbRegen').addEventListener('change', ev => cardAuthorSet(-1, 'regen', ev.target.value));
$('gbAdd').addEventListener('click', cardAuthorAdd);

// ---------------------------------------------------------------- the pressure director (ML-052)
// An experiment in author-bounded pacing. Create: the bands the author allows it to spend in, its cap on living enemies and
// its rest after a casualty (director.pressure; 22-scenario.js reads, writes and checks it like the plugin). Direct: the game's
// own decision log, its mode and the host's "Hold now" and "Spend now". The page decides nothing: every row is the game's
// (/api/state "pressure", Core PressureDirector.Json), so whichever page looks sees the same log, manual choices labelled.
const PRESSURE_WHOLE = ['minPlayers', 'maxEnemies'], PRESSURE_NUMBER = ['from', 'every', 'rest'];
const pressureBandNew = name => ({ name, from: 0, goals: [], minPlayers: 0, spend: -1, every: 60 });
const pressureOf = () => (state.director && state.director.pressure) || null;
function pressureAuthorOn(on) {
  const d = state.director; if (!d || !!on === !!d.pressure) return false;
  willChange(on ? 'Let the pressure director pace the mission' : 'Take the pressure director away');
  d.pressure = on ? { mode: 'off', bands: [pressureBandNew('Whole mission')], maxEnemies: 0, rest: 60 } : null;
  changed(); return true;
}
function pressureBandAdd() {
  const p = pressureOf(); if (!p) return false;
  if (p.bands.length >= 6) { say('A pressure director has at most 6 bands.'); return false; }
  willChange('Add a pressure band'); p.bands.push(pressureBandNew('Band ' + (p.bands.length + 1))); changed(); return true;
}
function pressureBandDelete(i) {
  const p = pressureOf(); if (!p || !p.bands[i]) return false;
  if (p.bands.length === 1) { say('The pressure director needs at least one band; switch it off instead.'); return false; }
  willChange('Delete the pressure band "' + (p.bands[i].name || i + 1) + '"'); p.bands.splice(i, 1); changed(); return true;
}
// The order matters: the first band whose conditions hold is the one used.
function pressureBandMove(i, by) {
  const p = pressureOf(), j = i + by; if (!p || !p.bands[i] || !p.bands[j]) return false;
  willChange('Move the pressure band "' + (p.bands[i].name || i + 1) + '"'); const b = p.bands.splice(i, 1)[0]; p.bands.splice(j, 0, b); changed(); return true;
}
// One value of the director (i < 0) or of band i. Types are kept; ranges are left to the file rules, which name the field.
// An empty spend means the whole budget.
function pressureSet(i, key, value) {
  const p = pressureOf(); if (!p) return false;
  const target = i < 0 ? p : p.bands[i]; if (!target || !(key in target) || key === 'goals' || key === 'bands') return false;
  let v;
  if (key === 'spend') v = value === null || value === undefined || String(value).trim() === '' || +value < 0 ? -1 : Math.round(+value || 0);
  else if (PRESSURE_WHOLE.includes(key)) v = Math.round(+value || 0);
  else if (PRESSURE_NUMBER.includes(key)) v = Math.round((+value || 0) * 100) / 100;
  else if (key === 'mode') v = PRESSURE_MODES.includes(value) ? value : 'off';
  else v = String(value === undefined || value === null ? '' : value).trim();
  if (target[key] === v) return false;
  willChange(i < 0 ? 'Change the pressure director' : 'Change the pressure band "' + (target.name || i + 1) + '"'); target[key] = v; changed(); return true;
}
function pressureGoalSet(i, goal, on) {
  const p = pressureOf(), b = p && p.bands[i]; if (!b || b.goals.includes(goal) === !!on) return false;
  willChange('Change the objectives of the pressure band "' + (b.name || i + 1) + '"');
  b.goals = on ? b.goals.concat([goal]) : b.goals.filter(g => g !== goal); changed(); return true;
}
// What the page can add to the file rules: a director with no card to play, bands that are never reached and bands that only hold.
function pressureAuthorNotes() {
  const d = state.director, p = d && d.pressure; if (!p) return [];
  const notes = [], always = p.bands.findIndex(b => !(b.from > 0) && !b.goals.length && !(b.minPlayers > 0));
  const cheapest = d.actions.length ? Math.min(...d.actions.map(a => a.cost)) : 0;
  if (!d.actions.length) notes.push({ level: 'note', path: 'director.pressure', text: 'the director only plays this mission\'s cards, and there are none yet' });
  p.bands.forEach((b, i) => {
    const at = 'director.pressure.bands[' + i + ']';
    if (always >= 0 && i > always) notes.push({ level: 'note', path: at, text: 'never used: band ' + (always + 1) + ' always applies and comes first' });
    if (d.actions.length && b.spend >= 0 && b.spend < cheapest) notes.push({ level: 'note', path: at + '.spend', text: 'every card costs more than this band spends, so it only holds (a quiet band)' });
  });
  return notes;
}
function syncPressureAuthor() {
  const on = $('prOn'); if (!on) return;
  const p = pressureOf();
  if (document.activeElement !== on) on.checked = !!p;
  $('prBody').hidden = !p;
  if (!p) { cards.prAuthorShape = ''; return; }
  setVal($('prMode'), p.mode); setVal($('prMax'), p.maxEnemies); setVal($('prRest'), p.rest);
  $('prAdd').disabled = p.bands.length >= 6;
  const list = $('prBands'), mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  // A band's objective that is no longer in the mission still gets a box, so the author can take it away.
  const choices = b => state.goals.map(g => [g.id, g.name + ' (' + g.id + ')']).concat(b.goals.filter(id => !state.goals.some(g => g.id === id)).map(id => [id, id + ' (no such objective: untick it)']));
  const shape = p.bands.length + '|' + p.bands.map(b => choices(b).map(c => c[1]).join('+')).join(';');
  if (cards.prAuthorShape !== shape) {
    cards.prAuthorShape = shape; list.textContent = '';
    p.bands.forEach((b, i) => {
      const li = mk('li'), uid = 'pr' + i;
      const field = (label, k, input) => {
        const row = mk('div', { className: 'field' }); input.id = uid + k; input.dataset.k = k;
        input.addEventListener('change', ev => pressureSet(i, k, ev.target.value));
        row.append(mk('label', { htmlFor: input.id, textContent: label }), input); return row;
      };
      const num = (min, max, step, title, placeholder) => mk('input', { type: 'number', min, max, step, title, placeholder: placeholder || '' });
      const goals = mk('fieldset', { className: 'goals' });
      goals.appendChild(mk('legend', { textContent: state.goals.length ? 'Only while these objectives are active' : 'No objectives in this mission to wait for' }));
      for (const [id, label] of choices(b)) {
        const box = mk('input', { type: 'checkbox' }), row = mk('label', { className: 'check' });
        box.dataset.goal = id; box.addEventListener('change', ev => pressureGoalSet(i, id, ev.target.checked));
        row.append(box, mk('span', { textContent: label })); goals.appendChild(row);
      }
      const up = mk('button', { type: 'button', className: 'btn', textContent: 'Earlier', title: 'The first band whose conditions hold is the one used.' });
      const down = mk('button', { type: 'button', className: 'btn', textContent: 'Later', title: 'The first band whose conditions hold is the one used.' });
      const del = mk('button', { type: 'button', className: 'btn danger', textContent: 'Delete band' });
      up.addEventListener('click', () => pressureBandMove(i, -1)); down.addEventListener('click', () => pressureBandMove(i, 1)); del.addEventListener('click', () => pressureBandDelete(i));
      up.disabled = i === 0; down.disabled = i === p.bands.length - 1;
      const row = mk('div', { className: 'row' }); row.append(up, down, del);
      li.append(mk('b', { className: 'title' }),
        field('Name', 'name', mk('input', { type: 'text', maxLength: 60 })),
        field('From (s)', 'from', num(0, 36000, 10, 'Seconds after the start before this band can apply, 0 to 36000.')),
        field('Players at least', 'minPlayers', num(0, 16, 1, 'Living players the game sees, 0 to 16. 0: any number.')),
        field('Spend at most', 'spend', num(0, 1000, 1, 'Points one decision may spend, 0 to 1000. Empty: the whole budget.', 'whole budget')),
        field('Decide every (s)', 'every', num(10, 600, 5, 'Seconds between decisions while this band applies, 10 to 600.')),
        goals, row);
      list.appendChild(li);
    });
  }
  const items = list.children || [];
  p.bands.forEach((b, i) => {
    const li = items[i]; if (!li || !li.querySelector) return;
    for (const k of ['name', 'from', 'minPlayers', 'every']) { const el = li.querySelector('[data-k="' + k + '"]'); if (el) setVal(el, b[k]); }
    const spend = li.querySelector('[data-k="spend"]'); if (spend) setVal(spend, b.spend >= 0 ? b.spend : '');
    for (const [id] of choices(b)) { const box = li.querySelector('[data-goal="' + id + '"]'); if (box && document.activeElement !== box) box.checked = b.goals.includes(id); }
    const title = li.querySelector('.title'); if (title) title.textContent = 'Band ' + (i + 1) + (b.name ? ': ' + b.name : '');
  });
}
$('prOn').addEventListener('change', ev => pressureAuthorOn(ev.target.checked));
$('prMode').addEventListener('change', ev => pressureSet(-1, 'mode', ev.target.value));
$('prMax').addEventListener('change', ev => pressureSet(-1, 'maxEnemies', ev.target.value));
$('prRest').addEventListener('change', ev => pressureSet(-1, 'rest', ev.target.value));
$('prAdd').addEventListener('click', pressureBandAdd);

// Direct: the host's controls. Each is one command to the game; what came of it appears in the game's log.
const pressureIo = { send: body => liveCommand(body.cmd, { what: body.what, ...(body.arg !== undefined ? { arg: body.arg } : {}) }) };
function pressureCommand(what, arg, io) {
  return (io || pressureIo).send({ cmd: 'pressure', what, ...(arg !== undefined ? { arg } : {}) });
}
function pressureModeSet(mode, io) { if (!PRESSURE_MODES.includes(mode)) return null; return pressureCommand('mode', mode, io); }
function pressureHoldNow(io) { return pressureCommand('hold', undefined, io); }
function pressureSpendNow(io) { return pressureCommand('spend', undefined, io); }
// Why it held, in words, by the constraint the game names.
const PRESSURE_HELD = { pending: 'waiting for its last card to settle', off: 'off', 'budget-off': 'the budget rules are off', unknown: 'something could not be seen',
  rest: 'resting', 'no-band': 'no band applies', 'no-card': 'no card fits' };
// The game's log, newest first: what it did or would do, why, what it saw, and each card it passed over with the reason.
function pressureRows(p) {
  p = p === undefined ? cards.pressure : p;
  if (!p || !Array.isArray(p.log)) return [];
  const names = new Map(((cards.budget && cards.budget.actions) || []).map(a => [a.id, a.name || a.id]));
  return p.log.slice().reverse().map(e => {
    const card = '"' + (names.get(e.action) || e.action || '') + '" (' + cardPlural(e.cost | 0, 'point', 'points') + ')';
    let head;
    if (e.kind === 'spend') head = 'Played ' + card;
    else if (e.kind === 'preview') head = 'Would play ' + card + '; preview fires nothing';
    else if (e.kind === 'result') head = 'Result of ' + card + ': ' + (e.constraint || 'unknown');
    else if (e.kind === 'mode') head = 'Mode changed';
    else head = 'Held: ' + (PRESSURE_HELD[e.constraint] || e.constraint || 'no reason given');
    const band = e.band && (e.kind === 'spend' || e.kind === 'preview' || e.kind === 'hold') ? ' in band "' + e.band + '"' : '';
    return {
      at: cardTime(e.at), kind: String(e.kind || ''), manual: e.manual === true, label: (e.manual === true ? 'Manual: ' : '') + head + band,
      why: String(e.why || ''), observed: e.observed && e.kind !== 'result' && e.kind !== 'mode' ? 'Seen: ' + e.observed : '',
      rejected: (Array.isArray(e.rejected) ? e.rejected : []).map(r => '"' + (names.get(r.action) || r.action) + '" ' + r.constraint + ': ' + r.why + ' (seen: ' + r.observed + ')'),
    };
  });
}
function pressureNote(p) {
  if (!p) return '';
  const mode = p.mode === 'on' ? 'On: the director plays this mission\'s cards by itself, within the author\'s bands, cap and budget, through the same checks as a card you play.'
    : p.mode === 'preview' ? 'Preview: the director logs what it would play and fires nothing.' : 'Off: the director decides nothing; switch it to preview or on to use Spend now.';
  const authored = p.authored && p.authored !== p.mode ? ' The author set ' + p.authored + '.' : '';
  const now = p.waiting ? ' Waiting for its last card to settle.' : +p.restLeft > 0 ? ' Resting, ' + cardTime(p.restLeft) + ' left.' : p.mode !== 'off' && +p.next >= 0 ? ' Next decision in ' + cardTime(p.next) + '.' : '';
  return mode + authored + now + ' Where reinforcements appear is checked by ' + (p.approximation || 'the cards\' distance and view test, an approximation') + '.';
}
function syncPressure() {
  const box = $('prLive'); if (!box) return;
  const p = cards.pressure; box.hidden = !p;
  if (!p) { cards.pressureShape = ''; return; }
  $('prLiveNote').textContent = pressureNote(p);
  setVal($('prLiveMode'), p.mode);
  $('prSpendNow').disabled = p.mode === 'off' || !!p.waiting;
  const last = p.log[p.log.length - 1], shape = p.log.length + '|' + (last ? last.at + last.kind + last.words : '');
  if (cards.pressureShape === shape) return;
  cards.pressureShape = shape;
  const list = $('prLog'); list.textContent = '';
  for (const r of pressureRows(p)) {
    const li = document.createElement('li'); li.className = 'k-' + r.kind + (r.manual ? ' manual' : '');
    const head = document.createElement('b'); head.textContent = r.at + '  ' + r.label; li.appendChild(head);
    for (const t of [r.why, r.observed].concat(r.rejected.map(x => 'Not chosen: ' + x))) if (t) { const s = document.createElement('span'); s.textContent = t; li.appendChild(s); }
    list.appendChild(li);
  }
}
$('prLiveMode').addEventListener('change', ev => pressureModeSet(ev.target.value));
$('prHoldNow').addEventListener('click', () => pressureHoldNow());
$('prSpendNow').addEventListener('click', () => pressureSpendNow());
expose({ pressureAuthorOn, pressureBandAdd, pressureBandDelete, pressureBandMove, pressureSet, pressureGoalSet, pressureAuthorNotes, syncPressureAuthor,
  pressureCommand, pressureModeSet, pressureHoldNow, pressureSpendNow, pressureRows, pressureNote, syncPressure, pressureIn, pressureOut });   // the pressure director (ML-052), for automated tests
