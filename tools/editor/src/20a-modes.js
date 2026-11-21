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
// saved or opened, so the strip can tell saved from unsaved. savedFile: that file's own text when the page was given it
// (opened from disk or from the game), which can be laid out differently from the page's text of the same scenario.
// lobbyFile: a scenario opened from the catalogue between missions; while it is set, the page keeps it instead of
// clearing itself for the empty lobby.
const catalogue = { list: null, error: '', pick: '', loading: false, savedText: null, savedName: '', savedFile: null, lobbyFile: '', s: null, runSeen: null, wasRunning: false, listKey: '', detailKey: '' };
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
// The page's scenario now equals a file: one just opened or saved. text: the file's own text, when the page has it.
function markSaved(name, text) {
  catalogue.savedText = state.example ? null : scenarioText(); catalogue.savedName = name || fileName();
  catalogue.savedFile = catalogue.savedText !== null && typeof text === 'string' ? text : null;
  syncStrip();
}

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

// ------------------------------------------------------------------ what each capability has been seen to do (ML-036)
// docs/capabilities.json, which tools/editor/build.py writes in place of the literal below; the plugin carries the same file,
// and the README Status table is made from it. In the parts as they are (unbuilt) it stays a string and every capability
// reads as unknown. capJudge and capNeeds mirror Core's Capabilities.Judge and Capabilities.Needs word for word.
const CAPABILITY_LIST = '__ML_CAPABILITIES__';
const CAP_STATUSES = ['supported', 'experimental', 'failing', 'untested', 'unavailable'];
const CAP_SCOPES = ['solo', 'two-player-host-log', 'co-op-client'];
const CAP_RESULTS = ['worked', 'failed', 'partial', 'not-seen'];
const capText = v => (typeof v === 'string' ? v : '');
function capVersion(s) { return typeof s === 'string' && /^\d{1,6}(\.\d{1,6})*$/.test(s.trim()) ? s.trim().split('.').map(Number) : null; }
function capCompare(a, b) { for (let i = 0; i < Math.max(a.length, b.length); i++) { const x = a[i] || 0, y = b[i] || 0; if (x !== y) return x < y ? -1 : 1; } return 0; }
function capSameBuild(a, b) { const x = capVersion(a), y = capVersion(b); return !!x && !!y && capCompare(x, y) === 0; }
function capEvidenceProblem(x) {
  if (x.build !== 'unrecorded' && !capVersion(x.build)) return 'build \'' + x.build + '\' is neither a version nor "unrecorded"';
  if (x.date !== 'unrecorded' && !/^\d{4}-\d{2}-\d{2}$/.test(x.date)) return 'date \'' + x.date + '\' is neither YYYY-MM-DD nor "unrecorded"';
  if (!x.map || !x.mode) return 'map or mode is empty';
  if (!CAP_SCOPES.includes(x.scope)) return 'scope \'' + x.scope + '\' is not one of ' + CAP_SCOPES.join(', ');
  if (!CAP_RESULTS.includes(x.result)) return 'result \'' + x.result + '\' is not one of ' + CAP_RESULTS.join(', ');
  return '';
}
// As Core reads it: a row with an unknown status counts as untested and evidence without a proper build, date or scope is
// left out, so a broken list can only claim less.
function capRead(src) {
  const reg = { records: [], byWord: new Map(), problems: [] };
  let root = src;
  if (typeof src === 'string') { try { root = JSON.parse(src); } catch (e) { root = null; } }
  if (!root || typeof root !== 'object' || Array.isArray(root)) { reg.problems.push('not a JSON object'); return reg; }
  if (root.schema !== 'missionlab-capabilities/1') { reg.problems.push('schema is not missionlab-capabilities/1'); return reg; }
  if (!Array.isArray(root.capabilities)) { reg.problems.push('no capabilities list'); return reg; }
  const ids = new Set();
  root.capabilities.forEach((c, n) => {
    const at = 'capabilities[' + n + ']';
    if (!c || typeof c !== 'object' || Array.isArray(c)) { reg.problems.push(at + ' is not an object'); return; }
    const r = { id: capText(c.id), label: capText(c.label), status: capText(c.status), qualifier: capText(c.qualifier), where: capText(c.where), summary: capText(c.summary), uses: [], evidence: [] };
    if (!r.id || ids.has(r.id)) { reg.problems.push(at + '.id is empty or used twice'); return; }
    ids.add(r.id);
    if (!CAP_STATUSES.includes(r.status)) { reg.problems.push(at + '.status \'' + r.status + '\' is not one of ' + CAP_STATUSES.join(', ') + '; read as untested'); r.status = 'untested'; }
    for (const w of Array.isArray(c.uses) ? c.uses : []) {
      if (typeof w !== 'string' || !w) continue;
      if (reg.byWord.has(w)) { reg.problems.push(at + '.uses: \'' + w + '\' already belongs to ' + reg.byWord.get(w).id); continue; }
      reg.byWord.set(w, r); r.uses.push(w);
    }
    (Array.isArray(c.evidence) ? c.evidence : []).forEach((e, k) => {
      if (!e || typeof e !== 'object' || Array.isArray(e)) { reg.problems.push(at + '.evidence[' + k + '] is not an object'); return; }
      const x = { build: capText(e.build), date: capText(e.date), map: capText(e.map), mode: capText(e.mode), scope: capText(e.scope), result: capText(e.result), note: capText(e.note) };
      const why = capEvidenceProblem(x);
      if (why) { reg.problems.push(at + '.evidence[' + k + ']: ' + why + '; left out'); return; }
      r.evidence.push(x);
    });
    reg.records.push(r);
  });
  return reg;
}
const capList = capRead(CAPABILITY_LIST);
// Newer build first; an unrecorded build is older than every recorded one; at the same build the later date wins.
function capLater(a, b) {
  const x = capVersion(a.build), y = capVersion(b.build);
  if (x && !y) return true;
  if (!x && y) return false;
  const c = x ? capCompare(x, y) : 0;
  if (c) return c > 0;
  const d = v => (v === 'unrecorded' ? '' : v);
  return d(a.date) > d(b.date);
}
function capNewest(r, result) { let best = null; for (const e of r.evidence) if (e.result === result && (!best || capLater(e, best))) best = e; return best; }
const capBuildWords = b => (!b || b === 'unrecorded' ? 'an unrecorded build' : b);
// Who saw it, where and when. Solo and host's-log evidence never reads as a client's.
function capContext(e) {
  const who = e.scope === 'co-op-client' ? 'seen by a client' : e.scope === 'two-player-host-log' ? 'two players, by the host\'s log only' : 'solo';
  return who + (e.map ? ', ' + e.map : '') + (e.date && e.date !== 'unrecorded' ? ', ' + e.date : '');
}
// One capability word for the running build (current; '' when not known, as offline, and then nothing is confirmed).
// Evidence confirms only the build it names: a newer build never inherits "supported" from an older one.
function capJudge(word, current, reg) {
  const list = reg === undefined ? capList : reg, cur = String(current || '').trim();
  const st = { name: word, id: '', label: '', status: '', shown: '', build: '', scope: '', confirmed: false, client: false, words: '' };
  const r = list && list.byWord ? list.byWord.get(word) : null;
  if (!r) { st.shown = 'unknown'; st.words = 'not in this MissionLab\'s capability list, so nothing about it has been recorded in the game'; return st; }
  st.id = r.id; st.label = r.label; st.status = r.status;
  const best = capNewest(r, 'worked');
  st.client = r.evidence.some(e => e.result === 'worked' && e.scope === 'co-op-client');
  if (best) { st.build = best.build; st.scope = best.scope; }
  const seen = best ? 'seen working on ' + capBuildWords(best.build) + ' (' + capContext(best) + ')' : '', noClient = st.client ? '' : '; no client has seen it';
  if (r.status === 'supported') {
    if (!best) { st.shown = 'untested'; st.words = 'recorded as supported, but no evidence says it worked: treat it as untested'; }
    else if (cur && capSameBuild(best.build, cur)) { st.shown = 'supported'; st.confirmed = true; st.words = seen + noClient; }
    else { st.shown = 'seen-earlier'; st.words = seen + (cur ? ', not yet on ' + cur : '; which build the game runs is not known here') + noClient; }
  } else if (r.status === 'failing') {
    const f = capNewest(r, 'failed');
    st.shown = 'failing';
    if (f) { st.build = f.build; st.scope = f.scope; }
    st.words = 'failing: ' + (f ? (f.note || 'it did not work') + ' (' + capBuildWords(f.build) + ', ' + capContext(f) + ')' : 'tried in the game, and it did not work') + '. Using it is an experiment';
  } else if (r.status === 'experimental') {
    st.shown = 'experimental'; st.words = 'experimental: built, but not proven in the game' + (r.qualifier ? ' (' + r.qualifier + ')' : '') + '. Using it is an experiment';
  } else if (r.status === 'unavailable') {
    st.shown = 'unavailable'; st.words = 'unavailable: the game does not do it, so MissionLab does not use it (' + r.where + ')';
  } else {
    st.shown = 'untested'; st.words = 'untested in the game' + (best ? '; only ' + seen + ', which is not what this needs' : '');
  }
  return st;
}
// What an item is, whichever copy the mission uses: "Cell Phone_2" and "cell phone" are both "cell phone" (Core ObjectivePlan.Kind).
function capKind(name) {
  const s = capText(name);
  if (!s) return '';
  let end = s.length;
  while (end > 0 && s[end - 1] >= '0' && s[end - 1] <= '9') end--;
  if (end < s.length && end > 1 && (s[end - 1] === '_' || s[end - 1] === ' ')) end--; else end = s.length;
  return s.slice(0, end).trim().toLowerCase();
}
const capStem = f => capText(f).trim().replace(/^.*[\\/]/, '').replace(/\.[^.]*$/, '');
// What a scenario needs of the map and mode in hand (facts; any part may be unknown: '' or null). An error is a known
// incompatibility; everything else is a note, and never stops anyone.
function capFactNeeds(body, facts) {
  const found = [], add = (level, code, path, text) => found.push({ level, code, path, text });
  if (!facts) return found;
  const b = body || {}, from = facts.source ? ' (' + facts.source + ')' : '';
  if (facts.mode && !facts.pve) add('error', 'wrong-mode', '', 'this is a ' + facts.mode + ' mission, not a PvE raid' + from + ': MissionLab leaves it as the game made it, so this scenario is not used');
  const scene = capText(b.scene).trim(), nav = capStem(b.navFile);
  if (scene && facts.scene) {
    if (!scene.split(',').some(p => p.trim() === '*' || p.trim().toLowerCase() === facts.scene.toLowerCase()))
      add('error', 'wrong-scene', 'scene', 'the scenario is for \'' + scene + '\' and this is \'' + facts.scene + '\'' + from + ': MissionLab does not use it here');
  } else if (!scene && nav && facts.mapFile && nav.toLowerCase() !== capStem(facts.mapFile).toLowerCase()) {
    add('error', 'wrong-map', 'navFile', 'the scenario is for the map file \'' + b.navFile + '\' and this mission uses \'' + facts.mapFile + '\'' + from + ': MissionLab does not use it here');
  }
  if (Array.isArray(facts.items)) {
    const kinds = new Set(facts.items.map(capKind).filter(Boolean)), level = facts.itemsComplete ? 'error' : 'note';
    const missing = facts.itemsComplete ? 'which this map does not have' + from : 'which is not among this mission\'s intel items' + from + '; the game picks the items, so it may be there another time';
    (Array.isArray(b.triggers) ? b.triggers : []).forEach((t, i) => {
      if (!t || typeof t !== 'object' || t.when !== 'intel') return;
      const k = capKind(t.item), any = !k || k === 'any' || k === 'all';
      if (any ? !kinds.size : !kinds.has(k)) add(level, 'missing-item', 'triggers[' + i + '].item', 'trigger ' + t.id + ' waits for ' + (any ? 'intel' : 'the ' + k) + ', ' + missing);
    });
    (Array.isArray(b.objectives) ? b.objectives : []).forEach((o, i) => {
      const k = capKind(o && o.name);
      if (k && !kinds.has(k)) add(level, 'missing-item', 'objectives[' + i + '].name', 'the scenario moves the ' + k + ', ' + missing);
    });
  }
  if (facts.targetKnown && !facts.hasTarget && (Array.isArray(b.spawns) ? b.spawns : []).some(s => s && s.hvt === true))
    add('note', 'no-target', 'spawns', 'the scenario places the target person, and the game\'s mission had none' + from + '; whether the game makes one in another mode is untested');
  return found;
}
function capNeeds(obj, facts, current, reg) {
  const found = [];
  for (const c of capabilitiesOf(obj).map(n => capJudge(n, current, reg))) {
    const say = (code, text) => found.push({ level: 'note', code, path: 'capabilities.' + c.name, text: 'the scenario uses ' + c.name + ', which is ' + text });
    if (c.shown === 'failing') say('capability-failing', c.words);
    else if (c.shown === 'unavailable') say('capability-unavailable', c.words + '; that part of the scenario is left out');
    else if (c.shown === 'experimental') say('capability-experimental', c.words);
  }
  return found.concat(capFactNeeds(planBody(obj), facts));
}
// Facts from a Recon file (missionlab-recon/1), as Core MapFacts.FromRecon: the objective sets list every candidate item of
// the map, so the kinds are complete; target points from raid.hvtSpawnPoints. null when it is not one.
function reconFacts(obj, name) {
  if (!obj || typeof obj !== 'object' || obj.schema !== 'missionlab-recon/1' || !obj.raid || typeof obj.raid !== 'object') return null;
  const raid = obj.raid, ng = obj.navGraphs && typeof obj.navGraphs === 'object' ? obj.navGraphs : {};
  const f = { source: name || '', scene: capText(obj.scene), mapFile: '', navCache: capText(ng.cachedStartupFile), mode: '', pve: false, items: null, itemsComplete: false, targetKnown: false, hasTarget: false };
  const add = n => { const k = capKind(n); if (k && !f.items.includes(k)) f.items.push(k); };
  if (Array.isArray(raid.objectiveSets)) {
    f.items = []; f.itemsComplete = true;
    for (const set of raid.objectiveSets) {
      if (!set || typeof set !== 'object') continue;
      add(set.name);
      for (const o of Array.isArray(set.objectives) ? set.objectives : []) if (o && typeof o === 'object') { add(o.name); add(o.displayName); }
    }
  }
  if (Array.isArray(raid.hvtSpawnPoints)) { f.targetKnown = true; f.hasTarget = raid.hvtSpawnPoints.length > 0; }
  return f;
}
// Whether recon facts are of the scenario's map, as Core MapFacts.Fits. A scenario that names neither scene nor map file is
// judged against the recon file opened beside it: the author has opened it for the map in hand.
function capFitting(facts, obj) {
  if (!facts) return null;
  const b = planBody(obj) || {}, scene = capText(b.scene).trim(), nav = capStem(b.navFile).toLowerCase();
  if (scene) return facts.scene && scene.split(',').some(p => p.trim().toLowerCase() === facts.scene.toLowerCase()) ? facts : null;
  if (!nav) return facts;
  return (facts.navCache && capStem(facts.navCache).toLowerCase() === nav) || (facts.mapFile && capStem(facts.mapFile).toLowerCase() === nav) ? facts : null;
}
// A capability in a few words, for lists.
function capShort(c) {
  if (!c) return 'not known';
  if (c.shown === 'supported') return 'seen working on this build';
  if (c.shown === 'seen-earlier') return 'seen on ' + capBuildWords(c.build) + ', not confirmed on the build in use';
  if (c.shown === 'failing') return 'failing when last tried' + (c.build ? ' (' + capBuildWords(c.build) + ')' : '');
  if (c.shown === 'experimental') return 'experimental';
  if (c.shown === 'unavailable') return 'unavailable in the game';
  if (c.shown === 'untested') return 'untested in the game';
  return 'not recorded';
}
const capRisky = e => ((e && e.capabilities) || []).filter(c => c && (c.shown === 'failing' || c.shown === 'experimental' || c.shown === 'unavailable'));
// Readiness rows for the scenario chosen for the next mission: what it needs that is missing here (the game's catalogue
// judged it against the recon files it has; the page adds the mode the game is set to), and which of its capabilities
// are not confirmed on the plugin's build. 29-controls.js readyRows adds them.
function scenarioReadyRows(st, s) {
  const rows = [], add = (ok, text) => rows.push({ ok, text });
  const sv = s || catalogue.s;
  const e = st && st.role !== 'client' && sv ? entryByValue(sv.choice) : null;
  if (!e || e.page) return rows;
  const name = e.title || e.value;
  const needs = (Array.isArray(e.needs) ? e.needs : []).concat(capFactNeeds({ navFile: e.navFile, scene: e.scene }, { source: 'the game now', mode: st.mode || '', pve: !!st.pve }));
  for (const f of needs) add(false, (f.level === 'error' ? 'Will not run as written: ' : /^capability-/.test(f.code) ? 'Experimental: ' : 'Check: ') + name + ': ' + f.text + '.');
  const caps = Array.isArray(e.capabilities) ? e.capabilities : [], open = caps.filter(c => !c.confirmed);
  if (caps.length) add(!open.length, open.length ? name + ' uses ' + plural(caps.length, 'feature', 'features') + '; not confirmed on MissionLab ' + (st.plugin || '?') + ': ' + open.map(c => c.name + ' (' + capShort(c) + ')').join(', ') + '.'
    : name + ': every feature it uses was seen working on MissionLab ' + (st.plugin || '?') + '.');
  return rows;
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
  // Offline the plugin's build is not known, so nothing is confirmed; a recon file opened for this map supplies its items.
  const needs = capNeeds(obj, capFitting(state.reconFacts || null, obj), '').map(f => (hides ? Object.assign({}, f, { text: sealedStrip(f.text) }) : f));
  const block = needs.find(f => f.level === 'error');
  return {
    page: true, file: name, value: name.replace(/\.json$/, ''), readable: !errors.length, usable: !errors.length && !block && state.enemies.length > 0,
    why: errors.length ? 'MissionLab would refuse it: ' + (errors[0].path ? errors[0].path + ': ' : '') + errors[0].text : block ? 'it cannot run as written here: ' + (block.path ? block.path + ': ' : '') + block.text : '', findings: found,
    title: typeof body.name === 'string' ? body.name : '', id: typeof body.id === 'string' ? body.id : '', revision: body.revision | 0, sealed: state.sealed === true,
    briefing: Array.isArray(body.briefing) ? body.briefing : [], author: { squad: Array.isArray(body.squad) ? body.squad : null, minutes: num(body.minutes, 0) > 0 ? body.minutes : null },
    measured: null, navFile: typeof body.navFile === 'string' ? body.navFile : '', scene: typeof body.scene === 'string' ? body.scene : '', map: '', mode: 'pve',
    compat: null, enemies: hides ? null : state.enemies.length, capabilities: capabilitiesOf(obj).map(n => capJudge(n, '')), needs, lastRun: undefined,
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
  // Each capability with what was recorded for it in the game, and what the scenario needs that is missing here (ML-036).
  const caps = Array.isArray(e.capabilities) ? e.capabilities : [];
  if (caps.length) facts.push(['Features, as recorded in the game', caps.map(c => c.name + ': ' + capShort(c)).join('; ')]);
  const needs = Array.isArray(e.needs) ? e.needs : [];
  if (needs.length) facts.push(['Before you choose it', needs.map(f => (f.level === 'error' ? 'Will not run as written: ' : /^capability-/.test(f.code) ? 'Experimental: ' : 'Check: ') + f.text + '.').join(' ')]);
  const risky = capRisky(e);
  if (risky.length && e.usable) facts.push(['Choosing it', 'an experiment: ' + risky.map(c => c.name + ' is ' + capShort(c)).join(', ') + '. It can still be played; expect that part not to work']);
  const last = lastRunWords(e.lastRun, ctx);
  facts.push(['Last run', sealedEntryHides(e) ? sealedStrip(last) : last]);
  return facts;
}
function capabilityWords(c) { return c.name + ': ' + (c.words || capShort(c)); }
function shortLastRun(r) {
  if (!r) return 'never played';
  if (r.outcome === 'refused') return 'last run refused';
  if (typeof r.minutes !== 'number') return 'last run: no end seen';
  return 'last run ' + (r.result === 'success' ? 'a success' : r.result === 'failed' ? 'failed' : 'ended') + ', ' + r.minutes + ' min';
}
// The line under a row's title. Rows with the same title (a copy or a new revision installed beside the original) are
// told apart by file name and revision only, which say nothing more about a sealed mission than its row already does.
function catalogueRowMeta(e, list) {
  const shown = x => String(x.title || x.value || '').trim().toLowerCase();
  const which = list.some(x => x !== e && shown(x) === shown(e)) ? [e.file + ((e.revision | 0) ? ' · revision ' + (e.revision | 0) : '')] : [];
  const seal = e.sealed ? [sealedEntryHides(e) ? 'sealed' : 'sealed, revealed'] : [];
  if (!e.usable) return [...seal, ...which, sealedEntryHides(e) ? sealedStrip(e.why) : e.why].join(' · ');
  const risky = capRisky(e).map(c => c.name + ' ' + c.shown);
  return [...seal, ...which, e.map || e.navFile || 'map not named', e.compat === 'tested' ? 'tested' : e.compat ? e.compat : 'compatibility unknown', ...risky, shortLastRun(e.lastRun)].join(' · ');
}
function syncPlay() {
  if (!viewIs('play')) return;
  $('playLive').hidden = !live.on;
  const s = catalogue.s, list = catalogue.list || [], choice = s && typeof s.choice === 'string' ? s.choice.toLowerCase() : null;
  $('catCount').textContent = live.on && catalogue.list ? String(list.length) : '';
  $('catNote').textContent = !live.on ? '' : catalogue.error || (!catalogue.list ? 'Reading the scenarios folder...'
    : !list.length ? 'No scenario files in BepInEx/MissionLab/scenarios yet. Make one in Create and save it there.' : '');
  const picked = pickedEntry();
  const listKey = JSON.stringify([list.map(e => [e.file, e.hash, e.title, e.revision, e.usable, e.compat, e.lastRun, e.sealed, sealedEntryHides(e), capRisky(e).map(c => c.name)]), picked && picked.file, choice]);
  if (live.on && listKey !== catalogue.listKey) {
    catalogue.listKey = listKey;
    const ul = $('catList'); ul.textContent = '';
    for (const e of list) {
      const li = document.createElement('li'), b = document.createElement('button'), t = document.createElement('span'), meta = document.createElement('span');
      b.type = 'button'; b.setAttribute('aria-current', String(!!picked && picked.file === e.file));
      t.textContent = (e.title || e.value) + (choice !== null && String(e.value).toLowerCase() === choice ? '  · next mission' : '');
      meta.className = 'meta' + (e.usable ? '' : ' refused');
      meta.textContent = catalogueRowMeta(e, list);
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
    // Confirmed only for the build in use; failing and unavailable are marked apart from merely unproven.
    const bad = c.shown === 'failing' || c.shown === 'unavailable', known = !!c.shown && c.shown !== 'unknown';
    const li = document.createElement('li'); li.className = 'chip' + (c.confirmed ? ' tested' : known ? ' untested' : '');
    const tag = c.confirmed || !known ? '' : c.shown === 'seen-earlier' ? ' · seen on ' + capBuildWords(c.build) : ' · ' + c.shown;
    li.textContent = (c.confirmed ? '✓ ' : bad ? '✗ ' : known ? '? ' : '') + c.name + tag; li.title = capabilityWords(c); caps.appendChild(li);
  }
  const fl = $('cdFindings'); fl.textContent = '';
  const told = t => (hides ? sealedStrip(t) : t);
  for (const f of (e.findings || []).slice(0, 5)) { const li = document.createElement('li'); li.className = f.level === 'error' ? 'error' : 'note'; li.textContent = (f.level === 'error' ? 'Problem: ' : 'Note: ') + (f.path ? f.path + ': ' : '') + told(f.text); fl.appendChild(li); }
  if (!e.usable && e.why) { const li = document.createElement('li'); li.className = 'error'; li.textContent = told(e.why); fl.appendChild(li); }
  const chosen = choice !== null && String(e.value).toLowerCase() === choice, risky = capRisky(e);
  $('cdPlay').hidden = !live.on; $('cdPlay').disabled = !e.usable || chosen;
  // Choosing a scenario with something failing or experimental stays possible, and the button says it is an experiment.
  $('cdPlay').textContent = chosen ? 'Chosen for the next mission' + (risky.length ? ' (experimental)' : '') : risky.length ? 'Play this next (experimental)' : 'Play this next';
  $('cdPlay').title = risky.length ? 'Uses ' + risky.map(c => c.name + ' (' + capShort(c) + ')').join(', ') + ': that part may not work.' : '';
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
    const ok = s && typeof s.choice === 'string' && s.choice.toLowerCase() === String(e.value).toLowerCase(), risky = capRisky(e);
    $('cdNote').textContent = ok ? 'Chosen. Host a mission' + (e.map || e.navFile ? ' on ' + (e.map || e.navFile) : '') + ' in the game: it uses ' + (e.title || e.value) + '.'
        + (risky.length ? ' This is an experiment: ' + risky.map(c => c.name + ' is ' + capShort(c)).join(', ') + '.' : '')
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
    markSaved(e.file, text); setView('create', true); showTab('enemies', true); changed();
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
expose({ capList, capRead, capJudge, capNeeds, capFactNeeds, capKind, capFitting, reconFacts, scenarioReadyRows, catalogueRowMeta });
