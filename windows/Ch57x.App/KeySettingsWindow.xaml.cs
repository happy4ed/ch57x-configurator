using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>앱 곳곳에서 여는 창 · 동작 — 트레이(TrayIcon)가 맡는다. 렌더 모드에서는 null.</summary>
public interface IShell
{
    void ShowLog();
    void ShowAbout(bool openUpdate);
    void ImportProfile(Window owner);
    void BackupAll(Window owner);
    void RestoreBackup(Window owner);
}

/// <summary>
/// 키 설정 창 = 메인 창 (판정 M1 · S2). 2판 개선안 모형(artifacts/ch57x-pc-app-ux/mocks/mock.html keyset)의 배치 · 크기 그대로.
/// </summary>
public partial class KeySettingsWindow : FluentWindow
{
    private readonly Controller _ctrl;
    private readonly IShell? _shell;
    private int _layer;
    private bool _busy;
    private bool _syncing;
    private Action? _infoAction;
    private string? _doneRest;   // 업로드 끝 알림 뒤 글 — null 이면 알림 없음 (S3 · 첫 사용 점검 6번)
    private string? _doneTip;
    private readonly DispatcherTimer _doneTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private const double LockedOpacity = 0.45;

    /// <summary>처음 닫을 때(트레이에서 계속 동작함 안내 — 판정 M5)</summary>
    public event Action? FirstClose;
    /// <summary>앱 종료 때만 진짜로 닫는다. 평소 닫기 = 숨김(트레이 상주).</summary>
    public bool AllowClose { get; set; }

    /// <summary>조명 방식 이름 — 괄호 설명 없이(첫 사용 점검 8번: 색은 옆 견본으로 고르는 것이 보임)</summary>
    public static readonly (int Mode, string Label)[] LedModes =
    {
        (0, "끄기"), (1, "백라이트"), (5, "백라이트 흰색"),
        (4, "누르면 켜짐"), (2, "누르면 효과 1"), (3, "누르면 효과 2"),
    };

    /// <summary>펌웨어 LED 7색 (DESIGN.md led-* · 0.2.1 앱 값 그대로)</summary>
    public static readonly (int V, string Name, Color C)[] LedColors =
    {
        (1, "빨강", Color.FromRgb(0xC0, 0x39, 0x2B)), (2, "주황", Color.FromRgb(0xE6, 0x7E, 0x22)),
        (3, "노랑", Color.FromRgb(0xD4, 0xB1, 0x06)), (4, "초록", Color.FromRgb(0x2C, 0xA5, 0x4A)),
        (5, "청록", Color.FromRgb(0x17, 0xA2, 0xA2)), (6, "파랑", Color.FromRgb(0x2D, 0x6C, 0xDF)),
        (7, "보라", Color.FromRgb(0x8E, 0x44, 0xAD)),
    };

