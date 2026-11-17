# Backlog status

Where each ticket of `docs/backlog.md` stands. Updated 17 November 2026, build 0.24.1.

"Tested" means an automatic test proves it (`./test.sh`, the map tests, or the editor tests). "In the game" means it
needs a run in OPERATOR; "co-op" means a run with a friend who has no mod. 0.24.1 itself has not run in the game yet.

## v0.24

In the game so far (17 November, 0.24.0, one player): the trigger engine, a shared wave, two refused missions.

| Ticket | State | Tested | Still open |
|---|---|---|---|
| ML-001 completion path | Built, seen in the game | Chance, transitive, all/any, by hand, missing, circle, target person, 60 seeds against the dice; editor check. In the game: refused twice, the game's own mission ran | Co-op |
| ML-002 shared waves | Built, seen in the game | Staggered deaths, pending not counted, three tries, cap, failed call. In the game: announced, asked, one player seen back | The whole squad dead at once; co-op |
| ML-003 current positions | Built; the call still moved nobody | Order log: "3 accepted, walkable ground at ..." | 0.24.1 follows every order up and tries three ways; needs the next run |
| ML-004 trigger history | Built, seen in the game | Rearm keeps history, repeats, countdown, order of the list, circles, delete and re-make, fire by hand, hold. In the game: every trigger of the feature test | |
| ML-005 failure isolation | Built | Guard: pauses, growth to a minute, independence, recovery | In the game: nothing forces an error there |
| ML-006 command results | Built | Ledger; page: stale, expired, pending, numbers and session sent | A batch gets one result, not one per operation |
| ML-007 scenario contract | Built | Seven files that must be refused, with the same findings in plugin and editor; text escaping; schema kept in step with the writer; every scenario on the test PC passes | A newer format has nothing to convert from yet, so there is no conversion summary beyond the note for a file without a format line |
| ML-008 resolved run record | Built | Fingerprint; page: list and choice of a run to repeat | In the game: the record itself and a repeated run. Random extras are in the record but were not checked for repeating |
| ML-009 replay events | Built | Removed is not down; requests counted apart; older and damaged recordings | Marker and vehicle identity in every row is not there yet |
| ML-010 asset-free tests | Partly | `./test.sh`: 150 checks without a game file, also on GitHub; fixtures; examples checked; `--read` checks any scenario file | Editor tests still live outside the repository and need local game files |
| ML-011 co-op proof | Not started | | Needs you and a friend without the mod |
| ML-012 packaging | Built | ZIP built and its contents checked; readiness rows; report without names or folders; 3D library beside the page | The ZIP on a clean game install; another game version only warns, it does not hide controls |

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
