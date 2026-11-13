using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// 권한 질문 팝업 — 항상 위 · 포커스 안 뺏음 · 작업 표시줄 없음. 보이는 질문 하나를 그리고 단추 · 패드 키(F19~F21)를 받는다.
/// 무엇을 띄우고 언제 닫는지는 PadPopupController 가 정한다(이 창은 그리기 · 입력만).
/// </summary>
public partial class PermissionPopup : Window
{
    /// <summary>바뀐 지 1초 안에 누름 (첫 사용 점검 중 10)</summary>
    public const string FreshNotice = "방금 누른 키는 무시함 · 바뀐 질문 보고 다시 누르기";
    /// <summary>F19~F21 을 다른 프로그램이 먼저 등록함 — 상태 문장(링크처럼 보이지 않게 · 점검 중 11). 「수 있음」 은 줄바꿈 없는 공백(렌더 실측 「답할 수 / 있음」)</summary>
    public const string KeysBusyNotice = "패드 키를 다른 프로그램이 씀 — 마우스로만 답할 수\u00A0있음";

    /// <summary>단추로 답함 (그때 그려져 있던 request_id 와 함께 — 그 사이 바뀌었으면 대기열이 버린다). 닫기 단추 = None(터미널에서 답)</summary>
    public event Action<string, PadDecision>? Clicked;
    /// <summary>F19 · F20 · F21 = 패드 7 · 8 · 9 (팝업이 떠 있는 동안만 등록) — 키 그대로(Allow · AllowAlways · Deny). Codex 의 8 을 Yes 로 바꾸는 것은 컨트롤러가.</summary>
    public event Action<PadDecision>? HotKey;

    private string? _id;
    private PadView? _view;
    private PadRequest? _shown;
    private bool _keysBusy;
    private string _rawCommand = "";
    private readonly System.Windows.Threading.DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly System.Windows.Threading.DispatcherTimer _flashTimer = new();
    private readonly List<Border> _flashed = new();

