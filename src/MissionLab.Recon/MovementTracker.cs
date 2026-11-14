using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MissionLab
{
    /// <summary>
    /// Follows every enemy through a mission: where it was first seen, how far it
    /// got from there, when it first moved, and which states it was in. Written to
    /// recon/&lt;scene&gt;.movement.json once a minute and when the mission ends.
    /// Read-only, sampled every 5 seconds.
    ///
    /// Two positions are kept because the game moves enemies through a pathfinding
    /// entity (AgentController): the object's own transform and the agent position
    /// can disagree, and we want to know which one tells the truth.
    /// </summary>
    internal static class MovementTracker
    {
        private sealed class Track
        {
            public string Name, MyName, Idle;
            public bool IsHvt, HasAgent;
            public Vector3 First, Last, AgentFirst, AgentLast;
            public float MaxDist, AgentMaxDist, FirstSeen, LastSeen, FirstMovedAt = -1f, DeadAt = -1f;
            public int Samples, WithPath, PatrolPoints = -1, Waypoint = -1;
            public readonly Dictionary<string, int> States = new Dictionary<string, int>();
        }

        private static readonly Dictionary<IntPtr, Track> Tracks = new Dictionary<IntPtr, Track>();
        private static string _scene;
        private static float _start, _lastWrite;
        private static int _errors;
        private static string _lastError;

        public static void Begin()
        {
            Tracks.Clear();
            _scene = SceneManager.GetActiveScene().name ?? "unknown";
            _start = Time.unscaledTime;
            _lastWrite = _start;
            _errors = 0;
            _lastError = null;
        }

        public static void Sample()
        {
            GameManager g = GameManager.instance;
            var ai = g == null ? null : g.allAI;
            if (ai == null) return;
            float t = Time.unscaledTime - _start;
            int n = Math.Min(ai.Count, 400);
            for (int i = 0; i < n; i++)
            {
                try
                {
                    BrainAI b = ai[i];
                    if (b == null) continue;
                    Vector3 p = b.transform.position;
                    if (!Tracks.TryGetValue(b.Pointer, out Track tr))
                    {
                        tr = new Track { Name = b.name, MyName = b.myName, IsHvt = b.isHVT, Idle = b.idleStates.ToString(), First = p, FirstSeen = t };
                        Tracks[b.Pointer] = tr;
                    }
                    tr.Samples++;
                    tr.Last = p; tr.LastSeen = t;
                    float d = Vector3.Distance(p, tr.First);
                    if (d > tr.MaxDist) tr.MaxDist = d;

                    AgentController ac = b.agent;
                    if (ac != null && ac.entityExists)
                    {
                        Vector3 ap = ac.position;
                        if (!tr.HasAgent) { tr.HasAgent = true; tr.AgentFirst = ap; }
                        tr.AgentLast = ap;
                        float ad = Vector3.Distance(ap, tr.AgentFirst);
                        if (ad > tr.AgentMaxDist) tr.AgentMaxDist = ad;
                        if (ac.hasPath) tr.WithPath++;
                    }
                    if (tr.FirstMovedAt < 0f && Math.Max(tr.MaxDist, tr.AgentMaxDist) > 1.5f) tr.FirstMovedAt = t;

                    string state = b.CurrentState.ToString();
                    tr.States.TryGetValue(state, out int seen);
                    tr.States[state] = seen + 1;

                    Health h = b.health;
                    if (h != null && h.isDead && tr.DeadAt < 0f) tr.DeadAt = t;

                    AIPatrol patrol = b.aiPatrol;
                    if (patrol != null)
                    {
                        var pts = patrol.patrolPoints;
                        tr.PatrolPoints = pts == null ? 0 : pts.Count;
                        tr.Waypoint = patrol.currentWayPointIndex;
                    }
                }
                catch (Exception e)
                {
                    _errors++;
                    _lastError = e.GetType().Name + ": " + e.Message;
                }
            }
            if (Time.unscaledTime - _lastWrite >= 60f) Write();
        }

        public static void Write()
        {
            if (_scene == null || Tracks.Count == 0) return;
            _lastWrite = Time.unscaledTime;
            var j = new J();
            j.Obj();
            j.V("schema", "missionlab-movement/2");
            j.V("plugin", Plugin.Version);
            j.V("scene", _scene);
            j.V("writtenUtc", DateTime.UtcNow.ToString("o"));
            j.V("trackedSeconds", Time.unscaledTime - _start);
            j.V("sampleErrors", _errors);
            j.V("lastError", _lastError);
            j.Arr("bots");
            foreach (Track tr in Tracks.Values)
            {
                j.Obj();
                j.V("name", tr.Name);
                j.V("myName", tr.MyName);
                j.V("isHVT", tr.IsHvt);
                j.V("idle", tr.Idle);
                j.Vec("first", tr.First);
                j.Vec("last", tr.Last);
                j.V("maxDistance", tr.MaxDist);
                j.V("hasAgent", tr.HasAgent);
                if (tr.HasAgent)
                {
                    j.Vec("agentFirst", tr.AgentFirst);
                    j.Vec("agentLast", tr.AgentLast);
                    j.V("agentMaxDistance", tr.AgentMaxDist);
                }
                j.V("samples", tr.Samples);
                j.V("samplesWithPath", tr.WithPath);
                var states = new StringBuilder();
                foreach (var kv in tr.States) states.Append(states.Length > 0 ? "," : "").Append(kv.Key).Append(':').Append(kv.Value);
                j.V("states", states.ToString());
                j.V("patrolPoints", tr.PatrolPoints);
                j.V("waypoint", tr.Waypoint);
                j.V("firstSeenAt", tr.FirstSeen);
                j.V("firstMovedAt", tr.FirstMovedAt);
                j.V("lastSeenAt", tr.LastSeen);
                j.V("deadAt", tr.DeadAt);
                j.EndObj();
            }
            j.EndArr();
            j.EndObj();
            var name = new StringBuilder();
            foreach (char c in _scene) name.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
            File.WriteAllText(Path.Combine(Plugin.OutDir, name + ".movement.json"), j.ToString(), new UTF8Encoding(false));
        }

        public static void End()
        {
            try { Write(); }
            finally { Tracks.Clear(); _scene = null; }
        }
    }
}
