using Ch57x.Core;
using Xunit;

namespace Ch57x.Tests;

/// <summary>판정 S1 — 업로드 보존 (웹 upload() 와 같은 방식 + 「비움」은 되살리지 않음)</summary>
public class UploadPlanTests
{
    private static Dictionary<int, Dictionary<int, Binding>> Device() => new()
    {
        [0] = new() { [1] = Binding.Key("A"), [2] = Binding.Key("B"), [16] = new Binding { Type = BindingType.Media, Media = "VolumeDown" } },
        [1] = new() { [1] = Binding.Key("C"), [19] = Binding.Key("F1", new[] { "Ctrl" }) },
        [2] = new() { [5] = Binding.Key("D") },
    };

    private static List<(int Layer, int Key)> KeysIn(UploadPlanResult r) =>
        r.Messages.Where(m => m[0] == 0x03 && m[1] == 0xfe && m[2] != 0xb0).Select(m => (m[3] - 1, (int)m[2])).ToList();

    [Fact]
    public void PartialProfile_KeepsDeviceKeysItDoesNotOwn()
    {
        // 에이전트 패드처럼 2층 일부만 가진 프로필
        var p = new Profile();
        p.Layers[1][19] = Binding.Key("F16", new[] { "Ctrl" });
        p.Layers[1][20] = Binding.Key("F17", new[] { "Ctrl" });
        var r = UploadPlan.Build(p, Device());
        Assert.Equal(2, r.Mine);
        Assert.Equal(5, r.Kept);   // 0:1 0:2 0:16 1:1 2:5 — 1:19 는 프로필 것
        Assert.Equal(1, r.LayersUsed);
        var keys = KeysIn(r);
        Assert.Equal(new[] { (1, 19), (1, 20), (0, 1), (0, 2), (0, 16), (1, 1), (2, 5) }, keys);
        // 프로필 값이 이긴다: 1:19 는 F16(0x6b) 이지 기기의 F1(0x3a) 이 아님
        var k19 = r.Messages.First(m => m[1] == 0xfe && m[2] == 19 && m[3] == 2);
        Assert.Equal(0x6b, k19[12]);
        Assert.Equal(0x01, k19[11]);
    }

    [Fact]
    public void ReadFailed_NullMeansNothingPreserved()
    {
        var p = new Profile();
        p.Layers[0][1] = Binding.Key("A");
        var r = UploadPlan.Build(p, null);
        Assert.Equal(1, r.Mine);
        Assert.Equal(0, r.Kept);
        Assert.Equal(new[] { (0, 1) }, KeysIn(r));
    }

    [Fact]
    public void ClearedKey_IsNotRevivedFromDevice()
    {
        var p = new Profile();
        p.Layers[0][1] = new Binding { Type = BindingType.None };   // 「이 키 비우기」
        var r = UploadPlan.Build(p, Device());
        Assert.Equal(1, r.Cleared);
        Assert.DoesNotContain((0, 1), KeysIn(r));
        Assert.Contains((0, 2), KeysIn(r));
    }

    [Fact]
    public void ProfileValueWithNothingToSend_KeepsDeviceValue_LikeWeb()
    {
        var p = new Profile();
        p.Layers[0][2] = new Binding { Type = BindingType.Text, Text = "안녕" };   // 넣을 글자 0개 → 패킷 0
        var r = UploadPlan.Build(p, Device());
        Assert.Equal(0, r.Mine);
        Assert.Contains((0, 2), KeysIn(r));
    }

    [Fact]
    public void LedAlwaysSentForAllLayers_OffAndWhiteUseColorZero()
    {
        var p = new Profile();
        p.Led[0] = new LedSetting { Mode = 1, Color = 4 };
        p.Led[1] = new LedSetting { Mode = 0, Color = 6 };
        p.Led[2] = new LedSetting { Mode = 5, Color = 3 };
        var r = UploadPlan.Build(p, new Dictionary<int, Dictionary<int, Binding>>());
        var leds = r.Messages.Where(m => m[1] == 0xfe && m[2] == 0xb0).ToList();
        Assert.Equal(3, leds.Count);
        Assert.Equal((4 << 4) | 1, leds[0][12]);
        Assert.Equal(0, leds[1][12]);
        Assert.Equal(5, leds[2][12]);
    }

    [Fact]
    public void EmptyDeviceRead_MeansDeviceIsEmpty()
    {
        var p = new Profile();
        p.Layers[2][9] = Binding.Key("Z");
        var r = UploadPlan.Build(p, new Dictionary<int, Dictionary<int, Binding>>());
        Assert.Equal(0, r.Kept);
        Assert.Equal(1, r.Mine);
        // 키 1개 = 바인딩 1 + 커밋 3, LED 3층 × 2
        Assert.Equal(4 + 6, r.Messages.Count);
    }
}
