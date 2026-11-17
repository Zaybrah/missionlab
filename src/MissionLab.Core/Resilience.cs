using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// What became of one part of a change: one marker added, moved or removed, one enemy given an order, one trigger set off.
    /// Status is one of CommandLedger.Applied, Failed, Pending (asked of the game, the answer is still to come) or Unconfirmed
    /// (the call was made, but whether it took cannot be seen). Applied means seen: for a spawn, the enemy exists.
    /// </summary>
    public sealed class CommandOp
    {
        public string Kind = "", Ref = "", Status = CommandLedger.Applied, Said = "";
        public int At = -1;     // its place in the list the page sent (-1: not in that list, as for a removed marker)
        public CommandOp() { }
        public CommandOp(string kind, string reference, int at, string status, string said)
        {
            Kind = kind ?? ""; Ref = reference ?? ""; At = at; Status = status ?? CommandLedger.Applied; Said = said ?? "";
        }
    }

    /// <summary>
    /// The operations of one change. At most Keep are listed, so one huge change cannot fill the memory or the page; the
    /// ones that do not fit are only counted. When full, an applied one makes room for one that did not simply work,
    /// because those are the ones the page has to show.
    /// </summary>
    public sealed class Outcome
    {
        public const int Keep = 100;
        public readonly List<CommandOp> Ops = new List<CommandOp>();
        public int LeftOut;                                     // operations counted but not listed
        private int _leftApplied, _leftFailed, _leftUnconfirmed;

        public void Add(CommandOp op)
        {
            if (op == null) return;
            if (Ops.Count < Keep) { Ops.Add(op); return; }
            if (op.Status != CommandLedger.Applied)
            {
                int plain = Ops.FindIndex(o => o.Status == CommandLedger.Applied);
                if (plain >= 0) { Ops.RemoveAt(plain); Count(CommandLedger.Applied); Ops.Add(op); return; }
            }
            Count(op.Status);
        }

        // One that is left out is never followed up, so a pending one counts as unconfirmed.
        private void Count(string status)
        {
            LeftOut++;
            if (status == CommandLedger.Applied) _leftApplied++;
            else if (status == CommandLedger.Failed) _leftFailed++;
            else _leftUnconfirmed++;
        }

        /// <summary>The whole change in a word: applied only when every part was; partial when some failed and some did not.</summary>
        public string Overall(string none)
        {
            int applied = _leftApplied, failed = _leftFailed, pending = 0, total = LeftOut + Ops.Count;
            foreach (CommandOp o in Ops)
            {
                if (o.Status == CommandLedger.Applied) applied++;
                else if (o.Status == CommandLedger.Failed) failed++;
                else if (o.Status == CommandLedger.Pending) pending++;
            }
            if (total == 0) return none;
            if (applied == total) return CommandLedger.Applied;
            if (failed == total) return CommandLedger.Failed;
            if (failed > 0) return CommandLedger.Partial;
            if (pending > 0) return CommandLedger.Pending;
            return CommandLedger.Unconfirmed;
        }

        /// <summary>
        /// The list part of the JSON, without braces:
        /// "ops":[{"op":"move","ref":"12","at":3,"status":"failed","said":"why"}, ...],"left":0
        /// ref is the marker id, trigger id or list place the operation is about; left counts operations not listed.
        /// </summary>
        public string Json()
        {
            var sb = new StringBuilder("\"ops\":[");
            for (int i = 0; i < Ops.Count; i++)
            {
                CommandOp o = Ops[i];
                sb.Append(i > 0 ? ",{\"op\":\"" : "{\"op\":\"").Append(Guard.Esc(o.Kind)).Append("\",\"ref\":\"").Append(Guard.Esc(o.Ref)).Append("\",\"at\":").Append(o.At.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"status\":\"").Append(Guard.Esc(o.Status)).Append("\",\"said\":\"").Append(Guard.Esc(o.Said)).Append("\"}");
            }
            return sb.Append("],\"left\":").Append(LeftOut.ToString(CultureInfo.InvariantCulture)).ToString();
        }
    }

    /// <summary>
    /// What became of each change the page asked for. Every change carries a number of its own, made by the page. The same
    /// number sent twice is carried out once: the second time gets the first answer. A page that was closed and opened again
    /// can ask what became of a number, part by part. The last couple of hundred are kept, in memory only.
    /// Used from the web server's threads and from the game's thread, so every method locks.
    /// </summary>
    public sealed class CommandLedger
    {
        public const string Pending = "pending", Applied = "applied", Failed = "failed", Expired = "expired", Stale = "stale", Partial = "partial", Unconfirmed = "unconfirmed";
        public sealed class Entry
        {
            public string Id = "", Session = "", Path = "", Status = Pending, Said = "", Body = "";
            public int Code = 202;
            public float At;
            public bool Done;               // the game has answered it (or it was struck off); before that a repeat is told to wait
            public Outcome Result;          // its parts, when the game reported them (null: none)
            public bool ByParts;            // Status follows the parts, as the spawns they wait for finish
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

        /// <summary>
        /// The parts of a change, handed over by the game before it answers. The ledger owns them from now on: they are
        /// changed only through Settle, under the lock.
        /// </summary>
        public void Record(string id, Outcome result)
        {
            if (string.IsNullOrEmpty(id) || result == null) return;
            lock (_byId) { if (_byId.TryGetValue(id, out Entry e)) e.Result = result; }
        }

        /// <summary>The game has answered. A change it carried out takes its status from its parts, when it has any.</summary>
        public void Finish(string id, string status, int code, string body, string said)
        {
            lock (_byId)
            {
                if (!_byId.TryGetValue(id, out Entry e)) return;
                e.ByParts = status == Applied && e.Result != null;
                e.Status = e.ByParts ? e.Result.Overall(Applied) : status;
                e.Code = code; e.Body = body ?? ""; e.Said = said ?? ""; e.Done = true;
            }
        }

        /// <summary>A part that was pending has its answer now, such as a queued spawn that made its enemy or did not.</summary>
        public bool Settle(string id, string reference, string status, string said)
        {
            lock (_byId)
            {
                if (string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out Entry e) || e.Result == null) return false;
                CommandOp op = e.Result.Ops.Find(o => o.Ref == reference && o.Status == Pending);
                if (op == null) return false;
                op.Status = status; op.Said = said ?? "";
                if (e.ByParts) e.Status = e.Result.Overall(Applied);
                return true;
            }
        }

        /// <summary>The mission is over: whatever was still on its way into it will not come. Returns how many parts that was.</summary>
        public int Abandon(string why)
        {
            int n = 0;
            lock (_byId)
            {
                foreach (Entry e in _byId.Values)
                {
                    if (e.Result == null) continue;
                    foreach (CommandOp op in e.Result.Ops) if (op.Status == Pending) { op.Status = Failed; op.Said = why ?? ""; n++; }
                    if (e.ByParts) e.Status = e.Result.Overall(Applied);
                }
            }
            return n;
        }

        public bool TryGet(string id, out Entry e) { lock (_byId) return _byId.TryGetValue(id, out e); }

        /// <summary>
        /// One entry for the page: {"command":"c1","status":"partial","path":"/api/markers","session":"m1-123","done":true,
        /// "ops":[...],"left":0,"said":""}. With parts false, "ops" and "left" are left out (the short list in every state answer).
        /// </summary>
        public static string Json(Entry e, bool parts = true)
        {
            string ops = parts && e.Result != null ? "," + e.Result.Json() : parts ? ",\"ops\":[],\"left\":0" : "";
            return "{\"command\":\"" + Guard.Esc(e.Id) + "\",\"status\":\"" + e.Status + "\",\"path\":\"" + Guard.Esc(e.Path) + "\",\"session\":\"" + Guard.Esc(e.Session) + "\",\"done\":" + (e.Done ? "true" : "false")
                 + ops + ",\"said\":\"" + Guard.Esc(e.Said) + "\"}";
        }

        /// <summary>A copy of one entry made under the lock, so a web thread never reads parts the game is changing.</summary>
        public string JsonOf(string id)
        {
            lock (_byId) return _byId.TryGetValue(id, out Entry e) ? Json(e) : null;
        }

        /// <summary>The newest entries, newest last, without their parts.</summary>
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
                    sb.Append(Json(e, false));
                }
                return sb.Append(']').ToString();
            }
        }
    }
}
