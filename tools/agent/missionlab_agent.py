#!/usr/bin/env python3
"""MissionLab's draft-authoring tools for coding agents, over the Model Context Protocol on stdio (ML-053).

A thin adapter: it speaks JSON-RPC 2.0 (initialize, ping, tools/list, tools/call), one message per line, and forwards
every tool call to MissionLab's local HTTP API at http://127.0.0.1:<port>/api/agent/... with the headers the plugin asks
of any change (X-MissionLab: 1 and an X-MissionLab-Command number, so a resent call is carried out once). It adds no
rules of its own: what is valid, stale or refused is decided by the plugin, and its answer is handed back as it came.
Nothing here can touch the running mission; a proposal is kept apart until the author applies and saves it.

    python3 tools/agent/missionlab_agent.py [--port 8765]     # the MCP server on stdin/stdout
    python3 tools/agent/missionlab_agent.py --selftest        # checks itself against a stub server it starts

Python 3.8 or later, standard library only. See docs/agent-api.md.
"""
import argparse
import http.server
import io
import json
import os
import sys
import threading
import urllib.error
import urllib.parse
import urllib.request
import uuid

HOST = '127.0.0.1'                      # loopback only: the plugin binds nothing else, and neither does this
PROTOCOLS = ('2025-06-18', '2025-03-26', '2024-11-05')
SERVER = {'name': 'missionlab', 'title': 'MissionLab draft authoring', 'version': '1'}
TIMEOUT = 30                            # seconds; a proposal with validation takes at most about 10 in the plugin

INSTRUCTIONS = (
    'Draft authoring for MissionLab scenarios (OPERATOR). Read a scenario with read_scenario, then call propose_draft with '
    'its hash as expectedHash and operations that use the ids it lists. The plugin applies them to a copy and checks the '
    'result with the same rules as the editor; an accepted proposal is kept for the author to review and apply. Nothing '
    'changes a scenario file or the running game.')

_OPERATIONS = (
    'Operations, each an object with "op": setTrigger {id, set:{field: value}}, addTrigger {trigger:{when, ...}}, '
    'removeTrigger {id}, setHold {trigger, seconds, everyone?}, linkPlace {trigger, place}, copyPlace {trigger, place}, '
    'addGoal {goal:{id, name, succeed, after?, activate?, fail?, required?, notify?}}, setGoal {id, set}, removeGoal {id}, '
    'setExtraction {set:{after?, goals?}}, addEnemy {enemy:{pos, role?, trigger?, tag?, ...}}, setEnemy {index, set} '
    '(setEnemy needs expectedHash). Trigger fields: name when pos radius delay hold after of count tag item chance warn '
    'everyone red hunt respawn facePlayer repeat times rally message power end extraction outcome within place.')


def _schema(props, required=()):
    return {'type': 'object', 'properties': props, 'required': list(required), 'additionalProperties': False}


_COMMAND = {'type': 'string', 'description': 'Optional command number (letters, digits, - and _). Send the same one again to retry: the plugin answers it from the first try and never does it twice.'}

# name -> (description, input schema, how the call becomes a request)
TOOLS = {
    'list_scenarios': (
        'Every scenario file in BepInEx/MissionLab/scenarios: file, id, revision, hash, title and the capabilities it uses.',
        _schema({}),
        lambda a: ('GET', '/api/agent/scenarios', None)),
    'read_scenario': (
        'One scenario: its exact text and hash, its named places, triggers, goals and enemies (by index), with their ids.',
        _schema({'file': {'type': 'string', 'description': 'The file name, such as "house.json".'}}, ['file']),
        lambda a: ('GET', '/api/agent/scenario/' + urllib.parse.quote(a['file'], safe=''), None)),
    'read_capabilities': (
        'The capability list: what each feature has been seen to do in the game. A proposal may not add a feature that is failing or unavailable.',
        _schema({}),
        lambda a: ('GET', '/api/agent/capabilities', None)),
    'propose_draft': (
        'Proposes changes to one scenario as structured operations. Accepted proposals are kept for the author to review; '
        'a stale hash or revision, an invalid reference or an unsupported capability is refused with findings. ' + _OPERATIONS,
        _schema({
            'file': {'type': 'string'},
            'expectedHash': {'type': 'string', 'description': 'The hash read_scenario gave for the text the operations were written against.'},
            'expectedRevision': {'type': 'integer', 'minimum': 0},
            'name': {'type': 'string', 'description': 'A few words for the author, such as "Upstairs hold".'},
            'operations': {'type': 'array', 'items': {'type': 'object'}, 'minItems': 1, 'maxItems': 64},
            'validate': {'type': 'boolean', 'description': 'Also run the bounded validation: completion over squad sizes and seeds, and the saved rehearsals.'},
            'players': {'type': 'integer', 'minimum': 0, 'maximum': 16},
            'command': _COMMAND,
        }, ['file', 'operations']),
        lambda a: ('POST', '/api/agent/propose', {k: v for k, v in a.items() if k != 'command'})),
    'list_proposals': (
        'The kept proposals, newest first, each with whether its scenario has changed since (stale).',
        _schema({}),
        lambda a: ('GET', '/api/agent/proposals', None)),
    'read_proposal': (
        'One kept proposal: the text it was made against, the proposed text, its changes and findings, and whether it is stale.',
        _schema({'id': {'type': 'string'}}, ['id']),
        lambda a: ('GET', '/api/agent/proposal/' + urllib.parse.quote(a['id'], safe=''), None)),
    'run_checks': (
        'The bounded validation for a scenario file or a kept proposal: the strict reader, completion over squad sizes and seeds, and the saved rehearsals. Never the game\'s map.',
        _schema({
            'file': {'type': 'string'},
            'proposal': {'type': 'string'},
            'players': {'type': 'array', 'items': {'type': 'integer', 'minimum': 1, 'maximum': 16}},
            'seeds': {'description': 'A list of seeds, or a count (1 to n).', 'anyOf': [{'type': 'integer', 'minimum': 1}, {'type': 'array', 'items': {'type': 'integer'}}]},
            'budget': {'type': 'integer', 'minimum': 1, 'maximum': 500},
            'command': _COMMAND,
        }),
        lambda a: ('POST', '/api/agent/check', {k: v for k, v in a.items() if k != 'command'})),
}


