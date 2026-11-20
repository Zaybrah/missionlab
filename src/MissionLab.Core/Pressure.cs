using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MissionLab.Core
{
    /// <summary>
    /// What the host could see at one decision time (ML-052). Only observations the plugin can trust are used, and an
    /// unknown one is said rather than guessed: NaN, -1 or null. The director then holds and logs which one was missing.
    /// </summary>
    public sealed class PressureLook
    {
        public float Elapsed = float.NaN;     // seconds since the mission was laid out
        public IList<string> ActiveGoals;     // ids of the goals now active (GoalBook); null: unknown
        public int Players = -1;              // living players seen
        public int Enemies = -1;              // living enemies seen
        public bool BudgetOff;                // the host turned the budget rules off for the session: nothing would be paid
    }

    /// <summary>A card the director did not choose, the observation that ruled it out and the author's constraint it broke.</summary>
    public sealed class PressureReject
    {
        public string Action = "", Constraint = "", Observed = "", Why = "";
    }

    /// <summary>One entry of the decision log: a spend, a preview of one, a hold, the result of a spend, or a change of mode.</summary>
    public sealed class PressureDecision
    {
        public const string Spend = "spend", Preview = "preview", Hold = "hold", Result = "result", Mode = "mode";
        public float At;
        public string Kind = Hold;
        public string RunMode = PressurePlan.Off;
        public bool Manual;                   // the host asked for it ("hold now", "spend now", a mode change)
        public string Band = "";
        public string Action = "";
        public int Cost;
        public string Command = "";           // the card path's command id of a spend ("": nothing was sent)
        public string Constraint = "";        // what held it, or a result's status
        public string Observed = "";
        public string Why = "";
        public List<PressureReject> Rejected = new List<PressureReject>();

        /// <summary>One line for the replay and the log.</summary>
        public string Words()
        {
            string who = Manual ? "manual: " : "";
            string card = "'" + Action + "' (" + Cost.ToString(CultureInfo.InvariantCulture) + (Cost == 1 ? " point)" : " points)");
            string band = Band.Length > 0 ? " in band '" + Band + "'" : "";
            string text;
            if (Kind == Spend) text = who + "spend " + card + band + ": " + Why;
            else if (Kind == Preview) text = who + "preview: would spend " + card + band + ": " + Why;
            else if (Kind == Result) text = "result of '" + Action + "' (" + Command + "): " + Constraint + (Why.Length > 0 ? ", " + Why : "");
            else if (Kind == Mode) text = who + Why;
            else text = who + "hold" + band + ": " + Why;
            if (Observed.Length > 0 && Kind != Result && Kind != Mode) text += " [seen: " + Observed + "]";
            for (int i = 0; i < Rejected.Count; i++)
                text += (i == 0 ? "; not chosen: " : ", ") + "'" + Rejected[i].Action + "' (" + Rejected[i].Constraint + ": " + Rejected[i].Why + ")";
            return text;
        }
    }

    /// <summary>
    /// The pressure director (ML-052): an experiment in author-bounded pacing. At each decision time it takes the first band, in
    /// the author's order, whose conditions hold, and chooses among the director block's own action cards the most expensive
    /// one that fits every bound: the band's spend, the budget (points, cooldown, its trigger), the cap on living enemies and
    /// the card's own placement rule. Ties go to the card listed first. It has no clock and no randomness: the host hands in
    /// the time and what it saw, so the same observations give the same decisions.
    ///   - It never fires anything itself. A spend in "on" mode is a request the host plays through the ordinary card path
    ///     (Budget.Spend, the placement check, the trigger), so prices, refunds and the ledger are the cards' own. It cannot
    ///     invent an encounter or change how the mission is completed: a card only fires a trigger, as the host could.
    ///   - While a spend has not settled it decides nothing more, so a slow or lost answer never pays or spawns twice.
    ///   - "preview" decides exactly as "on" would and logs it, but sends nothing.
    ///   - A card whose firing failed (refunded by the card path) may be tried once more; after MostFailures it is not chosen.
    ///   - After a player goes down (fewer living players than last seen) nothing is spent for Rest seconds.
    /// The placement check is the cards' distance and view test (Budget.Placement): an approximation, not the game's sight.
    /// </summary>
    public sealed class PressureDirector
    {
        public const int Kept = 40, MostFailures = 2;
        public const string Pending = "pending", Off = "off", BudgetOff = "budget-off", Unknown = "unknown", Rest = "rest",
                            NoBand = "no-band", NoCard = "no-card";
        public const string Failed = "failed", SpendCap = "spend", Points = "budget", Cooldown = "cooldown", Trigger = "trigger",
                            MaxEnemies = "maxEnemies", MinDistance = "minDistance", Unseen = "unseen";
        public const string Approximation = "an approximation: the cards' distance and view test, not the game's own sight";

        public readonly PressurePlan Plan;
        public readonly Budget Budget;
        /// <summary>How many enemies firing the card would bring now; -1 or no function: unknown.</summary>
        public Func<ActionPlan, int> Brings;
        /// <summary>The card's placement rule now: "" when safe, else why not (Budget.Placement). No function: no check is available.</summary>
        public Func<ActionPlan, string> Placement;
        /// <summary>Told of every new log entry, for the replay and the plugin's log.</summary>
        public Action<PressureDecision> Noted;

        private readonly float _shortest;     // the shortest "every" of any band: the wait when no band applies
        private readonly List<PressureDecision> _log = new List<PressureDecision>();
        private readonly Dictionary<string, int> _failures = new Dictionary<string, int>(StringComparer.Ordinal);
        private string _mode;
        private float _nextAt;
        private float _restUntil = -1f;
        private string _restWhy = "";
        private int _lastPlayers = -1, _spends;
        private string _pending = "", _pendingAction = "";

        public PressureDirector(PressurePlan plan, Budget budget)
        {
            Plan = plan ?? new PressurePlan();
            Budget = budget;
            _mode = Array.IndexOf(PressurePlan.Modes, Plan.Mode) >= 0 ? Plan.Mode : PressurePlan.Off;
            float shortest = float.MaxValue;
            foreach (PressureBand b in Plan.Bands) if (b != null) shortest = Math.Min(shortest, b.Every);
            _shortest = shortest == float.MaxValue ? PressureBand.EveryDefault : Math.Max(PressureBand.EveryLeast, shortest);
            _nextAt = _shortest;      // the first decision comes once the players have had the shortest interval to settle in
        }

        public string RunMode => _mode;
        /// <summary>The command id of a spend still on its way through the card path ("" when none).</summary>
        public string Waiting => _pending;
        public IList<PressureDecision> Log => _log.AsReadOnly();
        public float NextAt => _nextAt;

        /// <summary>Whether a decision is due: the director is not off and its interval has passed.</summary>
        public bool Due(float now) => _mode != PressurePlan.Off && now >= _nextAt;

        /// <summary>The host switches the mode for this mission. False for a mode that does not exist.</summary>
        public bool SetMode(string mode, float now, bool manual)
        {
            if (Array.IndexOf(PressurePlan.Modes, mode) < 0) return false;
            if (mode == _mode) return true;
            string was = _mode;
            _mode = mode;
            if (_nextAt < now) _nextAt = now;
            Note(new PressureDecision { At = now, Kind = PressureDecision.Mode, RunMode = mode, Manual = manual, Why = "mode " + was + " -> " + mode });
            return true;
        }

        /// <summary>
        /// Every look at the players: fewer living than last seen means somebody went down (or left), and nothing is spent for
        /// Rest seconds from then. An unknown count (-1) teaches nothing and keeps the last one known.
        /// </summary>
        public void Watch(float now, int players)
        {
            if (players < 0) return;
            if (_lastPlayers >= 0 && players < _lastPlayers && Plan.Rest > 0f)
            {
                _restUntil = Math.Max(_restUntil, now + Plan.Rest);
                _restWhy = "a player went down at " + Secs(now) + " s";
                if (_mode != PressurePlan.Off)
                    Note(new PressureDecision { At = now, Kind = PressureDecision.Hold, RunMode = _mode, Constraint = Rest, Observed = "living players " + _lastPlayers + " -> " + players,
                                                Why = "a player went down: no spending for " + Secs(Plan.Rest) + " s" });
            }
            _lastPlayers = players;
        }

        /// <summary>"Hold now": no automatic spending for the author's rest, at least the shortest band interval. Returns the seconds held.</summary>
        public float Hold(float now)
        {
            float span = Math.Max(Plan.Rest, PressureBand.EveryLeast);
            _restUntil = Math.Max(_restUntil, now + span);
            _restWhy = "the host held at " + Secs(now) + " s";
            Note(new PressureDecision { At = now, Kind = PressureDecision.Hold, RunMode = _mode, Manual = true, Constraint = Rest, Why = "no automatic spending for " + Secs(span) + " s" });
            return span;
        }

        /// <summary>
        /// Decides now. manual ("spend now") skips the interval and the rest, never a bound. A spend in "on" mode is left
        /// waiting (Waiting) until Settle is told what the card path made of it.
        /// </summary>
        public PressureDecision Decide(float now, PressureLook look, bool manual = false)
        {
            look = look ?? new PressureLook();
            var d = new PressureDecision { At = now, RunMode = _mode, Manual = manual, Observed = Seen(look) };
            _nextAt = now + _shortest;
            if (_mode == PressurePlan.Off) return Held(d, Off, "the director is off");
            if (_pending.Length > 0) return Held(d, Pending, "waiting for '" + _pendingAction + "' (" + _pending + ") to settle; nothing more is spent until then");
            if (look.BudgetOff) return Held(d, BudgetOff, "the budget rules are off for this session; the director spends only within the author's budget");
            string unknown = float.IsNaN(look.Elapsed) ? "the time since the start" : look.Players < 0 ? "the living players"
                           : look.ActiveGoals == null && Plan.Bands.Exists(b => b != null && b.Goals.Count > 0) ? "the active goals"
                           : look.Enemies < 0 && Plan.MaxEnemies > 0 ? "the living enemies" : "";
            if (unknown.Length > 0) return Held(d, Unknown, unknown + " could not be seen: hold, nothing is spent, and the next decision comes in " + Secs(_shortest) + " s");
            if (!manual && now < _restUntil) return Held(d, Rest, _restWhy + ": resting, " + Secs((float)Math.Ceiling(_restUntil - now)) + " s left");

            PressureBand band = null;
            var misses = new List<string>();
            foreach (PressureBand b in Plan.Bands)
            {
                if (b == null) continue;
                string miss = Misses(b, look);
                if (miss.Length == 0) { band = b; break; }
                misses.Add("'" + b.Name + "' " + miss);
            }
            if (band == null) return Held(d, NoBand, "no band applies (" + string.Join("; ", misses) + ")");
            d.Band = band.Name;
            _nextAt = now + band.Every;
            int limit = band.Spend >= 0 ? band.Spend : Budget == null ? 0 : Budget.Initial;

            ActionPlan best = null;
            if (Budget != null)
                foreach (ActionPlan a in Budget.Actions)
                {
                    PressureReject no = Judge(a, now, limit, band, look);
                    if (no != null) { d.Rejected.Add(no); continue; }
                    if (best == null || a.Cost > best.Cost) best = a;
                }
            string earlier = misses.Count > 0 ? " (earlier bands: " + string.Join("; ", misses) + ")" : "";
            if (best == null) return Held(d, NoCard, "no approved card fits now" + earlier);
            d.Action = best.Id; d.Cost = best.Cost;
            d.Why = "the most expensive approved card within " + limit.ToString(CultureInfo.InvariantCulture) + " point(s) that passes every bound" + earlier;
            if (_mode == PressurePlan.On)
            {
                d.Kind = PressureDecision.Spend;
                d.Command = "pressure-" + (++_spends).ToString(CultureInfo.InvariantCulture);
                _pending = d.Command; _pendingAction = best.Id;
            }
            else d.Kind = PressureDecision.Preview;
            Note(d);
            return d;
        }

        /// <summary>
        /// What the card path made of a spend: CommandLedger.Applied, Unconfirmed or Failed (refunded there). Only the spend
        /// still waiting is taken, once; any other id is ignored and false is returned.
        /// </summary>
        public bool Settle(string command, string status, string said, float now)
        {
            if (string.IsNullOrEmpty(command) || command != _pending) return false;
            string action = _pendingAction;
            _pending = ""; _pendingAction = "";
            if (status == CommandLedger.Failed) _failures[action] = (_failures.TryGetValue(action, out int n) ? n : 0) + 1;
            ActionPlan a = Budget == null ? null : Budget.Find(action);
            Note(new PressureDecision { At = now, Kind = PressureDecision.Result, RunMode = _mode, Action = action, Cost = a == null ? 0 : a.Cost, Command = command, Constraint = status ?? "", Why = said ?? "" });
            return true;
        }

        // Why the band does not apply now ("" when it does): time first, then the goals, then the players.
        private static string Misses(PressureBand b, PressureLook look)
        {
            if (look.Elapsed < b.From) return "starts at " + Secs(b.From) + " s, " + Secs(look.Elapsed) + " s have passed";
            foreach (string g in b.Goals)
                if (look.ActiveGoals == null || !look.ActiveGoals.Contains(g)) return "needs goal '" + g + "' active";
            if (look.Players < b.MinPlayers) return "needs " + b.MinPlayers + " living players, " + look.Players + " seen";
            return "";
        }

        // Null when the card fits; else the first bound it breaks, cheapest checks first, so the reason is always the same one.
        private PressureReject Judge(ActionPlan a, float now, int limit, PressureBand band, PressureLook look)
        {
            if (_failures.TryGetValue(a.Id, out int failed) && failed >= MostFailures)
                return No(a, Failed, "earlier results", "its firing failed " + failed + " times this mission; not tried again");
            if (a.Cost > limit)
                return No(a, SpendCap, "band '" + band.Name + "'", "costs " + a.Cost + ", the band spends at most " + limit + " per decision");
            if (!Budget.Can(a.Id, now, out string why))
            {
                string kind = why.StartsWith("cooldown", StringComparison.Ordinal) ? Cooldown : why.StartsWith("not enough points", StringComparison.Ordinal) ? Points : Trigger;
                return No(a, kind, "the budget: " + Shown(Budget.Points) + " of " + Budget.Initial + " points", why);
            }
            if (Plan.MaxEnemies > 0)
            {
                int brings = Brings == null ? -1 : Brings(a);
                if (brings < 0) return No(a, MaxEnemies, "the card's enemies", "how many enemies it brings could not be seen, so the cap of " + Plan.MaxEnemies + " cannot be kept");
                if (look.Enemies + brings > Plan.MaxEnemies)
                    return No(a, MaxEnemies, "living enemies " + look.Enemies, look.Enemies + " living + " + brings + " it brings = " + (look.Enemies + brings) + ", over the cap of " + Plan.MaxEnemies);
            }
            if (a.MinDistance > 0f || a.Unseen)
            {
                string kind = a.MinDistance > 0f ? MinDistance : Unseen;
                if (Placement == null) return No(a, kind, "the players' positions", "no placement check is available");
                string risky = Placement(a) ?? "";
                if (risky.Length > 0)
                {
                    if (a.Unseen && risky.IndexOf(" see ", StringComparison.Ordinal) >= 0) kind = Unseen;
                    else if (a.MinDistance > 0f && risky.IndexOf(" m from ", StringComparison.Ordinal) >= 0) kind = MinDistance;
                    return No(a, kind, "the players' positions and facing", risky + " (" + Approximation + ")");
                }
            }
            return null;
        }

        private static PressureReject No(ActionPlan a, string constraint, string observed, string why) =>
            new PressureReject { Action = a.Id, Constraint = constraint, Observed = observed, Why = why };

        private PressureDecision Held(PressureDecision d, string constraint, string why)
        {
            d.Kind = PressureDecision.Hold; d.Constraint = constraint; d.Why = why;
            Note(d);
            return d;
        }

        private void Note(PressureDecision d)
        {
            _log.Add(d);
            while (_log.Count > Kept) _log.RemoveAt(0);
            if (Noted != null) Noted(d);
        }

        private static string Seen(PressureLook look)
        {
            string goals = look.ActiveGoals == null ? "unknown" : look.ActiveGoals.Count == 0 ? "none" : string.Join(", ", look.ActiveGoals);
            return "elapsed " + (float.IsNaN(look.Elapsed) ? "unknown" : Secs(look.Elapsed) + " s") + ", living players " + (look.Players < 0 ? "unknown" : look.Players.ToString(CultureInfo.InvariantCulture))
                 + ", living enemies " + (look.Enemies < 0 ? "unknown" : look.Enemies.ToString(CultureInfo.InvariantCulture)) + ", active goals " + goals;
        }

        private static string Secs(float s) => s.ToString("0.#", CultureInfo.InvariantCulture);
        private static string Shown(float f) => ((float)Math.Round(f, 2)).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The snapshot for the page: {"mode":"on","authored":"preview","next":42.5,"restLeft":0,"waiting":"pressure-2",
        /// "approximation":"...","log":[{"at":..,"kind":"spend","mode":"on","manual":false,"band":"Push","action":"rear","cost":3,
        /// "command":"pressure-2","constraint":"","observed":"...","why":"...","words":"...","rejected":[{"action":"flare",
        /// "constraint":"cooldown","observed":"...","why":"..."}]}]}. Every key is always written; next is the seconds until the
        /// next decision (-1 when off); the log is oldest first.
        /// </summary>
        public string Json(float now)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"mode\":\"").Append(_mode).Append("\",\"authored\":\"").Append(Array.IndexOf(PressurePlan.Modes, Plan.Mode) >= 0 ? Plan.Mode : PressurePlan.Off)
              .Append("\",\"next\":").Append(_mode == PressurePlan.Off ? "-1" : Shown(Math.Max(0f, _nextAt - now)))
              .Append(",\"restLeft\":").Append(Shown(Math.Max(0f, _restUntil - now))).Append(",\"waiting\":\"").Append(Guard.Esc(_pending))
              .Append("\",\"maxEnemies\":").Append(Plan.MaxEnemies.ToString(CultureInfo.InvariantCulture)).Append(",\"rest\":").Append(Shown(Plan.Rest))
              .Append(",\"approximation\":\"").Append(Guard.Esc(Approximation)).Append("\",\"log\":[");
            for (int i = 0; i < _log.Count; i++)
            {
                PressureDecision d = _log[i];
                sb.Append(i > 0 ? "," : "").Append("{\"at\":").Append(Shown(d.At)).Append(",\"kind\":\"").Append(d.Kind).Append("\",\"mode\":\"").Append(d.RunMode)
                  .Append("\",\"manual\":").Append(d.Manual ? "true" : "false").Append(",\"band\":\"").Append(Guard.Esc(d.Band)).Append("\",\"action\":\"").Append(Guard.Esc(d.Action))
                  .Append("\",\"cost\":").Append(d.Cost.ToString(CultureInfo.InvariantCulture)).Append(",\"command\":\"").Append(Guard.Esc(d.Command))
                  .Append("\",\"constraint\":\"").Append(Guard.Esc(d.Constraint)).Append("\",\"observed\":\"").Append(Guard.Esc(d.Observed))
                  .Append("\",\"why\":\"").Append(Guard.Esc(d.Why)).Append("\",\"words\":\"").Append(Guard.Esc(d.Words())).Append("\",\"rejected\":[");
                for (int j = 0; j < d.Rejected.Count; j++)
                {
                    PressureReject r = d.Rejected[j];
                    sb.Append(j > 0 ? "," : "").Append("{\"action\":\"").Append(Guard.Esc(r.Action)).Append("\",\"constraint\":\"").Append(r.Constraint)
                      .Append("\",\"observed\":\"").Append(Guard.Esc(r.Observed)).Append("\",\"why\":\"").Append(Guard.Esc(r.Why)).Append("\"}");
                }
                sb.Append("]}");
            }
            return sb.Append("]}").ToString();
        }
    }
}
