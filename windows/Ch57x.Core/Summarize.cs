namespace Ch57x.Core;

/// <summary>
/// 바인딩 한 줄 요약 (키 칸 · HUD). 0.3.0: 기호(그림 글자) 대신 말로 — 「재생/일시정지」 · 「Ctrl+휠 위」 · 「클릭 가운데」(개선안 모형 그대로).
/// </summary>
public static class Summarize
{
    public const string Empty = "비어 있음";

    /// <summary>HUD · 키 칸: 이름(alias)이 있으면 이름만.</summary>
    public static string Of(Binding? b)
    {
        if (b is null || b.Type == BindingType.None) return Empty;
        if (!string.IsNullOrEmpty(b.Alias)) return b.Alias!;
        return Technical(b);
    }

    /// <summary>도움말: 이름과 키 조합 둘 다 (「캡쳐 — Shift+Win+S」).</summary>
    public static string OfFull(Binding? b)
    {
        if (b is null || b.Type == BindingType.None) return Empty;
        var tech = Technical(b);
        return string.IsNullOrEmpty(b.Alias) ? tech : $"{b.Alias} — {tech}";
    }

    private static readonly IReadOnlyDictionary<string, string> ButtonName = new Dictionary<string, string>
    {
        ["Left"] = "왼쪽", ["Right"] = "오른쪽", ["Middle"] = "가운데",
    };

    public static string Technical(Binding b)
    {
        switch (b.Type)
        {
            case BindingType.Key:
            {
                var s = string.Join(" → ", (b.Steps ?? new()).Select(st =>
                    string.Join("+", (st.Mods ?? new()).Append(st.Code ?? "").Where(x => !string.IsNullOrEmpty(x))))).Trim();
                return s.Length == 0 ? Empty : s;
            }
            case BindingType.Text:
                return "상용구 " + (b.Text ?? "");
            case BindingType.Media:
                return b.Media != null && KeyCodes.MediaCodes.TryGetValue(b.Media, out var m) ? m.Label : b.Media ?? Empty;
            case BindingType.Mouse:
            {
                var mods = (b.Mods ?? new()).ToList();
                string mod = mods.Count > 0 ? string.Join("+", mods) + "+" : "";
                string btn = string.Join(" ", (b.Buttons ?? new()).Select(x => ButtonName.TryGetValue(x, out var n) ? n : x));
                return b.Action switch
                {
                    "wheel" => $"{mod}휠 {(b.Delta >= 0 ? "위" : "아래")}",
                    "click" => $"{mod}클릭 {btn}".TrimEnd(),
                    "move" => $"{mod}이동 {b.Dx},{b.Dy}",
                    "drag" => $"{mod}드래그 {btn} {b.Dx},{b.Dy}".Replace("  ", " "),
                    _ => "마우스 " + b.Action,
                };
            }
            default:
                return Empty;
        }
    }
}
