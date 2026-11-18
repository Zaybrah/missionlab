---
name: MissionLab Scenario Editor
description: A dark, engine-style level editor for OPERATOR scenarios; the map fills the screen and an inspector column holds the controls.
colors:
  paper: "#1e1f22"
  well: "#1e1f22"
  panel: "#2b2d31"
  bar: "#232428"
  head: "#303237"
  raise: "#35373c"
  raise-hi: "#3f4147"
  ink: "#e6e6e6"
  ink-2: "#a3a6ab"
  line: "#3a3c41"
  line-hi: "#4d5056"
  ground-lo: "#2e3136"
  ground-hi: "#5d646e"
  wall: "#9aa0a8"
  accent: "#2a6fdc"
  accent-ink: "#ffffff"
  accent-text: "#79aefc"
  accent-soft: "#2f4a73"
  enemy: "#f06a5a"
  friend: "#5ea6ee"
  warn: "#e0a84a"
  place: "#b892e8"
  ok: "#4fb37a"
  light-paper: "#e4e7eb"
  light-well: "#ffffff"
  light-panel: "#f6f7f9"
  light-bar: "#eceef1"
  light-head: "#e7e9ed"
  light-raise: "#ffffff"
  light-raise-hi: "#eef0f3"
  light-ink: "#1c1f24"
  light-ink-2: "#565d68"
  light-line: "#d6dae0"
  light-line-hi: "#b7bdc6"
  light-ground-lo: "#f4f5f7"
  light-ground-hi: "#9aa5b1"
  light-wall: "#39424c"
  light-accent: "#1f6fe5"
  light-accent-text: "#1a5fc7"
  light-accent-soft: "#d8e6fc"
  light-enemy: "#bb3825"
  light-friend: "#1c6dcc"
  light-warn: "#96600a"
  light-place: "#7a4bb0"
  light-ok: "#2c8a52"
typography:
  headline:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, -apple-system, Helvetica Neue, Arial, sans-serif"
    fontSize: "16px"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "0.005em"
  title:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, -apple-system, Helvetica Neue, Arial, sans-serif"
    fontSize: "12.5px"
    fontWeight: 600
  subtitle:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, -apple-system, Helvetica Neue, Arial, sans-serif"
    fontSize: "12px"
    fontWeight: 600
    lineHeight: 1.3
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, -apple-system, Helvetica Neue, Arial, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.45
  body-small:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, -apple-system, Helvetica Neue, Arial, sans-serif"
    fontSize: "12.5px"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, -apple-system, Helvetica Neue, Arial, sans-serif"
    fontSize: "11.5px"
    fontWeight: 500
  mono:
    fontFamily: "Cascadia Mono, Cascadia Code, Consolas, ui-monospace, SF Mono, Menlo, monospace"
    fontSize: "12px"
    fontWeight: 400
    fontFeature: "tnum"
rounded:
  sm: "4px"
  md: "5px"
  lg: "6px"
  pill: "999px"
spacing:
  xs: "4px"
  sm: "6px"
  md: "8px"
  lg: "12px"
  xl: "16px"
components:
  button:
    backgroundColor: "{colors.raise}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    padding: "4px 12px"
    height: "28px"
  button-hover:
    backgroundColor: "{colors.raise-hi}"
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.accent-ink}"
    rounded: "{rounded.sm}"
    padding: "4px 12px"
    height: "28px"
  button-danger:
    backgroundColor: "{colors.raise}"
    textColor: "{colors.enemy}"
    rounded: "{rounded.sm}"
  input:
    backgroundColor: "{colors.well}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    padding: "4px 8px"
    height: "28px"
  segmented:
    backgroundColor: "{colors.bar}"
    textColor: "{colors.ink-2}"
    rounded: "{rounded.md}"
    padding: "5px 8px"
  segmented-selected:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.accent-ink}"
  view-switch:
    backgroundColor: "{colors.well}"
    textColor: "{colors.ink-2}"
    rounded: "{rounded.lg}"
    padding: "2px"
  tab:
    backgroundColor: "{colors.bar}"
    textColor: "{colors.ink-2}"
    padding: "9px 8px 7px"
  section-header:
    backgroundColor: "{colors.head}"
    textColor: "{colors.ink}"
    typography: "{typography.title}"
    height: "32px"
  list-row-selected:
    backgroundColor: "{colors.accent-soft}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
  chip:
    textColor: "{colors.ink-2}"
    typography: "{typography.label}"
    rounded: "{rounded.pill}"
    padding: "0 8px"
  tool-header:
    backgroundColor: "{colors.bar}"
    rounded: "{rounded.lg}"
    padding: "3px"
  float-panel:
    backgroundColor: "{colors.panel}"
    rounded: "{rounded.lg}"
    padding: "6px"
