namespace Ch57x.Core;

/// <summary>프로필 이름 = 파일 이름 (판정 S9 「프로필 이름 하나」). Windows 파일 이름 규칙으로 거른다(어느 OS 에서 시험해도 같게).</summary>
public static class ProfileNames
{
    private static readonly char[] Invalid = "<>:\"/\\|?*".ToCharArray();
    private static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
    public const int MaxLength = 40;

    /// <summary>쓸 수 없는 글자를 뺀 이름. 빈 이름 · 예약어면 null.</summary>
    public static string? Clean(string? name)
    {
        if (name == null) return null;
        var s = new string(name.Where(c => c >= 0x20 && Array.IndexOf(Invalid, c) < 0).ToArray()).Trim().TrimEnd('.');
        if (s.Length > MaxLength) s = s[..MaxLength].Trim();
        if (s.Length == 0) return null;
        if (Reserved.Contains(s, StringComparer.OrdinalIgnoreCase)) return null;
        return s;
    }

    /// <summary>이름이 안 되는 이유(대화상자 안내) — 되면 null</summary>
    public static string? Problem(string? name, IEnumerable<string> existing, string? except = null)
    {
        var c = Clean(name);
        if (c == null) return "이름을 적으세요 (< > : \" / \\ | ? * 는 못 씀)";
        if (c != (name ?? "").Trim()) return "< > : \" / \\ | ? * 는 이름에 못 씀";
        if (existing.Any(e => string.Equals(e, c, StringComparison.OrdinalIgnoreCase) && !string.Equals(e, except, StringComparison.OrdinalIgnoreCase)))
            return "같은 이름의 프로필이 있음";
        return null;
    }

    /// <summary>겹치지 않는 이름: 「이름」 → 「이름 (2)」 → 「이름 (3)」</summary>
    public static string Unique(string baseName, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var b = Clean(baseName) ?? "새 프로필";
        if (!set.Contains(b)) return b;
        for (int i = 2; ; i++)
        {
            var n = $"{b} ({i})";
            if (!set.Contains(n)) return n;
        }
    }

    /// <summary>복제 이름: 「이름 복사본」 (겹치면 번호)</summary>
    public static string CopyOf(string name, IEnumerable<string> existing) => Unique($"{name} 복사본", existing);
}
