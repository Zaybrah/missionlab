// ------------------------------------------------------------------ the session playlist in Play (ML-049)
// A host's agenda for an evening: missions in order, each pinned as it was added (file, id, revision and the text's
// fingerprint), with its options, the squad size expected and its dice: a new roll, an exact repeat of one run, or the
// setup of the last failed run. The game keeps every playlist in BepInEx/MissionLab/playlists (Playlists.cs), so a reload
// of the page or a restart of the game keeps the agenda and its next entry; this page only shows and edits it.
// Setting an entry up uses the game's own paths (the next-mission choice, the option picks, the dice) and says which map
// and mode to host: the lobby stays the host's. A scenario that is missing or changed since it was pinned is never set up
// silently: the host chooses to use it as it is now, to use another mission, or to remove the entry. How a run ended is
// the game's record; an end it did not see stays unknown or incomplete until the host marks it, and that mark says so.
// Core's Playlist (src/MissionLab.Core/Playlist.cs) holds the same rules for identity, results and advancing.
state.playlist = state.playlist || { lists: [], file: '', data: null, runs: [], note: '', trouble: '', key: '', pending: null };
const PL_SCHEMA = 'missionlab-playlist/1';
const PL_MOST = 50;
const PL_SEEDS = [['new', 'A new roll'], ['repeat', 'Repeat one run exactly'], ['retry', 'Retry the last failed setup']];
const plStr = v => (typeof v === 'string' ? v : '');

// ---- what the game said
function playlistTake(d) {
  const pl = state.playlist;
  if (!d || !Array.isArray(d.playlists)) throw new Error('not a playlist answer');
  pl.lists = d.playlists.filter(p => p && typeof p.file === 'string');
  pl.file = plStr(d.selected);
  pl.data = d.playlist && typeof d.playlist === 'object' && Array.isArray(d.playlist.entries) ? d.playlist : null;
  if (pl.data && !Array.isArray(pl.data.results)) pl.data.results = [];
  pl.runs = Array.isArray(d.runs) ? d.runs.filter(r => r && typeof r.file === 'string') : [];
  pl.trouble = plStr(d.trouble) ? 'The playlist in use cannot be read: ' + d.trouble + '.' : '';
  if (plStr(d.said)) pl.note = d.said;
  return d;
}
// Asked whenever Play reads the catalogue (on opening Play, every 15 s, after each mission). A second call while one is
// on its way waits for that one.
function playlistRefresh() {
  const pl = state.playlist;
  if (!live.on) return Promise.resolve();
  if (pl.pending) return pl.pending;
  pl.pending = (async () => {
    try {
      const r = await fetch('/api/playlists', { cache: 'no-store' });
      if (r.status === 404) pl.trouble = 'The game runs an older MissionLab without playlists.';
      else playlistTake(await r.json());
    } catch (e) { pl.trouble = 'The game did not hand over the playlists. Is it still running?'; }
    pl.pending = null;
    syncPlaylist();
  })();
  return pl.pending;
}
// The files-only requests: answered by the game at once, in the lobby and during a mission alike.
async function playlistPost(path, body) {
  const r = await fetch(path, { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify(body) });
  let d = null; try { d = await r.json(); } catch (e) { /* no body */ }
  if (r.status !== 200) throw new Error((d && d.error) || 'the game answered with status ' + r.status);
  return playlistTake(d);
}

