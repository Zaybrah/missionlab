// ------------------------------------------------------------------ control column
function say(text) { $('banner').textContent = text; $('banner').hidden = !text; clearTimeout(say.t); if (text) say.t = setTimeout(() => { $('banner').hidden = true; }, 9000); }
const setVal = (el, v) => { if (document.activeElement !== el) el.value = v; };
function syncPos() { const e = state.enemies[state.selected]; if (e) $('pos').textContent = 'x ' + e.pos[0].toFixed(1) + '   z ' + e.pos[2].toFixed(1) + '   height ' + e.pos[1].toFixed(1); }
function syncSide() {
  const m = state.map;
  $('exampleChip').hidden = !(m && m.isExample);
  $('mapInfo').textContent = live.on && !m ? '' : !m ? (state.navFile ? 'No map open. This scenario is for ' + state.navFile + '.' : 'No map open.')
    : m.isExample ? 'An invented compound to try the tools on. Open a real map file to start.'
    : m.name + ' · ' + Math.round(m.bounds.maxX - m.bounds.minX) + ' × ' + Math.round(m.bounds.maxZ - m.bounds.minZ) + ' m · ' + m.n.toLocaleString('en') + ' triangles'
      + (state.navFile && state.navFile.toLowerCase() !== m.name.toLowerCase() ? ' · the scenario was made for ' + state.navFile : '');

  const groups = new Set(state.enemies.map(e => e.group));
  $('count').textContent = state.enemies.length ? state.enemies.length + ' in ' + groups.size + (groups.size === 1 ? ' group' : ' groups') : '';
  $('empty').hidden = state.enemies.length > 0;
  const list = $('list'); list.textContent = '';
  let warn = 0;
  state.enemies.forEach((e, i) => {
    const w = warningFor(e); if (w) warn++;
    const li = document.createElement('li'), b = document.createElement('button');
    const op = liveOp(e);       // the last change to this marker failed or is still on its way: it is not shown as live
    b.type = 'button'; b.setAttribute('aria-current', String(i === state.selected)); b.dataset.squad = String(state.squad.includes(i)); b.dataset.role = e.vehicle ? 'Truck' : e.role; if (w) b.title = 'Enemy ' + (i + 1) + ' is ' + w + '.';
    if (op) b.title = (b.title ? b.title + ' ' : '') + 'The last change to enemy ' + (i + 1) + (op.status === 'failed' ? ' failed: ' : ' is still on its way into the game: ') + (op.said || 'no reason given') + '.';
    const cell = (cls, text) => { const s = document.createElement('span'); s.className = cls; s.textContent = text; b.appendChild(s); };
    cell('n', '#' + (i + 1)); cell('role', e.vehicle ? 'Truck' : e.hvt ? 'Target' : (e.fixed ? 'Sentry' : e.role) + (e.prone ? ', prone' : ''));
    cell('xy', 'group ' + (e.group + 1) + ' · '); const at = document.createElement('span'); at.className = 'c'; at.textContent = e.pos[0].toFixed(0) + ', ' + e.pos[2].toFixed(0);     // the coordinates in the number face
    b.lastChild.append(at, (e.either ? ' · ' + e.either + optionLetter(e.option) : '') + (e.chance < 100 ? ' · ' + e.chance + '%' : '') + (e.team === 4 ? ' · side 2' : e.team === 1 ? ' · ally' : '') + (e.minPlayers > 1 ? ' · ' + e.minPlayers + '+ players' : '') + (e.skill ? ' · ' + e.skill : '') + (e.nvg ? ' · night vision' : ''));
    cell('flag', op ? op.status : w ? 'check' : e.trigger && !(live.on && e.live) ? trigTag(e.trigger) : live.on && e.waiting ? 'waiting' : live.on && !e.live ? 'gone' : '');
    b.addEventListener('click', () => { select(i); const [x, y] = toScreen(e.pos[0], e.pos[2]), s = size(); if (x < 20 || y < 70 || x > s.w - 20 || y > s.h - 50) { state.view.cx = e.pos[0]; state.view.cz = e.pos[2]; invalidate(); } });
    li.appendChild(b); list.appendChild(li);
  });
  const wbox = $('warnings'); wbox.textContent = '';
  if (warn) { const c = document.createElement('span'); c.className = 'chip warn'; c.textContent = warn + (warn === 1 ? ' enemy to check' : ' enemies to check') + ' (hover a row, or select it, for the reason)'; wbox.appendChild(c); }
  const seen = dangers().filter(Boolean).length;
  const nl = document.createElement('label'), nb = document.createElement('input'); nl.className = 'check'; nl.style.flexBasis = '100%'; nb.type = 'checkbox'; nb.id = 'night'; nb.checked = !!state.night;
  nb.addEventListener('change', () => { state.night = nb.checked; changed(); });
  const ns = document.createElement('span'); ns.textContent = 'Night mission: enemies see 25 m instead of 50' + (state.insertion && state.insertionSpots.some(q => q.name === state.insertion) ? '. Checked against ' + state.insertion + ' only.' : ''); nl.append(nb, ns); wbox.appendChild(nl);
  const note = document.createElement('p'); note.className = 'hint'; note.id = 'sightNote';
  note.textContent = !state.infils.length ? 'Danger check: off. It needs the map\'s insertion points, from the game or a recon file.'
    : !state.geo ? 'Danger check: only distance so far. With the 3D map file it also finds enemies that can see an insertion point.'
    : seen ? 'Danger check: ' + seen + (seen === 1 ? ' enemy has' : ' enemies have') + ' an insertion point in view. Red dashed lines on the map show which.'
    : 'Danger check: no enemy that is there from the start can see an insertion point.';
  wbox.appendChild(note);

  // Squads: what can be done with several selected, and the squads kept for later.
  if (state.squad.some(i => i >= state.enemies.length)) state.squad = [];
  const many = picked(), sq = allSquads();
  $('squadInfo').textContent = many.length > 1 ? many.length + ' enemies selected. Drag any of them to move all; the panel below is for enemy ' + (state.selected + 1) + '.'
    : 'Shift-click enemies, or hold Shift and drag a box, to select several. Ctrl+C and Ctrl+V copy them; Ctrl+D doubles them.';
  $('sqActs').hidden = many.length < 2; $('sqTrigRow').hidden = many.length < 2 || !state.zones.length; $('sqSaveRow').hidden = many.length < 1; $('sqPaste').hidden = !clip;
  if (many.length > 1 && state.zones.length && document.activeElement !== $('sqTrig')) {
    const sel = $('sqTrig'), same = many.every(i => state.enemies[i].trigger === state.enemies[many[0]].trigger);
    sel.textContent = '';
    [['', 'Not the same for all'], [0, 'From the start'], ...state.zones.map(z => [z.id, trigText(z) + (z.name ? ': ' + z.name : '')])].forEach(([v, t]) => { const o = document.createElement('option'); o.value = v; o.textContent = t; sel.appendChild(o); });
    sel.value = same ? state.enemies[many[0]].trigger : '';
  }
  const pick = $('sqPick');
  if (document.activeElement !== pick && pick.dataset.key !== sq.map(q => q.name).join('|')) {
    const was = pick.value; pick.dataset.key = sq.map(q => q.name).join('|'); pick.textContent = '';
    sq.forEach((q, n) => { const o = document.createElement('option'); o.value = n; o.textContent = q.name + ' (' + q.data.length + ')'; pick.appendChild(o); });
    if (was && +was < sq.length) pick.value = was;
  }
  $('sqForget').hidden = !(sq[+pick.value] && !sq[+pick.value].fixed); $('squadCount').textContent = sq.length ? String(sq.length) : '';
  syncCardsAuthor();     // the Game Master budget in the Mission tab (ML-025, 29h-cards.js)
  syncOptionsAuthor(); syncQuick();     // host options in the Mission tab, the quick mission in the Map tab (ML-022, 29f-options.js)

  const e = state.enemies[state.selected];
  $('panel').hidden = !e;
  if (e) {
    $('selNo').textContent = '#' + (state.selected + 1);
    document.querySelectorAll('.seg button').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.role === e.role)));
    $('roleHint').textContent = warningFor(e) ? 'To check: ' + warningFor(e) + '.'
      : e.role === 'Guard' ? 'Stays where it is placed until it notices you.'
      : e.role === 'Wander' ? 'Roams around its position.'
      : 'Walks its ' + e.patrol.length + ' route stops in a loop. Choose Draw route to add stops, drag a stop to move it, Backspace removes the last one.';
    setVal($('group'), e.group + 1); setVal($('yaw'), Math.round(e.yaw)); setVal($('wander'), e.wanderDistance); setVal($('wait'), e.patrolWaitSeconds);
    $('defensive').checked = e.defensive; $('fixed').checked = e.fixed; $('prone').checked = e.prone; $('hvt').checked = !!e.hvt; $('vehicle').checked = !!e.vehicle;
    setVal($('chance'), e.chance || 100); syncEither(e); setVal($('team'), String(e.team || 0));
    setVal($('minPlayers'), Math.max(1, e.minPlayers || 1)); setVal($('skill'), e.skill || ''); $('nvg').checked = !!e.nvg; setVal($('tag'), e.tag || ''); $('tagList').replaceChildren(...tagsOf().map(t => Object.assign(document.createElement('option'), { value: t })));
    $('wanderRow').hidden = e.role !== 'Wander'; $('waitRow').hidden = e.role !== 'Patrol';
    $('clearRoute').hidden = !(e.role === 'Patrol' && e.patrol.length);
    syncPos();
  }

  $('tabEnemies').textContent = state.enemies.length || ''; $('tabTriggers').textContent = state.zones.length || ''; $('trigCount').textContent = state.zones.length ? String(state.zones.length) : ''; syncNext();
  syncCheck();
  $('tabDoors').textContent = (state.doors.filter(doorSet).length + state.things.length) || ''; $('noDoors').hidden = state.doors.length > 0;
  syncThings();
  // Doors. Choosing an enemy by any route lets go of the door.
  if (state.selected >= 0 && state.selected !== syncSide.lastEnemy) state.door = -1;
  syncSide.lastEnemy = state.selected;
  const dr = state.doors[state.door];
  $('doorPanel').hidden = !dr;
  if (dr) {
    setVal($('doorLock'), dr.locked === null ? '' : dr.locked ? '1' : '0'); setVal($('doorAi'), dr.enemiesCanOpen === null ? '' : dr.enemiesCanOpen ? '1' : '0'); setVal($('doorOpen'), dr.open == null ? '' : dr.open ? '1' : '0');
    setVal($('doorChance'), dr.chance || 100);
    $('doorNow').textContent = (dr.name || 'Door') + ' at ' + dr.mid[0].toFixed(0) + ', ' + dr.mid[2].toFixed(0) + ' · height ' + dr.mid[1].toFixed(1)
      + (dr.broken ? ' · broken open' : dr.isLocked === true ? ' · locked right now' : dr.isLocked === false ? ' · unlocked right now' : '')
      + (dr.enemiesKeptOut === true ? ' · enemies cannot open it right now' : '')
      + (dr.known ? '' : ' · not in the list of this map\'s doors, so it is drawn as a short line where the file says it is');
    $('doorClear').disabled = !doorSet(dr);
  }
  const setDoors = state.doors.filter(doorSet).length, knownDoors = state.doors.filter(d => d.known).length;
  $('doorsBox').hidden = !state.doors.length;
  $('doorCount').textContent = state.doors.length ? (knownDoors ? knownDoors + ' on this map, ' : '') + setDoors + ' set' : '';
  const dl = $('doorList'); dl.textContent = '';
  state.doors.forEach((d, n) => {
    if (!doorSet(d)) return;
    const li = document.createElement('li'), what = document.createElement('b'), at = document.createElement('span'), show = document.createElement('button');
    what.textContent = [d.locked === true ? 'Locked' : d.locked === false ? 'Unlocked' : '', d.enemiesCanOpen === false ? 'enemies kept out' : d.enemiesCanOpen === true ? 'enemies can open' : '', d.open === true ? 'open' : d.open === false ? 'closed' : ''].filter(Boolean).join(', ').replace(/^e/, 'E');
    at.textContent = d.mid[0].toFixed(0) + ', ' + d.mid[2].toFixed(0) + ' · height ' + d.mid[1].toFixed(1); at.className = 'hint';
    show.type = 'button'; show.className = 'btn'; show.textContent = 'Show'; show.style.padding = '3px 8px';
    show.addEventListener('click', () => { selectDoor(n); showAt(d.mid, d.mid[1] + 1.4); });
    li.append(what, at, show); dl.appendChild(li);
  });

  setVal($('title'), state.title); setVal($('scene'), state.scene);
  $('objectiveHint').hidden = !state.objectives.length; $('intelSection').hidden = !state.objectives.length;
  $('intelBox').hidden = !state.objectives.length;
  const il = $('intelList'); il.textContent = '';
  state.objectives.forEach(o => {
    const li = document.createElement('li'), name = document.createElement('b'), at = document.createElement('span'), show = document.createElement('button'), back = document.createElement('button');
    name.textContent = o.label + (o.obtained ? ' (picked up)' : o.moved !== false && o.home ? ' (moved)' : ''); at.textContent = o.pos[0].toFixed(0) + ', ' + o.pos[2].toFixed(0) + ' · height ' + o.pos[1].toFixed(1); at.className = 'hint';
    show.type = 'button'; show.className = 'btn'; show.textContent = 'Show'; show.style.padding = '3px 8px';
    show.addEventListener('click', () => showAt(o.pos, o.pos[1] + 1.8));
    // Only while connected to the game do we know where the game itself put the item.
    back.type = 'button'; back.className = 'btn'; back.textContent = 'Usual place'; back.style.padding = '3px 8px'; back.hidden = !(o.home && o.moved !== false);
    back.title = 'Put it back where the game laid it. It is then left out of the scenario file.';
    back.addEventListener('click', () => { willChange('Put intel ' + o.label + ' back in its usual place'); o.pos = o.home.slice(); o.yaw = o.homeYaw; o.moved = false; changed(); });
    li.append(name, at, show, back); il.appendChild(li);
  });
  const zl = $('zoneList');
  if (!zl.contains(document.activeElement)) {
    zl.textContent = '';
    state.zones.forEach((z, zi) => {
      const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
      const zEdit = what => willChange('Change ' + what + ' of ' + trigTitle(z).toLowerCase());      // one named undo step per field (ML-045)
      const li = mk('li'), title = mk('b', { textContent: trigTitle(z) });
      const name = mk('input', { type: 'text', value: z.name || '', placeholder: 'Name (optional)', maxLength: 40 }); name.setAttribute('aria-label', 'Name of ' + trigTitle(z));
      name.addEventListener('change', () => { zEdit('the name'); z.name = name.value.trim(); changed(); });
      const fire = mk('button', { type: 'button', className: 'btn', textContent: 'Fire now', hidden: !live.on || state.fired.includes(z.id) }); fire.style.padding = '3px 8px';
      fire.title = 'Immediate: sets this trigger off in the game now, whatever it waits for. Never staged, also in Draft mode.'; fire.addEventListener('click', () => liveCommand('fire', { id: z.id }));
      const del = mk('button', { type: 'button', className: 'btn danger', textContent: 'Delete' }); del.style.padding = '3px 8px';
      del.addEventListener('click', () => trigDelete(zi));
      // What it waits for.
      const what = mk('div', { className: 'what' });
      const numIn = (value, min, max, step, label, set) => { const el = mk('input', { type: 'number', min, max, step, value }); el.setAttribute('aria-label', label); el.addEventListener('change', () => { willChange('Change ' + label.charAt(0).toLowerCase() + label.slice(1)); set(Math.max(min, Math.min(max, +el.value || 0))); changed(); }); return el; };
      const grp = (...parts) => { const g = mk('span', { className: 'grp' }); g.append(...parts); return g; };
      if (z.when === 'zone') {
        // Shape: the old circle, or an area painted floor by floor (27a-paint.js), which then replaces the radius.
        const painted = !!(z.area && z.area.parts && z.area.parts.length), editing = paintIs('trigger', zi);
        const shape = mk('span', { className: 'shape' }), circleB = mk('button', { type: 'button', textContent: 'Circle' }), areaB = mk('button', { type: 'button', textContent: 'Painted area' });
        shape.setAttribute('role', 'group'); shape.setAttribute('aria-label', 'Shape of zone ' + z.id);
        circleB.setAttribute('aria-pressed', String(!painted && !editing)); areaB.setAttribute('aria-pressed', String(painted || editing));
        circleB.title = 'Within the radius of its anchor, and at most 2 m above or below it';
        areaB.title = 'A shape painted on the map, floor by floor, with holes left out: a house without its garden, street or courtyard';
        circleB.addEventListener('click', () => areaClear({ kind: 'trigger', index: zi }));
        areaB.addEventListener('click', () => { if (!editing) areaEditStart({ kind: 'trigger', index: zi }, painted ? z.area.parts[0].floor.slice(0, 2) : undefined); });
        shape.append(circleB, areaB);
        what.append(grp('Players walk in. Shape', shape));
        const placeBox = placeLinkBox(z); if (placeBox) what.append(placeBox);     // copy or link a named place's area (ML-040; 29b-places.js)
        if (!painted) what.append(grp('Radius', numIn(Math.round(z.radius), 2, 200, 1, 'Radius of zone ' + z.id + ' in metres', v => { z.radius = v; }), 'm'));
        what.append(grp('and stay', numIn(Math.round(z.hold || 0), 0, 3600, 5, 'Seconds players must stay in zone ' + z.id, v => { z.hold = v; }), 's'));
        // ML-055: the zone or hold watches nobody until the chosen trigger has fired, so a hold cannot be done before its goal starts.
        const gate = mk('select'); gate.setAttribute('aria-label', 'Which trigger ' + trigTitle(z) + ' starts after');
        [[0, 'At the start'], ...state.zones.filter(q => q.id !== z.id).map(q => [q.id, trigTitle(q) + (q.name ? ': ' + q.name : '')])].forEach(([v, t]) => gate.appendChild(mk('option', { value: v, textContent: t })));
        gate.value = state.zones.some(q => q.id === z.after && q.id !== z.id) ? z.after : 0; gate.addEventListener('change', () => { if (!outcomeSet(z.id, 'after', +gate.value || 0)) gate.value = z.after; });
        gate.title = 'Players inside count only once this trigger has fired; a hold\'s time starts then';
        what.append(grp('Starts after', gate));
      }
      else if (z.when === 'down') what.append(grp('Enemies down:', numIn(z.count | 0, 0, 200, 1, 'How many enemies must be down for trigger ' + z.id, v => { z.count = v | 0; })), z.count > 0 ? '' : ' (0: all of them)');
      else if (z.when === 'alert') what.append('An enemy notices the players');
      else if (z.when === 'time') what.append('The mission has started');
      else if (z.when === 'all' || z.when === 'any') what.append(z.when === 'all' ? 'All of the ticked triggers have fired' : 'One of the ticked triggers has fired');
      else if (z.when === 'intel') {
        const sel = mk('select'); sel.setAttribute('aria-label', 'Which intel item');
        const kinds = [...new Set(state.objectives.map(o => o.label || o.name).filter(Boolean))];
        if (z.item && z.item !== 'all' && !kinds.includes(z.item)) kinds.push(z.item);
        [['', 'Any item'], ['all', 'Every item'], ...kinds.map(k => [k, k])].forEach(([v, t]) => sel.appendChild(mk('option', { value: v, textContent: t })));
        sel.value = z.item || ''; sel.addEventListener('change', () => { zEdit('the intel item'); z.item = sel.value; changed(); });
        what.append(grp('Picked up:', sel));
      } else if (z.when === 'outcome') {
        // ML-047: waits for what was seen of the latest firing of another trigger, not for the firing itself.
        const sel = mk('select'); sel.setAttribute('aria-label', 'Which trigger\'s action ' + trigTitle(z) + ' watches');
        [[0, 'Choose a trigger'], ...state.zones.filter(q => q.id !== z.id).map(q => [q.id, trigTitle(q) + (q.name ? ': ' + q.name : '')])].forEach(([v, t]) => sel.appendChild(mk('option', { value: v, textContent: t })));
        sel.value = state.zones.some(q => q.id === z.after && q.id !== z.id) ? z.after : 0; sel.addEventListener('change', () => { if (!outcomeSet(z.id, 'after', +sel.value || 0)) sel.value = z.after; });
        const word = mk('select'); word.setAttribute('aria-label', 'Outcome ' + trigTitle(z) + ' waits for');
        [['applied', 'is seen to take'], ['failed', 'fails'], ['partial', 'only partly takes'], ['unconfirmed', 'goes through but cannot be seen'], ['timeout', 'has no answer in time']].forEach(([v, t]) => word.appendChild(mk('option', { value: v, textContent: t })));
        word.value = z.outcome || 'applied'; word.addEventListener('change', () => outcomeSet(z.id, 'outcome', word.value));
        const within = mk('input', { type: 'number', min: 1, max: 3600, step: 5, value: z.within || OUTCOME_WITHIN }); within.setAttribute('aria-label', 'Seconds ' + trigTitle(z) + ' gives the action to be seen');
        within.addEventListener('change', () => outcomeSet(z.id, 'within', +within.value));
        within.title = 'Only for "has no answer in time": how long after the firing an answer must come. A late answer is recorded, never acted on twice.';
        what.append(grp('When the action of', sel), grp(word));
        if (z.outcome === 'timeout') what.append(grp('Answer within', within, 's'));
      } else {
        const sel = mk('select'); sel.setAttribute('aria-label', 'Which trigger comes first');
        [[0, 'Choose a trigger'], ...state.zones.filter(q => q.id !== z.id).map(q => [q.id, trigTitle(q) + (q.name ? ': ' + q.name : '')])].forEach(([v, t]) => sel.appendChild(mk('option', { value: v, textContent: t })));
        sel.value = state.zones.some(q => q.id === z.after && q.id !== z.id) ? z.after : 0; sel.addEventListener('change', () => { zEdit('the trigger it waits for'); z.after = +sel.value || 0; changed(); });
        what.append(grp('After', sel));
      }
      if (z.when === 'zone' && isVec(z.pos)) { const at = placeName(z.pos); if (at) what.append(grp('Its spot is in ' + at)); }
      what.append(grp('Then wait', numIn(Math.round(z.delay || 0), 0, 7200, 5, 'Seconds ' + trigTitle(z) + ' waits before it fires', v => { z.delay = v; }), 's'));
      const face = mk('label'), box = mk('input', { type: 'checkbox', checked: z.facePlayer !== false });
      box.addEventListener('change', () => { zEdit('facing'); z.facePlayer = box.checked; changed(); });
      face.append(box, 'Its enemies appear looking at ' + (z.when === 'zone' ? 'the player who walks in' : 'the nearest player'));
      const n = state.enemies.filter(en => en.trigger === z.id).length, locks = (z.doors || []).filter(d => d.locked === true).length, opens = (z.doors || []).filter(d => d.locked === false).length, moves = (z.doors || []).filter(d => d.open != null).length;
      const sum = mk('div', { className: 'sum hint' });
      const act = mk('div', { className: 'what' });
      const pick = (label, options, value, set) => { const sel = mk('select'); sel.setAttribute('aria-label', label + ' of ' + trigTitle(z)); options.forEach(([v, t]) => sel.appendChild(mk('option', { value: v, textContent: t }))); sel.value = value || ''; sel.addEventListener('change', () => { zEdit(label.toLowerCase()); set(sel.value); changed(); }); return sel; };
      if (z.when === 'zone') { const el = mk('label'), eb = mk('input', { type: 'checkbox', checked: !!z.everyone }); eb.setAttribute('aria-label', 'Every player must be in zone ' + z.id); eb.addEventListener('change', () => { zEdit('every player'); z.everyone = eb.checked; changed(); }); el.append(eb, ' every player, not just one'); what.append(el); }
      if (z.when === 'down') { const tags = tagsOf(); what.append(grp('Only those tagged', pick('Tag wanted', [['', 'any enemy'], ...tags.map(t => [t, t]), ...(z.tag && !tags.includes(z.tag) ? [[z.tag, z.tag + ' (no enemy has it)']] : [])], z.tag || '', v => { z.tag = v; }))); }
      if (z.when === 'door') { const at = doorOf(z); what.append(grp('This door is opened or broken:', pick('Door watched', [...(at === '' ? [['', isVec(z.pos) ? 'the door from the file' : 'choose a door']] : []), ...state.doors.map((d, i) => [String(i), (d.name || 'Door') + ' ' + (i + 1)])], at, v => { if (v !== '' && state.doors[+v]) z.pos = state.doors[+v].pos.slice(0, 3); })), state.doors.length ? '' : ' Doors are listed once the game has sent them, on the World tab.'); }
      let ofBox = null;
      if (z.when === 'all' || z.when === 'any') {
        ofBox = mk('div', { className: 'of' });
        const others = state.zones.filter(q => q.id !== z.id);
        if (!others.length) ofBox.append('Add other triggers first, then tick them here.');
        for (const q of others) {
          const lab = mk('label'), cb = mk('input', { type: 'checkbox', checked: (z.of || []).includes(q.id) }); cb.setAttribute('aria-label', trigTitle(q) + ' counts for ' + trigTitle(z));
          cb.addEventListener('change', () => { zEdit('the triggers it waits for'); z.of = (z.of || []).filter(id => id !== q.id); if (cb.checked) z.of.push(q.id); changed(); });
          lab.append(cb, trigTitle(q) + (q.name ? ' (' + q.name + ')' : '')); ofBox.appendChild(lab);
        }
      }
      const odds = mk('div', { className: 'what' });
      odds.append(grp('Chance', numIn(z.chance || 100, 1, 100, 5, 'Chance in percent that ' + trigTitle(z) + ' is part of the mission', v => { z.chance = Math.round(v); }), '%'));
      if (z.delay >= 20) { const wl = mk('label'), wb = mk('input', { type: 'checkbox', checked: !!z.warn }); wb.setAttribute('aria-label', 'Warn players before ' + trigTitle(z)); wb.addEventListener('change', () => { zEdit('the warning'); z.warn = wb.checked; changed(); }); wl.append(wb, ' tell players how much time is left'); odds.append(wl); }
      odds.append(grp('Again after', numIn(Math.round(z.repeat || 0), 0, 3600, 10, 'Seconds before ' + trigTitle(z) + ' may fire again', v => { z.repeat = v > 0 && v < 5 ? 5 : v; }), 's'));
      if (z.repeat > 0) odds.append(grp('at most', numIn(z.times | 0, 0, 100, 1, 'How often ' + trigTitle(z) + ' may fire', v => { z.times = v | 0; }), 'times (0: no limit)'));
      act.append(grp('Lights', pick('Lights', [['', 'stay as they are'], ['off', 'go out'], ['on', 'come back on']], z.power, v => { z.power = v; })),
                 grp('Mission', pick('End of the mission', [['', 'goes on'], ['success', 'ends as a success'], ['failed', 'ends as failed']], z.end, v => { z.end = v; })));
      const rg = grp('Enemies within', numIn(Math.round(z.rally || 0), 0, 300, 10, 'Metres within which enemies walk to the place of ' + trigTitle(z), v => { z.rally = v; }), 'm walk there');
      const tick = (text, label, key) => { const l = mk('label'), c = mk('input', { type: 'checkbox', checked: !!z[key] }); c.setAttribute('aria-label', label + ' ' + trigTitle(z)); c.addEventListener('change', () => { zEdit('"' + text + '"'); z[key] = c.checked; changed(); }); l.append(c, ' ' + text); return l; };
      act.append(tick('its enemies come to the players', 'Enemies come to the players for', 'hunt'), tick('dead players come back', 'Dead players come back on', 'respawn'));
      rg.title = 'There: where the player stood who set off a zone, the door of a door trigger, and otherwise where the first player is.'; act.append(rg);
      const msg = mk('input', { type: 'text', className: 'msg', value: z.message || '', placeholder: 'Message on every player\'s screen (optional)', maxLength: 120 }); msg.setAttribute('aria-label', 'Message of ' + trigTitle(z));
      msg.addEventListener('change', () => { zEdit('the message'); z.message = cleanMessage(msg.value); changed(); });
      msg.title = '{enemies}, {players} and {time} become the numbers of the moment';
      const redL = mk('label'), redB = mk('input', { type: 'checkbox', checked: !!z.red }); redB.setAttribute('aria-label', 'Show the message of ' + trigTitle(z) + ' in red');
      redB.addEventListener('change', () => { zEdit('the message colour'); z.red = redB.checked; changed(); }); redL.append(redB, ' in red'); redL.hidden = !z.message;
      sum.textContent = [n + (n === 1 ? ' enemy' : ' enemies'), z.power ? (z.power === 'off' ? 'lights out' : 'lights on') : '', z.message ? 'shows a message' : '', z.end ? 'ends the mission' : '', z.rally > 0 ? 'calls enemies' : '', z.repeat > 0 ? 'repeats' : '', moves ? 'opens or closes ' + moves + (moves === 1 ? ' door' : ' doors') : '', z.hunt && n ? 'its enemies come to you' : '', z.respawn ? 'brings players back' : '', phaseWords(z), locks ? 'locks ' + locks + (locks === 1 ? ' door' : ' doors') : '', opens ? 'unlocks ' + opens + (opens === 1 ? ' door' : ' doors') : '', z.opensExtraction ? 'opens the extraction' : '', state.extractionAfter === z.id ? 'the extraction waits for it' : '', state.fired.includes(z.id) ? 'has fired' : ''].filter(Boolean).join(' · ');
      const exf = mk('label'), exb = mk('input', { type: 'checkbox', checked: !!z.opensExtraction });
      exb.addEventListener('change', () => { zEdit('the extraction'); z.opensExtraction = exb.checked; changed(); });
      exf.append(exb, 'It opens the extraction, whatever the mission\'s own rule says');
      li.append(title, name, fire, del, what); if (ofBox) li.append(ofBox); li.append(face, exf, act, odds, msg, redL, sum);
      const paintBox = paintBoxFor('trigger', zi); if (paintBox) li.append(paintBox);     // the painting tools, while this zone's area is painted
      zl.appendChild(li);
    });
  }
  // Settings for the whole mission.
  const fill = (sel, options, value) => {
    if (document.activeElement === sel) return;
    sel.textContent = ''; options.forEach(([v, t]) => { const o = document.createElement('option'); o.value = v; o.textContent = t; sel.appendChild(o); });
    sel.value = options.some(([v]) => String(v) === String(value)) ? value : options[0][0];
  };
  fill($('extractAfter'), [[0, 'By the mission\'s own rule'], ...state.zones.map(z => [z.id, 'Only after ' + trigTitle(z).toLowerCase() + (z.name ? ' (' + z.name + ')' : '')])], state.extractionAfter);
  const ways = state.insertions.slice(); if (state.insertion && !ways.includes(state.insertion)) ways.push(state.insertion);
  fill($('insertion'), [['', 'Each player\'s own choice'], ...ways.map(n => [n, 'Everyone starts at ' + n])], state.insertion);
  setVal($('wxRain'), state.weather.rain === null ? '' : state.weather.rain ? '1' : '0'); setVal($('wxStorm'), state.weather.sandstorm === null ? '' : state.weather.sandstorm ? '1' : '0');
  setVal($('wxHour'), state.weather.hour === null ? '' : state.weather.hour);
  setVal($('power'), state.power || ''); setVal($('allSkill'), state.allSkill || ''); $('allNvg').checked = !!state.allNvg; setVal($('respawnAfter'), Math.round(state.respawnAfter || 0)); setVal($('respawnTimes'), state.respawnTimes || 0);
  $('respawnHint').textContent = (state.respawnAfter > 0 ? 'Reinforcement wave: ' + Math.round(state.respawnAfter) + ' s after the first death, everyone who is dead by then comes back together' + (state.respawnTimes > 0 ? ', at most ' + state.respawnTimes + (state.respawnTimes === 1 ? ' wave' : ' waves') + ' per mission.' : ', as often as needed.') + ' The game may then not save the score.' : 'Wave after: 0 leaves dead players to the game.') + waveNow();
  $('needsGm').checked = !!state.gameMaster;
  if (document.activeElement !== $('briefing')) $('briefing').value = state.briefing.join('\n');
  setVal($('squadMin'), state.squadRange ? state.squadRange[0] : ''); setVal($('squadMax'), state.squadRange ? state.squadRange[1] : '');
  setVal($('minutes'), state.minutes > 0 ? state.minutes : '');
  $('scenarioId').value = state.scenarioId || 'Made on the first save'; $('revision').value = String(state.revision || 0);
  // Positions only match the map they were placed on; a changed map file is worth a look before the mission is run.
  $('mapPrintNote').hidden = !(state.mapPrint && state.map && !state.map.isExample && state.map.print && state.map.print !== state.mapPrint);
  syncPlaces();     // 29b-places.js: the places list and, on another map file, the review of every place
  syncScatter();    // 29g-scatter.js: placement areas, and the selected enemy's scatter with its preview and seed sweep
  const wn = state.weatherNow;
  $('missionHint').textContent = 'Extraction: the mission\'s own rule still applies; this only holds it back until a trigger has fired. Lights work on maps with a breaker box; Clean House has one. '
    + (wn ? (wn.has ? 'This map has weather' + (wn.fixedTime ? ', but its time of day is fixed.' : ' and a time of day.') : 'This map has no weather or time of day, so rain, sandstorm and hour do nothing here.')
          : 'Rain, sandstorm and hour only work on maps that have weather; Clean House, Mexico Depot and Georgia do not.')
    + (ways.length ? '' : ' To list a map\'s insertions, connect to the game or open a recon file.');
  // What each trigger does to the selected door.
  const dt = $('doorTrigs');
  if (!dt.contains(document.activeElement)) {
    dt.textContent = '';
    if (dr) state.zones.forEach(z => {
      const row = document.createElement('div'), lab = document.createElement('label'), sel = document.createElement('select'), id = 'doorTrig' + z.id;
      row.className = 'field'; lab.htmlFor = id; lab.textContent = trigTitle(z) + (z.name ? ': ' + z.name : ''); sel.id = id;
      [['', 'Leaves it alone'], ['1', 'Locks it'], ['0', 'Unlocks it'], ['o', 'Opens it'], ['c', 'Closes it'], ['cl', 'Closes and locks it'], ['uo', 'Unlocks and opens it']].forEach(([v, t]) => { const o = document.createElement('option'); o.value = v; o.textContent = t; sel.appendChild(o); });
      const cur = (z.doors || []).find(x => nearDoor(x.pos, [dr]) === 0);
      sel.value = !cur ? '' : cur.open === true ? (cur.locked === false ? 'uo' : 'o') : cur.open === false ? (cur.locked === true ? 'cl' : 'c') : cur.locked ? '1' : '0';
      sel.addEventListener('change', () => {
        willChange('Change what ' + trigTitle(z).toLowerCase() + ' does to ' + (dr.name ? 'door "' + dr.name + '"' : 'the door')); z.doors = (z.doors || []).filter(x => nearDoor(x.pos, [dr]) !== 0);
        const v = sel.value;
        if (v !== '') z.doors.push({ name: dr.name || '', pos: dr.pos.slice(), locked: v === '1' || v === 'cl' ? true : v === '0' || v === 'uo' ? false : null, open: v === 'o' || v === 'uo' ? true : v === 'c' || v === 'cl' ? false : null });
        changed();
      });
      row.append(lab, sel); dt.appendChild(row);
    });
  }
  $('triggerRow').hidden = !(e && state.zones.length);
  if (e && state.zones.length) {
    const sel = $('trigger');
    if (document.activeElement !== sel) {
      sel.textContent = '';
      [[0, 'From the start'], ...state.zones.map(z => [z.id, trigText(z) + (z.name ? ': ' + z.name : '')])].forEach(([v, t]) => { const o = document.createElement('option'); o.value = v; o.textContent = t; sel.appendChild(o); });
      sel.value = state.zones.some(z => z.id === e.trigger) ? e.trigger : 0;
    }
  }
  // An enemy takes one of the game's enemy types; a gun truck one of its vehicles.
  const kinds = e ? (e.vehicle ? live.vehicleTypes : live.types).slice() : [];
  if (e && e.type && !kinds.includes(e.type)) kinds.push(e.type);
  const tr = $('typeRow'); tr.hidden = !kinds.length;
  if (kinds.length) {
    const sel = $('type'), want = ['', ...kinds], key = (e.vehicle ? 'v|' : 'e|') + want.join('|');
    $('typeLabel').textContent = e.vehicle ? 'Vehicle' : 'Enemy type';
    if (sel.dataset.key !== key && document.activeElement !== sel) {
      sel.dataset.key = key; sel.textContent = '';
      want.forEach(t => { const o = document.createElement('option'); o.value = t; o.textContent = t ? t.replace(/^BOT V2 ?/, '').replace(/ Variant$/, '') || 'Standard' : e.vehicle ? 'The usual gun truck' : 'Random'; sel.appendChild(o); });
    }
    if (document.activeElement !== sel) sel.value = e.type || '';
  }
  // An inserted module with an outside reference still to bind (29e-modules.js) holds back every export until it is bound.
  const usable = state.enemies.length > 0 && !state.example, held = moduleBlock();
  $('save').disabled = $('copy').disabled = !usable || held !== '';
  $('fileText').value = usable && held === '' ? scenarioText() : '';
  if (!usable) $('saveMsg').textContent = state.example ? 'Open a real map file first: the example cannot be saved.' : 'Place at least one enemy to save.';
  else if (held) $('saveMsg').textContent = held;
  else if (/first|least/.test($('saveMsg').textContent)) $('saveMsg').textContent = '';
  syncModules(); syncPacks();

  const c = $('ceiling');
  if (m) { c.min = Math.floor(m.bounds.minY * 2) / 2; c.max = Math.ceil(m.bounds.maxY * 2) / 2; c.disabled = false; if (!isFinite(state.ceiling)) c.value = c.max; }
  else c.disabled = true;
  $('ceilingOut').textContent = isFinite(state.ceiling) ? 'up to ' + state.ceiling.toFixed(1) + ' m' : 'all floors';
  $('floorDown').disabled = $('floorUp').disabled = !m;
  syncPaint();
  syncHistory(); syncEncounter();     // 27-editing.js and 27b-history.js: the undo history, and moving an encounter together (ML-045)
}
// Any other edit makes a previewed repair stale, so its preview goes (ML-017).
// Live: the game's own state arriving (applying) becomes the staging base; in Draft mode an edit is staged, not sent (ML-014).
// Between missions there is no game to send edits to: a scenario opened from the catalogue stays on the page until saved (ML-013).
function changed() { if (state.repair) cancelRepair(); syncSide(); view3d.syncEnemies(); invalidate(); if (live.on) { if (live.applying) stageFollowGame(); else if (stageHolds()) stageChanged(); else if (live.mission) pushLive(); } else if (!stageKeepSeparate()) saveDraft(); viewsChanged(); }
const edit = fn => { const e = state.enemies[state.selected]; if (!e) return; willChange('Change enemy ' + (state.selected + 1)); fn(e); state.lastRole = e.role; state.lastGroup = e.group; changed(); };

