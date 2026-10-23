using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SCS.Core;

namespace SCS.UI;

public partial class PickerWindow : Window
{
    public string? Selected { get; private set; }
    private string? _category;

    public PickerWindow(string current, string slotNumber)
    {
        InitializeComponent();
        TitleText.Text = $"SLOT {slotNumber}  /  SELECT STRATAGEM";
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        AddChip("ALL", null, true);
        foreach (var cat in Stratagems.Categories) AddChip(cat.ToUpperInvariant(), cat, false);

        Filter();
        var cur = Stratagems.Find(current);
        if (cur != null)
        {
            StratList.SelectedItem = cur;
            StratList.ScrollIntoView(cur);
        }
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void AddChip(string label, string? category, bool isChecked)
    {
        var chip = new RadioButton
        {
            Content = label,
            GroupName = "cat",
            IsChecked = isChecked,
            Style = (Style)FindResource("Chip")
        };
        chip.Checked += (_, _) => { _category = category; Filter(); };
        Chips.Children.Add(chip);
    }

    private void Filter()
    {
        var q = SearchBox.Text.Trim();
        SearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var items = Stratagems.All.Where(s =>
            (_category == null || s.Category == _category) &&
            terms.All(t => s.SearchText.Contains(t, StringComparison.OrdinalIgnoreCase) || s.Category.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var prev = StratList.SelectedItem;
        StratList.ItemsSource = items;
        if (prev != null && items.Contains(prev)) StratList.SelectedItem = prev;
        else if (items.Count > 0) StratList.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Filter();

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (StratList.SelectedIndex < StratList.Items.Count - 1) StratList.SelectedIndex++;
                StratList.ScrollIntoView(StratList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                if (StratList.SelectedIndex > 0) StratList.SelectedIndex--;
                StratList.ScrollIntoView(StratList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                Accept();
                e.Handled = true;
                break;
        }
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();
    private void Select_Click(object sender, RoutedEventArgs e) => Accept();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Selected = "";
        DialogResult = true;
    }

    private void Accept()
    {
        if (StratList.SelectedItem is not Stratagem s) return;
        Selected = s.Name;
        DialogResult = true;
    }
}
