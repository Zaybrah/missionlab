// ML-049: the session playlist in Play. The plugin's storage (BepInEx/MissionLab/playlists, Playlists.cs) is stood in for
// by a fake fetch that keeps the playlist the way the plugin does, so a second page opened on the same fake is a reload:
// it must show the same agenda and the same next entry. Checked: each entry lists its pinned identity and which map and
// mode to host; a changed or missing scenario offers the host's three choices and is never set up silently; an end the
// game did not see stays unknown until the host marks it, and that mark is labelled as the host's.
const windowOf = api => api.setView.constructor('return this')();
const tick = () => new Promise(r => setTimeout(r, 0));
const reply = (status, body) => ({ status, ok: status === 200, json: async () => body, text: async () => JSON.stringify(body), arrayBuffer: async () => new ArrayBuffer(0) });

const scenario = (value, title, id, revision, hash, map, navFile, extra) => Object.assign({
  file: value + '.json', value, readable: true, usable: true, why: '', findings: [], title, id, revision, sealed: false, briefing: [], author: { squad: [2, 4], minutes: 20 },
  measured: null, navFile, scene: '', map, mode: 'pve', compat: 'tested', enemies: 8, options: [], capabilities: [], needs: [], lastRun: null, hash,
}, extra || {});
const WARM = scenario('warmup', 'Warm-up', 'sc-warm', 1, 'fnv1a64:aaaa1111aaaa1111', 'Clean House', 'suburbv2.bytes');
const RAID = scenario('raid', 'Quiet raid', 'sc-raid', 4, 'fnv1a64:bbbb4444bbbb4444', 'Depot Mexico', 'depotraid1.bytes',
  { options: [{ id: 'garrison', name: 'Garrison size', pick: 1, choices: ['Light', 'Full'] }] });
const DEFENCE = scenario('defence', 'Defence', 'sc-def', 2, 'fnv1a64:cccc2222cccc2222', 'Georgia', 'georgia1.bytes');

// The plugin as the page sees it: the playlists, the one in use and its next entry survive any number of pages.
function plugin() {
  const store = { selected: '', files: new Map(), sent: [], catalogue: [WARM, RAID, DEFENCE] };
  const view = said => ({
    selected: store.selected, playlists: [...store.files].map(([file, p]) => ({ file, name: p.name, entries: p.entries.length, why: '' })),
    playlist: store.selected ? JSON.parse(JSON.stringify(store.files.get(store.selected))) : null, runs: [], said: said || '', trouble: '',
  });
  const fetch = async (url, init) => {
    const body = init && typeof init.body === 'string' ? JSON.parse(init.body) : null;
    store.sent.push({ url, body, headers: (init && init.headers) || {} });
    if (url === '/api/catalogue') return reply(200, { scenarios: store.catalogue.slice(), skipped: 0 });
    if (url === '/api/playlists') return reply(200, view());
    if (url === '/api/playlists/save') {
      const old = body.file ? store.files.get(body.file) : null, file = body.file || body.playlist.name.toLowerCase() + '.json', p = body.playlist;
      const keep = !!(old && old.armed && old.next === p.next && JSON.stringify(old.entries[old.next]) === JSON.stringify(p.entries[p.next]));
      store.files.set(file, Object.assign({}, p, { armed: keep, results: old ? old.results : [] }));
      if (!old) store.selected = file;
      return reply(200, view(old && old.armed && !keep ? 'Saved. The next entry changed, so set it up again for the next mission.' : ''));
    }
    if (url === '/api/playlists/arm') {
      const p = store.files.get(body.file); p.next = body.next; p.armed = true; store.selected = body.file;
      return reply(200, view('Next mission: ' + p.entries[body.next].label + '.'));
    }
    if (url === '/api/playlists/mark') {
      const p = store.files.get(store.selected), r = p.results.find(x => x.record === body.record);
      r.result = body.result; r.manual = true; r.advanced = true; p.next++; p.armed = false;
      return reply(200, view(r.label + ': marked.'));
    }
    return reply(404, null);
  };
  return { store, fetch };
}

