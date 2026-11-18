// ------------------------------------------------------------------ views: Play, Create, Direct (ML-013)
// Choosing a mission, making one and running one live need different things on screen. All three work on the same
// scenario model; a view only decides what is shown. A section names its views in data-view ("play", "create direct");
// one without it belongs to Create, where the tabs choose among its parts. Placing enemies, routes, zones and things is
// Create's alone: Play only looks, and Direct selects live enemies for deliberate orders.
// state.view is the map's pan and zoom, so the view is kept in state.pane.
const VIEWS = ['play', 'create', 'direct'];
const VIEW_NAMES = { play: 'Play', create: 'Create', direct: 'Direct' };
const VIEW_KEY = 'missionlab-view';
const PLACING_TOOLS = ['add', 'route', 'zone', 'thing'];
// The Play catalogue as the game last sent it (null: not read yet), the scenario picked in it, and what the page last
// saved or opened, so the strip can tell saved from unsaved. lobbyFile: a scenario opened from the catalogue between
// missions; while it is set, the page keeps it instead of clearing itself for the empty lobby.
const catalogue = { list: null, error: '', pick: '', loading: false, savedText: null, savedName: '', lobbyFile: '', s: null, runSeen: null, wasRunning: false, listKey: '', detailKey: '' };
state.pane = 'create';
const CHOICE_WORDS = { '*': 'any scenario made for the map', '': 'a random layout', '(empty)': 'an empty map' };

function setView(v, byHand) {
  let view = VIEWS.includes(v) ? v : 'create';
  // Create lists every enemy: a sealed mission opens there only once it is revealed (20b-sealed.js).
  if (view === 'create' && sealedHides() && !sealedAsk()) {
    view = viewIs('create') ? 'play' : state.pane;
    say('Left sealed: Create shows every enemy, so it stays closed until you reveal the mission.');
  }
  state.pane = view;
  $('side').dataset.pane = view; document.body.dataset.pane = view;
  document.querySelectorAll('#views button').forEach(b => b.setAttribute('aria-selected', String(b.dataset.viewTo === view)));
  $('tabs').hidden = view !== 'create'; $('saveBox').hidden = view !== 'create';
  $('playBox').hidden = view !== 'play'; $('directBox').hidden = view !== 'direct';
  document.querySelectorAll('#tools .tool').forEach(b => { if (PLACING_TOOLS.includes(b.dataset.tool)) b.hidden = view !== 'create'; });
  $('undo').hidden = view !== 'create';
  if (view !== 'create') { state.placing = null; if (PLACING_TOOLS.includes(state.tool)) setTool('select'); }
  if (byHand) { try { localStorage.setItem(VIEW_KEY, view); } catch (e) { /* storage unavailable */ } }
  if (view === 'play' && live.on) refreshCatalogue();
  toolHint(); syncPlay(); syncStrip(); invalidate();
}
const viewIs = v => state.pane === v;
document.querySelectorAll('#views button').forEach(b => b.addEventListener('click', () => setView(b.dataset.viewTo, true)));

// Called once the page has started. Connected to the game, a host starts in Play; offline, an author starts in Create.
function startViews() {
  let v = '';
  try { v = localStorage.getItem(VIEW_KEY) || ''; } catch (e) { /* storage unavailable */ }
  v = VIEWS.includes(v) ? v : live.on ? 'play' : 'create';
  if (v === 'create' && sealedHides()) v = 'play';       // a sealed draft after a reload: sealed again, so no question at start
  setView(v);
  if (live.on) setInterval(() => { if (viewIs('play') && !document.hidden) refreshCatalogue(); }, 15000);
}

// Shortcut keys that place, delete, paste or undo belong to Create; in Play and Direct they do nothing, so a host cannot
// change a scenario or the live mission by a stray key. Flying keys in 3D still work.
window.addEventListener('keydown', ev => {
  if (viewIs('create') || !ev.target || /^(INPUT|TEXTAREA|SELECT)$/.test(ev.target.tagName)) return;
  const key = String(ev.key || '').toLowerCase(), mod = ev.ctrlKey || ev.metaKey;
  const flying = state.mode === '3d' && !mod && key.length === 1 && 'wasdqe'.includes(key);
  const editing = mod ? ['z', 'v', 'd'].includes(key) : ['a', 'r', 'z', 'delete', 'backspace'].includes(key);
  if (editing && !flying) ev.stopImmediatePropagation();
}, true);

