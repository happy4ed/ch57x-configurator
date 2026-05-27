# CH57x 매크로 키보드 설정기

AliExpress 미니 매크로 키보드(**VID 1189 / PID 8840** 등 CH57x 칩 기반, 9키 + 3노브)용
오픈 설정 프로그램. 불친절하고 불안정한 벤더 소프트(특히 *일부 버튼이 초기화되는 버그*)를 대체한다.

## 폴더 구조
```
ch57x-configurator/
├── docs/      공용 프로토콜 스펙 (웹·윈도우 공통 진본)
│   ├── PROTOCOL.md            패킷/키ID/LED/커밋 시퀀스 전체 스펙
│   └── HID-DESCRIPTOR-DUMP.md WebHID sendReport 인자 확정용 덤프 절차
├── web/       WebHID 웹앱 (드라이버 불필요, Chrome/Edge)   ← 현재 동작
└── windows/   네이티브 윈도우 버전 (예정)
```

## 웹 버전 실행
WebHID 는 `https://` 또는 `localhost` 에서만 동작한다 (file:// 불가).
```bash
cd web
python3 -m http.server 8000
# Chrome/Edge 에서 http://localhost:8000 접속
```
1. **키보드 연결** → 팝업에서 키보드 선택
2. 레이어 탭 선택 → 버튼/노브 클릭 → 동작 설정 → **적용**
3. **키보드에 업로드** (전체 레이어를 한 번에 전송)
4. 프로필은 브라우저에 자동 저장 + JSON 내보내기/불러오기 가능

## "일부 버튼 초기화" 버그 대응
펌웨어는 키마다 커밋 시퀀스(`03 aa aa` / `03 fd fe ff`)로 저장을 확정한다.
벤더 소프트는 부분 전송 시 나머지 키를 기본값으로 되돌린다. 이 도구는 **업로드 때마다
전 레이어·전체 키를 커밋 시퀀스와 함께 전송**하므로 부분 초기화가 발생하지 않는다.

## 상태 / 다음 작업
- [x] 프로토콜 포팅 (키보드/미디어/마우스/지연/LED) — `docs/PROTOCOL.md`
- [x] WebHID 연결·업로드, 9키+3노브+3레이어 UI, 프로필 저장/내보내기
- [ ] 실제 장치에서 HID descriptor 덤프로 `REPORT_ID` 확정 (`docs/HID-DESCRIPTOR-DUMP.md`)
- [ ] 다단계 매크로 시퀀스 UI, LED 설정 UI
- [ ] 윈도우 네이티브 버전 (`windows/`) — 동일 `docs/PROTOCOL.md` 재사용

## 출처
- 프로토콜 기반: [kriomant/ch57x-keyboard-tool](https://github.com/kriomant/ch57x-keyboard-tool) (MIT)
