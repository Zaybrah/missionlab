// ------------------------------------------------------------------ constrained scatter and placement areas (ML-023)
// An enemy with a scatter radius is placed at random within that distance of where the author put it, each time the mission
// starts (Core Scatter.Resolve in the plugin). Placement areas are painted: an "allow" area is where an enemy that names it
// as its "within" must stand, an "empty" area is where no scattered enemy may stand. The preview below shows the rules on
// this page's walkable surface, sampled every 0.5 m: it is not the roll. "Try seeds" asks the game to start the scenario
// with real seeds on the map's navmesh (POST /api/scatter/preview); offline only the preview is there.
const SCATTER_MAX = 30, SCATTER_FLOOR = 1.5, SCATTER_SPACING = 2, SCATTER_CLEARANCE = 20;   // Core Scatter: ScatterLimit, SameFloor, Spacing, Clearance
const SCATTER_STEP = 0.5;                          // metres between the preview's sample points
const SCATTER_RULES = ['floor', 'within', 'empty', 'spacing', 'insertion'];     // Core Scatter's order; "island" is the game's alone
const SCATTER_RULE_WORDS = {
  floor: 'no walkable ground within 1.5 m of its height (another floor, or off the map)',
  within: 'outside its "within" area',
  empty: 'inside an empty area',
  spacing: 'within 2 m of another enemy',
  insertion: 'within 20 m of an insertion point',
};
const SEEDS_DEFAULT = 20, SEEDS_MAX = 200;
const scatterUi = {
  selected: -1,          // index into state.placement
  refused: -1,           // the id whose delete was refused, with what still uses it
  preview: null,         // the selected enemy's sampled region, see scatterGrid()
  previewKey: '',
  sweep: null,           // the game's answer to Try seeds, with the scenario text it was asked for
  sweeping: false,
};

const placementById = id => state.placement.find(p => p.id === id) || null;
const placementPainted = p => !!(p && p.area && Array.isArray(p.area.parts) && p.area.parts.length);

// ----- placement areas
function addPlacement(mode) {
  willChange(mode === 'empty' ? 'Add an empty area' : 'Add an allow area');
  const id = state.placement.reduce((m, p) => Math.max(m, p.id), 0) + 1, empty = mode === 'empty';
  state.placement.push({ id, name: (empty ? 'Empty area ' : 'Area ') + id, mode: empty ? 'empty' : 'allow', area: null });
  scatterUi.selected = state.placement.length - 1; scatterUi.refused = -1;
  changed();
  return state.placement[scatterUi.selected];
}
function renamePlacement(id, name) {
  const p = placementById(id), clean = String(name || '').replace(/\s+/g, ' ').trim().slice(0, 40);
  if (!p || !clean || clean === p.name) return false;
  willChange('Rename placement area "' + p.name + '"'); p.name = clean; changed();
  return true;
}
// The enemies that scatter within a placement, as the author sees them: 'enemy 3'.
function placementUses(p) {
  const out = [];
  if (p) state.enemies.forEach((e, i) => { if (e.within === p.id) out.push('enemy ' + (i + 1)); });
  return out;
}
// An allow area some enemy scatters within cannot become an empty one: the file would refuse that enemy.
function setPlacementMode(id, mode) {
  const p = placementById(id); if (!p || (mode !== 'allow' && mode !== 'empty') || p.mode === mode) return [];
  const uses = mode === 'empty' ? placementUses(p) : [];
  if (uses.length) return uses;
  willChange('Change the kind of placement area "' + p.name + '"'); p.mode = mode; changed();
  return [];
}
// Deleting is refused while an enemy still scatters within it. Returns what uses it ([] when it was deleted).
function deletePlacement(id) {
  const i = state.placement.findIndex(p => p.id === id);
  if (i < 0) return [];
  const uses = placementUses(state.placement[i]);
  if (uses.length) { scatterUi.refused = id; syncSide(); return uses; }
  if (paintIs('placement', i)) areaEditStop();
  willChange('Delete placement area "' + state.placement[i].name + '"');
  state.placement.splice(i, 1);
  scatterUi.refused = -1;
  if (scatterUi.selected >= state.placement.length) scatterUi.selected = state.placement.length - 1;
  changed();
  return [];
}
function startPlacementPaint(index) {
  const p = state.placement[index]; if (!p) return false;
  const e = state.enemies[state.selected];
  const band = placementPainted(p) ? p.area.parts[0].floor.slice(0, 2) : e ? [r2(e.pos[1] - 0.5), r2(e.pos[1] + 2.6)] : undefined;
  scatterUi.selected = index;
  return areaEditStart({ kind: 'placement', index }, band);
}

