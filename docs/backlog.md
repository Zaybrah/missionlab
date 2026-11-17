# MissionLab implementation backlog

Prepared 17 November 2026. Updated with the requested paintable trigger areas.

Baseline: [v0.23 at commit 581c316](https://github.com/Zaybrah/missionlab/commit/581c31634b922ae82a10e8a6e67114be396fc97e).

## Recommended direction

Make MissionLab dependable for a host and an unmodded co-op group, then make good missions easier to author. Preserve the current C# core, local browser editor and host-only architecture. Treat new game calls as small feasibility experiments until their effects have been observed.

This backlog contains **27 implementation tickets** and **11 additional or expanded ideas**. Six ideas are already represented by, or included within, a ticket; ideas are not a second set of automatically approved work. The version groupings below are proposed release targets. **ML-027, paintable trigger areas, is an explicit user requirement and a priority for the authoring milestone.**

- **v0.24:** 12 tickets for predictable mission behavior, reproducible runs, a demonstrated co-op baseline and complete packaging.
- **v0.25:** 9 tickets for authoring, custom trigger areas, diagnostics, reusable content and finished missions.
- **v0.26 candidates:** 6 optional growth tickets. Choose a small coherent subset after observing real use.

All acceptance checkboxes start unchecked. Source findings describe the reviewed implementation; acceptance criteria describe the behavior to implement. They are not claims that a fix or a live-game test has already happened.

## Decisions to make explicit

1. **Respawn means a shared reinforcement wave initially.** The demonstrated game method respawns everyone. Individual lives need a separate proven capability.
2. **Ordinary trigger dependencies remember an event for the whole mission.** Repeat cooldowns must not erase that history.
3. **An authored scenario and a resolved run are different records.** One preserves alternatives; the other preserves the choices of one run.
4. **A structural validation failure blocks applying that custom setup.** The host receives a reason and the shipped mission remains available. A runtime recovery must not silently rewrite required objectives.
5. **MissionLab reports observed effects separately from requested calls.** A successful method invocation alone does not prove an unmodded client saw the effect.
6. **The editor remains local and simple to distribute.** Source modules and a small build step may produce the same convenient distributable.
7. **Rehearsal checks exact rule logic against assumed events.** It does not predict combat, path execution, spotting or network outcomes.
8. **Defaults follow demonstrated capability.** Untested moved intel, respawn combinations or other features must be recognizable before a host relies on them.
9. **Trigger areas can be painted and shaped to the intended space.** A house interior is a union of explicitly selected floor regions, with outdoor areas and holes excluded. Circle zones remain compatible; custom shapes use actual area containment throughout the editor and runtime.

## Priority and size

- **P0:** correctness or an acceptance gate for the stable co-op release.
- **P1:** recommended foundation or authoring work.
- **P2:** optional growth work to select after the core experience is demonstrated.
- **S:** a contained change in an existing component.
- **M:** a change across several existing components or a focused game-validation effort.
- **S–M:** scope depends on how much existing UI/data handling can be reused.
- **M–L:** a larger feature crossing geometry, authoring and runtime behavior; deliver it through bounded sub-tasks.

These are relative scope labels, not calendar estimates. Undocumented game behavior can change an estimate. A feasibility spike ends with an observed result and a proceed/change/defer decision.

## Start here

Prepare the ML-011 test scenario and ML-010 fixtures early, then take these slices:

1. **Immediate behavior fixes:** ML-001, ML-003, ML-002.
2. **Runtime resilience:** ML-005 and ML-006.
3. **Rule and format contracts:** ML-004, ML-007 and the asset-free part of ML-010.
4. **Reproduction and evidence:** ML-008 and ML-009; extend fixtures with their cases.
5. **Stable release:** complete ML-011 on the actual release build, then ML-012 and a fresh-install smoke test.

Work on independent components can proceed in parallel after their shared contracts are agreed. Dependencies specify what must be available for completion; test scaffolding, packaging and acceptance-scenario preparation can begin earlier.

For a small hotfix before the full milestone, prioritize ML-001 and ML-003, then the command/session failure paths in ML-005 and ML-006. Label that build according to what was actually verified.

For the authoring milestone, start **ML-027 paintable trigger areas** alongside the view/editing work in ML-013/ML-014 as soon as the format, trigger and fixture foundations are available. Agree the area model during ML-007, implement its brush/polygon workflow early, and complete live application after ML-014. Automatic room recognition is a later convenience and does not block manual painting.

## Milestone acceptance

### v0.24

A new host installs the package, selects one proven mission, plays it with an unmodded friend, edits one encounter, saves it, restarts and runs the saved setup. The tested paths finish correctly; rejected or unsupported setups explain why before they are applied.

The release evidence names the game, BepInEx and MissionLab versions, map, mode and host/client combination. Retain one clearly documented supported baseline; expand the matrix as more combinations pass.

### v0.25

An author can paint the actual interior of a house as a trigger, exclude outdoor spaces and choose the intended floors. They can understand a blocked trigger, rehearse the mission's intended logic, reuse an encounter, preview a repair and produce a complete scenario with a useful briefing. Three polished missions demonstrate different pacing goals.

### v0.26 candidates

Choose two or three visible improvements that share the stable foundations. My first combination is **sealed missions, bounded host options and constrained scatter**. Use the same proven mission to demonstrate that these add variety without breaking completion.

## Ticket index

### v0.24

| ID | Task | Priority | Size |
|---|---|---|---|
| ML-001 | Reject missions whose required completion path disappears | P0 | M |
| ML-002 | Define and verify shared reinforcement waves | P0 | M |
| ML-003 | Use current enemy positions for rally and proximity decisions | P0 | S |
| ML-004 | Make trigger history, repeats and dependencies explicit | P0 | M |
| ML-005 | Isolate failures and clean up each mission session | P0 | M |
| ML-006 | Make live edits session-bound and return clear command results | P0 | M |
| ML-007 | Publish and enforce a versioned scenario contract | P0 | M |
| ML-008 | Save authored scenarios and resolved run setups separately | P1 | M |
| ML-009 | Make replay events accurately describe what happened | P1 | M |
| ML-010 | Run core and editor regression tests without game assets | P0 | M |
| ML-011 | Prove and publish the supported co-op combinations | P0 | M |
| ML-012 | Ship a complete install with an offline editor and readiness check | P0 | M |

### v0.25

| ID | Task | Priority | Size |
|---|---|---|---|
| ML-027 | Paint custom trigger areas with floors and exclusions | P1 | M–L |
| ML-013 | Give hosts clear Play Create and Direct entry points | P1 | M |
| ML-014 | Stage live changes and show a semantic diff before applying | P1 | M |
| ML-015 | Explain why every live trigger is waiting or finished | P1 | M |
| ML-016 | Rehearse mission logic and save author acceptance tests | P1 | M |
| ML-017 | Add previewable repairs for selected Mission check findings | P1 | M |
| ML-018 | Let authors name meaningful places on a map | P1 | S–M |
| ML-019 | Save and reuse complete encounter modules | P1 | M |
| ML-020 | Ship three polished missions and a guided first edit | P1 | M |

### v0.26 candidates

| ID | Task | Priority | Size |
|---|---|---|---|
| ML-021 | Add sealed missions with a spoiler-free Play view | P2 | S–M |
| ML-022 | Offer bounded host options and a quick mission form | P2 | M |
| ML-023 | Add constrained scatter and authored placement areas | P2 | M |
| ML-024 | Deliver a useful debrief and an unused-content report | P2 | M |
| ML-025 | Add an optional Game Master budget and action cards | P2 | M |
| ML-026 | Export portable scenario packs with a local import preview | P2 | S–M |

## Detailed implementation tickets

### ML-001 Reject missions whose required completion path disappears

**Target:** v0.24 · **Priority:** P0 · **Size:** M · **Basis:** Source finding

**Dependencies:** None; work can begin immediately.

**Why:** An extraction gate is installed when its trigger exists, even if the resolved chance roll has disabled that trigger. The normal evaluator skips disabled triggers. Unplanned host intervention should not be required to finish an automatic mission.

**Implement:** Add editor preflight and runtime validation after chance, either/or and player-count resolution. Follow required extraction/target dependencies transitively through after, all and any. Validate before the spawn-list swap and other mission mutations. Reject a structurally invalid resolved setup with an actionable reason; retain the shipped mission instead of silently removing the author's rules. The immediate defect fix should precede broader format work. An explicitly authored manual completion path remains valid when the scenario declares that a Game Master is required; distinguish it from a broken automatic path.

**Acceptance criteria:**

- [ ] A trigger excluded by chance cannot leave a newly installed extraction hold behind.
- [ ] Transitive cases are covered: extraction → A → B; all requires every required branch, while any accepts an actually viable alternative.
- [ ] Required targets excluded by chance, either/or or player-count filtering are reported by name and field.
- [ ] The editor identifies the failing dependency. Runtime preflight names the effective seed and squad size so the failure can be reproduced.
- [ ] A rejected plan leaves the original spawn list and game extraction behavior intact. Structural validity is not presented as proof of tactical winnability.

**Evidence/context:** [Director startup and variant resolution][S01] · [Game Master lifecycle, trigger evaluation, respawn and saving][S02] · [Variant resolution][S07] · [Browser editor and Mission check][S10].

### ML-002 Define and verify shared reinforcement waves

**Target:** v0.24 · **Priority:** P0 · **Size:** M · **Basis:** Source finding and product decision

**Dependencies:** None; work can begin immediately.

**Why:** The current respawn rule uses one death timer and one quota for the mission, then calls the game's respawn-all method. Its wording implies an individual delay and individual lives.

**Implement:** For the first stable release, make the contract explicitly shared reinforcement waves: after is the delay from the first eligible death and times is the number of successfully observed waves. Preserve legacy behavior with an explanation during migration. Defer individual respawn timers until a selective game method is demonstrated.

**Acceptance criteria:**

- [ ] With after=20, deaths 19 seconds apart produce the documented shared-wave outcome; both host and client can explain why.
- [ ] A wave remains pending until its result is observed or a bounded timeout occurs. Missing game manager, exception or no confirmed revival does not consume a successful wave.
- [ ] The UI distinguishes waves remaining, request pending and request failed. A pending request cannot spawn duplicate waves.
- [ ] The temporary base-game respawn setting is restored on success, failure and mission exit.
- [ ] Test the entire squad dying against the game's own ending behavior. If that combination cannot be supported, identify it before play and document the supported rule.

**Evidence/context:** [Game Master lifecycle, trigger evaluation, respawn and saving][S02] · [Live orders and game respawn calls][S03] · [Latest recorded playtest findings][S11].

### ML-003 Use current enemy positions for rally and proximity decisions

**Target:** v0.24 · **Priority:** P0 · **Size:** S · **Basis:** Source finding

**Dependencies:** None; work can begin immediately.

**Why:** The rally radius currently reads the enemy's object transform, while the findings document that it stays at the original spawn position.

**Implement:** Use the existing EnemyPose helper for current-position decisions. Review rally filtering and nearest-player selection for hunters, keeping original-position reads only where they intentionally identify a spawn marker.

**Acceptance criteria:**

- [ ] An enemy that walked into the rally radius receives the order; one that walked out does not.
- [ ] Fixed sentries and vehicle crew are excluded from walking orders when they lack a usable navigation agent.
- [ ] Orders record the requested target, the actual snapped target and the number of enemies accepted.
- [ ] An in-game check confirms the ordered movement for the host and an unmodded client.

**Evidence/context:** [Game Master lifecycle, trigger evaluation, respawn and saving][S02] · [Live orders and game respawn calls][S03] · [Latest recorded playtest findings][S11].

### ML-004 Make trigger history, repeats and dependencies explicit

**Target:** v0.24 · **Priority:** P0 · **Size:** M · **Basis:** Source finding and semantic clarification

**Dependencies:** None; work can begin immediately.

**Why:** The Fired set is used both as firing history and as repeat/cooldown state. Rearming removes an ID that dependency checks also consult.

**Implement:** Extract the decision portion of trigger evaluation into a small pure C# component with an injected clock and observed events. Keep Unity effects in the existing adapter. Track has-ever-fired, last-fired time, fire count, cooldown, condition progress and action outcome separately. Ordinary after/all/any dependencies use mission-wide firing history; recurring dependency events require a future explicit option.

**Acceptance criteria:**

- [ ] Rearming A does not erase its completion for all(A,B) when B fires later.
- [ ] Timer repeats, condition repeats, delay, times and manual Fire now each have documented behavior and simulated-clock cases.
- [ ] Reordering a valid acyclic chain does not change its eventual result. Invalid cycles are diagnosed rather than relying on a fixed evaluation-pass limit.
- [ ] Deleting and recreating a trigger clears its history, cooldown, warnings and hold progress without affecting unrelated triggers.
- [ ] A mission restart begins with an empty trigger session.

**Evidence/context:** [Game Master lifecycle, trigger evaluation, respawn and saving][S02] · [Scenario model, serialization and normalization][S06].

### ML-005 Isolate failures and clean up each mission session

**Target:** v0.24 · **Priority:** P0 · **Size:** M · **Basis:** Source finding

**Dependencies:** None; work can begin immediately.

**Why:** The update loop has one lifetime error counter; after eleven accumulated exceptions, every update subsystem stops. Pending state also needs a single clear mission boundary.

**Implement:** Give each mission a session identifier and centralize reset of its pending spawns, orders, messages, trigger state and temporary game settings. Replace the shared error cutoff with small per-subsystem consecutive-failure counters and bounded backoff. Expose degraded status and a useful recovery action without adding a new service framework.

**Acceptance criteria:**

- [ ] Repeated picker or browser errors do not silently stop unrelated mission behavior.
- [ ] A failing subsystem stops flooding logs and exposes its state and last error; a later successful operation or a fresh mission can recover where safe.
- [ ] Restart, map change, host exit and mode change clear mission-owned pending work and do not carry prior trigger results forward.
- [ ] Leaving Game Master mode or disabling its subsystem restores input/camera state that the mod owns.
- [ ] A critical mission-rule failure is visible to the host, with a deliberate recovery route; gameplay-affecting actions are never blindly replayed.

**Evidence/context:** [Game Master lifecycle, trigger evaluation, respawn and saving][S02] · [Plugin configuration and update error handling][S04] · [Live browser request queue and editor serving][S05].

### ML-006 Make live edits session-bound and return clear command results

**Target:** v0.24 · **Priority:** P0 · **Size:** M · **Basis:** Source finding and reliability improvement

**Dependencies:** ML-005

**Why:** A live request can time out while its job remains queued. Jobs do not currently identify the mission they were created for.

**Implement:** Attach session and command identifiers to mutations. Bound the queue, expire jobs that have not begun, reject stale-session requests and make retries idempotent. Return per-command status so the editor can distinguish requested, applied, failed and unconfirmed effects. Persist enough recent command results for a browser refresh, without introducing a database.

**Acceptance criteria:**

- [ ] A request that expires before execution never runs later or in the next mission.
- [ ] If execution has begun when the HTTP wait expires, report pending/unknown and allow lookup by command ID; do not claim it was cancelled or encourage a duplicate action.
- [ ] Submitting the same command twice cannot create duplicate enemies or re-run a one-shot action.
- [ ] Partial batches show the outcome of each operation, and the editor reconciles to the actual game state.
- [ ] A slow/disconnected browser cannot create an unbounded work queue. Read-only status remains available outside a mission.

**Evidence/context:** [Live browser request queue and editor serving][S05] · [Game Master lifecycle, trigger evaluation, respawn and saving][S02] · [Browser editor and Mission check][S10].

### ML-007 Publish and enforce a versioned scenario contract

**Target:** v0.24 · **Priority:** P0 · **Size:** M · **Basis:** Source finding

**Dependencies:** None; work can begin immediately.

**Why:** The loader silently converts unknown trigger kinds and missing references. JavaScript and C# separately normalize the format.

**Implement:** Document and validate the current format with a JSON Schema plus semantic reference checks. Define defaults, bounds, enum values, stable IDs and supported legacy conversions. Keep future formats intact when refusing them. Add a new version only for an actual incompatible format or meaning change; do not force a ceremonial bump.

**Acceptance criteria:**

- [ ] All checked-in examples validate or have an explicit reviewed migration.
- [ ] Wrong types, duplicate IDs, missing references and unsupported kinds produce a field path and actionable message.
- [ ] An unsupported future version cannot appear to load successfully after losing required behavior.
- [ ] Legacy conversion shows a reviewable summary, can be cancelled and preserves the original file.
- [ ] Quotes, backslashes and control characters survive valid serialization. Editor and runtime agree on canonical supported semantics.

**Evidence/context:** [Scenario model, serialization and normalization][S06] · [Browser editor and Mission check][S10] · [Core test harness][S09].

### ML-008 Save authored scenarios and resolved run setups separately

**Target:** v0.24 · **Priority:** P1 · **Size:** M · **Basis:** Source finding

**Dependencies:** ML-004, ML-007

**Why:** The executed variant roll and serialized seed can differ; disabled trigger outcomes are not serialized. Reusing a mission record as a scenario can reroll its contents.

**Implement:** Introduce a resolved-run record containing scenario identity/revision, canonical content hash, selected scenario, effective seeds, selected options, player-count assumptions, resolved entities and disabled-trigger outcomes. Include mod/generator/game identifiers and a local map fingerprint. Preserve the authored scenario with every alternative. Make scenario selection deterministic over a stable candidate list or record the chosen file as a required input.

**Acceptance criteria:**

- [ ] A record with chance enemies, either/or, chance doors/things and disabled triggers reloads to the same effective initial plan without another random roll.
- [ ] Saving an authored scenario after playing still retains every authored alternative.
- [ ] Reproduce this setup and Play this scenario again are distinct actions with accurate labels.
- [ ] Changing a filename does not erase scenario identity; changing gameplay content produces a new content hash/revision.
- [ ] Compatibility differences are visible. The guarantee concerns the effective setup, not identical AI decisions, physics, damage or player actions.

**Evidence/context:** [Director startup and variant resolution][S01] · [Scenario model, serialization and normalization][S06] · [Variant resolution][S07] · [Game Master lifecycle, trigger evaluation, respawn and saving][S02].

### ML-009 Make replay events accurately describe what happened

**Target:** v0.24 · **Priority:** P1 · **Size:** M · **Basis:** Source finding

**Dependencies:** ML-005, ML-008

**Why:** An enemy disappearing from the tracked list is recorded as down, including live editing that removes and recreates the enemy.

**Implement:** Use stable authored/instance identifiers and distinguish confirmed death, editor removal, editor recreation, cleanup and unexplained disappearance. Record trigger outcomes, order targets, command results, reinforcement-wave outcomes and explicit end reasons.

**Acceptance criteria:**

- [ ] Moving, rotating or deleting a live enemy does not increase a confirmed combat-death count.
- [ ] Mission cleanup is not reported as a mass kill. Unknown disappearances remain unknown.
- [ ] Events connect to the correct scenario revision, resolved run and mission session.
- [ ] Existing replay files still open with their legacy inference limits identified.
- [ ] Sampling/serialization time and recording size are measured in a representative long session; only change flushing if that measurement shows an actual problem.

**Evidence/context:** [Replay collection and events][S08] · [Game Master lifecycle, trigger evaluation, respawn and saving][S02] · [Latest recorded playtest findings][S11].

### ML-010 Run core and editor regression tests without game assets

**Target:** v0.24 · **Priority:** P0 · **Size:** M · **Basis:** Testability improvement

**Dependencies:** ML-004, ML-007

**Why:** The current harness loads a supplied navmesh before its pure model and randomness checks, which prevents those checks from running independently.

**Implement:** Separate asset-free tests from real-map integration checks. Build a small shared fixture corpus for editor/C# roundtrips and use synthetic navigation geometry for algorithm checks. Add CI for the asset-free suite. Develop this alongside the fixes; the dependency list describes final completion, not when work must begin.

**Acceptance criteria:**

- [ ] One documented command runs pure model, serialization, randomness and rule tests from a clean checkout without OPERATOR.
- [ ] Fixtures cover all v0.23 fields, legacy defaults, future versions, malformed references and the extraction/chance defect.
- [ ] Editor → JSON → C# → JSON → editor preserves supported gameplay semantics.
- [ ] Simulated-clock tests cover repeats, overlapping messages, respawn accounting and mission reset. Synthetic maps cover disconnected areas, stacked floors and insertion clearance.
- [ ] Game-reference builds and real-map checks are separate opt-in commands with clear missing-input messages. CI contains no redistributed game binaries or map assets.

**Evidence/context:** [Core test harness][S09] · [Scenario model, serialization and normalization][S06] · [Browser editor and Mission check][S10] · [Navigation-aware generator][S15].

### ML-011 Prove and publish the supported co-op combinations

**Target:** v0.24 · **Priority:** P0 · **Size:** M · **Basis:** In-game release gate

**Dependencies:** ML-001, ML-002, ML-003, ML-004, ML-005, ML-006, ML-007, ML-008, ML-009, ML-010

**Why:** Published co-op evidence covers a narrower older subset than the current feature set. The README and later findings also disagree about some tested features.

**Implement:** Prepare one compact Clean House acceptance mission and a test record for one modded host plus at least one unmodded client. Consolidate version/capability status as tested, experimental, unavailable or untested for exact map/mode/build combinations. Select stable defaults from those results, including whether RandomExtras may move intel.

**Acceptance criteria:**

- [ ] Both players verify enemies, reinforcements, doors, messages, intel visibility and collection, extraction, shared respawns, endings and saved reload.
- [ ] Exercise two staggered deaths, repeat triggers, live edits, a full restart and a map transition. Test reconnect/late join only where the base game permits it.
- [ ] An invalid randomized completion path is rejected before mission mutation. Modded non-host and unsupported modes remain unaffected.
- [ ] Logs distinguish a requested call from an observed effect; visual/co-op passes require player confirmation.
- [ ] The current compatibility table replaces stale README claims, names the exact tested versions and records failures/unknowns without presenting them as passes.
- [ ] Optional features that fail or remain unverified are excluded from the demonstrated default baseline and clearly marked experimental/unavailable; they do not turn a failed observation into a pass.

**Evidence/context:** [Latest recorded playtest findings][S11] · [README, setup instructions and recorded co-op scope][S12] · [Plugin configuration and update error handling][S04] · [Random layout extras][S16].

### ML-012 Ship a complete install with an offline editor and readiness check

**Target:** v0.24 · **Priority:** P0 · **Size:** M · **Basis:** Packaging and onboarding

**Dependencies:** ML-007, ML-011

**Why:** The build produces DLLs but the live editor requires an additional page. The editor's 3D dependency currently comes from a CDN.

**Implement:** Produce one release ZIP with the intended BepInEx folder layout, both plugins, the editor and its pinned local dependencies, dependency notices, one proven starter mission and a concise install/update/remove guide. Add a small readiness view for plugin/editor versions, connection, map data, active mode and compatibility status. Export a local diagnostic report with an optional scenario/resolved-run bundle.

**Acceptance criteria:**

- [ ] A fresh installation following the guide opens the editor and runs the supplied mission without manually discovering missing files.
- [ ] With an empty cache and networking disabled, both 2D and 3D startup work using synthetic fixtures or the user's local files; unavailable WebGL has its own message.
- [ ] An update preserves user scenarios, replays and configuration, and editor/plugin mismatches have a clear repair step.
- [ ] Release construction does not depend on one hard-coded SDK directory; game references remain user-supplied and are validated before compilation.
- [ ] Diagnostic export lists its contents and contains no game binaries, navmesh files or exported geometry. Logs omit unnecessary player identifiers and local paths by default.

**Evidence/context:** [Build script][S14] · [Live browser request queue and editor serving][S05] · [Browser editor and Mission check][S10] · [README, setup instructions and recorded co-op scope][S12].

### ML-013 Give hosts clear Play Create and Direct entry points

**Target:** v0.25 · **Priority:** P1 · **Size:** M · **Basis:** Product workflow

**Dependencies:** ML-007, ML-008, ML-012

**Why:** Selecting a mission, authoring one and directing a live session require different information and controls.

**Implement:** Add a local mission catalogue with title, briefing, recommended squad range, measured playtest duration, required map/mode, tested capabilities and last-run status. Use Play, Create and Direct views over the same scenario model. Keep experimental controls recognizable.

**Acceptance criteria:**

- [ ] A host can choose and start the supplied mission without opening enemy-placement controls.
- [ ] Create exposes authored content and validation; Direct exposes live state and deliberate interventions.
- [ ] Mission metadata separates author recommendations from measurements; unknown duration or compatibility stays unknown.
- [ ] The current mode, draft state, selected next mission and running mission are always distinguishable.

**Evidence/context:** [Browser editor and Mission check][S10] · [README, setup instructions and recorded co-op scope][S12].

### ML-014 Stage live changes and show a semantic diff before applying

**Target:** v0.25 · **Priority:** P1 · **Size:** M · **Basis:** New authoring workflow

**Dependencies:** ML-006, ML-007, ML-008, ML-013

**Why:** A host needs to know which changes exist only in an authored draft and which have affected the running game.

**Implement:** Add a draft/live switch, grouped Apply changes, a short diff and per-operation receipts. Keep explicit save revisions with optional notes tied to the scenario hash. Preserve immediate Game Master commands as clearly identified actions.

**Acceptance criteria:**

- [ ] Moving three draft enemies and changing a door does not alter the game before Apply.
- [ ] The preview names affected objects, reinforcements and mission rules; validation blocks invalid batches.
- [ ] Retries do not duplicate effects, partial failures remain visible and the displayed live state follows the runtime result.
- [ ] Undo restores the draft. It does not promise to undo a death, completed mission, collected intel or another irreversible game effect.
- [ ] An earlier local revision can be opened as a separate draft without overwriting the current one.
- [ ] A saved revision displays its notes and linked run recordings; opening a run retains its resolved inputs and compatibility metadata.

**Evidence/context:** [Live browser request queue and editor serving][S05] · [Browser editor and Mission check][S10] · [Game Master lifecycle, trigger evaluation, respawn and saving][S02].

### ML-015 Explain why every live trigger is waiting or finished

**Target:** v0.25 · **Priority:** P1 · **Size:** M · **Basis:** New diagnostic interface

**Dependencies:** ML-004, ML-006, ML-007

**Why:** A static warning cannot explain a valid trigger's current state during a mission.

**Implement:** Add a read-only trigger inspector and dependency view using runtime snapshots. Show excluded by chance, waiting for a condition, counting down, fired, cooling down, exhausted and action failed/unconfirmed as distinct states. Keep Fire now separate.

**Acceptance criteria:**

- [ ] The inspector can say Waiting for the phone, 1 of 2 living players inside, 12 seconds left, or Dependency 4 was excluded, with actual runtime values.
- [ ] Selecting a trigger highlights its prerequisites and affected enemies/doors/extraction.
- [ ] Displayed repeat counts and countdowns match the runtime and survive browser refresh.
- [ ] A failed action is distinct from an unmet trigger condition.
- [ ] The provided diagnostic mission demonstrates every supported state.

**Evidence/context:** [Game Master lifecycle, trigger evaluation, respawn and saving][S02] · [Browser editor and Mission check][S10].

### ML-016 Rehearse mission logic and save author acceptance tests

**Target:** v0.25 · **Priority:** P1 · **Size:** M · **Basis:** Extension of Dry run with new scenario assertions

**Dependencies:** ML-004, ML-007, ML-008, ML-010, ML-015, ML-027

**Why:** Authors need a quick way to preserve their intended rules as scenarios grow and MissionLab changes.

**Implement:** Run the same pure rule evaluator against a simulated clock and injected observations such as intel collected, zone occupied, tagged enemy down and player death. Let an author save event traces with expected outcomes beside a scenario. Start with exact logic, without tactical AI simulation. The initial browser rehearsal calls that evaluator through the existing local plugin/API while MissionLab is running; a mission need not be active. Standalone offline HTML continues to support authoring and static validation. A browser-native evaluator is later work requiring parity fixtures.

**Acceptance criteria:**

- [ ] An author can test that collecting the phone starts the countdown, that an optional branch cannot remove extraction and that the correct player-count variant is selected.
- [ ] Each assertion identifies the scenario revision and records expected versus actual state/action.
- [ ] The evaluator used for rehearsal and live decisions shares the same contract and fixtures; browser presentation cannot silently introduce different rule semantics.
- [ ] A changed scenario reruns its saved assertions and explains failures by step/trigger.
- [ ] The interface states which events are injected assumptions. It does not claim to predict spotting, pathfinding, physics, networking or combat results.

**Evidence/context:** [Existing ideas and their implementation status][S13] · [Core test harness][S09] · [Game Master lifecycle, trigger evaluation, respawn and saving][S02].

### ML-017 Add previewable repairs for selected Mission check findings

**Target:** v0.25 · **Priority:** P1 · **Size:** M · **Basis:** Existing unbuilt idea developed

**Dependencies:** ML-001, ML-007, ML-014

**Why:** Warnings are useful; a limited, understandable repair can help an author complete the task.

**Implement:** Implement a small first set: snap to nearby walkable ground, separate overlapping placements and repair an unambiguous reference. Show the proposed edit before applying. Reuse the existing undo mechanism for authored changes.

**Acceptance criteria:**

- [ ] Each supported finding offers a before/after preview and a bounded search area.
- [ ] Apply and Undo restore the full authored change, including grouped positions and references.
- [ ] Validation runs again afterward; unresolved findings remain visible.
- [ ] No valid repair within bounds produces an explanation rather than an arbitrary move.
- [ ] A repair never silently changes a required goal, opens extraction or sends an irreversible live action.

**Evidence/context:** [Browser editor and Mission check][S10] · [Existing ideas and their implementation status][S13].

### ML-018 Let authors name meaningful places on a map

**Target:** v0.25 · **Priority:** P1 · **Size:** S–M · **Basis:** Develops named-location prerequisites in the idea list

**Dependencies:** ML-007, ML-008, ML-027

**Why:** Names such as Rear stairs or Upstairs office make rules, diagnostics and briefings easier to understand than coordinates.

**Implement:** Add author-defined points or small areas with a label and floor/height bounds. Use those labels in trigger explanations, module anchors, notes and debriefs. Keep locations tied to a specific local map fingerprint.

**Acceptance criteria:**

- [ ] Two vertically overlapping places remain distinct by floor/height.
- [ ] Renaming a place does not break stable references.
- [ ] Unknown/changed map fingerprints produce a rebind review instead of assuming positions remain valid.
- [ ] Scenario export contains only authored labels, coordinates and references; it does not bundle extracted geometry.
- [ ] Location names appear consistently in the relevant editor and replay views.

**Evidence/context:** [Existing ideas and their implementation status][S13] · [Scenario model, serialization and normalization][S06] · [Browser editor and Mission check][S10].

### ML-019 Save and reuse complete encounter modules

**Target:** v0.25 · **Priority:** P1 · **Size:** M · **Basis:** New generalization of squads and ready-made trigger sets

**Dependencies:** ML-007, ML-014, ML-015, ML-018

**Why:** A useful encounter often consists of enemies, a patrol, a trigger and a message that should remain connected when reused.

**Implement:** Save a selected encounter relative to an anchor. Begin with enemies, patrol routes, triggers and messages. Remap internal IDs when inserting it; require explicit binding of external doors, objectives and named places. Insert as an independent copy initially, avoiding a live inheritance system.

**Acceptance criteria:**

- [ ] Placing the same module twice creates independent IDs and no cross-wired triggers.
- [ ] Translation and rotation consistently transform positions, facing and patrol routes.
- [ ] A module with external bindings cannot be exported as a valid scenario until required bindings are supplied.
- [ ] One module can be used on another map only after local placement validation and rebinding.
- [ ] Ship a two-guard alarm response and a hold-zone encounter as small examples.

**Evidence/context:** [README, setup instructions and recorded co-op scope][S12] · [Existing ideas and their implementation status][S13] · [Scenario model, serialization and normalization][S06].

### ML-020 Ship three polished missions and a guided first edit

**Target:** v0.25 · **Priority:** P1 · **Size:** M · **Basis:** Content and usability

**Dependencies:** ML-011, ML-012, ML-013, ML-015, ML-017

**Why:** Finished missions make the toolkit's value visible and provide repeatable regression content.

**Implement:** Create a quiet intel raid, an escalating defense and a Game Master-oriented mission using demonstrated map/mode combinations. Start on Clean House and add other maps after their acceptance runs. Build a short walkthrough around changing one encounter in a supplied mission.

**Acceptance criteria:**

- [ ] Each mission has a short briefing, clear completion/failure rules, intended squad size and observed duration from playtests.
- [ ] At least one run with an unmodded client completes each mission, followed by save/reload and another run.
- [ ] A first-time host can install, select a mission, change an encounter, fix a warning, save and reopen it without the author intervening.
- [ ] The missions exercise different pacing goals and remain playable when optional content is absent.
- [ ] Record what testers found confusing and address the highest-friction step before adding tutorial length.

**Evidence/context:** [Latest recorded playtest findings][S11] · [README, setup instructions and recorded co-op scope][S12].

### ML-021 Add sealed missions with a spoiler-free Play view

**Target:** v0.26 candidates · **Priority:** P2 · **Size:** S–M · **Basis:** Existing unbuilt idea developed

**Dependencies:** ML-008, ML-011, ML-013

**Why:** The person hosting should be able to discover a mission alongside the squad.

**Implement:** Show briefing, requirements, selected options and validation status while hiding placements, actual rolls and future trigger outcomes. Reveal authoring detail only through an explicit mode change or after the run.

**Acceptance criteria:**

- [ ] A host can select, validate and run a supplied sealed mission without seeing enemy locations.
- [ ] Refreshing or opening another normal Play tab does not accidentally reveal hidden details.
- [ ] Errors and unsupported capabilities remain visible without unnecessary tactical spoilers.
- [ ] Editing or explicitly revealing the mission changes the visible state honestly.
- [ ] The feature is described as spoiler avoidance; locally readable files remain readable.

**Evidence/context:** [Existing ideas and their implementation status][S13].

### ML-022 Offer bounded host options and a quick mission form

**Target:** v0.26 candidates · **Priority:** P2 · **Size:** M · **Basis:** Existing unbuilt ideas combined into one entry flow

**Dependencies:** ML-007, ML-008, ML-013, ML-016

**Why:** Hosts should be able to adjust a mission's length or intensity without editing its trigger graph.

**Implement:** Let authors expose a small number of named presets such as garrison size, alarm behavior or reinforcement allowance. Add a quick form over the existing generator for enemy count/mix and demonstrated extras. Compile choices to the scenario/resolved setup and validate them before play.

**Acceptance criteria:**

- [ ] Every offered preset produces a valid resolved setup for its declared squad range.
- [ ] Options appear in the run record and can be reproduced.
- [ ] An option cannot silently remove a mandatory target or extraction dependency.
- [ ] Generate, preview, reroll and keep a setup work through one local flow.
- [ ] Labels describe concrete changes; difficulty claims remain author suggestions until measured.

**Evidence/context:** [Existing ideas and their implementation status][S13] · [Navigation-aware generator][S15] · [Random layout extras][S16].

### ML-023 Add constrained scatter and authored placement areas

**Target:** v0.26 candidates · **Priority:** P2 · **Size:** M · **Basis:** Existing unbuilt ideas

**Dependencies:** ML-001, ML-007, ML-008, ML-010, ML-027

**Why:** Controlled local variation preserves the shape of an encounter while making repeat runs less predictable.

**Implement:** Implement scatter radius first, then optional painted allowed/empty areas. Resolve placements with seeded randomness and enforce navigation, floor, spacing and insertion constraints. Define a bounded fallback when no valid placement exists.

**Acceptance criteria:**

- [ ] Same version, inputs and seed yield the same resolved placement.
- [ ] A placement never escapes its allowed area or silently moves to a different floor.
- [ ] A failed search uses an explicitly declared fallback or rejects the setup; it cannot erase a required target.
- [ ] The editor previews the eligible region and reports which constraints prevented placement.
- [ ] Validation across representative seeds records counterexamples and never describes sampling as proof of every possible roll.

**Evidence/context:** [Existing ideas and their implementation status][S13] · [Navigation-aware generator][S15].

### ML-024 Deliver a useful debrief and an unused-content report

**Target:** v0.26 candidates · **Priority:** P2 · **Size:** M · **Basis:** Existing unbuilt ideas developed

**Dependencies:** ML-008, ML-009, ML-018, ML-020

**Why:** The recording should help the entire group understand the session and help the author improve it.

**Implement:** Produce a short factual debrief and a richer local author report. Start with elapsed time, first recorded contact, objective/trigger milestones, confirmed outcomes and unused triggers. After several comparable runs, list consistently unused encounter content.

**Acceptance criteria:**

- [ ] Every number has a defined event source; unavailable health/shots/door-use data is not invented.
- [ ] Runs are grouped only when their scenario and effective setup are meaningfully comparable.
- [ ] Known editing removals do not count as combat success.
- [ ] Test the timing of an in-game debrief with an unmodded client; provide a local text summary if the game's end-screen timing prevents it.
- [ ] Unused content is presented as an author review suggestion, since a guard can contribute tension without engaging.

**Evidence/context:** [Existing ideas and their implementation status][S13] · [Replay collection and events][S08] · [Latest recorded playtest findings][S11].

### ML-025 Add an optional Game Master budget and action cards

**Target:** v0.26 candidates · **Priority:** P2 · **Size:** M · **Basis:** Existing unbuilt ideas developed

**Dependencies:** ML-003, ML-006, ML-009, ML-015, ML-019

**Why:** A smaller set of bounded actions can make directing a session easier and more consistent.

**Implement:** Begin with a local budget, cooldowns and author-defined actions. Apply player-distance and supported visibility rules to manual reinforcements. Build a small card presentation on top only after the action policies work. The host selects the mode for the session.

**Acceptance criteria:**

- [ ] The same server-side action checks apply regardless of which UI sends the command.
- [ ] A rejected or unconfirmed action does not silently consume budget; a retry cannot spend twice.
- [ ] Cards show their effect, cost and reason for being unavailable.
- [ ] Unavailable safe placement produces an explicit wait/fallback outcome rather than a hidden unsafe spawn.
- [ ] The session records manual interventions and budget changes so the debrief can explain them.

**Evidence/context:** [Existing ideas and their implementation status][S13] · [Live browser request queue and editor serving][S05] · [Game Master lifecycle, trigger evaluation, respawn and saving][S02].

### ML-026 Export portable scenario packs with a local import preview

**Target:** v0.26 candidates · **Priority:** P2 · **Size:** S–M · **Basis:** Practical extension of existing sharing ideas

**Dependencies:** ML-007, ML-008, ML-012, ML-013

**Why:** Players can exchange missions through ordinary files before the project needs any hosted catalogue.

**Implement:** Export scenario JSON, authored metadata, required capabilities and optional authored encounter modules as a versioned pack. Import into a preview, validate against the recipient's local map/game state and avoid silent overwrites. A share-link wrapper can be considered after the file contract works.

**Acceptance criteria:**

- [ ] The manifest explicitly lists included files and required map/mode/mod capabilities.
- [ ] The pack excludes game binaries, raw navigation data and exported game geometry.
- [ ] Import reports unsupported features and unresolved map bindings before installation.
- [ ] A colliding scenario identity offers a separate revision or copy instead of silently replacing local work.
- [ ] A recipient can reproduce the authored setup when compatible local inputs are available, while incompatibilities are shown clearly.

**Evidence/context:** [Existing ideas and their implementation status][S13] · [README, setup instructions and recorded co-op scope][S12].

### ML-027 Paint custom trigger areas with floors and exclusions

**Target:** v0.25, early authoring priority · **Priority:** P1 · **Size:** M–L · **Basis:** Explicit user requirement

**Dependencies:** ML-004, ML-007, ML-010, ML-014. Geometry and offline authoring can start before staged live edits are complete; ML-014 is needed for the final live-application acceptance.

**User outcome:** Create a trigger covering the interior of an irregularly shaped house without also including its garden, neighboring street or courtyard. Choose a single floor, selected rooms or a whole house with multiple floors. The existing enter, hold and everyone rules operate on the authored area.

**Current behavior:** v0.23 checks a circle in the horizontal plane with a fixed height tolerance of two metres above/below its position. PlayerInside and EveryoneInside duplicate that check. Serialization, drawing, editing and several Mission check calculations also assume a position and radius. This feature therefore changes the complete shape contract, not only the drawn outline. [Runtime containment][S02] · [Scenario format][S06] · [Editor][S10]

#### ML-027A Define one area model and containment contract

Represent a custom area as a union of polygon regions. Each region has an outer outline in world X/Z coordinates, optional interior holes and an explicit vertical interval. Support concave outlines and disconnected pieces. Different floors may have different outlines. Preserve world-space authored boundaries and map identity; do not make saved areas depend on transient navmesh triangle indices or raw brush strokes.

Define one pure containment contract in the core and matching editor fixtures. Specify edge inclusion, hole-edge behavior, numerical tolerance and vertical-boundary behavior. Use cached bounds before precise checks; do not perform brush union or geometry reconstruction in every game update. Keep the verified host-observed actor position convention explicit and test standing, crouching, stairs and thresholds. The camera entering a room alone must not activate a player-area trigger.

Retain legacy circles with their existing horizontal and vertical behavior. Custom shapes require a supported format/capability. Verify that a custom-area export cannot be silently interpreted as an ordinary circle by an older loader: v0.23 ignores unfamiliar fields and does not enforce the schema marker. Choose an actual incompatible envelope or other tested format boundary if necessary; a new version label alone is insufficient. This compatibility decision belongs to ML-007.

#### ML-027B Add manual authoring tools

Provide Brush with adjustable size, Erase, Rectangle and Polygon. Brush strokes add to the selected area; erasing removes coverage and can create a courtyard hole or separate pieces. Rectangle is a shortcut for simple rooms; polygon/vertex handles allow precise wall and doorway boundaries. All tools produce the same saved area representation.

Show the final outline and fill throughout editing. One completed stroke or shape edit is one undo step. Allow translation, rotation and vertex editing. Bound the number of points and simplify only within a documented tolerance that cannot silently bridge an excluded doorway, courtyard or passage. Reject invalid geometry with a useful finding.

#### ML-027C Make floors and connected spaces deliberate

Painting targets the selected floor/height band. Optional snapping or clipping to the selected local walkable surface assists placement; the visible outline remains the authoritative authored boundary. Walkable connectivity does not identify a room: an open doorway may connect the house to the street.

A whole-house trigger combines separately authored ground-floor, upstairs and stair-connection regions under one logical area. Moving between touching or overlapping included parts must not create a false exit because of an internal seam. An actual excluded gap still counts as leaving. Balconies, courtyards, upper-floor voids and adjacent buildings are included only when deliberately painted. Show the selected volume and excluded parts in 3D for inspection.

#### ML-027D Use the area in every relevant trigger path

Use the same membership rule for entering an area, holding it and requiring everyone inside. For a multipart house, everyone means every eligible living player is somewhere in the union; it does not require a player in every room. No living players remains false. Preserve the established rules for players already inside at startup, hold handoffs and repeating triggers while occupants remain inside; geometry alone must not redefine those event semantics.

Update serialization, parsing, import/export, undo, duplication, live synchronization, trigger inspection, rehearsal and replay snapshots. A live area edit is one validated change. Define its effect on an unfinished hold/occupancy evaluation; reset that unfinished progress when applied, while preserving completed firing history unless the host explicitly resets it.

Retain a separate authored anchor for labels and fallback rally locations. A concave polygon's centroid may lie outside its area or in a hole, so it must not automatically replace the old position field. An order destination uses the actual entrant location or a validated authored fallback.

Update reinforcement pop-in checks to use the real area and intended floors. Where visibility checks sample boundary/interior positions, report that limitation; a single check from the old circle centre cannot establish coverage of the whole house.

#### ML-027E Preserve and reuse the geometry

Use this spatial primitive for ML-018 named areas and, where appropriate, ML-023 allowed/empty placement regions. Their meanings remain separate: a trigger area detects occupants, while a placement area constrains generation. ML-019 encounter transforms must preserve all outlines, holes, height bands and references when translating, rotating or copying an encounter.

**Acceptance criteria:**

- [ ] Paint an L-shaped house interior as one trigger. Entering either wing activates it; the excluded outdoor corner does not.
- [ ] Paint and erase a courtyard hole. A player standing in it remains outside the trigger after save/reload and live synchronization.
- [ ] A ground-floor area excludes an upstairs player and the neighboring street. A whole-house area includes only its selected floors and deliberately connected stairs.
- [ ] Brush, eraser, rectangle and polygon produce the same canonical area representation; edit handles and undo preserve every part and hole.
- [ ] C# containment, editor checks and rehearsal agree on fixture points inside, outside, on edges, in holes and on vertical boundaries.
- [ ] Enter, hold, everyone, delays and repeats retain their documented behavior. Moving between included adjacent parts does not spuriously reset a hold; crossing a true excluded gap does.
- [ ] Existing circle scenarios behave identically. Unsupported custom-area files are rejected through a compatibility boundary tested against the older parser, without a bounding-circle fallback.
- [ ] Live application treats geometry as one validated edit, clears unfinished occupancy/hold progress as documented and preserves completed trigger history.
- [ ] Self-intersections, non-finite coordinates, invalid holes, inverted height bounds, negligible area and excessive complexity produce actionable findings before application.
- [ ] Trigger inspection, Mission check, undo, saved resolved setups, replay snapshots and encounter transforms all retain the authored shape and intended floor coverage.
- [ ] Runtime membership checks use preprocessed bounds/geometry and are measured with several complex areas and a supported co-op group; a detailed paint stroke does not rebuild geometry every update.
- [ ] A host and unmodded client verify the L-shaped house, courtyard, floor separation, stairs and everyone/hold cases. Standing beside an exterior wall does not activate the interior area.

**Later convenience:** Add Suggest this room or Suggest this building floor using locally available navigation and collider geometry. Always preview the proposed boundary so the author can correct it. Doorways, missing colliders, stairs and courtyards make fully automatic room recognition uncertain; manual brush/erase/polygon support fulfills the initial requirement.

**Evidence/context:** [Scenario model][S06] · [Runtime membership checks][S02] · [Editor drawing and validation][S10].

## Additional and expanded ideas

These ideas complement the existing backlog. Entries already promoted to tickets are identified so they do not become duplicate implementation work. Existing squads, waves, trigger recipes, briefings, Mission check and replay remain the foundation.

### IDEA-01 Explain live trigger state

**Recommendation:** Should build · **Size:** M · **Placement:** Promoted to ML-015

Show why a running trigger is waiting, excluded, counting down, cooling down, exhausted or unable to apply an action. This makes mission debugging a visible user workflow.

**Minimum useful version:** A read-only inspector with actual prerequisite values and click-through to affected objects.

**Acceptance or proof:** Demonstrate every state with a small diagnostic scenario and show that a browser refresh preserves the explanation.

**Dependencies:** ML-004, ML-006.

**Relationship to existing work:** New interface beyond the existing static Mission check; related to the existing Dry run proposal.

### IDEA-02 Validate many seeds and squad sizes at once

**Recommendation:** Should build after resolved runs · **Size:** M · **Placement:** Next addition after ML-016

Resolve a scenario across, for example, 100 seeds and selected supported squad sizes. Report a concrete counterexample when a required target disappears, an extraction dependency is disabled or a placement violates a constraint.

**Minimum useful version:** A bounded local sweep of structural rules, with each failure opening the exact seed, player count and resolved setup.

**Acceptance or proof:** Known broken fixtures are caught and every reported counterexample reproduces. Small finite choice sets may be exhaustive; larger sampled sets explicitly state coverage limits.

**Dependencies:** ML-001, ML-008, ML-010, ML-016.

**Relationship to existing work:** New regression workflow. It complements Score card and Dry run without claiming to prove combat balance or all possible outcomes.

### IDEA-03 Preserve local scenario revisions and playtest notes

**Recommendation:** Should build · **Size:** S–M · **Placement:** Included in ML-014

Save named revisions such as Before moving the rear patrol and attach short notes plus recordings to the exact revision. Show a semantic diff of gameplay changes.

**Minimum useful version:** Explicit Save revision, reopen as draft, short text notes and a link to the resolved run.

**Acceptance or proof:** Old revisions reopen without overwriting current work; changes in map/build/options are visible before comparing runs.

**Dependencies:** ML-007, ML-008, ML-009.

**Relationship to existing work:** New workflow supporting existing replay comparison and unused-content ideas.

### IDEA-04 Export a reproducible failure bundle

**Recommendation:** Should build · **Size:** S · **Placement:** Included in ML-012

Package the scenario, resolved setup, versions, capability results and relevant errors so a broken mission can be diagnosed without a long back-and-forth.

**Minimum useful version:** Local opt-in export with a manifest and a preview of included text files; recording optional.

**Acceptance or proof:** Opening the exported resolved setup through ML-008's reproduction flow presents the same plan or validation failure when matching local map inputs are available. Missing local assets are identified. Game assets and unnecessary personal data are excluded.

**Dependencies:** ML-007, ML-008.

**Relationship to existing work:** New support workflow; no hosted telemetry or automatic sending is needed.

### IDEA-05 Preview a batch of live changes

**Recommendation:** Should build · **Size:** M · **Placement:** Promoted to ML-014

Stage several enemy moves and a door change, review their combined effect, then apply once with a receipt per operation.

**Minimum useful version:** Draft/live state, grouped Apply and a clear partial-result summary.

**Acceptance or proof:** Draft changes do not affect the game; retries do not duplicate actions; an unsuccessful apply never looks successful merely because a browser marker moved.

**Dependencies:** ML-006, ML-007.

**Relationship to existing work:** New live-authoring workflow. Draft undo and reversing completed game effects have different guarantees.

### IDEA-06 Reuse encounters as coherent modules

**Recommendation:** Should build after format stability · **Size:** M · **Placement:** Promoted to ML-019

Keep enemies, routes, triggers and messages together as an encounter. A reusable checkpoint alarm can be placed twice or adapted to another map.

**Minimum useful version:** Anchor-based transforms, internal ID remapping and explicit external bindings; insert independent copies.

**Acceptance or proof:** Two copies remain independent and external doors/objectives must be rebound before the scenario passes validation.

**Dependencies:** ML-007, ML-014, ML-018.

**Relationship to existing work:** New generalization of existing squads and ready-made trigger sets.

### IDEA-07 Save tests that express a mission author's intent

**Recommendation:** Should build after the trigger evaluator · **Size:** M · **Placement:** Promoted to ML-016

An author states that collecting the phone starts a countdown, an optional event cannot block extraction and a two-player setup excludes the extra squad. Those expectations become runnable event traces.

**Minimum useful version:** A handful of supported observations, a simulated clock and expected state/action assertions.

**Acceptance or proof:** Changing a prerequisite or dependency causes an understandable failed assertion. Live and rehearsal logic agree on the same fixture trace.

**Dependencies:** ML-004, ML-007, ML-010.

**Relationship to existing work:** New focused extension of the proposed Dry run; evaluates mission rules using injected events.

### IDEA-08 Require co-op conditions to remain true together

**Recommendation:** Promising gameplay addition · **Size:** M · **Placement:** Prototype after ML-016

Require one player to hold a power room while another occupies a terminal zone, or keep two areas occupied together for 20 seconds. Continuous conditions create coordination challenges.

**Minimum useful version:** Two occupancy conditions, a shared continuous timer and an explicit reset-or-pause rule. Reuse verified actions when the goal completes.

**Acceptance or proof:** The timer advances only while all conditions are currently true. Leaving, death, disconnection and player-count changes have documented behavior.

**Dependencies:** ML-003, ML-004, ML-016, ML-018.

**Relationship to existing work:** A meaningful extension: current all-of records that events happened; this feature evaluates simultaneous current state. The condition itself needs observations, not a new game mutation.

### IDEA-09 Schedule patrol changes that create infiltration windows

**Recommendation:** Feasibility experiment · **Size:** S spike, then M · **Placement:** Probe before committing to a gameplay feature

A guard leaves a doorway for a scheduled patrol change, giving the squad a deliberate infiltration opportunity.

**Minimum useful version:** One idle group, two authored multi-stop routes, a timed switch and a later return. Avoid continuously overriding combat behavior.

**Acceptance or proof:** First show that a calm enemy follows the replacement route and resumes the original, and that an unmodded client sees the movement. Record what happens if the enemy is alerted during the switch.

**Dependencies:** ML-003, ML-004, ML-011.

**Relationship to existing work:** New gameplay use of existing patrol/order work; live route switching and combat interaction are not yet established by the reviewed evidence.

### IDEA-10 Use one explicit placement policy for reinforcements

**Recommendation:** Experiment after roster and trigger fixes · **Size:** M · **Placement:** Follow-up to ML-023 and ML-025

A reinforcement group waits until its authored location is sufficiently distant and out of supported player sight checks, then spawns. If that never becomes possible, it takes an explicit fallback.

**Minimum useful version:** Minimum distance, a tested visibility check, bounded wait and author-chosen fallback/cancel behavior.

**Acceptance or proof:** Watching the spawn location postpones the wave, an eligible location allows it, and timeout takes the declared outcome. Pending/skipped enemies cannot silently satisfy a kill-all goal or erase a mandatory target.

**Dependencies:** ML-001, ML-003, ML-004, ML-009, ML-011.

**Relationship to existing work:** Develops existing pop-in checks and Game Master keep-out ideas into a shared runtime policy; co-op viewpoints and visibility behavior require an in-game probe.

### IDEA-11 Let mission alarms escalate by named area

**Recommendation:** Promising gameplay addition after telemetry validation · **Size:** M · **Placement:** Prototype after ML-015 and ML-018

A contact near the rear warehouse activates that area's authored response, while a separate area keeps its own reinforcement state until another condition occurs. This can create a mission that escalates in stages.

**Minimum useful version:** Author-defined alarm areas, tagged observers, local states such as quiet/raised and a small set of actions driven by reliable observed alert events.

**Acceptance or proof:** An alert attributed to area A activates only A's MissionLab-authored response; cross-area escalation happens only through an explicit authored rule. Startup transient enemy states do not raise the alarm.

**Dependencies:** ML-003, ML-004, ML-009, ML-015, ML-018.

**Relationship to existing work:** New authoring layer around local alarm responses. It does not claim control over the base game's own AI communication or hearing propagation.

## Playable concepts unlocked by the additions

These are content proposals, not claims that every required capability already exists.

| Concept | Player experience | Required work | First proof |
|---|---|---|---|
| Two-room operation | One player holds the power room while another occupies the terminal area; the timer advances only while both conditions hold. | IDEA-08, ML-004, ML-018 | Test leaving, dying and re-entering each room with an unmodded client. |
| Patrol change infiltration | The squad observes a guard route, waits for a scheduled change and uses the opening. | IDEA-09, ML-003, ML-011 | Demonstrate a two-route switch and define behavior when combat begins. |
| Local alarm raid | Contact in one area causes a contained authored response; a separate area escalates only after another event. | IDEA-11, ML-015, ML-018 | Distinguish actual alerted enemies from startup/transient states and confirm area attribution. |
| Unseen setup night | The host selects a briefing and a few allowed options, then discovers the resolved layout with the squad. | ML-021, ML-022, ML-023 | Validate several seeds and supported squad sizes; preserve exact resolved records. |
| Reusable checkpoint alarm | An author places a guard group, alarm condition and delayed response as one reusable encounter. | ML-019, ML-015 | Insert two copies and confirm independent trigger IDs and bindings. |

## A focused co-op verification record

Use one record per actual game/mod/map/mode combination. A successful API return is a requested-call result; a visible effect and a client observation are separate fields.

| Check | Host observation | Unmodded client observation | Result and evidence |
|---|---|---|---|
| Startup and chosen insertion | Pending | Pending | Pass, fail, untested or unavailable |
| Authored enemies and patrols | Pending | Pending | Record positions and observed movement |
| Triggered and repeated reinforcements | Pending | Pending | Record trigger times and counts |
| Door lock, unlock, open and close | Pending | Pending | Record collision/use behavior, not just appearance |
| Moved intel and collection | Pending | Pending | Verify both location and objective progression |
| Messages and countdown overlap | Pending | Pending | Verify order and any missing messages |
| Extraction gate and opening | Pending | Pending | Complete extraction after required conditions |
| Shared respawn waves | Pending | Pending | Include staggered deaths and full-squad death |
| Success and failure endings | Pending | Pending | Record actual result/lobby behavior and score flags |
| Live edits, retries and stale commands | Pending | Pending | Check duplicate and delayed effects |
| Save, reload and reproducible setup | Pending | Pending | Compare resolved content, not exact gameplay |
| Restart, map transition and cleanup | Pending | Pending | Check old work does not affect the new mission |
| Non-host and unsupported-mode behavior | Pending | Pending | Confirm the mod does not apply the custom setup |
| Rejoin or late join if the base game supports it | Pending | Pending | Otherwise mark unavailable, not failed |

## What to defer

Keep these as bounded research items until a concrete user need and the required game behavior are established:

- Individual player lives, respawn-at-checkpoint and selective revives.
- Escort/bystander behaviors, moving reinforcements and new vehicle routes.
- Automated difficulty adaptation and enemies that learn a squad's habits.
- Persistent multi-mission campaigns and detailed progression.
- Remote Game Master access, squad tablets and hosted scenario catalogues.
- A full tactical simulation or claims that a sampled seed sweep proves every mission winnable.

The repository already contains ideas in these areas. Their presence in a brainstorm is not evidence that the necessary game methods are reliable. Use one short, recorded experiment for one uncertain behavior before expanding it into a feature. [Existing ideas and parked work][S13]

## Implementation discipline

- A defect ticket includes a focused regression case for the behavior it changes.
- A format/rule change includes fixtures consumed by both editor and runtime, plus an explicit migration decision.
- A game-facing feature includes an observed result on the supported map/mode/build and an unmodded-client check when co-op behavior matters.
- A shipped feature has current compatibility status and a usable error path.
- Preserve the separation between authored alternatives, resolved choices and recorded observations.
- Add infrastructure only when an actual task cannot be completed cleanly with the current local architecture.

## Sources

All source links below refer to the reviewed v0.23 commit. The code findings are static observations; the recorded playtest outcomes are those reported by the repository. This backlog proposes implementation and acceptance work.

- [Director startup and variant resolution][S01]
- [Game Master lifecycle, trigger evaluation, respawn and saving][S02]
- [Live orders and game respawn calls][S03]
- [Plugin configuration and update error handling][S04]
- [Live browser request queue and editor serving][S05]
- [Scenario model, serialization and normalization][S06]
- [Variant resolution][S07]
- [Replay collection and events][S08]
- [Core test harness][S09]
- [Browser editor and Mission check][S10]
- [Latest recorded playtest findings][S11]
- [README, setup instructions and recorded co-op scope][S12]
- [Existing ideas and their implementation status][S13]
- [Build script][S14]
- [Navigation-aware generator][S15]
- [Random layout extras][S16]

[S01]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Director/Director.cs
[S02]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Director/GameMaster.cs
[S03]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Director/TryOuts.cs
[S04]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Director/DirectorPlugin.cs
[S05]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Director/LiveLink.cs
[S06]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Core/MissionPlan.cs
[S07]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Core/Variants.cs
[S08]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Director/Replay.cs
[S09]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/tests/CoreTest/Program.cs
[S10]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/tools/editor/index.html
[S11]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/docs/findings.md
[S12]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/README.md
[S13]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/docs/ideas.md
[S14]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/build.sh
[S15]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Core/Generator.cs
[S16]: https://github.com/Zaybrah/missionlab/blob/581c31634b922ae82a10e8a6e67114be396fc97e/src/MissionLab.Director/RandomExtras.cs

