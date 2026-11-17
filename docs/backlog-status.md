# Backlog status

Where each ticket of `docs/backlog.md` stands. Updated 17 November 2026, build 0.24.0.

"Tested" means an automatic test proves it (`./test.sh`, the map tests, or the editor tests). "In the game" means it
needs a run in OPERATOR; "co-op" means a run with a friend who has no mod. Nothing below was run in the game yet:
0.23 and 0.24 are both waiting for their first mission.

## v0.24

| Ticket | State | Tested | Still open |
|---|---|---|---|
| ML-001 completion path | Built | Chance, transitive, all/any, by hand, missing, circle, target person, 60 seeds against the dice; editor check | In the game: a refused scenario leaves the game's own mission untouched |
| ML-002 shared waves | Built | Staggered deaths, pending not counted, three tries, cap, failed call, mixed back-and-dead | In the game: the wave itself; the whole squad dead at once; co-op |
| ML-003 current positions | Built | none automatic: it reads the game's navigation agent | In the game: an enemy that walked into the call's distance is called; co-op |
| ML-004 trigger history | Built | Rearm keeps history, timer and condition repeats, countdown, order of the list, circles, delete and re-make, restart, fire by hand, hold | In the game: one mission with the feature test (the engine replaced the old code) |
| ML-005 failure isolation | Built | Guard: pauses, growth to a minute, independence, recovery, valid JSON | In the game: nothing forces an error there; mode change and map change clean-up |
| ML-006 command results | Built | Ledger: once only, repeat gets the first answer, bounded; page: stale, expired, pending, numbers and session sent | In the game: a real slow frame; the per-operation receipt for a batch is one result for the whole batch so far |
| ML-007 scenario contract | Not started | | JSON Schema, field paths for wrong types, future versions refused, proper text escaping |
| ML-008 resolved run record | Not started | | |
| ML-009 replay events | Not started | | An enemy re-made by an edit still reads as "down" |
| ML-010 asset-free tests | Partly | `./test.sh`: 130 checks without a game file; fixtures; examples checked; CI workflow | Editor tests still live outside the repository and need local game files; synthetic map; editor to C# round trip |
| ML-011 co-op proof | Not started | | Needs you and a friend; the record table is in the backlog |
| ML-012 packaging | Not started | | Release ZIP, 3D library served locally, readiness view, diagnostic export |

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
