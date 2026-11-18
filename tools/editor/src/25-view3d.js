// ------------------------------------------------------------------ 3D view
// Game coordinates are left-handed (x right, y up, z forward); the 3D library is right-handed,
// so everything is drawn at (x, y, -z). That keeps the map the same way round as the flat view.
const view3d = (() => {
  const T = window.THREE, cv = $('view3d');
  let renderer = null, scene, camera, mapMesh = null, borderLines = null, geoMesh = null, ghostMesh = null, enemyGroup, extraGroup, liveGroup, areaGroup, raf = 0, last = 0;
  let yaw = 0, pitch = -0.7, started = false;
  const keys = new Set(), fly = new Set(['w', 'a', 's', 'd', 'q', 'e', 'shift']);
  const col = name => new T.Color(css[name]);
  function available() { if (!T) return false; if (renderer) return true; try { renderer = new T.WebGLRenderer({ canvas: cv, antialias: true }); } catch (e) { renderer = null; return false; } setup(); return true; }
  function setup() {
    scene = new T.Scene(); camera = new T.PerspectiveCamera(62, 1, 0.1, 3000);
    scene.add(new T.HemisphereLight(0xffffff, 0x556070, 0.95));
    const sun = new T.DirectionalLight(0xffffff, 0.55); sun.position.set(0.4, 1, 0.25); scene.add(sun);
    enemyGroup = new T.Group(); extraGroup = new T.Group(); liveGroup = new T.Group(); areaGroup = new T.Group(); scene.add(enemyGroup, extraGroup, liveGroup, areaGroup);
    let down = null;
    cv.addEventListener('pointerdown', ev => {
      cv.setPointerCapture(ev.pointerId);
      down = { x: ev.clientX, y: ev.clientY, moved: false, button: ev.button, grab: -1, item: -1, saved: false };
      if (ev.button === 0 && state.tool === 'select') {
        const item = cast(ev).intersectObjects(enemyGroup.children, true).find(h => Number.isInteger(h.object.userData.objective)), h = enemyHit(ev);
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
        if (!down.saved) { pushUndo(); down.saved = true; }
        o.pos = [hit.point.x, hit.point.y + 0.02, -hit.point.z]; o.moved = true; syncEnemies();
        return;
      }
      if (down.grab >= 0) {                                // dragging an enemy across the floor
        const e = state.enemies[down.grab], hit = ground(ev);
        if (!e || !hit) return;
        const p = [hit.point.x, hit.point.y, -hit.point.z], level = levelsAt(p[0], p[2]).filter(y => Math.abs(y - p[1]) <= 1.0).sort((a, b) => Math.abs(a - p[1]) - Math.abs(b - p[1]))[0];
        if (level === undefined && !e.fixed) return;       // only a fixed sentry may stand where nobody can walk
        if (!down.saved) { pushUndo(); down.saved = true; }
        e.pos = level !== undefined ? [p[0], level, p[2]] : p; syncEnemies();
        return;
      }
      yaw -= dx * 0.005; pitch = Math.max(-1.55, Math.min(1.55, pitch - dy * 0.005)); invalidate3();
    });
    cv.addEventListener('pointerup', ev => { const d = down; down = null; if (!d) return; if (d.saved) return changed(); if (!d.moved && (d.button === 0 || d.button === 2)) pick(ev, d.button); });
    cv.addEventListener('pointercancel', () => { down = null; });
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
  function buildMap() {
    if (mapMesh) { scene.remove(mapMesh, borderLines); mapMesh.geometry.dispose(); borderLines.geometry.dispose(); mapMesh = borderLines = null; }
    const m = state.map; if (!m) return;
    const pos = new Float32Array(m.n * 9), colr = new Float32Array(m.n * 9), lo = col('ground-lo'), hi = col('ground-hi'), span = (m.bounds.maxY - m.bounds.minY) || 1;
    for (let i = 0; i < m.n * 3; i++) {
      pos[i * 3] = m.tris[i * 3]; pos[i * 3 + 1] = m.tris[i * 3 + 1] + (state.geo ? 0.04 : 0); pos[i * 3 + 2] = -m.tris[i * 3 + 2];
      const k = (m.avgY[Math.floor(i / 3)] - m.bounds.minY) / span;
      colr[i * 3] = lo.r + (hi.r - lo.r) * k; colr[i * 3 + 1] = lo.g + (hi.g - lo.g) * k; colr[i * 3 + 2] = lo.b + (hi.b - lo.b) * k;
    }
    const g = new T.BufferGeometry(); g.setAttribute('position', new T.BufferAttribute(pos, 3)); g.setAttribute('color', new T.BufferAttribute(colr, 3));
    // With the real geometry loaded, the walkable ground becomes a thin blue film over the floors.
    mapMesh = new T.Mesh(g, state.geo ? new T.MeshBasicMaterial({ color: col('friend'), transparent: true, opacity: 0.16, side: T.DoubleSide, depthWrite: false })
                                      : new T.MeshBasicMaterial({ vertexColors: true, side: T.DoubleSide }));
    const e = m.borders, lp = new Float32Array(e.length / 5 * 6);
    for (let i = 0, o = 0; i < e.length; i += 5, o += 6) { lp[o] = e[i]; lp[o + 1] = e[i + 4] + 0.06; lp[o + 2] = -e[i + 1]; lp[o + 3] = e[i + 2]; lp[o + 4] = e[i + 4] + 0.06; lp[o + 5] = -e[i + 3]; }
    const lg = new T.BufferGeometry(); lg.setAttribute('position', new T.BufferAttribute(lp, 3));
    borderLines = new T.LineSegments(lg, new T.LineBasicMaterial({ color: col('ink'), transparent: true, opacity: state.geo ? 0.35 : 0.85 }));
    scene.add(mapMesh, borderLines);
  }
  function buildGeo() {
    if (geoMesh) { scene.remove(geoMesh); geoMesh.geometry.dispose(); geoMesh = null; }
    if (ghostMesh) { scene.remove(ghostMesh); ghostMesh.geometry.dispose(); ghostMesh = null; }
    const geo = state.geo; if (!geo) return;
    const p = new Float32Array(geo.positions.length);
    for (let i = 0; i < p.length; i += 3) { p[i] = geo.positions[i]; p[i + 1] = geo.positions[i + 1]; p[i + 2] = -geo.positions[i + 2]; }
    const g = new T.BufferGeometry(); g.setAttribute('position', new T.BufferAttribute(p, 3)); g.setIndex(new T.BufferAttribute(geoIndices(), 1));
    geoMesh = new T.Mesh(g, new T.MeshPhongMaterial({ color: col('ground-lo').lerp(col('ground-hi'), 0.4), flatShading: true, shininess: 0, side: T.DoubleSide }));
    scene.add(geoMesh);
    const walls = geoIndices(true);
    if (walls.length) {                                    // walls you cannot see in the game but cannot walk through either
      const wg = new T.BufferGeometry(); wg.setAttribute('position', g.getAttribute('position')); wg.setIndex(new T.BufferAttribute(walls, 1));
      ghostMesh = new T.Mesh(wg, new T.MeshBasicMaterial({ color: col('enemy'), transparent: true, opacity: 0.16, side: T.DoubleSide, depthWrite: false }));
      scene.add(ghostMesh);
    }
  }
  function label(text) {
    const c = document.createElement('canvas'); c.width = 96; c.height = 48; const x = c.getContext('2d');
    x.font = '600 30px sans-serif'; x.textAlign = 'center'; x.textBaseline = 'middle'; x.lineWidth = 6; x.strokeStyle = css.paper; x.strokeText(text, 48, 24); x.fillStyle = css.ink; x.fillText(text, 48, 24);
    const s = new T.Sprite(new T.SpriteMaterial({ map: new T.CanvasTexture(c), depthTest: false })); s.scale.set(1.6, 0.8, 1); s.renderOrder = 5; return s;
  }
  function syncEnemies() {
    if (!renderer) return;
    for (const grp of [enemyGroup, extraGroup]) while (grp.children.length) { const o = grp.children.pop(); if (o.geometry) o.geometry.dispose(); }
    const red = col('enemy'), ink = col('ink');
    state.enemies.forEach((e, i) => {
      const ghost = live.on && !e.live;                     // placed, but not in the game (yet)
      const sel = i === state.selected, g = new T.Group(), mat = new T.MeshPhongMaterial({ color: red, emissive: sel ? red : 0x000000, emissiveIntensity: 0.45, transparent: ghost, opacity: ghost ? 0.4 : 1 });
      const body = e.vehicle ? new T.Mesh(new T.BoxGeometry(2, 1.8, 5.2), mat) : e.prone ? new T.Mesh(new T.BoxGeometry(0.5, 0.3, 1.7), mat) : new T.Mesh(new T.CylinderGeometry(0.28, 0.28, 1.75, 12), mat);
      body.position.y = e.vehicle ? 0.9 : e.prone ? 0.15 : 0.875; body.userData.enemy = i;
      const nose = new T.Mesh(new T.ConeGeometry(0.2, 0.7, 10), mat); nose.rotation.x = -Math.PI / 2; nose.position.set(0, e.vehicle ? 2.1 : e.prone ? 0.25 : 1.5, -(e.vehicle ? 2.2 : e.prone ? 1.15 : 0.6)); nose.userData.enemy = i;   // points where it looks
      g.add(body, nose); g.position.set(e.pos[0], e.pos[1], -e.pos[2]); g.rotation.y = -e.yaw * Math.PI / 180;
      if (sel) { const ring = new T.Mesh(new T.RingGeometry(0.55, 0.7, 28), new T.MeshBasicMaterial({ color: ink, side: T.DoubleSide })); ring.rotation.x = -Math.PI / 2; ring.position.y = 0.05; g.add(ring); }
      enemyGroup.add(g);
      const tag = label(e.hvt ? 'T' + (i + 1) : String(i + 1)); tag.position.set(e.pos[0], e.pos[1] + (e.prone ? 0.9 : 2.3), -e.pos[2]); extraGroup.add(tag);
      if (e.hvt) { const halo = new T.Mesh(new T.TorusGeometry(0.75, 0.07, 8, 28), new T.MeshBasicMaterial({ color: col('warn') })); halo.rotation.x = Math.PI / 2; halo.position.set(e.pos[0], e.pos[1] + 2.0, -e.pos[2]); extraGroup.add(halo); }
      if (e.role === 'Patrol' && !e.fixed && e.patrol.length) {
        const pts = [e.pos, ...e.patrol, ...(e.patrol.length > 2 ? [e.patrol[0]] : [])].map(p => new T.Vector3(p[0], p[1] + 0.25, -p[2]));
        extraGroup.add(new T.Line(new T.BufferGeometry().setFromPoints(pts), new T.LineBasicMaterial({ color: red, linewidth: 2 })));
        e.patrol.forEach(p => { const s = new T.Mesh(new T.SphereGeometry(sel ? 0.28 : 0.18, 10, 8), new T.MeshBasicMaterial({ color: red })); s.position.set(p[0], p[1] + 0.25, -p[2]); extraGroup.add(s); });
      }
      if (e.role === 'Wander' && !e.fixed && e.wanderDistance > 0) {
        const pts = []; for (let a = 0; a <= 64; a++) pts.push(new T.Vector3(e.pos[0] + Math.cos(a / 64 * 6.2832) * e.wanderDistance, e.pos[1] + 0.2, -e.pos[2] + Math.sin(a / 64 * 6.2832) * e.wanderDistance));
        extraGroup.add(new T.Line(new T.BufferGeometry().setFromPoints(pts), new T.LineBasicMaterial({ color: red, transparent: true, opacity: sel ? 0.95 : 0.55 })));
      }
    });
    const blue = col('friend');
    state.zones.forEach(z => {
      if (z.when !== 'zone' || !isVec(z.pos)) return;
      if (!(z.area && z.area.parts && z.area.parts.length)) {        // a painted area is a volume of its own, see areas()
        const pts = []; for (let a = 0; a <= 72; a++) pts.push(new T.Vector3(z.pos[0] + Math.cos(a / 72 * 6.2832) * z.radius, z.pos[1] + 0.3, -z.pos[2] + Math.sin(a / 72 * 6.2832) * z.radius));
        extraGroup.add(new T.Line(new T.BufferGeometry().setFromPoints(pts), new T.LineBasicMaterial({ color: blue, transparent: true, opacity: state.fired.includes(z.id) ? 0.35 : 1 })));
      }
      const post = new T.Mesh(new T.CylinderGeometry(0.08, 0.08, 3, 6), new T.MeshBasicMaterial({ color: blue })); post.position.set(z.pos[0], z.pos[1] + 1.5, -z.pos[2]); extraGroup.add(post);
      const tag = label('Z' + z.id); tag.position.set(z.pos[0], z.pos[1] + 3.5, -z.pos[2]); extraGroup.add(tag);
    });
    areas(areaVolumes());
    const amber = col('warn');
    for (const t of state.things) {
      const bar = new T.Mesh(new T.BoxGeometry(0.9, 0.12, 0.2), new T.MeshPhongMaterial({ color: col('ink') }));
      bar.position.set(t.pos[0], t.pos[1] + 0.08, -t.pos[2]); bar.rotation.y = -(t.yaw || 0) * Math.PI / 180; extraGroup.add(bar);
      const tag = label(t.name.trim().slice(0, 8)); tag.position.set(t.pos[0], t.pos[1] + 0.7, -t.pos[2]); extraGroup.add(tag);
    }
    state.objectives.forEach((o, n) => {
      const box = new T.Mesh(new T.BoxGeometry(0.35, 0.35, 0.35), new T.MeshPhongMaterial({ color: amber, emissive: amber, emissiveIntensity: 0.5, transparent: o.obtained, opacity: o.obtained ? 0.35 : 1 }));
      box.position.set(o.pos[0], o.pos[1] + 0.18, -o.pos[2]); box.rotation.y = Math.PI / 4; box.userData.objective = n; enemyGroup.add(box);
      const tag = label(o.label.slice(0, 6)); tag.position.set(o.pos[0], o.pos[1] + 0.9, -o.pos[2]); extraGroup.add(tag);
    });
    for (const p of state.infils) { const c = new T.Mesh(new T.ConeGeometry(0.5, 1.4, 4), new T.MeshBasicMaterial({ color: blue })); c.position.set(p[0], p[1] + 0.7, -p[2]); extraGroup.add(c); }
    // Doors: a see-through slab over the leaf. Faint is left to the game, red is locked, blue is unlocked.
    const grey = col('ink-2');
    state.doors.forEach((d, n) => {
      const dx = d.mid[0] - d.pos[0], dz = d.mid[2] - d.pos[2], half = Math.hypot(dx, dz), set = doorSet(d), lockedNow = d.locked === true || (d.locked === null && d.isLocked === true);
      const slab = new T.Mesh(new T.BoxGeometry(half > 0.15 ? half * 2 : 0.9, 2.0, 0.2),
        new T.MeshBasicMaterial({ color: d.locked === true || (!set && lockedNow) ? red : d.locked === false ? blue : grey, transparent: true, opacity: n === state.door ? 0.9 : set ? 0.65 : lockedNow ? 0.4 : 0.2, depthWrite: false }));
      slab.position.set(d.mid[0], d.mid[1], -d.mid[2]); slab.rotation.y = Math.atan2(dz, dx); slab.userData.door = n; enemyGroup.add(slab);
      if (d.enemiesCanOpen === false) { const bar = new T.Mesh(new T.BoxGeometry(0.12, 0.12, 1.1), new T.MeshBasicMaterial({ color: ink })); bar.position.copy(slab.position); bar.rotation.y = slab.rotation.y; extraGroup.add(bar); }
    });
    invalidate3();
  }
  // Painted areas as see-through volumes, each part from the bottom to the top of its floor band, holes cut out, so floors,
  // stairs, courtyards and what is left out can be looked at from any side. list: [{ area, colour (a css name), strong, faint }].
  function areas(list) {
    if (!renderer) return;
    while (areaGroup.children.length) { const o = areaGroup.children.pop(); if (o.geometry) o.geometry.dispose(); if (o.material) o.material.dispose(); }
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
        const mesh = new T.Mesh(g, new T.MeshBasicMaterial({ color: colour, transparent: true, opacity: one.strong ? 0.24 : one.faint ? 0.05 : 0.12, side: T.DoubleSide, depthWrite: false }));
        const edges = new T.LineSegments(new T.EdgesGeometry(g), new T.LineBasicMaterial({ color: colour, transparent: true, opacity: one.strong ? 0.95 : one.faint ? 0.3 : 0.6 }));
        for (const o of [mesh, edges]) { o.rotation.x = -Math.PI / 2; o.position.y = bottom; areaGroup.add(o); }
      }
    }
    invalidate3();
  }
  // Players and the enemies' real positions, redrawn at every update from the game.
  function syncLive() {
    if (!renderer || !live.on) return;
    while (liveGroup.children.length) { const o = liveGroup.children.pop(); o.traverse(c => { if (c.geometry) c.geometry.dispose(); }); }
    const blue = col('friend'), red = col('enemy'), byId = new Map(state.enemies.map(e => [e.id, e]));
    for (const p of live.players) {
      const g = new T.Group(), mat = new T.MeshPhongMaterial({ color: blue });
      const body = new T.Mesh(new T.CylinderGeometry(0.3, 0.3, 1.8, 12), mat); body.position.y = 0.9;
      const nose = new T.Mesh(new T.ConeGeometry(0.22, 0.8, 10), mat); nose.rotation.x = -Math.PI / 2; nose.position.set(0, 1.55, -0.7);
      g.add(body, nose); g.position.set(p.pos[0], p.pos[1], -p.pos[2]); g.rotation.y = -p.yaw * Math.PI / 180; liveGroup.add(g);
    }
    for (const b of live.enemies) {
      const home = byId.get(b.marker);
      if (home && Math.hypot(home.pos[0] - b.pos[0], home.pos[2] - b.pos[2]) < 1.2) continue;
      const dot = new T.Mesh(new T.SphereGeometry(0.35, 12, 10), new T.MeshPhongMaterial({ color: red, emissive: red, emissiveIntensity: 0.3 })); dot.position.set(b.pos[0], b.pos[1] + 1.1, -b.pos[2]); liveGroup.add(dot);
    }
    invalidate3();
  }
  function cast(ev) {
    const r = cv.getBoundingClientRect(), ray = new T.Raycaster();
    ray.setFromCamera(new T.Vector2((ev.clientX - r.left) / r.width * 2 - 1, -((ev.clientY - r.top) / r.height * 2 - 1)), camera);
    return ray;
  }
  // Only what is drawn can be clicked: floors above the cut-off height are ignored.
  const ground = ev => cast(ev).intersectObjects([geoMesh, mapMesh].filter(Boolean), false).find(h => h.point.y <= state.ceiling + 0.4);
  const enemyHit = ev => cast(ev).intersectObjects(enemyGroup.children, true).find(h => Number.isInteger(h.object.userData.enemy));   // the body, not its selection ring
  function screenOf(p) { const v = new T.Vector3(p[0], p[1] + 0.9, -p[2]).project(camera), r = cv.getBoundingClientRect(); return [r.left + (v.x + 1) / 2 * r.width, r.top + (1 - v.y) / 2 * r.height]; }
  function pick(ev, button) {
    const hit = ground(ev), hitEnemy = button === 2 ? null : enemyHit(ev);
    if (hitEnemy && (!hit || hitEnemy.distance <= hit.distance + 0.3)) return click3d(null, hitEnemy.object.userData.enemy, button);
    // A door is clicked through its slab, but not through a wall in front of it.
    const hitDoor = button === 0 && state.tool === 'select' ? cast(ev).intersectObjects(enemyGroup.children, true).find(h => Number.isInteger(h.object.userData.door) && h.point.y <= state.ceiling + 2.4) : null;
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
    renderer.clippingPlanes = isFinite(state.ceiling) ? [new T.Plane(new T.Vector3(0, -1, 0), state.ceiling + 0.2)] : [];
    renderer.setClearColor(col('paper'));
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
    recolor() { if (renderer) { buildMap(); buildGeo(); syncEnemies(); } },
  };
})();

