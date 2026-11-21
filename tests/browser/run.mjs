// The editor in a real browser: node tests/browser/run.mjs (Node 22 or later, no packages; Chromium or Microsoft Edge).
// tests/editor/run.mjs checks the page's logic quickly with a stand-in for the browser. This suite opens the distributed
// page (tools/editor/index.html, as build.py makes it) in a headless Chromium and works it with real pointer and keyboard
// input sent over the DevTools Protocol, so pointer capture, key handling, layout, downloads and WebGL are the browser's own.
//
// Five workflows, each in a fresh tab on synthetic data made here (no map or recon file from the game):
//   1. open a scenario through the file picker and save it: the download is the file that was opened, revision counted up;
//   2. paint a rectangle, drag a corner and give it up with Esc, finish a drag, insert a corner, move the whole area, and
//      give a drag up by pointercancel;
//   3. select and move enemies on a two-floor map with Shift-drag boxes, PageDown/PageUp and drags;
//   4. Draft mode against a stand-in for the plugin's browser link (fixture.mjs): a change refused as meant for another
//      mission, a change the game has begun and not finished, and retries that carry the same command number;
//   5. 2D to 3D and back: no uncaught errors, and the 3D view draws each enemy where its map position projects.
// Workflows 1-3 and 5 open the page from disk (file://), as a host does offline: served from 127.0.0.1 it links to the game.
//
// Browser: CHROME_PATH, else Edge in its Windows install folders, else google-chrome, chromium or chromium-browser on PATH.
// Prints one line per check and a summary. Exit 0: all passed; 1: a check failed; 2: no browser or Node too old to run them.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { findBrowser, launch, sleep, CTRL, SHIFT } from './browser.mjs';
import { startFixture, spawn } from './fixture.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const PAGE = pathToFileURL(path.join(ROOT, 'tools', 'editor', 'index.html')).href;

let checks = 0, failures = 0;
function check(what, ok, more) {
  checks++;
  if (ok) { console.log('PASS browser: ' + what); return; }
  failures++;
  console.log('FAIL browser: ' + what + (more ? ' | ' + more : ''));
}
const note = text => console.log('note browser: ' + text);

// ---------------------------------------------------------------- synthetic data
// A flat walkable rectangle at height y: two triangles, 9 numbers each, as makeMap takes them.
const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
const near = (a, b, tol) => Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => Math.abs(v - b[i]) <= tol);
// The same ring of corners, whichever corner it starts from.
const sameRing = (got, want, tol) => Array.isArray(got) && got.length === want.length && want.every(w => got.some(g => near(g, w, tol)));
const text = v => JSON.stringify(v);
const rgb = hex => { const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(String(hex).trim()); return m ? [1, 2, 3].map(k => parseInt(m[k], 16)) : null; };
const closeTo = (px, want, tol) => !!want && px.every((v, k) => Math.abs(v - want[k]) <= tol);
const reddish = px => px[0] >= 90 && px[0] > px[1] * 1.5 && px[0] > px[2] * 1.5;

// Where two scenarios differ, as JSON paths; empty when they agree.
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

