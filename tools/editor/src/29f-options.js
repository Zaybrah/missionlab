// ------------------------------------------------------------------ host options and the quick mission form (ML-022)
// Create, Mission tab: the author's options. Each has 2 to 6 choices, and a choice says plainly what it changes: enemies of
// a lot and triggers it drops, how many reinforcement waves, how good the enemies are. A choice's name is the author's
// word; the page never calls a choice easier or harder, as nobody has measured that. "Check every combination" asks the
// game's plugin (Core Options.Validate): every combination, every squad size, a sample of rolls. Offline only the editor's
// own mission check runs, on the default picks.
// Play: the options of the next mission as choices, sent to the game as {"picks"} (kept on the game's thread and used at
// the next mission's start), with what each choice changes and the picks recorded in the last run.
// Create, Map tab: the quick mission. The game's plugin runs the generator on the map's own navigation file
// (POST /api/generate); the page previews the layout on the map, Reroll asks for another seed, Keep opens it in Create
// as a new scenario. Offline there is no generator: the map file stays in the user's game folder.
const OPTION_ID = /^[A-Za-z0-9_-]{1,32}$/;
const OPTION_FEWEST = 2, OPTION_MOST = 6, OPTION_SEEDS = 20;
// check: the last "Check every combination" ({rows, text} or {busy}); play: the next mission's options read from its file;
// picks: what the game holds for the next mission; runs: run records read, by file name.
const opts = { check: null, play: null, reading: '', picks: null, picksFor: '', note: '', runs: new Map(), authorKey: '' };

const andWords = list => (list.length < 2 ? list.join('') : list.slice(0, -1).join(', ') + ' and ' + list[list.length - 1]);
const optionEl = (text, value) => Object.assign(document.createElement('option'), { textContent: text, value });
const newChoice = n => ({ name: 'Choice ' + n, lots: [], triggers: [], respawnTimes: -1, skill: '' });

// What a choice changes, in concrete words: "drops enemies of lot 3 and trigger 7; 1 reinforcement wave".
function optionChoiceWords(c) {
  const lots = (c.lots || []).slice().sort((a, b) => a - b), trigs = (c.triggers || []).slice().sort((a, b) => a - b), parts = [], drops = [];
  if (lots.length) drops.push('enemies of ' + (lots.length === 1 ? 'lot ' : 'lots ') + andWords(lots));
  if (trigs.length) drops.push((trigs.length === 1 ? 'trigger ' : 'triggers ') + andWords(trigs));
  if (drops.length) parts.push('drops ' + drops.join(' and '));
  if (c.respawnTimes === 0) parts.push('reinforcement waves without limit');
  else if (c.respawnTimes > 0) parts.push(c.respawnTimes + (c.respawnTimes === 1 ? ' reinforcement wave' : ' reinforcement waves'));
  if (c.skill === 'recruit' || c.skill === 'veteran') parts.push('every enemy a ' + c.skill);
  return parts.length ? parts.join('; ') : 'changes nothing';
}
// A choice as the host reads it: the author's name, then what it changes.
const optionLabel = (c, j) => (c.name || 'Choice ' + (j + 1)) + ' — ' + optionChoiceWords(c);
// The author's default, or the first choice when the default is out of range (as Core Options.Chosen).
const optionDefault = o => (o.pick >= 0 && o.pick < o.choices.length ? o.pick : 0);
const optionChosen = (o, picks) => { const p = picks && Object.prototype.hasOwnProperty.call(picks, o.id) ? picks[o.id] : -1; return Number.isInteger(p) && p >= 0 && p < o.choices.length ? p : optionDefault(o); };

// Options as a file holds them, with nothing taken from the page (a catalogue file, a run record).
function readOptions(list) {
  const whole = v => (Array.isArray(v) ? v.filter(n => Number.isInteger(n)) : []);
  return (Array.isArray(list) ? list : []).filter(o => o && typeof o.id === 'string' && Array.isArray(o.choices)).map(o => ({
    id: o.id, name: typeof o.name === 'string' ? o.name : '', pick: Number.isInteger(o.pick) ? o.pick : 0,
    choices: o.choices.filter(c => c && typeof c === 'object').map(c => { const d = c.drop && typeof c.drop === 'object' ? c.drop : {};
      return { name: typeof c.name === 'string' ? c.name : '', lots: whole(d.lots), triggers: whole(d.triggers), respawnTimes: Number.isInteger(c.respawnTimes) ? c.respawnTimes : -1, skill: c.skill === 'recruit' || c.skill === 'veteran' ? c.skill : '' }; }),
  })).filter(o => o.choices.length > 0);
}
// The picks the game is sent: one choice for every option of the scenario, the author's default where the host chose none.
function optionsPicksBody(options, picks) {
  const out = {};
  for (const o of options || []) if (OPTION_ID.test(o.id) && o.choices.length) out[o.id] = optionChosen(o, picks);
  return { picks: out };
}
const optionsPicksWords = (options, picks) => (options || []).map(o => (o.name || o.id) + ': ' + (o.choices[optionChosen(o, picks)].name || 'choice ' + (optionChosen(o, picks) + 1))).join('; ');

