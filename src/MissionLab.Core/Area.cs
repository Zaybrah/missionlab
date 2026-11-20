using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionLab.Core
{
    /// <summary>A point on the ground plan: world X and Z in metres.</summary>
    public struct P2
    {
        public float X, Z;
        public P2(float x, float z) { X = x; Z = z; }
    }

    /// <summary>
    /// One floor piece of a shaped area: a ring on the ground plan, rings cut out of it, and the band of heights it covers.
    /// A two-storey house is two parts with different bands, with a third for the stairs between them.
    /// </summary>
    public sealed class AreaPart
    {
        public List<P2> Outline = new List<P2>();            // either winding; the first point is not repeated at the end
        public List<List<P2>> Holes = new List<List<P2>>();  // a courtyard or a light well: not part of the area, but their edges are
        public float Bottom, Top;                             // world Y in metres; both ends count
    }

    /// <summary>
    /// A painted trigger area (ML-027): the union of its parts, so parts may touch or overlap and walking across a seam
    /// never leaves the area. The editor carries the same rules in tools/editor/src/21a-area.js and both are held to
    /// tests/fixtures/area_points.json. Contains caches bounds: call Prepare after changing the parts.
    /// </summary>
    public sealed class AreaPlan
    {
        public const int MaxParts = 32, MaxRing = 256, MaxPoints = 2048;
        public const double MaxFloor = 60, MaxCoord = 100000, MinOutline = 0.25, MinHole = 0.05;
        private const double Near = 0.01;      // Membership.Edge as a double, so the editor and the plugin compare alike

        public List<AreaPart> Parts = new List<AreaPart>();

        // What Contains needs, worked out once: rings as plain doubles and the bounds of each part and of the whole area.
        private sealed class Piece
        {
            public double MinX, MaxX, MinZ, MaxZ, Bottom, Top;
            public double[] Outline;
            public double[][] Holes;
        }
        private Piece[] _pieces;
        private double _minX, _maxX, _minZ, _maxZ, _bottom, _top;

        /// <summary>Works out the bounds Contains checks first. Safe to call again; call it after any change to the parts.</summary>
        public void Prepare()
        {
            var pieces = new Piece[Parts.Count];
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            double bottom = double.PositiveInfinity, top = double.NegativeInfinity;
            for (int i = 0; i < Parts.Count; i++)
            {
                AreaPart part = Parts[i];
                var piece = new Piece { Outline = Flat(part.Outline), Holes = new double[part.Holes.Count][], Bottom = part.Bottom, Top = part.Top,
                    MinX = double.PositiveInfinity, MaxX = double.NegativeInfinity, MinZ = double.PositiveInfinity, MaxZ = double.NegativeInfinity };
                for (int h = 0; h < part.Holes.Count; h++) piece.Holes[h] = Flat(part.Holes[h]);
                for (int k = 0; k < piece.Outline.Length; k += 2)
                {
                    piece.MinX = Math.Min(piece.MinX, piece.Outline[k]); piece.MaxX = Math.Max(piece.MaxX, piece.Outline[k]);
                    piece.MinZ = Math.Min(piece.MinZ, piece.Outline[k + 1]); piece.MaxZ = Math.Max(piece.MaxZ, piece.Outline[k + 1]);
                }
                minX = Math.Min(minX, piece.MinX); maxX = Math.Max(maxX, piece.MaxX); minZ = Math.Min(minZ, piece.MinZ); maxZ = Math.Max(maxZ, piece.MaxZ);
                bottom = Math.Min(bottom, piece.Bottom); top = Math.Max(top, piece.Top);
                pieces[i] = piece;
            }
            _minX = minX; _maxX = maxX; _minZ = minZ; _maxZ = maxZ; _bottom = bottom; _top = top;
            _pieces = pieces;
        }

        /// <summary>
        /// Whether a point is in the area: in some part's height band (no tolerance), in its outline (within 0.01 m of an
        /// edge counts), and not well inside one of its holes. Bounds are checked first; nothing is allocated per call.
        /// </summary>
        public bool Contains(V3 p)
        {
            Piece[] pieces = _pieces;
            if (pieces == null || pieces.Length != Parts.Count) { Prepare(); pieces = _pieces; }
            double x = p.X, y = p.Y, z = p.Z;
            if (pieces.Length == 0 || y < _bottom || y > _top || x < _minX - Near || x > _maxX + Near || z < _minZ - Near || z > _maxZ + Near) return false;
            for (int i = 0; i < pieces.Length; i++)
            {
                Piece piece = pieces[i];
                if (y < piece.Bottom || y > piece.Top || x < piece.MinX - Near || x > piece.MaxX + Near || z < piece.MinZ - Near || z > piece.MaxZ + Near) continue;
                if (!InRing(piece.Outline, x, z)) continue;
                bool cut = false;
                for (int h = 0; h < piece.Holes.Length && !cut; h++) cut = WellInside(piece.Holes[h], x, z);
                if (!cut) return true;
            }
            return false;
        }

        /// <summary>
        /// The lenient reading, for live edits and run records: points that are not two numbers are skipped, a ring left with
        /// fewer than 3 points is dropped, an upside-down floor is turned round, and a part without a floor or a usable outline
        /// is dropped. null when no part is left, so the trigger falls back to its circle.
        /// </summary>
        public static AreaPlan Read(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("parts", out JsonElement parts) || parts.ValueKind != JsonValueKind.Array) return null;
            var area = new AreaPlan();
            foreach (JsonElement one in parts.EnumerateArray())
            {
                if (one.ValueKind != JsonValueKind.Object) continue;
                if (!one.TryGetProperty("floor", out JsonElement floor) || !Pair(floor, out double bottom, out double top)) continue;     // no band of heights to go by
                if (top < bottom) { double swap = top; top = bottom; bottom = swap; }
                List<P2> outline = one.TryGetProperty("outline", out JsonElement ring) ? LooseRing(ring) : null;
                if (outline == null) continue;
                var part = new AreaPart { Outline = outline, Bottom = (float)bottom, Top = (float)top };
                if (one.TryGetProperty("holes", out JsonElement holes) && holes.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement hole in holes.EnumerateArray())
                    {
                        List<P2> cut = LooseRing(hole);
                        if (cut != null) part.Holes.Add(cut);
                    }
                area.Parts.Add(part);
            }
            return area.Parts.Count == 0 ? null : area;
        }

        /// <summary>The canonical text, {"parts":[...]} with 2 decimals; the caller writes the "area" key. "holes" is left out when empty.</summary>
        public void Write(StringBuilder sb)
        {
            sb.Append("{\"parts\":[");
            for (int i = 0; i < Parts.Count; i++)
            {
                AreaPart part = Parts[i];
                sb.Append(i > 0 ? ",{\"floor\":[" : "{\"floor\":[").Append(Written(part.Bottom)).Append(',').Append(Written(part.Top)).Append("],\"outline\":");
                WriteRing(sb, part.Outline);
                if (part.Holes.Count > 0)
                {
                    sb.Append(",\"holes\":[");
                    for (int h = 0; h < part.Holes.Count; h++) { if (h > 0) sb.Append(','); WriteRing(sb, part.Holes[h]); }
                    sb.Append(']');
                }
                sb.Append('}');
            }
            sb.Append("]}");
        }

        /// <summary>The canonical text of the shape: two areas with the same signature are the same shape once written.</summary>
        public string Signature()
        {
            var sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }

        /// <summary>
        /// An independent copy of the parts, for a trigger that stops following a named place (MissionPlan.Flatten): changing
        /// one afterwards must never change the other.
        /// </summary>
        public AreaPlan Copy()
        {
            var copy = new AreaPlan();
            foreach (AreaPart part in Parts)
            {
                var one = new AreaPart { Outline = new List<P2>(part.Outline), Bottom = part.Bottom, Top = part.Top };
                foreach (List<P2> hole in part.Holes) one.Holes.Add(new List<P2>(hole));
                copy.Parts.Add(one);
            }
            return copy;
        }

        /// <summary>
        /// The strict rules for the "area" of a trigger or a place, found at <paramref name="at"/> ("triggers[3]"; paths read
        /// "triggers[3].area.parts[0].outline"). At most one finding per ring: the first rule it breaks. Never throws.
        /// </summary>
        public static void Check(JsonElement e, string at, Action<string, string, string> bad)
        {
            string a = at == "" ? "area" : at + ".area";
            if (e.ValueKind != JsonValueKind.Object) { bad("wrong-type", a, "\"area\" is an object: {\"parts\": [...]}"); return; }
            if (!e.TryGetProperty("parts", out JsonElement parts) || parts.ValueKind != JsonValueKind.Array) { bad("wrong-type", a + ".parts", "\"parts\" is a list of floor pieces"); return; }
            int count = parts.GetArrayLength();
            if (count == 0) { bad("bad-area", a + ".parts", "an area needs at least one part"); return; }
            if (count > MaxParts) { bad("bad-area", a + ".parts", "an area has at most " + MaxParts + " parts; this one has " + count); return; }
            int points = 0, j = 0;
            foreach (JsonElement part in parts.EnumerateArray())
            {
                string pa = a + ".parts[" + j++ + "]";
                if (part.ValueKind != JsonValueKind.Object) { bad("wrong-type", pa, "a part is an object: {\"floor\": [bottom, top], \"outline\": [[x, z], ...]}"); continue; }
                if (!Has(part, "floor", out JsonElement floor) || !Pair(floor, out double bottom, out double top)) bad("wrong-type", pa + ".floor", "\"floor\" is [bottom, top]: two heights in metres");
                else if (top <= bottom) bad("bad-area", pa + ".floor", "the top of \"floor\" must be above its bottom");
                else if (top - bottom > MaxFloor) bad("bad-area", pa + ".floor", "a part covers at most " + MaxFloor + " m of height; split taller places into parts");

                double[] outline = null;
                if (!Has(part, "outline", out JsonElement ring) || ring.ValueKind != JsonValueKind.Array) bad("wrong-type", pa + ".outline", "\"outline\" is a list of points [x, z]");
                else { points += ring.GetArrayLength(); outline = StrictRing(ring, pa + ".outline", bad); }

                var holes = new List<double[]>();
                var holePaths = new List<string>();
                if (Has(part, "holes", out JsonElement holeList))
                {
                    if (holeList.ValueKind != JsonValueKind.Array) bad("wrong-type", pa + ".holes", "\"holes\" is a list of rings");
                    else
                    {
                        int h = 0;
                        foreach (JsonElement hole in holeList.EnumerateArray())
                        {
                            string hp = pa + ".holes[" + h++ + "]";
                            if (hole.ValueKind != JsonValueKind.Array) { bad("wrong-type", hp, "a hole is a list of points [x, z]"); continue; }
                            points += hole.GetArrayLength();
                            double[] cut = StrictRing(hole, hp, bad);
                            if (cut != null) { holes.Add(cut); holePaths.Add(hp); }
                        }
                    }
                }

                // Geometry: the outline, then each hole on its own, then each hole against the outline and the holes before it.
                if (outline != null)
                {
                    string wrong = Shape(outline, MinOutline);
                    if (wrong != null) { bad("bad-area", pa + ".outline", wrong); outline = null; }
                }
                var good = new List<double[]>();
                for (int h = 0; h < holes.Count; h++)
                {
                    double[] hole = holes[h];
                    string wrong = Shape(hole, MinHole);
                    if (wrong == null && outline != null && !HoleWithin(hole, outline)) wrong = "a hole must lie inside its outline";
                    for (int g = 0; wrong == null && g < good.Count; g++)
                        if (Overlap(hole, good[g])) wrong = "this hole overlaps an earlier hole; draw them as one";
                    if (wrong != null) bad("bad-area", holePaths[h], wrong);
                    else good.Add(hole);
                }
            }
            if (points > MaxPoints) bad("bad-area", a, "an area has at most " + MaxPoints + " points; this one has " + points);
        }

        // ----- reading

        private static bool Has(JsonElement o, string key, out JsonElement value) => o.TryGetProperty(key, out value) && value.ValueKind != JsonValueKind.Null;

        // A number too large for a double reads as infinity, as JavaScript reads it, so the size rules catch it the same way.
        private static double Number(JsonElement e) => e.TryGetDouble(out double v) ? v : e.GetRawText().StartsWith("-", StringComparison.Ordinal) ? double.NegativeInfinity : double.PositiveInfinity;

        private static bool Pair(JsonElement e, out double a, out double b)
        {
            a = b = 0;
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2 || e[0].ValueKind != JsonValueKind.Number || e[1].ValueKind != JsonValueKind.Number) return false;
            a = Number(e[0]); b = Number(e[1]);
            return true;
        }

        private static double[] StrictRing(JsonElement ring, string path, Action<string, string, string> bad)
        {
            var flat = new double[ring.GetArrayLength() * 2];
            int k = 0;
            foreach (JsonElement point in ring.EnumerateArray())
            {
                if (!Pair(point, out double x, out double z)) { bad("wrong-type", path + "[" + k + "]", "a point is [x, z]: two numbers"); return null; }
                flat[2 * k] = x; flat[2 * k + 1] = z; k++;
            }
            return flat;
        }

        private static List<P2> LooseRing(JsonElement ring)
        {
            if (ring.ValueKind != JsonValueKind.Array) return null;
            var list = new List<P2>();
            foreach (JsonElement point in ring.EnumerateArray())
                if (Pair(point, out double x, out double z) && !double.IsInfinity(x) && !double.IsInfinity(z)) list.Add(new P2((float)x, (float)z));
            return list.Count < 3 ? null : list;
        }

        // ----- writing

        private static string Written(float f)
        {
            double v = Math.Round(f, 2);
            if (v == 0) v = 0;      // no "-0": the editor writes 0
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private static void WriteRing(StringBuilder sb, List<P2> ring)
        {
            sb.Append('[');
            for (int k = 0; k < ring.Count; k++)
                sb.Append(k > 0 ? ",[" : "[").Append(Written(ring[k].X)).Append(',').Append(Written(ring[k].Z)).Append(']');
            sb.Append(']');
        }

        // ----- geometry, on rings stored as x0, z0, x1, z1, ...; the editor's copy follows these lines one for one

        private static double[] Flat(List<P2> ring)
        {
            var flat = new double[ring.Count * 2];
            for (int k = 0; k < ring.Count; k++) { flat[2 * k] = ring[k].X; flat[2 * k + 1] = ring[k].Z; }
            return flat;
        }

        // Why a ring cannot be used, or null when it can.
        private static string Shape(double[] r, double smallest)
        {
            int n = r.Length / 2;
            if (n < 3) return "a ring needs at least 3 points";
            if (n > MaxRing) return "a ring has at most " + MaxRing + " points; this one has " + n;
            for (int k = 0; k < r.Length; k++) if (!(Math.Abs(r[k]) <= MaxCoord)) return "a point lies more than " + MaxCoord + " m from the middle of the map";
            for (int k = 0; k < n; k++)
            {
                int next = (k + 1) % n;
                if (r[2 * k] == r[2 * next] && r[2 * k + 1] == r[2 * next + 1]) return "point " + next + " is the same as the one before it; leave out repeated points, including a closing one";
            }
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;     // neighbours share a corner
                    if (Touch(r, i, r, j)) return "the edges of this ring cross or touch each other";
                }
            if (Math.Abs(Shoelace(r)) / 2 < smallest) return "the ring encloses less than " + smallest.ToString(CultureInfo.InvariantCulture) + " square metres";
            return null;
        }

        private static double Shoelace(double[] r)
        {
            int n = r.Length / 2;
            double sum = 0;
            for (int k = 0; k < n; k++)
            {
                int next = (k + 1) % n;
                sum += r[2 * k] * r[2 * next + 1] - r[2 * next] * r[2 * k + 1];
            }
            return sum;
        }

        private static double Orient(double ax, double az, double bx, double bz, double cx, double cz) => (bx - ax) * (cz - az) - (bz - az) * (cx - ax);

        private static bool Between(double ax, double az, double bx, double bz, double px, double pz) => Math.Min(ax, bx) <= px && px <= Math.Max(ax, bx) && Math.Min(az, bz) <= pz && pz <= Math.Max(az, bz);

        // Whether edge i of ring r and edge j of ring s share any point: a crossing, a touch at an end, or a collinear overlap.
        private static bool Touch(double[] r, int i, double[] s, int j)
        {
            int ni = (i + 1) % (r.Length / 2), nj = (j + 1) % (s.Length / 2);
            double ax = r[2 * i], az = r[2 * i + 1], bx = r[2 * ni], bz = r[2 * ni + 1], cx = s[2 * j], cz = s[2 * j + 1], dx = s[2 * nj], dz = s[2 * nj + 1];
            double d1 = Orient(cx, cz, dx, dz, ax, az), d2 = Orient(cx, cz, dx, dz, bx, bz), d3 = Orient(ax, az, bx, bz, cx, cz), d4 = Orient(ax, az, bx, bz, dx, dz);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            return (d1 == 0 && Between(cx, cz, dx, dz, ax, az)) || (d2 == 0 && Between(cx, cz, dx, dz, bx, bz))
                || (d3 == 0 && Between(ax, az, bx, bz, cx, cz)) || (d4 == 0 && Between(ax, az, bx, bz, dx, dz));
        }

        // Whether edge i of ring r and edge j of ring s cross at a point inside both; touching does not count.
        private static bool Cross(double[] r, int i, double[] s, int j)
        {
            int ni = (i + 1) % (r.Length / 2), nj = (j + 1) % (s.Length / 2);
            double ax = r[2 * i], az = r[2 * i + 1], bx = r[2 * ni], bz = r[2 * ni + 1], cx = s[2 * j], cz = s[2 * j + 1], dx = s[2 * nj], dz = s[2 * nj + 1];
            double d1 = Orient(cx, cz, dx, dz, ax, az), d2 = Orient(cx, cz, dx, dz, bx, bz), d3 = Orient(ax, az, bx, bz, cx, cz), d4 = Orient(ax, az, bx, bz, dx, dz);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static bool Crossing(double[] r, double[] s)
        {
            for (int i = 0; i < r.Length / 2; i++)
                for (int j = 0; j < s.Length / 2; j++)
                    if (Cross(r, i, s, j)) return true;
            return false;
        }

        private static double Distance2(double px, double pz, double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
            double t = len > 0 ? ((px - ax) * dx + (pz - az) * dz) / len : 0;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double qx = ax + t * dx - px, qz = az + t * dz - pz;
            return qx * qx + qz * qz;
        }

        private static bool OnEdge(double[] r, double x, double z)
        {
            int n = r.Length / 2;
            for (int k = 0; k < n; k++)
            {
                int next = (k + 1) % n;
                if (Distance2(x, z, r[2 * k], r[2 * k + 1], r[2 * next], r[2 * next + 1]) <= Near * Near) return true;
            }
            return false;
        }

        // Even-odd: a ray towards +X crosses the ring's edges an odd number of times from inside.
        private static bool Odd(double[] r, double x, double z)
        {
            int n = r.Length / 2;
            bool inside = false;
            for (int k = 0, prev = n - 1; k < n; prev = k++)
            {
                double xi = r[2 * k], zi = r[2 * k + 1], xj = r[2 * prev], zj = r[2 * prev + 1];
                if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
            }
            return inside;
        }

        // In the ring or on its edge: an outline's edge belongs to the area.
        private static bool InRing(double[] r, double x, double z) => OnEdge(r, x, z) || Odd(r, x, z);

        // Inside and clear of the edge: a hole's edge still belongs to the area.
        private static bool WellInside(double[] r, double x, double z) => !OnEdge(r, x, z) && Odd(r, x, z);

        private static bool HoleWithin(double[] hole, double[] outline)
        {
            for (int k = 0; k < hole.Length; k += 2) if (!InRing(outline, hole[k], hole[k + 1])) return false;
            return !Crossing(hole, outline);
        }

        private static bool Overlap(double[] a, double[] b)
        {
            for (int k = 0; k < a.Length; k += 2) if (WellInside(b, a[k], a[k + 1])) return true;
            for (int k = 0; k < b.Length; k += 2) if (WellInside(a, b[k], b[k + 1])) return true;
            return Crossing(a, b);
        }
    }

    /// <summary>Whether a player stands in a trigger's place: its painted area when it has one, else its circle.</summary>
    public static class Membership
    {
        public const float CircleHeight = 2f;     // the circle rule as GameMaster has always had it: the floor above or below does not count
        public const float Edge = 0.01f;          // metres: a point this close to an outline or hole edge is on it

        /// <summary>A trigger with an area ignores its radius; one without keeps the old circle exactly.</summary>
        public static bool Inside(TriggerPlan t, V3 p)
        {
            if (t == null) return false;
            if (t.Area != null && t.Area.Parts.Count > 0) return t.Area.Contains(p);
            float dx = p.X - t.Pos.X, dz = p.Z - t.Pos.Z;
            return dx * dx + dz * dz <= t.Radius * t.Radius && Math.Abs(p.Y - t.Pos.Y) <= CircleHeight;
        }
    }
}
