// ------------------------------------------------------------------ replay
const replay = { data: null, t: 0, playing: false, speed: 4, last: 0, name: '' };
const mmss = t => Math.floor(t / 60) + ':' + String(Math.floor(t % 60)).padStart(2, '0');
// Who an enemy row is (recordings of semantics 3 on): "s4" is the scenario's spawn 4 counted from 0, "m12" a marker the
// Game Master placed, and "v1" after either marks the gunner of gun truck 1. '' when the recording does not know.
const whoOf = q => (typeof q[6] === 'string' ? q[6] : '');
function whoName(who) {
  const m = /^(?:([sm])(\d+))?(?:v(\d+))?$/.exec(who || '');
  if (!m || !who) return '';
  const said = [];
  if (m[1] === 's') said.push('spawn #' + (+m[2] + 1));
  if (m[1] === 'm') said.push('marker ' + m[2]);
  if (m[3]) said.push('gun truck ' + m[3]);
  return said.join(', ');
}
function loadReplay(obj, name) {
  if (!obj || obj.schema !== 'missionlab-replay/1' || !Array.isArray(obj.frames) || !obj.frames.length) throw new Error('It is a replay without any recorded moments.');
  replay.debrief = debriefOrNull(obj, name);      // the debrief (32a-debrief.js), read before the rows below are tidied for playback
  // Only moments that are whole are kept: a time, players and enemies, each with a place. A damaged file is read as far as it goes.
  const row = q => Array.isArray(q) && q.length >= 4 && [1, 2, 3].every(k => typeof q[k] === 'number' && isFinite(q[k]));
  // An identity that cannot be read is dropped, so that enemy is simply not known.
  const clean = q => { if (q.length > 6 && !whoName(whoOf(q))) q.length = 6; return q; };
  const frames = obj.frames.filter(f => Array.isArray(f) && typeof f[0] === 'number' && isFinite(f[0]) && Array.isArray(f[1]) && Array.isArray(f[2])).map(f => [f[0], f[1].filter(row), f[2].filter(row).map(clean)]);
  if (!frames.length) throw new Error('It is a replay, but none of its recorded moments can be read.');
  const semantics = typeof obj.semantics === 'number' ? obj.semantics : 1, linked = semantics >= 3;
  if (!linked) {
    // Older recordings do not say who an enemy is, so what follows is worked out by guessing.
    // A new enemy's first moments were recorded as a state it was not in (plugins before 0.17). They count as idle here.
    const born = new Map();
    for (const f of frames) for (const q of f[2]) { if (!born.has(q[0])) born.set(q[0], f[0]); if (f[0] - born.get(q[0]) < 2 && q[5] && q[5] !== 'I') q[5] = 'I'; }
    // A gun truck's gunner reports "Evade" for as long as he lives (plugin 0.17.0 recorded that). He is where the truck is.
    const trucks = (Array.isArray(obj.events) ? obj.events : []).filter(e => Array.isArray(e) && /gun truck/.test(String(e[2])) && Array.isArray(e[3])).map(e => e[3]);
    if (trucks.length) {
      const crew = new Set(), seen = new Set();
      for (const f of frames) for (const q of f[2]) { if (seen.has(q[0])) continue; seen.add(q[0]); if (trucks.some(t => Math.hypot(t[0] - q[1], t[2] - q[3]) < 4)) crew.add(q[0]); }
      if (crew.size) for (const f of frames) for (const q of f[2]) if (crew.has(q[0]) && q[5] !== 'I') q[5] = 'I';
    }
  }
  const text = k => (typeof obj[k] === 'string' ? obj[k] : '');
  replay.data = { players: Array.isArray(obj.players) ? obj.players : [], frames, events: (Array.isArray(obj.events) ? obj.events : []).filter(e => Array.isArray(e) && e.length >= 3 && typeof e[0] === 'number'),
    semantics, linked, legacy: semantics < 2, plugin: text('plugin'), run: text('run'), session: text('session'), scenario: text('scenario'), scenarioHash: text('scenarioHash'),
    start: frames[0][0], end: frames[frames.length - 1][0], scene: obj.scene || '', navFile: obj.navFile || '' };
  replay.t = replay.data.start; replay.playing = false; replay.name = name;
  if (state.mode === '3d') setMode('2d');
  $('tabReplay').hidden = false; showTab('replay');
  syncReplay(); syncDebrief(); invalidate();
  return 'Replay of ' + (replay.data.scene || 'a mission') + ': ' + mmss(replay.data.end - replay.data.start) + ' long, ' + replay.data.events.length + ' events.' +
    (linked ? '' : ' An older recording: it links enemies by a guess.') + ' Press Play.';
}
// The mission in a few numbers, worked out from the recording. A row with a time can be jumped to.
function replayStats(d) {
  const fr = d.frames, rows = [], alert = q => !!q[5] && q[5] !== 'I';
  // Who each row id was, and how to call an enemy: by who it is when the recording says so, by its row id otherwise.
  const who = new Map();
  for (const f of fr) for (const q of f[2]) if (!who.get(q[0]) && whoOf(q)) who.set(q[0], whoOf(q));
  const call = id => (whoName(who.get(id)) || 'enemy ' + id);
  rows.push(['Length', mmss(d.end - d.start)]);
  let first = null, contact = 0, most = 0; const seen = new Set();
  fr.forEach((f, i) => {
    most = Math.max(most, f[2].length); for (const q of f[2]) seen.add(q[0]);
    const one = f[2].find(alert);
    if (one && !first) { let near = Infinity; for (const p of f[1]) near = Math.min(near, Math.hypot(p[1] - one[1], p[2] - one[2], p[3] - one[3])); first = { t: f[0], id: one[0], dist: near, at: [one[1], one[2], one[3]] }; }
    if (one && i > 0) contact += f[0] - fr[i - 1][0];
  });
  rows.push(['Players first noticed', first ? mmss(first.t) + ', by ' + call(first.id) + (isFinite(first.dist) ? ' from ' + Math.round(first.dist) + ' m' : '') : 'never', first ? first.t : null, first ? first.at : null]);
  rows.push(['Time with the enemy alerted', mmss(contact)]);
  const kind = k => d.events.filter(e => e[1] === k), downs = kind('down'), minutes = Math.max(1 / 60, (d.end - d.start) / 60);
  // An enemy made again after an edit is the same enemy: the writer links it to the one taken out, and so does this count.
  let again = 0;
  if (d.linked) {
    const waiting = new Map();
    for (const e of d.events) {
      const w = typeof e[4] === 'number' ? who.get(e[4]) : '';
      if (!w) continue;
      if (e[1] === 'remade') waiting.set(w, (waiting.get(w) || 0) + 1);
      else if (e[1] === 'appears' && waiting.get(w)) { waiting.set(w, waiting.get(w) - 1); again++; }
    }
  }
  rows.push(['Enemies seen in all', String(seen.size - again)]);
  rows.push(['Most at one time', String(most)]);
  rows.push(['Enemies down', downs.length + (downs.length ? ' (' + (downs.length / minutes).toFixed(1) + ' a minute)' : '')]);
  if (downs.length) rows.push(['First enemy down', mmss(downs[0][0]) + (d.linked && typeof downs[0][4] === 'number' ? ', ' + call(downs[0][4]) : ''), downs[0][0], downs[0][3]]);
  const taken = kind('removed'), remade = kind('remade'), gone = kind('gone'), asked = kind('command'), ended = kind('end');
  if (d.linked) {
    if (taken.length) rows.push(['Taken out by an edit', String(taken.length)]);
    if (remade.length) rows.push(['Made again after an edit', String(remade.length)]);
    if (gone.length) rows.push(['Gone for a reason not known', String(gone.length), gone[0][0], gone[0][3]]);
  } else if (taken.length) rows.push(['Taken out or made again by an edit', String(taken.length)]);
  if (asked.length) rows.push(['Game Master requests', String(asked.length)]);
  const turns = kind('goal');      // objective transitions (ML-046), each told in the recording's own words in the list below
  if (turns.length) rows.push(['Objective changes', String(turns.length) + ', the first at ' + mmss(turns[0][0]), turns[0][0], null]);
  if (ended.length) rows.push(['Recording ended', mmss(ended[ended.length - 1][0]) + ', ' + ended[ended.length - 1][2], ended[ended.length - 1][0], null]);
  if (!d.linked) rows.push(['Older recording' + (d.plugin ? ' (' + d.plugin + ')' : ''), 'enemies are linked by a guess, and every enemy that left counts as down' + (d.legacy ? ', even one an edit took out' : '')]);
  if (d.scenario) rows.push(['Scenario', d.scenario + (d.scenarioHash ? ' (' + d.scenarioHash.replace(/^[a-z0-9]+:/, '').slice(0, 8) + ')' : '')]);
  if (d.run) rows.push(['Run record', d.run]);
  if (d.session) rows.push(['Session', d.session]);
  d.players.forEach((name, n) => {
    let dist = 0, last = null, lastT = null;
    for (const f of fr) { const p = f[1].find(q => q[0] === n); if (!p) continue; if (last) dist += Math.hypot(p[1] - last[1], p[3] - last[3]); last = p; lastT = f[0]; }
    if (lastT === null) return;
    rows.push([name + ' walked', Math.round(dist) + ' m']);
    if (lastT < d.end - 3) rows.push([name + ' last seen', mmss(lastT), lastT, [last[1], last[2], last[3]]]);
  });
  return rows;
}
function closeReplay() { replay.data = null; replay.debrief = null; replay.playing = false; $('tabReplay').hidden = true; if ($('side').dataset.show === 'replay') showTab('map'); syncReplay(); syncDebrief(); invalidate(); }
// The recorded moment at or before a time, the one after it, and how far along we are between them.
function replayFrame(t) {
  const fr = replay.data.frames; let lo = 0, hi = fr.length - 1;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (fr[mid][0] <= t) lo = mid; else hi = mid - 1; }
  const a = fr[lo], b = fr[Math.min(fr.length - 1, lo + 1)], span = b[0] - a[0];
  return { i: lo, a, b, k: span > 0 ? Math.max(0, Math.min(1, (t - a[0]) / span)) : 0 };
}
// Everybody at the current time, moved smoothly between two recorded moments.
function replayNow() {
  const { a, b, k } = replayFrame(replay.t);
  const mix = (list, next) => list.map(p => {
    const q = next.find(n => n[0] === p[0]);
    return { id: p[0], pos: q ? [p[1] + (q[1] - p[1]) * k, p[2] + (q[2] - p[2]) * k, p[3] + (q[3] - p[3]) * k] : [p[1], p[2], p[3]], yaw: p[4], state: p[5], who: whoOf(p) };
  });
  return { players: mix(a[1], b[1]).map(p => Object.assign(p, { name: replay.data.players[p.id] || 'Player' })), enemies: mix(a[2], b[2]) };
}
// " in Rear stairs" for a recorded position, when the recording was made on the open scenario's map file; else ''.
function replayPlace(at) {
  const d = replay.data;
  if (!d || !Array.isArray(at) || (d.navFile && state.navFile && d.navFile.toLowerCase() !== state.navFile.toLowerCase())) return '';
  return placeWords(at);
}
// An event as the list shows it: the recorded text, with a label before it for kinds that read alike otherwise.
const REPLAY_KINDS = { goal: 'Objective · ', pressure: 'Pressure director · ' };
const replayEventText = ev => (Object.prototype.hasOwnProperty.call(REPLAY_KINDS, ev[1]) ? REPLAY_KINDS[ev[1]] : '') + ev[2];
function syncReplay() {
  const d = replay.data; $('replayBox').hidden = !d; if (!d) return;
  $('rpName').textContent = replay.name + (d.scene ? ' · ' + d.scene : '');
  const tr = $('rpTime'); tr.min = d.start; tr.max = d.end; if (document.activeElement !== tr) tr.value = replay.t;
  $('rpPlay').textContent = replay.playing ? 'Pause' : 'Play';
  const now = replayNow();
  $('rpClock').textContent = mmss(replay.t) + ' of ' + mmss(d.end) + ' · ' + now.players.length + (now.players.length === 1 ? ' player, ' : ' players, ') + now.enemies.length + (now.enemies.length === 1 ? ' enemy' : ' enemies')
    + now.players.map(p => { const w = replayPlace(p.pos); return w ? ' · ' + p.name + w : ''; }).join('');
  const ul = $('rpEvents'), key = replay.name + '/' + d.events.length + '/' + state.places.map(p => p.id + ':' + p.name + ':' + p.pos.join(',')).join('|');
  if (ul.dataset.key !== key) {
    const jump = (t, at) => { replay.t = Math.max(d.start, Math.min(d.end, t - 2)); if (Array.isArray(at)) { state.view.cx = at[0]; state.view.cz = at[2]; } syncReplay(); invalidate(); };
    const st = $('rpStats'); st.textContent = '';
    for (const [label, value, t, at] of replayStats(d)) {
      const li = document.createElement('li'), a = document.createElement('span'); a.textContent = label; li.appendChild(a);
      if (typeof t === 'number') { const b = document.createElement('button'); b.type = 'button'; b.textContent = value + replayPlace(at); b.title = 'Go to this moment'; b.addEventListener('click', () => jump(t, at)); li.appendChild(b); }
      else { const b = document.createElement('b'); b.textContent = value; li.appendChild(b); }
      st.appendChild(li);
    }
    ul.dataset.key = key; ul.textContent = '';
    d.events.forEach(ev => {
      const li = document.createElement('li'), b = document.createElement('button');
      b.type = 'button'; b.textContent = mmss(ev[0]) + '  ' + replayEventText(ev) + replayPlace(ev[3]);
      b.addEventListener('click', () => { replay.t = Math.max(d.start, Math.min(d.end, ev[0] - 2)); if (Array.isArray(ev[3])) { state.view.cx = ev[3][0]; state.view.cz = ev[3][2]; } syncReplay(); invalidate(); });
      li.appendChild(b); ul.appendChild(li);
    });
  }
}
function replayTick(now) {
  if (!replay.playing || !replay.data) return;
  const dt = Math.min(0.1, (now - replay.last) / 1000); replay.last = now;
  replay.t += dt * replay.speed;
  if (replay.t >= replay.data.end) { replay.t = replay.data.end; replay.playing = false; }
  syncReplay(); invalidate();
  if (replay.playing) requestAnimationFrame(replayTick);
}
$('rpPlay').addEventListener('click', () => {
  if (!replay.data) return;
  if (replay.t >= replay.data.end) replay.t = replay.data.start;
  replay.playing = !replay.playing; replay.last = performance.now(); syncReplay();
  if (replay.playing) requestAnimationFrame(replayTick);
});
$('rpSpeed').addEventListener('change', ev => { replay.speed = +ev.target.value || 1; });
$('rpTime').addEventListener('input', ev => { replay.t = +ev.target.value; syncReplay(); invalidate(); });
$('rpClose').addEventListener('click', closeReplay);
// Connected to the game: its recordings are listed, and one opens with its map even after the mission has ended.
async function listReplays() {
  try {
    const list = await (await fetch('/api/replays', { cache: 'no-store' })).json(), sel = $('rpList');
    if (!Array.isArray(list) || document.activeElement === sel) return;
    const key = list.map(r => r.name).join('|');
    if (sel.dataset.key !== key) {
      sel.dataset.key = key; sel.textContent = '';
      list.forEach(r => { const o = document.createElement('option'); o.value = r.name; o.textContent = r.name.replace(/\.json$/, '').replace(/^(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})\d{2}_/, '$3-$2 $4:$5  ').replace(/_/g, ' '); sel.appendChild(o); });
    }
    $('replayPick').hidden = !list.length;
  } catch (e) { /* an older game side has no recordings to list */ }
}
$('rpOpen').addEventListener('click', async () => {
  const name = $('rpList').value; if (!name) return;
  try {
    const obj = await (await fetch('/api/replay/' + encodeURIComponent(name), { cache: 'no-store' })).json();
    if (obj.navFile && (!state.map || state.navFile !== obj.navFile)) {
      const r = await fetch('/api/mapfile/' + encodeURIComponent(obj.navFile), { cache: 'no-store' });
      if (r.ok) { state.map = makeMap(await readMapFile(await r.arrayBuffer()), obj.navFile, false); state.navFile = obj.navFile; state.ceiling = Infinity; live.nav = ''; view3d.dropMap(); fit(); }
    }
    say(loadReplay(obj, name)); syncSide(); invalidate();
  } catch (e) { say('The replay could not be opened. ' + (e && e.message ? e.message : '')); }
});

