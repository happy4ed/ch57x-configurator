using System.Text.Json;

namespace Ch57x.App;

/// <summary>%AppData%\Ch57x\settings.json — 다시 켜도 남는 앱 설정.</summary>
public sealed class AppSettings
{
    /// <summary>마지막으로 연 프로필 파일 — 다음 실행에 그대로 연다.</summary>
    public string? LastActiveProfile { get; set; }

    /// <summary>테마: system(기본) · light · dark (판정 S15)</summary>
    public string Theme { get; set; } = ThemeManager.System;

    /// <summary>키 설정 창을 처음 닫을 때 「트레이에서 계속 동작함」 알림을 보였는가 (판정 M5)</summary>
    public bool CloseHintShown { get; set; }

    /// <summary>로그 보기 창의 같은 안내를 「확인」으로 닫았는가</summary>
    public bool TrayHintDismissed { get; set; }

    /// <summary>「권한 질문 팝업 받기」 (0.3.1 · 기본 켜기) — 끄면 파이프를 닫아 확장이 VS Code 알림으로 넘어간다</summary>
    public bool PadPopup { get; set; } = true;

    /// <summary>권한 질문 팝업 자리: bottom-right(기본) · top-right</summary>
    public string PadPopupCorner { get; set; } = PadPopupController.CornerBottomRight;

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ch57x", "settings.json");

    public static AppSettings Load()
    {
        if (AppMode.Render || AppMode.PipeTest) return new();
        try { if (File.Exists(Path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new(); }
        catch { /* 깨진 파일 = 새로 시작 */ }
        return new();
    }

    public void Save()
    {
        if (AppMode.Render || AppMode.PipeTest) return;   // 렌더 · 시험 모드는 설정 파일을 건드리지 않는다
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Opt));
        }
        catch (Exception ex) { Log.Error("설정 저장", ex); }
    }
}
