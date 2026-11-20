// ML-044: the Intel raid recipe. On a small invented map with two insertions, a painted building, a named room and a
// known intel kind, it makes a mission the strict reader takes, the mission check finds nothing blocking in, and its own
// walkthrough test fits; it never sends enemies to hunt or rally; every part stays editable with one undo per edit, the
// whole recipe is one undo step, and the saved text reopens unchanged. Without known intel, with a hunting module or with
// the insertion inside the building it explains and makes nothing. Also: the editor keeps an enemy's lot at chance 100,
// as the plugin does (the Garrison option of clean_house_two_man_job.json depends on it).
import fs from 'node:fs';
import path from 'node:path';

export default async function (h) {
  const { script, openEditor, load, check, ROOT } = h;
  const text = JSON.stringify;
  const read = rel => fs.readFileSync(path.join(ROOT, rel), 'utf8');
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  const alarmModule = JSON.parse(read('examples/modules/two_guard_alarm.module.json'));
  const huntModule = JSON.parse(read('examples/modules/hold_zone.module.json'));
  // Flat ground 80 x 80 m; the house is the painted square 0..12 x 0..10 on the ground floor; the kitchen a point place in it.
  const fresh = (withIntel) => {
    const page = openEditor(script), api = page.api, s = api.state;
    s.map = api.makeMap(new Float32Array(plate(-40, -40, 40, 40, 0)), 'recipe test');
    Object.assign(s, { example: false, navFile: 'recipe_test.bytes', infils: [], enemies: [], zones: [], things: [], doors: [], options: [], placement: [], extractionAfter: 0, selected: -1, squad: [] });
    s.insertions = ['Alpha 1', 'Bravo'];
    s.insertionSpots = [{ name: 'Alpha 1', pos: [-30, 0, -30] }, { name: 'Inside', pos: [6, 0, 5] }];
    s.authored = [[0, 0, 35], [35, 0, 0], [-35, 0, 35]];
    s.objectives = withIntel ? [{ name: 'Cell Phone_1', label: 'Cell Phone', pos: [5, 0, 5], yaw: 0, obtained: false, home: null, homeYaw: 0, moved: false }] : [];
    s.places = [
      { id: 1, name: 'House', pos: [6, 0, 5], floor: null, area: api.areaRead({ parts: [{ floor: [-0.5, 2.6], outline: [[0, 0], [12, 0], [12, 10], [0, 10]] }] }) },
      { id: 2, name: 'Kitchen', pos: [3, 0, 3], floor: [-0.5, 2.6], area: null },
    ];
    return { api, s, done: () => api.historyList().done.length };
  };
  const choice = more => Object.assign({ insertion: 'Alpha 1', item: 'Cell Phone', building: 1, encounters: [{ module: alarmModule, room: 2, name: alarmModule.name }],
    garrison: 'standard', skill: '', clock: 20, alarm: true, minutes: 15, squad: [2, 4] }, more || {});

  {   // The whole mission, as one step.
    const { api, s, done } = fresh(true), before = api.scenarioText(), steps = done();
    const ready = api.recipeCheck(choice());
    check('with insertions, a painted building and a known intel kind nothing stops the recipe', ready.blockers.length === 0, ready.blockers.join(' | '));
    const r = api.recipeMake(choice());
    check('the recipe makes the mission', r.made === true, text(r.why));
    check('it is one history step', done() === steps + 1 && api.historyList().done[steps] === 'Make the Intel raid', text(api.historyList().done));
    const made = api.scenarioText(), file = JSON.parse(made);
    const errors = api.scenarioProblems(made).filter(f => f.level === 'error');
    check('the strict reader takes it', errors.length === 0, text(errors));
    const blocking = api.missionCheck().filter(f => f.level === 'problem');
    check('the mission check finds nothing blocking', blocking.length === 0, blocking.map(f => f.text).join(' | '));
    check('no trigger hunts or rallies', s.zones.every(z => !z.hunt && !(z.rally > 0)) && !api.capabilitiesOf(file).some(w => w === 'hunt' || w === 'rally'), text(api.capabilitiesOf(file)));

    const byId = id => s.zones.find(z => z.id === id), enter = byId(r.zones.enter), intel = byId(r.zones.intel), alarm = byId(r.zones.alarm), clock = byId(r.zones.clock);
    check('the extraction is held until the intel trigger, which waits for the chosen kind', s.extractionAfter === intel.id && intel.when === 'intel' && intel.item === 'Cell Phone', text([s.extractionAfter, intel]));
    check('walking into the house is a zone with the house\'s own painted shape', enter.when === 'zone' && api.areaSignature(enter.area) === api.areaSignature(s.places[0].area), text(enter));
    check('the time limit fails the mission after 20 minutes, and the host can switch it off', clock.when === 'time' && clock.delay === 1200 && clock.end === 'failed'
      && s.options.some(o => o.id === 'clock' && o.choices.some(c => c.triggers.includes(clock.id))), text([clock, s.options]));
    check('the squad goes in at the chosen insertion', s.insertion === 'Alpha 1', s.insertion);

    const garrison = r.enemies.map(i => s.enemies[i]).filter(e => !e.trigger), extra = r.enemies.map(i => s.enemies[i]).filter(e => e.trigger);
    check('six guards stand inside the house on walkable ground, at least 1.5 m from every other enemy', garrison.length === 6
      && garrison.every(e => api.placeContains(s.places[0], e.pos) && api.levelsAt(e.pos[0], e.pos[2]).some(y => Math.abs(y - e.pos[1]) < 0.01))
      && garrison.every(e => s.enemies.every(o => o === e || Math.hypot(o.pos[0] - e.pos[0], o.pos[2] - e.pos[2]) >= 1.5)), text(garrison.map(e => e.pos)));
    const opt = s.options.find(o => o.id === 'garrison'), oi = s.options.indexOf(opt), lots = [...new Set(garrison.map(e => e.lot).filter(l => l > 0))];
    check('the garrison option drops real lots: Light keeps 2, Standard 4 (the default), Heavy 6', !!opt && opt.pick === 1 && lots.length === 2
      && opt.choices.map(c => 6 - garrison.filter(e => c.lots.includes(e.lot)).length).join() === '2,4,6' && api.optionWarnings(opt, oi).length === 0, text([opt, api.optionWarnings(opt, oi)]));
    check('written and read back, the lots of the garrison are still there', JSON.parse(made).mission.spawns.filter(sp => sp.lot > 0).length === 4, made);
    check('the alarm brings two patrols from outside on a route into the house', extra.length === 2 && extra.every(e => e.role === 'Patrol' && e.trigger === alarm.id && !api.placeContains(s.places[0], e.pos)
      && e.patrol.length === 2 && e.patrol.every(p => api.placeContains(s.places[0], p))), text(extra));
    check('the encounter went into the kitchen with its own alarm trigger', s.zones.filter(z => z.when === 'alert').length === 2 && s.enemies.some(e => Math.hypot(e.pos[0] - 3, e.pos[2] - 3) < 0.01), text(s.enemies.map(e => e.pos)));

    const rows = s.rehearse.rows, ids = s.zones.map(z => z.id), players = rows.filter(row => row.kind === 'players');
    check('the walkthrough test fits the scenario', rows.length >= 10 && api.rehearseProblems(rows, ids).length === 0, text(api.rehearseProblems(rows, ids)));
    check('it starts outside the house at the insertion, goes in, and comes back out', players.length === 3 && text(players[0].players[0]) === '[-30,0,-30]'
      && !api.membershipInside(enter, players[0].players[0]) && players[1].players.every(p => api.membershipInside(enter, p)) && !api.membershipInside(enter, players[2].players[0]), text(players));
    const intelRow = rows.find(row => row.kind === 'intel'), heldAfter = rows.filter(row => row.kind === 'extraction' && row.value === 'held').every(row => row.at < intelRow.at);
    check('the intel is taken inside, the extraction is checked held before it and open after', intelRow.item === 'Cell Phone' && intelRow.at > players[1].at && heldAfter
      && rows.some(row => row.kind === 'extraction' && row.value === 'open' && row.at > intelRow.at) && rows.some(row => row.kind === 'fired' && row.id === intel.id), text(rows));
    check('the test is written for this scenario text', s.rehearse.pin && s.rehearse.pin.scenario === api.textHash(made) && s.rehearse.name === api.RECIPE_TEST, text(s.rehearse.pin));

    // Everything made is ordinary data: each edit is one step, and Undo gives back the mission as made.
    api.willChange('Rename trigger ' + enter.id); enter.name = 'Into the safe house'; api.changed();
    check('a made trigger can be renamed', api.scenarioText().includes('Into the safe house'));
    api.undo();
    check('one undo puts the name back', api.scenarioText() === made);
    const g = r.enemies.find(i => !s.enemies[i].trigger);    // an index: Undo puts fresh objects in place
    api.willChange('Move enemy ' + (g + 1)); s.enemies[g].pos = [2, 0, 8]; api.changed();
    check('a made enemy can be moved', JSON.parse(api.scenarioText()).mission.spawns[g].pos.join() === '2,0,8');
    api.undo();
    check('one undo moves it back', api.scenarioText() === made);
    const page2 = await load(script, 'recipe_intel_raid.json', made);
    check('saved and reopened, the scenario writes the same text', page2.api.scenarioText() === made, text(page2.api.scenarioProblems(made)));
    api.undo();
    check('one undo takes the whole recipe back', api.scenarioText() === before && s.zones.length === 0 && s.enemies.length === 0, api.scenarioText());
  }

  {   // No intel kind known for the map: explained, nothing made.
    const { api, s, done } = fresh(false), before = api.scenarioText(), steps = done();
    const c = api.recipeCheck(choice({ item: null }));
    check('without a known intel kind the recipe says so before making anything', c.blockers.some(t => /No intel is known/.test(t)), c.blockers.join(' | '));
    const r = api.recipeMake(choice());
    check('and makes nothing', r.made === false && api.scenarioText() === before && done() === steps && s.zones.length === 0, text(r));
  }

  {   // A module whose enemies hunt is refused with the reason; so is an insertion inside the building.
    const { api, s, done } = fresh(true), before = api.scenarioText(), steps = done();
    const c = api.recipeCheck(choice({ encounters: [{ module: huntModule, room: 1, name: huntModule.name }] }));
    check('a hunting module is explained before anything is made', c.blockers.some(t => t.includes('Hold the yard') && t.includes('hunt')), c.blockers.join(' | '));
    const r = api.recipeMake(choice({ encounters: [{ module: huntModule, room: 1, name: huntModule.name }] }));
    check('and nothing is made', r.made === false && api.scenarioText() === before && done() === steps && !s.zones.some(z => z.hunt), text(r));
    const inside = api.recipeCheck(choice({ insertion: 'Inside' }));
    check('an insertion inside the building is explained', inside.blockers.some(t => t.includes('"Inside" is inside House')), inside.blockers.join(' | '));
  }

  {   // The editor keeps a lot at chance 100, as the plugin does: the shipped two-man job's Garrison option depends on it.
    const name = 'clean_house_two_man_job.json', original = read('examples/' + name), page = await load(script, name, original), api = page.api, s = api.state;
    const was = JSON.parse(original).mission.spawns.map(sp => sp.lot || 0), now = JSON.parse(api.scenarioText()).mission.spawns.map(sp => sp.lot || 0);
    check('every lot of the two-man job survives opening and writing it', text(now) === text(was) && was.some(l => l > 0), text([was, now]));
    const gi = s.options.findIndex(o => o.id === 'garrison'), warn = gi >= 0 ? api.optionWarnings(s.options[gi], gi) : ['no garrison option'];
    check('its Garrison option no longer claims a lot that no enemy has', !warn.some(w => /no enemy has/.test(w)) && gi >= 0, warn.join(' | '));
    // Changing a garrison guard's chance keeps its lot: Light would otherwise stop dropping him.
    const i = s.enemies.findIndex(e => e.lot === 1), j = s.enemies.findIndex((e, k) => k !== i && e.lot === 1);
    api.willChange('Change enemy ' + (i + 1)); api.chanceSet([s.enemies[i]], 60); api.changed();
    const lotNow = JSON.parse(api.scenarioText()).mission.spawns[i];
    check('one guard of lot 1 left to chance (60) keeps lot 1 in the written file', i >= 0 && lotNow.lot === 1 && lotNow.chance === 60, text(lotNow));
    api.chanceSet([s.enemies[i]], 100);
    check('and back at 100 it still has lot 1', s.enemies[i].lot === 1 && s.enemies[i].chance === 100, text(s.enemies[i]));
    api.chanceSet([s.enemies[i], s.enemies[j]], 50);
    check('two guards of lot 1 made chancy together keep lot 1 as their shared roll', s.enemies[i].lot === 1 && s.enemies[j].lot === 1, text([s.enemies[i].lot, s.enemies[j].lot]));
    const free = s.enemies.map((e, k) => k).filter(k => !s.enemies[k].lot && !s.enemies[k].trigger).slice(0, 2);
    api.chanceSet(free.map(k => s.enemies[k]), 50);
    check('two enemies without a lot made chancy together still get a new shared lot', free.length === 2 && s.enemies[free[0]].lot > 2 && s.enemies[free[0]].lot === s.enemies[free[1]].lot, text(free.map(k => s.enemies[k])));
  }
}
