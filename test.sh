#!/usr/bin/env bash
# The tests that need nothing of the game: scenario model, file format, dice, trigger rules, completion check.
# They run from a clean checkout with the .NET 8 SDK alone.
#   ./test.sh            the asset-free suite
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
