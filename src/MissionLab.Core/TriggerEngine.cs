using System;
using System.Collections.Generic;
using System.Globalization;

namespace MissionLab.Core
{
    /// <summary>What the triggers ask of the world. The game answers in the plugin; a test or a rehearsal answers from a script.</summary>
    public interface ITriggerWorld
    {
        bool Alerted();
        bool IntelTaken(string item);
        /// <summary>Zone and hold: is a player inside (every living player, when the trigger asks for everyone)?</summary>
        bool Occupied(TriggerPlan t, out V3 where);
        /// <summary>Have that many enemies gone down (0: all of them)? With a tag: only enemies that carry it.</summary>
        bool Down(int count, string tag);
        bool DoorOpen(V3 hinge);
        /// <summary>Zone and hold: how many living players stand inside the trigger's place (Membership.Inside), and how many are alive. Only asked to explain a trigger.</summary>
        int Inside(TriggerPlan t, out int alive);
    }

    /// <summary>
    /// Everything the engine remembers about one trigger in one mission. History (how often it fired, when) is kept
    /// apart from the repeat state (latched, armed again at), so that arming a trigger again never erases that it fired.
    /// </summary>
    public sealed class TriggerState
    {
        public string Family = "";       // the kind of trigger this state belongs to; a trigger re-made as another kind starts clean
        public int Fires;                // how often it has fired this mission
        public float? LastFiredAt;
        public bool Latched;             // it has fired and is not armed again
        public float? RearmAt;           // it may fire again from this time
        public float? MetAt;             // its condition was met then; its wait runs from there
        public float? InsideSince;       // hold: a player has been inside since then
        public bool HasWhere; public V3 Where;   // where the player stood who set it off
        public int Told;                 // countdown steps already told, as bits
        public bool ByHand;              // its last firing was by hand
        public string Shape = "";        // zone and hold: the place as last read (TriggerEngine.ShapeOf); a moved or reshaped place starts its count again
        public string Outcome = "";      // what became of its last firing's actions: "", applied, failed or unconfirmed (told by the runtime through Report)
        public string OutcomeWhy = "";
        public bool EverFired => Fires > 0;
    }

    /// <summary>Where one trigger stands and why, in words a mission author can act on. Made by TriggerEngine.Explain.</summary>
    public sealed class TriggerView
    {
        public int Id;
        public string Phase;             // one of the engine's phase words: off, circle, waiting, holding, counting, fired, cooling, exhausted
        public float Left;               // seconds, where the phase has a count (counting, holding, cooling)
        public string Why = "";          // one plain sentence with the real values
        public int Fires;
        public int Times;
        public List<int> Waiting = new List<int>();     // triggers it waits for that have not fired yet
        public List<int> Excluded = new List<int>();    // triggers it waits for that sat this mission out
        public int Inside = -1, Alive = -1;             // zone and hold: living players inside, and alive (-1: not asked)
        public string Outcome = "", OutcomeWhy = "";
    }

    public struct TriggerEvent
    {
        public const int Fire = 0, Warn = 1;
        // Countdown steps. The numbers are those of the first version and stay, for the log.
        public const int OneMinute = 1, TenSeconds = 2, FiveMinutes = 3, Started = 4;
        public int Kind, Id, Step;
        public float Left;               // Warn: seconds until it fires
        public bool Again;               // Fire: it had fired before
        public bool ByHand;
    }

