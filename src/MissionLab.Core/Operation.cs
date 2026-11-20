using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// One way out of a mission of an operation. Every condition given must hold: Result (success or failed; "" for either)
    /// and Goal with State (the objective's final state in the run record: succeeded or failed; Goal "" for none). A branch
    /// with neither condition would hide the fallback, so it is refused. To: a node id, or Operation.End.
    /// </summary>
    public sealed class OperationBranch
    {
        public string Result = "";
        public string Goal = "";
        public string State = GoalBook.Succeeded;
        public string To = "";
    }

    /// <summary>
    /// One mission of an operation: its setup pinned as a playlist entry is (file, id, revision, fingerprint, host picks and
    /// dice), so identity and dice follow the playlist's rules; its branches in the author's order; and the fallback taken
    /// when no branch matches. The fallback is required: an operation never stops because the author did not foresee an end.
    /// </summary>
    public sealed class OperationNode
    {
        public string Id = "";
        public PlaylistEntry Setup = new PlaylistEntry();
        public List<OperationBranch> Branches = new List<OperationBranch>();
        public string Fallback = "";
    }

    /// <summary>
    /// One run record counted for a node, at most once (the record's file name is the key). Branch: the branch it left by,
    /// Operation.Fallback for the fallback, Operation.Undecided while it has not moved the operation on. Again: the host said
    /// it does not count and the mission is to be played again. Manual: the host chose, not the recorded results.
    /// </summary>
    public sealed class OperationStep
    {
        public string Record = "";
        public string Node = "";
        public int Round = 1;
        public string Result = "";                  // success, failed, unknown, incomplete or refused (Playlist's words)
        public SortedDictionary<string, string> Goals = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public string Hash = "";
        public bool Manual, Advanced, Again;
        public int Branch = Operation.Undecided;
        public string To = "";
    }

    /// <summary>A run record as an operation needs it: the summary the catalogue reads, and the objectives' final states.</summary>
    public sealed class OperationRun
    {
        public RunSummary Summary;
        public SortedDictionary<string, string> Goals = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>A run record's text; null when it cannot be read. Goals come from end.goals (written by RunRecord.Finish, ML-046).</summary>
        public static OperationRun Read(string file, string json)
        {
            RunSummary s = RunSummary.Read(file, json);
            if (s == null) return null;
            var run = new OperationRun { Summary = s };
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement r = doc.RootElement;
                    if (r.TryGetProperty("end", out JsonElement end) && end.ValueKind == JsonValueKind.Object && end.TryGetProperty("goals", out JsonElement goals))
                        Operation.ReadGoals(goals, run.Goals);
                }
            }
            catch (Exception) { }
            return run;
        }
    }

    /// <summary>
    /// A short chain of scenarios with consequences (missionlab-operation/1, ML-051): which mission comes next depends on how
    /// the last one ended, as its run record says (success or failed, and the objectives' final states). It carries results
    /// between missions only. Nothing inside a mission is saved or restored: not enemies, inventory, positions, navigation,
    /// physics or networked objects; every mission starts as its scenario says.
    ///
    /// Progress (the next node, the runs counted, the choices made, the round) changes only at mission boundaries: when a run
    /// record shows a mission's end, when the host sets the next mission up between missions, or when the host decides. A run
    /// record counts once, keyed by its file name, however often it is seen. A branch is taken only when the record says
    /// enough to decide it: an end without a known result and without objective states, or an objective the record does not
    /// list, waits for the host, whose choice is labelled as such. Success is never assumed.
    /// </summary>
    public sealed class Operation
    {
        public const string Schema = "missionlab-operation/1";
        public const string End = "end";                                    // a branch target and a next node: the operation is over
        public const int Fallback = -1, Undecided = -2, Again = -3;        // OperationStep.Branch, and the host's choices
        public const int MostNodes = 6, MostBranches = 6, MostSteps = 100, LongestName = 60;

        public string Id = "", Name = "", Start = "";
        public string Next = "";                    // the node the next mission is for; End when the operation is over
        public int Round = 1;                       // a restart begins a new round; earlier rounds' runs stay on record
        public bool Armed;                          // the host set the next node up in the game
        public string ArmedAfter = "";              // the newest run record when it was set up: only a later run counts for it
        public string Repeat = "";                  // the run record whose setup the next mission repeats ("": as the dice say)
        public List<OperationNode> Nodes = new List<OperationNode>();
        public List<OperationStep> Steps = new List<OperationStep>();

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        public OperationNode Node(string id) => id == null ? null : Nodes.Find(n => n.Id == id);
        public OperationNode Current => Next == End ? null : Node(Next);
        public bool Done => Next == End;

        // ----- the file

        public string ToJson()
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\"schema\":\"").Append(Schema).Append("\",\"id\":").Append(Q(Id)).Append(",\"name\":").Append(Q(Name))
              .Append(",\"start\":").Append(Q(Start)).Append(",\"next\":").Append(Q(Next));
            if (Round > 1) sb.Append(",\"round\":").Append(Round.ToString(Inv));
            if (Armed) sb.Append(",\"armed\":true");
            if (ArmedAfter != "") sb.Append(",\"armedAfter\":").Append(Q(ArmedAfter));
            if (Repeat != "") sb.Append(",\"repeat\":").Append(Q(Repeat));
            sb.Append(",\"nodes\":[");
            for (int i = 0; i < Nodes.Count; i++)
            {
                OperationNode n = Nodes[i];
                sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(Q(n.Id)).Append(",\"setup\":");
                Playlist.EntryJson(n.Setup, sb);
                sb.Append(",\"branches\":[");
                for (int j = 0; j < n.Branches.Count; j++)
                {
                    OperationBranch b = n.Branches[j];
                    sb.Append(j > 0 ? "," : "").Append('{');
                    if (b.Result != "") sb.Append("\"result\":").Append(Q(b.Result)).Append(',');
                    if (b.Goal != "") sb.Append("\"goal\":").Append(Q(b.Goal)).Append(",\"state\":").Append(Q(b.State)).Append(',');
                    sb.Append("\"to\":").Append(Q(b.To)).Append('}');
                }
                sb.Append("],\"fallback\":").Append(Q(n.Fallback)).Append('}');
            }
            sb.Append("],\"steps\":[");
            for (int i = 0; i < Steps.Count; i++)
            {
                OperationStep s = Steps[i];
                sb.Append(i > 0 ? "," : "").Append("{\"record\":").Append(Q(s.Record)).Append(",\"node\":").Append(Q(s.Node))
                  .Append(",\"round\":").Append(s.Round.ToString(Inv)).Append(",\"result\":").Append(Q(s.Result));
                if (s.Goals.Count > 0) sb.Append(",\"goals\":").Append(GoalsJson(s.Goals));
                if (s.Hash != "") sb.Append(",\"hash\":").Append(Q(s.Hash));
                if (s.Manual) sb.Append(",\"manual\":true");
                if (s.Again) sb.Append(",\"again\":true");
                if (s.Advanced) sb.Append(",\"advanced\":true,\"branch\":").Append(s.Branch.ToString(Inv)).Append(",\"to\":").Append(Q(s.To));
                sb.Append('}');
            }
            return sb.Append("]}").ToString();
        }

        public static string GoalsJson(SortedDictionary<string, string> goals)
        {
            var sb = new StringBuilder(64);
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, string> g in goals) { sb.Append(first ? "" : ",").Append(Q(g.Key)).Append(':').Append(Q(g.Value)); first = false; }
            return sb.Append('}').ToString();
        }

        public static Operation Read(string json, out string why) => Read(json, out why, out bool _);

        /// <summary>
        /// An operation file's text; null with the reason when it cannot be read. later: it was written by another MissionLab
        /// version, so it is left as it is (never rewritten and never replaced by its previous version).
        /// </summary>
        public static Operation Read(string json, out string why, out bool later)
        {
            why = ""; later = false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? "", Lenient))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) { why = "an operation is a JSON object"; return null; }
                    string schema = Text(r, "schema");
                    if (schema != Schema)
                    {
                        later = schema.StartsWith("missionlab-operation/", StringComparison.Ordinal);
                        why = later ? "it was written by another MissionLab version (" + schema + "), so it is left as it is" : "it is not a MissionLab operation (" + Schema + " expected)";
                        return null;
                    }
                    var op = new Operation
                    {
                        Id = Clean(Text(r, "id"), 64), Name = Clean(Text(r, "name"), LongestName), Start = Text(r, "start"), Round = Math.Max(1, Int(r, "round")),
                        Armed = r.TryGetProperty("armed", out JsonElement a) && a.ValueKind == JsonValueKind.True,
                        ArmedAfter = RecordName(Text(r, "armedAfter")), Repeat = RecordName(Text(r, "repeat"))
                    };
                    if (!MissionPlan.IdOk(op.Id)) op.Id = "";
                    if (r.TryGetProperty("nodes", out JsonElement ns) && ns.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement x in ns.EnumerateArray())
                        {
                            if (op.Nodes.Count >= MostNodes) break;
                            if (x.ValueKind == JsonValueKind.Object) op.Nodes.Add(ReadNode(x));
                        }
                    op.Next = r.TryGetProperty("next", out JsonElement nx) && nx.ValueKind == JsonValueKind.String ? nx.GetString() ?? "" : op.Start;
                    if (r.TryGetProperty("steps", out JsonElement ss) && ss.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement x in ss.EnumerateArray())
                        {
                            if (x.ValueKind != JsonValueKind.Object) continue;
                            string record = RecordName(Text(x, "record"));
                            if (record == "" || op.Steps.Exists(o => o.Record == record)) continue;     // a record counts once, also in a hand-edited file
                            var s = new OperationStep
                            {
                                Record = record, Node = Text(x, "node"), Round = Math.Max(1, Int(x, "round")), Result = Clean(Text(x, "result"), 20), Hash = Clean(Text(x, "hash"), 40),
                                Manual = True(x, "manual"), Again = True(x, "again"), Advanced = True(x, "advanced"), To = Text(x, "to")
                            };
                            if (s.Advanced) s.Branch = x.TryGetProperty("branch", out JsonElement b) && b.ValueKind == JsonValueKind.Number && b.TryGetInt32(out int bi) && bi >= Fallback ? bi : Fallback;
                            if (x.TryGetProperty("goals", out JsonElement g)) ReadGoals(g, s.Goals);
                            op.Steps.Add(s);
                        }
                    Trim(op.Steps);
                    if (op.Current == null) { op.Armed = false; op.Repeat = ""; }
                    return op;
                }
            }
            catch (JsonException e) { why = "it is not JSON: " + e.Message; return null; }
        }

        private static OperationNode ReadNode(JsonElement x)
        {
            var n = new OperationNode { Id = Text(x, "id"), Fallback = Text(x, "fallback") };
            // The setup is read by the playlist's own reader, so a pinned mission means the same in both.
            if (x.TryGetProperty("setup", out JsonElement setup) && setup.ValueKind == JsonValueKind.Object)
            {
                Playlist one = Playlist.Read("{\"schema\":\"" + Playlist.Schema + "\",\"entries\":[" + setup.GetRawText() + "]}", out string _);
                if (one != null && one.Entries.Count == 1) n.Setup = one.Entries[0];
            }
            if (x.TryGetProperty("branches", out JsonElement bs) && bs.ValueKind == JsonValueKind.Array)
                foreach (JsonElement b in bs.EnumerateArray())
                {
                    if (n.Branches.Count >= MostBranches) break;
                    if (b.ValueKind != JsonValueKind.Object) continue;
                    string state = Text(b, "state");
                    n.Branches.Add(new OperationBranch { Result = Text(b, "result"), Goal = Text(b, "goal"), State = state == "" ? GoalBook.Succeeded : state, To = Text(b, "to") });
                }
            return n;
        }

        internal static void ReadGoals(JsonElement goals, SortedDictionary<string, string> into)
        {
            if (goals.ValueKind != JsonValueKind.Object) return;
            foreach (JsonProperty p in goals.EnumerateObject())
                if (GoalPlan.IdOk(p.Name) && p.Value.ValueKind == JsonValueKind.String) into[p.Name] = Clean(p.Value.GetString(), 20);
        }

        // ----- is it a usable operation?

        /// <summary>What stops the operation from being saved or set up, as findings with the file's paths. Empty when it can be used.</summary>
        public List<Finding> Check()
        {
            var found = new List<Finding>();
            void Bad(string code, string path, string text) => found.Add(new Finding { Code = code, Path = path, Text = text });
            if (Nodes.Count == 0) Bad("missing-field", "nodes", "an operation needs at least one mission");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Nodes.Count; i++)
            {
                OperationNode n = Nodes[i];
                string at = "nodes[" + i.ToString(Inv) + "]";
                if (!GoalPlan.IdOk(n.Id) || n.Id == End) Bad("bad-id", at + ".id", "a mission's id is 1 to 32 of a-z, 0-9 and '-', and not \"end\"");
                else if (!ids.Add(n.Id)) Bad("duplicate-id", at + ".id", "two missions have the id " + n.Id);
                if (n.Setup == null || !Playlist.FileOk(n.Setup.File)) Bad("missing-field", at + ".setup.file", "the mission names no scenario file");
            }
            bool Target(string to) => to == End || ids.Contains(to);
            if (Nodes.Count > 0 && !ids.Contains(Start)) Bad(Start == "" ? "missing-field" : "missing-ref", "start", "the operation must start with one of its missions");
            for (int i = 0; i < Nodes.Count; i++)
            {
                OperationNode n = Nodes[i];
                string at = "nodes[" + i.ToString(Inv) + "]";
                if (n.Fallback == "") Bad("missing-field", at + ".fallback", "every mission needs a fallback: where the operation goes when no branch matches");
                else if (!Target(n.Fallback)) Bad("missing-ref", at + ".fallback", "the fallback names no mission of this operation");
                for (int j = 0; j < n.Branches.Count; j++)
                {
                    OperationBranch b = n.Branches[j];
                    string bat = at + ".branches[" + j.ToString(Inv) + "]";
                    if (b.Result != "" && b.Result != Playlist.Success && b.Result != Playlist.Failed) Bad("unknown-value", bat + ".result", "a branch's result is success or failed");
                    if (b.Goal != "" && !GoalPlan.IdOk(b.Goal)) Bad("bad-id", bat + ".goal", "not an objective id");
                    if (b.Goal != "" && b.State != GoalBook.Succeeded && b.State != GoalBook.Failed) Bad("unknown-value", bat + ".state", "an objective's state is succeeded or failed");
                    if (b.Result == "" && b.Goal == "") Bad("missing-field", bat + ".result", "a branch without a result or an objective would always be taken; that is what the fallback is for");
                    if (b.To == "") Bad("missing-field", bat + ".to", "the branch leads nowhere");
                    else if (!Target(b.To)) Bad("missing-ref", bat + ".to", "the branch names no mission of this operation");
                }
            }
            if (Next != End && Next != "" && Nodes.Count > 0 && !ids.Contains(Next)) Bad("missing-ref", "next", "the next mission is no longer in the operation");
            return found;
        }

        // ----- which way out

        /// <summary>
        /// The branch a run leaves by: its index, Fallback when the record rules out every branch, or Undecided (with the
        /// reason) when it does not say enough. Branches are tried in the author's order; a branch whose condition the record
        /// cannot answer stops the search, because a later branch or the fallback could be the wrong way.
        /// </summary>
        public static int Choose(OperationNode n, string result, IDictionary<string, string> goals, out string why)
        {
            why = "";
            if (n == null) { why = "the mission is no longer in the operation"; return Undecided; }
            bool resultKnown = result == Playlist.Success || result == Playlist.Failed, goalsKnown = goals != null && goals.Count > 0;
            if (!resultKnown && !goalsKnown) { why = "the game did not say how it ended and no objective states were recorded"; return Undecided; }
            for (int i = 0; i < n.Branches.Count; i++)
            {
                OperationBranch b = n.Branches[i];
                bool no = false, unknown = false;
                if (b.Result != "") { if (!resultKnown) unknown = true; else if (b.Result != result) no = true; }
                if (b.Goal != "")
                {
                    if (goals == null || !goals.TryGetValue(b.Goal, out string state)) unknown = true;
                    else if (state != b.State) no = true;
                }
                if (no) continue;
                if (!unknown) return i;
                why = "branch " + (i + 1).ToString(Inv) + " asks " + ConditionWords(b) + ", which the run record does not say";
                return Undecided;
            }
            return Fallback;
        }

        public static string ConditionWords(OperationBranch b)
        {
            if (b == null) return "";
            var parts = new List<string>();
            if (b.Result == Playlist.Success) parts.Add("a success");
            else if (b.Result == Playlist.Failed) parts.Add("failed");
            if (b.Goal != "") parts.Add("objective " + b.Goal + " " + b.State);
            return string.Join(" and ", parts);
        }

        // ----- runs seen

        /// <summary>
        /// One run record looked at (session: the mission running now, "" for none). A new record counts for the node set up
        /// before it began; a record already counted only gains an end that was not known. Returns what changed, "" for nothing.
        /// </summary>
        public string Take(OperationRun run, string session)
        {
            RunSummary r = run == null ? null : run.Summary;
            if (r == null || RecordName(r.File) == "") return "";
            string result = Playlist.ResultOf(r, session);
            OperationStep known = Steps.Find(x => x.Record == r.File);
            if (known != null)
            {
                // Seen again: the host's word and a step taken stand; only an end that was not known can be filled in.
                if (known.Manual || known.Advanced || result == "" || result == known.Result || (known.Result != Playlist.Unknown && known.Result != Playlist.Incomplete)) return "";
                known.Result = result;
                known.Goals = new SortedDictionary<string, string>(run.Goals, StringComparer.Ordinal);
                return Settle(known);
            }
            OperationNode n = Current;
            if (!Armed || n == null || result == "" || string.CompareOrdinal(r.File, ArmedAfter) <= 0 || !Playlist.Belongs(n.Setup, r)) return "";
            // Played from other content than was pinned (the file changed after it was set up): not this mission's run.
            if (n.Setup.Hash != "" && r.Hash != "" && r.Hash != n.Setup.Hash) return "";
            var step = new OperationStep { Record = r.File, Node = n.Id, Round = Round, Result = result, Hash = r.Hash, Goals = new SortedDictionary<string, string>(run.Goals, StringComparer.Ordinal) };
            Steps.Add(step);
            Trim(Steps);
            return Settle(step);
        }

        /// <summary>The last run counted for the next node in this round, when it has not moved the operation on; else null.</summary>
        public OperationStep Latest()
        {
            if (Done) return null;
            for (int i = Steps.Count - 1; i >= 0; i--)
            {
                OperationStep s = Steps[i];
                if (s.Round == Round && s.Node == Next) return s.Advanced ? null : s;
            }
            return null;
        }

        /// <summary>
        /// The run waiting for the host's decision: the last run of the next node that has not moved the operation on, was
        /// played (not refused) and was not set aside. Its end was not seen, not known, or the record does not answer a branch.
        /// </summary>
        public OperationStep Pending()
        {
            OperationStep s = Latest();
            return s != null && !s.Again && s.Result != Playlist.Refused ? s : null;
        }

        /// <summary>The run whose exact setup "retry" plays again: the played run still on the next node ("" for none).</summary>
        public string RetryRecord()
        {
            OperationStep s = Latest();
            return s != null && s.Result != Playlist.Refused ? s.Record : "";
        }

        private string Settle(OperationStep step)
        {
            string label = NodeName(step.Node), how = ResultWords(step);
            if (step.Result == Playlist.Refused) return label + ": " + how + ". The operation stays on it";
            if (step.Advanced || !ReferenceEquals(step, Latest())) return label + ": " + how;
            if (step.Result == Playlist.Incomplete) return label + ": " + how + ". The operation stays on it: host it again to retry, or decide how it went";
            OperationNode n = Node(step.Node);
            int branch = Choose(n, step.Result, step.Goals, out string why);
            if (branch == Undecided) return label + ": " + how + ". " + Cap(why) + ", so the operation waits for your decision";
            return Advance(step, n, branch, label + ": " + how);
        }

        private string Advance(OperationStep step, OperationNode n, int branch, string head)
        {
            step.Branch = branch;
            step.To = branch >= 0 ? n.Branches[branch].To : n.Fallback;
            step.Advanced = true;
            Next = step.To; Armed = false; ArmedAfter = ""; Repeat = "";
            return head + ". " + Cap(WayWords(n, branch)) + ". Next: " + (Done ? "nothing, the operation is over" : NodeName(Next));
        }

        /// <summary>
        /// The host decides for the run waiting on them: a branch by its index, Fallback, or Again (the run does not count and
        /// the mission is played again). Labelled as manual; it moves the operation on at most once.
        /// </summary>
        public bool Decide(string record, int choice, out string said)
        {
            said = "";
            OperationStep step = Steps.Find(x => x.Record == (record ?? ""));
            if (step == null) { said = "that run was not played for this operation"; return false; }
            if (step.Advanced) { said = "that run has already moved the operation on"; return false; }
            if (!ReferenceEquals(step, Pending())) { said = "the operation is not waiting for a decision on that run"; return false; }
            OperationNode n = Node(step.Node);
            if (n == null) { said = "its mission is no longer in the operation"; return false; }
            if (choice == Again)
            {
                step.Manual = true; step.Again = true;
                said = NodeName(n.Id) + ": set aside by the host; it does not count. Host it again to play it";
                return true;
            }
            if (choice < Fallback || choice >= n.Branches.Count) { said = "that mission has no such way out"; return false; }
            step.Manual = true;
            said = Advance(step, n, choice, NodeName(n.Id) + ": decided by the host");
            return true;
        }

        /// <summary>The host set the next node up in the game. runs: the run records on disk, oldest first.</summary>
        public void ArmNext(List<RunSummary> runs, string repeat)
        {
            Armed = Current != null;
            ArmedAfter = runs != null && runs.Count > 0 ? RecordName(runs[runs.Count - 1].File) : "";
            Repeat = Armed ? RecordName(repeat) : "";
        }

        /// <summary>Begins again from the start node. Every run counted so far stays on record under its round.</summary>
        public string Restart()
        {
            Round++; Next = Start; Armed = false; ArmedAfter = ""; Repeat = "";
            return "Round " + Round.ToString(Inv) + " begins with " + NodeName(Start) + "; the runs of earlier rounds stay on record";
        }

        public string NodeName(string id)
        {
            if (id == End) return "the end of the operation";
            OperationNode n = Node(id);
            return n == null ? id : Playlist.NameOf(n.Setup) != "" ? Playlist.NameOf(n.Setup) : n.Id;
        }

        public string WayWords(OperationNode n, int branch)
        {
            if (branch == Fallback) return "no branch matched, so the fallback";
            if (n == null || branch < 0 || branch >= n.Branches.Count) return "";
            return "branch " + (branch + 1).ToString(Inv) + " (" + ConditionWords(n.Branches[branch]) + ")";
        }

        public static string ResultWords(OperationStep s)
        {
            if (s == null) return "";
            string how = s.Result == Playlist.Success ? "a success" : s.Result == Playlist.Failed ? "failed" : s.Result == Playlist.Refused ? "refused, so it was not played"
                       : s.Result == Playlist.Incomplete ? "incomplete: its end was not seen" : "unknown: the game did not say how it ended";
            if (s.Again) how += "; set aside by the host";
            else if (s.Manual) how += "; the way on was decided by the host";
            return how;
        }

        /// <summary>Where it stands, in a few words: for messages such as the recovery of a damaged file.</summary>
        public string Summary()
        {
            int runs = Steps.FindAll(s => s.Round == Round).Count;
            return "round " + Round.ToString(Inv) + ", " + runs.ToString(Inv) + (runs == 1 ? " run" : " runs") + " counted, next: " + (Done ? "nothing, it is over" : NodeName(Next)) + (Armed ? ", set up" : "");
        }

        // ----- helpers

        private static void Trim(List<OperationStep> steps) { if (steps.Count > MostSteps) steps.RemoveRange(0, steps.Count - MostSteps); }

        // A run record named by its file name only, never a folder.
        private static string RecordName(string s)
        {
            string n = (s ?? "").Trim();
            return n.EndsWith(".run.json", StringComparison.OrdinalIgnoreCase) && n.IndexOf('/') < 0 && n.IndexOf('\\') < 0 && !n.Contains("..") && n.Length <= 120 ? n : "";
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);
        private static string Q(string s) => "\"" + Guard.Esc(s ?? "") + "\"";
        private static bool True(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.True;
        private static string Text(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        private static int Int(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;

        private static string Clean(string s, int most)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").Trim()) if (c >= ' ') sb.Append(c);
            string t = sb.ToString();
            return t.Length > most ? t.Substring(0, most) : t;
        }
    }

    /// <summary>
    /// Saving and reading an operation file so that a crash or a cut-off write never loses the operation. A save writes a
    /// temporary file, keeps the current file as "&lt;name&gt;.prev.json" (only when it can be read, so a damaged file never
    /// replaces a good previous version) and then moves the new text into place. Reading a damaged file puts the previous
    /// version back, keeps the damaged text beside it as "&lt;name&gt;.json.damaged" and says so.
    /// </summary>
    public static class OperationStore
    {
        public const long MaxBytes = 1024 * 1024;

        public static string PrevPath(string path)
        {
            string p = path ?? "";
            return p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? p.Substring(0, p.Length - 5) + ".prev.json" : p + ".prev";
        }

        public static void Save(string path, Operation op)
        {
            string temp = path + ".part";
            File.WriteAllText(temp, op.ToJson());
            if (File.Exists(path) && Readable(path)) File.Copy(path, PrevPath(path), true);
            File.Move(temp, path, true);
        }

        private static bool Readable(string path)
        {
            try { return new FileInfo(path).Length <= MaxBytes && Operation.Read(File.ReadAllText(path), out string _) != null; }
            catch (Exception) { return false; }
        }

        /// <summary>The operation in path; null with the reason. recovered: what was put back from the previous version ("" for nothing).</summary>
        public static Operation Load(string path, out string why, out string recovered)
        {
            why = ""; recovered = "";
            string name = Path.GetFileName(path ?? "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { why = "there is no operation " + name; return null; }
            string bad = "";
            bool later = false;
            Operation op = null;
            try
            {
                if (new FileInfo(path).Length > MaxBytes) bad = "it is too large to be an operation";
                else op = Operation.Read(File.ReadAllText(path), out bad, out later);
            }
            catch (Exception e) { bad = "it could not be read: " + e.Message; }
            if (op != null) return op;
            if (later) { why = name + " cannot be used: " + bad; return null; }
            string prev = PrevPath(path), prevText = null;
            Operation back = null;
            try
            {
                if (File.Exists(prev) && new FileInfo(prev).Length <= MaxBytes) { prevText = File.ReadAllText(prev); back = Operation.Read(prevText, out string _); }
            }
            catch (Exception) { back = null; }
            if (back == null) { why = name + " is damaged (" + bad + ") and has no readable previous version (" + Path.GetFileName(prev) + "); nothing was changed"; return null; }
            string kept = "";
            try { File.Copy(path, path + ".damaged", true); kept = " The damaged file is kept as " + name + ".damaged."; } catch (Exception) { }
            try { string temp = path + ".part"; File.WriteAllText(temp, prevText); File.Move(temp, path, true); }
            catch (Exception e) { kept += " The previous version could not be written back (" + e.Message + "); it is used for now."; }
            recovered = name + " was damaged (" + bad + "), so its previous version was put back: the operation is as it stood one save earlier (" + back.Summary() + "). Check the next mission before you host it." + kept;
            return back;
        }
    }
}
