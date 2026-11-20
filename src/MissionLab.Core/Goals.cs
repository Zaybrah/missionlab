using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MissionLab.Core
{
    /// <summary>One change of a goal's state, as GoalBook.Step returns it. At: seconds into the mission, as the caller gave it.</summary>
    public struct GoalEvent
    {
        public string Id;
        public string From, To;
        public float At;
    }

    /// <summary>
    /// Where the mission's player goals stand (ML-046): inactive, active, succeeded or failed, the last two for good. It has no
    /// clock and no world of its own: it is told the time and asked which triggers have ever fired, so the plugin, rehearsal and
    /// the tests run the same rules. A goal becomes active once every goal in its "after" has succeeded and its activate trigger
    /// (if any) has fired; an active goal fails when its fail trigger has fired and succeeds when its succeed trigger has.
    /// When both have fired by the same look, failure wins: an author who wires a fail trigger means "this can be lost", and
    /// the order in which two firings happened to be seen within one look must not decide it. Goals are looked at in
    /// dependency order and again until nothing changes, so a chain completes in one step whatever order the list is in.
    /// Goals in a circle of "after" lists never become active (Completion reports the circle).
    /// </summary>
    public sealed class GoalBook
    {
        public const string Inactive = "inactive", Active = "active", Succeeded = "succeeded", Failed = "failed";

        private readonly List<GoalPlan> _goals = new List<GoalPlan>();
        private readonly List<GoalPlan> _order = new List<GoalPlan>();
        private readonly Dictionary<string, string> _state = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The goals as last synced, in the author's order.</summary>
        public IList<GoalPlan> Goals => _goals;

        /// <summary>A new mission: every goal is inactive again.</summary>
        public void Reset() => _state.Clear();

        /// <summary>Takes the goal list. A goal that is still there keeps its state; a new one starts inactive.</summary>
        public void Sync(IList<GoalPlan> goals)
        {
            _goals.Clear();
            if (goals != null) _goals.AddRange(goals);
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (GoalPlan g in _goals) keep.Add(g.Id);
            var gone = new List<string>();
            foreach (string id in _state.Keys) if (!keep.Contains(id)) gone.Add(id);
            foreach (string id in gone) _state.Remove(id);
            _order.Clear();
            _order.AddRange(Order(_goals));
        }

        public string State(string id) => id != null && _state.TryGetValue(id, out string s) ? s : Inactive;

        public GoalPlan Find(string id) => _goals.Find(g => g.Id == id);

        /// <summary>
        /// Moves goals on. everFired: whether a trigger has fired at least once in this mission. Returns every transition, each
        /// exactly once; a goal that went inactive -> active -> succeeded in this look gives two events.
        /// </summary>
        public List<GoalEvent> Step(float now, Func<int, bool> everFired)
        {
            var events = new List<GoalEvent>();
            bool Fired(int id) => id > 0 && everFired(id);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (GoalPlan g in _order)
                {
                    string s = State(g.Id);
                    if (s == Inactive)
                    {
                        if (!g.After.TrueForAll(a => State(a) == Succeeded)) continue;
                        if (g.Activate != 0 && !Fired(g.Activate)) continue;
                        Move(g, s, Active, now, events); s = Active; changed = true;
                    }
                    if (s == Active)
                    {
                        if (Fired(g.Fail)) { Move(g, s, Failed, now, events); changed = true; }
                        else if (Fired(g.Succeed)) { Move(g, s, Succeeded, now, events); changed = true; }
                    }
                }
            }
            return events;
        }

        private void Move(GoalPlan g, string from, string to, float now, List<GoalEvent> events)
        {
            _state[g.Id] = to;
            events.Add(new GoalEvent { Id = g.Id, From = from, To = to, At = now });
        }

        /// <summary>The goals that have not succeeded yet, in the author's order (required: only required ones).</summary>
        public List<GoalPlan> Remaining(bool required)
        {
            var left = new List<GoalPlan>();
            foreach (GoalPlan g in _goals) if ((!required || g.Required) && State(g.Id) != Succeeded) left.Add(g);
            return left;
        }

        /// <summary>The goals named for people: "'Recover the phone' (failed), 'Secure the upper floor'". A goal's state is said unless it is still to do.</summary>
        public string Words(List<GoalPlan> goals)
        {
            var parts = new List<string>();
            foreach (GoalPlan g in goals)
            {
                string s = State(g.Id);
                parts.Add("'" + g.Name + "'" + (s == Failed ? " (failed)" : s == Inactive ? " (not yet active)" : ""));
            }
            return string.Join(", ", parts);
        }

        /// <summary>Why an extraction that waits for goals is held, or "" when every required goal has succeeded.</summary>
        public string Holding()
        {
            List<GoalPlan> left = Remaining(true);
            return left.Count == 0 ? "" : "the extraction waits for " + (left.Count == 1 ? "the objective " : "the objectives ") + Words(left);
        }

        /// <summary>{"phone":"succeeded","upper":"active"} in the author's order, for run records and the page.</summary>
        public string StatesJson()
        {
            var sb = new StringBuilder(64);
            sb.Append('{');
            for (int i = 0; i < _goals.Count; i++) sb.Append(i > 0 ? "," : "").Append('"').Append(Guard.Esc(_goals[i].Id)).Append("\":\"").Append(State(_goals[i].Id)).Append('"');
            return sb.Append('}').ToString();
        }

        /// <summary>The live board for the page: [{"id","name","required","state"}] in the author's order.</summary>
        public string BoardJson()
        {
            var sb = new StringBuilder(128);
            sb.Append('[');
            for (int i = 0; i < _goals.Count; i++)
            {
                GoalPlan g = _goals[i];
                sb.Append(i > 0 ? "," : "").Append("{\"id\":\"").Append(Guard.Esc(g.Id)).Append("\",\"name\":\"").Append(Guard.Esc(g.Name))
                  .Append("\",\"required\":").Append(g.Required ? "true" : "false").Append(",\"state\":\"").Append(State(g.Id)).Append("\"}");
            }
            return sb.Append(']').ToString();
        }

        /// <summary>
        /// The goals in dependency order: a goal comes after every goal it names in "after", ties go by position in the list.
        /// Goals in a circle, and goals that wait for one, are left out: they can never become active.
        /// </summary>
        public static List<GoalPlan> Order(IList<GoalPlan> goals)
        {
            var order = new List<GoalPlan>();
            var placed = new HashSet<string>(StringComparer.Ordinal);
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (GoalPlan g in goals) known.Add(g.Id);
            bool added = true;
            while (added)
            {
                added = false;
                foreach (GoalPlan g in goals)
                {
                    if (placed.Contains(g.Id)) continue;
                    // A name that is no goal can never succeed; the lenient reader drops it, the strict one refuses it.
                    if (!g.After.TrueForAll(a => known.Contains(a) && placed.Contains(a))) continue;
                    order.Add(g); placed.Add(g.Id); added = true;
                    break;                    // start again from the top, so ties go by position in the list
                }
            }
            return order;
        }

        /// <summary>
        /// The circles among goals' "after" lists: each one the ids of the goals in it, in list order. A goal that waits for a
        /// circle without being in one is not listed.
        /// </summary>
        public static List<List<string>> Rings(IList<GoalPlan> goals)
        {
            var byId = new Dictionary<string, GoalPlan>(StringComparer.Ordinal);
            foreach (GoalPlan g in goals) if (!byId.ContainsKey(g.Id)) byId[g.Id] = g;
            // Goal a reaches goal b when b is in a's after list, directly or through others. A goal in a circle reaches itself.
            bool Reaches(string from, string to)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var stack = new Stack<string>();
                stack.Push(from);
                while (stack.Count > 0)
                {
                    string at = stack.Pop();
                    if (!byId.TryGetValue(at, out GoalPlan g)) continue;
                    foreach (string a in g.After)
                    {
                        if (a == to) return true;
                        if (seen.Add(a)) stack.Push(a);
                    }
                }
                return false;
            }
            var rings = new List<List<string>>();
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (GoalPlan g in goals)
            {
                if (done.Contains(g.Id) || !Reaches(g.Id, g.Id)) continue;
                var ring = new List<string>();
                foreach (GoalPlan o in goals)
                    if (!done.Contains(o.Id) && (o.Id == g.Id || (Reaches(g.Id, o.Id) && Reaches(o.Id, g.Id)))) { ring.Add(o.Id); done.Add(o.Id); }
                rings.Add(ring);
            }
            return rings;
        }

        /// <summary>One transition in words, for logs and replays: "goal 'Recover the phone' (phone): active -> succeeded at 42 s".</summary>
        public string Say(GoalEvent e)
        {
            GoalPlan g = Find(e.Id);
            return "goal '" + (g == null ? e.Id : g.Name) + "' (" + e.Id + "): " + e.From + " -> " + e.To + " at " + e.At.ToString("0.#", CultureInfo.InvariantCulture) + " s";
        }

        /// <summary>The on-screen notice for a transition, or null when players are not told: (text, red).</summary>
        public static string Notice(GoalPlan g, GoalEvent e, out bool red)
        {
            red = false;
            if (g == null || !g.Notify) return null;
            if (e.To == Active) return g.FromStart ? null : "New objective: " + g.Name;
            if (e.To == Succeeded) return "Objective complete: " + g.Name;
            if (e.To == Failed) { red = true; return "Objective failed: " + g.Name; }
            return null;
        }
    }
}