    /// <summary>
    /// Decides which triggers fire, and when. It holds no game objects and reads no clock of its own: the caller passes
    /// the time and a world to ask. The plugin applies what a firing does; a test only looks at the decisions.
    ///
    /// The rules, in short:
    /// - A trigger's condition is met once, then its wait (delay) runs, then it fires. A timer's wait runs from the mission's start.
    /// - "after", "all" and "any" ask whether another trigger has EVER fired this mission. A repeat does not take that back.
    /// - A trigger with a repeat is armed again that many seconds after it fired, at most "times" firings in all. A timer
    ///   then fires at once; every other kind waits for its condition (and its delay) again.
    /// - Fire by hand fires a trigger that is not latched, whatever its condition. It counts as a firing like any other.
    /// - Triggers that wait for each other in a circle never fire; they are listed in Problems.
    /// - A zone or hold whose place changes during a mission (moved, resized, reshaped, everyone switched) loses only its
    ///   unfinished progress: a wait not yet over, a hold under way, the place it was set off from and the countdown steps told.
    ///   Its history (how often it fired, when, latched, armed again at) stays, because a moved place does not undo a firing.
    /// - The runtime tells the engine what became of a firing's actions (Report). A firing counts whatever that outcome is;
    ///   the outcome is only there to tell "it fired but its actions failed" apart from "its condition is not met".
    /// - Explain says where a trigger stands and why, in one sentence. It only reads: asking never changes a trigger.
    /// - The order of the list does not matter: a trigger is always looked at after the ones it waits for.
    /// </summary>
    public sealed class TriggerEngine
    {
        public float Start;                                          // timers count from here
        public readonly List<string> Problems = new List<string>();  // circles, found when the list was last read
        private List<TriggerPlan> _order = new List<TriggerPlan>();
        private readonly Dictionary<int, TriggerPlan> _byId = new Dictionary<int, TriggerPlan>();
        private readonly Dictionary<int, TriggerState> _state = new Dictionary<int, TriggerState>();
        private readonly HashSet<int> _circle = new HashSet<int>();

        /// <summary>A new mission: nothing is remembered.</summary>
        public void Reset(float start)
        {
            Start = start; _order = new List<TriggerPlan>(); _byId.Clear(); _state.Clear(); _circle.Clear(); Problems.Clear();
        }

        private static string FamilyOf(TriggerPlan t) => t.When == TriggerPlan.OnHold ? TriggerPlan.OnZone : (t.When ?? "");

        /// <summary>
        /// Reads the list of triggers: at the start of a mission and whenever it changes. A trigger that is gone is forgotten;
        /// one that comes back under the same number later, or as another kind, starts clean. Everything else keeps its state,
        /// except that a zone or hold whose place changed loses its unfinished progress (see the class comment).
        /// addedAt: when the list changes during a mission, the time of the change: a timer added then counts from there.
        /// </summary>
        public void Sync(IList<TriggerPlan> triggers, float addedAt = -1f)
        {
            var seen = new HashSet<int>();
            _byId.Clear();
            foreach (TriggerPlan t in triggers)
            {
                if (!seen.Add(t.Id)) continue;                       // a second trigger with the same number is ignored
                _byId[t.Id] = t;
                string family = FamilyOf(t);
                string shape = TriggerPlan.Placed(t.When) ? ShapeOf(t) : "";
                if (!_state.TryGetValue(t.Id, out TriggerState s) || s.Family != family)
                {
                    s = new TriggerState { Family = family };
                    if (addedAt >= 0f && t.When == TriggerPlan.OnTime) s.MetAt = addedAt;
                    _state[t.Id] = s;
                }
                else if (s.Shape != shape)
                {
                    // The place is not where the players were counted: what was under way there no longer stands. Its firings do.
                    if (!s.Latched) s.MetAt = null;
                    s.InsideSince = null; s.HasWhere = false; s.Where = default; s.Told = 0;
                }
                s.Shape = shape;
            }
            foreach (int id in new List<int>(_state.Keys)) if (!seen.Contains(id)) _state.Remove(id);
            Order(triggers);
        }

        /// <summary>A zone's or hold's place as text, to notice that it moved or changed shape: anchor, radius, everyone and the area.</summary>
        public static string ShapeOf(TriggerPlan t)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string F(float f) => Math.Round(f, 2).ToString(inv);
            return F(t.Pos.X) + "," + F(t.Pos.Y) + "," + F(t.Pos.Z) + "|" + F(t.Radius) + "|" + (t.Everyone ? "everyone" : "anyone") + "|" + (t.Area?.Signature() ?? "");
        }

