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
| `place` | `place` on a zone or hold trigger that was exactly a named place | a named place: the trigger takes its anchor and shape |
| `trigger` | `after`, `of`, an enemy's `trigger` | another trigger of the mission |
| `extraction` | `trigger` names the module trigger the extraction waited for | yes or no; optional |

- `required` defaults to true, and to false for `extraction`. `ref` is 1 to 32 letters, digits, `_` or `-`.

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