class Adapter:
    """Answers JSON-RPC messages; tool calls go to the plugin at HOST:port."""

    def __init__(self, port):
        self.port = port

    def forward(self, method, path, body, command):
        """One request to the plugin: (status, text). Status 0 when it did not answer."""
        headers = {'Accept': 'application/json'}
        data = None
        if method != 'GET':
            headers.update({'Content-Type': 'application/json', 'X-MissionLab': '1', 'X-MissionLab-Command': command or 'a' + uuid.uuid4().hex[:24]})
            data = json.dumps(body, ensure_ascii=False).encode('utf-8')
        req = urllib.request.Request('http://%s:%d%s' % (HOST, self.port, path), data=data, method=method, headers=headers)
        try:
            # Never through a proxy: the plugin answers on this computer only.
            with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(req, timeout=TIMEOUT) as r:
                return r.status, r.read().decode('utf-8', 'replace')
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode('utf-8', 'replace')
        except (urllib.error.URLError, OSError) as e:
            reason = getattr(e, 'reason', e)
            return 0, json.dumps({'error': 'MissionLab is not answering on %s:%d (%s). Start OPERATOR with MissionLab, or pass --port with [LiveLink] Port.' % (HOST, self.port, reason)})

    def call(self, params):
        name = params.get('name') if isinstance(params, dict) else None
        args = params.get('arguments') if isinstance(params, dict) else None
        args = {} if args is None else args
        if name not in TOOLS:
            raise RpcError(-32602, 'Unknown tool: %s' % name)
        if not isinstance(args, dict):
            raise RpcError(-32602, 'arguments is an object')
        schema = TOOLS[name][1]
        for key in schema['required']:
            if not isinstance(args.get(key), str if schema['properties'][key].get('type') == 'string' else list):
                raise RpcError(-32602, '%s needs "%s"' % (name, key))
        method, path, body = TOOLS[name][2](args)
        command = args.get('command') if isinstance(args.get('command'), str) else ''
        status, text = self.forward(method, path, body, command)
        return {'content': [{'type': 'text', 'text': text}], 'isError': status == 0 or status >= 400}

    def handle(self, msg):
        """The answer to one message, or None for a notification."""
        if not isinstance(msg, dict) or msg.get('jsonrpc') != '2.0' or not isinstance(msg.get('method'), str):
            return _error(msg.get('id') if isinstance(msg, dict) else None, -32600, 'not a JSON-RPC 2.0 request')
        has_id = 'id' in msg
        mid, method, params = msg.get('id'), msg['method'], msg.get('params') or {}
        try:
            if method == 'initialize':
                asked = params.get('protocolVersion') if isinstance(params, dict) else None
                result = {'protocolVersion': asked if asked in PROTOCOLS else PROTOCOLS[0],
                          'capabilities': {'tools': {'listChanged': False}}, 'serverInfo': SERVER, 'instructions': INSTRUCTIONS}
            elif method == 'ping':
                result = {}
            elif method == 'tools/list':
                result = {'tools': [{'name': n, 'description': d, 'inputSchema': s} for n, (d, s, _) in TOOLS.items()]}
            elif method == 'tools/call':
                result = self.call(params)
            elif not has_id:
                return None                     # notifications/initialized, notifications/cancelled, ...: nothing to answer
            else:
                raise RpcError(-32601, 'Method not found: %s' % method)
        except RpcError as e:
            return _error(mid, e.code, e.message) if has_id else None
        return {'jsonrpc': '2.0', 'id': mid, 'result': result} if has_id else None

    def serve(self, reader, writer):
        """Newline-delimited JSON-RPC from reader to writer until the input ends."""
        for line in reader:
            line = line.strip()
            if not line:
                continue
            try:
                msg = json.loads(line)
            except ValueError as e:
                answer = _error(None, -32700, 'Parse error: %s' % e)
            else:
                answer = _error(None, -32600, 'batches are not supported') if isinstance(msg, list) else self.handle(msg)
            if answer is not None:
                writer.write(json.dumps(answer, ensure_ascii=False) + '\n')
                writer.flush()


