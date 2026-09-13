using Microsoft.Win32;

namespace Ch57x.App;

/// <summary>
/// Windows 시작 시 자동실행 — HKCU\...\Run 사용(관리자 권한 불필요).
///
/// 핵심 규칙: Run 키는 <b>재부팅 뒤에도 살아 있는 경로</b>만 가리킨다.
/// 브라우저로 받은 zip 을 탐색기에서 열어 그 안의 exe 를 바로 실행하면 실제 실행 경로가
/// <c>%LOCALAPPDATA%\Temp\Temp1_xxx.zip\...</c> 이고, 그 폴더는 탐색기를 닫거나 재부팅하면 사라진다.
/// 그 경로를 Run 에 등록하면 다음 부팅에 <b>아무 일도 안 일어나고 사유도 안 남는다</b>(2026-09-13 실사고).
/// 다운로드 폴더도 사용자가 정리·이동하는 자리라 같은 취급을 한다.
///
/// 그래서 자동실행을 켤 때 현재 exe 가 그런 위치면 <see cref="StableExePath"/> 로 <b>자기를 복사</b>하고
/// 그 사본을 등록한다. 이후 부팅에서는 그 사본이 뜬다.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Ch57xConfigurator";

    /// <summary>재부팅 뒤에도 살아 있는 설치 위치. 사용자별이라 관리자 권한이 필요 없다.</summary>
    public static string StableFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Ch57x");

    public static string StableExePath => Path.Combine(StableFolder, "CH57x 설정기.exe");

    /// <summary>지금 돌고 있는 exe 의 절대 경로.</summary>
    public static string CurrentExePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("실행 경로를 알 수 없음");

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return !string.IsNullOrEmpty(k?.GetValue(ValueName) as string);
        }
    }

    /// <summary>Run 키에 등록된 원시 값(따옴표 포함). 미등록이면 null.</summary>
    public static string? RegisteredCommand
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) as string;
        }
    }

    /// <summary>등록 값에서 따옴표를 벗긴 실제 exe 경로. 미등록이면 null.</summary>
    public static string? RegisteredExePath
    {
        get
        {
            var raw = RegisteredCommand;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            return raw.StartsWith('"') ? raw.Trim('"') : raw.Split(' ')[0];
        }
    }

    private static string Quote(string path) => $"\"{path}\"";

    // ───────────────────────────────────────────────────────────────────────────
    // 위치 판정
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>재부팅·정리로 사라질 수 있는 위치인가(압축 내부 · 임시 폴더 · 다운로드).</summary>
    public static bool IsVolatileLocation(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return true;
        string full;
        try { full = Path.GetFullPath(exePath); } catch { return true; }

        // ① 탐색기가 zip 을 열어 실행한 경우 — 경로 중간에 ".zip\" 이 낀다.
        if (full.Contains(".zip" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;

        // ② %TEMP% 하위
        if (IsUnder(full, Path.GetTempPath())) return true;

        // ③ 다운로드 폴더 하위 — 사용자가 지우고 옮기는 자리다.
        foreach (var dl in DownloadFolders())
            if (IsUnder(full, dl)) return true;

        return false;
    }

    private static bool IsUnder(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        try
        {
            var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>다운로드 폴더 후보. 레지스트리의 실제 위치를 먼저 보고, 없으면 기본 경로로 근사한다.</summary>
    private static IEnumerable<string> DownloadFolders()
    {
        string? fromRegistry = null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            var raw = k?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
            if (!string.IsNullOrWhiteSpace(raw)) fromRegistry = Environment.ExpandEnvironmentVariables(raw);
        }
        catch { /* 못 읽으면 기본 경로로 근사한다 */ }

        if (!string.IsNullOrWhiteSpace(fromRegistry)) yield return fromRegistry!;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) yield return Path.Combine(profile, "Downloads");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // 설치 · 등록
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 안정 위치에 현재 exe 사본을 만든다. 이미 안정 위치에서 돌고 있거나 같은 사본이 있으면 아무것도 안 한다.
    /// </summary>
    /// <returns>실제로 복사했으면 true.</returns>
    public static bool EnsureStableCopy()
    {
        var current = CurrentExePath;
        if (string.Equals(current, StableExePath, StringComparison.OrdinalIgnoreCase)) return false;

        Directory.CreateDirectory(StableFolder);

        // 같은 파일이 이미 있으면(길이 같고 사본이 더 최신) 다시 쓰지 않는다.
        var src = new FileInfo(current);
        var dst = new FileInfo(StableExePath);
        if (dst.Exists && dst.Length == src.Length && dst.LastWriteTimeUtc >= src.LastWriteTimeUtc) return false;

        File.Copy(current, StableExePath, overwrite: true);
        return true;
    }

    /// <summary>자동실행 켜기/끄기. 켤 때 현재 위치가 불안정하면 안정 위치로 자기를 복사한 뒤 그 경로를 등록한다.</summary>
    /// <returns>등록에 쓴 exe 경로(끌 때는 null).</returns>
    public static string? Set(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (k == null) throw new InvalidOperationException("Run 키에 접근 불가");

        if (!enabled)
        {
            k.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }

        var target = ResolveTargetExe();
        k.SetValue(ValueName, Quote(target));
        return target;
    }

    /// <summary>등록에 쓸 exe 경로 — 현재 위치가 불안정하면 안정 사본을 만들어 그 경로를 돌려준다.</summary>
    private static string ResolveTargetExe()
    {
        var current = CurrentExePath;
        if (!IsVolatileLocation(current)) return current;

        try
        {
            EnsureStableCopy();
            return StableExePath;
        }
        catch (Exception ex)
        {
            // 복사가 실패해도 등록 자체는 남긴다 — 다만 그 사실이 상태(Describe)에 드러난다.
            Log.Error("안정 위치 복사 실패 — 현재 경로로 등록함", ex);
            return current;
        }
    }

    /// <summary>
    /// 자가 치유. 매 시작 시 한 번 호출한다. 자동실행이 꺼져 있으면 아무것도 안 한다.
    /// 켜져 있으면 Run 키가 <b>실제로 존재하는 안정 경로</b>를 가리키도록 맞춘다.
    /// </summary>
    /// <returns>등록을 바꿨으면 true.</returns>
    public static bool SyncIfEnabled()
    {
        var registered = RegisteredExePath;
        if (string.IsNullOrEmpty(registered)) return false;   // 꺼짐 — 손대지 않는다

        var current = CurrentExePath;
        var currentIsVolatile = IsVolatileLocation(current);
        var registeredExists = File.Exists(registered);
        var registeredIsVolatile = IsVolatileLocation(registered);

        // 등록이 이미 살아 있는 안정 경로인데 지금 내가 사라질 자리에서 돌고 있다면 덮어쓰지 않는다.
        // (옛 코드는 무조건 현재 경로로 덮어써서, 다운로드에서 한 번 실행하면 멀쩡한 등록이 임시 경로로 바뀌었다.)
        if (registeredExists && !registeredIsVolatile && currentIsVolatile)
        {
            try
            {
                // 다만 지금 내가 더 새 빌드면 안정 사본만 갱신한다(등록 경로는 그대로).
                if (string.Equals(registered, StableExePath, StringComparison.OrdinalIgnoreCase))
                    EnsureStableCopy();
            }
            catch (Exception ex) { Log.Error("안정 사본 갱신", ex); }
            return false;
        }

        var target = ResolveTargetExe();
        if (registeredExists && string.Equals(registered, target, StringComparison.OrdinalIgnoreCase))
            return false;   // 이미 맞다

        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (k == null) return false;
        k.SetValue(ValueName, Quote(target));
        return true;
    }

    // ───────────────────────────────────────────────────────────────────────────
    // 상태 — 사람이 읽는 자리
    // ───────────────────────────────────────────────────────────────────────────

    public enum Health
    {
        Off,        // 꺼짐
        Ok,         // 켜짐 · 등록 경로 존재 · 안정 위치
        Broken,     // 켜짐인데 등록 경로의 파일이 없음 → 다음 부팅에 안 뜬다
        Volatile,   // 켜짐이고 파일은 있으나 사라질 위치 → 언젠가 안 뜬다
    }

    public static Health State
    {
        get
        {
            var p = RegisteredExePath;
            if (string.IsNullOrEmpty(p)) return Health.Off;
            if (!File.Exists(p)) return Health.Broken;
            return IsVolatileLocation(p) ? Health.Volatile : Health.Ok;
        }
    }

    /// <summary>트레이 메뉴 · 로그에 그대로 쓰는 한 줄 상태.</summary>
    public static string Describe() => State switch
    {
        Health.Off      => "자동실행: 꺼짐",
        Health.Ok       => $"자동실행: 켜짐 — {RegisteredExePath}",
        Health.Broken   => $"자동실행: 켜짐인데 등록된 파일이 없음(다음 부팅에 안 뜸) — {RegisteredExePath}",
        Health.Volatile => $"자동실행: 켜짐이나 사라질 위치에 등록됨(임시·다운로드 폴더) — {RegisteredExePath}",
        _               => "자동실행: 상태 불명",
    };

    /// <summary>Broken · Volatile 을 지금 실행 중인 exe 기준으로 고친다.</summary>
    /// <returns>고쳤으면 등록된 새 경로, 고칠 것이 없으면 null.</returns>
    public static string? Repair()
    {
        if (State is Health.Off or Health.Ok) return null;
        var target = ResolveTargetExe();
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (k == null) return null;
        k.SetValue(ValueName, Quote(target));
        return target;
    }
}
