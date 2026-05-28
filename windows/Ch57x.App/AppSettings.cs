using System.Text.Json;

namespace Ch57x.App;

/// <summary>App-level settings persisted to %AppData%\Ch57x\settings.json — survives restarts.</summary>
public sealed class AppSettings
{
    /// <summary>Path of the profile last applied to the keyboard. Auto-loaded on next start.</summary>
    public string? LastActiveProfile { get; set; }

    // future: AppMappings (앱 감지 자동 전환), HudVisible, etc.

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Ch57x", "settings.json");

    public static AppSettings Load()
    {
        try { if (File.Exists(Path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new(); }
        catch { /* corrupt file = start fresh */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Opt));
        }
        catch (Exception ex) { Log.Error("settings 저장", ex); }
    }
}
