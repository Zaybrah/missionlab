using System;
using System.Collections.Generic;
using System.IO;
using MissionLab.Core;

/// <summary>
/// Branching operations (ML-051): the next mission follows the run record's result and objective states, the fallback is
/// taken only when the record rules every branch out, a run record moves the operation on at most once, an end the game did
/// not say waits for the host's labelled decision, an interrupted mission stays incomplete and can be retried without counting
/// twice, a damaged file is put back from its previous version, and a changed scenario keeps the progress made. Reading the
/// files, setting the game's next mission and the lobby are the plugin's (Operations.cs) and are not run here.
/// </summary>
static class Ml051Tests
{
    private const string Intel = "{\"schema\":\"missionlab-plan/1\",\"id\":\"sc-intel\",\"revision\":2,\"spawns\":[{\"pos\":[0,0,0]}]}";
    private const string Quiet = "{\"schema\":\"missionlab-plan/1\",\"id\":\"sc-quiet\",\"revision\":1,\"spawns\":[{\"pos\":[0,0,0]}]}";
    private const string Quiet2 = "{\"schema\":\"missionlab-plan/1\",\"id\":\"sc-quiet\",\"revision\":2,\"spawns\":[{\"pos\":[1,0,0]}]}";
    private const string Loud = "{\"schema\":\"missionlab-plan/1\",\"id\":\"sc-loud\",\"revision\":1,\"spawns\":[{\"pos\":[0,0,0]}]}";
    private const string Before = "20261120-200000_Suburb.run.json";

    private static PlaylistEntry Setup(string label, string file, string text)
    {
        CatalogueInfo.Identity(text, out string id, out int revision);
        return new PlaylistEntry { Label = label, File = file, ScenarioId = id, Revision = revision, Hash = ScenarioFile.Hash(text), Map = "Clean House", NavFile = "suburbv2.bytes" };
    }

    // Recover the phone in mission one for the quiet approach; fail it and play it again; anything else is the loud fallback.
    private static Operation PhoneJob()
    {
        var op = new Operation { Id = "op-phone", Name = "Phone job", Start = "intel", Next = "intel", Armed = true, ArmedAfter = Before };
        var intel = new OperationNode { Id = "intel", Setup = Setup("Intel raid", "intel", Intel), Fallback = "loud" };
        intel.Branches.Add(new OperationBranch { Goal = "phone", State = GoalBook.Succeeded, To = "quiet" });
        intel.Branches.Add(new OperationBranch { Result = Playlist.Failed, To = "intel" });
        op.Nodes.Add(intel);
        op.Nodes.Add(new OperationNode { Id = "quiet", Setup = Setup("Quiet approach", "quiet", Quiet), Fallback = Operation.End });
        op.Nodes.Add(new OperationNode { Id = "loud", Setup = Setup("Loud assault", "loud", Loud), Fallback = Operation.End });
        return op;
    }

    private static OperationRun Run(string file, string id, string scenario, string result, string phone = "", string session = "m1", string outcome = "used")
    {
        var run = new OperationRun { Summary = new RunSummary { File = file, ScenarioId = id, Scenario = scenario, Outcome = outcome, Session = session, Result = result, Ended = result == "" ? "" : "2026-11-20T21:00:00Z" } };
        if (phone != "") run.Goals["phone"] = phone;
        return run;
    }

