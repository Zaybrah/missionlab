using System;
using System.Collections.Generic;
using System.IO;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// Debriefs through the browser link (ML-024):
    ///   GET /api/debrief/&lt;replay file&gt;            the debrief of one recording, with its run record when it can be found
    ///   GET /api/debrief/unused/&lt;scenario id&gt;      unused content across this scenario's comparable runs, as review suggestions
    /// Answered on the browser link's worker thread from files only (recordings and run records, by name, never by folder), so it
    /// works in the lobby and with no mission running. The reading itself is Core's Debrief and UnusedReport.
    /// At the end of a mission a trigger ends, a short debrief can also go to every player's screen (Replay/DebriefMessage).
    /// </summary>
    internal static class Debriefs
    {
        private const string One = "/api/debrief/", Unused = "/api/debrief/unused/";
        private const int Runs = 60;            // RunRecord keeps as many

        /// <summary>The answer for a debrief path, null for any other path.</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            if (!r.Path.StartsWith(One, StringComparison.Ordinal)) return null;
            if (r.Method != "GET") return HttpResponse.Error(400, "GET expected");
            try
            {
                if (r.Path.StartsWith(Unused, StringComparison.Ordinal))
                {
                    string id = Uri.UnescapeDataString(r.Path.Substring(Unused.Length));
                    if (!RevisionFile.GoodId(id)) return HttpResponse.Error(400, "not a scenario id");
                    return HttpResponse.Json(UnusedFor(id).ToJson());
                }
                string name = Uri.UnescapeDataString(r.Path.Substring(One.Length));
                if (!GoodName(name, ".json") || name.EndsWith(".run.json", StringComparison.OrdinalIgnoreCase)) return HttpResponse.Error(400, "not a recording's file name");
                string text = ReadIn(Replay.Dir, name, ".json");
                if (text == null) return HttpResponse.Error(404, "no such recording");
                Debrief d = Debrief.Read(name, text, RunText, out string error);
                return d == null ? HttpResponse.Error(422, "the recording could not be read: " + error) : HttpResponse.Json(d.ToJson());
            }
            catch (Exception e) { return HttpResponse.Error(500, "the debrief could not be made: " + e.Message); }
        }

        private static UnusedReport UnusedFor(string id)
        {
            var runs = new List<KeyValuePair<string, string>>();
            if (Directory.Exists(RunRecord.Dir))
            {
                var files = new List<string>(Directory.GetFiles(RunRecord.Dir, "*.run.json"));
                files.Sort(StringComparer.Ordinal); files.Reverse();      // names start with the date: newest first
                foreach (string f in files)
                {
                    if (runs.Count >= Runs) break;
                    string text = ReadIn(RunRecord.Dir, Path.GetFileName(f), ".run.json");
                    if (text != null) runs.Add(new KeyValuePair<string, string>(Path.GetFileName(f), text));
                }
            }
            return UnusedReport.Build(id, runs, name => GoodName(name, ".json") ? ReadIn(Replay.Dir, name, ".json") : null);
        }

        private static string RunText(string name) => GoodName(name, ".run.json") ? ReadIn(RunRecord.Dir, name, ".run.json") : null;

        // A plain file name of the right kind: no folders, no way up.
        private static bool GoodName(string name, string ending) =>
            !string.IsNullOrEmpty(name) && name.Length <= 120 && name == Path.GetFileName(name) && !name.Contains("..") && name.EndsWith(ending, StringComparison.OrdinalIgnoreCase);

        // The file's text, or null when it is not there or cannot be read (a recording being written may be busy for a moment).
        private static string ReadIn(string dir, string name, string ending)
        {
            if (!GoodName(name, ending)) return null;
            string path = Path.Combine(dir, name);
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>
        /// The mission is being ended by one of its triggers: three lines of debrief for every player, from the recording so far and
        /// this mission's run record. Main thread only (it sends game messages). Experimental: whether the lines reach the screen
        /// before the game's end screen has not been checked with an unmodded client; the later lines wait their turn in the message queue.
        /// </summary>
        internal static void ShowAtEnd(RaidManager raid)
        {
            if (raid == null) return;
            try
            {
                string text = Replay.Snapshot();
                if (text == "") { DirectorPlugin.L.LogInfo("debrief: nothing is being recorded, so no debrief is shown"); return; }
                Debrief d = Debrief.Read(Replay.FileName, text, RunText, out string error);
                if (d == null) { DirectorPlugin.L.LogWarning("debrief: the recording could not be read: " + error); return; }
                foreach (string line in d.Message())
                {
                    string said = TryOuts.Message(raid, line);
                    DirectorPlugin.L.LogInfo("debrief: " + said);
                }
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("debrief: the message could not be shown: " + e.Message); }
        }
    }
}
