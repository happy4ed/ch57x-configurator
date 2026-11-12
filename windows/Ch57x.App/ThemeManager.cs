using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace Ch57x.App;

/// <summary>
/// 테마 (판정 S15 · 전사 규칙): 시스템 / 라이트 / 다크 · 기본 시스템 · 바꾸면 바로 반영 · settings.json 에 저장.
/// 시스템 = 레지스트리 HKCU\…\Themes\Personalize AppsUseLightTheme + SystemEvents.UserPreferenceChanged.
/// 강조색 = 사용자 Windows 강조색(HKCU\…\Explorer\Accent AccentPalette) — 라이트는 Dark1, 다크는 Light2(WinUI AccentFillColorDefault 규칙).
/// 못 읽으면 DESIGN.md primary(#005A9E / #76B9ED).
/// </summary>
public static class ThemeManager
{
    public const string System = "system", Light = "light", Dark = "dark";
    public static readonly string[] Modes = { System, Light, Dark };

    public static string Mode { get; private set; } = System;
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    private static ResourceDictionary? _current;
    private static bool _hooked;

    public static string ModeOf(string? v) => v is Light or Dark ? v : System;

    public static void Init(string? saved)
    {
        Mode = ModeOf(saved);
        Apply();
        if (!_hooked && !AppMode.Render)
        {
            _hooked = true;
            try { SystemEvents.UserPreferenceChanged += (_, e) => { if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle) Application.Current?.Dispatcher.BeginInvoke(Apply); }; }
            catch { /* 이벤트를 못 걸면 다음 실행 때 반영 */ }
        }
    }

    public static void SetMode(string mode)
    {
        Mode = ModeOf(mode);
        Apply();
    }

    /// <summary>렌더 모드: 레지스트리를 안 보고 정해진 값으로</summary>
    public static void ApplyFixed(bool dark)
    {
        Mode = dark ? Dark : Light;
        Swap(dark, dark ? Color.FromRgb(0x76, 0xB9, 0xED) : Color.FromRgb(0x00, 0x5A, 0x9E));
    }

    private static void Apply()
    {
        bool dark = Mode == Dark || (Mode == System && !SystemUsesLight());
        Swap(dark, ReadAccent(dark) ?? (dark ? Color.FromRgb(0x76, 0xB9, 0xED) : Color.FromRgb(0x00, 0x5A, 0x9E)));
    }

    private static void Swap(bool dark, Color accent)
    {
        var app = Application.Current;
        if (app == null) return;
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute) };
        var merged = app.Resources.MergedDictionaries;
        if (_current != null) merged.Remove(_current);
        merged.Insert(0, dict);
        _current = dict;
        // 강조색 — 기본 · 마우스 올림(90%) · 누름(80%) (WinUI AccentFillColorSecondary · Tertiary)
        app.Resources["Accent"] = Frozen(new SolidColorBrush(accent));
        app.Resources["Accent2"] = Frozen(new SolidColorBrush(accent) { Opacity = 0.9 });
        app.Resources["Accent3"] = Frozen(new SolidColorBrush(accent) { Opacity = 0.8 });
        app.Resources["AccentColor"] = accent;
        IsDark = dark;
        Changed?.Invoke();
    }

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private static bool SystemUsesLight()
    {
        if (AppMode.Render) return true;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch { return true; }
    }

    /// <summary>AccentPalette = 8색 × RGBA (Light3 Light2 Light1 Base Dark1 Dark2 Dark3 ·). 라이트 = Dark1(4) · 다크 = Light2(1).</summary>
    private static Color? ReadAccent(bool dark)
    {
        if (AppMode.Render) return null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (k?.GetValue("AccentPalette") is byte[] p && p.Length >= 32)
            {
                int i = (dark ? 1 : 4) * 4;
                return Color.FromRgb(p[i], p[i + 1], p[i + 2]);
            }
        }
        catch { }
        return null;
    }
}