    /// <summary>레이어 LED 색 붓 — 끄기 · 흰색이면 null(중립 테두리 · DESIGN.md)</summary>
    public static Brush? LedBrush(LedSetting? s)
    {
        if (s == null || s.Mode == 0 || s.Mode == 5) return null;
        var c = LedColors.FirstOrDefault(x => x.V == s.Color).C;
        if (c == default) return null;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public KeySettingsWindow(Controller ctrl, IShell? shell)
    {
        InitializeComponent();
        _ctrl = ctrl;
        _shell = shell;
        Title = Terms.WindowTitle(Terms.KeySettings);
        _layer = 0;

        foreach (var (mode, label) in LedModes) LedMode.Items.Add(new ComboBoxItem { Content = label, Tag = mode });
        LedMode.SelectionChanged += (_, _) =>
        {
            if (_syncing || LedMode.SelectedItem is not ComboBoxItem it) return;
            _ctrl.SetLed(_layer, mode: (int)it.Tag);
        };

        UndoBtn.Click += (_, _) => DoUndo();
        LoadPcBtn.Click += (_, _) => _ctrl.LoadFromPc();
        UploadBtn.Click += async (_, _) => await UploadAsync();
        ReadBtn.Click += async (_, _) => await ReadAsync();
        MoreBtn.Click += (_, _) => OpenMenu(BuildMoreMenu(), MoreBtn, alignRight: true);
        ProfileBtn.Click += (_, _) => OpenMenu(BuildProfileMenu(), ProfileBtn, alignRight: false);
        InfoClose.Click += (_, _) => HideInfo();
        InfoAction.Click += (_, _) => _infoAction?.Invoke();
        PreviewKeyDown += OnKey;
        _doneTimer.Tick += (_, _) => { _doneTimer.Stop(); _doneRest = null; _doneTip = null; UpdateStatus(); };
        Closed += (_, _) => _doneTimer.Stop();

        _ctrl.Changed += Refresh;
        Closing += OnClosing;
        Refresh();
    }

    /// <summary>보고 있는 레이어 (0부터)</summary>
    public int Layer
    {
        get => _layer;
        set { _layer = Math.Clamp(value, 0, 2); Refresh(); }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose || AppMode.Render) return;
        e.Cancel = true;   // 닫기 = 숨김 — 앱은 트레이에서 계속 동작
        Hide();
        if (!_ctrl.Settings.CloseHintShown)
        {
            _ctrl.Settings.CloseHintShown = true;
            _ctrl.Settings.Save();
            FirstClose?.Invoke();
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { DoUndo(); e.Handled = true; }
    }

    private void DoUndo()
    {
        if (_busy) return;   // 업로드 · 읽기 중에는 편집 잠금 (첫 사용 점검 5번)
        if (!_ctrl.Undo()) ShowInfo("info", "되돌릴 것 없음", "");
    }

    // ───────────────────────── 그리기

    public void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var p = _ctrl.Profile;

        // 연결 상태 (E1 · M3)
        if (_ctrl.IsConnected)
        {
            StatusDot.SetResourceReference(Shape_Fill, "Success");
            StatusText.Text = "연결됨";
            StatusCaption.Text = $"{p.KeyCount}키 · {p.KnobCount}노브";
        }
        else
        {
            StatusDot.SetResourceReference(Shape_Fill, "T3");
            StatusText.Text = "연결 안 됨";
            StatusCaption.Text = "키보드를 꽂으면 자동으로 연결됨";
        }
        ProfileBtn.Content = new TextBlock { Text = _ctrl.ProfileName };
        ProfileBtn.ToolTip = $"프로필: {_ctrl.ProfileName} — 눌러서 다른 프로필 · 새로 만들기 · 이름 바꾸기";

        // 레이어 고르기 — 점 · 선택 표시줄 = 그 레이어의 LED 색 (E3)
        LayerTabs.Children.Clear();
        for (int l = 0; l < 3; l++)
        {
            int idx = l;
            var tab = new RadioButton { Content = $"레이어 {l + 1}", GroupName = "layer", IsChecked = l == _layer };
            tab.SetResourceReference(StyleProperty, "LayerTab");
            var led = LedBrush(p.Led.ElementAtOrDefault(l));
            if (led != null) tab.Tag = led; else tab.SetResourceReference(TagProperty, "T3");
            tab.Checked += (_, _) => { if (_layer != idx) { _layer = idx; Refresh(); } };
            LayerTabs.Children.Add(tab);
        }

        BuildDevice(p);
        BuildLed(p);

        UpdateStatus();
        ApplyLock();
        UndoBtn.ToolTip = _ctrl.History.NextLabel is { } lab ? $"{Terms.Undo} — {lab} (Ctrl+Z)" : $"{Terms.Undo} (Ctrl+Z)";
        LoadPcBtn.ToolTip = "PC 에 저장된 이 프로필을 화면으로 다시 불러옴 — 키보드 값이 아님";
    }

    private static readonly DependencyProperty Shape_Fill = System.Windows.Shapes.Shape.FillProperty;

