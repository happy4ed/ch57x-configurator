using System.Drawing;
using System.Windows;
using Ch57x.Core;
using WinForms = System.Windows.Forms;

namespace Ch57x.App;

/// <summary>
/// 트레이 아이콘 + 메뉴 — 2판 트레이 개선안(build3.py TRAY_NEW) 구조.
/// 두 번 클릭 · 「키 설정 열기」 = 키 설정 창(메인 · 판정 M1). T3 켜짐 표시 = 메뉴 체크 한 가지 · T4 글자 = 동작만 ·
/// T5 맨 위 누를 수 없는 상태 줄 · 「다시 연결」은 끊겼을 때만. 보류 T1 · T2 · T6 · T7(프로필 이름 = 바로 적용 · 키보드에서 읽기)은 지금 동작 그대로.
/// </summary>
public sealed class TrayIcon : IShell, IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    public Controller Controller { get; }
    private KeySettingsWindow? _keys;
    private LogWindow? _log;
    private HudWindow? _hud;
    private AboutWindow? _about;

    public TrayIcon(Controller controller)
    {
        Controller = controller;
        _icon = new WinForms.NotifyIcon { Icon = LoadTrayIcon(), Visible = true, Text = Terms.AppName };
        _icon.DoubleClick += (_, _) => ShowKeySettings();
        _icon.ContextMenuStrip = new WinForms.ContextMenuStrip { Renderer = new TrayRenderer() };
        _icon.ContextMenuStrip.Opening += (_, _) => RebuildMenu();
        Controller.Changed += () => { UpdateTooltip(); RebuildMenu(); };
        Controller.Profiles.Changed += RebuildMenu;
        ThemeManager.Changed += RebuildMenu;
        RebuildMenu();
        UpdateTooltip();
        Log.Write($"프로필 폴더: {Controller.Profiles.Folder}");
    }

    // ───────────────────────── 창

    public void ShowKeySettings()
    {
        if (_keys == null)
        {
            _keys = new KeySettingsWindow(Controller, this);
            _keys.FirstClose += () => _icon.ShowBalloonTip(6000, Terms.AppName, "이 창을 닫아도 앱은 트레이에서 계속 동작함 — 트레이 아이콘을 두 번 눌러 다시 엶", WinForms.ToolTipIcon.Info);
            Application.Current.MainWindow = _keys;
        }
        _keys.Show();
        if (_keys.WindowState == WindowState.Minimized) _keys.WindowState = WindowState.Normal;
        _keys.Activate();
    }

    public void ShowLog()
    {
        _log ??= new LogWindow(Controller);
        _log.Show();
        if (_log.WindowState == WindowState.Minimized) _log.WindowState = WindowState.Normal;
        _log.Activate();
    }

    public void ShowAbout(bool openUpdate)
    {
        if (_about != null) { _about.Close(); _about = null; }
        _about = new AboutWindow(Controller.Profiles.Folder, openUpdate);
        _about.Closed += (_, _) => _about = null;
        _about.Show();
        _about.Activate();
    }

    private void ToggleHud()
    {
        if (_hud == null)
        {
            _hud = new HudWindow(Controller);
            _hud.Closed += (_, _) => { _hud = null; RebuildMenu(); };
            _hud.LockChanged += RebuildMenu;
            _hud.IsVisibleChanged += (_, _) => RebuildMenu();
        }
        if (_hud.IsVisible) _hud.Hide(); else { _hud.Show(); _hud.Activate(); }
        RebuildMenu();
    }

    private void ToggleHudLock()
    {
        var s = HudSettings.Load();
        s.ClickThrough = !s.ClickThrough;
        s.Save();
        _hud?.ReloadSettingsAndApply();
        Log.Write($"HUD 잠금: {(s.ClickThrough ? "켜짐 (머리만 잡힘)" : "꺼짐 (전체 잡힘)")}");
        RebuildMenu();
    }

    private Window? Owner => _keys != null && _keys.IsVisible ? _keys : null;

    private void Report(Window? owner, string kind, string strong, string rest)
    {
        if (owner is KeySettingsWindow ks) ks.ShowInfo(kind, strong, rest);
        else _icon.ShowBalloonTip(5000, strong, rest, kind == "error" ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Info);
    }

    // ───────────────────────── 파일 (IShell)

    public void ImportProfile(Window owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "프로필 JSON|*.json|모든 파일|*.*", Title = "새 프로필로 가져올 설정 파일" };
        if (dlg.ShowDialog(owner) != true) return;
        try
        {
            var path = Controller.ImportAsNewProfile(dlg.FileName);
            if (path != null) Report(owner, "ok", "프로필 가져옴", $"새 프로필 「{Path.GetFileNameWithoutExtension(path)}」 · 지금 프로필은 그대로");
        }
        catch (Exception ex) { Dialogs.Error(owner, "프로필 가져오기", ex); }
    }

    /// <summary>트레이 「JSON 가져오기 (병합)」 — 에이전트 패드 설치 안내가 이 이름을 쓴다(NOTES-0.3.0.md)</summary>
    private void MergeJson()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "프로필 JSON|*.json|모든 파일|*.*", Title = $"지금 프로필 「{Controller.ProfileName}」에 합칠 설정 파일" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int n = Controller.MergeIntoCurrent(dlg.FileName);
            Report(Owner, "ok", "합침", $"{Path.GetFileName(dlg.FileName)} → 「{Controller.ProfileName}」 {n}키 · 실행 취소로 되돌림");
        }
        catch (Exception ex) { Dialogs.Error(Owner, "JSON 가져오기 (병합)", ex); }
    }

    public void BackupAll(Window owner)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "ZIP 백업|*.zip", FileName = $"ch57x-backup-{DateTime.Now:yyyyMMdd}.zip", Title = "전체 백업" };
        if (dlg.ShowDialog(owner) != true) return;
        try
        {
            ProfileBackup.ExportAll(Controller.Profiles.Folder, dlg.FileName);
            Log.Write($"전체 백업: {dlg.FileName}");
            Report(owner, "ok", "전체 백업함", dlg.FileName);
        }
        catch (Exception ex) { Dialogs.Error(owner, "전체 백업", ex); }
    }

    public void RestoreBackup(Window owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ZIP 백업|*.zip|모든 파일|*.*", Title = "백업에서 복원" };
        if (dlg.ShowDialog(owner) != true) return;
        if (!Dialogs.Confirm(owner, "백업에서 복원", "백업 안의 프로필 · 설정으로 바꿈 — 같은 이름의 프로필은 백업 내용이 됨.", "복원")) return;
        try
        {
            var (n, _) = ProfileBackup.Restore(dlg.FileName, Controller.Profiles.Folder);
            Controller.Profiles.Refresh();
            if (Controller.ProfilePath != null && File.Exists(Controller.ProfilePath)) Controller.OpenProfile(Controller.ProfilePath);
            Log.Write($"복원: 프로필 {n}개");
            Report(owner, "ok", "백업에서 복원함", $"프로필 {n}개");
        }
        catch (Exception ex) { Dialogs.Error(owner, "백업에서 복원", ex); }
    }

    private void SaveCurrentAs()
    {
        var name = Dialogs.Input(Owner, "지금 설정을 프로필로 저장", "이름", ProfileNames.CopyOf(Controller.ProfileName, Controller.Profiles.Names),
            s => ProfileNames.Problem(s, Controller.Profiles.Names), "저장");
        if (name != null) Controller.SaveCurrentAs(name);
    }

    private async void ApplyProfile(string path)
    {
        var o = await Controller.ApplyProfileFileAsync(path, () => Dialogs.Confirm(Owner, "키보드 설정을 읽지 못함",
            "이대로 업로드하면 이 프로필에 없는 키는 키보드에서 지워질 수 있음.", "그래도 업로드"));
        if (o.Ok) _icon.ShowBalloonTip(3000, "업로드 끝", $"{Path.GetFileNameWithoutExtension(path)} · {o.At:HH:mm}", WinForms.ToolTipIcon.Info);
        else if (o.NotConnected) _icon.ShowBalloonTip(4000, "키보드가 연결되지 않음", "키보드를 꽂으면 자동으로 연결됨", WinForms.ToolTipIcon.Warning);
        else if (o.Error != null) Dialogs.Error(Owner, "프로필 적용", o.Error, retry: () => ApplyProfile(path), device: true);
    }

    private async void ReadFromDevice()
    {
        var o = await Controller.ReadFromDeviceAsync(null);
        if (o.Ok) _icon.ShowBalloonTip(3000, "키보드에서 읽음", $"{o.Total}키 · 「{Controller.ProfileName}」 화면에 엶", WinForms.ToolTipIcon.Info);
        else if (o.NotConnected) _icon.ShowBalloonTip(4000, "키보드가 연결되지 않음", "키보드를 꽂으면 자동으로 연결됨", WinForms.ToolTipIcon.Warning);
        else if (o.Error != null) Dialogs.Error(Owner, Terms.ReadDevice, o.Error, retry: ReadFromDevice, device: true);
    }

    // ───────────────────────── 메뉴

    private void RebuildMenu()
    {
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess()) { app.Dispatcher.BeginInvoke(RebuildMenu); return; }
        var menu = _icon.ContextMenuStrip!;
        if (menu.Renderer is TrayRenderer r) r.Dark = ThemeManager.IsDark;
        menu.Items.Clear();
        WinForms.ToolStripMenuItem Item(string text, Action onClick)
        {
            var it = new WinForms.ToolStripMenuItem(text);
            it.Click += (_, _) => { try { onClick(); } catch (Exception ex) { Log.Error(text, ex); Dialogs.Error(Owner, text, ex); } };
            return it;
        }
        var sep = () => new WinForms.ToolStripSeparator();

        // T5: 맨 위 누를 수 없는 상태 줄
        menu.Items.Add(new WinForms.ToolStripMenuItem(Controller.IsConnected ? $"CH57x · 연결됨 · {Controller.ProfileName}" : "CH57x · 연결 안 됨") { Enabled = false });
        if (!Controller.IsConnected) menu.Items.Add(Item("다시 연결", () => System.Threading.Tasks.Task.Run(() => Controller.Connect())));
        var open = Item(Terms.OpenKeySettings, ShowKeySettings);
        open.Font = new Font(open.Font, System.Drawing.FontStyle.Bold);   // 기본 동작 = 두 번 클릭과 같음
        menu.Items.Add(open);
        menu.Items.Add(Item(Terms.LogView, ShowLog));
        menu.Items.Add(sep());

        // 프로필 — 누르면 바로 키보드에 적용(보류 T2 · 지금 동작 유지) · 지금 프로필 = 체크
        foreach (var f in Controller.Profiles.Files)
        {
            var path = f.FullName;
            var it = Item(Path.GetFileNameWithoutExtension(f.Name), () => ApplyProfile(path));
            it.Checked = string.Equals(Controller.Profiles.ActivePath, path, StringComparison.OrdinalIgnoreCase);
            it.ToolTipText = "누르면 이 프로필을 키보드에 바로 업로드";
            menu.Items.Add(it);
        }
        menu.Items.Add(Item("프로필 폴더 열기", () => System.Diagnostics.Process.Start("explorer.exe", Controller.Profiles.Folder)));
        menu.Items.Add(Item(Terms.ImportProfile, () => { ShowKeySettings(); ImportProfile(_keys!); }));
        menu.Items.Add(Item("JSON 가져오기 (병합)…", MergeJson));
        menu.Items.Add(sep());
        menu.Items.Add(Item(Terms.BackupAll, () => { ShowKeySettings(); BackupAll(_keys!); }));
        menu.Items.Add(Item(Terms.RestoreBackup, () => { ShowKeySettings(); RestoreBackup(_keys!); }));
        menu.Items.Add(sep());

        // T3: 켜짐 = 메뉴 체크 한 가지
        menu.Items.Add(new WinForms.ToolStripMenuItem("HUD 보이기", null, (_, _) => ToggleHud()) { Checked = _hud?.IsVisible == true });
        menu.Items.Add(new WinForms.ToolStripMenuItem("HUD 잠금", null, (_, _) => ToggleHudLock()) { Checked = HudSettings.Load().ClickThrough });
        bool autoOn = false;
        try { autoOn = AutoStart.IsEnabled; } catch { }
        var auto = new WinForms.ToolStripMenuItem("윈도우 시작 시 자동실행", null, (_, _) => ToggleAutoStart()) { Checked = autoOn };
        try { auto.ToolTipText = AutoStart.Describe(); } catch { }
        menu.Items.Add(auto);
        AutoStart.Health health = AutoStart.Health.Off;
        try { health = AutoStart.State; } catch { }
        if (health is AutoStart.Health.Broken or AutoStart.Health.Volatile or AutoStart.Health.Malformed or AutoStart.Health.Blocked)
        {
            var warn = Item("자동실행 등록 고치기", RepairAutoStart);
            warn.ForeColor = System.Drawing.Color.FromArgb(0xC4, 0x2B, 0x1C);
            try { warn.ToolTipText = AutoStart.Describe(); } catch { }
            menu.Items.Add(warn);
        }
        menu.Items.Add(sep());
        menu.Items.Add(Item(Terms.ReadDevice, ReadFromDevice));
        menu.Items.Add(Item("지금 설정을 프로필로 저장…", SaveCurrentAs));
        menu.Items.Add(sep());
        menu.Items.Add(Item(Terms.Update, () => ShowAbout(true)));
        menu.Items.Add(Item(Terms.About, () => ShowAbout(false)));
        menu.Items.Add(Item(Terms.Exit, () => Application.Current.Shutdown()));
        foreach (WinForms.ToolStripItem i in menu.Items) if (i is WinForms.ToolStripMenuItem mi && mi.Enabled && mi.ForeColor != System.Drawing.Color.FromArgb(0xC4, 0x2B, 0x1C)) mi.ForeColor = TrayRenderer.TextColor(ThemeManager.IsDark);
    }

    private void ToggleAutoStart()
    {
        try
        {
            var turningOn = !AutoStart.IsEnabled;
            var target = AutoStart.Set(turningOn);
            if (!turningOn) { Log.Write("자동실행: 꺼짐"); RebuildMenu(); return; }
            Log.Write($"자동실행: 켜짐 → {target}");
            if (!string.Equals(target, AutoStart.CurrentExePath, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write($"지금 실행 파일이 사라질 위치(임시 · 다운로드)라 안정 위치로 복사함: {target}");
                _icon.ShowBalloonTip(8000, "자동실행 켬", $"다운로드 · 임시 폴더는 재부팅에 사라져 자동실행이 안 됨.\n아래 위치로 복사해 등록함.\n{target}", WinForms.ToolTipIcon.Info);
            }
            RebuildMenu();
        }
        catch (Exception ex) { Log.Error("자동실행 설정", ex); }
    }

    private void RepairAutoStart()
    {
        try
        {
            var before = AutoStart.Describe();
            var target = AutoStart.Repair();
            if (target == null) { Log.Write($"고칠 것 없음 — {before}"); return; }
            Log.Write($"자동실행 다시 등록: {before} → {AutoStart.Describe()}");
            _icon.ShowBalloonTip(8000, "자동실행을 고침", $"다음 부팅부터 이 파일이 뜸.\n{target}", WinForms.ToolTipIcon.Info);
            RebuildMenu();
        }
        catch (Exception ex) { Log.Error("자동실행 다시 등록", ex); }
    }

    private void UpdateTooltip()
    {
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess()) { app.Dispatcher.BeginInvoke(UpdateTooltip); return; }
        var t = Controller.IsConnected ? $"CH57x · 연결됨 · {Controller.ProfileName}" : "CH57x · 연결 안 됨";
        _icon.Text = t.Length > 63 ? t[..63] : t;
    }

    public void Dispose()
    {
        if (_keys != null) _keys.AllowClose = true;
        if (_log != null) _log.AllowClose = true;
        _icon.Visible = false;
        _icon.Dispose();
        Controller.Dispose();
    }

    /// <summary>트레이 아이콘 = 새 앱 아이콘(icon.ico) — 화면 배율에 맞는 작은 크기</summary>
    private static Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
            using var s = Application.GetResourceStream(uri).Stream;
            return new Icon(s, WinForms.SystemInformation.SmallIconSize);
        }
        catch { return SystemIcons.Application; }
    }
}

