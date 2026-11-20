// ML-040: zones linked to a named place. Linking and copying are different choices; one change to a linked place's shape
// reaches every linked zone in one undo step and one staged change, while a copy stays as it was; renaming keeps links;
// deleting waits for the links to be moved or detached; a dangling link is never saved; the file keeps the links as
// missionlab-plan/3 and the flattened export keeps every zone's exact shape; a linked zone is not painted on its own;
// moving an encounter leaves a shared place where it is; a module asks for the place again or keeps a copy.
export default async function (h) {
  const { script, openEditor, load, check } = h;
  const text = JSON.stringify, GROUND = [-0.5, 2.6];
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  const square = (x0, z0, x1, z1) => ({ parts: [{ floor: GROUND.slice(), outline: [[x0, z0], [x1, z0], [x1, z1], [x0, z1]], holes: [] }] });
  // The house (-10..10) and the yard (20..30) are painted places; the stairs a point. Zone 1 brings the enemy standing in the
  // house; zone 2 is a hold; zone 3 is in the yard; zone 4 will take a copy of the house.
  const fresh = () => {
    const page = openEditor(script), api = page.api, s = api.state;
    s.map = api.makeMap(new Float32Array(plate(-60, -60, 60, 60, 0)), 'place links test');
    Object.assign(s, { example: false, title: 'Links', infils: [], authored: [], objectives: [], things: [], doors: [], extractionAfter: 0, selected: -1, squad: [] });
    s.enemies = [Object.assign(api.newEnemy([-5, 0, -5], 'Guard', 0), { trigger: 1 }), api.newEnemy([-40, 0, -40], 'Guard', 1)];
    s.zones = [api.newTrigger(1, 'zone', { pos: [0, 0, 0], radius: 8 }), api.newTrigger(2, 'zone', { pos: [1, 0, 1], radius: 8, hold: 20 }),
      api.newTrigger(3, 'zone', { pos: [25, 0, 25], radius: 4 }), api.newTrigger(4, 'zone', { pos: [2, 0, 2], radius: 4 })];
    s.places = [{ id: 1, name: 'House', pos: [0, 0, 0], floor: null, area: square(-10, -10, 10, 10) },
      { id: 2, name: 'Yard', pos: [25, 0, 25], floor: null, area: square(20, 20, 30, 30) },
      { id: 3, name: 'Stairs', pos: [40, 0, 40], floor: [-0.5, 2.6], area: null }];
    api.changed();
    return { page, api, s, sig: a => (a ? api.areaSignature(a) : 'none'), depth: () => api.undoDepth() };
  };
  const linkHouse = t => { t.api.placeLink(1, 1); t.api.placeLink(2, 1); t.api.placeCopyArea(4, 1); };

  {   // Link and copy are two choices, each one undo step.
    const t = fresh(), { api, s } = t, n = t.depth();
    check('a zone links a painted place', api.placeLink(1, 1) === true && s.zones[0].place === 1 && t.sig(s.zones[0].area) === t.sig(s.places[0].area));
    check('a hold links the same place', api.placeLink(2, 1) === true && s.zones[1].place === 1);
    check('another zone takes a copy of it, with no link', api.placeCopyArea(4, 1) === true && s.zones[3].place === 0 && t.sig(s.zones[3].area) === t.sig(s.places[0].area));
    check('each choice is one undo step', t.depth() === n + 3, t.depth() - n);
    check('a point place cannot be linked', api.placeLink(3, 3) === false && s.zones[2].place === 0 && t.page.banner().includes('point'), t.page.banner());
  }
  {   // One change to the place's footprint: every linked zone, one undo entry, one staged change; the copy stays.
    const t = fresh(), { api, s } = t;
    linkHouse(t);
    const before = api.stageSnapshot(), copy = t.sig(s.zones[3].area), n = t.depth();
    check('"Edit the place" from a linked zone starts painting the place', api.placeEditFor(1) === true && s.paint.target && s.paint.target.kind === 'place');
    check('painting a linked place names the zones that change with it', t.page.banner().includes('Zone 1') && t.page.banner().includes('zone 2'), t.page.banner());
    api.paintRect([10, -10], [16, 10]);
    api.areaEditStop();
    const grown = [13, 1, 0];
    check('the place grew', api.areaContains(s.places[0].area, grown));
    check('both linked zones took the new shape', api.membershipInside(s.zones[0], grown) && api.membershipInside(s.zones[1], grown) && t.sig(s.zones[0].area) === t.sig(s.places[0].area) && t.sig(s.zones[1].area) === t.sig(s.places[0].area));
    check('the copy kept the old shape', t.sig(s.zones[3].area) === copy && !api.membershipInside(s.zones[3], grown));
    check('it is one undo entry', t.depth() === n + 1, t.depth() - n);
    const lines = api.semanticDiff(before, api.stageSnapshot(), { fired: [] });
    const places = lines.filter(l => l.kind === 'place'), triggers = lines.filter(l => l.kind === 'trigger');
    check('it is one staged change, naming both dependants, and no separate trigger change', places.length === 1 && triggers.length === 0 && places[0].text.includes('zone 1') && places[0].text.includes('zone 2'), text(lines));
    check('the batch for the game carries the places with the links', Array.isArray(api.stageSnapshot().places) && api.stageSnapshot().triggers[0].place === 1 && api.stageSnapshot().triggers[0].area === undefined);
    api.undo();
    check('one undo takes the place and both zones back together', !api.areaContains(s.places[0].area, grown) && !api.membershipInside(s.zones[0], grown) && !api.membershipInside(s.zones[1], grown) && s.zones[0].place === 1);
    api.redo();
    check('redo gives all of them the new shape again', api.membershipInside(s.zones[0], grown) && api.membershipInside(s.zones[1], grown) && t.sig(s.zones[3].area) === copy);
  }
  {   // Rename keeps the links; deleting waits for retargeting or detaching.
    const t = fresh(), { api, s } = t;
    linkHouse(t);
    check('renaming keeps the links', api.renamePlace(1, 'Farmhouse') === true && s.zones[0].place === 1 && s.zones[1].place === 1);
    const written = JSON.parse(api.scenarioText()).mission;
    check('the file names the place by id, under its new name', written.triggers[0].place === 1 && written.places[0].name === 'Farmhouse', text(written.triggers[0]));
    const uses = api.deletePlace(1, true);
    check('a linked place is not deleted, even when asked twice', uses.some(u => u.includes('zone 1 is linked')) && s.places.some(p => p.id === 1), text(uses));
    check('its links move to another painted place as one step', api.placeRetarget(1, 2) === true && s.zones[0].place === 2 && s.zones[1].place === 2 && t.sig(s.zones[0].area) === t.sig(s.places[1].area));
    check('then it can be deleted', api.deletePlace(1, false).length === 0 && !s.places.some(p => p.id === 1));
    const u = fresh();
    linkHouse(u);
    const was = u.sig(u.s.zones[0].area);
    check('detaching all keeps each zone\'s shape as its own copy', u.api.placeDetachAll(1) === true && u.s.zones[0].place === 0 && u.sig(u.s.zones[0].area) === was && u.s.zones[1].place === 0);
    check('then the place can be deleted', u.api.deletePlace(1, false).length === 0);
  }
  {   // A dangling link is never saved as a valid mission.
    const t = fresh(), { api, s } = t;
    linkHouse(t);
    s.zones[0].place = 99;
    const found = api.scenarioProblems(api.scenarioText()).filter(f => f.level === 'error').map(f => f.code + ' ' + f.path);
    check('the file rules refuse it with the plugin\'s code and path', text(found) === text(['missing-ref triggers[0].place']), text(found));
    check('saving and exporting are held with a reason', api.moduleBlock().startsWith('Not saved') && api.moduleBlock().includes('place 99'), api.moduleBlock());
  }
  {   // Save and reload keep the links, as plan/3; without links the format stays as it was.
    const t = fresh(), { api } = t;
    linkHouse(t);
    const saved = api.scenarioText(), obj = JSON.parse(saved);
    check('a page with links saves missionlab-plan/3', obj.schema === 'missionlab-plan/3' && obj.mission.triggers[0].place === 1 && obj.mission.triggers[0].area === undefined && obj.mission.triggers[1].when === 'hold' && obj.mission.triggers[1].place === 1, text(obj.mission.triggers.slice(0, 2)));
    check('the copy is saved as its own area', obj.mission.triggers[3].area !== undefined && obj.mission.triggers[3].place === undefined);
    const back = await load(script, 'linked.json', saved), z = back.api.state.zones, p = back.api.state.places;
    check('it loads without a finding', !back.api.state.refused, back.banner());
    check('the links come back, with the place\'s shape', z[0].place === 1 && z[1].place === 1 && back.api.areaSignature(z[0].area) === back.api.areaSignature(p[0].area));
    const again = JSON.parse(back.api.scenarioText()).mission;
    check('and its triggers and places are written back the same', text(again.triggers) === text(obj.mission.triggers) && text(again.places) === text(obj.mission.places), h.differences(obj.mission, again, '', []).join(' / '));
    const u = fresh();
    u.api.placeCopyArea(1, 1);
    check('a page with only copies still saves missionlab-plan/2', JSON.parse(u.api.scenarioText()).schema === 'missionlab-plan/2');
  }
  {   // The flattened export: no links, plan/2, the exact effective shape of every zone.
    const t = fresh(), { api, s } = t;
    linkHouse(t);
    api.areaEditStart({ kind: 'place', index: 0 }, GROUND); api.paintRect([10, -10], [16, 10]); api.areaEditStop();
    const flatText = api.placeExportFlat(), flat = JSON.parse(flatText);
    check('the flattened copy is missionlab-plan/2 and names no link', flat.schema === 'missionlab-plan/2' && flat.mission.triggers.every(z => z.place === undefined) && flat.mission.triggers[0].area !== undefined, flatText.slice(0, 200));
    check('the page keeps its links', s.zones[0].place === 1 && JSON.parse(api.scenarioText()).schema === 'missionlab-plan/3');
    const other = await load(script, 'flat.json', flatText), oz = other.api.state.zones;
    check('the flattened copy loads', !other.api.state.refused && oz.length === s.zones.length, other.banner());
    let probes = 0, differ = 0;
    for (let x = -14; x <= 34; x += 1.5) for (let zz = -14; zz <= 34; zz += 1.5) for (const y of [-1, 1, 2.5, 4]) {
      for (let i = 0; i < s.zones.length; i++) { probes++; if (api.membershipInside(s.zones[i], [x, y, zz]) !== other.api.membershipInside(oz[i], [x, y, zz])) differ++; }
    }
    check('every zone holds the same probe points flattened as linked', differ === 0 && probes > 4000, differ + ' of ' + probes);
  }
  {   // A linked zone's area is not painted on its own: edit the place, or detach.
    const t = fresh(), { api, s } = t;
    linkHouse(t);
    check('painting a linked zone is refused with the two ways on', api.areaEditStart({ kind: 'trigger', index: 0 }, GROUND) === false && t.page.banner().includes('Edit the place') && t.page.banner().includes('Detach'), t.page.banner());
    check('"Edit the place" paints the place', api.placeEditFor(1) === true && api.state.paint.target && api.state.paint.target.kind === 'place' && api.state.paint.target.id === 1);
    api.areaEditStop();
    const n = t.depth();
    check('detaching is one step and keeps the shape', api.placeDetach(1) === true && s.zones[0].place === 0 && t.sig(s.zones[0].area) === t.sig(s.places[0].area) && t.depth() === n + 1);
    check('the detached zone can be painted on its own', api.areaEditStart({ kind: 'trigger', index: 0 }, GROUND) === true);
    api.paintRect([-20, -2], [-12, 2]); api.areaEditStop();
    check('and its painting no longer reaches the place or the zone still linked', !api.areaContains(s.places[0].area, [-15, 1, 0]) && !api.membershipInside(s.zones[1], [-15, 1, 0]) && api.membershipInside(s.zones[0], [-15, 1, 0]));
  }
  {   // Moving an encounter leaves a place shared with another zone where it is, and keeps both links.
    const t = fresh(), { api, s } = t;
    linkHouse(t);
    const house = t.sig(s.places[0].area);
    const plan = api.encounterPlan([0], { dx: 10, dz: 0, turn: 0 });
    check('the preview lists the shared place as staying', plan.error === '' && plan.stays.some(r => r.key === 'place:1' && r.why.startsWith('stays: shared')) && !plan.places.length, text(plan.stays) + ' ' + plan.error);
    const moved = api.encounterMove([0], { dx: 10, dz: 0, turn: 0 });
    check('the encounter moved', moved.done === true && text(s.enemies[0].pos) === '[5,0,-5]' && text(s.zones[0].pos) === '[10,0,0]', text(s.enemies[0].pos) + ' ' + text(s.zones[0].pos));
    check('the place did not move', t.sig(s.places[0].area) === house);
    check('both zones are still linked to it, with its shape', s.zones[0].place === 1 && s.zones[1].place === 1 && t.sig(s.zones[0].area) === house && t.sig(s.zones[1].area) === house);
  }
  {   // A module of a linked zone asks for the place again, or keeps its own copy; it never moves the shared place.
    const t = fresh(), { api, s } = t;
    linkHouse(t);
    const house = t.sig(s.places[0].area);
    s.selected = 0; s.squad = [];
    const got = api.moduleOfSelection('House raid'), mod = got.module;
    check('the module is made', !!mod, got.error);
    if (mod) {
      const b = mod.bindings.find(x => x.kind === 'place');
      check('the linked place becomes a binding marked as a link, and the zone keeps its shape for a copy', b && b.link === true && mod.triggers[0].place === '@' + b.ref && mod.triggers[0].area !== undefined, text(mod.bindings) + ' ' + text(mod.triggers[0]));
      const one = api.moduleInsert(mod, [40, 0, 40], 0), z1 = s.zones.find(z => z.id === one.triggers[0]);
      check('an inserted copy is not linked until bound, and saving is held', z1 && z1.place === 0 && api.moduleBlock() !== '' && t.sig(s.places[0].area) === house);
      const key = api.moduleOpen().find(x => x.kind === 'place').key;
      check('binding links the zone to the chosen place', api.moduleBind(key, 1) === true && z1.place === 1 && t.sig(z1.area) === house && api.moduleBlock() === '');
      const two = api.moduleInsert(mod, [40, 0, 40], 0), z2 = s.zones.find(z => z.id === two.triggers[0]);
      const key2 = api.moduleOpen().find(x => x.kind === 'place').key;
      check('or the copy keeps its own moved shape, unlinked', api.moduleBind(key2, 'copy') === true && z2.place === 0 && api.membershipInside(z2, [45, 1, 45]) && !api.membershipInside(z2, [0, 1, 0]), text(z2.area));
      check('the shared place never moved', t.sig(s.places[0].area) === house && s.zones[0].place === 1);
    }
  }
}
