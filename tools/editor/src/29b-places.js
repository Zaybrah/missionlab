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
  willChange(kind === 'area' ? 'Add a shaped place' : 'Add a place');
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
  willChange('Rename place "' + p.name + '"'); p.name = clean; changed();
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
// A place that zones link is never deleted, force or not: they would point at nothing. Move their links to another place
// or detach them (each keeps a copy of its shape) first.
function deletePlace(id, force) {
  const i = state.places.findIndex(p => p.id === id);
  if (i < 0) return [];
  const uses = placeUses(state.places[i]);
  if (uses.length && (!force || placeLinkedZones(id).length)) return uses;
  willChange('Delete place "' + state.places[i].name + '"');
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
  if (!areaEditStart({ kind: 'place', index }, band)) return;
  // Before anything changes: who else changes with it.
  const linked = placeLinkedZones(p.id);
  if (linked.length) say('Painting ' + p.name + '. ' + placeLinkWords(linked) + ' linked to it: each change to its shape changes ' + (linked.length === 1 ? 'that zone' : 'all of them') + ' too, as one step.');
}
function placeClicked(pos) {
  const kind = placeUi.waiting; placeUi.waiting = '';
  const band = [r2(pos[1] - PLACE_BELOW), r2(pos[1] + PLACE_ABOVE)];
  if (kind === 'move') {
    const p = state.places[placeUi.selected]; if (!p) return;
    willChange('Move place "' + p.name + '"'); p.pos = pos.slice(0, 3); if (p.floor) p.floor = band;
    markPlace(p.id, 'moved'); changed();
    return say(p.name + ' moved' + (p.area && p.area.parts.length ? '. Its shape stays where it was: use Paint its shape to redraw it.' : '.'));
  }
  const p = addPlace(pos, kind, band);
  if (kind === 'area') { startPlaceShape(placeUi.selected); say(p.name + ' added. Paint its shape on the map, then give it a name.'); }
  else say(p.name + ' added. Give it a name in the Map tab.');
}
// While a place waits for its spot, the click belongs to it and not to the tool above the map. It lands on the highest
// floor that Floors and rooms leaves shown and unlocked (29k-outliner.js), as a new enemy does.
function placePointerDown(ev) {
  if (!placeUi.waiting || ev.button !== 0) return;
  ev.stopImmediatePropagation(); ev.preventDefault();
  const r = canvas.getBoundingClientRect(), [wx, wz] = toWorld(ev.clientX - r.left, ev.clientY - r.top), y = outlinerGround(wx, wz);
  if (y === null && groundAt(wx, wz) !== null) return say('That floor is hidden, locked or outside what is isolated in Floors and rooms. Unlock or show it there first.');
  if (y === null) return say('Nobody can walk there. Click on the shaded ground' + (isFinite(state.ceiling) ? ', or raise "Show up to" to reach higher floors.' : '.'));
  placeClicked([wx, y, wz]);
}
canvas.addEventListener('pointerdown', placePointerDown, true);
expose({ placePointerDown, waitForPlace });   // the map click that sets a place (ML-041), for automated tests
window.addEventListener('keydown', ev => { if (ev.key === 'Escape' && placeUi.waiting) { placeUi.waiting = ''; say('No place added.'); } });

