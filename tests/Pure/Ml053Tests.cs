using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using MissionLab.Core;

/// <summary>
/// Draft authoring for coding agents (ML-053): operations are applied to a copy and never to the text they came from; a
/// stale hash or revision is refused before anything is applied; a dangling reference is refused with the strict reader's own
/// code and path; an operation may not add a feature the capability list records as failing; linking a place to a new hold
/// and adding an objective gives a text the strict reader and Completion accept; and what is returned is that scenario only.
/// The HTTP routes, the proposals folder and the editor's review are the plugin's (AgentApi.cs) and the page's.
/// </summary>
static class Ml053Tests
{
    private const string Upstairs = "{\"parts\":[{\"floor\":[3,6],\"outline\":[[-10,-10],[10,-10],[10,10],[-10,10]]}]}";
    private const string Current = "0.26.2";

    // A house: the phone is intel (1), the front door a zone (2) that brings the second enemy, an alarm (3). Upstairs is a
    // painted place, the porch only a point. Recovering the phone is an objective.
    private static string House(string trigger2Extra = "") =>
        "{\"schema\":\"missionlab-plan/3\",\"mission\":{\"name\":\"House\",\"id\":\"house-1\",\"revision\":4," +
        "\"places\":[{\"id\":1,\"name\":\"Upstairs\",\"pos\":[0,4,0],\"area\":" + Upstairs + "},{\"id\":2,\"name\":\"Porch\",\"pos\":[20,0,0]}]," +
        "\"triggers\":[{\"id\":1,\"name\":\"Phone\",\"when\":\"intel\",\"item\":\"Cell Phone\"}," +
        "{\"id\":2,\"name\":\"Front door\",\"when\":\"zone\",\"pos\":[20,0,0],\"radius\":4" + trigger2Extra + "}," +
        "{\"id\":3,\"name\":\"Alarm\",\"when\":\"alert\"}]," +
        "\"goals\":[{\"id\":\"phone\",\"name\":\"Recover the phone\",\"succeed\":1}]," +
        "\"spawns\":[{\"pos\":[5,4,5]},{\"pos\":[-5,0,-5],\"trigger\":2}]}}";

    private static string Codes(IEnumerable<Finding> f) => string.Join(" | ", f.Where(x => x.Level == Finding.Error).Select(x => x.Code + " " + x.Path));

    private static DraftResult Propose(string source, string ops, string hash = null, int revision = -1, bool useHash = true)
    {
        var ask = new DraftAsk { File = "house.json", Name = "test", Operations = ops, ExpectedRevision = revision, ExpectedHash = useHash ? (hash ?? ScenarioFile.Hash(source)) : "" };
        return AgentDraft.Propose(source, ask, CapabilityRegistry.Embedded(), Current, null, CancellationToken.None);
    }

