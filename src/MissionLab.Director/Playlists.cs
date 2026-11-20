using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MissionLab.Core;
using UnityEngine;

namespace MissionLab
{
    /// <summary>
    /// Squad-session playlists (ML-049), one file each under BepInEx/MissionLab/playlists/ (missionlab-playlist/1), and the
    /// one in use named in playlists/selected.txt, so a restart of the browser or the game keeps the agenda and its next entry.
    ///   GET  /api/playlists            the playlists, the one in use (with the run records seen since it was last read) and recent runs
    ///   POST /api/playlists/save       {file, playlist}: its name, entries and next entry; the results are the plugin's own
    ///   POST /api/playlists/select     {file}: the playlist in use ("" for none)
    ///   POST /api/playlists/mark       {record, result}: the host says how a run ended (success or failed), labelled as manual
    ///   POST /api/playlists/delete     {file}
    ///   POST /api/playlists/arm        {file, next}: sets the next mission up from that entry (game thread: LiveLink queues it)
    /// Everything but arm is files only, answered on the browser link's worker thread. Setting up goes through the existing
    /// paths (Picker.Choose, the host's option picks, Director.SetRepeat); the map and mode stay the host's choice in the lobby.
    /// </summary>
    internal static class Playlists
    {
        internal static string Dir => Path.Combine(DirectorPlugin.BaseDir, "playlists");
        internal const string Route = "/api/playlists", ArmRoute = "/api/playlists/arm";
        private const string SelectedFile = "selected.txt";
        private const long MaxBytes = 1024 * 1024;
        private const int MostPlaylists = 40, RunsListed = 30;
        // Worker threads and the game's thread (setting up, mission start) both read and write the files.
        private static readonly object Gate = new object();

        /// <summary>Worker thread. null: not a playlist request, or setting up, which the game's thread answers (Arm).</summary>
        internal static HttpResponse Handle(HttpRequest r)
        {
            if (r.Path != Route && !r.Path.StartsWith(Route + "/", StringComparison.Ordinal)) return null;
            if (r.Path == ArmRoute) return null;
            try
            {
                lock (Gate)
                {
                    if (r.Path == Route) return r.Method == "GET" ? HttpResponse.Json(Answer("")) : HttpResponse.Error(400, "GET expected");
                    if (r.Method != "POST") return HttpResponse.Error(400, "POST expected");
                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(r.BodyText ?? ""); }
                    catch (JsonException) { return HttpResponse.Error(400, "the request is not JSON"); }
                    using (doc)
                    {
                        JsonElement body = doc.RootElement;
                        if (body.ValueKind != JsonValueKind.Object) return HttpResponse.Error(400, "the request is not a JSON object");
                        switch (r.Path)
                        {
                            case Route + "/save": return Save(body);
                            case Route + "/select":
                                {
                                    string file = Name(Str(body, "file"));
                                    if (file != "" && !File.Exists(Path.Combine(Dir, file))) return HttpResponse.Error(404, "there is no playlist " + file);
                                    Directory.CreateDirectory(Dir);
                                    File.WriteAllText(Path.Combine(Dir, SelectedFile), file);
                                    return HttpResponse.Json(Answer(file == "" ? "No playlist in use." : ""));
                                }
                            case Route + "/mark":
                                {
                                    Playlist p = Selected(out string file, out string why);
                                    if (p == null) return HttpResponse.Error(409, why);
                                    if (!p.Mark(Str(body, "record"), Str(body, "result"), out string said)) return HttpResponse.Error(409, said);
                                    Write(file, p);
                                    return HttpResponse.Json(Answer(said));
                                }
                            case Route + "/delete":
                                {
                                    string file = Name(Str(body, "file"));
                                    if (file == "") return HttpResponse.Error(400, "not a playlist file name");
                                    string path = Path.Combine(Dir, file);
                                    if (File.Exists(path)) File.Delete(path);
                                    if (SelectedName() == file) File.WriteAllText(Path.Combine(Dir, SelectedFile), "");
                                    return HttpResponse.Json(Answer("Playlist " + file + " deleted."));
                                }
                            default: return HttpResponse.Error(404, "not found");
                        }
                    }
                }
            }
            catch (Exception e) { return HttpResponse.Error(500, "the playlists could not be read or written: " + e.Message); }
        }

