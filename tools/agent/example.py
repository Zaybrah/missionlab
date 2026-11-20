#!/usr/bin/env python3
"""A scripted agent client (ML-053): creates and validates one supported encounter through the MCP adapter.

It starts tools/agent/missionlab_agent.py, speaks MCP to it over stdio exactly as a coding agent would, reads one scenario,
and proposes a zone trigger with a message and one guard who arrives with it: a trigger, a message and an enemy brought in
by a trigger are all supported in the capability list. The plugin checks the proposal with the editor's rules and keeps it
in BepInEx/MissionLab/proposals; the bounded validation then runs on it. Nothing changes a scenario file or the running
game: the author reviews the proposal on the editor's Mission tab (Agent proposals), applies it and saves it.

    python3 tools/agent/example.py [--port 8765] [--file house.json]

Needs OPERATOR running with MissionLab (any menu will do) and one scenario in BepInEx/MissionLab/scenarios.
"""
import argparse
import itertools
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


class Client:
    """A minimal MCP client over the adapter's stdin and stdout."""

    def __init__(self, port):
        self.proc = subprocess.Popen([sys.executable, os.path.join(HERE, 'missionlab_agent.py'), '--port', str(port)],
                                     stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding='utf-8', bufsize=1)
        self.ids = itertools.count(1)

    def request(self, method, params=None):
        mid = next(self.ids)
        self.proc.stdin.write(json.dumps({'jsonrpc': '2.0', 'id': mid, 'method': method, 'params': params or {}}) + '\n')
        self.proc.stdin.flush()
        line = self.proc.stdout.readline()
        if not line:
            raise SystemExit('the adapter stopped without answering')
        answer = json.loads(line)
        if 'error' in answer:
            raise SystemExit('%s failed: %s' % (method, answer['error'].get('message')))
        return answer['result']

    def notify(self, method):
        self.proc.stdin.write(json.dumps({'jsonrpc': '2.0', 'method': method}) + '\n')
        self.proc.stdin.flush()

    def tool(self, name, **arguments):
        """The plugin's answer as an object, and whether the plugin refused it."""
        result = self.request('tools/call', {'name': name, 'arguments': arguments})
        text = result['content'][0]['text']
        try:
            return json.loads(text), bool(result.get('isError'))
        except ValueError:
            return {'error': text}, True

    def close(self):
        self.proc.stdin.close()
        self.proc.wait(timeout=10)


def say_findings(answer):
    for f in answer.get('findings', []):
        print('   %s %s %s: %s' % (f.get('level'), f.get('code'), f.get('path') or '-', f.get('text')))


def main():
    p = argparse.ArgumentParser(description='Create and validate one supported encounter through the MissionLab agent adapter.')
    p.add_argument('--port', type=int, default=int(os.environ.get('MISSIONLAB_PORT') or 8765))
    p.add_argument('--file', help='the scenario file to propose against (default: the first usable one)')
    a = p.parse_args()

    c = Client(a.port)
    try:
        info = c.request('initialize', {'protocolVersion': '2025-06-18', 'capabilities': {}, 'clientInfo': {'name': 'missionlab-example', 'version': '1'}})
        c.notify('notifications/initialized')
        print('Connected to %s (MCP %s).' % (info['serverInfo']['name'], info['protocolVersion']))

        listed, bad = c.tool('list_scenarios')
        if bad:
            raise SystemExit('The scenarios could not be listed: %s' % listed.get('error'))
        usable = [s for s in listed.get('scenarios', []) if s.get('usable')]
        pick = next((s for s in usable if s['file'] == a.file), None) if a.file else (usable[0] if usable else None)
        if pick is None:
            raise SystemExit('No usable scenario%s in BepInEx/MissionLab/scenarios.' % (' named ' + a.file if a.file else ''))

        scenario, bad = c.tool('read_scenario', file=pick['file'])
        if bad:
            raise SystemExit('The scenario could not be read: %s' % scenario.get('error'))
        enemies = scenario.get('enemies', [])
        if not enemies:
            raise SystemExit('%s has no enemies to place the encounter beside.' % pick['file'])
        # Spots the author already uses are walkable ground: the zone goes round the first enemy, the newcomer where the
        # last one stands (a metre aside when there is only one).
        centre = enemies[0]['pos']
        spot = enemies[-1]['pos'] if len(enemies) > 1 else [centre[0] + 1.0, centre[1], centre[2]]
        new_id = max([t['id'] for t in scenario.get('triggers', [])] or [0]) + 1
        operations = [
            {'op': 'addTrigger', 'trigger': {'id': new_id, 'when': 'zone', 'name': 'Agent example: contact', 'pos': centre, 'radius': 6,
                                            'message': 'Movement nearby.'}},
            {'op': 'addEnemy', 'enemy': {'pos': spot, 'role': 'Guard', 'trigger': new_id, 'tag': 'agent-example'}},
        ]
        print('Proposing for %s (revision %s, %s): zone %d with one guard.' % (pick['file'], scenario['revision'], scenario['hash'], new_id))
        answer, bad = c.tool('propose_draft', file=pick['file'], expectedHash=scenario['hash'], expectedRevision=scenario['revision'],
                             name='Agent example encounter', operations=operations, validate=True)
        if bad or not answer.get('accepted'):
            print('Refused: %s' % (answer.get('why') or answer.get('error')))
            say_findings(answer)
            return 1
        print('Accepted as %s (kept in %s):' % (answer['id'], answer['kept']))
        for change in answer.get('changes', []):
            print(' - ' + change)
        say_findings(answer)

        checked, bad = c.tool('run_checks', proposal=answer['id'], players=[1, 2, 4], seeds=5)
        if bad:
            print('The check did not run: %s' % checked.get('error'))
            return 1
        report = checked['report']
        print('Validation: %s. %s' % (report.get('verdict'), report.get('covers', '')))
        for check in report.get('checks', []):
            print(' - %s: %s%s' % (check.get('name'), check.get('state'), (' (' + check['why'] + ')') if check.get('why') else ''))
        print('Review it in the editor: Mission tab, Agent proposals. Nothing changes until you apply and save it.')
        return 0 if report.get('verdict') != 'failed' else 1
    finally:
        c.close()


if __name__ == '__main__':
    sys.exit(main())
