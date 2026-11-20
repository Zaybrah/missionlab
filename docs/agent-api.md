# Draft authoring for coding agents (ML-053)

A coding assistant can help build a scenario through the same format, checks and review as the editor. It reads one scenario,
proposes changes as a short list of structured operations, and MissionLab either keeps the proposal for the author or refuses
it with the findings the editor would show. Nothing an agent does changes a scenario file or the running mission: the author
opens the proposal in the editor, reviews its differences, applies it as one undo step and saves through the normal path.

Live Game Master actions (spawning, firing triggers, messages) are not part of this surface. They need a separately designed,
explicitly authorised host workflow.

## How it connects

```
coding agent  --MCP over stdio-->  tools/agent/missionlab_agent.py  --HTTP-->  127.0.0.1:8765/api/agent/...  (the plugin)
```

- The plugin's browser link answers on this computer only (`[LiveLink] Port`, 8765 by default). The routes work in the lobby,
  in the menus and during a mission alike; they only read and write files.
- `tools/agent/missionlab_agent.py` is a thin adapter for the Model Context Protocol: JSON-RPC 2.0 (`initialize`, `ping`,
  `tools/list`, `tools/call`), one message per line on stdin and stdout. It forwards each tool call to the HTTP API with the
  headers the plugin asks for and hands the answer back as it came. It has no rules of its own. Python 3.8+, standard library.
- `python3 tools/agent/missionlab_agent.py --selftest` checks the adapter against a stub server it starts itself.
- `python3 tools/agent/example.py [--file house.json]` is a scripted client: it starts the adapter, reads a scenario, proposes
  a zone trigger with a message and one guard who arrives with it, and runs the bounded validation on the proposal.

An MCP client is configured with the adapter as a stdio server, for example:

```json
{ "mcpServers": { "missionlab": { "command": "python3", "args": ["tools/agent/missionlab_agent.py", "--port", "8765"] } } }
```

## Protections

The agent routes sit behind the browser link's existing rules, and the adapter does not get round any of them:

- the server binds the loopback address only; the adapter connects to `127.0.0.1` and never through a proxy;
- a request with an `Origin` header must come from the plugin's own page, so another website cannot use the API;
- every request that is not a `GET` needs `X-MissionLab: 1`;
- `X-MissionLab-Command: <number>` on a proposal makes a resent request answer from the first one instead of keeping a second
  proposal (the adapter sends one with every change; pass `command` to retry under the same number);
- no route here queues work for the game's thread, so the mission session header is not involved: nothing reaches the mission.

## What is returned

Only the scenario asked for and its metadata. File names are names in `BepInEx/MissionLab/scenarios` only (no folders, `..`
or drive letters). No map, navmesh, recon file, recording, run record or other game file is read or returned; the bounded
validation never runs the scatter check, as that would read the game's map.

## Routes

| Route | What it does |
|---|---|
| `GET /api/agent/scenarios` | Every scenario file: `file`, `id`, `revision`, `hash`, `title`, `readable`, `usable`, `sealed`, `capabilities` (as the catalogue judges them for this build). |
| `GET /api/agent/scenario/<file>` | One scenario: its exact `text` and `hash`, `places` (id, name, pos, shaped), `triggers` (id, name, when, place, after, of, hold, ...), `goals`, `enemies` by `index`, `extraction`, findings and capabilities. |
| `GET /api/agent/capabilities` | The capability list (`docs/capabilities.json`) with each row's status for this build. |
| `POST /api/agent/propose` | `{file, expectedHash, expectedRevision, name, operations, validate, players}`: see below. |
| `GET /api/agent/proposals` | The kept proposals, newest first, each with `stale` and `staleWhy`. |
| `GET /api/agent/proposal/<id>` | `{summary, proposal}`: one kept proposal with whether it is stale now. |
| `POST /api/agent/proposal/<id>/discard` | Forgets a kept proposal. The scenario is not touched. |
| `POST /api/agent/check` | `{file}` or `{proposal}`, with `players` (list), `seeds` (list or count) and `budget` (at most 500): the bounded validation. Its report is returned and not kept. |

The adapter's tools map onto these one to one: `list_scenarios`, `read_scenario`, `read_capabilities`, `propose_draft`,
`list_proposals`, `read_proposal`, `run_checks`.

## A proposal

