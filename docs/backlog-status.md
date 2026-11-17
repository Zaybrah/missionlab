# Backlog status

Where each ticket of `docs/backlog.md` stands. Updated 17 November 2026, build 0.24.3.

"Tested" means an automatic test proves it (`./test.sh`: the C# suite and the editor checks, or the map tests). "In the game" means it
needs a run in OPERATOR; "co-op" means a run with a friend who has no mod. 0.24.1 to 0.24.3 have not run in the game yet.

## v0.24

In the game so far (17 November, 0.24.0, one player): the trigger engine, a shared wave, two refused missions.

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

## v0.25 and v0.26

Not started. ML-027 (painted trigger areas) comes first in v0.25; its area model is to be agreed with ML-007.
One piece of ML-015 is already there: the page shows where each trigger stands in the running mission
(waiting, being held, counting down, fired, armed again later, done repeating, sat out, in a circle).

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
