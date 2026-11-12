# PC 앱 0.3.0 — 작업 노트 (ch57x-engineer · 2026-11-12)

지시 진본: autographer `projects/ch57x/planning/PC앱-0.3.0-구현-지시.md` · 근거: 2판 판정(48건 「고쳐 줘」) · 2판 모형(`artifacts/ch57x-pc-app-ux/mocks/`) · `projects/ch57x/DESIGN.md`.
브랜치 `pc-0.3.0`(master push · 태그 · 릴리스 안 함 — 오케가 첫 사용 점검 뒤에).

## 검증 (서버에서 WPF 를 못 띄워 셋으로)
- 리눅스 컴파일: `~/.dotnet/dotnet build windows/Ch57x.App/Ch57x.App.csproj -p:EnableWindowsTargeting=true` → 성공 · 오류 0 (경고 WFAC010 manifest DPI 1개 — 0.2.1 부터 있던 것).
- Core 단위 시험 `windows/Ch57x.Tests` (xunit · 리눅스와 CI Windows 둘 다): 23개 통과 — 업로드 보존 6 · 가져오기 4 · 실행 취소 · 상용구 · 변경 수 · 자동실행 명령 · 프로필 이름 · 요약 글 6 · 용어 4 · 테마 사전 3.
  음성 대조: 용어 시험 2개는 0.2.1 앱 소스에서 실패했다(옛 말 · 그림 글자 검출 — 바꾸기 전 실측). 테마 열쇠 겹침 시험은 앞 커밋 Controls.xaml 에 대해 Card · Divider 겹침을 잡는다(실측).
- 화면 렌더(`CH57x 설정기.exe --render-screens <폴더>`)를 GitHub Actions windows-latest 에서 돌려 PNG 22장(11 화면 × 라이트 · 다크, 125%) — 아티팩트 `render-png`(render.log 포함).
  러너: Windows NT 10.0.26100 · 글꼴 Segoe UI Variable Text · Segoe UI · Malgun Gothic 모두 있음(render.log).
  첫 CI 렌더가 잡은 결함: 스타일 열쇠 `Card` · `Divider` 가 같은 이름의 색 열쇠를 가려 모든 창이 「'Style' is not a valid value for property 'Background'」로 그려지지 않음 → 열쇠 이름 바꿈 + 시험 추가.
  렌더로 잡아 고친 것(PNG 를 보고 · 픽셀을 재서): 아이콘이 위로 붙음(세로 가운데) · 프로필 칸 높이 40 → 32 · 입력 칸 여백이 두 번 들어감 · 높은 입력 칸(상용구)에서 옆선 절반이 진해짐(기울기 붓 → 아래 1px 선) · HUD 칸 글이 잘림(줄 높이 14).
- 마지막 CI: https://github.com/happy4ed/ch57x-configurator/actions/runs/43198159117 (성공 · 커밋 ab3ac43) — Core tests 23 통과(Windows) · 렌더 성공 22 실패 0 ·
  exe 아티팩트 portable 71 941 290 B · slim 784 583 B. PNG 는 autographer `artifacts/ch57x-pc-app-0.3.0-preview/png/` 에도 복사(22장).

## 판단한 점 (지시와 다르거나 해석한 곳)
- **Mica 안 씀 — DESIGN.md 단색**: Mica 는 DWM 유리 틀(GlassFrameThickness=-1)이 있어야 비치는데, 그러면 시스템 캡션 단추(흐린 최대화 포함)가 함께 그려져 E7 · D8(최대화 없음)과 부딪힌다. 서버에서 실기로 확인할 길이 없어 「안 되면 단색」 쪽. 확신도 중간.
  대신 Windows 11 둥근 모서리는 `DWMWA_WINDOW_CORNER_PREFERENCE=ROUND`(실패해도 무시) · 그림자는 `GlassFrameThickness 0,0,0,1` 관례 — 둘 다 실기 미확인.
- **창 틀 = 직접 그린 제목 표시줄**(WindowChrome): 아이콘 16 · 제목 12 · 최소화 · 닫기(46×32). 렌더 PNG 에 제목 표시줄까지 들어가는 이유도 있음. 렌더 모드에서만 둥근 모서리 8 · 1px 테두리를 그림에 그린다(실제 창은 Windows 가 그림).
- **「이 키 비우기」 = 프로필에 `type:none` 으로 남김**: 업로드 보존(S1)이 비운 키를 키보드 값으로 되살리지 않게(웹은 none 을 「안 보내는 키」로 보고 되살림 — 여기만 다름). 업로드는 그 키를 보내지 않으므로 펌웨어 기본값이 된다(0.2.1 과 같음). 「레이어 비우기」도 같은 방식.
- **트레이 「JSON 가져오기 (병합)…」 남김**: 2판 트레이 안에는 없지만, 에이전트 패드 설치 안내(배포 페이지 · README)가 이 이름으로 2층만 합치게 안내한다. 빼면 그 안내가 깨짐 → 이름 그대로 두고 결함(S7: LED · 이름 덮어쓰기)만 고침. 빼려면 패드 안내도 같이 바꿔야 함 — 오케 판단.
  참고: 패드 파일(`ch57x-agent-pad-layer2.json`)은 led 배열을 실제로 담고 있어(웹 기본값 3층) 이 병합에서도 LED 는 덮인다(「파일에 실제로 있을 때만」 규칙대로). 패드 파일에서 led 를 빼면 PC 앱 병합은 LED 를 지킨다(웹 「불러오기」는 어차피 전체 교체).
