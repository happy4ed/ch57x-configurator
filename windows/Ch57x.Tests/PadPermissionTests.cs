using System.Text;
using Ch57x.Core;
using Xunit;

namespace Ch57x.Tests;

/// <summary>PC 앱 0.3.1 권한 질문 받기 — 줄 JSON 해석 · 평문 · 줄 나누기 · 파이프 이름 · 대기열(한 건씩 · request_id · 1초 무시 · 시간 초과)</summary>
public class PadPermissionTests
{
    private const long T0 = 1_800_000_000_000;

    private static string Perm(string id = "r1", string cli = "claude", string extra = "") =>
        $"{{\"type\":\"permission\",\"v\":1,\"request_id\":\"{id}\",\"cli\":\"{cli}\",\"tool\":\"Bash\",\"summary\":\"npm test\",\"where\":\"터미널 「bash」 · repo\",\"timeout_ms\":120000,\"session\":\"abc\",\"yes_all\":\"저장소 로컬 설정에 Bash 허용 규칙 저장\",\"deadline\":{T0 + 120000}{extra}}}";

    // ───────────────────────── 줄 JSON 해석

    [Fact]
    public void Parse_Permission_AllFields()
    {
        var m = Assert.IsType<PadMessage.Permission>(PadProtocol.Parse(Perm(), T0));
        var r = m.Request;
        Assert.Equal("r1", r.RequestId);
        Assert.Equal(PadCli.Claude, r.Cli);
        Assert.Equal("Bash", r.Tool);
        Assert.Equal("npm test", r.Summary);
        Assert.Equal("터미널 「bash」 · repo", r.Where);
        Assert.Equal("abc", r.Session);
        Assert.Equal("저장소 로컬 설정에 Bash 허용 규칙 저장", r.YesAll);
        Assert.Equal(120000, r.TimeoutMs);
        Assert.Equal(T0 + 120000, r.Deadline);
        Assert.Equal(PadCli.Codex, Assert.IsType<PadMessage.Permission>(PadProtocol.Parse(Perm(cli: "codex"), T0)).Request.Cli);
    }

    [Fact]
    public void Parse_RejectsWrongShape_AndKeepsUnknownTypes()
    {
        Assert.IsType<PadMessage.Invalid>(PadProtocol.Parse("{bad", T0));
        Assert.IsType<PadMessage.Invalid>(PadProtocol.Parse("[1,2]", T0));
        Assert.IsType<PadMessage.Invalid>(PadProtocol.Parse(Perm().Replace("\"v\":1", "\"v\":2"), T0));
        Assert.IsType<PadMessage.Invalid>(PadProtocol.Parse(Perm().Replace("\"v\":1,", ""), T0));
        Assert.IsType<PadMessage.Invalid>(PadProtocol.Parse(Perm(cli: "gemini"), T0));
        Assert.IsType<PadMessage.Invalid>(PadProtocol.Parse(Perm(id: ""), T0));
        Assert.IsType<PadMessage.Invalid>(PadProtocol.Parse(Perm(id: new string('a', 129)), T0));
        Assert.IsType<PadMessage.Invalid>(PadProtocol.Parse(Perm().Replace("\"request_id\":\"r1\"", "\"request_id\":7"), T0));
        Assert.Equal("ping", Assert.IsType<PadMessage.Unknown>(PadProtocol.Parse("{\"type\":\"ping\"}", T0)).Type);
        Assert.Equal("r9", Assert.IsType<PadMessage.Close>(PadProtocol.Parse("{\"type\":\"close\",\"request_id\":\"r9\"}", T0)).RequestId);
        Assert.IsType<PadMessage.Invalid>(PadProtocol.Parse("{\"type\":\"close\"}", T0));
    }

