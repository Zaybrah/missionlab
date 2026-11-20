using System;
using System.Globalization;

namespace MissionLab.Core
{
    /// <summary>One look at an enemy under a walking order, taken by the plugin once a second. Distances are flat (x and z).</summary>
    public struct OrderLook
    {
        public float Age;           // seconds since the order was given
        public float Start, Left;   // metres to the spot when the order was given, and now
        public float Moved;         // metres from where the enemy stood when it was ordered
        public float SinceCloser;   // seconds since it last came a metre closer than ever before (the whole age if it never did)
        public bool HasPath, Pending, Fighting;   // its navigation agent has a path; is still working one out; the enemy is in a fight
        public float PathShort;     // metres between the end of its path and the spot, once the path has ended short of it for a few looks (0: it does not)
    }

    /// <summary>
    /// What became of one walking order (ML-035). An order the game took is not yet a success: in the runs of 17 and 18
    /// November enemies that had just appeared took every order and none of them had a path or came closer. So an order is
    /// followed through three stages (taken, a path found, coming closer) and ends in one of a few fixed words, which the log,
    /// the replay and the Game Master can all use. The plugin asks the game; these rules decide what the answers mean.
    /// </summary>
    public static class OrderWatch
    {
        // The stages of an order that is still running, in the order they are reached.
        public const string Accepted = "accepted", PathFound = "path found", Moving = "on its way";

        // How an order ends, or why it is never given. Only Arrived is a success.
        public const string Arrived = "arrived", Fight = "in a fight", NoProgress = "no progress", Short = "stopped short",
                            Unreachable = "unreachable", InvalidFloor = "invalid floor", NoLegs = "no legs", FixedPost = "fixed post",
                            Gone = "gone", Replaced = "replaced";

        public const float ArriveWithin = 2.5f;     // metres: each enemy's spot is spread 2 m round the one asked for
        public const float CloserBy = 3f;           // metres closer than at the start before it counts as on its way (a guard turning on the spot drifts a little)
        public const float PathWithin = 2.5f;       // the game makes a path to the nearest point it can reach; ending farther off than this, it stops short
        public const float UnreachableAfter = 45f;  // seconds: the two fallbacks (at 6 s and 14 s) have had their turn, and orders seen to work late started within about 20 s
        public const float StallAfter = 45f;        // seconds without coming closer
        public const float GiveUpAfter = 90f;
        public const float FightAfter = 2f;         // a state read in the first moments can still be the one from before the order
        public const float GroundGap = 3f;          // metres, flat, between the spot asked for and the nearest walkable ground
        public const float FloorGap = 1.8f;         // metres up or down: the floors of Clean House are 3 m apart

        public static bool Succeeded(string word) => word == Arrived;

        /// <summary>
        /// Is the spot asked for on ground an enemy can stand on? "" if so, otherwise why not. found: the game's navigation
        /// gave a nearest walkable point at all; ground: that point. A door's hinge or a spot by a wall is moved a little to
        /// the ground beside it (seen on 16 November); a spot moved a long way, or onto another floor, is not where the
        /// author meant, so the order is not given rather than sending enemies somewhere else.
        /// </summary>
        public static string Spot(V3 asked, bool found, V3 ground)
        {
            var inv = CultureInfo.InvariantCulture;
            if (!found) return "there is no walkable ground near it";
            float up = ground.Y - asked.Y, flat = V3.DistXZ(asked, ground);
            if (Math.Abs(up) > FloorGap)
                return "the nearest walkable ground is on another floor, " + Math.Abs(up).ToString("0.0", inv) + " m " + (up < 0f ? "below" : "above");
            if (flat > GroundGap) return "the nearest walkable ground is " + flat.ToString("0.0", inv) + " m away";
            return "";
        }

        /// <summary>The stage an order has reached by this look.</summary>
        public static string Stage(OrderLook l) => l.Start - l.Left >= CloserBy ? Moving : l.HasPath ? PathFound : Accepted;

        /// <summary>How the order ends at this look, or null while it is still running. why: the numbers behind the word.</summary>
        public static string Judge(OrderLook l, out string why)
        {
            var inv = CultureInfo.InvariantCulture;
            string age = l.Age.ToString("0", inv) + " s", closer = Math.Max(0f, l.Start - l.Left).ToString("0.#", inv);
            string left = l.Left.ToString("0", inv) + " m short";
            why = "";
            if (l.Left < ArriveWithin) { why = "after " + age; return Arrived; }
            if (l.Fighting && l.Age > FightAfter) { why = "after " + l.Moved.ToString("0.#", inv) + " m; the order is dropped"; return Fight; }
            if (l.Age >= UnreachableAfter)
            {
                if (l.PathShort > 0f) { why = "its path ends " + l.PathShort.ToString("0.#", inv) + " m from the spot, " + left; return Unreachable; }
                if (!l.HasPath && l.Start - l.Left < CloserBy)
                {
                    why = (l.Pending ? "its path was still being worked out after " : "no path in ") + age + ", " + l.Moved.ToString("0.#", inv) + " m moved, " + left;
                    return Unreachable;
                }
            }
            if ((l.Age >= StallAfter && l.SinceCloser >= StallAfter) || l.Age >= GiveUpAfter)
            {
                if (l.Start - l.Left < CloserBy) { why = l.Moved.ToString("0.#", inv) + " m moved in " + age + " with a path, " + left; return NoProgress; }
                why = "came " + closer + " m closer, then no closer for " + l.SinceCloser.ToString("0", inv) + " s, " + left;
                return Short;
            }
            return null;
        }
    }
}
