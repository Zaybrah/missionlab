using System;
using System.Collections.Generic;
using System.Text;
using MissionLab.Core;
using UnityEngine;

namespace MissionLab
{
    /// <summary>
    /// Game Master experiment: F6 spawns one enemy in front of the host, mid-mission.
    /// It answers one question: can enemies be added live, and through which door?
    ///
    ///   Method A: the game's own spawner. Hand it a list holding just our one point
    ///             and a count of one, call it, and put both back a moment later.
    ///   Method B: create the enemy directly at the spot and apply the point's settings.
    ///
    /// The first press uses A. If A adds nobody, B is tried automatically. Further
    /// presses alternate, so two presses always exercise both.
    /// </summary>
    internal static class GmTest
    {
        private const KeyCode Key = KeyCode.F6;
        private const float ReportAfterSeconds = 3f;

        private static bool _keyOk = true;
        private static int _presses;
        private static float _reportAt = -1f;
        private static char _method;
        private static bool _wasFallback;
        private static IntPtr _raidPtr;
        private static List<GameObject> _savedList;
        private static int _savedAmount, _before;
        private static Vector3 _target;
        private static string _where;

        public static void Tick(float now)
        {
            if (_reportAt > 0f && now >= _reportAt) Report(now);
            if (!_keyOk || !DirectorPlugin.GmTestKey.Value) return;
            bool pressed;
            try { pressed = Input.GetKeyDown(Key); }
            catch (Exception e) { _keyOk = false; DirectorPlugin.L.LogWarning("GM test key disabled: " + e.Message); return; }
            if (!pressed) return;
            if (_reportAt > 0f) { Say("still busy with the previous spawn"); return; }
            char method = _presses % 2 == 0 ? 'A' : 'B';
            if (TrySpawn(now, method, false)) _presses++;
        }

        private static void Say(string text)
        {
            Director.Banner = "MissionLab GM test: " + text;
            Director.BannerUntil = Time.unscaledTime + 8f;
            DirectorPlugin.L.LogInfo(Director.Banner);
        }

