
// ------------------------------------------------------------------ scenario packs (ML-026)
// A pack is one file, missionlab-pack/1, to hand a mission to other players: the scenario, optionally encounter modules,
// and a manifest listing every file and what it needs (map files, PvE, the MissionLab version, the features it uses).
// Each file travels as its exact text (a JSON string), so the manifest's hash is over that text and an installed file is
// the author's byte for byte. Never navigation data, map geometry, replays or game files: each player's own game supplies
// the map. Core's Pack.cs has the same rules; tests/fixtures/packs holds packs both must judge alike.
// Import shows everything before anything is written: each file with what the strict reader says, features this version
// does not have, whether the positions were made on the same map file as the one open here, and names or ids that are
// already in the scenarios folder (copy, new revision or skip). The game installs; it never writes over a file.
const PACK_SCHEMA = 'missionlab-pack/1', PACK_SUFFIX = '.mlpack.json', PACK_MODULE = '.module.json', PACK_PVE = 'pve', PACK_MAX_FILES = 64;
const PACK_KNOWN = ['areas', 'chance', 'director', 'door-trigger', 'doors', 'endings', 'extraction-hold', 'goals', 'hunt', 'hvt', 'insertion', 'intel-move', 'messages',
  'options', 'outcomes', 'places', 'power', 'pressure', 'rally', 'repeat', 'respawn', 'scatter', 'sealed', 'teams', 'things', 'vehicles', 'weather', 'zone-after'];
const PACK_FORBIDDEN = ['.bytes', '.binary', '.mlgeo', '.dll', '.zip', '.assets', '.bundle', '.exe'];
const PACK_FORBIDDEN_SCHEMAS = ['missionlab-replay/', 'missionlab-recon/', 'missionlab-movement/'];
const packs = { extra: [], include: new Set(), text: '', fileName: '', preview: null, choices: {}, key: '', busy: false, lastExport: null };

// CatalogueInfo.SafeName: letters, digits, '_' and '-', at most 60, ".json" added; '' when nothing is left.
function packSafeName(name) {
  let n = String(name == null ? '' : name).trim(), out = '';
  if (/\.json$/i.test(n)) n = n.slice(0, -5);
  for (const c of n) {
    if (/^[A-Za-z0-9_-]$/.test(c)) out += c;
    else if (out.length && out[out.length - 1] !== '_') out += '_';
  }
  const trim = s => s.replace(/^[_-]+|[_-]+$/g, '');
  let s = trim(out);
  if (s.length > 60) s = trim(s.slice(0, 60));
  if (!s) return '';
  const up = s.toUpperCase();
  if (['CON', 'PRN', 'AUX', 'NUL'].includes(up) || /^(COM|LPT)\d$/.test(up)) s += '_';
  return s + '.json';
}
// The name a pack file must have (Pack.FileName): a scenario's as the catalogue saves it, a module's as the modules folder keeps it.
function packFileName(name, kind) {
  if (kind !== 'module') return packSafeName(name);
  let n = String(name == null ? '' : name).trim();
  if (/\.json$/i.test(n)) n = n.slice(0, -5);
  if (/\.module$/i.test(n)) n = n.slice(0, -7);
  const safe = packSafeName(n);
  return safe ? safe.slice(0, -5) + PACK_MODULE : '';
}
const packParts = v => { const p = String(v == null ? '' : v).split('.'); return p.every(x => /^\d+$/.test(x)) ? p.map(Number) : null; };
function packNewer(a, b) {
  const x = packParts(a), y = packParts(b);
  if (!x || !y) return false;
  for (let i = 0; i < Math.max(x.length, y.length); i++) { const p = x[i] || 0, q = y[i] || 0; if (p !== q) return p > q; }
  return false;
}
const packForbiddenName = name => PACK_FORBIDDEN.some(end => String(name || '').toLowerCase().endsWith(end));
function packForbiddenText(text) {
  const t = String(text || '').trimStart();
  if (t.startsWith('PK') || t.startsWith('MLGEO')) return true;
  try { const o = JSON.parse(t); return !!(o && typeof o === 'object' && !Array.isArray(o) && typeof o.schema === 'string' && PACK_FORBIDDEN_SCHEMAS.some(f => o.schema.startsWith(f))); }
  catch (e) { return false; }
}
const packBytes = text => new TextEncoder().encode(String(text)).length;
const packBase = p => String(p || '').trim().split(/[\\/]/).pop();

