using System;
using System.Collections.Generic;

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
        public bool EverFired => Fires > 0;
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
        /// one that comes back under the same number later, or as another kind, starts clean. Everything else keeps its state.
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
                if (!_state.TryGetValue(t.Id, out TriggerState s) || s.Family != family)
                {
                    s = new TriggerState { Family = family };
                    if (addedAt >= 0f && t.When == TriggerPlan.OnTime) s.MetAt = addedAt;
                    _state[t.Id] = s;
                }
            }
            foreach (int id in new List<int>(_state.Keys)) if (!seen.Contains(id)) _state.Remove(id);
            Order(triggers);
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
            s.Fires++; s.LastFiredAt = now; s.Latched = true; s.ByHand = byHand;
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
    }
}
