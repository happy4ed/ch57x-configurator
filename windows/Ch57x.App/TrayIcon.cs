using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Ch57x.App;

/// <summary>Tray-resident icon + right-click menu. Double-click opens the log/status window.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Controller _ctrl = new();
    private MainWindow? _window;

    public TrayIcon()
    {
        _icon = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "CH57x 설정기",
        };
        _icon.DoubleClick += (_, _) => ShowWindow();
        _ctrl.Changed += UpdateTooltip;
        BuildMenu();
        UpdateTooltip();
    }

    private void BuildMenu()
    {
        var menu = new WinForms.ContextMenuStrip();
        WinForms.ToolStripMenuItem Item(string text, Action onClick)
        {
            var it = new WinForms.ToolStripMenuItem(text);
            it.Click += (_, _) => { try { onClick(); } catch (Exception ex) { Log.Error("메뉴", ex); } };
            return it;
        }

        menu.Items.Add(Item("열기 / 로그 보기", ShowWindow));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("키보드 연결", () => _ctrl.Connect()));
        menu.Items.Add(Item("프로필 불러오기…", LoadProfileDialog));
        menu.Items.Add(Item("키보드에 업로드", () => _ctrl.Upload()));
        menu.Items.Add(Item("키보드에서 읽기", () => _ctrl.ReadFromDevice()));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("종료", () => Application.Current.Shutdown()));
        _icon.ContextMenuStrip = menu;
    }

    private void LoadProfileDialog()
    {
        var dlg = new WinForms.OpenFileDialog { Filter = "프로필 JSON|*.json|모든 파일|*.*" };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK) _ctrl.LoadProfile(dlg.FileName);
    }

    private void ShowWindow()
    {
        if (_window == null)
        {
            _window = new MainWindow(_ctrl);
            _window.Closed += (_, _) => _window = null;
        }
        _window.Show();
        _window.Activate();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
    }

    private void UpdateTooltip()
    {
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess()) { app.Dispatcher.Invoke(UpdateTooltip); return; }
        _icon.Text = _ctrl.IsConnected ? $"CH57x — 연결됨" : "CH57x — 미연결";
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); _ctrl.Dispose(); }
}
