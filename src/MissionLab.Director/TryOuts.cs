using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Il2Generic = Il2CppSystem.Collections.Generic;

namespace MissionLab
{
    /// <summary>
    /// One-click experiments from the browser. Each calls one thing the game already has and says what happened, so a
    /// single run shows which of them can be made into a real feature. Nothing here runs unless the Game Master asks.
    /// What each call does is known from the game's class listing only; the answers come from trying.
    /// </summary>
    internal static class TryOuts
    {
        /// <summary>What the last try-out reported, shown in the browser until the next one.</summary>
        internal static string Last = "";

        private static List<string> _things;

        // Things placed by hand are looked at again two seconds later: still there, fallen through the floor, visible?
        private struct Placed { public GameObject Go; public string Name; public Vector3 At; public float When; }
        private static readonly List<Placed> Watch = new List<Placed>();

        internal static void Reset() { Last = ""; Watch.Clear(); _marked = false; _lastAction = "the start of the mission"; _endAt = -1f; Orders.Clear(); }

        // A failed mission: the players are told, and a few seconds later the game's own "end operation" takes everyone out.
        private static float _endAt = -1f;
        private static readonly List<GameObject> Orders = new List<GameObject>();

        // The game marks a run as cheated by itself after some calls. Which ones is found out by watching the flag.
        private static bool _marked;
        private static string _lastAction = "the start of the mission";
        internal static void Did(string what) { _lastAction = what; }

        private static void WatchFlag()
        {
            bool now;
            try { OperationsManager ops = OperationsManager.singleton; if (ops == null) return; now = ops.HasCheated; } catch { return; }
            if (now == _marked) return;
            _marked = now;
            string said = now ? "the game now marks this run as cheated; the last thing MissionLab did was: " + _lastAction
                              : "the game no longer marks this run as cheated";
            DirectorPlugin.L.LogInfo("cheated flag: " + said);
            Replay.Event("note", "cheated flag: " + said);
            GameMaster.Say(said);
        }

