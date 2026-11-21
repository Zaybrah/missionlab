// ML-026: scenario packs, and the Play list, as found against the game on 21 November.
// A scenario opened unchanged from its file goes into a pack as the file's own text, so its fingerprint is the file's and
// installing the pack back finds the file already there instead of a clash. examples/clean_house_quiet_raid.json is laid
// out by hand, so the page's own text of it is different (seen in the game: 5046 bytes on disk, 3562 in the pack). After
// an edit, and for the flat export, the page's text goes in.
// Three files with one title (the original, a copy installed from a pack and a new revision) had identical rows in Play;
// such rows now name their file and revision, and nothing else, so a sealed mission gives nothing more away.
import fs from 'node:fs';
import path from 'node:path';

const entry = (file, title, id, revision, more) => Object.assign({
  file, value: file.replace(/\.json$/, ''), title, id, revision, readable: true, usable: true, why: '', findings: [], sealed: false, briefing: [],
  map: 'Clean House', navFile: 'suburbv2.bytes', compat: 'tested', capabilities: [], needs: [], lastRun: null, hash: '',
}, more || {});

export default async function (h) {
  const S = 'clean_house_quiet_raid.json';
  const raid = fs.readFileSync(path.join(h.ROOT, 'examples', S), 'utf8');      // as on disk, line ends and all
  {
    const { api } = await h.load(h.script, S, raid);
    const lf = raid.replace(/\r\n/g, '\n');
    h.check('ML-026 the example is laid out unlike the page\'s own text, so the checks below mean something', !api.state.refused && api.scenarioText() !== lf, api.state.refused && JSON.stringify(api.state.refused));
    const told = await api.packExport({ flat: false }), back = api.packRead(api.packs.lastExport.text), f = back.pack ? back.info.files[0] : null;
    h.check('ML-026 a file opened and not changed goes into the pack as its own text', !!f && f.text === raid, told);
    h.check('ML-026 so the pack names the file\'s fingerprint (line ends do not count)', !!f && f.hash === api.textHash(raid) && f.hash === api.textHash(lf), f && f.hash);
    h.check('ML-026 and the message says the file goes in as it is on disk', told.startsWith('Saved as ') && told.includes('exactly as ' + S + ' holds it on disk'), told);

    const flatTold = await api.packExport({ flat: true }), flat = api.packRead(api.packs.lastExport.text);
    h.check('ML-026 the flat export writes the page\'s text, not the file\'s', flat.pack && flat.info.files[0].text === api.scenarioText(api.scenarioFlatObject()) && flat.info.files[0].text !== raid && !flatTold.includes('on disk'), flatTold);

    const was = api.state.minutes;
    api.state.minutes = was + 5;
    const editedTold = await api.packExport({ flat: false }), edited = api.packRead(api.packs.lastExport.text);
    h.check('ML-026 after an edit the page\'s text goes in', edited.pack && edited.info.files[0].text === api.scenarioText() && edited.info.files[0].hash === api.textHash(api.scenarioText()) && !editedTold.includes('on disk'), editedTold);
    api.state.minutes = was;
    await api.packExport({ flat: false });
    h.check('ML-026 back as it was opened, the file\'s own text goes in again', api.packRead(api.packs.lastExport.text).info.files[0].text === raid);
  }

  {   // The Play list: three rows with one title and one with its own.
    const api = h.openEditor(h.script).api;
    const original = entry(S, 'Clean House: Quiet Raid', 'sc-cleanhouse-quiet', 0);
    const copy = entry('clean_house_quiet_raid-copy.json', 'Clean House: Quiet Raid', 'sc-cleanhouse-quiet-copy', 0);
    const r1 = entry('clean_house_quiet_raid-r1.json', 'Clean House: Quiet Raid', 'sc-cleanhouse-quiet', 1);
    const other = entry('depot_run.json', 'Depot run', 'sc-depot', 3, { map: 'Depot Mexico', navFile: 'depotraid1.bytes' });
    const list = [original, copy, r1, other], rows = list.map(e => api.catalogueRowMeta(e, list));
    h.check('ML-026 a row sharing its title names its file', rows[0] === S + ' · Clean House · tested · never played', rows[0]);
    h.check('ML-026 the copy names its file', rows[1] === 'clean_house_quiet_raid-copy.json · Clean House · tested · never played', rows[1]);
    h.check('ML-026 a revision other than 0 is named too', rows[2] === 'clean_house_quiet_raid-r1.json · revision 1 · Clean House · tested · never played', rows[2]);
    h.check('ML-026 the three rows can be told apart', new Set(rows.slice(0, 3)).size === 3, rows.join(' | '));
    h.check('ML-026 a row with a title of its own stays as it was', rows[3] === 'Depot Mexico · tested · never played', rows[3]);
    const sealed = Object.assign({}, copy, { sealed: true, enemies: 9 }), refused = Object.assign({}, r1, { usable: false, why: 'MissionLab would refuse it: spawns: empty' });
    const twins = [original, sealed, refused];
    h.check('ML-026 a sealed row adds its file name and nothing else', api.catalogueRowMeta(sealed, twins) === 'sealed · clean_house_quiet_raid-copy.json · Clean House · tested · never played', api.catalogueRowMeta(sealed, twins));
    h.check('ML-026 a refused row names its file before why', api.catalogueRowMeta(refused, twins) === 'clean_house_quiet_raid-r1.json · revision 1 · MissionLab would refuse it: spawns: empty', api.catalogueRowMeta(refused, twins));
  }
}
