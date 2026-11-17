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
| `missionlab-plan/1` (scenario) | `MissionPlan.ToJson`, editor `scenarioObject` | `scenarios/*.json` (subfolders are ignored, which is how a file is switched off) |
| `missionlab-mission/1` | `Director.WriteRecord` | `missions/last.json` + timestamped copies |
| `missionlab-run/1` | `RunRecord` | `runs/` (pruned to 60) |
| `missionlab-replay/1` (`semantics:3`: rows carry `who` ids; 1 and 2 still open) | `Replay` | `replays/` |
| `missionlab-recon/1`, `missionlab-movement/2` | Recon `ReconDump`, `MovementTracker` | `recon/` |
| `.mlgeo` (`MLGEO2\n\0`) | `MapExport` | `maps/` (local only) |

The navmesh input is `OPERATOR_Data/NavMeshData/*.bytes`: a zip holding `graph0_extra.binary`, with vertices stored in Int3 millimetres. It has **three parsers that must change together**: `src/MissionLab.Core/NavMesh.cs`, the editor's `parseTiles`, and `tools/preview/navmesh.py`. The layout is described in `docs/findings.md`.

## Key Directories
- `src/MissionLab.Core/`: pure logic.
  - Some names hide what the file holds: the JSON escaper `Guard.Esc` lives in `Resilience.cs`, and `V3` lives in `NavMesh.cs`.
  - `ScenarioFile` is the strict reader. `MissionPlan` is the model, the writer and the *lenient* reader.
- `src/MissionLab.Director/`: the plugin.
  - `GameMaster.cs` is one ~2350-line static class split by `// ---- name` dividers (life cycle, markers, triggers, doors, live spawning, keys, browser editor, saving, overlay).
  - `TryOuts.cs` holds experiments that run only from an explicit browser `{"cmd":"try"}`. It also holds shared helpers (`Message`, `WalkTo`, `RespawnNow`, `End`).
- `src/MissionLab.Recon/`: the observer, with its own minimal JSON writer `J` (`Json.cs`).
- `tests/Pure/`: the asset-free suite, run by CI. `tests/CoreTest/`: checks that need a real map. `tests/fixtures/`: the format contract.
- `tools/editor/`: the editor page plus a vendored `three.min.js` (MIT). `tools/preview/`: Python/matplotlib plan renders. `tools/package.py`: builds the release ZIP.
- `examples/`: shipped scenarios. Tests check them.
- `docs/`: `backlog.md` (ML-NNN tickets), `backlog-status.md` (where each ticket stands), `findings.md` (game internals seen in practice), `ideas.md`, `research-*.md`, `scenario.schema.json`.

