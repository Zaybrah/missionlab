# Co-op verification record (ML-037), 0.27.0 candidate

One complete session of one host with MissionLab and one friend without any mod, on the exact 0.27.0 candidate. The
record below the line, for 0.24.3 (ML-011), is kept as it was: copy a record for a new combination, never edit a filled
one into another.

| | |
|---|---|
| MissionLab | 0.27.0 candidate. Commit: ________ Build (ZIP name and its SHA-256): ________ |
| OPERATOR | 0.09.5 (write the version the main menu shows if it differs): ________ |
| BepInEx | 6.0.0-be.785 (IL2CPP) on the host; write what is installed if it differs: ________ |
| Scenario | `examples/clean_house_acceptance.json` (missionlab-plan/2). Fingerprint from the run record: ________ |
| Map, mode | Clean House, `suburbv2.bytes`; scene: Suburb Day / Suburb Night (strike one); mode as the mission laptop names it: ________ |
| Host | Has MissionLab; hosts the mission and has the page open at `http://localhost:8765` |
| Client | No mod of any kind: no BepInEx, no MissionLab, no other loader. Confirmed by the client: ________ |
| Date, players | ________ |

**How to read the columns.** "Host" and "Unmodded client" are what each player *saw* on their own screen. A line in the
log or the recording ("accepted", "asked", "sent", "fired") is only the host's side and the requested call: it goes into
"Result and evidence", never into a player's column. A row passes only when the player whose column it is confirms it,
and a row needing the client passes only with the client's own word. Every row starts as Pending; at the end each is
Pass, Fail, Untested (not reached, or the client did not say) or Unavailable (the base game does not offer it). A row
that was not reached stays Untested, never Pass.

