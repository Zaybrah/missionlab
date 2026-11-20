// ML-045: named undo and redo. One finished command is one entry; Undo and Redo give back the same scenario text with
// areas that agree with a fresh inside test; a cancelled gesture leaves no entry; a new command after Undo drops Redo;
// opening another file starts the history afresh; an encounter (enemies, routes, their trigger with its area, a thing and
// a shaped place) moves as one step around one pivot while the map's door stays; and an undo of a change the running game
// already has is staged as a reverse change, never claimed as undone in the game.
export default async function (h) {
  const { script, openEditor, fakeFile, check } = h;
  const text = JSON.stringify, GROUND = [-0.5, 2.6];
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  // Two enemies waiting for zone 2 (painted square -5..6), one with a route; a third far away; an AKM appearing with zone 2;
  // the place "Yard" around them; the map's front door inside the zone, which zone 2 locks.
  const fresh = () => {
    const page = openEditor(script), api = page.api, s = api.state;
    s.map = api.makeMap(new Float32Array(plate(-60, -60, 60, 60, 0)), 'history test');
    Object.assign(s, { example: false, infils: [], authored: [], objectives: [], extractionAfter: 0, selected: -1, squad: [] });
    const e0 = Object.assign(api.newEnemy([0, 0, 0], 'Patrol', 0), { trigger: 2, patrol: [[4, 0, 0], [4, 0, 4]] });
    const e1 = Object.assign(api.newEnemy([2, 0, 2], 'Guard', 0), { trigger: 2, yaw: 30 });
    s.enemies = [e0, e1, api.newEnemy([-30, 0, -30], 'Guard', 1)];
    s.zones = [api.newTrigger(2, 'zone', { pos: [1, 0, 1], radius: 8, doors: [{ name: 'Front', pos: [3, 0, 3], locked: true }] })];
    s.doors = [{ name: 'Front', pos: [3, 0, 3], mid: [3, 0, 3.4], locked: null, enemiesCanOpen: null, open: null, chance: 100, isLocked: null, enemiesKeptOut: null, broken: false, known: true }];
    s.things = [{ name: 'AKM', pos: [1, 0.02, 0], yaw: 0, trigger: 2, chance: 100 }];
    s.places = [{ id: 1, name: 'Yard', pos: [1, 0, 1], floor: null, area: { parts: [{ floor: GROUND, outline: [[-8, -8], [9, -8], [9, 9], [-8, 9]], holes: [] }] } }];
    api.areaEditStart({ kind: 'trigger', index: 0 }, GROUND);
    api.paintRect([-5, -5], [6, 6]);
    api.areaEditStop();
    api.changed();
    return { page, api, s, done: () => api.historyList().done.length, undone: () => api.historyList().undone.length };
  };
  // The cached inside test of an area against one worked out from scratch.
  const agrees = (api, area, points) => points.every(p => api.areaContains(area, p) === api.areaContains(api.areaRead(JSON.parse(api.areaSignature(area))), p));
  const probes = [[0, 1, 0], [12, 1, 0], [5.9, 1, 5.9], [15, 1, 5], [-6, 1, 0], [17, 1, 17]];

  {   // The encounter moves as one step, the door stays, and Undo / Redo give back exactly the scenario text.
    const t = fresh(), { api, s } = t, before = api.scenarioText(), n = t.done();
    api.setSquad([0, 1], 0);
    const plan = api.encounterPlan([0, 1], { dx: 10, dz: 0, turn: 0 });
    const keys = plan.moves.filter(r => !r.off).map(r => r.key).sort();
    check('the preview lists both enemies, zone 2, the AKM and the Yard as moving', text(keys) === text(['enemy:0', 'enemy:1', 'place:1', 'thing:0', 'trigger:2']) && plan.error === '', text(plan.moves) + ' ' + plan.error);
    check('the preview lists the map\'s door as staying, map-bound', plan.stays.some(r => r.key === 'door:0' && r.why.startsWith('stays: map-bound')), text(plan.stays));
    check('the preview changes nothing', api.scenarioText() === before && t.done() === n);
    const moved = api.encounterMove([0, 1], { dx: 10, dz: 0, turn: 0 });
    const after = api.scenarioText();
    check('the move is done as one history entry', moved.done === true && t.done() === n + 1 && t.undone() === 0, t.done() + ' ' + moved.error);
    check('both enemies and the whole route moved 10 m, the enemy not selected stayed', text(s.enemies[0].pos) === '[10,0,0]' && text(s.enemies[0].patrol) === '[[14,0,0],[14,0,4]]' && text(s.enemies[1].pos) === '[12,0,2]' && text(s.enemies[2].pos) === '[-30,0,-30]', text(s.enemies.map(e => e.pos)));
    check('the zone\'s anchor, its painted area, the AKM and the Yard moved with them', text(s.zones[0].pos) === '[11,0,1]' && api.areaContains(s.zones[0].area, [12, 1, 0]) && !api.areaContains(s.zones[0].area, [0, 1, 0])
      && text(s.things[0].pos) === '[11,0.02,0]' && api.areaContains(s.places[0].area, [15, 1, 5]) && !api.areaContains(s.places[0].area, [-6, 1, 0]), text(s.zones[0]) + ' ' + text(s.places[0]));
    check('the map\'s door and the zone\'s door action stay where the map has the door', text(s.doors[0].pos) === '[3,0,3]' && text(s.zones[0].doors[0].pos) === '[3,0,3]', text(s.doors[0]) + ' ' + text(s.zones[0].doors));
    check('one undo puts enemies, routes, zone, area, thing and place back exactly', api.undo() === true && api.scenarioText() === before, h.differences(JSON.parse(before), JSON.parse(api.scenarioText()), '', []).join(' / '));
    check('after undo the zone and the place answer inside tests as a fresh reading of their shapes does',
      agrees(api, s.zones[0].area, probes) && agrees(api, s.places[0].area, probes) && api.areaContains(s.zones[0].area, [0, 1, 0]) && !api.areaContains(s.zones[0].area, [12, 1, 0]));
    check('the undone move waits in Redo', t.done() === n && t.undone() === 1);
    check('redo gives back the moved scenario exactly', api.redo() === true && api.scenarioText() === after && t.done() === n + 1 && t.undone() === 0);
    check('after redo the shapes agree with a fresh reading too', agrees(api, s.zones[0].area, probes) && agrees(api, s.places[0].area, probes) && api.areaContains(s.zones[0].area, [12, 1, 0]));
  }
  {   // A turn goes around the middle of the selected enemies, and they face the turned way.
    const t = fresh(), { api, s } = t;
    const r = api.encounterMove([0, 1], { dx: 0, dz: 0, turn: 90 });
    const near = (p, x, z) => Math.abs(p[0] - x) < 0.01 && Math.abs(p[2] - z) < 0.01;
    check('a quarter turn moves each enemy around the pivot between them', r.done && near(s.enemies[0].pos, 0, 2) && near(s.enemies[1].pos, 2, 0), text(s.enemies.map(e => e.pos)));
    check('and turns where they face by the same angle', s.enemies[0].yaw === 90 && s.enemies[1].yaw === 120, s.enemies[0].yaw + ' ' + s.enemies[1].yaw);
  }
  {   // An offset that puts enemies off the map is refused before anything changes.
    const t = fresh(), { api } = t, before = api.scenarioText(), n = t.done();
    const r = api.encounterMove([0, 1], { dx: 500, dz: 0, turn: 0 });
    check('a move off the walkable ground is refused with the reason, and nothing changes', r.done === false && r.error.includes('nobody can walk') && api.scenarioText() === before && t.done() === n, r.error);
  }
  {   // One command, one entry; a cancelled transaction or gesture leaves none; a new command after Undo drops Redo.
    const t = fresh(), { api, s } = t, n = t.done(), base = api.scenarioText();
    api.addPlace([30, 0, 30], 'point', GROUND);
    check('adding a place is one entry', t.done() === n + 1, text(api.historyList()));
    api.txBegin('Drag enemy 1');
    s.enemies[0].pos = [20, 0, 20]; s.enemies[0].pos = [21, 0, 21];
    check('nothing is recorded while a gesture is under way', t.done() === n + 1);
    check('cancelling it restores the state and records nothing', api.txCancel() === true && text(s.enemies[0].pos) === '[0,0,0]' && t.done() === n + 1);
    api.txBegin('Drag enemy 1');
    check('a gesture that ends where it began records nothing', api.txCommit() === false && t.done() === n + 1);
    api.txBegin('Drag enemy 1'); s.enemies[0].pos = [5, 0, 5];
    check('a gesture that ends in a change is one entry', api.txCommit() === true && t.done() === n + 2);
    // An area corner dragged through the canvas's own handlers, then taken away by the browser: no entry (ML-032).
    api.areaEditStart({ kind: 'trigger', index: 0 }, GROUND); api.paintToolSet('vertex');
    Object.assign(s.view, { cx: 0, cz: 0, scale: 10 });
    const ev = (x, z) => { const [sx, sy] = api.toScreen(x, z); return { clientX: sx, clientY: sy, button: 0, pointerId: 1, target: { tagName: 'CANVAS' }, stopImmediatePropagation() {}, preventDefault() {} }; };
    const sig = api.areaSignature(s.zones[0].area);
    api.paintPointerDown(ev(6, 6)); api.paintPointerMove(ev(8, 8)); api.paintPointerMove(ev(9, 9));
    check('the corner drag is under way and not yet recorded', api.historyList().open !== '' && t.done() === n + 2, text(api.historyList()));
    api.paintPointerCancel({ pointerId: 1 });
    check('the cancelled corner drag leaves the area as it was and no entry', api.areaSignature(s.zones[0].area) === sig && t.done() === n + 2 && api.historyList().open === '', api.areaSignature(s.zones[0].area));
    api.areaEditStop();
    check('undo twice goes back to before the place was added', api.undo() && api.undo() && api.scenarioText() === base && t.undone() === 2);
    api.willChange('Nothing really');                                    // an edit that changed nothing does not count as a command
    check('an edit that changed nothing keeps Redo', api.redo() === true && t.undone() === 1, text(api.historyList()));
    api.addPlace([40, 0, 40], 'point', GROUND);
    check('a new command after undo clears what could be redone', t.undone() === 0 && api.redo() === false, text(api.historyList()));
  }
  {   // Opening another file: the history starts afresh, says so, and keeps the replaced page as a separate draft.
    const t = fresh(), { api, page } = t;
    api.addPlace([30, 0, 30], 'point', GROUND); api.undo();
    check('there is something to undo and to redo before opening', t.done() > 0 && t.undone() === 1);
    const other = JSON.stringify({ name: 'other', navFile: 'history test', spawns: [{ pos: [5, 0, 5] }] });
    await api.openFiles([fakeFile('other.json', other)]);
    const opened = api.scenarioText(), told = page.banner();          // what opening said, before Undo and Redo say their own line
    check('the other file is open', !api.state.refused && api.state.enemies.length === 1, page.banner());
    check('its history is empty, Undo and Redo do nothing to it', t.done() === 0 && t.undone() === 0 && api.undo() === false && api.redo() === false && api.scenarioText() === opened);
    check('the page says why the history started afresh', api.historyList().note.includes('other.json') && told.includes('Undo history starts afresh') && told.includes('other.json'), told);
    check('the scenario that was on the page is kept as a separate draft', api.separateDrafts().some(d => d.key === 'replaced' && (d.scenario.spawns || (d.scenario.mission || {}).spawns || []).length === 3), text(api.separateDrafts().map(d => d.key)));
  }
  {   // A change the running game already has: Undo stages the reverse change for review, it does not claim to rewind the game.
    const t = fresh(), { api, s, page } = t, live = api.live;
    live.on = true; live.mission = true; live.session = 's1'; live.rev = 'r1';
    api.changed(); clearTimeout(live.pushTimer); live.pending = false;
    const inGame = api.scenarioText();
    api.willChange('Move enemy 1'); s.enemies[0].pos = [6, 0, 0]; api.changed();
    clearTimeout(live.pushTimer); live.pending = true;                  // sent at once in Live mode, still on its way
    check('while the change is on its way, Undo waits and changes nothing', api.undo() === false && text(s.enemies[0].pos) === '[6,0,0]' && api.stage.mode === 'live', page.banner());
    live.pending = false;                                               // the game has it now
    const sent = api.scenarioText();
    check('then Undo changes the page', api.undo() === true && api.scenarioText() === inGame && text(s.enemies[0].pos) === '[0,0,0]');
    check('it switched to Draft mode and staged the reverse change instead of sending it', api.stage.mode === 'draft' && api.stage.dirty === true && live.staged.some(l => l.text.includes('moved')) && !live.sending, text(live.staged));
    check('it says the game keeps the change until the staged one is applied', page.banner().includes('The game keeps it') && page.banner().includes('on this page only'), page.banner());
    check('redo stages nothing more: the page matches the game again', api.redo() === true && api.scenarioText() === sent && api.stage.dirty === false, text(live.staged));
    live.session = 's2'; api.changed();
    check('another mission in the game clears the history, so it is never applied there', t.done() === 0 && t.undone() === 0 && page.banner().includes('another mission'), page.banner());
  }
}