// ----- an enemy's scatter fields
// '' when the enemy may be left out, otherwise why it may not (the file refuses "skip" on these).
function fallbackLocked(e) {
  if (!e) return '';
  if (e.hvt) return 'The target person is never left out: when no spot passes, it stays where it is placed.';
  if (e.vehicle) return 'A gun truck is never left out: when no spot passes, it stays where it is placed.';
  return '';
}
function setScatter(e, radius) {
  const r = Math.max(0, Math.min(SCATTER_MAX, r2(+radius || 0)));
  if (!e || r === e.scatter) return false;
  willChange('Change the scatter of enemy ' + (state.enemies.indexOf(e) + 1)); e.scatter = r; changed();
  return true;
}
function setWithin(e, id) {
  id = +id || 0;
  const p = placementById(id);
  if (!e || (id && !(p && p.mode === 'allow')) || id === (e.within || 0)) return false;
  willChange('Change where enemy ' + (state.enemies.indexOf(e) + 1) + ' scatters'); e.within = id; changed();
  return true;
}
function setFallback(e, fallback) {
  if (!e || (fallback !== 'stay' && fallback !== 'skip') || fallback === e.fallback) return false;
  if (fallback === 'skip' && fallbackLocked(e)) { say(fallbackLocked(e)); syncSide(); return false; }
  willChange('Change the fallback of enemy ' + (state.enemies.indexOf(e) + 1)); e.fallback = fallback; changed();
  return true;
}

