using System.Text.RegularExpressions;
using Xunit;

namespace Ch57x.Tests;

/// <summary>테마 사전 — 라이트 · 다크 짝(전사 규칙 · DESIGN.md) · 스타일 열쇠가 색 열쇠와 겹치지 않음(0.3.0 첫 CI 렌더 실측 결함)</summary>
public class ThemeTests
{
    private static string Themes()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Ch57x.sln"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "Ch57x.App", "Themes");
    }

    private static HashSet<string> Keys(string file) =>
        Regex.Matches(File.ReadAllText(Path.Combine(Themes(), file)), "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void LightAndDarkHaveTheSameKeys()
    {
        var l = Keys("Light.xaml");
        var d = Keys("Dark.xaml");
        Assert.Empty(l.Except(d));
        Assert.Empty(d.Except(l));
        Assert.True(l.Count > 40);
    }

    [Fact]
    public void StyleKeysDoNotShadowColorKeys()
    {
        var colors = Keys("Light.xaml");
        var controls = Keys("Controls.xaml");
        Assert.Empty(controls.Intersect(colors));
    }

    [Fact]
    public void EveryDynamicResourceInControlsExists()
    {
        var text = File.ReadAllText(Path.Combine(Themes(), "Controls.xaml"));
        var used = Regex.Matches(text, "DynamicResource ([A-Za-z0-9]+)").Select(m => m.Groups[1].Value).ToHashSet();
        var known = Keys("Light.xaml").Union(Keys("Controls.xaml"))
            .Union(new[] { "Accent", "Accent2", "Accent3", "AppIcon16", "AppIcon48" });   // ThemeManager · AppIcons 가 넣는 열쇠
        Assert.Empty(used.Except(known));
    }
}