/// <summary>트레이 메뉴 색 — 앱 테마(라이트 · 다크)를 따른다. Windows Forms 메뉴라 Fluent 토큰을 색 값으로 옮김(DESIGN.md Bg4 · Subtle · Divider).</summary>
internal sealed class TrayRenderer : WinForms.ToolStripProfessionalRenderer
{
    private readonly Colors _c;
    public bool Dark { get => _c.Dark; set => _c.Dark = value; }

    public TrayRenderer() : this(new Colors()) { }
    private TrayRenderer(Colors c) : base(c) { _c = c; RoundedEdges = false; }

    public static System.Drawing.Color TextColor(bool dark) => dark ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(0x1B, 0x1B, 0x1B);

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled) e.TextColor = Dark ? System.Drawing.Color.FromArgb(0x9D, 0x9D, 0x9D) : System.Drawing.Color.FromArgb(0x6E, 0x6E, 0x6E);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        using var pen = new System.Drawing.Pen(TextColor(Dark), Math.Max(1.6f, r.Width / 10f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        g.DrawLines(pen, new[] { new PointF(x + w * 0.24f, y + h * 0.52f), new PointF(x + w * 0.42f, y + h * 0.70f), new PointF(x + w * 0.76f, y + h * 0.32f) });
    }

    private sealed class Colors : WinForms.ProfessionalColorTable
    {
        public bool Dark { get; set; }
        private System.Drawing.Color Bg => Dark ? System.Drawing.Color.FromArgb(0x2C, 0x2C, 0x2C) : System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xFF);
        private System.Drawing.Color Hover => Dark ? System.Drawing.Color.FromArgb(0x3D, 0x3D, 0x3D) : System.Drawing.Color.FromArgb(0xEC, 0xEC, 0xEC);
        private System.Drawing.Color Line => Dark ? System.Drawing.Color.FromArgb(0x40, 0x40, 0x40) : System.Drawing.Color.FromArgb(0xE5, 0xE5, 0xE5);
        public override System.Drawing.Color ToolStripDropDownBackground => Bg;
        public override System.Drawing.Color ImageMarginGradientBegin => Bg;
        public override System.Drawing.Color ImageMarginGradientMiddle => Bg;
        public override System.Drawing.Color ImageMarginGradientEnd => Bg;
        public override System.Drawing.Color MenuBorder => Line;
        public override System.Drawing.Color MenuItemBorder => Hover;
        public override System.Drawing.Color MenuItemSelected => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override System.Drawing.Color SeparatorDark => Line;
        public override System.Drawing.Color SeparatorLight => Line;
        public override System.Drawing.Color CheckBackground => Bg;
        public override System.Drawing.Color CheckSelectedBackground => Hover;
        public override System.Drawing.Color CheckPressedBackground => Hover;
        public override System.Drawing.Color ButtonSelectedBorder => Hover;
    }
}
