# Backlog status

Where each ticket of `docs/backlog.md` stands. Updated 18 November 2026, build 0.25.0.

"Tested" means an automatic test proves it (`./test.sh`: 571 C# checks and 501 editor checks, or the map tests). "In the game" means it
needs a run in OPERATOR; "co-op" means a run with a friend who has no mod. 0.24.1 to 0.25.0 have not run in the game yet.

## v0.24

In the game so far (17 November, 0.24.0, one player): the trigger engine, a shared wave, two refused missions.
On 18 November (0.26.0, one player, Clean House by night; `docs/findings.md`): readiness all OK and the catalogue right;
the acceptance mission to a success (door stood open, timer repeat and cap, moved phone, extraction held and opened,
tagged enemy down, all-of ending); two shared waves and the game's own ending after the third death; the diagnostic
mission with the inspector following every trigger. Found: messages fired with a success ending were lost (fixed in
0.26.1, not run yet). Not played: painted-area walk, sealed, options, scatter, cards, Game Master save, repeat, other map.

| Ticket | State | Tested | Still open |
|---|---|---|---|
| ML-001 completion path | Built, seen in the game | Chance, transitive, all/any, by hand, missing, circle, target person, 60 seeds against the dice; editor check. In the game: refused twice, the game's own mission ran | Co-op |
| ML-002 shared waves | Built, seen in the game | Staggered deaths, pending not counted, three tries, cap, failed call. In the game: announced, asked, one player seen back | The whole squad dead at once; co-op |
| ML-003 current positions | Built; the call still moved nobody | Order log: "3 accepted, walkable ground at ..." | 0.24.1 follows every order up and tries three ways; needs the next run |
| ML-004 trigger history | Built, seen in the game | Rearm keeps history, repeats, countdown, order of the list, circles, delete and re-make, fire by hand, hold. In the game: every trigger of the feature test | |
| ML-005 failure isolation | Built | Guard: pauses, growth to a minute, independence, recovery | In the game: nothing forces an error there |
| ML-006 command results | Built | Ledger with one result per part (applied, failed, pending, unconfirmed), parts kept for a reload, partial status, mission end closes waiting parts; page: stale, expired, pending, numbers and session sent | In the game: a partial change and a spawn that settles later |
| ML-007 scenario contract | Built | Seven files that must be refused, with the same findings in plugin and editor (now checked by `tests/editor/run.mjs`); text escaping; schema kept in step with the writer; every scenario on the test PC passes | A newer format has nothing to convert from yet, so there is no conversion summary beyond the note for a file without a format line |
| ML-008 resolved run record | Built | Fingerprint; page: list and choice of a run to repeat | In the game: the record itself and a repeated run. Random extras are in the record but were not checked for repeating |
| ML-009 replay events | Built | Removed is not down; requests counted apart; older and damaged recordings; rows carry `who` (scenario number, marker, gun truck), `down` only when the game says dead, `gone` otherwise, end reason, session/run/scenario in the header | In the game: whether a kill is seen as dead before the body is removed; size and time of a long recording |
| ML-010 asset-free tests | Built | `./test.sh`: C# suite (simulated clock: repeats, countdown, message queue, waves, reset; synthetic maps: islands, stacked floors, insertion clearance) and 62 editor checks in Node (refusals equal to the plugin's, editor → C# → editor, awkward text, mission check), also on GitHub | |
| ML-011 co-op proof | Prepared | `examples/clean_house_acceptance.json`, `docs/coop-record.md`, the README Status table | Needs you and a friend without the mod |
| ML-012 packaging | Built | ZIP built and its contents checked, refused when the page is stale or its version differs; build on Windows and Linux with the references checked first; readiness rows incl. version mismatch with the repair step, mode, map data, compatibility; report with an optional scenario and run, listing its contents, without names or folders | The ZIP on a clean game install |

## v0.25

Built in 0.25.0; nothing of it has run in the game yet.

| Ticket | State | Tested | Still open |
|---|---|---|---|
| ML-027 painted trigger areas | Built | Area model and containment in C# and the editor from one fixture (`tests/fixtures/area_points.json`: L-shape, courtyard hole and its edge, floors, stairs, seams, vertical bounds, tolerance); geometry refusals; plan/2 envelope refused by a verbatim copy of the 0.23 reader; brush, eraser, rectangle and polygon give the same shape; undo; live shape edit is one checked change that restarts a hold and keeps history; hold across touching parts vs a real gap; everyone in the union; circles unchanged; 33 µs a frame for four complex areas (`CoreTest --bench-areas`) | In the game and co-op: the L-shaped house, courtyard, floors, stairs, beside an outside wall, everyone/hold; whether the player's position is at the feet on stairs and crouching |
| ML-013 Play / Create / Direct | Built | Catalogue entries (author's squad and estimate apart from measured duration; unknown stays unknown); panes hide placement controls in Play | In the game: choosing and starting the supplied mission from Play |
| ML-014 draft changes | Built | Semantic diff, nothing sent before Apply, invalid batches blocked, revisions saved/listed/opened as a separate draft | In the game: a staged batch applied, retried and partly failed |
| ML-015 trigger inspector | Built | Explain sentences with real values, outcomes (applied/failed/unconfirmed), highlight sets; `examples/clean_house_diagnostic.json` (no circle: examples may not hold one) | In the game: every state on screen, refresh |
| ML-016 rehearsal | Built | Phone starts the countdown, an optional branch cannot remove the way out, player-count variants, a changed scenario fails a saved test by step and trigger | Through the running plugin from the page |
| ML-017 repairs | Built | Snap to walkable ground within bounds, separate enemies, duplicate number; preview, one Undo step, re-check, explanation when nothing fits | |
| ML-018 named places | Built | Overlapping floors stay apart, rename keeps the id, map print change asks for a review, export holds only labels/coordinates/references | Names in replays of a real recording |
| ML-019 encounter modules | Built | Two copies are independent, turning and moving keep positions/facing/routes/areas together, unbound links block export, two examples | Another map: placement on its walkable ground |
| ML-020 three missions | Written | Strict read, Completion for every roll, round trip | Playtests with an unmodded client, measured durations, a first-time host on `docs/first-edit.md` |

## v0.26

Built in 0.26.0; nothing of it has run in the game yet.

| Ticket | State | Tested | Still open |
|---|---|---|---|
| ML-021 sealed missions | Built | Catalogue leaves out positions, counts and coordinates in findings; Play, maps, inspector, replay stay sealed; reveal only in memory (a reload or tab is sealed again); Create and after-run reveal | In the game: host `clean_house_sealed`, live map and Direct while sealed |
| ML-022 host options and quick mission | Built | Apply drops lots/triggers/overrides; same dice whatever the picks; every combination × squad size × rolls checked, counterexamples named, sampled coverage said; picks in the run record and reproduced; generator unchanged for default input (pinned) | In the game: picks at mission start; generating on a real map |
| ML-023 scatter and placement areas | Built | Same inputs and seed give the same spots; never leaves its area or floor; spacing and insertion clearance; declared fallback, never a target; skip that breaks the mission caught; seed sweep lists counterexamples; editor preview; Game Master save keeps authored spots | In the game: scattered enemies on a real map; the sweep through the plugin |
| ML-024 debrief | Built | Every number names its event source; health/shots/doors never invented; runs grouped by scenario and resolved hash; edits apart from deaths; unused content as suggestions; C# and editor give the same text | In the game: whether the `DebriefMessage` lines show before the end screen for an unmodded client; whether `down` is seen before a body is removed |
| ML-025 budget and cards | Built | Spend, rest, refill on a simulated clock; a retry never pays twice; refunds; cards and reasons; budget mode off/on; plain fire refused for a card's trigger | In the game: the distance/view-cone rule against what players see; where manual reinforcements appear |
| ML-026 scenario packs | Built | Manifest lists every file and requirement; game data refused by kind, name and content; hash and id checks; previews findings, missing features, map differences and clashes; never overwrites; C# and editor agree on the fixtures | Installing through the running game |

A Game Master save now carries every authored field it does not edit (ML-008 follow-up; `tests/Pure/SaveTests.cs`).

## Decisions taken while building

- **By-hand triggers on the way out.** A scenario whose extraction waits for a trigger fired by hand is refused unless
  it says `"gameMaster": true` (Mission tab: "This mission needs a Game Master").
- **Another way out counts.** If the extraction's own trigger cannot fire but another trigger that "opens the
  extraction" can, the scenario is accepted.
- **A gate on a trigger that does not exist** was never installed by the plugin; that stays so, with a note in the log.
- **Fire by hand** fires a trigger that is not latched, whatever its condition, also when it sat the mission out.
- **"after", "all" and "any"** ask whether the other trigger has ever fired this mission. A repeating trigger of these
  kinds therefore fires again on every repeat once that is true.
- **A zone given a hold time, or the reverse,** keeps its state; any other change of kind starts the trigger clean.
- **A change sent without a session** (an older page) is still accepted; one sent for another mission is refused.
- **A file with a link to a trigger that does not exist is refused**, also for an enemy or a thing. Before, the editor
  quietly unbound such an enemy, which made it appear from the start.
- **A scenario asked for by name that is refused** leaves the game's own mission; it no longer falls back on a random layout.
- **The run record's fingerprint** is FNV-1a over the file's text: enough to tell "same file", not a security feature.
