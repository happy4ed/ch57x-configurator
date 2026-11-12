using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ch57x.Core;

/// <summary>
/// Load/save profiles as JSON. Compatible with the web app's export format:
/// { name, layers: [ {keyId: binding}, ... ], led: [...] }.
/// </summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    public static string Serialize(Profile p) => JsonSerializer.Serialize(p, Options);

    public static Profile Deserialize(string json)
    {
        var p = JsonSerializer.Deserialize<Profile>(json, Options) ?? new Profile();
        while (p.Layers.Count < 3) p.Layers.Add(new());
        while (p.Led.Count < 3) p.Led.Add(new());
        return p;
    }

    /// <summary>바인딩 하나의 JSON (같은 동작 비교용 — ProfileDiff)</summary>
    public static string SerializeBinding(Binding b) => JsonSerializer.Serialize(b, Options);

    /// <summary>바인딩 복사본 (레이어 복사)</summary>
    public static Binding CloneBinding(Binding b) => JsonSerializer.Deserialize<Binding>(SerializeBinding(b), Options) ?? new Binding();

    /// <summary>
    /// 가져오기용 읽기 — <see cref="Deserialize"/> 는 led 가 없으면 기본값으로 채우므로, 파일에 led · name 이
    /// ★실제로 있었는지를 따로 알려 준다(판정 S7 · 병합이 LED · 이름을 덮어쓰던 결함).
    /// </summary>
    public static ImportedProfile DeserializeWithInfo(string json)
    {
        var p = Deserialize(json);
        bool hasLed = false, hasName = false;
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "led", StringComparison.OrdinalIgnoreCase))
                        hasLed = prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0;
                    else if (string.Equals(prop.Name, "name", StringComparison.OrdinalIgnoreCase))
                        hasName = prop.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prop.Value.GetString());
                }
        }
        return new ImportedProfile(p, hasLed, hasName);
    }

    public static void Save(Profile p, string path) => File.WriteAllText(path, Serialize(p));
    public static Profile Load(string path) => Deserialize(File.ReadAllText(path));
}
