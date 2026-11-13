using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// 권한 질문 팝업 — 항상 위 · 포커스 안 뺏음 · 작업 표시줄 없음. 보이는 질문 하나를 그리고 단추 · 패드 키(F19~F21)를 받는다.
/// 무엇을 띄우고 언제 닫는지는 PadPopupController 가 정한다(이 창은 그리기 · 입력만).
/// </summary>
public partial class PermissionPopup : Window
{
    public const string FreshNotice = "새 질문이 떴습니다 — 보고 다시 누르세요";
    public const string KeysBusyNotice = "패드 키를 다른 프로그램이 씀 — 단추로 답하기";

    /// <summary>단추로 답함 (그때 그려져 있던 request_id 와 함께 — 그 사이 바뀌었으면 대기열이 버린다)</summary>
    public event Action<string, PadDecision>? Clicked;
    /// <summary>F19 · F20 · F21 (팝업이 떠 있는 동안만 등록)</summary>
    public event Action<PadDecision>? HotKey;

    private string? _id;
    private PadView? _view;
    private bool _keysBusy;
    private readonly System.Windows.Threading.DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? _notice;

    public PermissionPopup()
    {
        InitializeComponent();
        BtnYes.Click += (_, _) => { if (_id != null) Clicked?.Invoke(_id, PadDecision.Allow); };
        BtnYesAll.Click += (_, _) => { if (_id != null) Clicked?.Invoke(_id, PadDecision.AllowAlways); };
        BtnNo.Click += (_, _) => { if (_id != null) Clicked?.Invoke(_id, PadDecision.Deny); };
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); _notice = null; UpdateHint(); };
        SizeChanged += (_, _) => Place();
    }

    /// <summary>시험 기록용 — 지금 그려진 남은 시간 칸 · 안내 줄 글</summary>
    public string TimeText => TimeLeft.Text;
    public string HintText => Hint.Text;

    /// <summary>시험 · 렌더용 — 단추 셋</summary>
    public Button ButtonFor(PadDecision d) => d switch { PadDecision.Allow => BtnYes, PadDecision.AllowAlways => BtnYesAll, _ => BtnNo };

    /// <summary>위치: bottom-right(기본) · top-right</summary>
    public string Corner { get; set; } = PadPopupController.CornerBottomRight;

    /// <summary>보이는 질문을 그린다 (같은 질문이면 남은 시간 · 대기 수만)</summary>
    public void ShowView(PadView v, long now)
    {
        var r = v.Request;
        if (r.RequestId != _id)
        {
            _id = r.RequestId;
            _notice = null;
            _noticeTimer.Stop();
            Heading.Text = $"{PadProtocol.CliName(r.Cli)} 권한 질문";
            var cmd = PadProtocol.Plain(r.Summary);
            Command.Text = cmd;
            CommandBox.Visibility = cmd.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var tool = PadProtocol.Plain(r.Tool);
            Tool.Text = tool.Length > 0 ? tool : "(이름 없음)";
            var where = PadProtocol.Plain(r.Where);
            Where.Text = where;
            WhereLabel.Visibility = Where.Visibility = where.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // Codex 는 세션 허용이 없어 Yes all = 이번만 허용 (훅이 allow 로 바꿈 — 근거 P-2)
            var yesAll = r.Cli == PadCli.Codex ? (r.YesAll.Length > 0 ? PadProtocol.Plain(r.YesAll) : "Codex 는 이번만 허용") : PadProtocol.Plain(r.YesAll);
            YesAll.Text = yesAll;
            YesAllLabel.Visibility = YesAll.Visibility = yesAll.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        _view = v;
        UpdateTime(now);
        UpdateHint();
    }

    public void UpdateTime(long now)
    {
        if (_view == null) return;
        TimeLeft.Text = _view.Waiting > 0 ? $"{_view.SecondsLeft(now)}초 · 대기 {_view.Waiting}건" : $"{_view.SecondsLeft(now)}초";
    }

    /// <summary>알림 한 줄 (3초) — 패드 키 안내 자리에</summary>
    public void Notice(string text)
    {
        _notice = text;
        _noticeTimer.Stop();
        _noticeTimer.Start();
        UpdateHint();
    }

    /// <summary>패드 키를 못 잡음(다른 프로그램이 이미 등록) — 단추로만</summary>
    public bool KeysBusy
    {
        get => _keysBusy;
        set { _keysBusy = value; UpdateHint(); }
    }

    private void UpdateHint()
    {
        if (_notice != null) { Hint.Text = _notice; Hint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "T1"); Hint.FontWeight = FontWeights.SemiBold; return; }
        Hint.FontWeight = FontWeights.Normal;
        Hint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "T2");
        Hint.Text = _keysBusy ? KeysBusyNotice : "패드 7 Yes · 8 Yes all · 9 No";
    }

    public void Forget()
    {
        _id = null;
        _view = null;
        _notice = null;
        _noticeTimer.Stop();
    }

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