// Points get a small pin and a faint 3 m ring, areas their outline; both carry the name. Places above "Show up to" are faint,
// and so is one Floors and rooms leaves faint or locked; a hidden one is left out (29k-outliner.js).
function drawPlaces() {
  const font = '600 11px ' + getComputedStyle(document.body).getPropertyValue('--mono'), scale = state.view.scale;
  state.places.forEach((p, n) => {
    const fade = outlinerAlpha(p.pos);
    if (!fade) return;
    const on = n === placeUi.selected, shaped = p.area && p.area.parts && p.area.parts.length;
    const low = shaped ? Math.min(...p.area.parts.map(q => q.floor[0])) : p.floor ? p.floor[0] : p.pos[1];
    ctx.globalAlpha = (low > state.ceiling + 0.4 ? 0.35 : 1) * fade;
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
  $('placeFlatRow').hidden = !state.zones.some(z => z.place > 0);
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
  const linked = placeLinkedZones(p.id);
  $('placeUses').textContent = placeUi.deleting !== p.id ? '' : linked.length ? 'Not deleted: ' + placeLinkWords(linked) + ' linked to it. Move the links to another place or detach them below, then delete it.'
    : 'Still used: ' + placeUses(p).join('; ') + '. Press Delete again to delete it anyway.';
  syncPlaceLinks(p, shaped, linked);
}
$('placeAddPoint').addEventListener('click', () => waitForPlace('point'));
$('placeAddArea').addEventListener('click', () => waitForPlace('area'));
$('placeMove').addEventListener('click', () => { if (state.places[placeUi.selected]) waitForPlace('move'); });
$('placeShapeEdit').addEventListener('click', () => { const p = state.places[placeUi.selected]; if (!p) return; markPlace(p.id, placeUi.marks.get(p.id) === 'new' ? 'new' : 'moved'); startPlaceShape(placeUi.selected); });
$('placePoint').addEventListener('click', () => {
  const p = state.places[placeUi.selected]; if (!p) return;
  const linked = placeLinkedZones(p.id);
  if (linked.length) return say(p.name + ' stays an area: ' + placeLinkWords(linked) + ' linked to its shape. Move the links to another place or detach them first.');
  areaEditStop(); willChange('Make place "' + p.name + '" a point'); p.area = null; p.floor = [r2(p.pos[1] - PLACE_BELOW), r2(p.pos[1] + PLACE_ABOVE)]; changed();
});
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
  willChange('Change the floor of place "' + p.name + '"'); p.floor = floor; changed();
};
$('placeBottom').addEventListener('change', placeBand);
$('placeTop').addEventListener('change', placeBand);
$('placeDel').addEventListener('click', () => {
  const p = state.places[placeUi.selected]; if (!p) return;
  const uses = deletePlace(p.id, placeUi.deleting === p.id);
  if (uses.length) { placeUi.deleting = p.id; syncSide(); return; }
  say(p.name + ' deleted.');
});

// ---- zones linked to a place (ML-040)
// A zone or hold can take a place's painted area in two ways. "Copy area" gives it its own copy, as before: later changes to
// the place do not reach it. "Link" makes it use the place's one shape: the file stores the place's id, a change to the
// place's shape changes every linked zone in the same undo step and the same staged change, and renaming the place keeps
// the links. A linked zone has no shape of its own to paint: the author edits the place, or detaches the zone, which then
// keeps a copy of the shape it had. A place that zones link is deleted only once they are moved to another place or detached.
const placePainted = p => !!(p && p.area && Array.isArray(p.area.parts) && p.area.parts.length);
const placeLinkedZones = id => state.zones.filter(z => z.place > 0 && z.place === id);
// "Zone 1 is", "Zone 1 and zone 3 are": the start of a sentence about the zones linked to a place.
const placeLinkWords = list => {
  const names = list.map((z, i) => (i ? trigTitle(z).toLowerCase() : trigTitle(z)));
  return (names.length > 1 ? names.slice(0, -1).join(', ') + ' and ' + names[names.length - 1] : names[0] || 'Nothing') + (list.length === 1 ? ' is' : ' are');
};
placeUsers.push(p => placeLinkedZones(p.id).map(z => trigTitle(z).toLowerCase() + ' is linked to it'));
const placeZoneOf = id => state.zones.find(z => z.id === id) || null;
// Painting stops on a zone that is about to take a place's shape, so the tools do not go on painting a shape it no longer owns.
function placeStopPainting(z) { const t = state.paint.target; if (t && t.kind === 'trigger' && state.zones[t.index] === z) areaEditStop(); }
// Why a zone cannot take a place's area, or '' when it can.
function placeLinkRefusal(z, p) {
  if (!z || z.when !== 'zone') return 'Only a zone or a hold can take the area of a place.';
  if (!p) return 'There is no such place.';
  if (!placePainted(p)) return p.name + ' is a point: paint its shape first, then use it.';
  return '';
}
function placeCopyArea(zoneId, placeId) {
  const z = placeZoneOf(zoneId), p = placeById(placeId), why = placeLinkRefusal(z, p);
  if (why) { say(why); return false; }
  placeStopPainting(z);
  willChange('Copy the area of place "' + p.name + '" into ' + trigTitle(z).toLowerCase());
  z.place = 0; z.area = placeAreaCopy(p.id); changed();
  say(trigTitle(z) + ' has its own copy of the shape of ' + p.name + '. Later changes to the place do not change it.');
  return true;
}
function placeLink(zoneId, placeId) {
  const z = placeZoneOf(zoneId), p = placeById(placeId), why = placeLinkRefusal(z, p);
  if (why) { say(why); return false; }
  placeStopPainting(z);
  willChange('Link ' + trigTitle(z).toLowerCase() + ' to place "' + p.name + '"');
  z.place = p.id; z.area = placeAreaCopy(p.id); changed();
  const others = placeLinkedZones(p.id).filter(q => q !== z);
  say(trigTitle(z) + ' now uses the shape of ' + p.name + '. ' + (others.length ? placeLinkWords(others) + ' linked to it too. ' : '') + 'Editing the place changes every zone linked to it.');
  return true;
}
// The zone keeps a copy of the shape it has now, and from then on is painted on its own.
function placeDetach(zoneId) {
  const z = placeZoneOf(zoneId);
  if (!z || !(z.place > 0)) return false;
  const p = placeById(z.place), name = p ? p.name : 'place ' + z.place;
  willChange('Detach ' + trigTitle(z).toLowerCase() + ' from place "' + name + '"');
  if (!(z.area && z.area.parts && z.area.parts.length) && p) z.area = placeAreaCopy(p.id);
  z.place = 0; changed();
  say(trigTitle(z) + ' keeps its own copy of the shape of ' + name + ' and can now be painted on its own. Changes to the place no longer reach it.');
  return true;
}
function placeDetachAll(placeId) {
  const p = placeById(placeId), linked = placeLinkedZones(placeId);
  if (!linked.length) return false;
  willChange('Detach every zone from place "' + (p ? p.name : placeId) + '"');
  for (const z of linked) { if (!(z.area && z.area.parts && z.area.parts.length) && p) z.area = placeAreaCopy(p.id); z.place = 0; }
  changed();
  say(placeLinkWords(linked) + ' no longer linked to ' + (p ? p.name : 'the place') + '; each keeps a copy of the shape it had.');
  return true;
}
// Every zone linked to one place uses another painted place instead, as one step.
function placeRetarget(fromId, toId) {
  const from = placeById(fromId), to = placeById(toId), linked = placeLinkedZones(fromId);
  if (!linked.length || fromId === toId) return false;
  if (!placePainted(to)) { say(to ? to.name + ' is a point: only a painted place can be linked.' : 'There is no such place.'); return false; }
  linked.forEach(placeStopPainting);
  willChange('Move the links of place "' + (from ? from.name : fromId) + '" to place "' + to.name + '"');
  for (const z of linked) { z.place = to.id; z.area = placeAreaCopy(to.id); }
  changed();
  say(placeLinkWords(linked) + ' now linked to ' + to.name + ' and use its shape.');
  return true;
}
// "Edit the place" from a linked zone: the place's shape is painted, with every zone linked to it named first.
function placeEditFor(zoneId) {
  const z = placeZoneOf(zoneId), i = z && z.place > 0 ? state.places.findIndex(p => p.id === z.place) : -1;
  if (i < 0) { say('That zone is not linked to a place.'); return false; }
  placeUi.selected = i; placeUi.deleting = -1;
  showTab('map');
  startPlaceShape(i);
  return true;
}
// Zones whose link points at a place that is gone or has no shape: such a scenario is not saved (the file rules refuse it).
const placeLinkProblems = () => state.zones.filter(z => z.place > 0 && !placePainted(placeById(z.place))).map(z => trigTitle(z) + ' is linked to place ' + z.place + ', which ' + (placeById(z.place) ? 'has no painted shape' : 'is gone'));

// A linked zone's shape is its place's: painting it is refused with the two ways on, and going back to a circle unlinks it.
// A finished change to a place's shape is passed on to every zone linked to it before the change is reported, so the undo
// step and the batch for the game hold the place and all its zones together.
const placeAreaEditStart = areaEditStart, placeAreaClear = areaClear, placeAreaChanged = areaChanged;
areaEditStart = (target, band) => {
  const z = target && target.kind === 'trigger' ? areaHolder(target) : null;
  if (z && z.place > 0) {
    const p = placeById(z.place), others = placeLinkedZones(z.place).filter(q => q !== z);
    say(trigTitle(z) + ' uses the shape of place ' + (p ? p.name : z.place) + (others.length ? ', as ' + others.map(q => trigTitle(q).toLowerCase()).join(', ') + (others.length === 1 ? ' does' : ' do') : '')
      + '. Choose "Edit the place" to change it for every zone linked to it, or "Detach" to give ' + trigTitle(z).toLowerCase() + ' its own copy to paint.');
    return false;
  }
  return placeAreaEditStart(target, band);
};
areaClear = target => {
  const z = target && target.kind === 'trigger' ? areaHolder(target) : null;
  if (!(z && z.place > 0)) return placeAreaClear(target);
  const p = placeById(z.place);
  willChange('Back to a circle: ' + areaTargetName(target)); z.place = 0; z.area = null; changed();
  say(trigTitle(z) + ' no longer uses ' + (p ? p.name : 'the place') + ': its circle counts again. The place keeps its shape. Undo brings the link back.');
};
areaChanged = target => {
  if (target && target.kind === 'place') {
    const p = areaHolder(target);
    placeLinksResolve();
    if (p && !placePainted(p) && placeLinkedZones(p.id).length) say(p.name + ' has no shape left, but ' + placeLinkWords(placeLinkedZones(p.id)).replace(/ (is|are)$/, '') + ' still use it: paint it again, undo, or detach them. Until then the scenario is not saved.');
  }
  return placeAreaChanged(target);
};

// The links of the selected place: who uses its shape, and the ways to let go of it.
function syncPlaceLinks(p, shaped, linked) {
  const box = $('placeLinks'); box.textContent = '';
  if (!linked.length) return;
  const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  const small = (text, fn, title) => { const b = mk('button', { type: 'button', className: 'btn', textContent: text }); b.style.padding = '3px 8px'; if (title) b.title = title; b.addEventListener('click', fn); return b; };
  box.appendChild(mk('p', { className: 'hint', textContent: placeLinkWords(linked) + ' linked to this place: ' + (shaped ? 'changing its shape changes ' + (linked.length === 1 ? 'that zone' : 'all of them') + ' at once, as one step.' : 'it has no shape now, so the scenario is not saved until it is painted again or they are detached.') }));
  const ul = mk('ul');
  for (const z of linked) {
    const li = mk('li');
    li.append(mk('b', { textContent: trigTitle(z) + (z.name ? ': ' + z.name : '') }),
      small('Show', () => { if (isVec(z.pos)) showAt(z.pos, z.pos[1] + PLACE_ABOVE); }),
      small('Detach', () => placeDetach(z.id), 'It keeps a copy of the shape it has now, and changes to the place no longer reach it'));
    ul.appendChild(li);
  }
  box.appendChild(ul);
  const others = state.places.filter(q => q.id !== p.id && placePainted(q)), row = mk('div', { className: 'row' });
  if (others.length) {
    const sel = mk('select'); sel.setAttribute('aria-label', 'Place to move the links of ' + p.name + ' to');
    for (const q of others) sel.appendChild(mk('option', { value: String(q.id), textContent: q.name }));
    row.append(sel, small('Move the links there', () => placeRetarget(p.id, +sel.value), 'Every zone linked to ' + p.name + ' uses the chosen place instead'));
  }
  row.append(small('Detach all', () => placeDetachAll(p.id), 'Every zone linked to ' + p.name + ' keeps a copy of the shape it has now'));
  box.appendChild(row);
}

// In a zone's row (29-controls.js): the place whose shape it uses, or the painted places it could copy or link.
function placeLinkBox(z) {
  if (!z || z.when !== 'zone') return null;
  const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  // A focused button inside the trigger list keeps the list from being rebuilt (29-controls.js), so focus leaves it first.
  const small = (text, fn, title) => { const b = mk('button', { type: 'button', className: 'btn', textContent: text }); b.style.padding = '3px 8px'; if (title) b.title = title;
    b.addEventListener('click', () => { if (document.activeElement && typeof document.activeElement.blur === 'function') document.activeElement.blur(); fn(); }); return b; };
  const g = mk('span', { className: 'grp' });
  if (z.place > 0) {
    const p = placeById(z.place), others = placeLinkedZones(z.place).filter(q => q !== z);
    g.append('Uses the shape of ' + (p ? p.name : 'place ' + z.place + ', which is gone') + (others.length ? ' (also ' + others.map(q => trigTitle(q).toLowerCase()).join(', ') + ')' : ''),
      small('Edit the place', () => placeEditFor(z.id), 'Changes the shape for every zone linked to this place'),
      small('Detach', () => placeDetach(z.id), 'Keeps a copy of the shape it has now, to paint on its own'));
    return g;
  }
  const painted = state.places.filter(placePainted);
  if (!painted.length) return null;
  const sel = mk('select'); sel.setAttribute('aria-label', 'Place whose area ' + trigTitle(z) + ' takes');
  for (const q of painted) sel.appendChild(mk('option', { value: String(q.id), textContent: q.name }));
  g.append('Area from place', sel,
    small('Link', () => placeLink(z.id, +sel.value), 'Uses the place\'s shape: editing the place changes this zone too'),
    small('Copy', () => placeCopyArea(z.id, +sel.value), 'Takes a copy of the place\'s shape now: later changes to the place do not change this zone'));
  return g;
}

// The flattened export: the same mission with each link replaced by its own copy of the exact shape, as missionlab-plan/2,
// for MissionLab versions that cannot read links. Returns the text, or '' when nothing was exported.
function placeExportFlat() {
  if (exportHeld()) return '';
  const obj = scenarioFlatObject(), wrong = scenarioProblems(JSON.parse(JSON.stringify(obj))).filter(f => f.level === 'error');
  if (wrong.length) { say('Not exported: ' + (wrong[0].path ? wrong[0].path + ': ' : '') + wrong[0].text + '.'); return ''; }
  const text = scenarioText(obj), name = fileName().replace(/\.json$/, '') + '-flat.json';
  try {
    const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' })); a.download = name;
    document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(a.href), 4000);
    const body = obj.mission || obj, goalsKept = !!((body.goals && body.goals.length) || (body.extraction && body.extraction.goals));
    say(goalsKept
      ? 'Exported ' + name + ': every zone has its own copy of its shape and no links, but it keeps its goals, so it is still missionlab-plan/3 and versions before 0.27 cannot read it. Your scenario keeps its links.'
      : 'Exported ' + name + ': every zone has its own copy of its shape and no links, so older MissionLab versions read it. Your scenario keeps its links.');
  } catch (e) { say('The flattened copy could not be downloaded here.'); }
  return text;
}
$('placeFlat').addEventListener('click', placeExportFlat);
expose({ placeLink, placeCopyArea, placeDetach, placeDetachAll, placeRetarget, placeEditFor, placeLinkedZones, placeLinkProblems, placeLinksResolve, placeLinkBox, scenarioFlatObject, placeExportFlat });   // links to places (ML-040), for automated tests
