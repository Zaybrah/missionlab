using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// The Play view's list of missions: every scenario file directly in BepInEx/MissionLab/scenarios (subfolders are ignored,
    /// as the Director ignores them), each read with the strict reader, with its last run and the measured duration of the
    /// runs that were seen to end. Also saves a scenario from the page into that folder, after the same strict read.
    ///
    /// Files only, no game objects: both requests are answered on the browser link's worker thread, so the catalogue works
    /// in the lobby and while the game is busy.
    /// </summary>
    internal static class Catalogue
    {
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "scenarios");
        private const int MaxFiles = 200;                  // more than this and the folder is not a catalogue any more; the rest is left out
        private const long MaxBytes = 4L * 1024 * 1024;    // a scenario this large is not one the page made

        // What each capability has been seen to do in the game, with build, map, mode and who saw it (docs/capabilities.json,
        // built into this DLL by tools/build.py). Read once; a broken list only means less is claimed, never more.
        private static readonly CapabilityRegistry Registry = LoadRegistry();

        private static CapabilityRegistry LoadRegistry()
        {
            CapabilityRegistry r = CapabilityRegistry.Embedded();
            foreach (string p in r.Problems) DirectorPlugin.L.LogWarning("capability list: " + p + "; the catalogue claims less for it");
            return r;
        }

        // Recon files (BepInEx/MissionLab/recon, written by MissionLab.Recon when it is installed) tell which intel items and
        // target points a map has, so a scenario that needs one the map lacks is named before it is chosen. Each file is
        // parsed again only when its size or time changes; several worker threads may ask at once, hence the lock.
        private static readonly object ReconLock = new object();
        private static readonly Dictionary<string, KeyValuePair<string, MapFacts>> ReconSeen = new Dictionary<string, KeyValuePair<string, MapFacts>>(StringComparer.OrdinalIgnoreCase);
        private const int MaxRecon = 40;
        private const long MaxReconBytes = 64L * 1024 * 1024;

        private static List<MapFacts> Recon()
        {
            var list = new List<MapFacts>();
            try
            {
                string dir = Path.Combine(DirectorPlugin.BaseDir, "recon");
                if (!Directory.Exists(dir)) return list;
                var files = new List<string>();
                foreach (string f in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
                    if (!f.EndsWith(".movement.json", StringComparison.OrdinalIgnoreCase)) files.Add(f);
                files.Sort(StringComparer.OrdinalIgnoreCase);
                lock (ReconLock)
                {
                    for (int i = 0; i < files.Count && i < MaxRecon; i++)
                    {
                        try
                        {
                            var info = new FileInfo(files[i]);
                            if (info.Length > MaxReconBytes) continue;
                            string stamp = info.Length + "-" + info.LastWriteTimeUtc.Ticks;
                            if (!ReconSeen.TryGetValue(files[i], out KeyValuePair<string, MapFacts> had) || had.Key != stamp)
                            {
                                had = new KeyValuePair<string, MapFacts>(stamp, MapFacts.FromRecon("recon/" + info.Name, File.ReadAllText(files[i])));
                                ReconSeen[files[i]] = had;
                            }
                            if (had.Value != null) list.Add(had.Value);
                        }
                        catch (Exception) { }     // a recon file being written right now is read next time
                    }
                }
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("recon files could not be read for the catalogue: " + e.Message); }
            return list;
        }

        /// <summary>Worker thread. null: not a catalogue request.</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            if (r.Path == "/api/catalogue") return r.Method == "GET" ? HttpResponse.Json(Json()) : HttpResponse.Error(400, "GET expected");
            if (r.Path == "/api/scenario/save") return r.Method == "POST" ? Save(r.BodyText) : HttpResponse.Error(400, "POST expected");
            return null;
        }

        private static List<RunSummary> Runs()
        {
            var list = new List<RunSummary>();
            try
            {
                if (!Directory.Exists(RunRecord.Dir)) return list;
                foreach (string f in Directory.GetFiles(RunRecord.Dir, "*.run.json"))
                {
                    // A record being written by the game at this moment is skipped this time, not reported as broken.
                    try { RunSummary s = RunSummary.Read(Path.GetFileName(f), File.ReadAllText(f)); if (s != null) list.Add(s); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return list;
        }

        internal static string Json()
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"schema\":\"missionlab-catalogue/1\",\"plugin\":\"").Append(Guard.Esc(DirectorPlugin.Version)).Append("\",\"scenarios\":[");
            int n = 0, skipped = 0;
            try
            {
                if (Directory.Exists(Dir))
                {
                    List<RunSummary> runs = Runs();
                    List<MapFacts> maps = Recon();
                    var files = new List<string>(Directory.GetFiles(Dir, "*.json", SearchOption.TopDirectoryOnly));
                    files.Sort(StringComparer.OrdinalIgnoreCase);
                    foreach (string path in files)
                    {
                        if (n >= MaxFiles) { skipped++; continue; }
                        string file = Path.GetFileName(path), text;
                        try
                        {
                            if (new FileInfo(path).Length > MaxBytes) { skipped++; continue; }
                            text = File.ReadAllText(path);
                        }
                        catch (Exception) { skipped++; continue; }
                        CatalogueEntry e = CatalogueInfo.Build(file, text, runs);
                        CatalogueInfo.Check(e, Registry, DirectorPlugin.Version, maps);
                        // The compatibility table judges a map file; a scenario naming none cannot be judged, so it stays unknown ("").
                        string compat = e.NavFile != "" ? LiveLink.CompatFor(Path.GetFileName(e.NavFile), "", true, "host") : "";
                        string map = e.NavFile != "" || e.Scene != "" ? Picker.MapName(e.NavFile, e.Scene) : "";
                        sb.Append(n++ > 0 ? "," : "");
                        CatalogueInfo.Write(e, sb, compat, map);
                    }
                }
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the mission catalogue could not be read in full: " + e.Message); }
            return sb.Append("],\"skipped\":").Append(skipped).Append(",\"folder\":\"BepInEx/MissionLab/scenarios\"}").ToString();
        }

        private static HttpResponse Refuse(int status, string why, List<Finding> found, string file, bool exists)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"saved\":false,\"why\":\"").Append(Guard.Esc(why)).Append("\",\"file\":\"").Append(Guard.Esc(file)).Append("\",\"exists\":").Append(exists ? "true" : "false").Append(",\"findings\":");
            CatalogueInfo.WriteFindings(sb, found);
            return HttpResponse.Json(sb.Append('}').ToString(), status);
        }

        /// <summary>
        /// {name, text, replace}: the text is read strictly first and refused with its findings when MissionLab would refuse it
        /// at mission start. The name becomes a safe file name directly in the scenarios folder. An existing file is written
        /// over only when replace is true; the new text goes to a temporary file first, so a failed write leaves the old file whole.
        /// </summary>
        private static HttpResponse Save(string body)
        {
            string name = "", text = null; bool replace = false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body ?? ""))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return Refuse(400, "expected {name, text, replace}", null, "", false);
                    if (r.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
                    if (r.TryGetProperty("text", out JsonElement t) && t.ValueKind == JsonValueKind.String) text = t.GetString();
                    if (r.TryGetProperty("replace", out JsonElement rp) && rp.ValueKind == JsonValueKind.True) replace = true;
                }
            }
            catch (Exception e) { return Refuse(400, "the request is not JSON: " + e.Message, null, "", false); }
            if (string.IsNullOrEmpty(text)) return Refuse(400, "there is no scenario text to save", null, "", false);
            if (text.Length > MaxBytes) return Refuse(413, "the scenario is too large to save", null, "", false);
            string file = CatalogueInfo.SafeName(name);
            if (file == "") return Refuse(400, "give the scenario a file name made of letters, digits, '_' or '-'", null, "", false);

            List<Finding> found = ScenarioFile.Read(text, out MissionPlan plan);
            if (plan == null)
            {
                Finding first = found.Find(f => f.Level == Finding.Error);
                return Refuse(422, "not saved: MissionLab would refuse this scenario at mission start" + (first != null ? " (" + first + ")" : ""), found, file, false);
            }
            if (plan.Spawns.Count == 0) return Refuse(422, "not saved: it has no enemies, so MissionLab would leave the game's own mission", found, file, false);

            string path = Path.Combine(Dir, file), temp = path + ".saving";
            bool exists = File.Exists(path);
            if (exists && !replace) return Refuse(409, "a scenario named " + file + " is already there; it was left as it is. Save again with 'replace' to write over it", null, file, true);
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                File.Move(temp, path, true);
            }
            catch (Exception e)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                DirectorPlugin.L.LogWarning("scenario " + file + " could not be saved: " + e.Message);
                return Refuse(500, "the file could not be written: " + e.Message, null, file, exists);
            }
            DirectorPlugin.L.LogInfo("scenario saved from the page: " + file + (exists ? " (written over)" : ""));
            var sb = new StringBuilder(256);
            sb.Append("{\"saved\":true,\"file\":\"").Append(Guard.Esc(file)).Append("\",\"value\":\"").Append(Guard.Esc(CatalogueInfo.ValueOf(file)))
              .Append("\",\"replaced\":").Append(exists ? "true" : "false").Append(",\"hash\":\"").Append(ScenarioFile.Hash(text)).Append("\",\"findings\":");
            CatalogueInfo.WriteFindings(sb, found);
            return HttpResponse.Json(sb.Append('}').ToString());
        }
    }
}
