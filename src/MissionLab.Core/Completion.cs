using System;
using System.Collections.Generic;

namespace MissionLab.Core
{
    /// <summary>One thing found wrong with a scenario: how bad, where in the file, and what in plain words.</summary>
    public sealed class Finding
    {
        public const string Error = "error", Note = "note";
        public string Level = Error;
        public string Path = "";         // where in the file, e.g. "triggers[2].after"
        public string Code = "";         // a short fixed word for the kind of finding, the same in the editor: tests compare these, never the wording
        public string Text = "";
        public override string ToString() => (Path == "" ? "" : Path + ": ") + Text;
    }

    /// <summary>
    /// Can this mission still be finished? Asked of a scenario once chance, either/or and "players needed" have been
    /// decided for one mission. It follows what the extraction waits for through "after", "all" and "any", and looks
    /// whether the scenario's own target person is still there. It says nothing about whether the mission can be won
    /// in a fight: only that the rules do not make it impossible.
    /// </summary>
    public static class Completion
    {
        public static bool Blocks(List<Finding> findings) => findings.Exists(f => f.Level == Finding.Error);

        /// <summary>
        /// plan: the scenario as it stands for this mission (triggers that lost their roll carry Off).
        /// gone: what was taken out for this mission, or null when nothing was rolled.
        /// players: how many are in the mission (0: not known).
        /// </summary>
        public static List<Finding> Check(MissionPlan plan, Variants.Out gone, int players)
        {
            var found = new List<Finding>();
            var byId = new Dictionary<int, TriggerPlan>();
            var index = new Dictionary<int, int>();
            for (int i = 0; i < plan.Triggers.Count; i++) if (!byId.ContainsKey(plan.Triggers[i].Id)) { byId[plan.Triggers[i].Id] = plan.Triggers[i]; index[plan.Triggers[i].Id] = i; }

            // Can a trigger fire in this mission? Remembered per trigger; "why not" is kept for the message.
            var can = new Dictionary<int, bool>();
            var why = new Dictionary<int, string>();
            var visiting = new HashSet<int>();
            // The engine's own circles: a trigger in one never fires, even when its "any" list has another way in.
            var ringOf = new Dictionary<int, List<int>>();
            foreach (List<int> ring in TriggerEngine.Rings(plan.Triggers)) foreach (int id in ring) ringOf[id] = ring;
            string Name(TriggerPlan t) => "trigger " + t.Id + (string.IsNullOrEmpty(t.Name) ? "" : " '" + t.Name + "'");
            bool Reach(SpawnPlan s) => s.Trigger == 0 || (byId.TryGetValue(s.Trigger, out TriggerPlan bringer) && Can(bringer));
            bool Can(TriggerPlan t)
            {
                if (can.TryGetValue(t.Id, out bool known)) return known;
                if (!visiting.Add(t.Id)) { why[t.Id] = Name(t) + " waits for itself through other triggers"; return false; }
                bool ok = true; string no = "";
                if (t.Off) { ok = false; no = Name(t) + " sits this mission out (its chance is " + t.Chance + "%)"; }
                else if (ringOf.TryGetValue(t.Id, out List<int> circle))
                {
                    ok = false;
                    no = circle.Count == 1 ? Name(t) + " waits for itself"
                       : Name(t) + " waits for itself through other triggers: triggers " + string.Join(", ", circle) + " wait for each other in a circle, and the game never fires them";
                }
                else switch (t.When)
                {
                    case TriggerPlan.OnAfter:
                        if (t.After == 0) { ok = plan.GameMaster; no = Name(t) + " is fired by hand, and the scenario does not say that it needs a Game Master"; }
                        else if (t.After == t.Id) { ok = false; no = Name(t) + " waits for itself"; }
                        else if (!byId.TryGetValue(t.After, out TriggerPlan before)) { ok = false; no = Name(t) + " waits for trigger " + t.After + ", which does not exist"; }
                        else if (!Can(before)) { ok = false; no = Name(t) + " waits for it: " + why[before.Id]; }
                        break;
                    case TriggerPlan.OnOutcome:
                        if (t.After == 0) { ok = false; no = Name(t) + " watches no trigger"; }
                        else if (t.After == t.Id) { ok = false; no = Name(t) + " waits for itself"; }
                        else if (!byId.TryGetValue(t.After, out TriggerPlan watched)) { ok = false; no = Name(t) + " waits for trigger " + t.After + ", which does not exist"; }
                        else if (!Can(watched)) { ok = false; no = Name(t) + " waits for it: " + why[watched.Id]; }
                        else
                        {
                            string never = TriggerPlan.NeverSeen(watched, Enemies(plan, watched.Id), t.Outcome);
                            if (never != "") { ok = false; no = Unseen(t, watched, never); }
                        }
                        break;
                    case TriggerPlan.OnAll:
                        if (t.Of.Count == 0) { ok = false; no = Name(t) + " waits for a list of triggers that is empty"; }
                        foreach (int id in t.Of)
                        {
                            if (!ok) break;
                            if (!byId.TryGetValue(id, out TriggerPlan one)) { ok = false; no = Name(t) + " waits for trigger " + id + ", which does not exist"; }
                            else if (!Can(one)) { ok = false; no = Name(t) + " needs all of its list: " + why[one.Id]; }
                        }
                        break;
                    case TriggerPlan.OnAny:
                        ok = false; no = Name(t) + " has no trigger in its list that can fire";
                        foreach (int id in t.Of) if (byId.TryGetValue(id, out TriggerPlan one) && Can(one)) { ok = true; break; }
                        break;
                    case TriggerPlan.OnDown:
                        if (!string.IsNullOrEmpty(t.Tag))
                        {
                            int tagged = 0, reachable = 0;
                            foreach (SpawnPlan s in plan.Spawns)
                                if (string.Equals(s.Tag, t.Tag, StringComparison.OrdinalIgnoreCase)) { tagged++; if (Reach(s)) reachable++; }
                            if (tagged == 0) { ok = false; no = Name(t) + " waits for enemies tagged '" + t.Tag + "', and none is in this mission"; }
                            else if (t.Count <= 0 && reachable < tagged) { ok = false; no = Name(t) + " waits for every enemy tagged '" + t.Tag + "', and " + (tagged - reachable) + " of them can never appear"; }
                            else if (t.Count > reachable) { ok = false; no = Name(t) + " waits for " + t.Count + " enemies tagged '" + t.Tag + "', and only " + reachable + " can appear"; }
                        }
                        else if (t.Count > plan.Spawns.Count) { ok = false; no = Name(t) + " waits for " + t.Count + " enemies down, and the mission has " + plan.Spawns.Count; }
                        break;
                }
                visiting.Remove(t.Id);
                can[t.Id] = ok; if (!ok) why[t.Id] = no;
                return ok;
            }

            // 1. The extraction. The scenario holds it shut until one trigger has fired; any trigger that "opens the extraction" is another way out.
            var blocked = new HashSet<int>();      // triggers the extraction waits for, when that is what stops everyone leaving
            if (plan.ExtractionAfter != 0)
            {
                if (!byId.TryGetValue(plan.ExtractionAfter, out TriggerPlan gate))
                    found.Add(new Finding { Level = Finding.Note, Path = "extraction.after", Text = "the extraction waits for trigger " + plan.ExtractionAfter + ", which does not exist: the game's own rule is used" });
                else if (!Can(gate))
                {
                    TriggerPlan other = plan.Triggers.Find(t => t.OpensExtraction && Can(t));
                    if (other == null)
                    {
                        found.Add(new Finding { Path = "triggers[" + index[gate.Id] + "]", Text = "nobody could leave: the extraction waits for " + Name(gate) + ", and " + why[gate.Id] + ". No other trigger opens the extraction" });
                        var walk = new Stack<int>();
                        walk.Push(gate.Id);
                        while (walk.Count > 0)
                        {
                            int id = walk.Pop();
                            if (!blocked.Add(id) || !byId.TryGetValue(id, out TriggerPlan t)) continue;
                            if ((t.When == TriggerPlan.OnAfter || t.When == TriggerPlan.OnOutcome) && t.After != 0) walk.Push(t.After);
                            if (t.When == TriggerPlan.OnAll || t.When == TriggerPlan.OnAny) foreach (int one in t.Of) walk.Push(one);
                        }
                    }
                }
            }

            // An outcome trigger that waits for what its trigger can never be seen to do never fires. Where that is not already
            // why nobody can leave, it is said, so the author knows the branch is dead.
            for (int i = 0; i < plan.Triggers.Count; i++)
            {
                TriggerPlan t = plan.Triggers[i];
                if (t.When != TriggerPlan.OnOutcome || t.Off || blocked.Contains(t.Id) || !byId.TryGetValue(t.After, out TriggerPlan watched) || watched.Id == t.Id) continue;
                string never = TriggerPlan.NeverSeen(watched, Enemies(plan, watched.Id), t.Outcome);
                if (never != "") found.Add(new Finding { Level = Finding.Note, Code = UnseenOutcome, Path = "triggers[" + i + "].outcome", Text = Unseen(t, watched, never) });
            }

            // 2. The goals (ML-046). A circle among "after" lists, or a required goal that can never succeed, blocks the mission
            // only when the extraction waits for the goals and no trigger that can fire opens it another way; otherwise it is said.
            if (plan.Goals.Count > 0 || plan.ExtractionGoals) Goals(plan, found, byId, Can, why, Name);

            // 3. The scenario's own target person, taken out by a roll or for want of players.
            if (gone != null)
            {
                bool left = plan.Spawns.Exists(s => s.Hvt && s.Trigger == 0);
                for (int i = 0; i < gone.Spawns.Count && !left; i++)
                {
                    SpawnPlan s = gone.Spawns[i];
                    if (!s.Hvt) continue;
                    int at = i < gone.SpawnIndex.Count ? gone.SpawnIndex[i] : -1;
                    string field = players > 0 && s.MinPlayers > players ? "minPlayers" : s.Either > 0 && s.Chance >= 100 ? "either" : s.Chance < 100 ? "chance" : s.Either > 0 ? "either" : "chance";
                    string how = field == "minPlayers" ? "it needs " + s.MinPlayers + " players and " + players + (players == 1 ? " is" : " are") + " there"
                               : field == "either" ? "either/or set " + s.Either + " went to another option" : "it lost its " + s.Chance + "% chance";
                    found.Add(new Finding { Path = "spawns[" + (at < 0 ? "?" : at.ToString()) + "]." + field,
                                            Text = "the target person at (" + s.Pos.X.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + ", " + s.Pos.Z.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + ") is not in this mission: " + how });
                }
            }
            return found;
        }

