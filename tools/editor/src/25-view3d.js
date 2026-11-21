// ------------------------------------------------------------------ 3D view
// Game coordinates are left-handed (x right, y up, z forward); the 3D library is right-handed,
// so everything is drawn at (x, y, -z). That keeps the map the same way round as the flat view.
// The map looks like an engine viewport's clay model: solid geometry lit from the sky with its creases outlined, a
// ten-metre grid under it and fog that fades what is far away. Markers keep the flat map's language: the same role
// shapes on the floor, the same labels, and the same blue ring around what is selected.
const view3d = (() => {
  const T = window.THREE, cv = $('view3d');
  let renderer = null, scene, camera, mapMesh = null, borderLines = null, grid = null, geoMesh = null, geoEdges = null, ghostMesh = null, ghostEdges = null;
  let enemyGroup, extraGroup, liveGroup, areaGroup, raf = 0, last = 0, mono = 'monospace';
  let yaw = 0, pitch = -0.7, started = false;
  const keys = new Set(), fly = new Set(['w', 'a', 's', 'd', 'q', 'e', 'shift']);
  const col = name => new T.Color(css[name]);
  function available() { if (!T) return false; if (renderer) return true; try { renderer = new T.WebGLRenderer({ canvas: cv, antialias: true }); } catch (e) { renderer = null; return false; } setup(); return true; }
  function setup() {
    scene = new T.Scene(); camera = new T.PerspectiveCamera(62, 1, 0.1, 3000);
    // A studio light: a bright sky with a dim bounce from the floor, a key from one side and a weak fill from the other,
    // so roofs and floors come out lighter than walls, and each side of a building gets a tone of its own.
    scene.add(new T.HemisphereLight(0xffffff, 0x6f737a, 0.7));
    const key = new T.DirectionalLight(0xffffff, 0.5); key.position.set(0.45, 1, 0.3);
    const fill = new T.DirectionalLight(0xffffff, 0.18); fill.position.set(-0.6, 0.35, -0.7);
    scene.add(key, fill);
    scene.fog = new T.Fog(0x000000, 60, 400);              // coloured from --paper at every frame; distances follow the map
    enemyGroup = new T.Group(); extraGroup = new T.Group(); liveGroup = new T.Group(); areaGroup = new T.Group(); scene.add(enemyGroup, extraGroup, liveGroup, areaGroup);
    let down = null;
    cv.addEventListener('pointerdown', ev => {
      cv.setPointerCapture(ev.pointerId);
      down = { x: ev.clientX, y: ev.clientY, moved: false, button: ev.button, grab: -1, item: -1, saved: false };
      if (ev.button === 0 && state.tool === 'select') {
        const item = cast(ev).intersectObjects(enemyGroup.children, true).find(h => Number.isInteger(h.object.userData.objective) && !outlinerBlocks((state.objectives[h.object.userData.objective] || {}).pos)), h = enemyHit(ev);
        if (item && (!h || item.distance < h.distance)) down.item = item.object.userData.objective;
        else if (h) { down.grab = h.object.userData.enemy; select(down.grab); }
      }
    });
    cv.addEventListener('pointermove', ev => {
      if (!down) return;
      const dx = ev.clientX - down.x, dy = ev.clientY - down.y;
      if (!down.moved && Math.hypot(dx, dy) < 4) return;
      down.moved = true; down.x = ev.clientX; down.y = ev.clientY;
      if (down.item >= 0) {                                // dragging an intel item: it lies exactly where the cursor touches
        const o = state.objectives[down.item], hit = ground(ev);
        if (!o || !hit) return;
        if (!down.saved) { txBegin('Move intel ' + (o.label || o.name)); down.saved = true; }
        o.pos = [hit.point.x, hit.point.y + 0.02, -hit.point.z]; o.moved = true; syncEnemies();
        return;
      }
      if (down.grab >= 0) {                                // dragging an enemy across the floor
        const e = state.enemies[down.grab], hit = ground(ev);
        if (!e || !hit) return;
        const p = [hit.point.x, hit.point.y, -hit.point.z], level = levelsAt(p[0], p[2]).filter(y => Math.abs(y - p[1]) <= 1.0).sort((a, b) => Math.abs(a - p[1]) - Math.abs(b - p[1]))[0];
        if (level === undefined && !e.fixed) return;       // only a fixed sentry may stand where nobody can walk
        if (!down.saved) { txBegin('Move enemy ' + (down.grab + 1)); down.saved = true; }
        e.pos = level !== undefined ? [p[0], level, p[2]] : p; syncEnemies();
        return;
      }
      yaw -= dx * 0.005; pitch = Math.max(-1.55, Math.min(1.55, pitch - dy * 0.005)); invalidate3();
    });
    cv.addEventListener('pointerup', ev => { const d = down; down = null; if (!d) return; if (d.saved) { txCommit(); return changed(); } if (!d.moved && (d.button === 0 || d.button === 2)) pick(ev, d.button); });
    cv.addEventListener('pointercancel', () => { const d = down; down = null; if (d && d.saved) txCancel(); });     // a drag that did not finish leaves no change
    cv.addEventListener('contextmenu', ev => ev.preventDefault());
    cv.addEventListener('wheel', ev => { ev.preventDefault(); move(0, 0, -ev.deltaY * 0.03 * speed()); invalidate3(); }, { passive: false });
    new ResizeObserver(invalidate3).observe(cv);
  }
  const forward = () => new T.Vector3(-Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), -Math.cos(yaw) * Math.cos(pitch));
  const speed = () => keys.has('shift') ? 3 : 1;
  function move(right, up, ahead) {
    const f = forward(), r = new T.Vector3(Math.cos(yaw), 0, -Math.sin(yaw));
    camera.position.addScaledVector(f, ahead).addScaledVector(r, right); camera.position.y += up;
  }
  function home() {
    if (!renderer) return;
    const b = state.map ? state.map.bounds : { minX: -40, maxX: 40, minY: 0, maxY: 4, minZ: -40, maxZ: 40 };
    const span = Math.max(b.maxX - b.minX, b.maxZ - b.minZ);
    const sel = state.enemies[state.selected];
    if (sel) { camera.position.set(sel.pos[0], sel.pos[1] + 14, -(sel.pos[2] - 16)); yaw = 0; pitch = -0.72; }
    else { camera.position.set((b.minX + b.maxX) / 2, b.maxY + span * 0.45, -((b.minZ + b.maxZ) / 2 - span * 0.5)); yaw = 0; pitch = -0.75; }
    invalidate3();
  }
  // Where the camera looks at ground level, for the floor buttons.
  function focus() {
    if (!renderer) return [0, 0];
    const f = forward(), p = camera.position, t = f.y < -0.05 ? Math.min(200, Math.max(0, (p.y - (state.map ? state.map.bounds.minY : 0)) / -f.y)) : 15;
    return [p.x + f.x * t, -(p.z + f.z * t)];
  }
  function drop(...list) { for (const o of list) if (o) { scene.remove(o); o.geometry.dispose(); o.material.dispose(); } }
  function clear(grp) {
    while (grp.children.length) grp.children.pop().traverse(c => { if (c.geometry) c.geometry.dispose(); if (c.material) { if (c.material.map) c.material.map.dispose(); c.material.dispose(); } });
  }
  const lines = a => { const g = new T.BufferGeometry(); g.setAttribute('position', new T.BufferAttribute(a, 3)); return g; };
  function buildMap() {
    drop(mapMesh, borderLines, grid); mapMesh = borderLines = grid = null;
    const m = state.map; if (!m) return;
    const pos = new Float32Array(m.n * 9), colr = new Float32Array(m.n * 9), lo = col('ground-lo'), hi = col('ground-hi'), span = (m.bounds.maxY - m.bounds.minY) || 1;
    for (let i = 0; i < m.n * 3; i++) {
      pos[i * 3] = m.tris[i * 3]; pos[i * 3 + 1] = m.tris[i * 3 + 1] + (state.geo ? 0.04 : 0); pos[i * 3 + 2] = -m.tris[i * 3 + 2];
      const k = (m.avgY[Math.floor(i / 3)] - m.bounds.minY) / span;
      colr[i * 3] = lo.r + (hi.r - lo.r) * k; colr[i * 3 + 1] = lo.g + (hi.g - lo.g) * k; colr[i * 3 + 2] = lo.b + (hi.b - lo.b) * k;
    }
    const g = new T.BufferGeometry(); g.setAttribute('position', new T.BufferAttribute(pos, 3)); g.setAttribute('color', new T.BufferAttribute(colr, 3));
    // With the solid map loaded, the walkable ground becomes a thin film over its floors; on its own it is the map,
    // shaded from low to high as on the flat map.
    mapMesh = new T.Mesh(g, state.geo ? new T.MeshBasicMaterial({ color: col('ground-hi'), transparent: true, opacity: 0.4, side: T.DoubleSide, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -1 })
                                      : new T.MeshBasicMaterial({ vertexColors: true, side: T.DoubleSide }));
    const e = m.borders, lp = new Float32Array(e.length / 5 * 6);
    for (let i = 0, o = 0; i < e.length; i += 5, o += 6) { lp[o] = e[i]; lp[o + 1] = e[i + 4] + 0.06; lp[o + 2] = -e[i + 1]; lp[o + 3] = e[i + 2]; lp[o + 4] = e[i + 4] + 0.06; lp[o + 5] = -e[i + 3]; }
    borderLines = new T.LineSegments(lines(lp), new T.LineBasicMaterial({ color: col('wall'), transparent: true, opacity: state.geo ? 0.5 : 0.9 }));
    // A ten-metre grid a metre under the lowest floor, for scale; its lines lie on whole tens, as the game counts. Any
    // closer and it shows through flat ground lying at that height.
    const b = m.bounds, size = Math.max(40, Math.ceil(Math.max(b.maxX - b.minX, b.maxZ - b.minZ) * 1.5 / 20) * 20), ten = v => Math.round(v / 10) * 10;
    grid = new T.GridHelper(size, size / 10, col('line'), col('line'));
    grid.position.set(ten((b.minX + b.maxX) / 2), b.minY - 1, -ten((b.minZ + b.maxZ) / 2));
    grid.material.transparent = true; grid.material.opacity = 0.7; grid.material.depthWrite = false;
    const reach = Math.max(b.maxX - b.minX, b.maxZ - b.minZ, 40);
    scene.fog.near = reach * 0.4; scene.fog.far = reach * 2.4;
    scene.add(grid, mapMesh, borderLines);
  }
  // The outline of the solid map: every edge where the surface folds by more than 30 degrees, and every open edge.
  // Points on the same spot are welded first (a model repeats them along each hard edge), so a floor laid in tiles
  // shows no seams. Returns the ends of each line, three numbers per end.
  function creases(p, idx) {
    const weld = new Int32Array(p.length / 3).fill(-1), spot = new Map(); let n = 0;
    const id = v => {
      if (weld[v] < 0) { const k = Math.round(p[v * 3] * 100) + ',' + Math.round(p[v * 3 + 1] * 100) + ',' + Math.round(p[v * 3 + 2] * 100); let w = spot.get(k); if (w === undefined) { w = n++; spot.set(k, w); } weld[v] = w; }
      return weld[v];
    };
    const tris = idx.length / 3, nrm = new Float32Array(idx.length), first = new Map(), out = [], fold = Math.cos(Math.PI / 6);
    for (let t = 0; t < tris; t++) {
      const a = idx[t * 3] * 3, b = idx[t * 3 + 1] * 3, c = idx[t * 3 + 2] * 3;
      const ux = p[b] - p[a], uy = p[b + 1] - p[a + 1], uz = p[b + 2] - p[a + 2], vx = p[c] - p[a], vy = p[c + 1] - p[a + 1], vz = p[c + 2] - p[a + 2];
      const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, l = Math.hypot(nx, ny, nz) || 1;
      nrm[t * 3] = nx / l; nrm[t * 3 + 1] = ny / l; nrm[t * 3 + 2] = nz / l;
    }
    const push = (a, b) => out.push(p[a * 3], p[a * 3 + 1], p[a * 3 + 2], p[b * 3], p[b * 3 + 1], p[b * 3 + 2]);
    for (let t = 0; t < tris; t++) for (let k = 0; k < 3; k++) {
      const va = idx[t * 3 + k], vb = idx[t * 3 + (k + 1) % 3], a = id(va), b = id(vb);
      if (a === b) continue;
      const key = a < b ? a * 67108864 + b : b * 67108864 + a, seen = first.get(key);    // welded ids stay far below 2^26
      if (seen === undefined) { first.set(key, t * 3 + k); continue; }
      if (seen < 0) continue;                              // a third face on the same edge adds nothing
      const s = (seen / 3) | 0;
      if (nrm[s * 3] * nrm[t * 3] + nrm[s * 3 + 1] * nrm[t * 3 + 1] + nrm[s * 3 + 2] * nrm[t * 3 + 2] < fold) push(va, vb);
      first.set(key, -1);
    }
    for (const v of first.values()) if (v >= 0) { const t = (v / 3) | 0, k = v % 3; push(idx[t * 3 + k], idx[t * 3 + (k + 1) % 3]); }
    return new Float32Array(out);
  }
  function buildGeo() {
    drop(geoMesh, geoEdges, ghostMesh, ghostEdges); geoMesh = geoEdges = ghostMesh = ghostEdges = null;
    const geo = state.geo; if (!geo) return;
    const p = new Float32Array(geo.positions.length);
    for (let i = 0; i < p.length; i += 3) { p[i] = geo.positions[i]; p[i + 1] = geo.positions[i + 1]; p[i + 2] = -geo.positions[i + 2]; }
    const solid = geoIndices(), g = new T.BufferGeometry(); g.setAttribute('position', new T.BufferAttribute(p, 3)); g.setIndex(new T.BufferAttribute(solid, 1));
    // Pushed back a hair, so its outlines and the walkable film win where they touch it.
    geoMesh = new T.Mesh(g, new T.MeshPhongMaterial({ color: col('model'), specular: new T.Color(0, 0, 0), flatShading: true, side: T.DoubleSide, polygonOffset: true, polygonOffsetFactor: 1, polygonOffsetUnits: 1 }));
    geoEdges = new T.LineSegments(lines(creases(p, solid)), new T.LineBasicMaterial({ color: col('edge'), transparent: true, opacity: 0.55 }));
    scene.add(geoMesh, geoEdges);
    const walls = geoIndices(true);
    if (walls.length) {                                    // walls you cannot see in the game but cannot walk through either: a faint skin, a dashed outline
      const wg = new T.BufferGeometry(); wg.setAttribute('position', g.getAttribute('position')); wg.setIndex(new T.BufferAttribute(walls, 1));
      ghostMesh = new T.Mesh(wg, new T.MeshBasicMaterial({ color: col('wall'), transparent: true, opacity: 0.05, side: T.DoubleSide, depthWrite: false }));
      ghostEdges = new T.LineSegments(lines(creases(p, walls)), new T.LineDashedMaterial({ color: col('wall'), dashSize: 0.8, gapSize: 0.5, transparent: true, opacity: 0.75 }));
      ghostEdges.computeLineDistances();
      scene.add(ghostMesh, ghostEdges);
    }
  }
  // A theme change only repaints the solid map: finding its creases again would take a moment on a big map.
  function tint() {
    if (geoMesh) geoMesh.material.color.set(css.model);
    if (geoEdges) geoEdges.material.color.set(css.edge);
    for (const o of [ghostMesh, ghostEdges]) if (o) o.material.color.set(css.wall);
  }
  // ----- markers
  // Markers keep their colour at any distance: the fog is for the map, not for what is being worked on.
  const flat = (colour, o) => new T.MeshBasicMaterial(Object.assign({ color: colour, fog: false }, o));
  const lit = (colour, o) => new T.MeshPhongMaterial(Object.assign({ color: colour, fog: false, specular: new T.Color(0, 0, 0) }, o));
  // Shapes that lie on the floor, at game position p (or the middle of a group), drawn over the walkable film.
  function lay(geo, material, p, lift) {
    Object.assign(material, { transparent: true, depthWrite: false, side: T.DoubleSide, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 });
    const m = new T.Mesh(geo, material); m.rotation.x = -Math.PI / 2;    // made in x and y; a quarter turn about x lays y along -z
    if (p) m.position.set(p[0], p[1] + lift, -p[2]); else m.position.y = lift;
    return m;
  }
  const circle = (c, r, lift) => { const pts = []; for (let a = 0; a <= 72; a++) pts.push(new T.Vector3(c[0] + Math.cos(a / 72 * 6.2832) * r, c[1] + lift, -c[2] + Math.sin(a / 72 * 6.2832) * r)); return pts; };
  // A dashed line (routes, how far an enemy roams, zones), and a faint copy of it that shows through walls.
  function dashed(group, pts, colour, opacity, dash) {
    const g = new T.BufferGeometry().setFromPoints(pts);
    for (const behind of [false, true]) {
      const l = new T.Line(g, new T.LineDashedMaterial({ color: colour, dashSize: dash, gapSize: dash * 0.6, transparent: true, opacity: behind ? opacity * 0.3 : opacity, depthTest: !behind, fog: false }));
      l.computeLineDistances(); l.renderOrder = behind ? 1 : 0; group.add(l);
    }
  }
  // The role shapes of the flat map, in metres: a square guard, a round wanderer, a diamond patrol.
  function glyph(role, r) {
    if (role === 'Guard') return new T.PlaneGeometry(2 * r, 2 * r);
    if (role === 'Wander') return new T.CircleGeometry(r * 1.12, 28);
    const d = r * 1.35;
    return new T.ShapeGeometry(new T.Shape([new T.Vector2(0, d), new T.Vector2(d, 0), new T.Vector2(0, -d), new T.Vector2(-d, 0)]));
  }
  const wedge = (from, to, half) => new T.ShapeGeometry(new T.Shape([new T.Vector2(-half, from), new T.Vector2(half, from), new T.Vector2(0, to)]));
  // Somebody on the map: the role shape on the floor, a body with a visor on the side it faces, and a wedge on the floor
  // pointing the same way. who: { role, vehicle, prone }; a player is round, as on the flat map.
  function figure(who, colour, opacity, enemy) {
    const g = new T.Group(), see = opacity < 1, skin = lit(colour, { emissive: colour, emissiveIntensity: 0.2, transparent: see, opacity }), parts = [];
    if (who.vehicle) { const m = new T.Mesh(new T.BoxGeometry(2, 1.8, 5.2), skin); m.position.y = 0.9; parts.push(m); }
    else if (who.prone) { const m = new T.Mesh(new T.CapsuleGeometry(0.22, 1.25, 4, 12), skin); m.rotation.x = Math.PI / 2; m.position.y = 0.24; parts.push(m); }
    else {
      const m = new T.Mesh(new T.CapsuleGeometry(0.26, 1.2, 4, 12), skin); m.position.y = 0.86;
      const visor = new T.Mesh(new T.BoxGeometry(0.32, 0.09, 0.16), lit(col('panel'), { transparent: see, opacity })); visor.position.set(0, 1.5, -0.21);
      parts.push(m, visor);
    }
    parts.push(lay(who.vehicle ? new T.PlaneGeometry(2.4, 5.6) : glyph(who.role, 0.48), flat(colour, { opacity: 0.9 * opacity }), null, 0.06));
    parts.push(lay(who.vehicle ? wedge(2.9, 4.1, 0.6) : wedge(0.72, 1.45, 0.2), flat(colour, { opacity: 0.9 * opacity }), null, 0.06));
    for (const o of parts) { if (enemy !== undefined) o.userData.enemy = enemy; g.add(o); }
    return g;
  }
  // A ring on the floor: the selection (solid), or a dashed one as on the flat map (squad, something to check).
  function hoop(group, r, colour, dash) {
    if (dash) { dashed(group, circle([0, 0, 0], r, 0.07), colour, 0.95, dash); return; }
    group.add(lay(new T.RingGeometry(r - 0.11, r, 48), flat(colour), null, 0.07));
  }
  // A label that keeps its size on screen and shows through walls: a small tag in the panel colour, in the flat map's
  // face and colours, so it reads the same over a light roof as over the dark ground.
  function label(text, colour, opacity) {
    const k = dpr(), size = 11 * k, font = '600 ' + size + 'px ' + mono, c = document.createElement('canvas'); let x = c.getContext('2d');
    x.font = font; c.width = Math.ceil(x.measureText(text).width + 12 * k); c.height = Math.ceil(size + 8 * k);
    x = c.getContext('2d'); x.beginPath();
    if (x.roundRect) x.roundRect(k / 2, k / 2, c.width - k, c.height - k, 3 * k); else x.rect(k / 2, k / 2, c.width - k, c.height - k);
    x.globalAlpha = 0.9; x.fillStyle = css.panel; x.fill(); x.globalAlpha = 1; x.lineWidth = k; x.strokeStyle = css.line; x.stroke();
    x.font = font; x.textAlign = 'center'; x.textBaseline = 'middle'; x.fillStyle = colour || css.ink; x.fillText(text, c.width / 2, c.height / 2 + k / 2);
    const t = new T.CanvasTexture(c); t.generateMipmaps = false; t.minFilter = T.LinearFilter;
    const s = new T.Sprite(new T.SpriteMaterial({ map: t, depthTest: false, sizeAttenuation: false, fog: false, transparent: true, opacity: opacity === undefined ? 1 : opacity }));
    s.center.set(0.5, 0); s.renderOrder = 5; s.userData.px = [c.width / k, c.height / k];
    return s;
  }
  const at = (o, p, lift) => { o.position.set(p[0], p[1] + lift, -p[2]); return o; };
  function syncEnemies() {
    if (!renderer) return;
    clear(enemyGroup); clear(extraGroup);
    mono = getComputedStyle(document.body).getPropertyValue('--mono').trim() || 'monospace';
    const red = col('enemy'), blue = col('friend'), amber = col('warn'), ink = col('ink'), chosen = col('accent-text');
    const sealed = sealedHides();          // a sealed mission, not revealed (20b-sealed.js): no enemies, routes, zones or placed things
    state.enemies.forEach((e, i) => {
      if (sealed) return;
      const fade = outlinerAlpha(e.pos);                   // hidden, faint or locked in Floors and rooms (29k-outliner.js)
      if (!fade) return;
      const ghost = live.on && !e.live, sel = i === state.selected, size = e.vehicle ? 3.3 : 0.95;    // ghost: placed, but not in the game (yet)
      const g = figure(e, red, (ghost ? 0.35 : 1) * fade, i);
      if (sel) hoop(g, size + 0.25, chosen);
      if (e.hvt) hoop(g, size, amber);
      if (warningFor(e)) hoop(g, size - 0.15, amber, 0.3);
      if (state.squad.includes(i)) hoop(g, size + 0.55, blue, 0.4);
      at(g, e.pos, 0).rotation.y = -e.yaw * Math.PI / 180; enemyGroup.add(g);
      if (e.hvt) { const halo = new T.Mesh(new T.TorusGeometry(0.42, 0.05, 8, 32), flat(amber)); halo.rotation.x = Math.PI / 2; extraGroup.add(at(halo, e.pos, e.prone ? 0.75 : 1.98)); }
      const tag = String(i + 1) + (e.vehicle ? ' TRUCK' : e.hvt ? ' TARGET' : e.trigger ? ' \u2192 ' + trigTag(e.trigger) : '');
      extraGroup.add(at(label(tag, e.hvt ? css.warn : e.trigger ? css.friend : css.ink, fade), e.pos, e.vehicle ? 2.1 : e.prone ? 0.85 : 2.1));
      if (e.role === 'Patrol' && !e.fixed && e.patrol.length) {
        const stops = e.patrol.map(p => new T.Vector3(p[0], p[1] + 0.25, -p[2]));
        dashed(extraGroup, e.patrol.length > 2 ? [...stops, stops[0]] : stops, red, (sel ? 1 : 0.75) * fade, 0.9);
        extraGroup.add(new T.Line(new T.BufferGeometry().setFromPoints([new T.Vector3(e.pos[0], e.pos[1] + 0.25, -e.pos[2]), stops[0]]), new T.LineBasicMaterial({ color: red, transparent: true, opacity: 0.45, fog: false })));
        e.patrol.forEach((p, n) => {
          extraGroup.add(at(new T.Mesh(new T.SphereGeometry(sel ? 0.22 : 0.14, 12, 8), flat(red)), p, 0.25));
          if (sel) extraGroup.add(at(label(String(n + 1), css.ink), p, 0.45));
        });
      }
      if (e.role === 'Wander' && !e.fixed && e.wanderDistance > 0) {
        dashed(extraGroup, circle(e.pos, e.wanderDistance, 0.2), red, (sel ? 0.95 : 0.55) * fade, 0.9);
        extraGroup.add(lay(new T.CircleGeometry(e.wanderDistance, 64), flat(red, { opacity: sel ? 0.08 : 0.035 }), e.pos, 0.2));
      }
    });
    state.zones.forEach(z => {
      if (sealed) return;
      if (z.when !== 'zone' || !isVec(z.pos)) return;
      const fade = outlinerAlpha(z.pos);
      if (!fade) return;
      const done = state.fired.includes(z.id), a = (done ? 0.4 : 0.9) * fade;
      if (!(z.area && z.area.parts && z.area.parts.length)) {        // a painted area is a volume of its own, see areas()
        dashed(extraGroup, circle(z.pos, z.radius, 0.3), blue, a, 1.2);
        extraGroup.add(lay(new T.CircleGeometry(z.radius, 72), flat(blue, { opacity: done ? 0.03 : 0.07 }), z.pos, 0.3));
      }
      extraGroup.add(at(new T.Mesh(new T.CylinderGeometry(0.06, 0.06, 3, 6), flat(blue, { transparent: done || fade < 1, opacity: a })), z.pos, 1.5));
      extraGroup.add(at(label('Zone ' + z.id, css.friend, a), z.pos, 3.1));
    });
    // An area whose holder is hidden is left out; one that is faint or locked is drawn faint.
    areas(sealed ? [] : areaVolumes().map(one => { const f = outlinerAreaAlpha(one.area); return f === 1 ? one : f ? Object.assign({}, one, { strong: false, faint: true }) : null; }).filter(Boolean));
    for (const [n, t] of (sealed ? [] : state.things).entries()) {
      const fade = outlinerAlpha(t.pos);
      if (!fade) continue;
      const bar = at(new T.Mesh(new T.BoxGeometry(0.9, 0.12, 0.2), lit(ink, { transparent: fade < 1, opacity: fade })), t.pos, 0.08); bar.rotation.y = -(t.yaw || 0) * Math.PI / 180; extraGroup.add(bar);
      if (n === state.thing) { const g = at(new T.Group(), t.pos, 0); hoop(g, 0.8, chosen); extraGroup.add(g); }
      extraGroup.add(at(label(t.name.trim().slice(0, 14) + (t.trigger ? ' \u2192 ' + trigTag(t.trigger) : ''), css.ink, fade), t.pos, 0.6));
    }
    state.objectives.forEach((o, n) => {
      const fade = outlinerAlpha(o.pos), a = (o.obtained ? 0.35 : 1) * fade;
      if (!fade) return;
      const box = new T.Mesh(new T.BoxGeometry(0.35, 0.35, 0.35), lit(amber, { emissive: amber, emissiveIntensity: 0.5, transparent: a < 1, opacity: a }));
      box.rotation.y = Math.PI / 4; box.userData.objective = n; enemyGroup.add(at(box, o.pos, 0.18));
      extraGroup.add(at(label(o.label.slice(0, 24), css.warn, a), o.pos, 0.6));
    });
    for (const p of state.infils) extraGroup.add(at(new T.Mesh(new T.ConeGeometry(0.5, 1.4, 4), flat(blue)), p, 0.7));
    // Doors: a see-through slab over the leaf. Faint is left to the game, red is locked, blue is unlocked; the selected
    // one is outlined in the selection blue.
    const grey = col('ink-2');
    state.doors.forEach((d, n) => {
      const fade = outlinerAlpha(d.mid);
      if (!fade) return;
      const dx = d.mid[0] - d.pos[0], dz = d.mid[2] - d.pos[2], half = Math.hypot(dx, dz), set = doorSet(d), lockedNow = d.locked === true || (d.locked === null && d.isLocked === true);
      const slab = new T.Mesh(new T.BoxGeometry(half > 0.15 ? half * 2 : 0.9, 2.0, 0.2),
        flat(d.locked === true || (!set && lockedNow) ? red : d.locked === false ? blue : grey, { transparent: true, opacity: (n === state.door ? 0.9 : set ? 0.65 : lockedNow ? 0.4 : 0.2) * fade, depthWrite: false }));
      slab.position.set(d.mid[0], d.mid[1], -d.mid[2]); slab.rotation.y = Math.atan2(dz, dx); slab.userData.door = n; enemyGroup.add(slab);
      if (n === state.door) {                              // drawn over walls too, so the selected door can be found from anywhere
        const box = new T.LineSegments(new T.EdgesGeometry(slab.geometry), new T.LineBasicMaterial({ color: chosen, fog: false, depthTest: false, transparent: true }));
        box.position.copy(slab.position); box.rotation.copy(slab.rotation); box.renderOrder = 6; extraGroup.add(box);
      }
      if (d.enemiesCanOpen === false) { const bar = new T.Mesh(new T.BoxGeometry(0.12, 0.12, 1.1), flat(ink)); bar.position.copy(slab.position); bar.rotation.y = slab.rotation.y; extraGroup.add(bar); }
    });
    invalidate3();
  }
  // Painted areas as see-through volumes, each part from the bottom to the top of its floor band, holes cut out, so floors,
  // stairs, courtyards and what is left out can be looked at from any side. list: [{ area, colour (a css name), strong, faint }].
  function areas(list) {
    if (!renderer) return;
    clear(areaGroup);
    for (const one of list || []) {
      const colour = col(one.colour || 'friend');
      for (const part of (one.area && one.area.parts) || []) {
        const bottom = part.floor[0], top = part.floor[1];
        if (!(top > bottom) || part.outline.length < 3) continue;
        let g = null;
        try {
          const shape = new T.Shape(part.outline.map(p => new T.Vector2(p[0], p[1])));
          for (const hole of part.holes || []) if (hole.length >= 3) shape.holes.push(new T.Path(hole.map(p => new T.Vector2(p[0], p[1]))));
          g = new T.ExtrudeGeometry(shape, { depth: top - bottom, bevelEnabled: false });
        } catch (e) { continue; }                         // a broken ring is shown as a finding in the panel, not here
        // The shape lies in x and z; turned a quarter about x it stands from y = bottom up, at (x, y, -z) like everything else.
        const mesh = new T.Mesh(g, flat(colour, { transparent: true, opacity: one.strong ? 0.24 : one.faint ? 0.05 : 0.12, side: T.DoubleSide, depthWrite: false }));
        const edges = new T.LineSegments(new T.EdgesGeometry(g), new T.LineBasicMaterial({ color: colour, transparent: true, opacity: one.strong ? 0.95 : one.faint ? 0.3 : 0.6, fog: false }));
        for (const o of [mesh, edges]) { o.rotation.x = -Math.PI / 2; o.position.y = bottom; areaGroup.add(o); }
      }
    }
    invalidate3();
  }
  // Players and the enemies' real positions, redrawn at every update from the game.
  function syncLive() {
    if (!renderer || !live.on) return;
    clear(liveGroup);
    const blue = col('friend'), red = col('enemy'), byId = new Map(state.enemies.map(e => [e.id, e]));
    for (const p of live.players) {
      const g = at(figure({ role: 'Wander' }, blue, 1), p.pos, 0); g.rotation.y = -p.yaw * Math.PI / 180; liveGroup.add(g);
      liveGroup.add(at(label(p.name || 'Player', css.friend), p.pos, 2.1));
    }
    for (const b of sealedHides() ? [] : live.enemies) {
      const home = byId.get(b.marker);
      if (home && Math.hypot(home.pos[0] - b.pos[0], home.pos[2] - b.pos[2]) < 1.2) continue;      // still standing on its marker
      if (home) liveGroup.add(new T.Line(new T.BufferGeometry().setFromPoints([new T.Vector3(home.pos[0], home.pos[1] + 1.1, -home.pos[2]), new T.Vector3(b.pos[0], b.pos[1] + 1.1, -b.pos[2])]), new T.LineBasicMaterial({ color: red, transparent: true, opacity: 0.35, fog: false })));
      liveGroup.add(at(new T.Mesh(new T.SphereGeometry(0.35, 12, 10), lit(red, { emissive: red, emissiveIntensity: 0.3 })), b.pos, 1.1));
    }
    invalidate3();
  }
  function cast(ev) {
    // Figures rebuilt since the last frame (selecting one rebuilds them all) have no world position until a frame is
    // drawn; without this a release that comes before the next frame misses the enemy its press just selected.
    scene.updateMatrixWorld();
    const r = cv.getBoundingClientRect(), ray = new T.Raycaster();
    ray.setFromCamera(new T.Vector2((ev.clientX - r.left) / r.width * 2 - 1, -((ev.clientY - r.top) / r.height * 2 - 1)), camera);
    return ray;
  }
  // Only what is drawn and free can be clicked: floors above the cut-off height are ignored, and so is whatever Floors and
  // rooms hides, locks or leaves faint (29k-outliner.js); the ray goes on to what is free behind it.
  const ground = ev => cast(ev).intersectObjects([geoMesh, mapMesh].filter(Boolean), false).find(h => h.point.y <= state.ceiling + 0.4 && !outlinerBlocks([h.point.x, h.point.y, -h.point.z]));
  const enemyHit = ev => cast(ev).intersectObjects(enemyGroup.children, true).find(h => Number.isInteger(h.object.userData.enemy) && !outlinerBlocks((state.enemies[h.object.userData.enemy] || {}).pos));   // the figure, not its rings
  function screenOf(p) { const v = new T.Vector3(p[0], p[1] + 0.9, -p[2]).project(camera), r = cv.getBoundingClientRect(); return [r.left + (v.x + 1) / 2 * r.width, r.top + (1 - v.y) / 2 * r.height]; }
  function pick(ev, button) {
    const hit = ground(ev), hitEnemy = button === 2 ? null : enemyHit(ev);
    if (hitEnemy && (!hit || hitEnemy.distance <= hit.distance + 0.3)) return click3d(null, hitEnemy.object.userData.enemy, button);
    // A door is clicked through its slab, but not through a wall in front of it.
    const hitDoor = button === 0 && state.tool === 'select' ? cast(ev).intersectObjects(enemyGroup.children, true).find(h => Number.isInteger(h.object.userData.door) && h.point.y <= state.ceiling + 2.4 && !outlinerBlocks((state.doors[h.object.userData.door] || {}).mid)) : null;
    if (hitDoor && (!hit || hitDoor.distance <= hit.distance + 0.3)) return selectDoor(hitDoor.object.userData.door);
    click3d(hit ? [hit.point.x, hit.point.y, -hit.point.z] : null, -1, button);
  }
  function frame(t) {
    raf = 0;
    const dt = Math.min(0.05, (t - last) / 1000 || 0.016); last = t;
    const w = cv.clientWidth, h = cv.clientHeight; if (!w || !h) return;
    if (cv.width !== Math.round(w * dpr()) || cv.height !== Math.round(h * dpr())) { renderer.setPixelRatio(dpr()); renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix(); }
    if (keys.size) {
      const v = 14 * speed() * dt;
      move((keys.has('d') ? v : 0) - (keys.has('a') ? v : 0), (keys.has('e') ? v : 0) - (keys.has('q') ? v : 0), (keys.has('w') ? v : 0) - (keys.has('s') ? v : 0));
    }
    camera.rotation.set(pitch, yaw, 0, 'YXZ');
    const px = 2 / (camera.projectionMatrix.elements[5] * h);       // one screen pixel, for the labels that keep their size
    for (const grp of [extraGroup, liveGroup]) for (const o of grp.children) if (o.userData.px) o.scale.set(o.userData.px[0] * px, o.userData.px[1] * px, 1);
    renderer.clippingPlanes = isFinite(state.ceiling) ? [new T.Plane(new T.Vector3(0, -1, 0), state.ceiling + 0.2)] : [];
    renderer.setClearColor(col('paper')); scene.fog.color.set(css.paper);
    renderer.render(scene, camera);
    if ([...keys].some(k => k !== 'shift')) invalidate3();
  }
  function invalidate3() { if (renderer && state.mode === '3d' && !raf) raf = requestAnimationFrame(frame); }
  return {
    available, home, focus, syncEnemies, syncLive, screenOf, areas, invalidate: invalidate3,
    lookFrom(p) { if (!renderer) return; camera.position.set(p[0], p[1] + 7, -(p[2] - 8)); yaw = 0; pitch = -0.72; invalidate3(); },
    open() { if (!mapMesh && state.map) buildMap(); if (!geoMesh && state.geo) buildGeo(); if (!started) { started = true; home(); } syncEnemies(); },
    dropMap() { if (renderer) { buildMap(); if (state.mode === '3d') home(); } },
    dropGeo() { if (renderer) { buildGeo(); buildMap(); } },
    key(k, down) { if (!fly.has(k)) return false; if (down) keys.add(k); else keys.delete(k); invalidate3(); return true; },
    recolor() { if (renderer) { buildMap(); tint(); syncEnemies(); syncLive(); } },
  };
})();