// Reads a pack as Pack.Read does: findings with level, code, path and a sentence; pack (null when there is an error) and,
// for the preview, every listed file with its own findings and what it is.
function packRead(text) {
  const found = [], bad = (code, path, t) => found.push({ level: 'error', code, path, text: t }), note = (code, path, t) => found.push({ level: 'note', code, path, text: t });
  const isObj = v => v !== null && typeof v === 'object' && !Array.isArray(v), own = (o, k) => Object.prototype.hasOwnProperty.call(o, k);
  const info = { name: '', made: '', by: '', mod: '', maps: [], modes: [], capabilities: [], files: [] };
  const done = () => ({ findings: found, pack: found.some(f => f.level === 'error') ? null : info, info });
  let root;
  try { root = JSON.parse(String(text == null ? '' : text)); } catch (err) { bad('not-json', '', 'this is not a JSON file: ' + (err && err.message ? err.message : err)); return done(); }
  if (!isObj(root)) { bad('wrong-type', '', 'a pack is a JSON object'); return done(); }
  if (!own(root, 'schema')) { bad('missing-field', 'schema', 'a pack names its format: "schema": "' + PACK_SCHEMA + '"'); return done(); }
  const sv = typeof root.schema === 'string' ? root.schema : '';
  if (sv !== PACK_SCHEMA) {
    const m = /^missionlab-pack\/(\d+)$/.exec(sv);
    if (m && +m[1] > 1) bad('future-version', 'schema', 'this pack is ' + sv + '; this version of MissionLab reads ' + PACK_SCHEMA + '. Update MissionLab to use it');
    else bad('unknown-format', 'schema', '"' + sv + '" is not a MissionLab pack format');
    return done();
  }
  const str = (o, key, path, required, missing) => {
    if (!own(o, key) || o[key] === null) { if (required) bad('missing-field', path, missing); return ''; }
    if (typeof o[key] !== 'string') { bad('wrong-type', path, '"' + key + '" is text'); return ''; }
    const s = o[key].trim(); if (required && !s) bad('missing-field', path, missing); return s;
  };
  const words = (o, key, path) => {
    if (!own(o, key)) { bad('missing-field', path, 'the manifest lists "' + key + '" (an empty list when there are none)'); return []; }
    if (!Array.isArray(o[key])) { bad('wrong-type', path, '"' + key + '" is a list of text'); return []; }
    const r = []; o[key].forEach((w, i) => { if (typeof w !== 'string') bad('wrong-type', path + '[' + i + ']', '"' + key + '" is a list of text'); else r.push(w); }); return r;
  };
  info.name = str(root, 'name', 'name', true, 'the pack needs a "name"');
  info.made = str(root, 'made', 'made', false, ''); info.by = str(root, 'by', 'by', false, '');
  const man = root.manifest, texts = root.files;
  if (!isObj(man)) { bad('missing-field', 'manifest', 'a pack needs a "manifest" object'); return done(); }
  if (!isObj(texts)) { bad('missing-field', 'files', 'a pack needs "files": an object of file texts'); return done(); }
  if (!Array.isArray(man.files)) { bad('missing-field', 'manifest.files', 'the manifest lists every file under "files"'); return done(); }
  if (!man.files.length) bad('missing-field', 'manifest.files', 'the pack holds no files');
  if (man.files.length > PACK_MAX_FILES) { bad('out-of-range', 'manifest.files', 'a pack holds at most ' + PACK_MAX_FILES + ' files'); return done(); }

  const req = man.requires;
  if (!isObj(req)) bad('missing-field', 'manifest.requires', 'the manifest says what the pack needs under "requires"');
  else {
    info.maps = words(req, 'maps', 'manifest.requires.maps');
    info.maps.forEach((m, k) => { if (!m || /[\/\\:]/.test(m) || m.includes('..')) bad('bad-name', 'manifest.requires.maps[' + k + ']', 'a map is named by its file name only, such as suburbv2.bytes'); });
    info.modes = words(req, 'modes', 'manifest.requires.modes');
    if (own(req, 'modes') && !info.modes.length) bad('missing-field', 'manifest.requires.modes', 'the pack names its mode: ["pve"]');
    info.modes.forEach((m, k) => { if (m !== PACK_PVE) bad('unknown-value', 'manifest.requires.modes[' + k + ']', '"' + m + '" is not a mode MissionLab plays; it runs only in PvE'); });
    info.mod = str(req, 'mod', 'manifest.requires.mod', true, 'the pack names the MissionLab version it was made with');
    if (info.mod && !packParts(info.mod)) bad('unknown-value', 'manifest.requires.mod', '"' + info.mod + '" is not a version number such as 0.26.0');
    else if (info.mod && packNewer(info.mod, EDITOR_VERSION)) note('newer-mod', 'manifest.requires.mod', 'made with MissionLab ' + info.mod + '; this is ' + EDITOR_VERSION + '. Each file below is checked by this version\'s rules');
    info.capabilities = words(req, 'capabilities', 'manifest.requires.capabilities');
    info.capabilities.forEach((c, k) => { if (!PACK_KNOWN.includes(c)) bad('unsupported', 'manifest.requires.capabilities[' + k + ']', '"' + c + '" is a feature this version of MissionLab does not have'); });
  }

  const names = new Set(), ids = new Map();
  man.files.forEach((f, i) => {
    const at = 'manifest.files[' + i + ']';
    if (!isObj(f)) return bad('wrong-type', at, 'a manifest entry is an object');
    const e = { name: str(f, 'name', at + '.name', true, 'every file needs its "name"'), kind: str(f, 'kind', at + '.kind', true, 'every file needs its "kind": scenario or module'),
      id: '', revision: 0, hash: '', bytes: 0, text: '', navFile: '', mapPrint: '', title: '', capabilities: [], enemies: 0, findings: [] };
    if (!e.name || !e.kind) return;
    if (e.kind !== 'scenario' && e.kind !== 'module') return bad('not-allowed', at + '.kind', '"' + e.kind + '" files cannot go in a pack: only scenarios and modules do, never navigation data, map geometry, replays or game files');
    if (packForbiddenName(e.name)) return bad('not-allowed', at + '.name', e.name + ' is not a scenario or a module: navigation data, map geometry, replays and game files never go in a pack');
    if (packFileName(e.name, e.kind) !== e.name) return bad('bad-name', at + '.name', '"' + e.name + '" is not a plain file name: ' + (e.kind === 'module' ? 'letters, digits, \'_\' or \'-\' and ".module.json"' : 'letters, digits, \'_\' or \'-\' and ".json"') + ', no folders');
    if (names.has(e.name.toLowerCase())) return bad('duplicate-id', at + '.name', e.name + ' is listed twice');
    names.add(e.name.toLowerCase());
    const file = 'files.' + e.name, start = found.length;
    info.files.push(e);
    const mine = () => { e.findings = found.slice(start); };
    if (!own(texts, e.name)) { bad('missing-ref', at + '.name', 'the pack lists ' + e.name + ' but does not hold its text'); return mine(); }
    if (typeof texts[e.name] !== 'string') { bad('wrong-type', file, 'each file is held as its text, a JSON string'); return mine(); }
    e.text = texts[e.name]; e.bytes = packBytes(e.text);
    if (packForbiddenText(e.text)) { bad('not-allowed', file, 'this is navigation data, map geometry, a replay or a recon file: none of those go in a pack'); return mine(); }
    if (e.bytes > (e.kind === 'module' ? 1 << 20 : 4 * 1024 * 1024)) { bad('out-of-range', file, 'the file is larger than MissionLab takes (' + (e.kind === 'module' ? '1 MB' : '4 MB') + ')'); return mine(); }
    const hash = str(f, 'hash', at + '.hash', true, 'every file needs its "hash"');
    e.hash = textHash(e.text);
    if (hash && hash !== e.hash) bad('hash-mismatch', at + '.hash', 'the text of ' + e.name + ' is not the text the manifest describes: it was changed after the pack was made');
    if (!Number.isInteger(f.bytes)) bad('missing-field', at + '.bytes', 'every file needs its size in "bytes"');
    else if (f.bytes !== e.bytes) bad('hash-mismatch', at + '.bytes', e.name + ' has ' + e.bytes + ' bytes; the manifest says ' + f.bytes);
    if (e.kind === 'module') {
      let obj = null; try { obj = JSON.parse(e.text); } catch (err) { /* named below */ }
      const wrong = obj ? moduleProblems(obj) : ['it is not JSON'];
      if (wrong.length) bad('bad-module', file, 'not a module MissionLab can use: ' + wrong[0]);
      else { e.title = obj.name; e.enemies = obj.spawns.length; }
      return mine();
    }
    for (const x of scenarioProblems(e.text)) found.push(Object.assign({}, x, { path: x.path ? file + '.' + x.path : file }));
    if (found.slice(start).some(x => x.level === 'error' && x.path.startsWith(file))) return mine();
    const obj = JSON.parse(e.text), body = planBody(obj) || {};
    e.id = typeof body.id === 'string' ? body.id : ''; e.revision = Number.isInteger(body.revision) && body.revision > 0 ? body.revision : 0;
    e.navFile = packBase(typeof body.navFile === 'string' ? body.navFile : ''); e.mapPrint = typeof body.mapPrint === 'string' ? body.mapPrint : '';
    e.title = typeof body.name === 'string' ? body.name.trim() : ''; e.enemies = Array.isArray(body.spawns) ? body.spawns.length : 0;
    e.capabilities = capabilitiesOf(obj);
    if (!e.enemies) bad('no-enemies', file + '.spawns', 'it has no enemies, so MissionLab would leave the game\'s own mission');
    if (typeof f.id === 'string' && f.id !== e.id) bad('id-mismatch', at + '.id', 'the manifest says ' + e.name + ' is ' + f.id + '; the file says ' + (e.id || 'nothing'));
    if (e.id) { if (ids.has(e.id)) bad('duplicate-id', file + '.id', e.name + ' has the same id as ' + ids.get(e.id) + ': two scenarios in one pack are two different missions'); else ids.set(e.id, e.name); }
    for (const c of e.capabilities) if (!info.capabilities.includes(c)) bad('missing-ref', 'manifest.requires.capabilities', e.name + ' uses ' + c + ', which the manifest does not list');
    if (e.navFile && !info.maps.includes(e.navFile)) bad('missing-ref', 'manifest.requires.maps', e.name + ' is made for ' + e.navFile + ', which the manifest does not list');
    mine();
  });
  for (const k of Object.keys(texts))
    if (!names.has(k.toLowerCase()))
      bad(packForbiddenName(k) ? 'not-allowed' : 'unlisted', 'files.' + k, packForbiddenName(k) ? k + ' is not a scenario or a module: it cannot go in a pack' : 'the manifest does not list ' + k);
  return done();
}

