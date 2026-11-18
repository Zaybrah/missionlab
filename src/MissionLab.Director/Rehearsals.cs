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
    /// Rehearsal through the browser link (ML-016):
    ///   POST /api/rehearse                    {"scenario": text, "script": text}: the script played through the scenario
    ///   GET  /api/rehearsals/&lt;id&gt;            the tests kept for a scenario, summarised
    ///   GET  /api/rehearsals/&lt;id&gt;/&lt;file&gt;     one kept test
    ///   POST /api/rehearsals/&lt;id&gt;            {"name", "text", "replace"}: keeps a test under rehearsals/&lt;id&gt;/&lt;name&gt;.json
    /// Answered on the browser link's worker thread with Core only (RehearsalFile, Rehearsal, the TriggerEngine the live
    /// mission uses): no game objects, so it works in the lobby and with no mission running. Tests are never kept under
    /// scenarios/, whose every *.json is read as a scenario.
    /// </summary>
    internal static class Rehearsals
    {
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "rehearsals");
        private static readonly object Gate = new object();      // two saves of one name at once must not interleave
        private const string Kept = "/api/rehearsals/";

        /// <summary>The answer for a rehearsal path, null for any other path.</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            bool run = r.Path == "/api/rehearse", kept = r.Path.StartsWith(Kept, StringComparison.Ordinal);
            if (!run && !kept) return null;
            try
            {
                if (run)
                {
                    if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
                    string answer = RehearsalFile.Rehearse(r.BodyText, out int status);
                    return HttpResponse.Json(answer, status);
                }
                string[] parts = Uri.UnescapeDataString(r.Path.Substring(Kept.Length)).Split('/');
                if (!RevisionFile.GoodId(parts[0])) return HttpResponse.Error(400, "not a scenario id");
                if (parts.Length == 1) return r.Method == "POST" ? Save(parts[0], r.BodyText) : r.Method == "GET" ? List(parts[0]) : HttpResponse.Error(400, "GET or POST expected");
                if (parts.Length != 2 || r.Method != "GET" || !RehearsalFile.GoodFile(parts[1])) return HttpResponse.Error(400, "not a kept test");
                return HttpResponse.File(Path.Combine(Dir, parts[0], parts[1]), "application/json; charset=utf-8");
            }
            catch (Exception e) { return HttpResponse.Error(500, "the rehearsal could not be run, read or kept: " + e.Message); }
        }

        private static HttpResponse Save(string id, string body)
        {
            string text = "", name = ""; bool replace = false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return HttpResponse.Error(400, "a test is sent as {name, text, replace}");
                    if (root.TryGetProperty("text", out JsonElement t) && t.ValueKind == JsonValueKind.String) text = t.GetString();
                    if (root.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
                    if (root.TryGetProperty("replace", out JsonElement rp) && rp.ValueKind == JsonValueKind.True) replace = true;
                }
            }
            catch (JsonException) { return HttpResponse.Error(400, "the request is not JSON"); }
            string why = RehearsalFile.Prepare(text, name, out string canonical, out string file, out List<Finding> found);
            var sb = new StringBuilder(512);
            if (why != "")
            {
                sb.Append("{\"saved\":false,\"why\":\"").Append(Guard.Esc(why + "; nothing was saved")).Append("\",\"findings\":");
                CatalogueInfo.WriteFindings(sb, found);
                return HttpResponse.Json(sb.Append('}').ToString(), 400);
            }
            string folder = Path.Combine(Dir, id), full = Path.Combine(folder, file);
            bool replaced;
            lock (Gate)
            {
                bool existed = File.Exists(full);
                // The same text again is the same save, sent twice: answer as saved, write nothing.
                bool same = existed && File.ReadAllText(full) == canonical;
                if (existed && !same && !replace)
                {
                    sb.Append("{\"saved\":false,\"exists\":true,\"file\":\"").Append(Guard.Esc(file))
                      .Append("\",\"why\":\"a test named ").Append(Guard.Esc(file)).Append(" is already kept for this scenario; nothing was overwritten\"}");
                    return HttpResponse.Json(sb.ToString(), 409);
                }
                if (!same)
                {
                    Directory.CreateDirectory(folder);
                    // Written beside the file and then moved into place, so a crash never leaves half a test under its name.
                    string temp = full + ".part";
                    File.WriteAllText(temp, canonical);
                    File.Move(temp, full, true);
                }
                replaced = existed && !same;
            }
            sb.Append("{\"saved\":true,\"replaced\":").Append(replaced ? "true" : "false").Append(",\"test\":");
            RehearsalFile.Summary(sb, file, canonical, Saved(full));
            return HttpResponse.Json(sb.Append('}').ToString());
        }

        private static HttpResponse List(string id)
        {
            var files = new List<string>();
            string folder = Path.Combine(Dir, id);
            if (Directory.Exists(folder))
                foreach (string f in Directory.GetFiles(folder, "*.json"))
                    if (RehearsalFile.GoodFile(Path.GetFileName(f))) files.Add(f);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            if (files.Count > RehearsalFile.MaxListed) files.RemoveRange(RehearsalFile.MaxListed, files.Count - RehearsalFile.MaxListed);
            var sb = new StringBuilder(1024);
            sb.Append("{\"id\":\"").Append(Guard.Esc(id)).Append("\",\"rehearsals\":[");
            for (int i = 0; i < files.Count; i++)
            {
                string text = "";
                try { text = File.ReadAllText(files[i]); } catch (Exception) { text = ""; }   // unreadable: listed with the reason Rehearsal.Read gives
                if (i > 0) sb.Append(',');
                RehearsalFile.Summary(sb, Path.GetFileName(files[i]), text, Saved(files[i]));
            }
            return HttpResponse.Json(sb.Append("]}").ToString());
        }

        private static string Saved(string file)
        {
            try { return File.GetLastWriteTimeUtc(file).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture); }
            catch (Exception) { return ""; }
        }
    }
}
