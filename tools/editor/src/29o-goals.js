// ------------------------------------------------------------------ objectives (ML-046)
// What the players are asked to do: "Recover the phone", then "Secure the upper floor", with "Cut the power" optional. Each
// objective is inactive, then active, then succeeded or failed, moved on only by triggers that have fired; the plugin's
// GoalBook plays them and shows the game's own messages. The goals live in state.goals (read and written in
// 22-scenario.js), the mission check judges them (34-check.js), and a sealed mission shows only the ones the game has
// given (sealedGoals, 20b-sealed.js). Here: the Mission tab's editor, the board in Direct and the list in Play.
// While a mission runs, state.goals are the running plan's ("goalPlan" in the game's state, read in 32-replay.js) and the
// game keeps the goals it started with, so they cannot be edited until it ends. Rehearsals check them in 29d-rehearse.js.
const goalUi = { authorKey: '', shape: '', boardKey: '', playKey: '' };
const GOAL_WORDS = { inactive: 'Not given yet', active: 'Active', succeeded: 'Complete', failed: 'Failed' };

// ---- authoring
const goalTitle = g => 'the objective "' + (g.name || g.id) + '"';
const goalTrigger = id => state.zones.some(z => z.id === id);
function goalsChanged() { changed(); syncGoalsAuthor(true); }
// A running mission keeps the objectives it started with: an edit then would only show what the game is not doing.
const goalLocked = () => !!(live.on && live.mission);
function goalHeld() { say('A mission is running and keeps the objectives it started with. Change them once it has ended.'); return false; }
// A new id: goal-1, goal-2, ... whichever is free.
function goalNewId() { let n = state.goals.length + 1; while (state.goals.some(g => g.id === 'goal-' + n)) n++; return 'goal-' + n; }
// Adds an objective completed by the first trigger: every objective needs one, so there must be a trigger first.
function goalAdd() {
  if (goalLocked()) return goalHeld() || '';
  if (!state.zones.length) { say('Add a trigger first: an objective is completed when one fires.'); return ''; }
  willChange('Add an objective');
  const id = goalNewId();
  state.goals.push({ id, name: 'Objective ' + (state.goals.length + 1), required: true, after: [], activate: 0, succeed: state.zones[0].id, fail: 0, notify: true });
  goalsChanged();
  return id;
}
// Deleting one also takes it out of every "after" list, in the same step, so nothing waits for a goal that is gone.
function goalDelete(i) {
  const g = state.goals[i]; if (!g) return false;
  if (goalLocked()) return goalHeld();
  willChange('Delete ' + goalTitle(g));
  state.goals.splice(i, 1);
  for (const q of state.goals) q.after = q.after.filter(a => a !== g.id);
  goalsChanged(); return true;
}
// The list's order is the order the board shows, and it breaks ties when goals are evaluated.
function goalMove(i, by) {
  const j = i + by, g = state.goals[i];
  if (!g || j < 0 || j >= state.goals.length) return false;
  if (goalLocked()) return goalHeld();
  willChange('Move ' + goalTitle(g) + (by < 0 ? ' up' : ' down'));
  state.goals.splice(i, 1); state.goals.splice(j, 0, g);
  goalsChanged(); return true;
}
// One field of objective i. A value the file rules would refuse is turned down with a reason, and nothing changes.
function goalSet(i, key, value) {
  const g = state.goals[i]; if (!g || !(key in g)) return false;
  if (goalLocked()) { goalHeld(); syncGoalsAuthor(true); return false; }
  const no = text => { say(text); syncGoalsAuthor(true); return false; };
  let v = value;
  if (key === 'name') { v = goalName(value); if (!v) return no('An objective needs a name: players see it.'); }
  else if (key === 'id') {
    v = String(value === undefined || value === null ? '' : value).trim().toLowerCase();
    if (!GOAL_ID.test(v)) return no('An objective id is 1 to 32 lower-case letters, digits or "-", starting with a letter or digit.');
    if (v !== g.id && state.goals.some(q => q.id === v)) return no('Another objective already has the id "' + v + '".');
  } else if (key === 'required' || key === 'notify') v = value === true || value === 'true';
  else if (key === 'after') v = (Array.isArray(value) ? value : []).filter((a, k, all) => a !== g.id && all.indexOf(a) === k && state.goals.some(q => q.id === a));
  else {
    v = Math.round(+value || 0);
    if (key === 'succeed' ? !goalTrigger(v) : v !== 0 && !goalTrigger(v)) return no('Choose one of the triggers.');
  }
  if (JSON.stringify(g[key]) === JSON.stringify(v)) return false;
  willChange(key === 'id' ? 'Rename the id of ' + goalTitle(g) : 'Change ' + goalTitle(g));
  if (key === 'id') for (const q of state.goals) q.after = q.after.map(a => (a === g.id ? v : a));
  g[key] = v;
  goalsChanged(); return true;
}
// Ticks one goal in or out of objective i's "after" list.
function goalSetAfter(i, id, on) {
  const g = state.goals[i]; if (!g) return false;
  return goalSet(i, 'after', on ? g.after.concat(id) : g.after.filter(a => a !== id));
}
function goalExtraction(on) {
  if (!!on === state.extractionGoals) return false;
  if (goalLocked()) { goalHeld(); syncGoalsAuthor(true); return false; }
  willChange(on ? 'Hold the extraction for the objectives' : 'Stop holding the extraction for the objectives');
  state.extractionGoals = !!on;
  goalsChanged(); return true;
}

