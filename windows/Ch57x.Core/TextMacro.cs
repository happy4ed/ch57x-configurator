namespace Ch57x.Core;

/// <summary>상용구 분석 (판정 S12) — 몇 키가 되는지 · 못 넣는 글자 · 18 한도.</summary>
public sealed record TextAnalysis(int Typable, IReadOnlyList<char> Unsupported, bool OverLimit);

public static class TextMacro
{
    /// <summary>펌웨어 한도 — 한 키에 18 단계 (Protocol.TextToSteps · BuildKeyMessages)</summary>
    public const int MaxKeys = 18;

    public static TextAnalysis Analyze(string? text)
    {
        int typable = 0;
        var bad = new List<char>();
        foreach (var ch in text ?? "")
        {
            if (Protocol.CanType(ch)) typable++;
            else if (!bad.Contains(ch)) bad.Add(ch);
        }
        return new TextAnalysis(typable, bad, typable > MaxKeys);
    }

    /// <summary>「13 / 18」</summary>
    public static string Counter(TextAnalysis a) => $"{a.Typable} / {MaxKeys}";

    /// <summary>못 넣는 글자가 있으면 「「안」「녕」은 넣을 수 없음 — 영문 · 숫자 · 기호만」, 18 넘으면 그 안내, 없으면 null.</summary>
    public static string? Warning(TextAnalysis a)
    {
        if (a.Unsupported.Count > 0)
        {
            var shown = a.Unsupported.Take(6).Select(c => $"「{Show(c)}」");
            var more = a.Unsupported.Count > 6 ? " 등" : "";
            return $"{string.Concat(shown)}{more}은 넣을 수 없음 — 영문 · 숫자 · 기호만";
        }
        if (a.OverLimit) return $"{MaxKeys}자까지만 키보드에 들어감";
        return null;
    }

    private static string Show(char c) => c switch { '\r' => "줄바꿈", ' ' => "빈칸", _ => c.ToString() };
}
