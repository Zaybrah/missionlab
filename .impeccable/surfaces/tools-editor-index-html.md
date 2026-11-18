---
version: 1
slug: "tools-editor-index-html"
primary_target: "tools/editor/index.html"
related_targets: ["tools/editor/src"]
---

## Scope

The MissionLab scenario editor (`tools/editor/index.html`, built from `tools/editor/src/*`), all three views: Play, Create and Direct. It is in Operate mode.

## Audience and job

The primary user is a first-time host on a second monitor beside the running game, in a dark room. Within seconds they must see which view they are in, what the map shows and the one next step. After that they play a mission, place and change an enemy, and save, without reading the docs. Authors and Game Masters keep every feature, which is now grouped into collapsible inspector sections.

## Constraints

- Ids, `data-tab`, `data-view` and `data-pane` are the script's contract with the markup.
- The canvas colours come from CSS tokens (`readTokens`).
- No npm. The page must still work offline from disk.
- The literals `src="three.min.js"` and `'__ML_PAGE_STAMP__'` stay in the page.
- Long explanations show their first sentence and tuck the rest behind "More", with no wording changed.

## Direction contract

THESIS: A familiar engine-style level editor: an inspector on the left, a tool header over the map, the map filling the rest. It refuses the current tactical-HUD look (condensed uppercase headings, three different "selected" looks, a wall of equal-weight buttons) and its cluttered, always-open column.

OWN-WORLD:
- Graphite in three steps: #1e1f22 map ground, #232428 tool header, #2b2d31 panels, with 1px hairlines #3a3c41.
- One accent blue for selection, current state and the primary action, and nothing else: #2a6fdc as a fill, which gives white text 4.8:1 (the sketch's #3d8bfd failed AA), and #79aefc for text, lines and rings.
- Enemy red #f06a5a; player blue #5ea6ee, a lighter blue than the accent.
- The system UI stack (Segoe UI Variable) at 13px; Cascadia Mono for numbers.
- Icon plus label buttons, drawn in one 1.5px stroke set.
- Inspector sections with a chevron, a title and a count.
- A matching light rendition that follows the OS.

STORY: The host reads the Play / Create / Direct switch and a "Next:" line. They pick a tool from the one-row header, select an enemy in the list or on the map, change it in the "Selected enemy" property grid, and save from the pinned save bar.

FIRST VIEWPORT:
- Left, a 372px inspector:
  - the name "MissionLab" with the subtitle "Scenario editor · unofficial, for OPERATOR";
  - a segmented view switch;
  - the "Next:" callout and the status dots;
  - the underlined tab strip;
  - "Enemies 6" rows, each with a role glyph and its group;
  - the "Selected enemy #1" property grid;
  - the pinned save bar.
- Right: the map with the floating one-row tool header at top left, a status pill at bottom left and the scale bar at bottom right.
- The primary action is "Save file", in blue, at the bottom of the inspector.

FORM: Canon card (the standing exit, not on the ordered list; the user chose it over the roll). Peers that set the craft bar: Unity, Blender, VS Code. seed:43e060f5d49e947e

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Memorable moment

A first-timer never has to ask "where am I?". The view, the next step and the selected thing are always visible, and the selection is in the same blue everywhere.
