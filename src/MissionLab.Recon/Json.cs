using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MissionLab
{
    /// <summary>Minimal JSON writer. No dependencies, so nothing extra has to load inside the game.</summary>
    internal sealed class J
    {
        private readonly StringBuilder _sb = new StringBuilder(1 << 14);
        private readonly Stack<bool> _first = new Stack<bool>();

        public override string ToString() => _sb.ToString();

        private void Key(string key)
        {
            if (_first.Count > 0)
            {
                if (_first.Peek()) { _first.Pop(); _first.Push(false); }
                else _sb.Append(',');
            }
            if (key != null) { Str(key); _sb.Append(':'); }
        }

        private void Str(string s)
        {
            _sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }

        public J Obj(string key = null) { Key(key); _sb.Append('{'); _first.Push(true); return this; }
        public J EndObj() { _first.Pop(); _sb.Append('}'); return this; }
        public J Arr(string key = null) { Key(key); _sb.Append('['); _first.Push(true); return this; }
        public J EndArr() { _first.Pop(); _sb.Append(']'); return this; }

        public J V(string key, string v) { Key(key); if (v == null) _sb.Append("null"); else Str(v); return this; }
        public J V(string key, bool v) { Key(key); _sb.Append(v ? "true" : "false"); return this; }
        public J V(string key, int v) { Key(key); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public J V(string key, float v) { Key(key); Num(v); return this; }
        public J Raw(string key, string json) { Key(key); _sb.Append(json); return this; }

        public J Vec(string key, Vector3 v)
        {
            Key(key); _sb.Append('['); Num(v.x); _sb.Append(','); Num(v.y); _sb.Append(','); Num(v.z); _sb.Append(']');
            return this;
        }

        public J Vec(string key, Vector2 v)
        {
            Key(key); _sb.Append('['); Num(v.x); _sb.Append(','); Num(v.y); _sb.Append(']');
            return this;
        }

        private void Num(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) _sb.Append("null");
            else _sb.Append(System.Math.Round(f, 3).ToString(CultureInfo.InvariantCulture));
        }
    }
}