// A scenario with text that must survive the trip (quotes, a backslash, accents, CJK, a symbol), written exactly as the
// editor writes it (scenarioObject's key order, scenarioText's layout), so saving it unchanged gives the same text back.
function scenarioOf(revision) {
  return {
    schema: 'missionlab-plan/1',
    name: 'Browser open and save',
    id: 'sc-b7wser0001',
    revision,
    briefing: ['Hold the yard until the “guests” arrive.', 'Zweite Zeile: Größe, 東京, \\ und "Zitate".'],
    seed: 0,
    scene: 'Synthetic',
    navFile: 'synthetic_yard.bytes',
    notes: ['Made with the MissionLab scenario editor'],
    triggers: [
      { id: 1, name: 'Yard', when: 'zone', pos: [10, 0, 5], radius: 8, delay: 2, facePlayer: true, extraction: 'open', message: 'Zurück — 東京 ✓ "quoted" \\ back' },
      { id: 2, name: 'Clock', when: 'time', warn: true, delay: 300, facePlayer: true, end: 'failed' },
    ],
    objectives: [],
    doors: [{ name: 'Gate', pos: [2.5, 0, -3.25], locked: true }],
    things: [{ name: 'AKM', pos: [1, 0.5, 1], yaw: 45, trigger: 1 }],
    power: 'off',
    respawn: { after: 20, times: 2 },
    weather: { hour: 21.5 },
    spawns: [
      Object.assign(spawn([1.5, 0, 2.25], 'Guard', 0), { yaw: 90, indoor: true, defensive: true, tag: 'officer' }),
      Object.assign(spawn([-4, 0, 6], 'Patrol', 1), { yaw: 180, patrolWaitSeconds: 6.5, trigger: 1, chance: 60, skill: 'veteran', patrol: [[-4, 0, 6], [4, 0, 6], [4, 0, -2]] }),
      Object.assign(spawn([8, 0, -6], 'Wander', 1), { yaw: 270.5, wanderDistance: 6, nvg: true }),
    ].map(s => { const p = s.patrol; delete s.patrol; return Object.assign(s, { patrol: p }); }),      // patrol stays last, as spawnOf writes it
  };
}
function editorText(o) {
  const copy = JSON.parse(JSON.stringify(o)), body = copy.mission || copy, spawns = body.spawns, pad = copy.mission ? '  ' : ' ', mark = 'MISSIONLAB_SPAWNS_GO_HERE';
  body.spawns = mark;
  const list = spawns.length ? '[\n' + spawns.map(s => pad + ' ' + JSON.stringify(s)).join(',\n') + '\n' + pad + ']' : '[]';
  return JSON.stringify(copy, null, 1).replace('"' + mark + '"', () => list) + '\n';
}

// A fresh synthetic scene on the page: the map, these enemies and nothing else, viewed from (cx, cz) at `scale` pixels a
// metre. The centre is nudged by half a pixel on odd canvas sizes, so whole metres land on whole pixels.
async function scene(tab, tris, enemies, view) {
  await tab.run((E, tris, enemies, view) => {
    const s = E.state;
    s.map = E.makeMap(new Float32Array(tris), 'synthetic', false);
    Object.assign(s, { example: false, infils: [], authored: [], things: [], thing: -1, objectives: [], doors: [], door: -1, places: [], placement: [], zones: [], ceiling: Infinity });
    s.enemies = enemies.map(e => E.newEnemy(e.pos, e.role || 'Guard', e.group || 0));
    E.setSquad([], -1);
    if (view) {
      const c = document.getElementById('map');
      s.view.scale = view[2]; s.view.cx = view[0] + (c.clientWidth % 2) * 0.5 / view[2]; s.view.cz = view[1] + (c.clientHeight % 2) * 0.5 / view[2];
    } else E.fit();
    E.changed();
  }, tris, enemies, view || null);
  await tab.frames(2);
}

// ---------------------------------------------------------------- 1. open and save
async function openAndSave(browser, work) {
  const tab = await browser.tab();
  try {
    await tab.goto(PAGE); await tab.ready();
    const input = editorText(scenarioOf(3)), expected = editorText(scenarioOf(4)), file = path.join(work, 'browser-open-save.json');
    fs.writeFileSync(file, input, 'utf8');
    // The page's own file picker: the file goes into its <input type="file">, which fires the change event a choice fires.
    const { result } = await tab.send('Runtime.evaluate', { expression: "document.getElementById('fileInput')" });
    await tab.send('DOM.setFileInputFiles', { files: [file], objectId: result.objectId });
    const got = await tab.waitFor('the chosen file to be read', 10000, E => (E.state.refused || E.state.title === 'Browser open and save')
      ? { refused: E.state.refused, enemies: E.state.enemies.length, triggers: E.state.zones.length, banner: document.getElementById('banner').textContent } : null);
    check('open/save: the file chosen in the file picker is read as the scenario it is', !got.refused && got.enemies === 3 && got.triggers === 2, text(got));
    check('open/save: the page says what it opened', got.banner.includes('3 enemies loaded from browser-open-save.json'), got.banner);

    const download = browser.nextDownload(15000);
    await tab.clickOn('#save');
    let saved = null;
    try { saved = await download; } catch (e) { check('open/save: Save hands the browser a download', false, e.message); }
    if (saved) {
      check('open/save: the download is named after the scenario', saved.name === 'browser_open_and_save.json', saved.name);
      let back = null; try { back = JSON.parse(saved.text); } catch (e) { /* reported below */ }
      const diff = back ? differences(JSON.parse(expected), back, '$', []) : ['the download is not JSON'];
      check('open/save: the saved file is the same scenario with only its revision counted up', diff.length === 0, diff.join('; '));
      const a = expected.split('\n'), b = saved.text.split('\n'), line = a.findIndex((l, i) => l !== b[i]);
      check('open/save: the saved text is the opened text, byte for byte, apart from the revision', saved.text === expected,
        line < 0 ? 'lengths differ' : 'line ' + (line + 1) + ': expected ' + text(a[line]) + ', got ' + text(b[line]));
    }
    const said = await tab.run(() => document.getElementById('saveMsg').textContent);
    check('open/save: the page reports the save and its revision', said.includes('Saved as browser_open_and_save.json (revision 4)'), said);
    check('open/save: no uncaught errors', tab.errors.length === 0, tab.errors.slice(0, 3).join(' / '));
  } finally { await tab.close(); }
}

