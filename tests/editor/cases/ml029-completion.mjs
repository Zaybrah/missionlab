// ML-029: the editor's mission check treats a trigger in a circle as the plugin does: it never fires, even when its
// "any" list has another way in, so an extraction held behind it is refused; another trigger that opens the extraction
// is still a way out. The order of the trigger list changes nothing.
import fs from 'node:fs';
import path from 'node:path';

export default async function (h) {
  const fixture = JSON.parse(fs.readFileSync(path.join(h.FIXTURES, 'any_cycle_extraction.json'), 'utf8'));
  const orders = list => list.length <= 1 ? [list.slice()] : list.flatMap((x, i) => orders(list.filter((_, j) => j !== i)).map(rest => [x, ...rest]));
  const text = triggers => JSON.stringify(Object.assign({}, fixture, { triggers }));
  const findings = async triggers => (await h.load(h.script, 'any_cycle_extraction.json', text(triggers))).api.missionCheck().map(f => f.level + ': ' + f.text).sort();
  const blocked = list => list.some(f => f.startsWith('problem: ') && (f.includes('nobody could leave') || f.includes('The extraction can never open')));

  {   // The fixture: refused, and the circle of triggers 1 and 2 is named, in every order of the list.
    const all = [];
    for (const o of orders(fixture.triggers)) all.push(await findings(o));
    const first = all[0].join(' / ');
    h.check('the any-circle on the way out is refused by the mission check', blocked(all[0]), first);
    h.check('the mission check names triggers 1 and 2 as a circle', all[0].some(f => f.startsWith('problem: ') && f.includes('wait for each other in a circle')), first);
    h.check('all six orders of the list give the same findings', all.length === 6 && all.every(f => f.join(' / ') === first), all.map(f => f.join(' / ')).join(' ## '));
  }
  {   // A timer that opens the extraction is a way out, in every order.
    const opener = { id: 4, when: 'time', delay: 0, extraction: 'open' };
    const all = [];
    for (const o of orders(fixture.triggers.concat(opener))) all.push(await findings(o));
    h.check('with a timer that opens the extraction the any-circle is accepted in all 24 orders', all.length === 24 && all.every(f => !blocked(f)) && all.every(f => f.join(' / ') === all[0].join(' / ')), all[0].join(' / '));
  }
  {   // The editor finds the same circles as the plugin, whatever the order.
    const page = h.openEditor(h.script);
    const rings = zs => JSON.stringify(page.api.checkRings(zs));
    const z = (id, when, more) => Object.assign({ id, when, after: 0, of: [] }, more);
    const list = [z(1, 'after', { after: 2 }), z(2, 'all', { of: [1, 3] }), z(3, 'time'), z(4, 'after', { after: 4 }), z(5, 'after', { after: 1 }), z(6, 'any', { of: [6, 3] })];
    h.check('two circles are found, one of them a trigger waiting for itself, and a self-listing "any" is not one',
      orders(list.slice(0, 4)).every(o => rings(o.concat(list.slice(4))) === '[[1,2],[4]]'), rings(list));
  }
}
