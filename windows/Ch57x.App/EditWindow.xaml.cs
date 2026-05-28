using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ch57x.Core;

namespace Ch57x.App;

public partial class EditWindow : Window
{
    private readonly Controller _ctrl;
    private int _layer = 0;

    public EditWindow(Controller ctrl)
    {
        InitializeComponent();
        _ctrl = ctrl;
        _ctrl.Changed += Refresh;
        BtnUpload.Click += (_, _) => { _ctrl.Upload(); };
        BtnReload.Click += (_, _) => { if (_ctrl.ProfilePath != null) { _ctrl.LoadProfile(_ctrl.ProfilePath); } };
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Refresh); return; }
        HeaderText.Text = $"{_ctrl.Profile.Name}  ·  키 {_ctrl.Profile.KeyCount} / 노브 {_ctrl.Profile.KnobCount}";

        // layer tabs
        LayerTabs.Children.Clear();
        for (int l = 0; l < 3; l++)
        {
            int idx = l;
            var b = new Button
            {
                Content = "레이어 " + (l + 1),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 6, 0),
                Background = l == _layer ? new SolidColorBrush(Color.FromRgb(0x2d, 0x6c, 0xdf)) : (Brush)Brushes.Transparent,
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x42, 0x50)),
                BorderThickness = new Thickness(1),
            };
            b.Click += (_, _) => { _layer = idx; Refresh(); };
            LayerTabs.Children.Add(b);
        }

        // keys grid (left) + dial table (right)
        Device.Children.Clear();
        var keys = new StackPanel { Orientation = Orientation.Vertical };
        int n = 0;
        foreach (var len in Layout.KeyRows(_ctrl.Profile.KeyCount))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            for (int c = 0; c < len && n < _ctrl.Profile.KeyCount; c++, n++)
            {
                int keyId = n + 1;
                _ctrl.Profile.Layers[_layer].TryGetValue(keyId, out var b);
                row.Children.Add(Keycap((n + 1).ToString(), b, () => EditBinding(keyId, $"버튼 {keyId}")));
            }
            keys.Children.Add(row);
        }
        Device.Children.Add(keys);

        if (_ctrl.Profile.KnobCount > 0)
        {
            int knobs = _ctrl.Profile.KnobCount;
            var grid = new Grid { Margin = new Thickness(14, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int k = 0; k < knobs; k++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int r = 0; r < 3; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int k = 0; k < knobs; k++)
            {
                var th = new TextBlock { Text = $"K{k + 1}", FontSize = 12, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) };
                Grid.SetRow(th, 0); Grid.SetColumn(th, k + 1); grid.Children.Add(th);
            }
            string[] icons = { "↺", "↓", "↻" };
            string[] names = { "반시계", "누름", "시계" };
            for (int a = 0; a < 3; a++)
            {
                var rh = new TextBlock { Text = icons[a], FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) };
                Grid.SetRow(rh, a + 1); Grid.SetColumn(rh, 0); grid.Children.Add(rh);
            }
            for (int k = 0; k < knobs; k++)
                for (int a = 0; a < 3; a++)
                {
                    int knobKeyId = 16 + 3 * k + a;
                    int kk = k, aa = a;
                    _ctrl.Profile.Layers[_layer].TryGetValue(knobKeyId, out var b);
                    var cell = Keycap("", b, () => EditBinding(knobKeyId, $"노브 {kk + 1} {icons[aa]} {names[aa]}"), wide: true);
                    Grid.SetRow(cell, a + 1); Grid.SetColumn(cell, k + 1); grid.Children.Add(cell);
                }
            Device.Children.Add(grid);
        }

        RenderLed();
    }

    // LED 패널 — 레이어별 모드/색상 선택. 변경 즉시 자동 저장.
    private static readonly (int Mode, string Label)[] LedModes =
    {
        (0, "끄기"), (1, "백라이트(색상)"), (5, "백라이트 흰색"),
        (4, "누르면 켜짐(색상)"), (2, "누르면 효과1(색상)"), (3, "누르면 효과2(색상)"),
    };
    private static readonly (int V, string Label, byte R, byte G, byte B)[] LedColors =
    {
        (1, "빨강", 0xc0, 0x39, 0x2b), (2, "주황", 0xe6, 0x7e, 0x22),
        (3, "노랑", 0xd4, 0xb1, 0x06), (4, "초록", 0x2c, 0xa5, 0x4a),
        (5, "청록", 0x17, 0xa2, 0xa2), (6, "파랑", 0x2d, 0x6c, 0xdf),
        (7, "보라", 0x8e, 0x44, 0xad),
    };

    private void RenderLed()
    {
        LedPanel.Children.Clear();
        var led = _ctrl.Profile.Led[_layer];
        bool usesColor = led.Mode != 0 && led.Mode != 5;

        LedPanel.Children.Add(new TextBlock
        {
            Text = "💡 LED  레이어 " + (_layer + 1),
            FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });

        // mode buttons
        var modeBox = new WrapPanel { Margin = new Thickness(0, 0, 12, 0) };
        foreach (var (mode, label) in LedModes)
        {
            int mm = mode;
            var b = new Button
            {
                Content = label, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0),
                Background = led.Mode == mode ? new SolidColorBrush(Color.FromRgb(0x2d, 0x6c, 0xdf)) : new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2e)),
                Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)),
                BorderThickness = new Thickness(1),
            };
            b.Click += (_, _) => { _ctrl.Profile.Led[_layer].Mode = mm; PersistAndRefresh(); };
            modeBox.Children.Add(b);
        }
        LedPanel.Children.Add(modeBox);

        // color swatches (only when mode uses a color)
        var colorBox = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };
        foreach (var (v, label, r, g, bC) in LedColors)
        {
            int vv = v;
            var sw = new Button
            {
                Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromRgb(r, g, bC)),
                BorderBrush = (usesColor && led.Color == v) ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)),
                BorderThickness = new Thickness(usesColor && led.Color == v ? 2 : 1),
                ToolTip = label,
                IsEnabled = usesColor,
                Opacity = usesColor ? 1.0 : 0.35,
            };
            sw.Click += (_, _) => { _ctrl.Profile.Led[_layer].Color = vv; PersistAndRefresh(); };
            colorBox.Children.Add(sw);
        }
        LedPanel.Children.Add(colorBox);
    }

    private void PersistAndRefresh()
    {
        try { if (_ctrl.ProfilePath != null) ProfileStore.Save(_ctrl.Profile, _ctrl.ProfilePath); }
        catch (Exception ex) { Log.Error("프로필 저장", ex); }
        Refresh();
    }

    private Border Keycap(string topLabel, Binding? b, Action onClick, bool wide = false)
    {
        var top = new TextBlock { Text = topLabel, FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)) };
        // 편집 화면에선 alias + 기술 요약 둘 다 두 줄로 (HUD 는 alias 만)
        var sub = new TextBlock { Text = Summarize.OfFull(b), FontSize = 13, Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap };
        var bound = b != null && b.Type != BindingType.None;
        var sp = new StackPanel();
        if (!string.IsNullOrEmpty(topLabel)) sp.Children.Add(top);
        sp.Children.Add(sub);
        var btn = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(0x23, 0x28, 0x31)),
            BorderBrush = bound ? new SolidColorBrush(Color.FromRgb(0x33, 0xff, 0x78)) : new SolidColorBrush(Color.FromRgb(0x3a, 0x42, 0x50)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Width = wide ? 200 : 130, MinHeight = wide ? 60 : 100,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = sp,
        };
        btn.Click += (_, _) => onClick();
        return new Border { Child = btn };
    }

    private void EditBinding(int keyId, string title)
    {
        _ctrl.Profile.Layers[_layer].TryGetValue(keyId, out var cur);
        var dlg = new EditKeyDialog(title, _layer + 1, cur) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            if (dlg.Result == null || dlg.Result.Type == BindingType.None)
                _ctrl.Profile.Layers[_layer].Remove(keyId);
            else
                _ctrl.Profile.Layers[_layer][keyId] = dlg.Result;
            // host-side persistence so alias survives next read/restart
            try
            {
                if (_ctrl.ProfilePath != null) ProfileStore.Save(_ctrl.Profile, _ctrl.ProfilePath);
            }
            catch (Exception ex) { Log.Error("프로필 저장", ex); }
            _ctrl.Profile.Name = _ctrl.Profile.Name; // no-op to keep API symmetric
            Refresh();
            Log.Write($"편집됨: {title} → {Summarize.Of(dlg.Result)}");
        }
    }
}