// ---------------------------------------------------------------- 2. painting an area
async function paintArea(browser) {
  const tab = await browser.tab();
  try {
    await tab.goto(PAGE); await tab.ready();
    await scene(tab, plate(-40, -40, 60, 60, 0), [{ pos: [-20, 0, -20] }], [5, 5, 10]);
    await tab.run(E => {
      E.state.zones = [E.newTrigger(1, 'zone', { name: 'Yard', pos: [5, 0, 5], radius: 12 })];
      E.changed();
      E.areaEditStart({ kind: 'trigger', index: 0 }, [-0.5, 2.6]);
      E.paintToolSet('rect');
    });
    await tab.frames(2);
    const area = () => tab.run(E => { const z = E.state.zones[0]; return { sig: z.area ? E.areaSignature(z.area) : '', rings: z.area ? z.area.parts.map(p => p.outline) : [], pos: z.pos, depth: E.undoDepth() }; });
    const a0 = await area();

    let P = await tab.mapAt([[0, 0], [10, 10]]);
    await tab.drag(P[0], P[1], { steps: 8 });
    const a1 = await area();
    check('area: a pointer drag with Rectangle paints that rectangle', a1.rings.length === 1 && sameRing(a1.rings[0], [[0, 0], [10, 0], [10, 10], [0, 10]], 0.06), text(a1.rings));
    check('area: the rectangle is one undo step', a1.depth === a0.depth + 1, a0.depth + ' -> ' + a1.depth);

    await tab.run(E => E.paintToolSet('vertex'));
    await tab.frames(1);
    P = await tab.mapAt([[10, 10], [14, 14]]);
    await tab.press(P[0]); await tab.moveHeld(P[0], P[1], 6); await tab.frames(1);
    const held = await area();
    check('area: a corner follows the pointer while the button is held', held.rings.length === 1 && held.rings[0].some(p => near(p, [14, 14], 0.06)), text(held.rings));
    await tab.key('Escape');
    const esc = await area();
    check('area: Esc during a corner drag puts the area back exactly', esc.sig === a1.sig, esc.sig);
    check('area: Esc during a corner drag leaves the anchor and the undo list as they were', near(esc.pos, a1.pos, 0) && esc.depth === a1.depth, text(esc.pos) + ' depth ' + esc.depth);
    await tab.release(P[1]); await tab.frames(1);
    const after = await area();
    check('area: letting go after Esc changes nothing', after.sig === a1.sig && after.depth === a1.depth, after.sig);

    await tab.drag(P[0], P[1], { steps: 6 });
    const a2 = await area();
    check('area: a corner drag that is let go moves that corner', a2.rings.length === 1 && sameRing(a2.rings[0], [[0, 0], [10, 0], [14, 14], [0, 10]], 0.06), text(a2.rings));
    check('area: the finished corner drag is one undo step', a2.depth === a1.depth + 1, a1.depth + ' -> ' + a2.depth);

    P = await tab.mapAt([[12, 7], [16, 6]]);          // the middle of the edge from (10, 0) to (14, 14)
    await tab.drag(P[0], P[1], { steps: 6 });
    const a3 = await area();
    check('area: dragging an edge middle inserts a corner where it is let go', a3.rings.length === 1 && sameRing(a3.rings[0], [[0, 0], [10, 0], [16, 6], [14, 14], [0, 10]], 0.06), text(a3.rings));
    check('area: the corner insertion is one undo step', a3.depth === a2.depth + 1, a2.depth + ' -> ' + a3.depth);

    P = await tab.mapAt([[4, 4], [9, 7]]);            // inside the area, away from every handle
    await tab.drag(P[0], P[1], { steps: 8 });
    const a4 = await area();
    check('area: dragging inside the area moves all of it', a4.rings.length === 1 && sameRing(a4.rings[0], a3.rings[0].map(p => [p[0] + 5, p[1] + 3]), 0.06), text(a4.rings));
    check('area: the anchor moves with the area', near(a4.pos, [10, 0, 8], 0.06), text(a4.pos));
    check('area: the whole-area move is one undo step', a4.depth === a3.depth + 1, a3.depth + ' -> ' + a4.depth);
    await tab.key('z', CTRL);
    const a5 = await area();
    check('area: one Ctrl+Z takes the whole move back, area and anchor', a5.sig === a3.sig && near(a5.pos, [5, 0, 5], 0.001), a5.sig + ' ' + text(a5.pos));

    P = await tab.mapAt([[0, 0], [-3, -3]]);
    await tab.press(P[0]); await tab.moveHeld(P[0], P[1], 6); await tab.frames(1);
    const pulled = await area();
    check('area: a second corner drag is under way before it is cancelled', pulled.sig !== a5.sig, pulled.sig);
    // The DevTools Protocol cannot make the browser cancel a pointer, so a real PointerEvent is dispatched on the map instead.
    await tab.run(() => { document.getElementById('map').dispatchEvent(new PointerEvent('pointercancel', { pointerId: 1, pointerType: 'mouse', isPrimary: true, bubbles: true })); });
    await tab.frames(1);
    const cancelled = await area();
    check('area: pointercancel (dispatched in the page) puts the area back exactly, with no undo step', cancelled.sig === a5.sig && cancelled.depth === a5.depth, cancelled.sig);
    await tab.release(P[1]); await tab.frames(1);
    const last = await area();
    check('area: letting go after pointercancel changes nothing', last.sig === a5.sig && last.depth === a5.depth, last.sig);
    check('area: no uncaught errors', tab.errors.length === 0, tab.errors.slice(0, 3).join(' / '));
  } finally { await tab.close(); }
}

