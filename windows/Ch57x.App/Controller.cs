using Ch57x.Core;

namespace Ch57x.App;

/// <summary>Holds device + current profile, exposes connect/upload/read used by tray & window.</summary>
public sealed class Controller : IDisposable
{
    public Ch57xDevice? Device { get; private set; }
    public Profile Profile { get; private set; } = new();
    public string? ProfilePath { get; private set; }

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
        }
        catch (Exception ex) { Log.Error("업로드", ex); }
    }

    public void ReadFromDevice()
    {
        if (!IsConnected) { Log.Write("먼저 연결하세요."); return; }
        try
        {
            var map = Device!.ReadProfile((l, t) => Log.Write($"읽기 레이어 {l}/{t}"));
            int total = 0;
            foreach (var (layer, keys) in map)
            {
                if (layer < 0 || layer >= Profile.Layers.Count) continue;
                Profile.Layers[layer] = keys;
                total += keys.Count;
            }
            Log.Write($"✅ 불러오기 완료: {total}개 키 (레이어 {string.Join(",", map.Keys.Select(k => k + 1))})");
            Notify();
        }
        catch (Exception ex) { Log.Error("불러오기", ex); }
    }

    public void Dispose() => Device?.Dispose();
}