## Development Commands
```bash
./test.sh                                                   # CI equivalent: tests/Pure, editor page in sync (tools/editor/build.py --check), editor checks (node tests/editor/run.mjs); no game files
dotnet run --project tests/Pure -c Release                  # the C# suite alone from a Windows shell
node tests/editor/run.mjs                                   # the editor checks alone: fixtures, strict-reader parity with the plugin, editor -> C# -> editor round trip
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
- **CI** (`.github/workflows/test.yml`) sets up .NET 8, Python 3 and Node 20 and runs `./test.sh`. Director and Recon compile errors show up **only** in `build.sh`.
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
- Wherever iteration order affects the result, use ordered collections (`List`, `SortedDictionary`).

**Scenario format changes** (ML-007 contract). Update all of these together:
1. `MissionPlan.FromJson` and `ToJson`. The round trip must stay byte-identical.
2. The `ScenarioFile` tables (`SpawnFields`, `TriggerFields`, `ThingFields`, `DoorFields`, `Kinds`) and the root `Types`.
3. The editor's `PLAN_KINDS` and `*_FIELDS`, `scenarioProblems`, and its writer (`scenarioObject`/`spawnOf`). The editor has no tests in this repo, so mirror the change by hand.
4. `docs/scenario.schema.json`.
5. `tests/fixtures/all_fields.json` plus the key list in the `files` section of `tests/Pure`. A new refusal needs an `invalid/*.json` fixture and an entry in `expected.json`.

Other rules for the format:
- The error codes are fixed: `not-json wrong-type no-version(note) unknown-format future-version bad-id duplicate-id unknown-kind missing-field self-ref missing-ref unknown-value unreadable`.
- Raise `missionlab-plan/N` only for an incompatible change. A file with a higher N is refused (`future-version`) and never rewritten.
- There are two readers. The strict `ScenarioFile.Read` handles user files at mission start. The lenient `FromJson` clamps and defaults, and is used for runs, Game Master and live edits, and `CoreTest --check`.

**Editor JS**
- One IIFE with `'use strict'`, 2-space indent, a single global `state` object, user messages through `say()`, localStorage wrapped in try/catch. No npm.
- The page is built: edit the parts in `tools/editor/src/*.js`/`*.html` (joined in file-name order by `python3 tools/editor/build.py`), never `tools/editor/index.html`. `build.py --check` fails on a stale page; tests and `package.py` run it.
- Keep the literals `src="three.min.js"` (checked by `package.py`) and `'__ML_PAGE_STAMP__'` (replaced by LiveLink).

## Important Files
- `src/MissionLab.Director/DirectorPlugin.cs`: the entry point. It holds:
  - `public const string Version` (`package.py` reads it with a regex);
  - every `Config.Bind` (`[General] [Replay] [GameMaster] [Advanced] [LiveLink]`). This is the source of truth for settings and their defaults;
  - the Harmony patch classes;
  - `DirectorBehaviour`, the only `Update`/`OnGUI` loop.
- `src/MissionLab.Director/Director.cs`: `OnRaidStart` → `TryApply` → `Apply`, and `WriteRecord`.
- `src/MissionLab.Director/LiveLink.cs`: the HTTP API, and `TestedGame` (`package.py` reads it with a regex).
- `src/MissionLab.Recon/Plugin.cs`: the Recon entry point and its separate `Version`.
- `src/MissionLab.Core/ScenarioFile.cs`, `MissionPlan.cs`, `TriggerEngine.cs`, `Generator.cs`, `Rng.cs`.
- `tests/fixtures/expected.json`: the golden list of refusals as ordered `"code path"` strings. Both the plugin and the editor must match it.
- `docs/scenario.schema.json`: JSON Schema 2020-12. Unknown keys are ignored by design (no `additionalProperties:false`).

## Runtime/Tooling Preferences
- **Plugins:**
  - built with `-nostdlib` against the game's **.NET 6** BCL, **C# 10**, nullable off;
  - **no NuGet**: references come only from the game's `dotnet/`, `BepInEx/core` and `BepInEx/interop`;
  - an API added in .NET 7+ passes the net8.0 tests but breaks the plugin.
  - C# 11+ syntax is forbidden: raw strings, `required`, list patterns, `file` types, collection expressions.
- **Tests:** net8.0 console projects with no packages. Any newer SDK builds them, but they need the .NET 8 runtime.
- **Python 3:** `package.py` uses only the standard library. The preview scripts need numpy and matplotlib (there is no requirements file).
- **Git:**
  - Commit subjects look like `0.24.1: <summary>; ML-007, ML-008` or `ML-008 and ML-009: <summary>`.
  - There is one branch per minor version (`v0.24` is current). `main` is stale.
- **Release:**
  1. Bump `DirectorPlugin.Version`, and Recon's `Plugin.Version` if Recon changed. Update `LiveLink.TestedGame` when testing against a new game build.
  2. Add a README `### New in X.Y.Z` section; this is the changelog.
  3. Update the header and table of `docs/backlog-status.md`.
  4. Run `./test.sh` → `./build.sh` → `python3 tools/package.py --check`.

## Testing & QA
- **The harness is home-made; there is no xUnit.** `tests/Pure/Program.cs` uses top-level statements:
  - `Check("plain-English sentence", bool ok, string more)` counts each check and prints `FAIL:` lines. The run ends with `ALL N CHECKS PASSED` (exit 0) or exit 1.
  - Each section starts with a banner comment and a `Console.WriteLine("topic")`. Each case sits in its own bare `{ }` block.
  - Helpers: `Plan(json)`, `Engine(plan, start)`, `Fired(events)`, `OneEnemy`, `Broken(...)`.
  - The `World : ITriggerWorld` stub must stay **after** the top-level statements.
  ```csharp
  {   // one sentence on the behaviour pinned down
      var p = Plan("{\"triggers\":[{\"id\":1,\"when\":\"alert\",\"delay\":5}]," + OneEnemy + "}");
      Check("it survives save and load as the same text", Plan(p.ToJson()).ToJson() == p.ToJson(), p.ToJson());
  }
  ```
- Fixtures are found by walking up from `AppContext.BaseDirectory`, so the build output must stay inside the repo.
- **Enforced today:**
  - the `ToJson` round trip;
  - each invalid fixture gives exactly the codes and paths in `expected.json`, in order, and the file is left unchanged;
  - the schema lists every key that `ToJson` writes;
  - every `examples/*.json` passes `Completion.Check`, survives the round trip, and has no trigger circles;
  - the golden `ScenarioFile.Hash("abc")`;
  - dice statistics over fixed seeds.
- **Not covered by CI:**
  - the Director and Recon compile;
  - CoreTest, including its pure save/load checks, which run only with `--map`;
  - the editor, whose tests live outside the repo;
  - all in-game behaviour.
- **What a change needs:**
  - A bug fix comes with a focused regression case.
  - A format or rule change comes with fixtures for both runtime and editor.
  - A game-facing change must say honestly what was observed in the game. `docs/backlog-status.md` separates "Tested", "In the game" and "co-op"; tick progress there, not in the `- [ ]` boxes of `backlog.md`.
- Never add tests to `tests/Pure` that need `*.bytes` or recon data.