        private static bool TrySpawn(float now, char method, bool fallback)
        {
            if (!Mirror.NetworkServer.active) { Say("only the host can spawn"); return false; }
            RaidManager raid = RaidManager.singleton;
            GameManager gm = GameManager.instance;
            if (raid == null || gm == null) { Say("no mission is running"); return false; }
            if (raid.Pointer != Director.HandledRaidPtr || now - Director.HandledAt < 8f) { Say("wait until the mission has finished spawning"); return false; }
            var list = raid.botSpawnPoints;
            if (list == null) { Say("the mission has no spawn list"); return false; }
            bool known = Director.CurrentRaidPtr == raid.Pointer;

            // Where: about 15 m in front of the host, on known walkable ground if possible.
            Transform body = null;
            PlayerMaster me = PlayerMaster.MyPlayerMaster;
            PlayerNetworking pn = me == null ? null : me.PlayerSpawnedObject;
            if (pn != null) body = pn.transform;
            Camera cam = MainCameraSingleton.MainCam;
            if (cam == null) cam = Camera.main;
            if (body == null && cam == null) { Say("could not find your position"); return false; }
            Vector3 origin = body != null ? body.position : cam.transform.position;
            Vector3 fwd = cam != null ? cam.transform.forward : body.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.01f ? Vector3.forward : fwd.normalized;

            NavMesh nav = known ? Director.CurrentNav : null;
            Vector3 target = origin + fwd * 8f;
            bool onMesh = false;
            if (nav != null)
                foreach (float dist in new[] { 15f, 10f, 20f, 6f })
                {
                    Vector3 c = origin + fwd * dist;
                    if (nav.TriAt(c.x, c.z, origin.y, 4f, out float h) < 0) continue;
                    target = new Vector3(c.x, h + 0.1f, c.z);
                    onMesh = true;
                    break;
                }

            // A copy of one of the map's spawn points, set to wander with the map's usual senses.
            GameObject template = known ? Director.CurrentTemplate : null;
            for (int i = 0; template == null && i < list.Count; i++)
                if (list[i] != null && list[i].GetComponent<BotSpawnDetails>() != null) template = list[i];
            if (template == null) { Say("no spawn point to copy"); return false; }
            float yaw = Mathf.Atan2(-fwd.x, -fwd.z) * Mathf.Rad2Deg;      // facing the host
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            GameObject point = UnityEngine.Object.Instantiate((UnityEngine.Object)template, target, rot, raid.transform).Cast<GameObject>();
            point.name = "ML gm spawn " + method;
            var d = point.GetComponent<BotSpawnDetails>();
            d.idleState = BrainAI.IdleStates.Wander;
            d.reactType = BrainAI.ReactType.Offensive;
            d.isProne = false; d.isSpawnGroup = false; d.disableNavmesh = false; d.hvtLinkedSpawnGroup = null;
            if (d.patrolPoints != null) d.patrolPoints.Clear();
            if (known && Director.CurrentDetect > 0f)
            {
                d.DetectionRange = Director.CurrentDetect; d.HearingRange = Director.CurrentHear; d.FOV = Director.CurrentFov;
            }

            _before = gm.allAI != null ? gm.allAI.Count : 0;
            _raidPtr = raid.Pointer;
            _target = target;
            _method = method;
            _wasFallback = fallback;
            _where = "(" + target.x.ToString("0.0") + ", " + target.y.ToString("0.0") + ", " + target.z.ToString("0.0") + ")" + (onMesh ? "" : " off known ground");
            bool custom = raid.customAI != null && raid.customAI.Count > 0;

            // What the game thinks of this spot, for the record.
            var probe = new StringBuilder();
            try
            {
                foreach (float r in new[] { 1f, 5f, 15f, 30f }) probe.Append(" r" + r + "=" + raid.CanSpawn(target, r));
                probe.Append(" atHost(r1)=" + raid.CanSpawn(origin, 1f));
            }
            catch (Exception e) { probe.Append(" failed: " + e.Message); }
            DirectorPlugin.L.LogInfo("GM test " + method + ": target " + _where + ", host at (" + origin.x.ToString("0.0") + ", " + origin.y.ToString("0.0") + ", " + origin.z.ToString("0.0") +
                                     "), enemies now " + _before + ", list " + list.Count + ", botAmount " + gm.botAmount + ", customAI " + custom + ", CanSpawn:" + probe);

            try
            {
                if (method == 'A')
                {
                    // Swap in "one point, one enemy", run the game's spawner, restore shortly after.
                    _savedList = new List<GameObject>();
                    for (int i = 0; i < list.Count; i++) _savedList.Add(list[i]);
                    _savedAmount = gm.botAmount;
                    list.Clear();
                    list.Add(point);
                    gm.botAmount = 1;
                    _reportAt = now + ReportAfterSeconds;
                    raid.ServerSpawnAI(custom);
                    Say("A: asked the game's spawner for 1 enemy at " + _where);
                }
                else
                {
                    var prefabs = new List<GameObject>();
                    if (custom) { for (int i = 0; i < raid.customAI.Count; i++) if (raid.customAI[i] != null) prefabs.Add(raid.customAI[i]); }
                    else if (raid.standardAI != null) { for (int i = 0; i < raid.standardAI.Length; i++) if (raid.standardAI[i] != null) prefabs.Add(raid.standardAI[i]); }
                    if (prefabs.Count == 0) { Say("B: the mission has no enemy types to create"); return false; }
                    GameObject prefab = prefabs[UnityEngine.Random.Range(0, prefabs.Count)];
                    _reportAt = now + ReportAfterSeconds;
                    GameObject bot = raid.InstantiateBot(prefab, target, rot, null);
                    if (bot == null) { Say("B: the game returned no enemy for '" + prefab.name + "'"); return true; }
                    raid.ApplyBotSpawnSettings(bot, d);
                    Say("B: created '" + prefab.name + "' directly at " + _where);
                }
            }
            catch (Exception e)
            {
                DirectorPlugin.L.LogError("GM test " + method + ": the game threw: " + e);
                Say(method + ": the game refused (see log)");
                if (_reportAt < 0f) _reportAt = now;      // still restore and report
            }
            return true;
        }

        private static void Report(float now)
        {
            _reportAt = -1f;
            bool retryWithB = false;
            try
            {
                RaidManager raid = RaidManager.singleton;
                GameManager gm = GameManager.instance;
                if (raid == null || gm == null || raid.Pointer != _raidPtr) { DirectorPlugin.L.LogInfo("GM test: mission ended before the result was in"); return; }
                if (_method == 'A' && _savedList != null)
                {
                    var list = raid.botSpawnPoints;
                    if (list != null)
                    {
                        list.Clear();
                        foreach (GameObject g in _savedList) if (g != null) list.Add(g);
                    }
                    gm.botAmount = _savedAmount;
                }

                // Did someone appear, and where?
                int after = gm.allAI != null ? gm.allAI.Count : 0;
                float best = float.MaxValue; string nearest = "nobody";
                for (int i = 0; gm.allAI != null && i < gm.allAI.Count; i++)
                {
                    BrainAI b = gm.allAI[i];
                    if (b == null) continue;
                    float dist = Vector3.Distance(b.transform.position, _target);
                    if (dist < best) { best = dist; nearest = b.name + " '" + b.myName + "' state " + b.CurrentState + " idle " + b.idleStates; }
                }
                string result = after > _before ? "WORKED" : "no new enemy";
                Say(_method + ": " + result + " | enemies " + _before + " -> " + after + " | nearest to the spot: " + nearest + " at " + (best == float.MaxValue ? "-" : best.ToString("0.0") + " m"));
                retryWithB = _method == 'A' && after <= _before && !_wasFallback;
            }
            finally { _savedList = null; }
            if (retryWithB) TrySpawn(now, 'B', true);
        }
    }
}