        // ----- files

        // A playlist file name: letters, digits, '_' and '-' with ".json" (CatalogueInfo.SafeName), never a folder; "" when not one.
        private static string Name(string file)
        {
            string f = (file ?? "").Trim();
            if (f == "") return "";
            string safe = CatalogueInfo.SafeName(f);
            return safe == f ? f : "";
        }

        private static string Str(JsonElement o, string key) => o.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static string SelectedName()
        {
            string path = Path.Combine(Dir, SelectedFile);
            return File.Exists(path) ? Name(File.ReadAllText(path)) : "";
        }

        private static Playlist Load(string file, out string why)
        {
            why = "";
            string path = Path.Combine(Dir, file);
            if (file == "" || !File.Exists(path)) { why = "there is no playlist " + file; return null; }
            if (new FileInfo(path).Length > MaxBytes) { why = file + " is too large to be a playlist"; return null; }
            Playlist p = Playlist.Read(File.ReadAllText(path), out string bad);
            if (p == null) why = file + " cannot be used: " + bad;
            return p;
        }

        private static Playlist Selected(out string file, out string why)
        {
            file = SelectedName();
            if (file == "") { why = "no playlist is in use"; return null; }
            return Load(file, out why);
        }

        // Written beside the file and then moved into place, so a crash never leaves half a playlist under its name.
        private static void Write(string file, Playlist p)
        {
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, file), temp = path + ".part";
            File.WriteAllText(temp, p.ToJson());
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        // The run records on disk, oldest first. A record being written at this moment is skipped this time.
        private static List<RunSummary> Runs()
        {
            var list = new List<RunSummary>();
            try
            {
                if (!Directory.Exists(RunRecord.Dir)) return list;
                var files = new List<string>(Directory.GetFiles(RunRecord.Dir, "*.run.json"));
                files.Sort(StringComparer.Ordinal);
                foreach (string f in files)
                {
                    try { RunSummary s = RunSummary.Read(Path.GetFileName(f), File.ReadAllText(f)); if (s != null) list.Add(s); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return list;
        }

        /// <summary>Counts the run records seen since; written back only when something changed. Returns what changed ("" for nothing).</summary>
        private static string Reconcile(string file, Playlist p, List<RunSummary> runs)
        {
            string said = "";
            foreach (RunSummary r in runs)
            {
                string one = p.Take(r, Director.Session ?? "");
                if (one != "") said = one;
            }
            if (said != "") Write(file, p);
            return said;
        }

        private static string ScenarioText(string value)
        {
            string path = Path.Combine(Catalogue.Dir, value + ".json");
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception) { return null; }
        }

        // ----- answers

        private static string Q(string s) => "\"" + Guard.Esc(s ?? "") + "\"";

        /// <summary>The page's view: every playlist, the one in use as it stands after the runs seen, and the recent runs.</summary>
        private static string Answer(string said)
        {
            var sb = new StringBuilder(4096);
            string selected = SelectedName(), trouble = "";
            List<RunSummary> runs = Runs();
            Playlist p = null;
            if (selected != "")
            {
                p = Load(selected, out trouble);
                if (p != null) { string seen = Reconcile(selected, p, runs); if (said == "") said = seen; }
            }
            sb.Append("{\"selected\":").Append(Q(p != null ? selected : "")).Append(",\"playlists\":[");
            int n = 0;
            if (Directory.Exists(Dir))
            {
                var files = new List<string>(Directory.GetFiles(Dir, "*.json", SearchOption.TopDirectoryOnly));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string path in files)
                {
                    if (n >= MostPlaylists) break;
                    string file = Path.GetFileName(path);
                    if (Name(file) != file) continue;
                    string bad = "";
                    Playlist one = file == selected && p != null ? p : Load(file, out bad);
                    sb.Append(n++ > 0 ? "," : "").Append("{\"file\":").Append(Q(file)).Append(",\"name\":").Append(Q(one != null ? one.Name : ""))
                      .Append(",\"entries\":").Append(one != null ? one.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0")
                      .Append(",\"why\":").Append(Q(one != null ? "" : bad)).Append('}');
                }
            }
            sb.Append("],\"playlist\":").Append(p != null ? p.ToJson() : "null").Append(",\"trouble\":").Append(Q(trouble)).Append(",\"runs\":[");
            for (int i = runs.Count - 1, k = 0; i >= 0 && k < RunsListed; i--, k++)
            {
                RunSummary r = runs[i];
                sb.Append(k > 0 ? "," : "").Append("{\"file\":").Append(Q(r.File)).Append(",\"scenario\":").Append(Q(r.Scenario)).Append(",\"scenarioId\":").Append(Q(r.ScenarioId))
                  .Append(",\"outcome\":").Append(Q(r.Outcome)).Append(",\"result\":").Append(Q(r.Result)).Append(",\"started\":").Append(Q(r.Started)).Append(",\"hash\":").Append(Q(r.Hash)).Append('}');
            }
            return sb.Append("],\"said\":").Append(Q(said)).Append('}').ToString();
        }

        /// <summary>
        /// {file, playlist}: the page's name, entries and next entry. A new file is named after the playlist and never written
        /// over another. The results stay the plugin's own; the next entry stays set up only when it is the same entry as before.
        /// </summary>
        private static HttpResponse Save(JsonElement body)
        {
            if (!body.TryGetProperty("playlist", out JsonElement sent) || sent.ValueKind != JsonValueKind.Object) return HttpResponse.Error(400, "a playlist is sent as {file, playlist}");
            Playlist p = Playlist.Read(sent.GetRawText(), out string why);
            if (p == null) return HttpResponse.Error(400, "the playlist " + why + "; nothing was saved");
            if (p.Name == "") return HttpResponse.Error(400, "a playlist needs a name; nothing was saved");
            string file = Name(Str(body, "file"));
            if (Str(body, "file") != "" && file == "") return HttpResponse.Error(400, "not a playlist file name; nothing was saved");
            Playlist was = null;
            if (file != "" && File.Exists(Path.Combine(Dir, file)))
            {
                was = Load(file, out string bad);
                if (was == null) return HttpResponse.Error(409, bad + "; nothing was written over it");
            }
            else if (file == "")
            {
                string stem = CatalogueInfo.SafeName(p.Name);
                if (stem == "") stem = "playlist.json";
                stem = stem.Substring(0, stem.Length - 5);
                file = stem + ".json";
                for (int i = 2; File.Exists(Path.Combine(Dir, file)); i++) file = stem + "-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json";
            }
            if (was != null)
            {
                p.Results = was.Results;
                bool same = was.Armed && p.Next == was.Next && p.Current != null && was.Current != null && Playlist.EntryText(p.Current) == Playlist.EntryText(was.Current);
                p.Armed = same; p.ArmedAfter = same ? was.ArmedAfter : "";
            }
            else { p.Results = new List<PlaylistResult>(); p.Armed = false; p.ArmedAfter = ""; }
            Write(file, p);
            if (was == null || SelectedName() == "") File.WriteAllText(Path.Combine(Dir, SelectedFile), file);     // a new playlist is the one in use
            return HttpResponse.Json(Answer(was == null ? "Playlist " + p.Name + " saved as " + file + "." : (was.Armed && !p.Armed ? "Saved. The next entry changed, so set it up again for the next mission." : "")));
        }

        // ----- setting the next mission up (game thread)

        /// <summary>
        /// Game thread (a LiveLink job): {file, next}. Refused, with nothing changed in the game, when the entry's scenario is
        /// missing or changed since it was pinned, or its dice cannot be set as asked. Otherwise the next mission's scenario,
        /// options and dice come from the entry, and the host is told which map and mode to choose.
        /// </summary>
        internal static HttpResponse Arm(string bodyText)
        {
            string file = "", said;
            int next = -1;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(bodyText ?? ""))
                {
                    JsonElement b = doc.RootElement;
                    if (b.ValueKind == JsonValueKind.Object)
                    {
                        file = Name(Str(b, "file"));
                        if (b.TryGetProperty("next", out JsonElement n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out int at)) next = at;
                    }
                }
            }
            catch (JsonException) { return HttpResponse.Error(400, "the request is not JSON"); }
            lock (Gate)
            {
                if (file == "") return HttpResponse.Error(400, "no playlist named");
                Playlist p = Load(file, out string why);
                if (p == null) return HttpResponse.Error(404, why);
                if (next < 0 || next >= p.Entries.Count) return HttpResponse.Error(400, "the playlist has no entry " + (next + 1));
                PlaylistEntry e = p.Entries[next];
                string identity = Playlist.Identity(e, ScenarioText(e.File), out string changed);
                if (identity != Playlist.Same)
                    return HttpResponse.Json("{\"error\":" + Q(Playlist.NameOf(e) + " was not set up: " + changed + ". Choose to use the file as it is now, pick another mission or remove the entry.") + ",\"identity\":" + Q(identity) + "}", 409);
                List<RunSummary> runs = Runs();
                if (!p.SeedFor(e, runs, out string repeat, out string seedWhy))
                    return HttpResponse.Json("{\"error\":" + Q(Playlist.NameOf(e) + " was not set up: " + seedWhy + ". Change its dice or pick another entry.") + ",\"seed\":" + Q(e.Seed) + "}", 409);
                if (!Picker.Choose(e.File)) return HttpResponse.Json("{\"error\":" + Q("the game does not list scenarios/" + e.File + ".json; nothing was changed") + "}", 409);
                Picker.UsePicks(e.Picks);
                string dice = Director.SetRepeat(repeat);
                Reconcile(file, p, runs);        // a run of the entry set up before is counted for it first, not for this one
                p.Next = next; p.Armed = true;
                p.ArmedAfter = runs.Count > 0 ? runs[runs.Count - 1].File : "";
                Write(file, p);
                File.WriteAllText(Path.Combine(Dir, SelectedFile), file);
                said = "Next mission: " + Playlist.NameOf(e) + ". Host " + MapWords(e) + " in the game; " + dice + ".";
                Tell(said);
                return HttpResponse.Json(Answer(said));
            }
        }

