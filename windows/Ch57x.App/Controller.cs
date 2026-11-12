using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>업로드 · 읽기 결과 (창이 알림 줄로 보인다 — 판정 S3 · S14)</summary>
public sealed record OpOutcome(bool Ok, bool Cancelled = false, bool NotConnected = false, int Mine = 0, int Kept = 0, int Layers = 0,
                               int Total = 0, Exception? Error = null)
{
    public DateTime At { get; init; } = DateTime.Now;
}

/// <summary>
/// 장치 + 지금 프로필. 키 설정 창 · HUD · 트레이 · 로그 보기 창이 함께 쓴다.
/// 0.3.0: 장치 일은 뒤에서(Task.Run · 한 번에 하나) — 업로드 중 화면이 굳지 않게(판정 S4).
/// 바꿀 때마다 실행 취소 기록(S5) · PC 파일 자동 저장 · 「업로드 안 한 변경」 기준(E1).
/// </summary>
public sealed class Controller : IDisposable
{
    public Ch57xDevice? Device { get; private set; }
    public Profile Profile { get; private set; } = new();
    public string? ProfilePath { get; private set; }
    public ProfileManager Profiles { get; }
    public AppSettings Settings { get; }
    public ProfileHistory History { get; } = new();

    /// <summary>프로필 이름 = 파일 이름 (판정 S9)</summary>
    public string ProfileName => ProfilePath != null ? Path.GetFileNameWithoutExtension(ProfilePath) : Profile.Name;

    /// <summary>마지막으로 키보드와 맞춘(업로드 · 읽음) 내용 — 「업로드 안 한 변경 n개」 기준. 시작할 때는 연 프로필로 본다.</summary>
    private Profile _baseline = new();
    public int PendingChanges => ProfileDiff.Count(_baseline, Profile);

    public bool IsBusy { get; private set; }
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly bool _offline;
    private (string Name, int Keys, int Knobs)? _simulated;
    private (int Keys, int Knobs)? _detected;

    public bool IsConnected => _simulated != null || Device?.IsOpen == true;
    public string? DeviceName => _simulated?.Name ?? (Device?.IsOpen == true ? Device.Name : null);
    public event Action? Changed;

    public Controller(AppSettings settings, ProfileManager profiles, bool offline = false)
    {
        Settings = settings;
        Profiles = profiles;
        _offline = offline;
        var def = Profiles.EnsureDefault();
        var path = settings.LastActiveProfile;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = def;
        OpenProfile(path, quiet: true);
        _baseline = Clone(Profile);
        Profiles.ActivePath = ProfilePath;

        if (!_offline)
        {
            // 자동 연결: 시작 때 한 번 + 장치가 바뀔 때마다
            _ = Task.Run(() => Connect());
            HidSharp.DeviceList.Local.Changed += OnDeviceListChanged;
        }
    }

