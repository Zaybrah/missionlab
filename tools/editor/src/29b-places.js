// ------------------------------------------------------------------ named places (ML-018)
// Authors name parts of a map ("Rear stairs", "Upstairs office") so that triggers, the mission check and replays can say
// where something is in words. A place is a point (3 m around it, on its floor) or a painted area; its id is the stable
// reference and never changes when it is renamed. Positions only hold on the map they were made on, so a scenario opened on
// a map file with another fingerprint lists every place for the author to confirm or move.
const PLACE_REACH = 3, PLACE_HEIGHT = 2;          // metres: the plugin's PlacePlan.Reach and Membership.CircleHeight
const PLACE_BELOW = 0.5, PLACE_ABOVE = 2.6;       // metres: a new place's floor band around the ground that was clicked
const placeUi = {
  selected: -1,          // index into state.places
  waiting: '',           // 'point' | 'area' | 'move': the next click on the map says where
  reviewKey: '',         // the scenario's fingerprint and the open map's, while they differ
  marks: new Map(),      // place id -> 'right' | 'moved' | 'new' for that pair of fingerprints; never written to the file
  deleting: -1,          // the id whose delete button was pressed once and showed what still uses the place
};
// Other parts add functions here that say what refers to a place: (place) -> list of short descriptions.
const placeUsers = [];

// The plugin's PlacePlan.Contains: an area when it has parts, else 3 m across and inside the floor band, or within 2 m of
// height when no band is given.
function placeContains(p, pos) {
  if (p.area && p.area.parts && p.area.parts.length) return areaContains(p.area, pos);
  const dx = pos[0] - p.pos[0], dz = pos[2] - p.pos[2];
  if (dx * dx + dz * dz > PLACE_REACH * PLACE_REACH) return false;
  if (p.floor) return pos[1] >= p.floor[0] && pos[1] <= p.floor[1];
  return Math.abs(pos[1] - p.pos[1]) <= PLACE_HEIGHT;
}
// MissionPlan.PlaceAt: the name of the first place that holds a position, '' when none does.
function placeName(pos) {
  if (!isVec(pos)) return '';
  for (const p of state.places) if (placeContains(p, pos)) return p.name;
  return '';
}
// " in Rear stairs" for a finding or a label, '' when no place holds the position.
const placeWords = pos => { const n = placeName(pos); return n ? ' in ' + n : ''; };
const placeById = id => state.places.find(p => p.id === id) || null;

// The fingerprints of the scenario and of the open map, when they are not known to agree; '' when nothing needs checking.
function placeReviewKey() {
  const m = state.map;
  if (!m || m.isExample || !m.print || state.mapPrint === m.print) return '';
  const key = (state.mapPrint || 'unknown') + '>' + m.print;
  if (placeUi.reviewKey !== key) { placeUi.reviewKey = key; placeUi.marks = new Map(); }
  return key;
}
// The rebind review: every place with what the author said about it on this map, or null when there is nothing to check
// (same fingerprint, no real map open, no places, or only places made on this map).
function placeReview() {
  if (!state.places.length || !placeReviewKey()) return null;
  if (state.places.every(p => placeUi.marks.get(p.id) === 'new')) return null;
  return { why: state.mapPrint ? 'changed' : 'unknown', places: state.places.map(p => ({ id: p.id, name: p.name, mark: placeUi.marks.get(p.id) || '' })) };
}
function markPlace(id, mark) { if (placeReviewKey()) placeUi.marks.set(id, mark); }

