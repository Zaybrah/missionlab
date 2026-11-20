#!/usr/bin/env python3
"""Runs scenario files through docs/scenario.schema.json with a real JSON Schema validator (draft 2020-12).

  python3 tools/schema_check.py     exit 0 when every file is judged as listed below, 1 when one is not, 2 without jsonschema

Needs the jsonschema package (pip install jsonschema); the rest of the repository's Python uses only the standard library.

Must be valid:   every examples/*.json, tests/fixtures/all_fields*.json, tests/fixtures/legacy_minimal.json and
                 tests/fixtures/schema/valid/*.json.
Must be invalid: the structural refusals in tests/fixtures/invalid named in STRUCTURAL below, and every
                 tests/fixtures/schema/invalid/*.json. The strict readers refuse all of these too (tests/Pure/Ml034Tests.cs
                 and tests/editor/cases/ml034-schema-alignment.mjs hold them to that).

What the schema covers: types, allowed values and ranges, required keys, where the body sits for each format
(missionlab-plan/1 or no format at the root, missionlab-plan/2 under "mission" without a schema of its own, a mission
record's scenario under "plan"), that only zone and hold triggers in missionlab-plan/2 have an area, and that zone, hold
and door triggers have a pos.

What it does not cover; the strict readers (src/MissionLab.Core/ScenarioFile.cs and the editor's scenarioProblems) check
these, so a file the schema accepts can still be refused:
  - references: "after", "of", "trigger", "within", "fire", "extraction.after" and a choice's "drop.triggers" name a
    trigger or placement that exists, and a trigger does not wait for itself; a goal's "after", "activate", "succeed"
    and "fail" name a goal or trigger that exists, and a goal does not wait for itself; a pressure band's "goals" name
    goals that exist;
  - uniqueness: trigger, place and placement numbers, option, action and goal ids;
  - geometry: an area's rings do not cross themselves, holes lie inside their outline without overlapping, a floor band's
    top is above its bottom and at most 60 m above it, an area has at most 2048 points;
  - rules between keys: "pick" names one of the choices, "squad" has the fewest first, an hvt or a vehicle never has
    "fallback": "skip";
  - the mission as a whole (Completion.Check and the editor's mission check): that it can be finished at all.
Where the schema is stricter than the readers, on purpose or because a schema cannot say otherwise:
  - a key set to null counts as absent for the readers; the schema checks null as a value (and refuses it nearly
    everywhere);
  - the keys beside "mission" in a missionlab-plan/2 file, and a mission record's own keys beside "plan", are not read
    by MissionLab, but the schema still checks them by the body rules (the root lists the body keys once for both);
  - comments and trailing commas, which the readers allow, are not JSON and fail here before the schema is looked at.
"""
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCHEMA = os.path.join(HERE, 'docs', 'scenario.schema.json')

# Files in tests/fixtures/invalid whose refusal the schema can express: the version line, the envelope, areas, types and
# values. The others there are refused for references, duplicates or geometry, which only the strict readers see.
STRUCTURAL = [
    'area_in_plan1.json', 'area_on_alert.json', 'envelope_without_mission.json', 'mission_in_plan1.json',
    'schema_in_mission.json', 'signed_version.json', 'future_version.json', 'no_spawns.json', 'wrong_types.json',
    'unknown_values.json', 'bad_metadata.json', 'bad_metadata_types.json', 'place_in_plan2.json', 'bad_place_links.json',
    'goals_in_plan2.json', 'bad_goals.json', 'bad_outcomes.json', 'bad_pressure.json',
]


def rel(path):
    return os.path.relpath(path, HERE).replace(os.sep, '/')


def files(pattern):
    return sorted(glob.glob(os.path.join(HERE, *pattern.split('/'))))


def main():
    try:
        from jsonschema import Draft202012Validator
        from jsonschema.exceptions import SchemaError
    except ImportError:
        print('schema_check needs the jsonschema package: pip install jsonschema')
        return 2

    with open(SCHEMA, encoding='utf-8') as f:
        schema = json.load(f)
    wrong = []
    try:
        Draft202012Validator.check_schema(schema)
    except SchemaError as e:
        print('docs/scenario.schema.json is not a valid draft 2020-12 schema: ' + e.message)
        return 1
    # The body's rules are written once, under the root's properties, and $defs/missionBody points at each of them; a key
    # added to one list and not the other would go unchecked inside a missionlab-plan/2 file.
    root_keys = set(schema['properties']) - {'schema', 'mission', 'plan'}
    body_keys = set(schema['$defs']['missionBody']['properties'])
    if root_keys != body_keys:
        wrong.append('$defs/missionBody and the root properties name different keys: only at the root '
                     + ', '.join(sorted(root_keys - body_keys)) + '; only in missionBody ' + ', '.join(sorted(body_keys - root_keys)))
    validator = Draft202012Validator(schema)

    def judge(path, should_pass):
        try:
            with open(path, encoding='utf-8') as f:
                doc = json.load(f)
        except ValueError as e:
            wrong.append(rel(path) + ': not JSON (' + str(e) + ')')
            return
        # Paths mix names and list indices, so they are compared as text.
        errors = sorted(validator.iter_errors(doc), key=lambda e: '/'.join(str(p) for p in e.absolute_path))
        if should_pass and errors:
            for e in errors[:5]:
                at = '/'.join(str(p) for p in e.absolute_path) or '(root)'
                wrong.append(rel(path) + ': should be valid, but at ' + at + ': ' + e.message)
        elif not should_pass and not errors:
            wrong.append(rel(path) + ': should be invalid, but the schema accepts it')

    valid = files('examples/*.json') + files('tests/fixtures/all_fields*.json') + files('tests/fixtures/legacy_minimal.json') + files('tests/fixtures/schema/valid/*.json')
    invalid = [os.path.join(HERE, 'tests', 'fixtures', 'invalid', n) for n in STRUCTURAL] + files('tests/fixtures/schema/invalid/*.json')
    for path in invalid:
        if not os.path.exists(path):
            wrong.append(rel(path) + ': listed, but there is no such file')
    invalid = [p for p in invalid if os.path.exists(p)]
    for path in valid:
        judge(path, True)
    for path in invalid:
        judge(path, False)

    for line in wrong:
        print('DISAGREES: ' + line)
    if wrong:
        print(str(len(wrong)) + ' disagreement(s) with docs/scenario.schema.json')
        return 1
    print('schema_check: ' + str(len(valid)) + ' valid and ' + str(len(invalid)) + ' invalid files judged as listed')
    return 0


if __name__ == '__main__':
    sys.exit(main())