    private void Notify()
    {
        var d = Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) d.BeginInvoke(() => Changed?.Invoke());
        else Changed?.Invoke();
    }

    private static void Ui(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) d.Invoke(a); else a();
    }

    private static Profile Clone(Profile p) => ProfileStore.Deserialize(ProfileStore.Serialize(p));

    // ───────────────────────── 장치

    private void OnDeviceListChanged(object? sender, EventArgs e)
    {
        try
        {
            if (!IsConnected) { _ = Task.Run(() => Connect()); return; }
            var present = HidSharp.DeviceList.Local.GetHidDevices(Protocol.VendorId);
            if (!present.Any(d => Protocol.ProductIds.Contains((ushort)d.ProductID)))
            {
                Device?.Dispose(); Device = null;
                Log.Write("키보드 분리됨");
                Notify();
            }
        }
        catch (Exception ex) { Log.Error("장치 바뀜 처리", ex); }
    }

    public bool Connect()
    {
        if (_offline) return false;
        _io.Wait();
        try
        {
            Device?.Dispose();
            Device = Ch57xDevice.Find();
            if (Device == null) { Log.Write("키보드를 찾지 못함 (VID 1189) — 키보드를 꽂으면 자동으로 연결됨"); Notify(); return false; }
            Device.Open();
            Log.Write($"연결됨: {Device.Name}");
            var info = Device.ReadDeviceInfo();
            if (info is { } d)
            {
                _detected = (d.KeyCount, d.KnobCount);
                Ui(() => { Profile.KeyCount = d.KeyCount; Profile.KnobCount = d.KnobCount; });
                Log.Write($"기기 감지: 키 {d.KeyCount} · 노브 {d.KnobCount}");
            }
            else Log.Write("키 · 노브 개수 자동 감지 실패(이 펌웨어 미응답) — 프로필 값 그대로");
            Notify();
            return true;
        }
        catch (Exception ex) { Log.Error("연결", ex); Notify(); return false; }
        finally { _io.Release(); }
    }

    // ───────────────────────── 프로필 열기 · 저장

    private Profile LoadFile(string path)
    {
        var p = ProfileStore.Load(path);
        p.Name = Path.GetFileNameWithoutExtension(path);   // 이름 = 파일 이름 (S9)
        if (_detected is { } d) { p.KeyCount = d.Keys; p.KnobCount = d.Knobs; }
        return p;
    }

    /// <summary>키 설정 창에서 프로필을 고름 = 그 프로필을 화면에 연다(키보드에는 업로드해야 들어감).</summary>
    public void OpenProfile(string path, bool quiet = false)
    {
        try
        {
            Profile = LoadFile(path);
            ProfilePath = path;
            History.Clear();
            Settings.LastActiveProfile = path;
            if (!quiet) Settings.Save();
            Log.Write($"프로필 열림: {Path.GetFileNameWithoutExtension(path)}");
            Notify();
        }
        catch (Exception ex) { Log.Error("프로필 열기", ex); }
    }

    /// <summary>지금 프로필을 PC 파일에 저장 (바꿀 때마다 — 0.2.1 과 같음)</summary>
    private void Persist()
    {
        ProfilePath ??= Profiles.PathOf(ProfileManager.DefaultName);
        Profile.Name = Path.GetFileNameWithoutExtension(ProfilePath);
        if (AppMode.Render) return;
        try { ProfileStore.Save(Profile, ProfilePath); }
        catch (Exception ex) { Log.Error("프로필 저장", ex); }
    }

    // ───────────────────────── 바꾸기 (모두 실행 취소 기록 · S5)

    private void Edit(string label, Action<Profile> change)
    {
        History.Push(Profile, label);
        change(Profile);
        Persist();
        Notify();
    }

    /// <summary>키 하나 바꾸기. null · 「없음」 = 비움(프로필에 「비움」으로 남겨 업로드가 기기 값을 되살리지 않게 — UploadPlan)</summary>
    public void SetBinding(int layer, int keyId, Binding? b)
    {
        bool clear = b == null || b.Type == BindingType.None;
        Edit($"{Terms.KeyName(keyId)} {(clear ? "비움" : "바꿈")}", p => p.Layers[layer][keyId] = clear ? new Binding { Type = BindingType.None } : b!);
        Log.Write($"레이어 {layer + 1} {Terms.KeyName(keyId)} → {Summarize.Of(b)}");
    }

    public void SetLed(int layer, int? mode = null, int? color = null) =>
        Edit($"레이어 {layer + 1} 조명 바꿈", p =>
        {
            if (mode is { } m) p.Led[layer].Mode = m;
            if (color is { } c) p.Led[layer].Color = c;
        });

    public void CopyLayer(int from, int to) =>
        Edit($"레이어 {from + 1} → 레이어 {to + 1} 복사", p =>
            p.Layers[to] = p.Layers[from].ToDictionary(kv => kv.Key, kv => ProfileStore.CloneBinding(kv.Value)));

    /// <summary>레이어의 모든 키를 비움(업로드하면 키보드에서도 비워짐)</summary>
    public void ClearLayer(int layer) =>
        Edit($"레이어 {layer + 1} 비움", p =>
        {
            var ids = Enumerable.Range(1, p.KeyCount).Concat(Enumerable.Range(0, p.KnobCount).SelectMany(k => new[] { 16 + 3 * k, 17 + 3 * k, 18 + 3 * k }));
            p.Layers[layer] = ids.Union(p.Layers[layer].Keys).ToDictionary(k => k, _ => new Binding { Type = BindingType.None });
        });

    public void SetCounts(int keys, int knobs)
    {
        Edit("키 · 노브 개수 바꿈", p => { p.KeyCount = keys; p.KnobCount = knobs; });
        _detected = null;   // 손으로 정했으면 그 값
    }

    public bool Undo()
    {
        var label = History.NextLabel;
        var prev = History.Undo();
        if (prev == null) return false;
        Profile = prev;
        Persist();
        Log.Write($"실행 취소: {label}");
        Notify();
        return true;
    }

    /// <summary>「PC 저장값 불러오기」 — PC 에 저장된 이 프로필을 다시 화면으로(판정 E2: 키보드 값이 아니라 PC 저장값)</summary>
    public void LoadFromPc()
    {
        if (ProfilePath == null || !File.Exists(ProfilePath)) return;
        try
        {
            History.Push(Profile, Terms.LoadPc);
            Profile = LoadFile(ProfilePath);
            Log.Write($"PC 저장값 불러옴: {ProfileName}");
            Notify();
        }
        catch (Exception ex) { Log.Error(Terms.LoadPc, ex); }
    }

    // ───────────────────────── 프로필 관리 (S9 · S13)

    public void CreateProfile(string name)
    {
        var p = new Profile { KeyCount = Profile.KeyCount, KnobCount = Profile.KnobCount };
        var path = Profiles.SaveNew(p, name);
        Log.Write($"새 프로필: {Path.GetFileNameWithoutExtension(path)}");
        OpenProfile(path);
    }

    public void RenameCurrent(string newName)
    {
        if (ProfilePath == null) return;
        var old = ProfilePath;
        var path = Profiles.Rename(old, newName);
        ProfilePath = path;
        if (string.Equals(Profiles.ActivePath, old, StringComparison.OrdinalIgnoreCase)) Profiles.ActivePath = path;
        Settings.LastActiveProfile = path; Settings.Save();
        Persist();
        Log.Write($"이름 바꿈: {Path.GetFileNameWithoutExtension(old)} → {ProfileName}");
        Notify();
    }

    public void DuplicateCurrent()
    {
        var name = ProfileNames.CopyOf(ProfileName, Profiles.Names);
        var path = Profiles.SaveNew(Clone(Profile), name);
        Log.Write($"복제: {ProfileName} → {Path.GetFileNameWithoutExtension(path)}");
        OpenProfile(path);
    }

    public void DeleteProfile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        Profiles.Delete(path);
        if (string.Equals(Profiles.ActivePath, path, StringComparison.OrdinalIgnoreCase)) Profiles.ActivePath = null;
        Log.Write($"프로필 지움: {name}");
        if (string.Equals(ProfilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            var next = Profiles.Files.FirstOrDefault()?.FullName ?? Profiles.EnsureDefault();
            OpenProfile(next);
        }
        else Notify();
    }

    /// <summary>「프로필 가져오기…」 = 새 프로필로 넣기 · 지금 프로필은 그대로 (판정 S7)</summary>
    public string? ImportAsNewProfile(string file)
    {
        try
        {
            var src = ProfileStore.DeserializeWithInfo(File.ReadAllText(file));
            var (p, name) = ProfileImport.AsNewProfile(src, Path.GetFileNameWithoutExtension(file), Profiles.Names);
            var path = Profiles.SaveNew(p, name);
            Log.Write($"프로필 가져옴: {Path.GetFileName(file)} → 새 프로필 「{Path.GetFileNameWithoutExtension(path)}」");
            OpenProfile(path);
            return path;
        }
        catch (Exception ex) { Log.Error("프로필 가져오기", ex); throw; }
    }

    /// <summary>트레이 「JSON 가져오기 (병합)」 — 지금 프로필에 합치기. LED · 이름은 파일에 실제로 있을 때만(S7 결함 수정).</summary>
    public int MergeIntoCurrent(string file)
    {
        var src = ProfileStore.DeserializeWithInfo(File.ReadAllText(file));
        int n = 0;
        Edit($"{Path.GetFileName(file)} 합침", p => n = ProfileImport.MergeInto(p, src));
        Log.Write($"합침: {Path.GetFileName(file)} → {ProfileName} ({n}키{(src.HasLed ? " · 조명 포함" : " · 조명 그대로")})");
        return n;
    }

    /// <summary>트레이 「지금 설정을 프로필로 저장…」</summary>
    public void SaveCurrentAs(string name)
    {
        var path = Profiles.SaveNew(Clone(Profile), name);
        Log.Write($"프로필로 저장: {Path.GetFileNameWithoutExtension(path)}");
        OpenProfile(path);
    }

    // ───────────────────────── 업로드 · 읽기 (뒤에서 · S4)

    /// <summary>
    /// 키보드에 업로드 — 먼저 키보드를 읽어 프로필에 없는 키를 보존한다(S1). 읽지 못하면 confirmReadFailed 로 묻는다(UI 스레드).
    /// progress = 0~100.
    /// </summary>
    public Task<OpOutcome> UploadAsync(Func<bool>? confirmReadFailed, IProgress<int>? progress) =>
        UploadCoreAsync(Clone(Profile), ProfilePath, confirmReadFailed, progress, openAfter: false);

    /// <summary>트레이의 프로필 이름 = 그 파일을 바로 키보드에 적용(보류 T2 — 지금 동작 유지). 성공하면 그 프로필을 연다.</summary>
    public async Task<OpOutcome> ApplyProfileFileAsync(string path, Func<bool>? confirmReadFailed)
    {
        Profile p;
        try { p = LoadFile(path); }
        catch (Exception ex) { Log.Error("프로필 읽기", ex); return new OpOutcome(false, Error: ex); }
        Log.Write($"적용 시작: {Path.GetFileNameWithoutExtension(path)}");
        return await UploadCoreAsync(p, path, confirmReadFailed, null, openAfter: true);
    }

    private async Task<OpOutcome> UploadCoreAsync(Profile snapshot, string? path, Func<bool>? confirmReadFailed, IProgress<int>? progress, bool openAfter)
    {
        if (!IsConnected || Device == null) { Log.Write("키보드가 연결되지 않음 — 업로드 안 함"); return new OpOutcome(false, NotConnected: true); }
        await _io.WaitAsync();
        IsBusy = true; Notify();
        try
        {
            var dev = Device;
            progress?.Report(0);
            Dictionary<int, Dictionary<int, Binding>>? read = null;
            Exception? readErr = null;
            try { read = await Task.Run(() => dev.ReadProfile((l, t) => progress?.Report(l * 15 / Math.Max(t, 1)))); }
            catch (Exception ex) { readErr = ex; Log.Error("업로드 전 키보드 읽기", ex); }
            if (readErr != null && (confirmReadFailed == null || !confirmReadFailed()))
            {
                Log.Write("업로드 취소 — 키보드의 지금 설정을 읽지 못함");
                return new OpOutcome(false, Cancelled: true);
            }
            var plan = UploadPlan.Build(snapshot, read);
            await Task.Run(() => dev.SendAll(plan.Messages, (i, n) => progress?.Report(15 + (int)(85L * i / Math.Max(n, 1)))));
            Log.Write($"업로드 끝 — 내 설정 {plan.Mine}키 · 키보드에 있던 {plan.Kept}키 유지{(plan.Cleared > 0 ? $" · 비움 {plan.Cleared}키" : "")} · {plan.Messages.Count} 패킷");
            _baseline = Clone(snapshot);
            if (path != null)
            {
                Profiles.ActivePath = path;
                if (openAfter) OpenProfile(path);
                else Persist();
                Settings.LastActiveProfile = path; Settings.Save();
                Log.Write($"PC 저장: {Path.GetFileName(path)}");
            }
            return new OpOutcome(true, Mine: plan.Mine, Kept: plan.Kept, Layers: snapshot.Layers.Count);
        }
        catch (Exception ex) { Log.Error("업로드", ex); return new OpOutcome(false, Error: ex); }
        finally { IsBusy = false; _io.Release(); Notify(); }
    }

    /// <summary>키보드에서 읽기 — 키보드 → 화면(파일은 그대로 · 용어 표). 별칭 · 상용구는 PC 프로필 값으로 살린다.</summary>
    public async Task<OpOutcome> ReadFromDeviceAsync(IProgress<int>? progress)
    {
        if (!IsConnected || Device == null) { Log.Write("키보드가 연결되지 않음 — 읽기 안 함"); return new OpOutcome(false, NotConnected: true); }
        await _io.WaitAsync();
        IsBusy = true; Notify();
        try
        {
            var dev = Device;
            var map = await Task.Run(() => dev.ReadProfile((l, t) => { progress?.Report(l * 100 / Math.Max(t, 1)); Log.Write($"읽기 레이어 {l}/{t}"); }));
            History.Push(Profile, Terms.ReadDevice);
            // 펌웨어에는 프로필 이름 자리가 없다 → 모든 PC 프로필과 키 내용으로 대조해 가장 잘 맞는 것을 고른다.
            var (best, _) = ProfileMatcher.Identify(Profiles, map);
            if (best != null && best.Ratio >= 0.7 && !string.Equals(best.Path, ProfilePath, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write($"키보드 내용 = 「{Path.GetFileNameWithoutExtension(best.Path)}」 로 알아봄 ({best.Score}/{best.Total} = {best.Ratio:P0})");
                Profile = LoadFile(best.Path);
                ProfilePath = best.Path;
                History.Clear();
                Settings.LastActiveProfile = best.Path; Settings.Save();
            }
            else if (best != null && best.Ratio < 0.7)
                Log.Write($"맞는 프로필 없음 (가장 가까운 「{Path.GetFileNameWithoutExtension(best.Path)}」 = {best.Ratio:P0}) — 별칭 · 상용구는 일부만 살 수 있음");
            Profiles.ActivePath = ProfilePath;
            int total = 0;
            foreach (var (layer, keys) in map)
            {
                if (layer < 0 || layer >= Profile.Layers.Count) continue;
                var old = Profile.Layers[layer];
                foreach (var (keyId, b) in keys.ToList())
                {
                    if (!old.TryGetValue(keyId, out var prev)) continue;
                    if (prev.Type == BindingType.Text && b.Type == BindingType.Key && SameSteps(Protocol.TextToSteps(prev.Text ?? ""), b.Steps))
                    { keys[keyId] = prev; continue; }
                    if (!string.IsNullOrEmpty(prev.Alias)) b.Alias = prev.Alias;
                }
                Profile.Layers[layer] = keys;
                total += keys.Count;
            }
            _baseline = Clone(Profile);
            Log.Write($"키보드에서 읽음: {total}키 (레이어 {string.Join(",", map.Keys.Select(k => k + 1))})");
            return new OpOutcome(true, Total: total, Layers: map.Count);
        }
        catch (Exception ex) { Log.Error("키보드에서 읽기", ex); return new OpOutcome(false, Error: ex); }
        finally { IsBusy = false; _io.Release(); Notify(); }
    }

    private static bool SameSteps(List<Accord> a, List<Accord>? b)
    {
        if (b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if ((a[i].Code ?? "") != (b[i].Code ?? "")) return false;
            if (!(a[i].Mods ?? new()).OrderBy(x => x).SequenceEqual((b[i].Mods ?? new()).OrderBy(x => x))) return false;
        }
        return true;
    }

    // ───────────────────────── 렌더 모드 전용 (장치 없이 화면만)

    public void SimulateConnection(string? name, int keys = 9, int knobs = 3)
    {
        _simulated = name == null ? null : (name, keys, knobs);
        Notify();
    }

    public void SetBaseline(Profile p) { _baseline = Clone(p); Notify(); }
    public void SetBaselineToCurrent() => SetBaseline(Profile);

    public void Dispose()
    {
        if (!_offline) HidSharp.DeviceList.Local.Changed -= OnDeviceListChanged;
        Device?.Dispose();
    }
}
