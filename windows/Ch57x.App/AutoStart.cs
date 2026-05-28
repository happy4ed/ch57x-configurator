using Microsoft.Win32;

namespace Ch57x.App;

/// <summary>Windows 시작 시 자동실행 토글 — HKCU\...\Run 의 Run 키 사용 (관리자 권한 불필요).</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Ch57xConfigurator";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return !string.IsNullOrEmpty(k?.GetValue(ValueName) as string);
        }
    }

    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (k == null) throw new InvalidOperationException("Run 키에 접근 불가");
        if (enabled)
        {
            // self-contained single-file exe: ProcessPath 가 정답
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("실행 경로를 알 수 없음");
            k.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            k.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
