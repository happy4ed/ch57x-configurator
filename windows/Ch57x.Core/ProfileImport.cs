namespace Ch57x.Core;

/// <summary>가져올 파일 — 내용과 「파일에 실제로 있던 것」.</summary>
/// <param name="HasLed">파일에 led 배열이 실제로 있었다 (<see cref="ProfileStore.Deserialize"/> 는 없으면 기본값으로 채운다)</param>
/// <param name="HasName">파일에 비어 있지 않은 name 이 있었다</param>
public sealed record ImportedProfile(Profile Profile, bool HasLed, bool HasName);

/// <summary>프로필 가져오기 (판정 S7).</summary>
public static class ProfileImport
{
    /// <summary>
    /// 「프로필 가져오기…」 = 새 프로필로 넣는다 — 지금 프로필은 건드리지 않는다.
    /// 이름: 파일 안 name → 없으면 파일 이름. 이미 있으면 「이름 (2)」.
    /// </summary>
    public static (Profile Profile, string Name) AsNewProfile(ImportedProfile src, string fileStem, IEnumerable<string> existingNames)
    {
        var baseName = src.HasName ? src.Profile.Name : fileStem;
        var name = ProfileNames.Unique(ProfileNames.Clean(baseName) ?? ProfileNames.Clean(fileStem) ?? "가져온 프로필", existingNames);
        var p = ProfileStore.Deserialize(ProfileStore.Serialize(src.Profile));
        p.Name = name;
        return (p, name);
    }

    /// <summary>
    /// 지금 프로필에 합치기 (트레이 「JSON 가져오기 (병합)」 — 에이전트 패드 설치 안내가 쓰는 길).
    /// 0.2.1 결함 수정: LED 는 ★파일에 led 가 실제로 있을 때만, 이름은 ★비어 있지 않을 때만 바꾼다.
    /// </summary>
    /// <returns>덮어쓴 키 수</returns>
    public static int MergeInto(Profile target, ImportedProfile src)
    {
        int merged = 0;
        for (int li = 0; li < Math.Min(src.Profile.Layers.Count, target.Layers.Count); li++)
            foreach (var (keyId, b) in src.Profile.Layers[li])
            {
                target.Layers[li][keyId] = b;
                merged++;
            }
        if (src.HasLed)
            for (int li = 0; li < Math.Min(src.Profile.Led.Count, target.Led.Count); li++)
                target.Led[li] = new LedSetting { Mode = src.Profile.Led[li].Mode, Color = src.Profile.Led[li].Color };
        if (src.HasName && !string.IsNullOrWhiteSpace(src.Profile.Name)) target.Name = src.Profile.Name;
        return merged;
    }
}