    [Fact]
    public void Parse_CutsLikeParseRequest_ByCodePoints()
    {
        // permission.ts parseRequest: tool 80 · summary 121 · where 200 · session 40 · yes_all 200 (Array.from = 코드 포인트)
        string emoji = "\U0001F600";   // 대리 글자 짝 = 한 코드 포인트
        var line = $"{{\"type\":\"permission\",\"v\":1,\"request_id\":\"r\",\"cli\":\"claude\",\"tool\":\"{new string('t', 90)}\",\"summary\":\"{string.Concat(Enumerable.Repeat(emoji, 130))}\",\"where\":\"{new string('w', 250)}\",\"session\":\"{new string('s', 50)}\",\"yes_all\":\"{new string('y', 300)}\"}}";
        var r = Assert.IsType<PadMessage.Permission>(PadProtocol.Parse(line, T0)).Request;
        Assert.Equal(80, r.Tool.Length);
        Assert.Equal(121 * 2, r.Summary.Length);
        Assert.Equal(200, r.Where.Length);
        Assert.Equal(40, r.Session.Length);
        Assert.Equal(200, r.YesAll.Length);
        Assert.Equal("ab", PadProtocol.Cut("abc", 2));
        Assert.Equal("a\uD800", PadProtocol.Cut("a\uD800b", 2));   // 짝 없는 대리 글자도 한 개
    }

    [Fact]
    public void Parse_TimeoutAndDeadline_Clamped()
    {
        // timeout: 없음 · 0 · 음수 → 60초, 130초 넘음 → 130초, 수 모양 글자도 (JS Number)
        PadRequest P(string extra) => Assert.IsType<PadMessage.Permission>(PadProtocol.Parse($"{{\"type\":\"permission\",\"v\":1,\"request_id\":\"r\",\"cli\":\"codex\"{extra}}}", T0)).Request;
        Assert.Equal(60000, P("").TimeoutMs);
        Assert.Equal(T0 + 60000, P("").Deadline);
        Assert.Equal(60000, P(",\"timeout_ms\":0").TimeoutMs);
        Assert.Equal(60000, P(",\"timeout_ms\":-5").TimeoutMs);
        Assert.Equal(130000, P(",\"timeout_ms\":999999").TimeoutMs);
        Assert.Equal(5000, P(",\"timeout_ms\":\"5000\"").TimeoutMs);
        Assert.Equal(T0 + 5000, P(",\"timeout_ms\":5000").Deadline);
        // deadline 이 있으면 그대로 · 너무 멀면 지금 + 130초
        Assert.Equal(T0 + 7000, P(",\"timeout_ms\":5000,\"deadline\":" + (T0 + 7000)).Deadline);
        Assert.Equal(T0 + 130000, P(",\"deadline\":" + (T0 + 999_999)).Deadline);
        Assert.Equal(T0 - 1, P(",\"deadline\":" + (T0 - 1)).Deadline);   // 이미 지남 — 대기열이 받지 않는다
    }

