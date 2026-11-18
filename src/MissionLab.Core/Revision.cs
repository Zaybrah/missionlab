using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>
    /// A saved revision of a scenario (ML-014): the author's explicit save with a note, kept under
    /// BepInEx/MissionLab/revisions/&lt;scenario id&gt;/r&lt;n&gt;.json. It holds the scenario text exactly as the page wrote it,
    /// so its fingerprint (ScenarioFile.Hash) can be matched with the run records that played that text.
    /// Pure: the plugin does the file work and calls this from the browser link's worker threads.
    /// </summary>
    public static class RevisionFile
    {
        public const string Schema = "missionlab-revision/1";
        public const int MaxText = 4 * 1024 * 1024;     // a scenario is far smaller; a bigger body is a mistake, not a scenario
        public const int MaxNote = 200;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        public sealed class Entry { public string Id = "", Note = "", Saved = "", Hash = "", Text = ""; public int N; }

        /// <summary>The scenario id rule of the format (contract §3): 1 to 64 of A-Z a-z 0-9 _ -. It is also a folder name, so nothing else may pass.</summary>
        public static bool GoodId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id) if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
            return true;
        }

        /// <summary>
        /// Which scenario and which revision number a scenario text names. "" when both are usable, otherwise why not.
        /// A missionlab-plan/2 file keeps its body under "mission"; a plan/1 file has it at the root.
        /// </summary>
        public static string Identify(string text, out string id, out int n)
        {
            id = ""; n = 0;
            if (string.IsNullOrEmpty(text)) return "the scenario text is empty";
            if (text.Length > MaxText) return "the scenario text is too long to be a scenario";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text, Lenient))
                {
                    JsonElement body = doc.RootElement;
                    if (body.ValueKind != JsonValueKind.Object) return "the scenario text is not a JSON object";
                    if (body.TryGetProperty("mission", out JsonElement inner) && inner.ValueKind == JsonValueKind.Object) body = inner;
                    if (!body.TryGetProperty("id", out JsonElement idv) || idv.ValueKind != JsonValueKind.String || !GoodId(idv.GetString()))
                        return "the scenario has no usable id (1 to 64 letters, digits, - or _)";
                    id = idv.GetString();
                    if (!body.TryGetProperty("revision", out JsonElement rv) || rv.ValueKind != JsonValueKind.Number || !rv.TryGetInt32(out n) || n < 1)
                    { n = 0; return "the scenario has no revision number of 1 or more"; }
                    return "";
                }
            }
            catch (JsonException e) { return "the scenario text is not JSON: " + e.Message; }
        }

        /// <summary>A note as kept: one line, at most MaxNote characters.</summary>
        public static string CleanNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return "";
            var sb = new StringBuilder(note.Length);
            bool space = false;
            foreach (char c in note)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
            string s = sb.ToString();
            return s.Length > MaxNote ? s.Substring(0, MaxNote) : s;
        }

        public static string FileName(int n) => "r" + n.ToString(Inv) + ".json";

        /// <summary>The number a revision file name stands for, 0 when it is not one.</summary>
        public static int NumberOf(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.Length < 7 || fileName[0] != 'r' || !fileName.EndsWith(".json", StringComparison.Ordinal)) return 0;
            string digits = fileName.Substring(1, fileName.Length - 6);
            foreach (char c in digits) if (c < '0' || c > '9') return 0;
            return digits.Length > 0 && digits.Length <= 9 && int.TryParse(digits, NumberStyles.None, Inv, out int n) && n > 0 ? n : 0;
        }

        public static string Write(Entry e)
        {
            var sb = new StringBuilder((e.Text ?? "").Length + 256);
            sb.Append("{\"schema\":\"").Append(Schema).Append("\",\"id\":\"").Append(Guard.Esc(e.Id)).Append("\",\"n\":").Append(e.N.ToString(Inv))
              .Append(",\"note\":\"").Append(Guard.Esc(e.Note)).Append("\",\"saved\":\"").Append(Guard.Esc(e.Saved)).Append("\",\"hash\":\"").Append(Guard.Esc(e.Hash))
              .Append("\",\"scenario\":\"").Append(Guard.Esc(e.Text)).Append("\"}");
            return sb.ToString();
        }

        /// <summary>What the list shows of a revision: everything but the scenario text. runsJson is a JSON array of the runs that played it.</summary>
        public static string SummaryJson(Entry e, string runsJson)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"n\":").Append(e.N.ToString(Inv)).Append(",\"note\":\"").Append(Guard.Esc(e.Note)).Append("\",\"saved\":\"").Append(Guard.Esc(e.Saved))
              .Append("\",\"hash\":\"").Append(Guard.Esc(e.Hash)).Append("\",\"runs\":").Append(string.IsNullOrEmpty(runsJson) ? "[]" : runsJson).Append('}');
            return sb.ToString();
        }

        /// <summary>Reads a revision file; null when it is not one this version knows.</summary>
        public static Entry Read(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json, Lenient))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return null;
                    string S(string k) => r.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                    if (S("schema") != Schema) return null;
                    var e = new Entry { Id = S("id"), Note = S("note"), Saved = S("saved"), Hash = S("hash"), Text = S("scenario") };
                    if (!r.TryGetProperty("n", out JsonElement n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt32(out e.N) || e.N < 1 || !GoodId(e.Id)) return null;
                    return e;
                }
            }
            catch (JsonException) { return null; }
        }

        /// <summary>
        /// Whether a run record played this revision: the same text (the authored fingerprint), or the same scenario id and
        /// revision number. The second covers a file the game saved itself, whose text differs from the page's but is the same revision.
        /// </summary>
        public static bool Played(Entry rev, string runHash, string runScenarioId, int runRevision)
        {
            if (rev == null) return false;
            if (!string.IsNullOrEmpty(runHash) && runHash == rev.Hash) return true;
            return rev.N > 0 && runRevision == rev.N && !string.IsNullOrEmpty(runScenarioId) && runScenarioId == rev.Id;
        }
    }
}