// ------------------------------------------------------------------ the status strip
// Always on screen: which view this is, whether the page's scenario is saved, what the next mission uses and what is running.
function draftWords() {
  if (live.on && live.mission && !catalogue.lobbyFile) {
    const staged = Array.isArray(live.staged) ? live.staged.length : 0;
    if (staged) return staged + (staged === 1 ? ' change' : ' changes') + ' staged, not in the game yet';
    return typeof stageHolds === 'function' && stageHolds() ? 'draft mode, nothing staged' : 'the running mission (edits go into the game)';
  }
  if (state.example) return 'the example, not a scenario';
  if (!state.enemies.length && !state.zones.length && !state.things.length) return 'empty';
  const name = fileName();
  return catalogue.savedText !== null && catalogue.savedText === scenarioText() ? 'saved as ' + (catalogue.savedName || name) : 'unsaved changes to ' + name;
}
function entryByValue(v) {
  const k = String(v === undefined || v === null ? '' : v).toLowerCase();
  return (catalogue.list || []).find(e => String(e.value).toLowerCase() === k) || null;
}
function nextWords() {
  const s = catalogue.s;
  if (!live.on) return 'chosen in the game (not connected)';
  if (!s || typeof s.choice !== 'string') return 'not known yet';
  const e = entryByValue(s.choice);
  if (e) return (e.title || e.value) + (e.usable ? '' : ' (MissionLab would refuse it)');
  if (s.choice in CHOICE_WORDS) return CHOICE_WORDS[s.choice];
  const c = (Array.isArray(s.choices) ? s.choices : []).find(x => x && x.value === s.choice);
  return c ? c.label : s.choice + ' (no such file: a random layout is used)';
}
function runWords() {
  const s = catalogue.s;
  if (!live.on) return 'not connected to the game';
  if (!s) return 'not known yet';
  if (s.starting) return 'a mission is starting';
  if (!s.mission) return 'none';
  const e = s.run ? (catalogue.list || []).find(x => x.lastRun && x.lastRun.file === s.run) : null;
  const what = e ? e.title || e.value : s.rejected ? 'the game\'s own mission (the scenario was refused)' : 'a mission';
  return what + (s.scene ? ' on ' + s.scene : '');
}
function draftTone() {
  if (live.on && live.mission && !catalogue.lobbyFile) return Array.isArray(live.staged) && live.staged.length ? 'warn' : 'live';
  if (state.example || (!state.enemies.length && !state.zones.length && !state.things.length)) return '';
  return catalogue.savedText !== null && catalogue.savedText === scenarioText() ? 'ok' : 'warn';
}
function syncStrip() {
  const put = (id, text) => { const el = $(id); if (el.textContent !== text) el.textContent = text; };
  put('stDraft', draftWords()); put('stNext', nextWords()); put('stRun', runWords());
  // Offline, the game's two lines would both say "not connected": one line says it once.
  $('stGameRow').hidden = live.on; $('stNextRow').hidden = $('stRunRow').hidden = !live.on;
  $('stDraftRow').dataset.tone = draftTone(); $('stRunRow').dataset.tone = catalogue.s && catalogue.s.mission ? 'live' : '';
  // Between missions the game has no mission to save from, so the page saves the file itself; during one, the game saves it.
  if (live.on) { $('saveCat').hidden = !!(live.mission && !catalogue.lobbyFile); $('save').hidden = !$('saveCat').hidden; }
  syncNext();
}
// One plain sentence under the view switch: what someone new to the page can do next, here. It only points at controls
// that are on screen in this view and never repeats the map's own hint line for the tool in hand.
function nextStepWords() {
  if (viewIs('play')) {
    if (!live.on) return 'choose Open files below to see what a scenario file holds.';
    return pickedEntry() ? 'choose Play this next, then host the mission in the game.' : 'pick a mission in the list below.';
  }
  if (viewIs('direct')) {
    if (!live.on) return 'switch to Create to work on a scenario. Directing a mission needs the game, as said below.';
    return catalogue.s && catalogue.s.mission ? 'follow the mission on the map; the controls below change it.' : 'host a mission in the game. It shows up here when it starts.';
  }
  if (sealedHides()) return 'reveal the mission to see its enemies, or switch to Play.';
  if (!state.map && !state.enemies.length) return live.on ? 'choose Add enemy and click the map, or pick a mission to change in Play.' : 'open a map file from OPERATOR/OPERATOR_Data/NavMeshData with Open files on the Map tab.';
  if (!state.enemies.length) return 'choose Add enemy above the map, then click walkable ground.';
  if (state.selected < 0) return state.example ? 'click an enemy on the map to change it, or open a real map file to make your own.' : 'click an enemy on the map to change it, then save the file.';
  return state.example ? 'change enemy ' + (state.selected + 1) + ' on the Enemies tab, or drag it on the map.' : 'change enemy ' + (state.selected + 1) + ' on the Enemies tab, then save the file.';
}
function syncNext() { const el = $('nextText'), text = nextStepWords(); if (el.textContent !== text) el.textContent = text; }
// After every edit (changed() calls this).
function viewsChanged() { syncStrip(); syncSeal(); if (viewIs('play')) syncPlay(); }
// The page's scenario now equals a file: one just opened or saved.
function markSaved(name) { catalogue.savedText = state.example ? null : scenarioText(); catalogue.savedName = name || fileName(); syncStrip(); }

