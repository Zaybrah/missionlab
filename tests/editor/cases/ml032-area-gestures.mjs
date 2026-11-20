// ML-032: a corner drag, an edge-middle drag and a whole-area move are one change when the pointer is let go, and no change
// at all when they are given up (Esc, pointercancel, another tool, Done). Driven through the canvas's own handlers.
export default async function (h) {
  const { script, openEditor, check } = h;
  const text = JSON.stringify, GROUND = [-0.5, 2.6];
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];
  // A 10 x 10 m painted square from (0, 0) on a zone trigger anchored at (5, 5), in Draft mode against a running mission.
  const fresh = () => {
    const api = openEditor(script).api, s = api.state, live = api.live;
    s.map = api.makeMap(new Float32Array(plate(-40, -40, 60, 60, 0)), 'gesture test');
    Object.assign(s, { infils: [], authored: [], things: [], objectives: [], doors: [], extractionAfter: 0, selected: -1, squad: [] });
    s.enemies = [api.newEnemy([-20, 0, -20], 'Guard', 0)];
    s.zones = [api.newTrigger(1, 'zone', { pos: [5, 0, 5], radius: 12, message: 'Inside' })];
    api.areaEditStart({ kind: 'trigger', index: 0 }, GROUND);
    api.paintRect([0, 0], [10, 10]);
    live.on = true; live.mission = true; live.session = 's1'; live.rev = 'r1';
    api.stageSetMode('draft');
    api.paintToolSet('vertex');
    Object.assign(s.view, { cx: 5, cz: 5, scale: 10 });
    const ev = (x, z) => { const [sx, sy] = api.toScreen(x, z); return { clientX: sx, clientY: sy, button: 0, pointerId: 1, target: { tagName: 'CANVAS' }, stopImmediatePropagation() {}, preventDefault() {} }; };
    return {
      api, s, live,
      sig: () => api.areaSignature(s.zones[0].area), pos: () => text(s.zones[0].pos),
      cached: p => api.areaContains(s.zones[0].area, p),
      worked: p => api.areaContains(api.areaRead(JSON.parse(api.areaSignature(s.zones[0].area))), p),    // a fresh copy, nothing cached
      down: (x, z) => api.paintPointerDown(ev(x, z)), move: (x, z) => api.paintPointerMove(ev(x, z)), up: (x, z) => api.paintPointerUp(ev(x, z)),
      staged: () => api.stage.dirty || live.staged.length > 0 || live.pending,
    };
  };
  // Each drag: where it grabs, where it goes, and a point that is inside only once it has gone there.
  const drags = [
    { name: 'a corner drag', from: [10, 10], to: [14, 14], probe: [12, 1, 12] },
    { name: 'an edge-middle drag', from: [10, 5], to: [14, 5], probe: [12, 1, 5] },
    { name: 'a whole-area move', from: [5, 5], to: [25, 5], probe: [22, 1, 5], anchor: '[25,0,5]' },
  ];
  const probes = d => [d.probe, [5, 1, 5], [1, 1, 1], [9.9, 1, 9.9], [-1, 1, 5], [5, 1, 11], [20, 1, 5]];
  const agree = (t, d) => probes(d).every(p => t.cached(p) === t.worked(p));
  const routes = [
    { name: 'Esc', run: t => t.api.paintKey({ key: 'Escape', target: { tagName: 'CANVAS' }, stopImmediatePropagation() {}, preventDefault() {} }) },
    { name: 'pointercancel', run: t => t.api.paintPointerCancel({ pointerId: 1 }) },
    { name: 'choosing another tool', run: t => t.api.paintToolSet('brush') },
    { name: 'leaving painting', run: t => t.api.areaEditStop() },
  ];
  for (const d of drags) {
    for (const r of routes) {   // Giving the drag up halfway puts the shape and anchor back, with nothing to undo or send.
      const t = fresh(), sig = t.sig(), pos = t.pos(), depth = t.api.undoDepth();
      t.down(...d.from); t.move((d.from[0] + d.to[0]) / 2, (d.from[1] + d.to[1]) / 2); t.move(...d.to);
      const moved = t.sig() !== sig && t.cached(d.probe) && t.worked(d.probe);
      check(d.name + ' changes the shape while it is under way, and the cached inside test follows it', moved, t.sig());
      check(d.name + ' stages nothing and adds no undo entry before it ends', !t.staged() && t.api.undoDepth() === depth);
      r.run(t);
      check(d.name + ' given up by ' + r.name + ' puts the area back exactly', t.sig() === sig, t.sig());
      check(d.name + ' given up by ' + r.name + ' puts the anchor back exactly', t.pos() === pos, t.pos());
      check(d.name + ' given up by ' + r.name + ' leaves nothing to undo and nothing staged', t.api.undoDepth() === depth && !t.staged(), t.api.undoDepth() + ' ' + text(t.live.staged));
      check(d.name + ' given up by ' + r.name + ': the moved-to point is outside again, cached and freshly worked out', !t.cached(d.probe) && !t.worked(d.probe));
      check(d.name + ' given up by ' + r.name + ': cached and fresh inside tests agree everywhere', agree(t, d));
      t.up(...d.to);
      check(d.name + ' given up by ' + r.name + ': letting go afterwards changes nothing', t.sig() === sig && t.api.undoDepth() === depth);
    }
    {   // Letting go is one change: one undo entry, staged for the game, and Undo takes area, anchor and inside test back together.
      const t = fresh(), sig = t.sig(), pos = t.pos(), depth = t.api.undoDepth();
      t.down(...d.from); t.move(d.from[0] + 1, d.from[1]); t.move(...d.to); t.up(...d.to);
      check(d.name + ' let go makes exactly one undo entry', t.api.undoDepth() === depth + 1, String(t.api.undoDepth() - depth));
      check(d.name + ' let go is staged for the game', t.api.stage.dirty && t.live.staged.length > 0, text(t.live.staged));
      check(d.name + ' let go keeps the new shape', t.sig() !== sig && t.cached(d.probe) && t.worked(d.probe), t.sig());
      if (d.anchor) check(d.name + ' let go takes the anchor with the area', t.pos() === d.anchor, t.pos());
      check(d.name + ' let go: cached and fresh inside tests agree', agree(t, d));
      t.api.undo();
      check(d.name + ': one Undo restores the area', t.sig() === sig, t.sig());
      check(d.name + ': one Undo restores the anchor', t.pos() === pos, t.pos());
      check(d.name + ': after Undo the moved-to point is outside, cached and fresh', !t.cached(d.probe) && !t.worked(d.probe) && agree(t, d));
      check(d.name + ': after Undo nothing is staged', !t.api.stage.dirty && t.live.staged.length === 0);
    }
  }
  for (const d of [drags[0], drags[2]]) {   // Pressing and letting go without moving is no change (an edge middle's click adds a corner by design).
    const t = fresh(), sig = t.sig(), pos = t.pos(), depth = t.api.undoDepth();
    t.down(...d.from); t.up(...d.from);
    check(d.name + ' that never moves changes nothing and adds no undo entry', t.sig() === sig && t.pos() === pos && t.api.undoDepth() === depth && !t.staged());
  }
  {   // Dragging there and back again is no change either.
    const t = fresh(), sig = t.sig(), depth = t.api.undoDepth();
    t.down(10, 10); t.move(14, 14); t.move(10, 10); t.up(10, 10);
    check('a corner dragged away and back is no change and no undo entry', t.sig() === sig && t.api.undoDepth() === depth && !t.staged(), t.sig());
  }
}
