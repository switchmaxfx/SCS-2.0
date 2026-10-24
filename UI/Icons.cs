using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SCS.Core;

namespace SCS.UI;

/// <summary>
/// Stratagem icons. Looks in this order:
///   1. an icon downloaded from the wiki (stratagem_icons\ next to the exe), for stratagems added after this build
///   2. the icon set built into the exe (Assets\Icons, rendered from the wiki's SVGs, keyed by name)
/// Decoded images are frozen and cached, so scrolling the picker never re-decodes.
/// </summary>
public static class IconStore
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, bool> BundledExists = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(Stratagem? s)
    {
        if (s == null) return null;
        if (Cache.TryGetValue(s.Name, out var cached)) return cached;
        var img = Load(s);
        Cache[s.Name] = img;
        return img;
    }

    /// <summary>True if the exe already carries an icon for this wiki title (so there's nothing to download).</summary>
    public static bool HasBundled(string title) => BundledKey(title) != null;

    /// <summary>Forget decoded icons (after a wiki update added or changed some).</summary>
    public static void Invalidate() => Cache.Clear();

    private static ImageSource? Load(Stratagem s)
    {
        if (s.IconFile != null)
        {
            var path = WikiStratagems.IconPath(s.IconFile);
            if (File.Exists(path) && path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (SvgIcon.Parse(File.ReadAllText(path)) is { } svg && Rasterize(svg) is { } img) return img;
                }
                catch { /* unreadable: fall through to the built-in icon */ }
            }
            else if (File.Exists(path))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;   // read now, don't keep the file locked
                    bmp.DecodePixelWidth = WikiStratagems.IconSize;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch { /* corrupt download: fall through to the built-in icon */ }
            }
        }

        foreach (var name in new[] { s.Alias, s.Name })
        {
            if (name == null || BundledKey(name) is not { } key) continue;
            try
            {
                var bmp = new BitmapImage(new Uri($"pack://application:,,,/Assets/Icons/{key}.png"));
                bmp.Freeze();
                return bmp;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Draws a parsed SVG icon into a small frozen bitmap once, so lists never re-render vector paths.
    /// </summary>
    private static ImageSource? Rasterize(SvgIcon.Icon svg)
    {
        var viewBox = new Rect(svg.X, svg.Y, svg.Width, svg.Height);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(viewBox) };
        // Transparent backdrop the size of the viewBox, so the image keeps the icon's real proportions.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(viewBox)));
        foreach (var shape in svg.Shapes)
        {
            try
            {
                var geo = Geometry.Parse((shape.EvenOdd ? "F0 " : "F1 ") + shape.PathData);
                if (geo.IsFrozen) geo = geo.Clone();
                var m = shape.Matrix;
                geo.Transform = new MatrixTransform(new Matrix(m[0], m[1], m[2], m[3], m[4], m[5]));
                var a = shape.Argb;
                var brush = new SolidColorBrush(Color.FromArgb((byte)(a >> 24), (byte)(a >> 16), (byte)(a >> 8), (byte)a));
                group.Children.Add(new GeometryDrawing(brush, null, geo));
            }
            catch { /* skip a shape WPF can't parse rather than lose the whole icon */ }
        }
        if (group.Children.Count <= 1) return null;
        group.Freeze();

        const int px = WikiStratagems.IconSize;
        double scale = Math.Min(px / svg.Width, px / svg.Height);
        double w = svg.Width * scale, h = svg.Height * scale;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(new DrawingImage(group), new Rect((px - w) / 2, (px - h) / 2, w, h));
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static string? BundledKey(string name)
    {
        var key = Stratagems.Norm(name);
        if (key.Length == 0) return null;
        if (!BundledExists.TryGetValue(key, out var exists))
        {
            try { exists = Application.GetResourceStream(new Uri($"/Assets/Icons/{key}.png", UriKind.Relative)) != null; }
            catch (IOException) { exists = false; }
            BundledExists[key] = exists;
        }
        return exists ? key : null;
    }
}

/// <summary>XAML: Source="{Binding Converter={StaticResource StratagemIcon}}" on a Stratagem.</summary>
public sealed class StratagemIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        IconStore.Get(value as Stratagem);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
