using System.IO;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// Watches a folder of profile JSONs and exposes "apply this profile to the keyboard" as a
/// single atomic action. Currently-active profile is tracked by filename hash for the ✓ mark.
/// </summary>
public sealed class ProfileManager
{
    /// <summary>%AppData%\Ch57x\Profiles — auto-created.</summary>
    public string Folder { get; }

    /// <summary>Sorted list of profile json files in the folder.</summary>
    public List<FileInfo> Files { get; private set; } = new();

    /// <summary>File path currently applied to the keyboard (best-effort).</summary>
    public string? ActivePath { get; private set; }

    public event Action? Changed;
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new(); // upload is one-at-a-time

    public ProfileManager()
    {
        Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                              "Ch57x", "Profiles");
        Directory.CreateDirectory(Folder);
        Refresh();
        _watcher = new FileSystemWatcher(Folder, "*.json")
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        _watcher.Created += (_, _) => Refresh();
        _watcher.Deleted += (_, _) => Refresh();
        _watcher.Renamed += (_, _) => Refresh();
    }

    public void Refresh()
    {
        Files = new DirectoryInfo(Folder).GetFiles("*.json")
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Changed?.Invoke();
    }

    /// <summary>Load + atomically upload a profile to the device. Returns true on success.</summary>
    public bool Apply(string path, Ch57xDevice device)
    {
        lock (_gate)
        {
            try
            {
                Log.Write($"적용 시작: {Path.GetFileName(path)}");
                var p = ProfileStore.Load(path);
                int n = device.UploadProfile(p);
                ActivePath = path;
                Log.Write($"✅ 적용 완료: {p.Name} ({n} 패킷)");
                Changed?.Invoke();
                return true;
            }
            catch (Exception ex) { Log.Error("프로필 적용", ex); return false; }
        }
    }

    /// <summary>Save the controller's current in-memory profile into the managed folder.</summary>
    public string SaveAs(Profile p, string nameWithoutExtension)
    {
        var safe = string.Concat(nameWithoutExtension.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(Folder, safe + ".json");
        ProfileStore.Save(p, path);
        Log.Write($"프로필 저장: {Path.GetFileName(path)}");
        Refresh();
        return path;
    }
}