// ------------------------------------------------------------------ Create: the author's options
// Lots the file writes: every enemy with a lot, whatever its chance (the plugin drops a lot an option names either way).
const optionLots = () => [...new Set(state.enemies.filter(e => e.lot > 0).map(e => e.lot))].sort((a, b) => a - b);
// What the author should know before saving: the file rules, and choices that take out what the mission needs.
function optionWarnings(o, i) {
  const out = [], name = o.name || o.id || 'Option ' + (i + 1);
  if (!OPTION_ID.test(o.id)) out.push(name + ': the id is 1 to 32 letters, digits, _ or -; the game refuses the file otherwise.');
  else if (state.options.some((p, k) => k !== i && p.id === o.id)) out.push(name + ': another option has the id "' + o.id + '"; the game refuses the file.');
  if (!o.name.trim()) out.push('Option ' + (i + 1) + ' has no name; the game refuses the file.');
  if (o.choices.length < OPTION_FEWEST || o.choices.length > OPTION_MOST) out.push(name + ' needs 2 to 6 choices.');
  const targets = state.enemies.filter(e => e.hvt && !e.trigger);
  o.choices.forEach((c, j) => {
    const cn = name + ', ' + (c.name || 'choice ' + (j + 1));
    if (!c.name.trim()) out.push(name + ': choice ' + (j + 1) + ' has no name; the game refuses the file.');
    for (const lot of c.lots) {
      const members = state.enemies.filter(e => e.lot === lot);
      if (!members.length) out.push(cn + ' drops lot ' + lot + ', which no enemy has: it changes nothing.');
      if (targets.length && members.some(e => e.hvt && !e.trigger) && targets.every(e => e.lot > 0 && c.lots.includes(e.lot)))
        out.push(cn + ' drops lot ' + lot + ', which holds the target person: with this choice the game refuses the mission and plays its own.');
    }
    for (const id of c.triggers) {
      const z = state.zones.find(q => q.id === id);
      if (z && (state.extractionAfter === id || z.opensExtraction)) out.push(cn + ' switches off ' + trigTitle(z).toLowerCase() + ', which the extraction depends on. Check every combination before playing it.');
    }
    if (c.respawnTimes >= 0 && !(state.respawnAfter > 0)) out.push(cn + ' sets reinforcement waves, but the scenario has no wave rule (Wave after, above): it changes nothing.');
  });
  return out;
}
function syncOptionsAuthor() {
  const list = $('optList'); if (!list) return;
  $('optCount').textContent = state.options.length || '';
  const lots = optionLots(), key = JSON.stringify([state.options, lots, state.zones.map(z => [z.id, z.name, z.when]), state.respawnAfter, state.extractionAfter, state.enemies.map(e => [e.lot, e.chance, e.hvt, e.trigger])]);
  if (key !== opts.authorKey) {
    opts.authorKey = key;
    const active = document.activeElement, again = active && list.contains(active) && active.dataset ? active.dataset.k : '';
    list.textContent = '';
    state.options.forEach((o, i) => list.appendChild(optionItem(o, i, lots)));
    if (again) { const el = list.querySelector('[data-k="' + again + '"]'); if (el && el.focus) el.focus(); }
  }
  const c = opts.check, box = $('optCheckList');
  box.textContent = '';
  if (c && c.rows) for (const r of c.rows) { const li = document.createElement('li'); li.className = r.level; li.textContent = r.text; box.appendChild(li); }
  $('optCheckNote').textContent = !state.options.length ? 'Add an option first.' : c && c.busy ? 'The game is trying every combination...'
    : c && c.rows && c.text !== scenarioText() ? 'The scenario changed since this check: check again.' : '';
}
// One option's editor. Every change is one undo step.
function optionItem(o, i, lots) {
  const li = document.createElement('li'), edit = fn => { willChange('Change option "' + o.name + '"'); fn(); changed(); };
  const input = (k, type, value, label, set, more) => { const el = Object.assign(document.createElement('input'), { type, value }, more || {}); el.dataset.k = k; el.setAttribute('aria-label', label); el.addEventListener('change', () => edit(() => set(el))); return el; };
  const head = document.createElement('div'); head.className = 'row';
  head.append(input(i + 'n', 'text', o.name, 'Option name', el => { o.name = el.value.slice(0, 80); }, { placeholder: 'Garrison size', maxLength: 80 }),
    input(i + 'i', 'text', o.id, 'Option id', el => { o.id = el.value.trim(); }, { placeholder: 'garrison', maxLength: 32, title: 'Stays the same when the name changes; run records name the option by it.' }));
  const del = Object.assign(document.createElement('button'), { type: 'button', className: 'btn', textContent: 'Remove' });
  del.addEventListener('click', () => edit(() => { state.options.splice(i, 1); }));
  head.appendChild(del); li.appendChild(head);
  const dflt = document.createElement('select'); dflt.dataset.k = i + 'd'; dflt.setAttribute('aria-label', 'Default choice of ' + (o.name || o.id));
  o.choices.forEach((c, j) => dflt.append(optionEl('Default: ' + (c.name || 'choice ' + (j + 1)), String(j))));
  dflt.value = String(optionDefault(o)); dflt.addEventListener('change', () => edit(() => { o.pick = +dflt.value; }));
  li.appendChild(dflt);
  o.choices.forEach((c, j) => {
    const box = document.createElement('div'); box.className = 'optChoice';
    const row = document.createElement('div'); row.className = 'row';
    row.append(input(i + 'c' + j, 'text', c.name, 'Choice ' + (j + 1) + ' name', el => { c.name = el.value.slice(0, 80); }, { maxLength: 80 }),
      input(i + 'w' + j, 'number', c.respawnTimes >= 0 ? String(c.respawnTimes) : '', 'Reinforcement waves with choice ' + (j + 1), el => { const v = el.value.trim(); c.respawnTimes = v === '' ? -1 : Math.max(0, Math.min(50, parseInt(v, 10) || 0)); },
        { min: 0, max: 50, step: 1, placeholder: 'waves as set', title: 'Reinforcement waves with this choice (0: no limit). Empty: as the Mission tab says.' }));
    const skill = document.createElement('select'); skill.dataset.k = i + 's' + j; skill.setAttribute('aria-label', 'Enemy skill with choice ' + (j + 1));
    [['', 'skill as set'], ['recruit', 'every enemy a recruit'], ['veteran', 'every enemy a veteran']].forEach(([v, t]) => skill.append(optionEl(t, v)));
    skill.value = c.skill; skill.addEventListener('change', () => edit(() => { c.skill = skill.value; }));
    row.appendChild(skill);
    if (o.choices.length > OPTION_FEWEST) { const x = Object.assign(document.createElement('button'), { type: 'button', className: 'btn', textContent: '×', title: 'Remove this choice' }); x.addEventListener('click', () => edit(() => { o.choices.splice(j, 1); if (o.pick >= o.choices.length) o.pick = 0; })); row.appendChild(x); }
    box.appendChild(row);
    const ticks = (word, values, has, toggle, none) => {
      const r = document.createElement('div'); r.className = 'row optDrops'; const t = document.createElement('span'); t.className = 'hint'; t.textContent = word; r.appendChild(t);
      if (!values.length) { const n = document.createElement('span'); n.className = 'hint'; n.textContent = none; r.appendChild(n); }
      for (const [v, text] of values) {
        const l = document.createElement('label'), b = document.createElement('input'), s = document.createElement('span');
        l.className = 'check'; b.type = 'checkbox'; b.checked = has(v); b.dataset.k = i + word + j + '-' + v; s.textContent = text;
        b.addEventListener('change', () => edit(() => toggle(v, b.checked))); l.append(b, s); r.appendChild(l);
      }
      return r;
    };
    const flip = (list, v, on) => { const k = list.indexOf(v); if (on && k < 0) { list.push(v); list.sort((a, b) => a - b); } if (!on && k >= 0) list.splice(k, 1); };
    box.appendChild(ticks('Drop lots', lots.map(l => [l, 'lot ' + l]), v => c.lots.includes(v), (v, on) => flip(c.lots, v, on), 'no enemy has a lot yet (Enemies tab: a squad left to chance shares one)'));
    box.appendChild(ticks('Drop triggers', state.zones.map(z => [z.id, trigTitle(z) + (z.name ? ' ' + z.name : '')]), v => c.triggers.includes(v), (v, on) => flip(c.triggers, v, on), 'no triggers yet'));
    const words = document.createElement('p'); words.className = 'hint'; words.textContent = optionLabel(c, j) + (j === optionDefault(o) ? ' (the default)' : '');
    box.appendChild(words); li.appendChild(box);
  });
  if (o.choices.length < OPTION_MOST) { const add = Object.assign(document.createElement('button'), { type: 'button', className: 'btn', textContent: 'Add a choice' }); add.addEventListener('click', () => edit(() => { o.choices.push(newChoice(o.choices.length + 1)); })); li.appendChild(add); }
  const warn = optionWarnings(o, i);
  if (warn.length) { const ul = document.createElement('ul'); ul.className = 'optWarn'; for (const w of warn) { const x = document.createElement('li'); x.textContent = w; ul.appendChild(x); } li.appendChild(ul); }
  return li;
}
function optionAdd() {
  let n = state.options.length + 1; while (state.options.some(o => o.id === 'option' + n)) n++;
  willChange('Add option ' + n); state.options.push({ id: 'option' + n, name: 'Option ' + n, pick: 0, choices: [newChoice(1), newChoice(2)] }); changed();
}

