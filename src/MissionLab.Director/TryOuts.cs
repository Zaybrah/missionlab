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

        internal static void Reset() { RestoreRespawns("the mission is over"); Last = ""; Watch.Clear(); _marked = false; _lastAction = "the start of the mission"; _endAt = -1f; _finishAt = -1f; _ended = false; Orders.Clear(); Messages.Reset(); _respawnsOffAt = -1f; Hurried.Clear(); _speedLogged = false; Marches.Clear(); LastOrders.Clear(); _orderId = 0; _orderLines = 0; }

        // A failed mission: the players are told, and a few seconds later the game's own "end operation" takes everyone out.
        private static float _endAt = -1f;

        // A mission ending waits for the messages fired with it (MessageQueue.EndAt); the first ending decided wins, and
        // the mission's triggers stop while it is pending or carried out, so a countdown cannot end a won mission as failed.
        private static float _finishAt = -1f;
        private static bool _finishSuccess, _ended;
        internal static bool Ending => _finishAt >= 0f || _ended;
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
            FollowUp(now);
            string due = Messages.Due(now);
            if (due != null)
                try { RaidManager raid = RaidManager.singleton; if (raid != null) Show(raid, due, false); } catch { }
            if (_respawnsOffAt >= 0f && now >= _respawnsOffAt) RestoreRespawns("twelve seconds have passed");
            if (_endAt >= 0f && now >= _endAt)
            {
                _endAt = -1f;
                try { GameManagerNetwork g = GameManagerNetwork.instance; if (g != null) g.EndOperation(); DirectorPlugin.L.LogInfo("end: the operation was ended after the failed message"); }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the operation could not be ended: " + e.Message); }
            }
            if (_finishAt >= 0f && now >= _finishAt)
            {
                _finishAt = -1f;
                try
                {
                    GameManagerNetwork g = GameManagerNetwork.instance;
                    string said = g == null ? "end: the game's mission keeper was not found, so the mission was not ended" : Finish(g, _finishSuccess);
                    DirectorPlugin.L.LogInfo("MissionLab GM: " + said); Replay.Event("note", said);
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("the mission could not be ended: " + e.Message); }
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
            // One message would push the other off the screen: yellow ones wait their turn, red ones go at once.
            Core.MessageTurn turn = Messages.Offer(text, red, Time.unscaledTime);
            if (turn == Core.MessageTurn.Dropped) return "message dropped, eight are already waiting: \"" + text + "\"";
            if (turn == Core.MessageTurn.Waits) return "message waits its turn: \"" + text + "\"";
            return Show(raid, text, red);
        }

        private static readonly Core.MessageQueue Messages = new Core.MessageQueue();
        private static float _respawnsOffAt = -1f;
        private static readonly HashSet<IntPtr> Hurried = new HashSet<IntPtr>();
        private static bool _speedLogged;

        private static string Show(RaidManager raid, string text, bool red)
        {
            ExfilZone zone = null;
            var zones = raid.exfilZones;
            for (int i = 0; zones != null && i < zones.Count && zone == null; i++) zone = zones[i];
            if (zone == null) return "message: this map has no extraction zone to send it through";
            zone.RPCHint(text, red ? Popup.PopupType.RedWarning : Popup.PopupType.YellowWarning);
            Messages.Shown(Time.unscaledTime);
            return "message sent to every player's screen: \"" + text + "\"";
        }

        internal static Vector3 NearestPlayer(Vector3 from)
        {
            Vector3 best = Vector3.zero; float bd = float.MaxValue;
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerNetworking body = players[i] == null ? null : players[i].PlayerSpawnedObject;
                if (body == null) continue;
                float d = (body.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = body.transform.position; }
            }
            return best;
        }

        /// <summary>Brings dead players back. With restore, the game's own respawn switch goes back to what it was a few seconds later.</summary>
        internal static string RespawnNow(bool restore)
        {
            GameManagerNetwork g = GameManagerNetwork.instance;
            if (g == null) return "respawn: the game's mission keeper was not found";
            bool before = g.AllowRespawns;
            if (restore && _respawnsWere == null) _respawnsWere = before;      // what the game's own setting was, to put it back
            g.SERVER_SetRespawns(true);
            g.UserCode_SERVER_RespawnALL();
            if (restore) _respawnsOffAt = Time.unscaledTime + 12f;             // at the latest; the wave rule puts it back sooner
            return "respawn: the game was asked to bring every player back (respawns were " + (before ? "on" : "off") + (restore && !before ? ", and are put back once the wave is seen)" : ", now " + (g.AllowRespawns ? "on" : "off") + ")");
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
        // An order is followed up. Seen on 17 November: a patrol route of one stop was taken by enemies after a fight, but not by
        // fresh ones: a called enemy and a hunter stood still. So the mod keeps at it once a second until the order ends (Core
        // OrderWatch decides how), and it tries more than one way:
        //   step 0: the patrol route (one that loops) and the spot given straight to the navigation agent;
        //   step 1: after 6 s without progress, the game's own answer to a call for backup;
        //   step 2: after 14 s without progress, the call for backup itself.
        // Taking an order is not walking it (ML-035). On 18 November two fresh hunters took theirs and never had a path ("has a
        // path no, Infinity m to go"), which the old log could not tell from an enemy that sets off late. So each order is logged
        // by stage: taken (with its navigation agent as the game left it), a path found, on its way, and how it ended, in
        // OrderWatch's words. One line per stage per order and at most OrderLines a mission, so a busy mission cannot fill the log.
        private sealed class March
        {
            public IntPtr Bot; public Vector3 To, From; public float Since, NextLook, Start, Best, CloserAt; public int Step, Id, ShortLooks; public GameObject Stop;
            public BrainAI.IdleStates Was; public bool Hunter; public int Overruled; public string Stage = Core.OrderWatch.Accepted;
        }
        private static readonly Dictionary<IntPtr, March> Marches = new Dictionary<IntPtr, March>();
        internal static int Marching => Marches.Count;
        /// <summary>Is this enemy under an order that is still followed? Right after WalkTo: did it take the order.</summary>
        internal static bool Ordered(IntPtr bot) => Marches.ContainsKey(bot);

        /// <summary>How one order ended, or why it was never given; newest last, the last KeepOrders of the mission.</summary>
        internal struct OrderEnd
        {
            public int Order;               // the order's number in this mission (0: it was never given)
            public IntPtr Bot;              // the enemy
            public string Word, Why;        // one of Core.OrderWatch's words, and the numbers behind it
            public float Seconds, Closer;   // how long it ran, and how much closer the enemy came at best (metres, flat)
        }
        internal static readonly List<OrderEnd> LastOrders = new List<OrderEnd>();
        private const int KeepOrders = 32, OrderLines = 150;
        private static int _orderId, _orderLines;

        private static void Keep(IntPtr bot, int order, string word, string why, float seconds, float closer)
        {
            LastOrders.Add(new OrderEnd { Order = order, Bot = bot, Word = word, Why = why, Seconds = seconds, Closer = closer });
            if (LastOrders.Count > KeepOrders) LastOrders.RemoveAt(0);
        }

        private static void OrderLog(string line)
        {
            if (_orderLines > OrderLines) return;
            _orderLines++;
            DirectorPlugin.L.LogInfo(_orderLines > OrderLines ? "order: " + OrderLines + " lines about orders in this mission; the rest are left out of the log (how each order ended is still kept)" : line);
        }

        private static void Ended(March m, string word, string why, float now, string more)
        {
            string said = "order " + m.Id + ": " + word + ", " + why;
            OrderLog(said + more);
            Replay.Event("note", said);
            Keep(m.Bot, m.Id, word, why, now - m.Since, Mathf.Max(0f, m.Start - m.Best));
        }

        private static int Rank(string stage) => stage == Core.OrderWatch.Moving ? 2 : stage == Core.OrderWatch.PathFound ? 1 : 0;

        private static string Yes(bool v) => v ? "yes" : "no";

        /// <summary>The enemy's state and its navigation agent, in one line: what the spike needs to see why an enemy does not walk.</summary>
        private static string Legs(BrainAI b)
        {
            var inv = CultureInfo.InvariantCulture;
            string who = "";
            try { who = "state " + b.CurrentState + ", idle " + b.idleStates + ", "; } catch { }
            try
            {
                AgentController legs = b.agent; var feet = legs == null ? null : legs.Agent;
                if (feet == null) return who + "no navigation agent";
                float to = feet.remainingDistance;
                return who + "legs: path " + Yes(feet.hasPath) + ", working one out " + Yes(feet.pathPending) + ", searching " + Yes(feet.canSearch)
                     + ", may move " + Yes(feet.canMove) + ", stopped " + Yes(feet.isStopped) + ", top speed " + feet.maxSpeed.ToString("0.##", inv)
                     + ", speed now " + feet.velocity.magnitude.ToString("0.##", inv) + ", " + (float.IsInfinity(to) || float.IsNaN(to) ? "no distance" : to.ToString("0", inv) + " m") + " to go";
            }
            catch (Exception e) { return who + "legs could not be read: " + e.Message; }
        }

        private static Transform PlayerNear(Vector3 spot)
        {
            Transform best = null; float bd = float.MaxValue;
            var players = PlayerMaster.AllPlayerMasters;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                PlayerNetworking body = players[i] == null ? null : players[i].PlayerSpawnedObject;
                if (body == null) continue;
                float d = (body.transform.position - spot).sqrMagnitude;
                if (d < bd) { bd = d; best = body.transform; }
            }
            return best;
        }

        private static void Push(BrainAI b, March m, bool route)
        {
            if (route)
                try
                {
                    if (m.Stop == null) { m.Stop = new GameObject("MissionLab order " + Orders.Count); Orders.Add(m.Stop); }
                    m.Stop.transform.position = m.To;
                    AIPatrol way = b.aiPatrol;
                    if (way == null) { way = new AIPatrol(); b.aiPatrol = way; }
                    var stops = new Il2Generic.List<Transform>(); stops.Add(m.Stop.transform);
                    way.patrolPoints = stops; way.currentWayPointIndex = 0; way.Looping = true;
                    way.patrolWaitTime = 0f; way.onReachDestination = AIPatrol.OnReachDestination.MoveImmediately;
                    b.patrolTimer = 0f;
                    b.idleStates = BrainAI.IdleStates.Patrol;
                }
                catch { }
            try
            {
                AgentController legs = b.agent; var feet = legs == null ? null : legs.Agent;
                if (feet != null)
                {
                    bool ours = (feet.destination - m.To).sqrMagnitude <= 1f;
                    if (!route && !ours) m.Overruled++;     // something else gave it another spot since the last look
                    feet.isStopped = false; feet.canMove = true;
                    // An agent with its own path search off may never work out a path; whether fresh enemies have it off is
                    // in the order's first log line (read before this), not yet seen in the game.
                    try { feet.canSearch = true; } catch { }
                    // A new path only when the spot is new to it, or it has none and none is being worked out: asking every
                    // second, as up to 0.26.2, may have thrown away a path that was still being worked out.
                    if (route || !ours) { feet.destination = m.To; feet.SearchPath(); }
                    else if (!feet.hasPath && !feet.pathPending) feet.SearchPath();
                }
            }
            catch { }
            if (!route) return;
            if (m.Step == 1) try { b.ExecuteBackupResponse(m.To, b.transform, m.Hunter ? PlayerNear(m.To) : null); } catch { try { b.ExecuteBackupResponse(m.To, b.transform, PlayerNear(m.To)); } catch { } }
            if (m.Step == 2) try { b.CalledBackup(m.To, b.transform, PlayerNear(m.To)); } catch { }
        }

        private static readonly string[] Steps = { "the patrol route and the spot given to its legs", "the game's answer to a call for backup", "a call for backup" };

        private static void FollowUp(float now)
        {
            if (Marches.Count == 0) return;
            GameManager gm = GameManager.instance;
            if (gm == null) { Marches.Clear(); return; }
            var inv = CultureInfo.InvariantCulture;
            foreach (March m in new List<March>(Marches.Values))
            {
                if (now < m.NextLook) continue;
                m.NextLook = now + 1f;
                BrainAI b = GameMaster.Find(gm, m.Bot);
                if (b == null) { Ended(m, Core.OrderWatch.Gone, "it is no longer alive after " + (now - m.Since).ToString("0", inv) + " s", now, ""); Marches.Remove(m.Bot); continue; }
                try
                {
                    GameMaster.EnemyPose(b, out Vector3 at, out _);
                    Vector3 gone = at - m.From, togo = at - m.To; gone.y = 0f; togo.y = 0f;
                    var look = new Core.OrderLook { Age = now - m.Since, Start = m.Start, Left = togo.magnitude, Moved = gone.magnitude };
                    if (look.Left < m.Best - 1f) { m.Best = look.Left; m.CloserAt = now; }
                    look.SinceCloser = now - m.CloserAt;
                    BrainAI.States state = b.CurrentState;
                    look.Fighting = state == BrainAI.States.TargetSighted || state == BrainAI.States.WasSighted || state == BrainAI.States.Suppress || state == BrainAI.States.FallBack;
                    float pathEnds = 0f;
                    try
                    {
                        AgentController legs = b.agent; var feet = legs == null ? null : legs.Agent;
                        if (feet != null)
                        {
                            // A path to a spot the game gave it since says nothing about this order's spot.
                            bool ours = (feet.destination - m.To).sqrMagnitude <= 1f;
                            look.HasPath = ours && feet.hasPath; look.Pending = feet.pathPending;
                            // The game makes a path to the nearest point it can reach: one that ends away from the spot stops short.
                            if (look.HasPath && !look.Pending) { Vector3 end = feet.endOfPath - m.To; end.y = 0f; pathEnds = end.magnitude; }
                        }
                    }
                    catch { }
                    m.ShortLooks = pathEnds > Core.OrderWatch.PathWithin ? m.ShortLooks + 1 : 0;
                    if (m.ShortLooks >= 3) look.PathShort = pathEnds;           // three looks in a row, not a path still being finished
                    string word = Core.OrderWatch.Judge(look, out string why);
                    if (word != null)
                    {
                        bool ok = Core.OrderWatch.Succeeded(word);
                        Ended(m, word, why + (ok ? ", by " + Steps[m.Step] : ""), now, ok ? "" : "; tried up to " + Steps[m.Step] + ", overruled " + m.Overruled + " times; " + Legs(b));
                        try { if (b.idleStates == BrainAI.IdleStates.Patrol) b.idleStates = m.Was == BrainAI.IdleStates.Patrol ? BrainAI.IdleStates.Idle : m.Was; } catch { }
                        Marches.Remove(m.Bot);
                        continue;
                    }
                    string stage = Core.OrderWatch.Stage(look);
                    if (Rank(stage) > Rank(m.Stage))
                    {
                        m.Stage = stage;
                        string how = stage == Core.OrderWatch.Moving ? (look.Start - look.Left).ToString("0.#", inv) + " m closer"
                                   : pathEnds > Core.OrderWatch.PathWithin ? "but it ends " + pathEnds.ToString("0.#", inv) + " m from the spot" : "to the spot";
                        OrderLog("order " + m.Id + ": " + stage + " after " + look.Age.ToString("0", inv) + " s, " + how + ", " + look.Left.ToString("0", inv) + " m to go");
                    }
                    bool stuck = look.Moved < 1.5f;
                    if (m.Step == 0 && look.Age >= 6f && stuck) { m.Step = 1; OrderLog("order " + m.Id + ": no progress in 6 s; trying " + Steps[1]); Push(b, m, true); }
                    else if (m.Step == 1 && look.Age >= 14f && stuck) { m.Step = 2; OrderLog("order " + m.Id + ": no progress in 14 s; trying " + Steps[2]); Push(b, m, true); }
                    else Push(b, m, false);
                }
                catch (Exception e) { DirectorPlugin.L.LogWarning("an order could not be followed up: " + e.Message); Marches.Remove(m.Bot); }
            }
        }

        /// <summary>What the last order came to: what was asked, where the ground was, and who took it.</summary>
        internal struct WalkResult
        {
            public Vector3 Asked, Ground;          // the spot asked for, and the nearest spot an enemy can stand on
            public int Accepted, NoLegs, TooFar, Posted, OffGround;   // took the order; cannot walk; outside the distance; a fixed sentry or a crew member; not sent, the spot is not walkable ground
            public string GroundWhy;               // why the spot is not walkable ground ("" or null: it is, or the game could not say)
            public string Words()
            {
                var inv = CultureInfo.InvariantCulture;
                bool bad = !string.IsNullOrEmpty(GroundWhy);
                return Accepted + " accepted, asked for " + Asked.x.ToString("0.#", inv) + ", " + Asked.z.ToString("0.#", inv)
                     + (bad ? ", not walkable ground: " + GroundWhy : ", walkable ground at " + Ground.x.ToString("0.#", inv) + ", " + Ground.z.ToString("0.#", inv))
                     + (TooFar > 0 ? ", " + TooFar + " too far away" : "") + (NoLegs > 0 ? ", " + NoLegs + " that cannot walk" : "") + (Posted > 0 ? ", " + Posted + " at a fixed post" : "")
                     + (OffGround > 0 ? ", " + OffGround + " not sent there" : "");
            }
        }
        internal static WalkResult LastWalk;

        /// <summary>The nearest spot an enemy can stand on. known: the game's navigation could be asked; found: it gave a spot.</summary>
        private static Vector3 Ground(Vector3 spot, out bool known, out bool found)
        {
            // A door's hinge or a spot by a wall is not ground anyone can stand on: take the nearest that is.
            known = false; found = false;
            try
            {
                AstarPath paths = AstarPath.active;
                if (paths != null) { known = true; var near = paths.GetNearest(spot); if (near != null && near.node != null) { found = true; return near.position; } }
            }
            catch { known = false; found = false; }
            return spot;
        }

        internal static int WalkTo(GameManager gm, Vector3 to, float within, HashSet<IntPtr> only, bool hurry = false)
        {
            var inv = CultureInfo.InvariantCulture;
            Vector3 ground = Ground(to, out bool known, out bool found);
            // Without the game's navigation nothing can be said about the spot, and the order goes as it always did.
            string bad = known ? Core.OrderWatch.Spot(new Core.V3(to.x, to.y, to.z), found, new Core.V3(ground.x, ground.y, ground.z)) : "";
            LastWalk = new WalkResult { Asked = to, Ground = ground, GroundWhy = bad };
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
                    if (legs == null || !legs.entityExists) { LastWalk.NoLegs++; Keep(b.Pointer, 0, Core.OrderWatch.NoLegs, "not sent: it cannot walk", 0f, 0f); continue; }    // a gunner, or someone whose navigation is off
                    if (GameMaster.Posted(b)) { LastWalk.Posted++; Keep(b.Pointer, 0, Core.OrderWatch.FixedPost, "not sent: a fixed sentry or a vehicle's crew stays where it is", 0f, 0f); continue; }
                    // Where the enemy is now. Its own object stays where it was made; the navigation agent has the truth.
                    GameMaster.EnemyPose(b, out Vector3 at, out _);
                    if (within > 0f && (at - to).sqrMagnitude > within * within) { LastWalk.TooFar++; continue; }
                    if (bad.Length > 0) { LastWalk.OffGround++; Keep(b.Pointer, 0, Core.OrderWatch.InvalidFloor, "not sent: " + bad, 0f, 0f); continue; }
                    float turn = told * 2.4f;                                             // spread around the spot, 2 m out
                    Vector3 spot = Ground(to + new Vector3(Mathf.Sin(turn) * 2f, 0.1f, Mathf.Cos(turn) * 2f), out _, out bool near);
                    // Spread by the stairs or a wall, a spot can land on another floor or beyond the wall: then the middle is used.
                    if (found && (!near || Mathf.Abs(spot.y - ground.y) > Core.OrderWatch.FloorGap || (spot - ground).sqrMagnitude > 16f)) spot = ground;
                    var m = new March { Bot = b.Pointer, To = spot, From = at, Since = Time.unscaledTime, NextLook = Time.unscaledTime + 1f, Was = b.idleStates, Hunter = hurry, Id = ++_orderId };
                    Vector3 apart = spot - at; apart.y = 0f;
                    m.Start = m.Best = apart.magnitude; m.CloserAt = m.Since;
                    if (Marches.TryGetValue(b.Pointer, out March earlier))                // an order on top of an order
                    {
                        m.Was = earlier.Was; m.Stop = earlier.Stop;
                        Ended(earlier, Core.OrderWatch.Replaced, "order " + m.Id + " took its place after " + (m.Since - earlier.Since).ToString("0", inv) + " s", m.Since, "");
                    }
                    string before = Legs(b);          // as the game left it, before the order touches it: what a fresh enemy's navigation looks like
                    Marches[b.Pointer] = m;
                    Push(b, m, true);
                    OrderLog("order " + m.Id + ": " + Core.OrderWatch.Accepted + (hurry ? " by a hunter" : "") + ", " + m.Start.ToString("0", inv) + " m from its spot " + P(spot) + "; before the order: " + before);
                    if (hurry && Hurried.Add(b.Pointer))
                        try
                        {
                            float was = b.PatrolSpeed; b.PatrolSpeed = was * 2f;
                            if (!_speedLogged) { _speedLogged = true; DirectorPlugin.L.LogInfo("hurry: patrol speed " + was.ToString("0.##", inv) + " to " + b.PatrolSpeed.ToString("0.##", inv)); }
                        }
                        catch { }
                    told++;
                }
                catch { }
            }
            LastWalk.Accepted = told;
            if (Orders.Count > 200) { for (int i = 0; i < 100; i++) try { if (Orders[i] != null) UnityEngine.Object.Destroy(Orders[i]); } catch { } Orders.RemoveRange(0, 100); }
            return told;
        }

        private static string Walk(GameManager gm)
        {
            Vector3 to = FirstPlayer();
            if (to == Vector3.zero) return "walk: no player to walk to";
            int told = WalkTo(gm, to, 0f, null);
            return told == 0 ? "walk: no enemy could be sent (" + LastWalk.Words() + ")"
                             : "walk: " + told + " enemies were given a one-stop route to " + P(to) + " (" + LastWalk.Words() + "); taking it is not walking it: the log follows each order";
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
        private static string Respawn() => RespawnNow(false);

        private static bool? _respawnsWere;      // the game's own respawn setting before the mod switched it on for a wave

        /// <summary>Puts the game's own respawn setting back to what it was before a wave: on success, on failure and when the mission ends.</summary>
        internal static void RestoreRespawns(string why)
        {
            _respawnsOffAt = -1f;
            if (_respawnsWere == null) return;
            bool was = _respawnsWere.Value; _respawnsWere = null;
            try
            {
                GameManagerNetwork g = GameManagerNetwork.instance;
                if (g == null) return;
                if (g.AllowRespawns != was) g.SERVER_SetRespawns(was);
                DirectorPlugin.L.LogInfo("respawn: the game's own respawn setting is " + (was ? "on" : "off") + " again (" + why + ")");
            }
            catch (Exception e) { DirectorPlugin.L.LogWarning("the game's respawn setting could not be put back: " + e.Message); }
        }

        internal static string End(bool success)
        {
            GameManagerNetwork g = GameManagerNetwork.instance;
            if (g == null) return "end: the game's mission keeper was not found";
            if (Ending) return "end: the mission is already ending (" + (_finishSuccess ? "success" : "failed") + "); this ending is left out";
            _finishSuccess = success;
            try { RunRecord.Ending(success ? "success" : "failed"); } catch { }      // the run record names how this mission ended once the end is seen
            float now = Time.unscaledTime, at = Messages.EndAt(now);
            if (at <= now) return Finish(g, success);
            _finishAt = at;
            return "end: " + (success ? "success" : "failed") + " in " + ((int)Math.Ceiling(at - now)).ToString(CultureInfo.InvariantCulture) + " s, once the " +
                   Messages.Waiting + (Messages.Waiting == 1 ? " waiting message has" : " waiting messages have") + " been shown";
        }

        private static string Finish(GameManagerNetwork g, bool success)
        {
            _ended = true;
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
