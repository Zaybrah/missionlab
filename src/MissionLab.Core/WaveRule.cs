using System;

namespace MissionLab.Core
{
    /// <summary>
    /// The scenario's respawn rule, as a shared reinforcement wave. The game can only bring every dead player back at once,
    /// so that is what the rule says:
    ///   - the clock starts at the first death while nobody else is waiting;
    ///   - "after" seconds later everyone who is dead at that moment comes back together (someone who died a second
    ///     before the wave waits one second, not "after" seconds);
    ///   - "times" is the number of waves that were SEEN to bring someone back. Asking the game is not enough: a wave
    ///     counts when a player who was dead is alive again. If nobody is back within a few seconds the wave has failed,
    ///     is not counted, and is tried again a few times.
    /// This class only decides; the plugin asks the game and reports what it sees.
    /// </summary>
    public sealed class WaveRule
    {
        public const string Idle = "idle", Counting = "counting", Pending = "pending", Failed = "failed";
        public const float Timeout = 8f, RetryAfter = 10f;
        public const int MaxTries = 3;
        public enum Act { None, Announce, Ask, Observed, GaveUp }

        public float After;              // seconds from the first death to the wave (0: the rule is off)
        public int Times;                // waves per mission (0: no limit)
        public int Done;                 // waves seen to have happened
        public string State = Idle;
        public string Error = "";
        public float Since;              // counting: the first death; pending: when the game was asked; failed: when it failed
        public int Tries;                // how often the game was asked for the wave that is due

        public int WavesLeft => Times <= 0 ? -1 : Math.Max(0, Times - Done);
        public bool Spent => Times > 0 && Done >= Times;

        public void Reset() { Done = 0; State = Idle; Error = ""; Since = 0f; Tries = 0; }

        /// <summary>Seconds until the wave, while the clock runs.</summary>
        public float SecondsLeft(float now) => State == Counting ? Math.Max(0f, Since + After - now) : 0f;

        /// <summary>
        /// One look. dead: players who are dead now. back: of the players who were dead when the game was last asked, how
        /// many are alive again. Returns what the caller should do: Announce (tell the players the clock runs), Ask (ask the
        /// game for the wave, then call Asked), Observed (the wave happened: put the game's own setting back) or GaveUp
        /// (it did not: put the setting back; it is tried again later).
        /// </summary>
        public Act Step(float now, int dead, int back)
        {
            if (After <= 0f) { State = Idle; return Act.None; }
            switch (State)
            {
                case Pending:
                    if (back > 0)
                    {
                        Done++; Tries = 0; Error = "";
                        if (dead > 0 && !Spent) { State = Counting; Since = now; } else State = Idle;
                        return Act.Observed;
                    }
                    if (now - Since > Timeout) { State = Failed; Since = now; Error = "nobody was back " + (int)Timeout + " s after the game was asked"; return Act.GaveUp; }
                    return Act.None;
                case Failed:
                    if (dead == 0) { State = Idle; Tries = 0; return Act.None; }
                    return Tries < MaxTries && now - Since >= RetryAfter ? Act.Ask : Act.None;
                case Counting:
                    if (dead == 0) { State = Idle; return Act.None; }
                    return now - Since >= After ? Act.Ask : Act.None;
                default:
                    if (dead == 0 || Spent) return Act.None;
                    State = Counting; Since = now; Tries = 0;
                    return Act.Announce;
            }
        }

        /// <summary>The caller has asked the game. ok: the call went through (which proves nothing yet).</summary>
        public void Asked(float now, bool ok, string error)
        {
            Tries++; Since = now;
            if (ok) { State = Pending; Error = ""; }
            else { State = Failed; Error = string.IsNullOrEmpty(error) ? "the game could not be asked" : error; }
        }
    }
}
