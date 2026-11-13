using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Ch57x.Core;
using Xunit;

namespace Ch57x.Tests;

/// <summary>Windows 에서만 도는 시험 (리눅스에서는 건너뜀으로 보인다)</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows 만 (파이프 ACL · 첫 인스턴스)"; }
}

/// <summary>
/// 권한 질문 파이프 서버 — 실제 파이프로 왕복(리눅스는 .NET 이 유닉스 소켓으로 · Windows 는 진짜 named pipe).
/// node(pcPipe.ts 방식) 왕복은 CI 의 「Pad pipe round trip」 단계(windows/tools/pad-pipe-roundtrip.js).
/// </summary>
public class PadPipeServerTests
{
    private static string NewName() => "ch57x-pad-test-" + Guid.NewGuid().ToString("N")[..12];

    private static async Task<NamedPipeClientStream> Connect(string name)
    {
        var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await c.ConnectAsync(3000);
        return c;
    }

    private static async Task Send(Stream s, string line)
    {
        var b = Encoding.UTF8.GetBytes(line + "\n");
        await s.WriteAsync(b);
        await s.FlushAsync();
    }

    /// <summary>한 줄 읽기 (시간 안에 못 읽으면 null · 끊기면 null)</summary>
    private static async Task<string?> ReadLine(Stream s, int ms = 3000)
    {
        using var cts = new CancellationTokenSource(ms);
        var sb = new List<byte>();
        var one = new byte[1];
        try
        {
            while (true)
            {
                int n = await s.ReadAsync(one, cts.Token);
                if (n == 0) return null;
                if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(sb.ToArray());
                sb.Add(one[0]);
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException) { return null; }
    }

    /// <summary>끊길 때까지 기다림 — 끊겼으면 true</summary>
    private static async Task<bool> Closed(Stream s, int ms = 3000)
    {
        using var cts = new CancellationTokenSource(ms);
        var buf = new byte[256];
        try
        {
            while (true)
            {
                int n = await s.ReadAsync(buf, cts.Token);
                if (n == 0) return true;
            }
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return true; }
        catch (ObjectDisposedException) { return true; }
    }

    private static async Task Until(Func<bool> cond, int ms = 3000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < end) await Task.Delay(20);
        Assert.True(cond(), "시간 안에 안 됨");
    }

    private static string Perm(string id, string tool = "Bash") =>
        $"{{\"type\":\"permission\",\"v\":1,\"request_id\":\"{id}\",\"cli\":\"claude\",\"tool\":\"{tool}\",\"summary\":\"npm test\",\"where\":\"repo\",\"timeout_ms\":120000,\"session\":\"s\",\"yes_all\":\"\",\"deadline\":{PadProtocol.NowMs() + 60000}}}";

    [Fact]
    public async Task RoundTrip_PermissionDecision_TwoConnections_AnswersGoToTheirOwnConnection()
    {
        var name = NewName();
        var q = new PermissionQueue(keyGuardMs: 0);
        using var srv = new PadPipeServer(name, q);
        Assert.Equal(PadServerStart.Ok, srv.Start());
        using var a = await Connect(name);
        using var b = await Connect(name);
        await Send(a, Perm("A"));
        await Until(() => q.Count == 1);
        await Send(b, Perm("B"));
        await Until(() => q.Count == 2);
        Assert.Equal("A", q.Head!.Request.RequestId);
        Assert.Equal(1, q.Head!.Waiting);
        Assert.Equal(PadAnswer.Answered, q.Answer("A", PadDecision.AllowAlways));
        Assert.Equal("{\"type\":\"decision\",\"request_id\":\"A\",\"decision\":\"allow_always\"}", await ReadLine(a));
        Assert.Equal(PadAnswer.Answered, q.Answer("B", PadDecision.Deny));
        Assert.Equal("{\"type\":\"decision\",\"request_id\":\"B\",\"decision\":\"deny\"}", await ReadLine(b));
        // a 에는 B 의 답이 가지 않음
        Assert.Null(await ReadLine(a, 300));
    }

    [Fact]
    public async Task Close_FromOwner_ClosesPopup_AndDisconnectDrops()
    {
        var name = NewName();
        var q = new PermissionQueue(keyGuardMs: 0);
        using var srv = new PadPipeServer(name, q);
        Assert.Equal(PadServerStart.Ok, srv.Start());
        using var a = await Connect(name);
        var b = await Connect(name);
        await Send(a, Perm("A"));
        await Send(b, Perm("B"));
        await Until(() => q.Count == 2);
        // b 가 A 를 닫으려 해도 안 닫힘
        await Send(b, "{\"type\":\"close\",\"request_id\":\"A\"}");
        await Task.Delay(200);
        Assert.Equal(2, q.Count);
        await Send(a, "{\"type\":\"close\",\"request_id\":\"A\"}");
        await Until(() => q.Count == 1);
        Assert.Equal("B", q.Head!.Request.RequestId);
        // b 가 끊으면 B 도 닫힘(답 안 함)
        b.Dispose();
        await Until(() => q.Count == 0);
    }

