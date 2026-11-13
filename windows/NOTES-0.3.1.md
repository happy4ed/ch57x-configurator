# PC 앱 0.3.1 — 작업 노트 (ch57x-engineer · 2026-11-13)

지시 진본: autographer `projects/ch57x/planning/PC앱-0.3.1-팝업수신-지시.md` · 설계 `planning/권한팝업-설계.md` §4 · 근거 `planning/agent-pad-2차-근거.md` P-3 · P-4.
보내는 쪽 약속: `projects/ch57x/vscode-agent-pad/src/pcPipe.ts` 머리 주석 · `permission.ts` parseRequest · `vscode-agent-pad/NOTES.md` 「PC 앱 0.3.0 에 넘길 것」(확장 소스는 읽기만 — 고치지 않음).
브랜치 `pc-0.3.1`(master push · 태그 · 릴리스 안 함 — 오케가 점검 뒤에).

## 만든 것
- **Core `PadPermission.cs`** (순수 논리 · 리눅스에서도 시험)
  - 줄 JSON v1 해석: `permission`(v 가 1 이 아니거나 request_id · cli 가 틀리면 Invalid) · `close` · 모르는 type 은 Unknown(버림). 길이는 `parseRequest` 와 같게 코드 포인트로 자름(tool 80 · summary 121 · where 200 · session 40 · yes_all 200) · timeout_ms 없음/0/음수 → 60초 · 130초 넘으면 130초 · 수 모양 글자도 받음(JS `Number`).
  - deadline: 받은 값(유닉스 ms) 그대로 · 없으면 지금 + timeout · 지금 + 130초보다 멀면 거기까지. 이미 지난 deadline 은 띄우지 않음(답도 안 함).
  - 답 한 줄 `{"type":"decision","request_id":"…","decision":"allow|allow_always|deny"}` — **none 은 보내지 않는다**(시간 초과면 답 안 함). **(마무리에서 바뀜 — 아래 「마무리」)**
  - 평문 `Plain`: 제어 문자(줄바꿈 · 탭 포함) → 공백 하나 · 방향 뒤집기 · 폭 0 글자(U+061C · U+200B~200F · U+202A~202E · U+2060~2069 · U+FEFF) → 「�」(명령이 다르게 보이게 하는 것을 막음). 링크 · 마크업은 글자 그대로.
  - `LineSplitter`: `\n` 으로 나눔(`\r` 뗌) · 한 줄 64KB 넘으면 넘침.
  - `PadPipeName`: `ch57x-agent-pad-` + 사용자 이름의 `[A-Za-z0-9._-]` 밖 글자(UTF-16 한 칸씩)를 `_` — pcPipe.ts `pipePath` 와 같은 규칙.
  - `PermissionQueue`: 여러 연결에서 온 질문을 온 순서로 한 건씩 · `Answer(request_id, …)` 는 보이는 건일 때만 · 보이는 건이 바뀐 뒤(처음 뜬 때 포함) 1초는 Fresh(안 받음) · 뒤에 줄만 서면 1초 다시 안 셈 · `Close` 는 그 질문을 보낸 연결에서 온 것만 · 연결이 끊기면 그 연결 질문 모두 닫음 · `Tick` 이 시간 지난 건 닫음.
- **Core `PadPipeServer.cs`**
  - 이름 `\\.\pipe\ch57x-agent-pad-<GetUserNameW>` — node `os.userInfo().username` 이 쓰는 libuv 함수와 같은 것을 부름(기억 · 확신도 중간 — CI 에서 node 가 같은 이름으로 붙는 것으로 확인 · 아래).
  - 보안(`NamedPipeServerStreamAcl.Create` + `PipeSecurity`): 내 SID 허용(FullControl) · **NETWORK(S-1-5-2) 거부** · 소유자 = 나 · 상속 없음. NETWORK 거부는 지시 밖 판단 — 「현재 사용자만」 ACL 만으로는 다른 PC 에서 **같은 계정**으로 SMB 를 거쳐 붙는 것을 못 막는다. `PIPE_REJECT_REMOTE_CLIENTS`(문서 원문 "Connections from remote clients are automatically rejected.")를 .NET 이 켜는지는 소스를 못 봐서 근거 미확보 → ACL 로 막음.
  - **첫 인스턴스만**: 첫 서버 인스턴스는 `PipeOptions.FirstPipeInstance`. CreateNamedPipe 문서 원문 "If you attempt to create multiple instances of a pipe with this flag, creation of the first instance succeeds, but creation of the next instance fails with ERROR_ACCESS_DENIED ." — 같은 이름을 남이 먼저 열었으면 `UnauthorizedAccessException` → **받지 않음**(NameTaken · 로그 · 트레이 도움말). 연결을 여럿 받으려고 두 번째 인스턴스부터는 플래그 없이 만들되, 우리 인스턴스가 하나도 없게 된 순간 다음 것은 다시 첫 인스턴스로 만든다(그 사이 남이 열었으면 거부됨).
  - 연결 최대 16 · 한 줄 64KB 넘음 / JSON 아님 / 판 1 아님 / 모양 틀림 → 그 연결을 끊음(확장은 끊기면 VS Code 알림으로 넘어감) · 모르는 type 은 버리고 계속 · 답은 그 질문을 보낸 연결에만 · 질문 없이 30초 조용한 연결은 끊음.
  - 끄기(Stop) = 듣기 · 연결 모두 닫음 → 확장 쪽 「PC 앱이 답 없이 끊음 → VS Code 알림」.
