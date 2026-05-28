using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Ch57x.Core;

namespace Ch57x.App;

public partial class HudWindow : Window
{
    private readonly Controller _ctrl;
    private int _layer = 0; // 0-based view layer (user-selected, since firmware active layer is unknown)
    private readonly HudSettings _settings = HudSettings.Load();

    public HudWindow(Controller ctrl)
    {
        InitializeComponent();
        _ctrl = ctrl;
        _ctrl.Changed += Refresh;
        _ctrl.Profiles.Changed += Refresh;

        // 헤더 드래그: 빈 공간 클릭만 이동, 자식 버튼/슬라이더는 그대로 동작
        HeaderPanel.PreviewMouseLeftButtonDown += HeaderDragStart;
        ControlsRow.PreviewMouseLeftButtonDown += HeaderDragStart; // 슬라이더 라인 빈 공간으로도 끌어 이동
        BtnHide.Click += (_, _) => Hide();

        // sliders → box size scale (font fixed) + opacity. Live + persisted.
        ScaleSlider.Value = _settings.Scale;
        OpacitySlider.Value = _settings.Opacity;
        Opacity = _settings.Opacity;
        ScaleSlider.ValueChanged += (_, e) => { _settings.Scale = e.NewValue; _settings.Save(); Refresh(); };
        OpacitySlider.ValueChanged += (_, e) => { Opacity = e.NewValue; _settings.Opacity = e.NewValue; _settings.Save(); };

        Loaded += (_, _) =>
        {
            if (!double.IsNaN(_settings.X) && !double.IsNaN(_settings.Y))
            { Left = _settings.X; Top = _settings.Y; }
            else
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 16;
                Top = wa.Bottom - Height - 16;
            }
        };
        LocationChanged += (_, _) => { _settings.X = Left; _settings.Y = Top; _settings.Save(); };

