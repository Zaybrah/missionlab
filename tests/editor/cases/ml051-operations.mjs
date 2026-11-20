// ML-051: branching operations in Play. The plugin's storage (BepInEx/MissionLab/operations, Operations.cs) is stood in for
// by a fake fetch that keeps progress the way the plugin does (the page sends the missions; the next mission, the runs
// counted and the choices stay the game's), so a second page on the same fake is a reload. Checked: the operation is built
// and shown with its branches and fallback; an end the game did not say shows a decision that is labelled as the host's and
// sends exactly that run and branch; a scenario changed since it was pinned is never set up until it is retargeted, and the
// progress made stays; retry and restart are sent as such.
const windowOf = api => api.setView.constructor('return this')();
const tick = () => new Promise(r => setTimeout(r, 0));
const reply = (status, body) => ({ status, ok: status === 200, json: async () => body, text: async () => JSON.stringify(body), arrayBuffer: async () => new ArrayBuffer(0) });

const scenario = (value, title, id, revision, hash, map, navFile) => ({
  file: value + '.json', value, readable: true, usable: true, why: '', findings: [], title, id, revision, sealed: false, briefing: [], author: { squad: [2, 4], minutes: 20 },
  measured: null, navFile, scene: '', map, mode: 'pve', compat: 'tested', enemies: 8, options: [], capabilities: [], needs: [], lastRun: null, hash,
});
const INTEL = scenario('intel', 'Intel raid', 'sc-intel', 2, 'fnv1a64:aaaa1111aaaa1111', 'Clean House', 'suburbv2.bytes');
const QUIET = scenario('quiet', 'Quiet approach', 'sc-quiet', 1, 'fnv1a64:bbbb1111bbbb1111', 'Depot Mexico', 'depotraid1.bytes');
const LOUD = scenario('loud', 'Loud assault', 'sc-loud', 1, 'fnv1a64:cccc1111cccc1111', 'Georgia', 'georgia1.bytes');
const R1 = '20261120-210000_Suburb.run.json', R2 = '20261120-220000_Depot.run.json';

