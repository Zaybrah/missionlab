#!/usr/bin/env bash
# Builds the MissionLab plugins against the game's own .NET 6 runtime, BepInEx and interop assemblies.
#   MissionLab.Recon.dll     read-only observer (writes what a mission contains to JSON)
#   MissionLab.Director.dll  replaces the enemy layout from a seed (includes MissionLab.Core)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REFS="${OPERATOR_REFS:-/mnt/user-data/uploads/OPERATOR}"
CSC="$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | tail -1)"
OUT="$HERE/dist"
mkdir -p "$OUT"
args=()
for f in "$REFS"/dotnet/*.dll "$REFS"/BepInEx/core/*.dll "$REFS"/BepInEx/interop/*.dll; do args+=("-r:$f"); done
common=(-nologo -target:library -nostdlib -noconfig -langversion:10 -optimize+ -deterministic -nowarn:CS1701,CS1702)
dotnet "$CSC" "${common[@]}" -out:"$OUT/MissionLab.Recon.dll" "${args[@]}" "$HERE"/src/MissionLab.Recon/*.cs
dotnet "$CSC" "${common[@]}" -out:"$OUT/MissionLab.Director.dll" "${args[@]}" "$HERE"/src/MissionLab.Core/*.cs "$HERE"/src/MissionLab.Director/*.cs
ls -la "$OUT"
