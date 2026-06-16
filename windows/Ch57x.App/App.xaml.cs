using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Ch57x.App;

public partial class App : Application
{
    private TrayIcon? _tray;
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── 단일 인스턴스 강제: 이미 트레이에 떠 있으면 새 실행은 조용히 종료 (좀비 누적 방지)
        _singleInstance = new Mutex(initiallyOwned: true, name: "Ch57xConfigurator.SingleInstance.v1", out bool isNew);
        if (!isNew)
        {
            // 이미 실행 중. 종료 핸들러 안 거치고 즉시 죽음 — 트레이 아이콘 등 부작용 0.
            Shutdown();
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        // global crash guards → log instead of silent death (easy debugging)
        DispatcherUnhandledException += (_, ev) =>
        {
            Log.Error("UI 예외", ev.Exception);
            MessageBox.Show(ev.Exception.ToString(), "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            if (ev.ExceptionObject is Exception ex) Log.Error("치명적 예외", ex);
        };

        // 윈도우 로그아웃/종료 시 정리하고 즉시 죽기
        SessionEnding += (_, _) => ForceExit();

        // 자동실행 자가 치유: portable 새 빌드를 다른 위치로 받아 실행하면 Run 키가 옛 exe 를
        // 가리킨 채로 남아 재부팅 후 구버전 실행/미실행이 됨 → 매 시작 시 현재 exe 로 갱신.
        try
        {
            if (AutoStart.SyncIfEnabled())
                Log.Write($"자동실행 등록 경로를 현재 실행 파일로 갱신함:\n{AutoStart.RegisteredCommand}");
        }
        catch (Exception ex) { Log.Error("자동실행 경로 동기화", ex); }

        Log.Write("시작됨. 트레이 아이콘에서 메뉴를 여세요.");
        _tray = new TrayIcon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); } catch { }
        try { _singleInstance?.ReleaseMutex(); _singleInstance?.Dispose(); } catch { }
        base.OnExit(e);
        // HidSharp 내부 watcher 등이 foreground thread 로 남아 프로세스를 잡아두는 경우가 있어
        // 명시적 환경 종료로 좀비 방지.
        Environment.Exit(0);
    }

    private void ForceExit()
    {
        try { _tray?.Dispose(); } catch { }
        try { _singleInstance?.ReleaseMutex(); _singleInstance?.Dispose(); } catch { }
        Environment.Exit(0);
    }
}