    [Fact]
    public async Task BadLines_Disconnect_UnknownTypeKept()
    {
        var name = NewName();
        var q = new PermissionQueue(keyGuardMs: 0);
        using var srv = new PadPipeServer(name, q);
        Assert.Equal(PadServerStart.Ok, srv.Start());

        // 64KB 넘는 줄 → 끊음
        using (var big = await Connect(name))
        {
            await big.WriteAsync(Enumerable.Repeat((byte)'a', PadProtocol.MaxLineBytes + 10).ToArray());
            await big.FlushAsync();
            Assert.True(await Closed(big));
        }
        // JSON 아님 → 끊음
        using (var bad = await Connect(name))
        {
            await Send(bad, "{bad");
            Assert.True(await Closed(bad));
        }
        // 판 2 → 끊음 (답 없음)
        using (var v2 = await Connect(name))
        {
            await Send(v2, Perm("X").Replace("\"v\":1", "\"v\":2"));
            Assert.True(await Closed(v2));
        }
        Assert.Equal(0, q.Count);
        // 모르는 type 은 버리고 같은 연결로 계속
        using var ok = await Connect(name);
        await Send(ok, "{\"type\":\"ping\"}");
        await Send(ok, Perm("P"));
        await Until(() => q.Count == 1);
        q.Answer("P", PadDecision.Allow);
        Assert.Equal("{\"type\":\"decision\",\"request_id\":\"P\",\"decision\":\"allow\"}", await ReadLine(ok));
    }

    [Fact]
    public async Task Stop_DisconnectsClients_AndRestartWorks()
    {
        var name = NewName();
        var q = new PermissionQueue(keyGuardMs: 0);
        using var srv = new PadPipeServer(name, q);
        Assert.Equal(PadServerStart.Ok, srv.Start());
        using var a = await Connect(name);
        await Send(a, Perm("A"));
        await Until(() => q.Count == 1);
        srv.Stop();
        Assert.True(await Closed(a));
        await Until(() => q.Count == 0);
        Assert.False(srv.Running);
        Assert.Equal(PadServerStart.Ok, srv.Start());
        using var b = await Connect(name);
        await Send(b, Perm("B"));
        await Until(() => q.Count == 1);
        q.Answer("B", PadDecision.Allow);
        Assert.NotNull(await ReadLine(b));
    }

    [Fact]
    public async Task StopThenStart_RightAway_NotNameTaken()
    {
        // 0.3.1 마무리 CI 실측: Stop 이 기다리던 인스턴스를 취소만 걸고 바로 닫지 않아, 곧바로 Start 하면 첫 인스턴스가 거부됐다(NameTaken)
        var name = NewName();
        var q = new PermissionQueue(keyGuardMs: 0);
        using var srv = new PadPipeServer(name, q);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(PadServerStart.Ok, srv.Start());
            if (i % 2 == 1) await Task.Delay(20);   // 받기 고리가 첫 인스턴스로 기다리기 시작한 뒤에도
            srv.Stop();
        }
        // 연결을 받은 뒤(받기 고리가 다음 인스턴스를 만들어 기다림) 바로 끄고 켜기
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(PadServerStart.Ok, srv.Start());
            using var a = await Connect(name);
            await Send(a, Perm("R" + i));
            await Until(() => q.Count == 1);
            await Task.Delay(20);
            srv.Stop();
            await Until(() => q.Count == 0);
        }
        Assert.Equal(PadServerStart.Ok, srv.Start());
        using var b = await Connect(name);
        await Send(b, Perm("B"));
        await Until(() => q.Count == 1);
        q.Answer("B", PadDecision.Allow);
        Assert.NotNull(await ReadLine(b));
    }

    // ───────────────────────── Windows: 현재 사용자만 · 첫 인스턴스만

    [WindowsFact]
    public void Acl_OnlyCurrentUser_NetworkDenied()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = NewName();
        using var s = PadPipeServer.CreateInstance(name, first: true);
        var sec = s.GetAccessControl();
        var me = WindowsIdentity.GetCurrent().User!;
        var rules = sec.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>()
            .Select(r => ((SecurityIdentifier)r.IdentityReference, r.AccessControlType, r.PipeAccessRights)).ToList();
        // 규칙은 딱 둘: 나 허용 · NETWORK 거부 — Everyone · Users · 익명 · 다른 사용자 없음
        Assert.Equal(2, rules.Count);
        Assert.Contains(rules, r => r.Item1 == me && r.Item2 == AccessControlType.Allow && r.Item3 == PipeAccessRights.FullControl);
        Assert.Contains(rules, r => r.Item1.IsWellKnown(WellKnownSidType.NetworkSid) && r.Item2 == AccessControlType.Deny);
        Assert.DoesNotContain(rules, r => r.Item2 == AccessControlType.Allow && r.Item1 != me);
        Assert.True(sec.AreAccessRulesProtected);
        Assert.Equal(me, sec.GetOwner(typeof(SecurityIdentifier)));
    }

    [WindowsFact]
    public void FirstInstance_RefusesWhenNameAlreadyOpen()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = NewName();
        // 다른 프로그램이 먼저 같은 이름을 열었다고 침(ACL 없이 · 여러 인스턴스 허용)
        using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Assert.Throws<UnauthorizedAccessException>(() => PadPipeServer.CreateInstance(name, first: true));
        var q = new PermissionQueue();
        using var srv = new PadPipeServer(name, q);
        Assert.Equal(PadServerStart.NameTaken, srv.Start());
        Assert.False(srv.Running);
    }

    [WindowsFact]
    public void FirstInstance_OurOwnSecondInstanceStillWorks()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = NewName();
        using var first = PadPipeServer.CreateInstance(name, first: true);
        using var second = PadPipeServer.CreateInstance(name, first: false);   // 연결을 여럿 받으려면 두 번째부터는 플래그 없이
        Assert.Throws<UnauthorizedAccessException>(() => PadPipeServer.CreateInstance(name, first: true));
    }

    [WindowsFact]
    public void DefaultName_UsesGetUserName()
    {
        if (!OperatingSystem.IsWindows()) return;
        // node os.userInfo().username(libuv GetUserNameW) · USERNAME 환경 변수 — 보통 같다. 이름 규칙은 PipeName_SameAsPcPipeTs
        Assert.Equal(PadPipeName.For(Environment.GetEnvironmentVariable("USERNAME") ?? ""), PadPipeServer.DefaultName());
    }
}
