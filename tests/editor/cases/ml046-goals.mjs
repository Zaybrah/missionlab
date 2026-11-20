// ML-046: objectives. A plan with goals (or an extraction that waits for them) is written as missionlab-plan/3 and reads
// back byte for byte; without them nothing changes. Every refusal of the shared contract comes with its exact code and
// path. The mission check blocks a circle among objectives and a required objective that can never be completed when the
// extraction waits for them, and names the required ones. Each edit is one undo step. A sealed, unrevealed Play view and
// Direct board never show an objective the game has not given yet, and the board copes with a game that sends no goals.
import fs from 'node:fs';
import path from 'node:path';

export default async function (h) {
  const { script, openEditor, load, check } = h;
  const text = JSON.stringify;
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  // Zone 1 (the phone room) and zone 2 (the upper floor) fire when players arrive; 3 fires 60 s in; 4 waits for the Game
  // Master; 5 when the enemy is alerted.
  const fresh = () => {
    const page = openEditor(script), api = page.api, s = api.state;
    s.map = api.makeMap(new Float32Array(plate(-60, -60, 60, 60, 0)), 'objectives test');
    Object.assign(s, { example: false, title: 'Phone job', infils: [], authored: [], objectives: [], things: [], doors: [], extractionAfter: 0, selected: -1, squad: [] });
    s.enemies = [api.newEnemy([-40, 0, -40], 'Guard', 0)];
    s.zones = [api.newTrigger(1, 'zone', { pos: [0, 0, 0], radius: 6 }), api.newTrigger(2, 'zone', { pos: [30, 0, 30], radius: 6 }),
      api.newTrigger(3, 'time', { delay: 60 }), api.newTrigger(4, 'after', { after: 0 }), api.newTrigger(5, 'alert')];
    api.changed();
    return { page, api, s };
  };
  const goal = (id, name, more) => Object.assign({ id, name, required: true, after: [], activate: 0, succeed: 1, fail: 0, notify: true }, more || {});
  const phoneJob = () => [goal('phone', 'Recover the phone', { fail: 5 }), goal('upper', 'Secure the upper floor', { after: ['phone'], succeed: 2 }),
    goal('power', 'Cut the power', { required: false, activate: 3, succeed: 3, notify: false })];

  {   // Written as plan/3 in the agreed key order, read back byte for byte; without goals nothing changes.
    const { api, s } = fresh();
    const plainText = api.scenarioText(), plain = JSON.parse(plainText);
    check('without objectives the file stays missionlab-plan/1 with no goals key', plain.schema === 'missionlab-plan/1' && plain.goals === undefined && plain.extraction === undefined, plain.schema);
    s.goals = phoneJob(); s.extractionGoals = true; api.changed();
    const saved = api.scenarioText(), obj = JSON.parse(saved), m = obj.mission || {};
    check('a plan with objectives is written as missionlab-plan/3', obj.schema === 'missionlab-plan/3' && !!obj.mission, obj.schema);
    check('each goal is written in the contract\'s key order with defaults left out', text(m.goals) === text([
      { id: 'phone', name: 'Recover the phone', succeed: 1, fail: 5 },
      { id: 'upper', name: 'Secure the upper floor', after: ['phone'], succeed: 2 },
      { id: 'power', name: 'Cut the power', required: false, activate: 3, succeed: 3, notify: false }]), text(m.goals));
    const keys = Object.keys(m);
    check('the extraction says it waits for the objectives, and "goals" comes just before it', text(m.extraction) === text({ goals: true }) && keys.indexOf('goals') === keys.indexOf('extraction') - 1, keys.join());
    const errors = api.scenarioProblems(saved).filter(f => f.level === 'error');
    check('the file rules accept what the page writes', errors.length === 0, text(errors));
    const caps = api.capabilitiesOf(obj);
    check('the plan names the "goals" capability, and the gated extraction counts as an extraction hold', caps.includes('goals') && caps.includes('extraction-hold'), text(caps));
    const plainCaps = api.capabilitiesOf(plain);
    check('a plan without them names neither', !plainCaps.includes('goals') && !plainCaps.includes('extraction-hold'), text(plainCaps));
    const back = await load(script, 'phone_job.json', saved);
    check('it loads without a finding', !back.api.state.refused, back.banner());
    check('and writes back byte for byte', back.api.scenarioText() === saved, h.differences(obj, JSON.parse(back.api.scenarioText()), '', []).join(' / '));
    check('the page holds the goals as read', back.api.state.goals.length === 3 && back.api.state.goals[2].required === false && back.api.state.goals[2].notify === false && back.api.state.extractionGoals === true);
    s.extractionAfter = 1; api.changed();
    check('with a trigger too the extraction keeps "after" first', text(JSON.parse(api.scenarioText()).mission.extraction) === text({ after: 1, goals: true }));
    s.goals = []; api.changed();
    const gated = JSON.parse(api.scenarioText());
    check('an extraction that waits for objectives alone still needs plan/3', gated.schema === 'missionlab-plan/3' && gated.mission.goals === undefined, gated.schema);
    s.extractionGoals = false; api.changed();
    const none = JSON.parse(api.scenarioText());
    check('with neither, the format falls back exactly as before', none.schema === 'missionlab-plan/1' && text(none.extraction) === text({ after: 1 }), none.schema);
    s.extractionAfter = 0; api.changed();
    check('and the text is exactly what it was before any objective', api.scenarioText() === plainText, h.differences(plain, JSON.parse(api.scenarioText()), '', []).join(' / '));
    await back.api.openFiles([h.fakeFile('plain.json', plainText)]);
    check('opening a file without goals clears the page\'s objectives', !back.api.state.refused && back.api.state.goals.length === 0 && back.api.state.extractionGoals === false, back.banner());
  }

  {   // Each refusal of the contract: exact code and path, in the reader's order.
    const { api } = fresh();
    const triggers = [{ id: 1, name: '', when: 'time', delay: 1 }, { id: 2, name: '', when: 'alert' }];
    const plan3 = (goals, extra) => ({ schema: 'missionlab-plan/3', mission: Object.assign({ name: 'g', triggers, spawns: [], goals }, extra || {}) });
    const errs = obj => api.scenarioProblems(text(obj)).filter(f => f.level === 'error').map(f => f.code + ' ' + f.path);
    const ok = { id: 'a', name: 'A', succeed: 1 };
    const cases = [
      ['goals in plan/1', { schema: 'missionlab-plan/1', name: 'g', triggers, spawns: [], goals: [ok] }, ['unknown-format goals']],
      ['goals in plan/2', { schema: 'missionlab-plan/2', mission: { name: 'g', triggers, spawns: [], goals: [ok] } }, ['unknown-format goals']],
      ['goals in a file naming no format', { name: 'g', triggers, spawns: [], goals: [ok] }, ['unknown-format goals']],
      ['missing id', plan3([{ name: 'A', succeed: 1 }]), ['missing-field goals[0].id']],
      ['missing name', plan3([{ id: 'a', succeed: 1 }]), ['missing-field goals[0].name']],
      ['a name that is blank once cleaned', plan3([{ id: 'a', name: ' \n\t ', succeed: 1 }]), ['missing-field goals[0].name']],
      ['missing succeed', plan3([{ id: 'a', name: 'A' }]), ['missing-field goals[0].succeed']],
      ['a bad id', plan3([{ id: 'Phone 1', name: 'A', succeed: 1 }]), ['bad-id goals[0].id']],
      ['an id starting with "-"', plan3([{ id: '-a', name: 'A', succeed: 1 }]), ['bad-id goals[0].id']],
      ['a duplicate id', plan3([ok, { id: 'a', name: 'B', succeed: 2 }]), ['duplicate-id goals[1].id']],
      ['after names an unknown goal', plan3([{ id: 'a', name: 'A', after: ['zz'], succeed: 1 }]), ['missing-ref goals[0].after[0]']],
      ['after names itself', plan3([{ id: 'a', name: 'A', after: ['a'], succeed: 1 }]), ['self-ref goals[0].after[0]']],
      ['activate names an unknown trigger', plan3([{ id: 'a', name: 'A', activate: 9, succeed: 1 }]), ['missing-ref goals[0].activate']],
      ['succeed names an unknown trigger', plan3([{ id: 'a', name: 'A', succeed: 9 }]), ['missing-ref goals[0].succeed']],
      ['succeed is 0', plan3([{ id: 'a', name: 'A', succeed: 0 }]), ['missing-ref goals[0].succeed']],
      ['fail names an unknown trigger', plan3([{ id: 'a', name: 'A', succeed: 1, fail: 9 }]), ['missing-ref goals[0].fail']],
      ['every field of the wrong type, in reading order', plan3([{ id: 'a', name: 'A', required: 'yes', after: 'b', activate: '1', succeed: 1.5, fail: true, notify: 1 }]),
        ['wrong-type goals[0].required', 'wrong-type goals[0].after', 'wrong-type goals[0].activate', 'wrong-type goals[0].succeed', 'wrong-type goals[0].fail', 'wrong-type goals[0].notify']],
      ['an id that is not text', plan3([{ id: 5, name: 'A', succeed: 1 }]), ['wrong-type goals[0].id']],
      ['a name that is not text', plan3([{ id: 'a', name: 3, succeed: 1 }]), ['wrong-type goals[0].name']],
      ['an after entry that is not text', plan3([{ id: 'a', name: 'A', after: [3], succeed: 1 }]), ['wrong-type goals[0].after[0]']],
      ['goals that are not a list', plan3({ a: ok }), ['wrong-type goals']],
      ['a goal that is not an object', plan3([3]), ['wrong-type goals[0]']],
      ['extraction.goals that is not true or false', plan3([ok], { extraction: { goals: 'yes' } }), ['wrong-type extraction.goals']],
      ['several goals: each finding at its own goal', plan3([{ id: 'a', name: 'A', succeed: 9 }, { id: 'a', name: '', after: ['a'], succeed: 1 }]),
        ['missing-ref goals[0].succeed', 'duplicate-id goals[1].id', 'missing-field goals[1].name', 'self-ref goals[1].after[0]']],
    ];
    for (const [what, obj, want] of cases) { const got = errs(obj); check('refused: ' + what, text(got) === text(want), text(got)); }
    check('"after" may name a goal further down the list', errs(plan3([{ id: 'b', name: 'B', after: ['a'], succeed: 2 }, ok])).length === 0);
    check('extraction.goals in a plan/2 file is only checked for its type', errs({ schema: 'missionlab-plan/2', mission: { name: 'g', triggers, spawns: [], extraction: { goals: true } } }).length === 0);
    const long = 'x'.repeat(90), page = await load(script, 'long.json', text(plan3([{ id: 'a', name: long, succeed: 1 }])));
    check('a name over 80 characters is cut, not refused', !page.api.state.refused && page.api.state.goals[0].name === 'x'.repeat(80), page.banner());
  }

  {   // The mission check, mirroring Completion.
    const t = fresh(), { api, s } = t;
    const goalFindings = () => api.missionCheck().filter(f => f.about && Array.isArray(f.about.goals));
    s.goals = [goal('a', 'Alpha', { after: ['b'] }), goal('b', 'Bravo', { after: ['a'], succeed: 2 })]; s.extractionGoals = true; api.changed();
    let circle = goalFindings().find(f => f.about.why === 'circle');
    check('a circle among objectives blocks when the extraction waits for them', !!circle && circle.level === 'problem' && text(circle.about.goals) === text(['a', 'b']) && circle.about.extraction === true && circle.text.includes('"Alpha"') && circle.text.includes('"Bravo"'), circle && circle.text);
    s.extractionGoals = false; api.changed();
    circle = goalFindings().find(f => f.about.why === 'circle');
    check('without the wait it is only a note', !!circle && circle.level === 'note', circle && circle.level);
    s.goals = [goal('phone', 'Recover the phone', { succeed: 4 })]; s.extractionGoals = true; api.changed();
    let never = goalFindings().find(f => f.about.why === 'never');
    check('a required objective completed only by hand blocks the gated extraction', !!never && never.level === 'problem' && never.text.includes('"Recover the phone"') && text(never.about.triggers) === text([4]), never && never.text);
    s.gameMaster = true; api.changed();
    check('with a Game Master in the plan it can be completed', !goalFindings().some(f => f.about.why === 'never'), text(goalFindings().map(f => f.text)));
    s.gameMaster = false;
    s.goals = [goal('cut', 'Cut the power', { required: false, succeed: 4 }), goal('upper', 'Secure the upper floor', { after: ['cut'], succeed: 2 })]; api.changed();
    never = goalFindings().filter(f => f.about.why === 'never');
    check('an optional objective that can never be completed holds nothing by itself', !never.some(f => f.about.goals.includes('cut')), text(never.map(f => f.text)));
    check('but a required one waiting for it is blocked, and says why', never.length === 1 && never[0].about.goals[0] === 'upper' && never[0].level === 'problem' && never[0].text.includes('"Cut the power"'), text(never.map(f => f.text)));
    s.zones[1].opensExtraction = true; api.changed();
    never = goalFindings().find(f => f.about.why === 'never');
    check('a trigger that can fire and opens the extraction makes it a note', !!never && never.level === 'note', never && never.level);
    s.zones[1].opensExtraction = false;
    s.goals = phoneJob(); api.changed();
    const all = api.missionCheck(), named = all.find(f => f.text.startsWith('The extraction stays shut until every required objective'));
    check('nothing blocks the phone job', !all.some(f => f.level === 'problem' && f.about && f.about.goals), text(all.filter(f => f.level === 'problem').map(f => f.text)));
    check('the check names the required objectives the extraction waits for, not the optional one', !!named && named.text.includes('"Recover the phone"') && named.text.includes('"Secure the upper floor"') && !named.text.includes('Cut the power'), named && named.text);
    s.zones.splice(2, 1); api.changed();
    const gone = api.missionCheck().find(f => f.about && f.about.why === 'missing');
    check('a goal naming a trigger that is gone is a problem', !!gone && gone.level === 'problem' && gone.text.includes('trigger 3'), gone && gone.text);
  }

  {   // Every edit is one undo step, and undo puts each back.
    const { api, s } = fresh();
    const look = () => text([s.goals, s.extractionGoals]), steps = [], n0 = api.undoDepth();
    const step = (what, fn) => { const before = look(), depth = api.undoDepth(), done = fn(); steps.push(before); check(what + ' is one undo step', !!done && api.undoDepth() === depth + 1 && look() !== before, api.undoDepth() - depth); };
    step('adding an objective', () => api.goalAdd());
    step('naming it', () => api.goalSet(0, 'name', 'Recover the phone'));
    let depth = api.undoDepth();
    check('setting the same name again adds nothing', api.goalSet(0, 'name', 'Recover the phone') === false && api.undoDepth() === depth);
    check('a bad id is turned down, with no step', api.goalSet(0, 'id', 'Bad Id') === false && api.undoDepth() === depth && s.goals[0].id === 'goal-1', s.goals[0].id);
    check('a trigger that is not there is turned down', api.goalSet(0, 'succeed', 99) === false && api.undoDepth() === depth && s.goals[0].succeed === 1);
    step('adding a second', () => api.goalAdd());
    step('making it wait for the first', () => api.goalSetAfter(1, 'goal-1', true));
    step('renaming the first one\'s id', () => api.goalSet(0, 'id', 'phone'));
    check('the rename follows into the other goal\'s "after", in the same step', text(s.goals[1].after) === text(['phone']), text(s.goals[1].after));
    step('making it optional', () => api.goalSet(1, 'required', false));
    step('choosing the trigger that fails it', () => api.goalSet(0, 'fail', 5));
    step('holding the extraction for them', () => api.goalExtraction(true));
    step('moving it up', () => api.goalMove(1, -1));
    step('deleting the phone objective', () => api.goalDelete(1));
    check('deleting takes it out of every "after"', s.goals.length === 1 && s.goals[0].after.length === 0, text(s.goals));
    check('there are as many steps as edits', api.undoDepth() === n0 + steps.length, api.undoDepth() - n0);
    let back = true;
    for (let i = steps.length - 1; i >= 0; i--) { api.undo(); if (look() !== steps[i]) { back = false; break; } }
    check('undo puts every edit back, one at a time', back && s.goals.length === 0 && s.extractionGoals === false, look());
    api.redo(); api.redo();
    check('redo brings them again', s.goals.length === 1 && s.goals[0].name === 'Recover the phone', look());
  }

  {   // Sealed Play and the Direct board, from a status the game could send.
    const { api, s } = fresh();
    s.goals = phoneJob().concat([goal('roof', 'Reach the roof', { after: ['upper'], succeed: 2 })]); s.extractionGoals = true;
    const status = { mission: true, extraction: { goals: true, waits: "the extraction waits for the objectives 'Secure the upper floor', 'Reach the roof' (not yet active)" },
      goals: [{ id: 'phone', name: 'Recover the phone', required: true, state: 'succeeded' }, { id: 'upper', name: 'Secure the upper floor', required: true, state: 'active' },
        { id: 'roof', name: 'Reach the roof', required: true, state: 'inactive' }, { id: 'power', name: 'Cut the power', required: false, state: 'failed' },
        { id: 'odd', name: 'Something new', required: false, state: 'paused' }] };
    s.sealed = true; api.changed();
    check('the page is sealed and not revealed', api.sealedHides() === true);
    const play = api.goalPlayModel(status), board = api.goalBoardModel(status);
    check('sealed Play shows only the objectives the game has given', text(play.rows.map(r => r.id)) === text(['phone', 'upper', 'power']), text(play.rows.map(r => r.id)));
    check('nothing in sealed Play names the inactive objective', !text(play).includes('Reach the roof') && !text(play).includes('Something new'), text(play));
    check('the sealed board shows the same and leaves the game\'s sentence out', text(board.rows.map(r => r.id)) === text(['phone', 'upper', 'power']) && !text(board).includes('Reach the roof'), text(board));
    check('the sealed board still says what of it the extraction waits for', board.wait.includes('"Secure the upper floor"'), board.wait);
    const before = api.goalPlayModel(null);
    check('before the run a sealed Play view shows no objective at all', before.rows.length === 0 && !text(before).includes('Recover the phone'), text(before));
    s.sealed = false; api.changed();
    const open = api.syncGoalBoard(status);
    check('unsealed, the board shows every objective with its state in words', open.rows.length === 5 && open.rows[0].word === 'Complete' && open.rows[1].word === 'Active' && open.rows[2].word === 'Not given yet' && open.rows[3].word === 'Failed', text(open.rows));
    check('a state the page does not know counts as not given', open.rows[4].state === 'inactive', text(open.rows[4]));
    check('and uses the game\'s own sentence for the extraction', open.wait.startsWith('The extraction waits for the objectives') && open.wait.endsWith('.'), open.wait);
    const offline = api.goalPlayModel(null);
    check('offline and unsealed, Play lists the page\'s objectives', offline.rows.length === 4 && offline.rows[2].word === 'Optional', text(offline.rows));
    let old = null, threw = '';
    try { old = api.syncGoalBoard({ mission: true }); } catch (e) { threw = String(e); }
    check('a status without goals from an older plugin is tolerated', !threw && old.rows.length === 0 && old.note.includes('older'), threw || text(old));
    const failed = api.goalBoardModel({ mission: true, goals: [{ id: 'phone', name: 'Recover the phone', required: true, state: 'failed' }] });
    check('a failed required objective says the extraction can no longer open by objectives', failed.wait.includes('"Recover the phone"') && failed.wait.includes('no longer'), failed.wait);
  }

  {   // Rehearsal: an objective check is written and read as Core's Rehearsal reads it; the answer's transitions are events.
    const { api, s } = fresh();
    s.goals = phoneJob(); api.changed();
    const step = api.rehearseStep({ at: 12, kind: 'goal', goal: 'phone', state: 'succeeded' });
    check('a goal row is one step expecting that state', text(step) === text({ at: 12, expect: { goals: { phone: 'succeeded' } } }), text(step));
    check('a goal row without a state is no step', api.rehearseStep({ at: 1, kind: 'goal', goal: 'phone', state: '' }) === null);
    const scriptText = api.rehearseText({ players: 2, steps: [{ at: 5, expect: { fired: [1], phase: { 2: 'waiting' }, goals: { upper: 'active', phone: 'succeeded' }, extraction: 'held', enemies: 3 } }] });
    check('the expectation keeps Rehearsal.Write\'s key order, goal ids sorted', scriptText.includes('{"at":5,"expect":{"fired":[1],"phase":{"2":"waiting"},"goals":{"phone":"succeeded","upper":"active"},"extraction":"held","enemies":3}}'), scriptText);
    const rows = api.rehearseRows(JSON.parse(scriptText)).filter(r => r.kind === 'goal');
    check('read back, each goal is its own check row', text(rows) === text([{ at: 5, kind: 'goal', goal: 'phone', state: 'succeeded' }, { at: 5, kind: 'goal', goal: 'upper', state: 'active' }]), text(rows));
    const probs = api.rehearseProblems([{ at: 1, kind: 'goal', goal: 'roof', state: 'active' }, { at: 2, kind: 'goal', goal: 'phone', state: 'asleep' }, { at: 3, kind: 'goal', goal: 'phone', state: 'failed' }], [1, 2, 3, 4, 5]).map(f => f.text);
    check('an unknown objective and an unknown state are named; a good row passes', text(probs) === text(['Row 1: there is no objective "roof" in this scenario.', 'Row 2: choose the objective\'s state.']), text(probs));
    const look = (at, gb, ga) => ({ at, before: { 1: 'waiting' }, after: { 1: 'waiting' }, inside: { 1: '0/2' }, goalsBefore: gb, goalsAfter: ga });
    const move = (n, at) => ({ at, what: 'step ' + n + ' at ' + at + ' s: 2 living players stand', trigger: 0, extraction: false, expected: '', actual: '', ok: true });
    const answer = { passed: true, steps: [move(1, 0), move(2, 5), move(3, 10)],
      looks: [look(0, { phone: 'active' }, { phone: 'active' }), look(5, { phone: 'active' }, { phone: 'succeeded' }), look(10, { phone: 'succeeded' }, { phone: 'succeeded' })],
      goals: [{ id: 'phone', from: 'active', to: 'succeeded', at: 5 }], findings: [] };
    check('a move at which an objective changed state is an event; the others are passed over', text(api.rehearseEvents(answer)) === text([1]), text(api.rehearseEvents(answer)));
    const words = api.rehearseGoalWords(api.rehearseLook(answer, 5), id => id);
    check('the moment says which objective changed and how', text(words.changed) === text(['phone: active → succeeded']) && words.same.length === 0, text(words));
    check('the transitions are read in order', text(api.rehearseGoalEvents(answer).map(e => e.id + ':' + e.to)) === text(['phone:succeeded']));
    const later = JSON.parse(text(answer)); later.looks[1].goalsAfter.phone = 'active'; later.goals = [];
    const diff = api.rehearseDivergence(answer, later);
    check('two runs that part on an objective say where', !!diff && diff.at === 5 && diff.text.includes('phone was succeeded, now active'), diff && diff.text);
    const plain = JSON.parse(text(answer)); for (const l of plain.looks) { delete l.goalsBefore; delete l.goalsAfter; } delete plain.goals;
    check('an answer without objectives keeps the events as before', text(api.rehearseEvents(plain)) === text([]), text(api.rehearseEvents(plain)));
    s.rehearse.answer = answer; s.rehearse.cursor = 1;
    let threw = ''; try { api.rhShow(); } catch (e) { threw = String(e && e.stack || e); }
    check('the result view shows an answer with objectives', !threw, threw);
  }

  {   // While a mission runs, the page's objectives are the running plan's, and they cannot be edited.
    const { api, s } = fresh();
    s.goals = phoneJob(); s.extractionGoals = false; api.changed();
    api.live.on = true; api.live.mission = true;
    api.goalsFromGame({ mission: true, goalPlan: [{ id: 'raid', name: 'Raid the house', succeed: 2 }], extraction: { after: 1, goals: true } });
    check('the running plan\'s objectives replace the last opened scenario\'s', text(s.goals.map(g => g.id)) === text(['raid']) && s.extractionGoals === true, text(s.goals));
    const named = api.missionCheck().find(f => f.text.startsWith('The extraction stays shut'));
    check('the mission check judges the running plan', !!named && named.text.includes('"Raid the house"') && !named.text.includes('Recover the phone'), named && named.text);
    const depth = api.undoDepth();
    check('they cannot be edited while it runs', api.goalSet(0, 'name', 'Other') === false && api.goalAdd() === '' && api.undoDepth() === depth && s.goals[0].name === 'Raid the house', s.goals[0].name);
    api.goalsFromGame({ mission: true, extraction: { after: 1 } });
    check('a running plan without objectives leaves none on the page', s.goals.length === 0 && s.extractionGoals === false, text(s.goals));
    api.live.on = false; api.live.mission = false;
  }

  {   // A recording's objective events: labelled in the list, counted in the summary. The text is the recording's own.
    const { api } = fresh();
    const rec = JSON.parse(fs.readFileSync(path.join(h.FIXTURES, 'debrief', 'raid.replay.json'), 'utf8'));
    const said = "goal 'Recover the phone' (phone): active -> succeeded at 42.5 s";
    rec.events.push([42.5, 'goal', said]);
    api.loadReplay(rec, 'goals.replay.json');
    const row = api.replayStats(api.replay.data).find(r => r[0] === 'Objective changes');
    check('the summary counts objective changes and goes to the first', !!row && row[1].startsWith('1, ') && row[2] === 42.5, text(row));
    const ev = api.replay.data.events.find(e => e[1] === 'goal');
    check('the list shows the recorded text after an Objective label', !!ev && api.replayEventText(ev) === 'Objective · ' + said, ev && api.replayEventText(ev));
  }
}
