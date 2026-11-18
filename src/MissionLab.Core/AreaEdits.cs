using System.Collections.Generic;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// Painted trigger areas in a running mission (ML-027): what the plugin does with the shapes when the browser page sends
    /// its triggers, and the one-off preparation that keeps the per-frame membership check cheap. Pure, so the tests hold the
    /// live path to the same rules as a saved file.
    ///
    /// A shape is one change: it is checked with the strict file rules before anything is taken over, and a shape that breaks
    /// them is refused whole. The trigger then keeps the place it had (anchor, radius and area), so the engine sees no change
    /// of place and its progress stands; a good new shape goes through TriggerEngine.Sync, which drops only the unfinished
    /// progress of a reshaped zone or hold and keeps its firings.
    /// </summary>
    public static class AreaEdits
    {
        /// <summary>
        /// The first broken rule of each painted area of a zone or hold in the trigger list as the page sent it, by trigger number.
        /// Triggers without an area, or with one that keeps every rule, are not listed; nor are other kinds, whose area the reader
        /// ignores anyway. Of two triggers with the same number only the first counts, as in the engine. Numbers and kinds are read
        /// as MissionPlan.FromJson reads them. Paths read as in a file: "triggers[2].area.parts[0].outline: ...".
        /// </summary>
        public static Dictionary<int, string> Refused(JsonElement triggers)
        {
            var refused = new Dictionary<int, string>();
            if (triggers.ValueKind != JsonValueKind.Array) return refused;
            var seen = new HashSet<int>();
            int i = -1;
            foreach (JsonElement t in triggers.EnumerateArray())
            {
                i++;
                if (t.ValueKind != JsonValueKind.Object) continue;
                int id = t.TryGetProperty("id", out JsonElement n) && n.ValueKind == JsonValueKind.Number ? (int)n.GetDouble() : 0;
                if (id <= 0 || !seen.Add(id)) continue;
                string when = t.TryGetProperty("when", out JsonElement w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
                if (!TriggerPlan.Known(when)) when = TriggerPlan.OnZone;
                if (!TriggerPlan.Placed(when)) continue;
                if (!t.TryGetProperty("area", out JsonElement area) || area.ValueKind == JsonValueKind.Null) continue;     // null: the circle, as before
                string first = null;
                AreaPlan.Check(area, "triggers[" + i + "]", (code, path, text) => { if (first == null) first = path + ": " + text; });
                if (first != null) refused[id] = first;
            }
            return refused;
        }

        /// <summary>
        /// Takes the page's triggers over as far as their shapes go, in place in <paramref name="sent"/>. A trigger whose area was
        /// refused keeps the anchor, radius and area it had; one that had no place before (it was another kind of trigger) stays
        /// as it was; a new one is left out, because counting players in a circle the author did not draw would be worse than
        /// not having it yet. Every area left is then prepared. Returns, per refused shape in list order, the trigger number and
        /// what to tell the host.
        /// </summary>
        public static List<KeyValuePair<int, string>> Apply(List<TriggerPlan> sent, IList<TriggerPlan> was, Dictionary<int, string> refused)
        {
            var said = new List<KeyValuePair<int, string>>();
            if (refused != null && refused.Count > 0)
            {
                for (int i = sent.Count - 1; i >= 0; i--)
                {
                    TriggerPlan t = sent[i];
                    if (!refused.TryGetValue(t.Id, out string why)) continue;
                    // Same number twice: the engine reads only the first, so only the first is looked at here too.
                    if (sent.FindIndex(o => o.Id == t.Id) != i) continue;
                    TriggerPlan before = null;
                    for (int k = 0; was != null && k < was.Count && before == null; k++) if (was[k].Id == t.Id) before = was[k];
                    string kept;
                    if (before == null) { sent.RemoveAt(i); kept = "it was not added"; }
                    else if (TriggerPlan.Placed(before.When))
                    {
                        t.Pos = before.Pos; t.Radius = before.Radius; t.Area = before.Area;
                        kept = "it keeps the place it had";
                    }
                    else { sent[i] = before; kept = "it stays as it was"; }
                    said.Insert(0, new KeyValuePair<int, string>(t.Id, "the painted area was not taken (" + why + "); " + kept));
                }
            }
            Prepare(sent);
            return said;
        }

        /// <summary>
        /// Works out the bounds and flat rings of every painted area once, when a trigger list is read or changed, so that the
        /// check made every frame for every player builds no geometry. Contains would do it lazily, but only when the number of
        /// parts changes, so an edited part with the same count would otherwise keep stale bounds.
        /// </summary>
        public static void Prepare(IList<TriggerPlan> triggers)
        {
            for (int i = 0; triggers != null && i < triggers.Count; i++)
            {
                AreaPlan area = triggers[i] == null ? null : triggers[i].Area;
                if (area != null && area.Parts.Count > 0) area.Prepare();
            }
        }
    }
}
