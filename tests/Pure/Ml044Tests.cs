using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MissionLab.Core;

/// <summary>
/// ML-044: the example made with the Intel raid recipe (examples/clean_house_recipe_intel_raid.json) and its saved
/// walkthrough (examples/rehearsals/sc-cleanhouse-recipe-raid/intel_raid.json), played by the Core rehearsal: the squad
/// walks from Alpha 2's spot into the house, takes any intel and goes back out, with the extraction held until the intel
/// and the time limit not yet run out. The walkthrough is the one the recipe writes, with the same checks.
/// </summary>
static class Ml044Tests
{
    private const string Example = "clean_house_recipe_intel_raid.json", Walk = "sc-cleanhouse-recipe-raid/intel_raid.json";

    private static string Root()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "examples", Example))) root = Path.GetDirectoryName(root);
        return root;
    }

    private static string Failures(RehearsalResult r) =>
        (r.Blocked == "" ? "" : "blocked: " + r.Blocked + " | ") + string.Join(" | ", r.Steps.Where(x => !x.Ok).Select(x => x.What + " -> expected " + x.Expected + ", got " + x.Actual));

    public static void Run(Action<string, bool, string> check)
    {
        string root = Root();
        check("the recipe's example is found", root != null, "");
        if (root == null) return;
        string scenario = File.ReadAllText(Path.Combine(root, "examples", Example)).Replace("\r\n", "\n");
        string script = File.ReadAllText(Path.Combine(root, "examples", "rehearsals", Walk)).Replace("\r\n", "\n");
        List<Finding> read = ScenarioFile.Read(scenario, out MissionPlan plan);
        check("the recipe's example passes the strict reader", plan != null && !read.Exists(f => f.Level == Finding.Error), string.Join(" | ", read));
        if (plan == null) return;
        check("its triggers never hunt or rally", !plan.Triggers.Exists(t => t.Hunt || t.Rally > 0f), "");

        var found = Rehearsal.Read(script, out RehearsalScript s, plan);
        check("the saved walkthrough reads with nothing found", s != null && found.Count == 0, string.Join(" | ", found));
        if (s == null) return;
        check("the saved walkthrough is in Rehearsal.Write's own form", Rehearsal.Write(s) == script.TrimEnd('\n'), Rehearsal.Write(s));
        check("it is kept beside the scenario under its id", Walk.StartsWith(plan.Id + "/", StringComparison.Ordinal), plan.Id);

        {   // In from Alpha 2, into the house, the intel, back out: every check passes.
            RehearsalResult r = Rehearsal.Run(plan, s);
            check("the walkthrough passes against the example", r.Passed, Failures(r));
        }
        {   // The walkthrough notices the held extraction: without it, "held" before the intel is wrong.
            ScenarioFile.Read(scenario, out MissionPlan open);
            open.ExtractionAfter = 0;
            RehearsalResult r = Rehearsal.Run(open, s);
            check("with the extraction not held, the walkthrough fails on its held checks", !r.Passed && r.Steps.Exists(x => !x.Ok && x.Expected == Rehearsal.Held), Failures(r));
        }
        {   // It notices the building: a house zone the squad's spot is not in never fires.
            ScenarioFile.Read(scenario, out MissionPlan moved);
            TriggerPlan house = moved.Triggers.Find(t => t.Id == 1);
            foreach (AreaPart part in house.Area.Parts) for (int k = 0; k < part.Outline.Count; k++) part.Outline[k] = new P2(part.Outline[k].X + 100f, part.Outline[k].Z);
            house.Area.Prepare();
            RehearsalResult r = Rehearsal.Run(moved, s);
            check("with the house painted elsewhere, the walkthrough fails on entering it", !r.Passed && r.Steps.Exists(x => !x.Ok && x.Trigger == 1), Failures(r));
        }
    }
}
