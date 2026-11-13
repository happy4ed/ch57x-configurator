using System.Windows;
using System.Windows.Threading;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// 권한 질문 받기 (PC 앱 0.3.1) — 파이프 서버(Core) + 대기열(Core) + 팝업 + 전역 키.
///   - 한 번에 한 건 · request_id 로만 답 · close 오면 닫기 · 시간(deadline)이 지나면 닫고 답 안 함.
///   - F19 · F20 · F21 은 팝업이 떠 있는 동안만 등록(MOD_NOREPEAT) · 닫히면 바로 해제 · 못 잡으면 단추만 + 알림 한 줄.
///   - 보이는 질문이 바뀐 뒤 1초 동안은 키 · 단추 무시(확장 permission.ts 와 같은 규칙 · 대기열이 판정).
///   - 닫기 단추 = 터미널에서 답: none 을 보내고 팝업을 닫는다(확장 0.3.2 가 결정 없음으로 바로 끝냄 → 터미널 권한 창).
///   - Codex 는 Yes · No 둘 — 패드 8 도 Yes(allow · 훅이 세션 허용을 못 줌 · 근거 P-2).
///   - 설정: 「권한 질문 팝업 받기」 켜기/끄기(기본 켜기 · 끄면 파이프를 닫아 확장이 VS Code 알림으로) · 위치(오른쪽 아래 / 오른쪽 위).
/// 로그에는 명령 원문을 적지 않는다(CLI · 도구 이름 · request_id 앞 8자 · 결과만).
/// </summary>
public sealed class PadPopupController : IDisposable
{
    public const string CornerBottomRight = "bottom-right";
    public const string CornerTopRight = "top-right";

    public PermissionQueue Queue { get; } = new();
    private readonly PadPipeServer _server;
    private readonly AppSettings _settings;
    private readonly Action<string> _log;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private PermissionPopup? _popup;
    private string? _shownId;
    private PadCli _shownCli;

    /// <summary>마지막 Start 결과 (트레이 메뉴 도움말)</summary>
    public PadServerStart? LastStart { get; private set; }
    public event Action? StateChanged;
    /// <summary>새 질문이 보이기 시작함 (시험 모드 자동 조작)</summary>
    public event Action<PadView, PermissionPopup>? Shown;

    public PadPopupController(AppSettings settings, string? pipeName = null, Action<string>? log = null)
    {
        _settings = settings;
        _log = log ?? Log.Write;
        _server = new PadPipeServer(pipeName ?? PadPipeServer.DefaultName(), Queue, _log);
        Queue.Changed += () => Application.Current?.Dispatcher.BeginInvoke(Refresh);
        Queue.Done += (r, d, why) => _log(DoneText(r, d, why));
        _tick.Tick += (_, _) => OnTick();
    }

    public string PipeName => _server.PipeName;
    public bool Enabled => _settings.PadPopup;
    public bool Listening => _server.Running;
    public string Corner => _settings.PadPopupCorner == CornerTopRight ? CornerTopRight : CornerBottomRight;

    public PadServerStart? Start()
    {
        if (!Enabled) { _log("권한 질문 팝업 받기: 꺼짐"); return null; }
        LastStart = _server.Start();
        StateChanged?.Invoke();
        return LastStart;
    }

    public void SetEnabled(bool on)
    {
        _settings.PadPopup = on;
        _settings.Save();
        if (on) { LastStart = _server.Start(); }
        else
        {
            _server.Stop();
            Queue.Clear();
            LastStart = null;
        }
        _log($"권한 질문 팝업 받기: {(on ? "켬" : "끔")}");
        StateChanged?.Invoke();
    }

    public void SetCorner(string corner)
    {
        _settings.PadPopupCorner = corner == CornerTopRight ? CornerTopRight : CornerBottomRight;
        _settings.Save();
        if (_popup != null) { _popup.Corner = Corner; _popup.Place(); }
        StateChanged?.Invoke();
    }

    // ───────────────────────── 보이기 · 숨기기

    private PermissionPopup Popup()
    {
        if (_popup != null) return _popup;
        var p = new PermissionPopup { Corner = Corner };
        p.Clicked += (id, d) => Answer(id, d, d == PadDecision.None ? "닫기 단추" : "단추");
        // 패드 키 → 답: Codex 는 8 도 Yes (Core KeyDecision)
        p.HotKey += key => Answer(_shownId, PadProtocol.KeyDecision(_shownCli, key), "패드 " + PadKey(key));
        // 그림자 여백까지 크기를 알아야 자리를 잡으므로 처음엔 화면 밖에서 핸들만
        p.Left = -10000; p.Top = -10000;
        new System.Windows.Interop.WindowInteropHelper(p).EnsureHandle();
        _popup = p;
        return p;
    }

