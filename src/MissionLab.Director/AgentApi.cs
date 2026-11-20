using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MissionLab.Core;

namespace MissionLab
{
    /// <summary>
    /// The local draft-authoring surface for coding agents (ML-053), behind the browser link's own protections (loopback,
    /// Origin, X-MissionLab on every non-GET, X-MissionLab-Command for a proposal sent twice):
    ///   GET  /api/agent/scenarios                 every scenario file: file, id, revision, hash, title, capabilities
    ///   GET  /api/agent/scenario/&lt;file&gt;          one scenario: its text, named places, triggers, goals and enemies by index
    ///   GET  /api/agent/capabilities              the capability list, judged for this build
    ///   POST /api/agent/propose                   {file, expectedHash, expectedRevision, name, operations, validate, players}
    ///   GET  /api/agent/proposals                 the kept proposals, each with whether its scenario has changed since
    ///   GET  /api/agent/proposal/&lt;id&gt;            one kept proposal
    ///   POST /api/agent/proposal/&lt;id&gt;/discard    forgets it
    ///   POST /api/agent/check                     {file} or {proposal}, with players, seeds, budget: the bounded validation
    /// Files only, on the browser link's worker thread: nothing here touches the game or the running mission, and no game
    /// file, map, recon or run record is read or returned. A proposal is kept in proposals/&lt;id&gt;.json, never in scenarios/:
    /// the author reviews it in the editor and saves it through the normal path.
    /// </summary>
    internal static class AgentApi
    {
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "proposals");
        private const string Prefix = "/api/agent/";
        private const int MaxFiles = 200;
        private const long MaxBytes = 4L * 1024 * 1024;
        private const int CheckSeconds = 10;           // a check or a proposal's validation holds a worker thread this long at most
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly object Gate = new object();
        private static int _count;
        private static CapabilityRegistry _registry;

        // The capability list built into this DLL, read on first use; a broken list only means less is claimed.
        private static CapabilityRegistry Registry
        {
            get
            {
                lock (Gate)
                {
                    if (_registry == null)
                    {
                        _registry = CapabilityRegistry.Embedded();
                        foreach (string p in _registry.Problems) DirectorPlugin.L.LogWarning("capability list: " + p + "; agent proposals are judged with what could be read");
                    }
                    return _registry;
                }
            }
        }