// "Check every combination": what is sent, and the game's answer as rows. Unfinishable combinations come first with the
// squad size and roll that reproduce them; the game's notes (what was tried, sampled coverage) are shown word for word.
const optionsValidateBody = () => ({ scenario: scenarioText(), seeds: OPTION_SEEDS });
function optionsValidateRows(d, status) {
  if (!d || typeof d !== 'object' || d.error || !Array.isArray(d.findings)) return [{ level: 'error', text: 'Not checked: ' + ((d && d.error) || 'the game answered with status ' + status) + '.' }];
  const rows = [], found = d.findings.filter(f => f && typeof f.text === 'string');
  if (d.readable === false) {
    rows.push({ level: 'error', text: 'The game\'s reader refuses this scenario, so its options could not be tried. The Mission check lists what to fix.' });
    for (const f of found) if (f.level === 'error') rows.push({ level: 'error', text: (f.path ? f.path + ': ' : '') + f.text });
    return rows;
  }
  const bad = found.filter(f => f.level === 'error');
  for (const f of bad) rows.push({ level: 'error', text: 'Unfinishable: ' + f.text + (f.path ? ' (' + f.path + ')' : '') });
  if (!bad.length) rows.push({ level: 'ok', text: 'No combination tried made the mission unfinishable.' });
  for (const f of found) if (f.level !== 'error') rows.push({ level: 'note', text: f.text });
  return rows;
}
// The default picks applied to the page for a moment, as the game would: enemies of a dropped lot and the enemies and
// things of a dropped trigger are gone, the trigger with them; waves and skill as the choices say. Put back afterwards.
function optionsWithDefaults(fn) {
  const keep = { enemies: state.enemies, zones: state.zones, things: state.things, respawnTimes: state.respawnTimes, allSkill: state.allSkill, selected: state.selected, squad: state.squad };
  const lots = new Set(), trigs = new Set();
  let times = null, skill = '';
  for (const o of state.options) {
    if (!o.choices.length) continue;
    const c = o.choices[optionDefault(o)];
    c.lots.forEach(l => lots.add(l)); c.triggers.forEach(t => trigs.add(t));
    if (c.respawnTimes >= 0) times = c.respawnTimes;
    if (c.skill) skill = c.skill;
  }
  try {
    state.enemies = state.enemies.filter(e => !(e.lot > 0 && lots.has(e.lot)) && !(e.trigger && trigs.has(e.trigger)));
    state.zones = state.zones.filter(z => !trigs.has(z.id));
    state.things = state.things.filter(t => !(t.trigger && trigs.has(t.trigger)));
    if (times !== null) state.respawnTimes = times;
    if (skill) state.allSkill = skill;
    state.selected = -1; state.squad = [];
    return fn(keep);
  } finally { Object.assign(state, keep); }
}
function optionsOfflineRows() {
  const defaults = optionsPicksWords(state.options, {});
  const rows = [{ level: 'note', text: 'The full check of every combination needs the game\'s plugin: start OPERATOR with MissionLab and open the editor at http://localhost:8765. Here the editor\'s own mission check runs on the default picks only (' + defaults + ').' }];
  optionsWithDefaults(was => {
    const target = was.enemies.some(e => e.hvt && !e.trigger), still = state.enemies.some(e => e.hvt && !e.trigger);
    if (target && !still) rows.push({ level: 'error', text: 'The default picks leave out the target person: the game refuses the mission and plays its own.' });
    for (const f of missionCheck()) if (f.level !== 'note') rows.push({ level: f.level === 'problem' ? 'error' : 'warning', text: f.text });
  });
  if (rows.length === 1) rows.push({ level: 'ok', text: 'The mission check finds nothing to fix with the default picks. Other choices are not checked here.' });
  return rows;
}
async function optionsCheck() {
  if (!state.options.length) { syncOptionsAuthor(); return; }
  const text = scenarioText();
  if (!live.on) { opts.check = { rows: optionsOfflineRows(), text }; syncOptionsAuthor(); return; }
  opts.check = { busy: true }; syncOptionsAuthor();
  let rows;
  try {
    const r = await fetch('/api/options/validate', { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify(optionsValidateBody()) });
    let d = null; try { d = await r.json(); } catch (e) { /* no body */ }
    rows = r.status === 404 && !d ? [{ level: 'error', text: 'The game runs an older MissionLab that cannot check options. Update the plugin.' }] : optionsValidateRows(d, r.status);
  } catch (e) { rows = [{ level: 'error', text: 'The game did not answer; nothing was checked.' }]; }
  opts.check = { rows, text }; syncOptionsAuthor();
}
$('optAdd').addEventListener('click', optionAdd);
$('optCheck').addEventListener('click', optionsCheck);