// The plugin as the page sees it: operations and their progress survive any number of pages.
function plugin() {
  const store = { selected: '', files: new Map(), sent: [], catalogue: [INTEL, QUIET, LOUD], pending: '', retry: '' };
  const view = said => ({
    selected: store.selected, operations: [...store.files].map(([file, p]) => ({ file, name: p.name, nodes: p.nodes.length, why: '' })),
    operation: store.selected ? JSON.parse(JSON.stringify(store.files.get(store.selected))) : null, pending: store.pending, retry: store.retry,
    problems: [], runs: [], said: said || '', trouble: '', recovered: '',
  });
  const fetch = async (url, init) => {
    const body = init && typeof init.body === 'string' ? JSON.parse(init.body) : null;
    store.sent.push({ url, body, headers: (init && init.headers) || {} });
    if (url === '/api/catalogue') return reply(200, { scenarios: store.catalogue.slice(), skipped: 0 });
    if (url === '/api/operations') return reply(200, view());
    if (url === '/api/operations/save') {
      const old = body.file ? store.files.get(body.file) : null, file = body.file || body.operation.name.toLowerCase().replace(/\s+/g, '-') + '.json', p = body.operation;
      const progress = old ? { id: old.id, next: old.next, round: old.round, steps: old.steps, armed: old.armed } : { id: 'op-1', next: p.start, round: 1, steps: [], armed: false };
      store.files.set(file, Object.assign({}, p, progress));
      if (!old) store.selected = file;
      return reply(200, view());
    }
    if (url === '/api/operations/arm') { const p = store.files.get(body.file); p.armed = true; return reply(200, view('Next mission of ' + p.name + '.')); }
    if (url === '/api/operations/decide') {
      const p = store.files.get(store.selected), s = p.steps.find(x => x.record === body.record), n = p.nodes.find(x => x.id === s.node);
      s.manual = true; s.advanced = true; s.branch = body.branch; s.to = body.branch >= 0 ? n.branches[body.branch].to : n.fallback;
      p.next = s.to; p.armed = false; store.pending = ''; store.retry = '';
      return reply(200, view('Decided by the host.'));
    }
    if (url === '/api/operations/restart') {
      const p = store.files.get(store.selected); p.round = (p.round || 1) + 1; p.next = p.start; p.armed = false; store.pending = ''; store.retry = '';
      return reply(200, view('Round ' + p.round + ' begins.'));
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
  await api.operationRefresh();
  return api;
}

export default async function (h) {
  {   // Offline there is no plugin to keep an operation: the section says so and lists nothing.
    const api = h.openEditor(h.script).api;
    api.setView('play'); api.syncOperation();
    h.check('ML-051 offline the operation needs the game and lists nothing', api.byId('opLive').hidden === true && api.operationRows().length === 0);
  }

  const game = plugin();
  const a = await linked(h, game);
  a.byId('opName').value = 'Phone job';
  a.catalogue.pick = 'intel.json'; await a.operationNew();
  for (const f of ['quiet.json', 'loud.json']) { a.catalogue.pick = f; await a.operationAdd(); }
  await a.operationSetNode('intel', x => { x.fallback = 'loud'; }, 'fallback set');
  await a.operationAddBranch('intel');
  await a.operationSetBranch('intel', 0, x => { delete x.result; x.goal = 'phone'; x.state = 'succeeded'; }, 'branch set');
  const kept = game.store.files.get('phone-job.json');
  const intel = kept && kept.nodes[0];
  h.check('ML-051 the operation is saved by the game with its missions in order, starting with the first', !!kept && kept.nodes.map(n => n.id).join() === 'intel,quiet,loud' && kept.start === 'intel' && kept.next === 'intel', JSON.stringify(kept));
  h.check('ML-051 each mission pins its scenario as a playlist entry does', !!intel && intel.setup.file === 'intel' && intel.setup.id === 'sc-intel' && intel.setup.revision === 2 && intel.setup.hash === INTEL.hash, JSON.stringify(intel));
  h.check('ML-051 the branch on an objective and the fallback are saved as set', !!intel && JSON.stringify(intel.branches) === '[{"to":"quiet","goal":"phone","state":"succeeded"}]' && intel.fallback === 'loud', JSON.stringify(intel));
  const saves = game.store.sent.filter(s => s.url === '/api/operations/save');
  h.check('ML-051 the page never sends progress, only the missions', saves.length > 0 && saves.every(s => !('steps' in s.body.operation) && !('next' in s.body.operation) && !('armed' in s.body.operation)), JSON.stringify(saves.map(s => Object.keys(s.body.operation))));

  const rows = a.operationRows();
  h.check('ML-051 Play shows each mission with its pin, branches and fallback', rows.length === 3 && rows[0].pinned === 'intel.json · id sc-intel · revision 2 · fingerprint aaaa1111'
    && rows[0].branches.join() === 'If objective phone succeeded: Quiet approach' && rows[0].fallback === 'Otherwise: Loud assault' && rows[0].start && rows[0].next && rows[1].fallback === 'Otherwise: the end of the operation', JSON.stringify(rows[0]));
  const armed = await a.operationArm(false);
  const arms = () => game.store.sent.filter(s => s.url === '/api/operations/arm');
  h.check('ML-051 setting the next mission up asks the game for exactly that mission', armed && arms().length === 1 && arms()[0].body.file === 'phone-job.json' && arms()[0].body.node === 'intel' && arms()[0].body.retry === false && arms()[0].headers['X-MissionLab'] === '1', JSON.stringify(arms()));

  // The intel raid ended without the game saying how, and without objective states.
  const op = game.store.files.get('phone-job.json');
  op.steps.push({ record: R1, node: 'intel', round: 1, result: 'unknown' });
  game.store.pending = R1; game.store.retry = R1;
  const b = await linked(h, game);       // a reload
  const told = String(b.byId('opOutcome').textContent), pending = b.operationPending();
  h.check('ML-051 after a reload an unknown end shows and the operation stays on the mission', told.startsWith('Last run, Intel raid: unknown: the game did not say how it ended.') && told.includes('Next: Intel raid, set up in the game.'), told);
  h.check('ML-051 the decision waiting is shown and labelled as the host\'s', b.byId('opDecide').hidden === false && !!pending && pending.text.includes('recorded as decided by the host, not by the game')
    && pending.choices.map(c => c.words).join(' | ') === 'Branch 1 (objective phone succeeded): Quiet approach | Fallback: Loud assault | It does not count: play it again', JSON.stringify(pending));
  await b.operationDecide(0);
  const decided = game.store.sent.filter(s => s.url === '/api/operations/decide');
  h.check('ML-051 the decision is sent for that run and that branch', decided.length === 1 && decided[0].body.record === R1 && decided[0].body.branch === 0 && !('again' in decided[0].body), JSON.stringify(decided));
  const progress = b.operationProgress();
  h.check('ML-051 progress says the host decided, and the operation moves on', progress.length === 1 && progress[0] === 'Intel raid: unknown: the game did not say how it ended; the way on was decided by the host. Branch 1 (objective phone succeeded), on to Quiet approach.'
    && String(b.byId('opOutcome').textContent).includes('Next: Quiet approach.') && b.byId('opDecide').hidden === true, progress.join(' | '));

  // The quiet approach's file is saved again as revision 2.
  game.store.catalogue[1] = Object.assign({}, QUIET, { revision: 2, hash: 'fnv1a64:bbbb2222bbbb2222' });
  b.setView('play'); await tick(); await tick(); await b.operationRefresh();
  const changed = b.operationRows()[1];
  h.check('ML-051 a changed next mission offers retarget or restart and cannot be set up', changed.identity === 'changed' && changed.problem.includes('revision 1 when it was added, revision 2 now')
    && changed.choices.join() === 'current,other,restart' && !changed.canSetUp && b.byId('opArm').disabled === true, JSON.stringify(changed));
  const tried = arms().length, set = await b.operationArm(false);
  h.check('ML-051 and it is not set up silently', set === false && arms().length === tried && String(b.state.operation.note).includes('was not set up') && String(b.state.operation.note).includes('progress is kept'), b.state.operation.note);
  await b.operationResolve('quiet', 'current');
  const after = game.store.files.get('phone-job.json');
  h.check('ML-051 Use it as it is now pins revision 2 and keeps the progress and the branches', after.nodes[1].setup.revision === 2 && after.nodes[1].setup.hash === 'fnv1a64:bbbb2222bbbb2222' && after.nodes[1].setup.label === 'Quiet approach'
    && after.steps.length === 1 && after.steps[0].advanced && after.next === 'quiet' && after.nodes[0].branches.length === 1 && b.operationRows()[1].identity === 'same' && b.operationRows()[1].canSetUp, JSON.stringify(after));

  // The quiet approach was cut off: its end was never seen.
  after.steps.push({ record: R2, node: 'quiet', round: 1, result: 'incomplete' });
  game.store.pending = R2; game.store.retry = R2;
  await b.operationRefresh();
  h.check('ML-051 an interrupted mission shows as incomplete with a retry of its setup', String(b.byId('opOutcome').textContent).includes('Last run, Quiet approach: incomplete: its end was not seen.') && b.byId('opRetry').hidden === false, String(b.byId('opOutcome').textContent));
  await b.operationArm(true);
  h.check('ML-051 retry asks the game to replay that run\'s setup for the same mission', arms().length === tried + 1 && arms()[tried].body.retry === true && arms()[tried].body.node === 'quiet', JSON.stringify(arms()));
  await b.operationRestart();
  const restarted = game.store.files.get('phone-job.json');
  h.check('ML-051 restart begins a new round at the start and keeps the runs on record', restarted.round === 2 && restarted.next === 'intel' && restarted.steps.length === 2 && String(b.byId('opOutcome').textContent).startsWith('Round 2. Next: Intel raid'), String(b.byId('opOutcome').textContent));
}
