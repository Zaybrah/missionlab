using System;
using System.Collections.Generic;
using System.Globalization;
using MissionLab.Core;
using UnityEngine;

namespace MissionLab
{
    /// <summary>
    /// Game Master experiment: F6 spawns one enemy in front of the host, mid-mission,
    /// through the game's own spawner. We hand the spawner a list holding just our one
    /// point and a count of one, call it, and put both back as soon as the enemy exists.
    /// (Verified in game on 0.09.5. Creating the enemy directly with InstantiateBot +
    /// ApplyBotSpawnSettings does NOT work: that enemy never registers with the game
    /// and cannot be killed.)
    ///
    /// Odd presses spawn a random type from the mission's own list. Even presses try
    /// the next question: a type we choose, through the game's custom-type list.
    /// </summary>
    internal static class GmTest
    {
        private const KeyCode Key = KeyCode.F6;
        private const float GiveUpAfterSeconds = 3f;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static bool _keyOk = true;
        private static int _presses;
        private static float _startedAt = -1f;
        private static bool _typed;
        private static string _wantedType;
        private static IntPtr _raidPtr;
        private static List<GameObject> _savedList, _savedCustom;
        private static bool _customWasNull;
        private static int _savedAmount, _before;
        private static HashSet<IntPtr> _beforePtrs;
        private static Vector3 _target;
        private static string _where;

        public static void Tick(float now)
        {
            if (_startedAt >= 0f) Watch(now);
            if (!_keyOk || !DirectorPlugin.GmTestKey.Value) return;
            bool pressed;
            try { pressed = Input.GetKeyDown(Key); }
            catch (Exception e) { _keyOk = false; DirectorPlugin.L.LogWarning("GM test key disabled: " + e.Message); return; }
            if (!pressed) return;
            if (_startedAt >= 0f) { Say("still busy with the previous spawn"); return; }
            if (TrySpawn(now, _presses % 2 == 1)) _presses++;
        }

        private static void Say(string text)
        {
            Director.Banner = "MissionLab GM test: " + text;
            Director.BannerUntil = Time.unscaledTime + 8f;
            DirectorPlugin.L.LogInfo(Director.Banner);
        }

        private static string P(Vector3 v) => "(" + v.x.ToString("0.0", Inv) + ", " + v.y.ToString("0.0", Inv) + ", " + v.z.ToString("0.0", Inv) + ")";

        private static bool TrySpawn(float now, bool typed)
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

            // For a typed spawn: an enemy type this mission would not pick by itself,
            // so the result cannot be a coincidence.
            GameObject wanted = null;
            if (typed)
            {
                var own = new HashSet<string>();
                if (raid.standardAI != null) for (int i = 0; i < raid.standardAI.Length; i++) if (raid.standardAI[i] != null) own.Add(raid.standardAI[i].name);
                var all = gm.AllAITypes;
                for (int i = 0; all != null && i < all.Count && wanted == null; i++) if (all[i] != null && !own.Contains(all[i].name)) wanted = all[i];
                if (wanted == null && all != null && all.Count > 0) wanted = all[all.Count - 1];
                if (wanted == null) { Say("the game lists no enemy types to choose from"); return false; }
            }

            // A copy of one of the map's spawn points, set to wander with the map's usual senses.
            GameObject template = known ? Director.CurrentTemplate : null;
            for (int i = 0; template == null && i < list.Count; i++)
                if (list[i] != null && list[i].GetComponent<BotSpawnDetails>() != null) template = list[i];
            if (template == null) { Say("no spawn point to copy"); return false; }
            float yaw = Mathf.Atan2(-fwd.x, -fwd.z) * Mathf.Rad2Deg;      // facing the host
            GameObject point = UnityEngine.Object.Instantiate((UnityEngine.Object)template, target, Quaternion.Euler(0f, yaw, 0f), raid.transform).Cast<GameObject>();
            point.name = "ML gm spawn";
            var d = point.GetComponent<BotSpawnDetails>();
            d.idleState = BrainAI.IdleStates.Wander;
            d.reactType = BrainAI.ReactType.Offensive;
            d.isProne = false; d.isSpawnGroup = false; d.disableNavmesh = false; d.hvtLinkedSpawnGroup = null;
            if (d.patrolPoints != null) d.patrolPoints.Clear();
            if (known && Director.CurrentDetect > 0f)
            {
                d.DetectionRange = Director.CurrentDetect; d.HearingRange = Director.CurrentHear; d.FOV = Director.CurrentFov;
            }

