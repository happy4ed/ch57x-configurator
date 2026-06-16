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

    /// <summary>Run 키에 현재 등록된 원시 값(따옴표 포함). 미등록이면 null.</summary>
    public static string? RegisteredCommand
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) as string;
        }
    }

    /// <summary>지금 실행 중인 exe 의 Run 키 등록용 명령("따옴표 감싼 절대경로").</summary>
    private static string CurrentCommand =>
        $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("실행 경로를 알 수 없음")}\"";

    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (k == null) throw new InvalidOperationException("Run 키에 접근 불가");
        if (enabled)
        {
            // self-contained single-file exe: ProcessPath 가 정답
            k.SetValue(ValueName, CurrentCommand);
        }
        else
        {
            k.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// 자가 치유: 자동실행이 켜져 있는데 Run 키가 가리키는 경로가 지금 실행 중인 exe 와 다르면
    /// 현재 exe 로 다시 써준다. portable 새 빌드를 다른 폴더/파일명으로 받아 실행해도
    /// 그 순간 등록 경로가 최신으로 갱신되어 다음 부팅부터 정상 자동 실행됨.
    /// 매 시작 시 한 번 호출. 등록이 꺼져 있으면(또는 경로 동일하면) 아무것도 안 함.
    /// </summary>
    /// <returns>실제로 경로를 갱신했으면 true.</returns>
    public static bool SyncIfEnabled()
    {
        var current = RegisteredCommand;
        if (string.IsNullOrEmpty(current)) return false;   // 자동실행 꺼짐 — 손대지 않음
        var desired = CurrentCommand;
        if (string.Equals(current, desired, StringComparison.OrdinalIgnoreCase)) return false; // 이미 최신
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (k == null) return false;
        k.SetValue(ValueName, desired);
        return true;
    }
}
