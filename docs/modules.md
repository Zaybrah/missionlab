# Encounter modules (`missionlab-module/1`)

A module is a whole encounter kept for reuse (ML-019): enemies, their patrol routes, the triggers that bring them or that those
wait for, and the triggers' messages. It is measured from an anchor, so it can be put down anywhere, turned, on this map or
another. Inserting one makes an independent copy: nothing links the copy back to the module.

Where modules are kept: in the browser (editor, Triggers tab → Modules), as files you save or open, and, when the editor page
comes from the game, in `BepInEx/MissionLab/modules/<name>.module.json`. They never go under `scenarios/`: the scenario
scanner reads every `*.json` there. Two small examples are in `examples/modules/`.

## The file

```json
{
 "schema": "missionlab-module/1",
 "name": "Two-guard alarm response",
 "notes": "optional text",
 "anchor": { "pos": [0, 0, 0], "yaw": 0 },
 "spawns": [ { "group": 0, "role": "Guard", "pos": [1.5, 0, 2], "yaw": 60, "trigger": 0, "patrol": [] } ],
 "triggers": [ { "id": 1, "when": "after", "after": "@t1", "message": "They are coming", "doors": [{ "pos": "@d1", "locked": false }] } ],
 "bindings": [
  { "kind": "trigger", "ref": "t1", "label": "When the enemy is alerted (trigger 3)" },
  { "kind": "door", "ref": "d1", "label": "Door \"Front\" 6 m from the anchor", "hint": [6, 0, -1] }
 ]
}
```

- `spawns` and `triggers` are written exactly as a scenario writes them (`docs/scenario.schema.json`), with every position
  relative to the anchor: enemy `pos` and `patrol`, zone and hold `pos`, and `area` outlines and holes as `[x, z]` from the
  anchor with the `floor` band relative to the anchor's height. Trigger ids, `after`, `of` and an enemy's `trigger` refer to
  the module's own triggers.
- `anchor` records where the module was saved and is for information: the positions are already relative to it.
- Anything the encounter needs from outside itself is written as `"@<ref>"` where it is used and listed under `bindings`:

| kind | used in | bound to, on insert |
|---|---|---|
| `door` | a door trigger's `pos`; a trigger's `doors[].pos` | one of the map's doors (its hinge); `hint` is where it was, relative |
| `objective` | an intel trigger's `item` | an intel item name, any item (`""`) or `"all"`; `hint` is the old name |
| `place` | `place` on a zone or hold trigger that was exactly a named place, or was linked to one (`link`) | a named place: the trigger takes its anchor and shape; with `link`, a painted place it then links to |
| `trigger` | `after`, `of`, an enemy's `trigger` | another trigger of the mission |
| `extraction` | `trigger` names the module trigger the extraction waited for | yes or no; optional |

- `required` defaults to true, and to false for `extraction`. `ref` is 1 to 32 letters, digits, `_` or `-`.
- `link: true` (place bindings only, ML-040) marks a zone or hold that was *linked* to its place (`"place": id` in the
  scenario) rather than holding a copy of the place's shape. `hint` is the place's name, and the trigger still carries the
  shape as its `area`, relative to the anchor. On insert the author picks a painted place of this map, and the copy links
  to it (its anchor and shape become the place's, and the scenario is written as `missionlab-plan/3`); point places are
  not offered. Or the author chooses "Keep the module's own shape (not linked)": the copy keeps the module's area, moved
  and turned with it. Either way inserting never moves or reshapes a place. Without `link`, or with any value but
  `true`, it is an ordinary place binding: the trigger takes a copy of the chosen place's shape (a point place moves only
  its anchor) and is not linked. Core `ModuleFile` does not look at `link`; only the editor acts on it.

## Inserting

The editor turns the copy by the chosen rotation (clockwise on the map, as facing is) and moves it to the chosen anchor:
positions, facing, patrol routes, trigger anchors and areas all move together. Trigger ids are renumbered above every id in
use, and so are groups, lots and either-sets, so two copies never share a number. A tag counted by the module's own
"enemies down" trigger is renamed when the scenario already uses it.

Until every required binding of a copy is chosen, the scenario cannot be saved or copied, and the Mission check lists what is
missing. These pending bindings live in the editor only: they are not written to the draft or to any file. After inserting,
the editor lists every enemy, route stop and zone anchor that is off the walkable ground of the open map; on another map, that
list and the bindings are what decide whether the module fits.

