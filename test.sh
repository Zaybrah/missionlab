#!/usr/bin/env bash
# The tests that need nothing of the game: scenario model, file format, dice, trigger rules, completion check, and the
# editor's reading, writing and mission check (also against the plugin's own reading and writing).
# They run from a clean checkout with the .NET 8 SDK and Python 3; the editor checks also need Node 18 or later, and
# are skipped with a note when node is not found.
#   ./test.sh            the asset-free suite, then: the editor page matches its parts, and node tests/editor/run.mjs
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
if command -v node >/dev/null 2>&1; then
  node "$HERE/tests/editor/run.mjs"
else
  echo "node not found: editor checks skipped"
fi
