using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// 화면 렌더 확인 모드 — <c>CH57x 설정기.exe --render-screens &lt;폴더&gt;</c>.
/// 장치(HID) · 설정 파일(%AppData%) · 자동실행(레지스트리)을 건드리지 않고, 견본 프로필로 각 창을 라이트 · 다크 둘 다
/// RenderTargetBitmap(125% = 120 dpi)으로 PNG 저장한 뒤 끝난다. 이름은 2판 모형 그림(mocks/png/&lt;id&gt;-&lt;theme&gt;.png)과 같게.
/// 진행 · 오류는 같은 폴더 render.log 에(지어내지 않게 — 실패하면 실패라고 남김).
/// </summary>
public static class RenderScreens
{
    private const double Dpi = 120;   // 125%
    private static StringBuilder _log = new();
    private static string _logPath = "";

    public static void Note(string s) => L(s);

    private static void L(string s)
    {
        _log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {s}");
        try { File.WriteAllText(_logPath, _log.ToString()); } catch { }
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "render.log");
        _log = new StringBuilder();
        int ok = 0, fail = 0;
        string? tmp = null;
        L($"렌더 시작 · {Terms.AppName} {ErrorReport.Version()} · OS {Environment.OSVersion} · 폴더 {dir}");
        try
        {
            var app = Application.Current;
            app.Resources["WindowCorner"] = new CornerRadius(8);
            app.Resources["WindowBorderThickness"] = new Thickness(1);
            ThemeManager.ApplyFixed(false);
            AppIcons.Register(app.Resources);
            L($"글꼴 확인: Segoe UI Variable Text={HasFont("Segoe UI Variable Text")} · Segoe UI={HasFont("Segoe UI")} · Malgun Gothic={HasFont("Malgun Gothic")}");

            // 견본 프로필은 임시 폴더에(사용자 프로필 폴더 아님)
            tmp = Path.Combine(Path.GetTempPath(), "ch57x-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            ProfileStore.Save(new Profile { Name = "기본" }, Path.Combine(tmp, "기본.json"));
            ProfileStore.Save(Sample.My001(), Path.Combine(tmp, "My001.json"));
            var settings = new AppSettings { LastActiveProfile = Path.Combine(tmp, "My001.json") };
            var ctrl = new Controller(settings, new ProfileManager(tmp, watch: false), offline: true);
            ctrl.SimulateConnection("CH57x", 9, 3);
            var before = Sample.My001Before();
            ctrl.SetBaseline(before);
            Log.Lines.Clear();
            foreach (var line in Sample.LogLines) Log.Lines.Add(line);

            foreach (var dark in new[] { false, true })
            {
                ThemeManager.ApplyFixed(dark);
                var t = dark ? "dark" : "light";
                void Shot(string id, Func<BitmapSource> make)
                {
                    try
                    {
                        var bmp = make();
                        var path = Path.Combine(dir, $"{id}-{t}.png");
                        Save(bmp, path);
                        L($"OK {id}-{t}.png {bmp.PixelWidth}×{bmp.PixelHeight}");
                        ok++;
                    }
                    catch (Exception ex) { L($"실패 {id}-{t}: {ex}"); fail++; }
                }

                Shot("keyset-a", () => WithKeys(ctrl, w => Snap(w)));
                Shot("keyset-b", () => WithKeys(ctrl, w => MoreMenuShot(w)));
                Shot("keyset-c", () => WithKeys(ctrl, w => { w.SetBusy("업로드 중", 42); return Snap(w); }));
                Shot("keyset-d", () =>
                {
                    ctrl.SetBaselineToCurrent();
                    try
                    {
                        return WithKeys(ctrl, w =>
                        {
                            w.ShowUploaded(new OpOutcome(true, Layers: 3) { At = DateTime.Today.AddHours(14).AddMinutes(32) });
                            return Snap(w);
                        });
                    }
                    finally { ctrl.SetBaseline(before); }
                });
                Shot("keyset-e", () => WithKeys(ctrl, w => ProfileMenuShot(w)));
                Shot("dlg-key", () => Show(new KeyDialog(Terms.KeyTitle(1, 2), ctrl.Profile.Layers[2].GetValueOrDefault(1))));
                Shot("dlg-text", () => Show(new KeyDialog(Terms.KeyTitle(1, 2), new Binding { Type = BindingType.Text, Text = "Best regards 안녕" })));
                Shot("hud", () => HudShot(ctrl, dark));
                Shot("log", () =>
                {
                    ctrl.SimulateConnection(null);
                    try { return Show(new LogWindow(ctrl)); }
                    finally { ctrl.SimulateConnection("CH57x", 9, 3); }
                });
                Shot("about", () =>
                {
                    var a = new AboutWindow(tmp, openUpdate: true, exePathOverride: @"C:\Users\(사용자)\AppData\Local\Programs\Ch57x\CH57x 설정기.exe", autoStartOverride: "켜짐 — 위 실행 파일");
                    a.TroubleBox.IsExpanded = true;
                    return Show(a);
                });
                Shot("dlg-error", () => Show(Dialogs.BuildError(null, Terms.Upload, new System.IO.IOException("장치가 응답하지 않음 (HID 쓰기 시간 초과)"), canRetry: true, device: true)));
            }
            ctrl.Dispose();
        }
        catch (Exception ex) { L($"치명 — 렌더 중단: {ex}"); fail++; }
        finally
        {
            try { if (tmp != null) Directory.Delete(tmp, true); } catch { }
        }
        L($"끝 · 성공 {ok} · 실패 {fail}");
        return fail == 0 && ok > 0 ? 0 : 1;
    }

