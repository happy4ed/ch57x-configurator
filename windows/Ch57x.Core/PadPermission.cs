using System.Text;
using System.Text.Json;

namespace Ch57x.Core;

// 에이전트 패드 권한 질문 받기 (PC 앱 0.3.1) — 순수 논리. WPF 없이 시험한다.
// 약속 진본: autographer projects/ch57x/vscode-agent-pad/src/pcPipe.ts 머리 주석 · permission.ts parseRequest · NOTES.md 「PC 앱 0.3.0 에 넘길 것」.
//
//   확장 → 앱  {"type":"permission","v":1,request_id,cli,tool,summary,where,timeout_ms,session,yes_all,deadline}
//   앱 → 확장  {"type":"decision","request_id","decision":"allow|allow_always|deny"}   (none 은 보내지 않음 — 시간 초과면 답 안 함)
//   확장 → 앱  {"type":"close","request_id"}
//
//   - 한 번에 한 건(가장 먼저 온 것)만 보인다. 나머지는 줄 선다. 답은 request_id 로만.
//   - 보이는 건이 바뀐 뒤(처음 뜬 때 포함) 1초 동안은 패드 키 · 단추를 받지 않는다(확장 permission.ts 와 같은 규칙).
//   - 시간(deadline)이 지나면 닫고 답하지 않는다(훅이 결정 없이 끝남 → 터미널 권한 창 그대로).

public enum PadCli { Claude, Codex }

/// <summary>Yes · Yes all · No (패드 7 · 8 · 9 = F19 · F20 · F21)</summary>
public enum PadDecision { Allow, AllowAlways, Deny }

/// <summary>받은 권한 질문 (모양을 맞추고 길이를 자른 뒤). Deadline = 끝나는 시각(유닉스 ms · 이 PC 시계)</summary>
public sealed record PadRequest(string RequestId, PadCli Cli, string Tool, string Summary, string Where, string Session, string YesAll, int TimeoutMs, long Deadline);

public abstract record PadMessage
{
    public sealed record Permission(PadRequest Request) : PadMessage;
    public sealed record Close(string RequestId) : PadMessage;
    /// <summary>모르는 type — 버린다(다음 판 메시지와 섞여도 끊지 않게)</summary>
    public sealed record Unknown(string Type) : PadMessage;
    /// <summary>약속에 안 맞음 — 연결을 끊는다(확장은 끊기면 VS Code 알림으로 넘어간다)</summary>
    public sealed record Invalid(string Reason) : PadMessage;
}

public static class PadProtocol
{
    public const int Version = 1;
    /// <summary>한 줄 최대 (pcPipe.ts 받는 쪽과 같은 64KB) — 넘으면 끊는다</summary>
    public const int MaxLineBytes = 64 * 1024;
    /// <summary>훅이 기다리는 최대(Claude 120초) + 여유 — permission.ts MAX_TIMEOUT_MS</summary>
    public const int MaxTimeoutMs = 130000;
    public const int DefaultTimeoutMs = 60000;
    public const int MaxRequestId = 128;

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static string Wire(PadDecision d) => d switch
    {
        PadDecision.Allow => "allow",
        PadDecision.AllowAlways => "allow_always",
        _ => "deny",
    };

    public static string CliName(PadCli c) => c == PadCli.Codex ? "Codex" : "Claude Code";