// ----- the preview of the rules: sample points every 0.5 m inside the radius, each judged as Core Scatter judges a
// candidate, on the page's walkable surface. Connected ground ("island") is left to the game. Spacing counts the other
// enemies at their authored places; enemies later in the list that scatter themselves are not settled yet, so they do not
// count (as in the plugin). null when the enemy does not scatter.
function scatterGrid(index) {
  const e = state.enemies[index];
  if (!e || !(e.scatter > 0)) return null;
  const r = Math.min(SCATTER_MAX, e.scatter), [cx, cy, cz] = e.pos;
  const out = { index, radius: r, total: 0, points: [], removed: { floor: 0, within: 0, empty: 0, spacing: 0, insertion: 0 }, worst: '', noMap: !state.map };
  const inside = e.within > 0 ? placementById(e.within) : null;
  const usable = inside && inside.mode === 'allow' && placementPainted(inside) ? inside : null;
  const empties = state.placement.filter(p => p.mode === 'empty' && placementPainted(p));
  const others = state.enemies.filter((o, j) => j !== index && !(j > index && o.scatter > 0)).map(o => o.pos);
  const entries = state.infils.filter(isVec).concat(state.insertionSpots.map(s => s.pos).filter(isVec));
  const n = Math.floor(r / SCATTER_STEP);
  for (let a = -n; a <= n; a++) for (let b = -n; b <= n; b++) {
    const dx = a * SCATTER_STEP, dz = b * SCATTER_STEP;
    if (dx * dx + dz * dz > r * r + 1e-9) continue;
    out.total++;
    const x = cx + dx, z = cz + dz;
    let y = null;
    if (state.map) for (const level of levelsAt(x, z)) if (Math.abs(level - cy) <= SCATTER_FLOOR && (y === null || Math.abs(level - cy) < Math.abs(y - cy))) y = level;
    if (y === null) { out.removed.floor++; continue; }
    const p = [x, y, z];
    if (e.within > 0 && !(usable && areaContains(usable.area, p))) { out.removed.within++; continue; }
    if (empties.some(q => areaContains(q.area, p))) { out.removed.empty++; continue; }
    if (others.some(o => (o[0] - x) ** 2 + (o[1] - y) ** 2 + (o[2] - z) ** 2 < SCATTER_SPACING * SCATTER_SPACING)) { out.removed.spacing++; continue; }
    if (entries.some(q => Math.hypot(q[0] - x, q[2] - z) < SCATTER_CLEARANCE)) { out.removed.insertion++; continue; }
    out.points.push(x, z);
  }
  let most = 0;
  for (const k of SCATTER_RULES) if (out.removed[k] > most) { most = out.removed[k]; out.worst = k; }
  return out;
}
// The preview in words, always saying it is not the roll.
function scatterPreviewText(g) {
  if (!g) return '';
  if (g.noMap) return 'Open the map file to preview where this enemy may be placed. The radius is kept either way.';
  const ok = g.points.length / 2;
  return 'Preview of the rules, not the roll: ' + ok + ' of ' + g.total + ' sample points (every ' + SCATTER_STEP + ' m within ' + g.radius + ' m) are eligible, drawn as dots.'
    + (g.worst ? ' Most points were removed by "' + g.worst + '": ' + SCATTER_RULE_WORDS[g.worst] + ' (' + g.removed[g.worst] + ').' : ' No rule removed any point.')
    + (ok ? '' : ' With no eligible point, every roll falls back: ' + (state.enemies[g.index].fallback === 'skip' && !fallbackLocked(state.enemies[g.index]) ? 'the enemy is left out.' : 'the enemy stays where it is placed.'))
    + ' Connected ground and the places of other scattered enemies are only known when the game rolls.';
}
function scatterKey(index) {
  const e = state.enemies[index];
  if (!e || !(e.scatter > 0)) return '';
  return JSON.stringify([index, e.pos, e.scatter, e.within, state.enemies.map(o => [o.pos, o.scatter > 0]), state.placement, state.infils, state.insertionSpots, state.map ? state.map.name + state.map.n : '']);
}
function scatterPreviewNow() {
  const key = scatterKey(state.selected);
  if (key !== scatterUi.previewKey) { scatterUi.previewKey = key; scatterUi.preview = key ? scatterGrid(state.selected) : null; }
  return scatterUi.preview;
}

