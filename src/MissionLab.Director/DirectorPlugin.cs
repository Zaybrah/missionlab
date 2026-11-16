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
        public const string Version = "0.20.0";

        internal static ManualLogSource L;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> UseHook;
        internal static ConfigEntry<int> Seed;
        internal static ConfigEntry<int> EnemyCount;
        internal static ConfigEntry<string> Scenes;
        internal static ConfigEntry<string> Scenario;
        internal static ConfigEntry<string> FairPlay;
        internal static ConfigEntry<bool> TellPlayers;
        internal static ConfigEntry<bool> GmEnabled;
        internal static ConfigEntry<bool> GmBlockKeys, LiveEnabled, MoveTarget, RandomExtras, ReplayEnabled;
        internal static ConfigEntry<int> ReplayKeep;
        internal static ConfigEntry<int> LivePort;
        internal static ConfigEntry<KeyCode> KeyGm, KeyCam, KeySave, KeyStance, KeyType, KeyStay, KeyWhen, KeyPlaceHere, KeyStop, KeySpawn, KeyRemove, KeyRemoveAll, KeyExport;
        internal static bool InputHooked;

        /// <summary>
        /// Hooks the one function all of the game's key binds go through. Done the first time
        /// Game Master mode opens, so a normal session never touches it.
        /// </summary>
        internal static void PatchInputBlock()
        {
            try { new Harmony(Id + ".input").PatchAll(typeof(InputBlockPatch)); InputHooked = true; L.LogInfo("the game's key binds are hooked: they stay silent while Game Master mode is open"); }
            catch (Exception e) { L.LogWarning("could not hook the game's key reader (" + e.Message + "); relying on the game's own input switch"); }
        }
        private static bool _extractionHooked, _insertionHooked;
        /// <summary>Hooked only when a scenario holds the extraction back, so other sessions never touch it.</summary>
        internal static void PatchExtraction()
        {
            if (_extractionHooked) return;
            _extractionHooked = true;
            try { new Harmony(Id + ".extraction").PatchAll(typeof(ExtractionHoldPatch)); L.LogInfo("the game's extraction switch is hooked: it waits for the scenario's trigger"); }
            catch (Exception e) { L.LogWarning("could not hook the game's extraction switch (" + e.Message + "); the extraction is shut again whenever the game opens it early"); }
        }

        /// <summary>Hooked only when a scenario names the insertion.</summary>
        internal static void PatchInsertion()
        {
            if (_insertionHooked) return;
            _insertionHooked = true;
            // Players pick their insertion in the lobby, so the choice is already made when the mission loads. It is set
            // again when the mission is set up and once more just before the game places the players.
            try { new Harmony(Id + ".insertion.start").PatchAll(typeof(InsertionStartPatch)); L.LogInfo("the start of the insertion is hooked: every player gets the scenario's insertion"); }
            catch (Exception e) { L.LogWarning("could not hook the start of the insertion (" + e.Message + "); the insertion is set when the mission loads only"); }
            try { new Harmony(Id + ".insertion").PatchAll(typeof(InsertionPatch)); }
            catch (Exception e) { L.LogWarning("could not hook the insertion request (" + e.Message + ")"); }
        }
        internal static bool Hooked;
        internal static string BaseDir;
        internal static ConfigFile Settings;

        public override void Load()
        {
            L = Log;
            Settings = Config;
            Enabled = Config.Bind("General", "Enabled", true,
                "Replace the enemy layout when you host a mission. false = unmodified game.");
            Seed = Config.Bind("General", "Seed", 0,
                "0 = a new random mission every time. Any other number replays that exact mission.");
            EnemyCount = Config.Bind("General", "EnemyCount", 0,
                "0 = use the enemy count chosen on the mission laptop. Otherwise force this many enemies (1-60).");
            Scenes = Config.Bind("General", "Scenes", "*",
                "Comma-separated scene names where the director is active. * = every PvE mission.");
            MoveTarget = Config.Bind("General", "MoveTarget", true,
                "In missions with a target person, a generated layout also picks a new place for the target. false = the game's own places.");
            RandomExtras = Config.Bind("General", "RandomExtras", true,
                "A random layout also gets a wave of enemies that comes once the enemy is alerted, an ambush where players walk into a building, a few locked doors, and the intel laid somewhere else. false = positions only.");
            FairPlay = Config.Bind("General", "FairPlay", "Off",
                "Whether missions played with the mod are marked with the game's own 'cheated' flag (the end screen then says cheating was detected, and there are no achievements). " +
                "Off = never. ModEndedWins = only when a trigger or the Game Master ends a mission as a success. Always = every mission the mod sets up. " +
                "The game may keep the mark until it is restarted. The studio leaves this choice to the mod.");
            TellPlayers = Config.Bind("General", "TellPlayers", true,
                "When others are in the mission, a notice on every player's screen a few seconds in says that the host runs MissionLab. false = no notice.");
            Scenario = Config.Bind("General", "Scenario", "*",
                "What the next mission uses. * = any scenario file in BepInEx/MissionLab/scenarios made for the map being played (a random one if several fit); maps without one get a generated layout. A file name = only that file. Nothing = always a random layout. (empty) = an empty map to build your own. In the lobby, F8 steps through these.");
            ReplayEnabled = Config.Bind("Replay", "Enabled", true,
                "Record where everybody was during each mission you host, for playing back on the map in the browser page. Files go to BepInEx/MissionLab/replays and stay on this computer.");
            ReplayKeep = Config.Bind("Replay", "Keep", 20, "How many recordings to keep; the oldest are removed.");
            GmEnabled = Config.Bind("GameMaster", "Enabled", true,
                "Game Master mode while you host a mission: place, spawn and remove enemies in the running mission and save the result as a scenario.");
            KeyGm = Config.Bind("GameMaster", "KeyOpen", KeyCode.F8, "Opens and closes Game Master mode.");
            KeyCam = Config.Bind("GameMaster", "KeyFreeCamera", KeyCode.F9, "Switches the game's free camera on and off.");
            KeySave = Config.Bind("GameMaster", "KeySave", KeyCode.F10, "Saves all markers as a scenario file.");
            GmBlockKeys = Config.Bind("GameMaster", "BlockGameKeys", true, "Switches the game's own key binds off while Game Master mode is open, so its keys do nothing else.");
            KeyStance = Config.Bind("GameMaster", "KeyStance", KeyCode.Z, "Next enemy stands or lies prone.");
            KeyType = Config.Bind("GameMaster", "KeyEnemyType", KeyCode.T, "Next enemy type (hold left Shift to go back).");
            KeyStay = Config.Bind("GameMaster", "KeyStaysPut", KeyCode.F, "Next enemy is a fixed sentry or not.");
            KeyWhen = Config.Bind("GameMaster", "KeyAppears", KeyCode.G, "New enemies appear at once, or wait for the spawn key.");
            KeyPlaceHere = Config.Bind("GameMaster", "KeyPlaceHere", KeyCode.Space, "Places an enemy where the camera is; it faces where the camera looks.");
            KeyStop = Config.Bind("GameMaster", "KeyRouteStop", KeyCode.R, "Adds a patrol stop at the crosshair for the picked enemy.");
            KeySpawn = Config.Bind("GameMaster", "KeySpawnWaiting", KeyCode.Return, "Spawns every enemy that is still waiting.");
            KeyRemove = Config.Bind("GameMaster", "KeyRemoveMarker", KeyCode.Delete, "Removes the enemy at the crosshair. With left Ctrl, twice: removes everything.");
            KeyExport = Config.Bind("GameMaster", "KeyExportMap", KeyCode.M, "Writes this map's solid geometry to BepInEx/MissionLab/maps, for the 3D view of the scenario editor.");
            KeyRemoveAll = Config.Bind("GameMaster", "KeyRemoveAllEnemies", KeyCode.F11, "Pressed twice: removes all live enemies; their markers stay.");
            UseHook = Config.Bind("Advanced", "UseHook", true,
                "true = act the moment the mission's spawner starts. false = slower polling fallback; try this if a mission fails to load.");

            BaseDir = Path.Combine(Paths.BepInExRootPath, "MissionLab");
            Directory.CreateDirectory(Path.Combine(BaseDir, "missions"));
            Directory.CreateDirectory(Path.Combine(BaseDir, "scenarios"));
            Directory.CreateDirectory(Path.Combine(BaseDir, "maps"));
            LiveEnabled = Config.Bind("LiveLink", "Enabled", true, "Serves the scenario editor at http://localhost:<Port> on this computer only, connected to the mission you host.");
            LivePort = Config.Bind("LiveLink", "Port", 8765, "Port for the browser link.");
            if (LiveEnabled.Value) LiveLink.Start(LivePort.Value);

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
    /// <summary>While a scenario holds the extraction back, the game's own "you may extract now" is noted and not carried out.</summary>
    [HarmonyPatch(typeof(RaidManager), nameof(RaidManager.EnableGlobalExtraction))]
    internal static class ExtractionHoldPatch
    {
        private static bool Prefix()
        {
            if (!GameMaster.HoldExtraction) return true;
            GameMaster.ExtractionAsked = true;
            return false;
        }
    }

    /// <summary>Just before the game puts the players on their insertion, everyone is given the scenario's.</summary>
    [HarmonyPatch(typeof(InfiltrationManager), nameof(InfiltrationManager.Server_StartInfil))]
    internal static class InsertionStartPatch
    {
        private static void Prefix() { try { GameMaster.ApplyForcedInfil("just before the insertion starts"); } catch { } }
    }

    /// <summary>Whatever insertion a player asks for, the host hands out the scenario's.</summary>
    [HarmonyPatch(typeof(PlayerMaster), nameof(PlayerMaster.UserCode_CMD_RequestInfilTarget__Int32))]
    internal static class InsertionPatch
    {
        private static void Prefix(ref int __0) { if (GameMaster.ForcedInfil >= 0) __0 = GameMaster.ForcedInfil; }
    }

    [HarmonyPatch(typeof(InputLayer), nameof(InputLayer.GetKey))]
    internal static class InputBlockPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!GameMaster.BlockGameKeys) return true;
            __result = false;       // no game key is "pressed" while the Game Master is working
            return false;
        }
    }

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
        private bool _gmGuiOk = true;
        private int _errors;

        private void Update()
        {
            if (_errors > 10) return;
            float now = Time.unscaledTime;
            try { Picker.Tick(); }
            catch (Exception e) { _errors++; DirectorPlugin.L.LogError("scenario picker failed: " + e); }
            try { LiveLink.Pump(); }
            catch (Exception e) { _errors++; DirectorPlugin.L.LogError("browser link failed: " + e); }
            try { GameMaster.Tick(now); }
            catch (Exception e) { _errors++; DirectorPlugin.L.LogError("Game Master failed: " + e); }
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
            if (_gmGuiOk)
                try { Picker.Draw(); }
                catch (Exception e) { _gmGuiOk = false; DirectorPlugin.L.LogWarning("scenario picker overlay disabled: " + e); }
            if (_gmGuiOk)
                try { GameMaster.Draw(); }
                catch (Exception e) { _gmGuiOk = false; DirectorPlugin.L.LogWarning("Game Master overlay disabled: " + e); }
            if (!_guiOk || Director.Banner == null || Time.unscaledTime > Director.BannerUntil) return;
            try { GUI.Label(new Rect(24f, 56f, 1400f, 32f), Director.Banner); }
            catch (Exception e) { _guiOk = false; DirectorPlugin.L.LogWarning("on-screen banner disabled: " + e.Message); }
        }
    }
}