        private static IEnumerable<int> Needs(TriggerPlan t)
        {
            if (t.When == TriggerPlan.OnAfter && t.After != 0) yield return t.After;
            if (t.When == TriggerPlan.OnAll || t.When == TriggerPlan.OnAny) foreach (int id in t.Of) yield return id;
        }

        // Those a trigger waits for come first. Circles are found on the way.
        private void Order(IList<TriggerPlan> triggers)
        {
            _order = new List<TriggerPlan>(); _circle.Clear(); Problems.Clear();
            var mark = new Dictionary<int, int>();                   // 1: being visited, 2: done
            var path = new List<int>();
            void Visit(TriggerPlan t)
            {
                if (mark.TryGetValue(t.Id, out int m))
                {
                    if (m == 1)
                    {
                        int from = path.IndexOf(t.Id);
                        var ring = path.GetRange(from, path.Count - from);
                        bool fresh = false;
                        foreach (int id in ring) fresh |= _circle.Add(id);
                        if (fresh) Problems.Add(ring.Count == 1 ? "trigger " + ring[0] + " waits for itself and can never fire"
                                                                 : "triggers " + string.Join(", ", ring) + " wait for each other in a circle and can never fire");
                    }
                    return;
                }
                mark[t.Id] = 1; path.Add(t.Id);
                foreach (int need in Needs(t)) if (_byId.TryGetValue(need, out TriggerPlan other)) Visit(other);
                path.RemoveAt(path.Count - 1); mark[t.Id] = 2;
                _order.Add(t);
            }
            var done = new HashSet<int>();
            foreach (TriggerPlan t in triggers) if (done.Add(t.Id)) Visit(t);
        }

        public bool Known(int id) => _byId.ContainsKey(id);
        public bool EverFired(int id) => _state.TryGetValue(id, out TriggerState s) && s.Fires > 0;
        public int Fires(int id) => _state.TryGetValue(id, out TriggerState s) ? s.Fires : 0;
        public TriggerState State(int id) => _state.TryGetValue(id, out TriggerState s) ? s : null;
        public bool InCircle(int id) => _circle.Contains(id);
        public IEnumerable<int> FiredIds() { foreach (KeyValuePair<int, TriggerState> p in _state) if (p.Value.Fires > 0) yield return p.Key; }

        /// <summary>One look at every trigger. Returns what was decided, in the order it should happen.</summary>
        public List<TriggerEvent> Step(float now, ITriggerWorld world)
        {
            var events = new List<TriggerEvent>();
            foreach (TriggerPlan t in _order)
            {
                if (t.Off || _circle.Contains(t.Id)) continue;
                TriggerState s = _state[t.Id];
                if (s.Latched)
                {
                    // Once is enough, unless it repeats: then it is armed again when its pause is over.
                    if (s.RearmAt == null || now < s.RearmAt.Value) continue;
                    s.Latched = false; s.RearmAt = null; s.Told = 0; s.InsideSince = null;
                    if (t.When == TriggerPlan.OnTime) { s.MetAt = now - t.Delay; DoFire(t, s, now, events, false); continue; }
                    s.MetAt = null;
                }
                if (s.MetAt == null)
                {
                    V3 where = default;
                    bool yes;
                    switch (t.When)
                    {
                        case TriggerPlan.OnAlert: yes = world.Alerted(); break;
                        case TriggerPlan.OnIntel: yes = world.IntelTaken(t.Item); break;
                        case TriggerPlan.OnTime: yes = true; break;
                        case TriggerPlan.OnAfter: yes = t.After != 0 && t.After != t.Id && EverFired(t.After); break;
                        case TriggerPlan.OnHold: yes = Held(t, s, now, world, out where); break;
                        case TriggerPlan.OnDown: yes = world.Down(t.Count, t.Tag ?? ""); break;
                        case TriggerPlan.OnDoor: yes = world.DoorOpen(t.Pos); break;
                        case TriggerPlan.OnAll: yes = t.Of.Count > 0 && t.Of.TrueForAll(EverFired); break;
                        case TriggerPlan.OnAny: yes = t.Of.Exists(EverFired); break;
                        default: yes = world.Occupied(t, out where); break;
                    }
                    if (!yes) continue;
                    s.MetAt = t.When == TriggerPlan.OnTime ? Start : now;      // a timer counts from the start of the mission
                    if (TriggerPlan.Placed(t.When)) { s.Where = where; s.HasWhere = true; }
                }
                float left = s.MetAt.Value + t.Delay - now;
                if (left > 0f)
                {
                    if (t.Warn)
                    {
                        // The start is told a few seconds in, so that it does not land on top of the message of whatever started the clock.
                        if (t.Delay >= 20f && t.When != TriggerPlan.OnTime && now - s.MetAt.Value >= 4f && left > 15f) Tell(t, s, TriggerEvent.Started, left, events);
                        if (t.Delay >= 360f && left <= 300f && left > 290f) Tell(t, s, TriggerEvent.FiveMinutes, left, events);
                        if (t.Delay >= 90f && left <= 60f && left > 50f) Tell(t, s, TriggerEvent.OneMinute, left, events);
                        if (t.Delay >= 20f && left <= 10f) Tell(t, s, TriggerEvent.TenSeconds, left, events);
                    }
                    continue;
                }
                DoFire(t, s, now, events, false);
            }
            return events;
        }