// ----- Try seeds: the scenario started by the game with real seeds on the map's navmesh
const scatterServed = () => /^https?:$/.test(location.protocol) && /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
function scatterSweepBody(count, players) {
  const n = Math.max(1, Math.min(SEEDS_MAX, Math.round(+count) || SEEDS_DEFAULT));
  const entries = state.infils.filter(isVec).concat(state.insertionSpots.map(s => s.pos).filter(isVec)).slice(0, 64).map(p => p.slice(0, 3).map(r2));
  return { scenario: scenarioText(), seeds: Array.from({ length: n }, (_, i) => i + 1), players: Math.max(1, Math.min(16, Math.round(+players) || 1)), insertions: entries };
}
async function scatterPost(body) {
  const r = await fetch('/api/scatter/preview', { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify(body) });
  let data = null; try { data = await r.json(); } catch (e) { /* no body */ }
  return { status: r.status, data };
}
// send(body) -> {status, data}; the page's own request unless a test gives another.
async function trySeeds(count, players, send) {
  if (!send && !scatterServed()) { scatterUi.sweep = { error: 'Try seeds needs the page the game serves (http://localhost): the game reads the map\'s navmesh. Here only the preview of the rules is shown.' }; syncSide(); return scatterUi.sweep; }
  if (!state.navFile) { scatterUi.sweep = { error: 'The scenario names no map file, so the game cannot know its walkable ground. Open its map file first.' }; syncSide(); return scatterUi.sweep; }
  const body = scatterSweepBody(count, players);
  scatterUi.sweeping = true; syncSide();
  let got;
  try { got = await (send || scatterPost)(body); }
  catch (e) { got = { status: 0, data: { error: 'the game did not answer (' + (e.message || e) + ')' } }; }
  scatterUi.sweeping = false;
  const d = got && got.data;
  scatterUi.sweep = !d ? { error: 'The game gave no answer.' }
    : d.error ? { error: 'Not tried: ' + d.error }
    : d.readable === false ? { error: 'The game did not read the scenario: ' + (d.findings || []).slice(0, 3).map(f => f.path + ': ' + f.text).join('; ') }
    : Object.assign({ text: body.scenario }, d);
  syncSide();
  return scatterUi.sweep;
}
// The game's answer as lines for the page: what was tried (never every roll), the totals, then every counterexample.
function scatterSweepRows(sw) {
  if (!sw) return { head: '', rows: [] };
  if (sw.error) return { head: sw.error, rows: [] };
  if (sw.scatter === false) return { head: sw.tried || 'No enemy scatters.', rows: [] };
  const runs = sw.runs || [], sum = k => runs.reduce((t, r) => t + (r[k] || 0), 0), refused = runs.filter(r => r.refused).length;
  const head = sw.tried + ' Over all seeds: ' + sum('moved') + ' moved, ' + sum('stayed') + ' stayed, ' + sum('skipped') + ' left out'
    + (refused ? '; ' + refused + (refused === 1 ? ' seed would be refused' : ' seeds would be refused') + ' and the game\'s own mission run.' : '.')
    + (sw.text !== undefined && sw.text !== scenarioText() ? ' The scenario has changed since: try again.' : '');
  const words = { unfinishable: 'Unfinishable', 'skip-unfinishable': 'Left out, unfinishable', stayed: 'Stayed' };
  const rows = (sw.counterexamples || []).map(c => ({ seed: c.seed, kind: c.kind, spawn: c.spawn, text: 'Seed ' + c.seed + ' · ' + (words[c.kind] || c.kind) + ': ' + c.text }));
  return { head, rows };
}

// ----- on the map: every placement area, and the selected enemy's circle with its eligible points
function drawScatter() {
  const floor = paintShownFloor();
  state.placement.forEach((p, n) => {
    if (!placementPainted(p)) return;
    const on = n === scatterUi.selected, colour = p.mode === 'empty' ? css.enemy : css.warn;
    drawArea(ctx, p.area, { colour, fill: p.mode === 'empty' ? 0.1 : 0.08, line: on ? 0.95 : 0.6, width: on ? 2.2 : 1.3, floor });
    const part = p.area.parts[0], o = part.outline[0];
    if (o) {
      const [x, y] = toScreen(o[0], o[1]);
      ctx.font = '600 11px ' + getComputedStyle(document.body).getPropertyValue('--mono'); ctx.textAlign = 'left'; ctx.textBaseline = 'bottom';
      ctx.lineWidth = 3; ctx.strokeStyle = css.paper; ctx.strokeText(p.name, x + 3, y - 3); ctx.fillStyle = colour; ctx.fillText(p.name, x + 3, y - 3);
    }
  });
  const e = state.enemies[state.selected];
  if (!e || !(e.scatter > 0)) return;
  const [x, y] = toScreen(e.pos[0], e.pos[2]), scale = state.view.scale;
  ctx.setLineDash([5, 4]); ctx.lineWidth = 1.5; ctx.strokeStyle = css.enemy; ctx.globalAlpha = 0.85;
  ctx.beginPath(); ctx.arc(x, y, Math.min(SCATTER_MAX, e.scatter) * scale, 0, Math.PI * 2); ctx.stroke(); ctx.setLineDash([]);
  const g = scatterPreviewNow();
  if (g && g.points.length) {
    const d = Math.max(1.2, Math.min(4, SCATTER_STEP * scale * 0.45));
    ctx.fillStyle = css.enemy; ctx.globalAlpha = 0.55;
    for (let k = 0; k < g.points.length; k += 2) { const [px, py] = toScreen(g.points[k], g.points[k + 1]); ctx.fillRect(px - d / 2, py - d / 2, d, d); }
  }
  ctx.globalAlpha = 1;
}

