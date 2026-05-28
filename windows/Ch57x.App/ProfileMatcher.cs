using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// Identify which managed profile a keyboard's current contents correspond to —
/// since the firmware has no metadata, we match by binding content fingerprint.
/// </summary>
public static class ProfileMatcher
{
    public sealed record Match(string Path, Profile Profile, int Score, int Total)
    {
        public double Ratio => Total == 0 ? 0 : (double)Score / Total;
    }

    /// <summary>Find the managed profile whose key bindings best match the just-read layers.
    /// Returns (best, allRanked). `readLayers` keys are 0-based layer index.</summary>
    public static (Match? Best, List<Match> Ranked) Identify(
        ProfileManager manager,
        IReadOnlyDictionary<int, Dictionary<int, Binding>> readLayers)
    {
        var results = new List<Match>();
        foreach (var f in manager.Files)
        {
            Profile p;
            try { p = ProfileStore.Load(f.FullName); } catch { continue; }
            int score = 0, total = 0;
            foreach (var (layerIdx, keys) in readLayers)
            {
                if (layerIdx < 0 || layerIdx >= p.Layers.Count) continue;
                var pl = p.Layers[layerIdx];
                // every key on the device (after expanding text→keys) counted
                foreach (var (keyId, devBinding) in keys)
                {
                    total++;
                    if (!pl.TryGetValue(keyId, out var prof)) continue;
                    if (BindingsBehaveSame(prof, devBinding)) score++;
                }
                // keys that the profile has but the device doesn't return → mismatch (count as total)
                foreach (var kv in pl)
                    if (!keys.ContainsKey(kv.Key)) total++;
            }
            results.Add(new Match(f.FullName, p, score, total));
        }
        results.Sort((a, b) => b.Ratio.CompareTo(a.Ratio));
        return (results.FirstOrDefault(), results);
    }

    /// <summary>True if two bindings produce identical keyboard behaviour (text↔key expansion-aware).</summary>
    private static bool BindingsBehaveSame(Binding a, Binding b)
    {
        // text → expand to key steps for comparison
        var na = Normalize(a); var nb = Normalize(b);
        if (na.Type != nb.Type) return false;
        return na.Type switch
        {
            BindingType.Key => StepsEqual(na.Steps, nb.Steps),
            BindingType.Media => na.Media == nb.Media,
            BindingType.Mouse => na.Action == nb.Action && na.Delta == nb.Delta &&
                                 SetEqual(na.Buttons, nb.Buttons) && na.Dx == nb.Dx && na.Dy == nb.Dy,
            _ => true,
        };
    }

    private static Binding Normalize(Binding b)
    {
        if (b.Type == BindingType.Text) return new Binding { Type = BindingType.Key, Steps = Protocol.TextToSteps(b.Text ?? "") };
        return b;
    }

    private static bool StepsEqual(List<Accord>? a, List<Accord>? b)
    {
        a ??= new(); b ??= new();
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if ((a[i].Code ?? "") != (b[i].Code ?? "")) return false;
            if (!SetEqual(a[i].Mods, b[i].Mods)) return false;
        }
        return true;
    }

    private static bool SetEqual(List<string>? a, List<string>? b)
    {
        var sa = new HashSet<string>(a ?? new()); var sb = new HashSet<string>(b ?? new());
        return sa.SetEquals(sb);
    }
}