        private static void Tell(TriggerPlan t, TriggerState s, int step, float left, List<TriggerEvent> events)
        {
            int bit = 1 << step;
            if ((s.Told & bit) != 0) return;
            s.Told |= bit;
            events.Add(new TriggerEvent { Kind = TriggerEvent.Warn, Id = t.Id, Step = step, Left = left });
        }

        // A player has stayed in the zone long enough. Leaving it starts the count again.
        private static bool Held(TriggerPlan t, TriggerState s, float now, ITriggerWorld world, out V3 where)
        {
            if (!world.Occupied(t, out where)) { s.InsideSince = null; return false; }
            s.InsideSince ??= now;
            return now - s.InsideSince.Value >= t.Hold;
        }

        private static void DoFire(TriggerPlan t, TriggerState s, float now, List<TriggerEvent> events, bool byHand)
        {
            s.Fires++; s.LastFiredAt = now; s.Latched = true; s.ByHand = byHand; s.Outcome = ""; s.OutcomeWhy = "";   // this firing's outcome is still to be told
            s.RearmAt = t.Repeat > 0f && (t.Times <= 0 || s.Fires < t.Times) ? now + t.Repeat : (float?)null;
            s.MetAt ??= now;
            events.Add(new TriggerEvent { Kind = TriggerEvent.Fire, Id = t.Id, Again = s.Fires > 1, ByHand = byHand });
        }

        /// <summary>
        /// The Game Master sets a trigger off by hand: whatever its condition, also when it sat this mission out or stands in a
        /// circle. Refused while it is latched (it has fired and is not armed again). why says so.
        /// </summary>
        public bool FireNow(int id, float now, out TriggerEvent fired, out string why)
        {
            fired = default; why = "";
            if (!_byId.TryGetValue(id, out TriggerPlan t)) { why = "there is no trigger " + id; return false; }
            TriggerState s = _state[id];
            if (s.Latched) { why = s.RearmAt != null ? "it fires again by itself in " + Math.Max(0, (int)Math.Ceiling(s.RearmAt.Value - now)) + " s" : "it has already fired"; return false; }
            var events = new List<TriggerEvent>();
            DoFire(t, s, now, events, true);
            fired = events[0];
            return true;
        }

        public const string Off = "off", Circle = "circle", Waiting = "waiting", Holding = "holding", Counting = "counting",
                            Fired = "fired", Cooling = "cooling", Exhausted = "exhausted";