        public const string UnseenOutcome = "unseen-outcome";

        /// <summary>A trigger's own enemies in the mission as checked, without the target person and gun trucks (never re-made).</summary>
        public static int Enemies(MissionPlan plan, int trigger)
        {
            int n = 0;
            foreach (SpawnPlan s in plan.Spawns) if (s.Trigger == trigger && !s.Hvt && !s.Vehicle) n++;
            return n;
        }

        private static string Unseen(TriggerPlan t, TriggerPlan watched, string never)
        {
            string Name(TriggerPlan x) => "trigger " + x.Id + (string.IsNullOrEmpty(x.Name) ? "" : " '" + x.Name + "'");
            return Name(t) + " waits for " + Name(watched) + " to be seen to " + (t.Outcome == TriggerPlan.Partial ? "take in part" : "take") + ", but " + never + ", so it can never fire";
        }

        private static void Goals(MissionPlan plan, List<Finding> found, Dictionary<int, TriggerPlan> byId, Func<TriggerPlan, bool> can, Dictionary<int, string> why, Func<TriggerPlan, string> name)
        {
            bool waits = plan.ExtractionGoals && !plan.Triggers.Exists(t => t.OpensExtraction && can(t));
            string level = waits ? Finding.Error : Finding.Note;
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Goals.Count; i++) if (!index.ContainsKey(plan.Goals[i].Id)) index[plan.Goals[i].Id] = i;
            string Goal(GoalPlan g) => "goal '" + g.Name + "' (" + g.Id + ")";