async function pollLive() {
  try { await applyLive(await api('/api/state')); }
  catch (e) { $('liveStatus').textContent = 'The game is not answering. Is it still running?'; }
  setTimeout(pollLive, 600);
}
function clearLive() {
  if (replay.data || catalogue.lobbyFile) return;  // a replay is being watched, or a scenario was opened between missions: it stays
  if (!state.map && !state.enemies.length) return;
  state.map = null; state.geo = null; state.enemies = []; state.selected = -1; state.doors = []; state.door = -1; live.rev = null; live.nav = ''; live.hasGeo = false; live.players = []; live.enemies = [];
  view3d.dropMap(); view3d.dropGeo(); live.applying = true; changed(); live.applying = false;
}
function syncChoices(s) {
  const sel = $('nextMission');
  if (!s || !Array.isArray(s.choices) || document.activeElement === sel) return;
  const key = JSON.stringify(s.choices);
  if (sel.dataset.key !== key) { sel.dataset.key = key; sel.textContent = ''; s.choices.forEach(c => { const o = document.createElement('option'); o.value = c.value; o.textContent = c.label; sel.appendChild(o); }); }
  sel.value = s.choice;
}
$('nextMission').addEventListener('change', ev => api('/api/choose', { value: ev.target.value }).then(s => { syncChoices(s); say('The next mission you host uses: ' + ev.target.selectedOptions[0].textContent + '.'); }).catch(() => say('The game did not answer.')));
async function applyLive(s) {
  syncChoices(s);
  if (s && s.page && PAGE_STAMP.indexOf('ML_PAGE_STAMP') < 0 && s.page !== PAGE_STAMP) {
    let tried = ''; try { tried = sessionStorage.getItem('ml-reloaded') || ''; } catch (e) { /* no storage */ }
    if (tried !== s.page) { try { sessionStorage.setItem('ml-reloaded', s.page); } catch (e) { /* no storage */ } location.reload(); return; }
  }
  if ($('gmBox')) $('gmBox').hidden = !(live.on && s && s.mission);
  live.session = s && typeof s.session === 'string' ? s.session : null;
  live.trig = new Map(((s && s.triggerState) || []).map(t => [t.id, t])); live.wave = (s && s.respawn) || null;
  syncStanding(s);
  syncRuns(s);
  syncReady(s);
  live.mission = !!(s && s.mission); live.things = s && Array.isArray(s.things) ? s.things : []; live.tried = (s && s.tried) || '';
  syncTry();
  syncModes(s);
  if (!s || !s.mission) { $('liveStatus').textContent = s && s.starting ? 'The mission is starting...' : 'Waiting for a mission. Host one in the game.'; clearLive(); return; }
  if (s.navFile && s.navFile !== live.nav) {                 // a new mission: fetch its map from the game folder
    live.nav = s.navFile; live.hasGeo = false; live.rev = null;
    const buf = await (await fetch('/api/map', { cache: 'no-store' })).arrayBuffer();
    state.map = makeMap(await readMapFile(buf), s.navFile, false); state.navFile = s.navFile; state.geo = null; state.ceiling = Infinity;
    view3d.dropGeo(); view3d.dropMap(); fit();
  }
  if (s.hasGeo && !live.hasGeo) {
    live.hasGeo = true;
    const r = await fetch('/api/geo', { cache: 'no-store' });
    if (r.ok) { loadGeo(await r.arrayBuffer(), live.nav); view3d.dropMap(); }
  }
  live.players = s.players || []; live.enemies = s.enemies || []; live.types = s.types || []; live.vehicleTypes = Array.isArray(s.vehicleTypes) ? s.vehicleTypes : [];
  const mine = s.markers || [];
  if (s.rev !== live.rev && !live.sending && !live.pending && !drag) {
    live.rev = s.rev;
    const selId = state.enemies[state.selected] ? state.enemies[state.selected].id : 0, selIx = state.selected;
    state.zones = zonesIn(s.triggers); state.fired = Array.isArray(s.fired) ? s.fired : []; state.objectives = objectivesIn(s.objectives);
    settingsIn({ extraction: s.extraction, insertion: s.insertion, weather: s.weather, power: s.power, briefing: s.briefing, skill: s.skill, nvg: s.nvg, respawn: s.respawn, gameMaster: s.gameMaster });
    goalsFromGame(s);           // the running plan's objectives (ML-046, 29o-goals.js)
    if (typeof s.night === 'boolean') state.night = s.night;
    if (Array.isArray(s.insertionSpots)) state.insertionSpots = s.insertionSpots.filter(q => q && q.name && isVec(q.pos));
    live.satOut = s.satOut | 0;
    if (Array.isArray(s.infilPoints) && s.infilPoints.length) state.infils = s.infilPoints.filter(isVec).map(p => p.slice(0, 3));     // where players arrive: for the danger check
    state.insertions = Array.isArray(s.insertions) ? s.insertions.filter(n => typeof n === 'string' && n) : []; state.weatherNow = s.weather || null;
    const doorWas = state.doors[state.door] ? state.doors[state.door].pos : null;
    state.doors = doorsIn(s.doors); state.door = doorWas ? nearDoor(doorWas, state.doors) : -1;
    if (Array.isArray(s.thingMarks)) { state.things = thingsIn(s.thingMarks); if (state.thing >= state.things.length) state.thing = -1; }
    state.enemies = mine.map((m, i) => Object.assign(enemyOf(m, i), { id: m.id, live: !!m.live, waiting: !!m.waiting }));
    const again = selId ? state.enemies.findIndex(e => e.id === selId) : -1;
    state.selected = again >= 0 ? again : (selIx >= 0 && selIx < state.enemies.length && !selId ? selIx : -1);
    live.applying = true; changed(); live.applying = false;
  }
  const waiting = mine.filter(m => m.waiting).length;
  // A sealed mission, not revealed (20b-sealed.js), names no enemy count: knowing how many are left is a tactical spoiler.
  const enemyWords = sealedHides() ? 'enemies sealed' : live.enemies.length + ' enemies alive' + (waiting ? ', ' + waiting + ' waiting' : '');
  $('liveStatus').textContent = (s.scene || 'Mission') + ': ' + live.players.length + (live.players.length === 1 ? ' player, ' : ' players, ') + enemyWords + (s.busy ? ' (spawning...)' : '');
  $('liveSpawn').disabled = !waiting; $('liveExport').hidden = false; $('liveExport').textContent = s.hasGeo ? 'Make the 3D map again' : 'Make the 3D map';
  if (s.message && s.message !== live.lastMessage) { live.lastMessage = s.message; say(s.message.replace(/^MissionLab( GM)?: /, '')); }
  view3d.syncLive(); invalidate();
}
// Send the whole enemy list; the game works out what was added, changed or removed.
function pushLive() {
  live.pending = true; clearTimeout(live.pushTimer);
  live.pushTimer = setTimeout(async () => {
    live.sending = true; live.pending = false;
    try { const s = await api('/api/markers', { spawnNow: $('spawnNow').checked, triggers: zonesOut(), objectives: objectivesOut(), doors: doorsOut(), things: thingsOut(), settings: settingsOut(), markers: state.enemies.map(e => Object.assign({ id: e.id || 0 }, spawnOf(e))) }); live.sending = false; live.rev = null; await applyLive(s); }
    catch (e) { live.sending = false; say(liveTrouble(e)); }
  }, 150);
}
const liveCommand = (cmd, more) => api('/api/command', Object.assign({ cmd }, more || {})).then(s => { live.rev = null; return applyLive(s); }).catch(e => say(liveTrouble(e)));
$('liveSpawn').addEventListener('click', () => liveCommand('spawnWaiting'));
$('liveClear').addEventListener('click', () => liveCommand('removeAllEnemies'));
$('liveExport').addEventListener('click', async () => { say('Making the 3D map. The game freezes for a few seconds...'); await liveCommand('export'); live.hasGeo = false; });

// A reload should not lose work. The map itself is not kept; it has to be opened again.
let draftTimer = 0;
function saveDraft() {
  clearTimeout(draftTimer);
  draftTimer = setTimeout(() => { try { if (state.example) localStorage.removeItem(DRAFT_KEY); else localStorage.setItem(DRAFT_KEY, JSON.stringify(Object.assign(scenarioObject(), { name: state.title }))); } catch (e) { /* storage unavailable */ } }, 400);
}
function restoreDraft() {
  try {
    const raw = localStorage.getItem(DRAFT_KEY); if (!raw) return false;
    const o = JSON.parse(raw); if (!o || !Array.isArray(o.spawns) || !o.spawns.length) return false;
    state.map = null; state.infils = []; state.authored = []; state.doors = state.doors.filter(d => !d.known); state.door = -1;
    loadScenario(o, o.name || 'draft'); state.scene = typeof o.scene === 'string' ? o.scene : '';     // loadScenario starts the undo history afresh
    say('Your last scenario is back (' + o.spawns.length + ' enemies). Open ' + (state.navFile || 'its map file') + ' to see the ground.');
    return true;
  } catch (e) { return false; }
}
expose({ replayStats, replayEventText });   // goal events in a recording (ML-046), for automated tests

