// ------------------------------------------------------------------ live link to the running game
// When this page is served by the mod (http://localhost:8765), it shows the mission you are hosting:
// players and enemies where they are right now. In Live mode every change you make here is carried out in the game at once;
// in Draft mode changes are staged and go to the game together with Apply changes (31a-stage.js).
const PAGE_STAMP = '__ML_PAGE_STAMP__';        // filled in by the game when it serves this page
const live = { vehicleTypes: [], on: false, rev: null, nav: '', hasGeo: false, sending: false, pending: false, applying: false, players: [], enemies: [], types: [], pushTimer: 0, status: null, statusAt: 0, runsFor: null, session: null, trig: new Map(), wave: null, ops: new Map(), opsSession: null, following: new Set(), staged: [] };
// Reading the game's state is a plain request. A change carries a number of its own and the mission it was made for:
// the game carries a number out once, and refuses a change that was meant for another mission.
const newCommand = () => 'c' + Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
// A retry passes the number of the first try, so the game answers it from what it already did and never does it twice.
const api = async (path, body, again) => {
  if (body === undefined) { const s = await (await fetch(path, { cache: 'no-store' })).json(); if (path === '/api/state') stageSeen(s); return s; }
  const command = again || newCommand(), headers = { 'Content-Type': 'application/json', 'X-MissionLab': '1', 'X-MissionLab-Command': command };
  if (typeof live.session === 'string') headers['X-MissionLab-Session'] = live.session;
  const r = await fetch(path, { method: 'POST', cache: 'no-store', headers, body: JSON.stringify(body) });
  let s = null; try { s = await r.json(); } catch (e) { /* no body */ }
  if (r.status === 200) { if (s && s.result) liveResult(s.result, s); return s; }
  const err = new Error((s && s.error) || 'status ' + r.status); err.status = r.status; err.command = command; err.result = (s && s.status) || '';
  if (r.status === 202) {
    // The game has begun it. Ask what became of it; never send it again.
    let k = null;
    for (let i = 0; i < 12 && !(k && k.done); i++) {
      await new Promise(done => setTimeout(done, 500));
      try { const got = await (await fetch('/api/command/' + command, { cache: 'no-store' })).json(); if (got && got.status && got.status !== 'unknown') { k = got; err.result = got.status; err.said = got.said || ''; } } catch (e) { /* ask again */ }
    }
    if (k && k.done && ['applied', 'partial', 'pending', 'unconfirmed'].indexOf(k.status) >= 0) { liveResult(k, null); return (await fetch('/api/state', { cache: 'no-store' })).json(); }
    if (k && k.done && k.status === 'failed') err.message = (Array.isArray(k.ops) && k.ops.length ? opsSummary(k) : k.said) || 'no reason given';
    if (!k || !k.done) err.result = 'pending';
  }
  throw err;
};
// What became of each part of the last change. The game answers part by part: applied (seen in the game), failed,
// pending (on its way, such as a queued spawn) or unconfirmed (requested: the call was made, the effect is not seen).
const OP_WORDS = { add: 'added', move: 'moved', change: 'changed', remove: 'removed', spawn: 'made', order: 'ordered', fire: 'set off', save: 'saved', export: 'exported', clear: 'cleared' };
const MARKER_OPS = ['add', 'move', 'change', 'remove', 'spawn'];
function opsSummary(res) {
  const ops = res && Array.isArray(res.ops) ? res.ops : [], done = new Map(), failed = [];
  let pending = 0, asked = 0;
  ops.forEach(o => {
    if (o.status === 'applied') done.set(o.op, (done.get(o.op) || 0) + 1);
    else if (o.status === 'failed') failed.push(o);
    else if (o.status === 'pending') pending++;
    else asked++;
  });
  const parts = [...done].map(([op, n]) => (OP_WORDS[op] || op) + ' ' + n);
  if (asked) parts.push(asked + ' requested, not yet seen in the game');
  if (pending) parts.push(pending + ' still on the way into the game');
  if (failed.length) parts.push(failed.length + ' failed: ' + (failed[0].said || 'no reason given') + (failed.length > 1 ? ' (and ' + (failed.length - 1) + ' more)' : ''));
  if (res && res.left) parts.push(res.left + ' more not listed');
  const text = parts.join(', ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}
// Markers whose last change failed or is still on its way, by marker id, for the list. Kept per mission and bounded.
function noteOps(ops) {
  if (live.opsSession !== live.session) { live.ops.clear(); live.opsSession = live.session; }
  ops.forEach(o => {
    if (!o || !o.ref || MARKER_OPS.indexOf(o.op) < 0) return;
    if (o.status === 'failed' || o.status === 'pending') live.ops.set(o.ref, o); else live.ops.delete(o.ref);
  });
  while (live.ops.size > 200) live.ops.delete(live.ops.keys().next().value);
}
function liveOp(e) { return live.on && e && e.id && live.opsSession === live.session ? live.ops.get(String(e.id)) || null : null; }
function liveResult(res, s) {
  if (!res || !Array.isArray(res.ops)) return;
  if (s && typeof s.session === 'string') live.session = s.session;
  noteOps(res.ops);
  if (res.status !== 'applied') live.rev = null;          // the page takes the game's marker list over: it follows the game, not the request
  if (res.ops.some(o => o.status === 'failed')) {
    if (s && s.message) live.lastMessage = s.message;    // the outcome part by part says more than the game's last line
    say(opsSummary(res) + '.');
  }
  if (res.command && res.ops.some(o => o.status === 'pending')) followCommand(res.command);
  syncSide();
}
// Spawns answer later: ask the game what became of them until none is on its way, then read the game's state again.
async function followCommand(command) {
  if (live.following.has(command) || live.following.size >= 8) return;
  live.following.add(command);
  try {
    let seen = '';
    for (let i = 0; i < 30; i++) {
      await new Promise(done => setTimeout(done, 800));
      let k = null;
      try { k = await (await fetch('/api/command/' + encodeURIComponent(command), { cache: 'no-store' })).json(); } catch (e) { continue; }
      if (!k || !Array.isArray(k.ops)) return;           // the game no longer knows it: nothing more to learn
      const now = k.ops.map(o => o.status).join();
      if (now !== seen) { seen = now; noteOps(k.ops); stageReceipt(command, k); live.rev = null; syncSide(); invalidate(); }   // something settled: take the game's state over again
      if (!k.ops.some(o => o.status === 'pending')) {
        if (k.ops.some(o => o.status === 'failed')) say(opsSummary(k) + '.');
        return;
      }
    }
  } finally { live.following.delete(command); }
}
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
