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
            if (File.Exists(path))
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
