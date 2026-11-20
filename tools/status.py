#!/usr/bin/env python3
"""Makes the README's Status table from docs/capabilities.json, the one list of what MissionLab has been seen to do.

The plugin carries the same file as a resource (tools/build.py) and the editor page has it inlined (tools/editor/build.py),
so the table, the Play view and the readiness check cannot drift apart. Python 3 standard library only.

  python3 tools/status.py            prints the table as it would be written
  python3 tools/status.py --check    exit 1 when the README's table is not what the file makes, or the file is broken
  python3 tools/status.py --write    writes the table into README.md between its two marker lines

The list is checked as the plugin reads it: every status, scope and result is a known word, every evidence record names
its build and date (a version or "unrecorded"), map and mode, and a scenario capability word belongs to one row only.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LIST = os.path.join(HERE, 'docs', 'capabilities.json')
README = os.path.join(HERE, 'README.md')
START = '<!-- capability table: made from docs/capabilities.json by python3 tools/status.py --write; change that file, not this table -->'
END = '<!-- end of the capability table -->'
SCHEMA = 'missionlab-capabilities/1'
STATUSES = ('supported', 'experimental', 'failing', 'untested', 'unavailable')
SCOPES = ('solo', 'two-player-host-log', 'co-op-client')
RESULTS = ('worked', 'failed', 'partial', 'not-seen')
VERSION = re.compile(r'^\d{1,6}(\.\d{1,6})*$')
DATE = re.compile(r'^\d{4}-\d{2}-\d{2}$')


def text(v):
    return v if isinstance(v, str) else ''


def problems(data):
    """What is wrong with the list, in the words Core's CapabilityRegistry.Read uses."""
    found = []
    if not isinstance(data, dict) or data.get('schema') != SCHEMA:
        return ['schema is not ' + SCHEMA]
    rows = data.get('capabilities')
    if not isinstance(rows, list):
        return ['no capabilities list']
    if not isinstance(data.get('intro'), list) or not all(isinstance(p, str) for p in data['intro']):
        found.append('intro is not a list of paragraphs')
    ids, words = set(), {}
    for n, c in enumerate(rows):
        at = 'capabilities[' + str(n) + ']'
        if not isinstance(c, dict):
            found.append(at + ' is not an object')
            continue
        cid = text(c.get('id'))
        if not cid or cid in ids:
            found.append(at + '.id is empty or used twice')
        ids.add(cid)
        for key in ('label', 'where', 'summary'):
            if not text(c.get(key)):
                found.append(at + '.' + key + ' is empty')
        if text(c.get('status')) not in STATUSES:
            found.append(at + ".status '" + text(c.get('status')) + "' is not one of " + ', '.join(STATUSES))
        for w in c.get('uses') or []:
            if not isinstance(w, str) or not w:
                found.append(at + '.uses holds something that is not a word')
            elif w in words:
                found.append(at + ".uses: '" + w + "' already belongs to " + words[w])
            else:
                words[w] = cid
        for k, e in enumerate(c.get('evidence') or []):
            eat = at + '.evidence[' + str(k) + ']'
            if not isinstance(e, dict):
                found.append(eat + ' is not an object')
                continue
            build, date = text(e.get('build')), text(e.get('date'))
            if build != 'unrecorded' and not VERSION.match(build):
                found.append(eat + ": build '" + build + "' is neither a version nor \"unrecorded\"")
            if date != 'unrecorded' and not DATE.match(date):
                found.append(eat + ": date '" + date + "' is neither YYYY-MM-DD nor \"unrecorded\"")
            if not text(e.get('map')) or not text(e.get('mode')):
                found.append(eat + ': map or mode is empty')
            if text(e.get('scope')) not in SCOPES:
                found.append(eat + ": scope '" + text(e.get('scope')) + "' is not one of " + ', '.join(SCOPES))
            if text(e.get('result')) not in RESULTS:
                found.append(eat + ": result '" + text(e.get('result')) + "' is not one of " + ', '.join(RESULTS))
    return found


def cell(s):
    return s.replace('|', '\\|').replace('\r', ' ').replace('\n', ' ')


def table(data):
    """The block between the markers, one line per entry, without line endings."""
    lines = [START]
    for p in data.get('intro') or []:
        lines += [cell(p), '']
    lines.append('| Capability | Status | Build, map, mode | Evidence and what is still open |')
    lines.append('|---|---|---|---|')
    for c in data['capabilities']:
        status = text(c.get('status')) + (', ' + text(c.get('qualifier')) if text(c.get('qualifier')) else '')
        lines.append('| ' + ' | '.join(cell(x) for x in (text(c.get('label')), status, text(c.get('where')), text(c.get('summary')))) + ' |')
    lines.append(END)
    return lines


def main():
    args = sys.argv[1:]
    try:
        with open(LIST, encoding='utf-8') as f:
            data = json.load(f)
    except (OSError, ValueError) as e:
        print('docs/capabilities.json could not be read: ' + str(e))
        return 1
    wrong = problems(data)
    if wrong:
        print('docs/capabilities.json has problems; the plugin and the page would claim less for these rows:')
        for w in wrong:
            print('  ' + w)
        return 1
    block = table(data)
    if not args:
        print('\n'.join(block))
        return 0
    with open(README, 'rb') as f:
        raw = f.read().decode('utf-8')
    nl = '\r\n' if '\r\n' in raw else '\n'
    lines = raw.split(nl)
    if lines.count(START) != 1 or lines.count(END) != 1 or lines.index(START) > lines.index(END):
        print('README.md must hold the two marker lines of the capability table once each, start before end:\n  ' + START + '\n  ' + END)
        return 1
    a, b = lines.index(START), lines.index(END)
    if '--check' in args:
        if lines[a:b + 1] != block:
            print('The Status table in README.md is not what docs/capabilities.json makes: run python3 tools/status.py --write')
            return 1
        print('README.md Status table matches docs/capabilities.json (' + str(len(data['capabilities'])) + ' rows)')
        return 0
    if '--write' in args:
        new = nl.join(lines[:a] + block + lines[b + 1:])
        with open(README, 'wb') as f:
            f.write(new.encode('utf-8'))
        print('README.md Status table written from docs/capabilities.json (' + str(len(data['capabilities'])) + ' rows)')
        return 0
    print('use --check or --write (or nothing, to print the table)')
    return 2


if __name__ == '__main__':
    sys.exit(main())