            var inRing = new HashSet<string>(StringComparer.Ordinal);
            foreach (List<string> ring in GoalBook.Rings(plan.Goals))
            {
                foreach (string id in ring) inRing.Add(id);
                GoalPlan first = plan.Goals[index[ring[0]]];
                found.Add(new Finding { Level = level, Path = "goals[" + index[ring[0]] + "].after",
                                        Text = (ring.Count == 1 ? Goal(first) + " waits for itself" : "goals " + string.Join(", ", ring) + " wait for each other in a circle") + ", so " + (ring.Count == 1 ? "it never becomes" : "none of them ever becomes") + " active" });
            }

            // Can a goal ever succeed, and if not, where in the file and why. Goals in a circle never can; that is said above.
            var able = new Dictionary<string, bool>(StringComparer.Ordinal);
            var whyNot = new Dictionary<string, string>(StringComparer.Ordinal);
            var whereNot = new Dictionary<string, string>(StringComparer.Ordinal);
            bool Trigger(int id, out string no)
            {
                no = "";
                if (!byId.TryGetValue(id, out TriggerPlan t)) { no = "trigger " + id + " does not exist"; return false; }
                if (can(t)) return true;
                no = why.TryGetValue(id, out string w) ? w : name(t) + " can never fire";
                return false;
            }
            bool Able(GoalPlan g)
            {
                if (able.TryGetValue(g.Id, out bool known)) return known;
                able[g.Id] = false;               // a goal met again while it is being judged stands in a circle
                int at = index[g.Id];
                bool ok = true; string no = "", where = "";
                if (inRing.Contains(g.Id)) { ok = false; no = Goal(g) + " stands in a circle of goals"; where = "goals[" + at + "].after"; }
                for (int j = 0; ok && j < g.After.Count; j++)
                {
                    GoalPlan before = index.TryGetValue(g.After[j], out int b) ? plan.Goals[b] : null;
                    if (before == null) { ok = false; no = Goal(g) + " waits for goal " + g.After[j] + ", which does not exist"; }
                    else if (!Able(before)) { ok = false; no = Goal(g) + " waits for " + Goal(before) + ", which can never succeed: " + whyNot[before.Id]; }
                    if (!ok) where = "goals[" + at + "].after[" + j + "]";
                }
                if (ok && g.Activate != 0 && !Trigger(g.Activate, out string na)) { ok = false; no = Goal(g) + " never becomes active: " + na; where = "goals[" + at + "].activate"; }
                if (ok && !Trigger(g.Succeed, out string ns)) { ok = false; no = Goal(g) + " can never succeed: " + ns; where = "goals[" + at + "].succeed"; }
                able[g.Id] = ok;
                if (!ok) { whyNot[g.Id] = no; whereNot[g.Id] = where; }
                return ok;
            }
            foreach (GoalPlan g in plan.Goals)
                if (g.Required && !inRing.Contains(g.Id) && !Able(g))
                    found.Add(new Finding { Level = level, Path = whereNot[g.Id], Text = whyNot[g.Id] + (waits ? ". The extraction waits for every required objective, and no trigger opens it another way" : "") });

            // What the extraction waits for, in the words the run uses (GoalBook.Holding): before the mission, every required goal.
            if (plan.ExtractionGoals)
            {
                var book = new GoalBook();
                book.Sync(plan.Goals);
                string held = book.Holding();
                found.Add(new Finding { Level = Finding.Note, Path = "extraction.goals", Text = held == "" ? "the extraction waits for the required objectives, and the scenario has none, so nothing holds it" : held });
            }
        }
    }
}
