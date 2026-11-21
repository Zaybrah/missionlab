// The editor's checks, run without a browser and without the game: node tests/editor/run.mjs (Node 18 or later, no packages).
// The page is put together from tools/editor/src exactly as tools/editor/build.py does it, and its script runs with a
// stand-in for the browser. What it checks:
//   - every file in tests/fixtures/invalid is refused, with the same 'code path' list the plugin gives (tests/fixtures/expected.json);
//   - editor -> file -> plugin (C#) -> file -> editor keeps what the scenario does, for the fixtures and every example;
//   - text with quotes, backslashes, other scripts and control characters survives that trip;
//   - the editor's mission check finds the extraction that cannot always open (tests/fixtures/extraction_chance_defect.json).
// The plugin's side is "dotnet run --project tests/Pure -- --roundtrip <files>", called once. Without dotnet only that leg is skipped.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import vm from 'node:vm';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const FIXTURES = path.join(ROOT, 'tests', 'fixtures');
const SEPARATOR = '#missionlab-roundtrip ';    // tests/Pure/Program.cs starts each file with this line; no line of JSON can begin with '#'

let checks = 0, failures = 0;
function check(what, ok, more) {
  checks++;
  if (ok) return;
  failures++;
  console.log('  FAIL: ' + what + (more ? ' | ' + more : ''));
}

// ---------------------------------------------------------------- the page, as build.py makes it
function pageScript() {
  const src = path.join(ROOT, 'tools', 'editor', 'src');
  const names = fs.readdirSync(src).filter(n => n.endsWith('.html') || n.endsWith('.js')).sort();
  const page = Buffer.concat(names.map(n => fs.readFileSync(path.join(src, n)))).toString('utf8');
  // The page has two inline scripts: a one-line loader for the 3D library and the editor itself.
  const found = [...page.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g)].filter(m => m[1].includes('window.missionlabEditor'));
  if (found.length !== 1) throw new Error('expected one inline script with window.missionlabEditor in the joined page, found ' + found.length);
  const start = found[0].index + found[0][0].indexOf('>') + 1;
  // Line numbers in errors then point at tools/editor/index.html.
  return new vm.Script(found[0][1], { filename: 'tools/editor/index.html', lineOffset: page.slice(0, start).split('\n').length - 1 });
}

// A stand-in for anything of the browser the editor touches at start-up: every property is another stand-in, every call
// gives one, and values that are set are kept. It reads as 0 or '' and iterates as empty. 'then' is left out, so that
// awaiting one does not hang.
function stub() {
  const kept = new Map();
  return new Proxy(function () {}, {
    get(target, key) {
      if (kept.has(key)) return kept.get(key);
      if (key === Symbol.toPrimitive) return hint => (hint === 'number' ? 0 : '');
      if (key === Symbol.iterator) return function* () {};
      if (key === 'then' || typeof key === 'symbol') return undefined;
      if (key === 'length') return 0;
      const s = stub();
      kept.set(key, s);
      return s;
    },
    set(target, key, value) { kept.set(key, value); return true; },
    apply() { return stub(); },
    construct() { return stub(); },
  });
}

// A fresh page: its own window, document and storage, nothing remembered from an earlier load. No THREE, so 3D stays off.
function openEditor(script) {
  const elements = new Map(), store = new Map();
  const document = stub();
  document.getElementById = id => { if (!elements.has(id)) elements.set(id, stub()); return elements.get(id); };
  document.fonts = { ready: new Promise(() => {}) };     // fonts never finish loading here, so the page draws nothing again
  const later = fn => (...args) => { const t = fn(...args); if (t && typeof t.unref === 'function') t.unref(); return t; };
  const window = {
    document,
    console,
    localStorage: {
      getItem: k => (store.has(k) ? store.get(k) : null),
      setItem: (k, v) => { store.set(k, String(v)); },
      removeItem: k => { store.delete(k); },
      clear: () => store.clear(),
    },
    location: { protocol: 'file:', hostname: '', host: '', href: 'file:///missionlab/index.html', search: '', hash: '', pathname: '/missionlab/index.html' },
    navigator: stub(),
    history: stub(),
    fetch: () => Promise.reject(new Error('no network in the editor checks')),
    requestAnimationFrame: () => 0,
    cancelAnimationFrame: () => {},
    setTimeout: later(setTimeout), setInterval: later(setInterval), clearTimeout, clearInterval,
    queueMicrotask,
    matchMedia: () => stub(),
    getComputedStyle: () => stub(),
    MutationObserver: function () { return stub(); },
    ResizeObserver: function () { return stub(); },
    Image: function () { return stub(); },
    addEventListener: () => {}, removeEventListener: () => {},
    alert: () => {}, confirm: () => false, prompt: () => null,
    devicePixelRatio: 1, innerWidth: 1280, innerHeight: 800,
    TextDecoder, TextEncoder, URL, Blob,
    performance,
  };
  window.window = window;
  window.self = window;
  window.globalThis = window;
  const context = vm.createContext(window);
  script.runInContext(context);
  const api = context.missionlabEditor;
  if (!api) throw new Error('the page started but window.missionlabEditor is missing');
  return { api, banner: () => String(document.getElementById('banner').textContent || '') };
}

// A file as the page's file picker hands it over.
function fakeFile(name, text) {
  const bytes = new TextEncoder().encode(text);
  return { name, size: bytes.length, type: 'application/json', arrayBuffer: async () => bytes.slice().buffer, text: async () => text };
}

async function load(script, name, text) {
  const page = openEditor(script);
  await page.api.openFiles([fakeFile(name, text)]);
  return page;
}

// Where two written scenarios differ, as JSON paths; empty when they agree.
function differences(a, b, at, out) {
  if (out.length >= 12) return out;
  if (Array.isArray(a) && Array.isArray(b)) {
    if (a.length !== b.length) out.push(at + ': ' + a.length + ' items, then ' + b.length);
    for (let i = 0; i < Math.min(a.length, b.length); i++) differences(a[i], b[i], at + '[' + i + ']', out);
  } else if (a && b && typeof a === 'object' && typeof b === 'object' && !Array.isArray(a) && !Array.isArray(b)) {
    for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) {
      if (!(k in b)) out.push(at + '.' + k + ': lost (was ' + JSON.stringify(a[k]) + ')');
      else if (!(k in a)) out.push(at + '.' + k + ': appeared as ' + JSON.stringify(b[k]));
      else differences(a[k], b[k], at + '.' + k, out);
    }
  } else if (!Object.is(a, b)) out.push(at + ': ' + JSON.stringify(a) + ', then ' + JSON.stringify(b));
  return out;
}

