// ML-036: the page reads the same capability list as the plugin (docs/capabilities.json, inlined by tools/editor/build.py).
// A scenario with hunters shows the recorded failure, solo evidence never reads as a client's, nothing is confirmed on a
// build no evidence names (or offline, where the build is unknown), and a missing intel item or a mode MissionLab leaves
// alone is named before the mission is chosen. The parts are joined here with the list put in, as build.py does.
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';

function builtScript(ROOT) {
  const src = path.join(ROOT, 'tools', 'editor', 'src');
  const names = fs.readdirSync(src).filter(n => n.endsWith('.html') || n.endsWith('.js')).sort();
  const list = JSON.stringify(JSON.parse(fs.readFileSync(path.join(ROOT, 'docs', 'capabilities.json'), 'utf8'))).replace(/<\//g, '<\\/');
  const joined = Buffer.concat(names.map(n => fs.readFileSync(path.join(src, n)))).toString('utf8');
  if (!joined.includes("'__ML_CAPABILITIES__'")) throw new Error('the parts lost the capability list literal that build.py replaces');
  const page = joined.replace("'__ML_CAPABILITIES__'", () => list);
  const found = [...page.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g)].filter(m => m[1].includes('window.missionlabEditor'));
  return new vm.Script(found[0][1], { filename: 'tools/editor/index.html' });
}

const plan = extra => JSON.stringify(Object.assign({ schema: 'missionlab-plan/1', name: 'Test', navFile: 'suburbv2.bytes', spawns: [{ pos: [0, 0, 0] }, { pos: [5, 0, 5], trigger: 1 }] }, extra));
const recon = { schema: 'missionlab-recon/1', scene: 'Suburb_Night', navGraphs: { cachedStartupFile: 'suburbv2' },
  raid: { objectiveSets: [{ name: 'Cell Phone', objectives: [{ name: 'Cell Phone_1', displayName: 'Cell Phone' }, null] }], hvtSpawnPoints: [] } };

