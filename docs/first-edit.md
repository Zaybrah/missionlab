# Your first edit (ML-020)

A short walkthrough for a host who has never used MissionLab: install it, play a supplied mission, change one
encounter, fix what the Mission check finds, save, reopen and play your version. It takes about half an hour
plus the missions themselves.

**Not tried with testers yet.** Nobody new to MissionLab has followed these steps so far, so we do not yet know which
step is the hardest. If one confused you, write down which and why: the plan is to fix the worst step before making
this page longer.

Steps marked **(game)** need OPERATOR running with you as host; the others work in the browser alone.

## 1. Install

1. Unzip the release ZIP (`MissionLab-<version>.zip`) into the OPERATOR folder, the one that holds `OPERATOR_Data`.
2. The ZIP puts the example missions in `BepInEx/MissionLab/scenarios/examples/`, where they are not used. Move these
   three up one folder, into `BepInEx/MissionLab/scenarios/`:
   - `clean_house_quiet_raid.json`: a quiet intel raid;
   - `clean_house_escalation.json`: hold a room against waves;
   - `clean_house_game_master.json`: the host fires events by hand.
3. Leave `BepInEx/config/missionlab.director.cfg` as it is; it is made the first time the game starts with the mod.

## 2. Open the page from the game (game)

1. Start OPERATOR and host a PvE mission on Clean House (a find-intel mission, so that the phone is there).
2. Open `http://localhost:8765` in a browser on the same computer. Only your computer can reach it.
3. The buttons at the top of the side panel switch between **Play**, **Create** and **Direct**.

## 3. Pick a mission (Play)

1. In **Play**, choose "Clean House: Escalation". The details show its briefing, squad size and the author's
   estimate of its length.
2. Press **Play this next**, go back to the lobby and host Clean House again (game). The briefing appears at the
   start. Play it once so you know the mission before you change it.

## 4. Change one encounter (Create)

The encounter: when the squad reaches the room upstairs (trigger 1 "Upstairs"), one more enemy should join the fight.

1. Back on the page, in **Play**, choose Escalation again and press **Open in Create**.
2. In the Triggers tab, choose trigger 1 "Upstairs" and change its message, for example to "They were waiting for
   you. Hold the room."
3. Place one enemy inside that trigger's circle on the upper floor, then set its "Appears" to trigger 1 "Upstairs".

## 5. Fix a Mission check warning

1. Open the Mission tab. It now lists a warning: the new enemy appears inside trigger 1, where the players are when
   it fires (it would pop up in front of them). Click the warning to go to the enemy.
2. If the warning offers **Preview repair**, press it, read what would change and press **Apply repair** (one Undo
   step undoes it). Otherwise drag the enemy out of the circle by hand, for example down the stairs. If a new
   warning says it appears in plain view, move it further, out of sight of the room.
3. The warning disappears. The Mission check says whether the rules can be finished; it cannot tell whether the
   fight is fair.

## 6. Save and reopen

1. In the Save box, give the file a new name, for example `clean_house_escalation_mine`, so the supplied mission
   stays as it was.
2. With the game running, press **Save in the game** (game). Without the game, "Save file" downloads it; put it in
   `BepInEx/MissionLab/scenarios/` yourself.
3. Reload the page, go to **Play** and choose your copy: your message and your extra enemy are there. **Open in
   Create** shows them on the map.

## 7. Play it (game)

1. In **Play**, choose your copy and press **Play this next**.
2. Host Clean House and go upstairs: your message appears and your enemy comes up the stairs.

## The three missions

| Mission | Pacing | Way out | Failure | Squad | Minutes |
|---|---|---|---|---|---|
| Quiet Raid | Stealth: few enemies, a timer only after an alarm | Take the phone, then extract | Four minutes after the alarm; everyone dead | 1–4 | 15 |
| Escalation | Rising pressure: four timed waves and a last push | Hold the upper north room for three minutes | Fifteen minutes run out; everyone dead | 2–4 | 15 |
| Game Master | The host fires events by hand | Extract once the phone is taken, the host opens it, or 20 minutes pass | Everyone dead | 2–6 | 25 |

Squad and minutes are the author's estimates. No playtest has measured them yet; measured times will come from run
records. Each mission still plays when its optional (chance) enemies are absent: nothing the way out depends on is
left to a roll.