- **App `PermissionPopup`**: Fluent 떠 있는 표면(메뉴와 같은 Bg4 · FlyoutStroke · 모서리 8 · 그림자) · `Topmost` · `ShowActivated=False` · `ShowInTaskbar=False` · 오른쪽 아래(기본) / 오른쪽 위.
  내용: 「Claude Code 권한 질문」/「Codex 권한 질문」 · 명령 앞 120자(고정폭 · 줄바꿈) · 도구 · 위치 · Yes all 이면 · 남은 시간(1초마다 · 대기 n건) · 같은 폭 단추 Yes(강조) / Yes all / No · 아래 한 줄 「패드 7 Yes · 8 Yes all · 9 No」. 바깥 글은 모두 `TextBlock.Text`(평문). **(마무리에서 바뀜 — 아래 「마무리」)**
  - **WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW**(코드) — 근거: 문서 원문 "A top-level window created with this style does not become the foreground window when the user clicks it." `ShowActivated=False` 만으로는 단추를 누르면 팝업이 앞으로 와서 VS Code 입력 자리를 뺏는다(누른 뒤 바로 터미널에 치려면 이게 필요). 단추도 `Focusable=False`(누를 때 Focus() 가 창을 깨우지 않게). TOOLWINDOW 는 Alt+Tab 목록에서 빼려고.
- **전역 키**: 팝업이 보이는 동안만 `RegisterHotKey(MOD_NOREPEAT)` F19(0x82) · F20(0x83) · F21(0x84) → Yes · Yes all · No. 숨기면 바로 `UnregisterHotKey`. 셋 중 하나라도 못 잡으면 잡은 것도 풀고 단추만 + 안내 줄 「패드 키를 다른 프로그램이 씀 — 단추로 답하기」(그때는 VS Code 가 F19~F21 을 받으니 확장 쪽 규칙으로 같은 질문에 답이 간다). **(마무리에서 바뀜 — 아래 「마무리」)**
  **1초 무시**: 키 · 단추 모두(아래 「판단」) — 안내 줄 자리에 3초 「새 질문이 떴습니다 — 보고 다시 누르세요」(줄 수가 늘지 않게 같은 자리). **(마무리에서 바뀜 — 아래 「마무리」)**
- **설정(트레이 메뉴)**: 「권한 질문 팝업 받기」 체크(기본 켜기 · 끄면 파이프 닫음) · 「권한 팝업 위치」 → 오른쪽 아래 / 오른쪽 위. `settings.json` 의 `PadPopup` · `PadPopupCorner`.
- 로그(로그 보기 창): 받음 · 답 · 닫힘 · 시간 초과 · 키 잡음 여부 — **명령 원문은 안 적음**(CLI · 도구 이름 · request_id 앞 8자 · 결과).
- 판 0.3.1.

