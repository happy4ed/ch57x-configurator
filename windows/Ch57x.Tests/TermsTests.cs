using Ch57x.Core;
using Xunit;

namespace Ch57x.Tests;

/// <summary>판정 S10 — 용어 하나로. 2판 용어 표와 대조 · 앱 화면 소스에 옛 말이 남지 않음.</summary>
public class TermsTests
{
    [Fact]
    public void TermsMatchTheSecondEditionTable()
    {
        // 2판 페이지 용어 표(build3.py TERMS)를 따로 옮겨 적은 기대값
        Assert.Equal("키보드에 업로드", Terms.Upload);
        Assert.Equal("키보드에서 읽기", Terms.ReadDevice);
        Assert.Equal("PC 저장값 불러오기", Terms.LoadPc);
        Assert.Equal("프로필 가져오기…", Terms.ImportProfile);
        Assert.Equal("전체 백업…", Terms.BackupAll);
        Assert.Equal("백업에서 복원…", Terms.RestoreBackup);
        Assert.Equal("적용", Terms.Apply);
        Assert.Equal("실행 취소", Terms.Undo);
        Assert.Equal("CH57x 설정기 — 키 설정", Terms.WindowTitle(Terms.KeySettings));
        Assert.Equal("CH57x 설정기 — 로그 보기", Terms.WindowTitle(Terms.LogView));
    }

    [Fact]
    public void KeyNames_Knob1Style()
    {
        Assert.Equal("버튼 3", Terms.KeyName(3));
        Assert.Equal("노브1 왼쪽 돌림", Terms.KeyName(16));
        Assert.Equal("노브2 누름", Terms.KeyName(20));
        Assert.Equal("노브3 오른쪽 돌림", Terms.KeyName(24));
    }

    private static string AppDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Ch57x.sln"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "Ch57x.App");
    }

    private static IEnumerable<(string File, string Text)> AppSources() =>
        Directory.EnumerateFiles(AppDir(), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)));

    [Fact]
    public void AppSourceHasNoRetiredWords()
    {
        var hits = new List<string>();
        foreach (var (file, text) in AppSources())
            foreach (var w in Terms.Retired)
                if (text.Contains(w)) hits.Add($"{file}: {w}");
        Assert.Empty(hits);
    }

    [Fact]
    public void AppSourceHasNoEmojiOrOldSymbolsOnScreen()
    {
        // 0.2.1 화면에 있던 그림 글자 · 기호 (E5 「기호는 크기 이슈 — 아이콘을 다시 만들어줘」)
        var bad = new[] { "💡", "📝", "🎵", "🖱", "⌨", "🔒", "✅", "❌", "📥", "🎯", "⚠", "↺", "↻", "≡", "✕" };
        var hits = new List<string>();
        foreach (var (file, text) in AppSources())
            foreach (var w in bad)
                if (text.Contains(w)) hits.Add($"{file}: {w}");
        Assert.Empty(hits);
    }
}