        Refresh();
    }

    // ---- Click-through hook ----
    // We answer WM_NCHITTEST: header + layer tab row → HTCLIENT (interactive),
    // everything else → HTTRANSPARENT (clicks pass through to whatever's behind).
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        src.AddHook(HitTestHook);
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;

    private IntPtr HitTestHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST) return IntPtr.Zero;
        if (!_settings.ClickThrough) return IntPtr.Zero; // 잠금 해제면 전체 잡힘
        long lp = lParam.ToInt64();
        short sx = unchecked((short)(lp & 0xFFFF));
        short sy = unchecked((short)((lp >> 16) & 0xFFFF));
        Point local;
        try { local = PointFromScreen(new Point(sx, sy)); } catch { return IntPtr.Zero; }

        // interactive = 헤더 + 슬라이더 행 + 레이어 탭(L1/L2/L3 버튼) 영역의 합집합.
        // 각 element 의 실제 윈도우 좌표 + ActualWidth/Height 로 계산해서 정확.
        bool interactive = Inside(HeaderPanel, local) || Inside(ControlsRow, local) || Inside(LayerTabs, local);
        handled = true;
        return new IntPtr(interactive ? HTCLIENT : HTTRANSPARENT);
    }

    private void HeaderDragStart(object s, MouseButtonEventArgs e)
    {
        // 자식이 Button/Slider/Thumb 등 인터랙티브 컨트롤이면 드래그 시작 안 함
        var d = e.OriginalSource as DependencyObject;
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase) return;
            if (d is Slider || d is System.Windows.Controls.Primitives.Thumb) return;
            d = VisualTreeHelper.GetParent(d);
        }
        try { DragMove(); } catch { /* DragMove only valid with left button down */ }
    }

    private bool Inside(FrameworkElement el, Point pt)
    {
        if (el.ActualWidth <= 0 || el.ActualHeight <= 0) return false;
        try
        {
            Point origin = el.TranslatePoint(new Point(0, 0), this);
            var rect = new Rect(origin.X, origin.Y, el.ActualWidth, el.ActualHeight);
            return rect.Contains(pt);
        }
        catch { return false; }
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Refresh); return; }

        ProfileName.Text = _ctrl.Profile.Name ?? "(이름 없음)";

        // layer tabs — 버튼 색을 해당 레이어의 LED 색상으로 (구분 쉽게)
        LayerTabs.Children.Clear();
        for (int l = 0; l < 3; l++)
        {
            int idx = l;
            var ledColor = LedColorBrush(_ctrl.Profile.Led.ElementAtOrDefault(l));
            var btn = new Button
            {
                Content = "L" + (l + 1),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 11, FontWeight = FontWeights.Bold,
                Background = ledColor ?? (l == _layer ? (Brush)new SolidColorBrush(Color.FromRgb(0x2d, 0x6c, 0xdf)) : Brushes.Transparent),
                Foreground = Brushes.White,
                BorderBrush = l == _layer ? (Brush)new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff)) : (Brush)new SolidColorBrush(Color.FromRgb(0x3a, 0x42, 0x50)),
                BorderThickness = new Thickness(l == _layer ? 2 : 1),
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

        // dials (right) — table: col headers K1..Kn, row headers ↺/↓/↻ (shown once each)
        if (_ctrl.Profile.KnobCount > 0)
        {
            int knobs = _ctrl.Profile.KnobCount;
            var grid = new Grid { Margin = new Thickness(10, 0, 0, 0) };
            // column 0 = row header (icon); columns 1..knobs = each knob
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int k = 0; k < knobs; k++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // row 0 = column header (K1..Kn); rows 1..3 = action rows
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int r = 0; r < 3; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // column headers K1..Kn
            for (int k = 0; k < knobs; k++)
            {
                var th = new TextBlock { Text = $"K{k + 1}", FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = (Brush)new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 4, 4) };
                Grid.SetRow(th, 0); Grid.SetColumn(th, k + 1);
                grid.Children.Add(th);
            }
            // row headers ↺ ↓ ↻
            string[] icons = { "↺", "↓", "↻" };
            for (int a = 0; a < 3; a++)
            {
                var rh = new TextBlock { Text = icons[a], FontSize = 12,
                    Foreground = (Brush)new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) };
                Grid.SetRow(rh, a + 1); Grid.SetColumn(rh, 0);
                grid.Children.Add(rh);
            }
            // cells
            for (int k = 0; k < knobs; k++)
            {
                for (int a = 0; a < 3; a++)
                {
                    int knobKeyId = 16 + 3 * k + a;
                    _ctrl.Profile.Layers[_layer].TryGetValue(knobKeyId, out var b);
                    var cell = KnobCell(Summarize.Of(b));
                    Grid.SetRow(cell, a + 1); Grid.SetColumn(cell, k + 1);
                    grid.Children.Add(cell);
                }
            }
            Device.Children.Add(grid);
        }
    }

    // LED 모드/색상 → WPF Brush. mode 0(off)=null(기본색 유지), 5=흰색, 그 외는 color 코드별.
    private static Brush? LedColorBrush(LedSetting? led)
    {
        if (led == null || led.Mode == 0) return null;
        if (led.Mode == 5) return new SolidColorBrush(Color.FromArgb(0xcc, 0xee, 0xee, 0xee));
        // color 1..7: red orange yellow green cyan blue purple — alpha ~80% 로 살짝 투명
        Color c = led.Color switch
        {
            1 => Color.FromRgb(0xc0, 0x39, 0x2b),
            2 => Color.FromRgb(0xe6, 0x7e, 0x22),
            3 => Color.FromRgb(0xd4, 0xb1, 0x06),
            4 => Color.FromRgb(0x2c, 0xa5, 0x4a),
            5 => Color.FromRgb(0x17, 0xa2, 0xa2),
            6 => Color.FromRgb(0x2d, 0x6c, 0xdf),
            7 => Color.FromRgb(0x8e, 0x44, 0xad),
            _ => Color.FromRgb(0x55, 0x55, 0x55),
        };
        return new SolidColorBrush(Color.FromArgb(0xcc, c.R, c.G, c.B));
    }

    private Border KnobCell(string sum)
    {
        double s = _settings.Scale;
        return new Border
        {
            Width = 92 * s, MinHeight = 22 * s, Margin = new Thickness(0, 0, 4 * s, 4 * s), Padding = new Thickness(5, 2, 5, 2),
            CornerRadius = new CornerRadius(4),
            Background = (Brush)new SolidColorBrush(Color.FromRgb(0x23, 0x28, 0x31)),
            BorderBrush = (Brush)new SolidColorBrush(Color.FromRgb(0x3a, 0x42, 0x50)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = sum, Foreground = Brushes.White, FontSize = 10,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center },
        };
    }

    private Border Keycap(string topLabel, string sum)
    {
        double s = _settings.Scale;
        var top = new TextBlock { Text = topLabel, Foreground = (Brush)new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)), FontSize = 9, FontWeight = FontWeights.Bold };
        var sub = new TextBlock { Text = sum, Foreground = Brushes.White, FontSize = 10,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        return new Border
        {
            Width = 72 * s, Height = 52 * s, Margin = new Thickness(0, 0, 4 * s, 0), Padding = new Thickness(4, 3, 4, 3),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)new SolidColorBrush(Color.FromRgb(0x23, 0x28, 0x31)),
            BorderBrush = (Brush)new SolidColorBrush(Color.FromRgb(0x3a, 0x42, 0x50)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { top, sub } },
        };
    }

}

internal sealed class HudSettings
{
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.92;
    public bool ClickThrough { get; set; } = true;

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ch57x", "hud.json");

    public static HudSettings Load()
    {
        try { if (File.Exists(Path)) return System.Text.Json.JsonSerializer.Deserialize<HudSettings>(File.ReadAllText(Path)) ?? new(); }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, System.Text.Json.JsonSerializer.Serialize(this));
        }
        catch { }
    }
}
