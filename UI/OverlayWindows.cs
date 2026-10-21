using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SCS.Core;

namespace SCS.UI;

/// <summary>Base for the small always-on-top windows: never steals focus from the game, no taskbar entry.</summary>
public abstract class FloatingWindow : Window
{
    protected FloatingWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Foreground = Res("TextBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
    }

    protected static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    protected static Effect Fx(string key) => (Effect)Application.Current.FindResource(key);
}

/// <summary>In-game hotkey list. Drag by the header bar.</summary>
public sealed class OverlayWindow : FloatingWindow
{
    private readonly AppState _state;
    private readonly StackPanel _rows = new();
    private readonly Border _headerLine;
    private readonly Ellipse _dot;
    private readonly TextBlock _stateText;
    private readonly Dictionary<string, (Border row, TextBlock name, ArrowStrip arrows)> _byName = new();
    private readonly System.Windows.Threading.DispatcherTimer _flashTimer;
    private string? _flashed;

    public OverlayWindow(AppState state)
    {
        _state = state;
        Left = state.OverlayLeft ?? 40;
        Top = state.OverlayTop ?? 200;

        _dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _stateText = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center
        };
        var header = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(12, 8, 12, 6),
            Cursor = Cursors.SizeAll,
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "S.C.S", FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                        FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Res("TextDimBrush"), Margin = new Thickness(0, 0, 16, 0)
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _dot, _stateText } }
                }
            }
        };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        _headerLine = new Border { Height = 2, Margin = new Thickness(12, 0, 12, 4), CornerRadius = new CornerRadius(1), Background = Res("AccentLineBrush") };
        _rows.Margin = new Thickness(8, 2, 8, 8);

        Content = new Border
        {
            Margin = new Thickness(12),
            MinWidth = 220,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0A, 0x0C, 0x10)),
            BorderBrush = Res("BorderHiBrush"),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { header, _headerLine, _rows } }
        };

        _flashTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); Unflash(); };
        SetArmed(false);
        LocationChanged += (_, _) => { _state.OverlayLeft = Left; _state.OverlayTop = Top; };
    }

    public void SetRows(IReadOnlyList<OverlayRow> rows)
    {
        _rows.Children.Clear();
        _byName.Clear();
        if (rows.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = "No hotkeys bound", Foreground = Res("TextFaintBrush"), FontSize = 11.5, Margin = new Thickness(6, 4, 6, 4) });
            return;
        }
        foreach (var r in rows)
        {
            var key = new Border
            {
                MinWidth = 54, Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4),
                BorderBrush = Res("BorderHiBrush"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = r.Key, HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold, Foreground = Res("TextDimBrush")
                }
            };
            var name = new TextBlock { Text = r.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("TextBrush"), VerticalAlignment = VerticalAlignment.Center };
            var arrows = new ArrowStrip { Code = r.Code, GlyphSize = 8, Fill = Res("AccentDimBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(name, 1);
            Grid.SetColumn(arrows, 2);
            grid.Children.Add(key);
            grid.Children.Add(name);
            grid.Children.Add(arrows);
            var row = new Border { Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(5), Child = grid };
            _rows.Children.Add(row);
            _byName.TryAdd(r.Name, (row, name, arrows));
        }
    }

    public void SetOpacity(double opacity) => Opacity = Math.Clamp(opacity, 0.2, 1.0);

    public void SetArmed(bool armed)
    {
        _dot.Fill = armed ? Res("AccentBrush") : new SolidColorBrush(Color.FromRgb(0x5A, 0x1A, 0x20));
        _dot.Effect = armed ? Fx("AccentGlow") : null;
        _stateText.Text = armed ? "ARMED" : "STANDBY";
        _stateText.Foreground = armed ? Res("AccentBrush") : Res("TextFaintBrush");
        _headerLine.Opacity = armed ? 1 : 0.15;
    }

    public void Flash(string name)
    {
        Unflash();
        if (!_byName.TryGetValue(name, out var r)) return;
        _flashed = name;
        r.row.Background = Res("AccentSoftBrush");
        r.name.Foreground = Res("AccentBrush");
        r.arrows.Fill = Res("AccentBrush");
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void Unflash()
    {
        if (_flashed == null || !_byName.TryGetValue(_flashed, out var r)) return;
        r.row.Background = Brushes.Transparent;
        r.name.Foreground = Res("TextBrush");
        r.arrows.Fill = Res("AccentDimBrush");
        _flashed = null;
    }

    public void SaveAndClose()
    {
        _state.OverlayLeft = Left;
        _state.OverlayTop = Top;
        Close();
    }
}

/// <summary>Tiny draggable armed/standby light.</summary>
public sealed class LedWindow : FloatingWindow
{
    private readonly AppState _state;
    private readonly Ellipse _core;
    private readonly Ellipse _halo;

    public LedWindow(AppState state)
    {
        _state = state;
        Left = state.LedLeft ?? 20;
        Top = state.LedTop ?? 20;
        _halo = new Ellipse { Width = 18, Height = 18 };
        _core = new Ellipse { Width = 12, Height = 12, Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), StrokeThickness = 1 };
        var g = new Grid { Margin = new Thickness(12), Width = 18, Height = 18, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "S.C.S status (drag to move)" };
        g.Children.Add(_halo);
        g.Children.Add(_core);
        g.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Content = g;
        SetArmed(false);
        LocationChanged += (_, _) => { _state.LedLeft = Left; _state.LedTop = Top; };
    }

    public void SetArmed(bool armed)
    {
        _core.Fill = armed ? Res("AccentBrush") : new SolidColorBrush(Color.FromRgb(0x5A, 0x1A, 0x20));
        _halo.Fill = armed ? new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xE1, 0x3A)) : Brushes.Transparent;
        _halo.Effect = armed ? Fx("AccentGlow") : null;
    }

    public void SaveAndClose()
    {
        _state.LedLeft = Left;
        _state.LedTop = Top;
        Close();
    }
}
