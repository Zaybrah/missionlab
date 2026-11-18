// ------------------------------------------------------------------ sealed missions (ML-021)
// A sealed mission is for a host who wants to find the enemies along with the squad. Play and Direct then show what a
// player may know before the mission (briefing, map, mode, what it uses, the author's squad and minutes, the options, and
// whether MissionLab would take it) and leave out where enemies stand, how many there are, what was rolled and what
// triggers will do. This is spoiler avoidance, not secrecy: the file stays readable, and the game sends the page what it
// always sent; only what is drawn and written here changes.
// What was revealed is kept in memory only, never in storage, so a reload or another tab starts sealed again.
const SEAL_ASK = 'This mission is sealed: opening it shows every enemy. Reveal?';
const SEAL_REPLAY_ASK = 'This replay is of a sealed mission: watching it shows every enemy. Reveal?';
// page: the scenario on the page was revealed (cleared whenever a scenario is loaded; keep carries an explicit reveal over
// a load). keys: scenarios revealed on this page, 'id:<scenario id>', or 'file:<name>' / 'session:<mission>' without an id.
// run: the key of the sealed mission running now; ended: a sealed run that has ended here and was not revealed.
const sealing = { page: false, keep: false, keys: new Set(), run: '', ended: '' };

// A place written into a message: "at (12.5, -4)", "from 12, 34", "near 1, 2, 3" or a bare "(12.5, -4)". The same pattern
// is CatalogueInfo.Unplaced in Core, so the game's findings and the page's read alike.
const SEAL_SPOT = /\s*(?:\b(?:at|from|near)\s+\(?|\()\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?(?:\s*,\s*-?\d+(?:\.\d+)?)?(?:\s*\))?/g;
const sealedStrip = text => String(text === undefined || text === null ? '' : text).replace(SEAL_SPOT, '');

// The page shows the running mission (not a scenario opened between missions).
const sealedRunning = () => !!(live.on && live.mission && !catalogue.lobbyFile);
const sealedEntryKey = e => (e && e.id ? 'id:' + e.id : e && e.file ? 'file:' + e.file : '');
const sealedRunKey = s => (s && s.scenarioId ? 'id:' + s.scenarioId : 'session:' + (s && typeof s.session === 'string' ? s.session : ''));
// The catalogue entry of the mission running now: by its scenario id, or by its run record.
function sealedRunEntry(s) {
  if (!s || !catalogue.list) return null;
  return catalogue.list.find(e => s.scenarioId && e.id === s.scenarioId) || (s.run ? catalogue.list.find(e => e.lastRun && e.lastRun.file === s.run) : null) || null;
}
// The game says so in its state; a plugin that does not is answered by the catalogue. A refused scenario left the game's
// own mission running, which is not sealed.
function sealedLive(s) {
  if (!s || !s.mission) return false;
  if (s.sealed === true) return true;
  if (s.rejected) return false;
  const e = sealedRunEntry(s);
  return !!(e && e.sealed === true);
}
const sealedPageRevealed = () => sealing.page || (!!state.scenarioId && sealing.keys.has('id:' + state.scenarioId));
function sealedOn() { return sealedRunning() ? sealedLive(catalogue.s) : state.sealed === true; }
function sealedRevealed() { return sealedRunning() ? sealing.keys.has(sealedRunKey(catalogue.s)) : sealedPageRevealed(); }
// The one question the map, the inspector and Direct ask: is something to be left out right now?
function sealedHides() { return sealedOn() && !sealedRevealed(); }
// The same for a Play entry: the scenario on the page offline, or one of the catalogue.
function sealedEntryHides(e) {
  if (!e || e.sealed !== true) return false;
  return e.page ? !sealedPageRevealed() : !sealing.keys.has(sealedEntryKey(e));
}
// A run of this entry has ended: its last run is not the mission running now, or the page saw it end.
function sealedRunOver(e) {
  if (!e || !e.sealed) return false;
  if (sealing.ended && sealing.ended === sealedEntryKey(e)) return true;
  const r = e.lastRun;
  return !!r && !(live.mission && r.session && r.session === live.session);
}

// Revealing: an explicit act, by a button or by answering the question; never by a stray key or a reload.
function sealedReveal() {
  if (sealedRunning()) sealing.keys.add(sealedRunKey(catalogue.s));
  else { sealing.page = true; if (state.scenarioId) sealing.keys.add('id:' + state.scenarioId); }
  if (sealing.ended && sealing.keys.has(sealing.ended)) sealing.ended = '';
  sealedChanged();
}
function sealedRevealEntry(e) {
  const k = sealedEntryKey(e);
  if (k) sealing.keys.add(k);
  if (e && e.page) sealing.page = true;
  if (sealing.ended && sealing.ended === k) sealing.ended = '';
  sealedChanged();
}
function sealedRevealEnded() { if (sealing.ended) sealing.keys.add(sealing.ended); sealing.ended = ''; sealedChanged(); }
const sealedConfirm = text => { try { return confirm(text) === true; } catch (e) { return false; } };
// Before Create shows a sealed mission: true when it may (not sealed, already revealed, or revealed now).
function sealedAsk() {
  if (!sealedHides()) return true;
  if (!sealedConfirm(SEAL_ASK)) return false;
  sealedReveal();
  return true;
}
function sealedChanged() {
  catalogue.listKey = ''; catalogue.detailKey = '';
  syncSeal(); view3d.syncEnemies(); view3d.syncLive(); syncInspect(true); invalidate(); syncStrip();
  if (viewIs('play')) syncPlay();
}

// Every way a scenario reaches the page (a file, the draft kept in this browser, the catalogue, a revision, a replay's
// plan) goes through loadScenario, so a sealed scenario arrives sealed unless this page has revealed that very scenario
// (by its id). Its message then leaves out the enemy count, and Create (which lists every enemy) gives way to Play.
{
  const read = loadScenario;
  loadScenario = function (obj, name) {
    sealing.page = sealing.keep; sealing.keep = false;
    const said = read(obj, name);
    if (state.refused || !state.sealed || sealedPageRevealed()) return said;
    if (viewIs('create') && !sealedRunning()) setView('play');
    return 'Sealed: enemies loaded from ' + name + ' and not shown. Play shows its briefing and what it needs; Create asks before it shows the enemies.';
  };
}
// A replay of a sealed mission shows every enemy where it was, so it opens only once the mission is revealed.
function sealedReplayKey(obj) {
  const plan = obj && obj.plan && typeof obj.plan === 'object' ? planBody(obj.plan) : null;
  const byRun = obj && obj.run && catalogue.list ? catalogue.list.find(e => e.lastRun && e.lastRun.file === obj.run) : null;
  if (!(plan && plan.sealed === true) && !(byRun && byRun.sealed === true)) return '';
  const id = plan && typeof plan.id === 'string' && plan.id ? plan.id : byRun && byRun.id ? byRun.id : '';
  return id ? 'id:' + id : 'session:' + (obj && typeof obj.session === 'string' ? obj.session : '');
}
{
  const watch = loadReplay;
  loadReplay = function (obj, name) {
    const key = sealedReplayKey(obj);
    if (key && !sealing.keys.has(key)) {
      if (!sealedConfirm(SEAL_REPLAY_ASK)) throw new Error('It is a replay of a sealed mission and stays closed, so nothing is revealed.');
      sealing.keys.add(key); if (sealing.ended === key) sealing.ended = '';
      sealedChanged();
    }
    return watch(obj, name);
  };
}

// Every state the game sends (syncModes calls this): which mission runs, whether it is sealed, and when it has ended.
function sealedSeen(s, running) {
  const was = sealing.run;
  if (running) {
    // A plugin that does not say "sealed" in its state is answered by the catalogue, which then has to be read.
    if (!(s && s.sealed === true) && !catalogue.list && !catalogue.loading && !catalogue.error) refreshCatalogue();
    sealing.run = sealedLive(s) ? sealedRunKey(s) : '';
    if (sealing.run) sealing.ended = '';
    if (sealedHides() && viewIs('create')) {
      setView('direct');
      say('This mission is sealed, so the page shows Direct, which leaves the enemies out. Open Create to reveal them.');
    }
  } else if (sealing.run) {
    if (!sealing.keys.has(sealing.run)) sealing.ended = sealing.run;
    sealing.run = '';
  }
  if (was !== sealing.run) { view3d.syncEnemies(); syncInspect(true); invalidate(); }
  syncSeal();
}

// The Sealed tick in Create's Mission tab, and the Direct box that says what is left out and offers the reveal.
function syncSeal() {
  const box = $('sealSealed');
  if (box) {
    box.checked = state.sealed === true;
    box.disabled = sealedRunning();
    box.title = box.disabled ? 'The running mission keeps what it started with. Seal the scenario between missions.' : 'Play and Direct leave out where enemies are, how many, what is rolled and what triggers will do';
  }
  const sb = $('sealBox'); if (!sb) return;
  const running = sealedRunning() && sealedOn(), ended = !sealedRunning() && !!sealing.ended;
  sb.hidden = !running && !ended;
  if (sb.hidden) return;
  const hides = running && sealedHides();
  $('sealNote').textContent = ended ? 'The sealed mission has ended. Its enemies, rolls and replay stay hidden until you reveal them.'
    : hides ? 'This mission is sealed: the map leaves out enemies, routes, trigger zones and placed things, and the inspector does not say what triggers wait for or will do. Players, intel, doors and places are shown. This is spoiler avoidance: the files stay readable.'
    : 'This mission is sealed and revealed on this page: everything is shown. A reload or another tab hides it again.';
  $('sealReveal').hidden = !hides && !ended;
  $('sealReveal').textContent = ended ? 'Reveal now the run is over' : 'Reveal the mission';
}
// The author's tick. Whoever ticks it is the author and has seen everything, so the page stays revealed for them.
function sealedSet(on) {
  if (sealedRunning()) { syncSeal(); return; }
  if (on) { sealing.page = true; if (state.scenarioId) sealing.keys.add('id:' + state.scenarioId); }
  missionEdit(() => { state.sealed = !!on; });
  say(on ? 'Sealed: Play and Direct leave out enemies, rolls and what triggers will do. It stays revealed on this page until you reload it.' : 'Not sealed: Play and Direct show everything.');
}
$('sealSealed').addEventListener('change', ev => sealedSet(ev.target.checked));
$('sealReveal').addEventListener('click', () => { if (sealedRunning()) sealedReveal(); else sealedRevealEnded(); });
