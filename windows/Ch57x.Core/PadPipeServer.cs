using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Ch57x.Core;

/// <summary>Start 결과 — Ok 받는 중 · NameTaken 같은 이름 파이프를 다른 프로그램이 먼저 엶(받지 않음) · Error 그 밖</summary>
public enum PadServerStart { Ok, NameTaken, Error }

/// <summary>
/// 권한 질문 파이프 서버 (PC 앱 0.3.1) — \\.\pipe\ch57x-agent-pad-&lt;사용자&gt; · 줄 단위 JSON v1.
/// Windows: 현재 사용자만(ACL: 내 SID 허용 · NETWORK 거부 — 다른 PC 에서 같은 계정으로 붙는 것도 막음) · 첫 인스턴스만(FirstPipeInstance — 먼저 연 다른 프로그램이 있으면 받지 않음).
/// 연결 여럿 동시(VS Code 창마다) · 64KB 넘는 줄 · 약속에 안 맞는 줄은 그 연결을 끊는다 · 답은 그 질문을 보낸 연결에만.
/// 리눅스(시험)에서는 같은 이름의 유닉스 소켓(.NET 이 /tmp/CoreFxPipe_&lt;이름&gt;)으로 돈다 — ACL 없음.
/// </summary>
public sealed class PadPipeServer : IDisposable
{
    public const int MaxConnections = 16;
    /// <summary>붙고 나서 질문을 안 보내거나 마지막 질문이 끝난 뒤 이만큼 조용하면 끊는다</summary>
    public const int IdleMs = 30000;

    private readonly string _name;
    private readonly PermissionQueue _queue;
    private readonly Action<string> _log;
    private readonly object _lock = new();
    private Gen? _gen;
    private Timer? _sweep;

    /// <summary>Start 한 번의 상태 — 멈춘 뒤 다시 켜도 앞 세대의 끝난 연결이 새 세대의 수를 건드리지 않게</summary>
    private sealed class Gen
    {
        public readonly CancellationTokenSource Cts = new();
        public readonly List<Conn> Conns = new();
        /// <summary>
        /// 지금 연결을 기다리는 인스턴스(처음엔 Start 가 만든 첫 인스턴스). Stop 이 잠금 안에서 바로 닫는다 —
        /// 취소만 걸면 닫힘이 비동기로 늦어, 곧바로 Start 하면 첫 인스턴스가 거부(NameTaken)됐다(0.3.1 마무리 CI 실측).
        /// </summary>
        public NamedPipeServerStream? Waiting;
        /// <summary>살아 있는 서버 인스턴스 수 (듣는 중 + 붙은 것)</summary>
        public int Live;
    }

    public string PipeName => _name;
    public bool Running { get { lock (_lock) return _gen != null; } }

    public PadPipeServer(string pipeName, PermissionQueue queue, Action<string>? log = null)
    {
        _name = pipeName;
        _queue = queue;
        _log = log ?? (_ => { });
    }

    /// <summary>지금 사용자의 파이프 이름 (GetUserNameW — node os.userInfo().username 과 같은 함수)</summary>
    public static string DefaultName() => PadPipeName.For(CurrentUser());

