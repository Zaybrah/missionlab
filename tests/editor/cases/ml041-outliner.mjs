// ML-041: floors and rooms. On a synthetic two-floor map with an enemy on each floor at the same spot, a locked ground floor
// cannot take a click, a box or a drag meant for upstairs; isolating and hiding change nothing that is saved; presets and
// order change nothing until Apply, which is one undo step; the workspace comes back for the same map fingerprint and waits
// for the author on another; the section names bands that overlap or reach the basement. Driven through the canvas's own
// handlers where the map is involved.
export default async function (h) {
  const { script, openEditor, check } = h;
  const text = JSON.stringify, r2 = v => Math.round(v * 100) / 100;
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  const square = (x0, z0, x1, z1, floor) => ({ parts: [{ floor, outline: [[x0, z0], [x1, z0], [x1, z1], [x0, z1]] }] });
  // Ground at 0 and an upper floor at 3 over the same 40 x 40 m; enemy 1 downstairs and enemy 2 upstairs at (5, 5).
  const fresh = print => {
    const api = openEditor(script).api, s = api.state;
    s.map = api.makeMap(new Float32Array([...plate(-20, -20, 20, 20, 0), ...plate(-20, -20, 20, 20, 3)]), 'two floors');
    s.map.print = print || 'fnv1a64:00000000000000a1';
    Object.assign(s, { infils: [], authored: [], things: [], objectives: [], doors: [], places: [], zones: [], placement: [], selected: -1, squad: [], ceiling: Infinity, tool: 'select' });
    s.enemies = [api.newEnemy([5, 0, 5], 'Guard', 0), api.newEnemy([5, 3, 5], 'Guard', 0)];
    Object.assign(s.view, { cx: 0, cz: 0, scale: 10 });
    const ev = (x, z, more) => { const [sx, sy] = api.toScreen(x, z); return Object.assign({ clientX: sx, clientY: sy, button: 0, pointerId: 1, shiftKey: false }, more || {}); };
    const click = (x, z) => { api.mapPointerDown(ev(x, z)); api.mapPointerUp(ev(x, z)); };
    const box = (a, b) => { api.mapPointerDown(ev(a[0], a[1], { shiftKey: true })); api.mapPointerMove(ev(b[0], b[1], { shiftKey: true })); api.mapPointerUp(ev(b[0], b[1], { shiftKey: true })); };
    const drag = (a, b) => { api.mapPointerDown(ev(a[0], a[1])); api.mapPointerMove(ev((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)); api.mapPointerMove(ev(b[0], b[1])); api.mapPointerUp(ev(b[0], b[1])); };
    const ground = () => api.outlinerWorkspace().floors.find(f => f.name === 'Ground floor');
    return { api, s, ev, click, box, drag, ground };
  };

  {   // Without a lock the overlapping spot is ambiguous: the click, the box and the drag all take the ground-floor enemy too.
    const t = fresh();
    t.click(5, 5);
    check('without a lock a click at the shared spot picks the ground-floor enemy (the ambiguity this ticket removes)', t.s.selected === 0, String(t.s.selected));
    t.api.setSquad([], -1);
    t.box([2, 2], [8, 8]);
    check('without a lock a box around the shared spot takes both enemies', text(t.api.picked()) === '[0,1]', text(t.api.picked()));
  }
  {   // Locking the ground floor: the click, the box and the drag reach only the upstairs enemy.
    const t = fresh();
    check('the storeys preset sets up floors', t.api.outlinerSetPreset('storeys') && !!t.ground());
    t.api.outlinerFlag('floor', t.ground().id, 'locked', true);
    check('a ground-floor marker is blocked once its floor is locked, an upstairs one is not', t.api.outlinerBlocks([5, 0, 5]) === 'locked' && t.api.outlinerBlocks([5, 3, 5]) === '');
    t.click(5, 5);
    check('with the ground floor locked a click at the shared spot picks the upstairs enemy', t.s.selected === 1, String(t.s.selected));
    t.api.setSquad([], -1);
    t.box([2, 2], [8, 8]);
    check('with the ground floor locked a box around the shared spot takes only the upstairs enemy', text(t.api.picked()) === '[1]', text(t.api.picked()));
    t.api.setSquad([], -1);
    const depth = t.api.undoDepth();
    t.drag([5, 5], [9, 9]);
    check('the drag moves the upstairs enemy, and it stays upstairs', text(t.s.enemies[1].pos.map(r2)) === '[9,3,9]', text(t.s.enemies[1].pos));
    check('the drag leaves the ground-floor enemy exactly where it was', text(t.s.enemies[0].pos) === '[5,0,5]', text(t.s.enemies[0].pos));
    check('the drag is one undo step', t.api.undoDepth() === depth + 1, String(t.api.undoDepth() - depth));
    // Selecting both and then locking takes the locked one out of the selection, so no key reaches it.
    t.api.outlinerFlag('floor', t.ground().id, 'locked', false);
    t.api.setSquad([0, 1], 0);
    t.api.outlinerFlag('floor', t.ground().id, 'locked', true);
    check('locking a floor drops its markers from the selection', text(t.api.picked()) === '[1]', text(t.api.picked()));
    check('a click or a new enemy on a locked floor lands on the free floor above instead', r2(t.api.outlinerGround(5, 5)) === 3, String(t.api.outlinerGround(5, 5)));
    t.api.waitForPlace('point');
    t.api.placePointerDown(t.ev(2, 2, { stopImmediatePropagation() {}, preventDefault() {} }));
    const placed = t.s.places[t.s.places.length - 1];
    check('a new place clicked over the locked ground floor goes on the free floor above', !!placed && r2(placed.pos[1]) === 3, text(placed && placed.pos));
  }
  {   // Isolating a room and hiding a floor are display only: the saved text, the mission check and the history stay the same.
    const t = fresh();
    t.s.places = [{ id: 1, name: 'Upstairs bedroom', pos: [5, 3, 5], floor: [2.5, 5.5], area: null }];
    t.s.zones = [t.api.newTrigger(1, 'zone', { pos: [5, 0, 5], radius: 5, message: 'Downstairs' })];
    const before = t.api.scenarioText(), check0 = text(t.api.missionCheck()), depth = t.api.undoDepth();
    const b = t.api.outlinerAddBuilding('House', [1]);
    t.api.outlinerIsolate('room', 1);
    check('isolating the bedroom leaves the ground-floor enemy faint and unpickable, the one in the bedroom free', t.api.outlinerBlocks([5, 0, 5]) === 'ghost' && t.api.outlinerBlocks([5, 3, 5]) === '');
    t.api.outlinerSetPreset('storeys'); t.api.outlinerFlag('floor', t.ground().id, 'hidden', true); t.api.outlinerFlag('building', b.id, 'locked', true);
    check('isolating a room, hiding a floor and locking a building leave the saved scenario text identical', t.api.scenarioText() === before);
    check('the hidden enemy and trigger are still in the saved text', t.api.scenarioText().includes('Downstairs') && t.s.enemies.length === 2);
    check('the mission check sees the same mission while things are isolated or hidden', text(t.api.missionCheck()) === check0);
    check('workspace changes add no undo step', t.api.undoDepth() === depth, String(t.api.undoDepth() - depth));
  }
  {   // A preset, a changed band or another order changes nothing saved; Apply does, as one step that Undo takes back.
    const t = fresh();
    const area = t.api.areaRead(square(0, 0, 10, 10, [-0.5, 2.6]));
    t.api.areaPrepare(area);
    t.s.zones = [t.api.newTrigger(1, 'zone', { pos: [5, 0, 5], radius: 5, area })];
    const before = t.api.scenarioText(), depth = t.api.undoDepth();
    t.api.outlinerSetPreset('clean-house');
    const ws = t.api.outlinerWorkspace(), up = ws.floors.find(f => f.name === 'Upstairs');
    t.api.outlinerSetFloor(up.id, { band: [3.1, 6.3] });
    t.api.outlinerMove('floor', up.id, -1);
    t.api.outlinerSetPreset('storeys'); t.api.outlinerSetPreset('clean-house');
    const up2 = t.api.outlinerWorkspace().floors.find(f => f.name === 'Upstairs');
    t.api.outlinerMove('floor', up2.id, -1);
    check('changing presets, a band and the order changes nothing in the saved text', t.api.scenarioText() === before);
    check('changing presets, a band and the order adds no undo step', t.api.undoDepth() === depth);
    check('the order is the workspace\'s own: Upstairs moved up the list', t.api.outlinerWorkspace().floors.map(f => f.name).join(',') === 'Basement,Upstairs,Ground floor', t.api.outlinerWorkspace().floors.map(f => f.name).join(','));
    t.api.outlinerChoose('trigger:1', true);
    check('Apply changes one area', t.api.outlinerApply(up2.id) === 1);
    const o = JSON.parse(t.api.scenarioText()), body = o.mission || o;
    check('after Apply the saved area has the Upstairs band', text(body.triggers[0].area.parts[0].floor) === '[3.2,6.2]', text(body.triggers[0].area.parts[0].floor));
    check('Apply is exactly one undo step', t.api.undoDepth() === depth + 1, String(t.api.undoDepth() - depth));
    check('after Apply the area counts upstairs and no longer on the ground floor', t.api.areaContains(t.s.zones[0].area, [5, 3.68, 5]) && !t.api.areaContains(t.s.zones[0].area, [5, 0.68, 5]));
    t.api.undo();
    check('one Undo gives back the text from before Apply', t.api.scenarioText() === before);
    check('after Undo the inside test follows the old band again', t.api.areaContains(t.s.zones[0].area, [5, 0.68, 5]) && !t.api.areaContains(t.s.zones[0].area, [5, 3.68, 5]));
  }
  {   // The workspace is the map's: it comes back on the same fingerprint, and another fingerprint asks before using it.
    const t = fresh('fnv1a64:00000000000000a1');
    t.api.outlinerSetPreset('storeys');
    t.api.outlinerFlag('floor', t.ground().id, 'locked', true);
    const bands = text(t.api.outlinerWorkspace().floors.map(f => f.band));
    t.s.map = t.api.makeMap(new Float32Array([...plate(-20, -20, 20, 20, 0), ...plate(-20, -20, 20, 20, 3)]), 'two floors'); t.s.map.print = 'fnv1a64:00000000000000b2';
    const other = t.api.outlinerWorkspace();
    check('another map fingerprint asks for review', !!other.review && other.review.floors.length === 3, text(other.review));
    check('another map fingerprint does not reassign the floors while it waits', other.floors.length === 0 && t.api.outlinerBlocks([5, 0, 5]) === '');
    t.click(5, 5);
    check('while it waits nothing is locked on the other map: the click takes the ground-floor enemy', t.s.selected === 0, String(t.s.selected));
    t.s.map = t.api.makeMap(new Float32Array([...plate(-20, -20, 20, 20, 0), ...plate(-20, -20, 20, 20, 3)]), 'two floors again'); t.s.map.print = 'fnv1a64:00000000000000a1';
    const back = t.api.outlinerWorkspace();
    check('the same fingerprint restores the floors with their bands', !back.review && text(back.floors.map(f => f.band)) === bands, text(back.floors));
    check('the same fingerprint restores the lock', t.api.outlinerBlocks([5, 0, 5]) === 'locked');
    t.s.map = t.api.makeMap(new Float32Array([...plate(-20, -20, 20, 20, 0), ...plate(-20, -20, 20, 20, 3)]), 'two floors'); t.s.map.print = 'fnv1a64:00000000000000b2';
    t.api.outlinerCheckMap();
    check('keeping the reviewed floors uses them unchanged on the other map', t.api.outlinerReviewKeep() && text(t.api.outlinerWorkspace().floors.map(f => f.band)) === bands);
  }
  {   // The section: map floors at -3, 0 and 3 with stairs up, a ground-floor zone painted down into the basement, and an
      // upstairs zone whose band starts below the top of the ground floor's.
    const api = openEditor(script).api, s = api.state;
    const ramp = [10, 0, 4, 14, 3, 4, 14, 3, 6, 10, 0, 4, 14, 3, 6, 10, 0, 6];
    s.map = api.makeMap(new Float32Array([...plate(0, 0, 10, 10, -3), ...plate(0, 0, 10, 10, 0), ...plate(0, 0, 10, 10, 3), ...ramp]), 'house');
    Object.assign(s, { things: [], objectives: [], doors: [], places: [], placement: [], enemies: [] });
    const zone = (id, y, band) => { const a = api.areaRead(square(1, 1, 9, 9, band)); api.areaPrepare(a); return api.newTrigger(id, 'zone', { pos: [5, y, 5], radius: 4, area: a }); };
    s.zones = [zone(1, 0, [-3.5, 2.8]), zone(2, 3, [2.5, 6]), zone(3, 0, [-0.5, 2.6])];
    const m = api.sectionModel([-2, 5], [16, 5]);
    check('the section finds the three floors under the line', text(m.floors) === '[-3,0,3]', text(m.floors));
    check('the section finds the stairs between the floors', m.stairs.length === 1 && m.stairs[0].low < 0.6 && m.stairs[0].high > 2.4, text(m.stairs.map(q => [q.low, q.high])));
    const overlap = m.notes.find(n => n.kind === 'overlap' && n.a === 'trigger:1' && n.b === 'trigger:2');
    check('the section reports the ground-floor and upstairs bands overlapping from 2.5 to 2.8 m', !!overlap && overlap.bottom === 2.5 && overlap.top === 2.8, text(m.notes));
    const below = m.notes.find(n => n.kind === 'below' && n.a === 'trigger:1');
    check('the section reports the ground-floor zone reaching the basement', !!below && below.y === -3 && below.basement, text(m.notes));
    check('a zone that keeps to its own floor gets no coverage note', !m.notes.some(n => (n.kind === 'below' || n.kind === 'above') && n.a !== 'trigger:1'), text(m.notes));
    s.zones = [zone(1, 0, [0.2, 3.2]), zone(2, 3, [3.2, 6.2])];
    const touching = api.sectionModel([-2, 5], [16, 5]).notes.find(n => n.kind === 'touch');
    check('bands that only meet are reported as touching', !!touching && touching.bottom === 3.2, text(touching));
    api.outlinerSetPreset('clean-house');
    const feet = api.sectionModel([-2, 5], [16, 5]).feet.map(f => f.y);
    check('with floors set up the section uses their player-foot samples', text(feet) === '[-2.32,0.68,3.68]', text(feet));
  }
}
