// A stand-in for the browser link (src/MissionLab.Director/LiveLink.cs) for tests/browser/run.mjs. It serves the editor
// page and its 3D library the way the plugin does, and answers the API the page talks to in live mode with the same
// rules: the Origin check, X-MissionLab on every change, X-MissionLab-Session (409 stale for another mission),
// X-MissionLab-Command (the same number is carried out once; a repeat gets the first answer), 202 while the game is still
// working on a change, and the status and state shapes the page reads.
//
// It never makes up a result. What "the game" does with each change is scripted by the test (game.script, game.finish,
// game.unreachable); a change nobody scripted is answered as the plugin answers one the game never took: expired, not done.
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';

// An enemy as the page writes it (spawnOf in 22-scenario.js), in the same key order.
export const spawn = (pos, role, group) => ({ group, role, pos, yaw: 0, indoor: false, defensive: false, detectionRange: 0, hearingRange: 0, fov: 0,
  wanderDistance: 0, patrolWaitSeconds: 4, fixed: false, prone: false, type: '', trigger: 0, hvt: false, patrol: [] });

// The ledger's own rule for a command number (CommandLedger.Clean): anything else counts as no number.
const clean = id => (typeof id === 'string' && id.length <= 64 && /^[A-Za-z0-9_-]*$/.test(id) ? id : '');
const sameSpawn = (a, b) => { const key = o => JSON.stringify(Object.keys(o).sort().map(k => [k, o[k]])); return key(a) === key(b); };
// The whole change in a word (Outcome.Overall).
function overall(ops, none) {
  const n = ops.length, applied = ops.filter(o => o.status === 'applied').length, failed = ops.filter(o => o.status === 'failed').length, pending = ops.filter(o => o.status === 'pending').length;
  return !n ? none : applied === n ? 'applied' : failed === n ? 'failed' : failed > 0 ? 'partial' : pending > 0 ? 'pending' : 'unconfirmed';
}

