// ML-053: a coding agent's proposal, as the plugin keeps it, opens as a separate draft whose differences from the scenario it
// was made for name the changed trigger, the new hold and the new objective. Apply puts it on the author's own draft as one
// undo step that Undo takes back exactly, and the result passes the page's own check for the normal save. A stale proposal,
// or one made for the scenario before the author's own later edit, is refused and changes nothing.
export default async function (h) {
  const { script, load, check } = h;
  const text = JSON.stringify;
  const UPSTAIRS = { parts: [{ floor: [3, 6], outline: [[-10, -10], [10, -10], [10, 10], [-10, 10]] }] };
  const file = text({ schema: 'missionlab-plan/3', mission: { name: 'House', id: 'house-1', revision: 4,
    triggers: [{ id: 1, name: 'Phone', when: 'intel', item: 'Cell Phone' }, { id: 2, name: 'Front door', when: 'zone', pos: [20, 0, 0], radius: 4 }],
    places: [{ id: 1, name: 'Upstairs', pos: [0, 4, 0], area: UPSTAIRS }],
    goals: [{ id: 'phone', name: 'Recover the phone', succeed: 1 }],
    spawns: [{ pos: [5, 4, 5] }, { pos: [-5, 0, -5], trigger: 2 }] } });
  const page = await load(script, 'house.json', file), api = page.api, s = api.state;
  check('the house loads', !s.refused, page.banner());
  const baseText = api.scenarioText();

  // What the plugin keeps for "make the upstairs a 30-second hold once the phone is recovered": the front door becomes a
  // hold, a new hold linked to Upstairs, and an objective after the phone that the new hold completes.
  const after = JSON.parse(baseText), m = after.mission;
  m.triggers = m.triggers.map(t => (t.id === 2 ? Object.assign({}, t, { when: 'hold', hold: 30 }) : t));
  m.triggers.push({ id: 3, name: 'Hold upstairs', when: 'hold', pos: [0, 4, 0], radius: 10, place: 1, hold: 30, everyone: true });
  m.goals.push({ id: 'upstairs', name: 'Hold the upstairs', after: ['phone'], succeed: 3 });
  const proposal = { schema: 'missionlab-proposal/1', id: 'p20261120-101500-1', name: 'Upstairs hold', file: 'house.json', scenarioId: 'house-1', made: '2026-11-20T10:15:00Z',
    base: { hash: api.textHash(baseText), revision: 4, text: baseText }, operations: [], changes: ['trigger 2 is now a 30 s hold', 'added trigger 3 (hold)', 'added goal "upstairs"'],
    capabilities: [], findings: [], hash: '', scenario: text(after) };
  check('the page\'s own check accepts the proposed text', api.scenarioProblems(proposal.scenario).filter(f => f.level === 'error').length === 0, text(api.scenarioProblems(proposal.scenario)));

  {   // Review: the differences in words, then a separate draft.
    const lines = api.proposalDiff(proposal), said = lines.map(l => l.text).join(' / ');
    check('the diff names the changed trigger and what it now waits for', lines.some(l => l.ref === 'trigger:2' && l.text.includes('holding')), said);
    check('the diff names the new hold', lines.some(l => l.ref === 'trigger:3' && l.text.includes('"Hold upstairs"') && l.text.includes('added')), said);
    check('the diff names the new objective, what completes it and what it waits for', lines.some(l => l.kind === 'goal' && l.text.includes('"Hold the upstairs"') && l.text.includes('added') && l.text.includes('trigger 3') && l.text.includes('phone')), said);
    check('and nothing that did not change', !lines.some(l => l.ref === 'goal:phone' || l.ref === 'trigger:1' || l.kind === 'enemy' || l.kind === 'place'), said);
    check('an unchanged proposal has no differences', api.proposalDiff(Object.assign({}, proposal, { scenario: baseText })).length === 0);
    check('it opens as a separate draft', api.proposalOpen(proposal) === true && !!api.stage.separate && api.stage.separate.key === 'proposal:' + proposal.id, page.banner());
    check('the draft shows the proposal', s.zones.length === 3 && s.goals.length === 2, s.zones.length + ' triggers, ' + s.goals.length + ' objectives');
  }

  {   // Apply: on the author's own draft, as one step; Undo gives back exactly the scenario it was made for.
    check('Apply succeeds', api.proposalApply(proposal) === true, page.banner());
    check('it went on the author\'s own draft, not the separate one', api.stage.separate === null);
    const done = api.historyList().done;
    check('Apply is one history entry', done.length === 1 && done[0] === 'Apply proposal "Upstairs hold"', text(api.historyList()));
    const now = JSON.parse(api.scenarioText()).mission;
    check('the page has the hold linked to Upstairs and the objective after the phone',
      now.triggers.some(t => t.id === 3 && t.when === 'hold' && t.place === 1 && t.hold === 30 && t.everyone === true) && now.goals.some(g => g.id === 'upstairs' && text(g.after) === text(['phone']) && g.succeed === 3), text(now.triggers) + ' ' + text(now.goals));
    check('the front door is a hold now', now.triggers.find(t => t.id === 2).when === 'hold');
    check('the result passes the page\'s own check, ready for the normal save', api.scenarioProblems(api.scenarioText()).filter(f => f.level === 'error').length === 0);
    check('the scenario keeps its name and identity for saving', s.scenarioId === 'house-1' && s.title === 'House', s.scenarioId + ' ' + s.title);
    api.undo();
    check('one Undo gives back the scenario exactly', api.scenarioText() === baseText, h.differences(JSON.parse(baseText), JSON.parse(api.scenarioText()), '', []).join(' / '));
    api.redo();
    check('Redo puts the proposal back', JSON.parse(api.scenarioText()).mission.triggers.length === 3);
    api.undo();
  }

  {   // A stale proposal is never applied: nothing changes and no step is recorded.
    const before = api.scenarioText(), n = api.historyList().done.length;
    const stale = Object.assign({}, proposal, { stale: true, staleWhy: 'the scenario has changed since this was proposed (made for revision 4, the file is now revision 5)' });
    check('a stale proposal is refused', api.proposalApply(stale) === false && page.banner().startsWith('Not applied'), page.banner());
    check('and leaves the page and the history as they were', api.scenarioText() === before && api.historyList().done.length === n);
    check('the game\'s own stale word is the one taken', api.proposalTake({ summary: { stale: true, staleWhy: 'x' }, proposal }).stale === true && api.proposalTake({ summary: { stale: false }, proposal: Object.assign({}, proposal, { stale: true }) }).stale === false);
  }

  {   // Made for the scenario as it was before the author's own later edit: refused, and the edit stays.
    api.willChange('Add enemy'); s.enemies.push(api.newEnemy([30, 0, 30], 'Guard', 0)); api.changed();
    const edited = api.scenarioText(), n = api.historyList().done.length;
    check('a proposal for the scenario before an edit is refused', api.proposalApply(proposal) === false && page.banner().includes('does not hold the scenario'), page.banner());
    check('the edit stays and no step is added', api.scenarioText() === edited && api.historyList().done.length === n && s.enemies.length === 3);
  }

  {   // Only a proposal is taken for one.
    check('a scenario file is not taken for a proposal', api.proposalTake(JSON.parse(baseText)) === null && api.proposalOpen(JSON.parse(baseText)) === false);
    check('nor a proposal whose texts are missing', api.proposalTake(Object.assign({}, proposal, { base: { hash: '', revision: 4 } })) === null);
  }
}