        private static string MapWords(PlaylistEntry e)
        {
            string map = e.Map != "" ? e.Map : e.NavFile != "" ? e.NavFile : "the scenario's map";
            return map + " as a PvE raid";
        }

        private static void Tell(string text)
        {
            DirectorPlugin.L.LogInfo("playlist: " + text);
            try { Director.Banner = "MissionLab playlist: " + text; Director.BannerUntil = Time.unscaledTime + 8f; } catch { }
        }

        /// <summary>
        /// Game thread, at a mission's start, before its setup is read. Runs seen since are counted first. When the entry set up
        /// is still the next-mission choice and still the pinned content, it puts back the options and dice a restart lost; a
        /// choice the host changed by hand wins, and changed content disarms the entry rather than playing it under its name.
        /// </summary>
        internal static void BeforeMission()
        {
            lock (Gate)
            {
                Playlist p = Selected(out string file, out string why);
                if (p == null || !p.Armed) return;
                Reconcile(file, p, Runs());
                PlaylistEntry e = p.Current;
                if (!p.Armed || e == null) return;
                string now = Picker.Current();
                if (!string.Equals(now, e.File, StringComparison.OrdinalIgnoreCase))
                {
                    DirectorPlugin.L.LogInfo("playlist: the next-mission choice is '" + now + "', not " + e.File + ", so this mission does not count for " + Playlist.NameOf(e));
                    return;
                }
                if (Playlist.Identity(e, ScenarioText(e.File), out string changed) != Playlist.Same)
                {
                    p.Armed = false; Write(file, p);
                    Tell(Playlist.NameOf(e) + " is no longer set up: " + changed + ". This mission uses the file as it is now and does not count for the playlist");
                    return;
                }
                if (Picker.Picks().Count == 0 && e.Picks.Count > 0) Picker.UsePicks(e.Picks);
                if (e.Seed != Playlist.NewRoll && Director.RepeatFile == "")
                {
                    // A repeat or retry that cannot be set is not played as a new roll under the entry's name.
                    if (p.SeedFor(e, Runs(), out string repeat, out string seedWhy)) DirectorPlugin.L.LogInfo("playlist: " + Director.SetRepeat(repeat));
                    else
                    {
                        p.Armed = false; Write(file, p);
                        Tell(Playlist.NameOf(e) + " is no longer set up: " + seedWhy + ". This mission gets a new roll and does not count for the playlist");
                    }
                }
            }
        }
    }
}
