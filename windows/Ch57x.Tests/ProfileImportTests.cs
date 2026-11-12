using Ch57x.Core;
using Xunit;

namespace Ch57x.Tests;

/// <summary>판정 S7 — 프로필 가져오기 = 새 프로필 · 병합이 LED · 이름을 덮어쓰던 결함</summary>
public class ProfileImportTests
{
    private const string NoLed = """{ "name": "", "layers": [ {}, { "19": { "type": "key", "steps": [ { "mods": ["Ctrl"], "code": "F16" } ], "alias": "이전 세션" } }, {} ] }""";
    private const string WithLed = """{ "name": "패드", "layers": [ {}, { "19": { "type": "key", "steps": [ { "mods": ["Ctrl"], "code": "F16" } ] } }, {} ], "led": [ { "mode": 0, "color": 1 }, { "mode": 1, "color": 6 }, { "mode": 0, "color": 1 } ] }""";

    private static Profile Mine()
    {
        var p = new Profile { Name = "My001" };
        p.Layers[0][1] = Binding.Key("A");
        p.Layers[1][9] = Binding.Key("C");
        p.Led[0] = new LedSetting { Mode = 1, Color = 4 };
        p.Led[1] = new LedSetting { Mode = 1, Color = 2 };
        return p;
    }

    [Fact]
    public void DeserializeWithInfo_TellsWhatTheFileReallyHad()
    {
        var a = ProfileStore.DeserializeWithInfo(NoLed);
        Assert.False(a.HasLed);
        Assert.False(a.HasName);
        Assert.Equal(3, a.Profile.Led.Count);   // Deserialize 는 그래도 채운다 — 그래서 HasLed 가 필요
        var b = ProfileStore.DeserializeWithInfo(WithLed);
        Assert.True(b.HasLed);
        Assert.True(b.HasName);
    }

    [Fact]
    public void Merge_FileWithoutLed_KeepsMyLedAndName()
    {
        var me = Mine();
        int n = ProfileImport.MergeInto(me, ProfileStore.DeserializeWithInfo(NoLed));
        Assert.Equal(1, n);
        Assert.Equal("My001", me.Name);
        Assert.Equal(1, me.Led[0].Mode);
        Assert.Equal(4, me.Led[0].Color);
        Assert.Equal(2, me.Led[1].Color);
        Assert.Equal("A", me.Layers[0][1].Steps![0].Code);   // 다른 층 그대로
        Assert.Equal("F16", me.Layers[1][19].Steps![0].Code);
        Assert.Equal("C", me.Layers[1][9].Steps![0].Code);   // 파일에 없는 키 그대로
    }

    [Fact]
    public void Merge_FileWithLedAndName_ChangesThem()
    {
        var me = Mine();
        ProfileImport.MergeInto(me, ProfileStore.DeserializeWithInfo(WithLed));
        Assert.Equal("패드", me.Name);
        Assert.Equal(0, me.Led[0].Mode);
        Assert.Equal(6, me.Led[1].Color);
    }

    [Fact]
    public void AsNewProfile_DoesNotTouchCurrent_NameFromFileOrStem_Unique()
    {
        var me = Mine();
        var before = ProfileStore.Serialize(me);
        var (p1, n1) = ProfileImport.AsNewProfile(ProfileStore.DeserializeWithInfo(NoLed), "ch57x-agent-pad-layer2", new[] { "기본", "My001" });
        Assert.Equal("ch57x-agent-pad-layer2", n1);
        Assert.Equal(n1, p1.Name);
        var (_, n2) = ProfileImport.AsNewProfile(ProfileStore.DeserializeWithInfo(WithLed), "x", new[] { "기본", "패드" });
        Assert.Equal("패드 (2)", n2);
        Assert.Equal(before, ProfileStore.Serialize(me));
    }
}
