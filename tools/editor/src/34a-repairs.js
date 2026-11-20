// ---------------------------------------------------------------- repairs: small, previewed fixes for some Mission check findings (ML-017)
// A finding that has a repair carries an offer ({ kind, ... }, made by missionCheck). repairPlan() works out the edit from the scenario
// as it is now and shows it first: before and after on the map, with the area that was searched. Apply is one Undo step. Where no
// fix is clearly right, or none lies within the bounds, the plan holds an explanation and no edit: a repair never guesses.
// Repairs edit the scenario on this page only. They never move the target person, never touch the extraction, never change what a
// trigger does, and are off while the page is linked to a running game, so that nothing is ever sent to it.
//
// What counts as a repair:
//   snap      an enemy, a patrol route stop or a thing off the walkable ground goes to the nearest walkable spot within 3 m
//             horizontally on the same floor (ground within 1 m of its height), with ground at least 0.25 m around it.
//   apart     enemies closer than 0.6 m on the same floor: the first stays (the target person, if one of them is), each later one
//             goes to the nearest walkable spot within 2 m that is at least 1 m from every other enemy on that floor.
//   renumber  two triggers share a number. The later one gets the next free number, but only when every reference to that number
//             can mean just one of them: nothing else waits for it, and a trigger of the pair that waits for the number can only
//             mean the other one (a trigger never waits for itself). Anything else waiting for the number could mean either.
//   unlist    an all/any list names a trigger that is gone (or the trigger itself): the saved file leaves those out already, so
//             taking them off the list is the only fix that keeps what the mission does. Not when the list would be left empty.
//   unlink    an enemy or thing waits for a trigger that is gone: the saved file has it there from the start already, so clearing
//             the reference is the only fix that keeps what the mission does.
//   after, extraction  a trigger, or the extraction, waits for a trigger that is gone: any trigger could be meant (and a repair
//             never changes the extraction), so these only ever explain.
state.repair = null;            // the repair being previewed: { offer, title, why, edits, areas, marks }
const REPAIR = { snap: 3, band: 1, step: 0.25, margin: 0.25, overlap: 0.6, apart: 1, spread: 2, floor: 1.5 };
const REPAIR_LIVE = 'Repairs change the scenario on this page only. While the page is linked to a running game in Live mode every edit is sent to it at once, so Apply is off. Switch to Draft mode to stage a repair.';
const REPAIR_DRAFT = 'In Draft mode the repair is staged on this page: nothing reaches the game until you press Apply changes under Changes to the game.';

// The nearest spot to p within o.radius metres (horizontally) where levels(x, z) has ground within o.band metres of p's height, with
// ground of about that height o.margin metres to each side, and that o.ok(spot) accepts. Candidates lie on a fixed grid around p and
// are tried nearest first, so the answer depends on nothing but the inputs. null when there is none.
function walkableNear(p, levels, o) {
  const step = o.step || REPAIR.step, band = o.band == null ? REPAIR.band : o.band, margin = o.margin == null ? REPAIR.margin : o.margin;
  const n = Math.floor(o.radius / step + 1e-9), list = [];
  for (let i = -n; i <= n; i++) for (let k = -n; k <= n; k++) if (Math.hypot(i, k) * step <= o.radius + 1e-9) list.push([Math.hypot(i, k), i, k]);
  list.sort((a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2]);
  for (const [, i, k] of list) {
    const x = r2(p[0] + i * step), z = r2(p[2] + k * step);
    if (Math.hypot(x - p[0], z - p[2]) > o.radius) continue;
    let y = null;
    for (const l of levels(x, z)) if (Math.abs(l - p[1]) <= band && (y === null || Math.abs(l - p[1]) < Math.abs(y - p[1]))) y = l;
    if (y === null) continue;
    const near = (a, b) => levels(x + a, z + b).some(l => Math.abs(l - y) <= 0.3);
    if (margin > 0 && !(near(margin, 0) && near(-margin, 0) && near(0, margin) && near(0, -margin))) continue;
    const spot = [x, r2(y), z];
    if (o.ok && !o.ok(spot)) continue;
    return spot;
  }
  return null;
}

