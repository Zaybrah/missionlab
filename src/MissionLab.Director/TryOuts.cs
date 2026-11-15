using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

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

        internal static void Reset() { Last = ""; Watch.Clear(); }

        internal static void Tick(float now)
        {
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
                    case "hunt": said = Hunt(gm); break;
                    case "thing": said = Thing(arg, hasPos, pos); break;
                    case "sides": said = Sides(gm); break;
                    case "respawn": said = Respawn(); break;
                    case "fail": said = End(false); break;
                    case "complete": said = End(true); break;
                    default: said = "unknown try-out '" + what + "'"; break;
                }
            }
            catch (Exception e) { said = "try-out '" + what + "' failed: " + e.Message; }
            Last = said;
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
        internal static string Message(RaidManager raid, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) text = "MissionLab: test message";
            if (text.Length > 120) text = text.Substring(0, 120);
            ExfilZone zone = null;
            var zones = raid.exfilZones;
            for (int i = 0; zones != null && i < zones.Count && zone == null; i++) zone = zones[i];
            if (zone == null) return "message: this map has no extraction zone to send it through";
            zone.RPCHint(text, Popup.PopupType.YellowWarning);
            return "message sent to every player's screen: \"" + text + "\"";
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

        private static string Thing(string name, bool hasPos, Vector3 pos)
        {
            GameObject prefab = null;
            Mirror.NetworkManager net = Mirror.NetworkManager.singleton;
            var all = net == null ? null : net.spawnPrefabs;
            for (int i = 0; all != null && i < all.Count && prefab == null; i++)
                if (all[i] != null && all[i].name == name) prefab = all[i];
            if (prefab == null) return "thing: the game has no '" + name + "' to place";
            if (!Allowed(prefab)) return "thing: '" + name + "' is not placed by hand";
            float yaw = 0f;
            if (!hasPos && !InFront(6f, out pos, out yaw)) return "thing: no player to place it in front of, and no enemy marker was selected";
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
            GameObject go = UnityEngine.Object.Instantiate((UnityEngine.Object)prefab, pos + Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f)).Cast<GameObject>();
            Mirror.NetworkServer.Spawn(go, (Mirror.NetworkConnection)null);
            Watch.Add(new Placed { Go = go, Name = name, At = go.transform.position, When = Time.unscaledTime });
            CultureInfo inv = CultureInfo.InvariantCulture;
            string where = "(" + pos.x.ToString("0.0", inv) + ", " + pos.y.ToString("0.0", inv) + ", " + pos.z.ToString("0.0", inv) + ")";
            Replay.Event("appears", "'" + name + "' is placed", pos);
            return "thing: '" + name + "' placed at " + where + (hasPos ? ", where the selected marker is" : ", 6 m in front of you");
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
                g.SERVER_FailOperation();
                return "end: the game was told the mission has failed";
            }
            // A success the mod asked for must not earn anything: the game has a flag for that, set first.
            string flag;
            try { OperationsManager ops = OperationsManager.singleton; ops.NetworkHasCheated = true; flag = "the game's 'modded run' flag is set"; }
            catch (Exception e) { flag = "the 'modded run' flag could not be set (" + e.Message + ")"; }
            g.ServerCompleteExfil();
            return "end: the game was told the extraction is complete; " + flag;
        }
    }
}