    private void BuildDevice(Profile p)
    {
        Device.Children.Clear();
        var layer = p.Layers[_layer];
        var led = LedBrush(p.Led.ElementAtOrDefault(_layer));

        var keys = new StackPanel();
        var rows = Layout.KeyRows(p.KeyCount);
        int n = 0;
        for (int r = 0; r < rows.Length; r++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, r < rows.Length - 1 ? 8 : 0) };
            for (int c = 0; c < rows[r] && n < p.KeyCount; c++, n++)
            {
                int keyId = n + 1;
                layer.TryGetValue(keyId, out var b);
                var top = new TextBlock { Text = keyId.ToString(), FontWeight = FontWeights.SemiBold };
                top.SetResourceReference(TextBlock.ForegroundProperty, "T2");
                var cell = Cell(top, b, led, keyId);
                cell.Width = 120;
                cell.Margin = new Thickness(0, 0, c < rows[r] - 1 ? 8 : 0, 0);
                row.Children.Add(cell);
            }
            keys.Children.Add(row);
        }
        Device.Children.Add(keys);

        if (p.KnobCount <= 0) return;
        var grid = new Grid { Margin = new Thickness(24, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        for (int a = 0; a < 3; a++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string[] icons = { "knob-ccw", "knob-press", "knob-cw" };
        for (int k = 0; k < p.KnobCount; k++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new Glyph { Kind = "knob", Size = 20, Margin = new Thickness(0, 0, 8, 0) });
            label.Children.Add(new TextBlock { Text = Terms.Knob(k), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(label, k);
            grid.Children.Add(label);
            for (int a = 0; a < 3; a++)
            {
                int keyId = 16 + 3 * k + a;
                layer.TryGetValue(keyId, out var b);
                var top = new StackPanel { Orientation = Orientation.Horizontal };
                var g = new Glyph { Kind = icons[a], Size = 16, Margin = new Thickness(0, 0, 4, 0) };
                g.SetResourceReference(Glyph.ForegroundProperty, "T2");
                top.Children.Add(g);
                var t = new TextBlock { Text = Terms.KnobActions[a] };
                t.SetResourceReference(TextBlock.ForegroundProperty, "T2");
                top.Children.Add(t);
                var cell = Cell(top, b, led, keyId);
                cell.Width = 128;
                cell.Margin = new Thickness(8, 0, 0, k < p.KnobCount - 1 ? 8 : 0);
                Grid.SetRow(cell, k);
                Grid.SetColumn(cell, a + 1);
                grid.Children.Add(cell);
            }
        }
        Device.Children.Add(grid);
    }

    private Button Cell(FrameworkElement top, Binding? b, Brush? led, int keyId)
    {
        bool bound = b != null && b.Type != BindingType.None;
        var sum = new TextBlock { Text = Summarize.Of(b), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
        sum.SetResourceReference(TextBlock.ForegroundProperty, bound ? "T1" : "T3");
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(top);
        Grid.SetRow(sum, 1);
        g.Children.Add(sum);
        var btn = new Button { Content = g, ToolTip = $"{Terms.KeyName(keyId)}: {Summarize.OfFull(b)} — 눌러서 바꾸기" };
        btn.SetResourceReference(StyleProperty, "KeyCell");
        if (led != null) btn.BorderBrush = led; else btn.SetResourceReference(BorderBrushProperty, "StrongStroke");
        AutomationProperties_SetName(btn, $"{Terms.KeyName(keyId)}, {Summarize.Of(b)}");
        btn.Click += (_, _) => EditKey(keyId);
        return btn;
    }

    private static void AutomationProperties_SetName(DependencyObject d, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(d, name);

    private void BuildLed(Profile p)
    {
        var led = p.Led[_layer];
        _syncing = true;
        LedMode.SelectedItem = LedMode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == led.Mode);
        _syncing = false;
        Swatches.Children.Clear();
        bool usesColor = led.Mode != 0 && led.Mode != 5;
        Swatches.Visibility = usesColor ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (v, name, c) in LedColors)
        {
            int vv = v;
            bool on = led.Color == v;
            Swatches.Children.Add(Swatch(c, name, on, () => _ctrl.SetLed(_layer, color: vv)));
        }
    }

    /// <summary>색 견본 24 · 고른 색 = 바탕 2px 띠 + 글자색 2px 고리(색만으로 나타내지 않음 — DESIGN.md)</summary>
    private FrameworkElement Swatch(Color c, string name, bool on, Action pick)
    {
        var face = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(c) };
        var grid = new Grid { Width = 24, Height = 24 };
        if (on)
        {
            var outer = new Border { Margin = new Thickness(-4), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(2) };
            outer.SetResourceReference(Border.BorderBrushProperty, "T1");
            var gap = new Border { Margin = new Thickness(-2), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2) };
            gap.SetResourceReference(Border.BorderBrushProperty, "Bg");
            grid.Children.Add(outer);
            grid.Children.Add(gap);
        }
        grid.Children.Add(face);
        var btn = new Button { Content = grid, Width = 24, Height = 24, Padding = new Thickness(0), MinHeight = 0, Margin = new Thickness(0, 0, 8, 0), ToolTip = on ? $"{name} (고름)" : name, Cursor = Cursors.Hand };
        btn.Template = SwatchTemplate;
        AutomationProperties_SetName(btn, on ? $"조명 색 {name}, 고름" : $"조명 색 {name}");
        btn.Click += (_, _) => pick();
        return btn;
    }

    private static readonly ControlTemplate SwatchTemplate = BuildSwatchTemplate();
    private static ControlTemplate BuildSwatchTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        t.VisualTree = cp;
        return t;
    }

    // ───────────────────────── 키 바꾸기

    private void EditKey(int keyId)
    {
        if (_busy) return;
        _ctrl.Profile.Layers[_layer].TryGetValue(keyId, out var cur);
        var dlg = new KeyDialog(Terms.KeyTitle(keyId, _layer), cur) { Owner = this };
        if (dlg.ShowDialog() == true) _ctrl.SetBinding(_layer, keyId, dlg.Result);
    }

    // ───────────────────────── 알림 줄

    public void ShowInfo(string kind, string strong, string rest, string? action = null, Action? onAction = null)
    {
        InfoBar.Visibility = Visibility.Visible;
        InfoBar.SetResourceReference(Border.BackgroundProperty, kind switch { "ok" => "SuccessBg", "error" => "CriticalBg", _ => "AttentionBg" });
        InfoIcon.Kind = kind switch { "ok" => "ok", "error" => "error", _ => "info" };
        InfoIcon.SetResourceReference(Glyph.ForegroundProperty, kind switch { "ok" => "Success", "error" => "Critical", _ => "Accent" });
        InfoText.Inlines.Clear();
        InfoText.Inlines.Add(new Run(strong) { FontWeight = FontWeights.SemiBold });
        if (rest.Length > 0) InfoText.Inlines.Add(new Run(" · " + rest));
        _infoAction = onAction;
        InfoAction.Visibility = action != null ? Visibility.Visible : Visibility.Collapsed;
        InfoAction.Content = action;
        InfoAction.Tag = action == "정보 복사" ? "copy" : null;
    }

    public void HideInfo() { InfoBar.Visibility = Visibility.Collapsed; _infoAction = null; }

    /// <summary>
    /// 업로드 끝 알림 (판정 S3 · 첫 사용 점검 6번): 아래 줄 상태 자리에 「업로드 끝 · 14:32 · 레이어 3개 · 조명 포함」 10초.
    /// 알림 줄(위)에 띄우면 레이어 탭 · 키 칸이 밀려 내려가서 — 상태 자리는 폭 · 높이가 그대로라 레이아웃 이동 0.
    /// 키보드에 있던 키를 지킨 수는 도움말로(한 줄이 남는 폭을 넘지 않게).
    /// </summary>
    public void ShowUploaded(OpOutcome o)
    {
        _doneRest = $"{o.At:HH:mm} · 레이어 {o.Layers}개 · 조명 포함";
        _doneTip = o.Kept > 0 ? $"키보드에 있던 {o.Kept}키는 그대로 둠" : null;
        _doneTimer.Stop();
        _doneTimer.Start();
        UpdateStatus();
    }

    /// <summary>아래 줄 상태 자리 — 업로드 끝(10초 · 그 사이 바꾸면 바로 변경 수로) · 「업로드 안 한 변경 n개」 · 진행 중엔 비움</summary>
    private void UpdateStatus()
    {
        DoneIcon.Visibility = Visibility.Collapsed;
        StatusSlot.ToolTip = null;
        if (_busy) PendingText.Text = "";   // 진행은 단추 안 % + 막대만 — 같은 % 를 두 번 보이지 않음 (첫 사용 점검 5번)
        else if (_doneRest != null && _ctrl.PendingChanges == 0)
        {
            DoneIcon.Visibility = Visibility.Visible;
            PendingText.Inlines.Clear();
            PendingText.Inlines.Add(new Run("업로드 끝") { FontWeight = FontWeights.SemiBold });
            PendingText.Inlines.Add(new Run(" · " + _doneRest));
            StatusSlot.ToolTip = _doneTip;
        }
        else
        {
            _doneRest = null;
            PendingText.Text = _ctrl.PendingChanges is int n && n > 0 ? $"업로드 안 한 변경 {n}개" : "변경 없음";
        }
        FitStatus();
    }

    /// <summary>상태 글이 창 폭을 바꾸지 않게 — 아래 줄에서 단추들이 쓰고 남는 폭까지만(넘치면 말줄임). 최소 160 은 늘 잡아 둠(XAML).</summary>
    private void FitStatus()
    {
        PendingText.MaxWidth = double.PositiveInfinity;
        if (BottomRow.ActualWidth <= 0) return;
        double used = 16;   // 왼쪽 단추들과 띄움
        foreach (var b in new FrameworkElement[] { UndoBtn, LoadPcBtn, ReadBtn, UploadBtn, StatusSlot })
            used += b.Margin.Left + b.Margin.Right + (b == StatusSlot ? 0 : b.ActualWidth);
        double icon = DoneIcon.Visibility == Visibility.Visible ? DoneIcon.Size + DoneIcon.Margin.Right : 0;
        PendingText.MaxWidth = Math.Max(StatusSlot.MinWidth, BottomRow.ActualWidth - used) - icon;
    }

    /// <summary>업로드 · 읽기 실패: 연결 안 됨 · 멈춤은 알림 줄, 그 밖은 오류 알림(주 단추 「다시 해 보기」 · 첫 사용 점검 1번)</summary>
    private void ShowFailure(string context, OpOutcome o, Func<Task> retry)
    {
        if (o.NotConnected) { ShowInfo("error", "키보드가 연결되지 않음", "키보드를 꽂으면 자동으로 연결됨"); return; }
        if (o.Cancelled) { ShowInfo("info", "업로드 안 함", "키보드의 지금 설정을 읽지 못해 멈춤"); return; }
        Dialogs.Error(this, context, o.Error ?? new Exception("알 수 없는 오류"), retry: () => _ = retry(), device: true);
    }

    // ───────────────────────── 업로드 · 읽기 (뒤에서 — 화면이 굳지 않음 · S4)

    /// <summary>
    /// 업로드 · 읽기 중: 두 단추 잠김 · 단추 안 「업로드 중 42%」 · 진행 막대 (판정 S4 · 모형 keyset-c).
    /// 편집도 잠금 — 키 칸 · 노브 칸 · 레이어 탭 · 프로필 목록 · 실행 취소 · 조명 흐리게(첫 사용 점검 5번 · ApplyLock).
    /// 단추 폭은 시작할 때 폭으로 고정 — % 자릿수가 바뀌어도 옆 단추가 흔들리지 않게.
    /// </summary>
    public void SetBusy(string? label, int pct)
    {
        bool starting = label != null && !_busy;
        _busy = label != null;
        if (starting)
        {
            if (UploadBtn.ActualWidth > 0) UploadBtn.MinWidth = UploadBtn.ActualWidth;
            if (ReadBtn.ActualWidth > 0) ReadBtn.MinWidth = ReadBtn.ActualWidth;
        }
        ReadBtn.IsEnabled = !_busy;
        UploadBtn.IsEnabled = !_busy;
        if (_busy)
        {
            if (label == "업로드 중") UploadBtn.Content = $"업로드 중 {pct}%";
            else ReadBtn.Content = $"{label} {pct}%";
            Progress.Visibility = Visibility.Visible;
            Progress.Value = pct;
            UpdateStatus();
            ApplyLock();
        }
        else
        {
            UploadBtn.Content = Terms.Upload;
            ReadBtn.Content = Terms.ReadDevice;
            UploadBtn.ClearValue(MinWidthProperty);
            ReadBtn.ClearValue(MinWidthProperty);
            Progress.Visibility = Visibility.Collapsed;
            Refresh();
        }
    }

    /// <summary>편집 잠금 (첫 사용 점검 5번) — 업로드 · 읽기 동안 화면 내용이 바뀌지 않게. 흐리게 = 불투명도 0.45 · 단추는 Fluent 비활성 색.</summary>
    private void ApplyLock()
    {
        bool on = !_busy;
        foreach (var el in new UIElement[] { LayerTabs, Device, LedRow })
        {
            el.IsEnabled = on;
            el.Opacity = on ? 1 : LockedOpacity;
        }
        ProfileBtn.IsEnabled = on;
        UndoBtn.IsEnabled = on;
        LoadPcBtn.IsEnabled = on;
    }

    private async Task UploadAsync()
    {
        if (_busy) return;
        HideInfo();
        SetBusy("업로드 중", 0);
        OpOutcome o;
        try
        {
            o = await _ctrl.UploadAsync(
                () => Dialogs.Confirm(this, "키보드 설정을 읽지 못함",
                    "업로드 전에 키보드의 지금 설정을 읽어 이 화면에 없는 키를 지키는데, 읽지 못했음.\n이대로 업로드하면 이 화면에서 비어 있는 키는 키보드에서도 지워질 수 있음.",
                    "그래도 업로드"),
                new Progress<int>(p => { if (_busy) SetBusy("업로드 중", p); }));
        }
        finally { SetBusy(null, 0); }
        if (o.Ok) ShowUploaded(o); else ShowFailure(Terms.Upload, o, UploadAsync);
    }

    private async Task ReadAsync()
    {
        if (_busy) return;
        HideInfo();
        SetBusy("읽는 중", 0);
        OpOutcome o;
        try { o = await _ctrl.ReadFromDeviceAsync(new Progress<int>(p => { if (_busy) SetBusy("읽는 중", p); })); }
        finally { SetBusy(null, 0); }
        if (o.Ok) ShowInfo("ok", "키보드에서 읽음", $"{o.At:HH:mm} · {o.Total}키 · PC 파일은 그대로(되돌리려면 「PC 저장값 불러오기」)");
        else ShowFailure(Terms.ReadDevice, o, ReadAsync);
    }

    // ───────────────────────── 메뉴 (⋯ · 프로필 목록)

    private void OpenMenu(ContextMenu menu, FrameworkElement target, bool alignRight)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = (popup, targetSize, offset) => new[]
        {
            new CustomPopupPlacement(new Point(alignRight ? targetSize.Width - popup.Width + 12 : -12, targetSize.Height - 4), PopupPrimaryAxis.Horizontal),
        };
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, string? icon, Action onClick, bool critical = false)
    {
        // 프로필 이름의 「_」가 바로 가기 표지로 사라지지 않게 두 번 적음
        var it = new MenuItem { Header = header.Replace("_", "__"), Tag = icon };
        if (critical) it.SetResourceReference(StyleProperty, "CriticalMenuItem");
        it.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception ex) { Log.Error(header, ex); Dialogs.Error(Application.Current?.MainWindow, header, ex); }
        };
        return it;
    }

    /// <summary>
    /// ⋯ 메뉴 (모형 keyset-b · 판정 S13 · S15 · A1 · M1). 대상을 이름에(첫 사용 점검 9번): 「다른 레이어로 복사…」 · 「레이어 3 비우기…」.
    /// 업로드 · 읽기 중에는 화면 내용을 바꾸는 줄만 잠김(5번) — 로그 · 테마 · 정보는 그대로.
    /// </summary>
    public ContextMenu BuildMoreMenu()
    {
        var m = new ContextMenu { MinWidth = 248 };
        MenuItem Edit(MenuItem it) { it.IsEnabled = !_busy; return it; }
        m.Items.Add(Item(Terms.LogView, null, () => _shell?.ShowLog()));
        m.Items.Add(new Separator());
        m.Items.Add(Edit(Item("키 · 노브 개수…", "knob", () =>
        {
            var r = Dialogs.Counts(this, _ctrl.Profile.KeyCount, _ctrl.Profile.KnobCount);
            if (r is { } v) _ctrl.SetCounts(v.Keys, v.Knobs);
        })));
        m.Items.Add(Edit(Item("다른 레이어로 복사…", "layer", () =>
        {
            if (Dialogs.LayerTarget(this, _layer) is int to) { _ctrl.CopyLayer(_layer, to); ShowInfo("ok", $"레이어 {_layer + 1} → 레이어 {to + 1} 복사함", "실행 취소로 되돌림"); }
        })));
        m.Items.Add(Edit(Item($"레이어 {_layer + 1} 비우기…", "layer", () =>
        {
            if (Dialogs.Confirm(this, $"레이어 {_layer + 1} 비우기", $"레이어 {_layer + 1} 의 키를 모두 비움. 업로드하면 키보드에서도 비워짐.\n실행 취소로 되돌릴 수 있음.", "비우기"))
                _ctrl.ClearLayer(_layer);
        })));
        m.Items.Add(new Separator());
        m.Items.Add(Edit(Item(Terms.ImportProfile, "pc-load", () => _shell?.ImportProfile(this))));
        m.Items.Add(Item(Terms.BackupAll, "copy", () => _shell?.BackupAll(this)));
        m.Items.Add(Edit(Item(Terms.RestoreBackup, "undo", () => _shell?.RestoreBackup(this))));
        m.Items.Add(new Separator());
        m.Items.Add(BuildThemeItem());
        m.Items.Add(Item(Terms.Update, "upload", () => _shell?.ShowAbout(true)));
        m.Items.Add(Item(Terms.About, "info", () => _shell?.ShowAbout(false)));
        return m;
    }

    /// <summary>테마: 시스템(기본) · 라이트 · 다크 — 바꾸면 바로 반영 · 저장 (S15)</summary>
    public MenuItem BuildThemeItem()
    {
        var theme = new MenuItem { Header = Terms.Theme, Tag = "theme" };
        foreach (var ti in BuildThemeChoices()) theme.Items.Add(ti);
        return theme;
    }

    public List<MenuItem> BuildThemeChoices()
    {
        var list = new List<MenuItem>();
        foreach (var (mode, label) in new[] { (ThemeManager.System, Terms.ThemeSystem), (ThemeManager.Light, Terms.ThemeLight), (ThemeManager.Dark, Terms.ThemeDark) })
        {
            var mm = mode;
            var it = Item(label, "radio", () =>
            {
                ThemeManager.SetMode(mm);
                _ctrl.Settings.Theme = mm;
                _ctrl.Settings.Save();
                Log.Write($"테마: {label}");
            });
            it.IsChecked = ThemeManager.Mode == mm;
            list.Add(it);
        }
        return list;
    }

    /// <summary>프로필 목록 (모형 keyset-e · 판정 S9 · S13): 고르면 그 프로필을 화면에 엶. 관리 줄엔 지금 프로필 이름(첫 사용 점검 9번).</summary>
    public ContextMenu BuildProfileMenu()
    {
        var m = new ContextMenu { MinWidth = 232 };
        var curName = _ctrl.ProfileName;
        foreach (var f in _ctrl.Profiles.Files)
        {
            var path = f.FullName;
            var name = Path.GetFileNameWithoutExtension(f.Name);
            var it = Item(name, null, () => { if (!string.Equals(path, _ctrl.ProfilePath, StringComparison.OrdinalIgnoreCase)) _ctrl.OpenProfile(path); });
            it.IsChecked = string.Equals(path, _ctrl.ProfilePath, StringComparison.OrdinalIgnoreCase);
            m.Items.Add(it);
        }
        m.Items.Add(new Separator());
        m.Items.Add(Item("새 프로필…", "plus", () =>
        {
            var n = Dialogs.Input(this, "새 프로필", "이름", ProfileNames.Unique("새 프로필", _ctrl.Profiles.Names),
                s => ProfileNames.Problem(s, _ctrl.Profiles.Names), "만들기", "빈 프로필을 만들어 화면에 엶. 키보드에는 업로드해야 들어감.");
            if (n != null) _ctrl.CreateProfile(n);
        }));
        m.Items.Add(Item($"{curName} 이름 바꾸기…", null, () =>
        {
            var cur = _ctrl.ProfileName;
            var n = Dialogs.Input(this, "이름 바꾸기", "이름", cur, s => ProfileNames.Problem(s, _ctrl.Profiles.Names, except: cur), "바꾸기");
            if (n != null && n != cur) _ctrl.RenameCurrent(n);
        }));
        m.Items.Add(Item($"{curName} 복제", "copy", () => _ctrl.DuplicateCurrent()));
        m.Items.Add(Item($"{curName} 지우기…", "trash", () =>
        {
            var path = _ctrl.ProfilePath;
            if (path == null) return;
            if (_ctrl.Profiles.Files.Count <= 1) { Dialogs.Confirm(this, "지울 수 없음", "프로필이 하나뿐이라 지울 수 없음.", "확인", "닫기"); return; }
            if (Dialogs.Confirm(this, $"「{_ctrl.ProfileName}」 지우기", "PC 에서 이 프로필 파일을 지움 — 키보드에 업로드한 설정은 그대로.\n전체 백업이 있으면 「백업에서 복원…」으로 되살릴 수 있음.", "지우기"))
                _ctrl.DeleteProfile(path);
        }, critical: true));
        return m;
    }
}