// ---- entries, as Core's PlaylistEntry
function playlistEntryOf(e) {
  const squad = e.author && Array.isArray(e.author.squad) && e.author.squad.length === 2 ? e.author.squad[1] | 0 : 0, picks = {};
  for (const o of e.options || []) if (o && typeof o.id === 'string') picks[o.id] = o.pick | 0;
  return { label: e.title || e.value, file: e.value, id: e.id || '', revision: e.revision | 0, hash: e.hash || '', map: e.map || '', navFile: e.navFile || '', mode: e.mode || 'pve', squad, picks, seed: 'new', record: '' };
}
// Same, missing or changed, as Core's Playlist.Identity: an entry without a fingerprint counts as changed.
function playlistIdentity(en, list) {
  if (!Array.isArray(list)) return { state: 'unknown', text: 'not checked yet: the catalogue has not been read', now: null };
  const e = list.find(x => String(x.value).toLowerCase() === String(en.file).toLowerCase()) || null;
  if (!e) return { state: 'missing', text: 'scenarios/' + en.file + '.json is not there any more', now: null };
  const said = [], id = plStr(e.id);
  if (en.id && id !== en.id) said.push(id ? 'the file now holds scenario ' + id + ', not ' + en.id : 'the file no longer has scenario id ' + en.id);
  if ((e.revision | 0) !== (en.revision | 0)) said.push('revision ' + (en.revision | 0) + ' when it was added, revision ' + (e.revision | 0) + ' now');
  if (!en.hash) said.push('it was added without a fingerprint, so whether it changed cannot be told');
  else if (e.hash !== en.hash && !said.length) said.push('its text has changed since it was added, under the same revision number');
  return { state: said.length ? 'changed' : 'same', text: said.join('; '), now: e };
}
// Whether a run was of the entry's scenario: the ids decide when both have one, else the file name (Core's Playlist.Belongs).
function playlistBelongs(en, r) {
  if (en.id && r.scenarioId) return en.id === r.scenarioId;
  return !!r.scenario && String(r.scenario).replace(/\.json$/i, '').toLowerCase() === String(en.file).toLowerCase();
}
const playlistName = en => (en ? en.label || en.file : '');
function playlistPinWords(en) {
  return [en.file + '.json', en.id ? 'id ' + en.id : 'no scenario id', 'revision ' + (en.revision | 0), en.hash ? 'fingerprint ' + String(en.hash).replace(/^[a-z0-9]+:/i, '').slice(0, 8) : 'no fingerprint'].join(' · ');
}
function playlistMapWords(en) {
  const map = en.map && en.navFile && en.map !== en.navFile ? en.map + ' (' + en.navFile + ')' : en.map || en.navFile || 'the map the scenario is made for';
  const mode = !en.mode || en.mode === 'pve' ? 'a PvE raid' : 'a ' + en.mode + ' raid';
  return 'In the game, host ' + map + ' as ' + mode + '. MissionLab does not switch the map for you.';
}
function playlistSeedWords(en) {
  if (en.seed === 'new') return 'a new roll';
  if (en.seed === 'repeat') return en.record ? 'the exact setup of run ' + en.record : 'repeat one run: none chosen yet';
  if (en.seed === 'retry') return 'the setup of the last failed run of this scenario';
  return 'dice setting "' + en.seed + '", which this page does not know';
}
function playlistPickWords(en) {
  const e = entryByValue(en.file), picks = en.picks && typeof en.picks === 'object' ? en.picks : {}, ids = Object.keys(picks).sort();
  if (!ids.length) return 'the author\'s defaults';
  return ids.map(id => {
    const o = e && Array.isArray(e.options) ? e.options.find(q => q.id === id) : null, at = picks[id] | 0;
    return (o ? o.name || id : id) + ': ' + (o && Array.isArray(o.choices) && o.choices[at] ? o.choices[at] : 'choice ' + (at + 1));
  }).join('; ');
}
function playlistResultWords(res) {
  if (!res) return '';
  const how = res.result === 'success' ? 'a success' : res.result === 'failed' ? 'failed' : res.result === 'refused' ? 'refused, so it was not played'
    : res.result === 'incomplete' ? 'incomplete: its end was not seen' : 'unknown: the game did not say how it ended';
  return res.manual ? how + ' (marked by the host)' : how + (res.result === 'success' || res.result === 'failed' ? ', as the game recorded it' : '');
}
function playlistRunWords(r) {
  const when = String(r.started || '').slice(0, 16).replace('T', ' ');
  const how = r.result === 'success' ? 'a success' : r.result === 'failed' ? 'failed' : r.result ? 'ended, how not known' : 'no end seen';
  return (when ? when + ' UTC' : r.file) + ', ' + how;
}
// The rows Play lists: what each entry pins, the map and mode to host, and what stands in the way of setting it up.
function playlistRows() {
  const d = state.playlist.data; if (!d) return [];
  const list = live.on ? catalogue.list : null, next = d.next | 0;
  return d.entries.map((en, i) => {
    const id = playlistIdentity(en, list), bad = id.state === 'missing' || id.state === 'changed';
    return {
      index: i, label: playlistName(en), next: i === next, armed: i === next && d.armed === true, pinned: playlistPinWords(en), instruction: playlistMapWords(en),
      squad: en.squad > 0 ? 'for ' + plural(en.squad, 'player', 'players') : 'squad size not said', options: playlistPickWords(en), seed: playlistSeedWords(en),
      identity: id.state, problem: bad ? id.text : '', choices: id.state === 'changed' ? ['current', 'other', 'remove'] : id.state === 'missing' ? ['other', 'remove'] : [], canSetUp: !bad,
    };
  });
}
const playlistLast = () => { const d = state.playlist.data; return d && d.results.length ? d.results[d.results.length - 1] : null; };
// Only an end that is not known, of the entry still being played, can be marked; a mark moves the playlist on once.
const playlistCanMark = (res, d) => !!(res && d && !res.advanced && !res.manual && (res.result === 'unknown' || res.result === 'incomplete') && res.entry === (d.next | 0));
function playlistOutcome() {
  const d = state.playlist.data; if (!d) return '';
  const res = playlistLast(), en = d.entries[d.next | 0];
  const next = !en ? 'The playlist is done.' : 'Next: ' + playlistName(en) + (d.armed ? ', set up in the game. ' : '. Choose Set up for the next mission when you are ready. ') + playlistMapWords(en);
  return (res ? 'Last run, ' + (res.label || 'entry ' + ((res.entry | 0) + 1)) + ': ' + playlistResultWords(res) + '. ' : '') + next;
}