// A pack from [name, text] pairs, as Pack.Write makes it (the same text, line for line): the manifest is made from the files.
function packWrite(name, made, by, mod, files) {
  const maps = new Set(), caps = new Set(), Q = s => JSON.stringify(String(s == null ? '' : s));
  let out = '{"schema":' + Q(PACK_SCHEMA) + ',"name":' + Q(name) + ',"made":' + Q(made) + ',"by":' + Q(by) + ',\n "manifest":{"files":[';
  files.forEach(([n, t], i) => {
    const module = n.toLowerCase().endsWith(PACK_MODULE);
    out += (i ? ',' : '') + '\n  {"name":' + Q(n) + ',"kind":' + Q(module ? 'module' : 'scenario');
    if (!module) {
      let obj = null; try { obj = JSON.parse(t); } catch (e) { /* the read below names it */ }
      const body = obj ? planBody(obj) : null;
      if (body && typeof body.id === 'string' && /^[A-Za-z0-9_-]{1,64}$/.test(body.id)) out += ',"id":' + Q(body.id);
      if (obj && !scenarioProblems(obj).some(f => f.level === 'error')) {
        const nav = packBase(typeof body.navFile === 'string' ? body.navFile : '');
        if (nav) maps.add(nav);
        capabilitiesOf(obj).forEach(c => caps.add(c));
      }
    }
    out += ',"hash":' + Q(textHash(t)) + ',"bytes":' + packBytes(t) + '}';
  });
  const sorted = s => [...s].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)).map(Q).join(',');
  out += '\n ],\n "requires":{"maps":[' + sorted(maps) + '],"modes":[' + Q(PACK_PVE) + '],"mod":' + Q(mod) + ',"capabilities":[' + sorted(caps) + ']}},\n "files":{';
  files.forEach(([n, t], i) => { out += (i ? ',' : '') + '\n  ' + Q(n) + ':' + Q(t); });
  out += '\n }}\n';
  const read = packRead(out);
  return { text: read.pack ? out : '', findings: read.findings };
}

