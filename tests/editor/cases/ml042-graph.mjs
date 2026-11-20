// ML-042: the mission graph shows the scenario and nothing else. Its lines and what each box brings are checked against
// the files' own trigger lists; its marks against what the mission check finds (a circle, a trigger that is gone, an
// extraction that can never open); its layout is the same for every order of the list and never reaches the file; live
// history, phase and outcome stay apart; a rehearsal shows only for the scenario it was run on; a sealed view shows nothing.
import fs from 'node:fs';
import path from 'node:path';

export default async function (h) {
  const text = v => JSON.stringify(v);
  const example = name => fs.readFileSync(path.join(h.ROOT, 'examples', name), 'utf8');
  const fixture = name => fs.readFileSync(path.join(h.FIXTURES, name), 'utf8');
  const body = obj => obj.mission || obj;
  // What the graph must hold, worked out from the file alone.
  const expected = obj => {
    const b = body(obj), list = b.triggers || [], ids = new Set(list.map(t => t.id)), key = id => (ids.has(id) ? 't:' + id : 'gone:' + id);
    const edges = [], nodes = ['start', ...list.map(t => 't:' + t.id)], effects = {}, timings = {};
    for (const t of list) {
      if (t.when === 'after') edges.push(t.after ? key(t.after) + '>t:' + t.id + ' after' : 'start>t:' + t.id + ' hand');
      else if (t.when === 'all' || t.when === 'any') { for (const o of t.of || []) if (o !== t.id) edges.push(key(o) + '>t:' + t.id + ' ' + t.when); }
      else edges.push('start>t:' + t.id + ' start');
      if (t.extraction === 'open') edges.push('t:' + t.id + '>extraction opens');
      if (t.end) edges.push('t:' + t.id + '>end:' + t.end + ' end');
      const n = (b.spawns || []).filter(s => s.trigger === t.id).length, th = (b.things || []).filter(s => s.trigger === t.id).length;
      const doors = (t.doors || []).filter(d => typeof d.locked === 'boolean' || typeof d.open === 'boolean').length;
      effects['t:' + t.id] = [n ? 'enemies:' + n : '', th ? 'things:' + th : '', doors ? 'doors:' + doors : '', t.power ? 'power' : '', t.message ? 'message' : '',
        t.respawn ? 'respawn' : '', t.rally > 0 ? 'rally' : '', t.hunt ? 'hunt' : '', t.extraction === 'open' ? 'extraction' : '', t.end ? 'end' : ''].filter(Boolean).sort().join(' ');
      timings['t:' + t.id] = [t.delay > 0 && t.when !== 'time' ? 'delay' : '', t.chance < 100 ? 'chance' : '', t.repeat > 0 ? 'repeat' : ''].filter(Boolean).join(' ');
    }
    if (b.extraction && b.extraction.after) edges.push(key(b.extraction.after) + '>extraction holds');
    if ((b.extraction && b.extraction.after) || list.some(t => t.extraction === 'open')) nodes.push('extraction');
    for (const e of ['success', 'failed']) if (list.some(t => t.end === e)) nodes.push('end:' + e);
    return { edges: edges.sort(), nodes: nodes.sort(), effects, timings };
  };
  const got = model => {
    const triggers = model.nodes.filter(n => n.kind === 'trigger'), effects = {}, timings = {};
    for (const n of triggers) {
      effects[n.key] = n.effects.map(e => (e.n ? e.kind + ':' + e.n : e.kind)).sort().join(' ');
      timings[n.key] = n.timings.map(t => t.kind).join(' ');
    }
    return { edges: model.edges.map(e => e.from + '>' + e.to + ' ' + e.kind).sort(), nodes: model.nodes.map(n => n.key).sort(), effects, timings };
  };
  const agree = (name, obj, model) => {
    const want = expected(obj), have = got(model);
    h.check(name + ': the graph has a box for the start, every trigger, the extraction and each ending, and no other', text(have.nodes) === text(want.nodes), text(have.nodes) + ' / wanted ' + text(want.nodes));
    h.check(name + ': every after, all-of, any-of, extraction and ending link of the file is a line, and there is no other', text(have.edges) === text(want.edges),
      have.edges.filter(e => !want.edges.includes(e)).join(', ') + ' extra; ' + want.edges.filter(e => !have.edges.includes(e)).join(', ') + ' missing');
    h.check(name + ': what each trigger brings is what the file binds to it', text(have.effects) === text(want.effects), text(have.effects) + ' / wanted ' + text(want.effects));
    h.check(name + ': the delay, the chance and repeating are shown apart, for the triggers that have them', text(have.timings) === text(want.timings), text(have.timings));
  };
  const flagged = (model, why) => model.nodes.filter(n => n.flags.includes(why)).map(n => n.key).sort();
  const spots = (api, model, dir) => { const l = api.graphLayout(model, dir); return text([...l.at.entries()].map(([k, b]) => [k, b.x, b.y, b.layer]).sort((a, b) => (a[0] < b[0] ? -1 : 1))); };

  // ---- the shipped examples, against their own trigger lists
  const acceptanceText = example('clean_house_acceptance.json'), acceptance = JSON.parse(acceptanceText);
  {
    const { api } = await h.load(h.script, 'clean_house_acceptance.json', acceptanceText);
    const model = api.graphModel();
    agree('clean_house_acceptance', acceptance, model);
    h.check('clean_house_acceptance: a scenario the mission check passes has no marked box', model.nodes.every(n => n.flags.length === 0), text(model.nodes.filter(n => n.flags.length).map(n => n.key + ' ' + n.flags)));
  }
  {
    const raw = example('clean_house_two_man_job.json'), obj = JSON.parse(raw);
    const { api } = await h.load(h.script, 'clean_house_two_man_job.json', raw);
    const sealed = api.graphModel();
    h.check('a sealed scenario, not revealed, gives no trigger box and no line', sealed.sealed && !sealed.nodes.some(n => n.kind === 'trigger') && sealed.edges.length === 0, text(sealed.nodes.map(n => n.key)));
    const before = api.state.selected;
    api.graphPick(1);
    h.check('a sealed scenario: picking a trigger does nothing and says nothing about it', api.state.inspectSel !== 1 && api.state.selected === before && api.graphDetail() === '');
    api.sealedReveal();
    agree('clean_house_two_man_job, revealed', obj, api.graphModel());
  }

  // ---- marks from the mission check: the circle and the extraction behind it, in every order of the list
  {
    const cycle = JSON.parse(fixture('any_cycle_extraction.json'));
    const orders = list => (list.length <= 1 ? [list.slice()] : list.flatMap((x, i) => orders(list.filter((_, j) => j !== i)).map(rest => [x, ...rest])));
    const layouts = [];
    for (const o of orders(cycle.triggers)) {
      const { api } = await h.load(h.script, 'any_cycle_extraction.json', text(Object.assign({}, cycle, { triggers: o })));
      const model = api.graphModel(), exit = model.nodes.find(n => n.key === 'extraction'), order = o.map(t => t.id).join('');
      h.check('any_cycle_extraction (' + order + '): exactly triggers 1 and 2 are marked as a circle', text(flagged(model, 'circle')) === '["t:1","t:2"]', text(flagged(model, 'circle')));
      h.check('any_cycle_extraction (' + order + '): the extraction is marked as never opening', !!exit && exit.flags.includes('never'), exit ? text(exit.flags) : 'no extraction box');
      h.check('any_cycle_extraction (' + order + '): the timer outside the circle is not marked', model.nodes.find(n => n.key === 't:3').flags.length === 0);
      layouts.push(spots(api, model, 'down') + spots(api, model, 'across'));
    }
    h.check('any_cycle_extraction: every order of the trigger list gives the same layout', layouts.length === 6 && layouts.every(l => l === layouts[0]));
  }

  // ---- a reference to a trigger that is gone: that trigger is marked, and the line comes from a box for the missing number
  {
    // From the file: the "after" trigger the extraction is held behind, the first all-of trigger, and two numbers no trigger has.
    const b = body(acceptance), gate = b.triggers.find(t => b.extraction && t.id === b.extraction.after && t.when === 'after'), listing = b.triggers.find(t => t.when === 'all');
    const top = Math.max(...b.triggers.map(t => t.id)), goneList = top + 1, goneAfter = top + 31;
    h.check('clean_house_acceptance still holds its extraction behind an "after" trigger and has an all-of trigger (what this case breaks)', !!gate && !!listing && gate.id !== listing.id && !b.triggers.some(t => t.extraction === 'open'));
    if (gate && listing) {
      const { api } = await h.load(h.script, 'clean_house_acceptance.json', acceptanceText);
      api.state.zones.find(z => z.id === listing.id).of.push(goneList);
      api.state.zones.find(z => z.id === gate.id).after = goneAfter;
      const model = api.graphModel(), edges = model.edges.map(e => e.from + '>' + e.to + ' ' + e.kind);
      h.check('a missing reference marks exactly the triggers that make it', text(flagged(model, 'missing')) === text(['t:' + gate.id, 't:' + listing.id].sort()), text(flagged(model, 'missing')));
      h.check('a missing reference is drawn from a box for the missing number', edges.includes('gone:' + goneList + '>t:' + listing.id + ' all') && edges.includes('gone:' + goneAfter + '>t:' + gate.id + ' after')
        && model.nodes.some(n => n.key === 'gone:' + goneList) && model.nodes.some(n => n.key === 'gone:' + goneAfter), text(edges));
      h.check('the trigger the extraction waited for no longer has a line from where it waited before', !edges.includes('t:' + gate.after + '>t:' + gate.id + ' after'), text(edges));
      const exit = model.nodes.find(n => n.key === 'extraction');
      h.check('the extraction held behind the broken trigger is marked as never opening', !!exit && exit.flags.includes('never'), exit ? text(exit.flags) : '');
    }
  }

  // ---- the layout: by depth, ties by number, the same for every order, and never in the file
  {
    const { api } = await h.load(h.script, 'clean_house_acceptance.json', acceptanceText);
    const saved = api.scenarioText(), model = api.graphModel(), down = api.graphLayout(model, 'down');
    const byKey = new Map(model.nodes.map(n => [n.key, n]));
    h.check('every line runs from a shallower layer to a deeper one', model.edges.every(e => down.at.get(e.from).layer < down.at.get(e.to).layer),
      text(model.edges.filter(e => down.at.get(e.from).layer >= down.at.get(e.to).layer)));
    const layers = new Map();
    for (const [k, b] of down.at) if (byKey.get(k).kind === 'trigger') { if (!layers.has(b.layer)) layers.set(b.layer, []); layers.get(b.layer).push([b.x, byKey.get(k).id]); }
    h.check('within a layer, triggers stand in number order', [...layers.values()].every(l => l.sort((a, b) => a[0] - b[0]).every((p, i) => i === 0 || l[i - 1][1] < p[1])), text([...layers.values()]));
    const list = body(acceptance).triggers.slice().reverse();
    const reversed = acceptance.mission ? Object.assign({}, acceptance, { mission: Object.assign({}, acceptance.mission, { triggers: list }) }) : Object.assign({}, acceptance, { triggers: list });
    const other = (await h.load(h.script, 'reversed.json', text(reversed))).api;
    h.check('the same scenario with its trigger list reversed is laid out the same', spots(other, other.graphModel(), 'down') === spots(api, model, 'down'));
    api.graphSetDir('across');
    const across = api.graphLayout(api.graphModel(), api.state.graph.dir);
    h.check('switching the layout moves the boxes', spots(api, model, 'down') !== spots(api, model, 'across') && [...across.at.keys()].every(k => across.at.get(k).layer === down.at.get(k).layer));
    h.check('switching the layout leaves the scenario text exactly as it was', api.scenarioText() === saved);
    h.check('switching the layout leaves every line as it was', text(got(api.graphModel()).edges) === text(got(model).edges));
    api.graphPick(1);
    h.check('picking a trigger in the graph leaves the scenario text exactly as it was', api.scenarioText() === saved);
  }

  // ---- picking: the inspector's selection, and the trigger's enemies on the map
  {
    const { api } = await h.load(h.script, 'clean_house_acceptance.json', acceptanceText);
    const s = api.state, bound = s.enemies.map((e, i) => (e.trigger === 1 ? i : -1)).filter(i => i >= 0), zone = s.zones.find(z => z.id === 1);
    api.graphPick(1);
    h.check('in Create, picking a trigger opens it for the inspector and selects every enemy it brings', s.inspectSel === 1 && s.selected === bound[0] && text(s.squad) === text(bound.length > 1 ? bound : []), s.inspectSel + ' ' + s.selected + ' ' + text(s.squad) + ' / ' + text(bound));
    h.check('in Create, picking a zone trigger turns the map to its zone', s.view.cx === zone.pos[0] && s.view.cz === zone.pos[2]);
    h.check('the picked trigger\'s box is the selected one', api.graphNodeClass(api.graphModel().nodes.find(n => n.key === 't:1')).split(' ').includes('sel')
      && !api.graphNodeClass(api.graphModel().nodes.find(n => n.key === 't:2')).split(' ').includes('sel'));
    api.graphPick(1);
    h.check('picking it again lets it go', s.inspectSel === 0);
    api.setView('direct');
    const was = s.selected;
    api.graphPick(3);
    h.check('in Direct, picking a trigger selects it in the inspector and leaves the order selection alone', s.inspectSel === 3 && s.selected === was);
  }

  // ---- live: fired so far, the phase now and the outcome of its actions stay apart
  {
    const { api } = await h.load(h.script, 'clean_house_acceptance.json', acceptanceText);
    const live = api.live, ex = (phase, more) => Object.assign({ phase, left: 0, why: '', fires: 0, times: 0, waiting: [], excluded: [], inside: -1, alive: -1, outcome: '', outcomeWhy: '' }, more);
    live.on = true; live.mission = true;
    live.trig = new Map([
      [1, { id: 1, phase: 'waiting', explain: ex('waiting') }],
      [2, { id: 2, phase: 'cooling', left: 40, explain: ex('cooling', { left: 40, fires: 1, times: 3, outcome: 'applied' }) }],
      [3, { id: 3, phase: 'fired', explain: ex('fired', { fires: 1, outcome: 'failed', outcomeWhy: 'the door did not move' }) }],
      [5, { id: 5, phase: 'counting', left: 120, explain: ex('counting', { left: 120 }) }],
    ]);
    const model = api.graphModel(), cls = key => api.graphNodeClass(model.nodes.find(n => n.key === key)).split(' ');
    const has = (key, ...want) => want.every(c => cls(key).includes(c)), lacks = (key, prefix) => !cls(key).some(c => c.startsWith(prefix));
    h.check('live: a trigger cooling down after firing shows both its history and its phase', has('t:2', 'h-fired', 'p-cooling', 'o-applied'), text(cls('t:2')));
    h.check('live: a fired trigger whose action failed shows the failure apart from having fired', has('t:3', 'h-fired', 'p-fired', 'o-failed'), text(cls('t:3')));
    h.check('live: a trigger counting down has no history and no outcome yet', has('t:5', 'p-counting') && lacks('t:5', 'h-') && lacks('t:5', 'o-'), text(cls('t:5')));
    h.check('live: a waiting trigger that never fired looks like neither', has('t:1', 'p-waiting') && lacks('t:1', 'h-') && lacks('t:1', 'o-'), text(cls('t:1')));
    const n2 = model.nodes.find(n => n.key === 't:2'), n3 = model.nodes.find(n => n.key === 't:3');
    h.check('live: the count and the time left are the game\'s', n2.live.fires === 1 && n2.live.clock === '40 s' && n3.live.outcome === 'failed', text([n2.live, n3.live]));
    h.check('live: the four states look different', new Set(['t:1', 't:2', 't:3', 't:5'].map(k => cls(k).filter(c => /^[hpo]-/.test(c)).sort().join(' '))).size === 4);
  }

  // ---- the last rehearsal, only for the scenario text it was run on
  {
    const { api } = await h.load(h.script, 'clean_house_acceptance.json', acceptanceText);
    // The steps name their trigger in the field; the words of step 3 deliberately name another number, so a page that
    // read the number back out of the words would mark the wrong box.
    const answer = hash => ({ passed: false, refused: '', scenarioHash: hash, steps: [
      { at: 5, what: 'step 1 at 5 s: 2 living players stand where the script says', trigger: 0, extraction: false, expected: '', actual: '', ok: true },
      { at: 5, what: "step 2 at 5 s: trigger 3 'Phone taken' has fired", trigger: 3, extraction: false, expected: 'fired', actual: 'fired', ok: true },
      { at: 9, what: "step 3 at 9 s: trigger 1 'Extraction open' has fired", trigger: 4, extraction: false, expected: 'fired', actual: 'not fired: it waits', ok: false },
      { at: 9, what: 'step 3 at 9 s: the extraction is open', trigger: 0, extraction: true, expected: 'open', actual: 'held: it waits for trigger 4', ok: false },
    ] });
    api.state.rehearse.answer = answer(api.textHash(api.scenarioText()));
    const model = api.graphModel(), node = key => model.nodes.find(n => n.key === key);
    h.check('rehearsal: a passed check shows on its trigger', node('t:3').rehearsal && node('t:3').rehearsal.ok === true);
    h.check('rehearsal: a failed check shows on its trigger and on the extraction', node('t:4').rehearsal && node('t:4').rehearsal.ok === false && node('extraction').rehearsal && node('extraction').rehearsal.ok === false);
    h.check('rehearsal: a trigger no check named shows nothing', node('t:1').rehearsal === null);
    const older = answer(api.textHash(api.scenarioText()));
    older.steps.forEach(x => { delete x.trigger; delete x.extraction; });
    api.state.rehearse.answer = older;
    h.check('rehearsal: an answer from an older plugin, without trigger ids, shows nowhere rather than guessing from the words', api.graphModel().nodes.every(n => n.rehearsal === null));
    api.state.rehearse.answer = answer('fnv1a64:0000000000000000');
    h.check('rehearsal: an answer for another version of the scenario shows nowhere', api.graphModel().nodes.every(n => n.rehearsal === null));
  }
}