const repairNever = (a, b) => !!a.either && b.either === a.either && a.option !== b.option;      // either/or options never there together
const repairFloor = (a, b) => Math.abs(a[1] - b[1]) < REPAIR.floor;
const repairAt = p => p[0].toFixed(2) + ', ' + p[1].toFixed(2) + ', ' + p[2].toFixed(2);
const repairTrig = z => trigTitle(z) + (z.name ? ' (' + z.name + ')' : '');

// The edit an offer stands for, worked out from the scenario as it is now.
function repairPlan(offer) {
  const plan = { offer, title: '', why: '', edits: null, areas: [], marks: [] };
  const o = offer || {}, map = !!state.map, levels = (x, z) => levelsAt(x, z);
  const noMap = 'No map is open, so there is no walkable ground to look for. Open the map file first.';
  if (o.kind === 'snap') {
    const e = o.what === 'thing' ? null : state.enemies[o.i], t = o.what === 'thing' ? state.things[o.i] : null;
    const p = o.what === 'thing' ? t && t.pos : o.what === 'stop' ? e && e.patrol[o.n] : e && e.pos;
    const label = o.what === 'thing' ? (t ? 'The ' + t.name.trim() : 'The thing') : o.what === 'stop' ? 'Route stop ' + (o.n + 1) + ' of enemy ' + (o.i + 1) : 'Enemy ' + (o.i + 1);
    plan.title = label + ' to walkable ground';
    if (!p) { plan.why = 'It is no longer in the scenario.'; return plan; }
    if (!map) { plan.why = noMap; return plan; }
    if (onGround(p) || (e && o.what === 'enemy' && (e.fixed || e.vehicle))) { plan.why = label + ' is on walkable ground now (or needs none): nothing to repair.'; return plan; }
    if (e && o.what === 'enemy' && e.hvt) { plan.why = 'Enemy ' + (o.i + 1) + ' is the target person, which the mission needs: a repair never moves it. Move it by hand.'; return plan; }
    plan.areas.push({ at: p.slice(0, 3), radius: REPAIR.snap, band: REPAIR.band });
    // An enemy is not put on top of another one that can be there at the same time.
    const ok = o.what !== 'enemy' ? null : s => state.enemies.every((q, k) => k === o.i || repairNever(e, q) || !repairFloor(s, q.pos) || Math.hypot(s[0] - q.pos[0], s[2] - q.pos[2]) >= REPAIR.overlap);
    const to = walkableNear(p, levels, { radius: REPAIR.snap, ok });
    if (!to) { plan.marks.push({ from: p.slice(0, 3), to: null }); plan.why = 'There is no walkable ground within ' + REPAIR.snap + ' m of it on its floor (ground within ' + REPAIR.band + ' m of its height), so nothing is moved. Put it down by hand.'; return plan; }
    plan.edits = [{ what: o.what, i: o.i, n: o.n | 0, from: p.slice(0, 3), to }];
    plan.marks.push({ from: p.slice(0, 3), to });
    plan.why = label + ' goes ' + Math.hypot(to[0] - p[0], to[2] - p[2]).toFixed(1) + ' m to the nearest walkable ground on its floor.';
    return plan;
  }
  if (o.kind === 'apart') {
    plan.title = 'Enemies ' + (o.i + 1) + ' and ' + (o.j + 1) + ' apart';
    const a = state.enemies[o.i], b = state.enemies[o.j];
    if (!a || !b) { plan.why = 'One of them is no longer in the scenario.'; return plan; }
    if (!map) { plan.why = noMap; return plan; }
    const close = (x, y) => !repairNever(x, y) && repairFloor(x.pos, y.pos) && Math.hypot(x.pos[0] - y.pos[0], x.pos[2] - y.pos[2]) < REPAIR.overlap;
    if (!close(a, b)) { plan.why = 'They no longer stand on the same spot: nothing to repair.'; return plan; }
    // Everybody standing on that spot, through each other: the first stays, unless another one is the target person.
    const group = [o.i];
    for (let n = 0; n < group.length; n++) state.enemies.forEach((q, k) => { if (!group.includes(k) && close(state.enemies[group[n]], q)) group.push(k); });
    group.sort((x, y) => x - y);
    const targets = group.filter(k => state.enemies[k].hvt);
    if (targets.length > 1) { plan.why = 'Enemies ' + targets.map(k => k + 1).join(' and ') + ' are both the target person, which the mission needs: a repair never moves one. Move one by hand.'; return plan; }
    const stay = targets.length ? targets[0] : group[0], movers = group.filter(k => k !== stay);
    const where = state.enemies.map(q => q.pos.slice(0, 3)), edits = [];
    for (const k of movers) {
      const e = state.enemies[k], from = e.pos.slice(0, 3);
      plan.areas.push({ at: from, radius: REPAIR.spread, band: REPAIR.band });
      const ok = s => state.enemies.every((q, m) => m === k || repairNever(e, q) || !repairFloor(s, where[m]) || Math.hypot(s[0] - where[m][0], s[2] - where[m][2]) >= REPAIR.apart);
      const to = walkableNear(from, levels, { radius: REPAIR.spread, ok });
      if (!to) {
        plan.marks.push({ from, to: null });
        plan.why = 'No walkable spot within ' + REPAIR.spread + ' m of enemy ' + (k + 1) + ' is at least ' + REPAIR.apart + ' m from every other enemy on its floor, so nothing is moved. Move ' + (movers.length > 1 ? 'them' : 'it') + ' by hand.';
        return plan;
      }
      where[k] = to; edits.push({ what: 'enemy', i: k, n: 0, from, to }); plan.marks.push({ from, to });
    }
    plan.edits = edits;
    plan.why = 'Enemy ' + (stay + 1) + ' stays' + (targets.length ? ' (the target person)' : '') + '; ' + movers.map(k => 'enemy ' + (k + 1)).join(', ') + (movers.length > 1 ? ' move' : ' moves') + ' to the nearest walkable spot at least ' + REPAIR.apart + ' m from every other enemy.';
    return plan;
  }
  if (o.kind === 'renumber') {
    plan.title = 'Trigger number ' + o.id + ' used twice';
    const dup = []; state.zones.forEach((z, k) => { if (z.id === o.id) dup.push(k); });
    if (dup.length < 2) { plan.why = 'Only one trigger has number ' + o.id + ' now: nothing to repair.'; return plan; }
    const inside = [], outside = [];
    state.enemies.forEach((e, k) => { if (e.trigger === o.id) outside.push('enemy ' + (k + 1)); });
    state.things.forEach(t => { if (t.trigger === o.id) outside.push('the ' + t.name.trim()); });
    if (state.extractionAfter === o.id) outside.push('the extraction');
    // An outcome trigger's "after" (ML-047) is a link like an after trigger's.
    const waitsOn = z => ((z.when === 'after' || z.when === 'outcome') && z.after === o.id) || ((z.when === 'all' || z.when === 'any') && (z.of || []).includes(o.id));
    state.zones.forEach((z, k) => { if (waitsOn(z)) (dup.includes(k) ? inside : outside).push(repairTrig(z)); });
    if (outside.length) { plan.why = outside.join(', ') + (outside.length > 1 ? ' wait' : ' waits') + ' for number ' + o.id + ', which could mean any of the ' + dup.length + ' triggers that have it, so a repair cannot tell which is meant. Delete the one that is not wanted, or take number ' + o.id + ' out of what waits for it.'; return plan; }
    if (inside.length && dup.length > 2) { plan.why = inside.join(', ') + ' waits for number ' + o.id + ', which ' + dup.length + ' triggers have: it could mean more than one of them, so a repair cannot tell which is meant.'; return plan; }
    // The first keeps its number. A trigger of the pair that waits for the number means the other one, as nothing waits for itself.
    let next = state.zones.reduce((m, z) => Math.max(m, z.id | 0), 0);
    const edits = [], keep = state.zones[dup[0]];
    for (const k of dup.slice(1)) edits.push({ what: 'id', zi: k, from: o.id, to: ++next });
    if (dup.length === 2) {
      const fresh = edits[0].to;
      if ((keep.when === 'after' || keep.when === 'outcome') && keep.after === o.id) edits.push({ what: 'after', zi: dup[0], from: o.id, to: fresh });
      if ((keep.when === 'all' || keep.when === 'any') && (keep.of || []).includes(o.id)) edits.push({ what: 'of', zi: dup[0], from: keep.of.slice(), to: keep.of.map(id => (id === o.id ? fresh : id)) });
    }
    plan.edits = edits;
    plan.why = 'Nothing outside these triggers waits for number ' + o.id + ', so each later one can take a free number' + (edits.length > dup.length - 1 ? ', and ' + repairTrig(keep) + ', which can only have meant the other one, follows it' : '') + '. What each trigger does stays the same.';
    return plan;
  }
  if (o.kind === 'unlist') {
    const z = state.zones[o.zi];
    plan.title = z ? 'Gone triggers off the list of ' + repairTrig(z) : 'A trigger list';
    if (!z || (z.when !== 'all' && z.when !== 'any')) { plan.why = 'That trigger no longer waits for a list: nothing to repair.'; return plan; }
    const of = z.of || [], keepIds = of.filter(id => id !== z.id && state.zones.some(q => q.id === id));
    if (keepIds.length === of.length) { plan.why = 'Every trigger on the list is there: nothing to repair.'; return plan; }
    if (!keepIds.length) { plan.why = 'Nothing would be left on the list, and a trigger that waits for an empty list never fires. Tick the triggers it should wait for.'; return plan; }
    plan.edits = [{ what: 'of', zi: o.zi, from: of.slice(), to: keepIds }];
    plan.why = 'The saved file already leaves ' + (of.length - keepIds.length === 1 ? 'that number' : 'those numbers') + ' out, so this only makes the page say what the mission does.';
    return plan;
  }
  if (o.kind === 'unlink') {
    const x = o.what === 'thing' ? state.things[o.i] : state.enemies[o.i], label = o.what === 'thing' ? (x ? 'the ' + x.name.trim() : 'the thing') : 'enemy ' + (o.i + 1);
    plan.title = 'The gone trigger of ' + label;
    if (!x || !x.trigger || state.zones.some(z => z.id === x.trigger)) { plan.why = 'It no longer waits for a trigger that is gone: nothing to repair.'; return plan; }
    plan.edits = [{ what: o.what === 'thing' ? 'thing-trigger' : 'enemy-trigger', i: o.i, from: x.trigger, to: 0 }];
    plan.why = 'The saved file has ' + label + ' there from the start already, so clearing the reference only makes the page say what the mission does. Any other trigger would be a guess.';
    return plan;
  }
  if (o.kind === 'after') {
    const z = state.zones[o.zi];
    plan.title = 'What ' + (z ? repairTrig(z) : 'a trigger') + ' waits for';
    plan.why = 'It waits for a trigger that is gone. Any other trigger could be meant, so a repair cannot choose one. Pick the trigger it should wait for in its trigger panel.';
    return plan;
  }
  if (o.kind === 'extraction') {
    plan.title = 'What the extraction waits for';
    plan.why = 'A repair never changes the extraction. The saved file leaves the gone trigger out, so the mission\'s own rule applies; choose another trigger for the extraction by hand if one is wanted.';
    return plan;
  }
  plan.why = 'This finding has no repair.';
  return plan;
}

