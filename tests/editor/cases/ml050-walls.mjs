// ML-050: wall tracing and room suggestions from exported geometry. A house is built here as an .mlgeo file: outer walls
// with an open front door to a garden, a closed room with a pillar, a kitchen whose doorway opens onto the hall, and a
// back room whose stairwell goes down (no floor on the band). Corners snap to its walls only with snapping on and only
// with geometry of the open map; suggestions stop at leaks, and a draft changes nothing until it is added as one step.
export default async function (h) {
  const { script, openEditor, check } = h;
  const text = JSON.stringify, BAND = [-0.5, 2.6];
  const plate = (x0, z0, x1, z1, y) => [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z0, x1, y, z1, x0, y, z1];

  // The house: x 0 to 20, z 0 to 12, walls 0.2 m thick and 3 m high. South row: room A (x 0-8, closed, pillar at
  // 3.5-4.1 x 2.7-3.3), kitchen B (x 8-14, doorway x 10-11 to the hall), back room S (x 14-20, doorway x 15-16; behind a
  // wall at x 18 its stairwell x 18.1-19.9, z 0.1-4.5 goes down). North row: the hall (z 6-12), front door x 4-5.6 open
  // onto the garden. The garden's ground runs 20 m beyond the house.
  function houseGeo(shift) {
    const pos = [], idx = [];
    const quad = (a, b, c, d) => { const n = pos.length / 3; for (const p of [a, b, c, d]) pos.push(p[0] + shift, p[1], p[2]); idx.push(n, n + 1, n + 2, n, n + 2, n + 3); };
    const box = (x0, z0, x1, z1) => {
      const y0 = 0, y1 = 3;
      quad([x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0]); quad([x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]);
      quad([x0, y0, z0], [x0, y0, z1], [x0, y1, z1], [x0, y1, z0]); quad([x1, y0, z0], [x1, y0, z1], [x1, y1, z1], [x1, y1, z0]);
      quad([x0, y0, z0], [x1, y0, z0], [x1, y0, z1], [x0, y0, z1]); quad([x0, y1, z0], [x1, y1, z0], [x1, y1, z1], [x0, y1, z1]);
    };
    const floor = (x0, z0, x1, z1) => quad([x0, 0, z0], [x1, 0, z0], [x1, 0, z1], [x0, 0, z1]);
    box(-0.1, -0.1, 20.1, 0.1); box(-0.1, -0.1, 0.1, 12.1); box(19.9, -0.1, 20.1, 12.1);          // south, west, east
    box(-0.1, 11.9, 4, 12.1); box(5.6, 11.9, 20.1, 12.1);                                          // north, front door open
    box(-0.1, 5.9, 10, 6.1); box(11, 5.9, 15, 6.1); box(16, 5.9, 20.1, 6.1);                       // hall wall, two doorways
    box(7.9, -0.1, 8.1, 6.1); box(13.9, -0.1, 14.1, 6.1); box(17.9, -0.1, 18.1, 4.5);               // A|B, B|S, S|stairwell
    box(3.5, 2.7, 4.1, 3.3);                                                                       // the pillar in A
    floor(-20, -20, 40, 0.1); floor(-20, 4.5, 40, 32); floor(-20, 0.1, 18.1, 4.5); floor(19.9, 0.1, 40, 4.5);
    quad([18.1, 0, 4.5], [19.9, 0, 4.5], [19.9, -3, 0.1], [18.1, -3, 0.1]);                       // the stairs going down
    const nv = pos.length / 3, ni = idx.length, buf = new ArrayBuffer(20 + nv * 12 + ni * 4), dv = new DataView(buf);
    new Uint8Array(buf, 0, 8).set([...'MLGEO2\n'].map(c => c.charCodeAt(0)).concat([0]));
    dv.setInt32(8, nv, true); dv.setInt32(12, ni, true); dv.setInt32(16, 0, true);
    new Float32Array(buf, 20, nv * 3).set(pos); new Uint32Array(buf, 20 + nv * 12, ni).set(idx);
    return buf;
  }
  const geoFile = (name, shift) => { const buf = houseGeo(shift || 0); return { name, size: buf.byteLength, type: '', arrayBuffer: async () => buf.slice(0) }; };

  // A page with the walkable ground of house.bytes, a zone painted on the ground floor, and optionally a geometry file.
  async function fresh(geoName, shift) {
    const api = openEditor(script).api, s = api.state;
    s.map = api.makeMap(new Float32Array(plate(-20, -20, 40, 32, 0)), 'house.bytes');
    Object.assign(s, { example: false, infils: [], authored: [], things: [], objectives: [], doors: [], extractionAfter: 0, selected: -1, squad: [], enemies: [] });
    s.zones = [api.newTrigger(1, 'zone', { pos: [10, 0, 9], radius: 5, message: 'Inside' })];
    if (geoName) await api.openFiles([geoFile(geoName, shift)]);
    api.areaEditStart({ kind: 'trigger', index: 0 }, BAND);
    Object.assign(s.view, { cx: 6, cz: 3, scale: 20 });
    const ev = (x, z, button) => { const [sx, sy] = api.toScreen(x, z); return { clientX: sx, clientY: sy, button: button || 0, pointerId: 1, target: { tagName: 'CANVAS' }, stopImmediatePropagation() {}, preventDefault() {} }; };
    const t = {
      api, s,
      area: () => s.zones[0].area, parts: () => (s.zones[0].area ? s.zones[0].area.parts : []),
      down: (x, z) => api.paintPointerDown(ev(x, z)), move: (x, z) => api.paintPointerMove(ev(x, z)), up: (x, z) => api.paintPointerUp(ev(x, z)),
      rect: (a, b) => { api.paintToolSet('rect'); t.down(a[0], a[1]); t.move((a[0] + b[0]) / 2, (a[1] + b[1]) / 2); t.move(b[0], b[1]); t.up(b[0], b[1]); },
      corners: () => t.parts().flatMap(p => p.outline.map(q => q[0] + ',' + q[1])),
      inside: (parts, x, z) => api.areaContains({ parts: JSON.parse(text(parts)) }, [x, 0.5, z]),
      sig: () => api.scenarioText(), depth: () => api.undoDepth(),
    };
    return t;
  }
  const box = ring => ring.reduce((b, p) => ({ minX: Math.min(b.minX, p[0]), maxX: Math.max(b.maxX, p[0]), minZ: Math.min(b.minZ, p[1]), maxZ: Math.max(b.maxZ, p[1]) }), { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity });
  const near = (b, want, tol) => Math.abs(b.minX - want[0]) <= tol && Math.abs(b.minZ - want[1]) <= tol && Math.abs(b.maxX - want[2]) <= tol && Math.abs(b.maxZ - want[3]) <= tol;
  const ROOM_A = [0.1, 0.1, 7.9, 5.9], PILLAR = [3.5, 2.7, 4.1, 3.3], KITCHEN = [8.1, 0.1, 13.9, 5.9];

  // Without geometry, with another map's geometry, or with geometry that does not lie under this map's ground, wall
  // tracing is off with a reason, and painting by hand does what it always did.
  for (const c of [{ name: 'no geometry', file: null, why: /\.mlgeo/ }, { name: 'geometry of another map', file: 'other.mlgeo', why: /another map/ },
    { name: 'geometry that does not line up', file: 'house.mlgeo', shift: 500, why: /does not line up/ }]) {
    const t = await fresh(c.file, c.shift), m = t.api.wallsMatch();
    check('wall tracing is off with ' + c.name + ', and says why', !m.ok && c.why.test(m.why), m.why);
    t.api.wallsSet({ snap: true });
    const depth = t.depth();
    t.rect([4, 0.4], [6, 3]);
    check('with ' + c.name + ' a rectangle is painted by hand exactly where it is drawn, as one step', t.corners().includes('4,0.4') && t.depth() === depth + 1, text(t.corners()));
    const sig = t.sig(), r = t.api.wallsSuggest([2, 2]);
    check('with ' + c.name + ' no room is suggested and nothing changes', !!r.error && !t.s.walls.draft && !t.s.walls.leak && t.sig() === sig && t.depth() === depth + 1, text(r));
    check('with ' + c.name + ' the suggestion tools cannot be chosen', t.api.wallsToolSet('room') === false && !t.s.walls.tool);
  }

  {   // Snapping: a corner 0.3 m from a wall lands on the wall face only with snapping on; a wall corner wins near a corner.
    const t = await fresh('house.mlgeo'), m = t.api.wallsMatch(), slice = t.api.wallsSlice();
    check('geometry exported on the open map counts, and its walls are cut at waist height', m.ok && !!slice && slice.y === 1 && slice.n > 0, text(m) + ' ' + (slice && slice.y));
    t.rect([4, 0.4], [6, 3]);
    check('with snapping off a rectangle corner 0.3 m from a wall stays where it is drawn', t.corners().includes('4,0.4') && !t.corners().includes('4,0.1'), text(t.corners()));
    t.api.undo();
    t.api.wallsSet({ snap: true, tol: 0.5 });
    t.rect([4, 0.4], [6, 3]);
    check('with snapping on the same rectangle corner lands on the wall face', t.corners().includes('4,0.1') && t.corners().includes('6,3'), text(t.corners()));
    const depth = t.depth();
    t.api.paintToolSet('vertex');
    t.down(6, 3); t.move(6.8, 3); t.move(7.6, 3); t.up(7.6, 3);
    check('a corner dragged to 0.3 m from a wall lands on it, as one step', t.corners().includes('7.9,3') && t.depth() === depth + 1, text(t.corners()));
    t.api.undo(); t.api.wallsSet({ snap: false });
    t.api.paintToolSet('vertex');
    t.down(6, 3); t.move(6.8, 3); t.move(7.6, 3); t.up(7.6, 3);
    check('with snapping off the dragged corner stays where it is let go', t.corners().includes('7.6,3') && !t.corners().includes('7.9,3'), text(t.corners()));
    t.api.wallsSet({ snap: true });
    t.api.paintToolSet('poly');
    for (const p of [[9, 0.4], [12, 0.4], [12, 3], [9, 3], [9, 0.4]]) { t.down(p[0], p[1]); t.up(p[0], p[1]); }
    check('polygon corners placed 0.3 m from a wall land on it', t.corners().includes('9,0.1') && t.corners().includes('12,0.1') && t.corners().includes('12,3'), text(t.corners()));
    const corner = t.api.wallsSnapPoint([0.3, 0.35], 0.5), edge = t.api.wallsSnapPoint([2, 0.4], 0.5);
    check('near the corner of a room a point snaps to the corner, elsewhere onto the wall face', !!corner && corner.corner && Math.hypot(corner.p[0] - 0.1, corner.p[1] - 0.1) < 0.01
      && !!edge && !edge.corner && Math.abs(edge.p[1] - 0.1) < 0.01 && Math.abs(edge.p[0] - 2) < 0.01, text([corner, edge]));
    check('the tolerance is kept: a point 0.3 m from a wall does not snap within 0.2 m', t.api.wallsSnapPoint([2, 0.4], 0.2) === null);
  }

  {   // A closed room with a pillar: its outline with the pillar as a hole, shown as a draft; dropping it changes nothing.
    const t = await fresh('house.mlgeo'), sig = t.sig(), depth = t.depth();
    const r = t.api.wallsSuggest([2, 2]), d = t.s.walls.draft;
    check('a closed room gives a draft and no leak', !!r.draft && !!d && !t.s.walls.leak, text(r));
    const part = d && d.parts[0];
    check('the draft is the room\'s outline, within 0.25 m of its walls', !!part && d.parts.length === 1 && near(box(part.outline), ROOM_A, 0.25), part && text(part.outline));
    check('the pillar the room surrounds is kept as a hole, within 0.25 m', !!part && (part.holes || []).length === 1 && near(box(part.holes[0]), PILLAR, 0.25), part && text(part.holes));
    check('the draft is within the rules and limits of a painted area', !!d && d.findings.length === 0 && !d.limit, d && text(d.findings));
    check('a draft on screen changes neither the scenario nor the undo history', t.sig() === sig && t.depth() === depth && !(t.parts().length));
    t.api.wallsDraftVertex(0, -1, 0, [0.6, 0.6]);
    check('correcting a corner of the draft changes neither the scenario nor the undo history', t.sig() === sig && t.depth() === depth);
    t.api.wallsReject();
    check('dropping the draft leaves the scenario text and the undo depth exactly as they were', !t.s.walls.draft && t.sig() === sig && t.depth() === depth);

    t.api.wallsSuggest([2, 2]);
    const ok = t.api.wallsAccept(), a = t.area();
    check('adding the room is one undo step', ok && t.depth() === depth + 1, String(t.depth() - depth));
    check('the added room keeps its hole', !!a && a.parts.length === 1 && (a.parts[0].holes || []).length === 1, a && text(a));
    check('the added room passes the area rules', !!a && t.api.areaFindings(a).length === 0, a && text(t.api.areaFindings(a)));
    check('the added room counts inside the room and not on the pillar', !!a && t.api.areaContains(a, [2, 0.5, 2]) && !t.api.areaContains(a, [3.8, 0.5, 3]) && !t.api.areaContains(a, [11, 0.5, 3]));
    check('the draft is gone once added', !t.s.walls.draft);
    t.api.undo();
    check('one Undo takes the whole room out again', t.parts().length === 0 && t.sig() === sig, text(t.parts()));
  }

  {   // The kitchen opens onto the hall and the hall onto the garden: a leak, no draft, until its doorway is closed.
    const t = await fresh('house.mlgeo'), sig = t.sig(), depth = t.depth();
    const r = t.api.wallsSuggest([11, 3]), l = t.s.walls.leak;
    check('a room open to the outdoors reports a leak at the edge of the working region', !!r.leak && !!l && l.edge > 0, text(r.leak && { edge: r.leak.edge, drop: r.leak.drop }));
    check('a leak proposes nothing, and nothing can be added', !t.s.walls.draft && t.api.wallsAccept() === false && t.sig() === sig && t.depth() === depth);
    const gap = l && l.gap;
    check('the uncertain edge shown is the kitchen doorway', !!gap && Math.hypot(gap.mid[0] - 10.5, gap.mid[1] - 6) < 0.6 && gap.width > 0.6 && gap.width < 1.4, text(gap));
    t.api.wallsCloseGap();
    const d = t.s.walls.draft;
    check('with a barrier across the doorway the kitchen is suggested', !!d && !t.s.walls.leak && d.parts.length === 1 && near(box(d.parts[0].outline), KITCHEN, 0.3), d && text(d.parts));
    check('barriers and the suggestion leave the scenario and the undo history alone', t.sig() === sig && t.depth() === depth);
  }

  {   // The hall: the open front door leaks to the garden, then the stairwell leaks downstairs; barriers bound both.
    const t = await fresh('house.mlgeo'), sig = t.sig(), depth = t.depth();
    t.api.wallsSuggest([10, 9]);
    let l = t.s.walls.leak;
    check('the hall leaks through the open front door, which is shown as the uncertain edge', !!l && l.edge > 0 && !!l.gap && Math.hypot(l.gap.mid[0] - 4.8, l.gap.mid[1] - 12) < 0.8 && !t.s.walls.draft, l && text(l.gap));
    t.api.wallsCloseGap();
    l = t.s.walls.leak;
    check('with the front door closed the fill stops at the stairwell: missing floor, not the region edge', !!l && l.drop > 0 && l.edge === 0 && !t.s.walls.draft, l && text({ edge: l.edge, drop: l.drop }));
    t.api.wallsBarrierAdd([18, 4.4], [18, 6]);
    const d = t.s.walls.draft, P = d ? d.parts : [];
    check('with a barrier across the stairwell the ground floor is suggested', !!d && !t.s.walls.leak);
    check('the suggestion holds the hall, the kitchen and the back room', t.inside(P, 10, 9) && t.inside(P, 11, 3) && t.inside(P, 16, 2) && t.inside(P, 10.5, 6));
    check('the suggestion leaves out the stairwell, the closed room and the garden', !t.inside(P, 19, 2) && !t.inside(P, 19, 5.2) && !t.inside(P, 2, 2) && !t.inside(P, 4.8, 13), text(P));
    check('until it is added the scenario and the undo history are untouched', t.sig() === sig && t.depth() === depth);
    check('adding it is one undo step that passes the area rules', t.api.wallsAccept() && t.depth() === depth + 1 && t.api.areaFindings(t.area()).length === 0, text(t.api.areaFindings(t.area())));
  }
}