        internal static void Tick(float now)
        {
            WatchFlag();
            if (_endAt >= 0f && now >= _endAt)
            {
                _endAt = -1f;
                try { GameManagerNetwork g = GameManagerNetwork.instance; if (g != null) g.EndOperation(); DirectorPlugin.L.LogInfo("end: the operation was ended after the failed message"); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the operation could not be ended: " + e.Message); }
            }
            for (int i = Watch.Count - 1; i >= 0; i--)
            {
                Placed p = Watch[i];
                if (now - p.When < 2f) continue;
                Watch.RemoveAt(i);
                string said;
                try
                {
                    if (p.Go == null) said = "'" + p.Name + "' is gone again, 2 s after it was placed";
                    else
                    {
                        CultureInfo inv = CultureInfo.InvariantCulture;
                        Vector3 at = p.Go.transform.position;
                        var parts = p.Go.GetComponentsInChildren<Renderer>(true);
                        int shown = 0;
                        for (int k = 0; parts != null && k < parts.Length; k++)
                            if (parts[k] != null && parts[k].enabled && parts[k].gameObject.activeInHierarchy) shown++;
                        float moved = Vector3.Distance(at, p.At);
                        said = "'" + p.Name + "' 2 s later: " + (p.Go.activeInHierarchy ? "still there" : "there but switched off") +
                               (moved > 0.5f ? ", " + moved.ToString("0.0", inv) + " m from where it was put" + (at.y < p.At.y - 0.5f ? " (it fell to height " + at.y.ToString("0.0", inv) + ")" : "") : ", where it was put") +
                               ", " + shown + " of " + (parts == null ? 0 : parts.Length) + " parts drawn";
                    }
                }
                catch (Exception e) { said = "'" + p.Name + "' could not be looked at again: " + e.Message; }
                Last = said;
                Replay.Event("note", "try-out: " + said);
                GameMaster.Say(said);
            }
        }

        // Not for placing by hand: the game's own managers, players, enemies and vehicles (those have their own way in).
        private static readonly string[] NotThese =
        {
            "GameManagerNetwork", "OperationsManager", "PlayerController", "PlayerBoardItem", "SceneRandomiser", "BOT V2",
            "InfiltrationVehicleAnimator", "Full Body Interaction", "Optimisation Testing",
        };

        private static bool Allowed(GameObject prefab)
        {
            if (prefab == null) return false;
            foreach (string no in NotThese) if (prefab.name.IndexOf(no, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            try { if (prefab.GetComponentInChildren<VehicleAI>(true) != null) return false; } catch { }
            return true;
        }

        /// <summary>The names of everything the game can place for all players, as it registers them with the network.</summary>
        internal static List<string> Things()
        {
            if (_things != null && _things.Count > 0) return _things;
            var list = new List<string>();
            try
            {
                Mirror.NetworkManager net = Mirror.NetworkManager.singleton;
                var all = net == null ? null : net.spawnPrefabs;
                for (int i = 0; all != null && i < all.Count; i++)
                    if (Allowed(all[i]) && !list.Contains(all[i].name)) list.Add(all[i].name);
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return _things = list;
        }

        internal static string ThingsJson()
        {
            var sb = new StringBuilder("[");
            List<string> names = Things();
            for (int i = 0; i < names.Count; i++)
                sb.Append(i > 0 ? "," : "").Append('"').Append(names[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            return sb.Append(']').ToString();
        }

        internal static string Run(string what, string arg, bool hasPos, Vector3 pos, RaidManager raid, GameManager gm)
        {
            string said;
            try
            {
                switch (what)
                {
                    case "power": said = Power((arg ?? "") == "on"); break;
                    case "message": said = Message(raid, arg); break;
                    case "walk": said = Walk(gm); break;
                    case "doors": said = OpenDoors(); break;
                    case "thing": said = Thing(arg, hasPos, pos); break;
                    case "sides": said = Sides(gm); break;
                    case "respawn": said = Respawn(); break;
                    case "fail": said = End(false); break;
                    case "complete": said = End(true); break;
                    case "endop": said = EndOperation(); break;
                    default: said = "unknown try-out '" + what + "'"; break;
                }
            }
            catch (Exception e) { said = "try-out '" + what + "' failed: " + e.Message; }
            Last = said; Did("the try-out '" + what + "'");
            Replay.Event("note", "try-out: " + said);
            return said;
        }

        // ------------------------------------------------------------------ lights
        internal static string Power(bool on)
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<BreakerSwitch>(), true);
            int boxes = 0, lights = 0; bool night = false, says = on;
            for (int i = 0; found != null && i < found.Length; i++)
            {
                BreakerSwitch b = found[i] == null ? null : found[i].TryCast<BreakerSwitch>();
                if (b == null) continue;
                // The body of the game's own "set power" request, run where the host runs it.
                try { b.UserCode_CMD_SetPowerState__Boolean(on); }
                catch { b.NetworkPowerOn = on; b.RPC_SetPowerState(on); }
                boxes++;
                try { lights += b.effectedLights == null ? 0 : b.effectedLights.Count; night |= b.isNight; says = b.PowerOn; } catch { }
            }
            if (boxes == 0) return "power: this map has no breaker box";
            return "power " + (on ? "on" : "off") + ": " + boxes + " breaker box(es) with " + lights + " lights; the game now says the power is " + (says ? "on" : "off") +
                   (night ? "" : " (it is day in this mission, so there may be little to see)");
        }

        // ------------------------------------------------------------------ a message on every screen
        internal static string Message(RaidManager raid, string text, bool red = false)
        {
            if (string.IsNullOrWhiteSpace(text)) text = "MissionLab: test message";
            text = Fill(text);
            if (text.Length > 120) text = text.Substring(0, 120);
            ExfilZone zone = null;
            var zones = raid.exfilZones;
            for (int i = 0; zones != null && i < zones.Count && zone == null; i++) zone = zones[i];
            if (zone == null) return "message: this map has no extraction zone to send it through";
            zone.RPCHint(text, red ? Popup.PopupType.RedWarning : Popup.PopupType.YellowWarning);
            return "message sent to every player's screen: \"" + text + "\"";
        }

        /// <summary>{enemies}, {players} and {time} in a message become the numbers of the moment.</summary>
        private static string Fill(string text)
        {
            if (text.IndexOf('{') < 0) return text;
            try
            {
                int enemies = 0, players = 0;
                GameManager gm = GameManager.instance; var ai = gm == null ? null : gm.allAI;
                for (int i = 0; ai != null && i < ai.Count; i++) if (ai[i] != null) enemies++;
                var all = PlayerMaster.AllPlayerMasters;
                for (int i = 0; all != null && i < all.Count; i++) if (all[i] != null && all[i].PlayerSpawnedObject != null) players++;
                int secs = Mathf.Max(0, Mathf.RoundToInt(Time.unscaledTime - Director.HandledAt));
                text = text.Replace("{enemies}", enemies.ToString()).Replace("{players}", players.ToString()).Replace("{time}", (secs / 60) + ":" + (secs % 60).ToString("00"));
            }
            catch { }
            return text;
        }

        // ------------------------------------------------------------------ enemies
        private static bool NearestBody(Vector3 from, out Transform who)
        {
            who = null; float best = float.MaxValue;
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerNetworking body = players[i] == null ? null : players[i].PlayerSpawnedObject;
                if (body == null) continue;
                float sq = (body.transform.position - from).sqrMagnitude;
                if (sq < best) { best = sq; who = body.transform; }
            }
            return who != null;
        }

        private static string Hunt(GameManager gm)
        {
            int told = 0, all = 0;
            var ai = gm.allAI;
            for (int i = 0; ai != null && i < ai.Count; i++)
            {
                BrainAI b = ai[i];
                if (b == null) continue;
                all++;
                GameMaster.EnemyPose(b, out Vector3 at, out _);
                if (!NearestBody(at, out Transform who)) continue;
                try { b.HeardTarget(who.position, Vector3.Distance(at, who.position), who); told++; } catch { }
            }
            return told == 0 ? "hunt: no enemy could be told (" + all + " alive)" : "hunt: " + told + " of " + all + " enemies were told where the nearest player is";
        }

        internal static Vector3 FirstPlayer()
        {
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerNetworking body = players[i] == null ? null : players[i].PlayerSpawnedObject;
                if (body != null) return body.transform.position;
            }
            return Vector3.zero;
        }

        internal static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return s >= 60 ? (s / 60) + (s % 60 == 0 ? (s / 60 == 1 ? " minute" : " minutes") : ":" + (s % 60).ToString("00") + " minutes") : s + " seconds";
        }

        /// <summary>
        /// Enemies that can walk get a route of one stop, spread around a spot, and set off at once; the game's own
        /// patrol takes them there. (Seen in a run: with the game's usual pause they only left after some twenty seconds.
        /// Telling them a sound was heard, by HeardTarget or ShotFired, moved nobody.)
        /// within: only enemies this close to the spot (0: any distance). only: just these enemies (null: all).
        /// </summary>
        internal static int WalkTo(GameManager gm, Vector3 to, float within, HashSet<IntPtr> only)
        {
            if (gm == null) return 0;
            int told = 0;
            var ai = gm.allAI;
            for (int i = 0; ai != null && i < ai.Count; i++)
            {
                BrainAI b = ai[i];
                if (b == null || (only != null && !only.Contains(b.Pointer))) continue;
                try
                {
                    AgentController legs = b.agent;
                    if (legs == null || !legs.entityExists) continue;                      // a gunner, or someone told to stand still
                    if (within > 0f && (b.transform.position - to).sqrMagnitude > within * within) continue;
                    float turn = told * 2.4f;                                             // spread around the spot, 2 m out
                    var stop = new GameObject("MissionLab order " + Orders.Count);
                    stop.transform.position = to + new Vector3(Mathf.Sin(turn) * 2f, 0.1f, Mathf.Cos(turn) * 2f);
                    Orders.Add(stop);
                    AIPatrol route = b.aiPatrol;
                    if (route == null) { route = new AIPatrol(); b.aiPatrol = route; }
                    var stops = new Il2Generic.List<Transform>(); stops.Add(stop.transform);
                    route.patrolPoints = stops; route.currentWayPointIndex = 0; route.Looping = false;
                    route.patrolWaitTime = 0f; route.onReachDestination = AIPatrol.OnReachDestination.MoveImmediately;
                    b.patrolTimer = 0f;
                    b.idleStates = BrainAI.IdleStates.Patrol;
                    told++;
                }
                catch { }
            }
            if (Orders.Count > 200) { for (int i = 0; i < 100; i++) try { if (Orders[i] != null) UnityEngine.Object.Destroy(Orders[i]); } catch { } Orders.RemoveRange(0, 100); }
            return told;
        }

        private static string Walk(GameManager gm)
        {
            Vector3 to = FirstPlayer();
            if (to == Vector3.zero) return "walk: no player to walk to";
            int told = WalkTo(gm, to, 0f, null);
            return told == 0 ? "walk: no enemy could be sent" : "walk: " + told + " enemies were given a one-stop route to " + P(to) + " and told to set off at once";
        }

        private static string OpenDoors()
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<DoorV2>(), false);
            int n = 0;
            for (int i = 0; found != null && i < found.Length; i++)
            {
                DoorV2 d = found[i] == null ? null : found[i].TryCast<DoorV2>();
                if (d == null || d.isDead) continue;
                try { d.UserCode_CMD_SetDoorClosed__Boolean(false); n++; } catch { try { d.RPC_SetDoorClosed(false); n++; } catch { } }
            }
            return "doors: " + n + " doors were told to open";
        }