            // Remember who is already there, so the newcomer can be recognised even if it dies at once.
            _beforePtrs = new HashSet<IntPtr>();
            for (int i = 0; gm.allAI != null && i < gm.allAI.Count; i++) if (gm.allAI[i] != null) _beforePtrs.Add(gm.allAI[i].Pointer);
            _before = _beforePtrs.Count;
            _raidPtr = raid.Pointer;
            _target = target;
            _typed = typed;
            _wantedType = wanted == null ? null : wanted.name;
            _where = P(target) + (onMesh ? "" : " off known ground");
            bool missionUsesCustom = raid.customAI != null && raid.customAI.Count > 0;
            DirectorPlugin.L.LogInfo("GM test " + (typed ? "typed '" + _wantedType + "'" : "random type") + ": target " + _where + ", host at " + P(origin) +
                                     ", enemies now " + _before + ", list " + list.Count + ", botAmount " + gm.botAmount + ", mission uses custom types " + missionUsesCustom);

            // Swap in "one point, one enemy" (and, for a typed spawn, "one type"), then run the game's spawner.
            _savedList = new List<GameObject>();
            for (int i = 0; i < list.Count; i++) _savedList.Add(list[i]);
            _savedAmount = gm.botAmount;
            _savedCustom = null;
            _startedAt = now;
            try
            {
                list.Clear();
                list.Add(point);
                gm.botAmount = 1;
                if (typed)
                {
                    _customWasNull = raid.customAI == null;
                    if (_customWasNull) raid.customAI = new Il2CppSystem.Collections.Generic.List<GameObject>();
                    _savedCustom = new List<GameObject>();
                    for (int i = 0; i < raid.customAI.Count; i++) _savedCustom.Add(raid.customAI[i]);
                    raid.customAI.Clear();
                    raid.customAI.Add(wanted);
                    raid.ServerSpawnAI(true);
                }
                else raid.ServerSpawnAI(missionUsesCustom);
                Say("asked for 1 enemy" + (typed ? " of type '" + _wantedType + "'" : "") + " at " + _where);
            }
            catch (Exception e)
            {
                DirectorPlugin.L.LogError("GM test: the game's spawner threw: " + e);
                Say("the game refused (see log)");
                Finish(now, null, 0f);
            }
            return true;
        }

        /// <summary>Every frame while a spawn is pending: has someone new appeared?</summary>
        private static void Watch(float now)
        {
            RaidManager raid = RaidManager.singleton;
            GameManager gm = GameManager.instance;
            if (raid == null || gm == null || raid.Pointer != _raidPtr)
            {
                _startedAt = -1f; _savedList = null; _savedCustom = null;
                DirectorPlugin.L.LogInfo("GM test: mission ended before the result was in");
                return;
            }
            for (int i = 0; gm.allAI != null && i < gm.allAI.Count; i++)
            {
                BrainAI b = gm.allAI[i];
                if (b == null || _beforePtrs.Contains(b.Pointer)) continue;
                Finish(now, b, Vector3.Distance(b.transform.position, _target));
                return;
            }
            if (now - _startedAt >= GiveUpAfterSeconds) Finish(now, null, 0f);
        }

        private static void Finish(float now, BrainAI newcomer, float distance)
        {
            float took = now - _startedAt;
            _startedAt = -1f;
            try
            {
                RaidManager raid = RaidManager.singleton;
                GameManager gm = GameManager.instance;
                if (raid == null || gm == null || raid.Pointer != _raidPtr) return;
                var list = raid.botSpawnPoints;
                if (list != null && _savedList != null)
                {
                    list.Clear();
                    foreach (GameObject g in _savedList) if (g != null) list.Add(g);
                }
                gm.botAmount = _savedAmount;
                if (_savedCustom != null)
                {
                    if (_customWasNull) raid.customAI = null;
                    else
                    {
                        raid.customAI.Clear();
                        foreach (GameObject g in _savedCustom) if (g != null) raid.customAI.Add(g);
                    }
                }

                if (newcomer == null) { Say("no new enemy after " + GiveUpAfterSeconds.ToString("0", Inv) + " s" + (_typed ? " (typed '" + _wantedType + "')" : "")); return; }
                string name = newcomer.name ?? "?";
                string verdict = "WORKED";
                if (_typed) verdict = name.StartsWith(_wantedType + "(", StringComparison.Ordinal) ? "WORKED, and it is the chosen type" : "spawned, but NOT the chosen type '" + _wantedType + "'";
                Say(verdict + " | " + name + " | " + distance.ToString("0.0", Inv) + " m from the spot after " + took.ToString("0.00", Inv) + " s | enemies " + _before + " -> " + (gm.allAI != null ? gm.allAI.Count : 0));
            }
            finally { _savedList = null; _savedCustom = null; _beforePtrs = null; }
        }
    }
}