    /// <summary>앱 → 확장 답 한 줄 (끝 \n 포함)</summary>
    public static string DecisionLine(string requestId, PadDecision d)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", "decision");
            w.WriteString("request_id", requestId);
            w.WriteString("decision", Wire(d));
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }

    /// <summary>한 줄 해석. now = 지금(유닉스 ms) — deadline 이 없거나 너무 멀면 여기서 맞춘다.</summary>
    public static PadMessage Parse(string line, long now)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return new PadMessage.Invalid("JSON 아님"); }
        using (doc)
        {
            try { return ParseRoot(doc.RootElement, now); }
            catch (InvalidOperationException) { return new PadMessage.Invalid("글자가 깨짐"); }
        }
    }

    private static PadMessage ParseRoot(JsonElement o, long now)
    {
        if (o.ValueKind != JsonValueKind.Object) return new PadMessage.Invalid("객체 아님");
        var type = Str(o, "type");
        if (type == "close")
        {
            var cid = Str(o, "request_id");
            return string.IsNullOrEmpty(cid) || cid.Length > MaxRequestId ? new PadMessage.Invalid("close 의 request_id") : new PadMessage.Close(cid);
        }
        if (type != "permission") return new PadMessage.Unknown(type ?? "");

        if (!o.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var ver) || ver != Version)
            return new PadMessage.Invalid("판(v) 이 1 아님");
        var id = Str(o, "request_id");
        if (string.IsNullOrEmpty(id) || id.Length > MaxRequestId) return new PadMessage.Invalid("request_id");
        var cliText = Str(o, "cli");
        PadCli cli;
        if (cliText == "claude") cli = PadCli.Claude;
        else if (cliText == "codex") cli = PadCli.Codex;
        else return new PadMessage.Invalid("cli");

        // 길이는 permission.ts parseRequest 와 같게 (코드 포인트 수)
        double t = Num(o, "timeout_ms");
        int timeout = double.IsFinite(t) && t > 0 ? (int)Math.Min(t, MaxTimeoutMs) : DefaultTimeoutMs;
        double dl = Num(o, "deadline");
        long latest = now + MaxTimeoutMs;
        long deadline = double.IsFinite(dl) && dl > 0 ? (long)Math.Min(dl, latest) : now + timeout;

        return new PadMessage.Permission(new PadRequest(
            id, cli,
            Cut(Str(o, "tool"), 80),
            Cut(Str(o, "summary"), 121),
            Cut(Str(o, "where"), 200),
            Cut(Str(o, "session"), 40),
            Cut(Str(o, "yes_all"), 200),
            timeout, deadline));
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    /// <summary>JS Number(x) 처럼 — 수 · 수 모양 글자만 (그 밖은 NaN)</summary>
    private static double Num(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var e)) return double.NaN;
        if (e.ValueKind == JsonValueKind.Number) return e.GetDouble();
        if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        return double.NaN;
    }

    /// <summary>앞 n 코드 포인트 (JS Array.from(s).slice(0, n) — 짝 없는 대리 글자도 한 개로 센다)</summary>
    public static string Cut(string? s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = 0, count = 0;
        while (i < s.Length && count < n)
        {
            i += char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            count++;
        }
        return i >= s.Length ? s : s[..i];
    }

    /// <summary>
    /// 화면에 그리는 바깥 글 — 평문. 제어 문자(줄바꿈 · 탭 포함)는 공백 하나로, 보이지 않는 방향 · 폭 0 글자는 「�」로(명령이 다르게 보이게 하는 것을 막음).
    /// 링크 · 마크업은 해석하지 않는다 — 앱은 이 글을 TextBlock.Text 로만 넣는다(밑줄 바로 가기 · 링크 문법 없음).
    /// </summary>
    public static string Plain(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s)
        {
            if (c < 0x20 || (c >= 0x7F && c <= 0x9F))
            {
                if (!space) sb.Append(' ');
                space = true;
                continue;
            }
            space = false;
            sb.Append(IsInvisibleFormat(c) ? '�' : c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// 한글 낱말 안에서 줄이 갈리지 않게 — 한글과 붙은 이웃 글자(공백 아님) 사이에 U+2060(WORD JOINER). 띄어쓰기에서만 줄이 바뀐다.
    /// WPF 는 한글을 글자마다 끊는다(0.3.1 첫 CI 렌더 실측: 「허용 규칙 저 / 장」). 한 낱말이 칸보다 길면 TextWrapping.Wrap 이 그래도 끊는다.
    /// Plain 다음에 화면에만 쓴다(받은 글의 U+2060 은 Plain 이 「�」로 바꿔 둠).
    /// </summary>
    public static string KeepWords(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && !char.IsWhiteSpace(s[i - 1]) && !char.IsWhiteSpace(s[i]) && (IsHangul(s[i - 1]) || IsHangul(s[i]))) sb.Append('\u2060');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static bool IsHangul(char c) => (c >= '\uAC00' && c <= '\uD7A3') || (c >= '\u1100' && c <= '\u11FF') || (c >= '\u3130' && c <= '\u318F');

    private static bool IsInvisibleFormat(char c) =>
        c == '؜' || (c >= '​' && c <= '‏') || (c >= '‪' && c <= '‮') || (c >= '⁠' && c <= '⁩') || c == '﻿';
}

/// <summary>바이트를 줄로 — \n 으로 나누고(\r 은 뗌), 줄 하나가 max 바이트를 넘으면 넘침(false).</summary>
public sealed class LineSplitter
{
    private readonly int _max;
    private readonly List<byte> _buf = new();

    public LineSplitter(int maxLineBytes = PadProtocol.MaxLineBytes) { _max = maxLineBytes; }

    /// <summary>받은 조각을 넣고 다 된 줄을 lines 에. 넘치면 false (연결을 끊을 것)</summary>
    public bool Push(ReadOnlySpan<byte> data, List<string> lines)
    {
        foreach (byte b in data)
        {
            if (b == (byte)'\n')
            {
                int n = _buf.Count;
                if (n > 0 && _buf[n - 1] == (byte)'\r') n--;
                lines.Add(Encoding.UTF8.GetString(_buf.GetRange(0, n).ToArray()));
                _buf.Clear();
                continue;
            }
            _buf.Add(b);
            if (_buf.Count > _max) return false;
        }
        return true;
    }
}

/// <summary>파이프 이름 — pcPipe.ts pipePath 와 글자까지 같게: 사용자 이름의 [A-Za-z0-9._-] 밖 글자(UTF-16 한 칸씩)를 「_」로.</summary>
public static class PadPipeName
{
    public const string Prefix = "ch57x-agent-pad-";

    public static string Sanitize(string user)
    {
        var sb = new StringBuilder(user.Length);
        foreach (char c in user)
            sb.Append((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }

    /// <summary>파이프 이름(\\.\pipe\ 뒤) — 「ch57x-agent-pad-사용자」</summary>
    public static string For(string user) => Prefix + Sanitize(user);
}

/// <summary>질문을 보낸 쪽(연결 하나) — 답을 돌려줄 곳</summary>
public interface IPadOwner
{
    void Respond(string requestId, PadDecision decision);
}

/// <summary>지금 보이는 건 (그리기용 사본)</summary>
public sealed record PadView(PadRequest Request, int Waiting, long Deadline, long HeadSince)
{
    public int SecondsLeft(long now) => (int)Math.Max(0, Math.Ceiling((Deadline - now) / 1000.0));
}

/// <summary>답하기 결과 — Answered 답함 · Fresh 바뀐 지 1초 안이라 안 받음 · Stale 보이는 건이 아님(끝났거나 다른 건)</summary>
public enum PadAnswer { Answered, Fresh, Stale }

/// <summary>
/// 권한 질문 대기열 — 여러 연결(VS Code 창)에서 온 질문을 온 순서로 한 건씩. 스레드 안전(파이프 읽기 스레드 · 화면 스레드).
/// 시간 초과는 Tick() 을 부를 때 본다(화면이 250ms 마다).
/// </summary>
public sealed class PermissionQueue
{
    public const int KeyGuardMs = 1000;

    private sealed class Item
    {
        public required PadRequest Req;
        public required IPadOwner Owner;
    }

    private readonly object _lock = new();
    private readonly List<Item> _items = new();
    private readonly Func<long> _now;
    private readonly int _guardMs;
    private string? _headId;
    private long _headSince;

    /// <summary>보이는 건 · 대기 수가 바뀜 (아무 스레드에서나 — 받는 쪽이 화면 스레드로 옮긴다)</summary>
    public event Action? Changed;
    /// <summary>끝난 건 — 기록용 (why: answer · close · timeout · drop · clear)</summary>
    public event Action<PadRequest, PadDecision?, string>? Done;

    public PermissionQueue(Func<long>? now = null, int keyGuardMs = KeyGuardMs)
    {
        _now = now ?? PadProtocol.NowMs;
        _guardMs = keyGuardMs;
    }

    public int Count { get { lock (_lock) return _items.Count; } }

    /// <summary>지금 보이는 건 (없으면 null)</summary>
    public PadView? Head
    {
        get
        {
            lock (_lock)
                return _items.Count == 0 ? null : new PadView(_items[0].Req, _items.Count - 1, _items[0].Req.Deadline, _headSince);
        }
    }

    /// <summary>들어온 질문. 같은 request_id 가 이미 있거나 이미 시간이 지났으면 받지 않음(false — 답도 안 함)</summary>
    public bool Submit(PadRequest req, IPadOwner owner)
    {
        lock (_lock)
        {
            if (req.Deadline <= _now()) return false;
            if (_items.Any(i => i.Req.RequestId == req.RequestId)) return false;
            _items.Add(new Item { Req = req, Owner = owner });
            UpdateHead();
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>보이는 건이 바뀐 지 1초가 안 됐나</summary>
    public bool IsFresh
    {
        get { lock (_lock) return _items.Count > 0 && _now() - _headSince < _guardMs; }
    }

    /// <summary>request_id 로 답 — 보이는 건일 때만 · 바뀐 지 1초 안이면 Fresh(안 받음)</summary>
    public PadAnswer Answer(string requestId, PadDecision d)
    {
        Item it;
        lock (_lock)
        {
            if (_items.Count == 0 || _items[0].Req.RequestId != requestId) return PadAnswer.Stale;
            long now = _now();
            if (_items[0].Req.Deadline <= now) return PadAnswer.Stale;   // 시간이 지남 — Tick 이 닫는다(답 안 함)
            if (now - _headSince < _guardMs) return PadAnswer.Fresh;
            it = _items[0];
            _items.RemoveAt(0);
            UpdateHead();
        }
        Changed?.Invoke();
        Done?.Invoke(it.Req, d, "answer");
        try { it.Owner.Respond(it.Req.RequestId, d); } catch { /* 연결이 이미 끊김 — 확장이 알아서 끝냄 */ }
        return PadAnswer.Answered;
    }

    /// <summary>확장의 close — 그 질문을 보낸 연결에서 온 것만(다른 연결의 질문은 닫지 않음)</summary>
    public bool Close(string requestId, IPadOwner owner) =>
        RemoveWhere(i => i.Req.RequestId == requestId && ReferenceEquals(i.Owner, owner), "close") > 0;

    /// <summary>연결이 끊김 — 그 연결의 질문을 모두 닫음(답 안 함)</summary>
    public int Drop(IPadOwner owner) => RemoveWhere(i => ReferenceEquals(i.Owner, owner), "drop");

    /// <summary>받기를 끔 — 모두 닫음(답 안 함)</summary>
    public int Clear() => RemoveWhere(_ => true, "clear");

    /// <summary>시간이 지난 건을 닫음(답 안 함) — 닫은 수</summary>
    public int Tick()
    {
        long now = _now();
        return RemoveWhere(i => i.Req.Deadline <= now, "timeout");
    }

    /// <summary>그 연결에 아직 끝나지 않은 질문이 있나</summary>
    public bool HasOwner(IPadOwner owner) { lock (_lock) return _items.Any(i => ReferenceEquals(i.Owner, owner)); }

    private int RemoveWhere(Func<Item, bool> pred, string why)
    {
        List<Item> gone;
        lock (_lock)
        {
            gone = _items.Where(pred).ToList();
            if (gone.Count == 0) return 0;
            foreach (var g in gone) _items.Remove(g);
            UpdateHead();
        }
        Changed?.Invoke();
        foreach (var g in gone) Done?.Invoke(g.Req, null, why);
        return gone.Count;
    }

    /// <summary>맨 앞이 바뀌었으면 그 시각을 적는다 (처음 뜬 때 포함 · 뒤에 줄만 서면 그대로)</summary>
    private void UpdateHead()
    {
        var id = _items.Count > 0 ? _items[0].Req.RequestId : null;
        if (id != _headId)
        {
            _headId = id;
            _headSince = _now();
        }
    }
}
