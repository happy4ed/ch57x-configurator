using Ch57x.Core;
using Xunit;

namespace Ch57x.Tests;

/// <summary>판정 S5 실행 취소 · S12 상용구 · E1 변경 수 · S2 자동실행 명령 · S9 프로필 이름 · 요약 글</summary>
public class LogicTests
{
    [Fact]
    public void History_UndoesInReverseOrder_WithLabels_AndLimit()
    {
        var h = new ProfileHistory();
        var p = new Profile();
        Assert.False(h.CanUndo);
        h.Push(p, "키 1 바꿈");
        p.Layers[0][1] = Binding.Key("A");
        h.Push(p, "조명 바꿈");
        p.Led[0].Mode = 1;
        Assert.Equal("조명 바꿈", h.NextLabel);
        var u1 = h.Undo()!;
        Assert.Equal(0, u1.Led[0].Mode);
        Assert.True(u1.Layers[0].ContainsKey(1));
        var u2 = h.Undo()!;
        Assert.False(u2.Layers[0].ContainsKey(1));
        Assert.Null(h.Undo());
        for (int i = 0; i < ProfileHistory.Limit + 7; i++) h.Push(p, $"{i}");
        Assert.Equal(ProfileHistory.Limit, h.Count);
        h.Clear();
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void TextMacro_CountsTypableKeys_ListsUnsupported_Limit18()
    {
        var a = TextMacro.Analyze("Best regards 안녕");
        Assert.Equal(13, a.Typable);
        Assert.Equal(new[] { '안', '녕' }, a.Unsupported);
        Assert.Equal("13 / 18", TextMacro.Counter(a));
        Assert.Equal("「안」「녕」은 넣을 수 없음 — 영문 · 숫자 · 기호만", TextMacro.Warning(a));
        Assert.Null(TextMacro.Warning(TextMacro.Analyze("Hello, World!")));
        var over = TextMacro.Analyze(new string('a', 19));
        Assert.True(over.OverLimit);
        Assert.Equal("18자까지만 키보드에 들어감", TextMacro.Warning(over));
        Assert.Equal(18, Protocol.TextToSteps(new string('a', 19)).Count);   // 펌웨어 한도와 같은 수
        Assert.True(Protocol.CanType('~'));
        Assert.False(Protocol.CanType('가'));
    }

    [Fact]
    public void Diff_CountsChangedKeysAndLedLayers()
    {
        var a = new Profile();
        a.Layers[2][1] = Binding.Key("F1", new[] { "Ctrl" });
        a.Layers[2][8] = Binding.Key("Space", new[] { "Alt" });
        a.Led[2] = new LedSetting { Mode = 1, Color = 4 };
        var b = ProfileStore.Deserialize(ProfileStore.Serialize(a));
        Assert.Equal(0, ProfileDiff.Count(a, b));
        b.Layers[2][8] = Binding.Key("Tab", new[] { "Alt" });
        b.Layers[2][9] = Binding.Key("End");
        Assert.Equal(2, ProfileDiff.Count(a, b));
        b.Layers[0][5] = new Binding { Type = BindingType.None };   // 비움 = 없음
        Assert.Equal(2, ProfileDiff.Count(a, b));
        b.Led[0] = new LedSetting { Mode = 0, Color = 7 };            // 끄기면 색은 상관없음
        Assert.Equal(2, ProfileDiff.Count(a, b));
        b.Led[2] = new LedSetting { Mode = 1, Color = 6 };
        Assert.Equal(3, ProfileDiff.Count(a, b));
    }

    [Fact]
    public void AutoStartCommand_FormatParse()
    {
        var exe = @"C:\Users\u\AppData\Local\Programs\Ch57x\CH57x 설정기.exe";
        var raw = AutoStartCommand.Format(exe);
        Assert.Equal("\"" + exe + "\" --tray", raw);
        Assert.Equal(exe, AutoStartCommand.ParseExe(raw));
        Assert.Equal(exe, AutoStartCommand.ParseExe("\"" + exe + "\""));            // 0.2.1 형식
        Assert.Equal(@"C:\x\app.exe", AutoStartCommand.ParseExe(@"C:\x\app.exe --tray"));
        Assert.Null(AutoStartCommand.ParseExe("  "));
        Assert.True(AutoStartCommand.IsCurrentFormat(raw, exe));
        Assert.False(AutoStartCommand.IsCurrentFormat("\"" + exe + "\"", exe));     // 0.2.1 등록 → 고쳐 씀
        Assert.True(AutoStartCommand.HasTrayArg(new[] { "--TRAY" }));
        Assert.False(AutoStartCommand.HasTrayArg(Array.Empty<string>()));
    }

    [Fact]
    public void ProfileNames_CleanUniqueCopyProblem()
    {
        Assert.Equal("ab", ProfileNames.Clean("a<>:\"/\\|?*b"));
        Assert.Null(ProfileNames.Clean("  "));
        Assert.Null(ProfileNames.Clean("CON"));
        Assert.Equal("My001 (2)", ProfileNames.Unique("My001", new[] { "my001" }));
        Assert.Equal("My001 복사본", ProfileNames.CopyOf("My001", new[] { "My001" }));
        Assert.Equal("My001 복사본 (2)", ProfileNames.CopyOf("My001", new[] { "My001", "My001 복사본" }));
        Assert.Equal("같은 이름의 프로필이 있음", ProfileNames.Problem("기본", new[] { "기본" }));
        Assert.Null(ProfileNames.Problem("기본", new[] { "기본" }, except: "기본"));   // 이름 바꾸기에서 자기 이름
        Assert.NotNull(ProfileNames.Problem("a/b", Array.Empty<string>()));
    }

    [Fact]
    public void Summarize_PlainWords_LikeTheMock()
    {
        Assert.Equal("Ctrl+F1", Summarize.Of(Binding.Key("F1", new[] { "Ctrl" })));
        Assert.Equal("재생/일시정지", Summarize.Of(new Binding { Type = BindingType.Media, Media = "Play" }));
        Assert.Equal("휠 아래", Summarize.Of(new Binding { Type = BindingType.Mouse, Action = "wheel", Delta = -1 }));
        Assert.Equal("Ctrl+휠 위", Summarize.Of(new Binding { Type = BindingType.Mouse, Action = "wheel", Delta = 1, Mods = new() { "Ctrl" } }));
        Assert.Equal("클릭 가운데", Summarize.Of(new Binding { Type = BindingType.Mouse, Action = "click", Buttons = new() { "Middle" } }));
        Assert.Equal("캡쳐", Summarize.Of(new Binding { Type = BindingType.Key, Alias = "캡쳐", Steps = new() { new Accord { Code = "S", Mods = new() { "Shift", "Win" } } } }));
        Assert.Equal("캡쳐 — Shift+Win+S", Summarize.OfFull(new Binding { Type = BindingType.Key, Alias = "캡쳐", Steps = new() { new Accord { Code = "S", Mods = new() { "Shift", "Win" } } } }));
        Assert.Equal("비어 있음", Summarize.Of(null));
        Assert.Equal("상용구 hi", Summarize.Of(new Binding { Type = BindingType.Text, Text = "hi" }));
        foreach (var s in new[] { Summarize.Of(new Binding { Type = BindingType.Media, Media = "Mute" }), Summarize.Of(new Binding { Type = BindingType.Text, Text = "x" }) })
            Assert.DoesNotContain(s, c => char.IsSurrogate(c));   // 그림 글자(이모지) 없음
    }
}