document.querySelectorAll('.seg button').forEach(b => b.addEventListener('click', () => edit(e => { e.role = b.dataset.role; if (e.role === 'Patrol') { e.fixed = false; if (e.patrol.length < 2) { setTool('route'); say('Click the stops of the patrol route on the map.'); } } })));
$('group').addEventListener('change', ev => edit(e => { e.group = Math.max(0, Math.min(98, (parseInt(ev.target.value, 10) || 1) - 1)); }));
// A slider drag is one step: a transaction from its first movement to the release (ML-045).
$('yaw').addEventListener('input', ev => { const e = state.enemies[state.selected]; if (!e) return; if (!txOpen()) txBegin('Turn enemy ' + (state.selected + 1)); e.yaw = +ev.target.value; invalidate(); });
$('yaw').addEventListener('change', () => { txCommit(); changed(); });
$('wander').addEventListener('input', ev => { const e = state.enemies[state.selected]; if (e) { e.wanderDistance = Math.max(0, Math.min(200, Math.round(+ev.target.value || 0))); invalidate(); } });
$('wander').addEventListener('change', ev => edit(e => { e.wanderDistance = Math.max(0, Math.min(200, Math.round(+ev.target.value || 0))); }));
$('wait').addEventListener('change', ev => edit(e => { e.patrolWaitSeconds = Math.max(0, Math.min(120, +ev.target.value || 0)); }));
$('defensive').addEventListener('change', ev => edit(e => { e.defensive = ev.target.checked; }));
$('fixed').addEventListener('change', ev => edit(e => { e.fixed = ev.target.checked; if (e.fixed && e.role === 'Patrol') e.role = 'Guard'; }));
$('prone').addEventListener('change', ev => edit(e => { e.prone = ev.target.checked; }));
$('type').addEventListener('change', ev => edit(e => { e.type = ev.target.value; }));
// Chance and either/or go to everything selected: a squad shares one roll, and is one option of a set together.
function optionLetter(n) { return n >= 1 && n <= 26 ? String.fromCharCode(64 + n) : String(n || ''); }
const editPicked = fn => { const list = picked().map(i => state.enemies[i]); if (!list.length) return; willChange(list.length === 1 ? 'Change enemy ' + (picked()[0] + 1) : 'Change ' + list.length + ' enemies'); fn(list); changed(); };
function eitherSets() {
  const sets = new Map();
  for (const e of state.enemies) { if (!e.either) continue; if (!sets.has(e.either)) sets.set(e.either, new Map()); const o = sets.get(e.either); o.set(e.option, (o.get(e.option) || 0) + 1); }
  return sets;
}
function syncEither(e) {
  const sel = $('either'), sets = eitherSets(), opts = [['', 'Always part of the mission']];
  for (const [k, o] of [...sets].sort((a, b) => a[0] - b[0])) {
    for (const [n, c] of [...o].sort((a, b) => a[0] - b[0])) opts.push([k + ':' + n, 'Set ' + k + ', option ' + optionLetter(n) + ' (' + c + (c === 1 ? ' enemy)' : ' enemies)')]);
    opts.push([k + ':new', 'Set ' + k + ', a new option']);
  }
  opts.push(['new', 'A new either/or set']);
  if (document.activeElement !== sel) {
    sel.textContent = ''; for (const [v, t] of opts) { const o = document.createElement('option'); o.value = v; o.textContent = t; sel.appendChild(o); }
    sel.value = e.either ? e.either + ':' + e.option : '';
  }
  const n = picked().length, mates = e.lot ? state.enemies.filter(q => q.lot === e.lot && q.chance < 100).length - 1 : 0, bits = [];
  if (e.chance < 100) bits.push('There ' + e.chance + ' times in 100' + (mates > 0 ? ', together with ' + mates + (mates === 1 ? ' other' : ' others') + ' on the same roll' : '') + '.');
  if (e.either) { const o = sets.get(e.either) || new Map(); bits.push('One option of set ' + e.either + ' is there each mission; this is option ' + optionLetter(e.option) + ' of ' + o.size + '.'); }
  if (!bits.length) bits.push('Chance and either/or are rolled anew each time the mission starts.' + (n > 1 ? ' With ' + n + ' selected, they share one roll and one option.' : ''));
  $('chanceHint').textContent = bits.join(' ');
}
// A lot is the enemy's own (host options drop lots at any chance), so a change of chance keeps it. Several enemies made
// chancy together get a new shared lot only when they do not already share one, so that they share one roll.
function chanceSet(list, c) {
  const shared = list[0].lot > 0 && list.every(e => e.lot === list[0].lot);
  const lot = c < 100 && list.length > 1 && !shared ? 1 + Math.max(0, ...state.enemies.map(e => e.lot || 0)) : 0;
  for (const e of list) { e.chance = c; if (lot) e.lot = lot; }
}
$('chance').addEventListener('change', ev => editPicked(list => chanceSet(list, Math.max(1, Math.min(100, Math.round(+ev.target.value || 100))))));
expose({ chanceSet });   // chance keeps the lot (ML-044), for automated tests
$('team').addEventListener('change', ev => editPicked(list => { const t = +ev.target.value || 0; for (const e of list) e.team = t; }));
$('minPlayers').addEventListener('change', ev => editPicked(list => { const n = Math.max(1, Math.min(8, Math.round(+ev.target.value || 1))); for (const e of list) e.minPlayers = n > 1 ? n : 0; }));
$('skill').addEventListener('change', ev => editPicked(list => { const v = ev.target.value === 'recruit' || ev.target.value === 'veteran' ? ev.target.value : ''; for (const e of list) e.skill = v; }));
$('nvg').addEventListener('change', ev => editPicked(list => { for (const e of list) e.nvg = ev.target.checked; }));
$('tag').addEventListener('change', ev => editPicked(list => { const v = cleanTag(ev.target.value); for (const e of list) e.tag = v; }));
$('allSkill').addEventListener('change', ev => missionEdit(() => { state.allSkill = ev.target.value === 'recruit' || ev.target.value === 'veteran' ? ev.target.value : ''; }));
$('allNvg').addEventListener('change', ev => missionEdit(() => { state.allNvg = ev.target.checked; }));
$('needsGm').addEventListener('change', ev => missionEdit(() => { state.gameMaster = ev.target.checked; }));
$('repeatRun').addEventListener('change', ev => api('/api/repeat', { file: ev.target.value }).then(s => { live.rev = null; live.runsFor = null; say(ev.target.value ? 'The next mission you host repeats that run: same roll. A generated layout is repeated only if the map and the settings it was made with are unchanged; otherwise the game\'s own mission runs and the reason shows here. A scenario needs the same file and number of players to come out the same.' : 'The next mission gets a new roll.'); return applyLive(s); }).catch(e => say(liveTrouble(e))));
$('readyCopy').addEventListener('click', async () => {
  const text = readyReport($('readyBundle').checked ? await reportBundle() : null);
  (navigator.clipboard ? navigator.clipboard.writeText(text) : Promise.reject()).then(() => say('The report is on the clipboard. Its first lines list what it holds.')).catch(() => { $('fileText').value = text; say('The report could not be copied; it is in the text box of the File section instead.'); });
});
$('liveRecover').addEventListener('click', () => api('/api/recover', {}).then(s => { live.rev = null; return applyLive(s); }).catch(e => say(liveTrouble(e))));
// What the running game says about the wave, in words.
function waveNow() {
  const w = live.wave;
  if (!live.on || !live.mission || !w || !(w.after > 0)) return '';
  const left = w.left < 0 ? '' : ' Waves left: ' + w.left + '.';
  if (w.state === 'counting') return ' Now: a wave comes in about ' + w.in + ' s.' + left;
  if (w.state === 'pending') return ' Now: the game has been asked for a wave; waiting to see someone back.' + left;
  if (w.state === 'failed') return ' Now: the last wave failed (' + (w.error || 'no reason given') + '). It was not counted.' + left;
  return ' Now: nobody is waiting.' + left;
}
// Is everything ready? Asked from an address the game answers without its main thread, so it also works in the lobby and when the game is busy.
function readyRows(st, s) {
  const rows = [], add = (ok, text) => rows.push({ ok, text });
  if (!st) { add(false, 'The game does not answer. Is OPERATOR running with MissionLab installed?'); return rows; }
  add(true, 'MissionLab ' + (st.plugin || '?') + ' answers on port ' + (st.port || '?') + (st.plugin === EDITOR_VERSION ? '; this page is the same release.' : '.'));
  // Page and plugin come out of one ZIP; when they differ, the step that brings them together again is named.
  if (st.plugin && st.plugin !== EDITOR_VERSION) {
    const served = String(location.port) === String(st.port) ? '' : ', or open the page the game serves at http://localhost:' + st.port;
    add(false, 'This page is MissionLab ' + EDITOR_VERSION + ', the game runs MissionLab ' + st.plugin + '. Copy index.html and three.min.js from the MissionLab ' + st.plugin + ' ZIP into BepInEx/MissionLab/web/ and reload this page' + served + '. To use ' + EDITOR_VERSION + ' instead, install its ZIP whole, plugin included, and restart the game.');
  }
  if (st.startError) add(false, st.startError);
  // Another game version only warns: everything stays usable, as most of it usually still works.
  add(st.game === st.testedGame, st.game === st.testedGame ? 'OPERATOR ' + st.game + ': the version this MissionLab was tried with.' : 'OPERATOR ' + (st.game || '?') + ': this MissionLab was tried with ' + st.testedGame + '. It may not work, or only in part.');
  add(st.role !== 'client', st.role === 'host' ? 'You are the host.' : st.role === 'client' ? 'You joined someone else: MissionLab only works for the host. Nothing here changes that mission.' : 'No mission yet: host one to use the live tools.');
  if (st.mode) add(!!st.pve || st.role !== 'host', st.pve ? 'Game mode ' + st.mode + ': a PvE raid, the kind MissionLab works in.' : 'Game mode ' + st.mode + ': not a PvE raid. MissionLab leaves such a mission as the game made it; host a PvE raid to use it.');
  else if (st.role === 'host') add(false, 'The game mode could not be read, so MissionLab leaves this mission as the game made it.');
  if (st.role === 'host') add(!!st.mission, st.mission ? 'The mission is running on the map data ' + (st.map || '?') + '.' : 'A mission is starting, or its map data was not found; without it MissionLab leaves the mission as the game made it.');
  const compat = { tested: [true, 'has been played through with this release: the tested baseline.'],
    experimental: [false, 'has been tried, but not played through with this release: expect parts of it not to work.'],
    untested: [false, 'has not been played through with this release. It may well work; try it before you rely on it.'] }[st.compat];
  if (compat) add(compat[0], 'The map ' + (st.map || '?') + ' in ' + (st.mode || 'this mode') + ' as ' + (st.role || '?') + ' ' + compat[1]);
  add(!!st.library, st.library ? 'The 3D view works without the internet.' : 'The 3D view needs the internet: three.min.js is not beside the page.');
  if (st.rejected) add(false, st.rejected);
  for (const h of st.health || []) if (h.paused) add(false, 'The part "' + h.name + '" is paused: ' + (h.lastError || 'unknown error') + '.');
  if (s && s.page && PAGE_STAMP.indexOf('ML_PAGE_STAMP') < 0 && s.page !== PAGE_STAMP) add(false, 'This page is older than the one the game serves. Reload it.');
  for (const r of scenarioReadyRows(st, s)) rows.push(r);     // the next mission's scenario: its needs here and its capabilities (ML-036)
  return rows;
}
function syncReady(s) {
  const now = Date.now();
  if (now - live.statusAt < 3000) return;
  live.statusAt = now;
  fetch('/api/status', { cache: 'no-store' }).then(r => r.json()).catch(() => null).then(st => {
    live.status = st;
    const rows = readyRows(st, s), bad = rows.filter(r => !r.ok).length, list = $('readyList');
    $('readySum').textContent = bad ? bad + (bad === 1 ? ' thing needs' : ' things need') + ' a look' : 'Yes';
    list.textContent = '';
    for (const r of rows) { const li = document.createElement('li'); li.className = r.ok ? 'yes' : 'no'; li.textContent = (r.ok ? 'OK: ' : 'Look: ') + r.text; list.appendChild(li); }
  });
}
// A report goes to other people: folders and the names of players are taken out of everything in it, error texts included.
function scrubReport(text) {
  // Drive folders (also as written inside JSON, and with spaces in folder names), network shares and the usual home and
  // system folders of Linux and macOS. Taking out a little too much is better than leaving a user name in.
  let out = String(text).replace(/(^|[^A-Za-z0-9])[A-Za-z]:[\\/](?:[^\\/\n"'<>|]*[\\/])*[^\s"',;)\\/]*/g, '$1(folder)').replace(/(^|[\s"'(=])(?:\\\\){1,2}[^\s"',;)]+/g, '$1(folder)')
    .replace(/(^|[\s"'(=])\/(?:home|Users|mnt|media|run|tmp|var|opt|srv)\/(?:[^\\/\n"'<>|]*\/)*[^\s"',;)\/]*/g, '$1(folder)');
  const names = new Set();
  for (const p of [...(live.players || []), ...((replay.data && replay.data.players) || [])]) {
    const n = typeof p === 'string' ? p : p && typeof p.name === 'string' ? p.name : '';
    if (n.trim().length >= 3) names.add(n);
  }
  for (const n of names) out = out.split(n).join('(player)');
  return out;
}
const byteSize = text => new TextEncoder().encode(text).length;
// The optional parts of a report: the scenario on the page and the resolved plan of the game's last run, each with why it is missing.
async function reportBundle() {
  const st = live.status || {}, out = {};
  out.scenario = state.enemies.length > 0 && !state.example ? { name: fileName(), text: scenarioText() }
    : { name: fileName(), missing: state.example ? 'the page shows the example, not a scenario' : 'no enemies are placed' };
  if (!st.run) out.run = { name: '', missing: 'the game has written no run record yet' };
  else {
    try {
      const r = await fetch('/api/runfile/' + encodeURIComponent(st.run), { cache: 'no-store' });
      out.run = r.ok ? { name: st.run, text: await r.text() } : { name: st.run, missing: 'the game did not hand it over (status ' + r.status + ')' };
    } catch (e) { out.run = { name: st.run, missing: 'the game did not answer' }; }
  }
  return out;
}
// Versions and states first; with a bundle, the scenario and the resolved run follow. The first lines list what the report holds.
// Map files, 3D geometry, replays and game files are never part of it.
function readyReport(extra) {
  const st = live.status || {}, lines = [];
  lines.push('page ' + EDITOR_VERSION + ', plugin ' + (st.plugin || '?') + ', OPERATOR ' + (st.game || '?') + ' (tried with ' + (st.testedGame || '?') + '), role ' + (st.role || '?') + ', mission ' + (st.mission ? 'yes' : 'no') + ', 3D library beside the page ' + (st.library ? 'yes' : 'no'));
  lines.push('mode ' + (st.mode || '?') + (st.pve ? ' (PvE raid)' : '') + ', map data ' + (st.map || 'none') + ', compatibility ' + (st.compat || 'no map in use'));
  lines.push('session ' + (st.session || 'none') + ', run record ' + (st.run || 'none') + ', repeat ' + (st.repeat || 'no'));
  if (st.startError) lines.push('start: ' + st.startError);
  if (st.rejected) lines.push('refused: ' + st.rejected);
  for (const h of st.health || []) lines.push('part "' + h.name + '": ' + (h.paused ? 'paused' : h.ok ? 'ok' : h.failures + ' failures') + (h.lastError ? ', last error: ' + h.lastError : ''));
  for (const c of st.commands || []) lines.push('change ' + c.path + ': ' + c.status + (c.said ? ' (' + c.said + ')' : ''));
  for (const r of readyRows(live.status, null)) if (!r.ok) lines.push('look: ' + r.text);
  const summary = scrubReport(lines.join('\n')), parts = [], contents = ['- summary of versions and states: ' + byteSize(summary) + ' bytes'];
  for (const [key, label] of [['scenario', 'scenario'], ['run', 'resolved run']]) {
    const p = extra && extra[key];
    if (!p) continue;
    if (typeof p.text !== 'string') { contents.push('- ' + label + (p.name ? ' ' + p.name : '') + ': not included, ' + p.missing); continue; }
    const text = scrubReport(p.text);
    contents.push('- ' + label + ' ' + p.name + ': ' + byteSize(text) + ' bytes');
    parts.push('', '--- ' + label + ' ' + p.name + ' ---', text);
  }
  return ['MissionLab report', 'made ' + new Date().toISOString(), 'Contents:', ...contents,
    'Never included: game files, map files, 3D geometry, replays, player names, folders.', '', summary, ...parts].join('\n');
}
// The list of earlier runs is asked for when the page connects and whenever a mission has written a new record.
function syncRuns(s) {
  const sel = $('repeatRun'); if (!sel || !s || typeof s.run !== 'string') return;
  const key = s.run + '|' + (s.repeat || '');
  if (live.runsFor === key || document.activeElement === sel) return;
  live.runsFor = key;
  fetch('/api/runs', { cache: 'no-store' }).then(r => r.json()).then(d => {
    sel.textContent = ''; sel.append(new Option('A new roll', ''));
    for (const r of (d && d.runs) || []) {
      const when = String(r.startedUtc || '').slice(11, 16), what = r.scenario || 'generated layout';
      const o = new Option(when + ' UTC, ' + what + ', roll ' + r.roll + ', ' + r.players + (r.players === 1 ? ' player' : ' players') + (r.outcome === 'refused' ? ', refused' : ''), r.file);
      if (r.outcome === 'refused' && r.reason) o.title = r.reason;      // why it was refused, on hover: a refused repeat says what changed
      sel.append(o);
    }
    sel.value = (d && d.repeat) || '';
  }).catch(() => { live.runsFor = null; });
}
// A refused scenario and a part of the mod that is paused are shown at the top of the live box, mission or not.
function syncStanding(s) {
  const box = $('liveAlert'); if (!box) return;
  const paused = ((s && s.health) || []).filter(h => h.paused), lines = [];
  if (s && s.rejected) lines.push(s.rejected);
  for (const h of paused) lines.push('The part "' + h.name + '" is paused after ' + h.failures + ' errors in a row' + (h.retryIn > 0 ? '; next try in ' + h.retryIn + ' s' : '') + '. Last error: ' + (h.lastError || 'unknown') + '.');
  for (const p of (s && s.triggerProblems) || []) lines.push('Triggers: ' + p + '.');
  box.hidden = !lines.length; $('liveAlertText').textContent = lines.join(' '); $('liveRecover').hidden = !paused.length;
}
// Where a trigger stands in the running mission, in a few words.
function phaseWords(z) {
  const t = live.on && live.mission ? live.trig.get(z.id) : null;
  if (!t) return '';
  const again = t.fires > 1 ? ' (' + t.fires + ' times)' : '';
  return { off: 'now: sat this mission out', circle: 'now: in a circle, never fires', waiting: 'now: waiting', holding: 'now: being held', counting: 'now: counting down',
           fired: 'now: fired' + again, cooling: 'now: fired' + again + ', armed again later', exhausted: 'now: fired' + again + ', done repeating' }[t.phase] || '';
}
$('respawnAfter').addEventListener('change', ev => missionEdit(() => { const v = Math.max(0, Math.min(600, +ev.target.value || 0)); state.respawnAfter = v > 0 && v < 3 ? 3 : v; }));
$('respawnTimes').addEventListener('change', ev => missionEdit(() => { state.respawnTimes = Math.max(0, Math.min(50, Math.round(+ev.target.value || 0))); }));
$('gmOrder').addEventListener('click', () => {
  const ids = picked().map(i => state.enemies[i]).filter(e => e && e.live && e.id).map(e => e.id);
  if (!ids.length) return say('Select one or more live enemies first.');
  state.order = ids; say('Now click the spot on the map where ' + (ids.length === 1 ? 'that enemy' : 'those ' + ids.length + ' enemies') + ' should go.');
});
$('recipeAdd').addEventListener('click', () => {
  willChange('Add the triggers of a recipe'); let id = state.zones.reduce((m, q) => Math.max(m, q.id), 0);
  const add = (when, more) => { const z = newTrigger(++id, when, more); state.zones.push(z); return z; }, kind = $('recipe').value;
  if (kind === 'countdown') { const a = add('intel', { name: 'Intel taken', message: 'You have the intel. Get out.' }); add('after', { name: 'Out of time', after: a.id, delay: 180, warn: true, message: 'Out of time.', red: true, end: 'failed' }); }
  else if (kind === 'help') add('alert', { name: 'Alarm', delay: 5, rally: 60, message: 'They know you are here.', red: true });
  else if (kind === 'waves') add('time', { name: 'Help arrives', delay: 120, repeat: 120, times: 3, message: 'More of them are coming.' });
  else if (kind === 'blackout') add('alert', { name: 'Blackout', delay: 3, power: 'off', message: 'They cut the power.' });
  else add('down', { name: 'Targets down', tag: 'target', count: 0, message: 'All targets are down.', end: 'success' });
  changed(); say('Added. Change the texts and the times to taste' + (kind === 'waves' ? ', and bind enemies to it on the Enemies tab.' : kind === 'targets' ? ', and give enemies the tag "target".' : '.'));
});
$('briefing').addEventListener('change', ev => missionEdit(() => { state.briefing = briefingIn(ev.target.value.split('\n')); }));
// The author's recommended squad: either box alone stands for both ends; both empty says nothing.
const squadEdit = () => missionEdit(() => {
  const read = id => { const v = Math.round(+$(id).value); return $(id).value.trim() !== '' && v > 0 ? Math.min(16, v) : 0; };
  const lo = read('squadMin'), hi = read('squadMax');
  state.squadRange = lo || hi ? [Math.min(lo || hi, hi || lo), Math.max(lo || hi, hi || lo)] : null;
});
$('squadMin').addEventListener('change', squadEdit); $('squadMax').addEventListener('change', squadEdit);
$('minutes').addEventListener('change', ev => missionEdit(() => { const v = +ev.target.value || 0; state.minutes = v > 0 ? Math.min(600, v) : 0; }));
$('doorChance').addEventListener('change', ev => doorEdit(d => { d.chance = Math.max(1, Math.min(100, Math.round(+ev.target.value || 100))); }));
$('thingChance').addEventListener('change', ev => editThing(t => { t.chance = Math.max(1, Math.min(100, Math.round(+ev.target.value || 100))); }));
// Game Master, while a mission runs: words for the players, and the lights.
$('gmSend').addEventListener('click', () => { const text = cleanMessage($('gmText').value); if (!text) return say('Type the message first.'); liveCommand('try', { what: 'message', arg: text }).then(() => { $('gmText').value = ''; say('Message sent to every player.'); }); });
$('gmText').addEventListener('keydown', ev => { if (ev.key === 'Enter') { ev.preventDefault(); $('gmSend').click(); } });
$('gmDark').addEventListener('click', () => liveCommand('try', { what: 'power', arg: 'off' }));
$('gmLight').addEventListener('click', () => liveCommand('try', { what: 'power', arg: 'on' }));

// Things: weapons and the like, laid on the ground.
function selectThing(i) { state.thing = i; if (i >= 0) { showTab('doors'); state.door = -1; } syncSide(); invalidate(); }
function syncThings() {
  const sel = $('thingName'); if (!sel) return;
  const groups = THING_GROUPS.map(g => [g[0], g[1].slice()]), known = new Set(groups.flatMap(g => g[1]));
  const more = (live.things || []).concat(state.things.map(t => t.name)).filter(n => !known.has(n));
  if (more.length) groups.push(['Also in this game', [...new Set(more)].sort()]);
  const key = groups.map(g => g[1].join('|')).join('||');
  if (sel.dataset.key !== key) {
    sel.dataset.key = key; sel.textContent = '';
    for (const [title, names] of groups) { const og = document.createElement('optgroup'); og.label = title; for (const n of names) { const o = document.createElement('option'); o.value = n; o.textContent = n.trim(); og.appendChild(o); } sel.appendChild(og); }
  }
  if (document.activeElement !== sel) sel.value = state.thingName;
  $('thingCount').textContent = state.things.length ? state.things.length + ' placed' : '';
  const list = $('thingList'); list.textContent = '';
  state.things.forEach((t, n) => {
    const li = document.createElement('li'), b = document.createElement('button'), a = document.createElement('span'), c = document.createElement('span');
    b.type = 'button'; a.textContent = t.name.trim(); c.textContent = (t.trigger ? trigTag(t.trigger) + ' \u00b7 ' : '') + t.pos[0].toFixed(0) + ', ' + t.pos[2].toFixed(0);
    if (n === state.thing) b.setAttribute('aria-current', 'true');
    b.append(a, c); b.addEventListener('click', () => { selectThing(n); state.view.cx = t.pos[0]; state.view.cz = t.pos[2]; invalidate(); });
    li.appendChild(b); list.appendChild(li);
  });
  const t = state.things[state.thing]; $('thingPanel').hidden = !t;
  if (t) {
    setVal($('thingYaw'), Math.round(t.yaw || 0));
    const ts = $('thingTrigger');
    if (document.activeElement !== ts) {
      ts.textContent = ''; [[0, 'From the start'], ...state.zones.map(z => [z.id, trigText(z)])].forEach(([v, text]) => { const o = document.createElement('option'); o.value = v; o.textContent = text; ts.appendChild(o); });
      ts.value = String(state.zones.some(z => z.id === t.trigger) ? t.trigger : 0);
    }
    setVal($('thingChance'), t.chance || 100);
  }
}
const editThing = fn => { const t = state.things[state.thing]; if (!t) return; willChange('Change the ' + String(t.name).trim()); fn(t); changed(); };
$('thingName').addEventListener('change', ev => { state.thingName = ev.target.value; const t = state.things[state.thing]; if (t) editThing(q => { q.name = state.thingName; }); else syncSide(); });
$('thingYaw').addEventListener('change', ev => editThing(t => { t.yaw = ((Math.round(+ev.target.value || 0) % 360) + 360) % 360; }));
$('thingTrigger').addEventListener('change', ev => editThing(t => { t.trigger = +ev.target.value || 0; }));
$('thingDel').addEventListener('click', () => { if (state.thing < 0) return; const t = state.things[state.thing]; willChange('Delete the ' + (t ? String(t.name).trim() : 'thing')); state.things.splice(state.thing, 1); state.thing = -1; changed(); });

$('either').addEventListener('change', ev => editPicked(list => {
  const v = ev.target.value; let set = 0, opt = 0;
  if (v === 'new') { set = 1 + Math.max(0, ...state.enemies.map(e => e.either || 0)); opt = 1; }
  else if (v) { const parts = v.split(':'); set = +parts[0]; opt = parts[1] === 'new' ? 1 + Math.max(0, ...state.enemies.filter(e => e.either === set).map(e => e.option || 0)) : +parts[1]; }
  for (const e of list) { e.either = set; e.option = set ? opt : 0; }
}));
$('hvt').addEventListener('change', ev => edit(e => { e.hvt = ev.target.checked; if (e.hvt) { e.trigger = 0; e.type = ''; e.vehicle = false; } }));
$('vehicle').addEventListener('change', ev => edit(e => { e.vehicle = ev.target.checked; if (e.vehicle) { e.trigger = 0; e.type = ''; e.hvt = false; e.prone = false; e.role = 'Guard'; e.patrol = []; } }));
$('trigger').addEventListener('change', ev => edit(e => { e.trigger = +ev.target.value || 0; }));
function addZone(x, y, z) {
  willChange('Add zone ' + (state.zones.reduce((m, q) => Math.max(m, q.id), 0) + 1));
  const id = state.zones.reduce((m, q) => Math.max(m, q.id), 0) + 1;
  state.zones.push(newTrigger(id, 'zone', { pos: [x, y, z] })); showTab('triggers');
  const e = state.enemies[state.selected];
  changed();
  say('Zone ' + id + ' placed. Select an enemy and set "Appears" to bind it to this zone.' + (e ? '' : ''));
}
$('clearRoute').addEventListener('click', () => edit(e => { e.patrol = []; }));
$('del').addEventListener('click', removeSelected);
$('title').addEventListener('input', ev => { state.title = ev.target.value; changed(); });
$('scene').addEventListener('input', ev => { state.scene = ev.target.value; changed(); });
document.querySelectorAll('#tools .tool').forEach(b => b.addEventListener('click', () => setTool(b.dataset.tool)));
$('layersBtn').addEventListener('click', () => { $('layerBox').hidden = !$('layerBox').hidden; });
$('mode2d').addEventListener('click', () => setMode('2d'));
$('mode3d').addEventListener('click', () => setMode('3d'));
window.addEventListener('keyup', ev => view3d.key(ev.key.toLowerCase(), false));
$('undo').addEventListener('click', undo);
$('fit').addEventListener('click', fit);
$('zoomIn').addEventListener('click', () => { const s = size(); zoomAt(s.w / 2, s.h / 2, 1.4); });
$('zoomOut').addEventListener('click', () => { const s = size(); zoomAt(s.w / 2, s.h / 2, 1 / 1.4); });
// One history entry per new trigger; the Add trigger button and the automated tests come through here.
function trigAdd(when) {
  const id = state.zones.reduce((m, q) => Math.max(m, q.id), 0) + 1, last = state.zones.length ? state.zones[state.zones.length - 1].id : 0;
  willChange('Add trigger ' + id);
  state.zones.push(newTrigger(id, when, when === 'time' ? { delay: 300 } : when === 'after' || when === 'outcome' ? { after: last } : {}));
  changed();
  return id;
}
// One field of an outcome trigger (ML-047), as its row sets it: the watched trigger, the outcome word or the window in
// seconds; or the gate of a zone or hold (ML-055), where 0 is no gate. One named history entry each; a value the file
// would refuse is not taken, so the row cannot make one.
function outcomeSet(id, key, value) {
  const z = state.zones.find(q => q.id === id);
  if (!z || (z.when !== 'outcome' && !(z.when === 'zone' && key === 'after'))) return false;
  let v;
  if (key === 'after') { v = value | 0; if (v === z.id || ((z.when === 'outcome' || v !== 0) && !state.zones.some(q => q.id === v))) return false; }
  else if (key === 'outcome') { if (!OUTCOMES.includes(value)) return false; v = value; }
  else if (key === 'within') { v = Math.max(1, Math.min(3600, +value || OUTCOME_WITHIN)); }
  else return false;
  if (z[key] === v) return true;
  willChange('Change ' + (key === 'after' ? (z.when === 'zone' ? 'the trigger it starts after' : 'the trigger it watches') : key === 'outcome' ? 'the outcome it waits for' : 'the answer window') + ' of ' + trigTitle(z).toLowerCase());
  z[key] = v;
  changed();
  return true;
}
// Deletes one trigger in one history entry: what waited for it (enemies, after and outcome triggers, a zone or hold's gate
// (ML-055), the extraction) no longer does, as a gate on a trigger that is gone would never be installed.
function trigDelete(zi) {
  const z = state.zones[zi];
  if (!z) return false;
  willChange('Delete ' + trigTitle(z).toLowerCase());
  state.zones.splice(zi, 1);
  state.enemies.forEach(en => { if (en.trigger === z.id) en.trigger = 0; });
  state.zones.forEach(q => { if (q.after === z.id) q.after = 0; });
  if (state.extractionAfter === z.id) state.extractionAfter = 0;
  changed();
  return true;
}
expose({ trigAdd, outcomeSet, trigDelete });   // adding, editing and deleting triggers (ML-047, ML-055), for automated tests
$('trigNew').addEventListener('click', () => {
  const id = trigAdd($('trigKind').value);
  showTab('triggers');
  say('Trigger ' + id + ' added. Select an enemy and set "Appears" to bind it, or select a door to have the trigger lock or unlock it.');
});
// Waves: each comes from one side, walks to the spot and stays there. A wave is a trigger with its enemies:
// the first fires on a timer, each next one a while after the one before.
function addWaves(count, size, first, gap) {
  if (!state.map) return say('Open a map first.');
  const lead = state.enemies[state.selected];
  const C = lead ? lead.pos.slice() : (() => { const y = groundAt(state.view.cx, state.view.cz); return y === null ? null : [state.view.cx, y, state.view.cz]; })();
  if (!C) return say('The middle of the view is not walkable ground. Select an enemy at the spot to hold, or move the map.');
  const far = (x, z, min) => !state.infils.some(p => Math.hypot(p[0] - x, p[2] - z) < min);
  // A walkable spot at a distance and bearing from the middle, on the ground floor of wherever it lands.
  const spotAt = (bearing, dist, wantY) => {
    const x = C[0] + Math.sin(bearing) * dist, z = C[2] + Math.cos(bearing) * dist, levels = levelsAt(x, z);
    if (!levels.length) return null;
    return [x, wantY === undefined ? Math.min(...levels) : floorNear(x, z, wantY), z];
  };
  willChange(count === 1 ? 'Add a wave' : 'Add ' + count + ' waves');
  let id = state.zones.reduce((m, q) => Math.max(m, q.id), 0), group = state.enemies.reduce((m, e) => Math.max(m, e.group + 1), 0), prev = 0, made = 0, waves = 0;
  for (let w = 0; w < count; w++) {
    // The side this wave comes from: tried until enough starting spots are found there, well away from where players arrive.
    let starts = [], bearing = 0;
    for (let tries = 0; tries < 40 && starts.length < size; tries++) {
      bearing = Math.random() * Math.PI * 2; starts = [];
      for (let n = 0; n < 200 && starts.length < size; n++) {
        const p = spotAt(bearing + (Math.random() - 0.5) * 0.9, 45 + Math.random() * 50);
        if (p && far(p[0], p[2], 30) && !starts.some(q => Math.hypot(q[0] - p[0], q[2] - p[2]) < 3)) starts.push(p);
      }
    }
    if (!starts.length) continue;
    id++;
    state.zones.push(newTrigger(id, w === 0 ? 'time' : 'after', { name: 'Wave ' + (w + 1), delay: w === 0 ? first : gap, after: w === 0 ? 0 : prev, facePlayer: false }));
    prev = id; waves++;
    for (const p of starts) {
      // Where it walks to: two spots close to the middle on its own side. It goes to the first, waits long, then shifts a little.
      const stops = [];
      for (let n = 0; n < 60 && stops.length < 2; n++) { const q = spotAt(bearing + (Math.random() - 0.5) * 2, 5 + Math.random() * 8, C[1]); if (q && Math.abs(q[1] - C[1]) < 1.5) stops.push(q); }
      const e = newEnemy(p, stops.length === 2 ? 'Patrol' : 'Wander', group);
      e.trigger = id; e.patrol = stops.length === 2 ? stops : []; e.patrolWaitSeconds = 600; e.wanderDistance = stops.length === 2 ? 0 : 20;
      e.yaw = ((Math.atan2(C[0] - p[0], C[2] - p[2]) * 180 / Math.PI) % 360 + 360) % 360;
      state.enemies.push(e); made++;
    }
    group++;
  }
  showTab('triggers'); changed();
  say(made ? made + ' enemies in ' + waves + (waves === 1 ? ' wave' : ' waves') + ' added around ' + C[0].toFixed(0) + ', ' + C[2].toFixed(0) + '. The first comes after ' + first + ' s, each next one ' + gap + ' s after the one before.'
    : 'No room was found for the waves to start from. Try another spot.');
}
$('wvAdd').addEventListener('click', () => {
  const n = id => Math.round(+$(id).value || 0);
  addWaves(Math.max(1, Math.min(10, n('wvCount'))), Math.max(1, Math.min(12, n('wvSize'))), Math.max(0, Math.min(3600, n('wvFirst'))), Math.max(10, Math.min(3600, n('wvGap'))));
});
$('sqCopy').addEventListener('click', copySquad);
$('sqPaste').addEventListener('click', () => { if (clip) startPlacing(clip, 'the copied enemies'); });
$('sqTurnL').addEventListener('click', () => turnSquad(-45));
$('sqTurnR').addEventListener('click', () => turnSquad(45));
$('sqDel').addEventListener('click', removeSelected);
$('sqGroup').addEventListener('click', () => { const list = picked(); if (list.length < 2) return; willChange('Make ' + list.length + ' enemies one group'); const g = state.enemies[state.selected >= 0 ? state.selected : list[0]].group; for (const i of list) state.enemies[i].group = g; changed(); say('All ' + list.length + ' are group ' + (g + 1) + ' now.'); });
$('sqTrig').addEventListener('change', ev => { if (ev.target.value === '') return; const list = picked(); willChange('Change when ' + list.length + ' enemies appear'); for (const i of list) { const e = state.enemies[i]; if (!e.hvt && !e.vehicle) e.trigger = +ev.target.value || 0; } changed(); });
$('sqSave').addEventListener('click', () => {
  const list = picked(), name = $('sqName').value.trim();
  if (!list.length) return;
  if (!name) { $('sqName').focus(); return say('Give the squad a name first.'); }
  const mine = savedSquads().filter(q => q.name !== name); mine.push({ name, data: squadData(list).map(d => Object.assign(d, { trigger: 0 })) });
  storeSquads(mine); $('sqName').value = ''; syncSide(); say('Squad "' + name + '" saved on this computer. Pick it under Squads and press Place.');
});
$('sqPick').addEventListener('change', syncSide);
$('sqPlace').addEventListener('click', () => { const q = allSquads()[+$('sqPick').value]; if (q) startPlacing(q.data, 'the squad "' + q.name + '"'); });
$('sqForget').addEventListener('click', () => { const q = allSquads()[+$('sqPick').value]; if (!q || q.fixed) return; storeSquads(savedSquads().filter(x => x.name !== q.name)); syncSide(); say('Squad "' + q.name + '" forgotten.'); });
const missionEdit = fn => { willChange('Change the mission settings'); fn(); changed(); };
$('extractAfter').addEventListener('change', ev => missionEdit(() => { state.extractionAfter = +ev.target.value || 0; }));
$('insertion').addEventListener('change', ev => missionEdit(() => { state.insertion = ev.target.value; }));
$('power').addEventListener('change', ev => missionEdit(() => { state.power = ev.target.value === 'off' || ev.target.value === 'on' ? ev.target.value : ''; }));
$('wxRain').addEventListener('change', ev => missionEdit(() => { state.weather.rain = ev.target.value === '' ? null : ev.target.value === '1'; }));
$('wxStorm').addEventListener('change', ev => missionEdit(() => { state.weather.sandstorm = ev.target.value === '' ? null : ev.target.value === '1'; }));
$('wxHour').addEventListener('change', ev => missionEdit(() => { state.weather.hour = ev.target.value === '' ? null : Math.max(0, Math.min(24, +ev.target.value || 0)); }));
const doorEdit = fn => { const d = state.doors[state.door]; if (!d) return; willChange('Change ' + (d.name ? 'door "' + d.name + '"' : 'door ' + (state.door + 1))); fn(d); changed(); };
$('doorLock').addEventListener('change', ev => doorEdit(d => { d.locked = ev.target.value === '' ? null : ev.target.value === '1'; }));
$('doorAi').addEventListener('change', ev => doorEdit(d => { d.enemiesCanOpen = ev.target.value === '' ? null : ev.target.value === '1'; }));
$('doorClear').addEventListener('click', () => doorEdit(d => { d.locked = null; d.enemiesCanOpen = null; d.open = null; }));
$('doorOpen').addEventListener('change', ev => doorEdit(d => { d.open = ev.target.value === '' ? null : ev.target.value === '1'; }));
$('ceiling').addEventListener('input', ev => { state.ceiling = +ev.target.value >= +ev.target.max ? Infinity : +ev.target.value; syncSide(); invalidate(); });
// A click in the 3D view: the same tools as on the flat map. p = [x, y, z] in game coordinates.
function click3d(p, enemyIndex, button) {
  if (button === 2) { if (p && lookAt(p[0], p[2])) say('Enemy ' + (state.selected + 1) + ' now looks that way.'); return; }
  if (state.tool === 'zone' && p && enemyIndex < 0) { addZone(p[0], p[1], p[2]); return; }
  if (enemyIndex >= 0 && !(state.tool === 'route' && state.selected >= 0 && enemyIndex !== state.selected)) { select(enemyIndex); return; }
  if (!p) { if (state.tool === 'select') select(-1); return; }
  const level = levelsAt(p[0], p[2]).filter(y => Math.abs(y - p[1]) <= 1.0).sort((a, b) => Math.abs(a - p[1]) - Math.abs(b - p[1]))[0];
  if (state.tool === 'add') {
    willChange('Add enemy ' + (state.enemies.length + 1));
    const walk = level !== undefined;
    const e = newEnemy(walk ? [p[0], level, p[2]] : p, walk ? (state.lastRole === 'Patrol' ? 'Guard' : state.lastRole) : 'Guard', state.lastGroup);
    if (!walk) e.fixed = true;                           // on furniture, a roof, under a bed: it can only stay put
    state.enemies.push(facePlayers(e)); state.selected = state.enemies.length - 1; changed();
    say('Enemy ' + state.enemies.length + ' placed' + (walk ? '.' : ' as a fixed sentry: nobody can walk on that spot.'));
  } else if (state.tool === 'route') {
    const e = state.enemies[state.selected];
    if (!e) return say('Select an enemy first, then click the stops of its route.');
    if (level === undefined) return say('Route stops must be on walkable ground.');
    willChange('Add a route stop to enemy ' + (state.selected + 1)); e.role = 'Patrol'; e.fixed = false; e.patrol.push([p[0], level, p[2]]); changed();
    say('Stop ' + e.patrol.length + ' added' + (e.patrol.length < 2 ? '. Add at least one more.' : '.'));
  } else select(-1);
}
// Floors of the buildings in view: heights found where ground is stacked above other ground.
function floorsInView() {
  const { w, h } = size(), ys = [], focus = state.mode === '3d' ? view3d.focus() : null;
  for (let gx = 0; gx < 40; gx++) for (let gy = 0; gy < 26; gy++) {
    const [x, z] = focus ? [focus[0] + (gx - 19.5) * 1.2, focus[1] + (gy - 12.5) * 1.2] : toWorld(w * (gx + 0.5) / 40, h * (gy + 0.5) / 26), l = levelsAt(x, z);
    if (l.length > 1) ys.push(...l);
  }
  ys.sort((p, q) => p - q);
  const floors = [];
  for (const y of ys) { const last = floors[floors.length - 1]; if (last && y - last.hi < 1.2) last.hi = y; else floors.push({ lo: y, hi: y }); }
  return floors;
}
// Each step down hides the top visible floor; each step up brings one back.
function stepFloor(dir) {
  if (!state.map) return;
  const floors = floorsInView();
  if (floors.length < 2) return say('No building with several floors is in view. Zoom in on one first.');
  const shown = floors.filter(f => f.lo <= state.ceiling + 0.4), hidden = floors.filter(f => f.lo > state.ceiling + 0.4);
  if (dir < 0) { if (shown.length < 2) return say('This is the lowest floor here.'); state.ceiling = (shown[shown.length - 2].hi + shown[shown.length - 1].lo) / 2; }
  else state.ceiling = hidden.length > 1 ? (hidden[0].hi + hidden[1].lo) / 2 : Infinity;
  const c = $('ceiling'); c.value = isFinite(state.ceiling) ? state.ceiling : c.max;
  syncSide(); invalidate();
  say(isFinite(state.ceiling) ? 'Showing the ground up to ' + state.ceiling.toFixed(1) + ' m. New enemies land on the highest floor you can see.' : 'Showing all floors.');
}
$('floorDown').addEventListener('click', () => stepFloor(-1));
$('floorUp').addEventListener('click', () => stepFloor(1));