// ---- changes, saved by the game
function playlistCopy() {
  const d = state.playlist.data;
  return d ? JSON.parse(JSON.stringify({ schema: PL_SCHEMA, name: d.name, next: d.next | 0, entries: d.entries })) : null;
}
async function playlistSave(p, done) {
  const pl = state.playlist;
  try { const d = await playlistPost('/api/playlists/save', { file: pl.file, playlist: p }); pl.note = plStr(d.said) || done; }
  catch (err) { pl.note = 'Not saved: ' + (err && err.message ? err.message : err) + '. The playlist is as it was.'; }
  syncPlaylist();
}
async function playlistNew() {
  if (!live.on) return;
  const name = String($('plName').value || '').trim() || 'Tonight';
  state.playlist.file = '';
  await playlistSave({ schema: PL_SCHEMA, name, next: 0, entries: [] }, 'Playlist ' + name + ' started. Pick a mission above and add it.');
}
async function playlistAdd() {
  const pl = state.playlist;
  if (!live.on) return;
  const e = pickedEntry();
  if (!e || e.page) { pl.note = 'Pick a mission in the list above first.'; syncPlaylist(); return; }
  if (!e.usable) { pl.note = (e.title || e.value) + ' cannot be added: ' + (e.why || 'MissionLab would not use it') + '.'; syncPlaylist(); return; }
  let p = playlistCopy();
  if (!p) { pl.file = ''; p = { schema: PL_SCHEMA, name: String($('plName').value || '').trim() || 'Tonight', next: 0, entries: [] }; }
  if (p.entries.length >= PL_MOST) { pl.note = 'A playlist holds at most ' + PL_MOST + ' missions.'; syncPlaylist(); return; }
  p.entries.push(playlistEntryOf(e));
  await playlistSave(p, 'Added ' + (e.title || e.value) + ' as mission ' + p.entries.length + ' of ' + p.name + '.');
}
async function playlistSet(i, change, done) {
  const p = playlistCopy(); if (!p || !p.entries[i]) return;
  change(p.entries[i], p);
  await playlistSave(p, done);
}
async function playlistMove(i, by) {
  const p = playlistCopy(), j = i + by; if (!p || j < 0 || j >= p.entries.length) return;
  [p.entries[i], p.entries[j]] = [p.entries[j], p.entries[i]];
  if (p.next === i) p.next = j; else if (p.next === j) p.next = i;      // the next entry stays the same mission
  await playlistSave(p, 'Moved ' + playlistName(p.entries[j]) + ' to place ' + (j + 1) + '.');
}
async function playlistRemove(i) {
  const p = playlistCopy(); if (!p || !p.entries[i]) return;
  const gone = p.entries.splice(i, 1)[0];
  if (i < p.next) p.next--;
  p.next = Math.min(p.next, p.entries.length);
  await playlistSave(p, 'Removed ' + playlistName(gone) + ' from the playlist.');
}
// A missing or changed scenario: the host's explicit choice. 'current' pins the file as it is now, 'other' the mission
// picked in the list above, 'remove' drops the entry. The host's own name, squad size and dice stay; an option keeps its
// pick only where the scenario still offers it.
async function playlistResolve(i, how) {
  const pl = state.playlist, d = pl.data;
  if (!live.on || !d || !d.entries[i]) return;
  if (how === 'remove') { await playlistRemove(i); return; }
  const en = d.entries[i], id = playlistIdentity(en, catalogue.list);
  const e = how === 'current' ? id.now : pickedEntry();
  if (!e || e.page) { pl.note = how === 'current' ? 'The file is not there any more: use the picked mission instead, or remove the entry.' : 'Pick a mission in the list above first.'; syncPlaylist(); return; }
  if (!e.usable) { pl.note = (e.title || e.value) + ' cannot be used: ' + (e.why || 'MissionLab would not use it') + '.'; syncPlaylist(); return; }
  const fresh = playlistEntryOf(e), same = String(e.value).toLowerCase() === String(en.file).toLowerCase();
  if (en.label && same) fresh.label = en.label;
  if (en.squad > 0) fresh.squad = en.squad;
  fresh.seed = en.seed;
  fresh.record = same ? en.record || '' : '';          // a run of another scenario is not this one's setup
  for (const o of e.options || []) {
    const was = en.picks && Object.prototype.hasOwnProperty.call(en.picks, o.id) ? en.picks[o.id] | 0 : -1;
    if (was >= 0 && was < (o.choices || []).length) fresh.picks[o.id] = was;
  }
  await playlistSet(i, (x, p) => { p.entries[i] = fresh; }, 'Mission ' + (i + 1) + ' now uses ' + (e.title || e.value) + ', revision ' + (e.revision | 0) + ', as it is now.');
}
// Setting an entry up for the next mission: the game checks the file and the dice again before changing anything.
async function playlistArm(i) {
  const pl = state.playlist, d = pl.data;
  if (!live.on || !d || !d.entries[i]) return false;
  const en = d.entries[i], id = playlistIdentity(en, catalogue.list);
  if (id.state === 'missing' || id.state === 'changed') {
    pl.note = playlistName(en) + ' was not set up: ' + id.text + '. Choose Use it as it is now, Use the picked mission or Remove first.';
    syncPlaylist(); return false;
  }
  try {
    const answer = await api('/api/playlists/arm', { file: pl.file, next: i });
    playlistTake(answer);
    pl.note = plStr(answer.said) || 'Set up: ' + playlistName(en) + '.';
    live.runsFor = null; opts.picksFor = '';      // the Dice list and the Options box ask the game again
    syncPlaylist(); return true;
  } catch (err) {
    pl.note = err && err.status === 409 && err.result !== 'stale' ? err.message : liveTrouble(err);
    syncPlaylist(); return false;
  }
}
async function playlistMark(result) {
  const pl = state.playlist, d = pl.data, res = playlistLast();
  if (!live.on || !playlistCanMark(res, d)) return;
  try { const answer = await playlistPost('/api/playlists/mark', { record: res.record, result }); pl.note = plStr(answer.said); }
  catch (err) { pl.note = 'Not marked: ' + (err && err.message ? err.message : err) + '.'; }
  syncPlaylist();
}
async function playlistSelect(file) {
  try { await playlistPost('/api/playlists/select', { file }); state.playlist.note = ''; }
  catch (err) { state.playlist.note = 'The game did not change the playlist in use: ' + (err && err.message ? err.message : err) + '.'; }
  syncPlaylist();
}
async function playlistDelete() {
  const pl = state.playlist; if (!live.on || !pl.file) return;
  const d = pl.data;
  if (!confirm('Delete the playlist ' + ((d && d.name) || pl.file) + '? Its run records stay.')) return;
  try { await playlistPost('/api/playlists/delete', { file: pl.file }); }
  catch (err) { pl.note = 'Not deleted: ' + (err && err.message ? err.message : err) + '.'; }
  syncPlaylist();
}

