// ML-055: a zone or hold that waits for another trigger before it watches its area. The editor writes "after" on a zone or
// hold at the place the after kind writes it, and the missionlab-plan/4 envelope only when one has a gate; all_fields_v4
// reads and writes back byte for byte; the strict reader refuses a gate in an older file, on itself or on no trigger; the
// panel sets and clears the gate in one undo step; deleting the gate trigger clears it; the mission check, the circles and
// the graph treat the gate as a link like an after trigger's.
import fs from 'node:fs';
import path from 'node:path';

export default async function (h) {
  const { script, openEditor, load, check } = h;
  const text = JSON.stringify;
  const body = obj => obj.mission || obj;
  const fixture = name => fs.readFileSync(path.join(h.FIXTURES, name), 'utf8');

  // A fresh page with an alert (1), a zone (2) and a hold (3) on a small map.
  const fresh = () => {
    const page = openEditor(script), api = page.api, s = api.state;
    s.map = api.makeMap(new Float32Array([-60, 0, -60, 60, 0, -60, 60, 0, 60, -60, 0, -60, 60, 0, 60, -60, 0, 60]), 'gate test');
    Object.assign(s, { example: false, title: 'Gate check', infils: [], authored: [], objectives: [], things: [], doors: [], extractionAfter: 0, selected: -1, squad: [], enemies: [] });
    s.zones = [api.newTrigger(1, 'alert'), api.newTrigger(2, 'zone', { pos: [5, 0, 5], radius: 6 }), api.newTrigger(3, 'zone', { pos: [20, 0, 20], radius: 6, hold: 30 })];
    s.enemies = [Object.assign(api.newEnemy([0, 0, 0], 'Guard', 0), { trigger: 2 }), Object.assign(api.newEnemy([1, 0, 1], 'Guard', 0), { trigger: 3 })];
    api.changed();
    return { page, api, s };
  };

  // ---- the writer: plan/4 only with a gate, "after" where the after kind writes it
  {
    const { api, s } = fresh();
    check('without a gate the plan is not plan/4', JSON.parse(api.scenarioText()).schema !== 'missionlab-plan/4' && !/"after"/.test(api.scenarioText()));
    s.zones[1].after = 1; s.zones[2].after = 1; api.changed();
    const saved = JSON.parse(api.scenarioText()), trig = body(saved).triggers, keys = id => Object.keys(trig.find(t => t.id === id)).join(' ');
    check('a zone with a gate is written as missionlab-plan/4', saved.schema === 'missionlab-plan/4' && !!saved.mission, saved.schema);
    check('a zone writes "after" where the after kind writes it', keys(2) === 'id name when pos radius after facePlayer', keys(2));
    check('a hold writes "after" after its hold time', keys(3) === 'id name when pos radius hold after facePlayer', keys(3));
    check('a gated zone counts as the "zone-after" capability', api.capabilitiesOf(saved).includes('zone-after'));
    s.zones[1].after = 0; s.zones[2].after = 0; api.changed();
    check('with the gates cleared the capability is gone', !api.capabilitiesOf(JSON.parse(api.scenarioText())).includes('zone-after'));
  }

  // ---- all_fields_v4 reads and writes back byte for byte
  {
    const first = await load(script, 'all_fields_v4.json', fixture('all_fields_v4.json'));
    check('all_fields_v4.json loads without a finding', first.api.state.refused === null, text(first.api.state.refused));
    check('all_fields_v4.json passes the strict reader', first.api.scenarioProblems(fixture('all_fields_v4.json')).filter(f => f.level === 'error').length === 0);
    const saved = first.api.scenarioText(), o = JSON.parse(saved), trig = body(o).triggers;
    check('it is written as plan/4 with both gates', o.schema === 'missionlab-plan/4' && trig.find(t => t.id === 3).after === 1 && trig.find(t => t.id === 5).after === 6, saved.slice(0, 200));
    const again = await load(script, 'again.json', saved);
    check('the written file reads back and writes the same bytes', again.api.scenarioText() === saved, h.differences(o, JSON.parse(again.api.scenarioText()), '', []).join(' / '));
    check('all_fields_v4 lists zone-after', again.api.capabilitiesOf(o).includes('zone-after'));
  }

  // ---- the strict reader's refusals and the lenient reader's drop
  {
    const api = openEditor(script).api;
    const refusals = (schema, triggers) => api.scenarioProblems(text(schema === 'missionlab-plan/1' ? { schema, spawns: [], triggers } : { schema, mission: { spawns: [], triggers } }))
      .filter(f => f.level === 'error').map(f => f.code + ' ' + f.path);
    const alarm = { id: 1, when: 'alert' };
    check('a gate in a plan/3 file is unknown-format', text(refusals('missionlab-plan/3', [alarm, { id: 2, when: 'zone', pos: [0, 0, 0], after: 1 }])) === text(['unknown-format triggers[1].after']));
    check('a gate on itself is self-ref', text(refusals('missionlab-plan/4', [alarm, { id: 2, when: 'hold', pos: [0, 0, 0], hold: 5, after: 2 }])) === text(['self-ref triggers[1].after']));
    check('a gate on no trigger is missing-ref', text(refusals('missionlab-plan/4', [alarm, { id: 2, when: 'zone', pos: [0, 0, 0], after: 9 }])) === text(['missing-ref triggers[1].after']));
    check('a gate of 0 is no gate, in any version', refusals('missionlab-plan/1', [alarm, { id: 2, when: 'zone', pos: [0, 0, 0], after: 0 }]).length === 0);
    const words = api.scenarioProblems(text({ schema: 'missionlab-plan/3', mission: { spawns: [], triggers: [alarm, { id: 2, when: 'zone', pos: [0, 0, 0], after: 1 }] } })).find(f => f.code === 'unknown-format');
    check('the plan/3 refusal says what to do', !!words && words.text === 'a zone or hold that waits for another trigger needs missionlab-plan/4: save the file again with this MissionLab', text(words));
    check('the editor reads missionlab-plan/4 and refuses /5', api.scenarioProblems(text({ schema: 'missionlab-plan/5', mission: { spawns: [] } })).some(f => f.code === 'future-version'));
    const zones = api.zonesIn([{ id: 1, when: 'zone', pos: [0, 0, 0], after: 1 }, { id: 2, when: 'hold', hold: 5, pos: [0, 0, 0], after: 9 }, { id: 3, when: 'zone', pos: [0, 0, 0], after: 1 }]);
    check('the lenient reader drops a gate on itself or on no trigger and keeps a good one', text(zones.map(z => z.after)) === '[0,0,1]', text(zones.map(z => z.after)));
  }

  // ---- the panel's set path: one undo step, refuses itself and unknown triggers; deleting the gate clears it
  {
    const { api, s } = fresh();
    const done = () => api.historyList().done.length, n = done(), start = api.scenarioText();
    check('setting a zone\'s gate is one history entry', api.outcomeSet(2, 'after', 1) && s.zones[1].after === 1 && done() === n + 1, String(done() - n));
    check('a gate on itself or on no trigger is not taken and leaves no entry', !api.outcomeSet(2, 'after', 2) && !api.outcomeSet(2, 'after', 42) && done() === n + 1 && s.zones[1].after === 1);
    check('clearing it is one more entry', api.outcomeSet(2, 'after', 0) && s.zones[1].after === 0 && done() === n + 2);
    api.undo(); api.undo();
    check('two undos give back the zone without a gate', api.scenarioText() === start && s.zones[1].after === 0);
    check('other kinds still take no gate through the set path', !api.outcomeSet(1, 'after', 2));
    api.outcomeSet(3, 'after', 1);
    const before = done();
    check('deleting the gate trigger is one history entry', api.trigDelete(0) && done() === before + 1);
    check('and the hold that waited for it loses its gate', s.zones.find(z => z.id === 3).after === 0 && !/"after"/.test(api.scenarioText()));
    api.undo();
    check('undo gives back the trigger and the gate', s.zones.some(z => z.id === 1) && s.zones.find(z => z.id === 3).after === 1);
  }

  // ---- the mission check, the circles and the graph
  {
    const { api, s } = fresh();
    s.zones.push(api.newTrigger(4, 'after', { after: 0 }));      // by hand only: never fires by itself
    s.zones[1].after = 4; api.changed();
    const never = api.missionCheck().find(f => f.level === 'problem' && f.about && (f.about.triggers || []).includes(2) && f.about.why === 'never');
    check('a zone gated on a trigger that can never fire can never fire', !!never && /can never fire/.test(never.text), text(api.missionCheck().map(f => f.text)));
    s.zones[1].after = 3; s.zones[2].after = 2; api.changed();
    const rings = api.checkRings(s.zones);
    check('two zones gated on each other are a circle', text(rings) === '[[2,3]]', text(rings));
    check('the mission check says so', api.missionCheck().some(f => f.about && f.about.why === 'circle' && text(f.about.triggers) === '[2,3]'));
    s.zones[2].after = 0; api.changed();
    const edges = api.graphModel().edges;
    const gate = edges.find(e => e.from === 't:3' && e.to === 't:2');
    check('the graph draws an after line from the gate to the zone', !!gate && gate.kind === 'after' && gate.gate === true, text(edges));
    check('a gated zone is not armed from the start', !edges.some(e => e.from === 'start' && e.to === 't:2'));
    check('the inspector says what the zone waits for', /^Waits for trigger 3 before it watches its area/.test(api.inspectStatic(s.zones[1]).text), api.inspectStatic(s.zones[1]).text);
  }
}
