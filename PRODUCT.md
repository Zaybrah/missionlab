# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

- **Primary: a first-time host.** An OPERATOR player who hosts PvE missions for a small co-op squad. They open the page between missions to pick what the next mission uses, start it, and maybe change a few enemies. They have never read the docs. When needs conflict, this person wins.
- **Scenario authors.** They build missions in Create: enemies, routes, triggers, painted areas, doors, rehearsals, revisions, modules and packs. These are long sessions.
- **Game Masters.** They direct a running mission from Direct: messages, lights, orders, action cards, firing triggers, staged changes.

## Product Purpose

MissionLab is a free, unofficial mod toolkit for OPERATOR (Unity IL2CPP, BepInEx 6). The scenario editor is its one screen. It lets a host choose, make and direct custom PvE missions on the game's real maps. Every enemy, route and trigger is checked by the same rules the plugin runs in the game.

Success: someone who has never used it can open the page, understand where they are, play a mission, place and change an enemy, and save, all without reading the docs. Power users lose nothing.

## Positioning

The editor works on the game's own walkable surface (the navmesh files the game ships, read locally). It shares its strict reader, mission check, trigger engine and rehearsal with the plugin. It never claims more than was seen in the game: untested features are labelled as untested, and measured facts are kept apart from the author's estimates.

## Operating Context

- Usually open on a **second monitor beside the running game, often in a dark room**. The host glances at it between and during missions.
- There are two ways to run the page:
  - **Live:** served by the game at `http://localhost:8765` while the mod runs. It has the mission catalogue, Direct and saving into `BepInEx/MissionLab/scenarios/`.
  - **Offline:** `tools/editor/index.html` opened from disk. It has an example map, open and save files, and no game.
- The page has three views:
  - **Play:** choose and start a mission.
  - **Create:** make or change a scenario, through the Map, Enemies, Triggers, World and Mission tabs.
  - **Direct:** the running mission.
- The map canvas (2D top-down, or 3D fly-through) is the centre of every view.
- Desktop browsers are the norm. The link only accepts loopback, so phones are rare. The page must still not break on a narrow window.

## Capabilities and Constraints

- The editor is one offline HTML file, built from `tools/editor/src/*` by `tools/editor/build.py`. It has no npm and no build tools beyond that script. `three.min.js` is vendored, and the literals `src="three.min.js"` and `'__ML_PAGE_STAMP__'` must stay in the page.
- The canvas reads its colours from CSS custom properties (`readTokens` in `24-drawing.js`: `--paper --panel --ink --ink-2 --line --ground-lo --ground-hi --enemy --friend --warn --place`, plus `--mono`).
- Element ids and the `data-tab`/`data-view`/`data-pane` attributes are the script's contract with the markup.
- Terms the page uses: scenario, mission, trigger, zone, place, placement area, module, pack, revision, rehearsal, sealed, host options, action cards, Game Master.
- Never touch PvP. Ship no game files, and nothing derived from them.

## Brand Commitments

- The name is "MissionLab". It is unofficial, and it is not made or endorsed by Vector Interactive.
- The voice is plain everyday English: say what happens and what it means for the player, say what is not known, and never oversell.
- Enemies are red and players or friendly things are blue, on the map and in the panel.
- The visual standard is a dark pro editor, in the tradition of Unity, Blender and VS Code. It is dark by default, with a matching light version for systems set to light. The user chose this over more distinctive directions.

## Evidence on Hand

- Three shipped missions for Clean House in `examples/`. None has been playtested, so the squad sizes and minutes in them are author estimates.
- An invented example compound loads in the offline page. It is not a real map.
- There are no testimonials, user counts or benchmarks; do not fabricate them.

## Product Principles

1. Show where you are and the one next step before showing everything that is possible.
2. The map is the work; the panel serves it.
3. Say what is known, what is guessed and what is untested, in words. Colour only repeats what the words say.
4. Nothing is lost for power users: every feature stays reachable, and density is allowed where the task needs it.
5. If something fails, the mission and the user's draft stay intact.

## Accessibility & Inclusion

The page uses semantic tabs and labelled controls, and state is shown by words, not only by colour. Keep keyboard use, visible focus and reduced-motion support. No specific standard has been set.
