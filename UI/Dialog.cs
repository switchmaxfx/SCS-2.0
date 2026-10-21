using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SCS.UI;

/// <summary>Themed replacement for MessageBox / input prompts.</summary>
public sealed class Dialog : Window
{
    private TextBox? _input;
    private string? _result;

    private Dialog(Window? owner, string title, string message, bool danger, string? inputDefault,
        params (string Text, string? Result, bool Primary)[] buttons)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        Topmost = owner?.Topmost ?? false;
        Foreground = (Brush)FindResource("TextBrush");
        FontFamily = (FontFamily)FindResource("UiFont");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };

        Brush Res(string k) => (Brush)FindResource(k);

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20), MinWidth = 340, MaxWidth = 520 };
        stack.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = danger ? Res("DangerBrush") : Res("AccentBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = Res("TextBrush")
        });
        if (inputDefault != null)
        {
            _input = new TextBox { Text = inputDefault, Margin = new Thickness(0, 14, 0, 0) };
            stack.Children.Add(_input);
            Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        foreach (var (text, result, primary) in buttons)
        {
            var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), MinWidth = 90 };
            if (primary) b.Style = (Style)FindResource(danger ? "DangerButton" : "PrimaryButton");
            if (primary) b.IsDefault = true;
            b.Click += (_, _) => { _result = result; DialogResult = result != null; };
            row.Children.Add(b);
        }
        stack.Children.Add(row);

        var border = new Border
        {
            Background = Res("PanelBrush"),
            BorderBrush = danger ? Res("DangerBrush") : Res("BorderHiBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(20),
            Effect = (System.Windows.Media.Effects.Effect)FindResource("WindowShadow"),
            Child = stack
        };
        border.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Content = border;
    }

    public static void Info(Window? owner, string title, string message) =>
        new Dialog(owner, title, message, false, null, ("OK", "ok", true)).ShowDialog();

    public static void Error(Window? owner, string title, string message) =>
        new Dialog(owner, title, message, true, null, ("OK", "ok", true)).ShowDialog();

    public static bool Confirm(Window? owner, string title, string message, string yes = "CONFIRM", bool danger = false)
    {
        var d = new Dialog(owner, title, message, danger, null, ("CANCEL", null, false), (yes, "yes", true));
        return d.ShowDialog() == true;
    }

    public static string? Prompt(Window? owner, string title, string message, string initial = "")
    {
        var d = new Dialog(owner, title, message, false, initial, ("CANCEL", null, false), ("OK", "ok", true));
        return d.ShowDialog() == true ? d._input!.Text.Trim() : null;
    }
}
