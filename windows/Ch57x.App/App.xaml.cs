using System.Threading;
using System.Windows;
using Ch57x.Core;

namespace Ch57x.App;

public partial class App : Application
{
    private TrayIcon? _tray;
    private static Mutex? _singleInstance;
    private static EventWaitHandle? _showSignal;
    private const string MutexName = "Ch57xConfigurator.SingleInstance.v1";
    private const string ShowSignalName = "Ch57xConfigurator.ShowKeySettings.v1";

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── 화면 렌더 확인 모드 (--render-screens <폴더>): 장치 · 설정 파일 · 자동실행을 건드리지 않고 PNG 만 만들고 끝
        int ri = Array.FindIndex(e.Args, a => string.Equals(a, "--render-screens", StringComparison.OrdinalIgnoreCase));
        if (ri >= 0)
        {
            AppMode.Render = true;
            base.OnStartup(e);
            // 한 화면의 실패가 프로세스를 죽이지 않게 — 기록만 하고 다음 화면으로(render.log)
            DispatcherUnhandledException += (_, ev) => { RenderScreens.Note($"디스패처 오류(계속): {ev.Exception}"); ev.Handled = true; };
            var dir = ri + 1 < e.Args.Length ? e.Args[ri + 1] : Path.Combine(Environment.CurrentDirectory, "render");
            int code = RenderScreens.Run(dir);
            Shutdown(code);
            return;
        }

        bool trayOnly = AutoStartCommand.HasTrayArg(e.Args);

        // ── 하나만 실행. 이미 떠 있으면 그 창을 앞으로(판정 S2) 하고 이 실행은 끝
        _singleInstance = new Mutex(initiallyOwned: true, name: MutexName, out bool isNew);
        if (!isNew)
        {
            if (!trayOnly)
                try { using var ev = EventWaitHandle.OpenExisting(ShowSignalName); ev.Set(); } catch { /* 앞 판(0.2.1)은 신호가 없음 */ }
            Shutdown();
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        // 처리 안 된 오류 → 한 줄 설명 + 「정보 복사」 (판정 S14 — 전문 대화상자 대신)
        DispatcherUnhandledException += (_, ev) =>
        {
            Log.Error("처리 안 된 오류", ev.Exception);
            ev.Handled = true;
            Dialogs.Error(MainWindow, "예상하지 못한 오류", ev.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => { if (ev.ExceptionObject is Exception ex) Log.Error("치명적 오류", ex); };
        SessionEnding += (_, _) => ForceExit();

        var settings = AppSettings.Load();
        ThemeManager.Init(settings.Theme);
        AppIcons.Register(Resources);

        // 자동실행 자가 치유 (0.2.1 그대로 + 0.3.0 형식 「"exe" --tray」)
        try
        {
            if (AutoStart.SyncIfEnabled()) Log.Write($"자동실행 등록을 고침 → {AutoStart.RegisteredCommand}");
            if (AutoStart.IsEnabled) Log.Write(AutoStart.Describe());
        }
        catch (Exception ex) { Log.Error("자동실행 경로 맞추기", ex); }

        Log.Write($"시작됨 · {Terms.AppName} {ErrorReport.Version()}{(trayOnly ? " · 트레이로" : "")}");
        var ctrl = new Controller(settings, new ProfileManager());
        _tray = new TrayIcon(ctrl);
        if (!trayOnly) _tray.ShowKeySettings();   // 실행하면 키 설정 창 (S2)

        // 두 번째 실행의 「창을 앞으로」 신호
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        var t = new Thread(() =>
        {
            while (true)
            {
                try { _showSignal.WaitOne(); } catch { return; }
                Dispatcher.BeginInvoke(() => _tray?.ShowKeySettings());
            }
        }) { IsBackground = true, Name = "ShowSignal" };
        t.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (AppMode.Render) { base.OnExit(e); return; }
        try { _tray?.Dispose(); } catch { }
        try { _singleInstance?.ReleaseMutex(); _singleInstance?.Dispose(); } catch { }
        base.OnExit(e);
        // HidSharp 내부 감시 스레드가 프로세스를 붙잡는 일이 있어 명시적으로 끝낸다.
        Environment.Exit(e.ApplicationExitCode);
    }

    private void ForceExit()
    {
        try { _tray?.Dispose(); } catch { }
        try { _singleInstance?.ReleaseMutex(); _singleInstance?.Dispose(); } catch { }
        Environment.Exit(0);
    }
}
