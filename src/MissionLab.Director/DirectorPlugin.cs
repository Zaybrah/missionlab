using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace MissionLab
{
    /// <summary>
    /// MissionLab Director: an unofficial mod for OPERATOR. When you host a PvE
    /// mission it replaces the enemy layout with one generated from a seed, using
    /// the game's own spawning. It only acts on the host and only in the scenes
    /// listed in its config. Not made or endorsed by Vector Interactive.
    /// </summary>
    [BepInPlugin(Id, DisplayName, Version)]
    public class DirectorPlugin : BasePlugin
    {
        public const string Id = "missionlab.director";
        public const string DisplayName = "MissionLab Director (unofficial)";
        public const string Version = "0.3.0";

        internal static ManualLogSource L;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> UseHook;
        internal static ConfigEntry<int> Seed;
        internal static ConfigEntry<int> EnemyCount;
        internal static ConfigEntry<string> Scenes;
        internal static ConfigEntry<string> Scenario;
        internal static ConfigEntry<bool> GmTestKey;
        internal static bool Hooked;
        internal static string BaseDir;

        public override void Load()
        {
            L = Log;
            Enabled = Config.Bind("General", "Enabled", true,
                "Replace the enemy layout when you host a mission. false = unmodified game.");
            Seed = Config.Bind("General", "Seed", 0,
                "0 = a new random mission every time. Any other number replays that exact mission.");
            EnemyCount = Config.Bind("General", "EnemyCount", 0,
                "0 = use the enemy count chosen on the mission laptop. Otherwise force this many enemies (1-60).");
            Scenes = Config.Bind("General", "Scenes", "*",
                "Comma-separated scene names where the director is active. * = every PvE mission.");
            Scenario = Config.Bind("General", "Scenario", "",
                "Name of a file in BepInEx/MissionLab/scenarios to use instead of a generated layout (for the scene it was made for). Empty = generate.");
            GmTestKey = Config.Bind("GameMaster", "TestSpawnKey", true,
                "Experiment: while hosting a mission, F6 spawns one enemy about 15 m in front of you.");
            UseHook = Config.Bind("Advanced", "UseHook", true,
                "true = act the moment the mission's spawner starts. false = slower polling fallback; try this if a mission fails to load.");

            BaseDir = Path.Combine(Paths.BepInExRootPath, "MissionLab");
            Directory.CreateDirectory(Path.Combine(BaseDir, "missions"));
            Directory.CreateDirectory(Path.Combine(BaseDir, "scenarios"));

            if (UseHook.Value)
            {
                try
                {
                    new Harmony(Id).PatchAll(typeof(RaidStartPatch));
                    Hooked = true;
                }
                catch (Exception e)
                {
                    L.LogWarning("could not hook the mission spawner, falling back to polling: " + e);
                }
            }
            AddComponent<DirectorBehaviour>();
            L.LogInfo("MissionLab Director " + Version + " loaded. enabled=" + Enabled.Value + " hook=" + Hooked +
                      " seed=" + Seed.Value + " enemyCount=" + EnemyCount.Value + " scenes=[" + Scenes.Value + "] scenario=[" + Scenario.Value + "]");
        }
    }

    /// <summary>
    /// Runs right after the game's mission spawner has set itself up on the host.
    /// It has to be "after": the game fills its spawn list during that setup, and
    /// only spawns enemies a couple of seconds later.
    /// </summary>
    [HarmonyPatch(typeof(RaidManager), nameof(RaidManager.OnStartServer))]
    internal static class RaidStartPatch
    {
        private static void Postfix(RaidManager __instance)
        {
            try { Director.OnRaidStart(__instance, "hook"); }
            catch (Exception e) { DirectorPlugin.L.LogError("mission start hook failed (mission left unchanged if nothing was applied): " + e); }
        }
    }

    public class DirectorBehaviour : MonoBehaviour
    {
        public DirectorBehaviour(IntPtr ptr) : base(ptr) { }

        private float _nextPoll;
        private bool _guiOk = true;
        private int _errors;

        private void Update()
        {
            if (_errors > 10) return;
            float now = Time.unscaledTime;
            try { GmTest.Tick(now); }
            catch (Exception e) { _errors++; DirectorPlugin.L.LogError("GM test failed: " + e); }
            if (now < _nextPoll) return;
            try
            {
                // Safety net and retry: check every frame while a mission is waiting to be
                // handled (its spawn list may appear a moment after the hook), otherwise 4x a second.
                RaidManager raid = RaidManager.singleton;
                if (raid == null || raid.Pointer == Director.HandledRaidPtr) { _nextPoll = now + 0.25f; return; }
                Director.OnRaidStart(raid, "poll");
            }
            catch (Exception e) { _errors++; DirectorPlugin.L.LogError("poll failed: " + e); }
        }

        private void OnGUI()
        {
            if (!_guiOk || Director.Banner == null || Time.unscaledTime > Director.BannerUntil) return;
            try { GUI.Label(new Rect(24f, 56f, 1400f, 32f), Director.Banner); }
            catch (Exception e) { _guiOk = false; DirectorPlugin.L.LogWarning("on-screen banner disabled: " + e.Message); }
        }
    }
}
