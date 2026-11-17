#!/usr/bin/env bash
# Builds dist/MissionLab.Director.dll and dist/MissionLab.Recon.dll; the work is in tools/build.py.
#   OPERATOR_REFS=<OPERATOR folder> ./build.sh     or     ./build.sh --refs <OPERATOR folder>
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# On Windows "python3" can be the Microsoft Store stub, so try each name before using it.
for py in python3 python; do
  if command -v "$py" >/dev/null 2>&1 && "$py" -c 'import sys' >/dev/null 2>&1; then
    exec "$py" "$HERE/tools/build.py" "$@"
  fi
done
echo "No working python3 or python found; install Python 3." >&2
exit 2
