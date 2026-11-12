using System.Text.Json;

namespace Ch57x.Core;

/// <summary>「업로드 안 한 변경 n개」(판정 E1) — 두 프로필의 다른 키 수 + 다른 LED 레이어 수.</summary>
public static class ProfileDiff
{
    public static int Count(Profile? a, Profile? b)
    {
        if (a == null || b == null) return 0;
        int n = 0;
        int layers = Math.Max(a.Layers.Count, b.Layers.Count);
        for (int l = 0; l < layers; l++)
        {
            var la = l < a.Layers.Count ? a.Layers[l] : new();
            var lb = l < b.Layers.Count ? b.Layers[l] : new();
            foreach (var k in la.Keys.Union(lb.Keys))
                if (Norm(la.GetValueOrDefault(k)) != Norm(lb.GetValueOrDefault(k))) n++;
        }
        int leds = Math.Max(a.Led.Count, b.Led.Count);
        for (int l = 0; l < leds; l++)
            if (LedKey(l < a.Led.Count ? a.Led[l] : null) != LedKey(l < b.Led.Count ? b.Led[l] : null)) n++;
        return n;
    }

    /// <summary>비움 = 없음. 같은 동작이면 같은 글.</summary>
    private static string Norm(Binding? b) =>
        b == null || b.Type == BindingType.None ? "" : ProfileStore.SerializeBinding(b);

    private static string LedKey(LedSetting? s)
    {
        s ??= new LedSetting();
        bool usesColor = s.Mode != 0 && s.Mode != 5;
        return usesColor ? $"{s.Mode}:{s.Color}" : $"{s.Mode}";
    }
}
