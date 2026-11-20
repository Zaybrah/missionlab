// ML-034: the editor's strict reader judges the structural cases in tests/fixtures/schema as docs/scenario.schema.json does
// (tools/schema_check.py runs the same files through a JSON Schema validator, tests/Pure/Ml034Tests.cs through the plugin),
// reads the format line by the plugin's rule, and opens a mission record whose plan is a missionlab-plan/2 envelope.
import fs from 'node:fs';
import path from 'node:path';

export default async function (h) {
  const dir = path.join(h.FIXTURES, 'schema');
  const api = h.openEditor(h.script).api;
  const errors = obj => api.scenarioProblems(obj).filter(f => f.level === 'error').map(f => f.code + ' ' + f.path);
  const files = sub => fs.readdirSync(path.join(dir, sub)).filter(n => n.endsWith('.json')).sort();
  const text = (sub, name) => fs.readFileSync(path.join(dir, sub, name), 'utf8');

  {   // What the schema accepts the editor reads without an error.
    const names = files('valid');
    h.check('there are structural cases the schema must accept', names.length >= 3, String(names.length));
    for (const name of names) h.check('schema/valid/' + name + ' is read by the editor', errors(text('valid', name)).length === 0, errors(text('valid', name)).join(' | '));
  }
  {   // What the schema refuses the editor refuses too.
    const names = files('invalid');
    h.check('there are structural cases the schema must refuse', names.length >= 3, String(names.length));
    for (const name of names) h.check('schema/invalid/' + name + ' is refused by the editor', errors(text('invalid', name)).length > 0, 'no error');
  }
  {   // The format line is digits after the slash and nothing else, as the plugin reads it.
    const first = schema => { const e = errors({ schema, mission: { spawns: [{ pos: [0, 0, 0] }] } }); return e.length ? e[0] : ''; };
    for (const odd of ['missionlab-plan/+2', 'missionlab-plan/ 2', 'missionlab-plan/2 ', 'missionlab-plan/', 'missionlab-plan/2.0'])
      h.check('"' + odd + '" is not a scenario format', first(odd) === 'unknown-format schema', first(odd));
    h.check('a leading zero still names missionlab-plan/2', first('missionlab-plan/02') === '', first('missionlab-plan/02'));
    h.check('a number too big to hold is a newer format, not an unknown one', first('missionlab-plan/99999999999') === 'future-version schema', first('missionlab-plan/99999999999'));
  }
  {   // A mission record of a shaped mission opens, with its enemy and its shaped trigger.
    const page = await h.load(h.script, 'record.json', text('valid', 'mission_record.json'));
    const s = page.api.state;
    h.check('a mission record holding a plan/2 envelope is opened', !s.refused && s.enemies.length === 1 && s.zones.length === 1, page.banner());
    h.check('and its trigger keeps the shaped area', s.zones.length === 1 && /"area"/.test(page.api.scenarioText()), page.api.scenarioText().slice(0, 200));
  }
}
