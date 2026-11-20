using System;
using System.IO;
using MissionLab.Core;

/// <summary>
/// Walking orders (ML-035): an order the game took is not a success until the enemy has a path and arrives; fixed sentries,
/// spots off walkable ground, enemies with no path and enemies that stand still each end with their own word. The game's
/// navigation is not run here: the looks are what the plugin reads from it once a second. The shipped starter missions are
/// checked not to lean on hunters, which did not move in the game on 17 and 18 November.
/// </summary>
static class Ml035Tests
{
    static OrderLook Look(float age, float start, float left, float moved, bool path, float sinceCloser = -1f)
        => new OrderLook { Age = age, Start = start, Left = left, Moved = moved, HasPath = path, SinceCloser = sinceCloser < 0f ? age : sinceCloser };

    public static void Run(Action<string, bool, string> check)
    {
        {   // The hunters of 18 November: taken, never a path, 3.9 m in 90 s. That is unreachable, not "sent".
            string early = OrderWatch.Judge(Look(44f, 82f, 80f, 3.9f, false), out _);
            string late = OrderWatch.Judge(Look(45f, 82f, 80f, 3.9f, false), out string why);
            check("an enemy without a path is still given its time before the fallbacks are judged", early == null, early ?? "");
            check("an enemy that never gets a path ends as unreachable", late == OrderWatch.Unreachable, late + ": " + why);
            check("an unreachable order is not a success", !OrderWatch.Succeeded(late), late ?? "");
        }
        {   // A path still being worked out after the same time is unreachable too, and says so.
            var l = Look(45f, 30f, 30f, 0.2f, false); l.Pending = true;
            string word = OrderWatch.Judge(l, out string why);
            check("a path that is never finished ends as unreachable", word == OrderWatch.Unreachable, word + ": " + why);
        }
        {   // A path that ends short of the spot (the game's nearest reachable point) is unreachable even while the enemy walks it.
            var l = Look(45f, 40f, 25f, 15f, true, 1f); l.PathShort = 12f;
            string word = OrderWatch.Judge(l, out _);
            check("a path ending away from the spot ends as unreachable", word == OrderWatch.Unreachable, word ?? "");
            var fine = Look(45f, 40f, 25f, 15f, true, 1f);
            check("the same walk on a path to the spot goes on", OrderWatch.Judge(fine, out _) == null, "");
        }
        {   // A path but no steps: no progress, not unreachable and not arrived.
            string word = OrderWatch.Judge(Look(45f, 20f, 20f, 0.5f, true), out _);
            check("an enemy with a path that does not move ends with no progress", word == OrderWatch.NoProgress, word ?? "");
        }
        {   // Came a good way, then stopped: stopped short, which is not the same as never moving.
            string word = OrderWatch.Judge(Look(60f, 40f, 15f, 25f, true, 45f), out _);
            check("an enemy that came closer and then stopped ends as stopped short", word == OrderWatch.Short, word ?? "");
            check("an enemy still coming closer is left to walk", OrderWatch.Judge(Look(60f, 40f, 15f, 25f, true, 3f), out _) == null, "");
        }
        {   // A slow walker that keeps coming closer is given up only at 90 s.
            check("a walker coming closer is not given up at 89 s", OrderWatch.Judge(Look(89f, 120f, 20f, 100f, true, 1f), out _) == null, "");
            check("at 90 s it ends as stopped short", OrderWatch.Judge(Look(90f, 120f, 20f, 100f, true, 1f), out _) == OrderWatch.Short, "");
        }
        {   // Within 2.5 m of its spot it has arrived, the only success; a fight first ends the order as a fight.
            string word = OrderWatch.Judge(Look(30f, 40f, 2f, 38f, true), out _);
            check("an enemy within 2.5 m of its spot has arrived", word == OrderWatch.Arrived && OrderWatch.Succeeded(word), word ?? "");
            var fight = Look(10f, 40f, 30f, 10f, true); fight.Fighting = true;
            check("an enemy in a fight drops the order", OrderWatch.Judge(fight, out _) == OrderWatch.Fight, "");
            var first = Look(1f, 40f, 40f, 0f, false); first.Fighting = true;
            check("a fight state read in the first second does not end the order", OrderWatch.Judge(first, out _) == null, "");
        }
        {   // The stages are told apart: taken, a path found, on its way.
            check("taken with no path yet is only accepted", OrderWatch.Stage(Look(1f, 40f, 40f, 0f, false)) == OrderWatch.Accepted, "");
            check("with a path it has found one", OrderWatch.Stage(Look(2f, 40f, 39f, 0.5f, true)) == OrderWatch.PathFound, "");
            check("three metres closer it is on its way", OrderWatch.Stage(Look(5f, 40f, 36f, 4f, true)) == OrderWatch.Moving, "");
            check("drifting a metre is not on its way", OrderWatch.Stage(Look(5f, 40f, 39f, 1f, false)) == OrderWatch.Accepted, "");
        }
        {   // Spots: a door's hinge moved to the ground beside it is fine; a roof, another floor or far-off ground is not.
            check("a hinge 1 m from the ground beside it is walkable", OrderWatch.Spot(new V3(-19.5f, 1.0f, -19.6f), true, new V3(-19.5f, 0.7f, -18.8f)) == "", "");
            string roof = OrderWatch.Spot(new V3(-20f, 7.5f, 4f), true, new V3(-20f, 3.68f, 4f));
            check("a spot on the roof is on another floor", roof.Contains("another floor") && roof.Contains("below"), roof);
            string far = OrderWatch.Spot(new V3(10f, 0f, 10f), true, new V3(10f, 0f, 15f));
            check("ground 5 m away is not the spot", far != "", far);
            check("no ground at all is not the spot", OrderWatch.Spot(new V3(0f, 0f, 0f), false, new V3(0f, 0f, 0f)) != "", "");
            check("the floor below by a metre and a half still counts", OrderWatch.Spot(new V3(0f, 2.2f, 0f), true, new V3(0f, 0.7f, 0f)) == "", "");
        }
        {   // The starter missions finish without hunters: none of their triggers sends enemies after the players.
            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "examples", "clean_house_quiet_raid.json"))) root = Path.GetDirectoryName(root);
            check("the examples are found", root != null, "");
            if (root != null)
                foreach (string name in new[] { "clean_house_quiet_raid.json", "clean_house_sealed.json", "clean_house_escalation.json", "clean_house_game_master.json" })
                {
                    MissionPlan p = MissionPlan.FromJson(File.ReadAllText(Path.Combine(root, "examples", name)));
                    bool hunts = p.Triggers.Exists(t => t.Hunt);
                    int routed = p.Spawns.FindAll(s => s.Trigger != 0 && s.Patrol.Count > 0).Count;
                    check(name + " has no hunters and its enemies that come later walk routes", !hunts && routed > 0, "hunt " + hunts + ", routed " + routed);
                }
        }
    }
}
