using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// 파이프 왕복 시험 모드 — <c>CH57x 설정기.exe --pipe-test &lt;폴더&gt;</c> (CI 전용).
/// 진짜 파이프 서버 · 대기열 · 팝업 · 전역 키를 그대로 띄우고, 질문의 tool 칸에 적힌 각본대로 조작한다:
///   test:key:F19@1200      1200ms 뒤 F19 를 진짜 키 입력으로(SendInput) → RegisterHotKey 로 받는지
///   test:click:deny@1200   1200ms 뒤 No 단추를 누름(UI 자동화 Invoke) — allow · always · deny · none(닫기 = 터미널에서 답)
///   test:off@300,on@2500   300ms 뒤 받기 끔 · 2500ms 뒤 켬
///   test:wait              아무것도 안 함(close · 시간 초과 · 끊김 시험)
///   test:quit@100          끝냄
/// 장치 · 설정 파일 · 자동실행을 건드리지 않는다. 기록 = 폴더의 pipe-test.log (한 줄씩 바로 씀 — node 시험이 읽는다).
/// 종료 코드: 0 끝냄 · 3 파이프 이름을 다른 프로그램이 먼저 엶(FirstPipeInstance 시험) · 4 안전 시간(180초) 초과 · 5 파이프 열기 오류.
/// </summary>
public static class PipeTest
{
    private static string _logPath = "";
    private static readonly object Gate = new();

    public static void Note(string s)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {s}{Environment.NewLine}";
        lock (Gate) { try { File.AppendAllText(_logPath, line); } catch { } }
    }

    public static void Run(Application app, string dir)
    {
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "pipe-test.log");
        try { File.Delete(_logPath); } catch { }
        Note($"시작 · {Terms.AppName} {ErrorReport.Version()} · OS {Environment.OSVersion} · 사용자 {PadPipeServer.CurrentUser()} · USERNAME {Environment.GetEnvironmentVariable("USERNAME")}");
        var settings = new AppSettings();   // 저장 안 함 (AppMode.PipeTest)
        var pad = new PadPopupController(settings, log: Note);
        Note($"파이프 이름 {pad.PipeName}");
        pad.Shown += (v, popup) => Script(pad, v, popup, app);
        var start = pad.Start();
        Note($"Start = {start}");
        if (start == PadServerStart.NameTaken) { Note("끝 — 이름 선점됨 (코드 3)"); app.Shutdown(3); return; }
        if (start != PadServerStart.Ok) { Note("끝 — 파이프 오류 (코드 5)"); app.Shutdown(5); return; }
        Note("준비됨");
        var safety = new DispatcherTimer { Interval = TimeSpan.FromSeconds(180) };
        safety.Tick += (_, _) => { Note("끝 — 안전 시간 초과 (코드 4)"); pad.Dispose(); app.Shutdown(4); };
        safety.Start();
    }

    private static void Script(PadPopupController pad, PadView v, PermissionPopup popup, Application app)
    {
        var tool = v.Request.Tool;
        Note($"보임 {v.Request.RequestId} tool={tool} 대기={v.Waiting} 단추={popup.ButtonsText} 키={(popup.KeysRegistered ? "잡음" : "못 잡음")} 포커스={(popup.IsActive ? "팝업이 가져감" : "안 가져감")}");
        if (!tool.StartsWith("test:", StringComparison.Ordinal)) return;
        var id = v.Request.RequestId;
        foreach (var step in tool[5..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = step.LastIndexOf('@');
            int ms = at > 0 && int.TryParse(step[(at + 1)..], out var n) ? n : 0;
            var what = at > 0 ? step[..at] : step;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                Do(pad, popup, app, id, what);
            };
            t.Start();
        }
    }

    private static void Do(PadPopupController pad, PermissionPopup popup, Application app, string id, string what)
    {
        var parts = what.Split(':');
        Note($"{id} 각본 {what} · 남은 시간 칸 「{Shown(popup.TimeText)}」 · 대기 칸 「{Shown(popup.WaitText)}」 · 알림 「{Shown(popup.NoticeText)}」 · 보임={popup.IsVisible}");
        switch (parts[0])
        {
            case "key":
                var vk = parts.ElementAtOrDefault(1) switch { "F19" => (ushort)0x82, "F20" => (ushort)0x83, "F21" => (ushort)0x84, _ => (ushort)0 };
                uint sent = SendKey(vk);
                Note($"{id} {parts.ElementAtOrDefault(1)} 보냄(SendInput={sent}{(sent == 0 ? $" · Win32 {Marshal.GetLastWin32Error()}" : "")})");
                break;
            case "click":
                var d = parts.ElementAtOrDefault(1) switch { "allow" => PadDecision.Allow, "always" => PadDecision.AllowAlways, "none" => PadDecision.None, _ => PadDecision.Deny };
                var btn = popup.ButtonFor(d);
                ((IInvokeProvider)new ButtonAutomationPeer(btn).GetPattern(PatternInterface.Invoke)).Invoke();
                Note($"{id} 단추 {PadPopupController.Label(d)} 누름");
                break;
            case "off":
                pad.SetEnabled(false);
                Note("받기 끔");
                break;
            case "on":
                pad.SetEnabled(true);
                Note($"받기 켬 · {pad.LastStart}");
                break;
            case "quit":
                Note("끝 — 각본 quit (코드 0)");
                pad.Dispose();
                app.Shutdown(0);
                break;
            case "wait":
                break;
        }
    }

    /// <summary>기록용 — 화면 글의 U+2060(낱말 잇기)을 빼고 적는다(node 시험이 글자로 대조)</summary>
    private static string Shown(string s) => s.Replace("\u2060", "");

    // ───────────────────────── 진짜 키 입력 (SendInput)

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;

    private static uint SendKey(ushort vk)
    {
        if (vk == 0) return 0;
        var down = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } };
        var up = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } };
        return SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }
}
