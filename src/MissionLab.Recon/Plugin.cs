using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace MissionLab
{
    /// <summary>
    /// MissionLab Recon: an unofficial, read-only helper for OPERATOR.
    /// It changes nothing in the game. When a mission loads it writes what the
    /// map contains (enemy spawn points, HVT points, objectives, exfil zones,
    /// infil methods, AI types) to BepInEx/MissionLab/recon as JSON, so mission
    /// tools can be built on real data. Not made or endorsed by Vector Interactive.
    /// </summary>
    [BepInPlugin(Id, DisplayName, Version)]
    public class Plugin : BasePlugin
    {
        public const string Id = "missionlab.recon";
        public const string DisplayName = "MissionLab Recon (unofficial)";
        public const string Version = "0.2.0";

        internal static ManualLogSource L;
        internal static string OutDir;

        public override void Load()
        {
            L = Log;
            OutDir = Path.Combine(Paths.BepInExRootPath, "MissionLab", "recon");
            Directory.CreateDirectory(OutDir);
            AddComponent<ReconBehaviour>();
            L.LogInfo("MissionLab Recon " + Version + " loaded. Read-only. Output: " + OutDir + " (F7 = dump now)");
        }
    }

    public class ReconBehaviour : MonoBehaviour
    {
        public ReconBehaviour(IntPtr ptr) : base(ptr) { }

        private const float LateDelaySeconds = 45f;
        private const KeyCode ManualKey = KeyCode.F7;

        private IntPtr _raidPtr = IntPtr.Zero;   // raid instance we have already dumped
        private float _nextPoll;
        private float _nextSample;
        private bool _tracking;
        private float _lateAt = -1f;
        private bool _hotkeyOk = true;
        private bool _guiOk = true;
        private int _errors;
        private string _banner;
        private float _bannerUntil;

        private void Update()
        {
            if (_errors > 20) return;   // never spam the log or fight the game
            try { Tick(); }
            catch (Exception e) { _errors++; Plugin.L.LogError("tick failed: " + e); }
        }

        private void Tick()
        {
            float now = Time.unscaledTime;

            // Poll every frame while no raid is known (cheap), twice a second afterwards.
            if (_raidPtr == IntPtr.Zero || now >= _nextPoll)
            {
                _nextPoll = now + 0.5f;
                RaidManager raid = RaidManager.singleton;
                if (raid == null)
                {
                    _raidPtr = IntPtr.Zero;
                    _lateAt = -1f;
                    if (_tracking) { _tracking = false; MovementTracker.End(); }   // mission over: save the movement record
                }
                else if (raid.Pointer != _raidPtr)
                {
                    if (_tracking) MovementTracker.End();
                    _raidPtr = raid.Pointer;
                    Run("early");                      // before the game has spawned anything
                    _lateAt = now + LateDelaySeconds;  // and again once the map is populated
                    MovementTracker.Begin();
                    _tracking = true;
                    _nextSample = now + 5f;
                }
            }

            if (_tracking && now >= _nextSample)
            {
                _nextSample = now + 5f;
                MovementTracker.Sample();
            }

            if (_lateAt > 0f && now >= _lateAt)
            {
                _lateAt = -1f;
                Run("late");
            }

            if (_hotkeyOk)
            {
                try { if (Input.GetKeyDown(ManualKey)) Run("manual"); }
                catch (Exception e) { _hotkeyOk = false; Plugin.L.LogWarning("hotkey disabled: " + e.Message); }
            }
        }

        private void Run(string phase)
        {
            try
            {
                string summary = ReconDump.Write(phase);
                _banner = "MissionLab recon (" + phase + "): " + summary;
                _bannerUntil = Time.unscaledTime + 6f;
                Plugin.L.LogInfo(_banner);
            }
            catch (Exception e)
            {
                _errors++;
                Plugin.L.LogError("dump '" + phase + "' failed: " + e);
            }
        }

        private void OnGUI()
        {
            if (!_guiOk || _banner == null || Time.unscaledTime > _bannerUntil) return;
            try { GUI.Label(new Rect(24f, 24f, 1400f, 32f), _banner); }
            catch (Exception e) { _guiOk = false; Plugin.L.LogWarning("on-screen banner disabled: " + e.Message); }
        }
    }
}
