using System.IO.Compression;

namespace Ch57x.App;

/// <summary>모든 프로필 + settings 를 zip 으로 묶어 내보내기/복원.</summary>
public static class ProfileBackup
{
    public static void ExportAll(string profilesFolder, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var z = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var f in new DirectoryInfo(profilesFolder).GetFiles("*.json"))
            z.CreateEntryFromFile(f.FullName, "Profiles/" + f.Name);
        var settings = Path.Combine(Path.GetDirectoryName(profilesFolder)!, "settings.json");
        if (File.Exists(settings)) z.CreateEntryFromFile(settings, "settings.json");
        var hud = Path.Combine(Path.GetDirectoryName(profilesFolder)!, "hud.json");
        if (File.Exists(hud)) z.CreateEntryFromFile(hud, "hud.json");
    }

    /// <summary>Restore from a zip into the profiles folder (overwrites existing files of same name).</summary>
    /// <returns>(restoredProfiles, restoredSettings)</returns>
    public static (int Profiles, bool Settings) Restore(string zipPath, string profilesFolder)
    {
        var parent = Path.GetDirectoryName(profilesFolder)!;
        Directory.CreateDirectory(profilesFolder);
        int count = 0; bool sett = false;
        using var z = ZipFile.OpenRead(zipPath);
        foreach (var e in z.Entries)
        {
            if (string.IsNullOrEmpty(e.Name)) continue;   // directory entry
            if (e.FullName.StartsWith("Profiles/", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                e.ExtractToFile(Path.Combine(profilesFolder, e.Name), overwrite: true);
                count++;
            }
            else if (string.Equals(e.FullName, "settings.json", StringComparison.OrdinalIgnoreCase))
            {
                e.ExtractToFile(Path.Combine(parent, "settings.json"), overwrite: true);
                sett = true;
            }
            else if (string.Equals(e.FullName, "hud.json", StringComparison.OrdinalIgnoreCase))
            {
                e.ExtractToFile(Path.Combine(parent, "hud.json"), overwrite: true);
            }
        }
        return (count, sett);
    }
}
