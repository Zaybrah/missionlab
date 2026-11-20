// ML-052: the pressure director. The editor writes director.pressure in the plugin's key order with its defaults left out
// and reads it back byte for byte; its lenient reading clamps exactly as the plugin's; its strict reader refuses the
// contract's bad values with the same code and path as ScenarioFile (tests/Pure/Ml052Tests.cs holds the same list).
// Authoring keeps every edit one undo step and points at bands that are never reached. Direct shows the game's decision
// log newest first, manual choices labelled, and sends the mode, "hold now" and "spend now" as the plugin reads them.
import fs from 'node:fs';
import path from 'node:path';

export default async function (h) {
  const { script, openEditor, load, check } = h;
  const text = JSON.stringify;
  const fixture = name => fs.readFileSync(path.join(h.FIXTURES, name), 'utf8');

  {   // Written as the plugin writes it, read back byte for byte. The literal is the one Ml052Tests holds the C# writer to.
    const page = await load(script, 'plan3_pressure.json', fixture('schema/valid/plan3_pressure.json'));
    check('a file with pressure bounds loads without a finding', !page.api.state.refused, page.banner());
    const first = page.api.scenarioText(), m = JSON.parse(first).mission || {};
    check('the director block, pressure and all, is written in the plugin\'s key order with defaults left out', text(m.director) === '{"budget":10,"regen":1,"actions":[{"id":"rear","name":"Rear reinforcements","cost":3,"cooldown":60,"fire":2,"minDistance":30,"unseen":true}],'
      + '"pressure":{"mode":"on","bands":[{"name":"Phone taken","from":120,"goals":["phone"],"minPlayers":2,"spend":3,"every":30},{"name":"Otherwise quiet","spend":0}],"maxEnemies":12,"rest":45}}', text(m.director));
    const back = await load(script, 'again.json', first);
    check('it reads back and writes the same text', !back.api.state.refused && back.api.scenarioText() === first, h.differences(JSON.parse(first), JSON.parse(back.api.scenarioText()), '', []).join(' / '));
    const v27 = await load(script, 'all_fields_v27.json', fixture('all_fields_v27.json')), d27 = (JSON.parse(v27.api.scenarioText()).mission || {}).director || {};
    check('all_fields_v27.json keeps its pressure bounds exactly', text(d27.pressure) === '{"mode":"preview","bands":[{"name":"Upstairs push","from":120,"minPlayers":2,"spend":4,"every":45},{"name":"Otherwise quiet","spend":0}],"maxEnemies":12,"rest":90}', text(d27.pressure));
    const plain = { schema: 'missionlab-plan/1', triggers: [{ id: 1, when: 'alert' }], director: { budget: 5, actions: [{ id: 'r', name: 'R', cost: 1, fire: 1 }], pressure: { mode: 'off', bands: [{ name: ' All ', from: 0, every: 60 }], rest: 60 } }, spawns: [{ pos: [0, 0, 0] }] };
    const p = await load(script, 'plain.json', text(plain)), dp = JSON.parse(p.api.scenarioText()).director;
    check('every default is left out and the name is trimmed, as the plugin writes it', text(dp.pressure) === '{"bands":[{"name":"All"}]}', text(dp));
    const none = await load(script, 'none.json', text(Object.assign({}, plain, { director: { budget: 5, actions: [] } })));
    check('a director without pressure bounds writes none', !/"pressure"/.test(none.api.scenarioText()), none.api.scenarioText().slice(0, 200));
    const noneObj = Object.assign({}, plain, { director: { budget: 5, actions: [] } });
    check('any pressure bounds, even authored off, name the pressure capability, which the page knows; none do not',
      p.api.capabilitiesOf(plain).includes('pressure') && p.api.PACK_KNOWN.includes('pressure') && !p.api.capabilitiesOf(noneObj).includes('pressure') && p.api.capabilitiesOf(noneObj).includes('director'), text(p.api.capabilitiesOf(plain)));
  }

  {   // The lenient reading clamps as the plugin's does; the literal is the plugin's PressureJson of the same input.
    const api = openEditor(script).api;
    const seven = [1, 2, 3, 4, 5, 6, 7].map(i => ({ name: 'B' + i, every: 5, minPlayers: 40, spend: 2000 }));
    const read = api.pressureIn({ mode: 'auto', bands: seven, maxEnemies: 900, rest: -3 });
    check('values are clamped, the mode falls back to off and six bands are kept', text(api.pressureOut(read)) === text({ bands: [1, 2, 3, 4, 5, 6].map(i => ({ name: 'B' + i, minPlayers: 16, spend: 1000, every: 10 })), maxEnemies: 500, rest: 0 }), text(api.pressureOut(read)));
    check('a band waiting for an objective the plan lacks is left out, and no band means no bounds', text(api.pressureIn({ bands: [{ name: 'Phone', goals: ['phone'] }, { name: 'Rest' }] }).bands.map(b => b.name)) === '["Rest"]' && api.pressureIn({ bands: [] }) === null);
  }

  {   // The strict reader's refusals, in the plugin's order and with its paths.
    const api = openEditor(script).api;
    const plan = pressure => ({ schema: 'missionlab-plan/1', triggers: [{ id: 1, when: 'alert' }], director: { budget: 5, actions: [{ id: 'rear', name: 'Rear', cost: 1, fire: 1 }], pressure }, spawns: [{ pos: [0, 0, 0] }] });
    const errs = obj => api.scenarioProblems(text(obj)).filter(f => f.level === 'error').map(f => f.code + ' ' + f.path).join(' | ');
    const seven = [1, 2, 3, 4, 5, 6, 7].map(i => ({ name: 'B' + i }));
    const cases = [
      ['pressure that is not an object', 3, 'wrong-type director.pressure'],
      ['no bands', { mode: 'on' }, 'missing-field director.pressure.bands'],
      ['bands that are not a list', { bands: {} }, 'wrong-type director.pressure.bands'],
      ['no band in the list', { bands: [] }, 'out-of-range director.pressure.bands'],
      ['seven bands', { bands: seven }, 'out-of-range director.pressure.bands'],
      ['a mode that is not text', { mode: true, bands: [{ name: 'A' }] }, 'wrong-type director.pressure.mode'],
      ['an unknown mode', { mode: 'auto', bands: [{ name: 'A' }] }, 'unknown-value director.pressure.mode'],
      ['a goal in a plan without goals', { bands: [{ name: 'A', goals: ['phone'] }] }, 'missing-ref director.pressure.bands[0].goals[0]'],
      ['a blank name', { bands: [{ name: '  ' }] }, 'missing-field director.pressure.bands[0].name'],
      ['a name that is not text', { bands: [{ name: 3 }] }, 'wrong-type director.pressure.bands[0].name'],
      ['decisions too often', { bands: [{ name: 'A', every: 9.5 }] }, 'out-of-range director.pressure.bands[0].every'],
      ['a cap that is not whole and a rest that is not a number', { bands: [{ name: 'A' }], maxEnemies: 2.5, rest: 'long' }, 'wrong-type director.pressure.maxEnemies | wrong-type director.pressure.rest'],
      ['every limit at its edge', { mode: 'preview', bands: [{ name: 'A', from: 36000, minPlayers: 16, spend: 1000, every: 600 }, { name: 'B', from: 0, minPlayers: 0, spend: 0, every: 10 }], maxEnemies: 500, rest: 600 }, ''],
    ];
    for (const [what, pressure, want] of cases) { const got = errs(plan(pressure)); check((want ? 'refused: ' : 'accepted: ') + what, got === want, got); }
    const expected = JSON.parse(fixture('expected.json'))['bad_pressure.json'].join(' | ');
    check('invalid/bad_pressure.json is refused with exactly the listed codes and places', errs(JSON.parse(fixture('invalid/bad_pressure.json'))) === expected, errs(JSON.parse(fixture('invalid/bad_pressure.json'))));
  }

  {   // Authoring: each edit one undo step, the file rules' findings at the band, and what the page adds.
    const page = openEditor(script), api = page.api, s = api.state;
    s.map = api.makeMap(new Float32Array([-60, 0, -60, 60, 0, -60, 60, 0, 60, -60, 0, -60, 60, 0, 60, -60, 0, 60]), 'pressure test');
    Object.assign(s, { example: false, title: 'Pressure', infils: [], authored: [], objectives: [], things: [], doors: [], selected: -1, squad: [] });
    s.enemies = [api.newEnemy([0, 0, 0], 'Guard', 0)];
    s.zones = [api.newTrigger(1, 'after', { after: 0 }), api.newTrigger(2, 'alert')];
    api.changed();
    check('without a budget there are no pressure bounds to switch on', api.pressureAuthorOn(true) === false);
    api.cardAuthorOn(true); api.cardAuthorAdd();
    const steps = () => api.undoDepth();
    let n = steps();
    check('switching it on is one undo step and starts with one band that always applies, off', api.pressureAuthorOn(true) && steps() === n + 1 && text(s.director.pressure) === text({ mode: 'off', bands: [{ name: 'Whole mission', from: 0, goals: [], minPlayers: 0, spend: -1, every: 60 }], maxEnemies: 0, rest: 60 }), text(s.director.pressure));
    const findings = () => api.cardAuthorProblems();
    check('a fresh pressure block has no finding from the file rules', !findings().some(f => f.level === 'error'), text(findings()));
    n = steps();
    check('a band is added in one step', api.pressureBandAdd() && steps() === n + 1 && s.director.pressure.bands[1].name === 'Band 2');
    check('the page says the band after one that always applies is never used', findings().some(f => f.path === 'director.pressure.bands[1]' && /never used/.test(f.text)), text(findings()));
    api.pressureBandMove(1, -1);
    check('moving it first leaves the old first band reachable only after it', s.director.pressure.bands[0].name === 'Band 2' && findings().some(f => f.path === 'director.pressure.bands[1]'), text(s.director.pressure.bands.map(b => b.name)));
    n = steps();
    api.pressureSet(0, 'from', '120'); api.pressureSet(0, 'minPlayers', '2'); api.pressureSet(0, 'spend', '0'); api.pressureSet(-1, 'mode', 'preview');
    check('each value is its own undo step', steps() === n + 4 && s.director.pressure.bands[0].from === 120 && s.director.pressure.bands[0].spend === 0 && s.director.pressure.mode === 'preview');
    check('a band that spends less than any card is said to only hold', findings().some(f => f.path === 'director.pressure.bands[0].spend' && /only holds/.test(f.text)), text(findings()));
    api.pressureSet(0, 'spend', '');
    check('an empty spend means the whole budget and is left out of the file', s.director.pressure.bands[0].spend === -1 && !('spend' in JSON.parse(api.scenarioText()).director.pressure.bands[0]));
    api.pressureSet(0, 'every', '5');
    const tooOften = findings().find(f => f.path === 'director.pressure.bands[0].every');
    check('a value out of range is the file rules\' finding, named at the band', !!tooOften && tooOften.level === 'error', text(findings()));
    api.pressureSet(0, 'every', '30');
    s.goals = [{ id: 'phone', name: 'Recover the phone', required: true, after: [], activate: 0, succeed: 2, fail: 0, notify: true }]; api.changed();
    n = steps();
    check('an objective is added to a band in one step and written by its id', api.pressureGoalSet(0, 'phone', true) && steps() === n + 1 && text(JSON.parse(api.scenarioText()).mission.director.pressure.bands[0].goals) === '["phone"]');
    check('a plan with objectives and pressure bounds passes the file rules', !api.scenarioProblems(api.scenarioText()).some(f => f.level === 'error'), text(api.scenarioProblems(api.scenarioText()).filter(f => f.level === 'error')));
    s.goals = []; api.changed();
    check('a band naming an objective that is gone is refused at its place, not quietly widened', findings().some(f => f.level === 'error' && f.path === 'director.pressure.bands[0].goals[0]'), text(findings()));
    api.pressureGoalSet(0, 'phone', false);
    for (let i = 0; i < 6; i++) api.pressureBandAdd();
    check('there are never more than six bands', s.director.pressure.bands.length === 6 && api.pressureBandAdd() === false);
    while (s.director.pressure.bands.length > 1) api.pressureBandDelete(0);
    check('the last band cannot be deleted; the director is switched off instead', api.pressureBandDelete(0) === false && s.director.pressure.bands.length === 1);
    n = steps();
    check('switching it off is one step and leaves no pressure key', api.pressureAuthorOn(false) && steps() === n + 1 && !/"pressure"/.test(api.scenarioText()));
    api.undo();
    check('undo brings the bounds back', !!s.director.pressure && s.director.pressure.bands.length === 1, text(s.director));
  }

  {   // Direct: the game's log, newest first, manual choices labelled; the controls send what the plugin reads.
    const api = openEditor(script).api;
    const rejected = [{ action: 'push', constraint: 'maxEnemies', observed: 'living enemies 5', why: '5 living + 5 it brings = 10, over the cap of 8' }];
    const entry = (at, kind, more) => Object.assign({ at, kind, mode: 'on', manual: false, band: '', action: '', cost: 0, command: '', constraint: '', observed: '', why: '', words: kind + at, rejected: [] }, more);
    const snapshot = {
      mode: 'on', authored: 'preview', next: 12.5, restLeft: 0, waiting: '', maxEnemies: 8, rest: 60, approximation: 'an approximation: the cards\' distance and view test, not the game\'s own sight',
      log: [
        entry(60, 'spend', { band: 'All', action: 'rear', cost: 4, command: 'pressure-1', observed: 'elapsed 60 s, living players 4', why: 'the most expensive approved card within 20 point(s) that passes every bound', rejected }),
        entry(61, 'result', { action: 'rear', cost: 4, command: 'pressure-1', constraint: 'failed', why: 'the spawner refused' }),
        entry(90, 'hold', { manual: true, constraint: 'rest', why: 'no automatic spending for 60 s' }),
        entry(120, 'preview', { mode: 'preview', band: 'All', action: 'flare', cost: 2, why: 'the most expensive approved card within 20 point(s) that passes every bound' }),
      ],
    };
    const budget = { points: 16, budget: 20, regen: 0, on: true, actions: [{ id: 'rear', name: 'Rear group', cost: 4, fire: 12 }, { id: 'push', name: 'Push', cost: 6, fire: 13 }, { id: 'flare', name: 'Flare', cost: 2, fire: 11 }], spent: [] };
    api.cardTake({ session: 's1', mission: true, budget, pressure: snapshot });
    const rows = api.pressureRows();
    check('the log is shown newest first', text(rows.map(r => r.kind)) === '["preview","hold","result","spend"]', text(rows.map(r => r.kind)));
    check('a preview row says it fires nothing', rows[0].label === 'Would play "Flare" (2 points); preview fires nothing in band "All"', rows[0].label);
    check('a manual hold is labelled manual and says why', rows[1].manual && rows[1].label === 'Manual: Held: resting' && rows[1].why === 'no automatic spending for 60 s', text(rows[1]));
    check('a result names the card and what came of it', rows[2].label === 'Result of "Rear group" (4 points): failed' && rows[2].why === 'the spawner refused', text(rows[2]));
    check('a spend names the band, what was seen, and each card passed over with its bound and reason',
      rows[3].label === 'Played "Rear group" (4 points) in band "All"' && rows[3].observed === 'Seen: elapsed 60 s, living players 4' && rows[3].rejected[0] === '"Push" maxEnemies: 5 living + 5 it brings = 10, over the cap of 8 (seen: living enemies 5)', text(rows[3]));
    const note = api.pressureNote(snapshot);
    check('the note says the mode, the author\'s mode, when it decides next and that placement is an approximation',
      /^On: /.test(note) && note.includes('The author set preview.') && note.includes('Next decision in 0:13.') && note.includes('an approximation'), note);
    check('waiting and resting are said instead of the next decision', api.pressureNote(Object.assign({}, snapshot, { waiting: 'pressure-2' })).includes('Waiting for its last card') && api.pressureNote(Object.assign({}, snapshot, { restLeft: 40 })).includes('Resting, 0:40 left'));
    const sent = [], io = { send: body => { sent.push(body); return Promise.resolve({}); } };
    await api.pressureModeSet('preview', io); await api.pressureHoldNow(io); await api.pressureSpendNow(io);
    check('an unknown mode is never sent', api.pressureModeSet('auto', io) === null && sent.length === 3);
    check('the mode, hold now and spend now go as {"cmd":"pressure","what":...,"arg":...}, as the plugin reads them',
      text(sent) === text([{ cmd: 'pressure', what: 'mode', arg: 'preview' }, { cmd: 'pressure', what: 'hold' }, { cmd: 'pressure', what: 'spend' }]), text(sent));
    api.cardTake({ session: 's1', mission: true, budget });
    check('a state without the director shows none', api.cards.pressure === null && api.pressureRows().length === 0);
    api.cardTake({ session: 's1', mission: false, budget, pressure: snapshot });
    check('nor does a state without a mission', api.cards.pressure === null);
  }
}
