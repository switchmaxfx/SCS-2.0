using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SCS.Core;

namespace SCS.UI;

/// <summary>Draws a stratagem code ("UDRLU") as a row of arrow glyphs. Pure OnRender: no child elements, no allocations per frame.</summary>
public sealed class ArrowStrip : FrameworkElement
{
    private static readonly Geometry Arrow = Geometry.Parse("M6,0 L12,6.5 L8,6.5 L8,14 L4,14 L4,6.5 L0,6.5 Z");

    static ArrowStrip() => Arrow.Freeze();

    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(string), typeof(ArrowStrip),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ArrowStrip),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(ArrowStrip),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Code { get => (string)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double GlyphSize { get => (double)GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }

    private double Step => GlyphSize + GlyphSize * 0.45;

    protected override Size MeasureOverride(Size availableSize)
    {
        var n = Code?.Length ?? 0;
        return new Size(n == 0 ? 0 : n * Step - GlyphSize * 0.45, GlyphSize * 14 / 12);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var code = Code;
        if (string.IsNullOrEmpty(code)) return;
        double scale = GlyphSize / 12.0;
        double h = 14 * scale;
        for (int i = 0; i < code.Length; i++)
        {
            double angle = code[i] switch { 'R' => 90, 'D' => 180, 'L' => 270, _ => 0 };
            double cx = i * Step + GlyphSize / 2, cy = h / 2;
            dc.PushTransform(new TranslateTransform(cx, cy));
            dc.PushTransform(new RotateTransform(angle));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-6, -7));
            dc.DrawGeometry(Fill, null, Arrow);
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
}

/// <summary>
/// Click to listen, then press a key or mouse button. Esc cancels. Right-click (when idle) clears.
/// Stores the same key-name strings as v1.x so profiles stay compatible.
/// </summary>
public sealed class HotkeyBox : Border
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Refresh()));

    public static readonly DependencyProperty IsConflictProperty = DependencyProperty.Register(nameof(IsConflict), typeof(bool), typeof(HotkeyBox),
        new PropertyMetadata(false, (d, _) => ((HotkeyBox)d).Refresh()));

    public string Hotkey { get => (string)GetValue(HotkeyProperty); set => SetValue(HotkeyProperty, value); }
    public bool IsConflict { get => (bool)GetValue(IsConflictProperty); set => SetValue(IsConflictProperty, value); }
    public bool AllowModifiers { get; set; }
    public string Placeholder { get; set; } = "BIND KEY";

    private readonly TextBlock _text;
    private bool _capturing;

    public HotkeyBox()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Height = 32;
        _text = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
        };
        Child = _text;
        Loaded += (_, _) => Refresh();
        MouseEnter += (_, _) => Refresh();
        MouseLeave += (_, _) => Refresh();
        ToolTip = "Click, then press a key or mouse button. Esc cancels, right-click clears.";
    }

    private Brush R(string key) => (Brush)Application.Current.FindResource(key);

    private void Refresh()
    {
        if (_capturing)
        {
            _text.Text = "PRESS A KEY";
            _text.Foreground = R("AccentBrush");
            BorderBrush = R("AccentBrush");
            Background = R("AccentSoftBrush");
            Effect = (Effect)Application.Current.FindResource("AccentGlowSoft");
            return;
        }
        Effect = null;
        bool empty = string.IsNullOrWhiteSpace(Hotkey);
        _text.Text = empty ? Placeholder : KeyNames.Display(Hotkey);
        Background = R("InputBrush");
        if (IsConflict && !empty)
        {
            _text.Foreground = R("DangerBrush");
            BorderBrush = R("DangerBrush");
            Effect = (Effect)Application.Current.FindResource("DangerGlow");
        }
        else
        {
            _text.Foreground = empty ? R("TextFaintBrush") : R("TextBrush");
            BorderBrush = IsMouseOver ? R("AccentDimBrush") : R("BorderBrush");
        }
    }

    private void SetCapturing(bool on)
    {
        _capturing = on;
        if (on) { Focus(); Keyboard.Focus(this); }
        Refresh();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_capturing)
        {
            string? name = e.ChangedButton switch
            {
                MouseButton.Left => "mouse_left",
                MouseButton.Right => "mouse_right",
                MouseButton.Middle => "mouse_middle",
                MouseButton.XButton1 => "mouse_xbutton1",
                MouseButton.XButton2 => "mouse_xbutton2",
                _ => null
            };
            if (name != null) Commit(name);
            return;
        }
        if (e.ChangedButton == MouseButton.Left) SetCapturing(true);
        else if (e.ChangedButton == MouseButton.Right) Hotkey = "";
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true; // keep the card's context menu from opening on top of a clear/bind
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed || key == Key.DeadCharProcessed) return;
        if (key == Key.Escape) { SetCapturing(false); return; }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        bool isModifier = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
        if (isModifier && !AllowModifiers) return;

        var name = KeyNames.GetName(KeyNames.Normalize(vk));
        if (name != null) Commit(name);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (_capturing) SetCapturing(false);
    }

    private void Commit(string name)
    {
        Hotkey = name;
        SetCapturing(false);
        // Move focus away so the next key press doesn't land here.
        var scope = FocusManager.GetFocusScope(this);
        FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
    }
}

/// <summary>Formats a millisecond value for compact display.</summary>
public static class Fmt
{
    public static string Ms(double v) => v.ToString(v < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " ms";
}