// One edit of a plan, made on the page's scenario.
function repairEdit(ed) {
  if (ed.what === 'enemy') state.enemies[ed.i].pos = ed.to.slice();
  else if (ed.what === 'stop') state.enemies[ed.i].patrol[ed.n] = ed.to.slice();
  else if (ed.what === 'thing') state.things[ed.i].pos = ed.to.slice();
  else if (ed.what === 'enemy-trigger') state.enemies[ed.i].trigger = ed.to;
  else if (ed.what === 'thing-trigger') state.things[ed.i].trigger = ed.to;
  else if (ed.what === 'id') state.zones[ed.zi].id = ed.to;
  else if (ed.what === 'after') state.zones[ed.zi].after = ed.to;
  else if (ed.what === 'of') state.zones[ed.zi].of = ed.to.slice();
}
function repairEditText(ed) {
  if (ed.what === 'enemy' || ed.what === 'stop' || ed.what === 'thing') {
    const who = ed.what === 'thing' ? 'The ' + state.things[ed.i].name.trim() : ed.what === 'stop' ? 'Route stop ' + (ed.n + 1) + ' of enemy ' + (ed.i + 1) : 'Enemy ' + (ed.i + 1);
    return who + ': ' + repairAt(ed.from) + ' \u2192 ' + repairAt(ed.to) + ' (' + Math.hypot(ed.to[0] - ed.from[0], ed.to[2] - ed.from[2]).toFixed(1) + ' m)';
  }
  if (ed.what === 'enemy-trigger') return 'Enemy ' + (ed.i + 1) + ': waits for trigger ' + ed.from + ' (gone) \u2192 there from the start';
  if (ed.what === 'thing-trigger') return 'The ' + state.things[ed.i].name.trim() + ': waits for trigger ' + ed.from + ' (gone) \u2192 there from the start';
  const z = state.zones[ed.zi], who = trigTitle(z) + (z.name ? ' (' + z.name + ')' : '');
  if (ed.what === 'id') return who + ': number ' + ed.from + ' \u2192 ' + ed.to;
  if (ed.what === 'after') return who + ': waits for number ' + ed.from + ' \u2192 ' + ed.to;
  return who + ': list ' + (ed.from.join(', ') || 'empty') + ' \u2192 ' + (ed.to.join(', ') || 'empty');
}