| # | Check | How to check | Host | Unmodded client | Result and evidence (host side) |
|---|---|---|---|---|---|
| 1 | Startup notice and briefing | Both read the three briefing lines and the notice that the host runs MissionLab when the mission starts | Pending | Pending | Pending. Log: `notice to the other players:` with the player count; the briefing lines in the log, no `the briefing could not be shown` |
| 2 | Chosen insertion for both players | Each player says which insertion they took and where they stood | Pending | Pending | Pending. Log: `insertion:` (and `insertion, second try:` if any) naming the spot per player |
| 3 | Authored enemies and patrols | Five guards in the house, one patrol walking the yard north of it; the client says whether it walks and where the guards stand | Pending | Pending | Pending. Recording: the start positions and the patrol's positions over the first minute |
| 4 | Delayed and triggered spawns | The wanderer at 60, 150 and 240 s (trigger 2, none at 330 s); two patrols from the north after the front door (trigger 1); one guard in the north yard 20 s after the house hold (trigger 10). Each player says what they saw and when | Pending | Pending | Pending. Log and recording: the fire time of triggers 1, 2 and 10 and the enemies each brought |
| 5 | Door lock, unlock, open and close, walking through | The upstairs door "Door Interior Paneled_3" is locked at the start (try it), open after trigger 1 (both walk through); the back door "Door Exterior Paneled_3" is shut and locked after the phone (the client tries to open it) | Pending | Pending | Pending. Log: `doors at the start:`, `doors, a few seconds in:` and the `door '…'` lines with their result; no `a door could not be set by` |
| 6 | Moved intel and collection | The phone lies upstairs at the north end, not in the game's place; the client picks it up if possible | Pending | Pending | Pending. Log and recording: trigger 3's time and who picked it up; the objective progress the game shows |
| 7 | Messages taking turns | When the phone is taken: "Phone secured …" then, after it and not instead of it, "Second message: the extraction is open."; then the countdown lines. Each player writes the order they saw | Pending | Pending | Pending. Log: triggers 3 and 4 in the same moment and the 4.5 s turn of the second message |
| 8 | Objective notices (ML-046) | Play `examples/clean_house_objectives.json` (missionlab-plan/3) as an extra mission. At the start no "New objective" notice: the briefing names the phone and the officer. Expected, in this order: "Objective complete: Recover the phone", then "New objective: Secure the upper floor", then "Objective complete: Secure the upper floor" after the 30 s hold by everyone ("Objective complete: Take down the officer" whenever the officer goes down). The extraction stays shut until both required goals are done, whatever happens to the optional officer. Each player writes which notices they saw and in what order | Pending | Pending | Pending. Log: `objective notice: ` per notice and `extraction: held back until every required objective is complete (…)`; no `an objective notice could not be shown: ` |
| 9 | Held extraction, opening and extracting | Try to extract before the phone (it stays shut); after it, both extract | Pending | Pending | Pending. Log: `extraction: held back until trigger 4`, the opening line and the mission-wide `canExtract` it logs; the run record's result |
| 10 | Success ending and what each screen shows | Phone taken and the officer (upstairs, middle of the floor) down: "Phone and officer: mission accomplished.", then the success screen. Each player writes the result screen, any "cheating detected", and where they land | Pending | Pending | Pending. Log: the ending waits for its messages; run record says "success" |
| 11 | Failed ending and what each screen shows | Take the phone and let the three minutes run out: countdown at the start, one minute and ten seconds; "Out of time." in red; the failed screen | Pending | Pending | Pending. Log: trigger 5 and the ending call; run record says "failed" |
| 12 | Extraction not read as death (0.26.2) | At the success by extraction, no "players are down" or reinforcement message on either screen | Pending | Pending | Pending. Log: no `reinforcement wave` line after the extraction starts; run record "success", not "over" |
| 13 | Simultaneous deaths and a shared wave | Both die at the same moment (for example one grenade): both come back together 20 s later. Then a staggered pair: back 20 s after the first death. A third death after two waves: nobody comes back | Pending | Pending | Pending. Log: `reinforcement wave` lines with death and return times; the game's respawn setting put back |
| 14 | Client enters and leaves the painted house through the doors | The client walks in through the front door, then out through the back door, then round the garden, along the railed veranda in front of the front wall, across the courtyard behind the back door and down to the basement. Trigger 8 "Inside the house." fires only on entering the house; the host watches the inspector's inside count for trigger 9 go up and down with the client | Pending | Pending | Pending. Recording: the client's positions against the painted area; the inspector's count; trigger 8's time against the moment the client crossed the door |
| 15 | A hold surviving stair transitions | Both players inside, then go up and down the stairs during the 45 s: trigger 9 "House held by everyone for 45 seconds." fires 45 s after the second player went in, not later. Then a check that it restarts: in a second mission one player steps into the garden before 45 s | Pending | Pending | Pending. Log: trigger 9's fire time against both entry times; the inspector's time left during the stair climb |
| 16 | Crouching inside the area | The client crouches (and goes prone) in a ground-floor room and upstairs during the hold; the count does not drop | Pending | Pending | Pending. Inspector: inside count while crouched; recording positions at feet height |
| 17 | A live shape edit from the page | During a mission, reshape trigger 9's area on the page (shrink it to the upper floor only) and send it; the client stands downstairs and must drop out of the count, then upstairs and count again | Pending | Pending | Pending. Page: the change's result line (applied, failed, pending, unconfirmed); log: the command and its session |
| 18 | A retried command on the page | Send the same change twice quickly (for example move a waiting enemy): it happens once | Pending | Pending | Pending. Page: the result line of each send; log and recording: the effect once, not twice |
| 19 | A stale command on the page | Send a change, end the mission before it is carried out (or send it after the end): it is struck off and not carried out in the next mission | Pending | Pending | Pending. Page: stale or expired; log: no effect in the next mission |
| 20 | Two consecutive missions, nothing inherited | After a mission, host the same mission again from the lobby: no trigger fires early, the hold starts from nothing, the game's keys work, no queued page command from the first mission happens | Pending | Pending | Pending. Log: the second mission's start lines and trigger times from 0; run records with different run ids |
| 21 | Game Master controls released after a rules failure (ML-030) | Host opens Game Master (F8) with `BlockGameKeys` on; a rules failure is provoked the way ML-030's controlled game test does it. The game's keys work again and the camera is the player's. Without a way to provoke it in this build, open and close Game Master, check the keys and mark the failure half Untested | Pending | n/a | Pending. Log: the guard's pause and the release line; the host's keys working |