// ----- the preview
// Whether a scenario's positions were made on the map open here: its map file by name, then by fingerprint.
function packMapWords(e) {
  const m = state.map && !state.map.isExample ? state.map : null;
  if (!e.navFile) return { ok: true, words: 'Not tied to a map file.' };
  if (!m) return { ok: false, words: 'Made for ' + e.navFile + '. No map is open here: open ' + e.navFile + ' (or start a mission on it) to check the positions.' };
  if (packBase(m.name).toLowerCase() !== e.navFile.toLowerCase()) return { ok: false, words: 'Made for ' + e.navFile + ', but ' + packBase(m.name) + ' is open: positions need checking on ' + e.navFile + '.' };
  if (!e.mapPrint || !m.print) return { ok: false, words: 'Made for ' + e.navFile + ', which is open; the author\'s copy of it is unknown, so positions need checking.' };
  if (e.mapPrint !== m.print) return { ok: false, words: 'Made for ' + e.navFile + ', but your copy of it differs from the author\'s (the game was updated?): positions need checking.' };
  return { ok: true, words: 'Made on the same ' + e.navFile + ' as the one open here.' };
}
// What is already there with the same name or id: the catalogue for scenarios, the game's modules folder for modules.
// Without the game the folders cannot be seen, and that is said rather than guessed.
function packClash(e) {
  if (e.kind === 'module') {
    if (!live.on) return { known: false };
    const hit = moduleGame.find(m => String(m.file || '').toLowerCase() === e.name.toLowerCase());
    return { known: true, name: !!hit, id: false, same: false };
  }
  if (!live.on || !Array.isArray(catalogue.list)) return { known: false };
  const byName = catalogue.list.find(c => String(c.file).toLowerCase() === e.name.toLowerCase()), byId = e.id ? catalogue.list.filter(c => c.id === e.id) : [];
  const same = (byName && byName.hash === e.hash) || byId.some(c => c.hash === e.hash);
  return { known: true, name: !!byName, id: byId.length > 0, same: !!same, ids: byId.map(c => c.file + (c.revision ? ' (revision ' + c.revision + ')' : '')) };
}
function packPreview(text) {
  const r = packRead(text), info = r.info, unsupported = info.capabilities.filter(c => !PACK_KNOWN.includes(c));
  const files = info.files.map(e => {
    const errors = e.findings.filter(f => f.level === 'error'), clash = packClash(e);
    return { name: e.name, kind: e.kind, title: e.title, id: e.id, revision: e.revision, enemies: e.enemies, capabilities: e.capabilities, errors, notes: e.findings.filter(f => f.level === 'note'),
      map: e.kind === 'scenario' ? packMapWords(e) : null, clash, collides: !!(clash.known && !clash.same && (clash.name || clash.id)) };
  });
  const inFile = new Set(info.files.flatMap(e => e.findings));
  return { name: info.name, made: info.made, by: info.by, mod: info.mod, maps: info.maps, modes: info.modes, capabilities: info.capabilities, unsupported,
    findings: r.findings, general: r.findings.filter(f => !inFile.has(f)),
    files, ok: !!r.pack, unchecked: !live.on };
}
const packFindingText = f => (f.path ? f.path + ': ' : '') + f.text;
function renderPackPreview() {
  const box = $('packPreview'), p = packs.preview;
  box.textContent = '';
  $('packInstall').hidden = !p;
  if (!p) return;
  const line = (parent, tag, text, cls) => { const el = document.createElement(tag); el.textContent = text; if (cls) el.className = cls; parent.appendChild(el); return el; };
  line(box, 'p', '"' + (p.name || 'Unnamed pack') + '"' + (p.by ? ', made with ' + p.by : '') + (p.made ? ' on ' + p.made : '') + '.');
  line(box, 'p', 'Needs: ' + (p.maps.length ? 'map ' + p.maps.join(', ') : 'no particular map') + '; ' + (p.modes.join(', ') || 'no mode named').toUpperCase() + '; MissionLab ' + (p.mod || 'version not given')
    + '; features: ' + (p.capabilities.join(', ') || 'none') + '.', 'hint');
  if (p.unsupported.length) line(box, 'p', 'This version of MissionLab (' + EDITOR_VERSION + ') does not have: ' + p.unsupported.join(', ') + '. The pack cannot be installed here.', 'packBad');
  for (const f of p.general) line(box, 'p', packFindingText(f), f.level === 'error' ? 'packBad' : 'hint');
  if (p.unchecked) line(box, 'p', 'Without the game the scenarios and modules folders cannot be seen, so names and ids already there are not checked. Open this page from the game to install.', 'hint');
  const list = line(box, 'ul', '');
  for (const f of p.files) {
    const li = line(list, 'li', '');
    line(li, 'b', f.name);
    line(li, 'span', ' ' + (f.kind === 'module' ? 'module' : 'scenario') + (f.title ? ' "' + f.title + '"' : '') + (f.id ? ', ' + f.id + (f.revision ? ' revision ' + f.revision : '') : '')
      + (f.enemies ? ', ' + plural(f.enemies, 'enemy', 'enemies') : ''));
    if (!f.errors.length) line(li, 'div', f.kind === 'module' ? 'The module is fine.' : 'MissionLab would accept it.' + (f.notes.length ? ' ' + f.notes.map(n => n.text).join('. ') + '.' : ''), 'hint');
    for (const e of f.errors) line(li, 'div', packFindingText(e), 'packBad');
    if (f.map) line(li, 'div', f.map.words, f.map.ok ? 'hint' : 'packWarn');
    if (f.clash.same) line(li, 'div', 'The same text is already there: nothing to install.', 'hint');
    else if (f.collides) {
      const why = f.clash.name && f.clash.id ? 'A file of that name and a scenario with its id (' + f.clash.ids.join(', ') + ') are already there.'
        : f.clash.name ? 'A file of that name is already there.' : 'A scenario with its id is already there: ' + f.clash.ids.join(', ') + '.';
      line(li, 'div', why + ' Nothing is written over.', 'packWarn');
      const sel = document.createElement('select'); sel.setAttribute('aria-label', 'What to do with ' + f.name);
      const opts = [['copy', 'Install as a copy (new name' + (f.clash.id ? ', new id' : '') + ')'], ...(f.clash.id ? [['revision', 'Install as the next revision (same id, new name)']] : []), ['skip', 'Skip it']];
      for (const [v, t] of opts) { const o = document.createElement('option'); o.value = v; o.textContent = t; sel.appendChild(o); }
      sel.value = packs.choices[f.name] || 'copy';
      sel.addEventListener('change', () => { packs.choices[f.name] = sel.value; });
      li.appendChild(sel);
    }
  }
  $('packInstall').disabled = !p.ok;
  $('packInstall').textContent = live.on ? 'Install' : 'Open in Create';
}
async function packOpen(name, text) {
  packs.text = String(text); packs.fileName = name; packs.choices = {};
  if (live.on) { try { await refreshCatalogue(); } catch (e) { /* the preview says what is unknown */ } if (moduleServed()) await moduleLoadGame(); }
  packs.preview = packPreview(packs.text); packs.key = packPreviewKey();
  renderPackPreview();
  const p = packs.preview;
  return packNote('pack', p.ok ? name + ': ' + plural(p.files.length, 'file', 'files') + ', nothing wrong in them. Check the map lines, then ' + (live.on ? 'Install.' : 'Open in Create.')
    : name + ' has problems; it cannot be installed. They are listed above.');
}
const packPreviewKey = () => [live.on, catalogue.list ? catalogue.list.map(c => c.file + c.hash).join() : '-', moduleGame.map(m => m.file).join(), state.map ? state.map.name + (state.map.print || '') : ''].join('|');
const packNote = (which, text) => { $(which === 'pack' ? 'packMsg' : 'packExportMsg').textContent = text; return text; };

