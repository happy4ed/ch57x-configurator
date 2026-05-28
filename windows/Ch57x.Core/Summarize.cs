namespace Ch57x.Core;

/// <summary>Human-readable one-line summary of a binding (mirrors web's summarize()).</summary>
public static class Summarize
{
    public static string Of(Binding? b)
    {
        if (b is null || b.Type == BindingType.None) return "—";
        if (b.Type == BindingType.Key)
            return string.Join(" → ", (b.Steps ?? new()).Select(s =>
                string.Join("+", (s.Mods ?? new()).Append(s.Code ?? "").Where(x => !string.IsNullOrEmpty(x))))).TrimEnd();
        if (b.Type == BindingType.Text) return "📝 " + (b.Text ?? "");
        if (b.Type == BindingType.Media) return "🎵 " + (b.Media != null && KeyCodes.MediaCodes.TryGetValue(b.Media, out var m) ? m.Label : b.Media);
        if (b.Type == BindingType.Mouse)
        {
            var mods = (b.Mods ?? new()).ToList();
            string mod = mods.Count > 0 ? string.Join("+", mods) + "+" : "";
            string btn = string.Join("", (b.Buttons ?? new()).Select(x => x switch { "Left" => "L", "Right" => "R", "Middle" => "M", _ => x }));
            return b.Action switch
            {
                "wheel" => $"🖱 {mod}휠 {((b.Delta) >= 0 ? "위" : "아래")}",
                "click" => $"🖱 {mod}클릭 {btn}".TrimEnd(),
                "move" => $"🖱 {mod}이동 {b.Dx},{b.Dy}",
                "drag" => $"🖱 {mod}드래그 {btn} {b.Dx},{b.Dy}".Trim(),
                _ => "🖱 " + b.Action,
            };
        }
        return "—";
    }
}