// ---------------------------------------------------------------- 3. selecting on two floors
async function mixedFloors(browser) {
  const tab = await browser.tab();
  try {
    await tab.goto(PAGE); await tab.ready();
    // Open ground with a 20 x 20 m upper floor 3 m up over its middle: enemy 1 below it, enemy 2 on it, enemy 3 far away.
    const A0 = [0, 0, 0], B0 = [4, 3, 0], C0 = [-30, 0, 30];
    await scene(tab, plate(-40, -40, 40, 40, 0).concat(plate(-10, -10, 10, 10, 3)), [{ pos: A0 }, { pos: B0 }, { pos: C0 }], [2, 0, 10]);
    const now = () => tab.run(E => ({ pos: E.state.enemies.map(e => e.pos.slice()), picked: E.picked(), ceiling: isFinite(E.state.ceiling) ? E.state.ceiling : 'all', depth: E.undoDepth() }));
    const w0 = await now();

    let P = await tab.mapAt([[-3, 3], [7, -3]]);
    await tab.drag(P[0], P[1], { modifiers: SHIFT, steps: 6 });
    const w1 = await now();
    check('floors: a Shift-drag box around enemies on two floors selects both while every floor is shown', text(w1.picked) === '[0,1]', text(w1.picked));
    await tab.key('Escape');
    check('floors: Esc clears the selection', text((await now()).picked) === '[]');
    await tab.key('PageDown');
    const w2 = await now();
    check('floors: PageDown hides the upper floor', w2.ceiling !== 'all' && w2.ceiling > 0 && w2.ceiling < 3, String(w2.ceiling));
    await tab.drag(P[0], P[1], { modifiers: SHIFT, steps: 6 });
    const w3 = await now();
    check('floors: the same box then selects only the enemy on the floor still shown', text(w3.picked) === '[0]', text(w3.picked));

    P = await tab.mapAt([[0, 0], [-4, -3]]);
    await tab.drag(P[0], P[1], { steps: 8 });
    const w4 = await now();
    check('floors: dragging the selected enemy moves it on the shown floor, under the hidden one', near(w4.pos[0], [-4, 0, -3], 0.06), text(w4.pos[0]));
    check('floors: the enemy on the hidden floor and the one far away stay exactly where they were', text(w4.pos[1]) === text(B0) && text(w4.pos[2]) === text(C0), text(w4.pos));
    check('floors: the drag is one undo step', w4.depth === w0.depth + 1, w0.depth + ' -> ' + w4.depth);

    await tab.key('PageUp');
    check('floors: PageUp shows every floor again', (await now()).ceiling === 'all');
    await tab.key('Escape');
    P = await tab.mapAt([[-7, 6], [7, -6]]);
    await tab.drag(P[0], P[1], { modifiers: SHIFT, steps: 6 });
    const w5 = await now();
    check('floors: a box around both selects the two enemies on different floors', text(w5.picked) === '[0,1]', text(w5.picked));
    P = await tab.mapAt([[4, 0], [6, 1]]);            // grab the one upstairs
    await tab.drag(P[0], P[1], { steps: 8 });
    const w6 = await now();
    check('floors: dragging one of the two moves both, each staying on its own floor', near(w6.pos[0], [-2, 0, -2], 0.06) && near(w6.pos[1], [6, 3, 1], 0.06), text(w6.pos.slice(0, 2)));
    check('floors: the enemy outside the selection does not move', text(w6.pos[2]) === text(C0), text(w6.pos[2]));
    check('floors: the group drag is one undo step', w6.depth === w4.depth + 1, w4.depth + ' -> ' + w6.depth);
    await tab.key('z', CTRL);
    const w7 = await now();
    check('floors: one Ctrl+Z puts both back on their floors', text(w7.pos[0]) === text(w4.pos[0]) && text(w7.pos[1]) === text(B0), text(w7.pos.slice(0, 2)));
    check('floors: no uncaught errors', tab.errors.length === 0, tab.errors.slice(0, 3).join(' / '));
  } finally { await tab.close(); }
}

