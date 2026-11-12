namespace Ch57x.Core;

/// <summary>
/// 화면 용어 — 단일 진본 (PC 앱 진단 2판 용어 표 · 판정 S10). 같은 뜻은 어디서나 같은 말.
/// 시험(Ch57x.Tests TermsTests)이 이 표와 2판 용어 표를 대조하고, 앱 소스에 옛 말이 남았는지 찾는다.
/// </summary>
public static class Terms
{
    public const string AppName = "CH57x 설정기";

    // ── 2판 용어 표 (말 · 뜻)
    /// <summary>PC 의 지금 설정 → 키보드</summary>
    public const string Upload = "키보드에 업로드";
    /// <summary>키보드 → 화면(파일은 그대로)</summary>
    public const string ReadDevice = "키보드에서 읽기";
    /// <summary>PC 에 저장된 이 프로필 → 화면</summary>
    public const string LoadPc = "PC 저장값 불러오기";
    /// <summary>설정 파일(JSON) → 새 프로필</summary>
    public const string ImportProfile = "프로필 가져오기…";
    /// <summary>모든 프로필 → zip 파일</summary>
    public const string BackupAll = "전체 백업…";
    /// <summary>zip 파일 → 모든 프로필</summary>
    public const string RestoreBackup = "백업에서 복원…";
    /// <summary>대화상자 값 → 키 칸(키보드는 아직)</summary>
    public const string Apply = "적용";
    /// <summary>마지막 바꾼 것 되돌리기</summary>
    public const string Undo = "실행 취소";

    // ── 창 · 메뉴 이름
    public const string KeySettings = "키 설정";
    public const string OpenKeySettings = "키 설정 열기";
    public const string LogView = "로그 보기";
    public const string About = "정보";
    public const string Update = "업데이트…";
    public const string Exit = "종료";
    public const string Theme = "테마";
    public const string ThemeSystem = "시스템 (기본)";
    public const string ThemeLight = "라이트";
    public const string ThemeDark = "다크";

    /// <summary>창 제목: 「CH57x 설정기 — 키 설정」 (제목 표시줄은 화면 이름만 · E1)</summary>
    public static string WindowTitle(string screen) => $"{AppName} — {screen}";

    /// <summary>노브 이름 「노브1」 (1판 판정 E5 원문 "노브1 로 통일해")</summary>
    public static string Knob(int knobIndex0) => $"노브{knobIndex0 + 1}";

    /// <summary>노브 동작 이름 — 0 왼쪽 돌림 · 1 누름 · 2 오른쪽 돌림 (PROTOCOL §4 action 순서)</summary>
    public static readonly IReadOnlyList<string> KnobActions = new[] { "왼쪽 돌림", "누름", "오른쪽 돌림" };

    /// <summary>키 이름 「키 1」 — 본 창(키 설정) · 키 창 제목 · 도움말이 같은 낱말(첫 사용 점검 7번 · S10)</summary>
    public static string Key(int keyId) => $"키 {keyId}";

    /// <summary>키 ID → 사람 이름 (키 1 · 노브1 왼쪽 돌림). 도움말 · 실행 취소 이름 · 로그에 쓴다.</summary>
    public static string KeyName(int keyId)
    {
        if (keyId >= 16)
        {
            int k = (keyId - 16) / 3, a = (keyId - 16) % 3;
            return $"{Knob(k)} {KnobActions[a]}";
        }
        return Key(keyId);
    }

    /// <summary>키 창 제목 — 「키 1 · 레이어 3」 · 노브는 「노브1 · 왼쪽 돌림 · 레이어 3」 (첫 사용 점검 7번)</summary>
    public static string KeyTitle(int keyId, int layer0)
    {
        if (keyId >= 16)
        {
            int k = (keyId - 16) / 3, a = (keyId - 16) % 3;
            return $"{Knob(k)} · {KnobActions[a]} · 레이어 {layer0 + 1}";
        }
        return $"{Key(keyId)} · 레이어 {layer0 + 1}";
    }

    /// <summary>
    /// 화면 글에 쓰지 않는 낱말 — 같은 뜻은 한 말로(첫 사용 점검 2번 · 7번). 시험(TermsTests)이 앱 · Core 소스의 주석을 뺀 글에서 찾는다.
    /// 화살표 아이콘(upload · read) 방향은 그대로 — 회장님 승인.
    /// </summary>
    public static readonly IReadOnlyList<(string Word, string UseInstead)> Banned = new[]
    {
        // BANNED-BEGIN (이 목록 안의 글자는 시험이 건너뛴다)
        ("올리", "업로드 (「업로드 끝」 · 「그래도 업로드」 · 「업로드 전에」 · 「업로드 안 한 변경」)"),
        ("올림", "업로드 끝"),
        ("올린", "업로드한"),
        ("올려", "업로드해"),
        ("올렸", "업로드했"),
        ("층", "레이어"),
        ("버튼", "키 (「키 1」) — 마우스 단추는 「단추」"),
        // BANNED-END
    };

    /// <summary>옛 말 — 앱 화면에 남으면 안 된다(시험이 찾는다). 0.2.1 화면 글자.</summary>
    public static readonly IReadOnlyList<string> Retired = new[]
    {
        "다시 불러오기 (디스크에서)",
        "열기 / 로그 보기",
        "키 편집…",
        "키 편집 — CH57x",
        "프로필 백업 (.zip 내보내기)",
        "프로필 복원 (.zip 불러오기)",
        "키보드에서 읽기 (현재 설정)",
        "HUD 클릭 통과 (잠금)",
        "정보 (버전·저작권)",
        "별칭 (alias)",
        "동작 종류",
        "현재 설정 프로필로 저장",
        "여기 클릭 후 키 누르기",
        "변경 후 '키보드에 업로드' 로 적용",
        "연결됨 — 재연결",
        "(프로필 없음 — 폴더 열기)",
        // 0.3.0 첫 사용 점검에서 뺀 글 (3번 · 4번 · 8번 · 9번)
        "(색 선택)",
        "브라우저가 가로채는",
        "위에서부터 차례로 입력됨",
        "18자까지 키보드에 들어감",
        "\"레이어 복사…\"",
    };
}
