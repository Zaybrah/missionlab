#!/usr/bin/env bash
# The tests that need nothing of the game: scenario model, file format, dice, trigger rules, completion check, and the
# editor's reading, writing and mission check (also against the plugin's own reading and writing), in a stand-in page
# and in a real Chromium.
# They run from a clean checkout with the .NET 8 SDK and Python 3. The schema check needs the jsonschema package, the
# editor checks Node 18 or later, the browser checks Node 22 or later and Chrome or Edge. Locally a missing extra is
# skipped with a note; in CI (CI set) it is a failure.
#   ./test.sh            the C# suite, the Status table and the page against their sources, the schema check, then
#                        node tests/editor/run.mjs and node tests/browser/run.mjs
#   ./test.sh --map <navmesh.bytes> <recon.json>     also the checks that read a real map (your own game files)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
if [ "${1:-}" = "--map" ]; then
  [ -f "${2:-}" ] || { echo "missing map file. Usage: ./test.sh --map <OPERATOR_Data/NavMeshData/x.bytes> <BepInEx/MissionLab/recon/x.late.json>"; exit 2; }
  [ -f "${3:-}" ] || { echo "missing recon file. Usage: ./test.sh --map <navmesh.bytes> <recon.json>"; exit 2; }
  dotnet run --project "$HERE/tests/Pure" -c Release
  dotnet run --project "$HERE/tests/CoreTest" -c Release -- "$2" "$3" "${TMPDIR:-/tmp}/missionlab-coretest" 12 1 2 3
else
  dotnet run --project "$HERE/tests/Pure" -c Release
fi
# On Windows "python3" may be the Microsoft Store placeholder that runs nothing, so it has to actually start.
PYTHON=python3
"$PYTHON" -c "" >/dev/null 2>&1 || PYTHON=python
"$PYTHON" "$HERE/tools/editor/build.py" --check
# The README Status table is generated from docs/capabilities.json (ML-036).
"$PYTHON" "$HERE/tools/status.py" --check
# The published JSON Schema against the examples and the structural fixtures (ML-034); it needs the jsonschema package.
set +e
"$PYTHON" "$HERE/tools/schema_check.py"
status=$?
set -e
if [ "$status" = 2 ] && [ -z "${CI:-}" ]; then
  echo "jsonschema not installed: schema check skipped (pip install jsonschema)"
elif [ "$status" != 0 ]; then
  exit "$status"
fi
# The coding-agent adapter answers the protocol and forwards to a stand-in browser link it starts itself (ML-053).
"$PYTHON" "$HERE/tools/agent/missionlab_agent.py" --selftest
if command -v node >/dev/null 2>&1; then
  node "$HERE/tests/editor/run.mjs"
  # The same page in a real Chromium (ML-038). Exit 2: no browser or no Node 22 here, which only CI must not accept.
  set +e
  node "$HERE/tests/browser/run.mjs"
  status=$?
  set -e
  if [ "$status" = 2 ] && [ -z "${CI:-}" ]; then
    echo "no Chromium or Node 22 found: browser checks skipped"
  elif [ "$status" != 0 ]; then
    exit "$status"
  fi
elif [ -n "${CI:-}" ]; then
  echo "node not found: the editor checks are required in CI" >&2
  exit 1
else
  echo "node not found: editor checks skipped"
fi