// ---------------------------------------------------------------- 4. draft, apply and retry against the link fixture
async function draftApplyRetry(browser, link) {
  const game = link.game, tab = await browser.tab();
  try {
    await tab.goto(link.url); await tab.ready();
    // What the host sees of the link, as one plain object; tests below run on it here in Node.
    const look = async () => {
      const s = await tab.run(E => ({
        session: E.live.session, mission: !!E.live.mission, enemies: E.state.enemies.map(e => ({ id: e.id, role: e.role })), mode: E.stage.mode, dirty: E.stage.dirty,
        staged: E.live.staged.length, base: E.stage.baseSession, out: E.stage.out ? { command: E.stage.out.command, done: E.stage.out.done } : null,
        receipt: document.getElementById('stageReceipt').textContent, banner: document.getElementById('banner').textContent, drafts: E.separateDrafts().length,
      }));
      s.roles = s.enemies.map(e => e.role);
      return s;
    };
    const until = async (what, ms, test) => {
      for (const end = Date.now() + ms; ;) {
        const s = await look();
        if (test(s)) return s;
        if (Date.now() > end) throw new Error('waited ' + ms / 1000 + ' s for ' + what + '; the page showed ' + text({ session: s.session, mode: s.mode, dirty: s.dirty, base: s.base, roles: s.roles, out: s.out, drafts: s.drafts, receipt: s.receipt, banner: s.banner }));
        await sleep(80);
      }
    };
    const view = async v => { await tab.clickOn('#views button[data-view-to="' + v + '"]'); await tab.frames(1); };
    // An edit as a host makes it: click the enemy on the map, then its new role in the panel.
    const editRole = async (index, pos, role) => {
      await view('create');
      await tab.run(E => E.fit()); await tab.frames(2);
      const [p] = await tab.mapAt([[pos[0], pos[2]]]);
      await tab.click(p);
      await tab.waitFor('enemy ' + (index + 1) + ' to be selected', 5000, (E, i) => E.state.selected === i, index);
      await tab.clickOn('#panel .seg button[data-role="' + role + '"]');
      await until('the edit to be staged', 5000, s => s.dirty);
    };
    const apply = async () => { await view('direct'); await tab.clickOn('#stageApply'); };

    await until('the page to show the game\'s mission', 10000, s => s.session === 'm1-1000' && s.roles.length === 2);
    const l0 = await look();
    check('live: the page links to the fixture game and shows its two enemies', l0.mission && l0.enemies.length === 2 && l0.enemies.every(e => e.role === 'Guard'), text(l0.enemies));
    // Direct is complete the moment it opens: no section of it appears on a later timer and pushes its buttons down
    // (in CI a press on Apply changes landed on the heading above it, which had moved into its place).
    const jump = await tab.run(async () => {
      const wait = ms => new Promise(r => setTimeout(r, ms)), side = document.getElementById('side');
      const at = () => Math.round(document.getElementById('stageDraft').getBoundingClientRect().top - side.getBoundingClientRect().top + side.scrollTop);
      document.querySelector('#views button[data-view-to="play"]').click(); await wait(1000);
      document.querySelector('#views button[data-view-to="direct"]').click();
      const first = at(); await wait(1000);
      return { first, later: at() };
    });
    check('live: Direct is complete the moment it opens; its buttons are where they were a second later', jump.first === jump.later, text(jump));
    await view('direct');
    await tab.clickOn('#stageDraft');
    await until('Draft mode', 5000, s => s.mode === 'draft' && s.base === 'm1-1000');
    await editRole(0, [0, 0, 0], 'Wander');
    const l1 = await look();
    check('live: in Draft mode an edit is listed and held on the page; nothing reaches the game', l1.dirty && l1.staged > 0 && game.posts().length === 0 && game.markers[0].role === 'Guard', l1.staged + ' staged, ' + game.posts().length + ' sent');

    // The mission ends and the next one begins just before the game takes the batch: refused as stale, nothing done.
    game.script.push('newMission');
    await apply();
    await until('the answer to the stale batch', 10000, s => s.receipt.includes('another mission'));
    const l2 = await look(), first = game.posts()[0];
    check('live: the batch reached the game once and was refused as meant for another mission (409)', game.posts().length === 1 && first.outcome === 'stale' && first.session === 'm1-1000', text(game.posts()));
    check('live: the page tells the host that nothing was done', l2.receipt.includes('That change was made for another mission. Nothing was done.'), l2.receipt);
    check('live: the game carried nothing out', game.applied.size === 0 && game.markers.every(m => m.role === 'Guard'), text(game.markers.map(m => m.role)));
    await until('the old draft to be put aside', 10000, s => s.session === 'm2-2000' && !s.dirty && s.drafts > 0);
    check('live: the draft for the ended mission is kept aside and never sent to the new one', game.posts().length === 1, game.posts().length + ' sent');

    // The game begins the next batch and does not finish it in time: 202 pending.
    await until('the page to follow the new mission', 10000, s => s.base === 'm2-2000' && !s.dirty && s.roles.length === 2);
    game.script.push('hold');
    await editRole(0, [0, 0, 0], 'Wander');
    await apply();
    await until('the pending answer', 20000, s => s.receipt.includes('has begun that change'));
    const held = game.posts().at(-1), l3 = await look();
    check('live: the next batch reached the game once and the game answered that it has begun it (202)', held.outcome === 'pending' && game.posts(held.command).length === 1, text(held));
    check('live: the page says the game has begun it and keeps the same command number for a retry', l3.out && l3.out.command === held.command && !l3.out.done && l3.receipt.includes('never carried out twice'), text(l3.out) + ' ' + l3.receipt);
    check('live: nothing is shown as done while the game is still working', game.applied.size === 0 && l3.dirty, text(l3.enemies));
    const asked = game.lookups(held.command).length;
    await tab.clickOn('#stageApply');
    await until('the answer to the early retry', 8000, s => s.banner.includes('has begun the last batch'));
    check('live: a retry while the game still works asks about the same number and sends nothing again', game.lookups(held.command).length > asked && game.posts(held.command).length === 1 && game.applied.size === 0,
      game.lookups(held.command).length + ' lookups, ' + game.posts(held.command).length + ' sent');

    // The game finishes it: a retry now shows the game's own answer, and it was carried out once.
    game.finish(held.command);
    await tab.clickOn('#stageApply');
    await until('the applied answer', 8000, s => /\): applied/.test(s.receipt) && !s.dirty);
    await until('the page to show the game\'s state', 8000, s => s.roles[0] === 'Wander');
    const l4 = await look();
    check('live: the retry shows what the game did with the same batch: applied', /\): applied/.test(l4.receipt) && l4.receipt.includes(held.command), l4.receipt);
    check('live: the batch was carried out once and sent once', game.applied.get(held.command) === 1 && game.posts(held.command).length === 1, game.applied.get(held.command) + ' carried out, ' + game.posts(held.command).length + ' sent');
    check('live: the page then shows the game\'s own state with nothing staged', l4.enemies[0].role === 'Wander' && !l4.dirty, text(l4.enemies));

    // A batch lost on the way: the game never saw it. The retry carries the same number and is carried out once.
    await editRole(1, [8, 0, 4], 'Wander');
    const from = game.log.length;
    game.unreachable = true;
    await apply();
    await until('the page to give up on the lost batch', 15000, s => s.receipt.includes('The game did not answer'));
    const lost = game.log.slice(from).filter(l => l.kind === 'change'), l5 = await look(), number = lost.length ? lost[0].command : '';
    check('live: a batch lost on the way is reported as unanswered and its number kept for the retry', lost.length > 0 && !game.ledger.has(number) && l5.out && l5.out.command === number && !l5.out.done, text(l5.out) + ' ' + l5.receipt);
    game.unreachable = false;
    game.script.push('apply');
    await tab.clickOn('#stageApply');
    await until('the applied answer to the retry', 10000, s => /\): applied/.test(s.receipt) && !s.dirty);
    await until('the page to show the game\'s state', 8000, s => s.roles[1] === 'Wander');
    const tries = game.log.slice(from).filter(l => l.kind === 'change');
    check('live: the retry asked about the number first, then sent the batch again with the same number', game.lookups(number).length > 0 && tries.length >= 2 && tries.every(t => t.command === number), text(tries.map(t => t.command + ' ' + t.outcome)));
    check('live: the retried batch was carried out once', game.applied.get(number) === 1, String(game.applied.get(number)));
    check('live: every change the page sent carried X-MissionLab, its mission\'s session and a command number', game.posts().every(p => p.marked && p.session && p.command), text(game.posts()));
    if (tab.errors.length) note('the live page logged ' + tab.errors.length + ' error(s) (the fixture answers only what these checks need): ' + tab.errors.slice(0, 3).join(' / '));
  } finally { await tab.close(); }
}

