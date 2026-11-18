using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MissionLab.Core
{
    /// <summary>
    /// The Game Master's budget for one mission (ML-025): the author's actions, what each costs, how long each rests after use,
    /// and the points that come back over time. It only decides and keeps count; the plugin asks it before firing anything and
    /// tells it what came of the firing. The clock is handed in on every call (seconds, never going back), so tests run it on a
    /// plain number.
    ///   - Points are a float: regen adds a fraction every second. They never rise above the starting budget.
    ///   - Spend is idempotent per command id: the page may send the same request twice (a retry after a lost answer) and it is
    ///     paid for once. An empty id is never matched, so it always pays.
    ///   - Refund gives the points back and lifts the action's rest when the game refused the firing, could not confirm it, or
    ///     the request was cancelled. A refund happens once per command id.
    /// </summary>
    public sealed class Budget
    {
        public const int Keep = 200;            // spends remembered for retries and refunds; the ledger of the browser link keeps as many
        public const int Shown = 20;            // the newest spends listed in the snapshot

        private sealed class Spent { public string Command = "", Action = ""; public float At; public int Cost; public bool Refunded; }

        public readonly int Initial;            // the starting budget, also the ceiling for regen and refunds
        public readonly int Regen;              // points per minute
        public readonly List<ActionPlan> Actions = new List<ActionPlan>();
        /// <summary>Asked before an action may fire: "" when its trigger can be fired now, otherwise why not (no such trigger, it has already fired). Null: not asked.</summary>
        public readonly Func<int, string> TriggerWhy;

        private float _points;
        private float _clock = float.NaN;       // the time of the last look (NaN: none yet since the start or a reset)
        private readonly List<Spent> _spent = new List<Spent>();
        private readonly Dictionary<string, float> _olderUse = new Dictionary<string, float>();  // last use per action among spends no longer kept

        public Budget(DirectorPlan plan, Func<int, string> triggerWhy = null)
        {
            if (plan != null)
            {
                Initial = Math.Max(0, plan.Budget); Regen = Math.Max(0, plan.Regen);
                foreach (ActionPlan a in plan.Actions) if (a != null) Actions.Add(a);
            }
            TriggerWhy = triggerWhy;
            _points = Initial;
        }

        /// <summary>The points as of the last call that passed a time.</summary>
        public float Points => _points;

        /// <summary>Back to the start of a mission: the full budget, nothing spent, no action resting.</summary>
        public void Reset()
        {
            _points = Initial; _clock = float.NaN; _spent.Clear(); _olderUse.Clear();
        }

        public ActionPlan Find(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return null;
            foreach (ActionPlan a in Actions) if (a.Id == actionId) return a;
            return null;
        }

        /// <summary>Whether this command id has been spent before (refunded or not).</summary>
        public bool Has(string commandId) => !string.IsNullOrEmpty(commandId) && Record(commandId) != null;

        /// <summary>Whether the spend under this command id was given back.</summary>
        public bool Refunded(string commandId) { Spent s = Record(commandId); return s != null && s.Refunded; }

        // Regen runs from the first look after the start; points only ever sit below the ceiling after a spend, which needs a look.
        private void Advance(float now)
        {
            if (float.IsNaN(_clock)) { _clock = now; return; }
            if (now <= _clock) return;
            if (Regen > 0 && _points < Initial) _points = Math.Min(Initial, _points + Regen * (now - _clock) / 60f);
            _clock = now;
        }

        /// <summary>Seconds until the action may be used again (0: it may).</summary>
        public float CooldownLeft(ActionPlan a, float now)
        {
            if (a == null || a.Cooldown <= 0f) return 0f;
            float last = float.NaN;
            if (_olderUse.TryGetValue(a.Id, out float older)) last = older;
            foreach (Spent s in _spent) if (!s.Refunded && s.Action == a.Id && (float.IsNaN(last) || s.At > last)) last = s.At;
            return float.IsNaN(last) ? 0f : Math.Max(0f, last + a.Cooldown - now);
        }

        /// <summary>
        /// Whether the action may be used now. why, when not, in this order: no such action; it is resting; not enough points;
        /// its trigger cannot be fired (missing, or fired and not armed again).
        /// </summary>
        public bool Can(string actionId, float now, out string why)
        {
            Advance(now);
            ActionPlan a = Find(actionId);
            if (a == null) { why = "there is no action '" + (actionId ?? "") + "'"; return false; }
            float rest = CooldownLeft(a, now);
            if (rest > 0f) { why = "cooldown: " + Math.Ceiling(rest).ToString(CultureInfo.InvariantCulture) + " s left"; return false; }
            if (_points < a.Cost) { why = "not enough points (needs " + a.Cost.ToString(CultureInfo.InvariantCulture) + ", has " + Shown2(_points) + ")"; return false; }
            string trigger = TriggerWhy == null ? "" : TriggerWhy(a.Fire) ?? "";
            if (trigger.Length > 0) { why = trigger; return false; }
            why = "";
            return true;
        }

        /// <summary>
        /// Whether the action may be used while the host directs freely (budget mode off): it exists and its trigger can be
        /// fired. Points and rests do not count then, because nothing is paid.
        /// </summary>
        public bool CanFree(string actionId, out string why)
        {
            ActionPlan a = Find(actionId);
            if (a == null) { why = "there is no action '" + (actionId ?? "") + "'"; return false; }
            string trigger = TriggerWhy == null ? "" : TriggerWhy(a.Fire) ?? "";
            why = trigger;
            return trigger.Length == 0;
        }

        /// <summary>
        /// Pays for the action under this command id. A command id seen before pays nothing again: true when it paid for the
        /// same action and was not refunded, false otherwise. A refused action costs nothing.
        /// </summary>
        public bool Spend(string actionId, string commandId, float now, out string why)
        {
            Spent seen = string.IsNullOrEmpty(commandId) ? null : Record(commandId);
            if (seen != null)
            {
                Advance(now);
                if (seen.Action != actionId) { why = "this command was used for the action '" + seen.Action + "'"; return false; }
                if (seen.Refunded) { why = "this command was refunded; a new request is needed"; return false; }
                why = "already paid for by this command";
                return true;
            }
            if (!Can(actionId, now, out why)) return false;
            ActionPlan a = Find(actionId);
            _points -= a.Cost;
            _spent.Add(new Spent { Command = commandId ?? "", Action = a.Id, At = now, Cost = a.Cost });
            while (_spent.Count > Keep)
            {
                Spent old = _spent[0]; _spent.RemoveAt(0);
                if (!old.Refunded && (!_olderUse.TryGetValue(old.Action, out float was) || old.At > was)) _olderUse[old.Action] = old.At;
            }
            return true;
        }

        /// <summary>Gives back what the command paid and lifts the action's rest. False when the id is unknown or already refunded.</summary>
        public bool Refund(string commandId)
        {
            Spent s = string.IsNullOrEmpty(commandId) ? null : Record(commandId);
            if (s == null || s.Refunded) return false;
            s.Refunded = true;
            _points = Math.Min(Initial, _points + s.Cost);
            return true;
        }

        private Spent Record(string commandId)
        {
            for (int i = _spent.Count - 1; i >= 0; i--) if (_spent[i].Command == commandId) return _spent[i];
            return null;
        }

        private static string Shown2(float f) => ((float)Math.Round(f, 2)).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The snapshot for the page:
        /// {"points":7.5,"budget":10,"regen":1,"on":true,"actions":[{"id":"rear","name":"Rear reinforcements","cost":3,"cooldownLeft":12,
        ///  "available":false,"why":"cooldown: 12 s left","fire":12,"cooldown":60,"minDistance":30,"unseen":true,"fallback":"wait"}],
        ///  "spent":[{"command":"c1","id":"rear","at":42.5,"refunded":false}]}
        /// Every key is always written. spent lists the newest Shown spends, oldest first. on: the budget rules apply; when the
        /// host directs freely (on false) an action is available whenever its trigger can be fired (see CanFree). The action's
        /// own rules come last, so a card can name the trigger it fires and the placement rule it waits for.
        /// </summary>
        public string Json(float now, bool on = true)
        {
            Advance(now);
            var sb = new StringBuilder(256);
            sb.Append("{\"points\":").Append(Shown2(_points)).Append(",\"budget\":").Append(Initial.ToString(CultureInfo.InvariantCulture))
              .Append(",\"regen\":").Append(Regen.ToString(CultureInfo.InvariantCulture)).Append(",\"on\":").Append(on ? "true" : "false").Append(",\"actions\":[");
            for (int i = 0; i < Actions.Count; i++)
            {
                ActionPlan a = Actions[i];
                string why;
                bool ok = on ? Can(a.Id, now, out why) : CanFree(a.Id, out why);
                sb.Append(i > 0 ? "," : "").Append("{\"id\":\"").Append(Guard.Esc(a.Id)).Append("\",\"name\":\"").Append(Guard.Esc(a.Name ?? ""))
                  .Append("\",\"cost\":").Append(a.Cost.ToString(CultureInfo.InvariantCulture)).Append(",\"cooldownLeft\":").Append(Shown2(CooldownLeft(a, now)))
                  .Append(",\"available\":").Append(ok ? "true" : "false").Append(",\"why\":\"").Append(Guard.Esc(why))
                  .Append("\",\"fire\":").Append(a.Fire.ToString(CultureInfo.InvariantCulture)).Append(",\"cooldown\":").Append(Shown2(a.Cooldown))
                  .Append(",\"minDistance\":").Append(Shown2(a.MinDistance)).Append(",\"unseen\":").Append(a.Unseen ? "true" : "false")
                  .Append(",\"fallback\":\"").Append(a.Fallback == ActionPlan.Cancel ? ActionPlan.Cancel : ActionPlan.Wait).Append("\"}");
            }
            sb.Append("],\"spent\":[");
            for (int i = Math.Max(0, _spent.Count - Shown), n = 0; i < _spent.Count; i++, n++)
            {
                Spent s = _spent[i];
                sb.Append(n > 0 ? "," : "").Append("{\"command\":\"").Append(Guard.Esc(s.Command)).Append("\",\"id\":\"").Append(Guard.Esc(s.Action))
                  .Append("\",\"at\":").Append(Shown2(s.At)).Append(",\"refunded\":").Append(s.Refunded ? "true" : "false").Append('}');
            }
            return sb.Append("]}").ToString();
        }

        // ------------------------------------------------------------------ safe placement
        public const float SeenCone = 80f;      // degrees either side of where a player faces that count as in view
        public const float SeenRange = 250f;    // metres: farther than this a spawn is not taken to be seen

        /// <summary>
        /// Horizontal distance from p to a trigger's place: to its anchor, or for a painted area to the nearest outline edge (0
        /// inside any part's outline, holes included). The floor is ignored on purpose: a player on the floor above counts as near,
        /// which only ever makes the check stricter.
        /// </summary>
        public static float Distance(TriggerPlan t, V3 p)
        {
            if (t == null) return float.MaxValue;
            if (t.Area == null || t.Area.Parts.Count == 0) return V3.DistXZ(t.Pos, p);
            double best = double.MaxValue;
            foreach (AreaPart part in t.Area.Parts)
            {
                List<P2> ring = part.Outline;
                if (ring.Count < 3) continue;
                bool inside = false;
                for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                {
                    P2 a = ring[i], b = ring[j];
                    if ((a.Z > p.Z) != (b.Z > p.Z) && p.X < (b.X - a.X) * (p.Z - a.Z) / (b.Z - a.Z) + a.X) inside = !inside;
                    best = Math.Min(best, Segment(p.X, p.Z, a, b));
                }
                if (inside) return 0f;
            }
            return best == double.MaxValue ? V3.DistXZ(t.Pos, p) : (float)best;
        }

        private static double Segment(double px, double pz, P2 a, P2 b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z, len = dx * dx + dz * dz;
            double k = len <= 0 ? 0 : Math.Max(0, Math.Min(1, ((px - a.X) * dx + (pz - a.Z) * dz) / len));
            double ex = a.X + k * dx - px, ez = a.Z + k * dz - pz;
            return Math.Sqrt(ex * ex + ez * ez);
        }

        /// <summary>
        /// Whether firing the action now would put enemies too close to, or in view of, a living player. "" when safe, else why.
        /// trigger: the trigger it fires, checked by its place only when it has one (zone and hold); points: where its enemies
        /// would appear; players and yaws: each living player's position and facing (degrees, 0 = +Z). clearSight(eye, point):
        /// whether nothing blocks the view between them; null means no sight test is available, so every point in a player's
        /// view cone counts as seen.
        /// This is NOT a proven visibility test. It is deliberately strict: anything within SeenCone degrees of where a player
        /// faces and within SeenRange counts as seen unless clearSight says something is in the way.
        /// </summary>
        public static string Placement(ActionPlan a, TriggerPlan trigger, IList<V3> points, IList<V3> players, IList<float> yaws, Func<V3, V3, bool> clearSight)
        {
            if (a == null) return "";
            bool placed = trigger != null && TriggerPlan.Placed(trigger.When);
            for (int i = 0; i < players.Count; i++)
            {
                V3 who = players[i];
                if (a.MinDistance > 0f)
                {
                    if (placed && Distance(trigger, who) < a.MinDistance)
                        return "a player is " + Distance(trigger, who).ToString("0", CultureInfo.InvariantCulture) + " m from the trigger's place (needs " + a.MinDistance.ToString("0.#", CultureInfo.InvariantCulture) + " m)";
                    foreach (V3 p in points)
                        if (V3.DistXZ(p, who) < a.MinDistance)
                            return "a player is " + V3.DistXZ(p, who).ToString("0", CultureInfo.InvariantCulture) + " m from where an enemy would appear (needs " + a.MinDistance.ToString("0.#", CultureInfo.InvariantCulture) + " m)";
                }
                if (!a.Unseen) continue;
                float yaw = i < yaws.Count ? yaws[i] : float.NaN;
                if (placed && InView(who, yaw, trigger.Pos, clearSight)) return "a player can see the trigger's place";
                foreach (V3 p in points) if (InView(who, yaw, p, clearSight)) return "a player can see where an enemy would appear";
            }
            return "";
        }

        // Facing unknown counts as facing it.
        private static bool InView(V3 eye, float yaw, V3 p, Func<V3, V3, bool> clearSight)
        {
            float d = V3.DistXZ(eye, p);
            if (d > SeenRange) return false;
            if (d > 0.5f && !float.IsNaN(yaw))
            {
                double rad = yaw * Math.PI / 180.0;
                double cos = (Math.Sin(rad) * (p.X - eye.X) + Math.Cos(rad) * (p.Z - eye.Z)) / d;
                if (cos < Math.Cos(SeenCone * Math.PI / 180.0)) return false;
            }
            return clearSight == null || clearSight(eye, p);
        }
    }
}