    public static string CurrentUser()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var sb = new StringBuilder(257);
                int n = sb.Capacity;
                if (GetUserNameW(sb, ref n)) return sb.ToString();
            }
            catch { /* 아래로 */ }
        }
        return Environment.UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserNameW(StringBuilder lpBuffer, ref int pcbBuffer);

    /// <summary>파이프 보안 — 내 SID 만 허용 · NETWORK 거부 · 소유자 = 나 · 상속 없음</summary>
    [SupportedOSPlatform("windows")]
    public static PipeSecurity BuildSecurity()
    {
        var me = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("현재 사용자 SID 를 못 읽음");
        var ps = new PipeSecurity();
        ps.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        ps.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
        ps.SetOwner(me);
        return ps;
    }

    /// <summary>서버 인스턴스 하나. first = 이 이름의 첫 인스턴스여야 함(이미 있으면 Windows 가 거부 → UnauthorizedAccessException)</summary>
    public static NamedPipeServerStream CreateInstance(string name, bool first)
    {
        if (OperatingSystem.IsWindows())
        {
            var opt = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
            return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, MaxConnections, PipeTransmissionMode.Byte, opt, 0, 0, BuildSecurity());
        }
        return new NamedPipeServerStream(name, PipeDirection.InOut, MaxConnections, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    /// <summary>받기 시작. 첫 인스턴스를 바로 만들어 이름 선점 여부를 돌려준다.</summary>
    public PadServerStart Start()
    {
        Gen g;
        lock (_lock)
        {
            if (_gen != null) return PadServerStart.Ok;
            g = new Gen();
            try { g.Waiting = CreateInstance(_name, first: true); }
            catch (UnauthorizedAccessException) { _log($"권한 질문 파이프 「{_name}」 — 같은 이름을 다른 프로그램이 이미 엶 · 받지 않음"); return PadServerStart.NameTaken; }
            catch (Exception ex) { _log($"권한 질문 파이프 열기 실패: {ex.GetType().Name} — {ex.Message}"); return PadServerStart.Error; }
            g.Live = 1;
            _gen = g;
            _sweep = new Timer(_ => Sweep(), null, 1000, 1000);
        }
        _ = Task.Run(() => AcceptLoop(g));
        _log($"권한 질문 받는 중 · 파이프 {_name}");
        return PadServerStart.Ok;
    }

    /// <summary>받기 끔 — 듣기 · 연결을 모두 닫음(확장은 끊기면 VS Code 알림으로 넘어간다)</summary>
    public void Stop()
    {
        List<Conn> conns;
        Gen g;
        lock (_lock)
        {
            if (_gen == null) return;
            g = _gen;
            _gen = null;
            _sweep?.Dispose();
            _sweep = null;
            g.Cts.Cancel();
            try { g.Waiting?.Dispose(); } catch { }   // 기다리던 인스턴스를 지금 닫아 이름을 바로 비운다
            g.Waiting = null;
            conns = g.Conns.ToList();
        }
        foreach (var c in conns) c.Kill();
        _log("권한 질문 받기 멈춤");
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(Gen g)
    {
        var ct = g.Cts.Token;
        NamedPipeServerStream? next;
        lock (_lock) next = g.Waiting;   // Start 가 만든 첫 인스턴스 (g.Waiting 에 그대로 — Stop 이 닫을 수 있게)
        while (!ct.IsCancellationRequested)
        {
            if (next == null)
            {
                try
                {
                    // 만들기 · 자리 적기를 한 잠금 안에서 — Stop 과 엇갈려 닫히지 않은 인스턴스가 남는 틈이 없게
                    lock (_lock)
                    {
                        if (ct.IsCancellationRequested) break;
                        bool first = g.Live == 0;   // 우리 인스턴스가 하나도 없으면 다시 「첫 인스턴스」로 (그 사이 남이 열었으면 거부됨)
                        next = CreateInstance(_name, first);
                        g.Live++;
                        g.Waiting = next;
                    }
                }
                catch (UnauthorizedAccessException) { _log("권한 질문 파이프 — 다른 프로그램이 같은 이름을 엶 · 1초 뒤 다시"); await Delay(1000, ct); continue; }
                catch (IOException) { await Delay(200, ct); continue; }   // 연결 수 한도 — 하나가 끝나기를 기다림
                catch (Exception ex) { _log($"권한 질문 파이프 인스턴스 실패: {ex.GetType().Name} — {ex.Message}"); await Delay(1000, ct); continue; }
            }
            var s = next;
            next = null;
            try { await s.WaitForConnectionAsync(ct).ConfigureAwait(false); }
            catch
            {
                lock (_lock) { if (g.Waiting == s) g.Waiting = null; g.Live--; }
                s.Dispose();
                if (ct.IsCancellationRequested) return;
                continue;
            }
            var conn = new Conn(this, g, s);
            bool stopped;
            lock (_lock)
            {
                if (g.Waiting == s) g.Waiting = null;
                stopped = ct.IsCancellationRequested;
                if (!stopped) g.Conns.Add(conn);
            }
            if (stopped) { s.Dispose(); return; }
            _ = Task.Run(() => conn.Run(ct));
        }
    }

    private static async Task Delay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct).ConfigureAwait(false); } catch (OperationCanceledException) { }
    }

    private void Ended(Gen g, Conn c)
    {
        lock (_lock)
        {
            if (g.Conns.Remove(c)) g.Live--;
        }
    }

    /// <summary>조용한 연결 끊기 — 질문 없이 붙어만 있거나 끝난 뒤 30초</summary>
    private void Sweep()
    {
        long now = PadProtocol.NowMs();
        List<Conn> idle;
        lock (_lock)
        {
            if (_gen == null) return;
            idle = _gen.Conns.Where(c => !_queue.HasOwner(c) && now - c.QuietSince > IdleMs).ToList();
        }
        foreach (var c in idle) { _log("권한 질문 연결 — 30초 조용해 끊음"); c.Kill(); }
    }

    /// <summary>연결 하나 (VS Code 창 하나의 질문 하나 — pcPipe.ts 는 질문마다 새로 붙는다)</summary>
    private sealed class Conn : IPadOwner
    {
        private readonly PadPipeServer _srv;
        private readonly Gen _gen;
        private readonly NamedPipeServerStream _s;
        private readonly SemaphoreSlim _write = new(1, 1);
        private int _dead;
        public long QuietSince = PadProtocol.NowMs();

        public Conn(PadPipeServer srv, Gen gen, NamedPipeServerStream s) { _srv = srv; _gen = gen; _s = s; }

        public async Task Run(CancellationToken ct)
        {
            var split = new LineSplitter();
            var buf = new byte[4096];
            var lines = new List<string>();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n = await _s.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false);
                    if (n <= 0) break;
                    lines.Clear();
                    if (!split.Push(buf.AsSpan(0, n), lines)) { _srv._log("권한 질문 연결 — 한 줄이 64KB 를 넘어 끊음"); break; }
                    bool keep = true;
                    foreach (var line in lines)
                        if (!(keep = Handle(line))) break;
                    if (!keep) break;
                }
            }
            catch { /* 끊김 · 멈춤 */ }
            finally
            {
                Kill();
                int n = _srv._queue.Drop(this);
                if (n > 0) _srv._log($"권한 질문 연결 끊김 — {n}건 닫음");
                _srv.Ended(_gen, this);
            }
        }

        /// <summary>한 줄 처리 — false 면 연결을 끊는다</summary>
        private bool Handle(string line)
        {
            if (line.Length == 0) return true;
            switch (PadProtocol.Parse(line, PadProtocol.NowMs()))
            {
                case PadMessage.Permission p:
                    QuietSince = PadProtocol.NowMs();
                    if (!_srv._queue.Submit(p.Request, this)) _srv._log($"권한 질문 {Short(p.Request.RequestId)} — 이미 받았거나 시간이 지나 안 띄움");
                    return true;
                case PadMessage.Close c:
                    QuietSince = PadProtocol.NowMs();
                    _srv._queue.Close(c.RequestId, this);
                    return true;
                case PadMessage.Unknown:
                    return true;
                case PadMessage.Invalid bad:
                    _srv._log($"권한 질문 연결 — 약속에 안 맞는 줄({bad.Reason})이라 끊음");
                    return false;
            }
            return true;
        }

        public void Respond(string requestId, PadDecision decision)
        {
            QuietSince = PadProtocol.NowMs();
            var bytes = Encoding.UTF8.GetBytes(PadProtocol.DecisionLine(requestId, decision));
            _ = WriteAsync(bytes);
        }

        private async Task WriteAsync(byte[] bytes)
        {
            if (Volatile.Read(ref _dead) != 0) return;
            await _write.WaitAsync().ConfigureAwait(false);
            try
            {
                await _s.WriteAsync(bytes).ConfigureAwait(false);
                await _s.FlushAsync().ConfigureAwait(false);
            }
            catch { /* 확장이 먼저 끊음 */ }
            finally { _write.Release(); }
        }

        public void Kill()
        {
            if (Interlocked.Exchange(ref _dead, 1) != 0) return;
            try { _s.Dispose(); } catch { }
        }
    }

    public static string Short(string id) => id.Length > 8 ? id[..8] : id;
}
