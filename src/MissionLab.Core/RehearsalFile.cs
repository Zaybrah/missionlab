using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// The browser link's side of rehearsal (ML-016), kept pure so that the tests run exactly what the plugin runs:
    /// a scenario and a script sent by the page are read strictly, the script is played through the scenario with
    /// Rehearsal.Run (the same TriggerEngine the live mission uses) and the answer is written for the page; and the
    /// tests an author keeps under BepInEx/MissionLab/rehearsals/&lt;scenario id&gt;/&lt;name&gt;.json are checked and
    /// summarised. The plugin does the file work on the worker thread and calls this; nothing here touches the game.
    /// </summary>
    public static class RehearsalFile
    {
        public const int MaxText = 1024 * 1024;       // a script is a few kilobytes; a bigger one is a mistake, not a test
        public const int MaxListed = 200;
        public const string Same = "same", Changed = "changed", Unknown = "unknown";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>The file a test is kept in: letters, digits, '_' and '-' of its name, ".json" added; "" when nothing usable is left.</summary>
        public static string FileName(string name) => CatalogueInfo.SafeName(name);

        /// <summary>A file name as the page may ask for it: exactly what FileName makes, so no folder or other file can be reached.</summary>
        public static bool GoodFile(string file) => !string.IsNullOrEmpty(file) && FileName(file) == file;

        /// <summary>
        /// Answers POST /api/rehearse {"scenario": scenario text, "script": rehearsal text}. status is 400 only when the
        /// request itself is not usable; a refused scenario or script is an answer (200) that says which and why.
        /// </summary>
        public static string Rehearse(string body, out int status)
        {
            status = 400;
            string scenario = null, script = null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body ?? "", Lenient))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return Error("a rehearsal is sent as {scenario, script}");
                    if (root.TryGetProperty("scenario", out JsonElement sc) && sc.ValueKind == JsonValueKind.String) scenario = sc.GetString();
                    if (root.TryGetProperty("script", out JsonElement sp) && sp.ValueKind == JsonValueKind.String) script = sp.GetString();
                }
            }
            catch (JsonException) { return Error("the request is not JSON"); }
            if (string.IsNullOrEmpty(scenario)) return Error("the scenario text is missing; nothing was run");
            if (string.IsNullOrEmpty(script)) return Error("the rehearsal text is missing; nothing was run");
            if (scenario.Length > RevisionFile.MaxText) return Error("the scenario text is too long to be a scenario; nothing was run");
            if (script.Length > MaxText) return Error("the rehearsal text is too long to be a rehearsal; nothing was run");
            status = 200;
            return Run(scenario, script);
        }

        /// <summary>
        /// Reads the scenario as the Director reads it at a mission's start, reads the script against it and plays it.
        /// The answer always names the scenario's fingerprint and revision, and whether the script was written against
        /// this text: a script keeps the fingerprint it was written against, so a changed scenario is said, not hidden.
        /// </summary>
        public static string Run(string scenarioText, string scriptText)
        {
            var sb = new StringBuilder(2048);
            string hash = ScenarioFile.Hash(scenarioText);
            List<Finding> read = ScenarioFile.Read(scenarioText, out MissionPlan plan);
            if (plan == null)
            {
                Refused(sb, "scenario", "the scenario would be refused at a mission's start, so it cannot be rehearsed: " + First(read), hash, read);
                return sb.ToString();
            }
            List<Finding> heard = Rehearsal.Read(scriptText, out RehearsalScript s, plan);
            if (s == null)
            {
                Refused(sb, "script", "the rehearsal cannot be read: " + First(heard), hash, heard);
                return sb.ToString();
            }
            RehearsalResult r = Rehearsal.Run(plan, s);
            string match = Match(s, hash, plan.Revision, out string why);

            sb.Append("{\"passed\":").Append(r.Passed ? "true" : "false").Append(",\"refused\":\"\"");
            sb.Append(",\"blocked\":\"").Append(Guard.Esc(r.Blocked)).Append('"');
            sb.Append(",\"assumptions\":\"").Append(Guard.Esc(r.Assumptions)).Append('"');
            sb.Append(",\"scenarioHash\":\"").Append(hash).Append('"');
            sb.Append(",\"scenarioId\":\"").Append(Guard.Esc(plan.Id ?? "")).Append('"');
            sb.Append(",\"revision\":").Append(plan.Revision.ToString(Inv));
            sb.Append(",\"script\":{\"name\":\"").Append(Guard.Esc(s.Name)).Append("\",\"scenario\":\"").Append(Guard.Esc(s.Scenario))
              .Append("\",\"revision\":").Append(s.Revision.ToString(Inv)).Append(",\"players\":").Append(s.Players.ToString(Inv))
              .Append(",\"roll\":").Append(s.Roll.ToString(Inv)).Append('}');
            sb.Append(",\"match\":\"").Append(match).Append("\",\"matchWhy\":\"").Append(Guard.Esc(why)).Append('"');
            sb.Append(",\"steps\":[");
            for (int i = 0; i < r.Steps.Count; i++)
            {
                StepResult x = r.Steps[i];
                sb.Append(i > 0 ? "," : "").Append("{\"at\":").Append(Math.Round(x.At, 2).ToString(Inv))
                  .Append(",\"what\":\"").Append(Guard.Esc(x.What)).Append("\",\"expected\":\"").Append(Guard.Esc(x.Expected))
                  .Append("\",\"actual\":\"").Append(Guard.Esc(x.Actual)).Append("\",\"ok\":").Append(x.Ok ? "true" : "false").Append('}');
            }
            sb.Append("],\"findings\":");
            var notes = new List<Finding>(read);
            notes.AddRange(heard);
            CatalogueInfo.WriteFindings(sb, notes);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Whether a script was written against this scenario text: Same, Changed, or Unknown when it names no fingerprint.
        /// why says it in a sentence with both revisions, for the page.
        /// </summary>
        public static string Match(RehearsalScript s, string hash, int revision, out string why)
        {
            if (string.IsNullOrEmpty(s.Scenario))
            {
                why = "This test does not record which scenario text it was written for, so a change to the scenario cannot be told.";
                return Unknown;
            }
            if (s.Scenario == hash)
            {
                why = "Written for this scenario text (revision " + revision.ToString(Inv) + ").";
                return Same;
            }
            why = "The scenario changed since this test was written: the test was written for revision " + s.Revision.ToString(Inv) +
                  ", this is revision " + revision.ToString(Inv) + (s.Revision == revision ? " with other content" : "") +
                  ". Its steps ran against the scenario as it is now.";
            return Changed;
        }

        /// <summary>
        /// Readies a test for saving: the text must read as a rehearsal on its own (the triggers it names are checked when it
        /// runs, as the scenario may have changed since). name, when given, becomes the script's name. Returns "" with the
        /// canonical text and its file name, or why it cannot be saved.
        /// </summary>
        public static string Prepare(string text, string name, out string canonical, out string file, out List<Finding> found)
        {
            canonical = ""; file = ""; found = new List<Finding>();
            if (string.IsNullOrEmpty(text)) return "the test is empty";
            if (text.Length > MaxText) return "the test is too long to be a rehearsal";
            found = Rehearsal.Read(text, out RehearsalScript s);
            if (s == null) return "the test cannot be read: " + First(found);
            name = (name ?? "").Trim();
            if (name.Length > 0) s.Name = name;
            file = FileName(s.Name);
            if (file == "") return "the test needs a name with letters or digits in it";
            canonical = Rehearsal.Write(s);
            return "";
        }

        /// <summary>What the list of kept tests shows of one: everything but its steps. saved: the file's time, ISO 8601 ("": unknown).</summary>
        public static void Summary(StringBuilder sb, string file, string text, string saved)
        {
            List<Finding> found = Rehearsal.Read(text, out RehearsalScript s);
            sb.Append("{\"file\":\"").Append(Guard.Esc(file)).Append("\",\"saved\":\"").Append(Guard.Esc(saved ?? "")).Append('"');
            if (s == null) { sb.Append(",\"error\":\"").Append(Guard.Esc(First(found))).Append("\"}"); return; }
            sb.Append(",\"name\":\"").Append(Guard.Esc(s.Name)).Append("\",\"scenario\":\"").Append(Guard.Esc(s.Scenario))
              .Append("\",\"revision\":").Append(s.Revision.ToString(Inv)).Append(",\"players\":").Append(s.Players.ToString(Inv))
              .Append(",\"steps\":").Append(s.Steps.Count.ToString(Inv)).Append(",\"error\":\"\"}");
        }

        private static string First(List<Finding> found)
        {
            Finding f = found.Find(x => x.Level == Finding.Error);
            return f == null ? "no reason given" : f.ToString() + (found.FindAll(x => x.Level == Finding.Error).Count > 1 ? " (and more)" : "");
        }

        private static void Refused(StringBuilder sb, string which, string why, string hash, List<Finding> found)
        {
            sb.Append("{\"passed\":false,\"refused\":\"").Append(which).Append("\",\"why\":\"").Append(Guard.Esc(why))
              .Append("\",\"blocked\":\"\",\"assumptions\":\"\",\"scenarioHash\":\"").Append(hash).Append("\",\"steps\":[],\"findings\":");
            CatalogueInfo.WriteFindings(sb, found);
            sb.Append('}');
        }

        private static string Error(string why) => "{\"error\":\"" + Guard.Esc(why) + "\"}";
    }
}