// Every state the game sends (applyLive calls this).
function syncModes(s) {
  catalogue.s = s || null;
  const running = !!(s && s.mission);
  if (running && catalogue.lobbyFile) {
    // The mission takes the page over. What was opened between missions is kept in this browser, as an offline draft is.
    const was = catalogue.lobbyFile; catalogue.lobbyFile = '';
    if (catalogue.savedText !== scenarioText()) {
      try { localStorage.setItem(DRAFT_KEY, JSON.stringify(Object.assign(scenarioObject(), { name: state.title }))); } catch (e) { /* storage unavailable */ }
      say('A mission started, so the page now shows it. Your unsaved changes to ' + was + ' are kept in this browser: they come back when you open the editor without the game.');
    }
  }
  // A new run record, or a mission that has just ended, changes last runs and measured times.
  const runKey = s && typeof s.run === 'string' ? s.run : null;
  if (catalogue.list && (runKey !== catalogue.runSeen || (catalogue.wasRunning && !running))) refreshCatalogue();
  catalogue.runSeen = runKey; catalogue.wasRunning = running;
  sealedSeen(s, running);
  syncStrip(); if (viewIs('play')) syncPlay();
}

// ------------------------------------------------------------------ the Play catalogue
async function refreshCatalogue() {
  if (!live.on || catalogue.loading) return;
  catalogue.loading = true;
  try {
    const r = await fetch('/api/catalogue', { cache: 'no-store' });
    if (r.status === 404) catalogue.error = 'The game runs an older MissionLab without a mission catalogue. Choose the next mission from the list above.';
    else {
      const d = await r.json();
      if (!d || !Array.isArray(d.scenarios)) throw new Error('not a catalogue');
      catalogue.list = d.scenarios.filter(e => e && typeof e.file === 'string' && typeof e.value === 'string');
      catalogue.error = d.skipped ? d.skipped + (d.skipped === 1 ? ' file was' : ' files were') + ' left out: too large, or being written at the moment.' : '';
    }
  } catch (e) { catalogue.error = 'The game did not hand over the catalogue. Is it still running?'; }
  catalogue.loading = false;
  syncStrip(); syncPlay();
}
// Offline, Play shows the scenario on the page in the same shape as a catalogue entry. What only the game knows
// (measured times, the last run, what is tested) is left unknown, never guessed.
// A sealed scenario that is not revealed leaves its enemy count and the places in its findings out, as the game's
// catalogue does (CatalogueInfo): what is wrong and where in the file stays.
function pageEntry() {
  if (state.example || !state.enemies.length) return null;
  const obj = scenarioObject(), body = planBody(obj) || {}, name = fileName(), hides = state.sealed === true && !sealedPageRevealed();
  const found = scenarioProblems(obj).map(f => (hides ? Object.assign({}, f, { text: sealedStrip(f.text) }) : f));
  const errors = found.filter(f => f.level === 'error');
  return {
    page: true, file: name, value: name.replace(/\.json$/, ''), readable: !errors.length, usable: !errors.length && state.enemies.length > 0,
    why: errors.length ? 'MissionLab would refuse it: ' + (errors[0].path ? errors[0].path + ': ' : '') + errors[0].text : '', findings: found,
    title: typeof body.name === 'string' ? body.name : '', id: typeof body.id === 'string' ? body.id : '', revision: body.revision | 0, sealed: state.sealed === true,
    briefing: Array.isArray(body.briefing) ? body.briefing : [], author: { squad: Array.isArray(body.squad) ? body.squad : null, minutes: num(body.minutes, 0) > 0 ? body.minutes : null },
    measured: null, navFile: typeof body.navFile === 'string' ? body.navFile : '', scene: typeof body.scene === 'string' ? body.scene : '', map: '', mode: 'pve',
    compat: null, enemies: hides ? null : state.enemies.length, capabilities: capabilitiesOf(obj).map(n => ({ name: n, tested: null })), lastRun: undefined,
    options: (state.options || []).map(o => ({ id: o.id, name: o.name, pick: o.pick | 0, choices: (o.choices || []).map(c => (c && c.name) || '') })),
  };
}
function pickedEntry() {
  if (!live.on) return pageEntry();
  const list = catalogue.list || [];
  return list.find(e => e.file === catalogue.pick) || entryByValue(catalogue.s && catalogue.s.choice) || list[0] || null;
}
const plural = (n, one, many) => n + ' ' + (n === 1 ? one : many);
function lastRunWords(r, ctx) {
  const c = ctx || {};
  if (r === undefined) return 'not known without the game';
  if (!r) return 'never played';
  const when = String(r.started || '').slice(0, 16).replace('T', ' ') + ' UTC';
  if (r.outcome === 'refused') return 'refused on ' + when + (r.reason ? ': ' + r.reason : '');
  if (typeof r.minutes !== 'number') return c.mission && r.session && r.session === c.session ? 'running now (since ' + when + ')' : 'started ' + when + '; its end was not seen, so it is not measured';
  const how = r.result === 'success' ? 'ended as a success' : r.result === 'failed' ? 'ended as failed' : 'ended; how is not known';
  return how + ' after ' + r.minutes + ' min (' + when + ')';
}
// The details of one entry as label and text pairs. The author's words and the measurements are separate rows, and
// whatever is not known says so.
function catalogueFacts(e, ctx) {
  const a = (e && e.author) || {}, m = e ? e.measured : null, facts = [];
  const squad = Array.isArray(a.squad) && a.squad.length === 2 ? (a.squad[0] === a.squad[1] ? String(a.squad[0]) : a.squad[0] + ' to ' + a.squad[1]) + ' players' : 'not given by the author';
  facts.push(['Squad (the author\'s recommendation)', squad]);
  facts.push(['Length (the author\'s estimate)', typeof a.minutes === 'number' && a.minutes > 0 ? 'about ' + a.minutes + ' min' : 'not given by the author']);
  facts.push(['Length (measured)', !m ? 'not measured yet: run records are kept by the game'
    : typeof m.minutes === 'number' ? m.minutes + ' min, the middle of ' + plural(m.finished | 0, 'run', 'runs') + ' seen to end'
    : 'not measured yet' + (m.runs ? ' (' + plural(m.runs | 0, 'run', 'runs') + ' on record, none seen to end)' : '')]);
  const map = e.navFile ? (e.map && e.map !== e.navFile ? e.map + ' (' + e.navFile + ')' : e.navFile) : e.scene ? 'scenes: ' + e.scene : 'not named in the file';
  facts.push(['Map', map]);
  facts.push(['Mode', 'a PvE raid (MissionLab leaves PvP alone)']);
  facts.push(['Tested with this build', e.compat === 'tested' ? 'yes: this map, hosted with MissionLab' : e.compat === 'experimental' ? 'experimental on this map'
    : e.compat === 'untested' ? 'not tested on this map yet' : e.compat === '' ? 'unknown: the file names no map file' : 'not known without the game']);
  if (e.sealed) facts.push(['Sealed', sealedEntryHides(e) ? 'yes: where enemies are, how many, what is rolled and what triggers will do are left out here. Spoiler avoidance: the file itself stays readable'
    : 'yes, and revealed on this page: a reload or another tab hides it again']);
  if (Array.isArray(e.options) && e.options.length) {
    facts.push(['Options (the author\'s defaults)', e.options.map(o => (o.name || o.id) + ': ' + ((o.choices || [])[o.pick | 0] || 'choice ' + ((o.pick | 0) + 1))).join('; ')]);
  }
  const last = lastRunWords(e.lastRun, ctx);
  facts.push(['Last run', sealedEntryHides(e) ? sealedStrip(last) : last]);
  return facts;
}
function capabilityWords(c) { return c.name + (c.tested === true ? ': seen working in the game' : c.tested === false ? ': untested in the game' : ': not known without the game'); }
function shortLastRun(r) {
  if (!r) return 'never played';
  if (r.outcome === 'refused') return 'last run refused';
  if (typeof r.minutes !== 'number') return 'last run: no end seen';
  return 'last run ' + (r.result === 'success' ? 'a success' : r.result === 'failed' ? 'failed' : 'ended') + ', ' + r.minutes + ' min';
}
function syncPlay() {
  if (!viewIs('play')) return;
  $('playLive').hidden = !live.on;
  const s = catalogue.s, list = catalogue.list || [], choice = s && typeof s.choice === 'string' ? s.choice.toLowerCase() : null;
  $('catCount').textContent = live.on && catalogue.list ? String(list.length) : '';
  $('catNote').textContent = !live.on ? '' : catalogue.error || (!catalogue.list ? 'Reading the scenarios folder...'
    : !list.length ? 'No scenario files in BepInEx/MissionLab/scenarios yet. Make one in Create and save it there.' : '');
  const picked = pickedEntry();
  const listKey = JSON.stringify([list.map(e => [e.file, e.hash, e.title, e.usable, e.compat, e.lastRun, e.sealed, sealedEntryHides(e)]), picked && picked.file, choice]);
  if (live.on && listKey !== catalogue.listKey) {
    catalogue.listKey = listKey;
    const ul = $('catList'); ul.textContent = '';
    for (const e of list) {
      const li = document.createElement('li'), b = document.createElement('button'), t = document.createElement('span'), meta = document.createElement('span');
      b.type = 'button'; b.setAttribute('aria-current', String(!!picked && picked.file === e.file));
      t.textContent = (e.title || e.value) + (choice !== null && String(e.value).toLowerCase() === choice ? '  · next mission' : '');
      meta.className = 'meta' + (e.usable ? '' : ' refused');
      const seal = e.sealed ? [sealedEntryHides(e) ? 'sealed' : 'sealed, revealed'] : [];
      meta.textContent = e.usable ? [...seal, e.map || e.navFile || 'map not named', e.compat === 'tested' ? 'tested' : e.compat ? e.compat : 'compatibility unknown', shortLastRun(e.lastRun)].join(' · ')
        : (e.sealed ? seal[0] + ' · ' : '') + (sealedEntryHides(e) ? sealedStrip(e.why) : e.why);
      b.append(t, meta);
      b.addEventListener('click', () => { catalogue.pick = e.file; $('cdNote').textContent = ''; syncPlay(); });
      li.appendChild(b); ul.appendChild(li);
    }
  }
  const ctx = { session: live.session, mission: !!live.mission };
  const hides = sealedEntryHides(picked), over = hides && sealedRunOver(picked);
  const detailKey = JSON.stringify([picked, ctx, choice, live.on, hides, over]);
  if (detailKey === catalogue.detailKey) return;
  catalogue.detailKey = detailKey;
  $('catDetails').hidden = !picked; if (!picked) return;
  const e = picked;
  $('cdTitle').textContent = e.title || e.value;
  // A sealed entry that is not revealed names no enemy count, and its findings name no places (Core does the same).
  $('cdFile').textContent = e.file + (e.id ? ' · id ' + e.id : '') + (e.revision ? ' · revision ' + e.revision : '') + (e.page ? ' · the scenario on this page' : '') + (!hides && e.enemies ? ' · ' + plural(e.enemies, 'enemy', 'enemies') : '');
  $('cdSeal').hidden = !e.sealed;
  $('cdSealChip').textContent = hides ? 'sealed' : 'sealed, revealed';
  $('cdSealChip').title = hides ? 'Spoiler avoidance: Play and Direct leave out where enemies are, how many, rolls and what triggers will do. The file stays readable.' : 'Revealed on this page only: a reload or another tab hides it again.';
  $('cdReveal').hidden = !over;
  const brief = $('cdBriefing'); brief.textContent = '';
  for (const line of e.briefing || []) { const p = document.createElement('p'); p.textContent = line; brief.appendChild(p); }
  const dl = $('cdFacts'); dl.textContent = '';
  for (const [label, text] of catalogueFacts(e, ctx)) { const dt = document.createElement('dt'), dd = document.createElement('dd'); dt.textContent = label; dd.textContent = text; dl.append(dt, dd); }
  const caps = $('cdCaps'); caps.textContent = '';
  for (const c of e.capabilities || []) {
    const li = document.createElement('li'); li.className = 'chip' + (c.tested === true ? ' tested' : c.tested === false ? ' untested' : '');
    li.textContent = (c.tested === true ? '✓ ' : c.tested === false ? '? ' : '') + c.name; li.title = capabilityWords(c); caps.appendChild(li);
  }
  const fl = $('cdFindings'); fl.textContent = '';
  const told = t => (hides ? sealedStrip(t) : t);
  for (const f of (e.findings || []).slice(0, 5)) { const li = document.createElement('li'); li.className = f.level === 'error' ? 'error' : 'note'; li.textContent = (f.level === 'error' ? 'Problem: ' : 'Note: ') + (f.path ? f.path + ': ' : '') + told(f.text); fl.appendChild(li); }
  if (!e.usable && e.why) { const li = document.createElement('li'); li.className = 'error'; li.textContent = told(e.why); fl.appendChild(li); }
  const chosen = choice !== null && String(e.value).toLowerCase() === choice;
  $('cdPlay').hidden = !live.on; $('cdPlay').disabled = !e.usable || chosen;
  $('cdPlay').textContent = chosen ? 'Chosen for the next mission' : 'Play this next';
  $('cdEdit').disabled = !!(live.on && live.mission && !e.page);
  $('cdEdit').title = $('cdEdit').disabled ? 'A mission is running and the page shows it. Open this scenario after the mission.' : 'Open this scenario in Create to change it';
}
$('playOpen').addEventListener('click', () => $('fileInput').click());
$('cdPlay').addEventListener('click', async () => {
  const e = pickedEntry(); if (!e || !live.on) return;
  if (!e.usable) { $('cdNote').textContent = e.why || 'MissionLab would not use this file.'; return; }
  try {
    const s = await api('/api/choose', { value: e.value });
    syncChoices(s); syncModes(s);
    const ok = s && typeof s.choice === 'string' && s.choice.toLowerCase() === String(e.value).toLowerCase();
    $('cdNote').textContent = ok ? 'Chosen. Host a mission' + (e.map || e.navFile ? ' on ' + (e.map || e.navFile) : '') + ' in the game: it uses ' + (e.title || e.value) + '.'
      : 'The game did not take this choice: it does not list ' + e.file + '. Is the file still in the scenarios folder?';
  } catch (err) { $('cdNote').textContent = liveTrouble(err); }
});
$('cdEdit').addEventListener('click', async () => {
  const e = pickedEntry(); if (!e) return;
  if (!live.on || e.page) { setView('create', true); return; }
  if (live.mission) { $('cdNote').textContent = 'A mission is running and the page shows it. Open this scenario after the mission.'; return; }
  if (sealedEntryHides(e)) {
    if (!sealedConfirm(SEAL_ASK)) { $('cdNote').textContent = 'Left sealed: nothing was opened.'; return; }
    sealedRevealEntry(e);
  }
  if (e.sealed) sealing.keep = true;         // revealed just now or before: the load keeps it revealed
  try {
    const r = await fetch('/api/scenariofile/' + encodeURIComponent(e.file), { cache: 'no-store' });
    if (!r.ok) throw new Error('the game did not hand it over (status ' + r.status + ')');
    const text = await r.text();
    let obj; try { obj = JSON.parse(text); } catch (x) { obj = text; }       // not JSON: the strict reader names it, as the plugin does
    const said = loadScenario(obj, e.file);
    sealing.keep = false;
    if (state.refused) { setView('create', true); showTab('mission', true); say(said); return; }
    catalogue.lobbyFile = e.file;
    if (state.navFile) {
      try {
        const m = await fetch('/api/mapfile/' + encodeURIComponent(state.navFile), { cache: 'no-store' });
        if (m.ok) { const buf = await m.arrayBuffer(); state.map = makeMap(await readMapFile(buf), state.navFile, false); state.map.print = mapPrintOf(buf); state.ceiling = Infinity; view3d.dropMap(); fit(); }
      } catch (x) { /* the scenario is open; only its ground is missing */ }
    }
    markSaved(e.file); setView('create', true); showTab('enemies', true); changed();
    say(said + (state.map ? '' : ' Its map file could not be read from the game, so the ground is not shown.'));
  } catch (err) { sealing.keep = false; $('cdNote').textContent = e.file + ' could not be opened: ' + (err && err.message ? err.message : err) + '.'; }
});
$('cdReveal').addEventListener('click', () => { const e = pickedEntry(); if (e) sealedRevealEntry(e); $('cdNote').textContent = 'Revealed on this page. A reload or another tab hides it again.'; });