    public static void Run(Action<string, bool, string> check)
    {
        string house = House();
        {   // The scenario the cases start from is sound, so every refusal below is the operation's doing.
            var found = ScenarioFile.Read(house, out MissionPlan plan);
            check("the house reads strictly without an error", plan != null, Codes(found));
            check("and the mission can be finished as it is", plan != null && !Completion.Blocks(Completion.Check(plan, null, 0)), Codes(Completion.Check(plan, null, 0)));
        }

        {   // Applying operations works on a copy: the text it came from is left exactly as it was, accepted or refused.
            string before = house, hash = ScenarioFile.Hash(house);
            DraftResult ok = Propose(house, "[{\"op\":\"setTrigger\",\"id\":2,\"set\":{\"name\":\"Back door\",\"radius\":6}}]");
            check("a rename is accepted", ok.Accepted && ok.Status == 200, ok.Why + " " + Codes(ok.Findings));
            check("the source text is unchanged after an accepted proposal", house == before && ScenarioFile.Hash(house) == hash, "");
            DraftResult no = Propose(house, "[{\"op\":\"setTrigger\",\"id\":2,\"set\":{\"name\":\"Back door\"}},{\"op\":\"removeTrigger\",\"id\":1}]");
            check("a proposal that breaks a reference is refused", !no.Accepted && no.Text == "", no.Why);
            check("the source text is unchanged after a refused one too", house == before && ScenarioFile.Hash(house) == hash, "");
            check("the result says which text it was made against", ok.BaseHash == hash && ok.BaseRevision == 4 && ok.ScenarioId == "house-1", ok.BaseHash);
            // Only what was asked changed: putting the two fields back gives the source's own canonical text.
            MissionPlan back = MissionPlan.FromJson(ok.Text);
            TriggerPlan t2 = back.Triggers.First(t => t.Id == 2);
            check("the proposed text has the new name and radius", t2.Name == "Back door" && Math.Abs(t2.Radius - 6f) < 0.01f, ok.Text);
            t2.Name = "Front door"; t2.Radius = 4f;
            check("and nothing else differs from the source", back.ToJson() == MissionPlan.FromJson(house).ToJson(), back.ToJson());
            check("the same operations on the same text give the same text", Propose(house, "[{\"op\":\"setTrigger\",\"id\":2,\"set\":{\"name\":\"Back door\",\"radius\":6}}]").Text == ok.Text, "");
            check("the proposal's fingerprint is that of its text", ok.Hash == ScenarioFile.Hash(ok.Text) && ok.Hash != hash, ok.Hash);
        }

        {   // Stale: refused before anything is applied, and named as such.
            const string rename = "[{\"op\":\"setTrigger\",\"id\":2,\"set\":{\"name\":\"Back door\"}}]";
            DraftResult old = Propose(house, rename, hash: "fnv1a64:0000000000000000");
            check("an old hash is refused as stale with 409", !old.Accepted && old.Status == 409 && Codes(old.Findings) == "stale expectedHash", old.Status + " " + Codes(old.Findings));
            DraftResult oldRev = Propose(house, rename, revision: 3, useHash: false);
            check("an old revision is refused as stale with 409", !oldRev.Accepted && oldRev.Status == 409 && Codes(oldRev.Findings) == "stale expectedRevision", Codes(oldRev.Findings));
            DraftResult both = Propose(house, rename, revision: 3);
            check("the right hash with an old revision is still stale", !both.Accepted && both.Status == 409, Codes(both.Findings));
            DraftResult none = Propose(house, rename, useHash: false);
            check("a proposal that names no text it was made for is refused", !none.Accepted && none.Status == 400 && Codes(none.Findings) == "missing-field expectedHash", Codes(none.Findings));
            check("the right revision alone is enough for a trigger id", Propose(house, rename, revision: 4, useHash: false).Accepted, "");
            DraftResult byIndex = Propose(house, "[{\"op\":\"setEnemy\",\"index\":0,\"set\":{\"tag\":\"lookout\"}}]", revision: 4, useHash: false);
            check("an enemy named by index needs the hash of the text the index came from", !byIndex.Accepted && Codes(byIndex.Findings) == "missing-field expectedHash", Codes(byIndex.Findings));
            DraftResult tagged = Propose(house, "[{\"op\":\"setEnemy\",\"index\":0,\"set\":{\"tag\":\"lookout\"}}]");
            check("with it, the enemy at that index is changed", tagged.Accepted && MissionPlan.FromJson(tagged.Text).Spawns[0].Tag == "lookout", Codes(tagged.Findings));
        }

        {   // Invalid references: the strict reader's code and path, as for a file written that way by hand.
            string Strict(Action<MissionPlan> edit)
            {
                MissionPlan p = MissionPlan.FromJson(house);
                edit(p);
                return Codes(ScenarioFile.Read(p.ToJson(), out _));
            }
            DraftResult after = Propose(house, "[{\"op\":\"setTrigger\",\"id\":3,\"set\":{\"when\":\"after\",\"after\":99}}]");
            string hand = Strict(p => { p.Triggers[2].When = "after"; p.Triggers[2].After = 99; });
            check("waiting for a trigger that is not there: missing-ref at the trigger, as the strict reader says", !after.Accepted && after.Status == 422 && Codes(after.Findings) == "missing-ref triggers[2].after" && Codes(after.Findings) == hand, Codes(after.Findings) + " / " + hand);
            DraftResult removed = Propose(house, "[{\"op\":\"removeTrigger\",\"id\":1}]");
            check("removing a trigger a goal completes on is refused at the goal", !removed.Accepted && Codes(removed.Findings) == "missing-ref goals[0].succeed" && Codes(removed.Findings) == Strict(p => p.Triggers.RemoveAt(0)), Codes(removed.Findings));
            DraftResult door = Propose(house, "[{\"op\":\"removeTrigger\",\"id\":2}]");
            check("removing the trigger an enemy arrives with is refused at the enemy", !door.Accepted && Codes(door.Findings) == "missing-ref spawns[1].trigger", Codes(door.Findings));
            DraftResult nowhere = Propose(house, "[{\"op\":\"linkPlace\",\"trigger\":2,\"place\":9}]");
            check("a link to a place that is not there: missing-ref at the trigger's place", !nowhere.Accepted && Codes(nowhere.Findings) == "missing-ref triggers[1].place", Codes(nowhere.Findings));
            DraftResult point = Propose(house, "[{\"op\":\"linkPlace\",\"trigger\":2,\"place\":2}]");
            check("a link to a place that is only a point: bad-area, as the strict reader says", !point.Accepted && Codes(point.Findings) == "bad-area triggers[1].place", Codes(point.Findings));
            DraftResult notThere = Propose(house, "[{\"op\":\"setTrigger\",\"id\":9,\"set\":{\"name\":\"x\"}}]");
            check("a trigger id that is not there is refused at the operation", !notThere.Accepted && Codes(notThere.Findings) == "missing-ref operations[0].id", Codes(notThere.Findings));
            DraftResult goal = Propose(house, "[{\"op\":\"addGoal\",\"goal\":{\"id\":\"upstairs\",\"name\":\"Hold upstairs\",\"after\":[\"keys\"],\"succeed\":2}}]");
            check("a goal waiting for a goal that is not there: missing-ref at its after", !goal.Accepted && Codes(goal.Findings) == "missing-ref goals[1].after[0]", Codes(goal.Findings));
            DraftResult twice = Propose(house, "[{\"op\":\"addTrigger\",\"trigger\":{\"id\":2,\"when\":\"alert\"}}]");
            check("a new trigger with a number already used: duplicate-id", !twice.Accepted && Codes(twice.Findings) == "duplicate-id triggers[3].id", Codes(twice.Findings));
            DraftResult kind = Propose(house, "[{\"op\":\"setTrigger\",\"id\":3,\"set\":{\"when\":\"flying\"}}]");
            check("an unknown kind of trigger: unknown-kind", !kind.Accepted && Codes(kind.Findings) == "unknown-kind operations[0].set.when", Codes(kind.Findings));
            DraftResult end = Propose(house, "[{\"op\":\"setTrigger\",\"id\":3,\"set\":{\"end\":\"maybe\"}}]");
            check("a value the format does not have is refused, not quietly dropped", !end.Accepted && Codes(end.Findings) == "unknown-value operations[0].set.end", Codes(end.Findings));
            DraftResult type = Propose(house, "[{\"op\":\"setTrigger\",\"id\":2,\"set\":{\"radius\":\"big\"}}]");
            check("a value of the wrong type: wrong-type", !type.Accepted && Codes(type.Findings) == "wrong-type operations[0].set.radius", Codes(type.Findings));
            DraftResult op = Propose(house, "[{\"op\":\"spawnWave\"}]");
            check("an operation that is not in the vocabulary: unknown-value", !op.Accepted && Codes(op.Findings) == "unknown-value operations[0].op", Codes(op.Findings));
            DraftResult unanchored = Propose(house, "[{\"op\":\"addTrigger\",\"trigger\":{\"when\":\"zone\"}}]");
            check("a new zone with neither a spot nor a place is refused, not put at 0, 0, 0", !unanchored.Accepted && Codes(unanchored.Findings) == "missing-field operations[0].trigger.pos", Codes(unanchored.Findings));
        }

        {   // Capabilities: an operation may not add a feature the list records as failing; one the scenario has stays allowed.
            DraftResult hunt = Propose(house, "[{\"op\":\"setTrigger\",\"id\":2,\"set\":{\"hunt\":true}}]");
            Finding f = hunt.Findings.FirstOrDefault(x => x.Level == Finding.Error);
            check("making a trigger's enemies hunt is refused as a failing capability", !hunt.Accepted && f != null && f.Code == "capability-failing" && f.Path == "operations[0]", Codes(hunt.Findings));
            check("the refusal names the registry row and its status", f != null && f.Text.Contains("fresh-movement") && f.Text.Contains("failing"), f != null ? f.Text : "");
            DraftResult rally = Propose(house, "[{\"op\":\"addTrigger\",\"trigger\":{\"when\":\"time\",\"delay\":60,\"rally\":30}}]");
            check("a new trigger that calls enemies is refused the same way", !rally.Accepted && Codes(rally.Findings) == "capability-failing operations[0]", Codes(rally.Findings));
            string hunting = House(",\"hunt\":true");
            check("a house whose door trigger already hunts reads", ScenarioFile.Read(hunting, out MissionPlan hp) != null && hp != null, "");
            DraftResult rename = Propose(hunting, "[{\"op\":\"setTrigger\",\"id\":2,\"set\":{\"name\":\"Back door\"}}]");
            check("renaming a trigger that already hunts is not adding the feature", rename.Accepted, Codes(rename.Findings));
            check("the scenario's own failing capability is still reported as a note", rename.Findings.Any(x => x.Level == Finding.Note && x.Code == "capability-failing"), string.Join(" | ", rename.Findings.Select(x => x.Code)));
        }

        {   // The demonstration: the upstairs becomes a 30-second hold by everyone, an objective once the phone is recovered.
            const string ops = "[{\"op\":\"addTrigger\",\"trigger\":{\"when\":\"hold\",\"name\":\"Hold upstairs\",\"place\":1,\"hold\":30,\"everyone\":true}}," +
                               "{\"op\":\"addGoal\",\"goal\":{\"id\":\"upstairs\",\"name\":\"Hold the upstairs for 30 seconds\",\"after\":[\"phone\"],\"succeed\":4}}]";
            DraftResult r = Propose(house, ops);
            check("linking a place to a new hold and adding a goal is accepted", r.Accepted, r.Why + " " + Codes(r.Findings));
            check("each operation is said in words", r.Changes.Count == 2 && r.Changes[0].Contains("trigger 4") && r.Changes[1].Contains("upstairs"), string.Join(" / ", r.Changes));
            var found = ScenarioFile.Read(r.Text, out MissionPlan p);
            check("its text passes the strict reader", p != null && !ScenarioFile.Blocks(found), Codes(found));
            check("and Completion", p != null && !Completion.Blocks(Completion.Check(p, null, 0)), p != null ? Codes(Completion.Check(p, null, 0)) : "");
            TriggerPlan hold = p?.Triggers.FirstOrDefault(t => t.Id == 4);
            check("the new trigger is a 30 s hold by everyone, linked to Upstairs", hold != null && hold.When == "hold" && Math.Abs(hold.Hold - 30f) < 0.01f && hold.Everyone && hold.Linked && hold.Place == 1, r.Text);
            check("it uses the place's one shape and sits at the place's spot", hold != null && ReferenceEquals(hold.Area, p.Places[0].Area) && Math.Abs(hold.Pos.Y - 4f) < 0.01f, "");
            check("a player upstairs is inside it; one on the ground floor below is not", hold != null && Membership.Inside(hold, new V3(0, 4, 0)) && !Membership.Inside(hold, new V3(0, 0, 0)), "");
            GoalPlan g = p?.Goals.FirstOrDefault(x => x.Id == "upstairs");
            check("the goal waits for the phone and succeeds on the hold", g != null && g.After.SequenceEqual(new[] { "phone" }) && g.Succeed == 4 && g.Required, r.Text);
            check("the file stays missionlab-plan/3 under its envelope", r.Text.StartsWith("{\"schema\":\"missionlab-plan/3\",\"mission\":{", StringComparison.Ordinal), r.Text.Substring(0, Math.Min(60, r.Text.Length)));
            check("the capabilities it uses are listed", r.Capabilities.Contains("goals") && r.Capabilities.Contains("areas") && r.Capabilities.Contains("places"), string.Join(",", r.Capabilities));
            DraftResult held = Propose(house, "[{\"op\":\"setHold\",\"trigger\":2,\"seconds\":45}]");
            MissionPlan hp = held.Accepted ? MissionPlan.FromJson(held.Text) : null;
            check("setHold turns the door zone into a 45 s hold where it was", hp != null && hp.Triggers[1].When == "hold" && Math.Abs(hp.Triggers[1].Hold - 45f) < 0.01f && Math.Abs(hp.Triggers[1].Pos.X - 20f) < 0.01f, held.Why + " " + Codes(held.Findings));
        }

        {   // Completion: a change that leaves the mission unfinishable is refused; a problem it does not cause does not stop it.
            const string ops = "[{\"op\":\"addTrigger\",\"trigger\":{\"when\":\"after\",\"after\":0,\"name\":\"By hand\"}}," +
                               "{\"op\":\"addGoal\",\"goal\":{\"id\":\"signal\",\"name\":\"Signal\",\"succeed\":4}}," +
                               "{\"op\":\"setExtraction\",\"set\":{\"goals\":true}}]";
            DraftResult r = Propose(house, ops);
            check("an extraction that waits for an objective nobody can complete is refused", !r.Accepted && r.Status == 422 && r.Findings.Any(x => x.Level == Finding.Error && x.Path == "goals[1].succeed"), r.Why + " " + Codes(r.Findings));
        }

        {   // The bounded validation never looks at a map: scatter is not run, and nothing asks for the game's files.
            ValidationReport report = AgentDraft.Validate(house, null, new List<int> { 1, 2 }, new List<long> { 1, 2, 3 }, 10000, CancellationToken.None);
            ValidationCheck scatter = report.Checks.First(c => c.Name == MissionValidation.ScatterCheck), completion = report.Checks.First(c => c.Name == MissionValidation.CompletionCheck);
            check("the agent's validation does not run the scatter check", scatter.State == MissionValidation.NotRun, scatter.State);
            check("it runs the completion matrix and passes the house", completion.State == MissionValidation.Passed, completion.State + " " + completion.Why);
            check("its budget is capped for the agent", report.Budget <= AgentDraft.MaxBudget, report.Budget.ToString());
            check("a passing report gives no finding", AgentDraft.ValidationFindings(report).Count == 0, "");
        }

        {   // What is returned: that scenario and its metadata only, and file names that can never leave the scenarios folder.
            string json = AgentDraft.ScenarioJson("house.json", house, CapabilityRegistry.Embedded(), Current);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement r = doc.RootElement;
                var keys = r.EnumerateObject().Select(x => x.Name).ToList();
                var allowed = new[] { "file", "id", "revision", "hash", "title", "readable", "sealed", "findings", "capabilities", "needs", "places", "triggers", "goals", "enemies", "extraction", "text" };
                check("the scenario answer holds only the listed keys", keys.All(k => allowed.Contains(k)), string.Join(",", keys));
                check("its text is exactly the file's", r.GetProperty("text").GetString() == house && r.GetProperty("hash").GetString() == ScenarioFile.Hash(house), "");
                check("ids an operation can use are there", string.Join(",", r.GetProperty("triggers").EnumerateArray().Select(t => t.GetProperty("id").GetInt32())) == "1,2,3"
                    && string.Join(",", r.GetProperty("places").EnumerateArray().Select(t => t.GetProperty("id").GetInt32() + ":" + t.GetProperty("shaped").GetBoolean())) == "1:True,2:False"
                    && r.GetProperty("goals")[0].GetProperty("id").GetString() == "phone" && r.GetProperty("enemies").GetArrayLength() == 2, json);
                check("no folder or path from this computer is in it", !json.Contains(":\\\\") && !json.Contains("BepInEx") && !json.Contains("/scenarios"), "");
            }
            foreach (string bad in new[] { "../house.json", "sub/house.json", "sub\\house.json", "C:house.json", "house.txt", "", ".." })
                check("'" + bad + "' is not a scenario file name", !AgentDraft.GoodFile(bad), "");
            check("'house.json' is", AgentDraft.GoodFile("house.json"), "");
            check("proposal ids are the plugin's own shape only", AgentDraft.GoodProposalId("p20261120-101500-1") && !AgentDraft.GoodProposalId("../p1") && !AgentDraft.GoodProposalId("P1") && !AgentDraft.GoodProposalId("x1"), "");
        }

