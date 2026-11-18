using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MissionLab.Core;

/// <summary>
/// Painted trigger areas (ML-027): every point and every refusal in tests/fixtures/area_points.json, which the editor is
/// held to as well, then the canonical text, the signature, the old circle rule and a large area asked many times.
/// </summary>
static class AreaTests
{
    public static void Run(Action<string, bool, string> check)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "tests", "fixtures", "area_points.json"))) root = Path.GetDirectoryName(root);
        check("the area fixture is found", root != null, "");
        if (root == null) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "fixtures", "area_points.json")));
        JsonElement fixture = doc.RootElement;

        {   // Every point of every area is in or out as the fixture says, and every area passes the strict rules.
            foreach (JsonElement one in fixture.GetProperty("areas").EnumerateArray())
            {
                string name = one.GetProperty("name").GetString();
                var found = new List<string>();
                AreaPlan.Check(one.GetProperty("area"), "t", (code, path, text) => found.Add(code + " " + path));
                check(name + ": the area passes the strict rules", found.Count == 0, string.Join(" | ", found));
                AreaPlan area = AreaPlan.Read(one.GetProperty("area"));
                check(name + ": the area reads", area != null, "");
                if (area == null) continue;
                check(name + ": the fixture is already written canonically", area.Signature() == JsonSerializer.Serialize(one.GetProperty("area")), area.Signature());
                foreach (JsonElement point in one.GetProperty("points").EnumerateArray())
                {
                    JsonElement p = point.GetProperty("p");
                    var at = new V3((float)p[0].GetDouble(), (float)p[1].GetDouble(), (float)p[2].GetDouble());
                    bool want = point.GetProperty("in").GetBoolean();
                    check(name + ": " + point.GetProperty("why").GetString() + " is " + (want ? "in" : "out"), area.Contains(at) == want, p.GetRawText());
                }
            }
        }
        {   // Every broken area is refused with exactly the listed codes and places, in order.
            foreach (JsonElement one in fixture.GetProperty("invalid").EnumerateArray())
            {
                var found = new List<string>();
                AreaPlan.Check(one.GetProperty("area"), "t", (code, path, text) => found.Add(code + " " + path));
                string want = string.Join(" | ", one.GetProperty("expect").EnumerateArray().Select(x => x.GetString()));
                check(one.GetProperty("name").GetString() + " is refused with the listed places", string.Join(" | ", found) == want, "got  " + string.Join(" | ", found) + "\n        want " + want);
            }
        }
        {   // A ring of 257 points is refused, and so is an area of more than 2048 points in rings that are each fine.
            var found = new List<string>();
            using (var big = JsonDocument.Parse(Generated(1, 257))) AreaPlan.Check(big.RootElement, "t", (code, path, text) => found.Add(code + " " + path));
            check("a ring of 257 points is refused at the ring", string.Join(" | ", found) == "bad-area t.area.parts[0].outline", string.Join(" | ", found));
            found.Clear();
            using (var many = JsonDocument.Parse(Generated(9, 256))) AreaPlan.Check(many.RootElement, "t", (code, path, text) => found.Add(code + " " + path));
            check("nine rings of 256 points are refused as an area of too many points", string.Join(" | ", found) == "bad-area t.area", string.Join(" | ", found));
        }
        {   // Writing, reading and writing again gives the same text, with a hole and awkward decimals.
            var area = new AreaPlan();
            var part = new AreaPart { Bottom = -1.234f, Top = 2.006f };
            part.Outline.AddRange(new[] { new P2(0.333f, -0.001f), new P2(10.126f, 0), new P2(10, 10.5f), new P2(0, 10) });
            part.Holes.Add(new List<P2> { new P2(2, 2), new P2(4, 2), new P2(4, 4), new P2(2, 4) });
            area.Parts.Add(part);
            string once = area.Signature();
            using var back = JsonDocument.Parse(once);
            string twice = AreaPlan.Read(back.RootElement).Signature();
            check("an area written, read and written again is the same text", once == twice, once + " then " + twice);
            check("it is written with two decimals and no minus zero", once == "{\"parts\":[{\"floor\":[-1.23,2.01],\"outline\":[[0.33,0],[10.13,0],[10,10.5],[0,10]],\"holes\":[[[2,2],[4,2],[4,4],[2,4]]]}]}", once);
        }
        {   // The signature changes when a corner moves, and comes back when it moves back.
            var area = Square(0, 0, 10, 0, 3);
            string before = area.Signature();
            area.Parts[0].Outline[2] = new P2(10, 11);
            check("moving a corner changes the signature", area.Signature() != before, "");
            area.Parts[0].Outline[2] = new P2(10, 10);
            check("moving it back gives the same signature", area.Signature() == before, "");
        }
        {   // The lenient reader turns an upside-down floor round, skips odd points and drops what cannot be used.
            using var loose = JsonDocument.Parse("{\"parts\":[{\"floor\":[3,0],\"outline\":[[0,0],\"x\",[10,0],[10,10],[0,10]],\"holes\":[[[1,1],[2,1]]]},{\"floor\":[0,3],\"outline\":[[0,0],[1,1]]},{\"outline\":[[0,0],[1,0],[1,1]]}]}");
            AreaPlan area = AreaPlan.Read(loose.RootElement);
            check("a loose area keeps the one usable part", area != null && area.Parts.Count == 1, area == null ? "null" : area.Signature());
            check("its floor is turned round and the odd point and the two-point hole are gone", area != null && area.Signature() == "{\"parts\":[{\"floor\":[0,3],\"outline\":[[0,0],[10,0],[10,10],[0,10]]}]}", area == null ? "null" : area.Signature());
            using var none = JsonDocument.Parse("{\"parts\":[{\"floor\":[0,3],\"outline\":[[0,0],[1,1]]}]}");
            check("an area with nothing usable reads as none, so the circle is used", AreaPlan.Read(none.RootElement) == null, "");
        }
        {   // A trigger without an area keeps the circle exactly as GameMaster.PlayerInside had it.
            var t = new TriggerPlan { Pos = new V3(100, 10, 50), Radius = 5 };
            var points = new[] { new V3(100, 10, 50), new V3(105, 10, 50), new V3(105.01f, 10, 50), new V3(103, 12, 54), new V3(103, 12.01f, 54), new V3(100, 7.99f, 50), new V3(96, 8, 47) };
            string got = "", old = "";
            foreach (V3 p in points)
            {
                got += Membership.Inside(t, p) ? "1" : "0";
                float dx = p.X - t.Pos.X, dz = p.Z - t.Pos.Z;
                old += dx * dx + dz * dz <= t.Radius * t.Radius && Math.Abs(p.Y - t.Pos.Y) <= 2f ? "1" : "0";
            }
            check("the circle answers as before for every point", got == old && got == "1101001", got + " against " + old);
            t.Area = Square(200, 200, 10, 0, 3);
            check("with an area the radius no longer counts", !Membership.Inside(t, new V3(100, 10, 50)) && Membership.Inside(t, new V3(205, 1, 205)), "");
            t.Area = new AreaPlan();
            check("an area with no parts falls back to the circle", Membership.Inside(t, new V3(100, 10, 50)), "");
        }
        {   // A concave area of 200 corners in each of 8 parts answers 10 000 questions, all of them right.
            var area = new AreaPlan();
            for (int part = 0; part < 8; part++) area.Parts.Add(Star(part));
            area.Prepare();
            int wrong = 0;
            for (int i = 0; i < 10000; i++)
            {
                int part = i % 8, spike = (i / 8) % 100;
                bool tip = (i / 800) % 2 == 0;              // on the line to a spike's tip (in) or to the notch between two spikes (out)
                double angle = (2 * spike + (tip ? 0 : 1)) * Math.PI / 100;
                var p = new V3((float)(part * 50 + 15 * Math.Cos(angle)), part * 3 + 1, (float)(15 * Math.Sin(angle)));
                if (area.Contains(p) != tip) wrong++;
            }
            check("10 000 points in and out of a 200-corner area are all answered right", wrong == 0, wrong + " wrong");
            check("a point of one part at another part's height is out", !area.Contains(new V3(0, 4, 0)) && area.Contains(new V3(0, 1, 0)), "");
        }
    }

    private static AreaPlan Square(float x, float z, float size, float bottom, float top)
    {
        var area = new AreaPlan();
        var part = new AreaPart { Bottom = bottom, Top = top };
        part.Outline.AddRange(new[] { new P2(x, z), new P2(x + size, z), new P2(x + size, z + size), new P2(x, z + size) });
        area.Parts.Add(part);
        return area;
    }

    // A star of 100 spikes round (part * 50, 0): tips 20 m out, notches 10 m out, on its own band of heights.
    private static AreaPart Star(int part)
    {
        var p = new AreaPart { Bottom = part * 3, Top = part * 3 + 2.5f };
        for (int k = 0; k < 200; k++)
        {
            double r = k % 2 == 0 ? 20 : 10, angle = k * Math.PI / 100;
            p.Outline.Add(new P2((float)(part * 50 + r * Math.Cos(angle)), (float)(r * Math.Sin(angle))));
        }
        return p;
    }

    // An area of `parts` parts, each a ring of `points` points on a circle, as file text.
    public static string Generated(int parts, int points)
    {
        var sb = new StringBuilder("{\"parts\":[");
        for (int j = 0; j < parts; j++)
        {
            sb.Append(j > 0 ? ",{\"floor\":[0,3],\"outline\":[" : "{\"floor\":[0,3],\"outline\":[");
            for (int k = 0; k < points; k++)
            {
                double angle = 2 * Math.PI * k / points;
                sb.Append(k > 0 ? ",[" : "[").Append((j * 100 + Math.Round(30 * Math.Cos(angle), 2)).ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append(',').Append(Math.Round(30 * Math.Sin(angle), 2).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(']');
            }
            sb.Append("]}");
        }
        return sb.Append("]}").ToString();
    }
}