`expectedHash` is the `hash` that `read_scenario` gave; `expectedRevision` the `revision`. At least one is needed, and an
operation that names an enemy by index needs the hash, as an index only means something in the text it was read from. If the
file has changed since, the proposal is refused with code `stale` (HTTP 409) before anything is applied.

The operations are applied in order to a copy of the scenario read by the strict reader. The file is never written. The result
is then judged by the same rules as everything else:

1. each operation's own values: a missing id is `missing-ref`, a value of the wrong type `wrong-type`, a value the format does
   not have `unknown-value` (`unknown-kind` for `when`), a number out of its range `out-of-range`, all at
   `operations[n]...`;
2. the strict reader on the result's exact text, as at a mission's start: a dangling reference is refused with the reader's own
   code and path (`missing-ref triggers[2].after`, `missing-ref goals[0].succeed`, `bad-area triggers[1].place`, ...);
3. Completion: a change that leaves the mission unfinishable is refused; a problem the scenario already had stays a note;
4. the capability list: an operation may not add a feature whose row is `failing` or `unavailable` (code
   `capability-failing` or `capability-unavailable`, naming the row and its evidence). Hunting and calling enemies are failing
   in 0.26 (`fresh-movement`). An experimental feature is allowed and noted. The author may still use any of them by hand;
5. with `"validate": true`, the bounded validation: completion over squad sizes and seeds, and the scenario's saved rehearsals.
   A failed check refuses the proposal with code `validation-failed`. It runs for at most about ten seconds.

An accepted proposal answers `accepted: true` with its `id`, the proposed `scenario` text and its `hash`, the operations in
words (`changes`), notes and capabilities, and is kept as `BepInEx/MissionLab/proposals/<id>.json` (`missionlab-proposal/1`:
the exact text it was made against and the exact proposed text). It is never written under `scenarios/`. A refusal answers
`accepted: false` with `status`, `why` and `findings`, and nothing is kept.

### Operations

Each is an object with `"op"`:

| op | fields |
|---|---|
| `setTrigger` | `id`, `set: {field: value}` |
| `addTrigger` | `trigger: {when, ...}`; `id` is the next free number unless given. A zone or hold needs `pos` or a `place` (then it sits at the place's spot) |
| `removeTrigger` | `id`. Whatever still names it is refused by the strict reader at its own path; nothing is unhooked quietly |
| `setHold` | `trigger`, `seconds`, `everyone` (optional): a zone or hold becomes a hold of that length |
| `linkPlace` | `trigger`, `place`: the zone or hold uses the named place's painted area |
| `copyPlace` | `trigger`, `place`: the trigger gets its own copy of the place's shape, with no link |
| `addGoal` | `goal: {id, name, succeed, after, activate, fail, required, notify}` |
| `setGoal` | `id`, `set: {...}` (the id itself stays) |
| `removeGoal` | `id` |
| `setExtraction` | `set: {after, goals}` |
| `addEnemy` | `enemy: {pos, role, group, trigger, tag, type, chance, skill, team, minPlayers, scatter, nvg, indoor, defensive, prone, fixed, hvt, vehicle, patrol}` |
| `setEnemy` | `index`, `set: {...}` (needs `expectedHash`) |

Trigger fields: `name when pos radius delay hold after of count tag item chance warn everyone red hunt respawn facePlayer
repeat times rally message power end extraction outcome within place`, as in `docs/scenario.schema.json`.

### Example: Upstairs becomes a 30-second hold once the phone is recovered

```json
{ "file": "house.json", "expectedHash": "fnv1a64:...", "name": "Upstairs hold",
  "operations": [
    { "op": "addTrigger", "trigger": { "when": "hold", "name": "Hold upstairs", "place": 1, "hold": 30, "everyone": true } },
    { "op": "addGoal", "goal": { "id": "upstairs", "name": "Hold the upstairs for 30 seconds", "after": ["phone"], "succeed": 4 } }
  ] }
```

## Review in the editor

The Mission tab's **Agent proposals** lists the kept proposals when the page is opened from the game (it looks again every
ten seconds); without the game, **Open proposal file** opens one from `BepInEx/MissionLab/proposals`. **Review** opens a
proposal as a separate draft and lists its differences from the scenario it was made for, in the words of the staged-change
preview plus its objectives. **Apply to my draft** puts the proposal on the author's own draft as one undo step, and only when
that draft is still exactly the scenario the proposal was made for; otherwise it is refused and nothing changes. A stale
proposal (the file has changed since) cannot be applied. Saving is the normal Save, with all its checks.
