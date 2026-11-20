#!/usr/bin/env python3
"""Builds the release ZIP: dist/MissionLab-<version>.zip. Unzip it into the OPERATOR game folder.

  python3 tools/package.py            make the ZIP from dist/ (run ./build.sh first)
  python3 tools/package.py --check    make it, unpack it into a scratch folder and check what is inside

The ZIP holds no game file: only the two plugins, the editor page with its 3D library, example scenarios and a short text.
Nothing in it lands where a user keeps their own work, so unzipping a new version over an old one keeps that work.
"""
import hashlib, os, re, sys, tempfile, zipfile
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
def read(path): return open(os.path.join(HERE, path), 'rb').read()
version = re.search(r'public const string Version = "([^"]+)"', read('src/MissionLab.Director/DirectorPlugin.cs').decode()).group(1)
tested = re.search(r'TestedGame = "([^"]+)"', read('src/MissionLab.Director/LiveLink.cs').decode()).group(1)
import subprocess
# The page shipped is tools/editor/index.html, which build.py makes from tools/editor/src; refuse a stale or mismatched one.
refuse = []
if subprocess.run([sys.executable, os.path.join(HERE, 'tools', 'editor', 'build.py'), '--check']).returncode != 0:
    refuse.append('tools/editor/index.html is out of date with tools/editor/src: run  python3 tools/editor/build.py')
editor = re.search(r"const EDITOR_VERSION = '([^']+)'", read('tools/editor/src/20-state.js').decode('utf-8'))
if not editor: refuse.append('tools/editor/src/20-state.js has no EDITOR_VERSION constant')
elif editor.group(1) != version: refuse.append('the editor says ' + editor.group(1) + ' but DirectorPlugin.Version is ' + version + ': make them equal')
for p in refuse: print('REFUSED: ' + p)
if refuse: sys.exit(1)
TEXT = f"""MissionLab {version}
An unofficial, free mod toolkit for OPERATOR. It is not made or supported by Vector Interactive.

Tried with OPERATOR {tested} and BepInEx 6 (IL2CPP build be.785). Only the host needs it; friends join without it.
It stays out of PvP modes.

Install
1. Install BepInEx 6 for IL2CPP games into the OPERATOR folder and start the game once, so BepInEx can set itself up.
2. Unzip this file into the OPERATOR folder, so that the BepInEx folder here lands on the one that is there.
3. Start the game and host a co-op mission. Open http://localhost:8765 in a browser on the same PC for the editor and the
   live tools. "Is everything ready?" on that page says what is missing, if anything.

What is in here
  BepInEx/plugins/MissionLab/MissionLab.Director.dll   the mod
  BepInEx/plugins/MissionLab/MissionLab.Recon.dll      read-only helper that writes down what each map contains
  BepInEx/MissionLab/web/                              the editor page and its 3D library (three.js, MIT licence)
  BepInEx/MissionLab/examples/                         example scenarios; copy one into BepInEx/MissionLab/scenarios to use it
  BepInEx/MissionLab/modules/                          example encounter modules (Create > Modules > Insert)

Update
  Unzip the new version over the old one and let it replace files. It replaces only the files listed above: the mod, the
  page and the examples. Your scenarios, revisions, run records, replays, rehearsals, playlists, operations (with their
  .prev.json copies), validations, agent proposals and recon are kept. An example you changed in
  BepInEx/MissionLab/examples or BepInEx/MissionLab/modules is put back as shipped, so save your own copies under another name.

Uninstall
  Delete BepInEx/plugins/MissionLab. The mod no longer loads, and everything you made stays in BepInEx/MissionLab,
  ready for a later install.

Remove all MissionLab data as well (optional, cannot be undone)
  First keep what you want: copy BepInEx/MissionLab somewhere else, or use "Export pack" on the page.
  Then delete BepInEx/MissionLab. That erases your scenarios, modules, revisions, run records, replays, rehearsals,
  playlists, operations, validations, agent proposals, recon notes, maps, missions and the page.
  BepInEx/config/missionlab.director.cfg holds the settings; delete it too if you like.

Problems
  BepInEx/LogOutput.log holds what the mod did in the last game start. The page has "Copy a report".
"""
FILES = [
    ('BepInEx/plugins/MissionLab/MissionLab.Director.dll', 'dist/MissionLab.Director.dll'),
    ('BepInEx/plugins/MissionLab/MissionLab.Recon.dll', 'dist/MissionLab.Recon.dll'),
    ('BepInEx/MissionLab/web/index.html', 'tools/editor/index.html'),
    ('BepInEx/MissionLab/web/three.min.js', 'tools/editor/three.min.js'),
    ('BepInEx/MissionLab/web/three.LICENSE.txt', 'tools/editor/three.LICENSE.txt'),
]
for name in sorted(os.listdir(os.path.join(HERE, 'examples'))):
    if name.endswith('.json'): FILES.append(('BepInEx/MissionLab/examples/' + name, 'examples/' + name))
