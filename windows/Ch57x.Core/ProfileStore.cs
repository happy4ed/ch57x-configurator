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

    public static void Save(Profile p, string path) => File.WriteAllText(path, Serialize(p));
    public static Profile Load(string path) => Deserialize(File.ReadAllText(path));
}
