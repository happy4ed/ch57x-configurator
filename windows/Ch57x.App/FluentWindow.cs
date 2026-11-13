using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Ch57x.App;

/// <summary>
/// 모든 창의 바탕 — Fluent 모양 + 직접 그린 제목 표시줄(아이콘 · 제목 · 최소화 · 닫기, 최대화 없음 — 판정 E7 · D8).
/// 고정 크기: 하위 창은 ResizeMode="CanMinimize"(창) 또는 "NoResize"(대화상자).
///
/// Mica: 넣지 않음 — Mica 는 DWM 유리 틀(GlassFrameThickness=-1)이 있어야 비치는데, 그러면 시스템 캡션 단추(흐린 최대화 포함)가
/// 같이 그려져 E7 · D8 과 부딪힌다. 서버에서 실기 확인을 못 해 DESIGN.md 단색(SolidBackgroundFillColorBase)으로 간다(NOTES-0.3.0.md).
/// 둥근 모서리는 Windows 11 에 맡긴다(DWMWA_WINDOW_CORNER_PREFERENCE — 실패해도 그냥 넘어감).
/// </summary>
public class FluentWindow : Window
{
    public static readonly DependencyProperty TitleBarBackgroundProperty = DependencyProperty.Register(
        nameof(TitleBarBackground), typeof(Brush), typeof(FluentWindow), new PropertyMetadata(Brushes.Transparent));

    public Brush TitleBarBackground { get => (Brush)GetValue(TitleBarBackgroundProperty); set => SetValue(TitleBarBackgroundProperty, value); }

    public FluentWindow()
    {
        SetResourceReference(StyleProperty, "FluentWindowStyle");
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 32,
            // 아래 1px 만 유리 틀 — DWM 그림자를 받으려는 흔한 방법. 내용이 덮어 그린다.
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => Close()));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => WindowState = WindowState.Minimized));
        try { Icon = AppIcons.WindowIcon; } catch { /* 아이콘 없으면 기본 */ }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (AppMode.Render) return;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
        }
        catch { /* Windows 10 등 — 네모 모서리 그대로 */ }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

/// <summary>렌더 모드(--render-screens) 표시 — 장치 · 설정 파일 · 자동실행 · 레지스트리를 건드리지 않는다.</summary>
public static class AppMode
{
    public static bool Render { get; set; }
    /// <summary>파이프 왕복 시험 모드(--pipe-test · CI) — 설정 파일을 쓰지 않는다</summary>
    public static bool PipeTest { get; set; }
}
