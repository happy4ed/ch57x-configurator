using Ch57x.Core;
using Xunit;

namespace Ch57x.Tests;

/// <summary>0.3.0 첫 사용 점검 1번 — 오류 알림 본문은 쉬운 말 한 줄 + 할 일 · 기술 말은 「정보 복사」로만.</summary>
public class ErrorTextTests
{
    [Fact]
    public void DeviceTimeout_PlainLine_NoTechWords()
    {
        var ex = new IOException("장치가 응답하지 않음 (HID 쓰기 시간 초과)");
        var p = ErrorText.Of(ex, device: true);
        Assert.Equal("키보드가 응답하지 않음 — 케이블을 다시 꽂고 다시 해 보기", p.Line);
        Assert.DoesNotContain("HID", p.Line);
        Assert.DoesNotContain("시간 초과", p.Line);
        Assert.Equal(p, ErrorText.Of(new TimeoutException("The operation has timed out."), device: true));
        Assert.Equal(p, ErrorText.Of(new AggregateException(new ObjectDisposedException("stream")), device: true));
    }

    [Fact]
    public void ContentErrors_AreNotBlamedOnTheCable()
    {
        var steps = Enumerable.Range(0, 19).Select(_ => new Accord { Code = "A" }).ToList();
        var ex = Assert.Throws<ProfileContentException>(() => Protocol.BuildKeyMessages(1, 0, new Binding { Type = BindingType.Key, Steps = steps }));
        var p = ErrorText.Of(ex, device: true);
        Assert.DoesNotContain("케이블", p.Line);
        Assert.DoesNotContain(ex.Message, p.Line);
    }

    [Fact]
    public void FileErrors_EachSayWhatToDo()
    {
        var cases = new Exception[]
        {
            new UnauthorizedAccessException("Access to the path 'C:\\x.zip' is denied."),
            new FileNotFoundException("Could not find file 'C:\\a.json'."),
            new System.Text.Json.JsonException("'x' is an invalid start of a value. LineNumber: 0"),
            new IOException("The process cannot access the file because it is being used by another process."),
            new NullReferenceException("Object reference not set to an instance of an object."),
        };
        var lines = cases.Select(e => ErrorText.Of(e, device: false).Line).ToList();
        Assert.Equal(lines.Count, lines.Distinct().Count());
        for (int i = 0; i < cases.Length; i++)
        {
            Assert.Contains(" — ", lines[i]);
            Assert.DoesNotContain(cases[i].Message, lines[i]);
            Assert.DoesNotContain("Exception", lines[i]);
        }
    }
}
