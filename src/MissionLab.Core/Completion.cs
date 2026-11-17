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
            string Name(TriggerPlan t) => "trigger " + t.Id + (string.IsNullOrEmpty(t.Name) ? "" : " '" + t.Name + "'");
            bool Reach(SpawnPlan s) => s.Trigger == 0 || (byId.TryGetValue(s.Trigger, out TriggerPlan bringer) && Can(bringer));
            bool Can(TriggerPlan t)
            {
                if (can.TryGetValue(t.Id, out bool known)) return known;
                if (!visiting.Add(t.Id)) { why[t.Id] = Name(t) + " waits for itself through other triggers"; return false; }
                bool ok = true; string no = "";
                if (t.Off) { ok = false; no = Name(t) + " sits this mission out (its chance is " + t.Chance + "%)"; }
                else switch (t.When)
                {
                    case TriggerPlan.OnAfter:
                        if (t.After == 0) { ok = plan.GameMaster; no = Name(t) + " is fired by hand, and the scenario does not say that it needs a Game Master"; }
                        else if (t.After == t.Id) { ok = false; no = Name(t) + " waits for itself"; }
                        else if (!byId.TryGetValue(t.After, out TriggerPlan before)) { ok = false; no = Name(t) + " waits for trigger " + t.After + ", which does not exist"; }
                        else if (!Can(before)) { ok = false; no = Name(t) + " waits for it: " + why[before.Id]; }
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
            if (plan.ExtractionAfter != 0)
            {
                if (!byId.TryGetValue(plan.ExtractionAfter, out TriggerPlan gate))
                    found.Add(new Finding { Level = Finding.Note, Path = "extraction.after", Text = "the extraction waits for trigger " + plan.ExtractionAfter + ", which does not exist: the game's own rule is used" });
                else if (!Can(gate))
                {
                    TriggerPlan other = plan.Triggers.Find(t => t.OpensExtraction && Can(t));
                    if (other == null)
                        found.Add(new Finding { Path = "triggers[" + index[gate.Id] + "]", Text = "nobody could leave: the extraction waits for " + Name(gate) + ", and " + why[gate.Id] + ". No other trigger opens the extraction" });
                }
            }

            // 2. The scenario's own target person, taken out by a roll or for want of players.
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
    }
}