    public static void Run(Action<string, bool, string> check)
    {
        {   // An operation comes back the same after save and load, and a broken one says where.
            Operation op = PhoneJob();
            op.Nodes[0].Setup.Picks["garrison"] = 1;
            op.Steps.Add(new OperationStep { Record = "20261120-190000_Suburb.run.json", Node = "intel", Result = Playlist.Unknown, Manual = true, Advanced = true, Branch = Operation.Fallback, To = "loud", Goals = { ["phone"] = "active" } });
            op.Round = 2; op.Repeat = "20261120-190000_Suburb.run.json";
            string json = op.ToJson();
            Operation back = Operation.Read(json, out string why);
            check("an operation is read back", back != null, why);
            check("and written again as the same text", back != null && back.ToJson() == json, back == null ? "" : back.ToJson());
            check("its missions, branches, fallback and progress are kept", back != null && back.Nodes.Count == 3 && back.Nodes[0].Branches.Count == 2 && back.Nodes[0].Branches[0].Goal == "phone"
                  && back.Nodes[0].Fallback == "loud" && back.Nodes[0].Setup.Hash == ScenarioFile.Hash(Intel) && back.Nodes[0].Setup.Picks["garrison"] == 1
                  && back.Steps.Count == 1 && back.Steps[0].Branch == Operation.Fallback && back.Steps[0].Manual && back.Steps[0].Goals["phone"] == "active" && back.Round == 2 && back.Armed, json);
            check("a whole operation has nothing to fix", op.Check().Count == 0, string.Join("; ", op.Check()));
            Operation later = Operation.Read("{\"schema\":\"missionlab-operation/2\",\"nodes\":[]}", out string laterWhy, out bool isLater);
            check("an operation from a later MissionLab is refused as such", later == null && isLater && laterWhy.Contains("another MissionLab version"), laterWhy);

            Operation bad = PhoneJob();
            bad.Nodes[1].Fallback = "";
            bad.Nodes[2].Fallback = "nowhere";
            bad.Nodes[0].Branches.Add(new OperationBranch { To = "quiet" });
            var codes = bad.Check().ConvertAll(f => f.Code + " " + f.Path);
            check("a missing fallback, an unknown target and a branch without a condition are each refused", codes.Contains("missing-field nodes[1].fallback") && codes.Contains("missing-ref nodes[2].fallback")
                  && codes.Contains("missing-field nodes[0].branches[2].result"), string.Join(", ", codes));
        }
        {   // The run record's result and objective states pick the branch, first match in the author's order.
            Operation won = PhoneJob();
            string said = won.Take(Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", Playlist.Success, GoalBook.Succeeded), "");
            check("the phone recovered leads to the quiet approach", won.Next == "quiet" && !won.Armed && won.Steps.Count == 1 && won.Steps[0].Branch == 0 && won.Steps[0].Advanced && said.Contains("Next: Quiet approach"), said);

            Operation lost = PhoneJob();
            lost.Take(Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", Playlist.Failed, GoalBook.Failed), "");
            check("a failed mission takes the failure branch, here playing it again", lost.Next == "intel" && lost.Steps[0].Branch == 1, lost.ToJson());

            Operation fallback = PhoneJob();
            string fell = fallback.Take(Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", Playlist.Success, GoalBook.Failed), "");
            check("a success without the phone matches no branch and takes the fallback", fallback.Next == "loud" && fallback.Steps[0].Branch == Operation.Fallback && fell.Contains("fallback"), fell);

            Operation byGoal = PhoneJob();
            byGoal.Take(Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", "over", GoalBook.Succeeded), "");
            check("an objective state the record holds decides a branch even when the game did not say how it ended", byGoal.Next == "quiet" && byGoal.Steps[0].Branch == 0 && !byGoal.Steps[0].Manual, byGoal.ToJson());

            int b = Operation.Choose(PhoneJob().Nodes[0], Playlist.Success, new Dictionary<string, string>(), out string why);
            check("a branch on an objective the record does not list is not guessed, not even as the fallback", b == Operation.Undecided && why.Contains("phone"), why);
        }
        {   // A run record moves the operation on once, however often it is seen.
            Operation op = PhoneJob();
            OperationRun won = Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", Playlist.Success, GoalBook.Succeeded);
            op.Take(won, "");
            check("the same record seen again changes nothing", op.Take(won, "") == "" && op.Next == "quiet" && op.Steps.Count == 1, op.ToJson());
            op.ArmNext(new List<RunSummary> { won.Summary }, "");
            check("nor once the next mission is set up", op.Take(won, "m3") == "" && op.Next == "quiet" && op.Steps.Count == 1 && op.Armed, op.ToJson());
            check("nor can the host decide on it again", !op.Decide(won.Summary.File, Operation.Fallback, out string no) && op.Next == "quiet", no);
            Operation again = Operation.Read(op.ToJson(), out string _);
            check("nor after the operation has been saved and read back", again != null && again.Take(won, "m3") == "" && again.Next == "quiet" && again.Steps.Count == 1, again == null ? "" : again.ToJson());
            check("a run of another mission than the next one does not count", again != null && again.Take(Run("20261120-210000_Suburb.run.json", "sc-loud", "loud", Playlist.Success), "") == "" && again.Next == "quiet", again == null ? "" : again.ToJson());
            check("a run from before the mission was set up does not count", PhoneJob().Take(Run("20261120-190000_Suburb.run.json", "sc-intel", "intel", Playlist.Success, GoalBook.Succeeded), "") == "", "");
            Operation edited = PhoneJob();
            OperationRun other = Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", Playlist.Success, GoalBook.Succeeded);
            other.Summary.Hash = "fnv1a64:0000000000000000";       // the file was edited after the mission was set up
            check("a run played from other content than was pinned does not count", edited.Take(other, "") == "" && edited.Steps.Count == 0 && edited.Next == "intel", edited.ToJson());
        }
        {   // An end the game did not say, with no objective states, waits for the host, whose decision is labelled.
            Operation op = PhoneJob();
            OperationRun over = Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", "over");
            string said = op.Take(over, "");
            OperationStep p = op.Pending();
            check("it is not taken as a success and does not move on", op.Next == "intel" && op.Armed && p != null && p.Result == Playlist.Unknown && !p.Advanced && said.Contains("waits for your decision"), said);
            check("seen again it still waits", op.Take(over, "") == "" && op.Next == "intel" && op.Pending() == p, op.ToJson());
            check("a branch the mission does not have is refused", !op.Decide(over.Summary.File, 7, out string _) && op.Next == "intel", "");
            check("the host's decision moves on once", op.Decide(over.Summary.File, 0, out string decided) && op.Next == "quiet" && p.Manual && p.Branch == 0 && decided.Contains("decided by the host"), decided);
            check("and is labelled as the host's", Operation.ResultWords(p).Contains("decided by the host") && Operation.ResultWords(p).StartsWith("unknown"), Operation.ResultWords(p));
            check("a second decision does not move on again", !op.Decide(over.Summary.File, 1, out string _) && op.Next == "quiet", op.ToJson());
            check("nor does the record once its end is filled in", op.Take(Run(over.Summary.File, "sc-intel", "intel", Playlist.Failed), "") == "" && op.Next == "quiet" && p.Result == Playlist.Unknown, op.ToJson());
        }
        {   // An interrupted mission stays incomplete; retrying its setup counts once and keeps what came before.
            Operation op = PhoneJob();
            op.Steps.Add(new OperationStep { Record = "20261120-180000_Suburb.run.json", Node = "intel", Result = Playlist.Failed, Advanced = true, Branch = 1, To = "intel" });
            OperationRun cut = Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", "", "", "m7");
            check("a run still going on is not counted yet", op.Take(cut, "m7") == "" && op.Steps.Count == 1, op.ToJson());
            string said = op.Take(cut, "m8");
            OperationStep s = op.Steps.Find(x => x.Record == cut.Summary.File);
            check("a mission over without its end seen is incomplete and the operation stays on it", s != null && s.Result == Playlist.Incomplete && op.Next == "intel" && op.Pending() == s && said.Contains("incomplete"), said);
            check("retry plays that run's recorded setup", op.RetryRecord() == cut.Summary.File, op.RetryRecord());
            check("setting aside is labelled and leaves it to be played again", op.Decide(cut.Summary.File, Operation.Again, out string aside) && op.Pending() == null && op.Next == "intel" && op.RetryRecord() == cut.Summary.File && s.Manual, aside);
            op.ArmNext(new List<RunSummary> { cut.Summary }, op.RetryRecord());
            check("set up again it repeats that run and only a later run counts", op.Armed && op.Repeat == cut.Summary.File && op.ArmedAfter == cut.Summary.File, op.ToJson());
            check("the interrupted run seen again counts nothing", op.Take(cut, "m9") == "" && op.Steps.Count == 2, op.ToJson());
            op.Take(Run("20261120-213000_Suburb.run.json", "sc-intel", "intel", Playlist.Success, GoalBook.Succeeded), "m9");
            check("the retry moves the operation on once, with the earlier runs kept", op.Next == "quiet" && op.Steps.Count == 3 && op.Steps[0].Advanced && !s.Advanced && op.Repeat == "" && !op.Armed, op.ToJson());
            check("and an end of the interrupted run turning up later does not move it again", op.Take(Run(cut.Summary.File, "sc-intel", "intel", Playlist.Failed, "", "m7"), "m9") == "" && op.Next == "quiet", op.ToJson());
        }
        {   // A changed scenario: the host retargets or restarts, and the progress made stays.
            Operation op = PhoneJob();
            op.Take(Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", Playlist.Success, GoalBook.Succeeded), "");
            OperationNode quiet = op.Node("quiet");
            check("the next mission's file saved again is seen as changed", Playlist.Identity(quiet.Setup, Quiet2, out string changed) == Playlist.Changed && changed.Contains("revision 1 when it was added, revision 2 now"), changed);
            check("a missing file is seen as missing", Playlist.Identity(quiet.Setup, null, out string gone) == Playlist.Missing, gone);
            quiet.Setup = Setup("Quiet approach", "quiet", Quiet2);
            Operation saved = Operation.Read(op.ToJson(), out string _);
            check("retargeting it keeps the runs counted and the next mission", saved != null && saved.Next == "quiet" && saved.Steps.Count == 1 && saved.Steps[0].To == "quiet" && saved.Check().Count == 0
                  && Playlist.Identity(saved.Node("quiet").Setup, Quiet2, out string _) == Playlist.Same, saved == null ? "" : saved.ToJson());
            string restarted = op.Restart();
            check("restarting begins a new round at the start and keeps the earlier runs", op.Round == 2 && op.Next == "intel" && !op.Armed && op.Steps.Count == 1 && op.Latest() == null && restarted.Contains("Round 2"), restarted);
        }
        {   // Saves are atomic; a cut-off current file is put back from the previous version, with a message.
            string dir = Path.Combine(Path.GetTempPath(), "ml051-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "phone-job.json"), prev = Path.Combine(dir, "phone-job.prev.json");
                Operation op = PhoneJob();
                OperationStore.Save(path, op);
                string first = op.ToJson();
                op.Take(Run("20261120-203000_Suburb.run.json", "sc-intel", "intel", Playlist.Success, GoalBook.Succeeded), "");
                OperationStore.Save(path, op);
                check("a save keeps the version before it as <name>.prev.json", File.Exists(prev) && File.ReadAllText(prev) == first && File.ReadAllText(path) == op.ToJson() && !File.Exists(path + ".part"), File.Exists(prev) ? File.ReadAllText(prev) : "no prev");
                string whole = File.ReadAllText(path);
                File.WriteAllText(path, whole.Substring(0, whole.Length / 2));      // a write cut off half way
                Operation back = OperationStore.Load(path, out string why, out string recovered);
                check("a cut-off file is put back from the previous version", back != null && back.ToJson() == first && back.Next == "intel", why);
                check("and says so clearly", recovered.Contains("damaged") && recovered.Contains("previous version") && recovered.Contains("next: Intel raid"), recovered);
                check("the file is whole again and the damaged text is kept", Operation.Read(File.ReadAllText(path), out string _) != null && File.Exists(path + ".damaged"), File.ReadAllText(path));
                File.WriteAllText(path, "{\"schema\":\"missionlab-operation/1\",");
                OperationStore.Save(path, op);
                check("saving over a damaged file never replaces the good previous version", File.ReadAllText(prev) == first && File.ReadAllText(path) == op.ToJson(), File.ReadAllText(prev));
                File.WriteAllText(path, "{\"schema\":\"missionlab-operation/9\",\"nodes\":[]}");
                Operation newer = OperationStore.Load(path, out string newerWhy, out string notRecovered);
                check("a file from a later MissionLab is left as it is, not replaced by the previous version", newer == null && notRecovered == "" && File.ReadAllText(path).Contains("operation/9") && newerWhy.Contains("another MissionLab version"), newerWhy);
                File.Delete(prev);
                File.WriteAllText(path, "{\"schema\":");
                Operation none = OperationStore.Load(path, out string noneWhy, out string _);
                check("with no previous version a damaged file is reported and left alone", none == null && noneWhy.Contains("no readable previous version") && File.ReadAllText(path) == "{\"schema\":", noneWhy);
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
    }
}