export class Game {
  constructor(version, testedGame, stamp, missions) {
    this.version = version; this.testedGame = testedGame; this.stamp = stamp; this.port = 0;
    this.missions = missions.slice();          // [{ session, markers }]: the first runs now, the next when the script says so
    this.ledger = new Map(); this.order = [];   // command number -> entry, oldest first
    this.log = [];                              // every change and every command lookup the page sent, in order
    this.script = [];                           // what the game does with the next changes: 'apply', 'hold', 'newMission'
    this.unreachable = false;                   // changes are lost on the way: the connection drops before the game sees them
    this.held = new Map();                      // command -> body of a change the game has begun and not finished
    this.applied = new Map();                   // command -> how many times the game carried it out
    this.startMission();
  }
  startMission() {
    const m = this.missions.shift();
    this.session = m.session; this.markers = JSON.parse(JSON.stringify(m.markers)); this.rev = 1;
    this.nextId = this.markers.reduce((n, k) => Math.max(n, k.id + 1), 1);
    // A mission ended: what was still on its way into it never comes (LiveLink.Pump, Ledger.Abandon).
    for (const e of this.ledger.values()) if (e.result) for (const o of e.result.ops) if (o.status === 'pending') { o.status = 'failed'; o.said = 'the mission ended before the game made it'; }
  }
  entryJson(e, parts) {
    return Object.assign({ command: e.command, status: e.status, path: e.path, session: e.session, done: e.done },
      parts === false ? {} : { ops: e.result ? e.result.ops : [], left: 0 }, { said: e.said });
  }
  begin(command, session, where) {
    const e = { command, session: session || '', path: where, status: 'pending', code: 202, body: '', done: false, result: null, said: '' };
    this.ledger.set(command, e); this.order.push(command);
    return e;
  }
  finishEntry(e, status, code, body) { e.status = e.result && status === 'applied' ? overall(e.result.ops, 'applied') : status; e.code = code; e.body = body; e.done = true; }
  standing() {
    return { plugin: this.version, repeat: '', run: '', session: this.session, rejected: '', startError: '', health: [],
      commands: this.order.slice(-12).map(c => this.entryJson(this.ledger.get(c), false)) };
  }
  status() {
    return Object.assign({ mission: true, role: 'host', game: this.testedGame, testedGame: this.testedGame, mode: 'StandardPVE', pve: true, map: '', compat: '', port: this.port, library: true }, this.standing());
  }
  // GameMaster.StateJson for a running mission without a map file: markers, the live enemies standing on them, nothing else.
  state() {
    return Object.assign({ mission: true, page: this.stamp, choice: '', choices: [] }, this.standing(), {
      scene: 'Synthetic', navFile: null, hasGeo: false, targetMission: false, busy: false, message: '', players: [],
      enemies: this.markers.filter(m => m.live).map(m => ({ pos: m.pos, yaw: m.yaw, state: 'Idle', hvt: false, marker: m.id })),
      markers: this.markers, objectives: [], doors: [], triggers: [], fired: [], triggerState: [], triggerProblems: [], types: [], infilPoints: [],
      things: [], thingMarks: [], tried: '', vehicleTypes: [], rev: 'r' + this.rev + '-' + this.markers.length,
    });
  }
  // The game carries a batch out (GameMaster.Reconcile): each marker added, changed or removed is one part, applied.
  carryOut(command, body) {
    const incoming = Array.isArray(body.markers) ? body.markers : [], ops = [], kept = new Set(), next = [];
    incoming.forEach((m, at) => {
      const s = Object.assign({}, m); delete s.id; delete s.live;
      const was = m.id > 0 ? this.markers.find(k => k.id === m.id) : null;
      if (was) {
        kept.add(was.id);
        const old = Object.assign({}, was); delete old.id; delete old.live; delete old.waiting;
        if (!sameSpawn(old, s)) ops.push({ op: 'change', ref: String(was.id), at, status: 'applied', said: '' });
        next.push(Object.assign({ id: was.id, live: was.live, waiting: was.waiting }, s));
      } else {
        const id = this.nextId++;
        ops.push({ op: 'add', ref: String(id), at, status: 'applied', said: '' });
        next.push(Object.assign({ id, live: false, waiting: false }, s));
      }
    });
    for (const m of this.markers) if (!kept.has(m.id)) ops.push({ op: 'remove', ref: String(m.id), at: -1, status: 'applied', said: '' });
    this.markers = next; this.rev++;
    this.applied.set(command, (this.applied.get(command) || 0) + 1);
    const result = { command, status: overall(ops, 'applied'), ops, left: 0 };
    const e = this.ledger.get(command);
    if (e) e.result = result;
    return JSON.stringify(Object.assign(this.state(), { result }));
  }
  // The game finishes a change it had begun ('hold'): carried out now, and its number answers with the result from here on.
  finish(command) {
    const body = this.held.get(command), e = this.ledger.get(command);
    if (!body || !e) throw new Error('the game holds no change numbered ' + command);
    this.held.delete(command);
    const text = this.carryOut(command, body);
    this.finishEntry(e, 'applied', 200, text);
  }
  posts(command) { return this.log.filter(l => l.kind === 'change' && (command === undefined || l.command === command)); }
  lookups(command) { return this.log.filter(l => l.kind === 'lookup' && l.command === command); }
}

