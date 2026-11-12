using System.IO;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// 프로필 폴더(%AppData%\Ch57x\Profiles)의 JSON 목록 · 만들기 · 이름 바꾸기 · 복제 · 지우기 (판정 S9 · S13).
/// 프로필 이름 = 파일 이름(확장자 뺀 것) 하나로 쓴다(S9 「프로필 이름 하나」).
/// </summary>
public sealed class ProfileManager
{
    public string Folder { get; }
    public List<FileInfo> Files { get; private set; } = new();
    public IEnumerable<string> Names => Files.Select(f => Path.GetFileNameWithoutExtension(f.Name));

    /// <summary>지금 키보드에 적용된(것으로 아는) 파일 — 트레이 메뉴 체크</summary>
    public string? ActivePath { get; set; }

    public const string DefaultName = "기본";

    public event Action? Changed;
    private readonly FileSystemWatcher? _watcher;

    public ProfileManager(string? folder = null, bool watch = true)
    {
        Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ch57x", "Profiles");
        Directory.CreateDirectory(Folder);
        Refresh();
        if (watch)
        {
            _watcher = new FileSystemWatcher(Folder, "*.json")
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            _watcher.Created += (_, _) => Refresh();
            _watcher.Deleted += (_, _) => Refresh();
            _watcher.Renamed += (_, _) => Refresh();
        }
    }

    public string PathOf(string name) => Path.Combine(Folder, name + ".json");

    public void Refresh()
    {
        Files = new DirectoryInfo(Folder).GetFiles("*.json").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Changed?.Invoke();
    }

    /// <summary>「기본.json」 이 없으면 만든다 — 앱은 늘 프로필이 하나는 있다.</summary>
    public string EnsureDefault()
    {
        var path = PathOf(DefaultName);
        if (!File.Exists(path))
        {
            ProfileStore.Save(new Profile { Name = DefaultName }, path);
            Log.Write($"기본 프로필 만듦: {DefaultName}.json");
            Refresh();
        }
        return path;
    }

    /// <summary>새 이름으로 저장 (이름은 ProfileNames 로 거른 뒤 · 이미 있으면 「이름 (2)」)</summary>
    public string SaveNew(Profile p, string name)
    {
        var n = ProfileNames.Unique(name, Names);
        p.Name = n;
        var path = PathOf(n);
        ProfileStore.Save(p, path);
        Refresh();
        return path;
    }

    public string Rename(string path, string newName)
    {
        var n = ProfileNames.Clean(newName) ?? throw new InvalidOperationException("쓸 수 없는 이름");
        var target = PathOf(n);
        if (!string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(target)) throw new InvalidOperationException("같은 이름의 프로필이 있음");
            File.Move(path, target);
        }
        Refresh();
        return target;
    }

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        Refresh();
    }
}
