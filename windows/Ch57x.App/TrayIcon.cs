using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Ch57x.App;

/// <summary>Tray-resident icon + right-click menu. Double-click opens the log/status window.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    public Controller Controller { get; } = new();
    private readonly HotkeyService _hotkeys;
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
        Controller.Changed += () => { UpdateTooltip(); RebuildMenu(); };
        Controller.Profiles.Changed += RebuildMenu;
        _hotkeys = new HotkeyService(idx => Application.Current.Dispatcher.BeginInvoke(new Action(() => Controller.ApplyProfileByIndex(idx))));
        RebuildMenu();
        UpdateTooltip();
        Log.Write($"프로필 폴더: {Controller.Profiles.Folder}");
    }

    private void RebuildMenu()
    {
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess()) { app.Dispatcher.Invoke(RebuildMenu); return; }

        var menu = new WinForms.ContextMenuStrip();
        WinForms.ToolStripMenuItem Item(string text, Action onClick)
        {
            var it = new WinForms.ToolStripMenuItem(text);
            it.Click += (_, _) => { try { onClick(); } catch (Exception ex) { Log.Error("메뉴", ex); } };
            return it;
        }

        menu.Items.Add(Item("열기 / 로그 보기", ShowWindow));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item(Controller.IsConnected ? $"연결됨 — 재연결" : "키보드 연결", () => Controller.Connect()));

        // profile list — quick apply (Ctrl+Alt+N hotkey shown)
        var files = Controller.Profiles.Files;
        menu.Items.Add(new WinForms.ToolStripSeparator());
        if (files.Count == 0)
        {
            var empty = new WinForms.ToolStripMenuItem("(프로필 없음 — 폴더 열기)") { ForeColor = System.Drawing.Color.Gray };
            empty.Click += (_, _) => OpenProfileFolder();
            menu.Items.Add(empty);
        }
        else
        {
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string label = Path.GetFileNameWithoutExtension(f.Name);
                if (i < 9) label = $"{label}\tCtrl+Alt+{i + 1}";
                var it = Item(label, () => Controller.ApplyProfile(f.FullName));
                if (string.Equals(Controller.Profiles.ActivePath, f.FullName, StringComparison.OrdinalIgnoreCase))
                    it.Checked = true;
                menu.Items.Add(it);
            }
        }
        menu.Items.Add(Item("프로필 폴더 열기", OpenProfileFolder));

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("키보드에서 읽기 (현재 설정)", () => Controller.ReadFromDevice()));
        menu.Items.Add(Item("현재 설정 프로필로 저장…", SaveCurrentAsProfile));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("종료", () => Application.Current.Shutdown()));
        _icon.ContextMenuStrip = menu;
    }

    private void OpenProfileFolder()
    {
        System.Diagnostics.Process.Start("explorer.exe", Controller.Profiles.Folder);
    }

    private void SaveCurrentAsProfile()
    {
        var dlg = new WinForms.SaveFileDialog
        {
            Filter = "프로필 JSON|*.json",
            InitialDirectory = Controller.Profiles.Folder,
            FileName = (Controller.Profile.Name ?? "profile") + ".json",
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            Controller.SaveProfile(dlg.FileName);
            Controller.Profiles.Refresh();
        }
    }

    private void ShowWindow()
    {
        if (_window == null)
        {
            _window = new MainWindow(Controller);
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
        var active = Controller.Profiles.ActivePath != null ? " · " + Path.GetFileNameWithoutExtension(Controller.Profiles.ActivePath) : "";
        _icon.Text = Controller.IsConnected ? $"CH57x — 연결됨{active}" : "CH57x — 미연결";
    }

    public void Dispose() { _hotkeys.Dispose(); _icon.Visible = false; _icon.Dispose(); Controller.Dispose(); }
}