// ----- the side panel
function syncScatter() {
  const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  // The placement areas.
  if (scatterUi.selected >= state.placement.length) scatterUi.selected = state.placement.length - 1;
  $('plCount').textContent = state.placement.length ? String(state.placement.length) : '';
  const list = $('plList'); list.textContent = '';
  state.placement.forEach((p, n) => {
    const li = mk('li'), b = mk('button', { type: 'button' }), name = mk('span', { textContent: p.name }), what = mk('span');
    const users = placementUses(p).length;
    what.textContent = (p.mode === 'empty' ? 'empty' : 'allow') + ' · ' + (placementPainted(p) ? p.area.parts.length + (p.area.parts.length === 1 ? ' part' : ' parts') : 'not painted yet') + (users ? ' · ' + users + (users === 1 ? ' enemy' : ' enemies') + ' within' : '');
    if (n === scatterUi.selected) b.setAttribute('aria-current', 'true');
    b.append(name, what); b.addEventListener('click', () => { scatterUi.selected = n; scatterUi.refused = -1; syncSide(); invalidate(); });
    li.appendChild(b); list.appendChild(li);
  });
  const p = state.placement[scatterUi.selected], paint = p ? paintBoxFor('placement', scatterUi.selected) : null;
  $('plPanel').hidden = !p;
  $('plPaint').replaceChildren(...(paint ? [paint] : []));      // 27a-paint.js: the tools while this area is painted
  if (p) {
    setVal($('plName'), p.name); setVal($('plMode'), p.mode);
    $('plShape').textContent = placementPainted(p)
      ? (p.mode === 'empty' ? 'No scattered enemy is placed in this area.' : 'Enemies that name this area as "Within" are only placed inside it.') + ' Each part has its own floor band.'
      : 'Not painted yet: an area is only saved once it has a shape.';
    $('plPaintBtn').textContent = placementPainted(p) ? 'Edit its shape' : 'Paint its shape';
    $('plUses').textContent = scatterUi.refused === p.id && placementUses(p).length ? 'Not deleted: ' + placementUses(p).join(', ') + ' scatter within it. Set their "Within" to another area first.' : '';
  }
  // The selected enemy's scatter.
  const e = state.enemies[state.selected];
  $('scEnemy').hidden = !e;
  if (!e) return;
  setVal($('scRadius'), e.scatter || 0);
  const within = $('scWithin'), allow = state.placement.filter(q => q.mode === 'allow');
  if (document.activeElement !== within) {
    within.textContent = '';
    [[0, 'Anywhere the other rules allow'], ...allow.map(q => [q.id, q.name + (placementPainted(q) ? '' : ' (not painted yet)')])].forEach(([v, t]) => within.appendChild(mk('option', { value: v, textContent: t })));
    within.value = String(allow.some(q => q.id === e.within) ? e.within : 0);
  }
  within.disabled = !(e.scatter > 0);
  const locked = fallbackLocked(e), fb = $('scFallback');
  setVal(fb, locked ? 'stay' : e.fallback || 'stay'); fb.disabled = !(e.scatter > 0);
  fb.querySelector('option[value="skip"]').disabled = !!locked;
  $('scFallbackWhy').textContent = locked || (e.scatter > 0 ? (e.fallback === 'skip' ? 'When no spot passes this enemy is left out; the mission check and Try seeds show when that makes the mission unfinishable.' : 'When no spot passes it stays exactly where it is placed.') : 'Radius 0: it stands exactly where it is placed.');
  $('scPreview').textContent = e.scatter > 0 ? scatterPreviewText(scatterPreviewNow()) : '';
  const rm = $('scRemoved'); rm.textContent = '';
  const g = e.scatter > 0 ? scatterPreviewNow() : null;
  if (g && !g.noMap) for (const k of SCATTER_RULES) if (g.removed[k]) rm.appendChild(mk('li', { textContent: g.removed[k] + ' removed: ' + SCATTER_RULE_WORDS[k] }));
  // Try seeds.
  $('scTry').disabled = scatterUi.sweeping || !state.enemies.some(q => q.scatter > 0);
  $('scTry').textContent = scatterUi.sweeping ? 'Trying...' : 'Try seeds';
  const sw = scatterSweepRows(scatterUi.sweep);
  $('scTried').textContent = scatterServed() || scatterUi.sweep ? sw.head : 'Offline: only the preview of the rules. Try seeds works on the page the game serves.';
  const cl = $('scCounter'); cl.textContent = '';
  for (const row of sw.rows) {
    const li = mk('li', { textContent: row.text });
    if (row.spawn >= 0 && state.enemies[row.spawn]) {
      const show = mk('button', { type: 'button', className: 'btn', textContent: 'Select' }); show.style.padding = '3px 8px';
      show.addEventListener('click', () => { select(row.spawn); showAt(state.enemies[row.spawn].pos, state.enemies[row.spawn].pos[1] + 2); });
      li.appendChild(show);
    }
    cl.appendChild(li);
  }
}
$('plAddAllow').addEventListener('click', () => { addPlacement('allow'); startPlacementPaint(scatterUi.selected); say('Paint the area on the map: scattered enemies that name it are only placed inside.'); });
$('plAddEmpty').addEventListener('click', () => { addPlacement('empty'); startPlacementPaint(scatterUi.selected); say('Paint the area on the map: no scattered enemy is placed inside it.'); });
$('plName').addEventListener('change', ev => { const p = state.placement[scatterUi.selected]; if (p && !renamePlacement(p.id, ev.target.value)) { ev.target.value = p.name; syncSide(); } });
$('plMode').addEventListener('change', ev => {
  const p = state.placement[scatterUi.selected]; if (!p) return;
  const uses = setPlacementMode(p.id, ev.target.value);
  if (uses.length) { say('Still an allow area: ' + uses.join(', ') + ' scatter within it.'); ev.target.value = p.mode; }
});
$('plPaintBtn').addEventListener('click', () => startPlacementPaint(scatterUi.selected));
$('plDel').addEventListener('click', () => {
  const p = state.placement[scatterUi.selected]; if (!p) return;
  if (!deletePlacement(p.id).length) say(p.name + ' deleted.');
});
$('scRadius').addEventListener('change', ev => { const e = state.enemies[state.selected]; if (!setScatter(e, ev.target.value) && e) ev.target.value = e.scatter || 0; });
$('scWithin').addEventListener('change', ev => setWithin(state.enemies[state.selected], ev.target.value));
$('scFallback').addEventListener('change', ev => setFallback(state.enemies[state.selected], ev.target.value));
$('scTry').addEventListener('click', () => trySeeds($('scSeeds').value, $('scPlayers').value));
// Ticking "target person" or "gun truck" takes "leave it out" back in the same step, since the file refuses that pair.
for (const id of ['hvt', 'vehicle']) $(id).addEventListener('change', () => {
  const e = state.enemies[state.selected];
  if (e && e.fallback === 'skip' && fallbackLocked(e)) { e.fallback = 'stay'; changed(); say(fallbackLocked(e)); }
});
