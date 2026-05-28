using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ch57x.Core;

namespace Ch57x.App;

public partial class HudWindow : Window
{
    private readonly Controller _ctrl;
    private int _layer = 0; // 0-based view layer (user-selected, since firmware active layer is unknown)

    public HudWindow(Controller ctrl)
    {
        InitializeComponent();
        _ctrl = ctrl;
        _ctrl.Changed += Refresh;
        _ctrl.Profiles.Changed += Refresh;

        // drag anywhere on the HUD body to move it
        MouseLeftButtonDown += (_, _) => { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
        BtnHide.Click += (_, _) => Hide();

        // restore last position (saved to %AppData%\Ch57x\hud.txt as "x,y")
        Loaded += (_, _) =>
        {
            if (HudSettings.TryLoadPos(out double x, out double y)) { Left = x; Top = y; }
            else
            {
                // default: bottom-right corner with some margin
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 16;
                Top = wa.Bottom - Height - 16;
            }
        };
        LocationChanged += (_, _) => HudSettings.SavePos(Left, Top);

        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Refresh); return; }

        ProfileName.Text = _ctrl.Profile.Name ?? "(이름 없음)";

        // layer tabs
        LayerTabs.Children.Clear();
        for (int l = 0; l < 3; l++)
        {
            int idx = l;
            var btn = new Button
            {
                Content = "L" + (l + 1),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11,
                Background = l == _layer ? (Brush)new SolidColorBrush(Color.FromRgb(0x2d, 0x6c, 0xdf)) : Brushes.Transparent,
                Foreground = Brushes.White,
                BorderBrush = (Brush)new SolidColorBrush(Color.FromRgb(0x3a, 0x42, 0x50)),
                BorderThickness = new Thickness(1),
            };
            btn.Click += (_, _) => { _layer = idx; Refresh(); };
            LayerTabs.Children.Add(btn);
        }

        // device: keys (left) + dials (right)
        Device.Children.Clear();
        var keys = new StackPanel { Orientation = Orientation.Vertical };
        var rows = Layout.KeyRows(_ctrl.Profile.KeyCount);
        int n = 0;
        foreach (var len in rows)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            for (int c = 0; c < len && n < _ctrl.Profile.KeyCount; c++, n++)
            {
                int keyId = n + 1;
                _ctrl.Profile.Layers[_layer].TryGetValue(keyId, out var b);
                row.Children.Add(Keycap((n + 1).ToString(), Summarize.Of(b)));
            }
            keys.Children.Add(row);
        }
        Device.Children.Add(keys);

        // dials column (right) — one row per knob, action chips beside
        if (_ctrl.Profile.KnobCount > 0)
        {
            var dials = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 0, 0, 0) };
            for (int k = 0; k < _ctrl.Profile.KnobCount; k++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                row.Children.Add(new TextBlock { Text = $"K{k + 1}", Foreground = (Brush)new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Width = 18 });
                string[] icons = { "↺", "↓", "↻" };
                for (int a = 0; a < 3; a++)
                {
                    int knobKeyId = 16 + 3 * k + a;
                    _ctrl.Profile.Layers[_layer].TryGetValue(knobKeyId, out var b);
                    row.Children.Add(Chip(icons[a], Summarize.Of(b)));
                }
                dials.Children.Add(row);
            }
            Device.Children.Add(dials);
        }
    }

    private Border Keycap(string topLabel, string sum)
    {
        var top = new TextBlock { Text = topLabel, Foreground = (Brush)new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)), FontSize = 9, FontWeight = FontWeights.Bold };
        var sub = new TextBlock { Text = sum, Foreground = Brushes.White, FontSize = 10, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        return new Border
        {
            Width = 64, Height = 44, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(4, 3, 4, 3),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)new SolidColorBrush(Color.FromRgb(0x23, 0x28, 0x31)),
            BorderBrush = (Brush)new SolidColorBrush(Color.FromRgb(0x3a, 0x42, 0x50)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { top, sub } },
        };
    }

    private Border Chip(string icon, string sum)
    {
        var ic = new TextBlock { Text = icon, Foreground = (Brush)new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)), FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
        var t = new TextBlock { Text = sum, Foreground = Brushes.White, FontSize = 10, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        return new Border
        {
            Width = 110, Height = 22, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(5, 1, 5, 1),
            CornerRadius = new CornerRadius(4),
            Background = (Brush)new SolidColorBrush(Color.FromRgb(0x23, 0x28, 0x31)),
            BorderBrush = (Brush)new SolidColorBrush(Color.FromRgb(0x3a, 0x42, 0x50)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { ic, t } },
        };
    }
}

internal static class HudSettings
{
    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ch57x", "hud.txt");

    public static bool TryLoadPos(out double x, out double y)
    {
        x = y = 0;
        try
        {
            if (!File.Exists(Path)) return false;
            var parts = File.ReadAllText(Path).Split(',');
            return parts.Length == 2 && double.TryParse(parts[0], out x) && double.TryParse(parts[1], out y);
        }
        catch { return false; }
    }

    public static void SavePos(double x, double y)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, $"{x},{y}");
        }
        catch { }
    }
}
