using System.Text;
using System.Text.RegularExpressions;
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
        Assert.Equal("키 3", Terms.KeyName(3));
        Assert.Equal("노브1 왼쪽 돌림", Terms.KeyName(16));
        Assert.Equal("노브2 누름", Terms.KeyName(20));
        Assert.Equal("노브3 오른쪽 돌림", Terms.KeyName(24));
    }

    [Fact]
    public void KeyWindowTitles_SameWordsAsMainWindow()
    {
        // 첫 사용 점검 7번: 「버튼 1 · 레이어 3」 → 본 창과 같은 「키 1 · 레이어 3」
        Assert.Equal("키 1 · 레이어 3", Terms.KeyTitle(1, 2));
        Assert.Equal("키 9 · 레이어 1", Terms.KeyTitle(9, 0));
        Assert.Equal("노브1 · 왼쪽 돌림 · 레이어 3", Terms.KeyTitle(16, 2));
        Assert.Equal("노브3 · 오른쪽 돌림 · 레이어 2", Terms.KeyTitle(24, 1));
    }

    private static string WindowsDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Ch57x.sln"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string AppDir() => Path.Combine(WindowsDir(), "Ch57x.App");

    private static IEnumerable<(string File, string Text)> Sources(string dir, bool xaml) =>
        Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || (xaml && f.EndsWith(".xaml"))) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)));

    private static IEnumerable<(string File, string Text)> AppSources() => Sources(AppDir(), xaml: true);

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

    // ───────────────────────── 화면 글에 쓰지 않는 낱말 (첫 사용 점검 2번 「업로드」 하나로 · 7번 「층」 → 「레이어」)

    [Fact]
    public void BannedList_IsFixed()
    {
        var words = Terms.Banned.Select(b => b.Word).ToList();
        foreach (var w in new[] { "올리", "올림", "층", "버튼" }) Assert.Contains(w, words);
        // 「올리기」 · 「올림」 · 「올리지」 모두 걸린다
        foreach (var screen in new[] { "그래도 올리기", "키보드에 올림", "올리지 않은 변경 2개", "보는 층" })
            Assert.Contains(Terms.Banned, b => screen.Contains(b.Word));
    }

    /// <summary>주석을 뺀 소스 — 화면 글(글자열) · 코드만 남긴다. C#: // · /* */ (글자열 · 문자 안의 // 는 그대로) · XAML: &lt;!-- --&gt;</summary>
    internal static string StripComments(string text, bool xaml)
    {
        if (xaml) return Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);
        var sb = new StringBuilder(text.Length);
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];
            if (c == '/' && i + 1 < n && text[i + 1] == '/') { while (i < n && text[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < n && text[i + 1] == '*') { int e = text.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? n : e + 2; continue; }
            if (c == '"' || c == '\'')
            {
                bool verbatim = c == '"' && i > 0 && (text[i - 1] == '@' || (i > 1 && text[i - 1] == '$' && text[i - 2] == '@'));
                sb.Append(c); i++;
                while (i < n)
                {
                    char d = text[i]; sb.Append(d); i++;
                    if (verbatim)
                    {
                        if (d == '"') { if (i < n && text[i] == '"') { sb.Append('"'); i++; continue; } break; }
                        continue;
                    }
                    if (d == '\\' && i < n) { sb.Append(text[i]); i++; continue; }
                    if (d == c || d == '\n') break;
                }
                continue;
            }
            sb.Append(c); i++;
        }
        return sb.ToString();
    }

    [Fact]
    public void StripComments_DropsCommentsKeepsScreenText()
    {
        var src = "// 올림 주석\nvar a = \"업로드 // 끝\"; /* 층\n주석 */ var c = '\"'; var t = \"그래도 올리기\"; var v = @\"C:\\x \"\"층\"\"\";";
        var s = StripComments(src, xaml: false);
        Assert.DoesNotContain("올림 주석", s);
        Assert.DoesNotContain("주석 */", s);
        Assert.Contains("\"업로드 // 끝\"", s);
        Assert.Contains("그래도 올리기", s);
        Assert.Contains("\"\"층\"\"", s);
        var x = StripComments("<!-- 보는 층 --><TextBlock Text=\"보는 레이어\"/>", xaml: true);
        Assert.DoesNotContain("층", x);
        Assert.Contains("보는 레이어", x);
    }

    [Fact]
    public void ScreenTextHasNoBannedWords()
    {
        // 앱(.cs · .xaml) + Core(.cs) 의 주석을 뺀 글. Terms.cs 는 금지 목록 자리(BANNED-BEGIN ~ END)만 뺀다.
        var files = AppSources().Concat(Sources(Path.Combine(WindowsDir(), "Ch57x.Core"), xaml: false));
        var hits = new List<string>();
        foreach (var (file, raw) in files)
        {
            var text = raw;
            if (file == "Terms.cs")
            {
                int a = text.IndexOf("BANNED-BEGIN", StringComparison.Ordinal), b = text.IndexOf("BANNED-END", StringComparison.Ordinal);
                Assert.True(a > 0 && b > a, "Terms.cs 금지 목록 표지");
                text = text[..a] + text[b..];
            }
            text = StripComments(text, file.EndsWith(".xaml"));
            var lines = text.Split('\n');
            for (int ln = 0; ln < lines.Length; ln++)
                foreach (var (w, use) in Terms.Banned)
                    if (lines[ln].Contains(w)) hits.Add($"{file}: 「{w}」 → {use} · {lines[ln].Trim()}");
        }
        Assert.Empty(hits);
    }
}
