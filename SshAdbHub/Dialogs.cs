using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SshAdbHub;

public static class Dialogs
{
    private static Window MakeWindow(Window owner, string title, StackPanel panel) => new()
    {
        Title = title,
        Content = panel,
        SizeToContent = SizeToContent.WidthAndHeight,
        MaxHeight = 720,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = owner,
        Background = new SolidColorBrush(Color.FromRgb(0x11, 0x14, 0x1B)),
        Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false
    };

    private static StackPanel Buttons(out Button ok)
    {
        ok = new Button { Content = "OK", IsDefault = true, Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0xB8, 0xE9, 0x36)), Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x0D, 0x12)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xE9, 0x36)), FontWeight = FontWeights.SemiBold };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80, Height = 30, Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1D, 0x27)), Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x44)) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        return buttons;
    }

    public static string? Prompt(Window owner, string title, string label, string def = "")
    {
        var tb = new TextBox { Text = def, Margin = new Thickness(0, 6, 0, 0), MinWidth = 340, Padding = new Thickness(8, 5, 8, 5), Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1A, 0x23)), Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2D, 0x3D)) };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(tb);
        panel.Children.Add(Buttons(out var ok));
        var w = MakeWindow(owner, title, panel);
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return w.ShowDialog() == true ? tb.Text : null;
    }

    /// <summary>Shows one text box per placeholder. Returns name -> value, or null if cancelled.</summary>
    public static Dictionary<string, string>? PromptParams(Window owner, string title, IList<(string name, string def)> fields)
    {
        var panel = new StackPanel { Margin = new Thickness(14) };
        var boxes = new Dictionary<string, TextBox>();
        foreach (var (name, def) in fields)
        {
            panel.Children.Add(new TextBlock { Text = name, Margin = new Thickness(0, 6, 0, 2), FontWeight = FontWeights.SemiBold });
            var tb = new TextBox { Text = def, MinWidth = 380, Padding = new Thickness(8, 5, 8, 5), Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1A, 0x23)), Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2D, 0x3D)) };
            boxes[name] = tb;
            panel.Children.Add(tb);
        }
        panel.Children.Add(Buttons(out var ok));
        var w = MakeWindow(owner, title, panel);
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => { var first = boxes.Values.FirstOrDefault(); first?.Focus(); first?.SelectAll(); };
        if (w.ShowDialog() != true) return null;
        return boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text);
    }
}

