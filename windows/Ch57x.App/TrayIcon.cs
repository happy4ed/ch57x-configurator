using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Ch57x.App;

/// <summary>Tray-resident icon + right-click menu. Double-click opens the log/status window.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    public Controller Controller { get; } = new();
    private MainWindow? _window;
    private HudWindow? _hud;
    private EditWindow? _editor;

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
            foreach (var f in files)
            {
                var it = Item(Path.GetFileNameWithoutExtension(f.Name), () => Controller.ApplyProfile(f.FullName));
                if (string.Equals(Controller.Profiles.ActivePath, f.FullName, StringComparison.OrdinalIgnoreCase))
                    it.Checked = true;
                menu.Items.Add(it);
            }
        }
        menu.Items.Add(Item("프로필 폴더 열기", OpenProfileFolder));
        menu.Items.Add(Item("JSON 가져오기 (병합)…", ImportMergeDialog));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("키 편집…", OpenEditor));

        menu.Items.Add(new WinForms.ToolStripSeparator());
        var hudItem = Item(_hud?.IsVisible == true ? "✓ HUD 보이기" : "HUD 보이기", ToggleHud);
        menu.Items.Add(hudItem);
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

    private void ImportMergeDialog()
    {
        var dlg = new WinForms.OpenFileDialog { Filter = "프로필 JSON|*.json|모든 파일|*.*", Title = "현재 프로필에 병합할 JSON 선택" };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK) Controller.ImportMerge(dlg.FileName);
    }

    private void OpenEditor()
    {
        if (_editor == null) { _editor = new EditWindow(Controller); _editor.Closed += (_, _) => _editor = null; }
        _editor.Show(); _editor.Activate();
    }

    private void ToggleHud()
    {
        if (_hud == null)
        {
            _hud = new HudWindow(Controller);
            _hud.Closed += (_, _) => { _hud = null; RebuildMenu(); };
        }
        if (_hud.IsVisible) _hud.Hide(); else { _hud.Show(); _hud.Activate(); }
        RebuildMenu();
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

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); Controller.Dispose(); }
}
