using System.Text;

namespace Ch57x.Core;

/// <summary>
/// 권한 질문 팝업의 화면 글 (PC 앱 0.3.1 · 첫 사용 점검 반영) — 순수 논리라 리눅스에서도 시험한다.
/// 받은 글은 먼저 PadProtocol.Plain 을 거친 뒤 여기로 온다(이 함수들은 글을 해석하지 않고 순서 · 이름만 바꾼다).
/// </summary>
public static class PadText
{
    /// <summary>시간이 지난 뒤 생기는 일 — Claude 는 팝업만 닫히고 터미널에서 답 · Codex 는 터미널 권한 창으로(점검 상 4)</summary>
    public static string TimeLeft(PadCli cli, int seconds) =>
        cli == PadCli.Codex ? $"{seconds}초 뒤 터미널 권한 창으로" : $"{seconds}초 뒤 팝업만 닫힘 · 터미널에서 답";

    /// <summary>뒤에 줄 선 질문 수 (0 이면 빈 글 — 그 줄을 숨긴다)</summary>
    public static string Waiting(int waiting) => waiting > 0 ? $"뒤에 {waiting}건 더" : "";

    /// <summary>제목 아래 「도구」 줄 — 내부 도구 이름을 하는 일로(점검 중 7). 모르는 도구는 이름 그대로.</summary>
    public static string ToolLabel(string tool) => tool switch
    {
        "Bash" => "명령 실행",
        "Edit" or "MultiEdit" => "파일 고치기",
        "Write" => "파일 쓰기",
        _ => tool,
    };

    /// <summary>명령 칸의 줄 이름 — 파일 도구는 파일 이름만 오므로 「고칠 파일」(점검 중 6)</summary>
    public static string SubjectLabel(string tool) => tool switch
    {
        "Bash" => "명령",
        "Edit" or "MultiEdit" => "고칠 파일",
        "Write" => "쓸 파일",
        _ => "대상",
    };

    /// <summary>
    /// 위치 — 폴더(프로젝트) 이름을 맨 앞으로(점검 중 8). 보내는 쪽(서버 쪽 확장 whereOf)은
    /// 「Claude 확장 창 · 터미널 「이름」 · tmux 세션:창.칸 · 폴더」 순서로 붙이고 폴더는 늘 끝이다.
    /// 끝 토막이 폴더가 아니면(창 · 터미널 · tmux) 그대로 둔다.
    /// </summary>
    public static string FolderFirst(string where)
    {
        if (string.IsNullOrEmpty(where)) return "";
        int i = where.LastIndexOf(" · ", StringComparison.Ordinal);
        if (i < 0) return where;
        var tail = where[(i + 3)..];
        if (tail.Length == 0 || IsPlacePart(tail)) return where;
        return tail + " · " + where[..i];
    }

    private static bool IsPlacePart(string s) =>
        s.StartsWith("터미널 「", StringComparison.Ordinal) || s.StartsWith("tmux ", StringComparison.Ordinal) ||
        s.StartsWith("Claude 확장 창", StringComparison.Ordinal) || s.StartsWith("원격 창", StringComparison.Ordinal);

    /// <summary>
    /// 명령을 연결 기호에서 토막낸다 — 「 && 」 「 || 」 「 | 」 는 그 앞에서, 「; 」 는 그 뒤에서(「;」 가 줄 끝에 남게).
    /// 앞뒤가 공백인 기호만 본다(정규식 「a|b」 같은 것은 안 끊음). 따옴표 안은 가리지 않는다(화면 줄바꿈에만 씀).
    /// 토막을 다시 이으면 원래 글과 같다.
    /// </summary>
    public static List<string> ConnectorSegments(string cmd)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(cmd)) return parts;
        int start = 0;
        for (int i = 0; i < cmd.Length; i++)
        {
            int cut = -1;
            if (cmd[i] == ' ' && i > 0 && (At(cmd, i + 1, "&& ") || At(cmd, i + 1, "|| ") || At(cmd, i + 1, "| "))) cut = i + 1;
            else if (cmd[i] == ';' && i + 1 < cmd.Length && cmd[i + 1] == ' ') cut = i + 2;
            if (cut > start && cut < cmd.Length)
            {
                parts.Add(cmd[start..cut]);
                start = cut;
            }
        }
        parts.Add(cmd[start..]);
        return parts;
    }

    private static bool At(string s, int i, string what) => i + what.Length <= s.Length && string.CompareOrdinal(s, i, what, 0, what.Length) == 0;

    /// <summary>
    /// 명령 칸 줄바꿈 — 한 줄에 다 들어가면 그대로. 아니면 연결 기호 토막을 앞에서부터 채우고 안 들어가는 토막은 새 줄에서 시작(\n).
    /// 토막 하나가 한 줄보다 길면 그 안은 화면(TextWrapping)이 공백에서 끊는다. fits = 그 글이 한 줄에 들어가나(화면이 잰다).
    /// </summary>
    public static string BreakAtConnectors(string cmd, Func<string, bool> fits)
    {
        if (string.IsNullOrEmpty(cmd) || fits(cmd)) return cmd ?? "";
        var segs = ConnectorSegments(cmd);
        if (segs.Count < 2) return cmd;
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (var seg in segs)
        {
            if (cur.Length == 0) { cur.Append(seg); continue; }
            var joined = cur + seg;
            if (fits(joined.TrimEnd())) { cur.Append(seg); continue; }
            lines.Add(cur.ToString().TrimEnd());
            cur.Clear().Append(seg.TrimStart());
        }
        if (cur.Length > 0) lines.Add(cur.ToString().TrimEnd());
        return string.Join("\n", lines);
    }
}