export default async function (h) {
  const script = builtScript(h.ROOT);
  const docs = JSON.parse(fs.readFileSync(path.join(h.ROOT, 'docs', 'capabilities.json'), 'utf8'));

  {   // The built page carries the whole list and reads it as the plugin does.
    const api = h.openEditor(script).api;
    h.check('the page reads the capability list without problems', api.capList.problems.length === 0 && api.capList.records.length === docs.capabilities.length, JSON.stringify(api.capList.problems));
    const missing = api.PACK_KNOWN.filter(w => !api.capList.byWord.has(w));
    h.check('every capability word the page knows has a row', missing.length === 0, missing.join(','));
    h.check('an item is matched by kind, whichever copy', api.capKind('Cell Phone_2') === 'cell phone' && api.capKind('Sim cards 1') === 'sim cards' && api.capKind('AK47') === 'ak47', api.capKind('Sim cards 1'));
  }

  {   // Offline, a scenario with hunters shows them as failing, names the build, and choosing it is labelled an experiment.
    const page = await h.load(script, 'hunt.json', plan({ triggers: [{ id: 1, when: 'time', delay: 30, hunt: true }] }));
    const entry = page.api.pageEntry(), hunt = entry && entry.capabilities.find(c => c.name === 'hunt');
    h.check('hunters are failing, not untested', hunt && hunt.shown === 'failing' && hunt.status === 'failing' && hunt.build === '0.26.1', JSON.stringify(hunt));
    const facts = new Map(page.api.catalogueFacts(entry, {}));
    h.check('the details say hunt is failing', String(facts.get('Features, as recorded in the game')).includes('hunt: failing'), facts.get('Features, as recorded in the game'));
    h.check('choosing it stays possible and is called an experiment', entry.usable && String(facts.get('Choosing it')).startsWith('an experiment') && entry.needs.some(f => f.code === 'capability-failing' && f.level === 'note'), JSON.stringify(entry.needs));
    h.check('offline nothing is confirmed, since the game\'s build is not known', entry.capabilities.every(c => !c.confirmed && c.shown !== 'supported'), JSON.stringify(entry.capabilities.map(c => c.shown)));
  }

  {   // Solo and host's-log evidence never reads as a client's; confirmation needs the build the evidence names.
    const api = h.openEditor(script).api;
    const claims = api.PACK_KNOWN.map(w => api.capJudge(w, '0.26.1')).filter(c => !c.client && /seen by a client/.test(c.words));
    h.check('no capability without client evidence reads as seen by a client', claims.length === 0, claims.map(c => c.name + ': ' + c.words).join(' | '));
    const now = api.capJudge('chance', '0.26.0'), next = api.capJudge('chance', '0.27.0');
    h.check('the trigger engine is confirmed on 0.26.0, where it was seen', now.confirmed && now.shown === 'supported', now.words);
    h.check('on 0.27.0 it is seen on 0.26.0, not yet on 0.27.0, and not confirmed', !next.confirmed && next.shown === 'seen-earlier' && next.words.includes('not yet on 0.27.0') && next.words.includes('no client has seen it'), next.words);
    h.check('no capability is confirmed on a build no evidence names', api.PACK_KNOWN.every(w => !api.capJudge(w, '0.27.0').confirmed), '');
    const area = api.capJudge('areas', '0.26.1');
    h.check('a two-player run with only the host\'s log says so', area.words.includes('host\'s log') && !area.client, area.words);
  }

  {   // A recon file opened beside the scenario: an intel item the map does not have is named, and the scenario is not offered.
    const page = await h.load(script, 'sim.json', plan({ triggers: [{ id: 1, when: 'intel', item: 'Sim cards', message: 'Got them.' }] }));
    await page.api.openFiles([h.fakeFile('Suburb_Night.late.json', JSON.stringify(recon))]);
    const entry = page.api.pageEntry(), miss = entry && entry.needs.find(f => f.code === 'missing-item');
    h.check('the missing item is named with its trigger', miss && miss.level === 'error' && miss.path === 'triggers[0].item' && miss.text.includes('sim cards'), JSON.stringify(entry && entry.needs));
    h.check('the scenario is not usable here, and says why', entry && !entry.usable && entry.why.includes('sim cards'), entry && entry.why);
    const facts = new Map(page.api.catalogueFacts(entry, {}));
    h.check('the details list it before it is chosen', String(facts.get('Before you choose it')).startsWith('Will not run as written'), facts.get('Before you choose it'));
    const ok = await h.load(script, 'phone.json', plan({ triggers: [{ id: 1, when: 'intel', item: 'cell phone', message: 'Got it.' }] }));
    await ok.api.openFiles([h.fakeFile('Suburb_Night.late.json', JSON.stringify(recon))]);
    h.check('an item the map has needs nothing', !ok.api.pageEntry().needs.some(f => f.code === 'missing-item'), JSON.stringify(ok.api.pageEntry().needs));
    const other = page.api.capFitting(page.api.reconFacts(recon, 'r'), { navFile: 'georgia1.bytes', spawns: [] });
    h.check('a recon file of another map is not used', other === null, JSON.stringify(other));
  }

  {   // Readiness before launch: the chosen scenario in a mode MissionLab leaves alone, its needs from the game, its capabilities.
    const api = h.openEditor(script).api;
    const entry = { file: 'hunt.json', value: 'hunt', title: 'Hunt', usable: true, navFile: 'suburbv2.bytes', scene: '',
      capabilities: [{ name: 'hunt', shown: 'failing', confirmed: false, build: '0.26.1' }, { name: 'doors', shown: 'supported', confirmed: true, build: '0.27.0' }],
      needs: [{ level: 'error', code: 'missing-item', path: 'triggers[0].item', text: 'trigger 1 waits for the sim cards, which this map does not have (recon/x.json)' }] };
    api.catalogue.list = [entry];
    const rows = api.scenarioReadyRows({ role: 'host', mode: 'PVP_TDM', pve: false, plugin: '0.27.0' }, { choice: 'hunt' });
    h.check('a mode MissionLab leaves alone is named for the chosen scenario', rows.some(r => !r.ok && r.text.startsWith('Will not run as written: Hunt') && r.text.includes('not a PvE raid')), JSON.stringify(rows));
    h.check('the game\'s own finding about the missing item is shown', rows.some(r => !r.ok && r.text.includes('sim cards')), JSON.stringify(rows));
    h.check('the unconfirmed capability is named with its state; the confirmed one is not', rows.some(r => !r.ok && r.text.includes('hunt (failing') && !r.text.includes('doors (')), JSON.stringify(rows));
    api.catalogue.list = [Object.assign({}, entry, { needs: [], capabilities: [entry.capabilities[1]] })];
    const fine = api.scenarioReadyRows({ role: 'host', mode: 'PVE_RAID', pve: true, plugin: '0.27.0' }, { choice: 'hunt' });
    h.check('with nothing missing and every capability confirmed the rows are all fine', fine.length === 1 && fine[0].ok, JSON.stringify(fine));
  }
}