## The mission

`examples/clean_house_acceptance.json` puts these rows into one mission (its notes list every trigger):

- start enemies, the yard patrol, the timer repeat (trigger 2), the front-door zone (trigger 1) with two patrols and the
  upstairs door;
- the moved phone (trigger 3), two messages at once (triggers 3 and 4), the countdown and failed ending (trigger 5), the
  officer (trigger 6) and the success ending (trigger 7);
- the painted house (trigger 8) and the hold by everyone across the stairs (trigger 9), both on one area of two floors
  that leaves out the garden, veranda, courtyard and basement; a delayed standing guard after the hold (trigger 10);
- the extraction held behind trigger 4, and shared waves 20 s after the first death, two at most.

It uses nothing `docs/capabilities.json` marks failing: no hunters, no calling enemies, no Game Master orders to fresh
enemies. It has no goals; row 8 is played on `examples/clean_house_objectives.json` (missionlab-plan/3), a separate
mission with two required goals in a chain, one optional goal and the extraction waiting for the required ones.

## Before the session

1. Host: install the 0.27.0 candidate ZIP. Write the commit and the ZIP's SHA-256 into the header. Copy
   `examples/clean_house_acceptance.json` and `examples/clean_house_objectives.json` into
   `BepInEx/MissionLab/scenarios/`; move every other Clean House scenario into a subfolder so it is not picked.
2. Host: in `BepInEx/config/missionlab.director.cfg`, `[General]`: `Scenario = clean_house_acceptance.json` (for row 8,
   `Scenario = clean_house_objectives.json`), `FairPlay = Off`, `TellPlayers = true`; `[GameMaster]`:
   `BlockGameKeys = true`. Leave the rest as shipped.
3. Host: open the page, press "Is everything ready?" and paste "Copy a report" under the header.
4. Client: a clean game with no mod at all. Both players note the time of each event (the mission clock or a stopwatch
   started when the mission starts). If possible both record their screens.
5. Copy `BepInEx/LogOutput.log` before every restart of the game; it starts afresh each time.

## What the session must record

- The commit and build of the header, and the "Copy a report" text.
- The name of every run record (`BepInEx/MissionLab/runs`) and every recording (`BepInEx/MissionLab/replays`), one per
  mission, with which rows each mission covered. At least: one mission to the success ending, one to the failed
  ending, and two consecutive missions for row 20.
- The copied logs, with the lines that back each result quoted next to the row.
- For each row, the host's and the client's own words; a row the client did not comment on stays Untested on their side.

## Failed rows

A failed row is either fixed and rerun on a new candidate (a new record, with its own commit), or explicitly taken out of
the supported scope: the README Status table and `docs/capabilities.json` then say so, and the starter mission does not
rely on it. A row is never moved to Pass on the host's log alone. Afterwards update the README Status table,
`docs/capabilities.json` and the ML-037 row of `docs/backlog-status.md` with exactly what this record shows.

---

# Co-op verification record (ML-011)

One record per game / mod / map / mode combination. Copy this file for each further combination; never edit a filled-in
record into a new one.

| | |
|---|---|
| MissionLab | 0.24.3 (the release ZIP, `dist/MissionLab-0.24.3.zip`) |
| OPERATOR | 0.09.5 (the version the plugin names as tried; write the version the main menu shows if it differs) |
| BepInEx | 6.0.0-be.785 (IL2CPP) |
| Map | Clean House, `suburbv2.bytes`; scene: Suburb Day / Suburb Night (strike one) |
| Mode | As the mission laptop names it: ________ |
| Host | With MissionLab, hosts the mission and has the page open at `http://localhost:8765` |
| Client | Without any mod (no BepInEx); joins through the game's own lobby |
| Scenario | `examples/clean_house_acceptance.json`, fingerprint from the run record: ________ |
| Date, players | ________ |