class RpcError(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code, self.message = code, message


def _error(mid, code, message):
    return {'jsonrpc': '2.0', 'id': mid, 'error': {'code': code, 'message': message}}


# ---------------------------------------------------------------- self-test against a stub of the plugin
def selftest():
    seen = []
    answers = {('GET', '/api/agent/scenarios'): (200, {'scenarios': [{'file': 'house.json', 'hash': 'fnv1a64:0123456789abcdef'}]}),
               ('POST', '/api/agent/propose'): (409, {'accepted': False, 'status': 409, 'why': 'the scenario changed since it was read'})}

    class Stub(http.server.BaseHTTPRequestHandler):
        def _answer(self):
            length = int(self.headers.get('Content-Length') or 0)
            body = self.rfile.read(length).decode('utf-8') if length else ''
            # Header names are compared without case, as the plugin does: urllib sends "X-missionlab".
            seen.append({'method': self.command, 'path': self.path, 'headers': {k.lower(): v for k, v in self.headers.items()}, 'body': body})
            status, obj = answers.get((self.command, self.path.split('?')[0]), (404, {'error': 'not found'}))
            data = json.dumps(obj).encode('utf-8')
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        do_GET = do_POST = _answer

        def log_message(self, *a):
            pass

    stub = http.server.ThreadingHTTPServer((HOST, 0), Stub)
    threading.Thread(target=stub.serve_forever, daemon=True).start()
    adapter = Adapter(stub.server_address[1])
    failures, count = [], [0]

    def check(what, ok, more=''):
        count[0] += 1
        if not ok:
            failures.append(what + (' | ' + str(more) if more else ''))

    def session(*msgs):
        out = io.StringIO()
        adapter.serve(io.StringIO(''.join((m if isinstance(m, str) else json.dumps(m)) + '\n' for m in msgs)), out)
        return [json.loads(l) for l in out.getvalue().splitlines() if l.strip()]

    try:
        got = session({'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {'protocolVersion': '2025-06-18', 'capabilities': {}, 'clientInfo': {'name': 'selftest', 'version': '0'}}},
                      {'jsonrpc': '2.0', 'method': 'notifications/initialized'},
                      {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/list'})
        check('initialize and tools/list are answered, the notification is not', [m.get('id') for m in got] == [1, 2], got)
        init = got[0].get('result', {}) if got else {}
        check('initialize names the protocol, the server and the tools capability', init.get('protocolVersion') == '2025-06-18' and init.get('serverInfo', {}).get('name') == 'missionlab' and 'tools' in init.get('capabilities', {}), init)
        tools = {t['name']: t for t in (got[1].get('result', {}).get('tools', []) if len(got) > 1 else [])}
        check('tools/list offers every tool', sorted(tools) == sorted(TOOLS), sorted(tools))
        check('each tool has a description and an object input schema', all(t.get('description') and t.get('inputSchema', {}).get('type') == 'object' for t in tools.values()), '')
        check('an old protocol the client asks for is kept', session({'jsonrpc': '2.0', 'id': 3, 'method': 'initialize', 'params': {'protocolVersion': '2024-11-05'}})[0]['result']['protocolVersion'] == '2024-11-05')

        seen.clear()
        got = session({'jsonrpc': '2.0', 'id': 4, 'method': 'tools/call', 'params': {'name': 'list_scenarios', 'arguments': {}}})
        res = got[0].get('result', {}) if got else {}
        check('list_scenarios is a GET of /api/agent/scenarios', len(seen) == 1 and seen[0]['method'] == 'GET' and seen[0]['path'] == '/api/agent/scenarios', seen)
        check('a GET carries no change headers', len(seen) == 1 and 'x-missionlab' not in seen[0]['headers'], seen)
        check('the answer comes back as it came, not as an error', not res.get('isError') and json.loads(res['content'][0]['text'])['scenarios'][0]['file'] == 'house.json', res)

        seen.clear()
        ops = [{'op': 'setHold', 'trigger': 2, 'seconds': 30}]
        args = {'file': 'house.json', 'expectedHash': 'fnv1a64:0123456789abcdef', 'name': 'Upstairs hold', 'operations': ops, 'command': 'c-selftest-1'}
        got = session({'jsonrpc': '2.0', 'id': 5, 'method': 'tools/call', 'params': {'name': 'propose_draft', 'arguments': args}})
        res = got[0].get('result', {}) if got else {}
        sent = seen[0] if seen else {'headers': {}, 'body': '{}'}
        check('propose_draft is a POST of /api/agent/propose', len(seen) == 1 and sent['method'] == 'POST' and sent['path'] == '/api/agent/propose', seen)
        check('it carries X-MissionLab: 1 and the command number given', sent['headers'].get('x-missionlab') == '1' and sent['headers'].get('x-missionlab-command') == 'c-selftest-1', sent['headers'])
        check('it sends no Origin, so the plugin treats it as a local tool', 'origin' not in sent['headers'], sent['headers'])
        check('the body is the arguments without the command number, unchanged', json.loads(sent['body']) == {k: v for k, v in args.items() if k != 'command'}, sent['body'])
        check('a refusal from the plugin is an error result with its answer', res.get('isError') is True and json.loads(res['content'][0]['text'])['status'] == 409, res)

        seen.clear()
        session({'jsonrpc': '2.0', 'id': 6, 'method': 'tools/call', 'params': {'name': 'run_checks', 'arguments': {'proposal': 'p1'}}})
        check('without a command number one is made up', len(seen) == 1 and seen[0]['headers'].get('x-missionlab-command', '').startswith('a'), seen)
        seen.clear()
        session({'jsonrpc': '2.0', 'id': 7, 'method': 'tools/call', 'params': {'name': 'read_scenario', 'arguments': {'file': 'a b/../c.json'}}})
        check('a file name is sent as one path segment', len(seen) == 1 and seen[0]['path'] == '/api/agent/scenario/a%20b%2F..%2Fc.json', seen)

        got = session({'jsonrpc': '2.0', 'id': 8, 'method': 'tools/call', 'params': {'name': 'start_mission', 'arguments': {}}},
                      {'jsonrpc': '2.0', 'id': 9, 'method': 'tools/call', 'params': {'name': 'read_scenario', 'arguments': {}}},
                      {'jsonrpc': '2.0', 'id': 10, 'method': 'resources/list'},
                      '{not json',
                      {'jsonrpc': '2.0', 'id': 11, 'method': 'ping'})
        codes = [m.get('error', {}).get('code') for m in got]
        check('an unknown tool, a missing argument, an unknown method and broken JSON get their JSON-RPC errors', codes == [-32602, -32602, -32601, -32700, None], codes)
        check('ping is answered', got[-1].get('result') == {} and got[-1].get('id') == 11, got[-1] if got else '')

        down = Adapter(_free_port())
        res = down.handle({'jsonrpc': '2.0', 'id': 12, 'method': 'tools/call', 'params': {'name': 'list_proposals', 'arguments': {}}})['result']
        check('with the game not running the call is an error result that says so', res['isError'] is True and 'not answering' in res['content'][0]['text'], res)
    finally:
        stub.shutdown()
        stub.server_close()
    for f in failures:
        print('FAIL: ' + f)
    print('selftest: %d checks, %d failed' % (count[0], len(failures)) if failures else 'selftest: all %d checks passed' % count[0])
    return 1 if failures else 0


def _free_port():
    import socket
    s = socket.socket()
    s.bind((HOST, 0))
    port = s.getsockname()[1]
    s.close()
    return port


def main():
    p = argparse.ArgumentParser(description='MissionLab draft-authoring tools over MCP (stdio).')
    p.add_argument('--port', type=int, default=int(os.environ.get('MISSIONLAB_PORT') or 8765), help='[LiveLink] Port of the game (default 8765, or MISSIONLAB_PORT)')
    p.add_argument('--selftest', action='store_true', help='check the adapter against a stub server it starts, then exit')
    a = p.parse_args()
    if a.selftest:
        return selftest()
    for stream in (sys.stdin, sys.stdout):
        try:
            stream.reconfigure(encoding='utf-8', newline='\n')
        except (AttributeError, ValueError):
            pass
    Adapter(a.port).serve(sys.stdin, sys.stdout)
    return 0


if __name__ == '__main__':
    sys.exit(main())