        /// <summary>Where a trigger stands right now, for the page: one of the words above. left: seconds, where that means something.</summary>
        public string Phase(TriggerPlan t, float now, out float left)
        {
            left = 0f;
            if (!_state.TryGetValue(t.Id, out TriggerState s)) return Waiting;
            if (s.Latched)
            {
                if (s.RearmAt != null) { left = Math.Max(0f, s.RearmAt.Value - now); return Cooling; }
                return t.Repeat > 0f ? Exhausted : Fired;
            }
            if (t.Off) return Off;
            if (_circle.Contains(t.Id)) return Circle;
            if (s.MetAt != null) { left = Math.Max(0f, s.MetAt.Value + t.Delay - now); return Counting; }
            if (t.When == TriggerPlan.OnTime) { left = Math.Max(0f, Start + t.Delay - now); return Counting; }
            if (s.InsideSince != null) { left = Math.Max(0f, t.Hold - (now - s.InsideSince.Value)); return Holding; }
            return Waiting;
        }

        public const string Applied = "applied", Failed = "failed", Unconfirmed = "unconfirmed";

        /// <summary>
        /// The runtime says what became of a trigger's last firing: applied, failed (why says what) or unconfirmed (the call was
        /// made, but whether it took cannot be seen). The firing counts either way. An unknown trigger is ignored.
        /// </summary>
        public void Report(int id, string outcome, string why)
        {
            if (!_state.TryGetValue(id, out TriggerState s)) return;
            s.Outcome = outcome ?? ""; s.OutcomeWhy = why ?? "";
        }

        /// <summary>
        /// Where a trigger stands and why, in one plain sentence with the real values. Only reads: the trigger is not changed,
        /// and the world is only asked how many players stand inside a zone or hold (world may be null: then that is not said).
        /// </summary>
        public TriggerView Explain(TriggerPlan t, float now, ITriggerWorld world)
        {
            var v = new TriggerView { Id = t.Id, Times = t.Times };
            v.Phase = Phase(t, now, out float left);
            v.Left = left;
            _state.TryGetValue(t.Id, out TriggerState s);
            if (s != null) { v.Fires = s.Fires; v.Outcome = s.Outcome; v.OutcomeWhy = s.OutcomeWhy; }
            foreach (int need in Needs(t))
            {
                if (!EverFired(need) && !v.Waiting.Contains(need)) v.Waiting.Add(need);
                if (_byId.TryGetValue(need, out TriggerPlan other) && other.Off && !v.Excluded.Contains(need)) v.Excluded.Add(need);
            }
            if (TriggerPlan.Placed(t.When) && world != null) { v.Inside = world.Inside(t, out int alive); v.Alive = alive; }
            v.Why = WhyOf(t, s, v);
            return v;
        }

        // Whole seconds, rounded up, so that "1 s left" is said until it fires. The small margin keeps float noise from adding a second.
        private static int Secs(float f) => Math.Max(0, (int)Math.Ceiling(f - 0.01f));

