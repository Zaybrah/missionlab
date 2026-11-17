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
import { fileURLToPath } from 'node:url';

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
    // Text that is not JSON never reaches the strict reader: the page says it could not be opened. That is the plugin's not-json.
    let parses = true; try { JSON.parse(text); } catch (e) { parses = false; }
    const got = !parses && !refused && page.banner().includes(name + ' could not be opened') ? ['not-json ']
      : refused ? refused.errors.map(f => f.code + ' ' + f.path) : [];
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
  if (plugin === null) console.log('  dotnet not found: the plugin\'s leg of the round trip is skipped; the editor reads its own files back');
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

console.log(failures === 0 ? 'ALL ' + checks + ' EDITOR CHECKS PASSED' : failures + ' of ' + checks + ' EDITOR CHECKS FAILED');
process.exit(failures === 0 ? 0 : 1);
