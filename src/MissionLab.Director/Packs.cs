using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// Installing a scenario pack (ML-026): POST /api/pack/install {"pack": text, "choices": {"raid.json": "copy"|"revision"|"skip"}}.
    /// The pack is read strictly first (Core Pack.Read) and refused whole when anything in it is wrong. Scenarios go directly
    /// into scenarios/, modules into modules/. Nothing is ever written over: a file whose name or scenario id is already
    /// there goes in as a copy or a new revision under a new name, or is skipped, as the page chose (Core Pack.Plan).
    /// The answer lists what became of every file. Files only, answered on the browser link's worker thread; the page shows
    /// the preview, so there is no separate check request.
    /// </summary>
    internal static class Packs
    {
        private const int MaxListed = 400;                       // local files looked at for collisions; the catalogue lists 200
        private static readonly object Gate = new object();      // two installs at once must not both take the same free name

        /// <summary>Worker thread. null: not a pack request.</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            if (r.Path != "/api/pack/install") return null;
            if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
            try { return Install(r.BodyText); }
            catch (Exception e)
            {
                DirectorPlugin.L.LogWarning("a pack could not be installed: " + e.Message);
                return HttpResponse.Error(500, "the pack could not be installed: " + e.Message);
            }
        }

        private static HttpResponse Install(string body)
        {
            string text = null;
            var choices = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body ?? ""))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return Refuse(400, "expected {pack, choices}", null);
                    if (root.TryGetProperty("pack", out JsonElement p) && p.ValueKind == JsonValueKind.String) text = p.GetString();
                    if (root.TryGetProperty("choices", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
                        foreach (JsonProperty one in c.EnumerateObject())
                            if (one.Value.ValueKind == JsonValueKind.String) choices[one.Name] = one.Value.GetString();
                }
            }
            catch (JsonException e) { return Refuse(400, "the request is not JSON: " + e.Message, null); }
            if (string.IsNullOrEmpty(text)) return Refuse(400, "there is no pack text to install", null);

            List<Finding> found = Pack.Read(text, DirectorPlugin.Version, out PackInfo pack);
            if (pack == null)
            {
                Finding first = found.Find(f => f.Level == Finding.Error);
                return Refuse(422, "nothing was installed: the pack has problems" + (first != null ? " (" + first + ")" : ""), found);
            }

            List<PackStep> steps;
            var failed = new Dictionary<PackStep, string>();
            lock (Gate)
            {
                steps = Pack.Plan(pack, choices, Local(Catalogue.Dir, "*.json", true), Local(Modules.Dir, "*.module.json", false));
                foreach (PackStep s in steps)
                {
                    if (s.Action == PackStep.Skip || s.Action == PackStep.Same) continue;
                    string dir = s.Kind == PackEntry.Module ? Modules.Dir : Catalogue.Dir, path = Path.Combine(dir, s.File), temp = path + ".installing";
                    try
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(temp, s.Text, new UTF8Encoding(false));
                        // Not over anything: a file that appeared since the plan was made stays, and this one is reported as not written.
                        File.Move(temp, path, false);
                    }
                    catch (Exception e)
                    {
                        try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                        failed[s] = e.Message;
                    }
                }
            }

            int written = 0;
            var sb = new StringBuilder(1024);
            sb.Append("{\"ok\":true,\"pack\":\"").Append(Guard.Esc(pack.Name)).Append("\",\"files\":[");
            for (int i = 0; i < steps.Count; i++)
            {
                PackStep s = steps[i];
                bool fail = failed.TryGetValue(s, out string why);
                bool wrote = !fail && s.Action != PackStep.Skip && s.Action != PackStep.Same;
                if (wrote) written++;
                sb.Append(i > 0 ? "," : "").Append("{\"name\":\"").Append(Guard.Esc(s.Name)).Append("\",\"kind\":\"").Append(Guard.Esc(s.Kind))
                  .Append("\",\"action\":\"").Append(Guard.Esc(s.Action)).Append("\",\"written\":").Append(wrote ? "true" : "false")
                  .Append(",\"file\":\"").Append(Guard.Esc(s.File)).Append("\",\"folder\":\"").Append(s.Kind == PackEntry.Module ? "BepInEx/MissionLab/modules" : "BepInEx/MissionLab/scenarios")
                  .Append("\",\"id\":\"").Append(Guard.Esc(s.Id)).Append("\",\"revision\":").Append(s.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append(",\"hash\":\"").Append(Guard.Esc(s.Hash)).Append("\",\"why\":\"").Append(Guard.Esc(fail ? "not written: " + why : s.Why)).Append("\"}");
            }
            sb.Append("],\"written\":").Append(written.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\"findings\":");
            CatalogueInfo.WriteFindings(sb, found);
            DirectorPlugin.L.LogInfo("pack \"" + pack.Name + "\" installed from the page: " + written + " of " + steps.Count + " files written");
            return HttpResponse.Json(sb.Append('}').ToString());
        }

        /// <summary>The files directly in a folder, with their hash and, for scenarios, id and revision. Unreadable ones count by name.</summary>
        private static List<PackLocal> Local(string dir, string pattern, bool scenarios)
        {
            var list = new List<PackLocal>();
            if (!Directory.Exists(dir)) return list;
            foreach (string f in Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly))
            {
                var l = new PackLocal { Name = Path.GetFileName(f) };
                list.Add(l);
                if (list.Count > MaxListed) continue;       // still counted by name, never read
                try
                {
                    if (new FileInfo(f).Length > Pack.MaxScenarioBytes) continue;
                    string text = File.ReadAllText(f);
                    l.Hash = ScenarioFile.Hash(text);
                    if (scenarios) CatalogueInfo.Identity(text, out l.Id, out l.Revision);
                }
                catch (Exception) { }
            }
            return list;
        }

        private static HttpResponse Refuse(int status, string why, List<Finding> found)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"ok\":false,\"why\":\"").Append(Guard.Esc(why)).Append("\",\"files\":[],\"written\":0,\"findings\":");
            CatalogueInfo.WriteFindings(sb, found);
            return HttpResponse.Json(sb.Append('}').ToString(), status);
        }
    }
}