## 판단한 점 (지시 · 약속과 다르거나 해석한 곳)
- **1초 무시를 단추에도**: 지시는 「키 무시」. 이 앱은 팝업 창 하나를 다시 쓰므로 질문이 바뀌는 순간 마우스로 누른 단추가 새 질문에 답할 수 있다(VS Code 알림은 알림마다 단추가 따로라 확장은 단추를 무시하지 않음). 그래서 단추도 같은 1초 규칙 · 같은 안내. 단추는 그려진 request_id 를 같이 보내 대기열이 대조한다.
- **Codex 표기**: 단추 이름은 셋 다 그대로(Yes · Yes all · No — 패드 안내와 같은 말) · 「Yes all 이면」 줄에 받은 yes_all(훅이 「Codex 는 이번만 허용」을 보냄 · 비었으면 같은 글을 앱이 넣음). 지시 「Yes all = 이번만 표기」를 이 줄로 읽음. F20 · Yes all 은 allow_always 를 보내고 훅이 Codex 에서 allow 로 바꾼다(설계 §1). **(마무리에서 바뀜 — 아래 「마무리」)**
- **제목 = 「Claude Code 권한 질문」**, 남은 시간 · 대기 건수는 본문 줄(제목 영역에 상태를 넣지 않는 전사 규칙).
- **약속 밖 줄 = 끊음**: 확장은 받는 쪽에서 깨진 줄을 버리지만, 앱 쪽은 끊는 편이 확장을 VS Code 알림으로 넘겨 「아무 일도 안 일어남」을 막는다. 모르는 type 만 버림(다음 판 메시지).
- **시간 초과 뒤 연결은 그대로**: 확장이 같은 deadline 에 스스로 close 를 보낸다. 끊으면 확장이 그 순간 VS Code 알림을 띄울 수 있어(경쟁) 끊지 않음 — 30초 조용하면 앱이 정리.
- **대기 건수 = 앱이 받은 것만**: 확장은 자기 대기열의 맨 앞 하나만 파이프로 보낸다 → 팝업의 「대기 n건」은 **다른 VS Code 창들**이 보낸 것. 한 창 안에서 줄 선 질문 수는 모름(확장이 보내지 않음). → 약속 차이 1(아래).
- **팝업 위치 = 주 모니터 작업 영역**(`SystemParameters.WorkArea`) · HUD 기본 자리(오른쪽 아래)와 겹칠 수 있음 — 그때는 「오른쪽 위」.
- **팝업 닫기 단추 없음**: 지시 목록에 없어 넣지 않음. 지금은 답하거나 시간이 지나야 사라짐(후보). **(마무리에서 바뀜 — 아래 「마무리」)**

## 약속과 다른 곳 · 확장 쪽에 넘길 것 (확장은 안 고침 — 오케 판단)
1. **대기 건수**: 확장이 자기 대기열 수를 보내지 않아 앱은 창 사이 대기만 셈. 맞추려면 permission 에 `waiting`(그 창에서 뒤에 줄 선 수) 필드를 더하거나 · 그 수가 바뀔 때 새 메시지(예: `{"type":"waiting","request_id","n"}` — 지금 앱은 모르는 type 을 버리므로 섞어 보내도 안전).
2. **서버 신원 확인(확장 NOTES 「PC 앱 0.3.0 에 넘길 것」 3번)**: 앱 쪽에서 할 수 있는 몫(첫 인스턴스 · 현재 사용자 ACL · NETWORK 거부)은 함. 확장이 붙은 서버가 진짜 PC 앱인지 보는 `GetNamedPipeServerProcessId` 확인은 확장 쪽 몫 — 그대로 미해결.
3. 확장 `sendToPcApp` 은 앱이 `none` 을 보내면 `if (d) ctx.answer(d)` 로 none 답을 낸다 — 앱은 none 을 보내지 않으므로 지금은 문제없음(앞으로도 보내지 않을 것). **(마무리에서 바뀜 — 아래 「마무리」)** 확장 0.3.2 는 none 을 「결정 없음 → 훅 바로 끝」으로 받는다(extension.ts showPermission 주석 「0.3.2 (9번)」 · permission.ts answer 가 none 을 받음 — 읽어서 확인).

## 검증
(아래 CI 절에 결과)

## 마무리 — 첫 사용 점검 반영 (2026-11-13 · ch57x-engineer)
지시 진본: autographer `projects/ch57x/planning/PC앱-0.3.1-마무리-지시.md` · 점검 원문 정리 `planning/PC앱-0.3.1-팝업-첫사용점검.md`.
- run 43371772518 (7ebd653) 실패 — Core 시험 `Stop_DisconnectsClients_AndRestartWorks` 가 Windows 에서 NameTaken(앞 run 은 통과 · 경쟁). 원인 · 고침은 아래 「파이프 서버 경쟁」.
- run 43372115073 (9d3484f) 성공 — 렌더에서 줄바꿈 두 곳 어색(위치 「tmux / research:1.0」 · 「답할 수 / 있음」) → c492a68 에서 고침.
- run 43372524318 (c492a68) **성공** — Core 시험 61/61(Windows · 전용 4개 포함) · 빌드 · 렌더 32장 성공 0 실패 · 파이프 왕복 17/17 · 첫 인스턴스(이름 선점 → 코드 3) 통과.
- 시험은 리눅스에서도 먼저 돌림: `~/.dotnet/dotnet test`(Core 57 통과 · Windows 전용 4 건너뜀) · `dotnet build -p:EnableWindowsTargeting=true`(WPF · XAML 컴파일까지) — CI 왕복 전에 컴파일 오류를 잡으려고.

