using System;
using System.Collections.Generic;
using MissionLab.Core;

/// <summary>
/// Squad-session playlists (ML-049): the file survives save and load, a run record moves the playlist on at most once, an
/// end the game did not see stays unknown or incomplete until the host marks it (labelled as the host's), a scenario that
/// changed since it was pinned needs the host's choice, and each dice setting gives the right run record for the repeat.
/// Reading the files, setting the game's next mission and the lobby are the plugin's (Playlists.cs) and are not run here.
/// </summary>
static class Ml049Tests
{
    private const string Text3 = "{\"schema\":\"missionlab-plan/1\",\"id\":\"sc-raid\",\"revision\":3,\"name\":\"Quiet raid\",\"spawns\":[{\"pos\":[0,0,0]}]}";
    private const string Text4 = "{\"schema\":\"missionlab-plan/1\",\"id\":\"sc-raid\",\"revision\":4,\"name\":\"Quiet raid\",\"spawns\":[{\"pos\":[0,0,0]}]}";

    private static PlaylistEntry Entry(string label, string file, string id, string text, string seed = Playlist.NewRoll, string record = "")
    {
        CatalogueInfo.Identity(text, out string sid, out int revision);
        return new PlaylistEntry { Label = label, File = file, ScenarioId = id, Revision = revision, Hash = ScenarioFile.Hash(text), Map = "Clean House", NavFile = "suburbv2.bytes", Seed = seed, Record = record };
    }

    private static Playlist Evening()
    {
        var p = new Playlist { Name = "Friday" };
        p.Entries.Add(Entry("Warm-up", "warmup", "sc-warm", "{\"id\":\"sc-warm\",\"revision\":1,\"spawns\":[{\"pos\":[0,0,0]}]}"));
        p.Entries.Add(Entry("Quiet raid", "raid", "sc-raid", Text3));
        p.Entries.Add(Entry("Defence", "defence", "sc-def", "{\"id\":\"sc-def\",\"revision\":2,\"spawns\":[{\"pos\":[0,0,0]}]}"));
        p.Armed = true; p.ArmedAfter = "20261120-200000_Suburb.run.json";
        return p;
    }

    private static RunSummary Run(string file, string id, string scenario, string result, string session = "m1", string outcome = "used")
        => new RunSummary { File = file, ScenarioId = id, Scenario = scenario, Outcome = outcome, Session = session, Result = result, Ended = result == "" ? "" : "2026-11-20T21:00:00Z" };