function addPlace(pos, kind, band) {
  pushUndo();
  const id = state.places.reduce((m, p) => Math.max(m, p.id), 0) + 1;
  const place = { id, name: 'Place ' + id, pos: pos.slice(0, 3), floor: kind === 'point' && band ? band.slice(0, 2) : null, area: null };
  state.places.push(place);
  markPlace(id, 'new');
  placeUi.selected = state.places.length - 1;
  changed();
  return place;
}
// Renaming keeps the id, so whatever refers to the place still finds it.
function renamePlace(id, name) {
  const p = placeById(id), clean = String(name || '').replace(/\s+/g, ' ').trim().slice(0, 40);
  if (!p || !clean || clean === p.name) return false;
  pushUndo(); p.name = clean; changed();
  return true;
}
// What still uses a place: other parts' references by id, and texts that name it (they would go on naming a place that is gone).
function placeUses(p) {
  const out = [];
  for (const user of placeUsers) out.push(...user(p));
  const named = t => typeof t === 'string' && t.toLowerCase().includes(p.name.toLowerCase());
  for (const z of state.zones) {
    if (named(z.message)) out.push('the message of ' + trigTitle(z).toLowerCase() + ' names it');
    if (named(z.name)) out.push('the name of ' + trigTitle(z).toLowerCase() + ' names it');
  }
  state.briefing.forEach((line, i) => { if (named(line)) out.push('briefing line ' + (i + 1) + ' names it'); });
  return out;
}
// Deletes a place unless something still uses it and force is not given. Returns what uses it ([] when it was deleted).
function deletePlace(id, force) {
  const i = state.places.findIndex(p => p.id === id);
  if (i < 0) return [];
  const uses = placeUses(state.places[i]);
  if (uses.length && !force) return uses;
  pushUndo();
  state.places.splice(i, 1);
  placeUi.marks.delete(id); placeUi.deleting = -1;
  if (placeUi.selected >= state.places.length) placeUi.selected = state.places.length - 1;
  changed();
  return [];
}

// ---- on the map
function waitForPlace(kind) {
  if (state.mode === '3d') setMode('2d');
  placeUi.waiting = kind;
  say(kind === 'move' ? 'Click where the place is on this map. Esc cancels.' : kind === 'area' ? 'Click where the name of the place should stand, then paint its shape. Esc cancels.' : 'Click the spot to name. Esc cancels.');
}
function startPlaceShape(index) {
  const p = state.places[index]; if (!p) return;
  const band = p.area && p.area.parts.length ? p.area.parts[0].floor.slice(0, 2) : p.floor ? p.floor.slice(0, 2) : [p.pos[1] - PLACE_BELOW, p.pos[1] + PLACE_ABOVE];
  areaEditStart({ kind: 'place', index }, band);
}
function placeClicked(pos) {
  const kind = placeUi.waiting; placeUi.waiting = '';
  const band = [r2(pos[1] - PLACE_BELOW), r2(pos[1] + PLACE_ABOVE)];
  if (kind === 'move') {
    const p = state.places[placeUi.selected]; if (!p) return;
    pushUndo(); p.pos = pos.slice(0, 3); if (p.floor) p.floor = band;
    markPlace(p.id, 'moved'); changed();
    return say(p.name + ' moved' + (p.area && p.area.parts.length ? '. Its shape stays where it was: use Paint its shape to redraw it.' : '.'));
  }
  const p = addPlace(pos, kind, band);
  if (kind === 'area') { startPlaceShape(placeUi.selected); say(p.name + ' added. Paint its shape on the map, then give it a name.'); }
  else say(p.name + ' added. Give it a name in the Map tab.');
}
// While a place waits for its spot, the click belongs to it and not to the tool above the map.
canvas.addEventListener('pointerdown', ev => {
  if (!placeUi.waiting || ev.button !== 0) return;
  ev.stopImmediatePropagation(); ev.preventDefault();
  const r = canvas.getBoundingClientRect(), [wx, wz] = toWorld(ev.clientX - r.left, ev.clientY - r.top), y = groundAt(wx, wz);
  if (y === null) return say('Nobody can walk there. Click on the shaded ground' + (isFinite(state.ceiling) ? ', or raise "Show up to" to reach higher floors.' : '.'));
  placeClicked([wx, y, wz]);
}, true);
window.addEventListener('keydown', ev => { if (ev.key === 'Escape' && placeUi.waiting) { placeUi.waiting = ''; say('No place added.'); } });