        private string WhyOf(TriggerPlan t, TriggerState s, TriggerView v)
        {
            if (s == null) return "This trigger is not in the mission's list";
            switch (v.Phase)
            {
                case Off: return "Sat this mission out (its chance is " + t.Chance + "%); only Fire now sets it off";
                case Circle: return "Waits for itself through other triggers and can never fire";
                case Cooling: return FiredCount(t, s) + "; armed again in " + Secs(v.Left) + " s" + OutcomeOf(s);
                case Exhausted: return FiredCount(t, s) + "; it does not fire again" + OutcomeOf(s);
                case Fired: return "Fired " + (s.ByHand ? "by hand " : "") + Secs((s.LastFiredAt ?? Start) - Start) + " s into the mission" + OutcomeOf(s);
                case Counting: return (t.When == TriggerPlan.OnTime ? "Timer running" : "Condition met") + "; " + Secs(v.Left) + " s left";
                case Holding: return (v.Alive < 0 ? "A player is inside" : Players(t, v)) + "; " + Secs(v.Left) + " s left of the " + Secs(t.Hold) + " s hold";
            }
            switch (t.When)
            {
                case TriggerPlan.OnZone:
                case TriggerPlan.OnHold:
                    return Players(t, v);
                case TriggerPlan.OnAlert: return "Waiting for the enemy to raise the alarm";
                case TriggerPlan.OnIntel:
                {
                    string kind = ObjectivePlan.Kind(t.Item);
                    if (kind == "" || kind == "any") return "Waiting for any intel item to be picked up";
                    return kind == "all" ? "Waiting for every intel item to be picked up" : "Waiting for the " + kind + " to be picked up";
                }
                case TriggerPlan.OnDown:
                {
                    string tagged = string.IsNullOrEmpty(t.Tag) ? "" : " tagged '" + t.Tag + "'";
                    return t.Count <= 0 ? "Waiting for every enemy" + tagged + " to go down" : "Waiting for " + t.Count + (t.Count == 1 ? " enemy" : " enemies") + tagged + " to go down";
                }
                case TriggerPlan.OnDoor:
                {
                    CultureInfo inv = CultureInfo.InvariantCulture;
                    return "Waiting for the door at (" + t.Pos.X.ToString("0.#", inv) + ", " + t.Pos.Z.ToString("0.#", inv) + ") to be opened";
                }
                case TriggerPlan.OnAfter:
                    if (t.After == 0) return "Fired by hand only (Fire now)";
                    return v.Waiting.Count > 0 ? Need(v.Waiting[0]) : "Its condition is met; it fires at the next look";
                case TriggerPlan.OnAll:
                    if (t.Of.Count == 0) return "Waits for a list of triggers that is empty and can never fire";
                    foreach (int id in v.Waiting) if (Stuck(id)) return Need(id);
                    return v.Waiting.Count == 0 ? "Its condition is met; it fires at the next look"
                         : "Waiting for " + (v.Waiting.Count == 1 ? "trigger " : "triggers ") + string.Join(", ", v.Waiting);
                case TriggerPlan.OnAny:
                    if (t.Of.Count == 0) return "Waits for a list of triggers that is empty and can never fire";
                    if (v.Waiting.Count < t.Of.Count) return "Its condition is met; it fires at the next look";
                    if (v.Waiting.TrueForAll(Stuck)) return Need(v.Waiting[0]) + (v.Waiting.Count > 1 ? ", and none of the others in its list can fire either" : "");
                    return "Waiting for any of triggers " + string.Join(", ", v.Waiting);
            }
            return "Waiting for its condition";
        }

        // A trigger waited for that cannot fire by itself: it does not exist, it sat this mission out, or it stands in a circle.
        private bool Stuck(int id) => !_byId.TryGetValue(id, out TriggerPlan t) || t.Off || _circle.Contains(id);

        private string Need(int id)
        {
            if (!_byId.TryGetValue(id, out TriggerPlan t)) return "Waiting for trigger " + id + ", which does not exist";
            if (t.Off) return "Waiting for trigger " + id + ", which sat this mission out (chance)";
            if (_circle.Contains(id)) return "Waiting for trigger " + id + ", which stands in a circle and can never fire";
            return "Waiting for trigger " + id + " to fire";
        }

        private static string FiredCount(TriggerPlan t, TriggerState s) =>
            t.Times > 0 ? "Fired " + s.Fires + " of " + t.Times + " times" : s.Fires == 1 ? "Fired once" : "Fired " + s.Fires + " times";

        private static string OutcomeOf(TriggerState s)
        {
            if (s.Outcome == Failed) return ", but its actions failed" + (s.OutcomeWhy == "" ? "" : ": " + s.OutcomeWhy);
            if (s.Outcome == Unconfirmed) return "; whether its actions took could not be seen" + (s.OutcomeWhy == "" ? "" : ": " + s.OutcomeWhy);
            return "";
        }

        // Zone and hold: who stands inside, with the real numbers.
        private static string Players(TriggerPlan t, TriggerView v)
        {
            if (v.Alive < 0) return t.Everyone ? "Waiting for every living player to step inside" : "Waiting for a player to step inside";
            if (v.Alive == 0) return "No living player to step inside";
            string said = v.Inside + " of " + v.Alive + (v.Alive == 1 ? " living player" : " living players") + " inside";
            if (t.Everyone && v.Inside < v.Alive) return said + "; every one of them has to be";
            return said;
        }
    }
}