- **자동실행은 `--tray`**: 실행하면 키 설정 창이 뜨지만(S2) Windows 시작 때마다 창이 뜨지 않게 Run 값을 `"exe" --tray` 로. 0.2.1 등록(인자 없음)은 시작할 때 자가 치유가 형식만 고친다. 회장님 확인 대상(부팅 때도 창을 원하시면 인자만 빼면 됨).
- **두 번째 실행 → 떠 있는 창을 앞으로**: 이름 있는 이벤트(EventWaitHandle) — named pipe 아님(권한 팝업 기능과 무관).
- **「올리지 않은 변경 n개」 기준**: 마지막으로 키보드에 올리거나 읽은 내용. 앱을 새로 켜면 연 프로필을 기준으로 본다(키보드 내용을 모르므로) — 끄기 전에 안 올린 변경은 다시 켜면 0 으로 보인다. 확신도 높음(설계 한계).
- **PC 저장값 불러오기**: 0.2.1 처럼 키를 바꿀 때마다 PC 파일에 자동 저장된다 → 이 단추가 실제로 되돌리는 것은 「키보드에서 읽기」로 화면에 들어온 값(읽기는 파일을 안 바꿈 · 용어 표). 실행 취소로도 되돌릴 수 있음.
- **프로필 고르기(키 설정 창) = 그 프로필을 화면에 엶**(키보드에는 업로드해야 들어감). 트레이의 프로필 이름 = 바로 적용(보류 T2 — 지금 동작 유지). 둘 다 업로드 보존(S1) 경로를 탐.
- **업로드 중 모양**: 모형처럼 단추 안 「업로드 중 42%」 + 오른쪽 글 + 진행 막대. 잠긴 강조 단추는 Fluent 공식 비활성 색(회색 AccentFillColorDisabled) — 모형은 파랑 45% 불투명이었음.
- **테마 하위 메뉴 표지**: Fluent RadioMenuFlyoutItem 처럼 고른 것만 점 — 모형은 셋 다 동그라미였음.
- **HUD PNG 바탕**: HUD 는 바탕 없는 창(H1)이라 투명 PNG 는 보기 어렵다 → 모형처럼 책상 색(라이트 #E6E9EE · 다크 #141518) 위에 16 여백으로 그림.
- **노브 마우스 「드래그」 → 「끌기」**, 버튼 이름 L/R/M → 왼쪽 · 오른쪽 · 가운데(모형 「클릭 가운데」).
- **트레이 메뉴 색**: WinForms 메뉴라 Fluent 토큰을 색 값으로 옮김(Bg4 · Subtle · Divider · 다크 대응) — 앱 테마를 따름.

## 근거 확신도
- 공식 토큰(DESIGN.md · 2판 TOKENS 표에 있는 값): 높음.
- 표에 없던 상태 색 — ControlFillColorTertiary #4DF9F9F9/#08FFFFFF · ControlFillColorDisabled #4DF9F9F9/#0BFFFFFF · SubtleFillColorTertiary #06000000/#0AFFFFFF ·
  ControlAltFillColorTertiary #0F000000/#0BFFFFFF · TextFillColorDisabled #5C000000/#5DFFFFFF · AccentFillColorDisabled #37000000/#28FFFFFF · TextOnAccentFillColorDisabled #FFFFFF/#87FFFFFF:
  같은 WinUI 파일의 이름을 기억으로 옮김 — 이번에 파일을 다시 열어 대조하지 않음. 확신도 중간.
- 강조색: `HKCU\…\Explorer\Accent\AccentPalette`(8색 × RGBA)에서 라이트 = 4번(Dark1) · 다크 = 1번(Light2) — 순서(Light3 · Light2 · Light1 · Base · Dark1 · Dark2 · Dark3)는 기억 · 실기 미확인. 확신도 중간. 못 읽으면 DESIGN.md primary.

## 실기 미확인 (회장님 PC)
- HID: 업로드 보존(먼저 읽기 → 되쓰기) · 읽기 실패 확인 창 · 업로드 중 화면이 굳지 않는지 · 걸리는 시간.
- 창: 둥근 모서리 · 그림자 · 직접 그린 캡션 단추 끌기 · Alt+Space · 작업 표시줄 아이콘 · 125% 외 배율.
- 팝업(⋯ 메뉴 · 프로필 목록 · 고르기 목록)의 실제 자리 · 그림자 — 렌더는 실제 팝업을 열어 그린 메뉴를 계산한 자리에 겹친 것.
- 테마 「시스템」이 Windows 설정을 바꿀 때 바로 따라가는지(SystemEvents) · 강조색.
- 「눌러서 입력」에서 Win 조합 · IME(한글 입력 상태) 키.
- 트레이 메뉴 다크 색 · 체크 표지 · 첫 닫기 풍선 알림.
- 두 번째 실행 → 창 앞으로 · `--tray` 자동실행 · 0.2.1 등록의 형식 자가 치유.