// ------------------------------------------------------------------ saving into the scenarios folder (between missions)
async function saveToCatalogue(replace) {
  if (!live.on) return;
  const held = moduleBlock();     // an inserted module with an outside reference still to bind (ML-019)
  if (held) { $('saveMsg').textContent = held; return; }
  if (!state.enemies.length) { $('saveMsg').textContent = 'Place at least one enemy first: MissionLab does not use a scenario without enemies.'; return; }
  state.revision++;
  const name = fileName(), text = scenarioText();
  let r = null, body = null;
  try {
    r = await fetch('/api/scenario/save', { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify({ name, text, replace: !!replace }) });
    try { body = await r.json(); } catch (e) { /* no body */ }
  } catch (err) { state.revision--; $('saveMsg').textContent = 'The game did not answer; nothing was saved.'; return; }
  if (r.status === 200 && body && body.saved) {
    if (catalogue.lobbyFile) catalogue.lobbyFile = body.file;
    markSaved(body.file); syncSide(); refreshCatalogue();
    $('saveMsg').textContent = 'Saved as BepInEx/MissionLab/scenarios/' + body.file + ' (revision ' + state.revision + ')' + (body.replaced ? ', written over the old file' : '') + '. Play lists it; choose it there for the next mission.';
    return;
  }
  state.revision--;
  if (r.status === 409 && body && body.exists) {
    if (confirm('A scenario named ' + body.file + ' is already in the scenarios folder. Write over it?')) { await saveToCatalogue(true); return; }
    $('saveMsg').textContent = body.file + ' is already there and was left as it is. Change the file name to save a new one.';
    return;
  }
  const first = body && Array.isArray(body.findings) ? body.findings.filter(f => f && f.level === 'error').slice(0, 3).map(f => (f.path ? f.path + ': ' : '') + f.text) : [];
  $('saveMsg').textContent = 'Not saved: ' + ((body && body.why) || 'the game answered with status ' + r.status) + (first.length ? '. ' + first.join('; ') : '') + '. The Mission tab\'s check lists what to fix.';
}
$('saveCat').addEventListener('click', () => saveToCatalogue(false));