// Points get a small pin and a faint 3 m ring, areas their outline; both carry the name. Places above "Show up to" are faint.
function drawPlaces() {
  const font = '600 11px ' + getComputedStyle(document.body).getPropertyValue('--mono'), scale = state.view.scale;
  state.places.forEach((p, n) => {
    const on = n === placeUi.selected, shaped = p.area && p.area.parts && p.area.parts.length;
    const low = shaped ? Math.min(...p.area.parts.map(q => q.floor[0])) : p.floor ? p.floor[0] : p.pos[1];
    ctx.globalAlpha = low > state.ceiling + 0.4 ? 0.35 : 1;
    ctx.strokeStyle = css.place; ctx.fillStyle = css.place; ctx.lineWidth = on ? 2.4 : 1.4;
    if (shaped) {
      ctx.beginPath();
      for (const part of p.area.parts) for (const ring of [part.outline, ...(part.holes || [])]) ring.forEach((q, k) => { const [x, y] = toScreen(q[0], q[1]); if (k) ctx.lineTo(x, y); else ctx.moveTo(x, y); if (k === ring.length - 1) ctx.closePath(); });
      const a = ctx.globalAlpha; ctx.globalAlpha = a * 0.08; ctx.fill('evenodd'); ctx.globalAlpha = a; ctx.setLineDash([2, 3]); ctx.stroke(); ctx.setLineDash([]);
    } else {
      const [x, y] = toScreen(p.pos[0], p.pos[2]), a = ctx.globalAlpha;
      ctx.globalAlpha = a * 0.5; ctx.setLineDash([2, 3]); ctx.beginPath(); ctx.arc(x, y, PLACE_REACH * scale, 0, Math.PI * 2); ctx.stroke(); ctx.setLineDash([]); ctx.globalAlpha = a;
    }
    const [x, y] = toScreen(p.pos[0], p.pos[2]);
    ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x - 4, y - 8); ctx.lineTo(x + 4, y - 8); ctx.closePath(); ctx.fill();
    ctx.font = font; ctx.textAlign = 'center'; ctx.textBaseline = 'bottom';
    ctx.lineWidth = 3; ctx.strokeStyle = css.paper; ctx.strokeText(p.name, x, y - 10); ctx.fillText(p.name, x, y - 10);
    ctx.globalAlpha = 1;
  });
}

