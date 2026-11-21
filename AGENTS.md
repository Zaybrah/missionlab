# Repository Guidelines

## Project Overview
MissionLab is an unofficial, free mod toolkit for the game **OPERATOR** (Unity IL2CPP, BepInEx 6.0.0-be.785, tested with game `0.09.5`). It is made up of:
- **MissionLab.Director.dll** (`missionlab.director`, version in `DirectorPlugin.Version`). It runs only on the host and only in PvE. It replaces a mission's enemy layout with a seeded generated layout or a scenario file. It also provides Game Master mode (F8), a browser link at `http://localhost:8765`, replays and run records.
- **MissionLab.Recon.dll** (`missionlab.recon`, versioned separately in its own `Plugin.Version`). A read-only observer that writes mission contents and enemy movement to JSON.
- **Scenario editor** (`tools/editor/index.html`). A single offline page that also runs live through the browser link.

Rules that are never negotiable:
- No game files in the repo, and nothing derived from them (`*.bytes`, `.mlgeo`, interop DLLs, recon output).
- Never touch PvP modes.
- If anything fails, the mission must be left exactly as shipped.

## Architecture & Data Flow
**Compile units.** These decide what code may reference what:

| Unit | Sources | Built by | Game refs |
|---|---|---|---|
| Core (`namespace MissionLab.Core`) | `src/MissionLab.Core/*.cs` | compiled *into* Director.dll and both test projects | **none; must stay Unity-free** |
| Director (`namespace MissionLab`) | Core + `src/MissionLab.Director/*.cs` | `build.sh` | BepInEx, Il2Cpp interop, Unity, Mirror |
| Recon (`namespace MissionLab`) | `src/MissionLab.Recon/*.cs` only | `build.sh` | yes; **cannot use Core types** |

**Mission start** (`Director.cs`):
1. A Harmony postfix `RaidStartPatch` on `RaidManager.OnStartServer` (only when `[Advanced] UseHook` is set), or the 0.25 s poll in `DirectorBehaviour.Update`, calls `Director.OnRaidStart`. That calls `NewSession()` and `DirectorPlugin.Settings.Reload()`, so the config is re-read every mission.
2. `TryApply` checks its gates in this order: Enabled → host (`Mirror.NetworkServer.active`) → scene allowed → PvE mode → on the poll path, no AI alive yet. It returns `true` for "handled, don't retry" and `false` for "retry next frame" (up to 8 s).
3. It builds a pure plan:
   - **Scenario:** `ScenarioFile.Read` (strict) → `Variants.Resolve(plan, new Rng(roll ^ 0x5EED), …)` → `Completion.Check`. If the check blocks, the scenario is refused, a RunRecord is written with `outcome:"refused"`, and the game is left untouched.
   - **Generated:** `NavPicker.Pick` → `NavMesh.Load` → `Generator.Generate` → `RandomExtras.Add(…, new Rng(seed ^ 0xE87A5))`.
4. It builds **every** GameObject first and only then clears and refills `raid.botSpawnPoints`. If building throws, the root is destroyed, the exception is rethrown, and the raid is marked handled and never retried.
5. Held-back enemies (typed, triggered, HVT, vehicle) go to `GameMaster.BeginMission`. `PumpSpawns` temporarily swaps `botSpawnPoints`/`botAmount`/`customAI`, calls `raid.ServerSpawnAI`, then restores them.
6. Every frame, `GameMaster.Tick` → `CheckTriggers` runs `Engine.Step(Time.unscaledTime, GameWorld)`, and `RespawnRule` runs Core `WaveRule`.

**Seams**
- **`ITriggerWorld`** (Core) is implemented by `GameMaster.GameWorld`. The engine asks questions and the game answers. The engine has no clock and holds no game objects.
- **Main thread vs browser link threads:**
  - Core `MiniHttp` runs a loopback accept thread plus ThreadPool workers, and those call `LiveLink.Handle`. **Never touch Unity or game objects there.**
  - Game work is queued as a `Job` in a `ConcurrentQueue` and run by `LiveLink.Pump()` on the main thread: at most 8 per frame, with a 4 s wait.
  - Status codes: 202 = pending, 503 = expired, 429 = queue full, 409 = stale session.