### 고친 것 (지시 「고칠 것」 번호)
1. **Codex = Yes · No 둘**: Yes all 단추를 접고 UniformGrid 2칸 · 범위 줄 없음 · 패드 8(F20) = Yes(allow — Core `PadProtocol.KeyDecision`) · Yes 단추 안 숫자 「7 · 8」. 파이프 왕복 4번이 F20 → allow · 단추 「Yes·No」 로 확인.
2. **Claude Yes all**: 단추 이름 그대로 · 단추 아래 작은 글(13px · T2) 「Yes all 누르면 <yes_all>」. 표의 「Yes all 이면」 줄은 없앰(줄 이름 크기 섞임도 같이 사라짐). → 판단 ①
3. **남은 시간 뒤 결과**: Claude 「118초 뒤 팝업만 닫힘 · 터미널에서 답」 · Codex 「58초 뒤 터미널 권한 창으로」(Core `PadText.TimeLeft` · 1초마다). 줄 이름 「남은 시간」은 그대로.
4. **닫기 단추(제목 줄 오른쪽 x 아이콘 · IconButton Tag x)** = 터미널에서 답: 파이프로 `{"type":"decision","request_id","decision":"none"}` → 대기열에서 빠져 팝업 닫힘(다음 질문이 있으면 그것). 도움말 · 접근성 이름 「터미널에서 답」. 1초 무시 규칙은 다른 단추와 같게 적용. 파이프 왕복 16번(답 none · 기록 · 뒤에 팝업 숨음) 통과.
5. **줄 이름 · 글**: 도구 「명령 실행 · 파일 고치기」(MultiEdit 도 고치기 · Write = 파일 쓰기 · 모르는 도구는 이름 그대로) · 명령 칸은 표의 한 줄로 옮기고 줄 이름 「명령 / 고칠 파일 / 쓸 파일 / 대상」 · 위치는 폴더 이름 먼저(`PadText.FolderFirst` — 끝 토막이 창 · 터미널 · tmux 가 아니면 폴더로 봄) · 대기는 따로 한 줄 「대기 · 뒤에 n건 더」 → 판단 ② · 알림 「방금 누른 키는 무시함 · 바뀐 질문 보고 다시 누르기」(단추 바로 아래 · 3초 · 굵게) + 바뀐 칸(제목 · 도구 · 명령 · 위치 · Yes all 범위) 강조색 옅은 배경 1.5초(알림이 뜨면 3초) · 「패드 키를 다른 프로그램이 씀 — 마우스로만 답할 수 있음」(T2 상태 문장 · 링크 모양 없음 · 단추 안 숫자 숨김).
6. **맨 아래 패드 안내 줄 없앰** → 단추 안 작은 숫자 7 · 8 · 9 (13px · 불투명도 0.72).
7. **줄바꿈**: 한글 U+2060 처리는 그대로(렌더에서 「규칙 / 저장」 꼴 없음 확인) · 명령 칸은 화면이 폭을 잰 뒤 `PadText.BreakAtConnectors` 로 「 && 」 「 || 」 「 | 」 앞 · 「; 」 뒤에서 먼저 줄을 나눔(한 줄에 들어가면 그대로 · 토막이 한 줄보다 길면 그 안은 공백에서) → 판단 ③ · 위치는 「 · 」 에서만(토막 안 공백 U+00A0 · 렌더 실측 「tmux / research:1.0」) · 알림 「수 있음」 붙임(렌더 실측 「답할 수 / 있음」).
8. **Enter 확인(코드 근거)**: `PermissionPopup.xaml` 단추 넷 모두 `Focusable="False"` · `IsDefault` 없음(grep 0건 — Yes 의 파랑은 AccentButton 모양만) · 창 `ShowActivated="False"` + `OnSourceInitialized` 의 `WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE → MA_NOACTIVATE` → 창이 활성 · 포커스가 되지 않아 Enter 는 앞 창(VS Code)으로 간다. 파이프 왕복 17번: 보임 16회 모두 「포커스=안 가져감」(IsActive 기준 — 실기 미확인은 아래).
9. version 0.3.1 그대로(csproj Version · FileVersion · AssemblyVersion 0.3.1).