// ---------------------------------------------------------------- 5. 2D, 3D and back
async function views(browser) {
  const tab = await browser.tab();
  try {
    await tab.goto(PAGE); await tab.ready();
    const enemies = [[12, 0, -7], [-9, 0, 4], [3, 0, 11]];
    await scene(tab, plate(-30, -30, 30, 30, 0), enemies.map(pos => ({ pos })), null);
    const colour = rgb(await tab.run(() => getComputedStyle(document.body).getPropertyValue('--enemy')));
    let P = await tab.mapAt(enemies.map(p => [p[0], p[2]]));
    let shot = await tab.screenshot();
    enemies.forEach((p, i) => { const px = shot.px(P[i].x, P[i].y); check('views: 2D draws enemy ' + (i + 1) + ' at its map position', closeTo(px, colour, 40), 'pixel ' + text(px) + ', enemy colour ' + text(colour)); });

    await tab.clickOn('#mode3d');
    const opened = await tab.waitFor('the 3D view', 10000, E => (E.state.mode === '3d' || /3D view could not start/.test(document.getElementById('banner').textContent)) ? { mode: E.state.mode, banner: document.getElementById('banner').textContent } : null);
    check('views: the 3D view opens', opened.mode === '3d', opened.banner);
    if (opened.mode === '3d') {
      await tab.frames(4);
      const S = await tab.run(E => {
        const c = document.getElementById('view3d');
        return E.state.enemies.map(e => { const [x, y] = E.view3d.screenOf(e.pos), top = document.elementFromPoint(x, y); return { x, y, on: top === c ? '' : top ? top.tagName.toLowerCase() + (top.id ? '#' + top.id : '') : 'nothing' }; });
      });
      shot = await tab.screenshot();
      S.forEach((s, i) => {
        let seen = false;
        for (let dx = -3; dx <= 3 && !seen; dx++) for (let dy = -3; dy <= 3 && !seen; dy++) seen = reddish(shot.px(s.x + dx, s.y + dy));
        check('views: 3D draws enemy ' + (i + 1) + ' where its map position projects', !s.on && seen, s.on ? 'covered by ' + s.on : 'pixel ' + text(shot.px(s.x, s.y)) + ' at ' + Math.round(s.x) + ', ' + Math.round(s.y));
      });
      const order = [];
      for (let i = 0; i < S.length; i++) for (let j = i + 1; j < S.length; j++) {
        if (Math.sign(S[j].x - S[i].x) !== Math.sign(P[j].x - P[i].x) || Math.sign(S[j].y - S[i].y) !== Math.sign(P[j].y - P[i].y)) order.push((i + 1) + '/' + (j + 1));
      }
      check('views: 3D keeps the map\'s left-right and near-far order of the enemies (not mirrored)', order.length === 0, 'out of order: ' + order.join(', '));
      if (!S[1].on) {
        // Press and release with no frame drawn in between, as on a slow machine or a busy CI runner: selecting on the
        // press rebuilds the figures, and the release must still find the enemy it was pressed on.
        await tab.run(() => { window.__heldFrame = window.requestAnimationFrame; window.requestAnimationFrame = () => 0; });
        await tab.press(S[1]); await tab.release(S[1]);
        await tab.run(() => { window.requestAnimationFrame = window.__heldFrame; delete window.__heldFrame; });
        await tab.frames(1);
        const picked = await tab.waitFor('a selection', 3000, E => E.state.selected >= 0 ? E.state.selected + 1 : null).catch(() => 0);
        check('views: clicking enemy 2 in 3D selects that enemy, even with no frame drawn between press and release', picked === 2, 'selected ' + (picked - 1));
      }
    }
    await tab.clickOn('#mode2d');
    const back = await tab.waitFor('the flat map', 5000, E => E.state.mode === '2d' && !document.getElementById('map').hidden);
    await tab.frames(3);
    P = await tab.mapAt(enemies.map(p => [p[0], p[2]]));
    shot = await tab.screenshot();
    const px = shot.px(P[1].x, P[1].y);
    check('views: back in 2D the map draws the enemies where they were', back && closeTo(px, colour, 40), 'pixel ' + text(px));
    check('views: opening the page and switching 2D, 3D and 2D raise no uncaught errors', tab.errors.length === 0, tab.errors.slice(0, 3).join(' / '));
  } finally { await tab.close(); }
}