---

# Design System: MissionLab Scenario Editor

## Overview

**Creative North Star: "The Engine Workbench"**

The editor follows the category standard for level editors, in the line of Unity, Blender and VS Code. The map takes the whole screen apart from one 372px inspector column on the left. A one-row tool header floats over the map at top left, and a status pill sits at bottom left. The page itself never scrolls; only the inspector does. It is dark by default, because it usually sits beside the running game. A matching light rendition follows the system setting, and `data-theme` can force either.

It is dense and quiet: 13px system UI text, graphite surfaces three steps apart, 1px hairlines, and a single blue that means "selected, current or primary". Hierarchy comes from weight, space and header bands, not from boxes or shadows. It replaces the earlier tactical-HUD look with its condensed uppercase headings, IBM Plex, light default and three different "selected" looks.

**Key Characteristics:**
- Graphite surfaces separated by tone and 1px hairlines, not shadows.
- One accent blue for selection, current state and the primary action only.
- The system UI face at 13px, with Cascadia Mono for numbers, coordinates and file text.
- Inspector sections fold behind a full-width header band with a chevron, a title and a count.
- Icon-plus-label tools drawn in one 16px, 1.5px-stroke icon set.
- Map colours come from the same tokens as the interface.

## Colors

Neutral graphite with one working blue and a small set of map-meaning hues; every role has a light-scheme counterpart (the `light-` tokens).

### Primary
- **Working Blue** (`accent`): fills for the selected item, the current view, the pressed tool mode and the primary action ("Save file"). White on it is 4.8:1.
- **Working Blue as Text** (`accent-text`): the accent for text, lines and focus rings on the dark ground: the active tab underline, the pressed tool's inset ring and icon stroke, focus outlines, drop-zone and open-revision borders.
- **Selected Row** (`accent-soft`): the background of the current row in enemy, place, player, catalogue and inspector lists.

### Secondary
These are meaning colours, shared by the map and the inspector.
- **Enemy Red** (`enemy`): enemies, their routes, problems, failed outcomes and danger buttons.
- **Player Blue** (`friend`): players, insertion points, zones and "live" status. It is lighter than the accent so it is never mistaken for selection.
- **Caution Amber** (`warn`): something to check: warnings, untested capabilities, review boxes (as a 7–9% tint with a 55–60% border).
- **Place Violet** (`place`): named places.
- **Pass Green** (`ok`): saved, passed.

### Neutral
- **Map Ground** (`paper`): around the walkable surface, and the page background.
- **Well** (`well`): inputs, sunken fields, the view switch track, trigger blocks and cards.
- **Tool Bar** (`bar`): the tool header, the tab strip, the "Next:" callout and segmented control tracks.
- **Panel** (`panel`): the inspector and floating map controls.
- **Section Head** (`head`): the header band of each inspector section.
- **Raised** (`raise`, `raise-hi`): buttons at rest and hovered; also hover fills on rows and tabs.
- **Ink** (`ink`, `ink-2`): primary and secondary text; `ink-2` also strokes the tool icons and chevrons at rest.
- **Hairline** (`line`, `line-hi`): dividers, and the stronger border for controls.
- **Map Ground Ramp** (`ground-lo` to `ground-hi`): the canvas interpolates ten shades between these for walkable ground from lowest to highest.
- **Wall** (`wall`): borders nobody can cross, on the map.

### Named Rules
**The One Blue Rule.** The accent fills only selection, the current view or tool, and the primary action. Nothing decorative is blue.

**The Shared Palette Rule.** The map canvas reads `paper`, `panel`, `ink`, `ink-2`, `line`, `ground-lo`, `ground-hi`, `wall`, `enemy`, `friend`, `warn`, `place` and `accent-text` from CSS (`readTokens` in 24-drawing.js). A new map colour is a new token, never a literal in the drawing code. `ground-lo` and `ground-hi` must stay `#rrggbb`, because the ramp is computed from them.

