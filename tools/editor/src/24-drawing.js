// ------------------------------------------------------------------ drawing
const css = {}; let shades = [];
function readTokens() {
  const cs = getComputedStyle(document.documentElement);
  for (const k of ['paper', 'panel', 'ink', 'ink-2', 'line', 'ground-lo', 'ground-hi', 'wall', 'enemy', 'friend', 'warn', 'place', 'accent-text']) css[k] = cs.getPropertyValue('--' + k).trim();
  const rgb = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
  const a = rgb(css['ground-lo']), b = rgb(css['ground-hi']);
  shades = Array.from({ length: 10 }, (_, i) => 'rgb(' + a.map((v, k) => Math.round(v + (b[k] - v) * i / 9)).join(',') + ')');
  if (typeof view3d !== 'undefined') view3d.recolor();
  invalidate();
}
let dirty = false;
function invalidate() { if (state.mode === '3d') { view3d.invalidate(); return; } if (!dirty) { dirty = true; requestAnimationFrame(draw); } }
function groundPaths(m) {
  if (m.paths && m.pathsCeiling === state.ceiling) return m.paths;
  const paths = shades.map(() => new Path2D()), t = m.tris, span = (m.bounds.maxY - m.bounds.minY) || 1;
  for (const i of m.order) {                              // low to high, so upper floors cover lower ones
    if (m.minY[i] > state.ceiling) continue;
    const o = i * 9, p = paths[Math.min(9, Math.floor((m.avgY[i] - m.bounds.minY) / span * 10))];
    p.moveTo(t[o], -t[o + 2]); p.lineTo(t[o + 3], -t[o + 5]); p.lineTo(t[o + 6], -t[o + 8]); p.closePath();
  }
  const border = new Path2D(), e = m.borders;
  for (let i = 0; i < e.length; i += 5) { if (e[i + 4] > state.ceiling + 0.4) continue; border.moveTo(e[i], -e[i + 1]); border.lineTo(e[i + 2], -e[i + 3]); }
  m.paths = paths; m.borderPath = border; m.pathsCeiling = state.ceiling;
  return paths;
}
function shape(role, x, y, r) {
  ctx.beginPath();
  if (role === 'Guard') ctx.rect(x - r, y - r, 2 * r, 2 * r);
  else if (role === 'Wander') ctx.arc(x, y, r * 1.12, 0, Math.PI * 2);
  else { ctx.moveTo(x, y - r * 1.35); ctx.lineTo(x + r * 1.35, y); ctx.lineTo(x, y + r * 1.35); ctx.lineTo(x - r * 1.35, y); ctx.closePath(); }
}
function draw() {
  dirty = false;
  const { w, h } = size(), k = dpr();
  if (canvas.width !== Math.round(w * k) || canvas.height !== Math.round(h * k)) { canvas.width = Math.round(w * k); canvas.height = Math.round(h * k); }
  ctx.setTransform(k, 0, 0, k, 0, 0);
  ctx.fillStyle = css.paper; ctx.fillRect(0, 0, w, h);
  const v = state.view, ox = w / 2 - v.cx * v.scale, oz = h / 2 + v.cz * v.scale;

  const m = state.map;
  if (m) {
    // The ground is drawn from paths kept in world coordinates, so panning and zooming only change the transform.
    const paths = groundPaths(m);
    ctx.save();
    ctx.setTransform(k * v.scale, 0, 0, k * v.scale, k * ox, k * oz);
    ctx.lineJoin = 'round';
    paths.forEach((p, i) => { ctx.fillStyle = ctx.strokeStyle = shades[i]; ctx.lineWidth = 0.8 / v.scale; ctx.fill(p); ctx.stroke(p); });
    ctx.strokeStyle = css.wall; ctx.globalAlpha = 0.9; ctx.lineWidth = Math.min(1.5, Math.max(0.6, v.scale * 0.22)) / v.scale; ctx.stroke(m.borderPath); ctx.globalAlpha = 1;
    ctx.restore();
  }

  // The game's own spawn points, faint, for orientation.
  ctx.fillStyle = css['ink-2']; ctx.globalAlpha = 0.35;
  for (const p of state.authored) { const [x, y] = toScreen(p[0], p[2]); ctx.beginPath(); ctx.arc(x, y, 2.2, 0, Math.PI * 2); ctx.fill(); }
  ctx.globalAlpha = 1;

  // Zones: walk in here and the enemies bound to it appear. A painted area (27a-paint.js) replaces the circle; the anchor
  // keeps a small cross, since labels and rally points still use it.
  const shownFloor = paintShownFloor();
  // A sealed mission that is not revealed (20b-sealed.js) leaves out enemies, routes, sight lines, trigger zones and placed things.
  const sealed = sealedHides();
  state.zones.forEach((z, zi) => {
    if (sealed) return;
    if (z.when !== 'zone' || !isVec(z.pos)) return;
    const [x, y] = toScreen(z.pos[0], z.pos[2]), done = state.fired.includes(z.id), painted = !!(z.area && z.area.parts && z.area.parts.length), editing = paintIs('trigger', zi);
    let top = y - z.radius * v.scale - 3;
    if (painted || editing) {
      drawArea(ctx, z.area, { colour: css.friend, fill: done ? 0.05 : editing ? 0.2 : 0.12, line: done ? 0.4 : 0.95, width: editing ? 2.2 : 1.6, floor: shownFloor });
      ctx.globalAlpha = done ? 0.4 : 0.9; ctx.strokeStyle = css.friend; ctx.lineWidth = 1.6;
      ctx.beginPath(); ctx.moveTo(x - 5, y); ctx.lineTo(x + 5, y); ctx.moveTo(x, y - 5); ctx.lineTo(x, y + 5); ctx.stroke();
      top = y - 7;
    } else {
      ctx.beginPath(); ctx.arc(x, y, z.radius * v.scale, 0, Math.PI * 2);
      ctx.globalAlpha = done ? 0.04 : 0.1; ctx.fillStyle = css.friend; ctx.fill();
      ctx.globalAlpha = done ? 0.4 : 0.9; ctx.setLineDash([8, 5]); ctx.lineWidth = 1.6; ctx.strokeStyle = css.friend; ctx.stroke(); ctx.setLineDash([]);
    }
    ctx.globalAlpha = done ? 0.4 : 0.9;
    ctx.font = '600 12px ' + getComputedStyle(document.body).getPropertyValue('--mono'); ctx.textAlign = 'center'; ctx.textBaseline = 'bottom'; ctx.fillStyle = css.friend; ctx.fillText('Zone ' + z.id, x, top);
    ctx.globalAlpha = 1;
  });

  // Named places (29b-places.js): a pin or a painted outline, with the name.
  drawPlaces();
  // Placement areas, and the selected enemy's scatter circle with the points its rules leave (29g-scatter.js).
  if (!sealed) drawScatter();

  // The mission's intel items.
  state.things.forEach((t, n) => {
    if (sealed) return;
    const [x, y] = toScreen(t.pos[0], t.pos[2]), on = n === state.thing;
    ctx.fillStyle = css.ink; ctx.strokeStyle = css.panel; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.rect(x - 5, y - 5, 10, 10); ctx.fill(); ctx.stroke();
    if (on) { ctx.strokeStyle = css['accent-text']; ctx.lineWidth = 2; ctx.beginPath(); ctx.arc(x, y, 12, 0, Math.PI * 2); ctx.stroke(); }
    const text = t.name.trim().slice(0, 14) + (t.trigger ? ' \u2192 ' + trigTag(t.trigger) : '');
    ctx.font = '600 11px ' + getComputedStyle(document.body).getPropertyValue('--mono'); ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
    ctx.lineWidth = 3; ctx.strokeStyle = css.paper; ctx.strokeText(text, x + 10, y + 1); ctx.fillStyle = css.ink; ctx.fillText(text, x + 10, y + 1);
  });
  for (const o of state.objectives) {
    const [x, y] = toScreen(o.pos[0], o.pos[2]);
    ctx.globalAlpha = o.obtained ? 0.35 : 1;
    ctx.beginPath(); ctx.moveTo(x, y - 8); ctx.lineTo(x + 8, y); ctx.lineTo(x, y + 8); ctx.lineTo(x - 8, y); ctx.closePath();
    ctx.fillStyle = css.warn; ctx.fill(); ctx.lineWidth = 1.5; ctx.strokeStyle = css.panel; ctx.stroke();
    ctx.font = '600 11px ' + getComputedStyle(document.body).getPropertyValue('--mono'); ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
    ctx.lineWidth = 3; ctx.strokeStyle = css.paper; ctx.strokeText(o.label, x + 11, y + 1); ctx.fillStyle = css.warn; ctx.fillText(o.label, x + 11, y + 1);
    ctx.globalAlpha = 1;
  }

  // Danger check: a dashed line from an enemy to the insertion spot it can see.
  dangers().forEach((d, i) => {
    if (sealed) return;
    if (!d) return;
    const e = state.enemies[i], [x0, y0] = toScreen(e.pos[0], e.pos[2]), [x1, y1] = toScreen(d.at[0], d.at[2]);
    ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x1, y1); ctx.setLineDash([6, 5]);
    ctx.lineWidth = i === state.selected ? 2.5 : 1.5; ctx.strokeStyle = css.enemy; ctx.globalAlpha = i === state.selected || d.facing ? 0.9 : 0.5; ctx.stroke();
    ctx.setLineDash([]); ctx.globalAlpha = 1;
  });

  // Doors: a thick line where the leaf is. Grey is left to the game, red is locked, blue is unlocked.
  state.doors.forEach((d, n) => {
    if (d.mid[1] - 1 > state.ceiling + 0.4) return;
    const g = doorLine(d), set = doorSet(d), lockedNow = d.locked === true || (d.locked === null && d.isLocked === true);
    const stroke = (w, c) => { ctx.beginPath(); ctx.moveTo(g[0], g[1]); ctx.lineTo(g[2], g[3]); ctx.lineWidth = w; ctx.strokeStyle = c; ctx.stroke(); };
    ctx.lineCap = 'round'; ctx.globalAlpha = d.broken ? 0.3 : 1;
    stroke(n === state.door ? 13 : set ? 9 : 7, n === state.door ? css['accent-text'] : css.panel);          // a casing, so the door stands out from the ground
    if (n === state.door) stroke(9, css.panel);
    stroke(set ? 6 : 3, d.locked === true ? css.enemy : d.locked === false ? css.friend : css.ink);
    if (!set && lockedNow) { ctx.setLineDash([3, 4]); stroke(2, css.enemy); ctx.setLineDash([]); }      // the game locked this one itself
    const mx = (g[0] + g[2]) / 2, my = (g[1] + g[3]) / 2;
    if (d.locked !== null) {                       // a filled knob is locked, a hollow one unlocked: not told by colour alone
      ctx.beginPath(); ctx.arc(mx, my, 4.5, 0, 6.2832); ctx.fillStyle = d.locked ? css.enemy : css.panel; ctx.fill(); ctx.lineWidth = 1.6; ctx.strokeStyle = d.locked ? css.panel : css.friend; ctx.stroke();
    }
    if (d.enemiesCanOpen === false) {              // a bar across the door: enemies do not get through
      const dx = g[2] - g[0], dy = g[3] - g[1], l = Math.hypot(dx, dy) || 1, px = -dy / l * 8, py = dx / l * 8;
      ctx.beginPath(); ctx.moveTo(mx - px, my - py); ctx.lineTo(mx + px, my + py); ctx.lineWidth = 2.5; ctx.strokeStyle = css.ink; ctx.stroke();
    }
    ctx.lineCap = 'butt'; ctx.globalAlpha = 1;
  });

  // Patrol routes.
  state.enemies.forEach((e, i) => {
    if (sealed) return;
    if (e.role !== 'Patrol' || !e.patrol.length) return;
    const pts = e.patrol.map(p => toScreen(p[0], p[2])), start = toScreen(e.pos[0], e.pos[2]), sel = i === state.selected;
    ctx.strokeStyle = css.enemy; ctx.lineWidth = sel ? 2.4 : 1.6; ctx.setLineDash([6, 4]);
    ctx.beginPath(); pts.forEach((p, n) => n ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1])); if (pts.length > 2) ctx.closePath(); ctx.stroke();
    ctx.globalAlpha = 0.55; ctx.lineWidth = 1.2; ctx.beginPath(); ctx.moveTo(start[0], start[1]); ctx.lineTo(pts[0][0], pts[0][1]); ctx.stroke(); ctx.globalAlpha = 1;
    ctx.setLineDash([]);
    if (sel) pts.forEach((p, n) => {
      ctx.beginPath(); ctx.arc(p[0], p[1], 7, 0, Math.PI * 2); ctx.fillStyle = css.panel; ctx.fill(); ctx.lineWidth = 1.6; ctx.strokeStyle = css.enemy; ctx.stroke();
      ctx.fillStyle = css.ink; ctx.font = '500 10px ' + getComputedStyle(document.body).getPropertyValue('--mono'); ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText(String(n + 1), p[0], p[1] + 0.5);
    });
  });

  // Insertion points.
  for (const p of state.infils) {
    const [x, y] = toScreen(p[0], p[2]);
    ctx.beginPath(); ctx.moveTo(x, y - 7); ctx.lineTo(x + 6, y + 4.5); ctx.lineTo(x - 6, y + 4.5); ctx.closePath();
    ctx.fillStyle = css.friend; ctx.fill(); ctx.lineWidth = 1.2; ctx.strokeStyle = css.panel; ctx.stroke();
  }

  // Enemies.
  const mono = getComputedStyle(document.body).getPropertyValue('--mono');
  state.enemies.forEach((e, i) => {
    if (sealed) return;
    const [x, y] = toScreen(e.pos[0], e.pos[2]), sel = i === state.selected, r = 6.5, bad = warningFor(e);
    const yaw = e.yaw * Math.PI / 180;
    if (state.squad.includes(i)) { ctx.beginPath(); ctx.arc(x, y, 13, 0, 6.2832); ctx.lineWidth = 2; ctx.strokeStyle = css.friend; ctx.setLineDash([4, 3]); ctx.stroke(); ctx.setLineDash([]); }
    if (e.role === 'Wander' && !e.fixed && e.wanderDistance > 0) {            // how far it may roam
      ctx.beginPath(); ctx.arc(x, y, e.wanderDistance * v.scale, 0, Math.PI * 2);
      ctx.globalAlpha = sel ? 0.12 : 0.06; ctx.fillStyle = css.enemy; ctx.fill();
      ctx.globalAlpha = sel ? 0.9 : 0.5; ctx.setLineDash([5, 4]); ctx.lineWidth = sel ? 1.8 : 1.2; ctx.strokeStyle = css.enemy; ctx.stroke(); ctx.setLineDash([]); ctx.globalAlpha = 1;
    }
    const above = e.pos[1] > state.ceiling + 0.4;                            // stands on a floor that is hidden right now
    if (above) ctx.globalAlpha = 0.35;
    ctx.strokeStyle = css.enemy; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + Math.sin(yaw) * (r + 9), y - Math.cos(yaw) * (r + 9)); ctx.stroke();
    if (e.vehicle) {                                   // a gun truck: a box as long as the truck, nose where it faces
      const L = Math.max(10, 5.2 * v.scale), W = Math.max(5, 2 * v.scale);
      ctx.save(); ctx.translate(x, y); ctx.rotate(yaw); ctx.beginPath(); ctx.rect(-W / 2, -L / 2, W, L); ctx.restore();
    } else shape(e.role, x, y, r);
    ctx.fillStyle = live.on && !e.live ? css.panel : css.enemy; ctx.fill();      // hollow: not in the game (yet)
    if (live.on && !e.live) { ctx.lineWidth = 2; ctx.strokeStyle = css.enemy; ctx.stroke(); }
    ctx.lineWidth = 1.5; ctx.strokeStyle = bad ? css.warn : css.panel; if (bad) ctx.setLineDash([3, 2]); ctx.stroke(); ctx.setLineDash([]);
    if (e.fixed) { ctx.fillStyle = css.panel; ctx.fillRect(x - 2, y - 2, 4, 4); }
    if (sel) { ctx.beginPath(); ctx.arc(x, y, r + 7, 0, Math.PI * 2); ctx.lineWidth = 2.2; ctx.strokeStyle = css['accent-text']; ctx.stroke(); }
    ctx.font = '500 11px ' + mono; ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
    if (e.hvt) { ctx.beginPath(); ctx.arc(x, y, r + 4, 0, Math.PI * 2); ctx.lineWidth = 2.5; ctx.strokeStyle = css.warn; ctx.stroke(); }
    const tag = String(i + 1) + (e.vehicle ? ' TRUCK' : e.hvt ? ' TARGET' : e.trigger ? ' → ' + trigTag(e.trigger) : '');
    ctx.lineWidth = 3; ctx.strokeStyle = css.paper; ctx.strokeText(tag, x + r + 6, y - r - 3); ctx.fillStyle = e.hvt ? css.warn : e.trigger ? css.friend : css.ink; ctx.fillText(tag, x + r + 6, y - r - 3);
    ctx.globalAlpha = 1;
  });

  // Live: every enemy where it really is, and the players.
  if (live.on) {
    const byId = new Map(state.enemies.map(e => [e.id, e]));
    for (const b of sealed ? [] : live.enemies) {
      const [x, y] = toScreen(b.pos[0], b.pos[2]), home = byId.get(b.marker);
      if (home && Math.hypot(home.pos[0] - b.pos[0], home.pos[2] - b.pos[2]) < 1.2) continue;       // still standing on its marker
      if (home) { const [hx, hy] = toScreen(home.pos[0], home.pos[2]); ctx.strokeStyle = css.enemy; ctx.globalAlpha = 0.35; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(hx, hy); ctx.lineTo(x, y); ctx.stroke(); ctx.globalAlpha = 1; }
      ctx.beginPath(); ctx.arc(x, y, 4.5, 0, Math.PI * 2); ctx.fillStyle = css.panel; ctx.fill(); ctx.lineWidth = 2; ctx.strokeStyle = css.enemy; ctx.stroke();
    }
    for (const p of live.players) {
      const [x, y] = toScreen(p.pos[0], p.pos[2]), a = p.yaw * Math.PI / 180;
      ctx.strokeStyle = css.friend; ctx.lineWidth = 2.5; ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + Math.sin(a) * 16, y - Math.cos(a) * 16); ctx.stroke();
      ctx.beginPath(); ctx.arc(x, y, 6.5, 0, Math.PI * 2); ctx.fillStyle = css.friend; ctx.fill(); ctx.lineWidth = 1.5; ctx.strokeStyle = css.panel; ctx.stroke();
      ctx.font = '500 11px ' + mono; ctx.textAlign = 'left'; ctx.textBaseline = 'middle'; ctx.lineWidth = 3; ctx.strokeStyle = css.paper; ctx.strokeText(p.name || 'Player', x + 10, y + 12); ctx.fillStyle = css.friend; ctx.fillText(p.name || 'Player', x + 10, y + 12);
    }
  }

  // Replay: everybody where they were at this moment, and the way they came.
  if (replay.data) {
    const now = replayNow(), fr = replay.data.frames, at = replayFrame(replay.t).i, from = Math.max(0, at - 60);
    const trail = (which, id, colour, width, alpha) => {
      ctx.beginPath(); let going = false;
      for (let n = from; n <= at; n++) {
        const p = fr[n][which].find(q => q[0] === id);
        if (!p) { going = false; continue; }
        const [x, y] = toScreen(p[1], p[3]);
        if (going) ctx.lineTo(x, y); else { ctx.moveTo(x, y); going = true; }
      }
      ctx.globalAlpha = alpha; ctx.lineWidth = width; ctx.strokeStyle = colour; ctx.stroke(); ctx.globalAlpha = 1;
    };
    for (const e of now.enemies) trail(2, e.id, css.enemy, 1.2, 0.35);
    for (const p of now.players) trail(1, p.id, css.friend, 2.2, 0.6);
    for (const e of now.enemies) {
      const [x, y] = toScreen(e.pos[0], e.pos[2]), a = e.yaw * Math.PI / 180, alert = !!e.state && e.state !== 'I';
      ctx.globalAlpha = e.pos[1] > state.ceiling + 0.4 ? 0.35 : 1;
      ctx.strokeStyle = css.enemy; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + Math.sin(a) * 11, y - Math.cos(a) * 11); ctx.stroke();
      ctx.beginPath(); ctx.arc(x, y, 5, 0, 6.2832); ctx.fillStyle = alert ? css.enemy : css.panel; ctx.fill(); ctx.lineWidth = 2; ctx.strokeStyle = css.enemy; ctx.stroke();
      ctx.globalAlpha = 1;
    }
    for (const p of now.players) {
      const [x, y] = toScreen(p.pos[0], p.pos[2]), a = p.yaw * Math.PI / 180;
      ctx.strokeStyle = css.friend; ctx.lineWidth = 2.5; ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + Math.sin(a) * 16, y - Math.cos(a) * 16); ctx.stroke();
      ctx.beginPath(); ctx.arc(x, y, 6.5, 0, 6.2832); ctx.fillStyle = css.friend; ctx.fill(); ctx.lineWidth = 1.5; ctx.strokeStyle = css.panel; ctx.stroke();
      ctx.font = '500 11px ' + mono; ctx.textAlign = 'left'; ctx.textBaseline = 'middle'; ctx.lineWidth = 3; ctx.strokeStyle = css.paper; ctx.strokeText(p.name, x + 10, y + 12); ctx.fillStyle = css.friend; ctx.fillText(p.name, x + 10, y + 12);
    }
    // What just happened, where it happened: a ring that widens and fades over six seconds.
    for (const ev of replay.data.events) {
      if (!Array.isArray(ev[3]) || ev[0] > replay.t || replay.t - ev[0] > 6) continue;
      const [x, y] = toScreen(ev[3][0], ev[3][2]), age = (replay.t - ev[0]) / 6;
      ctx.globalAlpha = 1 - age; ctx.lineWidth = 2; ctx.strokeStyle = ev[1] === 'down' ? css.ink : css.warn;
      ctx.beginPath(); ctx.arc(x, y, 8 + age * 14, 0, 6.2832); ctx.stroke();
      if (ev[1] === 'down') { ctx.beginPath(); ctx.moveTo(x - 5, y - 5); ctx.lineTo(x + 5, y + 5); ctx.moveTo(x + 5, y - 5); ctx.lineTo(x - 5, y + 5); ctx.stroke(); }
      ctx.globalAlpha = 1;
    }
  }

  if (state.repair) drawRepair();          // a previewed repair: the searched area, before and after (34a-repairs.js)
  if (quick.result) drawQuick();           // a quick mission layout being previewed, not part of the scenario yet (29f-options.js)
  if (state.inspectSel) drawInspect();     // the inspected trigger: what it needs and what it brings (29c-inspect.js)
  if (state.rehearse && (state.rehearse.picking || state.rehearse.focus)) drawRehearsal();   // the players of the rehearsal row being edited (29d-rehearse.js)

  if (drag && drag.box && drag.moved) {
    ctx.fillStyle = css.friend; ctx.globalAlpha = 0.1; ctx.fillRect(drag.sx, drag.sy, drag.bx - drag.sx, drag.by - drag.sy);
    ctx.globalAlpha = 1; ctx.lineWidth = 1.5; ctx.strokeStyle = css.friend; ctx.setLineDash([5, 4]); ctx.strokeRect(drag.sx, drag.sy, drag.bx - drag.sx, drag.by - drag.sy); ctx.setLineDash([]);
  }
  drawPaint(ctx);            // the painting tool under way: brush tip, rectangle, polygon, corner handles (27a-paint.js)

  // Scale bar: this is a map, so say what a length on it means.
  const steps = [1, 2, 5, 10, 20, 50, 100, 200, 500]; let len = steps[steps.length - 1];
  for (const s of steps) if (s * v.scale >= 60) { len = s; break; }
  const bx = w - 20 - len * v.scale, by = h - 22;
  ctx.strokeStyle = css.ink; ctx.lineWidth = 1.5; ctx.beginPath(); ctx.moveTo(bx, by - 5); ctx.lineTo(bx, by); ctx.lineTo(bx + len * v.scale, by); ctx.lineTo(bx + len * v.scale, by - 5); ctx.stroke();
  ctx.fillStyle = css.ink; ctx.font = '500 11px ' + mono; ctx.textAlign = 'right'; ctx.textBaseline = 'bottom'; ctx.fillText(len + ' m', w - 20, by - 7);
}