### 판단 (지시와 다르거나 해석한 곳)
- ① **「누르면」**: 지시 「줄 이름 「Yes all 이면」 → 「누르면」」 과 「단추 아래 작은 글로 범위」 를 한 줄로 합침 — 범위가 단추 아래로 가면 표의 줄이 필요 없고, 단추 셋 아래에 「누르면」 만 두면 어느 단추인지 모르므로 「Yes all 누르면 …」. 표에 같은 글을 두 번 두지 않음.
- ② **「1/3」 을 제목 옆에 두지 않음**: 지시는 「대기 n건은 제목 옆 1/3 꼴」 이나, 회장님 원칙 「제목 영역은 지금 무슨 화면인지만 — 상태 요약 · 건수 금지」(전 프로젝트 공통 · 2026-10-06 · 예외는 선택 모드 「n개 선택됨」뿐)와 부딪힘. 점검이 지적한 것(남은 시간 줄에 섞임)은 따로 한 줄 「대기 · 뒤에 2건 더」(점검자 제안 두 번째 꼴)로 풂. 제목 옆으로 옮길지는 오케 · main 판단.
- ③ **「;」 는 뒤에서 끊음**: 지시는 「&& · | · ; 앞」. 「;」 앞에서 끊으면 줄 첫머리가 「; make」 가 되어 읽기 어려워 「cd windows;」 / 「git log …」 로 둠. 공백이 앞뒤에 있는 기호만 봄(정규식 「a|b」 안은 안 끊음) · 따옴표 안은 가리지 않음(화면 줄바꿈에만 쓰므로 명령 글자는 그대로).
- ④ **작은 글 13px**: DESIGN.md caption 은 12px 이나 회장님 원칙 「PC 최소 13px」 를 따름.
- ⑤ **닫기 단추도 1초 무시**: 바뀐 직후 누른 닫기가 새 질문을 터미널로 보내지 않게 다른 단추와 같은 규칙.
- ⑥ **알림이 뜨면 팝업이 한 줄 커짐**: 알림 줄을 단추 바로 아래에 넣었다 빼므로(오른쪽 아래 기본 자리에서는 단추가 한 줄 위로 감 · 마우스 아래에는 알림 글이 와서 다른 단추를 누르게 되지는 않음). 질문이 바뀔 때도 내용 길이로 높이가 바뀌므로 「레이아웃 이동 0」은 이 창에서 지키지 못함.

### 파이프 서버 경쟁 (지시 밖 · CI 가 잡음)
- 증상: run 43371772518 Windows Core 시험 `Stop_DisconnectsClients_AndRestartWorks` — 끈 뒤 바로 켜면 `NameTaken`. 앞 run 43342910059 에서는 통과(때에 따라).
- 원인: `Stop()` 이 연결을 기다리던 인스턴스를 취소만 걸고 닫기는 받기 고리에 맡김 → 닫힘이 늦으면 바로 부른 `Start()` 의 FirstPipeInstance 생성이 거부됨. 트레이 「권한 질문 팝업 받기」 끄기 → 바로 켜기에서도 같은 일이 날 수 있었음(그러면 「받지 않음」).
- 고침(9d3484f): 기다리는 인스턴스를 `Gen.Waiting` 한 자리에 두고 `Stop()` 이 잠금 안에서 바로 닫음 · 받기 고리는 인스턴스 만들기와 자리 적기를 한 잠금 안에서. 시험 `StopThenStart_RightAway_NotNameTaken`(바로 끄고 켜기 20번 + 연결 받은 뒤 5번) 추가 — Windows CI 2회(run 43372115073 · 43372524318) 통과. 고치기 전 코드로 이 새 시험이 Windows 에서 실패하는지는 돌려 보지 않음(근거 미확보).

