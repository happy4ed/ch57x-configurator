namespace Ch57x.Core;

/// <summary>
/// 자동실행(Run 키) 명령줄 — 형식 · 해석 (순수 함수 · 시험 가능).
/// 0.3.0: 실행하면 키 설정 창이 뜬다(판정 S2). 그래서 Windows 시작 때는 <see cref="TrayArg"/> 를 붙여 창 없이 트레이로만 뜬다.
/// </summary>
public static class AutoStartCommand
{
    public const string TrayArg = "--tray";

    /// <summary>「"C:\…\CH57x 설정기.exe" --tray」</summary>
    public static string Format(string exePath) => $"\"{exePath}\" {TrayArg}";

    /// <summary>등록 값에서 exe 경로만. 따옴표가 있으면 닫는 따옴표까지, 없으면 첫 빈칸 앞까지. 비면 null.</summary>
    public static string? ParseExe(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith('"'))
        {
            int end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : s.Trim('"');
        }
        int sp = s.IndexOf(' ');
        return sp > 0 ? s[..sp] : s;
    }

    /// <summary>등록 값이 이 판의 형식(트레이 인자 포함)과 같은가</summary>
    public static bool IsCurrentFormat(string? raw, string exePath) =>
        raw != null && string.Equals(raw.Trim(), Format(exePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>명령줄 인자에 트레이 인자가 있는가</summary>
    public static bool HasTrayArg(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, TrayArg, StringComparison.OrdinalIgnoreCase));
}
