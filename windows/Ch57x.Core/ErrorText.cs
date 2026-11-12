namespace Ch57x.Core;

/// <summary>보낼 수 없는 키 설정(단계 18 넘음 · 지연 6000ms 넘음 · 모르는 동작) — 키보드 문제와 가르려고 따로 둔다.</summary>
public sealed class ProfileContentException(string message) : InvalidOperationException(message);

/// <summary>오류 알림 본문 한 줄 = 무슨 일 + 할 일.</summary>
public sealed record PlainError(string What, string Todo)
{
    public string Line => $"{What} — {Todo}";
}

/// <summary>
/// 오류 알림(dlg-error)의 쉬운 말 (0.3.0 첫 사용 점검 1번). 예외 글 — 「HID 쓰기 시간 초과」 같은 기술 말 — 은 여기 넣지 않는다.
/// 그 글은 「정보 복사」 내용(앱 ErrorReport)으로만 간다.
/// </summary>
public static class ErrorText
{
    public static readonly PlainError DeviceNoAnswer = new("키보드가 응답하지 않음", "케이블을 다시 꽂고 다시 해 보기");

    /// <param name="device">키보드와 주고받던 일(업로드 · 읽기 · 프로필 적용)이면 true</param>
    public static PlainError Of(Exception ex, bool device)
    {
        while ((ex is AggregateException || ex is System.Reflection.TargetInvocationException) && ex.InnerException != null) ex = ex.InnerException;
        return ex switch
        {
            ProfileContentException => new("키보드에 보낼 수 없는 키 설정이 있음", "단계 · 지연을 줄인 뒤 다시 해 보기"),
            _ when device => DeviceNoAnswer,
            UnauthorizedAccessException => new("이 자리에 쓸 권한이 없음", "다른 폴더를 골라 다시 해 보기"),
            FileNotFoundException or DirectoryNotFoundException => new("파일을 찾지 못함", "파일이 그 자리에 있는지 확인하고 다시 해 보기"),
            System.Text.Json.JsonException or InvalidDataException or FormatException => new("파일 내용을 읽지 못함", "이 앱이나 웹 설정기에서 저장한 파일인지 확인"),
            IOException => new("파일을 읽거나 쓰지 못함", "그 파일을 연 프로그램을 닫고 다시 해 보기"),
            _ => new("뜻밖의 문제가 생김", "다시 해 보고, 또 나면 「정보 복사」로 알려 주기"),
        };
    }
}