// The plugin's reading and writing, for all files in one go: dotnet takes seconds to start. null when there is no dotnet.
function pluginRoundTrip(files) {
  const probe = spawnSync('dotnet', ['--version'], { encoding: 'utf8' });
  if (probe.error || probe.status !== 0) return null;
  const run = spawnSync('dotnet', ['run', '--project', path.join(ROOT, 'tests', 'Pure'), '-c', 'Release', '--', '--roundtrip', ...files],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const out = new Map();
  let current = null;
  for (const line of (run.stdout || '').split(/\r?\n/)) {
    if (line.startsWith(SEPARATOR)) { current = line.slice(SEPARATOR.length); out.set(current, []); }
    else if (current !== null) out.get(current).push(line);
  }
  const written = new Map();
  for (const [file, lines] of out) written.set(file, lines.join('\n').trim());
  if (!written.size) throw new Error('dotnet run gave nothing back (exit ' + run.status + '):\n' + ((run.stdout || '') + (run.stderr || '')).split(/\r?\n/).slice(-30).join('\n'));
  return written;
}

// ---------------------------------------------------------------- the checks
const script = pageScript();
try { openEditor(script); check('the editor starts without a browser', true); }
catch (err) { check('the editor starts without a browser', false, err && err.stack ? err.stack : String(err)); }
if (failures) { console.log(failures + ' of ' + checks + ' EDITOR CHECKS FAILED'); process.exit(1); }

console.log('files the editor must refuse (ML-010)');
{
  const expected = JSON.parse(fs.readFileSync(path.join(FIXTURES, 'expected.json'), 'utf8'));
  const dir = path.join(FIXTURES, 'invalid');
  for (const name of fs.readdirSync(dir).filter(n => n.endsWith('.json')).sort()) {
    const text = fs.readFileSync(path.join(dir, name), 'utf8');
    const page = openEditor(script), before = page.api.scenarioText();
    await page.api.openFiles([fakeFile(name, text)]);
    const refused = page.api.state.refused;
    // Text that is not JSON reaches the strict reader too, which names it not-json as the plugin does.
    const got = refused ? refused.errors.map(f => f.code + ' ' + f.path) : [];
    const want = Array.isArray(expected[name]) ? expected[name] : ['(not listed in expected.json)'];
    check('invalid/' + name + ' is refused with the listed places', got.join(' | ') === want.join(' | '), '\n        got  ' + got.join(' | ') + '\n        want ' + want.join(' | '));
    check('invalid/' + name + ' leaves the page as it was', page.api.scenarioText() === before);
    if (refused) check('invalid/' + name + ' shows up in the mission check', page.api.missionCheck().some(f => f.level === 'problem' && f.text.startsWith(name + ' was not loaded.')));
  }
}

console.log('mission check');
{
  const name = 'extraction_chance_defect.json';
  const page = await load(script, name, fs.readFileSync(path.join(FIXTURES, name), 'utf8'));
  const found = page.api.missionCheck();
  check(name + ' loads', !page.api.state.refused && page.api.state.zones.length === 1, page.banner());
  check(name + ': the check says nobody could leave on some runs', found.some(f => f.level === 'problem' && f.text.includes('nobody could leave') && f.text.includes('50%')),
    found.map(f => f.level + ': ' + f.text).join(' / '));
}

console.log('painted areas (ML-027)');
{
  // The same fixture as tests/Pure/AreaTests.cs, through the editor's copy of the rules.
  const api = openEditor(script).api;
  const fixture = JSON.parse(fs.readFileSync(path.join(FIXTURES, 'area_points.json'), 'utf8'));
  const problems = area => { const found = []; api.areaProblems(area, 't', (code, at) => found.push(code + ' ' + at)); return found.join(' | '); };
  for (const one of fixture.areas) {   // every point is in or out as the fixture says, and every area passes the strict rules
    check(one.name + ': the area passes the strict rules', problems(one.area) === '', problems(one.area));
    const area = api.areaRead(one.area);
    check(one.name + ': the area reads', area !== null);
    if (!area) continue;
    check(one.name + ': the fixture is already written canonically', api.areaSignature(area) === JSON.stringify(one.area), api.areaSignature(area));
    for (const point of one.points) check(one.name + ': ' + point.why + ' is ' + (point.in ? 'in' : 'out'), api.areaContains(area, point.p) === point.in, JSON.stringify(point.p));
  }
  for (const one of fixture.invalid) {   // every broken area is refused with exactly the listed codes and places, in order
    const got = problems(one.area), want = one.expect.join(' | ');
    check(one.name + ' is refused with the listed places', got === want, '\n        got  ' + got + '\n        want ' + want);
  }
  {   // A ring of 257 points is refused, and so is an area of more than 2048 points in rings that are each fine.
    const generated = (parts, points) => ({ parts: Array.from({ length: parts }, (_, j) => ({ floor: [0, 3],
      outline: Array.from({ length: points }, (_, k) => [j * 100 + Math.round(3000 * Math.cos(2 * Math.PI * k / points)) / 100, Math.round(3000 * Math.sin(2 * Math.PI * k / points)) / 100]) })) });
    check('a ring of 257 points is refused at the ring', problems(generated(1, 257)) === 'bad-area t.area.parts[0].outline', problems(generated(1, 257)));
    check('nine rings of 256 points are refused as an area of too many points', problems(generated(9, 256)) === 'bad-area t.area', problems(generated(9, 256)));
  }
  {   // Writing, reading and writing again gives the same text, with a hole and awkward decimals.
    const area = { parts: [{ floor: [-1.234, 2.006], outline: [[0.333, -0.001], [10.126, 0], [10, 10.5], [0, 10]], holes: [[[2, 2], [4, 2], [4, 4], [2, 4]]] }] };
    const once = JSON.stringify(api.areaWrite(area)), twice = JSON.stringify(api.areaWrite(api.areaRead(JSON.parse(once))));
    check('an area written, read and written again is the same text', once === twice, once + ' then ' + twice);
    check('it is written with two decimals and no minus zero', once === '{"parts":[{"floor":[-1.23,2.01],"outline":[[0.33,0],[10.13,0],[10,10.5],[0,10]],"holes":[[[2,2],[4,2],[4,4],[2,4]]]}]}', once);
  }
  const square = (x, z, size, bottom, top) => ({ parts: [{ floor: [bottom, top], outline: [[x, z], [x + size, z], [x + size, z + size], [x, z + size]], holes: [] }] });
  {   // The signature changes when a corner moves, and comes back when it moves back.
    const area = square(0, 0, 10, 0, 3), before = api.areaSignature(area);
    area.parts[0].outline[2] = [10, 11];
    check('moving a corner changes the signature', api.areaSignature(area) !== before);
    area.parts[0].outline[2] = [10, 10];
    check('moving it back gives the same signature', api.areaSignature(area) === before);
  }
  {   // The lenient reader turns an upside-down floor round, skips odd points and drops what cannot be used.
    const area = api.areaRead({ parts: [{ floor: [3, 0], outline: [[0, 0], 'x', [10, 0], [10, 10], [0, 10]], holes: [[[1, 1], [2, 1]]] }, { floor: [0, 3], outline: [[0, 0], [1, 1]] }, { outline: [[0, 0], [1, 0], [1, 1]] }] });
    check('a loose area keeps the one usable part', area !== null && area.parts.length === 1, JSON.stringify(area));
    check('its floor is turned round and the odd point and the two-point hole are gone', area !== null && api.areaSignature(area) === '{"parts":[{"floor":[0,3],"outline":[[0,0],[10,0],[10,10],[0,10]]}]}', area && api.areaSignature(area));
    check('an area with nothing usable reads as none, so the circle is used', api.areaRead({ parts: [{ floor: [0, 3], outline: [[0, 0], [1, 1]] }] }) === null);
  }
  {   // A trigger without an area keeps the circle exactly as the plugin has it.
    const t = { pos: [100, 10, 50], radius: 5, area: null };
    const got = [[100, 10, 50], [105, 10, 50], [105.01, 10, 50], [103, 12, 54], [103, 12.01, 54], [100, 7.99, 50], [96, 8, 47]].map(p => (api.membershipInside(t, p) ? '1' : '0')).join('');
    check('the circle answers as before for every point', got === '1101001', got);
    t.area = square(200, 200, 10, 0, 3);
    check('with an area the radius no longer counts', !api.membershipInside(t, [100, 10, 50]) && api.membershipInside(t, [205, 1, 205]));
    t.area = { parts: [] };
    check('an area with no parts falls back to the circle', api.membershipInside(t, [100, 10, 50]));
  }
  {   // A concave area of 200 corners in each of 8 parts answers 10 000 questions, all of them right.
    const area = { parts: Array.from({ length: 8 }, (_, part) => ({ floor: [part * 3, part * 3 + 2.5], holes: [],
      outline: Array.from({ length: 200 }, (_, k) => { const r = k % 2 === 0 ? 20 : 10, angle = k * Math.PI / 100; return [part * 50 + r * Math.cos(angle), r * Math.sin(angle)]; }) })) };
    api.areaPrepare(area);
    let wrong = 0;
    for (let i = 0; i < 10000; i++) {
      const part = i % 8, spike = Math.floor(i / 8) % 100, tip = Math.floor(i / 800) % 2 === 0;    // towards a spike's tip (in) or the notch between two (out)
      const angle = (2 * spike + (tip ? 0 : 1)) * Math.PI / 100;
      if (api.areaContains(area, [part * 50 + 15 * Math.cos(angle), part * 3 + 1, 15 * Math.sin(angle)]) !== tip) wrong++;
    }
    check('10 000 points in and out of a 200-corner area are all answered right', wrong === 0, wrong + ' wrong');
    check('a point of one part at another part\'s height is out', !api.areaContains(area, [0, 4, 0]) && api.areaContains(area, [0, 1, 0]));
  }
}

console.log('repairs for mission check findings (ML-017)');
{
  // The bounded search alone, on ground given by a function: walkable where x <= 0, at height 0.
  const api = openEditor(script).api, flat = (x, z) => (x <= 0 ? [0] : []);
  const at = (p, o) => JSON.stringify(api.walkableNear(p, flat, Object.assign({ radius: 3 }, o || {})));
  check('the nearest walkable spot with ground around it is found on the fixed grid', at([1, 0, 0]) === '[-0.25,0,0]', at([1, 0, 0]));
  check('nothing is found beyond the bound', at([1, 0, 0], { radius: 1 }) === 'null', at([1, 0, 0], { radius: 1 }));
  check('ground more than 1 m above or below does not count', at([1, 1.5, 0]) === 'null', at([1, 1.5, 0]));
  check('a spot the caller refuses is passed over for the next nearest', at([1, 0, 0], { ok: s => s[2] >= 1 }) === '[-0.25,0,1]', at([1, 0, 0], { ok: s => s[2] >= 1 }));
}
{
  // A small invented map: a ground floor and an upper floor 3 m above it over the same 10 x 10 m, and a lot 20 m to the side.
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  const fresh = () => {
    const api = openEditor(script).api, s = api.state;
    s.map = api.makeMap(new Float32Array([...plate(0, 0, 10, 10, 0), ...plate(0, 0, 10, 10, 3), ...plate(30, 0, 40, 10, 0)]), 'repair test');
    Object.assign(s, { infils: [], authored: [], zones: [], things: [], objectives: [], doors: [], extractionAfter: 0, selected: -1, squad: [] });
    s.enemies = [];
    return api;
  };
  const enemy = (api, pos, more) => Object.assign(api.newEnemy(pos, 'Guard', 0), more || {});
  const offer = (api, kind, has) => api.missionCheck().find(f => f.repair && f.repair.kind === kind && (!has || f.text.includes(has)));
  const text = v => JSON.stringify(v);
  {   // An enemy just off the ground is moved onto it; one far off stays where it is and keeps its finding.
    const api = fresh(), s = api.state;
    s.enemies = [enemy(api, [11, 0, 5]), enemy(api, [15, 0, 5])];
    const before = text(s.enemies), f = offer(api, 'snap', 'Enemy 1 ');
    check('an enemy off the walkable ground offers a repair', !!f, api.missionCheck().map(x => x.text).join(' / '));
    if (f) {
      const plan = api.previewRepair(f);
      check('the preview moves it to the nearest walkable ground, inside the searched area', plan.edits && text(plan.edits[0].to) === '[9.75,0,5]' && plan.areas[0].radius === 3, text(plan));
      check('the preview changes nothing yet', text(s.enemies) === before);
      check('Apply makes the change', api.applyRepair() === true && text(s.enemies[0].pos) === '[9.75,0,5]', text(s.enemies[0].pos));
      check('the check runs again: that finding is gone, the other one stays', !offer(api, 'snap', 'Enemy 1 ') && !!offer(api, 'snap', 'Enemy 2 '));
      api.undo();
      check('one Undo takes the whole repair back', text(s.enemies) === before, text(s.enemies));
    }
    const far = api.repairPlan({ kind: 'snap', what: 'enemy', i: 1 });
    check('with no walkable ground within 3 m there is an explanation and no edit', far.edits === null && far.why.includes('3 m') && far.areas.length === 1, text(far));
  }
  {   // Between two floors: the ground 1.6 m below and the floor 1.4 m above are both other floors, so nothing is moved.
    const api = fresh(), s = api.state;
    s.enemies = [enemy(api, [5, 1.6, 5])];
    const plan = api.repairPlan({ kind: 'snap', what: 'enemy', i: 0 });
    check('a repair never moves an enemy to another floor', plan.edits === null && plan.why.includes('nothing is moved'), text(plan));
  }
  {   // The target person is a goal of the mission: it is never moved.
    const api = fresh(), s = api.state;
    s.enemies = [enemy(api, [11, 0, 5], { hvt: true })];
    const plan = api.repairPlan({ kind: 'snap', what: 'enemy', i: 0 });
    check('the target person is never moved by a repair', plan.edits === null && plan.why.includes('target person'), text(plan));
  }
  {   // A patrol route stop and a thing off the ground are moved onto their own floor.
    const api = fresh(), s = api.state;
    s.enemies = [enemy(api, [5, 0, 5], { role: 'Patrol', patrol: [[2, 0, 2], [12, 0, 2]] })];
    s.things = [{ name: 'AKM', pos: [10.5, 3.2, 5], yaw: 0, trigger: 0, chance: 100 }, { name: 'AKM', pos: [5, 6, 5], yaw: 0, trigger: 0, chance: 100 }];
    const stop = offer(api, 'snap', 'Route stop 2');
    check('a route stop off the ground offers a repair', !!stop);
    if (stop) { api.previewRepair(stop); api.applyRepair(); }
    check('the route stop moves, the other stop and the enemy stay', text(s.enemies[0].patrol) === '[[2,0,2],[9.75,0,2]]' && text(s.enemies[0].pos) === '[5,0,5]', text(s.enemies[0]));
    const up = api.repairPlan({ kind: 'snap', what: 'thing', i: 0 }), high = api.repairPlan({ kind: 'snap', what: 'thing', i: 1 });
    check('a thing just off the upper floor goes onto the upper floor', up.edits && text(up.edits[0].to) === '[9.75,3,5]', text(up));
    check('a thing far above every floor gets an explanation', high.edits === null, text(high));
  }
  {   // Three enemies on one spot: the first stays, the others go at least 1 m from everybody, within 2 m, on the ground.
    const api = fresh(), s = api.state;
    s.enemies = [enemy(api, [5, 0, 5]), enemy(api, [5.2, 0, 5]), enemy(api, [5.1, 0, 5.1]), enemy(api, [8, 0, 8], { role: 'Patrol', patrol: [[8, 0, 8], [2, 0, 8]] })];
    const before = text(s.enemies), f = offer(api, 'apart');
    check('enemies on the same spot offer a repair', !!f);
    if (f) {
      const plan = api.previewRepair(f);
      check('the first stays and the two later ones move', plan.edits && plan.edits.map(e => e.i).join(',') === '1,2', text(plan.edits));
      api.applyRepair();
      const p = s.enemies.map(e => e.pos), d = (a, b) => Math.hypot(a[0] - b[0], a[2] - b[2]);
      check('afterwards every enemy is at least 1 m from every other', d(p[0], p[1]) >= 1 && d(p[0], p[2]) >= 1 && d(p[1], p[2]) >= 1, text(p));
      check('each moved no more than 2 m and stays on the ground', d(p[1], [5.2, 0, 5]) <= 2 && d(p[2], [5.1, 0, 5.1]) <= 2 && p[1][1] === 0 && p[2][1] === 0, text(p));
      check('no enemy stands on the same spot any more', !api.missionCheck().some(x => x.text.includes('same spot')));
      api.undo();
      check('one Undo puts all of them back, routes included', text(s.enemies) === before, text(s.enemies));
    }
  }
  {   // When the later one is the target person, the other one moves.
    const api = fresh(), s = api.state;
    s.enemies = [enemy(api, [5, 0, 5]), enemy(api, [5.2, 0, 5], { hvt: true })];
    const plan = api.repairPlan({ kind: 'apart', i: 0, j: 1 });
    check('the target person stays and the other enemy moves', plan.edits && plan.edits.length === 1 && plan.edits[0].i === 0, text(plan));
  }
  {   // A trigger number used twice is renumbered when every reference can mean only one of them.
    const api = fresh(), s = api.state;
    s.zones = [api.newTrigger(1, 'after', { after: 1, message: 'After the other one' }), api.newTrigger(1, 'time', { delay: 10, message: 'Ten seconds' })];
    const before = text(s.zones), f = offer(api, 'renumber');
    check('a trigger number used twice offers a repair', !!f);
    if (f) {
      const plan = api.previewRepair(f);
      check('the later one takes a free number and the one waiting for it follows', plan.edits && plan.edits.map(e => e.what + ':' + e.from + '>' + e.to).join(' ') === 'id:1>2 after:1>2', text(plan.edits));
      api.applyRepair();
      check('afterwards the numbers differ and the first waits for the second', s.zones[0].id === 1 && s.zones[0].after === 2 && s.zones[1].id === 2 && !offer(api, 'renumber'), text(s.zones));
      api.undo();
      check('Undo brings the old numbers and references back', text(s.zones) === before);
    }
    s.enemies = [enemy(api, [5, 0, 5], { trigger: 1 })];
    const vague = api.repairPlan({ kind: 'renumber', id: 1 });
    check('when an enemy waits for that number it could mean either: explanation, no edit', vague.edits === null && vague.why.includes('enemy 1'), text(vague));
  }
  {   // A gone trigger on an enemy or in an all-list: the repair only makes the page say what the saved file says.
    const api = fresh(), s = api.state;
    s.zones = [api.newTrigger(1, 'time', { delay: 5, message: 'Five' }), api.newTrigger(2, 'all', { of: [1, 9], message: 'Both' })];
    s.enemies = [enemy(api, [5, 0, 5], { trigger: 7 })];
    const file = api.scenarioText();
    for (const kind of ['unlink', 'unlist']) { const f = offer(api, kind); check(kind + ': offered', !!f); if (f) { api.previewRepair(f); check(kind + ': applied', api.applyRepair() === true); } }
    check('the enemy no longer waits for a gone trigger and the list holds only trigger 1', s.enemies[0].trigger === 0 && text(s.zones[1].of) === '[1]', text([s.enemies[0].trigger, s.zones[1].of]));
    check('the saved file is exactly as before', api.scenarioText() === file);
    s.zones[1].of = [9];
    check('a list that would be left empty gets an explanation', api.repairPlan({ kind: 'unlist', zi: 1 }).edits === null);
  }
  {   // A repair never touches the extraction.
    const api = fresh(), s = api.state;
    s.enemies = [enemy(api, [5, 0, 5])]; s.extractionAfter = 5;
    const f = offer(api, 'extraction'), plan = f ? api.previewRepair(f) : null;
    check('the extraction waiting for a gone trigger is explained, never changed', !!plan && plan.edits === null && s.extractionAfter === 5, text(plan));
  }
  {   // Nothing is applied while the page is linked to a game, nor when the scenario changed after the preview.
    const api = fresh(), s = api.state;
    s.enemies = [enemy(api, [11, 0, 5])];
    api.previewRepair(offer(api, 'snap'));
    api.live.on = true;
    check('Apply does nothing while linked to a running game', api.applyRepair() === false && text(s.enemies[0].pos) === '[11,0,5]');
    api.live.on = false;
    s.enemies[0].pos = [12, 0, 5];
    check('a stale preview is shown again, not applied', api.applyRepair() === false && text(s.enemies[0].pos) === '[12,0,5]' && text(s.repair.edits[0].from) === '[12,0,5]', text(s.repair));
  }
}

console.log('named places (ML-018)');
{
  const api = openEditor(script).api, s = api.state, text = JSON.stringify;
  const square = (bottom, top) => ({ parts: [{ floor: [bottom, top], outline: [[20, 20], [30, 20], [30, 30], [20, 30]], holes: [] }] });
  // A point place downstairs and one right above it, then an area place downstairs and one upstairs over the same ground.
  const hall = api.addPlace([10, 0, 10], 'point', [-0.5, 2.5]), landing = api.addPlace([10, 3, 10], 'point', [2.6, 5.6]);
  const shop = api.addPlace([25, 0, 25], 'area'), office = api.addPlace([25, 3, 25], 'area');
  shop.area = square(-0.5, 2.5); office.area = square(2.6, 5.5);
  check('new places get the next ids', text(s.places.map(p => p.id)) === '[1,2,3,4]', text(s.places.map(p => p.id)));
  check('point places on top of each other stay apart by their floor bands', api.placeName([11, 0.1, 10]) === hall.name && api.placeName([11, 3.1, 10]) === landing.name);
  check('area places on top of each other stay apart by their floor bands', api.placeName([22, 0.1, 28]) === shop.name && api.placeName([22, 3.1, 28]) === office.name);
  check('more than 3 m from a pin, or outside an area, is no place', api.placeName([13.1, 0.1, 10]) === '' && api.placeName([30.5, 0.1, 25]) === '');
  hall.floor = null;
  check('a point place without a band takes 2 m of height either way, as the plugin does', api.placeName([10, 2, 10]) === hall.name && api.placeName([10, -2.01, 10]) === '');
  hall.floor = [-0.5, 2.5];
  shop.pos = [25, 0, 19];      // the pin just outside its own area: a spot near the pin but outside the area is still no place
  check('an area place ignores the 3 m around its pin', api.placeName([25, 0.1, 19.9]) === '' && api.placeName([25, 0.1, 20.5]) === shop.name);
  shop.pos = [25, 0, 25];
  // Renaming keeps the id, so anything referring to the place by id still finds it.
  const before = api.scenarioObject().places;
  check('renaming a place is accepted', api.renamePlace(office.id, 'Upstairs office') === true);
  const after = api.scenarioObject().places;
  check('renaming keeps every id and changes only that name', text(after.map(p => p.id)) === text(before.map(p => p.id)) && after[3].name === 'Upstairs office' && text(after[0]) === text(before[0]), text(after));
  check('the renamed place is found under its new name', api.placeName([22, 3.1, 28]) === 'Upstairs office');
  // The file holds the places' own fields and nothing worked out from the map.
  const own = after.every(p => Object.keys(p).every(k => ['id', 'name', 'pos', 'floor', 'area'].includes(k)) && (!p.area || (Object.keys(p.area).join() === 'parts' && p.area.parts.every(q => Object.keys(q).every(k => ['floor', 'outline', 'holes'].includes(k))))));
  check('places are written with their own fields only', own, text(after));
  check('a point place has no area and an area place keeps its shape', !('area' in after[0]) && text(after[2].area) === text(api.areaWrite(square(-0.5, 2.5))), text(after));
  // Deleting is held back while something still names or refers to the place.
  s.briefing = ['Meet in the Upstairs office'];
  const uses = api.deletePlace(office.id);
  check('a place named in the briefing is not deleted at the first press', uses.length === 1 && uses[0].includes('briefing') && s.places.length === 4, text(uses));
  api.placeUsers.push(p => (p.id === shop.id ? ['module "two guards" binds it'] : []));
  check('a place another part refers to by id is held back too', text(api.deletePlace(shop.id)) === '["module \\"two guards\\" binds it"]' && s.places.length === 4);
  api.placeUsers.length = 0;
  check('deleting anyway removes it', api.deletePlace(office.id, true).length === 0 && s.places.length === 3 && !s.places.some(p => p.id === office.id));
  s.briefing = [];
  // Undo brings back the places as they were.
  api.addPlace([40, 0, 40], 'point', [-0.5, 2.6]);
  api.undo();
  check('undo takes back an added place', s.places.length === 3, text(s.places.map(p => p.id)));
  // The mission check calls things by the place they are in.
  s.enemies = [api.newEnemy([11, 0.1, 10]), api.newEnemy([11, 0.1, 10])];
  const found = api.missionCheck().map(f => f.text);
  check('the mission check names the place of two enemies on one spot', found.some(t => t.includes('same spot in ' + hall.name)), found.join(' / '));
  s.enemies = [];
  // A replay of the same map file names the places of its events; one of another map file does not.
  s.navFile = 'house.bytes';
  api.replay.data = { navFile: 'house.bytes' };
  check('a replay on the scenario\'s map names the place', api.replayPlace([11, 0.1, 10]) === ' in ' + hall.name);
  api.replay.data = { navFile: 'other.bytes' };
  check('a replay of another map file names no place', api.replayPlace([11, 0.1, 10]) === '');
  api.replay.data = null;
  // The rebind review: the open map file's fingerprint against the scenario's.
  const map = s.map, one = 'fnv1a64:' + '1'.repeat(16), two = 'fnv1a64:' + '2'.repeat(16);
  check('the example map asks for no review', api.placeReview() === null);
  s.map = { isExample: false, print: one }; s.mapPrint = one;
  check('the same fingerprint asks for no review', api.placeReview() === null);
  const written = Object.keys(api.scenarioObject()).join();
  s.map.print = two;
  let review = api.placeReview();
  check('another fingerprint lists every place, none checked', review !== null && review.why === 'changed' && text(review.places.map(q => [q.id, q.mark])) === text(s.places.map(p => [p.id, ''])), text(review));
  api.markPlace(hall.id, 'right'); api.markPlace(landing.id, 'moved');
  review = api.placeReview();
  check('what the author said about each place is shown', text(review.places.map(q => q.mark)) === '["right","moved",""]', text(review));
  const reviewed = api.scenarioObject();
  check('the review adds nothing to the file but the new fingerprint', Object.keys(reviewed).join() === written && reviewed.mapPrint === two && text(reviewed.places) === text(api.scenarioObject().places) && !text(reviewed).includes('"moved"'), Object.keys(reviewed).join());
  s.mapPrint = '';
  review = api.placeReview();
  check('a scenario without a fingerprint gets the review too, unchecked again', review !== null && review.why === 'unknown' && review.places.every(q => q.mark === ''), text(review));
  for (const p of s.places) api.markPlace(p.id, 'new');
  check('places all made on this map need no review', api.placeReview() === null);
  s.map = map; s.mapPrint = '';
}

console.log('staged live changes and revisions (ML-014)');
{
  const page = openEditor(script), api = page.api, texts = lines => lines.map(l => l.kind + ': ' + l.text).join(' / ');
  // The semantic diff names what a change does: enemies by number, tag and place, reinforcements, doors and mission rules.
  const before = {
    schema: 'missionlab-plan/1', name: 'Diff', places: [{ id: 1, name: 'Garage', pos: [10, 0, 10] }], extraction: { after: 2 },
    triggers: [{ id: 2, name: 'Alarm', when: 'alert' }], doors: [{ name: 'Front', pos: [5, 0, 5] }],
    spawns: [{ pos: [0, 0, 0], tag: 'officer' }, { pos: [3, 0, 0] }, { pos: [6, 0, 0] }, { pos: [20, 0, 20], trigger: 2 }],
  };
  const after = JSON.parse(JSON.stringify(before));
  after.spawns[0].pos = [10, 0, 11]; after.spawns[1].pos = [4, 0, 1]; after.spawns[2].pos = [7, 0, 2];
  after.spawns.push({ pos: [21, 0, 20], trigger: 2 });
  after.doors[0].locked = true; after.triggers[0].delay = 10; delete after.extraction; after.respawn = { after: 20, times: 3 };
  const lines = api.semanticDiff(before, after, { fired: [2] });
  check('the diff names the tagged enemy and the place it moved to', lines.some(l => l.kind === 'enemy' && l.text.startsWith('Enemy 1 (officer) in Garage: moved')), texts(lines));
  check('the diff lists each of the three moved enemies', lines.filter(l => l.kind === 'enemy' && l.text.includes('moved')).length === 3, texts(lines));
  check('the diff names the door and what happens to it', lines.some(l => l.kind === 'door' && l.text.includes('"Front"') && l.text.includes('locked (was left to the game)')), texts(lines));
  check('the diff counts the enemies a trigger brings, and says it has fired', lines.some(l => l.kind === 'reinforcement' && l.text.includes('now brings 2 enemies (was 1)') && l.text.includes('fired already')), texts(lines));
  check('the diff names the added reinforcement and its trigger', lines.some(l => l.kind === 'reinforcement' && l.text.includes('added') && l.text.includes('trigger 2 "Alarm"')), texts(lines));
  check('the diff names mission rules: extraction and reinforcement waves', lines.some(l => l.kind === 'extraction' && l.text.includes('no longer waits')) && lines.some(l => l.kind === 'respawn' && l.text.includes('after 20 s, 3 times')), texts(lines));
  check('the diff names a changed trigger', lines.some(l => l.kind === 'trigger' && l.text.includes('delay s not set → 10')), texts(lines));
  check('an unchanged setup has no diff', api.semanticDiff(before, JSON.parse(JSON.stringify(before))).length === 0);
  const markers = [{ id: 1, pos: [0, 0, 0] }, { id: 2, pos: [3, 0, 0], live: true }, { id: 3, pos: [6, 0, 0] }];
  const gone = api.semanticDiff({ markers }, { markers: [markers[0], markers[2]] });
  check('live markers pair by id: removing one is one line, saying its enemy goes too', gone.length === 1 && gone[0].text.startsWith('Enemy 2 removed') && gone[0].text.includes('its enemy in the game goes too'), texts(gone));

  // Staging rules: what blocks a batch, which number a retry carries, the page's own revision store.
  const v = api.stageVerdict([{ level: 'error', path: 'triggers[0].after', text: 'there is no trigger 77' }, { level: 'note', path: '', text: 'old' }],
    [{ level: 'problem', text: 'New problem.' }, { level: 'problem', text: 'Old problem.' }, { level: 'warning', text: 'Look.' }, { level: 'problem', text: 'x.json was not loaded. bad' }], ['Mission check: Old problem.'], 'x.json');
  check('a scenario refusal and a new Mission check problem block; known problems, warnings and refused files do not', v.length === 2 && v[0].includes('there is no trigger 77') && v[1] === 'Mission check: New problem.', v.join(' / '));
  let n = 0; const make = () => 'c' + (++n);
  const first = api.stageCommandFor(null, 'k1', make), again = api.stageCommandFor(first, 'k1', make);
  check('a retry of the same batch carries the same command number', again.command === first.command && n === 1);
  check('another batch gets a new number', api.stageCommandFor(first, 'k2', make).command !== first.command);
  first.done = true;
  check('a batch the game has answered is never reused', api.stageCommandFor(first, 'k1', make).command !== first.command);
  check('no answer or a begun batch keeps the number; refused, expired, busy or failed does not',
    api.stageKeepsCommand(new Error('network')) && api.stageKeepsCommand({ status: 202, result: 'pending' }) && !api.stageKeepsCommand({ status: 503, result: 'expired' }) && !api.stageKeepsCommand({ status: 409 }) && !api.stageKeepsCommand({ status: 429 }) && !api.stageKeepsCommand({ status: 500, result: 'failed' }));
  let store = {};
  for (let k = 1; k <= 5; k++) store = api.stageRevisionAdd(store, { id: 'sc-a', n: k, hash: 'h' + k, saved: '2026-01-0' + k + 'T00:00:00Z', note: '', scenario: 'x' }, { revisions: 3 }).all;
  check('the page keeps the newest revisions of a scenario', store['sc-a'].map(r => r.n).join() === '5,4,3', JSON.stringify(store));
  check('the same revision saved twice is one save', api.stageRevisionAdd(store, { id: 'sc-a', n: 5, hash: 'h5' }).again === true);
  check('another text under a number already taken is refused', !!api.stageRevisionAdd(store, { id: 'sc-a', n: 5, hash: 'other' }).error);
  // Each entry is 195 characters as JSON: two scenarios with one each make 411, one scenario with two makes 402.
  const big = 'y'.repeat(120);
  let small = api.stageRevisionAdd({}, { id: 'sc-b', n: 1, hash: 'a', saved: '2026-01-01T00:00:00Z', scenario: big }, { revisionChars: 405 }).all;
  small = api.stageRevisionAdd(small, { id: 'sc-c', n: 1, hash: 'b', saved: '2026-01-02T00:00:00Z', scenario: big }, { revisionChars: 405 }).all;
  small = api.stageRevisionAdd(small, { id: 'sc-c', n: 2, hash: 'c', saved: '2026-01-03T00:00:00Z', scenario: big }, { revisionChars: 405 }).all;
  check('the store is bounded: the oldest revision of any scenario goes first', !small['sc-b'] && small['sc-c'] && small['sc-c'].length === 2, JSON.stringify(Object.keys(small)));
  check('a text\'s fingerprint is the plugin\'s ScenarioFile.Hash', api.textHash('abc') === 'fnv1a64:e71fa2190541574b' && api.textHash('a\r\nb') === api.textHash('a\nb') && api.textHash('') === '', api.textHash('abc'));
}
{
  // Draft mode while linked to a running mission: moving three enemies and changing a door is staged, not sent.
  const file = JSON.stringify({ schema: 'missionlab-plan/1', name: 'Staging test', id: 'sc-stagetest1', navFile: 'test.bytes', places: [{ id: 1, name: 'Garage', pos: [10, 0, 10] }],
    triggers: [{ id: 2, name: 'Alarm', when: 'alert' }], spawns: [{ pos: [0, 0, 0], tag: 'officer' }, { pos: [3, 0, 0] }, { pos: [6, 0, 0] }, { pos: [20, 0, 20], trigger: 2 }] });
  const page = await load(script, 'staging.json', file), api = page.api, s = api.state, live = api.live;
  check('the staging scenario loads', !s.refused && s.enemies.length === 4, page.banner());
  s.doors.push({ name: 'Front', pos: [1, 0, 1], mid: [1, 0, 1], locked: null, enemiesCanOpen: null, open: null, chance: 100, known: true });
  live.on = true; live.mission = true; live.session = 's1'; live.rev = 'r1';
  api.stageSetMode('draft');
  check('Draft mode starts from the game\'s state', api.stage.mode === 'draft' && api.stage.base !== null && !api.stage.dirty);
  api.willChange('Move three enemies and lock the front door');
  s.enemies[0].pos = [10, 0, 11]; s.enemies[1].pos = [4, 0, 1]; s.enemies[2].pos = [7, 0, 2]; s.doors[s.doors.length - 1].locked = true;
  api.changed();
  const staged = live.staged.map(l => l.kind + ': ' + l.text).join(' / ');
  check('in Draft mode an edit is not sent to the game', live.pushTimer === 0 && !live.sending, String(live.pushTimer));
  check('the staged edit holds the page: the game\'s list does not overwrite it', live.pending === true && api.stage.dirty === true);
  check('the staged list names the three moved enemies and the door', live.staged.filter(l => l.kind === 'enemy' && l.text.includes('moved')).length === 3 && live.staged.some(l => l.kind === 'door' && l.text.includes('locked')), staged);
  api.stageSetMode('live');
  check('Live mode is refused while changes are staged', api.stage.mode === 'draft', page.banner());
  check('nothing is blocked in this draft', api.stageBlockers().length === 0, api.stageBlockers().join(' / '));
  await api.applyStaged();
  const sent = api.stage.out && api.stage.out.command;
  check('a batch with no answer keeps its command number for the retry', !!sent && api.stage.out.done === false && api.stage.receipt && api.stage.receipt.retry === true, JSON.stringify(api.stage.receipt));
  await api.applyStaged();
  check('the retry asks about the same batch and sends no new one', api.stage.out.command === sent, api.stage.out.command + ' ' + sent);
  api.undo();
  check('Undo restores the draft, and nothing is staged any more', !api.stage.dirty && live.staged.length === 0 && live.pending === false, live.staged.map(l => l.text).join(' / '));
  api.stage.out = null;
  s.zones.push(api.newTrigger(9, 'after', { after: 77 }));
  api.changed();
  const applied = await api.applyStaged();
  check('an invalid batch is blocked before anything is sent', applied === false && api.stage.out === null && page.banner().startsWith('Not applied: The scenario check refuses it'), page.banner());
  check('the blocked preview stays open', api.stage.preview === true);
}
{
  // Offline: a saved revision opens as a separate draft and never overwrites the author's own draft.
  const file = JSON.stringify({ schema: 'missionlab-plan/1', name: 'Mine', id: 'sc-revtest01', navFile: 'test.bytes', spawns: [{ pos: [0, 0, 0] }, { pos: [3, 0, 0] }] });
  const page = await load(script, 'mine.json', file), api = page.api, s = api.state;
  check('a revision is saved in the page\'s own store', await api.saveRevision() === true && api.localRevisions()['sc-revtest01'] && api.localRevisions()['sc-revtest01'][0].n === 1, page.banner());
  const r1 = api.localRevisions()['sc-revtest01'][0];
  check('the revision holds the scenario text and its fingerprint', r1.hash === api.textHash(r1.scenario) && JSON.parse(r1.scenario).revision === 1, r1.hash);
  s.enemies.push(api.newEnemy([6, 0, 0], 'Guard', 0)); api.changed();
  check('a second save is revision 2', await api.saveRevision() === true && api.localRevisions()['sc-revtest01'][0].n === 2);
  check('opening revision 1 as a separate draft', api.openSeparate('rev:sc-revtest01:1', 'revision 1', JSON.parse(r1.scenario), null) === true && s.enemies.length === 2 && api.stage.separate !== null, page.banner());
  s.enemies.push(api.newEnemy([9, 0, 0], 'Guard', 0)); s.enemies.push(api.newEnemy([12, 0, 0], 'Guard', 0)); api.changed();
  await new Promise(done => setTimeout(done, 600));      // longer than the page waits before it saves a draft
  check('the separate draft is kept under its own key', api.separateDrafts().some(d => d.key === 'rev:sc-revtest01:1' && (d.scenario.spawns || []).length === 4));
  api.backToMine();
  check('back to the own draft: it was not overwritten', api.stage.separate === null && s.enemies.length === 3 && s.revision === 2, s.enemies.length + ' enemies, revision ' + s.revision);
}

console.log('trigger inspector (ML-015)');
{
  // The diagnostic example, first without the game (what the file alone says), then with a state answer as the plugin
  // writes it: GameMaster.StateJson, triggerState[].explain from TriggerEngine.Explain.
  const name = 'clean_house_diagnostic.json', text = fs.readFileSync(path.join(ROOT, 'examples', name), 'utf8');
  const page = await load(script, name, text), api = page.api;
  check(name + ' loads with its nine triggers', !api.state.refused && api.state.zones.length === 9, page.banner());
  const offline = new Map(api.inspectRows(null).map(r => [r.id, r]));
  check('offline, nothing claims to be live', [...offline.values()].every(r => !r.live && r.clock === '' && r.outcome === ''));
  check('offline, the chance trigger is shown as left to chance', offline.get(5).kind === 'chance' && offline.get(5).word === 'Chance 50%' && offline.get(5).text.includes('excluded'), JSON.stringify(offline.get(5)));
  check('offline, the trigger after it says it hangs on that chance', offline.get(6).kind === 'depends' && offline.get(6).text.includes('trigger 5') && offline.get(6).text.includes('50%'), JSON.stringify(offline.get(6)));
  check('offline, the by-hand trigger says so', offline.get(7).kind === 'byhand' && offline.get(7).text.includes('Fire now'), JSON.stringify(offline.get(7)));
  check('offline, the intel trigger can fire and names the phone', offline.get(4).kind === 'can' && offline.get(4).text === 'Fires when the cell phone is picked up', offline.get(4).text);
  check('offline, the repeating trigger says how often it may fire', offline.get(2).count === 'up to 3 times', offline.get(2).count);
  {   // A trigger that waits for one that is gone can never fire, and the static view says why.
    api.state.zones.push(api.newTrigger(20, 'after', { after: 99 }));
    const r = api.inspectRows(null).find(q => q.id === 20);
    check('offline, a trigger waiting for a missing one never fires', r.kind === 'never' && r.text.includes('trigger 99 does not exist'), JSON.stringify(r));
    api.state.zones.pop();
  }

  // The upper north room as a named place: the inspector's sentences use it (29b-places.js).
  api.state.places = [{ id: 1, name: 'Upper north room', pos: [-20, 3.68, 4], floor: null, area: null }];
  const explain = (id, phase, why, more) => ({ id, phase, fires: (more && more.fires) | 0, left: (more && more.left) || 0,
    explain: Object.assign({ phase, left: 0, why, fires: 0, times: 0, waiting: [], excluded: [], inside: -1, alive: -1, outcome: '', outcomeWhy: '' }, more || {}) });
  const canned = JSON.parse(JSON.stringify({ mission: true, triggerState: [
    explain(1, 'fired', 'Fired 30 s into the mission; whether its actions took could not be seen: asked of the game, not seen to take: a message', { fires: 1, outcome: 'unconfirmed', outcomeWhy: 'asked of the game, not seen to take: a message' }),
    explain(2, 'cooling', 'Fired 2 of 3 times; armed again in 13 s', { fires: 2, times: 3, left: 12.3, outcome: 'applied', outcomeWhy: '1 enemy appeared' }),
    explain(3, 'waiting', '1 of 2 living players inside; every one of them has to be', { inside: 1, alive: 2 }),
    explain(4, 'waiting', 'Waiting for the cell phone to be picked up'),
    explain(5, 'off', 'Sat this mission out (its chance is 50%); only Fire now sets it off'),
    explain(6, 'waiting', 'Waiting for trigger 5, which sat this mission out (chance)', { waiting: [5], excluded: [5] }),
    explain(7, 'fired', 'Fired by hand 95 s into the mission, but its actions failed: no door was found at (-4, -2)', { fires: 1, outcome: 'failed', outcomeWhy: 'no door was found at (-4, -2)' }),
    explain(8, 'counting', 'Condition met; 12 s left', { left: 11.6 }),
    explain(9, 'waiting', 'Waiting for 2 enemies to go down'),
  ] }));
  const rows = new Map(api.inspectRows(canned).map(r => [r.id, r]));
  check('live, every row is the game\'s', [...rows.values()].every(r => r.live));
  check('live, waiting for the phone is said as the game says it', rows.get(4).word === 'Waiting' && rows.get(4).text === 'Waiting for the cell phone to be picked up', rows.get(4).text);
  check('live, players inside are counted with the room\'s name', rows.get(3).text === '1 of 2 living players inside Upper north room; every one of them has to be', rows.get(3).text);
  check('live, the countdown is the game\'s, rounded up as the engine does', rows.get(8).word === 'Counting down' && rows.get(8).clock === '12 s' && rows.get(8).text.endsWith('12 s left'), JSON.stringify(rows.get(8)));
  check('live, the repeat count is the game\'s', rows.get(2).count === 'fired 2 of 3' && rows.get(2).word === 'Cooling down' && rows.get(2).clock === '13 s', JSON.stringify(rows.get(2)));
  check('live, the excluded trigger is excluded', rows.get(5).word === 'Excluded' && rows.get(5).kind === 'off');
  check('live, the dependency that was excluded is named', rows.get(6).text.includes('trigger 5') && rows.get(6).text.includes('sat this mission out') && rows.get(6).excluded.join() === '5', JSON.stringify(rows.get(6)));
  check('live, a failed action is told apart from an unmet condition', rows.get(7).outcome === 'failed' && rows.get(7).outcomeText.startsWith('Action failed: no door') && rows.get(9).outcome === '' && rows.get(9).word === 'Waiting', JSON.stringify(rows.get(7)));
  check('live, an unconfirmed action is told apart from a failed one', rows.get(1).outcome === 'unconfirmed' && rows.get(1).outcomeText.startsWith('Action not confirmed') && rows.get(1).word === 'Fired', JSON.stringify(rows.get(1)));
  check('a minute or more is shown as minutes and seconds', api.inspectClock(125.2) === '2:06' && api.inspectClock(0) === '0 s' && api.inspectClock(1.001) === '1 s');
  {   // A reloaded page knows nothing of its own: fed the same state, it shows exactly the same.
    const again = await load(script, name, text);
    again.api.state.places = [{ id: 1, name: 'Upper north room', pos: [-20, 3.68, 4], floor: null, area: null }];
    check('after a reload the inspector shows the same rows', JSON.stringify(again.api.inspectRows(canned)) === JSON.stringify(api.inspectRows(canned)));
  }

  // What selecting a trigger highlights.
  const links = id => api.inspectLinks(id);
  check('selecting trigger 6 highlights the trigger it needs', links(6).before.join() === '5', JSON.stringify(links(6)));
  check('selecting trigger 5 highlights the enemy it brings', links(5).enemies.join() === '5', JSON.stringify(links(5)));
  check('selecting trigger 2 highlights its wave enemy', links(2).enemies.join() === '3');
  check('selecting trigger 8 highlights the door it watches and the door it locks', links(8).watched !== null && links(8).watched.pos.join() === '-19.55,0.7,-19.57' && links(8).doors.length === 1 && links(8).doors[0].pos.join() === '-24.06,0.7,-2.67', JSON.stringify(links(8)));
  check('selecting the phone trigger shows the extraction waits for it', links(4).extraction === 'holds');
  check('selecting trigger 9 shows it opens the extraction', links(9).extraction === 'opens');
  check('the words under a selected trigger name what it needs and brings', api.inspectLinkText(links(6)) === 'Needs trigger 5.' && api.inspectLinkText(links(5)).includes('enemy 6'), api.inspectLinkText(links(5)));
  {   // The chain is followed all the way back.
    api.state.zones.push(api.newTrigger(21, 'all', { of: [6, 4] }));
    check('a prerequisite of a prerequisite is highlighted too', links(21).before.slice().sort().join() === '4,5,6', JSON.stringify(links(21)));
    api.state.zones.pop();
  }
}

console.log('views: Play, Create and Direct (ML-013)');
{
  const page = openEditor(script), api = page.api, el = api.byId;
  check('offline the page starts in Create', api.viewIs('create') && el('tabs').hidden === false && el('playBox').hidden === true, String(api.state.pane));
  api.setView('play');
  check('Play hides the tabs with the enemy-placement controls, and Save', el('tabs').hidden === true && el('saveBox').hidden === true && el('playBox').hidden === false && el('directBox').hidden === true);
  check('Play hides Undo and leaves no placing tool chosen', el('undo').hidden === true && api.state.tool === 'select', api.state.tool);
  check('the column is set to show Play', el('side').dataset.pane === 'play' && api.viewIs('play'), String(el('side').dataset.pane));
  check('offline the strip says next and running missions need the game', el('stNext').textContent === 'chosen in the game (not connected)' && el('stRun').textContent === 'not connected to the game');
  check('offline the example is no scenario to show in Play', api.pageEntry() === null);
  api.setView('direct');
  check('Direct shows the live controls, not the tabs or Play', el('directBox').hidden === false && el('tabs').hidden === true && el('playBox').hidden === true && el('side').dataset.pane === 'direct');
  api.setView('create');
  check('Create brings back the tabs, Undo and Save', el('tabs').hidden === false && el('undo').hidden === false && el('saveBox').hidden === false && el('playBox').hidden === true);
  api.setView('somewhere');
  check('an unknown view falls back to Create', api.viewIs('create'));

  // A file opened offline: Play shows it with the author's words labelled as the author's, and what only the game knows as unknown.
  const house = JSON.stringify({ schema: 'missionlab-plan/1', name: 'House', id: 'sc-house', squad: [2, 4], minutes: 20, navFile: 'suburbv2.bytes', spawns: [{ pos: [0, 0, 0], chance: 50 }, { pos: [2, 0, 2] }] });
  await api.openFiles([fakeFile('house.json', house)]);
  check('a file just opened counts as saved', el('stDraft').textContent === 'saved as house.json', String(el('stDraft').textContent));
  api.state.title = 'House 2'; api.syncStrip();
  check('an edit makes the draft unsaved', String(el('stDraft').textContent).startsWith('unsaved changes'), String(el('stDraft').textContent));
  api.setView('play');
  const entry = api.pageEntry(), facts = new Map(api.catalogueFacts(entry, {}));
  check('Play shows the scenario on the page offline', entry && entry.page && entry.title === 'House 2' && entry.usable, JSON.stringify(entry && entry.title));
  check('the squad range is labelled as the author\'s', facts.get('Squad (the author\'s recommendation)') === '2 to 4 players', JSON.stringify([...facts]));
  check('the estimate is labelled as the author\'s', facts.get('Length (the author\'s estimate)') === 'about 20 min', JSON.stringify([...facts]));
  check('offline the measured length is not made up', facts.get('Length (measured)') === 'not measured yet: run records are kept by the game');
  check('offline compatibility and the last run stay unknown', facts.get('Tested with this build') === 'not known without the game' && facts.get('Last run') === 'not known without the game');
  // Offline the plugin's build is not known, so no capability may read as confirmed (ML-036: tests/editor/cases/ml036-capabilities.mjs has the rest).
  check('offline no capability is claimed as confirmed on a build', entry.capabilities.length > 0 && entry.capabilities.every(c => c.confirmed === false && c.shown !== 'supported'), JSON.stringify(entry.capabilities));

  // A catalogue entry as the plugin writes it (Catalogue.cs): unknown stays unknown, measurements are their own row.
  const none = new Map(api.catalogueFacts({ file: 'a.json', value: 'a', usable: true, author: { squad: null, minutes: null }, measured: { minutes: null, finished: 0, runs: 2 }, navFile: '', scene: '', map: '', compat: '', capabilities: [], lastRun: null }, {}));
  check('no author squad or estimate says so', none.get('Squad (the author\'s recommendation)') === 'not given by the author' && none.get('Length (the author\'s estimate)') === 'not given by the author');
  check('runs without a seen end leave the length unmeasured', none.get('Length (measured)') === 'not measured yet (2 runs on record, none seen to end)', none.get('Length (measured)'));
  check('a file naming no map has unknown compatibility', none.get('Tested with this build') === 'unknown: the file names no map file' && none.get('Map') === 'not named in the file');
  check('a scenario never played says so', none.get('Last run') === 'never played');
  const measured = new Map(api.catalogueFacts({ file: 'b.json', value: 'b', usable: true, author: { squad: [1, 1], minutes: 30 }, measured: { minutes: 24.5, finished: 3, runs: 4 }, navFile: 'suburbv2.bytes', map: 'Clean House', compat: 'tested', capabilities: [], lastRun: { file: 'r', started: '2026-11-17T10:00:00Z', session: 'm1', outcome: 'used', result: 'success', minutes: 22 } }, {}));
  check('measured and estimated lengths are different rows', measured.get('Length (measured)') === '24.5 min, the middle of 3 runs seen to end' && measured.get('Length (the author\'s estimate)') === 'about 30 min');
  check('a tested map and the last result are named', measured.get('Tested with this build') === 'yes: this map, hosted with MissionLab' && measured.get('Last run').startsWith('ended as a success after 22 min'), measured.get('Last run'));
  check('a run of this session without an end is running', api.lastRunWords({ started: '2026-11-17T10:00:00Z', session: 'm2', outcome: 'used', result: '', minutes: null }, { session: 'm2', mission: true }).startsWith('running now'));
  check('a run of an earlier session without an end is not measured', api.lastRunWords({ started: '2026-11-17T10:00:00Z', session: 'm1', outcome: 'used', result: '', minutes: null }, { session: 'm2', mission: true }).includes('not measured'));
}

console.log('rehearsal (ML-016)');
{
  const api = openEditor(script).api;
  const read = name => fs.readFileSync(path.join(FIXTURES, 'rehearsal', name), 'utf8').replace(/\r\n/g, '\n').trim();
  const trace = read('editor_trace.rehearsal.json'), traced = JSON.parse(trace);
  const meta = { name: traced.name, scenario: traced.scenario, revision: traced.revision, players: traced.players, roll: traced.roll };
  {   // The shared fixture: the plugin's Rehearsal.Write gives this very text (tests/Pure/RehearsalsTests.cs), and so must the page.
    const text = api.rehearseText(api.rehearseScript(api.rehearseRows(traced), meta));
    check('the editor writes the shared trace fixture byte for byte as the plugin does', text === trace, '\n' + text);
  }
  {   // Rows as an author makes them, out of order: one row is one step, in time order, rows at one time keep their order.
    const rows = [
      { at: 20, kind: 'fired', id: 3 },
      { at: 0, kind: 'players', players: [[0, 0, 0], [2, 0, 0]] },
      { at: 2, kind: 'down', tag: 'officer', count: 1 },
      { at: 3, kind: 'door', pos: [5, 0, 5] },
      { at: 4, kind: 'intel', item: 'cell phone' },
      { at: 5, kind: 'fired', id: 1 }, { at: 5, kind: 'notFired', id: 2 }, { at: 5, kind: 'notFired', id: 3 }, { at: 5, kind: 'phase', id: 2, phase: 'counting' },
      { at: 6, kind: 'fire', id: 2 },
      { at: 7, kind: 'phase', id: 2, phase: 'fired' }, { at: 7, kind: 'extraction', value: 'open' }, { at: 7, kind: 'enemies', count: 5 },
      { at: 8, kind: 'alert' },
      { at: 10, kind: 'phase', id: 3, phase: 'holding' },
      { at: 9.5, kind: 'players', players: [[100.001, 0, 100], [101.25, -0.001, 99]] },
    ];
    const text = api.rehearseText(api.rehearseScript(rows, meta));
    check('a trace built row by row, out of order, is written as the shared fixture', text === trace, '\n' + text);
  }
  {   // A script whose steps do several things reads as one row per thing, and nothing is lost on the way back.
    const phone = JSON.parse(read('phone_countdown.rehearsal.json')), rows = api.rehearseRows(phone);
    check('a step with five checks becomes five rows', rows.length === 14, JSON.stringify(rows));
    const again = JSON.parse(api.rehearseText(api.rehearseScript(rows, { name: phone.name, players: phone.players, roll: phone.roll })));
    const flat = s => s.steps.flatMap(st => [...(st.players ? ['players@' + st.at + JSON.stringify(st.players)] : []), ...(st.event ? [st.event + '@' + st.at + (st.item || '')] : []),
      ...(st.expect ? Object.entries(st.expect).flatMap(([k, v]) => (Array.isArray(v) ? v.map(id => k + id + '@' + st.at) : typeof v === 'object' ? Object.entries(v).map(([id, w]) => k + id + w + '@' + st.at) : [k + v + '@' + st.at])) : [])]).sort().join(' ');
    check('every event and check of the plugin\'s fixture survives rows and back', flat(again) === flat(phone), flat(again) + '\n        ' + flat(phone));
    check('players, roll and name are kept', again.players === 2 && again.roll === 12345 && again.name === phone.name && !('scenario' in again) && !('revision' in again));
  }
  {   // What stops a run is named by row.
    const found = api.rehearseProblems([{ at: -1, kind: 'alert' }, { at: 1, kind: 'players', players: null }, { at: 2, kind: 'door', pos: null }, { at: 3, kind: 'fired', id: 9 }, { at: 4, kind: 'phase', id: 1, phase: 'asleep' }, { at: 5, kind: 'players', players: [] }], [1, 2]);
    check('each incomplete row is named by number', found.map(f => f.row).join() === '1,2,3,4,5', found.map(f => f.text).join(' / '));
    check('a missing trigger is named', found.some(f => f.text === 'Row 4: there is no trigger 9 in this scenario.'));
    check('an empty trace asks for a row', api.rehearseProblems([], [1]).map(f => f.text).join() === 'Add at least one row.');
  }
  {   // The page and the plugin say the same about a changed scenario (RehearsalFile.Match).
    const changed = api.rehearseMatch({ scenario: 'fnv1a64:0123456789abcdef', revision: 3 }, 'fnv1a64:fedcba9876543210', 0);
    check('a test written for other text says the scenario changed, with both revisions', changed.match === 'changed' &&
      changed.why === 'The scenario changed since this test was written: the test was written for revision 3, this is revision 0. Its steps ran against the scenario as it is now.', changed.why);
    check('the same revision with other content says so', api.rehearseMatch({ scenario: 'a', revision: 4 }, 'b', 4).why.includes('this is revision 4 with other content'));
    check('the same text matches', api.rehearseMatch({ scenario: 'a', revision: 4 }, 'a', 4).match === 'same');
    check('no fingerprint cannot be told', api.rehearseMatch({ scenario: '', revision: 0 }, 'a', 4).match === 'unknown');
  }
  {   // The plugin's answer as shown: expected against actual per step, the failing row, notes, the assumptions.
    const answer = { passed: false, refused: '', blocked: '', assumptions: 'Events happen when the script says, as if injected; spotting, pathfinding, physics, networking and combat are not predicted.',
      scenarioHash: 'fnv1a64:fedcba9876543210', revision: 0, match: 'changed', matchWhy: 'The scenario changed since this test was written: the test was written for revision 3, this is revision 0. Its steps ran against the scenario as it is now.',
      steps: [{ at: 4, what: 'step 4 at 4 s: the cell phone is picked up', expected: '', actual: '', ok: true },
        { at: 6, what: 'step 9 at 6 s: trigger 2 is fired by hand', expected: 'it fires', actual: 'it fired', ok: true },
        { at: 7, what: 'step 12 at 7 s: 9 enemies are in the mission', expected: '9', actual: '5 (3 from the start, 2 brought by triggers)', ok: false }], findings: [] };
    const rep = api.rehearseReport(answer);
    check('a failed rehearsal says how many checks failed', rep.level === 'failed' && rep.title === 'Failed: 1 of 2 checks were not as expected.', rep.title);
    check('a failing step shows expected and actual', rep.steps[2].detail === 'expected 9; got 5 (3 from the start, 2 brought by triggers)' && !rep.steps[2].ok, rep.steps[2].detail);
    check('the failing step\'s row is marked', rep.failedRows.join() === '12', rep.failedRows.join());
    check('an injected event is shown but not counted as a check', !rep.steps[0].check && rep.steps[1].check);
    check('the changed scenario and the assumptions are shown', rep.notes.includes(answer.matchWhy) && rep.assumptions.includes('combat are not predicted'));
    const refused = api.rehearseReport({ passed: false, refused: 'scenario', why: 'the scenario would be refused at a mission\'s start, so it cannot be rehearsed: triggers[0].when: "teleport" is not a kind of trigger this version knows', findings: [{ level: 'error', code: 'unknown-kind', path: 'triggers[0].when', text: 'not known' }], steps: [] });
    check('a refused scenario is not run, with its findings', refused.level === 'refused' && refused.title.startsWith('Not run: the scenario would be refused') && refused.notes[0] === 'triggers[0].when: not known', refused.title);
    const blocked = api.rehearseReport({ passed: false, refused: '', blocked: 'extraction: it waits for trigger 2, which sat this mission out', assumptions: '', match: 'same', matchWhy: '', steps: [{ at: 1, what: 'step 1 at 1 s: trigger 1 has fired', expected: 'fired', actual: 'fired', ok: true }] });
    check('a roll the Director refuses fails even when every check passes', blocked.level === 'failed' && blocked.title.includes('Director would refuse') && blocked.notes[0].includes('sat this mission out'), blocked.title);
    const rerun = api.rehearseRerunReport([{ file: 'a.json', name: 'phone starts the countdown', answer: { ...answer, passed: true, steps: answer.steps.slice(0, 2), match: 'same' } },
      { file: 'b.json', name: 'reinforcements', answer }, { file: 'c.json', name: 'broken', error: 'status 404' }]);
    check('a rerun says how many kept tests fail', rerun.title === '2 of 3 kept tests fail.', rerun.title);
    check('a failing kept test is explained by step and trigger, and its changed scenario', rerun.lines[1].text.startsWith('reinforcements: failed: step 12 at 7 s: 9 enemies are in the mission (expected 9; got 5') && rerun.lines[1].text.includes('The scenario changed since'), rerun.lines[1].text);
    check('a passing kept test and an unreadable one are named', rerun.lines[0].ok && rerun.lines[0].text === 'phone starts the countdown: passed.' && !rerun.lines[2].ok && rerun.lines[2].text === 'broken: not run: status 404', rerun.lines.map(l => l.text).join(' / '));
  }
  {   // Offline, a test file opens into rows and says that the scenario has changed since it was written.
    const page = await load(script, 'phone_countdown.json', fs.readFileSync(path.join(FIXTURES, 'rehearsal', 'phone_countdown.json'), 'utf8'));
    page.api.rhLoad(trace, 'editor_trace.rehearsal.json');
    const r = page.api.state.rehearse;
    check('a test file opens as one row per step', r.rows.length === 16 && r.players === 2 && r.roll === 12345 && r.pin.revision === 3, JSON.stringify(r.pin));
    check('it says the scenario changed since the test was written', String(page.api.byId('rhPin').textContent).startsWith('The scenario changed since this test was written'), String(page.api.byId('rhPin').textContent));
    check('offline Run is off and says it needs the game; saving downloads a file', page.api.byId('rhRun').disabled === true && String(page.api.byId('rhRun').title).startsWith('Running needs the game') && page.api.byId('rhSave').textContent === 'Download test',
      String(page.api.byId('rhRun').title));
    page.api.rhLoad('{"schema":"missionlab-rehearsal/2","steps":[]}', 'later.json');
    check('a test from a newer version is not opened', r.rows.length === 16 && String(page.api.byId('rhStatus').textContent).includes('newer MissionLab'), String(page.api.byId('rhStatus').textContent));
  }
  {   // Putting players at a shaped zone whose anchor lies outside its area finds a spot that is really inside.
    const t = { pos: [50, 0, 50], radius: 5, area: { parts: [{ floor: [0, 3], outline: [[0, 0], [10, 0], [10, 10], [0, 10]], holes: [] }] } };
    const spot = api.rhInside(t.pos, t.area, q => api.membershipInside(t, q));
    check('a spot inside a shaped zone is found when its anchor is outside', spot !== null && api.membershipInside(t, spot), JSON.stringify(spot));
  }
}

console.log('painting areas (ML-027)');
{
  // A small invented map: ground at 0 m and an upper floor at 3 m over 60 x 60 m, and a zone trigger whose area is painted.
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  const GROUND = [-0.5, 2.6], text = JSON.stringify;
  const fresh = () => {
    const page = openEditor(script), api = page.api, s = api.state;
    s.map = api.makeMap(new Float32Array([...plate(-20, -20, 40, 40, 0), ...plate(-20, -20, 40, 40, 3)]), 'paint test');
    Object.assign(s, { infils: [], authored: [], things: [], objectives: [], doors: [], extractionAfter: 0, selected: -1, squad: [] });
    s.enemies = [api.newEnemy([5, 0, 5], 'Guard', 0)];
    s.zones = [api.newTrigger(1, 'zone', { pos: [5, 0, 5], radius: 12, message: 'Inside the house' })];
    api.areaEditStart({ kind: 'trigger', index: 0 }, GROUND);
    s.paint.tip = 'round'; s.paint.size = 1.5;
    return { api, s, page, area: () => s.zones[0].area, sig: () => api.areaSignature(s.zones[0].area), at: p => api.areaContains(s.zones[0].area, p) };
  };
  // The courtyard: a 20 x 20 m square with a round 4 m eraser drawn through its middle, from (6, 10) to (14, 10).
  const courtyard = t => { t.api.paintRect([0, 0], [20, 20]); t.s.paint.size = 4; t.api.paintStroke([[6, 10], [14, 10]], true); };
  {   // An L-shaped house from two rectangles: both wings are inside, the yard in the L's corner is not.
    const t = fresh();
    check('painting starts on the trigger with the chosen floor band', text(t.s.paint.target && [t.s.paint.target.kind, t.s.paint.target.index]) === '["trigger",0]' && text([t.s.paint.bottom, t.s.paint.top]) === text(GROUND));
    check('two rectangles are accepted', t.api.paintRect([0, 0], [20, 10]) === true && t.api.paintRect([10, 0], [0, 20]) === true);
    check('the L is two parts on the ground floor band', t.area().parts.length === 2 && t.area().parts.every(p => text(p.floor) === text(GROUND)), t.sig());
    check('a point in each wing is inside', t.at([15, 1, 5]) && t.at([5, 1, 15]));
    check('the seam where the wings overlap is inside', t.at([5, 1, 5]) && t.at([10, 1, 10]));
    check('the outdoor corner of the L\'s box is outside', !t.at([15, 1, 15]) && !t.at([19, 1, 19]));
    check('the trigger itself now goes by the area, not its 12 m circle', !t.api.membershipInside(t.s.zones[0], [15, 1, 15]) && t.api.membershipInside(t.s.zones[0], [15, 1, 5]));
  }
  {   // A courtyard: erasing through the middle of a rectangle leaves a hole, and saving and loading keeps it.
    const t = fresh();
    courtyard(t);
    const parts = t.area().parts;
    check('the eraser leaves one part with one hole', parts.length === 1 && parts[0].holes.length === 1, t.sig());
    check('the outline is still the 20 x 20 square', text(parts[0].outline) === '[[0,0],[20,0],[20,20],[0,20]]', text(parts[0].outline));
    check('the middle of the courtyard and both ends of the stroke are outside', !t.at([10, 1, 10]) && !t.at([6.5, 1, 10]) && !t.at([13.5, 1, 10]));
    check('the solid ring around it is inside', t.at([1, 1, 1]) && t.at([10, 1, 2]) && t.at([10, 1, 18]) && t.at([2, 1, 10]) && t.at([18, 1, 10]));
    check('the courtyard area has no findings', t.api.areaFindings(t.area()).length === 0, text(t.api.areaFindings(t.area())));
    const file = t.api.scenarioText(), obj = JSON.parse(file);
    check('a painted trigger is saved as missionlab-plan/2', obj.schema === 'missionlab-plan/2' && !!obj.mission && obj.mission.triggers[0].area !== undefined, file.slice(0, 80));
    check('the saved area is the painted one, hole included', text(obj.mission.triggers[0].area) === t.sig(), text(obj.mission.triggers[0].area));
    const again = await load(script, 'courtyard.json', file), z = again.api.state.zones[0];
    check('the saved courtyard loads again', !again.api.state.refused && !!z && !!z.area, again.banner());
    if (z && z.area) {
      check('the loaded area has the same shape', again.api.areaSignature(z.area) === t.sig(), again.api.areaSignature(z.area));
      check('after loading, the courtyard is still outside and the ring inside', !again.api.areaContains(z.area, [10, 1, 10]) && again.api.areaContains(z.area, [1, 1, 1]));
      check('saving the loaded file again gives the same text', again.api.scenarioText() === file);
    }
  }
  {   // The same 10 x 10 square by rectangle, polygon, brush and brush-then-erase is saved as the same shape.
    const made = fn => { const t = fresh(); fn(t); return t.sig(); };
    const rect = made(t => t.api.paintRect([10, 10], [0, 0]));
    const poly = made(t => t.api.paintPoly([[10, 10], [10, 0], [0, 0], [0, 10]]));    // clockwise, from another corner
    const brush = made(t => { t.s.paint.tip = 'square'; t.s.paint.size = 10; t.api.paintStroke([[5, 5]]); });
    const trimmed = made(t => {   // a 12 m square, then a 2 m square eraser along each of its four sides
      t.s.paint.tip = 'square'; t.s.paint.size = 12; t.api.paintStroke([[5, 5]]);
      t.s.paint.size = 2;
      t.api.paintStroke([[-1, -2], [-1, 12]], true); t.api.paintStroke([[11, -2], [11, 12]], true);
      t.api.paintStroke([[-2, -1], [12, -1]], true); t.api.paintStroke([[-2, 11], [12, 11]], true);
    });
    check('the rectangle gives the plain square', rect === '{"parts":[{"floor":[-0.5,2.6],"outline":[[0,0],[10,0],[10,10],[0,10]]}]}', rect);
    check('a polygon drawn the other way round gives the same signature', poly === rect, poly);
    check('a square brush covering it gives the same signature', brush === rect, brush);
    check('a bigger brush trimmed by the eraser gives the same signature', trimmed === rect, trimmed);
  }
  {   // Undo: rectangle, eraser and a corner move each come back exactly, every part and hole with them.
    const t = fresh();
    const s0 = t.sig();
    t.api.paintRect([0, 0], [20, 20]);
    const sA = t.sig();
    t.api.paintRect([30, 0], [40, 10]);
    const s1 = t.sig();
    t.s.paint.size = 4; t.api.paintStroke([[6, 10], [14, 10]], true);
    const s2 = t.sig();
    const j = t.area().parts.findIndex(p => p.holes.length);
    check('the eraser made a hole in the big part only', j >= 0 && t.area().parts.length === 2 && t.area().parts.filter(p => p.holes.length).length === 1, s2);
    check('a corner moves', t.api.areaVertex(j, -1, 0, [-1, -1]) === true && t.at([-0.5, 1, -0.4]));
    const s3 = t.sig();
    check('each step changed the shape', new Set([s0, s1, s2, s3]).size === 4);
    t.api.undo();
    check('one undo takes back the corner move, the hole still there', t.sig() === s2 && !t.at([-0.5, 1, -0.4]) && !t.at([10, 1, 10]), t.sig());
    t.api.undo();
    check('the next undo takes back the eraser: both parts, no hole', t.sig() === s1 && t.at([10, 1, 10]) && t.at([35, 1, 5]), t.sig());
    t.api.undo();
    check('the next undo takes back the second rectangle, the first stays', t.sig() === sA && t.area().parts.length === 1, t.sig());
    t.api.undo();
    check('the last undo takes back the first rectangle too', t.sig() === s0, t.sig());
  }
  {   // Limits: a polygon of 257 corners, and a 33rd part, are refused with a message and change nothing.
    const t = fresh();
    const ring = n => Array.from({ length: n }, (_, k) => [Math.round(3000 * Math.cos(2 * Math.PI * k / n)) / 100, Math.round(3000 * Math.sin(2 * Math.PI * k / n)) / 100]);
    t.api.paintRect([100, 100], [102, 102]);
    const before = t.sig();
    check('a polygon of 257 corners is refused', t.api.paintPoly(ring(257)) === false);
    check('the author is told a ring has at most 256 corners', t.page.banner().includes('at most 256 corners'), t.page.banner());
    check('the area is unchanged by the refused polygon', t.sig() === before);
    check('a polygon of 256 corners is accepted', t.api.paintPoly(ring(256)) === true && t.area().parts.length === 2);
    for (let k = 2; k < 32; k++) t.api.paintRect([k * 3 - 200, 0], [k * 3 - 198, 2]);
    const full = t.sig();
    check('32 parts are accepted', t.area().parts.length === 32, String(t.area().parts.length));
    check('a 33rd part is refused', t.api.paintRect([300, 300], [302, 302]) === false);
    check('the author is told an area has at most 32 parts', t.page.banner().includes('at most 32 parts'), t.page.banner());
    check('the area is unchanged by the refused part', t.sig() === full && !t.at([301, 1, 301]));
  }
  {   // Two floors and stairs: the ground floor courtyard copied upstairs, and a stair part whose band reaches both.
    const t = fresh();
    courtyard(t);
    check('a ground-floor-only area leaves out a point 3 m up', t.at([2, 1, 2]) && !t.at([2, 3, 2]));
    check('adding a floor is accepted', t.api.paintAddFloor() === true);
    check('the copy lies on the floor above, and painting goes on there', t.area().parts.length === 2 && text(t.area().parts[1].floor) === '[2.5,5.6]' && text([t.s.paint.bottom, t.s.paint.top]) === '[2.5,5.6]', t.sig());
    check('the copy keeps the courtyard hole', text(t.area().parts[1].holes) === text(t.area().parts[0].holes));
    check('the stair band is accepted', t.api.setPaintBand(-0.5, 5.6) === true);
    t.api.paintRect([20, 8], [23, 12]);
    check('the stairs are a third part spanning both floors', t.area().parts.length === 3 && text(t.area().parts[2].floor) === '[-0.5,5.6]', t.sig());
    check('ground floor, upstairs and the stairs are inside', t.at([2, 1, 2]) && t.at([2, 4, 2]) && t.at([21.5, 0.5, 10]) && t.at([21.5, 2.8, 10]) && t.at([21.5, 4.5, 10]));
    check('the void above the courtyard is outside, upstairs as on the ground', !t.at([10, 4, 10]) && !t.at([10, 1, 10]));
    check('above the top floor band is outside', !t.at([2, 6, 2]));
    check('a floor band upside down is refused', t.api.setPaintBand(3, 1) === false && t.s.paint.bottom === -0.5);
  }
  {   // Erasing everything leaves no area: the trigger is written as its circle again, and the file stays plan/1.
    const t = fresh();
    t.api.paintRect([0, 0], [10, 10]);
    t.s.paint.tip = 'square'; t.s.paint.size = 14;
    check('the eraser over the whole area is accepted', t.api.paintStroke([[5, 5]], true) === true);
    check('no part is left', t.area() === null || t.area().parts.length === 0, t.sig());
    const obj = t.api.scenarioObject();
    check('the file is missionlab-plan/1 again', obj.schema === 'missionlab-plan/1', obj.schema);
    check('the trigger is written as a circle, with no area key', obj.triggers && obj.triggers[0].radius === 12 && !('area' in obj.triggers[0]), text(obj.triggers && obj.triggers[0]));
    check('the circle counts again for the trigger', t.api.membershipInside(t.s.zones[0], [14, 0, 5]));
  }
  {   // The anchor: outside the area or in its courtyard, it is moved to a point inside, on the floor.
    const t = fresh();
    courtyard(t);
    t.s.zones[0].pos = [10, 0, 10];
    const p = t.api.areaInteriorPoint(t.area());
    check('the interior point is inside the area, not in the courtyard', !!p && t.at(p) && !(Math.abs(p[0] - 10) < 4.5 && Math.abs(p[2] - 10) < 4.5), text(p));
    check('the anchor in the courtyard is outside the area', !t.at(t.s.zones[0].pos));
    check('moving the anchor inside is accepted', t.api.areaAnchorInside() === true);
    check('the anchor now stands inside the area, on the ground', t.at(t.s.zones[0].pos) && t.s.zones[0].pos[1] === 0, text(t.s.zones[0].pos));
    t.api.undo();
    check('undo puts the anchor back in the courtyard', text(t.s.zones[0].pos) === '[10,0,10]', text(t.s.zones[0].pos));
    t.s.zones[0].pos = [50, 0, 50];
    t.api.areaAnchorInside();
    check('an anchor beyond the outline is moved inside too', t.at(t.s.zones[0].pos), text(t.s.zones[0].pos));
    const ring = { parts: [{ floor: [-0.5, 2.6], outline: [[0, 0], [40, 0], [40, 40], [0, 40]], holes: [[[1, 1], [39, 1], [39, 39], [1, 39]]] }] };
    const q = t.api.areaInteriorPoint(ring);
    check('a thin ring around a big hole still gets a point on the ring', !!q && t.api.areaContains(ring, q), text(q));
  }
  {   // Turning and moving there and back restore the shape; the hole and the anchor go with the outline.
    const t = fresh();
    t.api.paintRect([0, 0], [20, 10]);
    t.s.paint.tip = 'square'; t.s.paint.size = 4; t.api.paintStroke([[5, 5]], true);    // a 4 x 4 m hole at (5, 5)
    const before = t.sig(), pos = text(t.s.zones[0].pos);
    check('the hole is there to turn', t.area().parts[0].holes.length === 1 && !t.at([5, 1, 5]), before);
    t.api.areaTurn(90);
    check('a quarter turn about the middle stands the 20 x 10 m box on end', text(t.area().parts[0].outline) === '[[5,-5],[15,-5],[15,15],[5,15]]', text(t.area().parts[0].outline));
    check('the hole turns with it, to (10, 10)', !t.at([10, 1, 10]) && t.at([10, 1, 14]) && t.at([10, 1, 0]), text(t.area().parts[0].holes));
    t.api.areaTurn(-90);
    check('turning back gives the same signature', t.sig() === before, t.sig());
    t.api.areaMove(30, -7);
    check('a move takes the hole along', !t.at([35, 1, -2]) && t.at([31, 1, -6]) && t.at([5, 1, 5]) === false && !t.at([1, 1, 1]));
    check('the anchor moves with the area', text(t.s.zones[0].pos) === '[35,0,-2]', text(t.s.zones[0].pos));
    t.api.areaMove(-30, 7);
    check('moving back gives the same signature and anchor', t.sig() === before && text(t.s.zones[0].pos) === pos, t.sig());
  }
  {   // The brush joins what it touches into one part; an eraser right across a part cuts it in two; other floors stay as they were.
    const t = fresh();
    t.api.paintRect([0, 0], [10, 10]);
    t.api.setPaintBand(2.5, 5.6); t.api.paintRect([0, 0], [10, 10]); t.api.setPaintBand(-0.5, 2.6);
    const upstairs = text(t.area().parts[1]);
    t.s.paint.tip = 'square'; t.s.paint.size = 4; t.api.paintStroke([[12, 5]]);    // a 4 x 4 m square against the east side
    const ground = t.area().parts.filter(p => text(p.floor) === text(GROUND));
    check('a brush stroke against a rectangle makes them one part', ground.length === 1 && text(ground[0].outline) === '[[0,0],[10,0],[10,3],[14,3],[14,7],[10,7],[10,10],[0,10]]', t.sig());
    check('the part on the floor above is kept exactly', t.area().parts.some(p => text(p) === upstairs), t.sig());
    t.s.paint.size = 2; t.api.paintStroke([[5, -2], [5, 12]], true);
    const cut = t.area().parts.filter(p => text(p.floor) === text(GROUND));
    check('an eraser right across cuts the ground floor into two parts, without holes', cut.length === 2 && cut.every(p => !p.holes.length), t.sig());
    check('the cut is outside, both sides inside, upstairs still whole', !t.at([5, 1, 5]) && t.at([2, 1, 5]) && t.at([8, 1, 5]) && t.at([5, 4, 5]));
    check('an eraser on a floor with nothing painted there is refused and changes nothing', (() => { const b = t.sig(); t.api.setPaintBand(10, 12); const ok = t.api.paintStroke([[5, 5]], true); return ok === false && t.sig() === b && t.page.banner().includes('Nothing painted'); })(), t.page.banner());
  }
  {   // Corners and parts: inserting a corner, removing one, deleting a part, each undone in one step.
    const t = fresh();
    t.api.paintRect([0, 0], [10, 10]); t.api.paintRect([20, 0], [30, 10]);
    const two = t.sig();
    check('a corner is inserted after the first', t.api.areaInsertVertex(0, -1, 0, [5, -3]) === true && t.area().parts[0].outline.length === 5 && t.at([5, 1, -2]), t.sig());
    check('removing that corner gives the rectangle back', t.api.areaVertex(0, -1, 1, null) === true && t.sig() === two, t.sig());
    check('a triangle outline keeps its last three corners', (() => { t.api.areaVertex(0, -1, 0, null); const b = t.sig(); return t.api.areaVertex(0, -1, 0, null) === false && t.sig() === b; })(), t.sig());
    t.api.undo();
    check('undo brings the removed corner back', t.sig() === two, t.sig());
    check('deleting a part leaves the other', t.api.paintDeletePart(0) === true && t.area().parts.length === 1 && !t.at([5, 1, 5]) && t.at([25, 1, 5]));
    t.api.undo();
    check('undo brings the deleted part back', t.sig() === two, t.sig());
    check('the 3D view gets the painted trigger as one strong volume', (() => { const v = t.api.areaVolumes(); return v.length === 1 && v[0].strong && v[0].area.parts.length === 2; })());
    t.api.areaEditStop();
    check('after Done, painting is refused with a message and nothing changes', t.api.paintRect([40, 0], [50, 10]) === false && t.sig() === two && t.page.banner().includes('Choose what to paint'), t.page.banner());
  }
}

console.log('encounter modules (ML-019)');
{
  const text = JSON.stringify, MODS = path.join(ROOT, 'examples', 'modules');
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  // An empty scenario on a page of its own; with a map of one 10 x 10 m floor at height 0 when asked.
  const fresh = withMap => {
    const api = openEditor(script).api, s = api.state;
    Object.assign(s, { infils: [], authored: [], zones: [], things: [], objectives: [], doors: [], places: [], extractionAfter: 0, selected: -1, squad: [], enemies: [], example: false });
    s.map = withMap ? api.makeMap(new Float32Array(plate(0, 0, 10, 10, 0)), 'module test') : null;
    return api;
  };
  const ofCopy = (s, copy) => ({ enemies: s.enemies.filter(e => e.moduleCopy === copy), zones: s.zones.filter(z => z.moduleCopy === copy) });
  const module = (name, spawns, triggers, bindings) => ({ schema: 'missionlab-module/1', name, anchor: { pos: [0, 0, 0], yaw: 0 }, spawns, triggers, bindings: bindings || [] });
  const guard = (pos, more) => Object.assign({ group: 0, role: 'Guard', pos, yaw: 0, trigger: 0, patrol: [] }, more || {});
  for (const file of fs.readdirSync(MODS).filter(n => n.endsWith('.module.json')).sort()) {   // The shipped examples are valid and go into an empty scenario as they are.
    const api = fresh(false), s = api.state, mod = JSON.parse(fs.readFileSync(path.join(MODS, file), 'utf8'));
    check(file + ' has no problems', api.moduleProblems(mod).length === 0, api.moduleProblems(mod).join(' / '));
    const r = api.moduleInsert(mod, [0, 0, 0], 0);
    check(file + ' inserts into an empty scenario with all its enemies and triggers and nothing to bind', !r.error && s.enemies.length === mod.spawns.length && s.zones.length === mod.triggers.length && r.open.length === 0 && api.moduleBlock() === '', text(r));
    const errors = api.scenarioProblems(api.scenarioObject()).filter(f => f.level === 'error');
    check(file + ' gives a scenario the file rules accept', errors.length === 0, text(errors));
  }
  check('both example modules are shipped', fs.readdirSync(MODS).filter(n => n.endsWith('.module.json')).length === 2);
  {   // The same module put down twice: two copies with their own trigger ids, groups, lots, either-sets and counted tag.
    const api = fresh(false), s = api.state;
    const pair = module('Pair', [
      guard([0, 0, 0], { trigger: 2, chance: 50, lot: 1, tag: 'pair' }), guard([2, 0, 0], { trigger: 2, chance: 50, lot: 1, tag: 'pair' }),
      guard([0, 0, 4], { group: 1, trigger: 3, either: 1, option: 1 }), guard([2, 0, 4], { group: 1, trigger: 3, either: 1, option: 2 }),
    ], [{ id: 1, when: 'time', delay: 5, message: 'Five' }, { id: 2, when: 'after', after: 1, message: 'Then' },
      { id: 3, when: 'all', of: [1, 2], message: 'Both' }, { id: 4, when: 'down', tag: 'pair', count: 2, message: 'Pair down' }]);
    check('the test module has no problems', api.moduleProblems(pair).length === 0, api.moduleProblems(pair).join(' / '));
    const a = api.moduleInsert(pair, [0, 0, 0], 0), b = api.moduleInsert(pair, [20, 0, 0], 0);
    check('both copies go in', !a.error && !b.error && s.enemies.length === 8 && s.zones.length === 8, text([a, b]));
    check('every trigger id is used once', new Set(s.zones.map(z => z.id)).size === 8 && a.triggers.every(id => !b.triggers.includes(id)), text(s.zones.map(z => z.id)));
    for (const [r, n] of [[a, 1], [b, 2]]) {
      const own = ofCopy(s, r.copy), mine = new Set(r.triggers), [t1, t2] = r.triggers;
      check('copy ' + n + ': its enemies wait only for its own triggers', own.enemies.length === 4 && own.enemies.every(e => mine.has(e.trigger)), text(own.enemies.map(e => e.trigger)));
      const after = own.zones.find(z => z.when === 'after'), all = own.zones.find(z => z.when === 'all');
      check('copy ' + n + ': "after" and "all of" point inside the copy', after.after === t1 && text(all.of) === text([t1, t2]), text([after.after, all.of, r.triggers]));
    }
    const ca = ofCopy(s, a.copy), cb = ofCopy(s, b.copy), values = (c, f) => new Set(c.enemies.map(e => e[f]).filter(v => f === 'group' || v > 0));
    for (const f of ['group', 'lot', 'either']) {
      const va = values(ca, f), vb = values(cb, f);
      check('the copies share no ' + f + ' number', va.size > 0 && vb.size > 0 && [...va].every(v => !vb.has(v)), text([[...va], [...vb]]));
    }
    const downA = ca.zones.find(z => z.when === 'down'), downB = cb.zones.find(z => z.when === 'down');
    check('the second copy counts its own enemies under a new tag', downA.tag === 'pair' && downB.tag !== 'pair' && cb.enemies.filter(e => e.lot > 0).every(e => e.tag === downB.tag) && ca.enemies.filter(e => e.lot > 0).every(e => e.tag === 'pair'), text([downA.tag, downB.tag, cb.enemies.map(e => e.tag)]));
    const errors = api.scenarioProblems(api.scenarioObject()).filter(f => f.level === 'error');
    check('the scenario with both copies has no file errors', errors.length === 0, text(errors));
  }
  {   // Turned by 90 degrees at anchor (10, 3, 20): a point (x, y, z) of the module lands on (10 + z, 3 + y, 20 - x).
    const api = fresh(false), s = api.state;
    const area = { parts: [{ floor: [-0.5, 2.5], outline: [[0, 0], [4, 0], [4, 2], [0, 2]], holes: [[[1, 0.5], [2, 0.5], [2, 1.5]]] }] };
    const mod = module('Turned', [
      guard([2, 0, 1], { role: 'Patrol', yaw: 300, trigger: 1, patrol: [[2, 0, 1], [4, 1, 0]] }), guard([0, 0, 2], { yaw: 90, trigger: 1 }),
    ], [{ id: 1, when: 'zone', pos: [3, 0, 1], radius: 5, area, message: 'In the yard' }]);
    check('the turned test module has no problems', api.moduleProblems(mod).length === 0, api.moduleProblems(mod).join(' / '));
    const r = api.moduleInsert(mod, [10, 3, 20], 90), [p, g] = s.enemies, z = s.zones[0];
    check('enemy positions are turned and moved', !r.error && text(p.pos) === '[11,3,18]' && text(g.pos) === '[12,3,20]', text([p && p.pos, g && g.pos]));
    check('facing turns by 90 degrees, past 360 back to 30', p.yaw === 30 && g.yaw === 180, text([p.yaw, g.yaw]));
    check('the patrol route is turned and moved the same way', text(p.patrol) === '[[11,3,18],[10,4,16]]', text(p.patrol));
    check('the trigger anchor is turned and moved', text(z.pos) === '[11,3,17]', text(z.pos));
    const want = { parts: [{ floor: [2.5, 5.5], outline: [[10, 20], [10, 16], [12, 16], [12, 20]], holes: [[[10.5, 19], [10.5, 18], [11.5, 18]]] }] };
    check('the area outline and hole are turned and moved, its floor band raised by the anchor height', text(api.areaWrite(z.area)) === text(want), text(z.area));
    const back = api.moduleInsert(mod, [10, 3, 20], -270);
    check('turning by -270 is the same as by 90', !back.error && text(s.enemies[2].pos) === text(p.pos) && s.enemies[2].yaw === 30 && text(api.areaWrite(s.zones[1].area)) === text(want), text(s.enemies[2]));
  }
  {   // Keeping a selection: two enemies of a backup trigger that waits for a timer which also brings another enemy.
    const api = fresh(false), s = api.state;
    s.zones = [api.newTrigger(1, 'time', { delay: 10, message: 'Ten seconds' }), api.newTrigger(2, 'after', { after: 1, delay: 5, message: 'Backup' })];
    s.enemies = [Object.assign(api.newEnemy([10, 0, 10]), { trigger: 2 }), Object.assign(api.newEnemy([12, 0, 13], 'Patrol'), { trigger: 2, patrol: [[12, 0, 13], [14, 0, 13]] }),
      Object.assign(api.newEnemy([30, 0, 30]), { trigger: 1 })];
    s.squad = [0, 1]; s.selected = 0;
    const got = api.moduleOfSelection('Backup pair'), m = got.module;
    check('the selection is kept as a module', !!m, text(got));
    check('the outside timer becomes a trigger binding; only the backup trigger comes along',
      m.triggers.length === 1 && m.triggers[0].id === 1 && m.triggers[0].after === '@t1' && text(m.bindings) === text([{ kind: 'trigger', ref: 't1', label: 'After a set time (trigger 1)' }]), text([m.triggers, m.bindings]));
    check('positions are kept relative to the first selected enemy', text(m.anchor.pos) === '[10,0,10]' && text(m.spawns.map(e => e.pos)) === '[[0,0,0],[2,0,3]]' && text(m.spawns[1].patrol) === '[[2,0,3],[4,0,3]]' && m.spawns.every(e => e.trigger === 1), text(m.spawns));
    const read = JSON.parse(api.moduleText(m)), diff = differences(m, read, 'module', []);
    check('the module text reads back as the same module, without problems', diff.length === 0 && api.moduleProblems(read).length === 0, diff.join(' / '));
    const r = api.moduleInsert(read, [50, 0, 50], 0), open = api.moduleOpen();
    check('inserting the read-back module asks for the timer binding', !r.error && open.length === 1 && open[0].kind === 'trigger' && open[0].ref === 't1' && open[0].copy === r.copy && open[0].required, text(open));
    s.squad = [0, 1, 2];
    const whole = api.moduleOfSelection('All three').module;
    check('when the timer brings only selected enemies it comes along and nothing is bound', whole && whole.triggers.length === 2 && whole.bindings.length === 0 && whole.triggers[1].after === 1, text(whole));
  }
  {   // A module with an outside trigger and door: no save path lets the scenario out until both are bound.
    const api = fresh(false), s = api.state, msg = () => String(api.byId('saveMsg').textContent);
    s.doors = [{ name: 'Front', pos: [40, 0, 40], mid: [40.5, 0, 40], locked: null, enemiesCanOpen: null, open: null, chance: 100, isLocked: null, enemiesKeptOut: null, broken: false, known: true }];
    s.zones = [api.newTrigger(1, 'alert', { message: 'Alerted' })]; s.enemies = [api.newEnemy([0, 0, 0])];
    const mod = module('Door rush', [guard([1, 0, 0], { trigger: 1 })],
      [{ id: 1, when: 'after', after: '@t1', delay: 5, message: 'They rush the door', doors: [{ pos: '@d1', locked: false }] }],
      [{ kind: 'trigger', ref: 't1', label: 'When the enemy is alerted' }, { kind: 'door', ref: 'd1', label: 'The front door', hint: [3, 0, 0] }]);
    check('the door module has no problems', api.moduleProblems(mod).length === 0, api.moduleProblems(mod).join(' / '));
    const r = api.moduleInsert(mod, [20, 0, 20], 0), z = s.zones[1], key = ref => r.open.find(b => b.ref === ref).key;
    check('both outside references are open after inserting', !r.error && r.open.length === 2 && z.after === 0 && text(z.doors[0].pos) === '[23,0,20]', text(r));
    const held = api.moduleBlock();
    check('the save block names both references', held.startsWith('Not saved: 2 outside references') && held.includes('The front door') && held.includes('When the enemy is alerted'), held);
    check('Save and Copy are switched off', api.byId('save').disabled === true && api.byId('copy').disabled === true);
    check('the mission check lists the missing binding', api.missionCheck().some(f => f.text.includes('The front door')));
    const rev = s.revision;
    api.byId('saveMsg').textContent = ''; await api.saveFile();
    check('saving the file is refused with the reason', msg() === held && s.revision === rev, msg());
    api.byId('saveMsg').textContent = ''; await api.copyFile();
    check('copying the file text is refused with the reason', msg() === held, msg());
    api.live.on = true; api.byId('saveMsg').textContent = ''; await api.saveToCatalogue(false); api.live.on = false;
    check('saving into the game\'s scenarios folder is refused with the reason', msg() === held && s.revision === rev, msg());
    check('a trigger of the copy itself cannot be the outside trigger', api.moduleBind(key('t1'), z.id) === false && api.moduleOpen().length === 2);
    check('a door that is not on the map cannot be bound', api.moduleBind(key('d1'), 5) === false && api.moduleOpen().length === 2);
    check('binding the trigger leaves only the door', api.moduleBind(key('t1'), 1) === true && z.after === 1 && api.moduleBlock().startsWith('Not saved: 1 outside reference ') && api.moduleBlock().includes('(The front door)'), api.moduleBlock());
    check('binding the door clears the block', api.moduleBind(key('d1'), 0) === true && api.moduleBlock() === '' && api.byId('save').disabled === false, api.moduleBlock());
    const written = api.scenarioObject().triggers.find(t => t.id === z.id), file = api.scenarioText();
    check('the trigger now waits for trigger 1 and opens the map\'s front door', written.after === 1 && text(written.doors) === text([{ name: 'Front', pos: [40, 0, 40], locked: false }]), text(written));
    check('no module mark reaches the file', !file.includes('@') && !file.includes('binds') && !file.includes('moduleCopy'));
    await api.saveFile();
    check('with everything bound the scenario saves', msg().startsWith('Saved as') && s.revision === rev + 1, msg());
    api.undo();
    check('Undo takes the door binding back and holds saving again', api.moduleBlock().includes('(The front door)') && api.byId('save').disabled === true, api.moduleBlock());
  }
  {   // On a map with one 10 x 10 m floor: the alarm module at (5, 0, 5) leaves its two newcomers 9 m off the floor.
    const api = fresh(true), s = api.state, mod = JSON.parse(fs.readFileSync(path.join(MODS, 'two_guard_alarm.module.json'), 'utf8'));
    const r = api.moduleInsert(mod, [5, 0, 5], 0);
    const want = ['Enemy 3 at -9.0, 2.0 (height 0.0)', 'Route stop 1 of enemy 3 at -9.0, 2.0 (height 0.0)', 'Enemy 4 at -9.0, 8.0 (height 0.0)'];
    check('placement lists exactly the enemies and route stops off the floor', text(r.off) === text(want) && text(api.modulePlacement(r.copy)) === text(want), text(r.off));
    check('the report says the placement was checked against the map', s.moduleReport.checked === true && s.moduleReport.copy === r.copy);
    s.enemies[2].pos = [3, 0, 2]; s.enemies[2].patrol[0] = [3, 0, 2]; s.enemies[3].pos = [3, 0, 8];
    check('moved onto the floor, nothing is listed', api.modulePlacement(r.copy).length === 0, text(api.modulePlacement(r.copy)));
    check('another copy number lists nothing', api.modulePlacement(r.copy + 1).length === 0);
  }
}

console.log('Game Master budget and action cards (ML-025)');
{
  const text = JSON.stringify;
  // A by-hand trigger with a door and a message, a timer, and two enemies bound to the by-hand one.
  const fresh = () => {
    const api = openEditor(script).api, s = api.state;
    Object.assign(s, { infils: [], authored: [], things: [], objectives: [], doors: [], places: [], extractionAfter: 0, selected: -1, squad: [], example: false, director: null });
    s.zones = [api.newTrigger(12, 'after', { name: 'Rear gate', message: 'Contact rear', doors: [{ name: 'Gate', pos: [5, 0, 5], locked: false }] }), api.newTrigger(13, 'time', { delay: 60 })];
    s.enemies = [Object.assign(api.newEnemy([10, 0, 10]), { trigger: 12 }), Object.assign(api.newEnemy([12, 0, 10]), { trigger: 12 })];
    return api;
  };
  {   // Create: the director block, written in the file's order and checked by the file rules.
    const api = fresh(), s = api.state;
    check('without a budget there is nothing to add an action to', api.cardAuthorAdd() === false && s.director === null);
    api.cardAuthorOn(true);
    check('turning the budget on makes an empty block of 10 points', s.director && s.director.budget === 10 && s.director.actions.length === 0, text(s.director));
    check('a new action fires the trigger that only fires by hand', api.cardAuthorAdd() && s.director.actions[0].fire === 12 && s.director.actions[0].id === 'action1', text(s.director.actions));
    api.cardAuthorSet(0, 'name', 'Rear reinforcements'); api.cardAuthorSet(0, 'id', 'rear'); api.cardAuthorSet(0, 'cost', '3'); api.cardAuthorSet(0, 'cooldown', '60');
    api.cardAuthorSet(0, 'minDistance', '30'); api.cardAuthorSet(0, 'unseen', true); api.cardAuthorSet(-1, 'regen', '1');
    const want = { budget: 10, regen: 1, actions: [{ id: 'rear', name: 'Rear reinforcements', cost: 3, cooldown: 60, fire: 12, minDistance: 30, unseen: true }] };
    check('the file carries the block as authored, defaults left out', text(api.scenarioObject().director) === text(want), text(api.scenarioObject().director));
    check('a sound block has no findings', api.cardAuthorProblems().length === 0, text(api.cardAuthorProblems()));
    check('a second action takes the trigger not yet on a card', api.cardAuthorAdd() && s.director.actions[1].fire === 13);
    api.cardAuthorSet(1, 'id', 'rear'); api.cardAuthorSet(1, 'cost', '2000');
    const found = api.cardAuthorProblems(), at = (path, level) => found.some(f => f.path === path && f.level === level);
    check('the file rules name a repeated id and a price out of range', at('director.actions[1].id', 'error') && at('director.actions[1].cost', 'error'), text(found));
    check('the page adds that the card costs more than the budget and that its trigger fires by itself', at('director.actions[1].cost', 'note') && found.some(f => f.path === 'director.actions[1].fire' && f.text.includes('also fires by itself')), text(found));
    api.undo();
    check('Undo takes the last change back', s.director.actions[1].cost === 1 && s.director.actions[1].id === 'rear', text(s.director.actions[1]));
    check('deleting an action leaves the other', api.cardAuthorDelete(1) && s.director.actions.length === 1 && s.director.actions[0].id === 'rear');
    api.cardAuthorOn(false);
    check('turning the budget off leaves no director in the file', s.director === null && !('director' in api.scenarioObject()));
  }
  {   // Direct: cards from a budget snapshot as Core Budget.Json writes it; presses through a stand-in for the game.
    const api = fresh(), s = api.state;
    s.enemies[0].waiting = true; s.enemies[1].waiting = false; api.live.on = true; api.live.mission = true;
    const rear = { id: 'rear', name: 'Rear reinforcements', cost: 3, cooldownLeft: 0, available: true, why: '', fire: 12, cooldown: 60, minDistance: 30, unseen: true, fallback: 'wait' };
    const flare = { id: 'flare', name: 'Flare', cost: 8, cooldownLeft: 0, available: false, why: 'not enough points (needs 8, has 7)', fire: 13, cooldown: 0, minDistance: 0, unseen: false, fallback: 'cancel' };
    const snap = (points, more) => ({ mission: true, session: 'm1', budget: Object.assign({ points, budget: 10, regen: 1, on: true, actions: [rear, flare], spent: [] }, more || {}) });
    api.cardTake(snap(10));
    let rows = api.cardRows();
    check('an available card shows its effect, cost and rest, and can be played',
      rows[0].available && rows[0].enabled && rows[0].why === '' && rows[0].cost === '3 points' && rows[0].rest === 'rests 1:00 after use'
      && rows[0].effect === 'Fires trigger 12 "Rear gate": brings 1 enemy, sets 1 door, says "Contact rear"', text(rows[0]));
    check('the card states its placement rule and what happens when no safe spot comes', rows[0].rule.includes('at least 30 m from every player') && rows[0].rule.includes('out of every player\'s view') && rows[0].rule.includes('waits up to 30 s'), rows[0].rule);
    check('an unavailable card gives the game\'s reason and cannot be played', !rows[1].available && !rows[1].enabled && rows[1].why === 'not enough points (needs 8, has 7)', text(rows[1]));
    check('the meter shows the game\'s points', api.cardMeter() === '10 of 10 points, 1 back a minute', api.cardMeter());
    check('waiting for a safe spot reads as such', api.cardOpText({ status: 'pending', said: 'waiting: a player is 12 m from where an enemy would appear (needs 30 m)' }) === 'Waiting for a safe spot: a player is 12 m from where an enemy would appear (needs 30 m)');

    const sent = [], ledger = new Map();
    let answer = null;
    const io = { send: async (body, command) => { sent.push({ body, command }); if (!answer) throw new Error('Failed to fetch'); return answer(command); }, ask: async command => ledger.get(command) || { command, status: 'unknown' }, wait: async () => {} };
    const first = await api.cardPress('rear', io);
    rows = api.cardRows();
    check('no answer leaves the card to be asked again, still playable', first.status === 'lost' && rows[0].label === 'Ask again' && rows[0].enabled && rows[0].opText.includes('never paid for twice'), text(rows[0]));
    // The game took it and waits for a safe moment; its ledger then says it was cancelled and refunded.
    answer = command => {
      ledger.set(command, { command, status: 'failed', done: true, ops: [{ op: 'action', ref: 'rear', at: -1, status: 'failed', said: 'cancelled: no safe moment within 30 s (a player can see where an enemy would appear); 3 point(s) refunded' }], left: 0 });
      return Object.assign(snap(7, { spent: [{ command, id: 'rear', at: 42.5, refunded: false }] }), { result: { command, status: 'pending', ops: [{ op: 'action', ref: 'rear', at: -1, status: 'pending', said: 'waiting: a player can see where an enemy would appear' }], left: 0 } });
    };
    const second = await api.cardPress('rear', io);
    check('the retry carries the number of the first try', sent.length === 2 && sent[1].command === sent[0].command && second.command === first.command && text(sent[1].body) === text({ cmd: 'action', id: 'rear' }), text(sent));
    check('a cancelled result is shown as cancelled, with the game\'s reason', second.status === 'cancelled' && api.cardRows()[0].opText.startsWith('Cancelled: no safe moment within 30 s') && api.cardRows()[0].opText.includes('refunded'), text(second));
    check('the meter follows the game\'s points, not a sum of its own', api.cardMeter() === '7 of 10 points, 1 back a minute', api.cardMeter());
    api.cardTake(snap(10, { spent: [{ command: second.command, id: 'rear', at: 42.5, refunded: true }] }));
    check('after the game\'s refund the meter is back to 10 and the spend is marked refunded', api.cardMeter() === '10 of 10 points, 1 back a minute' && api.cardSpent()[0].refunded && api.cardSpent()[0].text === '0:43  Rear reinforcements · refunded', text(api.cardSpent()));
    answer = command => Object.assign(snap(7), { result: { command, status: 'applied', ops: [{ op: 'action', ref: 'rear', at: -1, status: 'applied', said: '1 enemy made' }], left: 0 } });
    const third = await api.cardPress('rear', io);
    check('a press after a settled one is a new request with a new number', third.command !== first.command && third.status === 'applied' && api.cardRows()[0].opText === 'Applied: 1 enemy made', text(third));
    answer = () => { const err = new Error('stale'); err.status = 409; err.result = 'stale'; throw err; };
    const fourth = await api.cardPress('rear', io);
    check('a request the game refused before running is failed, not asked again', fourth.status === 'failed' && api.cardRows()[0].label === 'Play' && fourth.said.includes('another mission'), text(fourth));
    api.cardTake(snap(2, { on: false }));
    check('with the budget rules off a card says it is free', api.cardRows()[0].cost === 'free: budget rules are off', api.cardRows()[0].cost);
    api.cardTake({ mission: true, session: 'm2' });
    check('a new mission forgets the presses of the last one, and a mission without a budget shows no cards', api.cards.ops.size === 0 && api.cardRows().length === 0 && api.cardMeter() === '');
  }
}

console.log('scenario packs (ML-026)');
{
  const text = JSON.stringify, PACKS = path.join(FIXTURES, 'packs');
  const lf = p => fs.readFileSync(path.join(ROOT, p), 'utf8').replace(/\r\n/g, '\n');
  const S = 'clean_house_quiet_raid.json', M = 'two_guard_alarm.module.json';
  const raid = lf('examples/clean_house_quiet_raid.json'), alarm = lf('examples/modules/two_guard_alarm.module.json'), valid = lf('tests/fixtures/packs/valid.mlpack.json');
  {   // The fixtures, judged as the plugin judges them (tests/Pure/PackTests.cs reads the same expected.json).
    const api = openEditor(script).api, expected = JSON.parse(fs.readFileSync(path.join(PACKS, 'expected.json'), 'utf8'));
    for (const [file, want] of Object.entries(expected)) {
      const got = api.packRead(fs.readFileSync(path.join(PACKS, file), 'utf8')).findings.filter(f => f.level === 'error').map(f => f.code + ' ' + f.path).sort();
      check(file + ' gives ' + (want.length ? want.join(', ') : 'no errors'), text(got) === text([...want].sort()), text(got));
    }
    check('every pack fixture is in expected.json', Object.keys(expected).length === fs.readdirSync(PACKS).filter(n => n.endsWith('.mlpack.json')).length);
    const w = api.packWrite('Clean House quiet raid', '2026-11-18', 'MissionLab 0.26.0', '0.26.0', [[S, raid], [M, alarm]]);
    check('packWrite gives the plugin\'s text line for line (valid.mlpack.json)', w.text === valid, w.text.slice(0, 200));
    const info = api.packRead(valid).info;
    check('each file travels as its exact text, hashed as the plugin hashes it', info.files[0].text === raid && info.files[0].hash === api.textHash(raid) && info.files[1].text === alarm, info.files[0].hash);
    check('the manifest names the map file, PvE, the version and the features', text(info.maps) === text(['suburbv2.bytes']) && text(info.modes) === text(['pve']) && info.mod === '0.26.0'
      && text(info.capabilities) === text(api.capabilitiesOf(JSON.parse(raid))), text(info));
    check('a pack with a navmesh file is not made', api.packWrite('x', '', '', '0.26.0', [[S, raid], ['suburbv2.bytes', 'PK\u0003\u0004']]).text === '');
  }
  {   // One list of feature words: the editor's, the plugin's, and every word either can produce.
    const api = openEditor(script).api;
    const known = [...fs.readFileSync(path.join(ROOT, 'src', 'MissionLab.Core', 'Pack.cs'), 'utf8').match(/Known = \{([^}]*)\}/)[1].matchAll(/"([a-z-]+)"/g)].map(m => m[1]);
    check('the editor knows the same feature words as the plugin', text([...api.PACK_KNOWN]) === text(known), text(known));
    const made = new Set([...[...fs.readFileSync(path.join(ROOT, 'src', 'MissionLab.Core', 'Capabilities.cs'), 'utf8').matchAll(/found\.Add\("([a-z-]+)"\)/g)].map(m => m[1]),
      ...[...fs.readFileSync(path.join(ROOT, 'tools', 'editor', 'src', '22-scenario.js'), 'utf8').matchAll(/add\('([a-z-]+)'/g)].map(m => m[1])]);
    check('every feature a scenario can use is a known word', [...made].every(c => known.includes(c)), [...made].filter(c => !known.includes(c)).join(','));
    check('file names follow the plugin\'s rules', api.packSafeName('My raid!.json') === 'My_raid.json' && api.packSafeName('con') === 'con_.json' && api.packSafeName('../x') === 'x.json'
      && api.packFileName('a.json', 'module') === 'a.module.json' && api.packFileName('a.module.json', 'scenario') !== 'a.module.json', api.packSafeName('My raid!.json'));
    check('versions compare by number', api.packNewer('0.27.0', '0.26.0') && !api.packNewer('0.26', '0.26.0') && !api.packNewer('next', '0.26.0'));
  }
  {   // Offline: the preview says what cannot be known, the map is unresolved, and Install opens the scenario without installing.
    const api = openEditor(script).api, s = api.state;
    const said = await api.packOpen('raid.mlpack.json', valid), p = api.packs.preview;
    check('the preview lists both files, nothing wrong', p.ok && p.files.length === 2 && p.files.every(f => !f.errors.length) && said.includes('2 files'), said);
    check('offline it says the folders cannot be checked', p.unchecked === true && p.files.every(f => !f.collides));
    check('with no map open the scenario\'s map binding is unresolved', p.files[0].map.ok === false && p.files[0].map.words.includes('suburbv2.bytes'), p.files[0].map.words);
    const done = await api.packInstall();
    check('offline Install opens the scenario in Create and installs nothing', s.title === 'Clean House: Quiet Raid' && s.enemies.length > 0 && done.includes('nothing was installed') && api.viewIs('create'), done);
    check('what opened is the author\'s scenario', s.scenarioId === 'sc-cleanhouse-quiet', s.scenarioId);
    const bad = await api.packOpen('x.mlpack.json', fs.readFileSync(path.join(PACKS, 'capability_unsupported.mlpack.json'), 'utf8'));
    check('a feature this version lacks is named and the pack cannot be installed', !api.packs.preview.ok && text(api.packs.preview.unsupported) === text(['jetpacks']) && bad.includes('cannot be installed'), bad);
    const refused = await api.packOpen('x.mlpack.json', fs.readFileSync(path.join(PACKS, 'scenario_refused.mlpack.json'), 'utf8'));
    check('a refused scenario shows the strict reader\'s finding on its file', api.packs.preview.files[0].errors.some(f => f.code === 'wrong-type' && f.path === 'files.' + S + '.spawns[0].yaw') && refused.includes('problems'), text(api.packs.preview.files[0].errors));
  }
  {   // The map open here against the scenario's map file and fingerprint.
    const api = openEditor(script).api, s = api.state, e = { navFile: 'suburbv2.bytes', mapPrint: 'fnv1a64:00000000000000aa' };
    s.map = api.makeMap(new Float32Array([0, 0, 0, 10, 0, 0, 10, 0, 10]), 'suburbv2.bytes'); s.map.print = 'fnv1a64:00000000000000aa';
    check('the same map file and fingerprint: positions match', api.packMapWords(e).ok === true);
    s.map.print = 'fnv1a64:00000000000000bb';
    check('another copy of the map file: positions need checking', api.packMapWords(e).ok === false && api.packMapWords(e).words.includes('differs'), api.packMapWords(e).words);
    check('no author fingerprint: positions need checking', api.packMapWords({ navFile: 'suburbv2.bytes', mapPrint: '' }).ok === false);
    s.map.name = 'farm.bytes';
    check('another map open: positions need checking on the pack\'s map', api.packMapWords(e).ok === false && api.packMapWords(e).words.includes('farm.bytes is open'), api.packMapWords(e).words);
    check('a scenario tied to no map needs nothing', api.packMapWords({ navFile: '' }).ok === true);
  }
  {   // Linked: collisions with the catalogue offer copy, revision or skip; Install sends the choices; the answer is told file by file.
    const api = openEditor(script).api;
    api.live.on = true;
    api.catalogue.list = [{ file: S, value: 'clean_house_quiet_raid', id: 'sc-cleanhouse-quiet', revision: 2, hash: 'fnv1a64:0000000000000001' }];
    await api.packOpen('raid.mlpack.json', valid);
    let p = api.packs.preview, f = p.files[0];
    check('a scenario with the same name and id collides', f.collides && f.clash.name && f.clash.id && f.clash.ids[0].includes('revision 2'), text(f.clash));
    check('a module the game does not keep does not', !p.files[1].collides);
    api.catalogue.list = [{ file: 'other.json', value: 'other', id: 'sc-cleanhouse-quiet', revision: 1, hash: api.textHash(raid) }];
    p = api.packPreview(valid);
    check('the identical text under another name is already there, nothing to choose', p.files[0].clash.same && !p.files[0].collides, text(p.files[0].clash));
    api.catalogue.list = [{ file: S, value: 'clean_house_quiet_raid', id: 'sc-cleanhouse-quiet', revision: 2, hash: 'fnv1a64:0000000000000001' }];
    api.syncPacks();
    api.packs.choices[S] = 'revision';
    const sent = [];
    const answer = { ok: true, pack: 'Clean House quiet raid', written: 2, files: [
      { name: S, kind: 'scenario', action: 'revision', written: true, file: 'clean_house_quiet_raid-r3.json', folder: 'BepInEx/MissionLab/scenarios', id: 'sc-cleanhouse-quiet', revision: 3 },
      { name: M, kind: 'module', action: 'install', written: true, file: M, folder: 'BepInEx/MissionLab/modules' }] };
    const said = await api.packInstall(async body => { sent.push(body); return { ok: true, status: 200, answer }; });
    check('Install sends the pack text and the choice for the colliding file only', sent.length === 1 && sent[0].pack === valid && text(sent[0].choices) === text({ [S]: 'revision' }), text(sent[0] && sent[0].choices));
    check('the answer is told file by file, nothing written over', said.includes('installed as a new revision, BepInEx/MissionLab/scenarios/clean_house_quiet_raid-r3.json') && said.includes('Nothing was written over'), said);
    const refused = await api.packInstall(async () => ({ ok: false, status: 422, answer: { ok: false, why: 'the pack has problems' } }));
    check('a refusal is passed on', refused === 'Nothing was installed: the pack has problems.', refused);
    const gone = await api.packInstall(async () => { throw new Error('Failed to fetch'); });
    check('no answer is not taken for an install', gone.includes('nothing is known to be installed'), gone);
    api.live.on = false;
  }
  {   // Export: the open scenario as it would be saved, modules chosen, a pack the reader accepts; nothing without a real scenario.
    const fresh = openEditor(script).api;
    check('the example cannot be exported', (await fresh.packExport()).includes('Open a real map file first'));
    const page = await load(script, S, raid), api = page.api;
    api.packs.extra.push({ file: M, text: alarm }); api.packs.include.add('f:0');
    const said = await api.packExport(), out = api.packs.lastExport;
    check('the pack is saved as <name>.mlpack.json', out && /\.mlpack\.json$/.test(out.name) && said.startsWith('Saved as ' + out.name), said);
    const back = api.packRead(out.text);
    check('it reads back with the scenario as its file holds it, and the module', back.pack && back.info.files.length === 2 && back.info.files[0].text === raid && back.info.files[1].text === alarm, text(back.findings.slice(0, 3)));
    check('only scenarios and modules are in it', back.info.files.every(f => f.kind === 'scenario' || f.kind === 'module') && Object.keys(JSON.parse(out.text).files).length === 2);
    check('it needs the scenario\'s map file and this version', text(back.info.maps) === text(['suburbv2.bytes']) && back.info.mod === api.EDITOR_VERSION, text(back.info.maps));
  }
}

console.log('constrained scatter and placement areas (ML-023)');
{
  const text = JSON.stringify;
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  const square = (x0, z0, x1, z1) => ({ parts: [{ floor: [-0.5, 2.5], outline: [[x0, z0], [x1, z0], [x1, z1], [x0, z1]], holes: [] }] });
  // Ground at 0 over x 0..10, a platform at 3 m over x 10..20 with nothing under it; one insertion point south.
  const fresh = () => {
    const page = openEditor(script), api = page.api, s = api.state;
    Object.assign(s, { infils: [[8, 0, -2]], insertionSpots: [], authored: [], zones: [], things: [], objectives: [], doors: [], places: [], placement: [], extractionAfter: 0, squad: [], example: false, navFile: 'synthetic.bytes', ceiling: Infinity });
    s.map = api.makeMap(new Float32Array([...plate(0, 0, 10, 40, 0), ...plate(10, 0, 20, 40, 3)]), 'scatter test');
    s.enemies = [api.newEnemy([5, 0, 20]), Object.assign(api.newEnemy([8, 0, 20]), { scatter: 6 }), Object.assign(api.newEnemy([9, 0, 24]), { scatter: 3 })];
    s.selected = 1;
    return page;
  };
  {   // The preview of the rules on a synthetic map: floor, within, empty, spacing and insertion, as Core Scatter judges them.
    const { api } = fresh(), s = api.state;
    const yard = api.addPlacement('allow'); yard.area = square(3, 14, 10, 26);
    const kill = api.addPlacement('empty'); kill.area = square(6, 18, 7.5, 22);
    check('new placement areas get the next ids and their kind', yard.id === 1 && yard.mode === 'allow' && kill.id === 2 && kill.mode === 'empty', text(s.placement.map(p => [p.id, p.mode])));
    check('an enemy scatters only within an allow area', api.setWithin(s.enemies[1], kill.id) === false && api.setWithin(s.enemies[1], yard.id) === true && s.enemies[1].within === 1);
    const g = api.scatterGrid(1), pts = [];
    for (let k = 0; k < g.points.length; k += 2) pts.push([g.points[k], g.points[k + 1]]);
    const inside = (p, x0, z0, x1, z1) => p[0] >= x0 - 0.011 && p[0] <= x1 + 0.011 && p[1] >= z0 - 0.011 && p[1] <= z1 + 0.011;
    check('the grid samples every 0.5 m within the radius', g.total > 400 && g.total < 500 && pts.every(p => Math.hypot(p[0] - 8, p[1] - 20) <= 6.001), g.total);
    check('some points are eligible', pts.length > 20, pts.length);
    check('no eligible point is on the platform 3 m up: another floor', g.removed.floor > 0 && pts.every(p => p[0] <= 10.001), text(g.removed));
    check('every eligible point is inside the within area', g.removed.within > 0 && pts.every(p => inside(p, 3, 14, 10, 26)), text(g.removed));
    check('no eligible point is inside the empty area', g.removed.empty > 0 && !pts.some(p => p[0] > 6.01 && p[0] < 7.49 && p[1] > 18.01 && p[1] < 21.99), text(g.removed));
    check('no eligible point is within 2 m of an enemy that does not scatter', g.removed.spacing > 0 && pts.every(p => Math.hypot(p[0] - 5, p[1] - 20) >= 2), text(g.removed));
    check('an enemy later in the list that scatters itself does not count yet, as in the plugin', pts.some(p => Math.hypot(p[0] - 9, p[1] - 24) < 2));
    check('no eligible point is within 20 m of an insertion point', g.removed.insertion > 0 && pts.every(p => Math.hypot(p[0] - 8, p[1] + 2) >= 20), text(g.removed));
    check('the counts add up', pts.length + Object.values(g.removed).reduce((a, b) => a + b, 0) === g.total, text(g));
    const most = Object.entries(g.removed).sort((a, b) => b[1] - a[1])[0][0];
    check('the rule that removed most points is named', g.worst === most, g.worst + ' / ' + most);
    const words = api.scatterPreviewText(g);
    check('the preview says it shows the rules, not the roll', words.includes('Preview of the rules, not the roll') && words.includes('"' + most + '"'), words);
    check('an enemy without scatter has no preview', api.scatterGrid(0) === null);
    s.map = null;
    check('without the map the preview asks for it', api.scatterGrid(1).noMap && api.scatterPreviewText(api.scatterGrid(1)).includes('Open the map file'));
  }
  {   // The fallback: "leave it out" is refused for the target person and for a gun truck, with the reason.
    const { api } = fresh(), s = api.state, e = s.enemies[1];
    check('an ordinary enemy may be left out', api.fallbackLocked(e) === '' && api.setFallback(e, 'skip') === true && e.fallback === 'skip');
    api.setFallback(e, 'stay');
    e.hvt = true;
    check('the target person cannot be left out, and the page says why', api.setFallback(e, 'skip') === false && e.fallback === 'stay' && api.fallbackLocked(e).includes('never left out'), api.fallbackLocked(e));
    e.hvt = false; e.vehicle = true;
    check('nor can a gun truck', api.setFallback(e, 'skip') === false && e.fallback === 'stay' && api.fallbackLocked(e).includes('gun truck'));
    e.vehicle = false;
    check('a radius is kept between 0 and 30 m', api.setScatter(e, 45) && e.scatter === 30 && api.setScatter(e, -2) && e.scatter === 0);
  }
  {   // Deleting an area an enemy scatters within is refused, and says who; so is making it an empty area.
    const page = fresh(), api = page.api, s = api.state;
    const yard = api.addPlacement('allow'); yard.area = square(3, 14, 10, 26);
    api.setWithin(s.enemies[1], yard.id);
    const uses = api.deletePlacement(yard.id);
    check('a placement area still in use is not deleted', text(uses) === '["enemy 2"]' && s.placement.length === 1, text(uses));
    check('turning it into an empty area is refused too', text(api.setPlacementMode(yard.id, 'empty')) === '["enemy 2"]' && yard.mode === 'allow');
    api.setWithin(s.enemies[1], 0);
    check('once nothing uses it, it is deleted', api.deletePlacement(yard.id).length === 0 && s.placement.length === 0);
    api.undo();
    check('undo brings it back', s.placement.length === 1 && s.placement[0].id === yard.id, text(s.placement));
  }
  {   // Painting a placement area uses the painting tools with kind 'placement'; the file carries it and the enemy's fields.
    const page = fresh(), api = page.api, s = api.state;
    api.addPlacement('allow');
    check('painting starts on the placement area', api.startPlacementPaint(0) === true && s.paint.target.kind === 'placement' && s.paint.target.index === 0, text(s.paint.target));
    check('a rectangle paints its shape', api.paintRect([3, 14], [10, 26]) !== false && s.placement[0].area.parts.length === 1, text(s.placement[0].area));
    api.areaEditStop();
    api.setWithin(s.enemies[1], 1); api.setFallback(s.enemies[1], 'skip');
    const file = api.scenarioObject();
    check('the file carries the area and the enemy\'s scatter, within and fallback', file.placement.length === 1 && file.placement[0].id === 1 && file.spawns[1].scatter === 6 && file.spawns[1].within === 1 && file.spawns[1].fallback === 'skip', text(file.spawns[1]));
    check('the file rules accept it', api.scenarioProblems(file).filter(f => f.level === 'error').length === 0, text(api.scenarioProblems(file)));
  }
  {   // Try seeds: offline only the preview; through the game, what was tried (never every roll) and every counterexample.
    const page = fresh(), api = page.api, s = api.state;
    const offline = await api.trySeeds(5, 2);
    check('offline, Try seeds says only the preview is there', offline.error && offline.error.includes('only the preview'), text(offline));
    let sent = null;
    const answer = { readable: true, scatter: true, navFile: 'synthetic.bytes', players: 2, seeds: 5, tried: '5 seeds tried with 2 players; not every possible roll. 1 counterexample found.',
      runs: [1, 2, 3, 4, 5].map(seed => ({ seed, moved: seed === 3 ? 1 : 2, stayed: seed === 3 ? 1 : 0, skipped: 0, refused: false, notes: [], spots: [] })),
      counterexamples: [{ seed: 3, kind: 'stayed', spawn: 1, text: 'spawns[1] stays at its authored place (8, 20): no spot passed in 24 tries (spacing 24)' }] };
    const got = await api.trySeeds(5, 2, async body => { sent = body; return { status: 200, data: answer }; });
    check('the request carries the scenario, seeds 1 to N, the players and the insertion points', sent && text(sent.seeds) === '[1,2,3,4,5]' && sent.players === 2 && text(sent.insertions) === '[[8,0,-2]]' && JSON.parse(sent.scenario).spawns[1].scatter === 6, text(sent && { seeds: sent.seeds, players: sent.players, insertions: sent.insertions }));
    const rows = api.scatterSweepRows(got);
    check('the page says how many seeds were tried and that it is not every roll', rows.head.startsWith('5 seeds tried with 2 players; not every possible roll') && rows.head.includes('9 moved, 1 stayed, 0 left out'), rows.head);
    check('every counterexample is listed with its seed and enemy', rows.rows.length === 1 && rows.rows[0].spawn === 1 && rows.rows[0].text.startsWith('Seed 3 · Stayed: spawns[1]'), text(rows.rows));
    s.enemies[1].scatter = 5;
    check('a changed scenario marks the answer as old', api.scatterSweepRows(got).head.includes('changed since'), api.scatterSweepRows(got).head);
    const refused = await api.trySeeds(5, 2, async () => ({ status: 404, data: { error: 'the map file synthetic.bytes is not in the game\'s NavMeshData folder; no seeds were tried' } }));
    check('a map the game does not have is said', refused.error.includes('NavMeshData'), text(refused));
  }
}

console.log('sealed missions (ML-021)');
{
  // The page's own window, to answer its questions (confirm) and watch its storage: a function made in the page's realm
  // returns that realm's global.
  const windowOf = api => api.setView.constructor('return this')();
  const name = 'clean_house_sealed.json', text = fs.readFileSync(path.join(ROOT, 'examples', name), 'utf8');
  {   // Offline: the shipped sealed example opens sealed, Play shows what a player may know, Create asks first.
    const page = await load(script, name, text), api = page.api, el = api.byId, win = windowOf(api);
    check(name + ' opens and is sealed', !api.state.refused && api.state.sealed === true && api.sealedHides(), page.banner());
    check('the message on opening names no enemy count', !/\d+ enem/.test(page.banner()) && page.banner().includes('Sealed'), page.banner());
    check('opening a sealed file in Create moves the page to Play', api.viewIs('play'), String(api.state.pane));
    const entry = api.pageEntry(), facts = new Map(api.catalogueFacts(entry, {}));
    check('Play: the sealed entry is valid and keeps briefing, squad, minutes, map and capabilities', entry.usable && entry.readable && entry.sealed === true && entry.briefing.length === 3
      && entry.author.squad[1] === 4 && entry.author.minutes === 15 && entry.navFile === 'suburbv2.bytes' && entry.capabilities.some(c => c.name === 'sealed'), JSON.stringify(entry));
    check('Play: the sealed entry leaves the enemy count out', entry.enemies === null, String(entry.enemies));
    check('Play: the sealed row calls it spoiler avoidance', String(facts.get('Sealed')).includes('Spoiler avoidance: the file itself stays readable'), facts.get('Sealed'));
    const e0 = api.state.enemies[0], at = api.toScreen(e0.pos[0], e0.pos[2]), got = api.hit(at[0], at[1]);
    check('a hidden enemy cannot be clicked', !got || got.enemy === undefined, JSON.stringify(got));
    api.syncInspect(true);
    check('the inspector says nothing of what triggers wait for or will do', el('inspectList').hidden === true && String(el('inspectNote').textContent).includes('sealed'), String(el('inspectNote').textContent));
    api.setView('create', true);
    check('Create stays shut when the reveal question is answered no', api.viewIs('play') && api.sealedHides(), String(api.state.pane));
    const written = [], setItem = win.localStorage.setItem;
    win.localStorage.setItem = (k, v) => { written.push(k); setItem(k, v); };
    win.confirm = () => true;
    api.setView('create', true);
    win.localStorage.setItem = setItem;
    check('answering yes reveals and opens Create', api.viewIs('create') && !api.sealedHides(), String(api.state.pane));
    check('the reveal is not kept in storage', written.every(k => k === 'missionlab-view' || k === api.DRAFT_KEY || k === 'missionlab-tab'), written.join());
    const shown = api.pageEntry();
    check('revealed, Play shows the enemy count and says revealed', shown.enemies === 8 && new Map(api.catalogueFacts(shown, {})).get('Sealed').startsWith('yes, and revealed'), JSON.stringify(shown.enemies));
    await api.openFiles([fakeFile(name, text)]);
    check('opening the same scenario again on this page keeps it revealed (by its id)', !api.sealedHides(), page.banner());
    const sealedOther = JSON.parse(text); sealedOther.id = 'sc-another-sealed';
    await api.openFiles([fakeFile('another.json', JSON.stringify(sealedOther))]);
    check('opening another sealed scenario seals the page again', api.sealedHides() && api.viewIs('play'), page.banner());
    // Errors stay visible: a reference to a missing placement area is a problem with its path, and the mission is not usable.
    api.state.enemies[0].within = 5;
    const broken = api.pageEntry();
    check('a sealed mission still shows its errors, by path', !broken.usable && broken.findings.some(f => f.level === 'error' && /spawns\[0\]\.within/.test(f.path)), JSON.stringify(broken.findings));
    api.state.enemies[0].within = 0;
  }
  {   // Another tab or a reload: a fresh page that opens the same file is sealed, whatever another page revealed.
    const page = await load(script, name, text);
    check('another tab starts sealed', page.api.sealedHides(), page.banner());
  }
  {   // Editing honestly: the author's tick seals, keeps the author's page revealed, and is written; unticking shows everything.
    const other = fs.readFileSync(path.join(ROOT, 'examples', 'clean_house_quiet_raid.json'), 'utf8');
    const page = await load(script, 'clean_house_quiet_raid.json', other), api = page.api;
    api.sealedSet(true);
    check('ticking Sealed writes sealed: true', api.state.sealed === true && /"sealed":\s*true/.test(api.scenarioText()), '');
    check('the author who ticks it still sees everything', !api.sealedHides() && api.pageEntry().enemies === 8 && new Map(api.catalogueFacts(api.pageEntry(), {})).get('Sealed').startsWith('yes, and revealed'));
    api.undo();
    check('undo takes the seal off again', api.state.sealed === false && !/"sealed"/.test(api.scenarioText()), '');
    api.sealedSet(false);
    check('unsealed, nothing is hidden and Play has no sealed row', !api.sealedHides() && !new Map(api.catalogueFacts(api.pageEntry(), {})).has('Sealed'));
  }
  {   // Places leave findings and reasons the same way as the plugin's CatalogueInfo.Unplaced.
    const api = openEditor(script).api;
    check('the page leaves a place in brackets out', api.sealedStrip('the target person at (12.5, -4) is not in this mission: lost its roll') === 'the target person is not in this mission: lost its roll');
    check('the page leaves a place after "from" out', api.sealedStrip('it can be seen from 12, -34 in the painted area') === 'it can be seen in the painted area');
    check('the page keeps lists of trigger ids', api.sealedStrip('needs triggers 3, 4 and 5') === 'needs triggers 3, 4 and 5');
  }
  {   // Live: the running mission is sealed by the game's word, or by its catalogue entry for a plugin that does not say so.
    const api = openEditor(script).api, el = api.byId;
    api.live.on = true; api.live.mission = true; api.live.session = 'm1';
    api.syncModes({ mission: true, sealed: true, scenarioId: 'sc-cleanhouse-sealed', session: 'm1', run: 'r1.run.json' });
    check('a sealed mission starting moves Create to Direct', api.viewIs('direct') && api.sealedHides(), String(api.state.pane));
    api.syncSeal();
    check('Direct says the mission is sealed and offers the reveal', el('sealBox').hidden === false && String(el('sealNote').textContent).includes('spoiler avoidance') && el('sealReveal').textContent === 'Reveal the mission', String(el('sealNote').textContent));
    api.catalogue.list = [{ file: 'y.json', value: 'y', id: 'sc-y', sealed: true, lastRun: null }, { file: 'z.json', value: 'z', id: '', sealed: true, lastRun: { file: 'r9.run.json' } }];
    check('without the game\'s word the catalogue entry by id says sealed', api.sealedLive({ mission: true, scenarioId: 'sc-y' }));
    check('without an id the run record finds the entry', api.sealedLive({ mission: true, run: 'r9.run.json' }));
    check('a refused scenario leaves the game\'s own mission, which is not sealed', !api.sealedLive({ mission: true, scenarioId: 'sc-y', rejected: 'no' }));
    check('an unsealed mission is not sealed', !api.sealedLive({ mission: true, scenarioId: 'sc-other' }));
    api.sealedReveal();
    check('revealing shows the running mission and says revealed', !api.sealedHides() && api.sealing.keys.has('id:sc-cleanhouse-sealed'));
    api.syncSeal();
    check('Direct labels it revealed', String(el('sealNote').textContent).includes('revealed on this page') && el('sealReveal').hidden === true, String(el('sealNote').textContent));
    api.live.session = 'm2';
    api.syncModes({ mission: true, sealed: true, scenarioId: 'sc-two', session: 'm2' });
    check('the next sealed mission is hidden again', api.sealedHides());
    api.live.mission = false;
    api.syncModes({ mission: false });
    check('when it ends, Direct offers the reveal now the run is over', api.sealing.ended === 'id:sc-two' && el('sealBox').hidden === false && el('sealReveal').textContent === 'Reveal now the run is over', api.sealing.ended);
    api.sealedRevealEnded();
    check('revealing after the run reveals that scenario', api.sealing.keys.has('id:sc-two') && api.sealing.ended === '');
    const over = { file: 'z2.json', value: 'z2', id: 'sc-z', sealed: true, usable: true, lastRun: { file: 'r', session: 'm0', started: '2026-11-17T10:00:00Z', outcome: 'used', result: 'success', minutes: 12 } };
    check('Play: a sealed entry whose run has ended offers the reveal', api.sealedEntryHides(over) && api.sealedRunOver(over));
    api.live.mission = true; api.live.session = 'm0';
    check('Play: not while that run is still going', !api.sealedRunOver(over));
    api.live.mission = false;
    api.sealedRevealEntry(over);
    check('Play: revealed, the entry says so', !api.sealedEntryHides(over) && new Map(api.catalogueFacts(over, {})).get('Sealed').startsWith('yes, and revealed'));
  }
  {   // A replay of a sealed mission opens only once the mission is revealed.
    const api = openEditor(script).api, win = windowOf(api);
    const rec = { schema: 'missionlab-replay/1', session: 'm7', frames: [[0, [[1, 0, 0, 0, 0]], [[2, 1, 0, 1, 0, 'I']]], [1, [[1, 0, 0, 0, 0]], []]], events: [], plan: { schema: 'missionlab-plan/1', id: 'sc-replayed', sealed: true, spawns: [{ pos: [1, 0, 1] }] } };
    let refused = '';
    try { api.loadReplay(JSON.parse(JSON.stringify(rec)), 'r.json'); } catch (err) { refused = err.message; }
    check('a sealed replay stays closed when the question is answered no', refused.includes('sealed') && !api.replay.data, refused);
    win.confirm = () => true;
    api.loadReplay(JSON.parse(JSON.stringify(rec)), 'r.json');
    check('answered yes, it opens and the mission counts as revealed', !!api.replay.data && api.sealing.keys.has('id:sc-replayed'));
  }
}

console.log('host options and the quick mission (ML-022)');
{
  const windowOf = api => api.setView.constructor('return this')();
  const reply = (status, body) => ({ status, ok: status === 200, json: async () => body, text: async () => JSON.stringify(body), arrayBuffer: async () => new ArrayBuffer(0) });
  {   // What a choice changes, in words; the author's name stays the author's.
    const api = openEditor(script).api;
    check('a choice says what it drops and how many waves', api.optionChoiceWords({ lots: [3], triggers: [7], respawnTimes: 1, skill: '' }) === 'drops enemies of lot 3 and trigger 7; 1 reinforcement wave', api.optionChoiceWords({ lots: [3], triggers: [7], respawnTimes: 1, skill: '' }));
    check('several lots, unlimited waves and skill are named', api.optionChoiceWords({ lots: [4, 3], triggers: [], respawnTimes: 0, skill: 'veteran' }) === 'drops enemies of lots 3 and 4; reinforcement waves without limit; every enemy a veteran');
    check('a choice that changes nothing says so', api.optionChoiceWords({ lots: [], triggers: [], respawnTimes: -1, skill: '' }) === 'changes nothing');
    const hard = api.optionLabel({ name: 'Hard', lots: [], triggers: [], respawnTimes: 2, skill: '' }, 0);
    check('the label is the author\'s name and the concrete change, no difficulty of its own', hard === 'Hard — 2 reinforcement waves' && !/easier|harder|difficult/i.test(hard), hard);
    const options = api.readOptions([{ id: 'garrison', name: 'Garrison size', pick: 1, choices: [{ name: 'Light', drop: { lots: [3] } }, { name: 'Full' }] }, { id: 'alarm', name: 'Alarm', choices: [{ name: 'Loud' }, { name: 'Quiet', drop: { triggers: [7] } }] }]);
    const body = api.optionsPicksBody(options, { garrison: 0, alarm: 9, other: 1 });
    check('the picks payload names every option once, out-of-range picks fall back to the default', JSON.stringify(body) === '{"picks":{"garrison":0,"alarm":0}}', JSON.stringify(body));
    check('no picks at all are the author\'s defaults', JSON.stringify(api.optionsPicksBody(options, {})) === '{"picks":{"garrison":1,"alarm":0}}');
    check('a run record\'s picks are named from the plan it kept', api.optionsRunWords({ picks: { garrison: 0 }, resolved: { options: [{ id: 'garrison', name: 'Garrison size', choices: [{ name: 'Light' }, { name: 'Full' }] }] } }, []) === 'Garrison size: Light');
    check('a record without picks says why there are none', api.optionsRunWords({ roll: 4 }, options).startsWith('no options recorded'));
    const rows = api.optionsValidateRows({ readable: true, findings: [
      { level: 'error', code: 'unfinishable', path: 'extraction', text: 'with garrison=0 \'Light\', 2 players and roll 3: the extraction can never open (5 of 80 tries fail)' },
      { level: 'note', code: '', path: 'options', text: 'coverage is sampled: 81 combinations of choices is more than 64, so 64 of them were tried' },
      { level: 'note', code: '', path: '', text: 'tried 64 combinations of choices, squad sizes 1 to 4 and rolls 1 to 20: a sample of rolls, not proof that every roll can be finished' }] }, 200);
    check('an unfinishable combination is listed with its squad size and roll', rows[0].level === 'error' && rows[0].text.startsWith('Unfinishable: with garrison=0 \'Light\', 2 players and roll 3'), JSON.stringify(rows[0]));
    check('the sampled-coverage note is shown word for word', rows.some(r => r.level === 'note' && r.text === 'coverage is sampled: 81 combinations of choices is more than 64, so 64 of them were tried'));
    check('no "nothing found" line when something was found', !rows.some(r => r.level === 'ok'));
    check('a refused scenario says its options were not tried', api.optionsValidateRows({ readable: false, findings: [{ level: 'error', path: 'options[0].id', text: 'bad id' }] }, 200)[0].text.includes('could not be tried'));
    check('an error answer says nothing was checked', api.optionsValidateRows({ error: 'the scenario text is missing; nothing was checked' }, 400)[0].text.startsWith('Not checked: the scenario text is missing'));
  }
  {   // Create: warnings, the offline check on the default picks, the game's check of every combination.
    const scen = { schema: 'missionlab-plan/1', name: 'Garrison', id: 'sc-gar', navFile: 'suburbv2.bytes', respawn: { after: 20, times: 2 },
      triggers: [{ id: 7, when: 'alert', name: 'Alarm', delay: 5 }],
      spawns: [{ pos: [0, 0, 0], hvt: true, chance: 50, lot: 3 }, { pos: [5, 0, 5], chance: 50, lot: 3 }, { pos: [10, 0, 10], chance: 50, lot: 4 }, { pos: [12, 0, 12], trigger: 7 }],
      options: [{ id: 'garrison', name: 'Garrison size', choices: [{ name: 'Light', drop: { lots: [3], triggers: [7] }, respawnTimes: 1 }, { name: 'Full' }] }] };
    const page = await load(script, 'garrison.json', JSON.stringify(scen)), api = page.api, win = windowOf(api);
    check('the options are read into the page', api.state.options.length === 1 && api.state.options[0].choices[0].lots[0] === 3, JSON.stringify(api.state.options));
    const warn = api.optionWarnings(api.state.options[0], 0);
    check('a choice that drops the target\'s lot is flagged', warn.some(w => w.includes('holds the target person')), warn.join(' | '));
    const before = api.state.enemies.length;
    await api.optionsCheck();
    const rows = api.opts.check.rows;
    check('offline the check says the full check needs the game\'s plugin', rows[0].text.startsWith('The full check of every combination needs the game\'s plugin') && rows[0].text.includes('default picks only (Garrison size: Light)'), rows[0].text);
    check('and the mission check on the default picks finds the target left out', rows.some(r => r.level === 'error' && r.text.includes('leave out the target person')), JSON.stringify(rows));
    check('the page is as it was after the check', api.state.enemies.length === before && api.state.zones.length === 1);
    let sent = null;
    win.fetch = async (url, init) => { sent = { url, init }; return reply(200, { readable: true, findings: [{ level: 'note', path: '', text: 'tried 2 combinations of choices, squad sizes 1 to 4 and rolls 1 to 20: a sample of rolls, not proof that every roll can be finished' }] }); };
    api.live.on = true;
    await api.optionsCheck();
    const asked = sent && JSON.parse(sent.init.body);
    check('linked, the check asks the game with the scenario text and 20 rolls', sent && sent.url === '/api/options/validate' && sent.init.method === 'POST' && sent.init.headers['X-MissionLab'] === '1' && asked.seeds === 20 && asked.scenario === api.scenarioText(), sent && sent.init.body.slice(0, 80));
    check('and lists the game\'s answer', api.opts.check.rows[0].level === 'ok' && api.opts.check.rows[1].text.startsWith('tried 2 combinations'), JSON.stringify(api.opts.check.rows));
    win.fetch = async (url, init) => { sent = { url, init }; return reply(200, { picks: JSON.parse(init.body).picks }); };
    await api.optionsPick(api.state.options, { garrison: 1 });
    check('Play sends the picks for the next mission', sent.url === '/api/options' && sent.init.body === '{"picks":{"garrison":1}}' && api.opts.picks.garrison === 1, sent.init.body);
    check('and says what the game holds', api.opts.note.includes('Garrison size: Full'), api.opts.note);
  }
  {   // The quick mission: the request, the preview's answer, Keep as a new scenario.
    const api = openEditor(script).api, win = windowOf(api), el = api.byId;
    api.state.navFile = 'suburbv2.bytes'; api.state.infils = [[1, 0, 2]]; api.state.authored = [];
    api.state.doors = [{ name: 'Door_1', pos: [3, 0, 3], mid: [3, 0, 3], known: true, isLocked: true, locked: null, enemiesCanOpen: null, open: null, chance: 100 }, { name: 'Old', pos: [9, 0, 9], mid: [9, 0, 9], known: false, locked: true, enemiesCanOpen: null, open: null, chance: 100 }];
    const form = { navFile: 'suburbv2.bytes', enemies: 12, guard: 40, wander: 35, patrol: 25, alert: true, ambush: false, doors: true, seed: 99 };
    const body = api.quickRequest(form);
    check('the request carries map, count, mix, extras and seed', JSON.stringify(body) === JSON.stringify({ navFile: 'suburbv2.bytes', enemies: 12, mix: { guard: 40, wander: 35, patrol: 25 }, extras: ['alert', 'doors'], seed: 99, infils: [[1, 0, 2]], doors: [{ name: 'Door_1', pos: [3, 0, 3], locked: true }] }), JSON.stringify(body));
    check('another map gets none of this map\'s points or doors', !('infils' in api.quickRequest(Object.assign({}, form, { navFile: 'depotraid1.bytes' }))));
    check('offline the form says it needs the game', (await api.quickGenerate(), api.quick.note.includes('needs the game')), api.quick.note);
    const plan = { schema: 'missionlab-plan/1', name: 'Quick mission 99', seed: 99, scene: '', navFile: 'suburbv2.bytes', requestedEnemies: 2, groups: 1, notes: ['extras: none'], triggers: [], objectives: [], doors: [],
      spawns: [{ group: 0, role: 'Guard', pos: [1, 0, 1], yaw: 90 }, { group: 0, role: 'Patrol', pos: [3, 0, 3], yaw: 0, patrol: [[3, 0, 3], [9, 0, 3]] }] };
    const answer = { navFile: 'suburbv2.bytes', seed: 99, enemies: 2, placed: 2, roles: { guard: 1, wander: 0, patrol: 1 }, later: 0, doorsLocked: 0, notes: ['extras: none'], leftOut: ['moved intel: never part of a quick mission'], plan };
    let sent = null;
    win.fetch = async (url, init) => { if (url === '/api/generate') { sent = init; return reply(200, answer); } return reply(404, null); };
    api.live.on = true; api.live.mission = false;
    el('qmMap').value = 'suburbv2.bytes'; el('qmEnemies').value = '2'; el('qmGuard').value = '1'; el('qmWander').value = '0'; el('qmPatrol').value = '1'; el('qmSeed').value = '99';
    el('qmAlert').checked = false; el('qmAmbush').checked = false; el('qmDoors').checked = false;
    await api.quickGenerate();
    check('Generate posts the form to /api/generate', sent && JSON.parse(sent.body).seed === 99 && JSON.parse(sent.body).enemies === 2 && sent.headers['X-MissionLab'] === '1', sent && sent.body);
    check('the answer is previewed, not yet part of the scenario', api.quick.result === answer && !api.state.enemies.some(e => e.pos[0] === 1 && e.pos[2] === 1), api.quick.note);
    check('the preview says what was placed', api.quick.note === 'Seed 99 on suburbv2.bytes: 2 enemies placed (1 guard, 0 wanderers, 1 patrol).', api.quick.note);
    api.live.mission = true;
    check('Keep waits while a mission runs', api.quickKeep(api.quick.result).kept === false && api.quick.result === answer);
    api.live.mission = false;
    const kept = api.quickKeep(api.quick.result);
    check('Keep opens the layout as the page\'s scenario', kept.kept && api.state.enemies.length === 2 && api.state.enemies[1].role === 'Patrol' && api.state.title === 'Quick mission 99' && api.state.navFile === 'suburbv2.bytes', kept.text);
    check('as a new scenario with a fresh id, not yet saved', /^sc-[0-9a-z]{10}$/.test(api.state.scenarioId) && api.state.revision === 0 && api.scenarioObject().id === api.state.scenarioId && api.quick.result === null && api.viewIs('create'), api.state.scenarioId);
    check('between missions the page keeps it until saved', api.catalogue.lobbyFile === 'quick_mission_99.json', api.catalogue.lobbyFile);
  }
}

console.log('debrief and unused content (ML-024)');
{
  // The same recordings and expected text as tests/Pure/DebriefTests.cs: the page's debrief is the plugin's, word for word.
  const dir = path.join(FIXTURES, 'debrief'), read = n => fs.readFileSync(path.join(dir, n), 'utf8').replace(/\r\n/g, '\n');
  const text = v => JSON.stringify(v);
  const api = openEditor(script).api;
  const raid = () => JSON.parse(read('raid.replay.json')), old = () => JSON.parse(read('old.replay.json'));
  const d = api.debriefOf(raid(), JSON.parse(read('raid.run.json')), '20261118-120000_Suburb.json');
  check('the page writes the plugin\'s debrief text (semantics 4, with its run record)', d.text === read('raid.expected.txt').trimEnd(), '\n' + d.text);
  check('every count names the event or record it came from', d.facts.every(f => f.count < 0 || f.source !== ''), text(d.facts.map(f => f.key + '=' + f.source)));
  const fact = (x, k) => x.facts.find(f => f.key === k);
  check('edits and unexplained disappearances are counted apart from enemies down',
    fact(d, 'down').count === 1 && fact(d, 'removed').count === 1 && fact(d, 'remade').count === 1 && fact(d, 'gone').count === 1);
  check('health, shots and door use are named as not recorded, never as numbers', fact(d, 'notRecorded').value === 'health, shots fired, door use by players' && fact(d, 'notRecorded').count < 0);
  check('the three message lines are the plugin\'s', text(d.message) === text(['Debrief: 1:56 recorded, first contact at 0:30.',
    'Enemies down: 1; 2 taken out by edits, not counted; 1 gone, reason not known.', 'Triggers fired: 1 of 2; Game Master cards fired: 1.']), text(d.message));
  const o = api.debriefOf(old(), null, 'old.replay.json');
  check('the page writes the plugin\'s debrief text (semantics 2, no run record)', o.text === read('old.expected.txt').trimEnd(), '\n' + o.text);
  check('an older recording marks its guesses', fact(o, 'down').guess && fact(o, 'contact').guess && fact(o, 'fired').guess && o.milestones.every(m => m.guess) && !fact(o, 'removed').guess);
  let refused = '';
  try { api.debriefOf({ schema: 'missionlab-plan/1', spawns: [] }, null, 'x.json'); } catch (e) { refused = e.message; }
  check('a scenario is not taken for a recording', refused.includes('not a MissionLab recording'), refused);

  // Opening a recording fills the Replay tab's Debrief block, read before playback tidies the rows of an older recording.
  api.loadReplay(old(), 'old.replay.json');
  check('opening a recording shows its debrief in the Replay tab', api.byId('debriefBox').hidden === false && api.byId('dbText').textContent === o.text, String(api.byId('dbText').textContent));
  check('offline, the block says it comes from the recording alone', String(api.byId('dbStatus').textContent).includes('recording alone'));
  api.loadReplay(raid(), 'raid.replay.json');
  check('another recording replaces it', String(api.byId('dbText').textContent).startsWith('MissionLab debrief: raid.replay.json\n'));

  // Offline unused content: one recording the author opens, compared with nothing; the plugin writes the same lines for it.
  const u = api.unusedFromRecording(raid(), 'raid.replay.json');
  check('the one-run report is the plugin\'s text', u.text === read('raid.unused.txt').trimEnd(), '\n' + u.text);
  check('the report is shown in the Mission tab', api.byId('unusedText').hidden === false && api.byId('unusedText').textContent === u.text);
  check('suggestions name triggers that did not fire and authored enemies that stayed idle, never markers',
    text(u.group.suggestions.map(s => s.kind + ':' + s.ref)) === text(['trigger:2', 'trigger:3', 'enemy:s0', 'enemy:s3', 'enemy:s4']), text(u.group.suggestions.map(s => s.ref)));
  api.state.scenarioId = 'another';
  api.unusedFromRecording(raid(), 'raid.replay.json');
  check('a recording of another scenario says so', String(api.byId('unusedStatus').textContent).includes('not of the open one'), String(api.byId('unusedStatus').textContent));
  const g = api.unusedOf([api.debriefOf(old(), null, 'old.replay.json')]);
  check('a recording without fire events or identities judges neither triggers nor enemies', g.suggestions.length === 0 && g.triggerRuns === 0 && g.enemyRuns === 0, text(g));
}

console.log('editor -> plugin -> editor');
{
  // Every kind of awkward text: quotes, backslashes, letters of other scripts, a character outside the BMP, control characters.
  const odd = 'Quote " back\\slash /slash tab\t bell\u0007 nul\u0000 esc\u001b del\u007f é ß Ελλάδα 漢字 𝄞 🙂 \u2028 end';
  const oddFile = JSON.stringify({
    schema: 'missionlab-plan/1', name: odd, scene: odd, navFile: odd + '.bytes', insertion: odd, briefing: [odd, 'Second line'],
    triggers: [
      { id: 1, name: odd, when: 'intel', item: odd, message: odd, doors: [{ name: odd, pos: [1, 0, 1], locked: true }] },
      { id: 2, name: 'line\nbreak', when: 'after', after: 1, message: 'a\r\nb' },
    ],
    doors: [{ name: odd, pos: [1, 0, 1], locked: false }],
    things: [{ name: odd, pos: [2, 0, 2], yaw: 45, trigger: 1 }],
    spawns: [{ pos: [0, 0, 0], type: odd, tag: 'Wache ü-1', trigger: 2 }],
  });
  const sources = [
    ['all_fields.json', fs.readFileSync(path.join(FIXTURES, 'all_fields.json'), 'utf8')],
    ['all_fields_v2.json', fs.readFileSync(path.join(FIXTURES, 'all_fields_v2.json'), 'utf8')],
    ['all_fields_v26.json', fs.readFileSync(path.join(FIXTURES, 'all_fields_v26.json'), 'utf8')],
    ['all_fields_v27.json', fs.readFileSync(path.join(FIXTURES, 'all_fields_v27.json'), 'utf8')],
    ['all_fields_v4.json', fs.readFileSync(path.join(FIXTURES, 'all_fields_v4.json'), 'utf8')],
    ['legacy_minimal.json', fs.readFileSync(path.join(FIXTURES, 'legacy_minimal.json'), 'utf8')],
    ...fs.readdirSync(path.join(ROOT, 'examples')).filter(n => n.endsWith('.json')).sort().map(n => ['examples/' + n, fs.readFileSync(path.join(ROOT, 'examples', n), 'utf8')]),
    ['awkward_text.json', oddFile],
  ];
  // First leg: the editor reads each file and writes it.
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'missionlab-editor-'));
  const legs = [];
  for (const [name, text] of sources) {
    const page = await load(script, path.basename(name), text);
    const ok = !page.api.state.refused && page.banner().includes('enemies loaded from');
    check(name + ' loads in the editor', ok, page.banner());
    if (!ok) continue;
    const first = page.api.scenarioText(), file = path.join(temp, String(legs.length) + '_' + path.basename(name));
    fs.writeFileSync(file, first, 'utf8');
    legs.push({ name, file, first, state: page.api.state });
  }
  let plugin = null;
  try { plugin = pluginRoundTrip(legs.map(l => l.file)); }
  catch (err) { check('the plugin writes the editor\'s files back', false, err.message); }
  // In CI a missing prerequisite is a failure, not a quietly skipped check (ML-028).
  if (plugin === null && process.env.CI) check('the plugin\'s leg of the round trip runs in CI (dotnet is required there)', false, 'dotnet not found');
  else if (plugin === null) console.log('  dotnet not found: the plugin\'s leg of the round trip is skipped; the editor reads its own files back');
  // Second leg: the editor reads what the plugin wrote (or, without dotnet, its own text) and writes it again.
  const again = new Map();
  for (const leg of legs) {
    const back = plugin === null ? leg.first : plugin.get(leg.file);
    if (back === undefined) { check(leg.name + ' comes back from the plugin', false, 'no output for ' + leg.file); continue; }
    let parsed = null; try { parsed = JSON.parse(back); } catch (e) { /* reported below */ }
    check(leg.name + ': the plugin writes JSON', parsed !== null, back.slice(0, 300));
    if (parsed === null) continue;
    const page = await load(script, path.basename(leg.name), back);
    check(leg.name + ': the editor reads the plugin\'s file without a finding', !page.api.state.refused && !page.api.state.readNotes.length, page.banner() + ' ' + page.api.state.readNotes.join(' '));
    const second = page.api.scenarioText();
    const diff = differences(JSON.parse(leg.first), JSON.parse(second), 'scenario', []);
    check(leg.name + ' keeps what it does through editor, plugin and editor', diff.length === 0, '\n        ' + diff.join('\n        '));
    again.set(leg.name, { page, written: JSON.parse(second) });
  }
  fs.rmSync(temp, { recursive: true, force: true });

  const odds = again.get('awkward_text.json');
  if (odds) {
    const s = odds.page.api.state, w = odds.written;
    const clean = t => t.replace(/\s+/g, ' ').trim();    // what the editor does to messages and briefing lines, on purpose
    check('awkward text: the scenario name', s.title === odd, JSON.stringify(s.title));
    check('awkward text: scene, map file and insertion', s.scene === odd && s.navFile === odd + '.bytes' && s.insertion === odd, JSON.stringify([s.scene, s.navFile, s.insertion]));
    check('awkward text: briefing', JSON.stringify(s.briefing) === JSON.stringify([clean(odd), 'Second line']), JSON.stringify(s.briefing));
    check('awkward text: trigger name, item and message', s.zones[0].name === odd && s.zones[0].item === odd && s.zones[0].message === clean(odd), JSON.stringify(s.zones[0]));
    check('awkward text: line breaks in a trigger name are kept, in a message they become spaces', s.zones[1].name === 'line\nbreak' && s.zones[1].message === 'a b', JSON.stringify([s.zones[1].name, s.zones[1].message]));
    check('awkward text: door names', s.zones[0].doors[0].name === odd && s.doors.length === 1 && s.doors[0].name === odd, JSON.stringify(w.doors));
    check('awkward text: thing name, enemy type and tag', s.things[0].name === odd && s.enemies[0].type === odd && s.enemies[0].tag === 'Wache ü-1', JSON.stringify([s.things[0].name, s.enemies[0].type, s.enemies[0].tag]));
  }
}

// ---------------------------------------------------------------- one file per ticket in tests/editor/cases, run in name order
// Each exports a default async function that receives the harness: { script, openEditor, load, fakeFile, check, differences, ROOT, FIXTURES }.
{
  const dir = path.join(ROOT, 'tests', 'editor', 'cases');
  const files = fs.existsSync(dir) ? fs.readdirSync(dir).filter(n => n.endsWith('.mjs')).sort() : [];
  for (const name of files) {
    const mod = await import(pathToFileURL(path.join(dir, name)).href);
    try { await mod.default({ script, openEditor, load, fakeFile, check, differences, ROOT, FIXTURES }); }
    catch (err) { check(name + ' runs to its end', false, err && err.stack ? err.stack : String(err)); }
  }
}

console.log(failures === 0 ? 'ALL ' + checks + ' EDITOR CHECKS PASSED' : failures + ' of ' + checks + ' EDITOR CHECKS FAILED');
process.exit(failures === 0 ? 0 : 1);
