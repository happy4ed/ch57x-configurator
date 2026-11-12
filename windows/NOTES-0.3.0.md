# PC 앱 0.3.0 — 작업 노트 (ch57x-engineer)

지시 진본: autographer `projects/ch57x/planning/PC앱-0.3.0-구현-지시.md`

## 상태 (2026-11-12)
- WIP: 분석 · 설계까지. 코드 변경 없음. 오케 지시로 Agent Pad 0.1.3 을 먼저 끝낸 뒤 이어서 한다.
- 리눅스 컴파일 기준선(0.2.1 그대로): `~/.dotnet/dotnet build windows/Ch57x.App/Ch57x.App.csproj -p:EnableWindowsTargeting=true` → 성공(경고 1 · WFAC010 manifest DPI).

## 설계 결정 (이어서 할 때 기준)
- Core(net8.0, 리눅스 시험 가능)에 순수 함수로 둔다: 업로드 보존 계획(S1) · 가져오기 병합/새 프로필(S7) · 실행 취소 기록(S5) · 상용구 분석(S12) · 변경 수 세기(E1) · 용어(S10) · 자동실행 명령 형식(`"exe" --tray`).
- 「이 키 비우기」는 프로필에서 지우지 않고 `type:none` 으로 남긴다 — 업로드 보존(S1)이 그 키를 기기 값으로 되살리지 않게(프로필이 「소유」한 키). 웹은 none 을 보내지 않는 키로 다루므로 호환됨.
- 창 모양: WindowChrome 직접 그린 제목 표시줄(아이콘 · 제목 · 최소화 · 닫기 — 최대화 없음, E7 · D8). 렌더 모드에서 제목 표시줄까지 PNG 에 들어가게 하려는 이유도 있다.
- Mica: 넣지 않고 DESIGN.md 단색(`SolidBackgroundFillColorBase`)으로 간다. 이유 — Mica 는 DWM 이 그리는 유리 틀(GlassFrameThickness=-1)이 필요하고, 그러면 시스템 캡션 단추(흐린 최대화 포함)가 함께 그려져 E7 · D8(최대화 없음)과 부딪힌다. 서버에서 실기 확인 수단이 없어 위험을 감수하지 않음. 확신도 중간.
- 자동실행(S2): 실행하면 키 설정 창이 뜨되, Windows 시작 시 자동실행은 `--tray` 인자로 조용히 뜬다(매 부팅마다 창이 뜨지 않게) — 엔지니어 판단, 회장님 확인 대상.
- 두 번째 실행: 이름 있는 이벤트(EventWaitHandle)로 떠 있는 창을 앞으로(named pipe 아님).