## The browser link

- `GET /api/modules`: `{"folder", "modules": [{"name", "file", "title", "spawns", "triggers", "bindings", "required", "problem"?}]}`
- `GET /api/modules/<name>`: the module's text.
- `POST /api/modules` `{"name", "text", "replace"}`: kept after a shape check; an existing module with other text is left alone
  (409) unless `replace` is true. Names are reduced to letters, digits, `_` and `-`; files only, never folders.

## Scenario packs (`missionlab-pack/1`, ML-026)

A pack is one file (`<name>.mlpack.json`) to hand a mission to other players: one or more scenarios, optionally modules,
and a manifest. Made in the editor (Create, Mission tab, Packs: Export pack); Core `Pack.Write` gives the same text.

```json
{"schema":"missionlab-pack/1","name":"Clean House quiet raid","made":"2026-11-18","by":"MissionLab 0.26.0",
 "manifest":{"files":[
  {"name":"clean_house_quiet_raid.json","kind":"scenario","id":"sc-cleanhouse-quiet","hash":"fnv1a64:…","bytes":4670},
  {"name":"two_guard_alarm.module.json","kind":"module","hash":"fnv1a64:…","bytes":1574}
 ],
 "requires":{"maps":["suburbv2.bytes"],"modes":["pve"],"mod":"0.26.0","capabilities":["chance","endings","…"]}},
 "files":{
  "clean_house_quiet_raid.json":"{\n  \"schema\": \"missionlab-plan/1\", …",
  "two_guard_alarm.module.json":"{\n \"schema\": \"missionlab-module/1\", …"
 }}
```

- Each file is held as its **exact text, a JSON string** (the v0.26 contract sketched embedded objects; strings were
  chosen instead). The manifest's `hash` is `ScenarioFile.Hash` of that text (FNV-1a 64, line endings ignored) and `bytes`
  its UTF-8 size, so reformatting a pack cannot break them, and an installed file is the author's byte for byte: its
  catalogue and run-record hash equals the manifest's.
- Only `kind` `scenario` (strict-read as at mission start, with enemies) and `module` (the shape check of "The file", Core `ModuleFile`). Navigation
  data, map geometry, replays, recon files and anything else from the game are refused by kind, name and content.
- Names are plain file names: a scenario's as the catalogue saves it, a module's ending in `.module.json`; no folders.
- `requires`: `maps` lists every scenario's map file, `modes` is `["pve"]`, `mod` the version that made it (a newer one is
  a note), `capabilities` every feature word the scenarios use (`Capabilities.Of`). A word this version does not know
  refuses the pack; a feature a scenario uses but the manifest leaves out is an error too.
- Findings use the scenario codes plus `bad-name`, `not-allowed`, `hash-mismatch`, `id-mismatch`, `unlisted`,
  `unsupported`, `bad-module`, `no-enemies` and the note `newer-mod`. Paths: `manifest.files[0].hash`,
  `files.raid.json`, `files.raid.json.spawns[0].yaw`. `tests/fixtures/packs` holds packs both readers must judge alike.

**Import** (Packs: Open pack file) shows before anything is written: every file with the strict reader's findings,
features this version lacks, whether each scenario's map file is the one open here and has the same fingerprint
(otherwise "positions need checking"), and names or ids already in the scenarios or modules folder, with a choice for
each: copy, next revision, or skip. Without the game the folders cannot be seen; Install then opens the scenario in
Create and installs nothing.

`POST /api/pack/install` `{"pack": text, "choices": {"raid.json": "copy"|"revision"|"skip"}}` (files only, worker thread):
the pack is read again and refused whole (422, with findings) when anything is wrong. Nothing is ever written over:
- the same text already there (same name, or a scenario with the same id): nothing to do (`same`);
- no clash: written under its own name (`install`);
- `copy` (the default): a new name (`-copy`, `-copy2` …) when the name is taken, and a new id (`<id>-copy`) when the id is;
- `revision`: the same id with the next revision after both the local and the pack's, under `<name>-r<n>.json`;
  for a module, or a scenario whose id nothing local has, it is a copy;
- `skip`: nothing written.
Scenarios go directly into `scenarios/`, modules into `modules/`. The answer:
`{"ok", "pack", "files": [{"name", "kind", "action", "written", "file", "folder", "id", "revision", "hash", "why"}], "written", "findings"}`.
Installing through the running game has not been tried in the game yet.