// Install: through the game, which writes the files (never over one); offline the scenario opens in Create, nothing installed.
// post sends the request (tests pass a stand-in for the game).
const packPost = async body => {
  const r = await fetch('/api/pack/install', { method: 'POST', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-MissionLab': '1' }, body: JSON.stringify(body) });
  return { status: r.status, ok: r.ok, answer: await r.json().catch(() => ({})) };
};
async function packInstall(post) {
  const p = packs.preview;
  if (!p) return packNote('pack', 'Open a pack file first.');
  if (!p.ok) return packNote('pack', 'Nothing was installed: the pack has problems, listed above.');
  if (!live.on) {
    const read = packRead(packs.text).info.files.filter(e => e.kind === 'scenario' && packs.choices[e.name] !== 'skip');
    if (!read.length) return packNote('pack', 'The pack holds no scenario to open.');
    const said = loadScenario(JSON.parse(read[0].text), read[0].name);
    if (state.refused) return packNote('pack', said);
    setView('create'); changed();
    const rest = p.files.filter(f => f.name !== read[0].name).map(f => f.name);
    const note = historyList().note;
    return packNote('pack', 'Opened ' + read[0].name + ' in Create; nothing was installed (that needs the game). ' + (note ? note + ' ' : '')
      + (rest.length ? 'Also in the pack: ' + rest.join(', ') + '. ' : '') + 'Save it to keep it.');
  }
  if (packs.busy) return '';
  packs.busy = true;
  const choices = {};
  for (const f of p.files) if (f.collides) choices[f.name] = packs.choices[f.name] || 'copy';
  try {
    const r = await (post || packPost)({ pack: packs.text, choices }), a = r.answer || {};
    if (!r.ok || !a.ok) return packNote('pack', 'Nothing was installed: ' + (a.why || a.error || 'the game answered ' + r.status) + '.');
    return packNote('pack', packInstalledWords(a));
  } catch (e) { return packNote('pack', 'The game did not answer; nothing is known to be installed. Is it still running?'); }
  finally { packs.busy = false; refreshCatalogue(); if (moduleServed()) moduleLoadGame(); }
}
const PACK_DONE = { install: 'installed as', copy: 'installed as a copy,', revision: 'installed as a new revision,', skip: 'skipped', same: 'already there as' };
function packInstalledWords(a) {
  const list = Array.isArray(a.files) ? a.files : [];
  const each = list.map(f => f.name + ' ' + (f.written === false && f.action !== 'skip' && f.action !== 'same' ? 'not written (' + f.why + ')' : (PACK_DONE[f.action] || f.action) + (f.file && f.action !== 'skip' ? ' ' + f.folder + '/' + f.file : '')));
  return 'Installed ' + plural(a.written | 0, 'file', 'files') + ': ' + each.join('; ') + '. Nothing was written over.';
}

// ----- export
function packChoices() {
  const local = savedModules();
  return [...moduleGame.map(m => ({ key: 'g:' + m.name, label: (m.title || m.name) + ' (the game\'s)', file: m.file })),
    ...local.map((m, i) => ({ key: 'l:' + i + ':' + m.name, label: m.name + ' (this browser)', file: moduleFileName(m.name) })),
    ...packs.extra.map((m, i) => ({ key: 'f:' + i, label: m.file + ' (file)', file: m.file }))];
}
async function packModuleText(key) {
  if (key.startsWith('g:')) { const r = await fetch('/api/modules/' + encodeURIComponent(key.slice(2)), { cache: 'no-store' }); if (!r.ok) throw new Error('the game did not hand over ' + key.slice(2)); return r.text(); }
  if (key.startsWith('l:')) { const m = savedModules()[+key.split(':')[1]]; if (!m) throw new Error('it is no longer kept in this browser'); return moduleText(m); }
  const f = packs.extra[+key.slice(2)]; if (!f) throw new Error('the file is gone'); return f.text;
}
const packDay = () => new Date().toISOString().slice(0, 10);
// The scenario goes in as it is saved: links to named places stay links, and the places they name are in the same text
// (ML-040). Flattened (the box under the module list, or opts.flat), each linked zone carries its own copy of its shape
// instead, as missionlab-plan/2, for players whose MissionLab cannot read links. A file opened and not changed since goes
// in as its own text, as laid out on disk: the page's text of it can differ, and then its fingerprint would too, so the
// pack installed back would clash with the very file it came from instead of being the same.
async function packExport(opts) {
  const held = moduleBlock();
  if (held) return packNote('export', held);
  if (state.example || !state.enemies.length) return packNote('export', state.example ? 'Open a real map file first: the example cannot be exported.' : 'Place at least one enemy first: a pack carries a scenario.');
  const flat = opts && typeof opts.flat === 'boolean' ? opts.flat : $('packFlat').checked === true;
  const page = scenarioText(), own = !flat && catalogue.savedFile !== null && catalogue.savedText === page;
  const text = own ? catalogue.savedFile : flat ? scenarioText(scenarioFlatObject()) : page, wrong = scenarioProblems(text).filter(f => f.level === 'error');
  if (wrong.length) return packNote('export', 'Not exported: MissionLab would refuse the scenario (' + packFindingText(wrong[0]) + '). The Mission tab lists every problem.');
  const files = [[fileName(), text]];
  try {
    for (const c of packChoices()) {
      if (!packs.include.has(c.key)) continue;
      if (files.some(f => f[0].toLowerCase() === c.file.toLowerCase())) return packNote('export', 'Two modules are named ' + c.file + '; include only one of them.');
      files.push([c.file, await packModuleText(c.key)]);
    }
  } catch (e) { return packNote('export', 'Not exported: a module could not be read (' + (e && e.message ? e.message : e) + ').'); }
  const name = String($('packName').value || '').trim() || state.title.trim() || 'scenario';
  const w = packWrite(name, packDay(), 'MissionLab ' + EDITOR_VERSION, EDITOR_VERSION, files);
  if (!w.text) { const first = w.findings.find(f => f.level === 'error'); return packNote('export', 'Not exported: ' + (first ? packFindingText(first) : 'the pack could not be made') + '.'); }
  const out = (packSafeName(name) || 'scenario.json').replace(/\.json$/, '') + PACK_SUFFIX;
  packs.lastExport = { name: out, text: w.text };
  try { await packDownload(out, w.text); }
  catch (e) { return packNote('export', e && e.code === 'declined' ? 'Not saved.' : 'The pack could not be saved here (' + (e && e.message ? e.message : e) + ').'); }
  const caps = packRead(w.text).info;
  return packNote('export', 'Saved as ' + out + ': ' + files.map(f => f[0]).join(', ') + '.' + (own ? ' The scenario goes in exactly as ' + catalogue.savedName + ' holds it on disk.' : '')
    + ' It needs ' + (caps.maps.join(', ') || 'no particular map') + ', PvE and MissionLab ' + EDITOR_VERSION
    + ' or later' + (caps.capabilities.length ? ', with ' + caps.capabilities.join(', ') : '') + '. No map data, geometry or replays are in it.');
}
// Downloads where the page can (a browser always can); the viewer's own saver when it gives one.
async function packDownload(name, text) {
  if (saver) return saver(name, text);
  const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' })); a.download = name;
  document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(a.href), 4000);
}

// Called from syncSide: the module list for export, and the preview again when the catalogue, modules or map changed.
function syncPacks() {
  if (!state.packsReady) return;      // syncSide can run before this part has started
  const choices = packChoices(), key = choices.map(c => c.key).join('|'), list = $('packModules');
  $('packFlatRow').hidden = !state.zones.some(z => z.place > 0);
  if (list.dataset.key !== key) {
    list.dataset.key = key; list.textContent = '';
    for (const k of [...packs.include]) if (!choices.some(c => c.key === k)) packs.include.delete(k);
    if (!choices.length) { const li = document.createElement('li'); li.className = 'hint'; li.textContent = 'No modules to add. Saved modules and module files you add show here.'; list.appendChild(li); }
    for (const c of choices) {
      const li = document.createElement('li'), label = document.createElement('label'), box = document.createElement('input');
      box.type = 'checkbox'; box.checked = packs.include.has(c.key);
      box.addEventListener('change', () => { if (box.checked) packs.include.add(c.key); else packs.include.delete(c.key); });
      label.appendChild(box); label.appendChild(document.createTextNode(' ' + c.label)); li.appendChild(label); list.appendChild(li);
    }
  }
  if (packs.preview && packPreviewKey() !== packs.key) { packs.preview = packPreview(packs.text); packs.key = packPreviewKey(); renderPackPreview(); }
}

$('packExport').addEventListener('click', () => { packExport(); });
$('packInstall').addEventListener('click', () => { packInstall(); });
$('packAddModule').addEventListener('click', () => $('packModuleFile').click());
$('packModuleFile').addEventListener('change', async ev => {
  const got = [...(ev.target.files || [])]; ev.target.value = '';
  for (const f of got) {
    try {
      const text = await f.text(), obj = JSON.parse(text), wrong = moduleProblems(obj);
      if (wrong.length) { packNote('export', f.name + ' is not a module MissionLab can use: ' + wrong[0] + '.'); continue; }
      const file = packFileName(f.name, 'module') || moduleFileName(obj.name);
      packs.extra.push({ file, text }); packs.include.add('f:' + (packs.extra.length - 1));
      packNote('export', f.name + ' added as ' + file + '.');
    } catch (err) { packNote('export', f.name + ' could not be read: ' + (err && err.message ? err.message : err)); }
  }
  syncPacks();
});
$('packOpen').addEventListener('click', () => $('packFile').click());
$('packFile').addEventListener('change', async ev => {
  const f = ev.target.files && ev.target.files[0]; ev.target.value = '';
  if (!f) return;
  try { await packOpen(f.name, await f.text()); } catch (err) { packNote('pack', f.name + ' could not be read: ' + (err && err.message ? err.message : err)); }
});
// Last, so syncPacks (called from syncSide) does nothing until everything above exists.
state.packsReady = true;