    private void Refresh()
    {
        var head = Queue.Head;
        if (head == null) { HidePopup(); return; }
        var p = Popup();
        bool isNew = head.Request.RequestId != _shownId;
        _shownId = head.Request.RequestId;
        _shownCli = head.Request.Cli;
        p.ShowView(head, PadProtocol.NowMs());
        if (!p.IsVisible)
        {
            p.Show();   // ShowActivated=False — 포커스를 가져가지 않음
            p.Place();
        }
        if (!p.KeysRegistered)
        {
            bool ok = p.RegisterKeys(out var why);
            p.KeysBusy = !ok;
            _log(ok ? "패드 키 F19 · F20 · F21 잡음 (팝업 동안)" : $"패드 키를 못 잡음 — 단추로만: {why}");
        }
        if (!_tick.IsEnabled) _tick.Start();
        if (isNew)
        {
            var r = head.Request;
            _log($"권한 질문 보임 · {PadProtocol.CliName(r.Cli)} · {PadProtocol.Plain(r.Tool)} · {PadPipeServer.Short(r.RequestId)} · 대기 {head.Waiting}건");
            Shown?.Invoke(head, p);
        }
    }

    private void HidePopup()
    {
        _tick.Stop();
        _shownId = null;
        if (_popup == null) return;
        _popup.UnregisterKeys();   // 닫히면 바로 해제
        _popup.Forget();
        _popup.Hide();
    }

    private void OnTick()
    {
        if (Queue.Tick() > 0) return;   // Changed → Refresh
        _popup?.UpdateTime(PadProtocol.NowMs());
    }

    /// <summary>답 — 보이는 질문의 request_id 로만 · 1초 안이면 무시하고 알림</summary>
    private void Answer(string? id, PadDecision d, string how)
    {
        if (id == null) return;
        var r = Queue.Answer(id, d);
        if (r == PadAnswer.Fresh)
        {
            _popup?.ShowNotice(PermissionPopup.FreshNotice);
            _log($"권한 질문 {PadPipeServer.Short(id)} — 바뀐 지 1초 안이라 {how} {Label(d)} 무시");
        }
        else if (r == PadAnswer.Stale) _log($"권한 질문 {PadPipeServer.Short(id)} — 이미 끝남 · {how} {Label(d)} 안 보냄");
        else _log($"권한 질문 {PadPipeServer.Short(id)} 답 {Label(d)} ({how})");
    }

    public static string Label(PadDecision d) => d switch
    {
        PadDecision.Allow => "Yes",
        PadDecision.AllowAlways => "Yes all",
        PadDecision.Deny => "No",
        _ => "터미널에서 답",
    };
    /// <summary>패드 키(F19 · F20 · F21 = Allow · AllowAlways · Deny 로 옴) → 패드 숫자</summary>
    private static string PadKey(PadDecision key) => key switch { PadDecision.Allow => "7", PadDecision.AllowAlways => "8", _ => "9" };

    private static string DoneText(PadRequest r, PadDecision? d, string why) => why switch
    {
        "answer" when d == PadDecision.None => $"권한 질문 {PadPipeServer.Short(r.RequestId)} 끝 — 터미널에서 답(none) 보냄 · 팝업 닫음",
        "answer" => $"권한 질문 {PadPipeServer.Short(r.RequestId)} 끝 — {Label(d!.Value)} 보냄",
        "close" => $"권한 질문 {PadPipeServer.Short(r.RequestId)} 닫힘 — 다른 곳에서 답함(확장 close)",
        "timeout" => $"권한 질문 {PadPipeServer.Short(r.RequestId)} 시간 초과 — 닫음 · 답 안 함",
        "drop" => $"권한 질문 {PadPipeServer.Short(r.RequestId)} 닫힘 — 연결 끊김",
        _ => $"권한 질문 {PadPipeServer.Short(r.RequestId)} 닫힘 — 받기 끔",
    };

    public void Dispose()
    {
        _server.Stop();
        Queue.Clear();
        try { _popup?.UnregisterKeys(); _popup?.Close(); } catch { }
        _popup = null;
    }
}