### 렌더 직접 확인 (run 43372524318 render-png · 125% · 10장 모두 열어 봄)
- perm-claude-light: 제목 줄 x 아이콘 · 도구 「명령 실행」 · 명령 「npm test -- --watch=false」 / 「&& git status --short」 · 위치 폴더 먼저 · 「118초 뒤 팝업만 닫힘 · 터미널에서 답」 한 줄 · Yes 7 / Yes all 8 / No 9 · 아래 「Yes all 누르면 이 저장소에서(내 로컬 설정) 「npm test」 로 시작하는 명령은 다시 묻지 않음」 두 줄 — 잘림 · 겹침 없음.
- perm-claude-dark: 위와 같은 배치 · 글 모두 읽힘 — 잘림 · 겹침 없음.
- perm-codex-light: 단추 Yes 「7 · 8」 / No 「9」 둘(같은 폭) · Yes all 범위 줄 없음 · 「58초 뒤 터미널 권한 창으로」 · 위치 「server-api · 터미널「bash」 ·」 / 「tmux research:1.0」 — 잘림 · 겹침 없음.
- perm-codex-dark: 위와 같음 — 잘림 · 겹침 없음.
- perm-queue-light: 「파일 고치기」 · 줄 이름 「고칠 파일」 · 「ch57x-configurator · Claude 확장 창」 · 「대기 뒤에 2건 더」 따로 한 줄 · 「Yes all 누르면 이 세션 동안 편집 자동 허용」 — 잘림 · 겹침 없음.
- perm-queue-dark: 위와 같음 — 잘림 · 겹침 없음.
- perm-busy-light: 명령 「cd windows;」 / 「git log --oneline -20」 / 「| grep -i popup | head -5」 · 단추 안 숫자 없음 · 「패드 키를 다른 프로그램이 씀 — 마우스로만 답할」 / 「수 있음」(T2 · 링크 모양 없음) — 잘림 · 겹침 없음.
- perm-busy-dark: 위와 같음 — 잘림 · 겹침 없음.
- perm-fresh-light: 앞 질문(npm test) → 다음 질문(git push) · 명령 칸 · Yes all 범위 칸 강조 배경(강조 칸 2 — render.log) · 단추 아래 굵은 「방금 누른 키는 무시함 · 바뀐 질문 보고 다시 누르기」 한 줄 · 「대기 뒤에 1건 더」 — 잘림 · 겹침 없음.
- perm-fresh-dark: 위와 같음 · 강조 배경이 라이트보다 옅지만 구분됨 — 잘림 · 겹침 없음.

### 안 고친 것 · 남은 것
- 「1/3」 제목 옆(판단 ②) — 오케 · main 판단 대기.
- 점검 「다크 글 흐림」 — 지시 「고칠 것」 에 없어 손대지 않음(T2 #C5FFFFFF 그대로).
- 확장 몫(점검 3 훅 글 · 13 되돌리기 안내)은 확장 0.3.2 쪽 — 이 저장소에서 안 함. 렌더 견본만 훅 0.3.2 rulesSaid · changeOf 꼴로 맞춤.
- 위치 「폴더 먼저」 는 끝 토막 규칙이라, 폴더 이름 안에 「 · 」 가 있으면 일부만 앞으로 옴(드묾).
- 실기 미확인(회장님 PC): 닫기 단추 도움말(ToolTip)이 활성 안 되는 창(WS_EX_NOACTIVATE)에서 뜨는지 · 바뀐 칸 강조가 실제 화면에서 눈에 띄는지 · 그 밖은 위 「실기 미확인」 그대로.

## 실기 미확인 (회장님 PC)
- 팝업이 뜰 때 · 단추를 누를 때 **포커스를 안 뺏는지**(VS Code 커서 · 터미널 입력 그대로) — CI 는 `Window.IsActive` 만 봄(러너에 사람 창이 없음).
- **RegisterHotKey 가 VS Code 보다 먼저 F19~F21 을 먹는지(이중 답 없음)** — 문서는 "the system posts the WM_HOTKEY message to the message queue of the window with which the hot key is associated." 까지만 말하고 앞 창에 키가 안 가는지는 직접 말하지 않음(근거 미확보 · 확신도 중간). 실기에서 VS Code 출력 「패드 7」 기록이 안 생기는지로 확인.
- CH57x 패드가 보내는 F19~F21 이 RegisterHotKey 에 걸리는지(CI 는 SendInput 으로 넣은 키).
- **회사 PC 에서 named pipe 가 막히지 않는지**(보안 제품 · 그룹 정책) — 막히면 확장은 VS Code 알림으로 넘어감.
- 125% 밖 배율 · 여러 모니터(주 모니터만 씀) · Windows 10.
- 트레이 메뉴 「권한 팝업 위치」 하위 메뉴 다크 색.
