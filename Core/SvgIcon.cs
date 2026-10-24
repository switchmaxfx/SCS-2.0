using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SCS.Core;

/// <summary>
/// A deliberately small SVG reader for the wiki's stratagem icons: filled &lt;path&gt;, &lt;circle&gt;,
/// &lt;ellipse&gt;, &lt;rect&gt;, &lt;polygon&gt;/&lt;polyline&gt;, nested &lt;g&gt;, transforms (matrix/translate/scale/rotate),
/// fill colours from attributes, inline style or simple CSS classes, and fill-rule. That covers every icon on
/// the wiki. It produces plain shape data (no WPF types), which the UI turns into a drawing.
/// Strokes, gradients, text and filters are ignored. Input is treated as untrusted (no DTDs, size-capped).
/// </summary>
public static class SvgIcon
{
    public sealed record Shape(string PathData, double[] Matrix, uint Argb, bool EvenOdd);
    public sealed record Icon(double X, double Y, double Width, double Height, IReadOnlyList<Shape> Shapes);

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    public static Icon? Parse(string text)
    {
        if (text.Length > 1_000_000) return null;
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,   // some icons carry a DOCTYPE; it is skipped, never fetched
                XmlResolver = null,
                MaxCharactersInDocument = 2_000_000,
            });
            doc = XDocument.Load(reader);
        }
        catch { return null; }

        var root = doc.Root;
        if (root == null || root.Name.LocalName != "svg") return null;

        // viewBox, falling back to width/height
        double vx = 0, vy = 0, vw = 0, vh = 0;
        var vb = Nums(root.Attribute("viewBox")?.Value ?? "");
        if (vb.Count == 4) { vx = vb[0]; vy = vb[1]; vw = vb[2]; vh = vb[3]; }
        else { vw = Len(root.Attribute("width")?.Value); vh = Len(root.Attribute("height")?.Value); }
        if (vw <= 0 || vh <= 0) return null;

        var classes = ParseCss(root);
        var shapes = new List<Shape>();
        Walk(root, Identity(), new Paint(0xFF000000, false, 1.0), classes, shapes);
        return shapes.Count == 0 ? null : new Icon(vx, vy, vw, vh, shapes);
    }

    // ------------------------------------------------------------------ tree walk

    private readonly record struct Paint(uint? Fill, bool EvenOdd, double Opacity);

    private static void Walk(XElement el, double[] parentM, Paint parent, Dictionary<string, Dictionary<string, string>> css, List<Shape> output)
    {
        var name = el.Name.LocalName;
        if (name is "defs" or "clipPath" or "mask" or "style" or "title" or "desc" or "metadata" or "symbol" or "pattern"
            or "linearGradient" or "radialGradient" or "filter" or "namedview")
            return;
        if (el.Name.Namespace != Svg && el.Name.Namespace != XNamespace.None) return; // editor-specific elements
        if (Prop(el, "display", css) == "none" || Prop(el, "visibility", css) == "hidden") return;

        var m = Multiply(parentM, ParseTransform(el.Attribute("transform")?.Value));
        var paint = parent;
        var fill = Prop(el, "fill", css);
        if (fill != null) paint = paint with { Fill = ParseColor(fill, paint.Fill) };
        var rule = Prop(el, "fill-rule", css);
        if (rule != null) paint = paint with { EvenOdd = rule.Trim() == "evenodd" };
        var op = Prop(el, "opacity", css);
        if (op != null && double.TryParse(op, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) paint = paint with { Opacity = paint.Opacity * Math.Clamp(o, 0, 1) };
        var fop = Prop(el, "fill-opacity", css);
        double fillOpacity = fop != null && double.TryParse(fop, NumberStyles.Float, CultureInfo.InvariantCulture, out var fo) ? Math.Clamp(fo, 0, 1) : 1;

        string? d = name switch
        {
            "path" => el.Attribute("d")?.Value,
            "rect" => RectPath(el),
            "circle" => EllipsePath(A(el, "cx"), A(el, "cy"), A(el, "r"), A(el, "r")),
            "ellipse" => EllipsePath(A(el, "cx"), A(el, "cy"), A(el, "rx"), A(el, "ry")),
            "polygon" => PolyPath(el.Attribute("points")?.Value, close: true),
            "polyline" => PolyPath(el.Attribute("points")?.Value, close: false),
            _ => null
        };

        if (d != null)
        {
            if (paint.Fill is uint argb && NormalizePath(d) is { Length: > 0 } nd)
            {
                double alpha = ((argb >> 24) / 255.0) * paint.Opacity * fillOpacity;
                uint a = (uint)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
                if (a > 0) output.Add(new Shape(nd, m, (a << 24) | (argb & 0xFFFFFF), paint.EvenOdd));
            }
            return;
        }

        if (name is "svg" or "g" or "a" or "switch")
            foreach (var child in el.Elements()) Walk(child, m, paint, css, output);
    }

    private static double A(XElement el, string attr) => Len(el.Attribute(attr)?.Value);

    /// <summary>Attribute, inline style, or class rule (style wins, as in SVG).</summary>
    private static string? Prop(XElement el, string prop, Dictionary<string, Dictionary<string, string>> css)
    {
        var style = el.Attribute("style")?.Value;
        if (style != null)
            foreach (var part in style.Split(';'))
            {
                var i = part.IndexOf(':');
                if (i > 0 && part[..i].Trim() == prop) return part[(i + 1)..].Trim();
            }
        var cls = el.Attribute("class")?.Value;
        if (cls != null)
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (css.TryGetValue(c, out var rules) && rules.TryGetValue(prop, out var v)) return v;
        return el.Attribute(prop)?.Value;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseCss(XElement root)
    {
        var map = new Dictionary<string, Dictionary<string, string>>();
        foreach (var style in root.Descendants().Where(e => e.Name.LocalName == "style"))
            foreach (Match m in Regex.Matches(style.Value, @"((?:\.[\w-]+\s*,?\s*)+)\{([^}]*)\}"))
            {
                var rules = m.Groups[2].Value.Split(';')
                    .Select(r => r.Split(':', 2)).Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
                foreach (Match c in Regex.Matches(m.Groups[1].Value, @"\.([\w-]+)"))
                {
                    if (!map.TryGetValue(c.Groups[1].Value, out var existing)) map[c.Groups[1].Value] = existing = new();
                    foreach (var (k, v) in rules) existing[k] = v;
                }
            }
        return map;
    }

    // ------------------------------------------------------------------ colours

    private static uint? ParseColor(string s, uint? current)
    {
        s = s.Trim().ToLowerInvariant();
        if (s is "none" or "transparent") return null;
        if (s is "currentcolor" or "inherit") return current;
        if (s.StartsWith("url(")) return current ?? 0xFF808080; // gradients: approximate
        if (s.StartsWith('#'))
        {
            var h = s[1..];
            if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
            if (h.Length == 6 && uint.TryParse(h, NumberStyles.HexNumber, null, out var rgb)) return 0xFF000000 | rgb;
            return current;
        }
        var m = Regex.Match(s, @"^rgba?\(\s*([\d.]+%?)\s*[, ]\s*([\d.]+%?)\s*[, ]\s*([\d.]+%?)");
        if (m.Success)
        {
            uint C(string v) => (uint)Math.Clamp(v.EndsWith('%') ? double.Parse(v[..^1], CultureInfo.InvariantCulture) * 2.55
                                                                 : double.Parse(v, CultureInfo.InvariantCulture), 0, 255);
            return 0xFF000000 | (C(m.Groups[1].Value) << 16) | (C(m.Groups[2].Value) << 8) | C(m.Groups[3].Value);
        }
        return s switch
        {
            "black" => 0xFF000000, "white" => 0xFFFFFFFF, "red" => 0xFFFF0000, "green" => 0xFF008000,
            "blue" => 0xFF0000FF, "yellow" => 0xFFFFFF00, "gray" or "grey" => 0xFF808080, "orange" => 0xFFFFA500,
            _ => current
        };
    }

    // ------------------------------------------------------------------ shapes

    private static string? RectPath(XElement el)
    {
        double x = A(el, "x"), y = A(el, "y"), w = A(el, "width"), h = A(el, "height");
        if (w <= 0 || h <= 0) return null;
        return F($"M{x} {y}H{x + w}V{y + h}H{x}Z");
    }

    private static string? EllipsePath(double cx, double cy, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) return null;
        return F($"M{cx - rx} {cy}A{rx} {ry} 0 1 0 {cx + rx} {cy}A{rx} {ry} 0 1 0 {cx - rx} {cy}Z");
    }

    private static string? PolyPath(string? points, bool close)
    {
        var n = Nums(points ?? "");
        if (n.Count < 4) return null;
        var sb = new StringBuilder(F($"M{n[0]} {n[1]}"));
        for (int i = 2; i + 1 < n.Count; i += 2) sb.Append(F($"L{n[i]} {n[i + 1]}"));
        if (close) sb.Append('Z');
        return sb.ToString();
    }

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ path data

    /// <summary>
    /// Re-tokenizes SVG path data per the SVG grammar and re-emits it fully space-separated.
    /// SVG allows "1.5.5" (two numbers), "29-8", and packed arc flags ("a1 1 0 001 1"); WPF's
    /// parser rejects some of those, so this makes the data safe for Geometry.Parse.
    /// </summary>
    public static string NormalizePath(string d)
    {
        var sb = new StringBuilder(d.Length + 16);
        int i = 0;
        char cmd = '\0';
        int argIndex = 0;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if ("MmLlHhVvCcSsQqTtAaZz".IndexOf(c) >= 0)
            {
                cmd = c; argIndex = 0; i++;
                sb.Append(c).Append(' ');
                continue;
            }
            if (cmd == '\0') return ""; // data must start with a command

            // Arc flags (4th and 5th arc args) are single characters "0"/"1" with no separator required.
            bool isArc = cmd is 'A' or 'a';
            if (isArc && (argIndex % 7 is 3 or 4) && (c == '0' || c == '1'))
            {
                sb.Append(c).Append(' ');
                i++; argIndex++;
                continue;
            }

            int start = i;
            if (c is '+' or '-') i++;
            bool dot = false, digits = false;
            while (i < d.Length)
            {
                char ch = d[i];
                if (char.IsDigit(ch)) { digits = true; i++; }
                else if (ch == '.' && !dot) { dot = true; i++; }
                else break;
            }
            if (i < d.Length && (d[i] == 'e' || d[i] == 'E') && digits)
            {
                int save = i; i++;
                if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
                if (i < d.Length && char.IsDigit(d[i])) { while (i < d.Length && char.IsDigit(d[i])) i++; }
                else i = save;
            }
            if (!digits) return ""; // malformed
            sb.Append(d, start, i - start).Append(' ');
            argIndex++;
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ transforms (a b c d e f)

    private static double[] Identity() => new double[] { 1, 0, 0, 1, 0, 0 };

    /// <summary>Returns parent × child (child applied first), SVG matrix order [a b c d e f].</summary>
    private static double[] Multiply(double[] p, double[] c) => new[]
    {
        p[0] * c[0] + p[2] * c[1],
        p[1] * c[0] + p[3] * c[1],
        p[0] * c[2] + p[2] * c[3],
        p[1] * c[2] + p[3] * c[3],
        p[0] * c[4] + p[2] * c[5] + p[4],
        p[1] * c[4] + p[3] * c[5] + p[5],
    };

    public static double[] ParseTransform(string? t)
    {
        var m = Identity();
        if (string.IsNullOrWhiteSpace(t)) return m;
        foreach (Match f in Regex.Matches(t, @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)"))
        {
            var n = Nums(f.Groups[2].Value);
            double[] x = f.Groups[1].Value switch
            {
                "matrix" when n.Count == 6 => n.ToArray(),
                "translate" when n.Count >= 1 => new[] { 1, 0, 0, 1, n[0], n.Count > 1 ? n[1] : 0 },
                "scale" when n.Count >= 1 => new[] { n[0], 0, 0, n.Count > 1 ? n[1] : n[0], 0, 0 },
                "rotate" when n.Count >= 1 => Rotate(n[0], n.Count >= 3 ? n[1] : 0, n.Count >= 3 ? n[2] : 0),
                "skewX" when n.Count == 1 => new[] { 1, 0, Math.Tan(n[0] * Math.PI / 180), 1, 0, 0 },
                "skewY" when n.Count == 1 => new[] { 1, Math.Tan(n[0] * Math.PI / 180), 0, 1, 0, 0 },
                _ => Identity()
            };
            m = Multiply(m, x);
        }
        return m;
    }

    private static double[] Rotate(double deg, double cx, double cy)
    {
        double r = deg * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        var rot = new[] { cos, sin, -sin, cos, 0, 0 };
        if (cx == 0 && cy == 0) return rot;
        return Multiply(Multiply(new double[] { 1, 0, 0, 1, cx, cy }, rot), new double[] { 1, 0, 0, 1, -cx, -cy });
    }

    // ------------------------------------------------------------------ numbers

    private static readonly Regex NumRx = new(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    private static List<double> Nums(string s) =>
        NumRx.Matches(s).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();

    private static double Len(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var m = NumRx.Match(s);
        return m.Success ? double.Parse(m.Value, CultureInfo.InvariantCulture) : 0;
    }
}