    public PermissionPopup()
    {
        InitializeComponent();
        BtnYes.Click += (_, _) => { if (_id != null) Clicked?.Invoke(_id, PadDecision.Allow); };
        BtnYesAll.Click += (_, _) => { if (_id != null) Clicked?.Invoke(_id, PadDecision.AllowAlways); };
        BtnNo.Click += (_, _) => { if (_id != null) Clicked?.Invoke(_id, PadDecision.Deny); };
        BtnTerminal.Click += (_, _) => { if (_id != null) Clicked?.Invoke(_id, PadDecision.None); };
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); Notice.Text = ""; Notice.Visibility = Visibility.Collapsed; UpdateFoot(); };
        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); Unflash(); };
        SizeChanged += (_, _) => Place();
        // 명령 칸 폭을 안 뒤에 연결 기호 앞에서 줄을 나눈다(폭은 창 폭으로 정해짐 — 글이 바뀌어도 폭은 그대로라 되돌이 없음)
        Command.SizeChanged += (_, e) => { if (e.WidthChanged) BreakCommand(); };
    }

    /// <summary>시험 기록용 — 지금 그려진 칸 글</summary>
    public string TimeText => TimeLeft.Text;
    public string WaitText => Waiting.Visibility == Visibility.Visible ? Waiting.Text : "";
    public string NoticeText => Notice.Visibility == Visibility.Visible ? Notice.Text : "";
    public string CommandText => Command.Text;
    /// <summary>보이는 답 단추 이름 (「Yes·Yes all·No」 · Codex 「Yes·No」)</summary>
    public string ButtonsText => string.Join("·", new[] { (BtnYes, "Yes"), (BtnYesAll, "Yes all"), (BtnNo, "No") }.Where(b => b.Item1.Visibility == Visibility.Visible).Select(b => b.Item2));

    /// <summary>시험 · 렌더용 — 답 단추 · 닫기(None)</summary>
    public Button ButtonFor(PadDecision d) => d switch
    {
        PadDecision.Allow => BtnYes,
        PadDecision.AllowAlways => BtnYesAll,
        PadDecision.Deny => BtnNo,
        _ => BtnTerminal,
    };

    /// <summary>위치: bottom-right(기본) · top-right</summary>
    public string Corner { get; set; } = PadPopupController.CornerBottomRight;

    /// <summary>보이는 질문을 그린다 (같은 질문이면 남은 시간 · 대기 수만). 앞에 다른 질문이 떠 있었으면 바뀐 칸을 잠깐 강조.</summary>
    public void ShowView(PadView v, long now)
    {
        var r = v.Request;
        if (r.RequestId != _id)
        {
            var before = _shown;
            _id = r.RequestId;
            _shown = r;
            Notice.Text = "";
            Notice.Visibility = Visibility.Collapsed;
            _noticeTimer.Stop();
            bool codex = r.Cli == PadCli.Codex;
            Heading.Text = $"{PadProtocol.CliName(r.Cli)} 권한 질문";

            var tool = PadProtocol.Plain(r.Tool);
            Tool.Text = tool.Length > 0 ? PadProtocol.KeepWords(PadText.ToolLabel(tool)) : "(이름 없음)";
            CommandLabel.Text = PadText.SubjectLabel(tool);
            _rawCommand = PadProtocol.Plain(r.Summary);
            CommandLabel.Visibility = CommandBox.Visibility = _rawCommand.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            BreakCommand();

            var where = PadProtocol.Plain(r.Where);
            Where.Text = PadProtocol.KeepWords(PadText.KeepParts(PadText.FolderFirst(where)));
            WhereLabel.Visibility = WhereBd.Visibility = where.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Codex: 훅이 세션 허용을 못 줌(근거 P-2) → Yes · No 둘 · 패드 8 도 Yes · 범위 글 없음
            BtnYesAll.Visibility = codex ? Visibility.Collapsed : Visibility.Visible;
            Buttons.Columns = codex ? 2 : 3;
            KeyYes.Text = codex ? "7 · 8" : "7";
            System.Windows.Automation.AutomationProperties.SetName(BtnYes, codex ? "Yes, 패드 7 · 8" : "Yes, 패드 7");
            var yesAll = codex ? "" : PadProtocol.Plain(r.YesAll);
            YesAllNote.Text = yesAll.Length > 0 ? PadProtocol.KeepWords($"Yes all 누르면 {yesAll}") : "";
            YesAllBd.Visibility = yesAll.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateFoot();

            // 바뀐 칸 잠깐 강조 (앞 질문이 보이던 채로 다음 질문으로 넘어간 때만)
            Unflash();
            if (before != null)
            {
                var changed = new List<Border>();
                if (before.Cli != r.Cli) changed.Add(HeadingBd);
                if (before.Tool != r.Tool) changed.Add(ToolBd);
                if (before.Summary != r.Summary) changed.Add(CommandBox);
                if (before.Where != r.Where) changed.Add(WhereBd);
                if (!codex && before.YesAll != r.YesAll) changed.Add(YesAllBd);
                Flash(changed, PermissionQueue.KeyGuardMs + 500);
            }
        }
        _view = v;
        UpdateTime(now);
    }

    public void UpdateTime(long now)
    {
        if (_view == null) return;
        TimeLeft.Text = PadProtocol.KeepWords(PadText.TimeLeft(_view.Request.Cli, _view.SecondsLeft(now)));
        var w = PadText.Waiting(_view.Waiting);
        Waiting.Text = PadProtocol.KeepWords(w);
        WaitingLabel.Visibility = Waiting.Visibility = w.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>알림 한 줄 (3초 · 단추 바로 아래) — 그동안 바뀐 칸 강조도 같이 둔다</summary>
    public void ShowNotice(string text)
    {
        Notice.Text = PadProtocol.KeepWords(text);
        Notice.Visibility = Visibility.Visible;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        if (_flashed.Count > 0) { _flashTimer.Stop(); _flashTimer.Interval = _noticeTimer.Interval; _flashTimer.Start(); }
        UpdateFoot();
    }

    /// <summary>패드 키를 못 잡음(다른 프로그램이 이미 등록) — 마우스로만 · 단추 안 숫자도 숨김</summary>
    public bool KeysBusy
    {
        get => _keysBusy;
        set
        {
            _keysBusy = value;
            KeysNote.Text = value ? PadProtocol.KeepWords(KeysBusyNotice) : "";
            KeysNote.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            var keys = value ? Visibility.Collapsed : Visibility.Visible;
            KeyYes.Visibility = KeyYesAll.Visibility = KeyNo.Visibility = keys;
            UpdateFoot();
        }
    }

    private void UpdateFoot() =>
        Foot.Visibility = Notice.Visibility == Visibility.Visible || YesAllBd.Visibility == Visibility.Visible || KeysNote.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;

    public void Forget()
    {
        _id = null;
        _view = null;
        _shown = null;
        Notice.Text = "";
        Notice.Visibility = Visibility.Collapsed;
        _noticeTimer.Stop();
        _flashTimer.Stop();
        Unflash();
    }

    // ───────────────────────── 명령 칸 줄바꿈 (공백보다 && · || · | 앞 · ; 뒤 먼저 — 점검 하 16)

    private void BreakCommand()
    {
        double avail = Command.ActualWidth;
        if (avail <= 0 || _rawCommand.Length == 0) { Command.Text = _rawCommand; return; }
        var tf = new Typeface(Command.FontFamily, Command.FontStyle, Command.FontWeight, Command.FontStretch);
        double ppd = VisualTreeHelper.GetDpi(Command).PixelsPerDip;
        bool Fits(string s) =>
            new FormattedText(s, System.Globalization.CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, tf, Command.FontSize, Brushes.Black, ppd)
                .WidthIncludingTrailingWhitespace <= avail - 1;
        var text = PadText.BreakAtConnectors(_rawCommand, Fits);
        if (Command.Text != text) Command.Text = text;
    }

    // ───────────────────────── 바뀐 칸 강조 (강조색 옅게 · 시간이 지나면 원래 배경)

    private void Flash(List<Border> cells, int ms)
    {
        if (cells.Count == 0) return;
        var accent = TryFindResource("Accent") as SolidColorBrush;
        var tint = new SolidColorBrush(accent?.Color ?? Colors.SteelBlue) { Opacity = 0.22 };
        tint.Freeze();
        foreach (var b in cells) { b.Background = tint; _flashed.Add(b); }
        _flashTimer.Stop();
        _flashTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _flashTimer.Start();
    }

    private void Unflash()
    {
        foreach (var b in _flashed)
        {
            if (b == CommandBox) b.SetResourceReference(Border.BackgroundProperty, "AltFill");
            else b.ClearValue(Border.BackgroundProperty);
        }
        _flashed.Clear();
    }

    /// <summary>렌더용 — 바뀐 칸 강조가 있나</summary>
    public int FlashedCount => _flashed.Count;

    // ───────────────────────── 자리 (작업 영역 오른쪽 아래 · 위 — 그림자 여백까지 창 크기)

    public void Place()
    {
        if (AppMode.Render || ActualHeight <= 0) return;
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth;
        Top = Corner == PadPopupController.CornerTopRight ? wa.Top : wa.Bottom - ActualHeight;
    }

    // ───────────────────────── 포커스 안 뺏기 · 전역 키

    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotKeyBase = 0x5731;
    /// <summary>F19 · F20 · F21 = 패드 7 · 8 · 9 (확장 signals 와 같은 신호)</summary>
    private static readonly (uint Vk, PadDecision D)[] Keys = { (0x82, PadDecision.Allow), (0x83, PadDecision.AllowAlways), (0x84, PadDecision.Deny) };

    private IntPtr _hwnd;
    private bool _registered;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (AppMode.Render) return;
        _hwnd = new WindowInteropHelper(this).Handle;
        // 눌러도 앞으로 오지 않게(단추를 눌러도 VS Code 에 입력 자리가 그대로) · Alt+Tab 목록에 없게
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32() - HotKeyBase;
            if (id >= 0 && id < Keys.Length) { handled = true; HotKey?.Invoke(Keys[id].D); }
        }
        return IntPtr.Zero;
    }

    /// <summary>F19~F21 등록 — 셋 다 잡히면 true. 하나라도 다른 프로그램이 잡았으면 모두 풀고 false(단추만)</summary>
    public bool RegisterKeys(out string detail)
    {
        detail = "";
        if (_registered) return true;
        if (_hwnd == IntPtr.Zero) { detail = "창 핸들 없음"; return false; }
        var ok = new List<int>();
        for (int i = 0; i < Keys.Length; i++)
        {
            if (RegisterHotKey(_hwnd, HotKeyBase + i, MOD_NOREPEAT, Keys[i].Vk)) { ok.Add(i); continue; }
            int err = Marshal.GetLastWin32Error();
            foreach (var j in ok) UnregisterHotKey(_hwnd, HotKeyBase + j);
            detail = $"F{19 + i} 등록 실패 (Win32 {err}{(err == 1409 ? " — 다른 프로그램이 이미 씀" : "")})";
            return false;
        }
        _registered = true;
        return true;
    }

    public void UnregisterKeys()
    {
        if (!_registered || _hwnd == IntPtr.Zero) return;
        for (int i = 0; i < Keys.Length; i++) UnregisterHotKey(_hwnd, HotKeyBase + i);
        _registered = false;
    }

    public bool KeysRegistered => _registered;
}