// ---- the side panel
function syncPlaces() {
  $('placeCount').textContent = state.places.length ? String(state.places.length) : '';
  const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  const small = (text, cls, fn) => { const b = mk('button', { type: 'button', className: 'btn' + (cls ? ' ' + cls : ''), textContent: text }); b.style.padding = '3px 8px'; b.addEventListener('click', fn); return b; };
  const show = p => showAt(p.pos, (p.floor ? p.floor[1] : p.pos[1] + PLACE_ABOVE));
  // The rebind review.
  const review = placeReview(), box = $('placeReviewBox');
  box.hidden = !review;
  if (review) {
    const left = review.places.filter(q => !q.mark).length;
    $('placeReviewWhy').textContent = (review.why === 'changed' ? 'This scenario was made on another version of this map file.' : 'This scenario does not say which version of the map file its places were made on.')
      + ' Look at each place and say whether it is still right. ' + (left ? left + (left === 1 ? ' place is' : ' places are') + ' not checked yet.' : 'All places are checked; saving records this map file.');
    const rl = $('placeReviewList'); rl.textContent = '';
    review.places.forEach(q => {
      const i = state.places.findIndex(p => p.id === q.id), p = state.places[i];
      const li = mk('li'), name = mk('b', { textContent: q.name }), how = mk('span', { className: 'hint', textContent: q.mark === 'right' ? 'still right' : q.mark === 'moved' ? 'moved' : q.mark === 'new' ? 'made on this map' : 'not checked' });
      li.append(name, how, small('Show', '', () => { placeUi.selected = i; show(p); syncSide(); }),
        small('Still right', '', () => { markPlace(q.id, 'right'); syncSide(); }),
        small('Move it', '', () => { placeUi.selected = i; show(p); if (p.area && p.area.parts.length) { markPlace(q.id, 'moved'); startPlaceShape(i); } else waitForPlace('move'); syncSide(); }));
      rl.appendChild(li);
    });
  }
  // The list.
  const list = $('placeList'); list.textContent = '';
  state.places.forEach((p, n) => {
    const li = mk('li'), b = mk('button', { type: 'button' }), a = mk('span', { textContent: p.name }), c = mk('span');
    c.textContent = (p.area && p.area.parts.length ? 'area' : 'point') + ' · ' + p.pos[0].toFixed(0) + ', ' + p.pos[2].toFixed(0) + ' · height ' + p.pos[1].toFixed(1);
    if (n === placeUi.selected) b.setAttribute('aria-current', 'true');
    b.append(a, c); b.addEventListener('click', () => { placeUi.selected = n; placeUi.deleting = -1; show(p); syncSide(); });
    li.appendChild(b); list.appendChild(li);
  });
  const p = state.places[placeUi.selected], paint = p ? paintBoxFor('place', placeUi.selected) : null;
  $('placePanel').hidden = !p;
  $('placePaint').replaceChildren(...(paint ? [paint] : []));     // 27a-paint.js: the tools while this place's shape is painted
  if (!p) return;
  const shaped = !!(p.area && p.area.parts.length);
  setVal($('placeLabel'), p.name);
  $('placeBottom').disabled = $('placeTop').disabled = shaped;
  setVal($('placeBottom'), !shaped && p.floor ? p.floor[0] : ''); setVal($('placeTop'), !shaped && p.floor ? p.floor[1] : '');
  $('placeShape').textContent = shaped ? 'A painted area of ' + p.area.parts.length + (p.area.parts.length === 1 ? ' part' : ' parts') + '; each part has its own floor band. The pin only labels it.'
    : 'Everything within ' + PLACE_REACH + ' m of the pin' + (p.floor ? ' between heights ' + p.floor[0] + ' and ' + p.floor[1] + ' m.' : ' and within ' + PLACE_HEIGHT + ' m of its height.') + ' The floor band keeps it apart from the floor above or below.';
  $('placePoint').hidden = !shaped;
  $('placeShapeEdit').textContent = shaped ? 'Edit its shape' : 'Paint its shape';
  $('placeUses').textContent = placeUi.deleting === p.id ? 'Still used: ' + placeUses(p).join('; ') + '. Press Delete again to delete it anyway.' : '';
}
$('placeAddPoint').addEventListener('click', () => waitForPlace('point'));
$('placeAddArea').addEventListener('click', () => waitForPlace('area'));
$('placeMove').addEventListener('click', () => { if (state.places[placeUi.selected]) waitForPlace('move'); });
$('placeShapeEdit').addEventListener('click', () => { const p = state.places[placeUi.selected]; if (!p) return; markPlace(p.id, placeUi.marks.get(p.id) === 'new' ? 'new' : 'moved'); startPlaceShape(placeUi.selected); });
$('placePoint').addEventListener('click', () => { const p = state.places[placeUi.selected]; if (!p) return; areaEditStop(); pushUndo(); p.area = null; p.floor = [r2(p.pos[1] - PLACE_BELOW), r2(p.pos[1] + PLACE_ABOVE)]; changed(); });
$('placeLabel').addEventListener('change', ev => { const p = state.places[placeUi.selected]; if (p && !renamePlace(p.id, ev.target.value)) { ev.target.value = p.name; syncSide(); } });
// Both fields empty take the band away; one empty is filled with a storey's height from the other. A band upside down is
// turned round and one over 60 m refused, since the file refuses both.
const placeBand = () => {
  const p = state.places[placeUi.selected]; if (!p) return;
  const b = $('placeBottom').value.trim(), t = $('placeTop').value.trim(), storey = PLACE_BELOW + PLACE_ABOVE;
  let floor = null;
  if (b !== '' || t !== '') {
    const one = b === '' ? +t - storey : +b, two = t === '' ? +b + storey : +t, lo = Math.min(one, two), hi = Math.max(one, two);
    if (hi - lo > AREA_MAX_FLOOR) { say('A floor band of more than ' + AREA_MAX_FLOOR + ' m is not a floor. Make it smaller.'); syncSide(); return; }
    floor = hi > lo ? [lo, hi] : [lo, lo + storey];
  }
  pushUndo(); p.floor = floor; changed();
};
$('placeBottom').addEventListener('change', placeBand);
$('placeTop').addEventListener('change', placeBand);
$('placeDel').addEventListener('click', () => {
  const p = state.places[placeUi.selected]; if (!p) return;
  const uses = deletePlace(p.id, placeUi.deleting === p.id);
  if (uses.length) { placeUi.deleting = p.id; syncSide(); return; }
  say(p.name + ' deleted.');
});
