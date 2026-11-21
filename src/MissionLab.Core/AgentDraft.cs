using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MissionLab.Core
{
    /// <summary>What a coding agent asks for with POST /api/agent/propose (ML-053).</summary>
    public sealed class DraftAsk
    {
        public string File = "";              // the scenario file in BepInEx/MissionLab/scenarios it is meant for
        public string ExpectedHash = "";      // ScenarioFile.Hash of the text the agent read ("": not given)
        public int ExpectedRevision = -1;     // the revision the agent read (-1: not given)
        public string Name = "";              // a few words for the author's list
        public string Operations = "[]";      // the operations as sent, a JSON list
        public bool Validate;                 // also run the bounded validation: completion matrix and the saved rehearsals
        public int Players;                   // squad size for the completion check (0: any)
    }

    /// <summary>What became of a proposal: accepted with its text, or refused with the findings the editor would show.</summary>
    public sealed class DraftResult
    {
        public bool Accepted;
        public int Status = 200;              // the HTTP answer: 200 accepted, 400 a malformed request, 409 stale, 422 refused
        public string Why = "";
        public List<Finding> Findings = new List<Finding>();
        public List<string> Changes = new List<string>();     // each operation in words, in order
        public List<string> Capabilities = new List<string>();
        public string Text = "", Hash = "";                   // the proposed scenario as MissionPlan.ToJson writes it
        public string BaseHash = "", ScenarioId = "";
        public int BaseRevision;
        public ValidationReport Report;                        // null when no validation was asked for
    }

    /// <summary>A check asked for with POST /api/agent/check: one scenario file or one kept proposal.</summary>
    public sealed class DraftCheck
    {
        public string File = "", Proposal = "";
        public List<int> Players = new List<int>();
        public List<long> Seeds = new List<long>();
        public int Budget = AgentDraft.MaxBudget;
    }

    /// <summary>
    /// Draft authoring for coding agents (ML-053). An agent reads one scenario, names what to change with a small set of
    /// structured operations that use stable ids (trigger, place and goal ids; an enemy by its index, only together with the
    /// hash of the text that index belongs to), and gets back either a proposal or the findings that refuse it.
    ///
    /// The operations are applied to a copy read from the text: the text itself, the file and the running mission are never
    /// changed. The result is judged by the same rules as everything else: its ToJson goes through the strict reader (so a
    /// dangling reference is refused with the reader's own code and path), Completion (only problems the scenario did not
    /// already have), the capability list (an operation may not add a feature recorded as failing or unavailable) and, when
    /// asked, a bounded validation with the scenario's saved rehearsals. There are no rules of the agent's own: a proposal
    /// is a scenario text, and the author reviews and applies it in the editor like any other separate draft.
    /// </summary>
    public static class AgentDraft
    {
        public const string ProposalSchema = "missionlab-proposal/1";
        public const string StaleCode = "stale";                 // the scenario changed since the agent read it
        public const int MaxOperations = 64, NameLength = 80, MaxBudget = 500, MaxListed = 100;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        // ----- names that may reach the file system

        /// <summary>A scenario file name as the scenarios folder holds it: a name only, never a folder, ending in ".json".</summary>
        public static bool GoodFile(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 120 || name.Contains("..") || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0) return false;
            return name == Path.GetFileName(name) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A proposal id as the plugin makes them: "p", then 1 to 47 of a-z, 0-9 and '-'.</summary>
        public static bool GoodProposalId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 2 || id.Length > 48 || id[0] != 'p') return false;
            foreach (char c in id) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
            return true;
        }

        /// <summary>Whether a kept proposal no longer fits its scenario: the file has gone, or its text is not the one it was made for.</summary>
        public static bool IsStale(string baseHash, string currentHash) => string.IsNullOrEmpty(currentHash) || currentHash != baseHash;

        // ----- the request

        /// <summary>Reads {file, expectedHash, expectedRevision, name, operations, validate, players}. "" with what was asked, or why it cannot be used.</summary>
        public static string ParseAsk(string body, out DraftAsk ask)
        {
            ask = new DraftAsk();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body ?? "", Lenient))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return "a proposal is sent as {file, expectedHash, expectedRevision, name, operations}";
                    ask.File = Text(r, "file") ?? "";
                    ask.ExpectedHash = Text(r, "expectedHash") ?? "";
                    if (r.TryGetProperty("expectedRevision", out JsonElement rev) && rev.ValueKind != JsonValueKind.Null)
                    {
                        if (rev.ValueKind != JsonValueKind.Number || !rev.TryGetInt32(out int n) || n < 0) return "expectedRevision is a whole number from 0";
                        ask.ExpectedRevision = n;
                    }
                    string name = GoalPlan.Clean(Text(r, "name") ?? "");
                    ask.Name = name.Length > NameLength ? name.Substring(0, NameLength) : name;
                    if (!r.TryGetProperty("operations", out JsonElement ops) || ops.ValueKind != JsonValueKind.Array) return "operations is a list of operations";
                    ask.Operations = ops.GetRawText();
                    ask.Validate = r.TryGetProperty("validate", out JsonElement v) && v.ValueKind == JsonValueKind.True;
                    if (r.TryGetProperty("players", out JsonElement pl) && pl.ValueKind != JsonValueKind.Null)
                    {
                        if (pl.ValueKind != JsonValueKind.Number || !pl.TryGetInt32(out int p) || p < 0 || p > MissionValidation.MaxPlayers) return "players is a whole number from 0 to " + MissionValidation.MaxPlayers;
                        ask.Players = p;
                    }
                }
            }
            catch (JsonException) { return "the request is not JSON"; }
            if (!GoodFile(ask.File)) return "file names one scenario file in the scenarios folder, such as \"house.json\"";
            return "";
        }

        /// <summary>Reads {file} or {proposal}, with players, seeds (a list or a count) and budget. "" with what was asked, or why not.</summary>
        public static string ParseCheck(string body, out DraftCheck check)
        {
            check = new DraftCheck();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body ?? "", Lenient))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return "a check is sent as {file} or {proposal}, with players, seeds and budget";
                    check.File = Text(r, "file") ?? "";
                    check.Proposal = Text(r, "proposal") ?? "";
                    if (r.TryGetProperty("players", out JsonElement pl) && pl.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in pl.EnumerateArray())
                        {
                            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out int n) || n < 1 || n > MissionValidation.MaxPlayers) return "every squad size is a whole number from 1 to " + MissionValidation.MaxPlayers;
                            if (!check.Players.Contains(n)) check.Players.Add(n);
                        }
                    if (r.TryGetProperty("seeds", out JsonElement sd))
                    {
                        if (sd.ValueKind == JsonValueKind.Number && sd.TryGetInt32(out int count) && count >= 1)
                            for (int i = 1; i <= Math.Min(count, MissionValidation.MaxSeeds); i++) check.Seeds.Add(i);
                        else if (sd.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement e in sd.EnumerateArray())
                            {
                                if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out long one)) return "every seed is a whole number";
                                if (check.Seeds.Count >= MissionValidation.MaxSeeds) return "at most " + MissionValidation.MaxSeeds + " seeds are tried at once";
                                if (!check.Seeds.Contains(one)) check.Seeds.Add(one);
                            }
                    }
                    if (r.TryGetProperty("budget", out JsonElement bu) && bu.ValueKind == JsonValueKind.Number && bu.TryGetInt32(out int budget)) check.Budget = Math.Max(1, Math.Min(MaxBudget, budget));
                }
            }
            catch (JsonException) { return "the request is not JSON"; }
            bool file = check.File != "", proposal = check.Proposal != "";
            if (file == proposal) return "name either a scenario file or a proposal, not both and not neither";
            if (file && !GoodFile(check.File)) return "file names one scenario file in the scenarios folder, such as \"house.json\"";
            if (proposal && !GoodProposalId(check.Proposal)) return "proposal is a proposal id, such as \"p20261120-1\"";
            return "";
        }

        // ----- the proposal

        /// <summary>
        /// Applies the operations to a copy of source and judges the result. source is never changed. inputs carries the
        /// scenario's saved rehearsals for a validation (ask.Validate); null when they are not to hand. registry and current
        /// judge capabilities as the catalogue does; a null registry only means less is known, never that more is allowed.
        /// </summary>
        public static DraftResult Propose(string source, DraftAsk ask, CapabilityRegistry registry, string current, ValidationInputs inputs, CancellationToken cancel)
        {
            var r = new DraftResult { BaseHash = ScenarioFile.Hash(source ?? "") };
            ask = ask ?? new DraftAsk();
            List<Finding> read = ScenarioFile.Read(source ?? "", out MissionPlan plan);
            if (plan == null) return Refuse(r, 422, "the scenario itself is refused by the strict reader, so nothing can be proposed against it; open it in the editor and fix it first", Errors(read));
            r.BaseRevision = plan.Revision; r.ScenarioId = plan.Id ?? "";

            // Only against the text that was read: an index or a sentence about the old text would land on something else.
            if (ask.ExpectedHash == "" && ask.ExpectedRevision < 0)
                return Refuse(r, 400, "send expectedHash (and/or expectedRevision) from the scenario you read, so a proposal never lands on a text that changed under it",
                    One("missing-field", "expectedHash", "the hash of the scenario text the operations were written against is missing"));
            if (ask.ExpectedHash != "" && ask.ExpectedHash != r.BaseHash)
                return Refuse(r, 409, "the scenario changed since it was read; read it again and propose against what is there now",
                    One(StaleCode, "expectedHash", "the operations were written for " + ask.ExpectedHash + " and the scenario is now " + r.BaseHash));
            if (ask.ExpectedRevision >= 0 && ask.ExpectedRevision != plan.Revision)
                return Refuse(r, 409, "the scenario changed since it was read; read it again and propose against what is there now",
                    One(StaleCode, "expectedRevision", "the operations were written for revision " + ask.ExpectedRevision.ToString(Inv) + " and the scenario is now revision " + plan.Revision.ToString(Inv)));

            var found = new List<Finding>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(ask.Operations ?? "", Lenient))
                {
                    JsonElement ops = doc.RootElement;
                    if (ops.ValueKind != JsonValueKind.Array) return Refuse(r, 400, "operations is a list of operations", One("wrong-type", "operations", "\"operations\" is a list"));
                    int count = ops.GetArrayLength();
                    if (count == 0 || count > MaxOperations) return Refuse(r, 400, "a proposal has 1 to " + MaxOperations + " operations", One("out-of-range", "operations", "a proposal has 1 to " + MaxOperations + " operations; it has " + count.ToString(Inv)));
                    int k = 0;
                    foreach (JsonElement op in ops.EnumerateArray()) Apply(plan, op, "operations[" + (k++).ToString(Inv) + "]", ask, found, r.Changes, registry, current);
                }
            }
            catch (JsonException e) { return Refuse(r, 400, "the operations are not JSON", One("not-json", "operations", e.Message)); }
            if (ScenarioFile.Blocks(found)) return Refuse(r, 422, "an operation was refused, so nothing was proposed", found);
            r.Findings.AddRange(found);          // only notes are left: features that are experimental

            // The same reader as at a mission's start, on exactly the text that would be saved.
            string text = plan.ToJson();
            List<Finding> strict = ScenarioFile.Read(text, out MissionPlan after);
            if (after == null) return Refuse(r, 422, "MissionLab would refuse the changed scenario at mission start, so nothing was proposed", Errors(strict));
            foreach (Finding f in strict) if (f.Level != Finding.Error) r.Findings.Add(f);

            // Completion: only what the change breaks. A problem the scenario already had is the author's, and stays a note.
            ScenarioFile.Read(source, out MissionPlan original);
            List<Finding> was = Completion.Check(original, null, ask.Players), now = Completion.Check(after, null, ask.Players);
            var broken = new List<Finding>();
            foreach (Finding f in now)
            {
                bool old = was.Exists(b => b.Level == f.Level && b.Code == f.Code && b.Path == f.Path && b.Text == f.Text);
                if (f.Level == Finding.Error && !old) broken.Add(f);
                else if (!old) r.Findings.Add(f);
            }
            if (broken.Count > 0) return Refuse(r, 422, "the changed mission could no longer be finished, so nothing was proposed", broken);
            foreach (Finding f in MissionLab.Core.Capabilities.Needs(after, null, registry, current)) r.Findings.Add(f);
            r.Capabilities = MissionLab.Core.Capabilities.Of(after);

            if (ask.Validate)
            {
                r.Report = Validate(text, inputs, ask.Players > 0 ? new List<int> { ask.Players } : null, null, MaxBudget, cancel);
                List<Finding> failed = ValidationFindings(r.Report);
                if (failed.Count > 0) return Refuse(r, 422, "the bounded validation failed, so nothing was proposed", failed);
            }
            r.Accepted = true;
            r.Text = text;
            r.Hash = ScenarioFile.Hash(text);
            return r;
        }

        /// <summary>
        /// The bounded validation an agent may run: the strict reader, the completion matrix over squad sizes and seeds, and the
        /// scenario's saved rehearsals (inputs.Rehearsals). Never the scatter check: that would read the game's map, which this
        /// surface does not touch. At most MaxBudget combinations; cancel stops it early, and the report says so.
        /// </summary>
        public static ValidationReport Validate(string text, ValidationInputs inputs, List<int> players, List<long> seeds, int budget, CancellationToken cancel)
        {
            var ask = new ValidationAsk { Completion = true, Scatter = false, Rehearsals = true, Budget = Math.Max(1, Math.Min(MaxBudget, budget)) };
            if (players != null) ask.Players.AddRange(players);
            if (seeds != null) ask.Seeds.AddRange(seeds);
            var given = inputs ?? new ValidationInputs { Rehearsals = null, RehearsalsWhy = "not run: the saved rehearsals were not to hand" };
            var safe = new ValidationInputs { Rehearsals = given.Rehearsals, RehearsalsWhy = given.RehearsalsWhy };
            return MissionValidation.Run(text, ask, safe, cancel);
        }

        /// <summary>A failed check of a validation report as error findings, one per check, with its first counterexample.</summary>
        public static List<Finding> ValidationFindings(ValidationReport report)
        {
            var list = new List<Finding>();
            if (report == null) return list;
            foreach (ValidationCheck c in report.Checks)
            {
                if (c.State != MissionValidation.Failed) continue;
                ValidationCounter x = c.Counter;
                string what = x == null ? (c.Why != "" ? c.Why : "it failed") :
                    (x.Rehearsal != "" ? "saved rehearsal " + x.Rehearsal + ": " : "seed " + x.Seed.ToString(Inv) + ", " + x.Players.ToString(Inv) + " players: ") +
                    (x.Found != "" ? x.Found : x.Expected);
                list.Add(new Finding { Code = "validation-failed", Path = x != null ? x.Path : "", Text = "the " + c.Name + " check failed (" + c.Failed.ToString(Inv) + " of " + c.Executed.ToString(Inv) + "): " + what });
            }
            return list;
        }

        private static DraftResult Refuse(DraftResult r, int status, string why, List<Finding> found)
        {
            r.Accepted = false; r.Status = status; r.Why = why;
            r.Findings = found ?? new List<Finding>();
            r.Text = ""; r.Hash = "";
            return r;
        }

        private static List<Finding> One(string code, string path, string text) => new List<Finding> { new Finding { Code = code, Path = path, Text = text } };

        private static List<Finding> Errors(List<Finding> found)
        {
            var list = new List<Finding>();
            foreach (Finding f in found) if (f.Level == Finding.Error) list.Add(f);
            return list;
        }

        // ----- the operations

        private static void Bad(List<Finding> found, string code, string path, string text) => found.Add(new Finding { Code = code, Path = path, Text = text });

        private static void Apply(MissionPlan plan, JsonElement op, string at, DraftAsk ask, List<Finding> found, List<string> changes, CapabilityRegistry registry, string current)
        {
            if (op.ValueKind != JsonValueKind.Object) { Bad(found, "wrong-type", at, "an operation is an object with \"op\""); return; }
            if (!op.TryGetProperty("op", out JsonElement kindEl) || kindEl.ValueKind != JsonValueKind.String) { Bad(found, "missing-field", at + ".op", "an operation names what it does in \"op\""); return; }
            string kind = kindEl.GetString();
            int errors = ErrorCount(found);
            TriggerPlan t = null; SpawnPlan s = null; GoalPlan g = null;
            List<string> before = new List<string>();
            string said = "";
            switch (kind)
            {
                case "setTrigger":
                {
                    t = TriggerAt(plan, op, "id", at, found); if (t == null) return;
                    before = Words(t, null, null);
                    bool wasPlaced = TriggerPlan.Placed(t.When);
                    var keys = new List<string>();
                    if (!Each(op, "set", at, found, (key, v, path) => { keys.Add(key); TriggerField(plan, t, key, v, path, found); })) return;
                    if (!wasPlaced && TriggerPlan.Placed(t.When) && !keys.Contains("pos") && !keys.Contains("place"))
                        Bad(found, "missing-field", at + ".set.pos", "a " + t.When + " trigger needs \"pos\": [x, y, z], or a \"place\" to link");
                    else if (!wasPlaced && keys.Contains("place") && !keys.Contains("pos")) AnchorAtPlace(plan, t);
                    said = "trigger " + t.Id.ToString(Inv) + ": set " + string.Join(", ", keys);
                    break;
                }
                case "addTrigger":
                {
                    if (!op.TryGetProperty("trigger", out JsonElement body) || body.ValueKind != JsonValueKind.Object) { Bad(found, "missing-field", at + ".trigger", "addTrigger needs \"trigger\": {when, ...}"); return; }
                    t = new TriggerPlan { Name = "" };
                    int id = 0;
                    if (body.TryGetProperty("id", out JsonElement idEl)) { if (!Whole(idEl, at + ".trigger.id", found, 1, int.MaxValue, out id)) return; }
                    else foreach (TriggerPlan x in plan.Triggers) id = Math.Max(id, x.Id);
                    t.Id = body.TryGetProperty("id", out _) ? id : id + 1;
                    if (!body.TryGetProperty("when", out _)) { Bad(found, "missing-field", at + ".trigger.when", "a new trigger says what it waits for in \"when\""); return; }
                    var keys = new List<string>();
                    foreach (JsonProperty p in body.EnumerateObject())
                    {
                        if (p.Name == "id") continue;
                        keys.Add(p.Name);
                        TriggerField(plan, t, p.Name, p.Value, at + ".trigger." + p.Name, found);
                    }
                    // A placed trigger needs its spot; a linked one is anchored at the place's own spot when none is given.
                    bool placed = TriggerPlan.Placed(t.When);
                    if ((placed || t.When == TriggerPlan.OnDoor) && !keys.Contains("pos"))
                    {
                        if (placed && t.Place > 0) AnchorAtPlace(plan, t);
                        else Bad(found, "missing-field", at + ".trigger.pos", "a " + t.When + " trigger needs \"pos\": [x, y, z]" + (placed ? ", or a \"place\" to link" : ""));
                    }
                    plan.Triggers.Add(t);
                    said = "added trigger " + t.Id.ToString(Inv) + " (" + t.When + ")";
                    break;
                }
                case "removeTrigger":
                {
                    TriggerPlan gone = TriggerAt(plan, op, "id", at, found); if (gone == null) return;
                    plan.Triggers.Remove(gone);
                    // Whatever still names it is refused by the strict reader with its own path: nothing is unhooked quietly.
                    said = "removed trigger " + gone.Id.ToString(Inv);
                    break;
                }
                case "setHold":
                {
                    t = TriggerAt(plan, op, "trigger", at, found); if (t == null) return;
                    before = Words(t, null, null);
                    if (!TriggerPlan.Placed(t.When)) { Bad(found, "missing-field", at + ".trigger", "trigger " + t.Id.ToString(Inv) + " waits for " + t.When + " and has no place: link a place or give it a pos with setTrigger first"); return; }
                    if (!op.TryGetProperty("seconds", out JsonElement sec)) { Bad(found, "missing-field", at + ".seconds", "setHold needs \"seconds\": how long players hold the place"); return; }
                    if (!Number(sec, at + ".seconds", found, out float hold)) return;
                    if (hold <= 0f || hold > 3600f) { Bad(found, "out-of-range", at + ".seconds", "a hold lasts more than 0 and at most 3600 seconds"); return; }
                    t.When = TriggerPlan.OnHold; t.Hold = hold;
                    if (op.TryGetProperty("everyone", out JsonElement ev)) { if (!Flag(ev, at + ".everyone", found, out bool every)) return; t.Everyone = every; }
                    said = "trigger " + t.Id.ToString(Inv) + " is now a " + hold.ToString("0.##", Inv) + " s hold" + (t.Everyone ? " by every player" : "");
                    break;
                }
                case "linkPlace":
                case "copyPlace":
                {
                    t = TriggerAt(plan, op, "trigger", at, found); if (t == null) return;
                    before = Words(t, null, null);
                    if (!op.TryGetProperty("place", out JsonElement pe)) { Bad(found, "missing-field", at + ".place", kind + " needs \"place\": the place's id"); return; }
                    if (!Whole(pe, at + ".place", found, 1, int.MaxValue, out int pid)) return;
                    if (!TriggerPlan.Placed(t.When)) { Bad(found, "bad-area", at + ".trigger", "only zone and hold triggers link a place"); return; }
                    PlacePlan place = plan.Places.Find(p => p.Id == pid);
                    if (kind == "copyPlace")
                    {
                        if (place == null) { Bad(found, "missing-ref", at + ".place", "there is no place " + pid.ToString(Inv)); return; }
                        if (!place.Shaped) { Bad(found, "bad-area", at + ".place", "place " + pid.ToString(Inv) + " is a point: only a place with a painted area can be copied"); return; }
                        t.Place = 0; t.Area = place.Area.Copy();
                        said = "trigger " + t.Id.ToString(Inv) + " has its own copy of the shape of place " + pid.ToString(Inv) + " \"" + place.Name + "\"";
                    }
                    else
                    {
                        // A place that is not there, or is a point, is left for the strict reader to refuse at the trigger's path.
                        Link(plan, t, pid);
                        said = "trigger " + t.Id.ToString(Inv) + " is linked to place " + pid.ToString(Inv) + (place != null ? " \"" + place.Name + "\"" : "");
                    }
                    break;
                }
                case "addGoal":
                {
                    if (!op.TryGetProperty("goal", out JsonElement body) || body.ValueKind != JsonValueKind.Object) { Bad(found, "missing-field", at + ".goal", "addGoal needs \"goal\": {id, name, succeed, ...}"); return; }
                    if (!body.TryGetProperty("id", out JsonElement gid) || gid.ValueKind != JsonValueKind.String) { Bad(found, "missing-field", at + ".goal.id", "a new goal needs a text \"id\""); return; }
                    g = new GoalPlan { Id = gid.GetString() };
                    foreach (JsonProperty p in body.EnumerateObject())
                        if (p.Name != "id") GoalField(g, p.Name, p.Value, at + ".goal." + p.Name, found);
                    plan.Goals.Add(g);
                    said = "added goal \"" + g.Id + "\"";
                    break;
                }
                case "setGoal":
                {
                    g = GoalAt(plan, op, at, found); if (g == null) return;
                    before = Words(null, null, g);
                    var keys = new List<string>();
                    if (!Each(op, "set", at, found, (key, v, path) =>
                    {
                        keys.Add(key);
                        if (key == "id") Bad(found, "unknown-value", path, "a goal keeps its id: other goals and the board name it");
                        else GoalField(g, key, v, path, found);
                    })) return;
                    said = "goal \"" + g.Id + "\": set " + string.Join(", ", keys);
                    break;
                }
                case "removeGoal":
                {
                    GoalPlan gone = GoalAt(plan, op, at, found); if (gone == null) return;
                    plan.Goals.Remove(gone);
                    said = "removed goal \"" + gone.Id + "\"";
                    break;
                }
                case "setExtraction":
                {
                    var keys = new List<string>();
                    if (!Each(op, "set", at, found, (key, v, path) =>
                    {
                        keys.Add(key);
                        if (key == "after") { if (Whole(v, path, found, 0, int.MaxValue, out int a)) plan.ExtractionAfter = a; }
                        else if (key == "goals") { if (Flag(v, path, found, out bool w)) plan.ExtractionGoals = w; }
                        else Bad(found, "unknown-value", path, "\"" + key + "\" is not an extraction setting an agent may set (after, goals)");
                    })) return;
                    said = "extraction: set " + string.Join(", ", keys);
                    break;
                }
                case "addEnemy":
                {
                    if (!op.TryGetProperty("enemy", out JsonElement body) || body.ValueKind != JsonValueKind.Object) { Bad(found, "missing-field", at + ".enemy", "addEnemy needs \"enemy\": {pos, ...}"); return; }
                    if (!body.TryGetProperty("pos", out _)) { Bad(found, "missing-field", at + ".enemy.pos", "an enemy needs \"pos\": [x, y, z]"); return; }
                    s = new SpawnPlan();
                    foreach (JsonProperty p in body.EnumerateObject()) SpawnField(s, p.Name, p.Value, at + ".enemy." + p.Name, found);
                    plan.Spawns.Add(s);
                    said = "added enemy " + plan.Spawns.Count.ToString(Inv) + (s.Trigger > 0 ? ", arriving with trigger " + s.Trigger.ToString(Inv) : ", there from the start");
                    break;
                }
                case "setEnemy":
                {
                    // An index means something only in the text it was read from.
                    if (ask.ExpectedHash == "") { Bad(found, "missing-field", "expectedHash", "an enemy is named by its index, which holds only for the text you read: send that text's expectedHash"); return; }
                    if (!op.TryGetProperty("index", out JsonElement ie)) { Bad(found, "missing-field", at + ".index", "setEnemy needs \"index\": the enemy's place in the list, from 0"); return; }
                    if (!Whole(ie, at + ".index", found, 0, int.MaxValue, out int index)) return;
                    if (index >= plan.Spawns.Count) { Bad(found, "missing-ref", at + ".index", "there is no enemy " + index.ToString(Inv) + "; the list has " + plan.Spawns.Count.ToString(Inv)); return; }
                    s = plan.Spawns[index];
                    before = Words(null, s, null);
                    var keys = new List<string>();
                    if (!Each(op, "set", at, found, (key, v, path) => { keys.Add(key); SpawnField(s, key, v, path, found); })) return;
                    said = "enemy " + index.ToString(Inv) + ": set " + string.Join(", ", keys);
                    break;
                }
                default:
                    Bad(found, "unknown-value", at + ".op", "\"" + kind + "\" is not an operation: setTrigger, addTrigger, removeTrigger, setHold, linkPlace, copyPlace, addGoal, setGoal, removeGoal, setExtraction, addEnemy, setEnemy");
                    return;
            }
            if (t != null && t.Place > 0 && !TriggerPlan.Placed(t.When))
                Bad(found, "bad-area", at, "trigger " + t.Id.ToString(Inv) + " links place " + t.Place.ToString(Inv) + " but waits for " + t.When + ": only zone and hold triggers link a place");
            if (ErrorCount(found) > errors) return;

            // A feature the capability list records as failing or unavailable may not be added by an agent; the author can.
            List<string> after = Words(t, s, g);
            foreach (string word in after)
            {
                if (before.Contains(word)) continue;
                CapabilityState st = MissionLab.Core.Capabilities.Judge(registry, word, current);
                if (st.Shown == MissionLab.Core.Capabilities.ShownFailing || st.Shown == MissionLab.Core.Capabilities.ShownUnavailable)
                    Bad(found, "capability-" + st.Shown, at, "uses " + word + ", which the capability list records as " + st.Status + " (" + st.Id + "): " + st.Words + ". A proposal may not add it; the author can, as an experiment");
                else if (st.Shown == MissionLab.Core.Capabilities.ShownExperimental)
                    found.Add(new Finding { Level = Finding.Note, Code = "capability-experimental", Path = at, Text = "uses " + word + ", which is " + st.Words });
            }
            if (ErrorCount(found) == errors) changes.Add(said);
        }

        private static int ErrorCount(List<Finding> found)
        {
            int n = 0;
            foreach (Finding f in found) if (f.Level == Finding.Error) n++;
            return n;
        }

        // The capability words of the objects an operation touches, as Capabilities.Of names them.
        private static List<string> Words(TriggerPlan t, SpawnPlan s, GoalPlan g)
        {
            var mini = new MissionPlan();
            if (t != null) mini.Triggers.Add(t);
            if (s != null) mini.Spawns.Add(s);
            if (g != null) mini.Goals.Add(g);
            return MissionLab.Core.Capabilities.Of(mini);
        }

        // A link as the readers resolve it: the trigger shares the place's one shape. A place that is missing or a point
        // leaves no shape, and the strict reader refuses the link at the trigger's own path.
        private static void Link(MissionPlan plan, TriggerPlan t, int pid)
        {
            if (pid == 0)
            {
                if (t.Place > 0 && t.Area != null) t.Area = t.Area.Copy();      // as Flatten: the trigger keeps the shape it had
                t.Place = 0;
                return;
            }
            PlacePlan place = plan.Places.Find(p => p.Id == pid);
            t.Place = pid;
            t.Area = place != null && place.Shaped ? place.Area : null;
        }

        private static void AnchorAtPlace(MissionPlan plan, TriggerPlan t)
        {
            PlacePlan place = plan.Places.Find(p => p.Id == t.Place);
            if (place != null) t.Pos = place.Pos;
        }

        private static TriggerPlan TriggerAt(MissionPlan plan, JsonElement op, string key, string at, List<Finding> found)
        {
            if (!op.TryGetProperty(key, out JsonElement e)) { Bad(found, "missing-field", at + "." + key, "name the trigger by its id in \"" + key + "\""); return null; }
            if (!Whole(e, at + "." + key, found, 1, int.MaxValue, out int id)) return null;
            TriggerPlan t = plan.Triggers.Find(x => x.Id == id);
            if (t == null) Bad(found, "missing-ref", at + "." + key, "there is no trigger " + id.ToString(Inv));
            return t;
        }

        private static GoalPlan GoalAt(MissionPlan plan, JsonElement op, string at, List<Finding> found)
        {
            if (!op.TryGetProperty("id", out JsonElement e) || e.ValueKind != JsonValueKind.String) { Bad(found, "missing-field", at + ".id", "name the goal by its text id in \"id\""); return null; }
            string id = e.GetString();
            GoalPlan g = plan.Goals.Find(x => x.Id == id);
            if (g == null) Bad(found, "missing-ref", at + ".id", "there is no goal \"" + id + "\"");
            return g;
        }

        private static bool Each(JsonElement op, string key, string at, List<Finding> found, Action<string, JsonElement, string> set)
        {
            if (!op.TryGetProperty(key, out JsonElement body)) { Bad(found, "missing-field", at + "." + key, "the operation lists what it changes in \"" + key + "\": {field: value}"); return false; }
            if (body.ValueKind != JsonValueKind.Object) { Bad(found, "wrong-type", at + "." + key, "\"" + key + "\" is an object: {field: value}"); return false; }
            foreach (JsonProperty p in body.EnumerateObject()) set(p.Name, p.Value, at + "." + key + "." + p.Name);
            return true;
        }

        private static void TriggerField(MissionPlan plan, TriggerPlan t, string key, JsonElement v, string path, List<Finding> found)
        {
            switch (key)
            {
                case "name": { if (Str(v, path, found, out string x)) t.Name = x; return; }
                case "when":
                {
                    if (!Str(v, path, found, out string x)) return;
                    if (!TriggerPlan.Known(x)) { Bad(found, "unknown-kind", path, "\"" + x + "\" is not a kind of trigger this version knows"); return; }
                    t.When = x; return;
                }
                case "pos": { if (Spot(v, path, found, out V3 p)) t.Pos = p; return; }
                case "radius": { if (Number(v, path, found, out float x)) { if (x <= 0f || x > 500f) Bad(found, "out-of-range", path, "a radius is more than 0 and at most 500 metres"); else t.Radius = x; } return; }
                case "delay": { if (Number(v, path, found, out float x)) { if (x < 0f) Bad(found, "out-of-range", path, "a delay is 0 or more seconds"); else t.Delay = x; } return; }
                case "hold": { if (Number(v, path, found, out float x)) { if (x <= 0f || x > 3600f) Bad(found, "out-of-range", path, "a hold lasts more than 0 and at most 3600 seconds"); else t.Hold = x; } return; }
                case "after": { if (Whole(v, path, found, 0, int.MaxValue, out int x)) t.After = x; return; }
                case "of": { if (Wholes(v, path, found, out List<int> x)) t.Of = x; return; }
                case "count": { if (Whole(v, path, found, 0, int.MaxValue, out int x)) t.Count = x; return; }
                case "tag": { if (Str(v, path, found, out string x)) t.Tag = SpawnPlan.CleanTag(x); return; }
                case "item": { if (Str(v, path, found, out string x)) t.Item = x; return; }
                case "chance": { if (Whole(v, path, found, 1, 100, out int x)) t.Chance = x; return; }
                case "warn": { if (Flag(v, path, found, out bool x)) t.Warn = x; return; }
                case "everyone": { if (Flag(v, path, found, out bool x)) t.Everyone = x; return; }
                case "red": { if (Flag(v, path, found, out bool x)) t.Red = x; return; }
                case "hunt": { if (Flag(v, path, found, out bool x)) t.Hunt = x; return; }
                case "respawn": { if (Flag(v, path, found, out bool x)) t.Respawn = x; return; }
                case "facePlayer": { if (Flag(v, path, found, out bool x)) t.FacePlayer = x; return; }
                case "repeat": { if (Number(v, path, found, out float x)) { if (x < 0f) Bad(found, "out-of-range", path, "\"repeat\" is 0 or more seconds"); else t.Repeat = x; } return; }
                case "times": { if (Whole(v, path, found, 0, int.MaxValue, out int x)) t.Times = x; return; }
                case "rally": { if (Number(v, path, found, out float x)) { if (x < 0f) Bad(found, "out-of-range", path, "\"rally\" is 0 or more metres"); else t.Rally = x; } return; }
                case "message":
                {
                    if (!Str(v, path, found, out string x)) return;
                    if (x.Length > TriggerPlan.MessageLength) { Bad(found, "out-of-range", path, "a message is at most " + TriggerPlan.MessageLength + " characters"); return; }
                    t.Message = x; return;
                }
                case "power": { if (Choice(v, path, found, new[] { "", "on", "off" }, out string x)) t.Power = x; return; }
                case "end": { if (Choice(v, path, found, new[] { "", "success", "failed" }, out string x)) t.End = x; return; }
                case "extraction": { if (Choice(v, path, found, new[] { "", "open" }, out string x)) t.OpensExtraction = x == "open"; return; }
                case "outcome": { if (Choice(v, path, found, TriggerPlan.Outcomes, out string x)) t.Outcome = x; return; }
                case "within":
                {
                    if (!Number(v, path, found, out float x)) return;
                    if (x < TriggerPlan.WithinLeast || x > TriggerPlan.WithinMost) { Bad(found, "out-of-range", path, "\"within\" is from " + TriggerPlan.WithinLeast + " to " + TriggerPlan.WithinMost + " seconds"); return; }
                    t.Within = x; return;
                }
                case "place": { if (Whole(v, path, found, 0, int.MaxValue, out int x)) Link(plan, t, x); return; }
                default:
                    Bad(found, "unknown-value", path, "\"" + key + "\" is not a trigger field an agent may set");
                    return;
            }
        }

        private static void SpawnField(SpawnPlan s, string key, JsonElement v, string path, List<Finding> found)
        {
            switch (key)
            {
                case "pos": { if (Spot(v, path, found, out V3 p)) s.Pos = p; return; }
                case "yaw": { if (Number(v, path, found, out float x)) s.Yaw = x; return; }
                case "role":
                {
                    if (!Str(v, path, found, out string x)) return;
                    if (x == "" || !Enum.TryParse(x, true, out SpawnRole role) || !Enum.IsDefined(typeof(SpawnRole), role)) { Bad(found, "unknown-value", path, "\"" + x + "\" is not a role: Guard, Wander or Patrol"); return; }
                    s.Role = role; return;
                }
                case "group": { if (Whole(v, path, found, 0, 1000, out int x)) s.Group = x; return; }
                case "trigger": { if (Whole(v, path, found, 0, int.MaxValue, out int x)) s.Trigger = x; return; }
                case "tag": { if (Str(v, path, found, out string x)) s.Tag = SpawnPlan.CleanTag(x); return; }
                case "type": { if (Str(v, path, found, out string x)) s.Type = x; return; }
                case "chance": { if (Whole(v, path, found, 1, 100, out int x)) s.Chance = x; return; }
                case "skill": { if (Choice(v, path, found, new[] { "", "recruit", "veteran" }, out string x)) s.Skill = x; return; }
                case "team": { if (Whole(v, path, found, 0, 16, out int x)) s.Team = x; return; }
                case "minPlayers": { if (Whole(v, path, found, 0, MissionPlan.SquadLimit, out int x)) s.MinPlayers = x; return; }
                case "scatter":
                {
                    if (!Number(v, path, found, out float x)) return;
                    if (x < 0f || x > SpawnPlan.ScatterLimit) { Bad(found, "out-of-range", path, "\"scatter\" is 0 (none) to " + SpawnPlan.ScatterLimit.ToString(Inv) + " metres"); return; }
                    s.Scatter = x; return;
                }
                case "nvg": { if (Flag(v, path, found, out bool x)) s.Nvg = x; return; }
                case "indoor": { if (Flag(v, path, found, out bool x)) s.Indoor = x; return; }
                case "defensive": { if (Flag(v, path, found, out bool x)) s.Defensive = x; return; }
                case "prone": { if (Flag(v, path, found, out bool x)) s.Prone = x; return; }
                case "fixed": { if (Flag(v, path, found, out bool x)) s.Fixed = x; return; }
                case "hvt": { if (Flag(v, path, found, out bool x)) s.Hvt = x; return; }
                case "vehicle": { if (Flag(v, path, found, out bool x)) s.Vehicle = x; return; }
                case "patrol":
                {
                    if (v.ValueKind != JsonValueKind.Array) { Bad(found, "wrong-type", path, "\"patrol\" is a list of [x, y, z] stops"); return; }
                    var stops = new List<V3>(); int i = 0;
                    foreach (JsonElement e in v.EnumerateArray()) { if (!Spot(e, path + "[" + (i++).ToString(Inv) + "]", found, out V3 p)) return; stops.Add(p); }
                    s.Patrol = stops; return;
                }
                default:
                    Bad(found, "unknown-value", path, "\"" + key + "\" is not an enemy field an agent may set");
                    return;
            }
        }

        private static void GoalField(GoalPlan g, string key, JsonElement v, string path, List<Finding> found)
        {
            switch (key)
            {
                case "name": { if (Str(v, path, found, out string x)) g.Name = x; return; }
                case "required": { if (Flag(v, path, found, out bool x)) g.Required = x; return; }
                case "after":
                {
                    if (v.ValueKind != JsonValueKind.Array) { Bad(found, "wrong-type", path, "\"after\" is a list of goal ids"); return; }
                    var list = new List<string>(); int i = 0;
                    foreach (JsonElement e in v.EnumerateArray())
                    {
                        if (e.ValueKind != JsonValueKind.String) { Bad(found, "wrong-type", path + "[" + i.ToString(Inv) + "]", "a goal id is text"); return; }
                        list.Add(e.GetString()); i++;
                    }
                    g.After = list; return;
                }
                case "activate": { if (Whole(v, path, found, 0, int.MaxValue, out int x)) g.Activate = x; return; }
                case "succeed": { if (Whole(v, path, found, 0, int.MaxValue, out int x)) g.Succeed = x; return; }
                case "fail": { if (Whole(v, path, found, 0, int.MaxValue, out int x)) g.Fail = x; return; }
                case "notify": { if (Flag(v, path, found, out bool x)) g.Notify = x; return; }
                default:
                    Bad(found, "unknown-value", path, "\"" + key + "\" is not a goal field an agent may set");
                    return;
            }
        }

        // ----- values, refused with the strict reader's codes

        private static bool Str(JsonElement v, string path, List<Finding> found, out string x)
        {
            x = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (x == null) Bad(found, "wrong-type", path, "this is text");
            return x != null;
        }

        private static bool Choice(JsonElement v, string path, List<Finding> found, string[] allowed, out string x)
        {
            if (!Str(v, path, found, out x)) return false;
            if (Array.IndexOf(allowed, x) >= 0) return true;
            Bad(found, "unknown-value", path, "\"" + x + "\" is not one of: " + string.Join(", ", allowed));
            return false;
        }

        private static bool Flag(JsonElement v, string path, List<Finding> found, out bool x)
        {
            x = v.ValueKind == JsonValueKind.True;
            if (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) return true;
            Bad(found, "wrong-type", path, "this is true or false");
            return false;
        }

        private static bool Number(JsonElement v, string path, List<Finding> found, out float x)
        {
            x = 0f;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d) || double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e6) { Bad(found, "wrong-type", path, "this is a number"); return false; }
            x = (float)d;
            return true;
        }

        private static bool Whole(JsonElement v, string path, List<Finding> found, int least, int most, out int x)
        {
            x = 0;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out x)) { Bad(found, "wrong-type", path, "this is a whole number"); return false; }
            if (x < least || x > most) { Bad(found, "out-of-range", path, "this is from " + least.ToString(Inv) + (most == int.MaxValue ? " up" : " to " + most.ToString(Inv))); return false; }
            return true;
        }

        private static bool Wholes(JsonElement v, string path, List<Finding> found, out List<int> x)
        {
            x = new List<int>();
            if (v.ValueKind != JsonValueKind.Array) { Bad(found, "wrong-type", path, "this is a list of trigger numbers"); return false; }
            int i = 0;
            foreach (JsonElement e in v.EnumerateArray()) { if (!Whole(e, path + "[" + (i++).ToString(Inv) + "]", found, 1, int.MaxValue, out int n)) return false; x.Add(n); }
            return true;
        }

        private static bool Spot(JsonElement v, string path, List<Finding> found, out V3 p)
        {
            p = default;
            if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3) { Bad(found, "wrong-type", path, "a position is [x, y, z]"); return false; }
            var c = new float[3];
            for (int i = 0; i < 3; i++)
            {
                JsonElement e = v[i];
                if (e.ValueKind != JsonValueKind.Number || !e.TryGetDouble(out double d) || double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e5) { Bad(found, "wrong-type", path, "a position is [x, y, z] in metres"); return false; }
                c[i] = (float)d;
            }
            p = new V3(c[0], c[1], c[2]);
            return true;
        }

        private static string Text(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        // ----- what is returned: one scenario and its metadata, never anything else from the folder or the game

        private static string Q(string s) => "\"" + Guard.Esc(s ?? "") + "\"";
        private static string F(float f) => Math.Round(f, 2).ToString(Inv);
        private static string P(V3 v) => "[" + F(v.X) + "," + F(v.Y) + "," + F(v.Z) + "]";

        private static void TextList(StringBuilder sb, List<string> words)
        {
            sb.Append('[');
            for (int i = 0; i < words.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(words[i]));
            sb.Append(']');
        }

        private static void States(StringBuilder sb, List<CapabilityState> states)
        {
            sb.Append('[');
            for (int i = 0; i < states.Count; i++)
            {
                CapabilityState c = states[i];
                sb.Append(i > 0 ? "," : "").Append("{\"name\":").Append(Q(c.Name)).Append(",\"id\":").Append(Q(c.Id)).Append(",\"status\":").Append(Q(c.Status))
                  .Append(",\"shown\":").Append(Q(c.Shown)).Append(",\"words\":").Append(Q(c.Words)).Append('}');
            }
            sb.Append(']');
        }

        /// <summary>One row of the agent's scenario list: identity, fingerprint and capabilities as the catalogue judged them.</summary>
        public static void ListEntry(StringBuilder sb, CatalogueEntry e)
        {
            sb.Append("{\"file\":").Append(Q(e.File)).Append(",\"id\":").Append(Q(e.Id)).Append(",\"revision\":").Append(e.Revision.ToString(Inv))
              .Append(",\"hash\":").Append(Q(e.Hash)).Append(",\"title\":").Append(Q(e.Title)).Append(",\"readable\":").Append(e.Readable ? "true" : "false")
              .Append(",\"usable\":").Append(e.Usable ? "true" : "false").Append(",\"why\":").Append(Q(e.Why)).Append(",\"sealed\":").Append(e.Sealed ? "true" : "false")
              .Append(",\"capabilities\":");
            States(sb, e.States.Count == e.Capabilities.Count ? e.States : MissionLab.Core.Capabilities.Judge(null, e.Capabilities, ""));
            sb.Append('}');
        }

        /// <summary>
        /// One scenario for an agent: its text and fingerprint, and what operations name, with their stable ids: named places,
        /// triggers, goals and the enemies by index. Only this scenario: no other file, no map, recon or run record.
        /// </summary>
        public static string ScenarioJson(string file, string text, CapabilityRegistry registry, string current)
        {
            CatalogueEntry e = CatalogueInfo.Build(file, text ?? "", null);
            CatalogueInfo.Check(e, registry, current, null);
            MissionPlan p = e.Plan;
            var sb = new StringBuilder(8192 + (text ?? "").Length);
            sb.Append("{\"file\":").Append(Q(file)).Append(",\"id\":").Append(Q(e.Id)).Append(",\"revision\":").Append(e.Revision.ToString(Inv))
              .Append(",\"hash\":").Append(Q(e.Hash)).Append(",\"title\":").Append(Q(e.Title)).Append(",\"readable\":").Append(e.Readable ? "true" : "false")
              .Append(",\"sealed\":").Append(e.Sealed ? "true" : "false").Append(",\"findings\":");
            CatalogueInfo.WriteFindings(sb, e.Findings);
            sb.Append(",\"capabilities\":");
            States(sb, e.States);
            sb.Append(",\"needs\":");
            CatalogueInfo.WriteFindings(sb, e.Needs);
            sb.Append(",\"places\":[");
            for (int i = 0; p != null && i < p.Places.Count; i++)
            {
                PlacePlan pl = p.Places[i];
                sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(pl.Id.ToString(Inv)).Append(",\"name\":").Append(Q(pl.Name)).Append(",\"pos\":").Append(P(pl.Pos))
                  .Append(",\"shaped\":").Append(pl.Shaped ? "true" : "false").Append('}');
            }
            sb.Append("],\"triggers\":[");
            for (int i = 0; p != null && i < p.Triggers.Count; i++)
            {
                TriggerPlan t = p.Triggers[i];
                sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(t.Id.ToString(Inv)).Append(",\"name\":").Append(Q(t.Name)).Append(",\"when\":").Append(Q(t.When));
                if (TriggerPlan.Placed(t.When) || t.When == TriggerPlan.OnDoor) sb.Append(",\"pos\":").Append(P(t.Pos));
                if (t.Linked) sb.Append(",\"place\":").Append(t.Place.ToString(Inv));
                else if (t.Shaped) sb.Append(",\"shaped\":true");
                if (t.When == TriggerPlan.OnAfter || t.When == TriggerPlan.OnOutcome || t.Gated) sb.Append(",\"after\":").Append(t.After.ToString(Inv));
                if (t.When == TriggerPlan.OnAll || t.When == TriggerPlan.OnAny) sb.Append(",\"of\":[").Append(string.Join(",", t.Of)).Append(']');
                if (t.When == TriggerPlan.OnHold) sb.Append(",\"hold\":").Append(F(t.Hold));
                if (t.When == TriggerPlan.OnIntel) sb.Append(",\"item\":").Append(Q(t.Item));
                if (t.Delay > 0f) sb.Append(",\"delay\":").Append(F(t.Delay));
                if (t.Everyone) sb.Append(",\"everyone\":true");
                sb.Append(",\"enemies\":").Append(MissionLab.Core.Completion.Enemies(p, t.Id).ToString(Inv)).Append('}');
            }
            sb.Append("],\"goals\":[");
            for (int i = 0; p != null && i < p.Goals.Count; i++)
            {
                GoalPlan g = p.Goals[i];
                sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(Q(g.Id)).Append(",\"name\":").Append(Q(g.Name)).Append(",\"required\":").Append(g.Required ? "true" : "false")
                  .Append(",\"after\":");
                TextList(sb, g.After);
                sb.Append(",\"activate\":").Append(g.Activate.ToString(Inv)).Append(",\"succeed\":").Append(g.Succeed.ToString(Inv)).Append(",\"fail\":").Append(g.Fail.ToString(Inv)).Append('}');
            }
            sb.Append("],\"enemies\":[");
            for (int i = 0; p != null && i < p.Spawns.Count; i++)
            {
                SpawnPlan s = p.Spawns[i];
                sb.Append(i > 0 ? "," : "").Append("{\"index\":").Append(i.ToString(Inv)).Append(",\"pos\":").Append(P(s.Pos)).Append(",\"role\":").Append(Q(s.Role.ToString()))
                  .Append(",\"trigger\":").Append(s.Trigger.ToString(Inv)).Append(",\"tag\":").Append(Q(s.Tag)).Append(",\"type\":").Append(Q(s.Type)).Append('}');
            }
            sb.Append("],\"extraction\":{\"after\":").Append(p != null ? p.ExtractionAfter.ToString(Inv) : "0").Append(",\"goals\":").Append(p != null && p.ExtractionGoals ? "true" : "false").Append('}');
            sb.Append(",\"text\":").Append(Q(text)).Append('}');
            return sb.ToString();
        }

        /// <summary>The capability list as an agent reads it: each row's status for the running build and the words it covers.</summary>
        public static string CapabilitiesJson(CapabilityRegistry registry, string current)
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"schema\":").Append(Q(CapabilityRegistry.Schema)).Append(",\"current\":").Append(Q(current)).Append(",\"problems\":");
            TextList(sb, registry != null ? registry.Problems : new List<string>());
            sb.Append(",\"capabilities\":[");
            for (int i = 0; registry != null && i < registry.Records.Count; i++)
            {
                CapabilityRecord r = registry.Records[i];
                sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(Q(r.Id)).Append(",\"label\":").Append(Q(r.Label)).Append(",\"status\":").Append(Q(r.Status))
                  .Append(",\"qualifier\":").Append(Q(r.Qualifier)).Append(",\"where\":").Append(Q(r.Where)).Append(",\"summary\":").Append(Q(r.Summary)).Append(",\"uses\":");
                TextList(sb, r.Uses);
                sb.Append(",\"shown\":[");
                for (int j = 0; j < r.Uses.Count; j++)
                {
                    CapabilityState st = MissionLab.Core.Capabilities.Judge(registry, r.Uses[j], current);
                    sb.Append(j > 0 ? "," : "").Append("{\"name\":").Append(Q(st.Name)).Append(",\"shown\":").Append(Q(st.Shown)).Append('}');
                }
                sb.Append("]}");
            }
            sb.Append("],\"rule\":\"an operation may not add a feature whose row is failing or unavailable; experimental ones are allowed and noted\"}");
            return sb.ToString();
        }

        /// <summary>The answer to a proposal. id and kept are "" when nothing was kept.</summary>
        public static string AnswerJson(DraftResult r, string file, string id, string kept)
        {
            var sb = new StringBuilder(4096 + r.Text.Length);
            sb.Append("{\"accepted\":").Append(r.Accepted ? "true" : "false").Append(",\"status\":").Append(r.Status.ToString(Inv)).Append(",\"why\":").Append(Q(r.Why))
              .Append(",\"file\":").Append(Q(file)).Append(",\"id\":").Append(Q(id)).Append(",\"kept\":").Append(Q(kept))
              .Append(",\"base\":{\"hash\":").Append(Q(r.BaseHash)).Append(",\"revision\":").Append(r.BaseRevision.ToString(Inv)).Append(",\"scenarioId\":").Append(Q(r.ScenarioId)).Append('}')
              .Append(",\"hash\":").Append(Q(r.Hash)).Append(",\"changes\":");
            TextList(sb, r.Changes);
            sb.Append(",\"capabilities\":");
            TextList(sb, r.Capabilities);
            sb.Append(",\"findings\":");
            CatalogueInfo.WriteFindings(sb, r.Findings);
            sb.Append(",\"validation\":").Append(r.Report != null ? MissionValidation.Json(r.Report) : "null");
            sb.Append(",\"scenario\":").Append(Q(r.Text)).Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// An accepted proposal as it is kept in BepInEx/MissionLab/proposals/&lt;id&gt;.json: the exact text it was made against
        /// and the exact proposed text, so the editor shows the difference and can tell when the scenario has moved on.
        /// </summary>
        public static string ProposalJson(DraftResult r, DraftAsk ask, string id, string made, string baseText)
        {
            var sb = new StringBuilder(8192 + r.Text.Length + (baseText ?? "").Length);
            sb.Append("{\"schema\":").Append(Q(ProposalSchema)).Append(",\"id\":").Append(Q(id)).Append(",\"name\":").Append(Q(ask.Name != "" ? ask.Name : "Proposal"))
              .Append(",\"file\":").Append(Q(ask.File)).Append(",\"scenarioId\":").Append(Q(r.ScenarioId)).Append(",\"made\":").Append(Q(made))
              .Append(",\"base\":{\"hash\":").Append(Q(r.BaseHash)).Append(",\"revision\":").Append(r.BaseRevision.ToString(Inv)).Append(",\"text\":").Append(Q(baseText)).Append('}')
              .Append(",\"operations\":").Append(string.IsNullOrWhiteSpace(ask.Operations) ? "[]" : ask.Operations.Trim()).Append(",\"changes\":");
            TextList(sb, r.Changes);
            sb.Append(",\"capabilities\":");
            TextList(sb, r.Capabilities);
            sb.Append(",\"findings\":");
            CatalogueInfo.WriteFindings(sb, r.Findings);
            sb.Append(",\"hash\":").Append(Q(r.Hash)).Append(",\"scenario\":").Append(Q(r.Text)).Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// One row of the proposal list from a kept record, judged against the scenario file as it is now (currentHash ""
        /// when the file is gone). False, writing nothing, when the record is not a proposal.
        /// </summary>
        public static bool Summary(StringBuilder sb, string record, string currentHash, int currentRevision)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(record ?? "", Lenient))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object || Text(r, "schema") != ProposalSchema) return false;
                    string baseHash = "", id = Text(r, "id") ?? ""; int baseRevision = 0;
                    if (!GoodProposalId(id)) return false;
                    if (r.TryGetProperty("base", out JsonElement b) && b.ValueKind == JsonValueKind.Object)
                    {
                        baseHash = Text(b, "hash") ?? "";
                        if (b.TryGetProperty("revision", out JsonElement rv) && rv.ValueKind == JsonValueKind.Number && rv.TryGetInt32(out int n)) baseRevision = n;
                    }
                    bool stale = IsStale(baseHash, currentHash);
                    string why = !stale ? "" : string.IsNullOrEmpty(currentHash) ? "the scenario file is not there any more"
                        : "the scenario has changed since this was proposed (made for revision " + baseRevision.ToString(Inv) + ", the file is now revision " + currentRevision.ToString(Inv) + ")";
                    sb.Append("{\"id\":").Append(Q(id)).Append(",\"name\":").Append(Q(Text(r, "name"))).Append(",\"file\":").Append(Q(Text(r, "file")))
                      .Append(",\"scenarioId\":").Append(Q(Text(r, "scenarioId"))).Append(",\"made\":").Append(Q(Text(r, "made")))
                      .Append(",\"base\":{\"hash\":").Append(Q(baseHash)).Append(",\"revision\":").Append(baseRevision.ToString(Inv)).Append('}')
                      .Append(",\"hash\":").Append(Q(Text(r, "hash"))).Append(",\"changes\":");
                    var changes = new List<string>();
                    if (r.TryGetProperty("changes", out JsonElement c) && c.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in c.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) changes.Add(e.GetString());
                    TextList(sb, changes);
                    sb.Append(",\"stale\":").Append(stale ? "true" : "false").Append(",\"staleWhy\":").Append(Q(why)).Append('}');
                    return true;
                }
            }
            catch (JsonException) { return false; }
        }

        /// <summary>A kept record with whether it is stale now, as GET /api/agent/proposal/&lt;id&gt; answers.</summary>
        public static string Wrapped(string record, string currentHash, int currentRevision)
        {
            var row = new StringBuilder(512);
            if (!Summary(row, record, currentHash, currentRevision)) return null;
            return "{\"summary\":" + row + ",\"proposal\":" + record.Trim() + "}";
        }

        /// <summary>A text field of a kept proposal ("file", "scenario", ...); "" when the record cannot be read or lacks it.</summary>
        public static string RecordField(string record, string key)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(record ?? "", Lenient))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object || Text(r, "schema") != ProposalSchema) return "";
                    return Text(r, key) ?? "";
                }
            }
            catch (JsonException) { return ""; }
        }
    }
}
