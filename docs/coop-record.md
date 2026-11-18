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
