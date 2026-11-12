using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// HUD — 키 배치를 화면 위에 작게. 0.3.0: H4 노브를 줄 단위(노브1~3) · 줄 높이를 키 칸과 같게 / H6 「보는 층」 · 체크 표지 + 테두리 /
/// H7 머리 단추 셋 같은 24(닫기 · 잠금 토글 · 옮기기). 바탕 없음(H1) · 작은 글자(H8)는 그대로.
/// </summary>
public partial class HudWindow : Window
{
    private readonly Controller _ctrl;
    private int _layer;   // 보는 층 (펌웨어의 지금 층은 알 수 없어 사용자가 고름)
    private readonly HudSettings _settings = HudSettings.Load();

    /// <summary>잠금을 바꿨을 때(트레이 메뉴 갱신)</summary>
    public event Action? LockChanged;

    public HudWindow(Controller ctrl)
    {
        InitializeComponent();
        _ctrl = ctrl;
        _ctrl.Changed += Refresh;
        _ctrl.Profiles.Changed += Refresh;

        HeaderPanel.PreviewMouseLeftButtonDown += HeaderDragStart;
        ControlsRow.PreviewMouseLeftButtonDown += HeaderDragStart;
        BtnMove.PreviewMouseLeftButtonDown += (_, e) => { try { DragMove(); } catch { } e.Handled = true; };
        BtnHide.Click += (_, _) => Hide();
        BtnLock.Click += (_, _) => { SetLocked(!_settings.ClickThrough); LockChanged?.Invoke(); };

        ScaleSlider.Value = _settings.Scale;
        OpacitySlider.Value = _settings.Opacity;
        Opacity = _settings.Opacity;
        ScaleSlider.ValueChanged += (_, e) => { _settings.Scale = e.NewValue; _settings.Save(); Refresh(); };
        OpacitySlider.ValueChanged += (_, e) => { Opacity = e.NewValue; _settings.Opacity = e.NewValue; _settings.Save(); };

        Loaded += (_, _) =>
        {
            if (AppMode.Render) return;
            if (!double.IsNaN(_settings.X) && !double.IsNaN(_settings.Y)) { Left = _settings.X; Top = _settings.Y; }
            else
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 16;
                Top = wa.Bottom - ActualHeight - 16;
            }
        };
        LocationChanged += (_, _) => { if (AppMode.Render) return; _settings.X = Left; _settings.Y = Top; _settings.Save(); };
        Refresh();
    }

    public int Layer { get => _layer; set { _layer = Math.Clamp(value, 0, 2); Refresh(); } }
    public bool Locked => _settings.ClickThrough;

    /// <summary>잠금 = 클릭 통과(머리 · 밀대 · 층 줄만 잡힘)</summary>
    public void SetLocked(bool locked)
    {
        _settings.ClickThrough = locked;
        _settings.Save();
        UpdateLockButton();
        UpdateClickThrough();
        Log.Write($"HUD 잠금: {(locked ? "켜짐 (머리만 잡힘)" : "꺼짐 (전체 잡힘)")}");
    }

    private void UpdateLockButton()
    {
        BtnLock.Tag = _settings.ClickThrough ? "lock" : "unlock";
        BtnLock.ToolTip = _settings.ClickThrough ? "HUD 잠금 켜짐 — 눌러서 풀기(클릭이 HUD 를 통과함)" : "HUD 잠금 꺼짐 — 눌러서 잠그기";
        if (_settings.ClickThrough) BtnLock.SetResourceReference(Button.BackgroundProperty, "CtrlFill2");
        else BtnLock.SetResourceReference(Button.BackgroundProperty, "CtrlFill");
    }

    // ---- 클릭 통과: WS_EX_TRANSPARENT 를 마우스 위치에 따라 켜고 끈다 (0.2.1 그대로) ----
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private IntPtr _hwnd;
    private System.Windows.Threading.DispatcherTimer? _hitTracker;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (AppMode.Render) return;
        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyClickThrough(true);
        _hitTracker = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _hitTracker.Tick += (_, _) => UpdateClickThrough();
        _hitTracker.Start();
        IsVisibleChanged += (_, _) => { if (IsVisible) _hitTracker?.Start(); else _hitTracker?.Stop(); };
    }

    private void UpdateClickThrough()
    {
        if (!IsVisible || _hwnd == IntPtr.Zero) return;
        if (!_settings.ClickThrough) { ApplyClickThrough(false); return; }
        if (!GetCursorPos(out POINT p)) return;
        Point local;
        try { local = PointFromScreen(new Point(p.X, p.Y)); } catch { return; }
        bool over = Inside(HeaderPanel, local) || Inside(ControlsRow, local) || Inside(LayerRow, local);
        ApplyClickThrough(!over);
    }

    private void ApplyClickThrough(bool transparent)
    {
        if (_hwnd == IntPtr.Zero) return;
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        bool isOn = (ex & WS_EX_TRANSPARENT) != 0;
        if (transparent && !isOn) SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT);
        else if (!transparent && isOn) SetWindowLong(_hwnd, GWL_EXSTYLE, ex & ~WS_EX_TRANSPARENT);
    }

    /// <summary>트레이 「HUD 잠금」에서 바꿨을 때 즉시 반영</summary>
    public void ReloadSettingsAndApply()
    {
        _settings.ClickThrough = HudSettings.Load().ClickThrough;
        UpdateLockButton();
        UpdateClickThrough();
    }

    private void HeaderDragStart(object s, MouseButtonEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase || d is Slider || d is System.Windows.Controls.Primitives.Thumb) return;
            d = VisualTreeHelper.GetParent(d);
        }
        try { DragMove(); } catch { }
    }

    private bool Inside(FrameworkElement el, Point pt)
    {
        if (el.ActualWidth <= 0 || el.ActualHeight <= 0) return false;
        try { var o = el.TranslatePoint(new Point(0, 0), this); return new Rect(o.X, o.Y, el.ActualWidth, el.ActualHeight).Contains(pt); }
        catch { return false; }
    }

    // ───────────────────────── 그리기

    public void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var p = _ctrl.Profile;
        ProfileName.Text = _ctrl.ProfileName;
        UpdateLockButton();

        LayerTabs.Children.Clear();
        for (int l = 0; l < 3; l++)
        {
            int idx = l;
            LayerTabs.Children.Add(LayerPill(l, KeySettingsWindow.LedBrush(p.Led.ElementAtOrDefault(l)), l == _layer, () => { _layer = idx; Refresh(); }));
        }

        double s = _settings.Scale;
        Device.Children.Clear();
        var keys = new StackPanel();
        var rows = Layout.KeyRows(p.KeyCount);
        var layer = p.Layers[_layer];
        int n = 0;
        for (int r = 0; r < rows.Length; r++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, r < rows.Length - 1 ? 4 : 0) };
            for (int c = 0; c < rows[r] && n < p.KeyCount; c++, n++)
            {
                layer.TryGetValue(n + 1, out var b);
                var cell = Card(72 * s, 48 * s);
                var top = new TextBlock { Text = (n + 1).ToString(), FontSize = 10, FontWeight = FontWeights.Bold, LineHeight = 12, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
                top.SetResourceReference(TextBlock.ForegroundProperty, "T2");
                cell.Child = new StackPanel { Children = { top, Sum(b) } };
                cell.Margin = new Thickness(0, 0, c < rows[r] - 1 ? 4 : 0, 0);
                row.Children.Add(cell);
            }
            keys.Children.Add(row);
        }
        Device.Children.Add(keys);

        if (p.KnobCount <= 0) return;
        // 노브 = 줄 단위(노브1 · 노브2 · 노브3), 줄 높이 = 키 칸 높이 → 키 묶음과 위아래가 맞음 (H4)
        var knobs = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        string[] icons = { "knob-ccw", "knob-press", "knob-cw" };
        for (int k = 0; k < p.KnobCount; k++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, k < p.KnobCount - 1 ? 4 : 0) };
            var label = new StackPanel { Orientation = Orientation.Horizontal, Width = 52, VerticalAlignment = VerticalAlignment.Center };
            var lg = new Glyph { Kind = "knob", Size = 14, Margin = new Thickness(0, 0, 4, 0) };
            lg.SetResourceReference(Glyph.ForegroundProperty, "T2");
            label.Children.Add(lg);
            var lt = new TextBlock { Text = Terms.Knob(k), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            lt.SetResourceReference(TextBlock.ForegroundProperty, "T2");
            label.Children.Add(lt);
            row.Children.Add(label);
            for (int a = 0; a < 3; a++)
            {
                layer.TryGetValue(16 + 3 * k + a, out var b);
                var cell = Card(80 * s, 48 * s);
                var g = new Glyph { Kind = icons[a], Size = 12, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 0) };
                g.SetResourceReference(Glyph.ForegroundProperty, "T2");
                cell.Child = new StackPanel { Children = { g, Sum(b) } };
                cell.Margin = new Thickness(a > 0 ? 4 : 0, 0, 0, 0);
                row.Children.Add(cell);
            }
            knobs.Children.Add(row);
        }
        Device.Children.Add(knobs);
    }

    private static Border Card(double w, double h)
    {
        var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(6, 4, 6, 4), ClipToBounds = true };
        b.SetResourceReference(Border.BackgroundProperty, "HudCard");
        b.SetResourceReference(Border.BorderBrushProperty, "CtrlStroke");
        return b;
    }

    private static TextBlock Sum(Binding? b)
    {
        // 두 줄까지 (줄 높이 14 — 칸 48 = 위 12 + 14 × 2 + 여백 8) · 넘치면 말줄임
        var t = new TextBlock { Text = Summarize.Of(b), FontSize = 11, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 28, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, b == null || b.Type == BindingType.None ? "T3" : "T1");
        return t;
    }

    /// <summary>층 알약: 바탕 = 그 층 LED 색(끄면 중립) · 고른 층 = 체크 + 2px 테두리 (H6)</summary>
    private static Button LayerPill(int l, Brush? led, bool on, Action pick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (on) content.Children.Add(new Glyph { Kind = "check", Size = 12, Margin = new Thickness(0, 0, 4, 0), Foreground = Brushes.White });
        var label = new TextBlock { Text = $"L{l + 1}", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        if (led != null) label.Foreground = Brushes.White; else label.SetResourceReference(TextBlock.ForegroundProperty, "T1");
        content.Children.Add(label);
        var bd = new Border { Height = 24, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2), Child = content };
        if (led != null) bd.Background = led; else bd.SetResourceReference(Border.BackgroundProperty, "CtrlFill");
        if (on) bd.SetResourceReference(Border.BorderBrushProperty, "T1"); else bd.BorderBrush = Brushes.Transparent;
        if (led == null && on) { /* 체크 표지 색 = 글자색 */ ((Glyph)content.Children[0]).SetResourceReference(Glyph.ForegroundProperty, "T1"); }
        var btn = new Button { Content = bd, Margin = new Thickness(0, 0, 4, 0), Focusable = false, MinHeight = 0, Padding = new Thickness(0), ToolTip = on ? $"레이어 {l + 1} 보는 중" : $"레이어 {l + 1} 보기" };
        btn.Template = PillTemplate;
        System.Windows.Automation.AutomationProperties.SetName(btn, on ? $"레이어 {l + 1}, 보는 중" : $"레이어 {l + 1}");
        btn.Click += (_, _) => pick();
        return btn;
    }

    private static readonly ControlTemplate PillTemplate = new(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
}

internal sealed class HudSettings
{
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.92;
    public bool ClickThrough { get; set; } = true;

    private static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ch57x", "hud.json");

    public static HudSettings Load()
    {
        if (AppMode.Render) return new();
        try { if (File.Exists(Path)) return System.Text.Json.JsonSerializer.Deserialize<HudSettings>(File.ReadAllText(Path)) ?? new(); }
        catch { }
        return new();
    }

    public void Save()
    {
        if (AppMode.Render) return;   // 렌더 모드는 설정 파일을 건드리지 않는다
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, System.Text.Json.JsonSerializer.Serialize(this));
        }
        catch { }
    }
}
