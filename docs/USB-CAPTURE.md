# 벤더 소프트 "읽어오기" USB 캡처 — read 명령 역추적

벤더 설정 프로그램에 **"읽어오기"** 기능이 있음 = 펌웨어가 config readback 지원 = 진짜 read 명령 존재.
그 순간의 USB 트래픽을 캡처해 **OUT 트리거 명령 + device 가 돌려주는 IN 리포트**를 그대로 따낸다.

## 1. 도구 설치 (Windows)
1. [Wireshark](https://www.wireshark.org/download.html) 설치 — 설치 중 **USBPcap** 체크박스 반드시 켜기.
2. 설치 후 재부팅(USBPcap 드라이버 로드).

## 2. 캡처 (짧게!)
1. 키보드 USB 연결. 다른 USB 작업/프로그램은 닫아 노이즈 최소화.
2. Wireshark 실행 → 인터페이스 목록에서 **USBPcap1 / USBPcap2 …** 중 키보드가 붙은 것을 더블클릭해 캡처 시작.
   (어느 건지 모르면 일단 아무거나, 안 잡히면 다른 USBPcapN 으로 재시도.)
3. **벤더 소프트에서 "읽어오기" 버튼 1번만** 누른다.
4. 곧바로 Wireshark 캡처 **정지**(빨간 네모). — 캡처 길이를 5~10초 안쪽으로 짧게.

## 3. 관심 패킷 찾기
상단 **Display filter** 에 입력:
```
usb.transfer_type == 0x01
```
(인터럽트 전송만 표시. 안 보이면 `usbhid` 또는 필터 비우고 64바이트 페이로드 프레임을 눈으로 탐색.)

찾을 패턴:
- **OUT** 프레임: `Leftover Capture Data` 가 `03 ...` 로 시작 (호스트→키보드 트리거 명령).
- 그 직후 연속되는 **IN** 프레임 여러 개: 64바이트, `03 ...` (키보드→호스트, **여기에 설정 덤프**가 실림).

키보드 장치를 특정하려면: 아무 IN/OUT 프레임 클릭 → 하단 상세에서 `USB URB` → `Device address` 값 확인 후
필터에 `usb.device_address == <그 번호>` 추가하면 키보드 트래픽만 남음.

## 4. 나에게 전달 (둘 중 하나)
**(A) hex 복사 — 추천, 빠름**
- "읽어오기" 직후의 **OUT 트리거 프레임 1~2개** + 뒤따르는 **IN 프레임 전부**를 각각 클릭 →
  하단 상세에서 `Leftover Capture Data` 우클릭 → **Copy → …as a Hex Stream** → 여기 붙여넣기.
- 각 프레임이 OUT인지 IN인지(방향)도 같이 적어주세요. 시간순으로.

**(B) 파일 공유**
- File → Export Specified Packets 로 관심 구간만 `keyboard-read.pcapng` 저장 →
  GitHub 저장소(`happy4ed/ch57x-configurator`)에 웹 UI 로 업로드(드래그&드롭 커밋) →
  알려주시면 AWS 에서 받아 분석.

## 5. 분석 후
OUT 트리거 + IN 응답 포맷을 PROTOCOL.md §"읽기"로 문서화하고, 웹앱에 "키보드에서 불러오기" 버튼을 구현한다.
응답 바이트 레이아웃은 §2 의 write 포맷(키ID/레이어/kind/페이로드)과 대칭일 가능성이 높다.
