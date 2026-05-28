using Ch57x.Core;

namespace Ch57x.App;

/// <summary>Holds device + current profile, exposes connect/upload/read used by tray & window.</summary>
public sealed class Controller : IDisposable
{
    public Ch57xDevice? Device { get; private set; }
    public Profile Profile { get; private set; } = new();
    public string? ProfilePath { get; private set; }
    public ProfileManager Profiles { get; } = new();
    private readonly AppSettings _settings = AppSettings.Load();

    public Controller()
    {
        // 영속 상태: 기본 프로필 보장 + 마지막 적용 프로필 자동 로드 (없으면 기본)
        Profiles.EnsureDefault();
        string? path = _settings.LastActiveProfile;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            path = Path.Combine(Profiles.Folder, ProfileManager.DefaultName + ".json");
        LoadProfile(path);
        Profiles.ActivePath = ProfilePath; // 시작 시 ✓ 표시도 동기화
    }

    public bool IsConnected => Device?.IsOpen == true;
    public event Action? Changed;
    private void Notify() => Changed?.Invoke();

    public bool Connect()
    {
        try
        {
            Device?.Dispose();
            Device = Ch57xDevice.Find();
            if (Device == null) { Log.Write("키보드를 찾지 못함 (VID 1189). 연결 확인."); Notify(); return false; }
            Device.Open();
            Log.Write($"연결됨: {Device.Name}");
            var info = Device.ReadDeviceInfo();
            if (info is { } d) { Profile.KeyCount = d.KeyCount; Profile.KnobCount = d.KnobCount; Log.Write($"기기 감지: 키 {d.KeyCount} · 노브 {d.KnobCount}"); }
            else Log.Write("개수 자동감지 실패(이 펌웨어 미응답) — 기존 설정 사용");
            Notify();
            return true;
        }
        catch (Exception ex) { Log.Error("연결", ex); Notify(); return false; }
    }

    public void LoadProfile(string path)
    {
        try { Profile = ProfileStore.Load(path); ProfilePath = path; Log.Write($"프로필 불러옴: {Path.GetFileName(path)} (이름: {Profile.Name})"); Notify(); }
        catch (Exception ex) { Log.Error("프로필 불러오기", ex); }
    }

    public void SaveProfile(string path)
    {
        try { ProfileStore.Save(Profile, path); ProfilePath = path; Log.Write($"프로필 저장: {Path.GetFileName(path)}"); }
        catch (Exception ex) { Log.Error("프로필 저장", ex); }
    }

    public void Upload()
    {
        if (!IsConnected) { Log.Write("먼저 연결하세요."); return; }
        try
        {
            int n = Device!.UploadProfile(Profile, (i, total) => { if (i == total) Log.Write($"업로드 진행 {i}/{total}"); });
            Log.Write($"✅ 업로드 완료 ({n} 패킷, 전체 레이어)");
            // 키보드에 올린 내용을 호스트에도 자동 저장 — 다음에 키보드 읽기 해도 alias/text 살리려고
            PersistActive();
        }
        catch (Exception ex) { Log.Error("업로드", ex); }
    }

    /// <summary>현재 메모리 프로필을 ProfilePath 에 저장 + settings.LastActiveProfile 갱신.</summary>
    private void PersistActive()
    {
        // ProfilePath 없으면 기본 프로필로 폴백
        if (string.IsNullOrEmpty(ProfilePath))
            ProfilePath = Path.Combine(Profiles.Folder, ProfileManager.DefaultName + ".json");
        try
        {
            ProfileStore.Save(Profile, ProfilePath);
            Profiles.ActivePath = ProfilePath;
            _settings.LastActiveProfile = ProfilePath;
            _settings.Save();
            Log.Write($"호스트 저장: {Path.GetFileName(ProfilePath)}");
            Notify();
        }
        catch (Exception ex) { Log.Error("자동 저장", ex); }
    }

    /// <summary>Apply a managed profile (by file path) to the keyboard.</summary>
    public bool ApplyProfile(string path)
    {
        if (!IsConnected) { Log.Write("먼저 연결하세요."); return false; }
        bool ok = Profiles.Apply(path, Device!);
        if (ok)
        {
            try
            {
                Profile = ProfileStore.Load(path);
                ProfilePath = path;
                _settings.LastActiveProfile = path; _settings.Save();
                Notify();
            }
            catch (Exception ex) { Log.Error("프로필 로드", ex); }
        }
        return ok;
    }

    public void ReadFromDevice()
    {
        if (!IsConnected) { Log.Write("먼저 연결하세요."); return; }
        try
        {
            var map = Device!.ReadProfile((l, t) => Log.Write($"읽기 레이어 {l}/{t}"));
            // 펌웨어엔 프로필명 슬롯이 없어 PC가 어느 프로필인지 모른다 →
            // 모든 호스트 프로필을 키 바인딩 핑거프린트로 매칭해 가장 잘 맞는 걸 자동 식별.
            var (best, _) = ProfileMatcher.Identify(Profiles, map);
            if (best != null && best.Ratio >= 0.7)
            {
                Log.Write($"🎯 키보드 내용 = '{best.Profile.Name}' 으로 식별 ({best.Score}/{best.Total} = {best.Ratio:P0})");
                Profile = best.Profile;
                ProfilePath = best.Path;
                Profiles.ActivePath = best.Path;
                _settings.LastActiveProfile = best.Path; _settings.Save();
            }
            else if (best != null)
                Log.Write($"⚠ 매칭 점수 낮음 (최고 '{best.Profile.Name}' = {best.Ratio:P0}). 메타데이터(alias/상용구) 일부만 보존될 수 있음.");
            int total = 0;
            foreach (var (layer, keys) in map)
            {
                if (layer < 0 || layer >= Profile.Layers.Count) continue;
                var old = Profile.Layers[layer];
                // 호스트 전용 메타(상용구/별칭)는 펌웨어에 안 저장되므로 read 결과에 보존해 끼워준다
                foreach (var (keyId, b) in keys.ToList())
                {
                    if (!old.TryGetValue(keyId, out var prev)) continue;
                    // 상용구 보존: text 였는데 키시퀀스로 되돌아왔고 확장 결과가 같으면 text 그대로
                    if (prev.Type == BindingType.Text && b.Type == BindingType.Key
                        && SameSteps(Protocol.TextToSteps(prev.Text ?? ""), b.Steps))
                    {
                        keys[keyId] = prev; continue;
                    }
                    // 별칭 보존
                    if (!string.IsNullOrEmpty(prev.Alias)) b.Alias = prev.Alias;
                }
                Profile.Layers[layer] = keys;
                total += keys.Count;
            }
            Log.Write($"✅ 불러오기 완료: {total}개 키 (레이어 {string.Join(",", map.Keys.Select(k => k + 1))})");
            Notify();
        }
        catch (Exception ex) { Log.Error("불러오기", ex); }
    }

    private static bool SameSteps(List<Accord> a, List<Accord>? b)
    {
        if (b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if ((a[i].Code ?? "") != (b[i].Code ?? "")) return false;
            var ma = (a[i].Mods ?? new()).OrderBy(x => x);
            var mb = (b[i].Mods ?? new()).OrderBy(x => x);
            if (!ma.SequenceEqual(mb)) return false;
        }
        return true;
    }

    public void Dispose() => Device?.Dispose();
}