// ------------------------------------------------------------------ Play: the next mission's options
// The entry the picks are for: the next mission in the game; offline, the scenario on the page.
function optionsEntry() {
  if (!live.on) return pageEntry();
  const s = catalogue.s;
  return s && typeof s.choice === 'string' ? entryByValue(s.choice) : null;
}
// A run record's picks in words. Names come from the plan the record kept, else from the scenario as it is now.
function optionsRunWords(rec, options) {
  if (!rec || typeof rec !== 'object') return '';
  const picks = rec.picks && typeof rec.picks === 'object' ? rec.picks : null;
  if (!picks || !Object.keys(picks).length) return 'no options recorded (the scenario had none then, or it was played before MissionLab 0.26)';
  const kept = readOptions((planBody(rec.resolved) || {}).options), list = kept.length ? kept : options || [];
  return Object.keys(picks).sort().map(id => {
    const o = list.find(q => q.id === id), at = picks[id];
    return o && Number.isInteger(at) && o.choices[at] ? (o.name || id) + ': ' + (o.choices[at].name || 'choice ' + (at + 1)) : id + ': choice ' + ((at | 0) + 1);
  }).join('; ');
}
async function optionsReadRun(file) {
  if (!file || opts.runs.has(file)) return;
  opts.runs.set(file, null);
  try { const r = await fetch('/api/runfile/' + encodeURIComponent(file), { cache: 'no-store' }); opts.runs.set(file, r.ok ? await r.json() : { error: true }); }
  catch (e) { opts.runs.set(file, { error: true }); }
  syncOptionsPlay();
}
async function optionsReadPlay(e) {
  const key = e.file + '|' + (e.hash || '');
  if (opts.reading === key) return;
  opts.reading = key;
  let play;
  try {
    const r = await fetch('/api/scenariofile/' + encodeURIComponent(e.file), { cache: 'no-store' });
    if (!r.ok) throw new Error('status ' + r.status);
    play = { key, options: readOptions((planBody(await r.json()) || {}).options), error: '' };
  } catch (err) { play = { key, options: [], error: 'The game did not hand over ' + e.file + ', so its options cannot be shown.' }; }
  opts.reading = ''; opts.play = play; syncOptionsPlay();
}
async function optionsReadPicks() {
  try { const d = await (await fetch('/api/options', { cache: 'no-store' })).json(); opts.picks = d && d.picks && typeof d.picks === 'object' ? d.picks : {}; }
  catch (e) { opts.picks = null; }
  syncOptionsPlay();
}
async function optionsPick(options, picks) {
  const body = optionsPicksBody(options, picks);
  opts.note = 'Sending the picks to the game...'; syncOptionsPlay();
  try {
    const r = await fetch('/api/options', { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify(body) });
    const d = await r.json();
    if (r.status !== 200 || !d || typeof d.picks !== 'object') throw new Error(d && d.error ? d.error : 'status ' + r.status);
    opts.picks = d.picks;
    opts.note = 'The game holds these picks for the next mission it starts: ' + optionsPicksWords(options, d.picks) + '.';
  } catch (err) { opts.note = 'The game did not take the picks: ' + (err && err.message ? err.message : err) + '. The next mission uses what the game held before.'; }
  syncOptionsPlay();
}
function syncOptionsPlay() {
  const box = $('optPlayBox'); if (!box) return;
  if (!viewIs('play')) return;
  const e = optionsEntry();
  let options = [], why = '';
  if (!live.on) options = e ? state.options : [];
  else if (e && Array.isArray(e.options) && e.options.length) {
    const key = e.file + '|' + (e.hash || '');
    if (!opts.play || opts.play.key !== key) { optionsReadPlay(e); why = 'Reading the options of ' + (e.title || e.value) + '...'; }
    else { options = opts.play.options; why = opts.play.error; }
    if (opts.picksFor !== key) { opts.picksFor = key; opts.note = ''; optionsReadPicks(); }      // asked again whenever the next mission changes
  }
  const s = catalogue.s, repeat = live.on && s && typeof s.repeat === 'string' ? s.repeat : '';
  const last = live.on && e && e.lastRun && e.lastRun.file ? e.lastRun.file : '';
  if (last) optionsReadRun(last);
  if (repeat) optionsReadRun(repeat);
  box.hidden = !(e && (options.length || why || last));
  if (box.hidden) return;
  $('optPlayFor').textContent = !live.on ? 'The scenario on this page. Picking needs the game: here the choices only show what each changes.'
    : 'For the next mission: ' + (e.title || e.value) + '.';
  const list = $('optPlayList'), picks = live.on ? opts.picks || {} : {};
  const key = JSON.stringify([options, picks, live.on]);
  if (list.dataset.key !== key && !(document.activeElement && list.contains(document.activeElement) && document.activeElement.tagName === 'SELECT')) {
    list.dataset.key = key; list.textContent = '';
    options.forEach(o => {
      const f = document.createElement('div'), l = document.createElement('label'), sel = document.createElement('select');
      f.className = 'field'; l.textContent = o.name || o.id; sel.id = 'optPick-' + o.id; l.htmlFor = sel.id;
      o.choices.forEach((c, j) => sel.append(optionEl(optionLabel(c, j) + (j === optionDefault(o) ? ' (the author\'s default)' : ''), String(j))));
      sel.value = String(optionChosen(o, picks)); sel.disabled = !live.on;
      sel.addEventListener('change', () => optionsPick(options, Object.assign({}, opts.picks || {}, { [o.id]: +sel.value })));
      f.append(l, sel); list.appendChild(f);
    });
  }
  $('optPlayNote').textContent = why || opts.note || (live.on && opts.picks === null && options.length ? 'The game did not say which picks it holds; the author\'s defaults are shown.' : '');
  const lines = [], rec = last ? opts.runs.get(last) : null, again = repeat ? opts.runs.get(repeat) : null;
  if (rec && !rec.error) lines.push('Its last run was played with: ' + optionsRunWords(rec, options) + '.');
  if (again && !again.error) lines.push('Dice repeats an earlier run, so the next mission uses that run\'s recorded options, whatever is picked above: ' + optionsRunWords(again, options) + '.');
  $('optPlayRun').textContent = lines.join(' ');
}
// Play's own refresh brings the options along (20a-modes.js calls syncPlay on every state and catalogue change).
{ const syncPlayBase = syncPlay; syncPlay = function () { syncPlayBase(); syncOptionsPlay(); }; }