async function linked(h, game) {
  const api = h.openEditor(h.script).api;
  windowOf(api).fetch = game.fetch;
  api.live.on = true;
  api.setView('play');
  await tick(); await tick();
  await api.playlistRefresh();
  return api;
}

export default async function (h) {
  {   // Offline there is no plugin to keep a playlist: the section says so and lists nothing.
    const api = h.openEditor(h.script).api;
    api.setView('play'); api.syncPlaylist();
    h.check('ML-049 offline the playlist needs the game and lists nothing', api.byId('plLive').hidden === true && api.playlistRows().length === 0);
  }

  const game = plugin();
  const a = await linked(h, game);
  h.check('ML-049 linked, Play has read the catalogue', Array.isArray(a.catalogue.list) && a.catalogue.list.length === 3, JSON.stringify(a.catalogue.list && a.catalogue.list.map(e => e.file)));
  a.byId('plName').value = 'Friday';
  for (const f of ['warmup.json', 'raid.json', 'defence.json']) { a.catalogue.pick = f; await a.playlistAdd(); }
  const kept = game.store.files.get('friday.json');
  h.check('ML-049 the three missions are saved by the game in order', !!kept && kept.entries.map(e => e.file).join() === 'warmup,raid,defence' && game.store.selected === 'friday.json', JSON.stringify(kept));
  const raid = kept && kept.entries[1];
  h.check('ML-049 an entry pins file, id, revision, fingerprint, options and squad as they were', !!raid && raid.id === 'sc-raid' && raid.revision === 4 && raid.hash === RAID.hash && raid.picks.garrison === 1 && raid.squad === 4 && raid.seed === 'new', JSON.stringify(raid));

  const rows = a.playlistRows();
  h.check('ML-049 Play lists each entry with its pinned identity', rows.length === 3 && rows[1].pinned === 'raid.json · id sc-raid · revision 4 · fingerprint bbbb4444', rows.map(r => r.pinned).join(' | '));
  h.check('ML-049 and tells the host which map and mode to host', rows[0].instruction === 'In the game, host Clean House (suburbv2.bytes) as a PvE raid. MissionLab does not switch the map for you.'
    && rows[1].instruction.includes('Depot Mexico (depotraid1.bytes)') && rows[2].instruction.includes('Georgia (georgia1.bytes)'), rows.map(r => r.instruction).join(' | '));
  h.check('ML-049 with the options and squad it was added with', rows[1].options === 'Garrison size: Full' && rows[1].squad === 'for 4 players' && rows[1].seed === 'a new roll', JSON.stringify(rows[1]));

  const armedOk = await a.playlistArm(1);
  const arms = () => game.store.sent.filter(s => s.url === '/api/playlists/arm');
  h.check('ML-049 setting the second entry up asks the game for exactly that entry', armedOk && arms().length === 1 && arms()[0].body.file === 'friday.json' && arms()[0].body.next === 1 && arms()[0].headers['X-MissionLab'] === '1', JSON.stringify(arms()));

  // A reload: a new page on the same game.
  const b = await linked(h, game);
  const again = b.playlistRows();
  h.check('ML-049 after a reload the agenda and the next entry are the same', again.length === 3 && again[1].next && again[1].armed && !again[0].next && b.state.playlist.file === 'friday.json', JSON.stringify(again.map(r => [r.label, r.next, r.armed])));
  h.check('ML-049 and the section names the next entry and its map', String(b.byId('plOutcome').textContent).startsWith('Next: Quiet raid, set up in the game.') && String(b.byId('plOutcome').textContent).includes('Depot Mexico'), String(b.byId('plOutcome').textContent));

  // The raid's file is saved again as revision 5.
  game.store.catalogue[1] = Object.assign({}, RAID, { revision: 5, hash: 'fnv1a64:bbbb5555bbbb5555' });
  b.setView('play'); await tick(); await tick(); await b.playlistRefresh();
  const changed = b.playlistRows()[1];
  h.check('ML-049 a changed revision shows the three choices', changed.identity === 'changed' && changed.problem.includes('revision 4 when it was added, revision 5 now') && changed.choices.join() === 'current,other,remove' && !changed.canSetUp, JSON.stringify(changed));
  const tried = arms().length, set = await b.playlistArm(1);
  h.check('ML-049 and it is not set up silently', set === false && arms().length === tried && String(b.state.playlist.note).includes('was not set up') && game.store.files.get('friday.json').entries[1].revision === 4, b.state.playlist.note);
  await b.playlistResolve(1, 'current');
  const repinned = game.store.files.get('friday.json').entries[1];
  h.check('ML-049 Use it as it is now pins revision 5 and keeps the host\'s settings', repinned.revision === 5 && repinned.hash === 'fnv1a64:bbbb5555bbbb5555' && repinned.label === 'Quiet raid' && repinned.squad === 4 && repinned.picks.garrison === 1 && b.playlistRows()[1].identity === 'same', JSON.stringify(repinned));
  h.check('ML-049 a re-pinned next entry has to be set up again', game.store.files.get('friday.json').armed === false && String(b.byId('plOutcome').textContent).includes('Choose Set up for the next mission'), String(b.byId('plOutcome').textContent));

  // The defence file is removed from the scenarios folder.
  game.store.catalogue.splice(2, 1);
  b.setView('play'); await tick(); await tick(); await b.playlistRefresh();
  const gone = b.playlistRows()[2];
  h.check('ML-049 a missing scenario offers another mission or removal, not "as it is now"', gone.identity === 'missing' && gone.choices.join() === 'other,remove' && !gone.canSetUp, JSON.stringify(gone));
  b.catalogue.pick = 'warmup.json';
  await b.playlistResolve(2, 'other');
  h.check('ML-049 Use the picked mission puts that mission in its place', game.store.files.get('friday.json').entries[2].file === 'warmup' && b.playlistRows()[2].identity === 'same', JSON.stringify(game.store.files.get('friday.json').entries[2]));

  // The raid was played, and the game did not see how it ended.
  const pl = game.store.files.get('friday.json');
  pl.next = 1; pl.armed = true; pl.results = [{ record: '20261120-210000_Depot.run.json', entry: 1, label: 'Quiet raid', result: 'unknown' }];
  await b.playlistRefresh();
  const told = String(b.byId('plOutcome').textContent);
  h.check('ML-049 an unknown end stays unknown and the entry stays next', told.startsWith('Last run, Quiet raid: unknown: the game did not say how it ended.') && told.includes('Next: Quiet raid') && b.byId('plMarkRow').hidden === false, told);
  await b.playlistMark('success');
  const mark = game.store.sent.filter(s => s.url === '/api/playlists/mark');
  const after = String(b.byId('plOutcome').textContent);
  h.check('ML-049 the host\'s mark is sent for that run record', mark.length === 1 && mark[0].body.record === '20261120-210000_Depot.run.json' && mark[0].body.result === 'success', JSON.stringify(mark));
  h.check('ML-049 and is labelled as the host\'s, with the next entry after it', after.startsWith('Last run, Quiet raid: a success (marked by the host).') && after.includes('Next: Warm-up') && b.byId('plMarkRow').hidden === true, after);
  pl.results.push({ record: '20261120-220000_Suburb.run.json', entry: 2, label: 'Warm-up', result: 'success', advanced: true }); pl.next = 3;
  await b.playlistRefresh();
  h.check('ML-049 an end the game recorded is not labelled as the host\'s', String(b.byId('plOutcome').textContent) === 'Last run, Warm-up: a success, as the game recorded it. The playlist is done.', String(b.byId('plOutcome').textContent));
}