// Serves root/tools/editor as the plugin does and answers its API from `game`.
export async function startFixture(root, missions) {
  const editor = path.join(root, 'tools', 'editor');
  const pageText = fs.readFileSync(path.join(editor, 'index.html'), 'utf8');
  const version = (pageText.match(/const EDITOR_VERSION = '([^']+)'/) || [])[1] || '';
  const tested = (fs.readFileSync(path.join(root, 'src', 'MissionLab.Director', 'LiveLink.cs'), 'utf8').match(/TestedGame = "([^"]+)"/) || [])[1] || '';
  const stamp = 'fixture-' + Date.now().toString(36);
  const page = Buffer.from(pageText.split('__ML_PAGE_STAMP__').join(stamp), 'utf8');   // the page learns which copy it is, as LiveLink does
  const lib = fs.readFileSync(path.join(editor, 'three.min.js'));
  const game = new Game(version, tested, stamp, missions);

  const answer = (res, status, body, type) => {
    const bytes = Buffer.isBuffer(body) ? body : Buffer.from(typeof body === 'string' ? body : JSON.stringify(body), 'utf8');
    res.writeHead(status, { 'Content-Type': type || 'application/json; charset=utf-8', 'Content-Length': bytes.length, 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff', Connection: 'close' });
    res.end(bytes);
  };
  const error = (res, status, message) => answer(res, status, { error: message });

  const server = http.createServer((req, res) => {
    const chunks = [];
    req.on('data', c => chunks.push(c));
    req.on('end', () => {
      try { handle(req, res, Buffer.concat(chunks).toString('utf8')); }
      catch (e) { error(res, 500, 'the fixture failed: ' + e.message); }
    });
  });
  function handle(req, res, text) {
    const url = new URL(req.url, 'http://127.0.0.1'), where = url.pathname, h = req.headers;
    // Only the page this server hands out may talk to it.
    if (h.origin !== undefined && h.origin !== 'http://localhost:' + game.port && h.origin !== 'http://127.0.0.1:' + game.port) return error(res, 403, 'this address only answers its own page');
    if (where === '/three.min.js') return answer(res, 200, lib, 'application/javascript');
    if (where === '/' || where === '/index.html') return answer(res, 200, page, 'text/html; charset=utf-8');
    if (!where.startsWith('/api/')) return error(res, 404, 'not found');
    const change = req.method !== 'GET';
    if (change && h['x-missionlab'] === undefined) return error(res, 403, 'not from the editor page');
    if (where === '/api/status') return answer(res, 200, game.status());
    if (where === '/api/state' && !change) return answer(res, 200, game.state());
    if (where === '/api/replays' && !change) return answer(res, 200, []);
    if (where.startsWith('/api/command/') && !change) {
      const asked = clean(decodeURIComponent(where.slice('/api/command/'.length))), e = asked ? game.ledger.get(asked) : null;
      game.log.push({ kind: 'lookup', command: asked, known: !!e });
      return e ? answer(res, 200, game.entryJson(e)) : answer(res, 404, { command: asked, status: 'unknown' });
    }
    if (where !== '/api/markers' || !change) return error(res, 404, 'not found');        // not part of these checks

    const session = typeof h['x-missionlab-session'] === 'string' ? h['x-missionlab-session'] : null;
    const command = clean(h['x-missionlab-command'] || '');
    const seen = { kind: 'change', path: where, command, session, marked: h['x-missionlab'] === '1', origin: h.origin || '', outcome: '' };
    game.log.push(seen);
    if (game.unreachable) { seen.outcome = 'lost'; req.socket.destroy(); return; }
    // The same number twice is carried out once: the second time gets what the first one got.
    if (command && game.ledger.has(command)) {
      const e = game.ledger.get(command);
      seen.outcome = 'repeat';
      return e.done ? answer(res, e.code, e.body) : answer(res, 202, game.entryJson(e));
    }
    let body;
    try { body = JSON.parse(text); } catch (e) { return error(res, 400, 'not JSON'); }
    const e = command ? game.begin(command, session, where) : null;
    const step = game.script.shift() || 'expire';
    seen.step = step;
    if (step === 'newMission') game.startMission();       // the mission ended and the next began just before the game took this
    if (session !== null && session !== game.session) {
      // Made for another mission: nothing is done.
      seen.outcome = 'stale';
      const stale = JSON.stringify({ status: 'stale', command, session: game.session, error: 'this was meant for another mission; nothing was done' });
      if (e) game.finishEntry(e, 'stale', 409, stale);
      return answer(res, 409, stale);
    }
    if (step === 'apply') {
      seen.outcome = 'applied';
      const out = game.carryOut(command, body);
      if (e) game.finishEntry(e, 'applied', 200, out);
      return answer(res, 200, out);
    }
    if (step === 'hold') {
      // The game has begun it and is not done: neither carried out for certain nor cancelled.
      seen.outcome = 'pending';
      game.held.set(command, body);
      return answer(res, 202, { status: 'pending', command, error: 'the game has begun this and has not finished; ask for its result, do not send it again' });
    }
    seen.outcome = 'expired';
    const late = JSON.stringify({ status: 'expired', command, error: 'the game did not take this in time; it was not carried out and will not run later' });
    if (e) game.finishEntry(e, 'expired', 503, late);
    return answer(res, 503, late);
  }

  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  game.port = server.address().port;
  return {
    game, port: game.port, url: 'http://127.0.0.1:' + game.port + '/',
    close: () => new Promise(done => { if (server.closeAllConnections) server.closeAllConnections(); server.close(() => done()); }),
  };
}