- **LiveLink security:** `Origin` must be `localhost`/`127.0.0.1:<Port>`. Non-GET requests need `X-MissionLab: 1`. `X-MissionLab-Session` must equal `Director.Session`. `X-MissionLab-Command` ids give idempotency through `CommandLedger`.

**File formats.** Each has a hand-versioned `schema` string, and the editor reads all of them:

| Schema | Writer | Output under `BepInEx/MissionLab/` |
|---|---|---|
| `missionlab-plan/1` (scenario); `missionlab-plan/2` = envelope `{"schema":"missionlab-plan/2","mission":{body}}`, written only when a zone/hold trigger has a shaped `area`; `missionlab-plan/3` = the same envelope, written only when the scenario has `goals` or a zone/hold trigger linked to a place (`"place": <id>`, no own `area`); `missionlab-plan/4` = the same envelope, written only when a zone/hold trigger waits for another trigger (`"after": <id>`, ML-055) | `MissionPlan.ToJson`, editor `scenarioObject` | `scenarios/*.json` (subfolders are ignored, which is how a file is switched off) |
| `missionlab-mission/1` | `Director.WriteRecord` | `missions/last.json` + timestamped copies |
| `missionlab-run/1` | `RunRecord` | `runs/` (pruned to 60) |
| `missionlab-replay/1` (`semantics:4`: rows carry `who` ids, `alert` per enemy, `fire` per trigger; 0.27 adds event kinds `goal` (objective notices) and `pressure` (the pressure director's decisions) without raising `semantics`; 1–3 still open) | `Replay` | `replays/` |
| `missionlab-recon/1`, `missionlab-movement/2` | Recon `ReconDump`, `MovementTracker` | `recon/` |
| `missionlab-revision/1` | `Revisions` (page: Save revision) | `revisions/<scenario id>/r<n>.json` |
| `missionlab-rehearsal/1` | Core `Rehearsal.Write`, page | `rehearsals/<scenario id>/<name>.json` (never under `scenarios/`: every `*.json` there is read as a scenario) |
| `missionlab-module/1` (coordinates relative to an anchor; `docs/modules.md`; shape check in Core `ModuleFile`) | page, `Modules` | `modules/*.module.json`; examples in `examples/modules/` |
| `missionlab-pack/1` (scenarios and modules as exact texts plus a manifest; `docs/modules.md`) | Core `Pack.Write`, page (Export pack) | the player's download; `POST /api/pack/install` puts its files into `scenarios/` and `modules/`, never over a file |
| `missionlab-playlist/1` (ML-049) | Director `Playlists` (model: Core `Playlist`), page (Play: Session playlist) | `playlists/<name>.json`; the one in use is named in `playlists/selected.txt` |
| `missionlab-operation/1` (ML-051) | Director `Operations` through Core `OperationStore` (temporary file, the current file kept as `<name>.prev.json`, then a move; a damaged file is restored from `.prev.json` and its text kept as `<name>.json.damaged`; a file of a later version is left untouched) | `operations/<name>.json`; the one in use is named in `operations/selected.txt` |
| `missionlab-validation/1` (ML-048) | Director `Validations` (Core `MissionValidation`) | `validations/<scenario id>/last.json`, and the first failing combination as `counterexample.json` |
| `missionlab-capabilities/1` (ML-036) | by hand: `docs/capabilities.json` in the repo; read by Core `CapabilityRegistry` | nothing: embedded in Director.dll (`tools/build.py`), inlined into the page (`tools/editor/build.py`), and the README Status table is made from it (`tools/status.py`) |
| `missionlab-proposal/1` (ML-053: the exact base text and the exact proposed text of a scenario change from a coding agent) | Core `AgentDraft.ProposalJson`, Director `AgentApi` | `proposals/<id>.json` (never under `scenarios/`, never sent to the running mission) |
| `.mlgeo` (`MLGEO2\n\0`) | `MapExport` | `maps/` (local only) |

The navmesh input is `OPERATOR_Data/NavMeshData/*.bytes`: a zip holding `graph0_extra.binary`, with vertices stored in Int3 millimetres. It has **three parsers that must change together**: `src/MissionLab.Core/NavMesh.cs`, the editor's `parseTiles`, and `tools/preview/navmesh.py`. The layout is described in `docs/findings.md`.

## Key Directories
- `src/MissionLab.Core/`: pure logic.
  - Some names hide what the file holds: the JSON escaper `Guard.Esc` lives in `Resilience.cs`, and `V3` lives in `NavMesh.cs`.
  - `ScenarioFile` is the strict reader. `MissionPlan` is the model, the writer and the *lenient* reader.
  - `Area.cs` is the painted-area model and the one membership rule (`Membership.Inside`) used by the plugin, rehearsal and (mirrored in `tools/editor/src/21a-area.js`) the editor; `tests/fixtures/area_points.json` is their shared contract.
  - `TriggerEngine.Explain` gives the page's per-trigger sentence; `Rehearsal` runs the same engine on a scripted clock.
  - `TriggerPlan.NeverSeen` (`MissionPlan.cs`) says when an `outcome` trigger waits for a result its watched trigger can never report; the editor's `outcomeNeverSeen` (`34-check.js`) is its mirror.
  - New in 0.27: `Goals.cs` (`GoalBook`, the one implementation of objectives, used by Completion, Rehearsal and GameMaster), `Capabilities.cs` (`Capabilities.Of`, the feature words a plan uses; `CapabilityRegistry`, the reader of `docs/capabilities.json`), `RepeatSetup.cs` (seed choice and recorded-input comparison for repeating a generated run), `OrderWatch.cs` (the stages and ending word of a walking order), `InputOwnership.cs` (what Game Master holds and must give back; `GameMaster.GiveBack` is the one release path), `MissionValidation.cs` (the validation matrix), `Playlist.cs` and `Operation.cs`. `OperationStore` in `Operation.cs` does the operations' file IO (the atomic save and recovery above).
  - `Pressure.cs` is the pressure director (ML-052): it decides from what the host hands it (time, active goals, living players and enemies, the budget), has no clock or randomness, and fires nothing itself; `GameMaster` plays its choice through the existing card path (`ActionRequest`). An unknown observation means hold.
  - `AgentDraft.cs` (ML-053) applies a coding agent's operations to a copy and judges the result with the strict reader, Completion (only newly introduced problems refuse) and the capability registry; a stale hash or revision is refused before anything is applied.
- `src/MissionLab.Director/`: the plugin.
  - `GameMaster.cs` is one ~2350-line static class split by `// ---- name` dividers (life cycle, markers, triggers, doors, live spawning, keys, browser editor, saving, overlay).
  - `TryOuts.cs` holds experiments that run only from an explicit browser `{"cmd":"try"}`. It also holds shared helpers (`Message`, `WalkTo`, `RespawnNow`, `End`).
  - `Validations.cs`, `Playlists.cs` and `Operations.cs` hold the `/api/validate*`, playlist and `/api/operations*` routes and their folders. `Operations.BeforeMission` runs after `Playlists.BeforeMission`.
  - `AgentApi.cs` holds the `/api/agent/*` routes (ML-053, `docs/agent-api.md`). They queue nothing for the game, so they need no session header; a repeated command number is carried out once through `LiveLink.Ledger`.
- `src/MissionLab.Recon/`: the observer, with its own minimal JSON writer `J` (`Json.cs`).
- `tests/Pure/`: the asset-free suite, run by CI; each 0.27 ticket has its own `MlNNNTests.cs`. `tests/CoreTest/`: checks that need a real map. `tests/fixtures/`: the format contract (`schema/valid` and `schema/invalid` hold the JSON Schema cases).
- `tools/editor/`: the editor page (built from `tools/editor/src/*` by `tools/editor/build.py`; parts are fragments of one IIFE, sorted by name) plus a vendored `three.min.js` (MIT). `tools/preview/`: Python/matplotlib plan renders. `tools/package.py`: builds the release ZIP. `tools/build.py`: builds the plugins. `tools/status.py`: the README Status table from `docs/capabilities.json`. `tools/schema_check.py`: `docs/scenario.schema.json` run over the examples and fixtures with `jsonschema`. `tools/agent/`: `missionlab_agent.py`, the coding-agent adapter (MCP, JSON-RPC 2.0 over stdio, standard library only, 127.0.0.1 only; `--selftest` runs against a stand-in link it starts itself), and `example.py`, a scripted client; repository tools, not in the ZIP.
  - Editor parts added in 0.27: `27b-history.js` (Move with everything; the history API itself is in `27-editing.js`), `27c-walls.js` (wall snapping, Suggest room), `29i-graph.js` (mission graph), `29j-walk.js` (walkthrough and area probe), `29k-outliner.js` (floors and rooms), `29l-validate.js`, `29m-recipe.js` (Intel raid recipe), `29n-playlist.js`, `29o-goals.js` (objectives), `29p-operations.js`, `29q-proposals.js` (agent proposals). The pressure director's authoring and Direct controls are in `29h-cards.js`.
- `tests/editor/run.mjs`: the editor's headless checks (Node 18+, no packages), part of `./test.sh`; per-ticket files in `tests/editor/cases/` run after the shared checks, in name order.
- `tests/browser/run.mjs`: the page in a real Chromium or Edge over the DevTools Protocol (Node 22+, no packages; `fixture.mjs` stands in for the plugin's browser link). Exit 2 means no browser or Node too old.
- `examples/`: shipped scenarios (top level only; tests strict-read each, check Completion, the round trip and trigger circles). `examples/modules/`: encounter modules. `examples/rehearsals/`: saved rehearsals for the examples.
- `docs/`: `backlog.md` (ML-NNN tickets), `backlog-status.md` (where each ticket stands), `capabilities.json` (what has been seen in the game), `findings.md` (game internals seen in practice), `coop-record.md` (the co-op record to fill in), `first-edit.md`, `modules.md`, `agent-api.md` (the coding-agent routes and proposals), `ideas.md`, `research-*.md`, `scenario.schema.json`.

## Development Commands
```bash
./test.sh                                                   # CI equivalent: tests/Pure, editor page in sync (tools/editor/build.py --check), README Status table in sync (tools/status.py --check), JSON Schema check (tools/schema_check.py), coding-agent adapter self-test (tools/agent/missionlab_agent.py --selftest), editor checks (node tests/editor/run.mjs), browser checks (node tests/browser/run.mjs); no game files
dotnet run --project tests/Pure -c Release                  # the C# suite alone from a Windows shell
node tests/editor/run.mjs                                   # the editor checks alone: fixtures, strict-reader parity with the plugin, editor -> C# -> editor round trip, tests/editor/cases/*
node tests/browser/run.mjs                                  # the page in headless Chrome/Edge (CHROME_PATH to choose one); Node 22+; exit 2 = no browser or Node too old
python3 tools/status.py --write                             # rewrite the README Status table after changing docs/capabilities.json (--check: exit 1 when stale)
python3 tools/schema_check.py                               # docs/scenario.schema.json over examples and fixtures; needs pip install jsonschema; exit 2 without it
python3 tools/agent/missionlab_agent.py --selftest          # the MCP adapter against its own stand-in browser link; no game
dotnet run --project tests/Pure -- --read examples/*.json   # strict-read scenario files exactly as the plugin does; exit 1 if refused
./test.sh --map <NavMeshData/x.bytes> <recon/x.late.json>   # adds CoreTest: generator determinism + placement rules, seeds 1 2 3
dotnet run --project tests/CoreTest -c Release -- --check <scenario.json> <navmesh.bytes> [recon.json]  # placement report (lenient reader)
OPERATOR_REFS=/path/to/OPERATOR ./build.sh                  # -> dist/MissionLab.Director.dll, dist/MissionLab.Recon.dll (or: python3 tools/build.py --refs <dir>)
python3 tools/package.py --check                            # -> dist/MissionLab-<Version>.zip, unpacked and verified
python3 tools/preview/render_plan.py <nav.bytes> <recon.json|-> <plan_dir> <out.png> "<title>" 1 2 3
```
- **`build.sh`:**
  - A thin wrapper around `tools/build.py` (Python 3 stdlib; Windows, Linux, macOS), run with the first working `python3`/`python`. Arguments pass through.
  - References come from `--refs <dir>` or `OPERATOR_REFS`; the OPERATOR game folder itself works. It must contain `dotnet/` (the game's .NET 6 BCL), `BepInEx/core/` and `BepInEx/interop/`; missing folders or key assemblies stop the build with exit 2 before compiling. `refs/` is gitignored for this.
  - Only managed DLLs are referenced (native ones such as `coreclr.dll` make csc fail with CS0009), through a response file `dist/refs.rsp` (400+ refs overflow the Windows command line).
  - `csc.dll` is taken from `CSC` if set, else from the newest SDK in `dotnet --list-sdks` that has `Roslyn/bincore/csc.dll`.
- **CI** (`.github/workflows/test.yml`) sets up .NET 8, Python 3 with `jsonschema` and Node 22 and runs `./test.sh` with `CI` set. There a missing Node, a skipped .NET leg of the editor round trip, a missing `jsonschema` or a browser suite that cannot run (exit 2) fails the run; locally each is skipped with a note. Director and Recon compile errors show up **only** in `build.sh`.
- There is no linter or formatter config. Match the style of the surrounding code.
- `CoreTest` crashes when run with no arguments. Use `test.sh --map` or the modes listed at the top of `tests/CoreTest/Program.cs`.

## Code Conventions & Common Patterns
**C# layout**
- Block namespaces only, never file-scoped: `namespace MissionLab.Core { }` in Core and `namespace MissionLab { }` in plugins.
- Allman braces, 4-space indent. Dense one-line blocks (`{ a; b; return; }`) and long lines are house style.
- **Explicit `using`s in every `src` file.** The test csprojs turn on `ImplicitUsings`, which hides missing usings that then break `build.sh`.
- **No string interpolation in `src`** (there are zero `$"` today). Use concatenation. Tests may use it.
- Naming:
  - private statics are `_camelCase`; consts and internal statics are PascalCase;
  - classes are mostly `internal static class`, and only plugin and behaviour classes are public;
  - Core models are `public sealed` with public mutable fields and inline defaults.
- Use string constants rather than enums for anything that reaches JSON (`TriggerPlan.OnZone = "zone"`, `Finding.Error`, `WaveRule.Idle`). `SpawnRole` is the only enum that gets serialised.
- Comments and messages use plain everyday English (British spelling in docs). They say *why*, or what was seen in the game. Log and banner text names the consequence ("…; mission left unchanged"). Sections are marked with `// ----- name` dividers, not `#region`.

**Error handling and resilience**
- Core validation returns `List<Finding>` (`Level` error/note, `Code`, `Path`, `Text`) and does not throw.
- Every call into the game is wrapped in try/catch with a fallback. A best-effort `try { … } catch { }` is accepted.
- Each per-frame subsystem runs under a `Guard` (`DirectorPlugin.PickerGuard/LinkGuard/RulesGuard/StartGuard`) and reports through `DirectorPlugin.Trouble`/`Fine`.
  - After 5 failures in a row the subsystem backs off 2^n s, capped at 60 s.
  - When `RulesGuard` degrades it calls `GameMaster.LetGo()`.
  - OnGUI parts switch themselves off after their first exception.
- Optional Harmony hooks are patched lazily, each with its own `new Harmony(Id + ".suffix")`. A failure logs `LogWarning` and falls back.
- Any game state you swap temporarily must always be restored (`Restore`, `RestoreRespawns`, `ReleaseInput`/`LetGo`).
- Logging goes through `DirectorPlugin.L` (Recon: `Plugin.L`):
  - `LogInfo` for decisions;
  - `LogWarning` with `e.Message` for degraded features;
  - `LogError` with the full exception.
  - On-screen text goes through `Director.Banner` and `GameMaster.Say`/`Tell`.

**Timing and state**
- No coroutines. Timers are `Time.unscaledTime` timestamps checked in `Update`. `TriggerEngine` needs monotonic seconds, with `Engine.Start` set to the mission start.
- Director statics own the engine instances (`GameMaster.Engine`, `Wave`, `LiveLink.Ledger`, `Director.Session`/`CurrentPlan`).
- Core holds no mutable statics. Only `CommandLedger` is thread-safe.

**IL2CPP interop**
- Use `FindObjectsOfType(Il2CppType.Of<T>(), …)` + `TryCast<T>()`. Never use C# casts on IL2CPP objects.
- Use `Object.Instantiate(…).Cast<GameObject>()`, `Il2CppReferenceArray<T>`, and `Il2CppSystem.Collections.Generic.List<T>` (aliased as `Il2Generic`).
- Compare identity by `.Pointer`; dictionaries are keyed by `IntPtr`.
- An injected MonoBehaviour needs an `(IntPtr ptr) : base(ptr)` constructor and is added with `AddComponent<T>()`. There are no explicit `ClassInjector` calls.
- Harmony parameters are `__instance`, `__0`, `__result`. Networked objects use `Mirror.NetworkServer.Spawn`/`Destroy`.

**JSON**
- Read with `System.Text.Json.JsonDocument` (trailing commas and comments allowed).
- Write by hand with `StringBuilder`:
  - numbers with `CultureInfo.InvariantCulture`, floats rounded to 2 decimal places;
  - strings escaped with `Guard.Esc`;
  - keys left out when they hold their default value.
- Recon uses only its own `J` writer (3 decimal places, NaN written as `null`).
- `V3.ToString()` uses the current culture; don't copy that pattern.

**Determinism (a contract)**
- All randomness goes through Core `Rng` (SplitMix64). `System.Random` is used only to pick among several scenario files that fit.
- The salts `^0x5EED` (variants) and `^0xE87A5` (extras) are fixed, and so are the order and number of draws in `Generator`, `Variants` and `Extras`. Changing any of them changes the layout for every seed.
- Repeat-run compares the recorded `resolved` text byte for byte with a fresh `plan.ToJson()` (`Director.cs`, `RunRecord.Read`). Any change to `ToJson` output therefore makes older run records report "different".
- A generated run is repeated from its recorded roll and inputs (`RepeatSetup`, ML-031), checked before anything is applied. Raise `RepeatSetup.GeneratorVersion` whenever the draws in `Generator`, `Extras` or the plugin's `RandomExtras` change, or what they place for the same seed and inputs; older records are then refused for repeating rather than repeated wrongly.
- Wherever iteration order affects the result, use ordered collections (`List`, `SortedDictionary`).

**Scenario format changes** (ML-007 contract). Update all of these together:
1. `MissionPlan.FromJson` and `ToJson`. The round trip must stay byte-identical.
2. The `ScenarioFile` tables (`SpawnFields`, `TriggerFields`, `ThingFields`, `DoorFields`, `Kinds`) and the root `Types`.
3. The editor's `PLAN_KINDS` and `*_FIELDS`, `scenarioProblems`, and its writer (`scenarioObject`/`spawnOf`). `tests/editor/run.mjs` holds the editor to the same fixtures and to the plugin's round trip.
4. `docs/scenario.schema.json`, and its cases in `tests/fixtures/schema/` (`tools/schema_check.py`).
5. `tests/fixtures/all_fields.json` (or the `all_fields_v*.json` of the format version) plus the key list in the `files` section of `tests/Pure`. A new refusal needs an `invalid/*.json` fixture and an entry in `expected.json`.
6. A new feature a pack must declare gets a capability word in all four lists: Core `Capabilities.Of` and `Pack.Known`, the editor's `capabilitiesOf` (`22-scenario.js`) and `PACK_KNOWN` (`30a-packs.js`), and a `uses` entry on one row of `docs/capabilities.json` (then `python3 tools/status.py --write`).

Other rules for the format:
- The error codes are fixed: `not-json wrong-type no-version(note) unknown-format future-version bad-id duplicate-id unknown-kind missing-field self-ref missing-ref unknown-value unreadable bad-area out-of-range`. Add a code only with an `invalid/*.json` fixture and `expected.json` entry that both readers pass.
- Raise `missionlab-plan/N` only for an incompatible change. A file with a higher N is refused (`future-version`) and never rewritten. plan/2 exists because v0.23 ignores `schema` and unknown keys and would read a shaped area as its anchor circle; the `mission` envelope makes v0.23 refuse the file (no root `spawns`), tested against a verbatim copy of the v0.23 reader in `tests/Pure/Legacy/`.
- There are two readers. The strict `ScenarioFile.Read` handles user files at mission start. The lenient `FromJson` clamps and defaults, and is used for runs, Game Master and live edits, and `CoreTest --check`.
- plan/3 exists for `goals` and for zone/hold triggers linked to a place. v0.26 ignores unknown keys, so it would play a linked zone as its anchor circle and drop the goals and an extraction waiting for them; plan/3 makes it refuse the file (`future-version`). A file is written as plan/3 only when it uses one of them, otherwise as plan/2 or plan/1 as before.
- plan/4 exists for a zone or hold with `after` (ML-055): it does not watch its area until that trigger has fired. The first 0.27.0 candidate reads `after` only on `after`/`outcome` triggers and would watch the area from the start, so plan/4 makes it refuse the file. Written only when a zone/hold has a gate; `tests/fixtures/invalid/future_version.json` is now plan/5. The gate is a dependency edge like `after`'s (circles, Completion, the graph, the inspector, module renumbering and repairs); a gate that names itself or no trigger is refused by the strict readers and dropped by the lenient one. Capability word `zone-after`.
- The `outcome` kind (ML-047) is in places 1 to 5 above. Its `after` is a dependency edge like `after`'s (circles, Completion, the graph, module renumbering and repairs). `TriggerPlan.NeverSeen` (Core) and `outcomeNeverSeen` (editor) decide together which outcomes a trigger can never report: change both.
- Goals (ML-046): Core `GoalBook` is the one implementation of goal states, used by Completion, Rehearsal and GameMaster; do not add a second. When a goal's success and failure come in the same step, failure wins.
- `director.pressure` (ML-052) is optional and needs no format bump: older readers ignore it and play the mission without pacing, which is acceptable for an optional aid. Its refusals use the existing codes (`invalid/bad_pressure.json`); its capability word is `pressure`, listed whenever the plan keeps any bounds, even authored `off`, because the host can switch it on.

**Editor JS**
- One IIFE with `'use strict'`, 2-space indent, a single global `state` object, user messages through `say()`, localStorage wrapped in try/catch. No npm.
- The page is built: edit the parts in `tools/editor/src/*.js`/`*.html` (joined in file-name order by `python3 tools/editor/build.py`), never `tools/editor/index.html`. The join is exact, with one exception: the literal `'__ML_CAPABILITIES__'` in `20a-modes.js` is replaced by `docs/capabilities.json` as compact JSON. `build.py --check` fails on a stale page; tests and `package.py` run it.
- Keep the literals `src="three.min.js"` (checked by `package.py`), `'__ML_PAGE_STAMP__'` (replaced by LiveLink) and `'__ML_CAPABILITIES__'` (replaced by `build.py`).
- Undo goes through the named history in `27-editing.js`: call `willChange('Move enemy 3')` just before a one-step edit, and wrap a gesture in `txBegin(name)` … `txCommit()` (one step, none if nothing changed) or `txCancel()` (puts the state back, no step). `pushUndo` is gone. Undo of a change the game already has stages the reverse change; it never rewinds the game.
- Hand what tests need to `window.missionlabEditor` with `expose({ … })`, in the part that defines it. Each ticket's editor checks live in their own `tests/editor/cases/mlNNN-*.mjs`.

## Important Files
- `src/MissionLab.Director/DirectorPlugin.cs`: the entry point. It holds:
  - `public const string Version` (`package.py` reads it with a regex);
  - every `Config.Bind` (`[General] [Replay] [GameMaster] [Advanced] [LiveLink]`). This is the source of truth for settings and their defaults;
  - the Harmony patch classes;
  - `DirectorBehaviour`, the only `Update`/`OnGUI` loop.
- `src/MissionLab.Director/Director.cs`: `OnRaidStart` → `TryApply` → `Apply`, and `WriteRecord`.
- `src/MissionLab.Director/LiveLink.cs`: the HTTP API, and `TestedGame` (`package.py` reads it with a regex).
- `src/MissionLab.Recon/Plugin.cs`: the Recon entry point and its separate `Version`.
- `src/MissionLab.Core/ScenarioFile.cs`, `MissionPlan.cs`, `TriggerEngine.cs`, `Generator.cs`, `Rng.cs`, `Goals.cs`, `Capabilities.cs`.
- `tests/fixtures/expected.json`: the golden list of refusals as ordered `"code path"` strings. Both the plugin and the editor must match it.
- `docs/scenario.schema.json`: JSON Schema 2020-12. Unknown keys are ignored by design (no `additionalProperties:false`). `tools/schema_check.py` lists what it does not cover (references, uniqueness, geometry) and the one known difference: it requires a thing's `name`, the strict reader does not.
- `docs/capabilities.json`: what MissionLab has been seen to do, with build, map, mode and scope per evidence record. The README Status table between its two marker comments is generated from it by `python3 tools/status.py --write`: never edit that table by hand; `./test.sh` fails when it is stale.

## Runtime/Tooling Preferences
- **Plugins:**
  - built with `-nostdlib` against the game's **.NET 6** BCL, **C# 10**, nullable off;
  - **no NuGet**: references come only from the game's `dotnet/`, `BepInEx/core` and `BepInEx/interop`;
  - an API added in .NET 7+ passes the net8.0 tests but breaks the plugin.
  - C# 11+ syntax is forbidden: raw strings, `required`, list patterns, `file` types, collection expressions.
- **Tests:** net8.0 console projects with no packages. Any newer SDK builds them, but they need the .NET 8 runtime. The Node suites use no packages either: the editor checks need Node 18+, the browser suite Node 22+ and a Chrome or Edge it finds itself (or `CHROME_PATH`).
- **Python 3:** `package.py`, `build.py`, `tools/editor/build.py`, `status.py` and `tools/agent/*.py` use only the standard library. `schema_check.py` needs `jsonschema` (`pip install jsonschema`; CI installs it). The preview scripts need numpy and matplotlib (there is no requirements file).
- **Git:**
  - Commit subjects look like `0.24.1: <summary>; ML-007, ML-008` or `ML-008 and ML-009: <summary>`.
  - There is one branch per minor version (`v0.27` is current). `main` is fast-forwarded to the newest release; releases are annotated tags `vX.Y.Z` on the minor branch.
- **Release:**
  1. Bump `DirectorPlugin.Version`, and Recon's `Plugin.Version` if Recon changed. Update `LiveLink.TestedGame` when testing against a new game build.
  2. Add a README `### New in X.Y.Z` section; this is the changelog. Record new game evidence in `docs/capabilities.json` and rewrite the Status table with `python3 tools/status.py --write`.
  3. Update the header (date, build, check counts) and tables of `docs/backlog-status.md`.
  4. Run `./test.sh` → `./build.sh` → `python3 tools/package.py --check`.

## Testing & QA
- **The harness is home-made; there is no xUnit.** `tests/Pure/Program.cs` uses top-level statements:
  - `Check("plain-English sentence", bool ok, string more)` counts each check and prints `FAIL:` lines. The run ends with `ALL N CHECKS PASSED` (exit 0) or exit 1.
  - Each section starts with a banner comment and a `Console.WriteLine("topic")`. Each case sits in its own bare `{ }` block.
  - Helpers: `Plan(json)`, `Engine(plan, start)`, `Fired(events)`, `OneEnemy`, `Broken(...)`.
  - The `World : ITriggerWorld` stub must stay **after** the top-level statements.
  - From 0.27 a ticket's C# checks go in their own `tests/Pure/MlNNNTests.cs` (`static class MlNNNTests` with `Run(Check)`), called from `Program.cs`.
  ```csharp
  {   // one sentence on the behaviour pinned down
      var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"delay\":5}]," + OneEnemy + "}");
      Check("it survives save and load as the same text", Plan(p.ToJson()).ToJson() == p.ToJson(), p.ToJson());
  }
  ```
- Fixtures are found by walking up from `AppContext.BaseDirectory`, so the build output must stay inside the repo.
- **Counts at 0.27.0 (released 21 November, with the fixes from its first solo run):** 1680 C# checks, 1619 editor checks, 67 browser checks, 18 adapter self-test checks.
- **Enforced today:**
  - the `ToJson` round trip;
  - each invalid fixture gives exactly the codes and paths in `expected.json`, in order, and the file is left unchanged, in the plugin and in the editor;
  - the schema lists every key that `ToJson` writes, and `tools/schema_check.py` judges the examples and `tests/fixtures/schema/` as listed in it;
  - every `examples/*.json` passes `Completion.Check`, survives the round trip, and has no trigger circles;
  - editor → C# → editor keeps what each fixture and example does;
  - the editor page and the README Status table are what their sources make;
  - the page in a real browser: open and save, area gestures with real pointer and keys, floors, draft/apply/retry against a stand-in link, 2D/3D alignment;
  - the golden `ScenarioFile.Hash("abc")`;
  - dice statistics over fixed seeds.
- **Not covered by CI:**
  - the Director and Recon compile;
  - CoreTest, including its pure save/load checks, which run only with `--map`;
  - all in-game behaviour, and anything a client without the mod sees.
- **What a change needs:**
  - A bug fix comes with a focused regression case.
  - A format or rule change comes with fixtures for both runtime and editor.
  - A game-facing change must say honestly what was observed in the game. `docs/backlog-status.md` separates "Tested", "In the game" and "co-op"; tick progress there, not in the `- [ ]` boxes of `backlog.md`.
- Never add tests to `tests/Pure` that need `*.bytes` or recon data.
