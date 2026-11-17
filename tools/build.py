#!/usr/bin/env python3
"""Builds the MissionLab plugins against the game's own .NET 6 runtime, BepInEx and interop assemblies.

  python3 tools/build.py --refs <OPERATOR folder>      (or set OPERATOR_REFS)

  dist/MissionLab.Recon.dll     read-only observer (writes what a mission contains to JSON)
  dist/MissionLab.Director.dll  replaces the enemy layout from a seed (includes MissionLab.Core)

Works on Windows, Linux and macOS. csc.dll comes from the newest installed .NET SDK, or from the CSC variable.
"""
import glob, os, re, struct, subprocess, sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FOLDERS = ['dotnet', 'BepInEx/core', 'BepInEx/interop']
KEY = [
    'dotnet/System.Runtime.dll', 'dotnet/System.Private.CoreLib.dll',
    'BepInEx/core/BepInEx.Unity.IL2CPP.dll', 'BepInEx/core/BepInEx.Core.dll', 'BepInEx/core/0Harmony.dll',
    'BepInEx/core/Il2CppInterop.Runtime.dll',
    'BepInEx/interop/Assembly-CSharp.dll', 'BepInEx/interop/Il2Cppmscorlib.dll',
    'BepInEx/interop/UnityEngine.CoreModule.dll', 'BepInEx/interop/Mirror.dll',
]
FLAGS = ['-nologo', '-target:library', '-nostdlib', '-noconfig', '-langversion:10', '-optimize+', '-deterministic',
         '-nowarn:CS1701,CS1702']


def fail(msg):
    print(msg, file=sys.stderr)
    sys.exit(2)


def refs_dir():
    args = sys.argv[1:]
    if '--refs' in args:
        i = args.index('--refs')
        if i + 1 >= len(args): fail('--refs needs a folder.')
        return args[i + 1]
    env = os.environ.get('OPERATOR_REFS')
    if env: return env
    fail('No reference folder given. Use  python3 tools/build.py --refs <folder>  or set OPERATOR_REFS.\n'
         'The OPERATOR game folder itself works (it has dotnet/, BepInEx/core/ and BepInEx/interop/), e.g.\n'
         '  python3 tools/build.py --refs "C:/Program Files (x86)/Steam/steamapps/common/OPERATOR"')


def is_managed(path):
    """A .NET assembly has a non-empty CLI header (data directory 14); native DLLs make csc fail with CS0009."""
    try:
        with open(path, 'rb') as f: head = f.read(4096)
        if head[:2] != b'MZ': return False
        pe = struct.unpack_from('<I', head, 0x3c)[0]
        if head[pe:pe + 4] != b'PE\0\0': return False
        opt = pe + 24
        magic = struct.unpack_from('<H', head, opt)[0]
        dd = opt + (96 if magic == 0x10b else 112)
        rva, size = struct.unpack_from('<II', head, dd + 14 * 8)
        return size > 0
    except (OSError, struct.error):
        return False


def find_csc():
    env = os.environ.get('CSC')
    if env:
        if not os.path.isfile(env): fail('CSC points to ' + env + ', which does not exist.')
        return env
    try:
        out = subprocess.run(['dotnet', '--list-sdks'], capture_output=True, text=True, check=True).stdout
    except (OSError, subprocess.CalledProcessError) as e:
        fail('Could not run "dotnet --list-sdks" (' + str(e) + '). Install a .NET SDK, or set CSC to a csc.dll.')
    found = []
    for line in out.splitlines():
        m = re.match(r'\s*(\S+)\s+\[(.+)\]\s*$', line)
        if not m: continue
        csc = os.path.join(m.group(2), m.group(1), 'Roslyn', 'bincore', 'csc.dll')
        if os.path.isfile(csc):
            nums = tuple(int(n) for n in re.findall(r'\d+', m.group(1).split('-')[0]))
            found.append((nums, csc))
    if not found: fail('No .NET SDK with Roslyn/bincore/csc.dll found. Install a .NET SDK, or set CSC to a csc.dll.')
    return max(found)[1]


def main():
    refs = refs_dir()
    missing = [d + '/' for d in FOLDERS if not os.path.isdir(os.path.join(refs, d))]
    missing += [k for k in KEY if not os.path.isfile(os.path.join(refs, k))]
    if missing:
        fail('The reference folder ' + refs + ' is missing:\n  ' + '\n  '.join(missing))
    dlls = []
    for d in FOLDERS:
        dlls += [p for p in sorted(glob.glob(os.path.join(refs, d, '*.dll'))) if is_managed(p)]
    csc = find_csc()
    out = os.path.join(HERE, 'dist')
    os.makedirs(out, exist_ok=True)
    rsp = os.path.join(out, 'refs.rsp')
    with open(rsp, 'w', encoding='utf-8') as f:     # one -r: per line: 400+ refs overflow the Windows command line
        for p in dlls: f.write('"-r:' + os.path.abspath(p) + '"\n')
    print('csc: ' + csc + '  (' + str(len(dlls)) + ' managed references)')
    src = lambda name: sorted(glob.glob(os.path.join(HERE, 'src', name, '*.cs')))
    builds = [('MissionLab.Recon.dll', src('MissionLab.Recon')),
              ('MissionLab.Director.dll', src('MissionLab.Core') + src('MissionLab.Director'))]
    for name, files in builds:
        target = os.path.join(out, name)
        code = subprocess.run(['dotnet', csc, '@' + rsp] + FLAGS + ['-out:' + target] + files).returncode
        if code != 0:
            print('Building ' + name + ' failed (exit ' + str(code) + ').', file=sys.stderr)
            sys.exit(1)
    for name, _ in builds:
        p = os.path.join(out, name)
        print('%-28s %d bytes' % (os.path.relpath(p, HERE), os.path.getsize(p)))


if __name__ == '__main__':
    main()
