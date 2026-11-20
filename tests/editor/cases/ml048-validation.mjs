// ML-048: the validation run on the page. Offline only the format and the page's own mission check run; completion, the
// saved rehearsals and the map-dependent scatter seeds are listed as not run, the scatter naming the map file it needs. A
// kept report is stale once the scenario text differs from the one it was made for. A counterexample selects the triggers
// it names (the first in the inspector, with the enemies it brings) and marks them, and only them, in the mission graph.
export default async function (h) {
  const { script, load, check } = h;
  const text = JSON.stringify;
  const scenario = {
    schema: 'missionlab-plan/1', id: 'sc-validate', revision: 2, navFile: 'synthetic.bytes', extraction: { after: 1 },
    triggers: [
      { id: 1, name: 'Timer', when: 'time', delay: 30 },
      { id: 2, name: 'Officer down', when: 'down', tag: 'officer', count: 1, extraction: 'open' },
      { id: 3, name: 'Later', when: 'time', delay: 90 },
    ],
    options: [{ id: 'pace', name: 'Pace', choices: [{ name: 'Slow' }, { name: 'Fast', drop: { triggers: [1] } }] }],
    spawns: [
      { pos: [10, 0, 10], role: 'Guard' },
      { pos: [20, 0, 20], role: 'Guard', tag: 'officer', minPlayers: 2 },
      { pos: [50, 0, 50], role: 'Guard', scatter: 5 },
      { pos: [30, 0, 30], role: 'Guard', trigger: 2 },
    ],
  };
  const { api } = await load(script, 'validate.json', text(scenario));
  const by = (m, name) => (m && m.checks.find(c => c.name === name)) || {};

  {   // Offline: what the page has runs, the rest says it did not.
    const m = await api.validateRun();
    check('offline a report is made', !!m && m.source === 'offline', text(m));
    check('offline the strict format and the page\'s mission check run', by(m, 'format').state === 'passed' && by(m, 'format').executed === 1 && by(m, 'check').executed === 1, text(m && m.checks));
    check('offline the scatter seeds are not run, and say which map file they need', by(m, 'scatter').state === 'not-run' && by(m, 'scatter').executed === 0 && by(m, 'scatter').why.includes('synthetic.bytes'), text(by(m, 'scatter')));
    check('offline completion across options and seeds is not run', by(m, 'completion').state === 'not-run' && by(m, 'completion').executed === 0, text(by(m, 'completion')));
    check('offline the saved rehearsals are not run', by(m, 'rehearsals').state === 'not-run' && by(m, 'rehearsals').executed === 0, text(by(m, 'rehearsals')));
    check('a report with checks not run is never a pass', m && m.verdict !== 'passed', m && m.verdict);
    check('the report just made is for the text on the page, so it is not stale', m && m.stale === '', m && m.stale);
  }

  const fresh = api.validateOffline();
  const stored = {
    schema: 'missionlab-validation/1', evaluator: 1, scenarioHash: fresh.scenarioHash, scenarioId: 'sc-validate', revision: 2, navFile: '', mapPrint: '',
    players: [1, 2, 4], seeds: [5, 6, 7], options: ['pace: choices 0, 1 of 2 (all)'], verdict: 'failed', covers: 'x', limits: 'y',
    checks: [{ name: 'format', state: 'passed', coverage: 'exhaustive', planned: 1, executed: 1, failed: 0, why: '', notes: [] },
      { name: 'completion', state: 'failed', coverage: 'sampled', planned: 18, executed: 18, failed: 3, why: '3 of 18 combinations fail', notes: [] }],
    first: { check: 'completion', seed: 5, players: 1, picks: { pace: 1 }, options: "pace=1 'Fast'", expected: 'the extraction can open, so the squad can leave',
      found: 'triggers[0]: nobody could leave', path: 'triggers[0]', rehearsal: '', resolved: '', triggers: [2, 1, 99], spawns: [], trace: [] },
  };

  {   // A kept report speaks for the text it was made for, and is stale for any other.
    let m = api.validateTake(stored, 'stored', '2026-01-01T00:00:00Z');
    check('a kept report for this very text is not stale', m.stale === '', m.stale);
    m = api.validateTake(Object.assign({}, stored, { scenarioHash: 'fnv1a64:0000000000000000' }), 'stored', '2026-01-01T00:00:00Z');
    check('a kept report for different text is shown as stale', m.stale !== '', text(m));
    api.validateTake(stored, 'stored', '');
    api.state.zones[0].delay = 45;
    check('changing the scenario after the report makes it stale', api.validateModel().stale !== '', api.validateModel().stale);
    api.state.zones[0].delay = 30;
    check('and putting it back makes it current again', api.validateModel().stale === '', api.validateModel().stale);
    m = api.validateTake(Object.assign({}, stored, { evaluator: 99 }), 'stored', '');
    check('a report made by other checks is stale', m.stale !== '', m.stale);
  }

  {   // The counterexample selects its triggers and marks them in the graph; a trigger no longer there is left out.
    api.validateTake(stored, 'stored', '');
    check('the counterexample is shown with what plays it again', (api.validateModel().counterWords || []).join(' ').includes('Seed 5'), text(api.validateModel().counterWords));
    const shown = api.validateShowCounter();
    check('showing the counterexample selects the first trigger it names', shown === true && api.state.inspectSel === 2, String(api.state.inspectSel));
    check('and the enemy that trigger brings', api.state.selected === 3, String(api.state.selected));
    check('a trigger no longer in the scenario is not marked', text(api.state.graph.mark.ids) === text([2, 1]), text(api.state.graph.mark));
    const model = api.graphModel(), node = id => model.nodes.find(n => n.key === 't:' + id);
    const marked = id => api.graphNodeClass(node(id)).split(' ').includes('g-mark') && node(id).rows.some(r => r.cls.split(' ').includes('g-markrow'));
    check('both triggers it names are marked in the mission graph, by class and in words', marked(1) && marked(2), text([1, 2].map(id => api.graphNodeClass(node(id)))));
    check('a trigger it does not name is not marked', !marked(3) && !api.graphNodeClass(node(3)).includes('g-mark'), api.graphNodeClass(node(3)));
    api.validateTake(fresh, 'offline', '');
    const after = api.graphModel().nodes.find(n => n.key === 't:2');
    check('a new report lets the marks go', !api.graphNodeClass(after).includes('g-mark') && api.state.graph.mark.ids.length === 0, api.graphNodeClass(after));
  }
}
