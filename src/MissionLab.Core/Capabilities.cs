using System;
using System.Collections.Generic;

namespace MissionLab.Core
{
    /// <summary>
    /// The features a scenario uses, as short fixed words ("areas", "respawn", "chance" ...). Lists of scenarios show them, and
    /// they tell at a glance whether a file needs something an older or a different MissionLab does not have.
    /// The editor has the same list in capabilitiesOf.
    /// </summary>
    public static class Capabilities
    {
        /// <summary>The features plan uses, sorted and each once.</summary>
        public static List<string> Of(MissionPlan plan)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            if (plan == null) return new List<string>();
            if (plan.Doors.Count > 0) found.Add("doors");
            if (plan.Things.Count > 0) found.Add("things");
            if (plan.Objectives.Count > 0) found.Add("intel-move");
            if (plan.RespawnAfter > 0f) found.Add("respawn");
            if (TriggerPlan.OnOff(plan.Power) != "") found.Add("power");
            if (plan.Weather.HasWish) found.Add("weather");
            if (!string.IsNullOrEmpty(plan.Insertion)) found.Add("insertion");
            if (plan.Briefing.Count > 0) found.Add("messages");
            if (plan.ExtractionAfter != 0) found.Add("extraction-hold");
            if (plan.Places.Count > 0) found.Add("places");
            foreach (TriggerPlan t in plan.Triggers)
            {
                if (t.Shaped) found.Add("areas");
                if (t.Doors.Count > 0) found.Add("doors");
                if (t.When == TriggerPlan.OnDoor) found.Add("door-trigger");
                if (t.Respawn) found.Add("respawn");
                if (TriggerPlan.OnOff(t.Power) != "") found.Add("power");
                if (TriggerPlan.Clean(t.Message) != "") found.Add("messages");
                if (TriggerPlan.Ending(t.End) != "") found.Add("endings");
                if (t.Hunt) found.Add("hunt");
                if (t.Rally > 0f) found.Add("rally");
                if (t.OpensExtraction) found.Add("extraction-hold");
                if (t.Chance < 100) found.Add("chance");
                if (t.Repeat > 0f) found.Add("repeat");
            }
            foreach (SpawnPlan s in plan.Spawns)
            {
                if (s.Vehicle) found.Add("vehicles");
                if (s.Hvt) found.Add("hvt");
                if (s.Team > 0) found.Add("teams");
                if (s.Chance < 100 || s.Either > 0 || s.Lot > 0 || s.MinPlayers > 1) found.Add("chance");
            }
            foreach (ThingPlan th in plan.Things) if (th.Chance < 100) found.Add("chance");
            foreach (DoorPlan d in plan.Doors) if (d.Chance < 100) found.Add("chance");
            return new List<string>(found);
        }
    }
}