**The Words First Rule.** Colour repeats a state; it never carries it alone. Failed, cancelled, unavailable and warning states also say so in words, a dashed border or a badge.

## Typography

**Body Font:** Segoe UI Variable Text (with Segoe UI, system-ui, -apple-system, Helvetica Neue, Arial)
**Label/Mono Font:** Cascadia Mono (with Cascadia Code, Consolas, ui-monospace, SF Mono, Menlo)

**Character:** The operating system's own interface face, set small and tight like an engine's inspector, with a monospace for anything you might compare digit by digit.

### Hierarchy
- **Headline** (600, 16px, 1.2): the "MissionLab" name at the top of the inspector only.
- **Title** (600, 12.5px): inspector section headers.
- **Subtitle** (600, 12px, 1.3, in `ink-2`): sub-headings inside a section.
- **Body** (400, 13px, 1.45): the base for everything.
- **Body Small** (400, 12.5px): hints, list rows, callouts, findings.
- **Label** (500, 11.5px): chips, badges, list metadata.
- **Mono** (12px, tabular figures): number inputs (12.5px), coordinates, counts, file paths, list numbers and pasted file text (11.5px).

### Named Rules
**The Sentence Case Rule.** Headings are sentence case at normal tracking. No uppercase, no condensed display face.

**The Tabular Number Rule.** Counts, coordinates, costs and numeric inputs use tabular figures, mostly in the mono face.

## Layout

A two-column grid: a 372px inspector, then the map at `minmax(0, 1fr)`. The body is `overflow: hidden` at full height; the inspector scrolls inside itself and its children never shrink. Inside the inspector, content sits 16px from the edges; sections stack with 10px gaps between their parts and 16px below. Form fields use a 118px label column and a flexible value column with an 8px gap. The common spacing steps are 4, 6, 8, 12 and 16px.

The inspector shows one part at a time. A segmented Play / Create / Direct switch picks the view; in Create, a sticky underlined tab strip picks among Map, Enemies, Triggers, Doors, Mission and Replay. The save bar is pinned to the foot of Create when the window is at least 680px tall.

Floating map controls sit 12px from the map edges. The tool header shortens with the map's width (a container query): below 1030px it drops the minor tools, below 970px the floor read-out, below 870px the tool names.

At 760px wide and below, the layout stacks: the map takes the top 46%, the inspector the bottom 54%, the save bar unpins, controls move to 8px insets and the tool header wraps onto rows without separators.

## Elevation & Depth

Flat. Depth is tone and hairline: the map ground is darkest, the tool bar one step up, the panels another, and every edge is a 1px line. Floating map controls get the hairline (`line-hi`) and no shadow.

### Shadow Vocabulary
- **Pinned save bar** (`box-shadow: 0 -8px 16px -12px rgb(0 0 0 / .5)`): only while the save bar is pinned, to show content scrolling under it.
- **Pressed tool ring** (`box-shadow: inset 0 0 0 1px var(--accent-text)`): a ring, not a lift, on the active tool.
- **Row divider** (`box-shadow: 0 -1px 0 var(--line)`): a hairline between inspector list rows.

### Named Rules
**The Hairline Rule.** A floating surface is separated by one 1px border. Drop shadows are reserved for the pinned save bar.

## Shapes

Small, even corners: 4px on buttons, inputs, list rows and the status pill; 5px on segmented controls; 6px on the floating controls, the view switch, callouts, cards and review boxes; full pills (999px) for chips, badges and costs; 10px only on the drag-and-drop zone. Enemy roles have their own glyphs, shared by the list and the map: a square guard, a round wanderer, a diamond patrol and a wide bar for a truck.

Icons are a 16px sprite drawn with a 1.5px round-capped, round-joined stroke and no fill; routes and zones use dashed strokes. The disclosure chevron is a 12px mask from the same stroke set and turns −90° when shut.

## Components

