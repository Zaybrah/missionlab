using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// Saved revisions of a scenario (ML-014), one file each under BepInEx/MissionLab/revisions/&lt;scenario id&gt;/r&lt;n&gt;.json:
    ///   GET  /api/revisions/&lt;id&gt;      the revisions, newest first, with the run records that played each one
    ///   GET  /api/revision/&lt;id&gt;/&lt;n&gt;   one revision file, scenario text included
    ///   POST /api/revisions             {"text": scenario text, "note": "..."}: the id and number come from the text
    /// Files only, answered on the browser link's worker thread: nothing here touches the game. A revision is never
    /// overwritten or pruned: it is the author's explicit save. The same text sent twice (a retry) is answered as saved.
    /// </summary>
    internal static class Revisions
    {
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "revisions");
        private static readonly object Gate = new object();      // two saves at once must not both claim the same number
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const int MaxListed = 100;

        /// <summary>The answer for a revisions path, null for any other path.</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            bool list = r.Path.StartsWith("/api/revisions/", StringComparison.Ordinal), one = r.Path.StartsWith("/api/revision/", StringComparison.Ordinal);
            if (r.Path != "/api/revisions" && !list && !one) return null;
            try
            {
                if (r.Path == "/api/revisions") return r.Method == "POST" ? Save(r.BodyText) : HttpResponse.Error(400, "POST expected");
                if (r.Method != "GET") return HttpResponse.Error(400, "GET expected");
                if (list) return List(Uri.UnescapeDataString(r.Path.Substring("/api/revisions/".Length)));
                string[] parts = Uri.UnescapeDataString(r.Path.Substring("/api/revision/".Length)).Split('/');
                if (parts.Length != 2 || !RevisionFile.GoodId(parts[0]) || !int.TryParse(parts[1], NumberStyles.None, Inv, out int n) || n < 1) return HttpResponse.Error(400, "not a revision");
                string file = Path.Combine(Dir, parts[0], RevisionFile.FileName(n));
                return File.Exists(file) ? HttpResponse.Json(File.ReadAllText(file)) : HttpResponse.Error(404, "no such revision");
            }
            catch (Exception e) { return HttpResponse.Error(500, "the revisions could not be read or written: " + e.Message); }
        }

        private static HttpResponse Save(string body)
        {
            string text = "", note = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return HttpResponse.Error(400, "a revision is sent as {text, note}");
                    if (root.TryGetProperty("text", out JsonElement t) && t.ValueKind == JsonValueKind.String) text = t.GetString();
                    if (root.TryGetProperty("note", out JsonElement nt) && nt.ValueKind == JsonValueKind.String) note = nt.GetString();
                }
            }
            catch (JsonException) { return HttpResponse.Error(400, "the request is not JSON"); }
            string why = RevisionFile.Identify(text, out string id, out int n);
            if (why != "") return HttpResponse.Error(400, why + "; nothing was saved");
            var entry = new RevisionFile.Entry { Id = id, N = n, Note = RevisionFile.CleanNote(note), Saved = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv), Hash = ScenarioFile.Hash(text), Text = text };
            string folder = Path.Combine(Dir, id), file = Path.Combine(folder, RevisionFile.FileName(n));
            lock (Gate)
            {
                if (File.Exists(file))
                {
                    RevisionFile.Entry was = RevisionFile.Read(File.ReadAllText(file));
                    // The same text again is the same save, sent twice: answer as the first time, write nothing.
                    if (was != null && was.Hash == entry.Hash) return HttpResponse.Json(RevisionFile.SummaryJson(was, "[]"));
                    return HttpResponse.Error(409, "revision " + n.ToString(Inv) + " of this scenario already exists with other content; nothing was overwritten. Load the list again and save once more");
                }
                Directory.CreateDirectory(folder);
                // Written beside the file and then moved into place, so a crash never leaves half a revision under its name.
                string temp = file + ".part";
                File.WriteAllText(temp, RevisionFile.Write(entry));
                File.Move(temp, file);
            }
            return HttpResponse.Json(RevisionFile.SummaryJson(entry, "[]"));
        }

        private static HttpResponse List(string id)
        {
            if (!RevisionFile.GoodId(id)) return HttpResponse.Error(400, "not a scenario id");
            var found = new List<RevisionFile.Entry>();
            string folder = Path.Combine(Dir, id);
            if (Directory.Exists(folder))
                foreach (string f in Directory.GetFiles(folder, "r*.json"))
                {
                    if (RevisionFile.NumberOf(Path.GetFileName(f)) == 0) continue;
                    RevisionFile.Entry e = null;
                    try { e = RevisionFile.Read(File.ReadAllText(f)); } catch { }
                    if (e != null && e.Id == id) found.Add(e);
                }
            found.Sort((a, b) => b.N.CompareTo(a.N));
            if (found.Count > MaxListed) found.RemoveRange(MaxListed, found.Count - MaxListed);
            List<RunSeen> runs = Runs();
            var sb = new StringBuilder(1024);
            sb.Append("{\"id\":\"").Append(Guard.Esc(id)).Append("\",\"revisions\":[");
            for (int i = 0; i < found.Count; i++)
            {
                var played = new StringBuilder("[");
                int k = 0;
                foreach (RunSeen run in runs)
                    if (RevisionFile.Played(found[i], run.Hash, run.ScenarioId, run.Revision)) played.Append(k++ == 0 ? "" : ",").Append(run.Json);
                sb.Append(i == 0 ? "" : ",").Append(RevisionFile.SummaryJson(found[i], played.Append(']').ToString()));
            }
            return HttpResponse.Json(sb.Append("]}").ToString());
        }

        private sealed class RunSeen { public string Hash = "", ScenarioId = "", Json = ""; public int Revision; }

        // The run records on disk (pruned to 60 by RunRecord), newest first, with what links them to a revision. Read here
        // rather than through RunRecord.Read, which logs, as this runs on a worker thread.
        private static List<RunSeen> Runs()
        {
            var list = new List<RunSeen>();
            try
            {
                if (!Directory.Exists(RunRecord.Dir)) return list;
                var files = new List<string>(Directory.GetFiles(RunRecord.Dir, "*.run.json"));
                files.Sort(StringComparer.Ordinal); files.Reverse();
                foreach (string f in files)
                {
                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(f)))
                        {
                            JsonElement r = doc.RootElement;
                            if (r.ValueKind != JsonValueKind.Object) continue;
                            string S(string k) => r.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                            long L(string k) => r.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long x) ? x : 0;
                            string result = r.TryGetProperty("end", out JsonElement end) && end.ValueKind == JsonValueKind.Object && end.TryGetProperty("result", out JsonElement res) && res.ValueKind == JsonValueKind.String ? res.GetString() : "";
                            var sb = new StringBuilder(256);
                            sb.Append("{\"file\":\"").Append(Guard.Esc(Path.GetFileName(f))).Append("\",\"startedUtc\":\"").Append(Guard.Esc(S("startedUtc")))
                              .Append("\",\"scene\":\"").Append(Guard.Esc(S("scene"))).Append("\",\"outcome\":\"").Append(Guard.Esc(S("outcome")))
                              .Append("\",\"result\":\"").Append(Guard.Esc(result)).Append("\",\"players\":").Append(L("players").ToString(Inv)).Append(",\"roll\":").Append(L("roll").ToString(Inv))
                              .Append(",\"plugin\":\"").Append(Guard.Esc(S("plugin"))).Append("\",\"game\":\"").Append(Guard.Esc(S("game"))).Append("\"}");
                            list.Add(new RunSeen { Hash = S("authoredHash"), ScenarioId = S("scenarioId"), Revision = (int)Math.Max(0, Math.Min(int.MaxValue, L("revision"))), Json = sb.ToString() });
                        }
                    }
                    catch { }      // a damaged record is left out of the links; the run list itself reports it
                }
            }
            catch { }
            return list;
        }
    }
}
