using System.Windows;
using System.Windows.Controls;

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
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false
    };

    private static StackPanel Buttons(out Button ok)
    {
        ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        return buttons;
    }

    public static string? Prompt(Window owner, string title, string label, string def = "")
    {
        var tb = new TextBox { Text = def, Margin = new Thickness(0, 6, 0, 0), MinWidth = 340 };
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
            var tb = new TextBox { Text = def, MinWidth = 380 };
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
