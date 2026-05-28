using System.Windows;
using System.Windows.Threading;

namespace Ch57x.App;

public partial class App : Application
{
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
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

        Log.Write("시작됨. 트레이 아이콘에서 메뉴를 여세요.");
        _tray = new TrayIcon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
