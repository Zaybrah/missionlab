// ------------------------------------------------------------------ live link to the running game
// When this page is served by the mod (http://localhost:8765), it shows the mission you are hosting:
// players and enemies where they are right now. Every change you make here is carried out in the game.
const PAGE_STAMP = '__ML_PAGE_STAMP__';        // filled in by the game when it serves this page
const live = { vehicleTypes: [], on: false, rev: null, nav: '', hasGeo: false, sending: false, pending: false, applying: false, players: [], enemies: [], types: [], pushTimer: 0, status: null, statusAt: 0, runsFor: null, session: null, trig: new Map(), wave: null };
// Reading the game's state is a plain request. A change carries a number of its own and the mission it was made for:
// the game carries a number out once, and refuses a change that was meant for another mission.
const newCommand = () => 'c' + Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
const api = async (path, body) => {
  if (body === undefined) return (await fetch(path, { cache: 'no-store' })).json();
  const command = newCommand(), headers = { 'Content-Type': 'application/json', 'X-MissionLab': '1', 'X-MissionLab-Command': command };
  if (typeof live.session === 'string') headers['X-MissionLab-Session'] = live.session;
  const r = await fetch(path, { method: 'POST', cache: 'no-store', headers, body: JSON.stringify(body) });
  let s = null; try { s = await r.json(); } catch (e) { /* no body */ }
  if (r.status === 200) return s;
  const err = new Error((s && s.error) || 'status ' + r.status); err.status = r.status; err.command = command; err.result = (s && s.status) || '';
  if (r.status === 202) {
    // The game has begun it. Ask what became of it; never send it again.
    for (let i = 0; i < 12 && err.result === 'pending'; i++) {
      await new Promise(done => setTimeout(done, 500));
      try { const k = await (await fetch('/api/command/' + command, { cache: 'no-store' })).json(); if (k && k.status && k.status !== 'unknown') { err.result = k.status; err.said = k.said || ''; } } catch (e) { /* ask again */ }
    }
    if (err.result === 'applied') return (await fetch('/api/state', { cache: 'no-store' })).json();
  }
  throw err;
};
// What to tell the host when a change did not go through. Each case says whether it was carried out.
const liveTrouble = err => {
  const st = err && err.status, res = err && err.result;
  if (st === 409 || res === 'stale') { live.rev = null; return 'That change was made for another mission. Nothing was done.'; }
  if (st === 429) return 'The game has too much waiting. That change was not carried out.';
  if (res === 'expired') return 'The game did not take that in time. It was not carried out, and it will not run later.';
  if (res === 'pending') return 'The game has begun that change and has not finished. It was not sent again; look at the map to see what happened.';
  if (res === 'failed' || st === 500) return 'The game could not do that: ' + ((err && err.message) || 'no reason given') + '.';
  return 'The game did not answer. Is it still running?';
};
function startLive() {
  live.on = true; document.body.classList.add('live');
  state.example = false; state.map = null; state.enemies = []; state.selected = -1; state.infils = []; state.authored = []; state.doors = state.doors.filter(d => !d.known); state.door = -1; undoStack.length = 0;
  $('save').hidden = false; $('save').textContent = 'Save in the game';
  listReplays(); setInterval(listReplays, 15000);
  pollLive();
}