        {   // A kept proposal: the exact text it was made against and the proposed text; stale once the file moves on.
            var ask = new DraftAsk { File = "house.json", Name = "Upstairs hold", ExpectedHash = ScenarioFile.Hash(house), Operations = "[{\"op\":\"setHold\",\"trigger\":2,\"seconds\":30}]" };
            DraftResult r = AgentDraft.Propose(house, ask, CapabilityRegistry.Embedded(), Current, null, CancellationToken.None);
            string record = AgentDraft.ProposalJson(r, ask, "p20261120-101500-1", "2026-11-20T10:15:00Z", house);
            using (JsonDocument doc = JsonDocument.Parse(record))
            {
                JsonElement x = doc.RootElement;
                check("the record keeps the base text and the proposed text exactly", x.GetProperty("base").GetProperty("text").GetString() == house && x.GetProperty("scenario").GetString() == r.Text, "");
                check("and its schema, file and operations", x.GetProperty("schema").GetString() == AgentDraft.ProposalSchema && AgentDraft.RecordField(record, "file") == "house.json" && x.GetProperty("operations").GetArrayLength() == 1, "");
            }
            string Row(string hash)
            {
                var sb = new StringBuilder();
                return AgentDraft.Summary(sb, record, hash, 5) ? sb.ToString() : "";
            }
            bool Stale(string row) => JsonDocument.Parse(row).RootElement.GetProperty("stale").GetBoolean();
            check("against the unchanged file it is not stale", Row(ScenarioFile.Hash(house)) != "" && !Stale(Row(ScenarioFile.Hash(house))), Row(ScenarioFile.Hash(house)));
            check("against a changed file it is stale", Stale(Row(ScenarioFile.Hash(House(",\"everyone\":true")))), "");
            check("with the file gone it is stale", Stale(Row("")), "");
            check("a record that is not a proposal is not listed", !AgentDraft.Summary(new StringBuilder(), house, ScenarioFile.Hash(house), 4), "");
            string refused = AgentDraft.AnswerJson(Propose(house, "[{\"op\":\"removeTrigger\",\"id\":1}]"), "house.json", "", "");
            check("a refused proposal's answer carries no scenario text", JsonDocument.Parse(refused).RootElement.GetProperty("scenario").GetString() == "", refused);
        }
    }
}