**How to read the columns.** "Host" and "Client" are what each player *saw* on their own screen. A line in the log
such as "accepted", "asked" or "sent" is only the requested call: write it into "Result and evidence", never into
"Client". A row passes only when the player whose column it is confirms it. Every row starts as Pending; at the end
each row is Pass, Fail, Untested or Unavailable (the base game does not offer it). A row that was not reached stays
Untested, never Pass.

| Check | Host observation | Unmodded client observation | Result and evidence |
|---|---|---|---|
| Startup and chosen insertion | Pending | Pending | Pending (briefing lines, the "host runs MissionLab" notice, each player's insertion) |
| Authored enemies and patrols | Pending | Pending | Pending (positions; the yard patrol walking its route) |
| Triggered and repeated reinforcements | Pending | Pending | Pending (trigger 1 time; trigger 2 at 60, 150, 240 s and no fourth) |
| Door lock, unlock, open and close | Pending | Pending | Pending (try to walk through each door, not only look at it) |
| Moved intel and collection | Pending | Pending | Pending (phone upstairs, picked up by whom, trigger 3 time, objective progress) |
| Messages and countdown overlap | Pending | Pending | Pending (order of lines; any line missing on either screen) |
| Extraction gate and opening | Pending | Pending | Pending (extraction refused before the phone, completed after) |
| Shared respawn waves | Pending | Pending | Pending (death times, wave time, who came back, third death after two waves) |
| Success and failure endings | Pending | Pending | Pending (result screen, back to lobby, "cheating detected" or not) |
| Live edits, retries and stale commands | Pending | Pending | Pending (the page's result per change; nothing done twice) |
| Save, reload and reproducible setup | Pending | Pending | Pending (run record, fingerprint, repeated run's resolved content) |
| Restart, map transition and cleanup | Pending | Pending | Pending (nothing of Clean House in the next mission) |
| Invalid completion path refused | Pending | Pending | Pending (refusal test: log reason, game's own mission played) |
| Non-host and unsupported-mode behaviour | Pending | Pending | Pending (the mod sets nothing up) |
| Rejoin or late join if the base game supports it | Pending | Pending | Pending (Unavailable if the game does not allow it) |
| Mission to play (ML-020): Quiet Raid, `examples/clean_house_quiet_raid.json` | Pending | Pending | Pending (finished or failed and why; start and end time for the measured duration; save, reload and a second run) |
| Mission to play (ML-020): Escalation, `examples/clean_house_escalation.json` | Pending | Pending | Pending (waves at 90, 165, 240 and 315 s; the hold; ending; duration; save, reload and a second run) |
| Mission to play (ML-020): Game Master, `examples/clean_house_game_master.json` | Pending | Pending | Pending (which triggers the host fired and when; how the way out opened; duration; save, reload and a second run) |

## Before the session

1. Host: install the 0.24.3 ZIP on the game. Copy `examples/clean_house_acceptance.json` and
   `examples/clean_house_refusal_test.json` into `BepInEx/MissionLab/scenarios/`; move every other scenario for Clean
   House into a subfolder so that it is not picked.
2. Host: in `BepInEx/config/missionlab.director.cfg`, `[General]`: `Scenario = clean_house_acceptance.json`,
   `FairPlay = Off`, `TellPlayers = true`; leave the rest as shipped. Note the values of `RandomExtras` and `RandomIntel`
   in the record (they do not act on a scenario file, only on random layouts).
3. Host: open the page, press "Is everything ready?" and paste "Copy a report" under the header.
4. Client: a clean game without BepInEx. Both players note the time each event happens (the mission clock, or a phone
   stopwatch started when the mission starts).
5. The game keeps one BepInEx log and starts it afresh with every start of the game: copy `BepInEx/LogOutput.log`
   before each restart. Recordings (`BepInEx/MissionLab/replays`) and run records (`BepInEx/MissionLab/runs`) are kept.

## The mission, step by step

1. **Start.** Both players read the three briefing lines and the notice that the host runs MissionLab. Each says
   which insertion they chose and where they started.
2. **Start enemies.** Five guards in the house and one patrol in the yard north of it. The client says whether the
   patrol walks, and where the guards stand when first seen.
3. **Repeat trigger.** Without entering the house, wait at the insertion: one wanderer comes at 60 s, 150 s and 240 s,
   each with the message "... one more of them is on the way". Note there is no fourth at 330 s. Both players say
   whether they saw each message and each new enemy.
4. **Zone and doors.** Walk up to the front door (trigger 1): the message about movement to the north, two patrols
   from the north. Go upstairs: the door "Door Interior Paneled_3" (south end, upper floor) was locked at the start;
   after trigger 1 it should stand open and let both players through. The client tries to walk through it.
5. **Intel.** The phone lies upstairs at the north end, not in the game's place. Both players say whether they see it
   there. The *client* picks it up if possible (collection by an unmodded player is the open question). At that
   moment: "Phone secured ..." then, after it and not instead of it, "Second message: the extraction is open."; the
   back door "Door Exterior Paneled_3" closes and locks (try to open it); the countdown starts and tells you the
   time, then one minute and, in red, ten seconds. Write down the order in which each player saw the lines.
6. **Extraction.** Before the phone, try to extract once (it should stay shut). After it, extract or go on.
7. **Two staggered deaths.** Before the phone is taken or after, let the client die, then the host about 10 s later.
   Twenty seconds after the *first* death both should come back together. Then let both die at the same moment: the
   second wave. A third death after that: no more waves (two at most). Write the death and return times per player.
8. **Endings.** Run the mission twice from the lobby:
   - success: take the phone and kill the officer (the guard upstairs, in the middle of the floor): "Phone and
     officer: mission accomplished." and the success ending;
   - failure: take the phone and let the three minutes run out: "Out of time." in red, then the failed ending.
   Note on each screen whether the end screen says cheating was detected, and where each player lands afterwards.
9. **Live edits.** In one run, on the page: move one waiting enemy, change trigger 2's message, press "Fire now" on
   a trigger, and send the same change twice quickly. The client says what changed for them; the page's result line
   and the log say what was asked. A change must happen once, not twice; a change sent after the mission ended must
   be struck off, not carried out in the next one.
10. **Save and repeat.** After a run, press "Save in the game", then on the page pick that run under "Dice" and repeat
    it. Compare the run records' fingerprint and resolved content (enemies, doors, intel place), not the fight.
11. **Full restart.** Quit the game completely on the host (copy the log first), start it again, host the same mission
    with the client: the same layout should appear.
12. **Map transition.** Back in the lobby, host another map (for example Mexico Depot). Nothing of Clean House may
    happen there: no messages, no doors, no waves, no countdown.
13. **Refusal.** Set `Scenario = clean_house_refusal_test.json` and host Clean House until the log says the scenario
    was refused (its extraction trigger loses a 50% roll). The game's own mission must play for both, unchanged.
14. **Non-host and other modes.** If the friend can install the mod: the friend hosts nothing, the modded player joins
    the friend's mission, and the mod must set nothing up. On the host, start a mode the mod leaves alone (PvP or wave
    survival) and check that nothing is changed. If the friend cannot install it, mark the non-host half Untested.
15. **Rejoin and late join.** Only if the base game lets a player join a running mission: the client leaves and joins
    again, and a second client joins late. Both say whether they see the current enemies, doors and intel. If the
    game does not allow it, write Unavailable.

## After the session

- Attach the copied logs, the recording of each run and the run records; quote the lines that back each result.
- Move every row to Pass, Fail, Untested or Unavailable, and update the "Status" table in `README.md` and the ML-011 row
  of `docs/backlog-status.md` with exactly what this record shows.