// ---- drawing the section
const plEl = (tag, cls, text) => { const el = document.createElement(tag); if (cls) el.className = cls; if (text !== undefined) el.textContent = text; return el; };
function plButton(text, cls, title, fn) {
  const b = plEl('button', 'btn' + (cls ? ' ' + cls : ''), text); b.type = 'button'; if (title) b.title = title;
  b.addEventListener('click', fn); return b;
}
function playlistEntryEl(row, en) {
  const d = state.playlist.data, li = plEl('li', (row.next ? 'next' : '') + (row.problem ? ' problem' : ''));
  const head = plEl('div', 'head'); head.append(plEl('span', '', row.label));
  if (row.next) head.append(plEl('span', 'chip info', row.armed ? 'next · set up in the game' : 'next'));
  li.append(head, plEl('div', 'meta', row.pinned), plEl('div', '', row.instruction), plEl('div', 'meta', 'Options: ' + row.options + '. Dice: ' + row.seed + '.'));
  const set = plEl('div', 'row');
  const squad = plEl('input'); squad.type = 'number'; squad.min = '0'; squad.max = '8'; squad.value = String(en.squad | 0); squad.title = 'Players expected (0: not said)';
  squad.setAttribute('aria-label', 'Players expected for ' + row.label);
  squad.addEventListener('change', () => { const n = Math.max(0, Math.min(8, Math.round(+squad.value || 0))); playlistSet(row.index, x => { x.squad = n; }, playlistName(en) + ': ' + (n ? 'for ' + plural(n, 'player', 'players') : 'squad size not said') + '.'); });
  const seed = plEl('select'); seed.setAttribute('aria-label', 'Dice for ' + row.label);
  for (const [v, t] of PL_SEEDS) seed.append(optionEl(t, v));
  seed.value = en.seed;
  seed.addEventListener('change', () => playlistSet(row.index, x => { x.seed = seed.value; if (seed.value !== 'repeat') x.record = ''; }, playlistName(en) + ': dice now ' + PL_SEEDS.find(s => s[0] === seed.value)[1].toLowerCase() + '.'));
  set.append(plEl('span', 'meta', row.squad), squad, seed);
  if (en.seed === 'repeat') {
    const runs = state.playlist.runs.filter(r => r.outcome === 'used' && playlistBelongs(en, r)), rec = plEl('select');
    rec.setAttribute('aria-label', 'Run to repeat for ' + row.label);
    rec.append(optionEl(runs.length ? 'Choose a run' : 'No run of it on record', ''));
    for (const r of runs) rec.append(optionEl(playlistRunWords(r), r.file));
    if (en.record && !runs.some(r => r.file === en.record)) rec.append(optionEl(en.record + ' (no longer on disk)', en.record));
    rec.value = en.record || '';
    rec.addEventListener('change', () => playlistSet(row.index, x => { x.record = rec.value; }, playlistName(en) + ': repeats ' + (rec.value || 'no run yet') + '.'));
    set.append(rec);
  }
  li.append(set);
  if (row.problem) {
    li.append(plEl('div', 'warn', (row.identity === 'missing' ? 'Missing: ' : 'Changed since it was added: ') + row.problem + '. It is not set up until you choose:'));
    const fix = plEl('div', 'row');
    if (row.choices.includes('current')) fix.append(plButton('Use it as it is now', '', 'Pin the file as it is now', () => playlistResolve(row.index, 'current')));
    fix.append(plButton('Use the picked mission', '', 'Put the mission picked in the list above in its place', () => playlistResolve(row.index, 'other')));
    fix.append(plButton('Remove', 'danger', 'Remove this entry from the playlist', () => playlistResolve(row.index, 'remove')));
    li.append(fix);
  }
  const acts = plEl('div', 'row');
  const go = plButton(row.armed ? 'Set up again' : 'Set up for the next mission', row.next && !row.armed ? 'primary' : '', 'The next mission you host uses this entry\'s scenario, options and dice', () => playlistArm(row.index));
  go.disabled = !row.canSetUp;
  const up = plButton('Up', '', 'Earlier in the playlist', () => playlistMove(row.index, -1)); up.disabled = row.index === 0;
  const down = plButton('Down', '', 'Later in the playlist', () => playlistMove(row.index, 1)); down.disabled = row.index === d.entries.length - 1;
  acts.append(go, up, down);
  if (!row.problem) acts.append(plButton('Remove', 'danger', 'Remove this entry from the playlist', () => playlistRemove(row.index)));
  li.append(acts);
  return li;
}
function syncPlaylist() {
  if (!viewIs('play')) return;
  const pl = state.playlist, d = pl.data;
  $('plLive').hidden = !live.on;
  if (!live.on) return;
  $('plCount').textContent = d ? String(d.entries.length) : '';
  const sel = $('plPick'), listKey = JSON.stringify([pl.lists, pl.file]);
  if (sel.dataset.key !== listKey && document.activeElement !== sel) {
    sel.dataset.key = listKey; sel.textContent = '';
    sel.append(optionEl(pl.lists.length ? 'None' : 'None yet: name one and choose New playlist', ''));
    for (const p of pl.lists) sel.append(optionEl((p.name || p.file) + (p.why ? ' (cannot be read)' : ' · ' + plural(p.entries | 0, 'mission', 'missions')), p.file));
    sel.value = pl.file;
  }
  $('plDelete').disabled = !pl.file;
  $('plOutcome').textContent = pl.trouble || playlistOutcome();
  $('plMarkRow').hidden = !playlistCanMark(playlistLast(), d);
  $('plNote').textContent = pl.note;
  const rows = playlistRows(), key = JSON.stringify([rows, d && d.entries, pl.runs.map(r => r.file)]);
  const f = document.activeElement, ul = $('plEntries');
  if (key === pl.key || (f && /^(INPUT|SELECT)$/.test(String(f.tagName || '')) && ul.contains(f))) return;
  pl.key = key; ul.textContent = '';
  rows.forEach(row => ul.appendChild(playlistEntryEl(row, d.entries[row.index])));
}
$('plAdd').addEventListener('click', () => playlistAdd());
$('plNew').addEventListener('click', () => playlistNew());
$('plDelete').addEventListener('click', () => playlistDelete());
$('plPick').addEventListener('change', ev => playlistSelect(ev.target.value));
$('plMarkWin').addEventListener('click', () => playlistMark('success'));
$('plMarkFail').addEventListener('click', () => playlistMark('failed'));
// Play's catalogue read brings the playlists along; Play's refresh draws them (20a-modes.js, 29f-options.js).
{ const refreshCatalogueBase = refreshCatalogue; refreshCatalogue = async function () { await refreshCatalogueBase(); await playlistRefresh(); }; }
{ const syncPlayBase = syncPlay; syncPlay = function () { syncPlayBase(); syncPlaylist(); }; }
expose({ playlistRefresh, playlistRows, playlistIdentity, playlistOutcome, playlistAdd, playlistArm, playlistResolve, playlistMark, playlistSet, playlistNew, syncPlaylist, playlistCanMark, playlistLast });
