using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// ML-042 follow-up: the mission graph puts a rehearsal's checks on the boxes they are about by the trigger id each
/// step carries, so the plugin's answer must name the right trigger for every fired, not-fired and phase check.
/// </summary>
static class Ml042Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "examples", "rehearsals", "painted_house.json"))) root = Path.GetDirectoryName(root);
        check("ml042: the painted house example is found", root != null, "");
        if (root == null) return;
        string dir = Path.Combine(root, "examples", "rehearsals");
        string scenario = File.ReadAllText(Path.Combine(dir, "painted_house.json")).Replace("\r\n", "\n");
        string script = File.ReadAllText(Path.Combine(dir, "sc-painted-house", "front_door_counts.json")).Replace("\r\n", "\n");
        // Trigger 2 has not fired in this walk; asking that names trigger 2, not the trigger 1 the script is about.
        script = script.Replace("{\"at\":3,\"expect\":{\"fired\":[1]}}", "{\"at\":3,\"expect\":{\"fired\":[1],\"notFired\":[2]}}");
        string body = "{\"scenario\":" + JsonSerializer.Serialize(scenario) + ",\"script\":" + JsonSerializer.Serialize(script) + "}";
        string answer = RehearsalFile.Rehearse(body, out int status);
        var got = new List<string>();
        try
        {
            using (JsonDocument doc = JsonDocument.Parse(answer))
                foreach (JsonElement x in doc.RootElement.GetProperty("steps").EnumerateArray())
                    if (x.GetProperty("expected").GetString() != "")
                        got.Add(x.GetProperty("expected").GetString() + "@" + x.GetProperty("at").GetDouble() + "=" + x.GetProperty("trigger").GetInt32()
                            + (x.GetProperty("extraction").GetBoolean() ? "+x" : ""));
        }
        catch (Exception e) { got.Add(e.Message); }
        string have = string.Join(",", got);
        check("ml042: every check in the answer names the trigger it is about (not fired, phase, fired, and the added not fired)",
            status == 200 && have == "not fired@2.5=1,waiting@2.5=1,fired@3=1,not fired@3=2", status + " " + have);
    }
}