        /// <summary>A shot that was never fired: the game is told one was heard where the first player stands.</summary>
        private static string Noise(GameManager gm)
        {
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerNetworking body = players[i] == null ? null : players[i].PlayerSpawnedObject;
                if (body == null) continue;
                Vector3 at = body.transform.position;
                gm.ShotFired(at, 80f, body.netId, false);
                return "noise: the game was told a shot was heard within 80 m of " + P(at);
            }
            return "noise: no player to make it at";
        }

        private static string P(Vector3 p)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return "(" + p.x.ToString("0.0", inv) + ", " + p.y.ToString("0.0", inv) + ", " + p.z.ToString("0.0", inv) + ")";
        }

        private static string Sides(GameManager gm)
        {
            const int other = 4;                                   // "RU" in the game's team list; players are 1, enemies 2
            int all = 0, moved = 0;
            var ai = gm.allAI;
            for (int i = 0; ai != null && i < ai.Count; i++)
            {
                BrainAI b = ai[i];
                if (b == null) continue;
                TeamIdentifier t = b.GetComponent<TeamIdentifier>();
                if (t == null) t = b.GetComponentInParent<TeamIdentifier>();
                if (t == null) t = b.GetComponentInChildren<TeamIdentifier>(true);
                if (t == null) continue;
                if (all++ % 2 == 0) continue;
                t.NetworkTeamID = other;
                moved++;
            }
            if (all == 0) return "sides: no enemy with a team was found";
            return "sides: " + moved + " of " + all + " enemies now belong to team " + other + " (RU), the rest stay on team 2; watch whether the two sides fight each other";
        }

        // ------------------------------------------------------------------ things
        private static bool InFront(float metres, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerMaster pm = players[i];
                PlayerNetworking body = pm == null ? null : pm.PlayerSpawnedObject;
                if (body == null) continue;
                yaw = GameMaster.PlayerYaw(pm, body);
                pos = body.transform.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * metres;
                return true;
            }
            return false;
        }

        /// <summary>Places one of the game's network prefabs on the ground at a spot, for every player. Null if it cannot be.</summary>
        internal static GameObject Place(string name, Vector3 pos, float yaw, out string said)
        {
            GameObject prefab = null;
            Mirror.NetworkManager net = Mirror.NetworkManager.singleton;
            var all = net == null ? null : net.spawnPrefabs;
            for (int i = 0; all != null && i < all.Count && prefab == null; i++)
                if (all[i] != null && all[i].name == name) prefab = all[i];
            if (prefab == null) { said = "the game has no '" + name + "' to place"; return null; }
            if (!Allowed(prefab)) { said = "'" + name + "' is not placed by hand"; return null; }
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
            GameObject go = UnityEngine.Object.Instantiate((UnityEngine.Object)prefab, pos + Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f)).Cast<GameObject>();
            Mirror.NetworkServer.Spawn(go, (Mirror.NetworkConnection)null);
            said = "'" + name + "' placed at " + P(pos);
            return go;
        }

        private static string Thing(string name, bool hasPos, Vector3 pos)
        {
            float yaw = 0f;
            if (!hasPos && !InFront(6f, out pos, out yaw)) return "thing: no player to place it in front of, and no enemy marker was selected";
            GameObject go = Place(name, pos, yaw, out string said);
            if (go == null) return "thing: " + said;
            Watch.Add(new Placed { Go = go, Name = name, At = go.transform.position, When = Time.unscaledTime });
            Replay.Event("appears", "'" + name + "' is placed", go.transform.position);
            return "thing: " + said + (hasPos ? ", where the selected marker is" : ", 6 m in front of you");
        }

        // ------------------------------------------------------------------ players and the mission's end
        private static string Respawn()
        {
            GameManagerNetwork g = GameManagerNetwork.instance;
            if (g == null) return "respawn: the game's mission keeper was not found";
            bool before = g.AllowRespawns;
            g.SERVER_SetRespawns(true);
            g.UserCode_SERVER_RespawnALL();
            return "respawn: the game was asked to bring every player back (respawns were " + (before ? "on" : "off") + ", now " + (g.AllowRespawns ? "on" : "off") + ")";
        }

        internal static string End(bool success)
        {
            GameManagerNetwork g = GameManagerNetwork.instance;
            if (g == null) return "end: the game's mission keeper was not found";
            if (!success)
            {
                // The game's failed screen closes again at once while a player is alive (seen in a run), so the players are told in words too.
                try { RaidManager raid = RaidManager.singleton; if (raid != null) Message(raid, "Mission failed.", true); } catch { }
                g.SERVER_FailOperation();
                _endAt = Time.unscaledTime + 5f;
                return "end: the players were told the mission has failed; the operation ends in 5 seconds";
            }
            // Whether a success the mod asked for counts is the host's choice (General/FairPlay); the studio leaves it to the mod.
            string flag = FairPlayIs("Off") ? "the run is not marked (FairPlay is Off)" : Mark();
            g.ServerCompleteExfil();
            return "end: the game was told the extraction is complete; " + flag;
        }

        private static string EndOperation()
        {
            GameManagerNetwork g = GameManagerNetwork.instance;
            if (g == null) return "end: the game's mission keeper was not found";
            g.EndOperation();
            return "end: the game's own 'end operation' was called";
        }

        internal static bool FairPlayIs(string what)
        {
            string v = (DirectorPlugin.FairPlay.Value ?? "").Trim();
            if (v.Length == 0) v = "Off";
            return string.Equals(v, what, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Sets the game's own 'cheated' flag, so that this run earns nothing.</summary>
        internal static string Mark()
        {
            try { OperationsManager.singleton.NetworkHasCheated = true; return "the game's 'cheated' flag is set, so this run earns nothing"; }
            catch (Exception e) { return "the 'cheated' flag could not be set (" + e.Message + ")"; }
        }

        /// <summary>Tells the others in the mission that the host runs the mod. Nobody else there: nothing is shown.</summary>
        internal static string Notice(RaidManager raid)
        {
            int players = 0;
            var all = PlayerMaster.AllPlayerMasters;
            for (int i = 0; all != null && i < all.Count; i++) if (all[i] != null) players++;
            if (players < 2) return "no notice: nobody else is in the mission";
            return Message(raid, "The host runs MissionLab, an unofficial mod, in this mission.") + " (" + players + " players)";
        }
    }
}