// ------------------------------------------------------------------ the quick mission
// result: the game's last answer ({navFile, seed, placed, roles, later, doorsLocked, notes, leftOut, plan}) while previewed.
const quick = { result: null, busy: false, note: '' };
const QUICK_SEED_MAX = 2147483647;
const quickSeed = () => 1 + Math.floor(Math.random() * (QUICK_SEED_MAX - 1));
function quickForm() {
  const n = (id, d) => { const v = parseFloat($(id).value); return isFinite(v) ? v : d; };
  return { navFile: String($('qmMap').value || '').trim(), enemies: Math.round(n('qmEnemies', 12)), guard: n('qmGuard', 0), wander: n('qmWander', 0), patrol: n('qmPatrol', 0),
    alert: !!$('qmAlert').checked, ambush: !!$('qmAmbush').checked, doors: !!$('qmDoors').checked, seed: Math.round(n('qmSeed', 0)) };
}
// The body of POST /api/generate. What the page knows of that map goes along: insertion points (enemies keep clear of
// them), the game's own spawn points (the play area) and its doors (for locked doors), only when they belong to that map.
function quickRequest(form) {
  const share = v => Math.max(0, Math.min(1000, +v || 0));
  const body = { navFile: form.navFile, enemies: Math.max(2, Math.min(60, form.enemies | 0)), mix: { guard: share(form.guard), wander: share(form.wander), patrol: share(form.patrol) },
    extras: [...(form.alert ? ['alert'] : []), ...(form.ambush ? ['ambush'] : []), ...(form.doors ? ['doors'] : [])], seed: form.seed | 0 };
  if (state.navFile && form.navFile && state.navFile.toLowerCase() === form.navFile.toLowerCase()) {
    const pts = list => list.filter(isVec).slice(0, 500).map(p => [r2(p[0]), r2(p[1]), r2(p[2])]);
    if (state.infils.length) body.infils = pts(state.infils);
    if (state.authored.length) body.authored = pts(state.authored);
    const known = state.doors.filter(d => d.known && isVec(d.pos));
    if (form.doors && known.length) body.doors = known.slice(0, 500).map(d => ({ name: d.name || '', pos: [r2(d.pos[0]), r2(d.pos[1]), r2(d.pos[2])], locked: d.isLocked === true }));
  }
  return body;
}
// The answer in a sentence: what was placed, by role, and what comes later.
function quickWords(d) {
  const r = d.roles || {}, plural = (n, one, many) => n + ' ' + (n === 1 ? one : many);
  return 'Seed ' + d.seed + ' on ' + d.navFile + ': ' + plural(d.placed | 0, 'enemy', 'enemies') + ' placed' + ((d.placed | 0) < (d.enemies | 0) ? ' of ' + d.enemies + ' asked for' : '')
    + ' (' + [plural(r.guard | 0, 'guard', 'guards'), plural(r.wander | 0, 'wanderer', 'wanderers'), plural(r.patrol | 0, 'patrol', 'patrols')].join(', ') + ')'
    + ((d.later | 0) ? ', ' + (d.later | 0) + ' of them held back for the alert wave or the ambush' : '') + ((d.doorsLocked | 0) ? ', ' + plural(d.doorsLocked | 0, 'door', 'doors') + ' locked' : '') + '.';
}
async function quickGenerate() {
  if (!live.on) { quick.note = 'The quick mission needs the game: its generator reads the map file in your OPERATOR folder.'; syncQuick(); return; }
  const form = quickForm();
  if (!/^[A-Za-z0-9_.-]{1,120}\.bytes$/i.test(form.navFile) || form.navFile.includes('..')) { quick.note = 'Name a map file of the game, such as suburbv2.bytes.'; syncQuick(); return; }
  if (live.mission && live.nav && live.nav.toLowerCase() !== form.navFile.toLowerCase()) { quick.note = 'A mission is running on ' + live.nav + ' and the map shows it. Generate for ' + live.nav + ', or after the mission.'; syncQuick(); return; }
  if (!form.seed) { form.seed = quickSeed(); $('qmSeed').value = String(form.seed); }
  quick.busy = true; quick.note = 'The game is making a layout...'; syncQuick();
  let r = null, d = null;
  try {
    r = await fetch('/api/generate', { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify(quickRequest(form)) });
    try { d = await r.json(); } catch (e) { /* no body */ }
  } catch (e) { r = null; }
  quick.busy = false;
  if (!r) { quick.note = 'The game did not answer. Is it still running?'; syncQuick(); return; }
  if (r.status !== 200 || !d || !d.plan || !Array.isArray(d.plan.spawns)) {
    quick.note = r.status === 404 && !(d && d.error) ? 'The game runs an older MissionLab without the quick mission. Update the plugin.' : 'No layout: ' + ((d && d.error) || 'the game answered with status ' + r.status) + '.';
    syncQuick(); return;
  }
  if (!state.map || state.map.name.toLowerCase() !== String(d.navFile).toLowerCase()) {
    try {
      const m = await fetch('/api/mapfile/' + encodeURIComponent(d.navFile), { cache: 'no-store' });
      if (m.ok) { const buf = await m.arrayBuffer(); state.map = makeMap(await readMapFile(buf), d.navFile, false); state.map.print = mapPrintOf(buf); state.ceiling = Infinity; view3d.dropMap(); fit(); }
    } catch (e) { /* the layout is shown without its ground */ }
  }
  quick.result = d; quick.note = quickWords(d); syncSide(); invalidate(); syncQuick();
}
// Keep: the layout becomes the page's scenario, new and unsaved, with an identity of its own. Unsaved work that was on the
// page is kept under Revisions as a separate draft (historyReplaced, 27-editing.js).
function quickKeep(d) {
  if (!d || !d.plan) return { kept: false, text: 'Generate a layout first.' };
  if (live.on && live.mission) return { kept: false, text: 'A mission is running and the page shows it. Keep the layout after the mission.' };
  const said = loadScenario(d.plan, 'quick_mission_' + d.seed + '.json');
  if (state.refused) return { kept: false, text: said };
  state.scenarioId = newScenarioId(); state.revision = 0;
  if (live.on) catalogue.lobbyFile = fileName();                  // between missions the page keeps it until saved
  quick.result = null;
  setView('create', true); showTab('enemies', true); changed();
  return { kept: true, text: said + ' It is a new scenario (id ' + state.scenarioId + '), not saved yet: Save keeps it.' };
}
function syncQuick() {
  if (!$('quickBox')) return;
  $('qmMap').placeholder = live.nav || state.navFile || 'suburbv2.bytes';
  const maps = new Set([live.nav, state.navFile, ...((catalogue.list || []).map(e => e.navFile))].filter(n => typeof n === 'string' && /\.bytes$/i.test(n)));
  const dl = $('qmMaps'), mk = [...maps].sort().join('|');
  if (dl.dataset.key !== mk) { dl.dataset.key = mk; dl.textContent = ''; for (const n of maps) dl.appendChild(Object.assign(document.createElement('option'), { value: n })); }
  const can = live.on && !quick.busy;
  $('qmGo').disabled = !can; $('qmReroll').disabled = !can;
  $('qmKeep').disabled = !quick.result || quick.busy || !!(live.on && live.mission);
  $('qmKeep').title = live.on && live.mission ? 'A mission is running and the page shows it. Keep the layout after the mission.' : 'Open this layout in Create as a new scenario';
  $('qmDrop').hidden = !quick.result;
  $('qmNote').textContent = quick.note;
  const ul = $('qmLeft'); ul.textContent = '';
  for (const t of (quick.result && quick.result.leftOut) || []) { const li = document.createElement('li'); li.textContent = 'Not in this layout, ' + t + '.'; ul.appendChild(li); }
}
// The preview, drawn over the map: dashed markers that are not part of the scenario until kept (24-drawing.js calls this).
function drawQuick() {
  const d = quick.result, body = d && planBody(d.plan);
  if (!body || !Array.isArray(body.spawns) || !state.map || state.map.name.toLowerCase() !== String(d.navFile).toLowerCase()) return;
  const roles = ['Guard', 'Wander', 'Patrol'];
  ctx.save(); ctx.setLineDash([4, 3]); ctx.lineWidth = 1.6; ctx.strokeStyle = css.warn;
  for (const s of body.spawns) {
    if (!isVec(s.pos)) continue;
    if (Array.isArray(s.patrol) && s.patrol.length > 1) {
      ctx.globalAlpha = 0.6; ctx.beginPath();
      s.patrol.filter(isVec).forEach((p, k) => { const [x, y] = toScreen(p[0], p[2]); if (k) ctx.lineTo(x, y); else ctx.moveTo(x, y); });
      ctx.stroke();
    }
    const [x, y] = toScreen(s.pos[0], s.pos[2]);
    ctx.globalAlpha = s.trigger ? 0.45 : 0.9;
    shape(roles[s.role] || s.role || 'Guard', x, y, 6.5); ctx.fillStyle = css.panel; ctx.fill(); ctx.stroke();
  }
  for (const t of Array.isArray(body.triggers) ? body.triggers : []) {
    if (t.when !== 'zone' || !isVec(t.pos)) continue;
    const [x, y] = toScreen(t.pos[0], t.pos[2]);
    ctx.globalAlpha = 0.8; ctx.beginPath(); ctx.arc(x, y, (t.radius || 7) * state.view.scale, 0, Math.PI * 2); ctx.stroke();
  }
  ctx.restore();
}
$('qmGo').addEventListener('click', quickGenerate);
$('qmReroll').addEventListener('click', () => { $('qmSeed').value = String(quickSeed()); quickGenerate(); });
$('qmKeep').addEventListener('click', () => { const k = quickKeep(quick.result); quick.note = k.text; syncQuick(); say(k.text); });
$('qmDrop').addEventListener('click', () => { quick.result = null; quick.note = ''; syncQuick(); invalidate(); });
// Between missions the page clears the map; a layout being previewed keeps it (32-replay.js clearLive).
{ const clearLiveBase = clearLive; clearLive = function () { if (quick.result && !live.mission) return; clearLiveBase(); }; }