    public static void Run(Action<string, bool, string> check)
    {
        {   // Everything a playlist holds comes back the same after save and load, odd text included.
            Playlist p = Evening();
            p.Name = "Fri \"late\" ü";
            p.Entries[1].Picks["garrison"] = 1; p.Entries[1].Picks["alarm"] = 0; p.Entries[1].Squad = 4;
            p.Entries[2].Seed = Playlist.Repeat; p.Entries[2].Record = "20261119-220000_Suburb.run.json";
            p.Next = 1;
            p.Results.Add(new PlaylistResult { Record = "20261120-201000_Suburb.run.json", Entry = 0, Label = "Warm-up", Result = Playlist.Success, Manual = true, Advanced = true });
            string json = p.ToJson();
            Playlist back = Playlist.Read(json, out string why);
            check("a playlist is read back", back != null, why);
            check("and written again as the same text", back != null && back.ToJson() == json, back == null ? "" : back.ToJson());
            check("its pins, options, squad and dice are kept", back != null && back.Entries.Count == 3 && back.Entries[1].Revision == 3 && back.Entries[1].Hash == ScenarioFile.Hash(Text3)
                  && back.Entries[1].Picks["garrison"] == 1 && back.Entries[1].Squad == 4 && back.Entries[2].Seed == Playlist.Repeat && back.Entries[2].Record == "20261119-220000_Suburb.run.json", json);
            check("the next entry, its set-up and the host's mark are kept", back != null && back.Next == 1 && back.Armed && back.ArmedAfter == p.ArmedAfter && back.Results.Count == 1 && back.Results[0].Manual && back.Results[0].Advanced, json);
            Playlist later = Playlist.Read("{\"schema\":\"missionlab-playlist/2\",\"name\":\"x\",\"entries\":[]}", out string laterWhy);
            check("a playlist from a later MissionLab is refused, not read as this one", later == null && laterWhy.Contains("another MissionLab version"), laterWhy);
            Playlist hostile = Playlist.Read("{\"schema\":\"missionlab-playlist/1\",\"name\":\"x\",\"next\":9,\"armed\":true,\"entries\":[{\"file\":\"../../BepInEx/config\"},{\"file\":\"raid\"}],\"results\":[{\"record\":\"a.run.json\"},{\"record\":\"a.run.json\"}]}", out string _);
            check("an entry naming a folder is left out, the next entry is kept within the list, and a record counts once", hostile != null && hostile.Entries.Count == 1 && hostile.Entries[0].File == "raid" && hostile.Next == 1 && !hostile.Armed && hostile.Results.Count == 1,
                  hostile == null ? "" : hostile.ToJson());
        }
        {   // A run record moves the playlist on once, however often it is seen.
            Playlist p = Evening();
            RunSummary won = Run("20261120-203000_Suburb.run.json", "sc-warm", "warmup", Playlist.Success);
            string said = p.Take(won, "m2");
            check("a success of the entry set up moves the playlist to the next entry", p.Next == 1 && !p.Armed && said.Contains("Next: Quiet raid"), said);
            check("the same record seen again changes nothing", p.Take(won, "m2") == "" && p.Next == 1 && p.Results.Count == 1, p.ToJson());
            p.Armed = true; p.ArmedAfter = won.File;
            check("nor once the next entry is set up", p.Take(won, "m3") == "" && p.Next == 1, p.ToJson());
            check("nor when the host marks that record as well", p.Mark(won.File, Playlist.Success, out string marked) && p.Next == 1, marked);
            Playlist again = Playlist.Read(p.ToJson(), out string _);
            check("nor after the playlist has been saved and read back", again != null && again.Take(won, "m3") == "" && again.Next == 1, again == null ? "" : again.ToJson());
            Playlist q = Evening();
            check("a run from before the entry was set up does not count", q.Take(Run("20261120-190000_Suburb.run.json", "sc-warm", "warmup", Playlist.Success), "") == "" && q.Next == 0, q.ToJson());
            check("a run of another scenario does not count", q.Take(Run("20261120-203000_Suburb.run.json", "sc-def", "defence", Playlist.Success), "") == "" && q.Next == 0, q.ToJson());
            q.Armed = false;
            check("nothing counts while no entry is set up", q.Take(Run("20261120-204000_Suburb.run.json", "sc-warm", "warmup", Playlist.Success), "") == "" && q.Next == 0 && q.Results.Count == 0, q.ToJson());
        }
        {   // An end the game did not see stays unknown or incomplete, and only the host's mark (labelled) moves on.
            Playlist p = Evening();
            RunSummary over = Run("20261120-203000_Suburb.run.json", "sc-warm", "warmup", "over");
            p.Take(over, "");
            PlaylistResult r = p.Results.Count == 1 ? p.Results[0] : null;
            check("an end without a known result is unknown and does not move on", r != null && r.Result == Playlist.Unknown && p.Next == 0 && p.Armed, p.ToJson());
            check("seen again it stays unknown", p.Take(over, "") == "" && p.Results[0].Result == Playlist.Unknown && p.Next == 0, p.ToJson());
            check("the host's mark moves on once and says it is the host's", p.Mark(over.File, Playlist.Success, out string said) && p.Next == 1 && p.Results[0].Manual && Playlist.ResultWords(p.Results[0]).Contains("marked by the host"), said);
            check("a second mark does not move on again", p.Mark(over.File, Playlist.Failed, out string _) && p.Next == 1, p.ToJson());
            check("a later look at the record does not replace the host's word", p.Take(Run(over.File, "sc-warm", "warmup", Playlist.Failed), "") == "" && p.Results[0].Result == Playlist.Failed && p.Results[0].Manual, p.ToJson());

            Playlist q = Evening();
            RunSummary running = Run("20261120-203000_Suburb.run.json", "sc-warm", "warmup", "", "m7");
            check("a run still going on is not counted yet", q.Take(running, "m7") == "" && q.Results.Count == 0, q.ToJson());
            q.Take(running, "m8");
            check("a run whose mission is over without an end seen is incomplete and does not move on", q.Results.Count == 1 && q.Results[0].Result == Playlist.Incomplete && q.Next == 0, q.ToJson());
            q.Take(Run(running.File, "sc-warm", "warmup", Playlist.Failed, "m7"), "m8");
            check("its end, written later, fills it in and moves on once", q.Results[0].Result == Playlist.Failed && !q.Results[0].Manual && q.Next == 1, q.ToJson());
            check("a refused run is not counted as played", Evening().Take(Run("20261120-203000_Suburb.run.json", "sc-warm", "warmup", "", "m1", "refused"), "").Contains("refused"), "");
            Playlist s = Evening();
            s.Take(Run("20261120-203000_Suburb.run.json", "sc-warm", "warmup", "", "m1", "refused"), "");
            check("and cannot be marked as a result", !s.Mark("20261120-203000_Suburb.run.json", Playlist.Success, out string no) && s.Next == 0, no);
        }
        {   // A scenario changed or gone since it was pinned needs the host's choice.
            PlaylistEntry e = Entry("Quiet raid", "raid", "sc-raid", Text3);
            check("the pinned text is the same", Playlist.Identity(e, Text3, out string same) == Playlist.Same, same);
            check("a new revision needs resolving and says which", Playlist.Identity(e, Text4, out string why) == Playlist.Changed && why.Contains("revision 3 when it was added, revision 4 now"), why);
            string edited = Text3.Replace("[0,0,0]", "[1,0,0]");
            check("changed text under the same revision needs resolving too", Playlist.Identity(e, edited, out string quiet) == Playlist.Changed && quiet.Contains("same revision"), quiet);
            check("a file that is gone is missing", Playlist.Identity(e, null, out string gone) == Playlist.Missing && gone.Contains("raid.json"), gone);
            e.Hash = "";
            check("an entry without a fingerprint is not taken as unchanged", Playlist.Identity(e, Text3, out string blind) == Playlist.Changed, blind);
            Playlist p = Evening();
            RunSummary edited2 = Run("20261120-203000_Suburb.run.json", "sc-warm", "warmup", Playlist.Success);
            edited2.Hash = "fnv1a64:0000000000000000";        // the warm-up's file was edited after it was set up
            check("a run played from other content than was pinned does not count", p.Take(edited2, "") == "" && p.Next == 0 && p.Results.Count == 0, p.ToJson());
        }
        {   // The dice: retry takes the newest failed run of the scenario, by the game's record or the host's mark.
            Playlist p = Evening();
            PlaylistEntry raid = p.Entries[1];
            raid.Seed = Playlist.RetryFailed;
            var runs = new List<RunSummary>
            {
                Run("20261120-190000_Suburb.run.json", "sc-raid", "raid", Playlist.Failed),
                Run("20261120-193000_Suburb.run.json", "sc-raid", "raid", Playlist.Failed),
                Run("20261120-194000_Suburb.run.json", "sc-raid", "raid", Playlist.Success),
                Run("20261120-195000_Suburb.run.json", "sc-def", "defence", Playlist.Failed),
                Run("20261120-195500_Suburb.run.json", "sc-raid", "raid", "", "m9", "refused"),
            };
            check("retry repeats the newest failed run of that scenario", p.SeedFor(raid, runs, out string file, out string why) && file == "20261120-193000_Suburb.run.json", file + " " + why);
            runs.Add(Run("20261120-196000_Suburb.run.json", "sc-raid", "raid", "over"));
            p.Results.Add(new PlaylistResult { Record = "20261120-196000_Suburb.run.json", Entry = 1, Result = Playlist.Failed, Manual = true });
            check("a run the host marked as failed is retried too", p.SeedFor(raid, runs, out string marked, out string _) && marked == "20261120-196000_Suburb.run.json", marked);
            var warm = new PlaylistEntry { File = "warmup", ScenarioId = "sc-warm", Seed = Playlist.RetryFailed };
            check("with no failed run on record, retry is refused rather than rolled anew", !p.SeedFor(warm, runs, out string none, out string noWhy) && none == "" && noWhy.Contains("no failed run"), noWhy);

            PlaylistEntry fresh = p.Entries[0];
            check("a new roll repeats no run", p.SeedFor(fresh, runs, out string newFile, out string _) && newFile == "", newFile);
            var rep = new PlaylistEntry { File = "raid", ScenarioId = "sc-raid", Seed = Playlist.Repeat, Record = "20261120-194000_Suburb.run.json" };
            check("repeat uses exactly the run chosen, not the newest", p.SeedFor(rep, runs, out string repFile, out string _) && repFile == "20261120-194000_Suburb.run.json", repFile);
            rep.Record = "20261101-100000_Suburb.run.json";
            check("a repeat whose record is gone is refused", !p.SeedFor(rep, runs, out string goneFile, out string goneWhy) && goneFile == "" && goneWhy.Contains("no longer on disk"), goneWhy);
            rep.Record = "20261120-195500_Suburb.run.json";
            check("a repeat of a refused run is refused", !p.SeedFor(rep, runs, out string _, out string refusedWhy) && refusedWhy.Contains("refused"), refusedWhy);
            rep.Record = "20261120-195000_Suburb.run.json";
            check("a repeat of another scenario's run is refused", !p.SeedFor(rep, runs, out string _, out string otherWhy) && otherWhy.Contains("another scenario"), otherWhy);
            rep.Record = "";
            check("a repeat with no run chosen is refused", !p.SeedFor(rep, runs, out string _, out string emptyWhy) && emptyWhy.Contains("no run is chosen"), emptyWhy);
            check("a dice setting this version does not know is refused", !p.SeedFor(new PlaylistEntry { File = "raid", Seed = "chaos" }, runs, out string _, out string oddWhy) && oddWhy.Contains("chaos"), oddWhy);
        }
    }
}