    private static bool HasFont(string name) =>
        Fonts.SystemFontFamilies.Any(f => f.FamilyNames.Values.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));

    // ───────────────────────── 그리기 도구

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static void Place(Window w)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -10000;
        w.Top = -10000;
        w.ShowActivated = false;
        w.ShowInTaskbar = false;
    }

    private static FrameworkElement Root(Window w) => (FrameworkElement)VisualTreeHelper.GetChild(w, 0);

    private static BitmapSource Render(FrameworkElement el)
    {
        el.UpdateLayout();
        int pw = (int)Math.Ceiling(el.ActualWidth * Dpi / 96), ph = (int)Math.Ceiling(el.ActualHeight * Dpi / 96);
        if (pw <= 0 || ph <= 0) throw new InvalidOperationException($"크기 0 ({el.ActualWidth}×{el.ActualHeight})");
        var rtb = new RenderTargetBitmap(pw, ph, Dpi, Dpi, PixelFormats.Pbgra32);
        rtb.Render(el);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource Snap(Window w) => Render(Root(w));

    private static BitmapSource Show(Window w)
    {
        Place(w);
        w.Show();
        Pump();
        try { return Snap(w); }
        finally { w.Close(); }
    }

    private static BitmapSource WithKeys(Controller ctrl, Func<KeySettingsWindow, BitmapSource> f)
    {
        var w = new KeySettingsWindow(ctrl, null) { Layer = 2 };   // 회장님 캡처의 레이어 3
        Place(w);
        w.Show();
        Pump();
        try { return f(w); }
        finally { w.Close(); }
    }

    /// <summary>메뉴를 실제 팝업으로 열어 그 메뉴만 그림으로 (ContextMenu 는 팝업 밖에 못 둠). 자리는 Compose 가 정한다.</summary>
    private static BitmapSource OpenAndRender(ContextMenu menu, FrameworkElement target, Action<ContextMenu>? whileOpen = null)
    {
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        Pump();
        try
        {
            whileOpen?.Invoke(menu);
            return Render(menu);
        }
        finally { menu.IsOpen = false; Pump(); }
    }

    private static BitmapSource Compose(BitmapSource baseImg, params (BitmapSource Img, double X, double Y)[] overlays)
    {
        double W = baseImg.PixelWidth * 96 / Dpi, H = baseImg.PixelHeight * 96 / Dpi;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(baseImg, new Rect(0, 0, W, H));
            foreach (var (img, x, y) in overlays) dc.DrawImage(img, new Rect(x, y, img.PixelWidth * 96 / Dpi, img.PixelHeight * 96 / Dpi));
        }
        var rtb = new RenderTargetBitmap(baseImg.PixelWidth, baseImg.PixelHeight, Dpi, Dpi, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // 메뉴 틀 바깥 여백 (Controls.xaml ContextMenu Border Margin="12,8,12,16")
    private const double ML = 12, MT = 8, MR = 12;

    /// <summary>⋯ 메뉴 + 테마 하위 메뉴 (모형 keyset-b) — 실제 팝업 대신 같은 메뉴를 그려 실제 놓이는 자리에 겹침</summary>
    private static BitmapSource MoreMenuShot(KeySettingsWindow w)
    {
        var root = Root(w);
        var baseImg = Render(root);
        var menu = w.BuildMoreMenu();
        double themeY = 0;
        var img = OpenAndRender(menu, w.MoreBtn, m =>
        {
            var theme = m.Items.OfType<MenuItem>().FirstOrDefault(i => (string?)i.Tag == "theme");
            if (theme != null) themeY = theme.TranslatePoint(new Point(0, 0), m).Y;
        });
        var sub = new ContextMenu { MinWidth = 176 };
        foreach (var it in w.BuildThemeChoices()) sub.Items.Add(it);
        var subImg = OpenAndRender(sub, w.MoreBtn);

        var btn = w.MoreBtn.TranslatePoint(new Point(0, 0), root);
        double menuW = img.PixelWidth * 96 / Dpi - ML - MR;
        double borderRight = btn.X + w.MoreBtn.ActualWidth;
        double mx = borderRight - menuW - ML, my = btn.Y + w.MoreBtn.ActualHeight - 4 + 4 - MT;
        double subW = subImg.PixelWidth * 96 / Dpi - ML - MR;
        double sx = mx + ML - 8 - subW - ML, sy = my + themeY - 4;
        return Compose(baseImg, (img, mx, my), (subImg, sx, sy));
    }

    /// <summary>프로필 목록 (모형 keyset-e)</summary>
    private static BitmapSource ProfileMenuShot(KeySettingsWindow w)
    {
        var root = Root(w);
        var baseImg = Render(root);
        var img = OpenAndRender(w.BuildProfileMenu(), w.ProfileBtn);
        var btn = w.ProfileBtn.TranslatePoint(new Point(0, 0), root);
        return Compose(baseImg, (img, btn.X - ML, btn.Y + w.ProfileBtn.ActualHeight - MT));
    }

    /// <summary>HUD 는 바탕이 없는 창이라(H1) 모형처럼 책상 색 위에 16 여백을 두고 그린다</summary>
    private static BitmapSource HudShot(Controller ctrl, bool dark)
    {
        var hud = new HudWindow(ctrl) { Layer = 0 };   // 회장님 HUD 캡처의 L1
        Place(hud);
        hud.Show();
        Pump();
        try
        {
            var img = Render(Root(hud));
            double w = img.PixelWidth * 96 / Dpi, h = img.PixelHeight * 96 / Dpi;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle((Brush)Application.Current.Resources["Desk"], null, new Rect(0, 0, w + 32, h + 32));
                dc.DrawImage(img, new Rect(16, 16, w, h));
            }
            var rtb = new RenderTargetBitmap((int)Math.Ceiling((w + 32) * Dpi / 96), (int)Math.Ceiling((h + 32) * Dpi / 96), Dpi, Dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
        finally { hud.Close(); }
    }

    private static void Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}

/// <summary>
/// 렌더 견본 — 회장님 캡처(artifacts/ch57x-pc-app-ux/captures/) 값을 손으로 옮김.
/// edit-window-L3.png = 레이어 3 · hud-L1.png = 레이어 1. LED: L1 파랑 · L2 주황 · L3 초록(캡처 레이어 단추 색).
/// 근거 미확보: 레이어 1 의 1~3번(「캡쳐」 · 「캡쳐&amp;복사」 · 「커스텀 캡쳐」)은 이름만 보여 실제 키는 견본(PrintScreen 계열) · 레이어 2 는 캡처가 없어 비움.
/// </summary>
internal static class Sample
{
    private static Binding K(string code, params string[] mods) => Binding.Key(code, mods);
    private static Binding Named(string alias, string code, params string[] mods) { var b = K(code, mods); b.Alias = alias; return b; }
    private static Binding Wheel(int d, params string[] mods) => new() { Type = BindingType.Mouse, Action = "wheel", Delta = d, Mods = mods.ToList(), Buttons = new() };
    private static Binding Click(string btn) => new() { Type = BindingType.Mouse, Action = "click", Buttons = new() { btn }, Mods = new() };
    private static Binding Media(string m) => new() { Type = BindingType.Media, Media = m };

    public static Profile My001()
    {
        var p = new Profile { Name = "My001", KeyCount = 9, KnobCount = 3 };
        var l1 = p.Layers[0];
        l1[1] = Named("캡쳐", "PrintScreen");
        l1[2] = Named("캡쳐&복사", "PrintScreen", "Ctrl");
        l1[3] = Named("커스텀 캡쳐", "PrintScreen", "Shift");
        l1[4] = K("NumPad4", "Ctrl", "Alt"); l1[5] = K("M", "Alt", "Win"); l1[6] = K("NumPad6", "Ctrl", "Alt");
        l1[7] = K("S", "Shift", "Win"); l1[8] = K("NumPad2", "Ctrl", "Alt"); l1[9] = K("9");
        l1[16] = Wheel(-1); l1[17] = Media("Play"); l1[18] = Wheel(1);
        l1[19] = Wheel(1, "Ctrl"); l1[20] = Click("Middle"); l1[21] = Wheel(-1, "Ctrl");
        l1[22] = Media("VolumeDown"); l1[23] = Media("Mute"); l1[24] = Media("VolumeUp");
        var l3 = p.Layers[2];
        l3[1] = K("F1", "Ctrl"); l3[2] = K("F2", "Ctrl"); l3[3] = K("F3", "Ctrl");
        l3[4] = K("T", "Shift", "Win"); l3[5] = K("C", "Shift", "Win"); l3[6] = K("PageUp");
        l3[7] = K("T", "Ctrl", "Win"); l3[8] = K("Space", "Alt"); l3[9] = K("PageDown");
        l3[16] = Wheel(-1); l3[17] = Media("Play"); l3[18] = Wheel(1);
        l3[19] = Wheel(1, "Ctrl"); l3[20] = Click("Middle"); l3[21] = Wheel(-1, "Ctrl");
        l3[22] = K("Z", "Ctrl"); l3[23] = K("Up", "Ctrl", "Shift"); l3[24] = K("Y", "Ctrl");
        p.Led[0] = new LedSetting { Mode = 1, Color = 6 };
        p.Led[1] = new LedSetting { Mode = 1, Color = 2 };
        p.Led[2] = new LedSetting { Mode = 1, Color = 4 };
        return p;
    }

    /// <summary>「업로드 안 한 변경 2개」 기준 — 레이어 3 의 8 · 9번이 달랐던 상태</summary>
    public static Profile My001Before()
    {
        var p = My001();
        p.Layers[2][8] = K("Tab", "Alt");
        p.Layers[2][9] = K("End");
        return p;
    }

    public static readonly string[] LogLines =
    {
        "[14:30:02] 시작됨 · CH57x 설정기 0.3.0",
        @"[14:30:02] 프로필 폴더: C:\Users\(사용자)\AppData\Roaming\Ch57x\Profiles",
        "[14:30:03] 연결됨: CH57x",
        "[14:30:03] 기기 감지: 키 9 · 노브 3",
        "[14:31:10] 업로드 끝 — 내 설정 18키 · 키보드에 있던 9키 유지 · 230 패킷",
        "[14:31:10] PC 저장: My001.json",
        "[14:40:55] 키보드 분리됨",
    };
}
