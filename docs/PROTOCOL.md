# CH57x 매크로 키보드 프로토콜 스펙

대상 장치: VID `0x1189` / PID `0x8840` (및 `0x8842`, `0x8890`) — CH57x 칩 기반 매크로 키보드.
이 문서는 [kriomant/ch57x-keyboard-tool](https://github.com/kriomant/ch57x-keyboard-tool) 의
Rust 구현(`src/keyboard/k884x.rs`)을 리버스/정리한 것으로, **웹(WebHID)·윈도우 버전이 공유**한다.

## 1. 전송 계층

| 항목 | 값 |
|------|----|
| 인터페이스 | MI_00 (HID 규격 공급업체 정의 / vendor-defined) |
| 엔드포인트 | `0x04` (Interrupt OUT) |
| 패킷 크기 | 항상 64 바이트 (모자라면 `0x00` 패딩) |
| 첫 바이트 | `0x03` = **Report ID** |

- 네이티브 도구: libusb `write_interrupt(0x04, chunk64)` → Windows 에서 USBDK 필요.
- 웹: `HIDDevice.sendReport(0x03, payload63)` → **드라이버 불필요** (OS HID 스택 사용).
  - `payload63` = 64바이트 패킷에서 첫 바이트(0x03=reportId)를 **뺀** 나머지 63바이트.
  - 단, 펌웨어가 numbered report 가 아니면 reportId=0 + 64바이트 전체일 수 있음 → `docs/HID-DESCRIPTOR-DUMP.md` 로 확정.

## 2. 키 바인딩 패킷 (`bind_key`)

```
오프셋  값            의미
0       0x03          Report ID
1       0xfe          Bind 커맨드
2       <keyId>       키 ID (아래 §4)
3       layer+1       레이어 (0-based 입력 → +1), 유효 layer 0..2
4       <kind>        매크로 종류: 1=키보드, 2=미디어, 3=마우스
5..9    0x00 ×5       예약
10..    <payload>     종류별 페이로드 (§3)
나머지  0x00          64바이트까지 패딩
```

### 매 키마다 반드시 뒤따르는 커밋 시퀀스 (★초기화 버그 방지 핵심)
```
[03 aa aa 00 ...]   커밋
[03 fd fe ff]       프로그래밍 종료
[03 aa aa 00 ...]   커밋
```
> 이 종료/커밋이 누락되거나 일부 키만 전송하면, **전송 안 된 키가 펌웨어 기본값으로 되돌아간다**
> (= 사용자가 겪는 "일부 버튼 초기화" 버그). 따라서 업로드는 **항상 전체 키 × 전 레이어**를 보내고
> 매 키마다 위 커밋 시퀀스를 붙인다.

## 3. 종류별 페이로드 (오프셋 10~)

### 3.1 키보드 (kind=1)
```
10      <count>       키 시퀀스 길이 (단일 modifier-only 조합이면 0)
11..    <mod> <code>  반복: HID modifier 바이트 + HID usage code (최대 18쌍)
```
- 지연(delay) 옵션 사용 시, 바인딩 패킷 뒤에 추가 패킷:
  ```
  [03 fe <keyId> layer+1 05 <low> <high>]   delay(ms) little-endian, 최대 6000
  ```

### 3.2 미디어 (kind=2)
```
10      0x00
11      <low>         MediaCode 하위 바이트
12      <high>        MediaCode 상위 바이트 (16-bit consumer usage)
```

### 3.3 마우스 (kind=3)
```
클릭:   [01 <mod> <buttons>]
휠:     [03 <mod> 00 00 00 <delta>]
이동:   [05 <mod> 00 <dx> <dy>]        dx,dy 는 i8 (음수는 256+n)
드래그: [05 <mod> <buttons> <dx> <dy>]
```
mouse modifier: Ctrl=0x01 Shift=0x02 Alt=0x04 / buttons 비트마스크: Left=0x01 Right=0x02 Middle=0x04

## 4. 키 ID 매핑 (`to_key_id`)

- 버튼 `n` (0-based) → `n + 1`
- 노브 `n` (0-based), 액션 a (CCW=0, Press=1, CW=2):
  - 일반: `16 + 3*n + a`  → 노브0={16,17,18}, 노브1={19,20,21}, 노브2={22,23,24}
  - (예외) 12버튼+4노브 모델의 4번째 노브만 `13 + a` 사용

9버튼 + 3노브 모델: 버튼 ID 1..9, 노브 ID 16..24.

## 5. 모디파이어 비트 (HID 표준)
Ctrl=0x01 Shift=0x02 Alt=0x04 Win=0x08 RCtrl=0x10 RShift=0x20 RAlt=0x40 RWin=0x80

## 6. LED 설정 (`set_led`)
```
[03 fe b0 layer+1 08 00 00 00 00 00 01 00 <code>]
[03 fd fe ff]
code = (color << 4) | mode
mode: 0=off, 1=backlight(color), 2=shock, 3=shock2, 4=press, 5=backlight white
color(1..7): red orange yellow green cyan blue purple  (white 는 mode5)
```

## 7. 미디어 코드 표 (16-bit)
Next=0xB5 Previous=0xB6 Stop=0xB7 Play=0xCD Mute=0xE2 VolumeUp=0xE9 VolumeDown=0xEA
Favorites=0x182 Calculator=0x192 ScreenLock=0x19E

## 8. 키보드 usage code 표
`A=0x04` 부터 선언 순서대로 1씩 증가 (HID Usage Page 0x07). 구현은 `web/js/keycodes.js` 참조.

## 9. 활성 레이어 전환 (live, 플래시 아님)
```
[03 a1 <layer>]   layer 1..3 (0 이면 1 로 보정). 즉시 적용, flash 저장 아님.
```
(공식 벤더 앱 `Send_SwLayer` 에서 확인: array[0]=0xa1, array[1]=layer.)

## 10. 미지원 / 한계  ★공식 벤더 앱 디컴파일로 확정
- **현재 설정 read 불가 (확정).** 공식 "MINI KeyBoard.exe"(.NET) 디컴파일 결과:
  - 들어온 HID 데이터 핸들러(`myhid_DataReceived`)가 `RecDataBuffer` 에 담기만 하고 **버림**(dead code).
  - 파일 불러오기(OpenFileDialog/.ini/.json) 경로 **없음**.
  - "버전 체크"조차 응답을 파싱하지 않고 write 성공 여부만 봄.
  → 벤더 앱도 **write-only**. 사용자가 본 "읽어오기"는 키보드가 아니라 **앱 자체 저장상태(Properties.Settings, PC별)** 복원으로 추정.
  → 따라서 설정은 호스트(브라우저/JSON)에 보관하고 "프로필=진실, 플래시로 동기화" 모델이 정답.
- 스크립트 실행/트리거 자동화는 키보드가 호스트로 키 입력을 보내는 것이므로 범위 밖.

## 부록: 공식 앱 대조 검증 (MINI KeyBoard.exe, .NET / namespace HIDTester)
- VID 0x1189, PID 0x8840 → mi_00 인터페이스 사용, **ReportID = 3** (`KeyBoardVersion_Check` 가 3→0→2 순으로 탐지).
- 저장/커밋: `Send_WriteFlash_Cmd` → array[0]=0xaa, array[1]=0xaa = §2 의 커밋 시퀀스와 일치.
- HidLibrary(C#) 사용. 우리 WebHID 구현과 바이트 포맷 동일.