### Buttons
- **Shape:** gently squared (4px), at least 28px tall, 1px `line-hi` border, medium weight.
- **Default:** `raise` fill, `ink` text; `raise-hi` on hover, `line` when pressed; disabled at 45% opacity.
- **Primary:** `accent` fill and border, `accent-ink` text, semibold; hover darkens the accent by 14% towards black. One per context; "Save file" is the main one.
- **Danger:** default fill with `enemy` text; hover tints the border towards red.
- **Transitions:** background, border and text colour change over 0.12s ease-out, only when motion is allowed.

### Segmented controls
- **Style:** a `bar` track with a `line-hi` border and 5px corners; options in `ink-2`, split by hairlines.
- **State:** hover lifts to `raise` and `ink`; the pressed option takes the `accent` fill with white text.
- **View switch:** the Play / Create / Direct switch is the same idea on a `well` track with 2px padding and 4px inner corners.

### Tabs
- **Style:** a sticky `bar` strip with hairlines above and below; tabs in `ink-2`, medium weight.
- **State:** the selected tab turns `ink` with a 2px `accent-text` underline. A count badge (mono 11px on `line`) follows the name when there is something to count.

### Chips
- **Style:** a pill outline in `line-hi`, 11.5px `ink-2` text. Warning and info chips take `warn` or `friend` text with a 60% border of the same colour. An untested chip has a dashed border.

### Inputs / Fields
- **Style:** `well` fill, 1px `line` border, 4px corners, 28px tall; `line-hi` border on hover. Number inputs use the mono face at 12.5px.
- **Focus:** a 2px `accent-text` outline drawn inside the border, and the border turns `accent-text`. Elsewhere, focus is a 2px `accent-text` outline 1px outside.
- **Read-only:** transparent fill and `ink-2` text. Checkboxes, radios and ranges use the accent as their native colour.

### Navigation: the tool header
- **Style:** a floating `bar` strip with a hairline, 6px corners and 3px padding, in one row. On the left, a 2D / 3D mode pair on a `well` track; then thin separators between groups of tools.
- **Tools:** transparent buttons with a 16px icon in `ink-2` and a 12.5px name; hover fills `raise` and darkens the icon to `ink`.
- **Pressed tool:** a 26% accent tint with a 1px inset `accent-text` ring, and the icon stroked in `accent-text`.

### Inspector sections (signature)
- **Header band:** each section opens with a full-width 32px `head` band, a hairline under it and a 12.5px semibold title. The band is a button: a chevron on the left, the title, and a count in `ink-2` pushed to the right with tabular figures. Clicking it folds the section down to the band (32b-panel.js).
- **Body:** 16px side padding, 10px gaps, and a hairline at the foot. No card around it.
- **Long explanations:** a hint shows its first sentence and a dotted-underlined "More" link in `ink` reveals the rest.
- **Lists:** rows 28px tall with 4px corners, `raise` on hover and `accent-soft` when current.

### Callouts and review boxes
- **"Next:" callout:** a `bar` box with a hairline and 6px corners, 12.5px text, the "Next:" in semibold `ink`.
- **Status strip:** small definition pairs, each led by a 7px dot in `line-hi`, `ok`, `warn` or `friend`.
- **Review and repair boxes:** a 7–9% amber tint with a 55–60% amber border and 6px corners.

### Floating map controls
- **Style:** `panel` fill, `line-hi` hairline, 6px corners, 6px padding. The status pill is a 92% `bar` with 4px corners and mono coordinates.

## Do's and Don'ts

### Do:
- **Do** take every colour from a token, in the interface and on the map canvas, and give each token its light counterpart in both light blocks.
- **Do** keep the accent for selection, the current view or tool, and the primary action.
- **Do** show a state in words, a badge or a dashed border as well as in colour.
- **Do** keep the page a single screen: the map fills the rest and only the inspector scrolls.
- **Do** draw new icons at 16px in the 1.5px round stroke set, with no fill.
- **Do** open new inspector sections with the folding header band: chevron, title, count.

### Don't:
- **Don't** use condensed uppercase headings or a display face; headings are sentence-case system UI.
- **Don't** give "selected" more than one look: an accent fill for controls, `accent-soft` for rows, an inset `accent-text` ring for tools.
- **Don't** use the accent blue for players or zones; that is `friend`.
- **Don't** lift floating controls with shadows; use the hairline.
- **Don't** wrap sections in cards; hierarchy comes from header bands, weight and space.