// ---------------------------------------------------------------- the run
async function main() {
  if (typeof globalThis.WebSocket !== 'function') {
    console.log('browser checks not run: they need Node 22 or later (the global WebSocket); this is Node ' + process.versions.node + '.');
    return 2;
  }
  const found = findBrowser();
  if (!found.exe) {
    console.log('browser checks not run: no Chromium or Edge found. Set CHROME_PATH to one. Looked at: ' + (found.tried.join(', ') || 'nothing on PATH') + '.');
    return 2;
  }
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'missionlab-browser-work-')), downloads = path.join(work, 'downloads');
  fs.mkdirSync(downloads);
  let browser = null, link = null;
  const cleanup = async () => {
    if (link) { await link.close(); link = null; }
    if (browser) { const left = await browser.close(); browser = null; if (left) note(left); }
    try { fs.rmSync(work, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 }); } catch (e) { note('the work folder ' + work + ' could not be removed: ' + e.message); }
  };
  const watchdog = setTimeout(() => {
    console.log('FAIL browser: the suite finishes within 5 minutes');
    if (browser) browser.kill();
    try { fs.rmSync(work, { recursive: true, force: true }); } catch (e) { /* left behind */ }
    process.exit(1);
  }, 300000);
  for (const sig of ['SIGINT', 'SIGTERM']) process.on(sig, () => { if (browser) browser.kill(); process.exit(130); });
  try {
    try { browser = await launch(found.exe, downloads); }
    catch (e) { console.log('browser checks not run: ' + e.message); return 2; }
    link = await startFixture(ROOT, [
      { session: 'm1-1000', markers: [Object.assign({ id: 1, live: true, waiting: false }, spawn([0, 0, 0], 'Guard', 0)), Object.assign({ id: 2, live: true, waiting: false }, spawn([8, 0, 4], 'Guard', 1))] },
      { session: 'm2-2000', markers: [Object.assign({ id: 1, live: true, waiting: false }, spawn([0, 0, 0], 'Guard', 0)), Object.assign({ id: 2, live: true, waiting: false }, spawn([8, 0, 4], 'Guard', 1))] },
    ]);
    const flows = [['open/save', () => openAndSave(browser, work)], ['area', () => paintArea(browser)], ['floors', () => mixedFloors(browser)],
      ['live', () => draftApplyRetry(browser, link)], ['views', () => views(browser)]];
    for (const [name, run] of flows) {
      try { await run(); }
      catch (e) { check(name + ': the workflow runs to its end', false, e && e.stack ? e.stack.split('\n').slice(0, 3).join(' ') : String(e)); }
    }
  } finally {
    clearTimeout(watchdog);
    await cleanup();
  }
  console.log(failures ? failures + ' of ' + checks + ' BROWSER CHECKS FAILED' : 'ALL ' + checks + ' BROWSER CHECKS PASSED');
  return failures ? 1 : 0;
}

main().then(code => process.exit(code), e => { console.log('FAIL browser: the suite itself failed | ' + (e && e.stack ? e.stack : e)); process.exit(1); });
