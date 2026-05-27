# 타당성 100% 확정 — HID Report Descriptor 덤프

WebHID `sendReport()` 의 인자(reportId 사용 여부 / payload 길이)를 확정하려면,
실제 장치 MI_00 인터페이스의 **HID Report Descriptor** 를 한 번 봐야 한다.

## 방법 A — 가장 쉬움 (Chrome 내장, 설치 0)

1. Chrome/Edge 주소창에 `chrome://device-log` 입력 후 열어둔다.
2. 새 탭에서 `about:blank` → F12(개발자도구) → Console 에 아래 붙여넣기:
   ```js
   const d = await navigator.hid.requestDevice({ filters: [{ vendorId: 0x1189, productId: 0x8840 }] });
   const dev = d[0];
   for (const c of dev.collections) {
     console.log('usagePage', c.usagePage.toString(16), 'usage', c.usage.toString(16));
     console.log('  output reports:', c.outputReports.map(r => ({ id: r.reportId, items: r.items.length })));
   }
   ```
3. 키보드를 USB 로 연결한 상태에서 실행 → 장치 선택 팝업에서 키보드 선택.
4. Console 출력에서 **vendor-defined usage page(0xFF00 대)** 컬렉션의 `output reports` 를 확인:
   - `id: 3` 이 보이면 → `sendReport(0x03, payload63)` (우리 기본값) ✅
   - `id: 0` 만 보이면 → `sendReport(0x00, full64)` 로 전환 필요.
   - output report 가 없고 input 만 있으면 → WebUSB(WinUSB) 경로 검토 필요.

> 위 결과를 그대로 붙여주면 `web/js/protocol.js` 의 `REPORT_ID` 상수를 확정해 맞춰준다.

## 방법 B — Windows USB 디스크립터 전체 (선택)

- [Wireshark + USBPcap] 또는 [USB Device Tree Viewer](https://www.uwe-sieber.de/usbtreeview_e.html) 로
  `VID_1189 PID_8840` 의 Interface 0 HID Report Descriptor 를 덤프.
- 또는 기존 벤더 소프트가 설정을 쓰는 순간을 USBPcap 으로 캡처하면, 실제 전송 패킷(03 fe ...)을
  눈으로 검증 가능 → PROTOCOL.md 와 대조.

## 체크리스트
- [ ] MI_00 컬렉션의 usagePage 가 0xFF00 대(vendor-defined)인지
- [ ] output report 의 reportId (3 / 0 / 없음)
- [ ] report count 가 63 또는 64 바이트인지