for name in sorted(os.listdir(os.path.join(HERE, 'examples', 'modules'))):
    if name.endswith('.module.json'): FILES.append(('BepInEx/MissionLab/modules/' + name, 'examples/modules/' + name))
out = os.path.join(HERE, 'dist', f'MissionLab-{version}.zip')
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    for arc, src in FILES:
        info = zipfile.ZipInfo(arc, date_time=(2026, 1, 1, 0, 0, 0)); info.compress_type = zipfile.ZIP_DEFLATED; info.external_attr = 0o644 << 16
        z.writestr(info, read(src))                      # fixed dates: the same sources give the same ZIP
    info = zipfile.ZipInfo('MissionLab-README.txt', date_time=(2026, 1, 1, 0, 0, 0)); info.external_attr = 0o644 << 16
    z.writestr(info, TEXT.replace('\n', '\r\n'))
digest = hashlib.sha256(open(out, 'rb').read()).hexdigest()
print(f'{os.path.relpath(out, HERE)}  {os.path.getsize(out)} bytes  sha256 {digest}')
if '--check' in sys.argv:
    problems = []
    with tempfile.TemporaryDirectory() as scratch, zipfile.ZipFile(out) as z:
        z.extractall(scratch)
        names = z.namelist()
        for arc, src in FILES:
            got = open(os.path.join(scratch, arc), 'rb').read()
            if got != read(src): problems.append(arc + ' differs from its source')
        dll = open(os.path.join(scratch, FILES[0][0]), 'rb').read()
        if version.encode('utf-16-le') not in dll and version.encode() not in dll: problems.append('the plugin in the ZIP does not carry version ' + version + ': run ./build.sh first')
        page = open(os.path.join(scratch, 'BepInEx/MissionLab/web/index.html'), encoding='utf-8').read()
        if 'src="three.min.js"' not in page: problems.append('the page does not look for its 3D library beside itself')
        for n in names:
            low = n.lower()
            if low.endswith(('.mlgeo', '.bytes', '.assets', '.bundle')) or 'interop' in low or 'assembly-csharp' in low: problems.append('a game file is in the ZIP: ' + n)
            if n.startswith('/') or '..' in n: problems.append('unsafe path: ' + n)
        if 'MissionLab-README.txt' not in names: problems.append('the README is missing')
        if not any(n.startswith('BepInEx/MissionLab/examples/') for n in names): problems.append('no example scenario')
        # Nothing shipped may land where the plugin reads the user's own work, or an update would overwrite it.
        for n in names:
            if n.startswith(tuple('BepInEx/MissionLab/' + d + '/' for d in ('scenarios', 'revisions', 'runs', 'replays', 'rehearsals', 'playlists', 'operations', 'validations', 'proposals'))):
                problems.append('the ZIP writes into a folder of user work: ' + n)
    # Update simulation: a new ZIP unpacked over an install that holds authored work must leave that work byte for byte,
    # and the documented uninstall (deleting the plugin folder) must leave it too.
    import shutil
    with tempfile.TemporaryDirectory() as game, zipfile.ZipFile(out) as z:
        own = {
            'BepInEx/MissionLab/scenarios/my_raid.json': b'{"name":"my raid","version":1}\n',
            'BepInEx/MissionLab/revisions/my_raid/r1.json': b'{"revision":1}\n',
            'BepInEx/MissionLab/runs/2026-11-19_my_raid.json': b'{"run":"kept"}\n',
            'BepInEx/MissionLab/replays/2026-11-19_my_raid.json': b'{"replay":"kept"}\n',
            'BepInEx/MissionLab/playlists/evening.json': b'{"playlist":"kept"}\n',
            'BepInEx/MissionLab/operations/my_op.json': b'{"operation":"kept"}\n',
            'BepInEx/MissionLab/operations/my_op.prev.json': b'{"operation":"previous"}\n',
        }
        for name, data in own.items():
            os.makedirs(os.path.dirname(os.path.join(game, name)), exist_ok=True)
            open(os.path.join(game, name), 'wb').write(data)
        z.extractall(game)                               # first install
        z.extractall(game)                               # the same ZIP again stands for an update over it
        for name, data in own.items():
            if open(os.path.join(game, name), 'rb').read() != data: problems.append('an update changed the user file ' + name)
        shutil.rmtree(os.path.join(game, 'BepInEx/plugins/MissionLab'))
        for name, data in own.items():
            p = os.path.join(game, name)
            if not os.path.exists(p) or open(p, 'rb').read() != data: problems.append('the uninstall lost the user file ' + name)
        if not os.path.exists(os.path.join(game, 'BepInEx/MissionLab/web/index.html')): problems.append('the uninstall removed the page')
    print(f'{len(names)} files' + ('' if problems else ', all as expected, no game file among them'))
    for p in problems: print('PROBLEM: ' + p)
    sys.exit(1 if problems else 0)
