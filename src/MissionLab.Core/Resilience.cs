using System;
using System.Collections.Generic;
using System.Text;

namespace MissionLab.Core
{
    /// <summary>
    /// One part of the mod that runs every frame, such as the browser link or the mission rules. Each part counts its own
    /// failures in a row. A few are simply logged. From the fifth on the part is paused, a little longer each time (2 s up to
    /// a minute), and tried once after each pause, so a broken part neither floods the log nor stops the other parts.
    /// One success, or a new mission, sets it right again.
    /// </summary>
    public sealed class Guard
    {
        public const int Limit = 5;
        public readonly string Name;
        public int Failures;             // in a row
        public int Pauses;               // how often it has been paused since it last worked
        public float RetryAt;            // not tried before this time
        public string LastError = "";
        public Guard(string name) { Name = name; }

        public bool Degraded => Failures >= Limit;
        public bool Due(float now) => now >= RetryAt;

        /// <summary>It worked. True when it had been paused before: worth one line in the log.</summary>
        public bool Ok()
        {
            bool recovered = Degraded;
            Failures = 0; Pauses = 0; RetryAt = 0f;
            return recovered;
        }

        /// <summary>It failed. Returns how long it is paused now (0: not paused, it runs again next frame).</summary>
        public float Failed(float now, string error)
        {
            Failures++; LastError = error ?? "";
            if (Failures < Limit) return 0f;
            Pauses++;
            float pause = Math.Min(60f, (float)Math.Pow(2, Math.Min(6, Pauses)));
            RetryAt = now + pause;
            return pause;
        }

        public void Fresh() { Failures = 0; Pauses = 0; RetryAt = 0f; LastError = ""; }

        public string Json(float now)
        {
            return "{\"name\":\"" + Esc(Name) + "\",\"ok\":" + (Failures == 0 ? "true" : "false") + ",\"paused\":" + (Degraded ? "true" : "false") + ",\"failures\":" + Failures
                 + ",\"retryIn\":" + Math.Max(0, (int)Math.Ceiling(RetryAt - now)) + ",\"lastError\":\"" + Esc(LastError) + "\"}";
        }

        internal static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r"); else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// What became of each change the page asked for. Every change carries a number of its own, made by the page. The same
    /// number sent twice is carried out once: the second time gets the first answer. A page that was closed and opened again
    /// can ask what became of a number. The last couple of hundred are kept, in memory only.
    /// Used from the web server's threads and from the game's thread, so every method locks.
    /// </summary>
    public sealed class CommandLedger
    {
        public const string Pending = "pending", Applied = "applied", Failed = "failed", Expired = "expired", Stale = "stale";
        public sealed class Entry
        {
            public string Id = "", Session = "", Path = "", Status = Pending, Said = "", Body = "";
            public int Code = 202;
            public float At;
        }
        private readonly Dictionary<string, Entry> _byId = new Dictionary<string, Entry>();
        private readonly Queue<string> _order = new Queue<string>();
        private readonly int _keep;
        public CommandLedger(int keep = 200) { _keep = Math.Max(8, keep); }

        /// <summary>A number the page may send: letters, digits, dash and underscore, up to 64. Anything else counts as no number.</summary>
        public static string Clean(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return "";
            foreach (char c in id) if (!(char.IsLetterOrDigit(c) && c < 128) && c != '-' && c != '_') return "";
            return id;
        }

        /// <summary>Notes a new change as pending. False when that number is known already: seen holds what is known of it.</summary>
        public bool Begin(string id, string session, string path, float at, out Entry seen)
        {
            lock (_byId)
            {
                if (_byId.TryGetValue(id, out seen)) return false;
                seen = new Entry { Id = id, Session = session ?? "", Path = path ?? "", At = at };
                _byId[id] = seen; _order.Enqueue(id);
                while (_order.Count > _keep) _byId.Remove(_order.Dequeue());
                return true;
            }
        }

        public void Finish(string id, string status, int code, string body, string said)
        {
            lock (_byId)
            {
                if (!_byId.TryGetValue(id, out Entry e)) return;
                e.Status = status; e.Code = code; e.Body = body ?? ""; e.Said = said ?? "";
            }
        }

        public bool TryGet(string id, out Entry e) { lock (_byId) return _byId.TryGetValue(id, out e); }

        /// <summary>One entry for the page: its state in a word, never the whole answer.</summary>
        public static string Json(Entry e)
        {
            return "{\"command\":\"" + Guard.Esc(e.Id) + "\",\"status\":\"" + e.Status + "\",\"path\":\"" + Guard.Esc(e.Path) + "\",\"session\":\"" + Guard.Esc(e.Session) + "\",\"said\":\"" + Guard.Esc(e.Said) + "\"}";
        }

        /// <summary>The newest entries, newest last.</summary>
        public string RecentJson(int count)
        {
            lock (_byId)
            {
                var ids = new List<string>(_order);
                var sb = new StringBuilder("[");
                for (int i = Math.Max(0, ids.Count - count); i < ids.Count; i++)
                {
                    if (!_byId.TryGetValue(ids[i], out Entry e)) continue;
                    if (sb.Length > 1) sb.Append(',');
                    sb.Append(Json(e));
                }
                return sb.Append(']').ToString();
            }
        }
    }
}