// Rebuilt when the goals or the triggers they can name change (checked twice a second, so an undo or an opened file shows
// at once); otherwise values are set where they stand, so typing in a field keeps the focus.
function syncGoalsAuthor(force) {
  const list = $('goalList'); if (!list) return;
  const locked = goalLocked();
  const key = JSON.stringify([locked, state.goals, state.extractionGoals, state.gameMaster, state.zones.map(z => [z.id, z.name, z.when, z.after, z.of, isVec(z.pos), z.chance])]);
  if (!force && key === goalUi.authorKey) return;
  goalUi.authorKey = key;
  $('goalCount').textContent = state.goals.length ? String(state.goals.length) : '';
  $('goalAdd').disabled = locked || !state.zones.length; $('goalNoTrig').hidden = state.zones.length > 0; $('goalLive').hidden = !locked;
  $('goalExtract').disabled = locked;
  if (document.activeElement !== $('goalExtract')) $('goalExtract').checked = state.extractionGoals;
  const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  const trigWords = z => 'Trigger ' + z.id + (z.name ? ' "' + z.name + '"' : '') + ': ' + trigText(z);
  const shape = JSON.stringify([locked, state.goals.map(g => [g.id, g.name]), state.zones.map(z => [z.id, z.name, z.when])]);
  if (force || goalUi.shape !== shape) {
    goalUi.shape = shape; list.textContent = '';
    state.goals.forEach((g, i) => {
      const li = mk('li'), uid = 'goal' + i;
      const field = (label, k, input) => {
        const row = mk('div', { className: 'field' }); input.id = uid + k; input.dataset.k = k;
        input.addEventListener('change', ev => goalSet(i, k, ev.target.value));
        row.append(mk('label', { htmlFor: input.id, textContent: label }), input); return row;
      };
      const pick = (none) => {
        const sel = mk('select');
        if (none) sel.appendChild(mk('option', { value: '0', textContent: none }));
        for (const z of state.zones) sel.appendChild(mk('option', { value: String(z.id), textContent: trigWords(z) }));
        return sel;
      };
      const tick = (k, words) => {
        const box = mk('input', { type: 'checkbox' }), lab = mk('label', { className: 'check' });
        box.id = uid + k; box.dataset.k = k;
        box.addEventListener('change', ev => goalSet(i, k, ev.target.checked));
        lab.append(box, mk('span', { textContent: words })); return lab;
      };
      const after = mk('div', { className: 'after' });
      const others = state.goals.filter(q => q.id !== g.id);
      after.appendChild(mk('span', { className: 'sub', textContent: others.length ? 'Given once these are complete:' : 'Given at the start' }));
      for (const q of others) {
        const box = mk('input', { type: 'checkbox' }), lab = mk('label', { className: 'check' });
        box.dataset.after = q.id;
        box.addEventListener('change', ev => goalSetAfter(i, q.id, ev.target.checked));
        lab.append(box, mk('span', { textContent: q.name || q.id })); after.appendChild(lab);
      }
      const row = mk('div', { className: 'row' });
      const btn = (text, title, fn) => { const b = mk('button', { type: 'button', className: 'btn', textContent: text, title }); b.addEventListener('click', fn); return b; };
      const up = btn('Up', 'Earlier in the list', () => goalMove(i, -1)), down = btn('Down', 'Later in the list', () => goalMove(i, 1));
      up.disabled = i === 0; down.disabled = i === state.goals.length - 1;
      const del = btn('Delete objective', '', () => goalDelete(i)); del.className = 'btn danger';
      row.append(up, down, del);
      li.append(mk('b', { className: 'title' }),
        field('Name', 'name', mk('input', { type: 'text', maxLength: GOAL_NAME, title: 'What players see, at most ' + GOAL_NAME + ' characters.' })),
        field('Id', 'id', mk('input', { type: 'text', maxLength: 32, spellcheck: false, title: 'Lower-case letters, digits and "-"; unique. Other objectives, replays and the board name it by this.' })),
        field('Completed by', 'succeed', pick('')),
        field('Given also after', 'activate', pick('No trigger: as soon as the objectives before it are complete')),
        field('Fails by', 'fail', pick('Never fails')),
        after, tick('required', 'Required: the mission is not done without it'), tick('notify', 'Tell players with a message when it is given, completed or failed'), row);
      if (locked) for (const el of li.querySelectorAll('input, select, button')) el.disabled = true;
      list.appendChild(li);
    });
  }
  const items = list.children || [];
  state.goals.forEach((g, i) => {
    const li = items[i]; if (!li || !li.querySelector) return;
    for (const k of ['name', 'id']) { const el = li.querySelector('[data-k="' + k + '"]'); if (el) setVal(el, g[k]); }
    for (const k of ['succeed', 'activate', 'fail']) { const el = li.querySelector('[data-k="' + k + '"]'); if (el) setVal(el, String(g[k])); }
    for (const k of ['required', 'notify']) { const el = li.querySelector('[data-k="' + k + '"]'); if (el && document.activeElement !== el) el.checked = g[k] !== false; }
    for (const q of state.goals) { const el = li.querySelector('[data-after="' + q.id + '"]'); if (el && document.activeElement !== el) el.checked = g.after.includes(q.id); }
    const title = li.querySelector('.title'); if (title) title.textContent = (i + 1) + '. ' + g.name + (g.required ? '' : ' (optional)');
  });
  const probs = $('goalProblems'); probs.textContent = '';
  for (const f of missionCheck().filter(x => x.about && Array.isArray(x.about.goals))) probs.appendChild(mk('li', { className: f.level, textContent: f.text }));
}
$('goalAdd').addEventListener('click', goalAdd);
$('goalExtract').addEventListener('change', ev => goalExtraction(ev.target.checked));

