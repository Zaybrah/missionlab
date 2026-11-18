using System;
using MissionLab.Core;

/// <summary>
/// Saved revisions (ML-014): which scenario and number a text names, the file written and read back, file names,
/// notes, and which run records count as having played a revision.
/// </summary>
static class RevisionsTests
{
    public static void Run(Action<string, bool, string> check)
    {
        {   // A plan/1 text names its id and revision at the root; plan/2 under "mission".
            string why1 = RevisionFile.Identify("{\"schema\":\"missionlab-plan/1\",\"name\":\"a\",\"id\":\"sc-abc\",\"revision\":3,\"spawns\":[]}", out string id1, out int n1);
            string why2 = RevisionFile.Identify("{\"schema\":\"missionlab-plan/2\",\"mission\":{\"name\":\"a\",\"id\":\"sc-xyz_2\",\"revision\":7,\"spawns\":[]}}", out string id2, out int n2);
            check("a plan/1 text names its scenario and revision", why1 == "" && id1 == "sc-abc" && n1 == 3, why1 + " " + id1 + " " + n1);
            check("a plan/2 text names them under \"mission\"", why2 == "" && id2 == "sc-xyz_2" && n2 == 7, why2 + " " + id2 + " " + n2);
        }
        {   // Anything that could not be a folder name, or no usable number, is refused with a reason.
            check("an id with a slash is refused", RevisionFile.Identify("{\"id\":\"../x\",\"revision\":1}", out _, out _) != "", "");
            check("an id longer than 64 is refused", RevisionFile.Identify("{\"id\":\"" + new string('a', 65) + "\",\"revision\":1}", out _, out _) != "", "");
            check("a missing id is refused", RevisionFile.Identify("{\"revision\":1}", out _, out _) != "", "");
            check("revision 0 is refused", RevisionFile.Identify("{\"id\":\"sc-a\",\"revision\":0}", out _, out _) != "", "");
            check("a revision of 1.5 is refused", RevisionFile.Identify("{\"id\":\"sc-a\",\"revision\":1.5}", out _, out _) != "", "");
            check("text that is not JSON is refused", RevisionFile.Identify("{nope", out _, out _) != "", "");
            check("an empty text is refused", RevisionFile.Identify("", out _, out _) != "", "");
        }
        {   // Written and read back, a revision keeps every character of its text and note.
            string text = "{\n \"id\": \"sc-a\",\n \"revision\": 2,\n \"name\": \"Quote \\\" back\\\\slash é 漢字 🙂\"\n}\n";
            var e = new RevisionFile.Entry { Id = "sc-a", N = 2, Note = RevisionFile.CleanNote("  faster\r\n  start \t "), Saved = "2026-11-17T20:00:00Z", Hash = ScenarioFile.Hash(text), Text = text };
            string json = RevisionFile.Write(e);
            RevisionFile.Entry back = RevisionFile.Read(json);
            check("a revision file reads back", back != null, json);
            check("its scenario text is kept byte for byte", back != null && back.Text == text, back == null ? "" : back.Text);
            check("its note, number, time and fingerprint are kept", back != null && back.Note == "faster start" && back.N == 2 && back.Saved == e.Saved && back.Hash == ScenarioFile.Hash(text), back == null ? "" : back.Note);
            check("the file names its schema", json.StartsWith("{\"schema\":\"missionlab-revision/1\"", StringComparison.Ordinal), json);
            check("another schema is not read as a revision", RevisionFile.Read(json.Replace("missionlab-revision/1", "missionlab-revision/2")) == null, "");
            check("a long note is cut to 200 characters", RevisionFile.CleanNote(new string('x', 300)).Length == RevisionFile.MaxNote, "");
        }
        {   // File names: r<n>.json and nothing else.
            check("r12.json is revision 12", RevisionFile.FileName(12) == "r12.json" && RevisionFile.NumberOf("r12.json") == 12, "");
            check("other names are no revision", RevisionFile.NumberOf("r.json") == 0 && RevisionFile.NumberOf("r1.json.part") == 0 && RevisionFile.NumberOf("rx2.json") == 0 && RevisionFile.NumberOf("r0.json") == 0, "");
        }
        {   // A run played a revision when it played the same text, or the same scenario id and revision number.
            var e = new RevisionFile.Entry { Id = "sc-a", N = 4, Hash = "fnv1a64:0123456789abcdef" };
            check("the same fingerprint links a run", RevisionFile.Played(e, "fnv1a64:0123456789abcdef", "", 0), "");
            check("the same id and number link a run saved by the game", RevisionFile.Played(e, "fnv1a64:ffffffffffffffff", "sc-a", 4), "");
            check("another number of the same scenario does not", !RevisionFile.Played(e, "fnv1a64:ffffffffffffffff", "sc-a", 3), "");
            check("a run without fingerprint or id does not", !RevisionFile.Played(e, "", "", 0), "");
        }
    }
}
