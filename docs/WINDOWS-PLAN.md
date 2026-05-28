# 윈도우 네이티브 버전 — 구상

웹(WebHID)으로 불가능한 **OS 통합 기능**이 목표. 프로토콜은 `docs/PROTOCOL.md`(읽기 0xFA / 쓰기 0xFE
/ 개수조회 0xFB / 커밋 aa aa·fd fe ff) 그대로 재사용 — HID 송수신 계층만 네이티브로.

## 핵심 기능 (사용자 비전)
1. **트레이 상주** — 백그라운드 상주, 트레이 아이콘에서 프로필/레이어 즉시 전환, 빠른 설정 변경.
2. **항상 위 반투명 오버레이(HUD)** — 현재 레이어의 키 배치를 화면 위에 반투명·always-on-top으로 표시.
   - 단순 키값이 아니라 **Alias(별칭/아이콘)** 로 직관 표시.
   - Alias 소스 후보: 사용자 지정 라벨, 아이콘, **화면 캡처/스크롤 캡처 썸네일**(그 키가 하는 동작을 시각적으로).
3. **앱 감지 자동 레이어 전환** — 포그라운드 앱(예: Photoshop, Premiere, 브라우저)을 감지해
   **그 앱 전용 레이어/프로필을 자동 주입(injection)**. 앱 떠나면 복귀.
   - 단, 키보드 펌웨어 레이어 전환은 전용 스위치 고정(§레이어 제약) → 호스트측에서 매핑 가짜전환이 필요할 수 있음.
   - 대안 설계: 키보드는 1레이어 고정 + 호스트 후킹으로 앱별 동작 재해석(키→액션 리매핑 레이어를 PC가 담당).

## 기술 스택 후보 (정하기)
- **Electron + node-hid** — 웹 코드/UI 자산 재사용 최대. 트레이·always-on-top·투명창 지원. 앱감지/캡처는 네이티브 애드온/PowerShell.
- **C# .NET (WPF/WinForms) + HidSharp** — 가볍고 윈도우 통합 강함(트레이, 글로벌훅, 앱감지 Win32 API). 단 UI 재작성.
- **Rust(tauri) + hidapi** — 가볍고 빠름, ch57x-keyboard-tool 로직 직접 활용. UI는 웹뷰.
→ 1순위 검토: **Electron(자산 재사용)** vs **C#(.NET, OS통합 강점)**. 결정 필요.

## 윈도우만의 구현 포인트
- HID: node-hid / HidSharp / hidapi 로 `sendReport(3,...)`·input report 수신 (드라이버 불필요, WinUSB 아님).
- 트레이: 메뉴에서 프로필·레이어 전환, 시작프로그램 등록.
- HUD 오버레이: 투명+클릭통과(WS_EX_LAYERED|TRANSPARENT) always-on-top 창.
- 앱 감지: SetWinEventHook(EVENT_SYSTEM_FOREGROUND) 또는 폴링으로 활성 프로세스명 추적.
- Alias 캡처: 키 동작 등록 시 화면 영역 캡처 → 썸네일 저장 → HUD에 표시.
- 글로벌 핫키/후킹: 앱별 리매핑을 PC가 담당하는 경우 low-level keyboard hook.

## 재사용 자산
- `web/js/keycodes.js`, `protocol.js` 의 패킷 빌더/파서 로직(언어 포팅 또는 Electron이면 그대로).
- `docs/PROTOCOL.md` 전체.

## 미해결/결정 필요
- [ ] 스택 결정 (Electron / C# / Tauri)
- [ ] 앱별 자동전환을 "키보드 레이어"로 할지, "호스트 리매핑 레이어"로 할지 (펌웨어 제약 때문)
- [ ] Alias 캡처 UX (영역 선택·스크롤 캡처 방식)
