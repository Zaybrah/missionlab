# Helping with MissionLab

MissionLab is free and open source (MIT), and help is welcome, with or without writing code. It is an unofficial mod for
OPERATOR, not made or endorsed by Vector Interactive.

## Ways to help
- **Play with a friend who has no mod.** This helps most, because it is the one thing that cannot be tested alone: what
  a client actually sees. [`docs/coop-record.md`](docs/coop-record.md) is the list of checks; each row counts only in the
  words of the player whose screen it is. A filled-in record (or any part of it) in an issue is very useful.
- **Report what you see.** Open an [issue](https://github.com/Zaybrah/missionlab/issues) with what you did, what you
  expected and what happened, plus "Copy a report" from the page at http://localhost:8765 (it leaves out player names and
  folders). `BepInEx/LogOutput.log` helps too. Say which OPERATOR and MissionLab versions you used.
- **Make missions.** Export one with Packs on the page and share the `.mlpack.json`; good ones can become examples.
- **Write code or docs:** below.

## Working on the code
1. Fork the repository and branch off the newest release branch (`v0.27` now; `main` follows the latest release).
2. Read [`AGENTS.md`](AGENTS.md) first. It explains how the parts fit together and the rules that keep them working:
   - C# 10 against the game's .NET 6 libraries, with no NuGet packages, and no game references in `src/MissionLab.Core`;
   - the scenario format is a contract shared by the plugin and the editor, changed only as AGENTS.md describes;
   - the editor page is built from `tools/editor/src`: edit the parts, never `tools/editor/index.html`.
3. Run `./test.sh`. It needs the .NET 8 SDK, Python 3 with `pip install jsonschema`, Node 22 or newer and Chrome or Edge,
   and no game files. CI runs the same on every push and pull request.
4. Building the plugins needs your own OPERATOR install with BepInEx:
   `python3 tools/build.py --refs "<OPERATOR folder>"`. Only `build.sh` compiles the plugin code, so run it when you change
   `src/MissionLab.Director` or `src/MissionLab.Recon`.
5. Open a pull request against the release branch. Say what changed, how you tested it, and whether it ran in the game.

## Ground rules
- **Never commit game files or anything made from them:** navmesh `.bytes`, `.mlgeo` geometry, interop DLLs, recon or
  replay output. The `.gitignore` covers the usual ones.
- **Be exact about what was seen in the game.** "Tested" means an automatic check proves it; what ran in OPERATOR, on
  which build and seen by whom goes in `docs/capabilities.json`. A host's log is never a client's word.
- **No PvP.** MissionLab only acts in PvE missions the player hosts, and stays that way.
- **Free for everyone.** OPERATOR's mod policy applies to anything shared for the game: no paid access, early access or
  supporter-only files.

Contributions are accepted under the [MIT license](LICENSE) of the project.