// ---- the game's state: the board in Direct and the list in Play
// The game sends [{id, name, required, state}] under "goals" while a mission with objectives runs; an older plugin sends
// nothing. A state this page does not know is taken as not given, so it is never shown in a sealed mission.
function goalStatusList(s) {
  if (!s || !Array.isArray(s.goals)) return null;
  return s.goals.filter(g => g && typeof g === 'object' && typeof g.id === 'string')
    .map(g => ({ id: g.id, name: typeof g.name === 'string' && g.name ? g.name : g.id, required: g.required !== false, state: GOAL_WORDS[g.state] ? g.state : 'inactive' }));
}
// While a mission runs, the page's objectives are the running plan's: the game sends them as "goalPlan" (file format) with
// "extraction": {"goals": true} beside its states. None sent means the mission has none (or the plugin cannot run them).
function goalsFromGame(s) { goalsRead({ goals: s && s.goalPlan, extraction: s && s.extraction }); }
// The state the game sent last: the mission poll, or the status the ready list asks for.
function goalLiveStatus() {
  const s = catalogue.s;
  if (s && !Array.isArray(s.goals) && live.status && Array.isArray(live.status.goals)) return Object.assign({}, s, { goals: live.status.goals });
  return s;
}
const goalRow = g => ({ id: g.id, name: g.name, required: g.required, state: g.state, word: g.state ? GOAL_WORDS[g.state] : g.required ? 'Required' : 'Optional' });
// Direct: every objective and where it stands, and what the extraction still waits for. s: the game's state; hides: a
// sealed mission that is not revealed, which shows only the objectives already given.
function goalBoardModel(s, hides) {
  const hide = hides === undefined ? sealedHides() : !!hides;
  if (!s || !s.mission) return { rows: [], note: state.goals.length ? 'No mission is running. While one runs, each objective shows where it stands.' : '', wait: '', shown: state.goals.length > 0 };
  const list = goalStatusList(s);
  if (!list) return { rows: [], note: 'The game reports no objectives for this mission.' + (state.goals.length ? ' The scenario on this page has some: the game\'s MissionLab may be older than this page.' : ''), wait: '', shown: state.goals.length > 0 };
  const rows = sealedGoals(list, hide).map(goalRow), known = new Set(rows.map(r => r.id));
  // A sealed board names only what players were given; the extraction's wait then leaves the rest out too.
  const open = list.filter(g => g.required && g.state !== 'succeeded' && known.has(g.id)), lost = open.filter(g => g.state === 'failed');
  const names = l => l.map(g => '"' + g.name + '"').join(', ');
  const gate = state.extractionGoals || !!(s.extraction && typeof s.extraction === 'object' && s.extraction.goals === true);
  let wait = '';
  if (lost.length) wait = 'Failed: ' + names(lost) + (lost.length === 1 ? ' is required' : ' are required') + (gate ? ', so the objectives can no longer open the extraction.' : ', so the mission\'s objectives can no longer all be completed.');
  else if (open.length) wait = (gate ? 'The extraction waits for: ' : 'Required and not complete yet: ') + names(open) + (hide ? ', and any not given yet' : '') + '.';
  else if (list.some(g => g.required) && !hide) wait = 'Every required objective is complete.' + (gate ? ' The objectives no longer hold the extraction.' : '');
  // The game's own sentence, from the model that holds the extraction (GoalBook.Holding), whenever all may be named.
  const told = s.extraction && typeof s.extraction === 'object' && typeof s.extraction.waits === 'string' ? s.extraction.waits.trim() : '';
  if (told && !hide) wait = told.charAt(0).toUpperCase() + told.slice(1) + (/[.!?]$/.test(told) ? '' : '.');
  return { rows, note: hide ? 'This mission is sealed: only objectives the players have been given are shown.' : '', wait, shown: true };
}
// Play: what the players have been given. While a mission runs, the game's state (a sealed one: given objectives only);
// otherwise the scenario on this page, all of them, unless it is sealed.
function goalPlayModel(s, hides) {
  const hide = hides === undefined ? sealedHides() : !!hides;
  if (s && s.mission) {
    const list = goalStatusList(s);
    if (!list) return { rows: [], note: '', shown: false };
    const rows = sealedGoals(list, hide).map(goalRow);
    return { rows, note: hide ? 'Sealed: objectives appear here as the mission gives them.' : '', shown: true };
  }
  if (live.on && !catalogue.lobbyFile) return { rows: [], note: '', shown: false };     // the catalogue's entries, not the page
  if (!state.goals.length) return { rows: [], note: '', shown: false };
  if (hide) return { rows: [], note: 'This mission is sealed: its objectives appear here as the mission gives them.', shown: true };
  return { rows: state.goals.map(g => goalRow({ id: g.id, name: g.name, required: g.required, state: '' })), note: 'The scenario on this page. While the mission runs, each shows where it stands.', shown: true };
}
function goalListRender(ul, rows) {
  ul.textContent = '';
  const mk = (tag, props) => Object.assign(document.createElement(tag), props || {});
  for (const r of rows) {
    const li = mk('li', { className: (r.state ? 's-' + r.state : '') + (r.required ? '' : ' optional') });
    li.append(mk('span', { className: 'chip state', textContent: r.word }), mk('span', { className: 'name', textContent: r.name + (r.required || !r.state ? '' : ' (optional)') }));
    ul.appendChild(li);
  }
}
// s: the game's state (by default the last one it sent). Returns the model shown, for the tests.
function syncGoalBoard(s) {
  const m = goalBoardModel(s === undefined ? goalLiveStatus() : s), key = JSON.stringify(m);
  if (key === goalUi.boardKey) return m;
  goalUi.boardKey = key;
  $('goalBoard').hidden = !m.shown;
  $('goalBoardCount').textContent = m.rows.length ? String(m.rows.length) : '';
  $('goalBoardNote').textContent = m.note; $('goalBoardWait').textContent = m.wait;
  goalListRender($('goalBoardList'), m.rows);
  return m;
}
function syncGoalPlay(s) {
  const m = goalPlayModel(s === undefined ? goalLiveStatus() : s), key = JSON.stringify(m);
  if (key === goalUi.playKey) return m;
  goalUi.playKey = key;
  $('goalPlayBox').hidden = !m.shown;
  $('goalPlayCount').textContent = m.rows.length ? String(m.rows.length) : '';
  $('goalPlayNote').textContent = m.note;
  goalListRender($('goalPlayList'), m.rows);
  return m;
}
setInterval(() => { syncGoalsAuthor(false); if (viewIs('direct')) syncGoalBoard(); if (viewIs('play')) syncGoalPlay(); }, 700);
expose({ goalAdd, goalDelete, goalMove, goalSet, goalSetAfter, goalExtraction, goalsIn, goalsOut, goalName, goalStatusList, goalBoardModel, goalPlayModel, syncGoalsAuthor, syncGoalBoard, syncGoalPlay, sealedGoals, goalsFromGame });   // objectives (ML-046), for automated tests
