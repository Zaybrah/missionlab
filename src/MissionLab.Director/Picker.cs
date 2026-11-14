using System;
using System.Collections.Generic;
using System.IO;
using MissionLab.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MissionLab
{
    /// <summary>
    /// Choosing what the next mission uses, while you are in the lobby picking the mission:
    /// any scenario that fits, a random layout, an empty map to build your own, or one
    /// scenario file by name. F8 steps through the choices; the browser page offers the same list.
    /// The choice is the General/Scenario setting, so it survives a restart.
    /// </summary>
    internal static class Picker
    {
        internal const string Any = "*", Random = "", Empty = "(empty)";
        internal sealed class Choice { public string Value, Label; }

        private static List<Choice> _choices = new List<Choice>();
        private static float _readAt = -100f, _showUntil = -1f;
        private static GUIStyle _style;

        private static string MapName(string navFile, string scene)
        {
            if (!string.IsNullOrWhiteSpace(scene)) return scene.Trim();
            string n = Path.GetFileNameWithoutExtension(navFile ?? "").ToLowerInvariant();
            return n == "suburbv2" || n == "suburb" ? "Clean House" : n == "depotraid1" ? "Depot Mexico" : n == "georgia1" ? "Georgia" : n.Length > 0 ? n : "any map";
        }

        internal static List<Choice> Choices()
        {
            if (Time.unscaledTime - _readAt < 3f) return _choices;
            _readAt = Time.unscaledTime;
            var list = new List<Choice>
            {
                new Choice { Value = Any, Label = "Any scenario made for the map (random if several)" },
                new Choice { Value = Random, Label = "Random layout" },
                new Choice { Value = Empty, Label = "Empty map: build your own" },
            };
            try
            {
                string dir = Path.Combine(DirectorPlugin.BaseDir, "scenarios");
                if (Directory.Exists(dir))
                    foreach (string path in Directory.GetFiles(dir, "*.json"))
                    {
                        string file = Path.GetFileNameWithoutExtension(path);
                        try
                        {
                            MissionPlan plan = MissionPlan.FromJson(File.ReadAllText(path));
                            list.Add(new Choice { Value = file, Label = file + "  (" + MapName(plan.NavFile, plan.Scene) + ", " + plan.Spawns.Count + " enemies)" });
                        }
                        catch (Exception) { list.Add(new Choice { Value = file, Label = file + "  (cannot be read)" }); }
                    }
            }
            catch (Exception) { }
            _choices = list;
            return list;
        }

        internal static string Current() => (DirectorPlugin.Scenario.Value ?? "").Trim();

        internal static string CurrentLabel()
        {
            string now = Current();
            foreach (Choice c in Choices()) if (string.Equals(c.Value, now, StringComparison.OrdinalIgnoreCase)) return c.Label;
            return "scenario '" + now + "' (file not found: a random layout will be used)";
        }

        internal static bool Choose(string value)
        {
            value = (value ?? "").Trim();
            foreach (Choice c in Choices())
                if (string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    DirectorPlugin.Scenario.Value = c.Value;        // written to the settings file at once
                    DirectorPlugin.L.LogInfo("next mission: " + c.Label);
                    return true;
                }
            return false;
        }

        /// <summary>True in the lobby: hosting, and no mission loaded.</summary>
        private static bool InLobby()
        {
            if (RaidManager.singleton != null) return false;
            if (Mirror.NetworkServer.active) return true;
            string scene = SceneManager.GetActiveScene().name ?? "";
            return scene.IndexOf("Operation", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Tick()
        {
            if (!InLobby() || !DirectorPlugin.Enabled.Value) return;
            if (!Input.GetKeyDown(DirectorPlugin.KeyGm.Value)) return;
            var list = Choices();
            if (Time.unscaledTime < _showUntil)                     // the list is open: step to the next choice
            {
                int at = 0; string now = Current();
                for (int i = 0; i < list.Count; i++) if (string.Equals(list[i].Value, now, StringComparison.OrdinalIgnoreCase)) at = i;
                Choose(list[(at + 1) % list.Count].Value);
            }
            _showUntil = Time.unscaledTime + 8f;
        }

        public static void Draw()
        {
            if (!InLobby() || !DirectorPlugin.Enabled.Value) return;
            Event ev = Event.current;
            if (ev != null && ev.type != EventType.Repaint) return;
            if (_style == null) { try { _style = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false }; } catch { return; } }
            bool open = Time.unscaledTime < _showUntil;
            var list = Choices();
            string now = Current();
            float lh = 21f, w = 640f, x = 24f;
            int lines = open ? list.Count + 2 : 1;
            float y = Screen.height - 40f - lines * lh;
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(x - 10f, y - 6f, w, lines * lh + 12f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (!open) { GUI.Label(new Rect(x, y, w, 22f), "MissionLab  next mission: " + CurrentLabel() + "   (" + DirectorPlugin.KeyGm.Value + " to change)", _style); return; }
            GUI.Label(new Rect(x, y, w, 22f), "MISSIONLAB  what the next mission uses   (" + DirectorPlugin.KeyGm.Value + " = next choice)", _style); y += lh;
            foreach (Choice c in list)
            {
                bool on = string.Equals(c.Value, now, StringComparison.OrdinalIgnoreCase);
                GUI.color = on ? new Color(1f, 0.82f, 0.25f) : Color.white;
                GUI.Label(new Rect(x, y, w, 22f), (on ? "> " : "   ") + c.Label, _style); y += lh;
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, w, 22f), "Also at http://localhost:" + LiveLink.Port + " in a browser.", _style);
        }

        internal static string Json()
        {
            var sb = new System.Text.StringBuilder(512);
            string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "'") + "\"";
            sb.Append("\"choice\":").Append(Q(Current())).Append(",\"choices\":[");
            var list = Choices();
            for (int i = 0; i < list.Count; i++) sb.Append(i > 0 ? "," : "").Append("{\"value\":").Append(Q(list[i].Value)).Append(",\"label\":").Append(Q(list[i].Label)).Append('}');
            return sb.Append(']').ToString();
        }
    }
}