        /// <summary>Worker thread. null: not an agent request.</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            if (!r.Path.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            string rest = r.Path.Substring(Prefix.Length);
            bool get = r.Method == "GET";
            try
            {
                if (rest == "scenarios") return get ? HttpResponse.Json(List()) : HttpResponse.Error(400, "GET expected");
                if (rest == "capabilities") return get ? HttpResponse.Json(AgentDraft.CapabilitiesJson(Registry, DirectorPlugin.Version)) : HttpResponse.Error(400, "GET expected");
                if (rest.StartsWith("scenario/", StringComparison.Ordinal)) return get ? One(Uri.UnescapeDataString(rest.Substring("scenario/".Length))) : HttpResponse.Error(400, "GET expected");
                if (rest == "propose") return r.Method == "POST" ? Once(r, Propose) : HttpResponse.Error(400, "POST expected");
                if (rest == "proposals") return get ? HttpResponse.Json(Proposals()) : HttpResponse.Error(400, "GET expected");
                if (rest.StartsWith("proposal/", StringComparison.Ordinal))
                {
                    string[] parts = rest.Substring("proposal/".Length).Split('/');
                    if (!AgentDraft.GoodProposalId(parts[0])) return HttpResponse.Error(400, "not a proposal id");
                    if (parts.Length == 1) return get ? Proposal(parts[0]) : HttpResponse.Error(400, "GET expected");
                    if (parts.Length == 2 && parts[1] == "discard") return r.Method == "POST" ? Once(r, body => Discard(parts[0])) : HttpResponse.Error(400, "POST expected");
                    return HttpResponse.Error(404, "not an agent path");
                }
                if (rest == "check") return r.Method == "POST" ? Check(r.BodyText) : HttpResponse.Error(400, "POST expected");
                return HttpResponse.Error(404, "not an agent path");
            }
            catch (Exception e)
            {
                DirectorPlugin.L.LogWarning("agent request " + r.Path + " failed: " + e.Message);
                return HttpResponse.Error(500, "the agent request could not be answered: " + e.Message);
            }
        }

        // A change sent twice with the same X-MissionLab-Command is carried out once; the second time gets the first answer.
        private static HttpResponse Once(HttpRequest r, Func<string, HttpResponse> work)
        {
            string command = r.Headers.TryGetValue("X-MissionLab-Command", out string number) ? CommandLedger.Clean(number) : "";
            if (command != "" && !LiveLink.Ledger.Begin(command, "", r.Path, 0f, out CommandLedger.Entry seen))
                return !seen.Done ? HttpResponse.Json(LiveLink.Ledger.JsonOf(command) ?? CommandLedger.Json(seen), 202)
                                  : new HttpResponse { Status = seen.Code, Body = Encoding.UTF8.GetBytes(seen.Body) };
            HttpResponse res;
            try { res = work(r.BodyText); }
            catch (Exception e)
            {
                DirectorPlugin.L.LogWarning("agent request " + r.Path + " failed: " + e.Message);
                res = HttpResponse.Error(500, "the agent request could not be carried out: " + e.Message);
            }
            if (command != "") LiveLink.Ledger.Finish(command, res.Status < 400 ? CommandLedger.Applied : CommandLedger.Failed, res.Status, Encoding.UTF8.GetString(res.Body), "");
            return res;
        }

        // ----- scenarios: read only

        private static List<string> ScenarioFiles()
        {
            var files = new List<string>();
            if (!Directory.Exists(Catalogue.Dir)) return files;
            foreach (string f in Directory.GetFiles(Catalogue.Dir, "*.json", SearchOption.TopDirectoryOnly)) files.Add(f);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        // The text of one scenario file, or null with the answer that says why not.
        private static string Read(string file, out HttpResponse refused)
        {
            refused = null;
            if (!AgentDraft.GoodFile(file)) { refused = HttpResponse.Error(400, "not a scenario file name"); return null; }
            string path = Path.Combine(Catalogue.Dir, file);
            if (!File.Exists(path)) { refused = HttpResponse.Error(404, "there is no scenario " + file + " in BepInEx/MissionLab/scenarios"); return null; }
            if (new FileInfo(path).Length > MaxBytes) { refused = HttpResponse.Error(413, "the scenario is too large to be one the page made"); return null; }
            return File.ReadAllText(path);
        }

        private static string List()
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"schema\":\"missionlab-agent-scenarios/1\",\"plugin\":\"").Append(Guard.Esc(DirectorPlugin.Version)).Append("\",\"scenarios\":[");
            int n = 0, skipped = 0;
            foreach (string path in ScenarioFiles())
            {
                if (n >= MaxFiles) { skipped++; continue; }
                string text;
                try
                {
                    if (new FileInfo(path).Length > MaxBytes) { skipped++; continue; }
                    text = File.ReadAllText(path);
                }
                catch (Exception) { skipped++; continue; }      // a file being written right now is listed next time
                CatalogueEntry e = CatalogueInfo.Build(Path.GetFileName(path), text, null);
                CatalogueInfo.Check(e, Registry, DirectorPlugin.Version, null);
                sb.Append(n++ > 0 ? "," : "");
                AgentDraft.ListEntry(sb, e);
            }
            return sb.Append("],\"skipped\":").Append(skipped.ToString(Inv)).Append('}').ToString();
        }

        private static HttpResponse One(string file)
        {
            string text = Read(file, out HttpResponse refused);
            return text == null ? refused : HttpResponse.Json(AgentDraft.ScenarioJson(file, text, Registry, DirectorPlugin.Version));
        }

        // ----- proposals

        private static HttpResponse Propose(string body)
        {
            string why = AgentDraft.ParseAsk(body, out DraftAsk ask);
            if (why != "") return HttpResponse.Error(400, why);
            string source = Read(ask.File, out HttpResponse refused);
            if (source == null) return refused;
            DraftResult res;
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(CheckSeconds)))
                res = AgentDraft.Propose(source, ask, Registry, DirectorPlugin.Version, ask.Validate ? Saved(source) : null, cancel.Token);
            string id = "", kept = "";
            if (res.Accepted)
            {
                string made = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv);
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    id = NewId();
                    Write(Path.Combine(Dir, id + ".json"), AgentDraft.ProposalJson(res, ask, id, made, source));
                }
                kept = "proposals/" + id + ".json";
                DirectorPlugin.L.LogInfo("agent proposal " + id + " kept for " + ask.File + " (" + res.Changes.Count + " changes); nothing changes until the author applies and saves it");
            }
            return HttpResponse.Json(AgentDraft.AnswerJson(res, ask.File, id, kept), res.Accepted ? 200 : res.Status);
        }

        // Under Gate. Time first, so the folder lists in the order they were made; a number for two in one second.
        private static string NewId()
        {
            string id;
            do id = "p" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", Inv) + "-" + (++_count).ToString(Inv);
            while (File.Exists(Path.Combine(Dir, id + ".json")));
            return id;
        }

        // Written beside the file and then moved into place, so a crash never leaves half a proposal under its name.
        private static void Write(string full, string text)
        {
            string temp = full + ".part";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, full, true);
        }

        // The scenario file as it is now, for telling whether a proposal is stale: its hash ("" when gone) and revision.
        private static string Current(string file, Dictionary<string, KeyValuePair<string, int>> seen, out int revision)
        {
            revision = 0;
            if (!AgentDraft.GoodFile(file)) return "";
            if (!seen.TryGetValue(file, out KeyValuePair<string, int> now))
            {
                now = new KeyValuePair<string, int>("", 0);
                try
                {
                    string path = Path.Combine(Catalogue.Dir, file);
                    if (File.Exists(path) && new FileInfo(path).Length <= MaxBytes)
                    {
                        string text = File.ReadAllText(path);
                        CatalogueInfo.Identity(text, out _, out int rev);
                        now = new KeyValuePair<string, int>(ScenarioFile.Hash(text), rev);
                    }
                }
                catch (Exception) { }          // unreadable now: shown as stale, which is the safe side
                seen[file] = now;
            }
            revision = now.Value;
            return now.Key;
        }

        private static string Proposals()
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"schema\":\"missionlab-proposals/1\",\"folder\":\"BepInEx/MissionLab/proposals\",\"proposals\":[");
            var files = new List<string>();
            if (Directory.Exists(Dir)) foreach (string f in Directory.GetFiles(Dir, "p*.json", SearchOption.TopDirectoryOnly)) files.Add(f);
            files.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));       // newest first
            var seen = new Dictionary<string, KeyValuePair<string, int>>(StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (string f in files)
            {
                if (n >= AgentDraft.MaxListed) break;
                string record;
                try { record = File.ReadAllText(f); } catch (Exception) { continue; }
                string hash = Current(AgentDraft.RecordField(record, "file"), seen, out int revision);
                var row = new StringBuilder(512);
                if (!AgentDraft.Summary(row, record, hash, revision)) continue;
                sb.Append(n++ > 0 ? "," : "").Append(row);
            }
            return sb.Append("]}").ToString();
        }

        private static HttpResponse Proposal(string id)
        {
            string path = Path.Combine(Dir, id + ".json");
            if (!File.Exists(path)) return HttpResponse.Error(404, "there is no proposal " + id);
            string record = File.ReadAllText(path);
            string hash = Current(AgentDraft.RecordField(record, "file"), new Dictionary<string, KeyValuePair<string, int>>(StringComparer.OrdinalIgnoreCase), out int revision);
            string answer = AgentDraft.Wrapped(record, hash, revision);
            return answer != null ? HttpResponse.Json(answer) : HttpResponse.Error(422, "proposal " + id + " cannot be read");
        }

        private static HttpResponse Discard(string id)
        {
            string path = Path.Combine(Dir, id + ".json");
            lock (Gate)
            {
                if (!File.Exists(path)) return HttpResponse.Error(404, "there is no proposal " + id);
                File.Delete(path);
            }
            DirectorPlugin.L.LogInfo("agent proposal " + id + " discarded");
            return HttpResponse.Json("{\"discarded\":true,\"id\":\"" + id + "\"}");
        }

        // ----- the bounded validation

        private static HttpResponse Check(string body)
        {
            string why = AgentDraft.ParseCheck(body, out DraftCheck ask);
            if (why != "") return HttpResponse.Error(400, why);
            string text;
            if (ask.File != "")
            {
                text = Read(ask.File, out HttpResponse refused);
                if (text == null) return refused;
            }
            else
            {
                string path = Path.Combine(Dir, ask.Proposal + ".json");
                if (!File.Exists(path)) return HttpResponse.Error(404, "there is no proposal " + ask.Proposal);
                text = AgentDraft.RecordField(File.ReadAllText(path), "scenario");
                if (text == "") return HttpResponse.Error(422, "proposal " + ask.Proposal + " holds no scenario text");
            }
            ValidationReport report;
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(CheckSeconds)))
                report = AgentDraft.Validate(text, Saved(text), ask.Players, ask.Seeds, ask.Budget, cancel.Token);
            var sb = new StringBuilder(4096);
            sb.Append("{\"file\":\"").Append(Guard.Esc(ask.File)).Append("\",\"proposal\":\"").Append(Guard.Esc(ask.Proposal)).Append("\",\"hash\":\"").Append(ScenarioFile.Hash(text))
              .Append("\",\"kept\":false,\"report\":").Append(MissionValidation.Json(report)).Append('}');
            return HttpResponse.Json(sb.ToString());
        }

        // The rehearsals kept for the scenario's id (Rehearsals.cs keeps them), by file name, as the validation matrix reads them.
        private static ValidationInputs Saved(string text)
        {
            var inputs = new ValidationInputs();
            CatalogueInfo.Identity(text, out string id, out _);
            if (!RevisionFile.GoodId(id)) { inputs.Rehearsals = null; inputs.RehearsalsWhy = "not run: the scenario has no id yet, so no rehearsal can be kept for it"; return inputs; }
            var list = new List<KeyValuePair<string, string>>();
            string folder = Path.Combine(Rehearsals.Dir, id);
            try
            {
                if (Directory.Exists(folder))
                    foreach (string f in Directory.GetFiles(folder, "*.json"))
                    {
                        string file = Path.GetFileName(f);
                        if (RehearsalFile.GoodFile(file)) list.Add(new KeyValuePair<string, string>(file, File.ReadAllText(f)));
                    }
                inputs.Rehearsals = list;
            }
            catch (Exception e) { inputs.Rehearsals = null; inputs.RehearsalsWhy = "not run: the saved rehearsals could not be read: " + e.Message; }
            return inputs;
        }
    }
}