function showRepair() {
  const r = state.repair, box = $('repairBox'); if (!box) return;
  box.hidden = !r;
  if (!r) return;
  $('repairTitle').textContent = 'Repair: ' + r.title;
  $('repairText').textContent = r.why + (r.edits && live.on ? ' ' + (stageHolds() ? REPAIR_DRAFT : REPAIR_LIVE) : '') + (r.areas.length ? ' The dashed ring on the map is the area that was searched.' : '');
  const list = $('repairEdits'); list.textContent = '';
  for (const ed of r.edits || []) { const li = document.createElement('li'); li.textContent = repairEditText(ed); list.appendChild(li); }
  $('repairApply').hidden = !r.edits; $('repairApply').disabled = !r.edits || (live.on && !stageHolds());
  $('repairCancel').textContent = r.edits ? 'Cancel' : 'Close';
}
function previewRepair(f) {
  state.repair = Object.assign(repairPlan(f.repair), { finding: f.text });
  showRepair();
  const first = state.repair.areas[0];
  if (first) { if (state.mode === '3d') setMode('2d'); showAt(first.at, first.at[1] + 2); }
  invalidate();
  return state.repair;
}
function cancelRepair() { state.repair = null; showRepair(); invalidate(); }
// Apply: the plan is worked out again first; when the scenario changed since the preview, the new plan is shown instead.
function applyRepair() {
  const r = state.repair; if (!r || !r.edits) return false;
  if (live.on && !stageHolds()) { say(REPAIR_LIVE); return false; }     // Draft mode stages the repair; nothing reaches the game before Apply changes (ML-014)
  const now = repairPlan(r.offer);
  if (!now.edits || JSON.stringify(now.edits) !== JSON.stringify(r.edits)) {
    state.repair = Object.assign(now, { finding: r.finding }); showRepair(); invalidate();
    say('The scenario changed after the preview. The preview now shows the repair as it stands.');
    return false;
  }
  willChange('Repair: ' + r.title);             // one Undo step for the whole repair
  for (const ed of r.edits) repairEdit(ed);
  state.repair = null; showRepair();
  changed();                                    // redraws and runs the Mission check again
  const left = missionCheck().filter(f => f.level !== 'note').length;
  say('Repaired: ' + r.title + '. Undo (Ctrl+Z) takes it back. ' + (left ? left + (left === 1 ? ' thing is' : ' things are') + ' still to look at in the Mission check.' : 'The Mission check finds nothing else to fix.'));
  return true;
}
// The preview on the 2D map: the searched area, where it was (hollow) and where it would go (filled), with an arrow.
function drawRepair() {
  const r = state.repair; if (!r) return;
  const sc = state.view.scale, mono = getComputedStyle(document.body).getPropertyValue('--mono');
  for (const a of r.areas) {
    const [x, y] = toScreen(a.at[0], a.at[2]);
    ctx.beginPath(); ctx.arc(x, y, a.radius * sc, 0, 6.2832);
    ctx.globalAlpha = 0.08; ctx.fillStyle = css.warn; ctx.fill();
    ctx.globalAlpha = 0.9; ctx.setLineDash([4, 4]); ctx.lineWidth = 1.5; ctx.strokeStyle = css.warn; ctx.stroke(); ctx.setLineDash([]);
    const text = 'searched: ' + a.radius + ' m, floor \u00b1' + a.band + ' m';
    ctx.font = '500 11px ' + mono; ctx.textAlign = 'center'; ctx.textBaseline = 'bottom';
    ctx.lineWidth = 3; ctx.strokeStyle = css.paper; ctx.strokeText(text, x, y - a.radius * sc - 3); ctx.fillStyle = css.warn; ctx.fillText(text, x, y - a.radius * sc - 3);
    ctx.globalAlpha = 1;
  }
  for (const m of r.marks) {
    const [x0, y0] = toScreen(m.from[0], m.from[2]);
    ctx.beginPath(); ctx.arc(x0, y0, 7, 0, 6.2832); ctx.lineWidth = 2; ctx.strokeStyle = css.ink; ctx.setLineDash([3, 2]); ctx.stroke(); ctx.setLineDash([]);
    if (!m.to) { ctx.beginPath(); ctx.moveTo(x0 - 5, y0 - 5); ctx.lineTo(x0 + 5, y0 + 5); ctx.moveTo(x0 + 5, y0 - 5); ctx.lineTo(x0 - 5, y0 + 5); ctx.stroke(); continue; }
    const [x1, y1] = toScreen(m.to[0], m.to[2]), l = Math.hypot(x1 - x0, y1 - y0);
    if (l > 16) {
      const ux = (x1 - x0) / l, uy = (y1 - y0) / l, ex = x1 - ux * 8, ey = y1 - uy * 8;
      ctx.beginPath(); ctx.moveTo(x0 + ux * 8, y0 + uy * 8); ctx.lineTo(ex, ey); ctx.lineWidth = 1.8; ctx.strokeStyle = css.friend; ctx.stroke();
      ctx.beginPath(); ctx.moveTo(ex, ey); ctx.lineTo(ex - ux * 6 - uy * 4, ey - uy * 6 + ux * 4); ctx.lineTo(ex - ux * 6 + uy * 4, ey - uy * 6 - ux * 4); ctx.closePath(); ctx.fillStyle = css.friend; ctx.fill();
    }
    ctx.beginPath(); ctx.arc(x1, y1, 5.5, 0, 6.2832); ctx.fillStyle = css.friend; ctx.fill(); ctx.lineWidth = 1.5; ctx.strokeStyle = css.panel; ctx.stroke();
  }
}
$('repairApply').addEventListener('click', applyRepair);
$('repairCancel').addEventListener('click', cancelRepair);