    [Fact]
    public void DecisionLine_ExactBytes()
    {
        Assert.Equal("{\"type\":\"decision\",\"request_id\":\"r1\",\"decision\":\"allow\"}\n", PadProtocol.DecisionLine("r1", PadDecision.Allow));
        Assert.Equal("{\"type\":\"decision\",\"request_id\":\"r1\",\"decision\":\"allow_always\"}\n", PadProtocol.DecisionLine("r1", PadDecision.AllowAlways));
        Assert.Equal("{\"type\":\"decision\",\"request_id\":\"r1\",\"decision\":\"deny\"}\n", PadProtocol.DecisionLine("r1", PadDecision.Deny));
        // 따옴표 · 줄바꿈이 든 ID 도 JSON 으로 (한 줄을 넘지 않음)
        var odd = PadProtocol.DecisionLine("a\"b\nc", PadDecision.Deny);
        Assert.Single(odd.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("a\\u0022b\\nc", odd);
    }

    [Fact]
    public void Plain_ControlsToSpace_InvisibleMarked_MarkupKeptAsText()
    {
        Assert.Equal("rm -rf build", PadProtocol.Plain("rm\t-rf\r\n\u0007build"));
        Assert.Equal("a �evil", PadProtocol.Plain("a ‮evil"));         // 방향 뒤집기 글자
        Assert.Equal("x�y", PadProtocol.Plain("x​y"));                // 폭 0
        Assert.Equal("[링크](https://a.b) <b>굵게</b> _밑줄", PadProtocol.Plain("[링크](https://a.b) <b>굵게</b> _밑줄"));   // 해석 없이 글자 그대로
        Assert.Equal("", PadProtocol.Plain(null));
    }

    [Fact]
    public void KeepWords_JoinsHangulInsideWordsOnly()
    {
        const char J = '\u2060';
        Assert.Equal($"규{J}칙 저{J}장", PadProtocol.KeepWords("규칙 저장"));
        Assert.Equal($"Bash{J}허{J}용", PadProtocol.KeepWords("Bash허용"));
        Assert.Equal("npm test --x", PadProtocol.KeepWords("npm test --x"));   // 한글 없으면 그대로
        Assert.Equal($"「bash」 · 터{J}미{J}널", PadProtocol.KeepWords("「bash」 · 터미널"));
        Assert.Equal("", PadProtocol.KeepWords(""));
    }

    // ───────────────────────── 줄 나누기 (64KB)

    [Fact]
    public void LineSplitter_SplitsAcrossChunks_AndUtf8()
    {
        var s = new LineSplitter();
        var lines = new List<string>();
        var bytes = Encoding.UTF8.GetBytes("{\"a\":\"권한\"}\r\n두번째\n끝");
        // 한글 3바이트 가운데에서 자름
        Assert.True(s.Push(bytes.AsSpan(0, 9), lines));
        Assert.Empty(lines);
        Assert.True(s.Push(bytes.AsSpan(9), lines));
        Assert.Equal(new[] { "{\"a\":\"권한\"}", "두번째" }, lines);
        lines.Clear();
        Assert.True(s.Push(Encoding.UTF8.GetBytes("\n"), lines));
        Assert.Equal(new[] { "끝" }, lines);
    }

    [Fact]
    public void LineSplitter_Over64KB_Overflows()
    {
        var s = new LineSplitter();
        var lines = new List<string>();
        Assert.True(s.Push(new byte[PadProtocol.MaxLineBytes].Select(_ => (byte)'a').ToArray(), lines));
        Assert.False(s.Push(new[] { (byte)'a' }, lines));
        // 64KB 딱 맞는 줄 + 줄바꿈은 됨
        var ok = new LineSplitter();
        var full = Enumerable.Repeat((byte)'b', PadProtocol.MaxLineBytes).Append((byte)'\n').ToArray();
        Assert.True(ok.Push(full, lines));
        Assert.Equal(PadProtocol.MaxLineBytes, lines.Single().Length);
    }

    // ───────────────────────── 파이프 이름 (pcPipe.ts pipePath)

    [Fact]
    public void PipeName_SameAsPcPipeTs()
    {
        // JS: user.replace(/[^A-Za-z0-9._-]/g, '_') — UTF-16 한 칸마다
        Assert.Equal("ch57x-agent-pad-hong.gil_dong-1", PadPipeName.For("hong.gil_dong-1"));
        Assert.Equal("ch57x-agent-pad-___", PadPipeName.For("홍길동"));
        Assert.Equal("ch57x-agent-pad-a_b_c", PadPipeName.For("a b\\c"));
        Assert.Equal("ch57x-agent-pad-x__", PadPipeName.For("x\U0001F600"));   // 대리 글자 둘 = 「_」 둘
        Assert.Equal("ch57x-agent-pad-", PadPipeName.For(""));
    }

    // ───────────────────────── 대기열

    private sealed class Clock { public long Now = T0; }

    private sealed class Owner : IPadOwner
    {
        public readonly List<(string Id, PadDecision D)> Got = new();
        public void Respond(string requestId, PadDecision decision) => Got.Add((requestId, decision));
    }

    private static PadRequest Req(string id, long deadline = T0 + 120000, PadCli cli = PadCli.Claude) =>
        new(id, cli, "Bash", "npm test", "repo", "", "", 120000, deadline);

    [Fact]
    public void Queue_OneAtATime_FirstComeFirst_AnswerByIdOnly()
    {
        var c = new Clock();
        var q = new PermissionQueue(() => c.Now);
        var a = new Owner();
        var b = new Owner();
        Assert.True(q.Submit(Req("A"), a));
        c.Now += 100;
        Assert.True(q.Submit(Req("B"), b));
        Assert.False(q.Submit(Req("B"), b));   // 같은 ID 두 번은 안 받음
        var h = q.Head!;
        Assert.Equal("A", h.Request.RequestId);
        Assert.Equal(1, h.Waiting);
        c.Now += 1000;
        Assert.Equal(PadAnswer.Stale, q.Answer("B", PadDecision.Allow));   // 줄 선 건은 답할 수 없음
        Assert.Equal(PadAnswer.Stale, q.Answer("Z", PadDecision.Allow));
        Assert.Equal(PadAnswer.Answered, q.Answer("A", PadDecision.Deny));
        Assert.Equal(new[] { ("A", PadDecision.Deny) }, a.Got);
        Assert.Empty(b.Got);   // 답은 그 질문을 보낸 쪽에만
        Assert.Equal("B", q.Head!.Request.RequestId);
        Assert.Equal(0, q.Head!.Waiting);
        Assert.Equal(PadAnswer.Stale, q.Answer("A", PadDecision.Allow));   // 끝난 건
    }

    [Fact]
    public void Queue_KeyGuard_OneSecondAfterHeadChanges()
    {
        var c = new Clock();
        var q = new PermissionQueue(() => c.Now);
        var o = new Owner();
        q.Submit(Req("A"), o);
        // 처음 뜬 때도 1초
        c.Now += 999;
        Assert.True(q.IsFresh);
        Assert.Equal(PadAnswer.Fresh, q.Answer("A", PadDecision.Allow));
        Assert.Empty(o.Got);
        // 뒤에 줄만 서면 1초 다시 안 셈
        q.Submit(Req("B"), o);
        c.Now += 1;
        Assert.False(q.IsFresh);
        Assert.Equal(PadAnswer.Answered, q.Answer("A", PadDecision.Allow));
        // 맨 앞이 B 로 바뀜 → 다시 1초
        Assert.Equal(PadAnswer.Fresh, q.Answer("B", PadDecision.Allow));
        c.Now += 1000;
        Assert.Equal(PadAnswer.Answered, q.Answer("B", PadDecision.AllowAlways));
        Assert.Equal(new[] { ("A", PadDecision.Allow), ("B", PadDecision.AllowAlways) }, o.Got);
    }

    [Fact]
    public void Queue_Timeout_ClosesWithoutAnswer()
    {
        var c = new Clock();
        var q = new PermissionQueue(() => c.Now);
        var o = new Owner();
        var done = new List<string>();
        q.Done += (r, d, why) => done.Add($"{r.RequestId}:{d?.ToString() ?? "-"}:{why}");
        Assert.False(q.Submit(Req("old", deadline: T0), o));   // 이미 지난 건은 띄우지 않음
        q.Submit(Req("A", deadline: T0 + 1500), o);
        q.Submit(Req("B", deadline: T0 + 5000), o);
        c.Now = T0 + 1499;
        Assert.Equal(0, q.Tick());
        c.Now = T0 + 1500;
        Assert.Equal(PadAnswer.Stale, q.Answer("A", PadDecision.Allow));   // 시간이 지난 건은 답하지 않음 (Tick 전이라도)
        Assert.Equal(1, q.Tick());
        Assert.Equal("B", q.Head!.Request.RequestId);
        Assert.Equal(new[] { "A:-:timeout" }, done);
        Assert.Empty(o.Got);
        Assert.Equal(4, q.Head!.SecondsLeft(c.Now));   // 3.5초 → 올림 4
    }

    [Fact]
    public void Queue_Close_OnlyFromOwner_Drop_Clear()
    {
        var c = new Clock();
        var q = new PermissionQueue(() => c.Now);
        var a = new Owner();
        var b = new Owner();
        int changed = 0;
        q.Changed += () => changed++;
        q.Submit(Req("A"), a);
        q.Submit(Req("B"), b);
        q.Submit(Req("C"), b);
        Assert.False(q.Close("A", b));   // 다른 연결이 보낸 close 는 버림
        Assert.Equal("A", q.Head!.Request.RequestId);
        c.Now += 500;
        Assert.True(q.Close("A", a));
        Assert.Equal("B", q.Head!.Request.RequestId);
        Assert.True(q.IsFresh);   // 닫혀서 맨 앞이 바뀜 → 1초 다시
        Assert.Equal(2, q.Drop(b));
        Assert.Null(q.Head);
        q.Submit(Req("D"), a);
        Assert.Equal(1, q.Clear());
        Assert.Equal(0, q.Count);
        Assert.Empty(a.Got);
        Assert.Empty(b.Got);
        Assert.True(changed >= 6);
    }
}
