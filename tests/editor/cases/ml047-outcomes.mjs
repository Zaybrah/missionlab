// ML-047: a trigger that waits for what was seen of another trigger's action. It reads and writes back byte for byte,
// with "outcome" and "within" right after "after" and nothing new on any other kind; every refusal of the shared contract
// comes with its exact code and path; the mission check blocks a watcher that can never hear "applied" only when the
// extraction hangs on it; adding and editing one is one undo step each; the graph draws its line apart from an after line,
// with the outcome on it; the inspector keeps a late result apart from the current one; a rehearsal row reports an outcome
// in the contract's step shape.
import fs from 'node:fs';
import path from 'node:path';

export default async function (h) {
  const { script, openEditor, load, check } = h;
  const text = JSON.stringify;
  const body = obj => obj.mission || obj;
  // Trigger 1 brings two guards 10 s in; 2 waits to see them made; 3 takes the fallback when no answer comes in 45 s;
  // 4 waits for a failure, with the default window.
  const outcomeFile = () => text({ schema: 'missionlab-plan/1', name: 'Reinforcements seen',
    spawns: [{ pos: [0, 0, 0], role: 'Guard', trigger: 1 }, { pos: [3, 0, 3], role: 'Guard', trigger: 1 }],
    triggers: [
      { id: 1, name: 'Reinforcements', when: 'time', delay: 10, facePlayer: true },
      { id: 2, name: 'Defend', when: 'outcome', after: 1, outcome: 'applied', delay: 5, facePlayer: true, message: 'Hold the yard' },
      { id: 3, name: 'Fallback', when: 'outcome', after: 1, outcome: 'timeout', within: 45, facePlayer: true, extraction: 'open' },
      { id: 4, name: 'Spawn failed', when: 'outcome', after: 1, outcome: 'failed', within: 30, facePlayer: true, message: 'No reinforcements' },
    ] }, null, 1);

  {   // Read and written back byte for byte; the outcome and its window sit right after "after", 30 s is left out.
    const first = await load(script, 'outcomes.json', outcomeFile());
    check('a scenario with outcome triggers loads', first.api.state.refused === null, text(first.api.state.refused));
    const saved = first.api.scenarioText(), trig = body(JSON.parse(saved)).triggers;
    const keys = id => Object.keys(trig.find(t => t.id === id));
    check('an outcome trigger writes "outcome" right after "after"', keys(2).slice(2, 5).join(' ') === 'when after outcome', keys(2).join(' '));
    check('"within" follows "outcome" when it is not 30 s', keys(3).slice(2, 6).join(' ') === 'when after outcome within' && trig.find(t => t.id === 3).within === 45, keys(3).join(' '));
    check('"within" of 30 s, the default, is not written', !keys(4).includes('within') && trig.find(t => t.id === 4).outcome === 'failed', keys(4).join(' '));
    check('the other kind writes neither "outcome" nor "within"', !keys(1).includes('outcome') && !keys(1).includes('within'), keys(1).join(' '));
    const again = await load(script, 'outcomes.json', saved);
    check('the written file reads back and writes the same bytes', again.api.scenarioText() === saved, h.differences(JSON.parse(saved), JSON.parse(again.api.scenarioText()), '', []).join(' / '));
    check('an outcome trigger counts as the "outcomes" capability', again.api.capabilitiesOf(JSON.parse(saved)).includes('outcomes') && !again.api.capabilitiesOf(JSON.parse(outcomeFile().replace(/"outcome"/g, '"after"'))).includes('outcomes'));
  }
  {   // Kinds that are not outcome triggers write nothing new, whatever the page holds for the unused fields.
    const ex = fs.readFileSync(path.join(h.ROOT, 'examples', 'clean_house_acceptance.json'), 'utf8');
    const { api } = await load(script, 'clean_house_acceptance.json', ex);
    const before = api.scenarioText();
    for (const z of api.state.zones) { z.outcome = 'failed'; z.within = 90; }
    check('stray outcome fields on other kinds change nothing in the file', api.scenarioText() === before && !/"outcome"|"within":90/.test(before));
  }

  // ---- every refusal of the contract, with its code and path, the same list the plugin gives
  {
    const refusals = (triggers) => openEditor(script).api.scenarioProblems(text({ schema: 'missionlab-plan/1', spawns: [], triggers }))
      .filter(f => f.level === 'error').map(f => f.code + ' ' + f.path);
    const base = { id: 1, when: 'time', delay: 10 };
    const cases = [
      ['no "after"', { id: 2, when: 'outcome', outcome: 'applied' }, ['missing-field triggers[1].after']],
      ['"after" naming no trigger', { id: 2, when: 'outcome', after: 9, outcome: 'applied' }, ['missing-ref triggers[1].after']],
      ['"after": 0', { id: 2, when: 'outcome', after: 0, outcome: 'failed' }, ['missing-ref triggers[1].after']],
      ['"after" naming itself', { id: 2, when: 'outcome', after: 2, outcome: 'failed' }, ['self-ref triggers[1].after']],
      ['an unknown outcome word', { id: 2, when: 'outcome', after: 1, outcome: 'seen' }, ['unknown-value triggers[1].outcome']],
      ['no "outcome"', { id: 2, when: 'outcome', after: 1 }, ['missing-field triggers[1].outcome']],
      ['"within" of 0', { id: 2, when: 'outcome', after: 1, outcome: 'timeout', within: 0 }, ['out-of-range triggers[1].within']],
      ['"within" above 3600', { id: 2, when: 'outcome', after: 1, outcome: 'timeout', within: 3601 }, ['out-of-range triggers[1].within']],
      ['wrong types', { id: 2, when: 'outcome', after: 1, outcome: 5, within: 'soon' }, ['wrong-type triggers[1].within', 'wrong-type triggers[1].outcome']],
      ['"after" as text', { id: 2, when: 'outcome', after: '1', outcome: 'applied' }, ['wrong-type triggers[1].after']],
    ];
    for (const [what, t, want] of cases) {
      const got = refusals([base, t]);
      check('an outcome trigger with ' + what + ' is refused as ' + want.join(', '), text(got) === text(want), text(got));
    }
    check('a valid outcome trigger at the edges of its window is accepted', refusals([base, { id: 2, when: 'outcome', after: 1, outcome: 'timeout', within: 1 }, { id: 3, when: 'outcome', after: 1, outcome: 'partial', within: 3600 }]).length === 0);
  }

  // ---- the mission check: a watcher that can never hear "applied"
  const fresh = (watched, more) => {
    const page = openEditor(script), api = page.api, s = api.state;
    s.map = api.makeMap(new Float32Array([-60, 0, -60, 60, 0, -60, 60, 0, 60, -60, 0, -60, 60, 0, 60, -60, 0, 60]), 'outcome test');
    Object.assign(s, { example: false, title: 'Outcome check', infils: [], authored: [], objectives: [], things: [], doors: [], extractionAfter: 0, selected: -1, squad: [], enemies: [] });
    s.zones = [api.newTrigger(1, 'time', Object.assign({ delay: 10 }, watched)), api.newTrigger(2, 'outcome', Object.assign({ after: 1, outcome: 'applied', message: 'Seen' }, more || {}))];
    api.changed();
    return { page, api, s };
  };
  const blind = api => api.missionCheck().filter(f => f.about && f.about.why === 'blind');
  {
    const { api, s } = fresh({ message: 'Reinforcements are coming' });
    s.extractionAfter = 2; api.changed();
    const found = blind(api);
    check('the extraction waiting for an "applied" watcher of a message-only trigger is a problem', found.length === 1 && found[0].level === 'problem' && found[0].about.extraction === true && text(found[0].about.triggers) === '[2]', text(found));
    check('and the extraction is said never to open', api.missionCheck().some(f => f.level === 'problem' && f.about && f.about.extraction && f.about.why === 'never'));
    s.extractionAfter = 0; api.changed();
    const loose = blind(api);
    check('the same watcher with nothing hanging on it is only a note', loose.length === 1 && loose[0].level === 'note' && !loose[0].about.extraction, text(loose));
    check('a note is not a problem about that trigger', !api.missionCheck().some(f => f.level === 'problem' && f.about && (f.about.triggers || []).includes(2)));
    s.enemies = [api.newEnemy([0, 0, 0], 'Guard', 0)]; s.enemies[0].trigger = 1; s.zones[0].message = ''; api.changed();
    check('a watched trigger that brings an enemy can be seen to take: nothing is said', blind(api).length === 0, text(blind(api)));
    s.zones[0].message = 'Reinforcements are coming'; api.changed();
    check('enemies with a message are never seen in full: "applied" can never come', blind(api).length === 1, text(blind(api)));
    s.zones[0].repeat = 30; api.changed();
    check('unless the trigger repeats with nothing beyond its enemies (a repeat leaves the message out)', blind(api).length === 0, text(blind(api)));
    s.zones[0].power = 'off'; api.changed();
    check('a repeating trigger that also switches the lights is never seen in full again', blind(api).length === 1, text(blind(api)));
  }
  {
    const { api } = fresh({ message: 'Reinforcements are coming' }, { outcome: 'failed' });
    check('a "failed" watcher of a message-only trigger is not judged blind', blind(api).length === 0, text(blind(api)));
  }
  {
    const { api } = fresh({ message: 'x' }, { outcome: 'partial' });
    check('a "partial" watcher of a trigger with fewer than two enemies is blind', blind(api).length === 1);
  }
  {
    const page = openEditor(script), api = page.api;
    const rings = api.checkRings([api.newTrigger(1, 'outcome', { after: 2 }), api.newTrigger(2, 'outcome', { after: 1, outcome: 'failed' }), api.newTrigger(3, 'time')]);
    check('two outcome triggers watching each other are a circle', text(rings) === '[[1,2]]', text(rings));
  }

  // ---- one history entry for adding and for each edit; undo restores
  {
    const { api, s } = fresh({ message: '' }, { outcome: 'failed' });
    s.enemies = [Object.assign(api.newEnemy([0, 0, 0], 'Guard', 0), { trigger: 1 })]; api.changed();
    const done = () => api.historyList().done.length, start = api.scenarioText(), n = done();
    const id = api.trigAdd('outcome');
    const added = s.zones.find(z => z.id === id);
    check('adding an outcome trigger is one history entry', done() === n + 1 && !!added && added.when === 'outcome' && added.after === 2 && added.outcome === 'applied' && added.within === 30, done() - n + ' ' + text(added));
    const withNew = api.scenarioText();
    check('outcomeSet: the watched trigger, the word and the window, one entry each', api.outcomeSet(id, 'after', 1) && api.outcomeSet(id, 'outcome', 'timeout') && api.outcomeSet(id, 'within', 75) && done() === n + 4, String(done() - n));
    const t = body(JSON.parse(api.scenarioText())).triggers.find(q => q.id === id);
    check('the edits reach the file', t.after === 1 && t.outcome === 'timeout' && t.within === 75, text(t));
    check('a word the file would refuse is not taken and leaves no entry', !api.outcomeSet(id, 'outcome', 'seen') && !api.outcomeSet(id, 'after', id) && !api.outcomeSet(id, 'after', 42) && done() === n + 4);
    api.undo();
    check('undo puts the window back', s.zones.find(z => z.id === id).within === 30 && s.zones.find(z => z.id === id).outcome === 'timeout');
    api.undo(); api.undo();
    check('two more undos give back the trigger as it was added', api.scenarioText() === withNew, h.differences(JSON.parse(withNew), JSON.parse(api.scenarioText()), '', []).join(' / '));
    api.undo();
    check('one more undo removes it', api.scenarioText() === start && !s.zones.some(z => z.id === id));
  }

  // ---- the graph and the inspector
  {
    const { api } = await load(script, 'outcomes.json', outcomeFile());
    const model = api.graphModel(), edges = model.edges.map(e => e.from + '>' + e.to + ' ' + e.kind + (e.word ? ' ' + e.word : ''));
    check('each outcome trigger has an outcome line from the trigger it watches, labelled with its outcome', ['t:1>t:2 outcome applied', 't:1>t:3 outcome timeout', 't:1>t:4 outcome failed'].every(e => edges.includes(e)), text(edges));
    check('an outcome trigger has no after line and is not armed from the start', !edges.some(e => /t:[234] (after|start|hand)/.test(e)), text(edges));
    check('outcome lines are a style of their own, not after, all or any', model.edges.filter(e => e.to === 't:2').every(e => e.kind === 'outcome'));
    const live = api.live, ex = (phase, more) => Object.assign({ phase, left: 0, why: '', fires: 0, times: 0, waiting: [], excluded: [], inside: -1, alive: -1, outcome: '', outcomeWhy: '', invocation: 0, late: '', pending: [] }, more);
    live.on = true; live.mission = true;
    live.trig = new Map([
      [1, { id: 1, phase: 'fired', explain: ex('fired', { fires: 2, invocation: 2, outcome: 'failed', outcomeWhy: 'no guard was made', late: 'firing 1 reported applied after the timeout' }) }],
      [2, { id: 2, phase: 'waiting', explain: ex('waiting') }],
      [3, { id: 3, phase: 'fired', explain: ex('fired', { fires: 1, invocation: 1 }) }],
      [4, { id: 4, phase: 'waiting', explain: ex('waiting') }],
    ]);
    const rows = api.inspectRows({ triggerState: [...live.trig.values()] }), row = id => rows.find(r => r.id === id);
    check('the inspector names the firing its outcome belongs to', row(1).invocation === 2 && row(1).outcome === 'failed' && row(1).outcomeText.startsWith('Firing 2: Action failed'), text(row(1)));
    check('the inspector keeps the late result apart from the current outcome', row(1).late === 'firing 1 reported applied after the timeout' && !row(1).outcomeText.includes('after the timeout'), text(row(1)));
    check('an outcome trigger says which firing it waits on', row(2).watch === 'waits on firing 2 of trigger 1', row(2).watch);
    const n1 = api.graphModel().nodes.find(n => n.key === 't:1'), cls = api.graphNodeClass(n1).split(' ');
    check('the graph shows the late result on the node, apart from the outcome', n1.live.late === row(1).late && cls.includes('l-late') && cls.includes('o-failed') && n1.rows.some(r => r.cls === 'g-late') && n1.rows.some(r => r.cls === 'g-out'), text(cls));
    const n4 = api.graphModel().nodes.find(n => n.key === 't:4');
    check('a trigger with no late result shows none', !n4.live.late && !api.graphNodeClass(n4).split(' ').includes('l-late'));
  }

  // ---- a rehearsal row that reports an outcome
  {
    const { api } = await load(script, 'outcomes.json', outcomeFile());
    const rows = [{ at: 10, kind: 'fire', id: 1 }, { at: 12, kind: 'outcome', id: 1, result: 'applied' }, { at: 20, kind: 'fired', id: 2 }];
    const step = api.rehearseStep(rows[1]);
    check('an outcome row is the contract\'s step shape', text(step) === text({ at: 12, outcome: { trigger: 1, result: 'applied' } }), text(step));
    const written = api.rehearseText(api.rehearseScript(rows, { players: 2 }));
    check('the script writes it in place', written.includes('\n{"at":12,"outcome":{"trigger":1,"result":"applied"}}'), written);
    check('the script reads back to the same rows', text(api.rehearseRows(JSON.parse(written))) === text(rows), text(api.rehearseRows(JSON.parse(written))));
    check('a timeout is never reported by a row: it is judged on the clock', api.rehearseStep({ at: 5, kind: 'outcome', id: 1, result: 'timeout' }) === null);
    const ids = api.state.zones.map(z => z.id);
    check('a row without an outcome, or for a trigger that is not there, is a problem', api.rehearseProblems([{ at: 1, kind: 'outcome', id: 1, result: '' }], ids).length === 1 && api.rehearseProblems([{ at: 1, kind: 'outcome', id: 9, result: 'failed' }], ids).length === 1);
  }

  // ---- the debrief counts outcome triggers, as MissionPlan.FromJson keeps them (they have no place on the map)
  {
    const api = openEditor(script).api;
    const plan = api.debriefPlan({ spawns: [{ pos: [0, 0, 0], trigger: 1 }], triggers: [{ id: 1, name: 'Reinforcements', when: 'time', delay: 5 }, { id: 2, name: 'Fallback', when: 'outcome', after: 1, outcome: 'failed' }] });
    check('the debrief keeps an outcome trigger, so "Triggers fired: x of N" counts it', !!plan && text(plan.triggers.map(t => t.id + ' ' + t.name)) === text(['1 Reinforcements', '2 Fallback']), text(plan && plan.triggers));
  }

  // ---- modules carry the watched trigger
  {
    const { api, s } = fresh({ message: 'Ten seconds' }, { outcome: 'failed', message: 'Fallback' });
    s.enemies = [Object.assign(api.newEnemy([10, 0, 10], 'Guard', 0), { trigger: 1 }), Object.assign(api.newEnemy([12, 0, 12], 'Guard', 0), { trigger: 2 })];
    s.squad = [0, 1]; s.selected = 0; api.changed();
    const m = api.moduleOfSelection('Fallback pair').module;
    check('a module of a watcher and the trigger it watches keeps the link inside it', !!m && m.triggers.length === 2 && m.bindings.length === 0 && m.triggers[1].when === 'outcome' && m.triggers[1].after === 1 && m.triggers[1].outcome === 'failed', text(m));
    const r = api.moduleInsert(m, [30, 0, 30], 0), copy = s.zones.filter(z => r.triggers.includes(z.id));
    const w = copy.find(z => z.when === 'outcome'), t = copy.find(z => z.when === 'time');
    check('the inserted watcher watches the inserted copy of its trigger under its new number', !r.error && !!w && !!t && w.after === t.id && t.id !== 1 && w.outcome === 'failed', text(copy.map(z => [z.id, z.when, z.after])));
    s.squad = []; s.selected = 1;
    const out = api.moduleOfSelection('Fallback only').module;
    check('a watcher whose trigger brings enemies outside the selection binds that trigger', !!out && out.triggers.length === 1 && out.triggers[0].when === 'outcome' && out.triggers[0].after === '@t1' && out.bindings.length === 1 && out.bindings[0].kind === 'trigger', text(out));
    check('such a module has no problems', api.moduleProblems(out).length === 0, api.moduleProblems(out).join(' / '));
    const spoilt = (key, value) => api.moduleProblems(Object.assign({}, out, { triggers: [Object.assign({}, out.triggers[0], { [key]: value })] }));
    check('a module whose watcher of an outside trigger has an unknown outcome word is refused', spoilt('outcome', 'seen').some(p => p.startsWith('triggers[0].outcome')), spoilt('outcome', 'seen').join(' / '));
    check('and one whose window is out of range', spoilt('within', 0).some(p => p.startsWith('triggers[0].within')) && spoilt('within', 4000).some(p => p.startsWith('triggers[0].within')), spoilt('within', 0).join(' / '));
    const r2 = api.moduleInsert(out, [40, 0, 40], 0), open = api.moduleOpen().filter(b => b.copy === r2.copy);
    check('inserting it asks for the watched trigger', !r2.error && open.length === 1 && open[0].kind === 'trigger', text(open));
    api.moduleBind(open[0].key, 1);
    const bound = s.zones.find(z => z.id === r2.triggers[0]);
    check('binding it makes the copy watch that trigger, still as an outcome trigger', bound.when === 'outcome' && bound.after === 1 && bound.outcome === 'failed', text(bound));
  }

  // ---- the renumber repair follows a watcher's "after"
  {
    const { api, s } = fresh({ message: 'x' }, { outcome: 'failed', message: 'Fallback' });
    s.zones = [api.newTrigger(5, 'outcome', { after: 5, outcome: 'failed', message: 'Fallback' }), api.newTrigger(5, 'time', { delay: 10, message: 'Ten seconds' })]; api.changed();
    const f = api.missionCheck().find(x => x.repair && x.repair.kind === 'renumber');
    check('two triggers with one number offer the renumber repair', !!f);
    const plan = api.previewRepair(f);
    check('the repair renumbers the second and makes the watcher follow it', text(plan.edits.map(e => e.what + ' ' + e.zi + ' ' + text(e.from) + '>' + text(e.to))) === text(['id 1 5>6', 'after 0 5>6']), text(plan.edits));
    check('applied, the watcher watches the renumbered trigger', api.applyRepair() === true && s.zones[1].id === 6 && s.zones[0].after === 6 && s.zones[0].when === 'outcome', text(s.zones.map(z => [z.id, z.after])));
    s.zones = [api.newTrigger(5, 'time', { delay: 10, message: 'a' }), api.newTrigger(5, 'time', { delay: 20, message: 'b' }), api.newTrigger(7, 'outcome', { after: 5, outcome: 'applied' })]; api.changed();
    const blocked = api.repairPlan({ kind: 'renumber', id: 5 });
    check('a watcher outside the pair makes the number ambiguous, so the repair changes nothing', !(blocked.edits && blocked.edits.length) && /waits for number 5/.test(blocked.why), blocked.why);
  }
}
