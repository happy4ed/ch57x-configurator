// CH57x packet builder + WebHID upload.
// Faithful port of ch57x-keyboard-tool src/keyboard/k884x.rs. See docs/PROTOCOL.md.
import { KEYCODES, MODIFIERS, MEDIA_CODES, MOUSE_BUTTONS, MOUSE_MODIFIERS } from "./keycodes.js";

export const VENDOR_ID = 0x1189;
export const PRODUCT_IDS = [0x8840, 0x8842, 0x8890];

// Report ID. Confirmed default = 3 (numbered report). If the descriptor dump
// (docs/HID-DESCRIPTOR-DUMP.md) shows unnumbered reports, set this to 0.
export const REPORT_ID = 0x03;

const PACKET_SIZE = 64;

function pad64(bytes) {
  const buf = new Uint8Array(PACKET_SIZE);
  buf.set(bytes.slice(0, PACKET_SIZE));
  return buf;
}

const i8 = (n) => n & 0xff;

function modByte(mods = []) {
  return mods.reduce((acc, m) => acc | (MODIFIERS[m] || 0), 0);
}

// keyId per docs/PROTOCOL.md §4 (9-button + 3-knob layout).
export function buttonId(n) { return n + 1; }               // n: 0-based button index
export function knobId(knob, action) { return 16 + 3 * knob + action; } // action: 0=ccw,1=press,2=cw

// Build all 64-byte messages for one key binding (bind + optional delay + commit sequence).
// binding: null | {type:"key", steps:[{mods,code}], delay} | {type:"media", media}
//          | {type:"mouse", action, buttons, mod, dx, dy, delta}
export function buildKeyMessages(keyId, layer, binding) {
  if (!binding || binding.type === "none") return [];

  const kind = { key: 1, media: 2, mouse: 3 }[binding.type];
  const msg = [0x03, 0xfe, keyId, layer + 1, kind, 0, 0, 0, 0, 0];

  if (binding.type === "key") {
    const steps = binding.steps || [];
    if (steps.length === 0) return [];
    if (steps.length > 18) throw new Error("매크로 시퀀스가 너무 깁니다 (최대 18)");
    // single modifier-only accord -> count 0 (combo modifier)
    if (steps.length === 1 && !steps[0].code) msg.push(0);
    else msg.push(steps.length);
    for (const s of steps) {
      const code = s.code ? (KEYCODES[s.code]?.code ?? 0) : 0;
      msg.push(modByte(s.mods), code);
    }
  } else if (binding.type === "media") {
    const code = MEDIA_CODES[binding.media]?.code ?? 0;
    msg.push(0, code & 0xff, (code >> 8) & 0xff);
  } else if (binding.type === "mouse") {
    const mod = binding.mod ? (MOUSE_MODIFIERS[binding.mod] || 0) : 0;
    const btns = (binding.buttons || []).reduce((a, b) => a | (MOUSE_BUTTONS[b] || 0), 0);
    switch (binding.action) {
      case "click": msg.push(0x01, mod, btns); break;
      case "wheel": msg.push(0x03, mod, 0, 0, 0, i8(binding.delta || 0)); break;
      case "move":  msg.push(0x05, mod, 0, i8(binding.dx || 0), i8(binding.dy || 0)); break;
      case "drag":  msg.push(0x05, mod, btns, i8(binding.dx || 0), i8(binding.dy || 0)); break;
      default: throw new Error("알 수 없는 마우스 동작: " + binding.action);
    }
  }

  const messages = [pad64(msg)];

  // optional key-macro delay packet
  if (binding.type === "key" && binding.delay) {
    const d = binding.delay;
    if (d > 6000) throw new Error("지원하는 최대 지연은 6000ms 입니다");
    messages.push(pad64([0x03, 0xfe, keyId, layer + 1, 5, d & 0xff, (d >> 8) & 0xff]));
  }

  // commit / end-programming sequence (★ prevents partial-reset bug)
  messages.push(pad64([0x03, 0xaa, 0xaa]));
  messages.push(pad64([0x03, 0xfd, 0xfe, 0xff]));
  messages.push(pad64([0x03, 0xaa, 0xaa]));
  return messages;
}

// LED packet (docs/PROTOCOL.md §6). mode: 0..5, color: 0..7
export function buildLedMessages(layer, mode, color) {
  const code = ((color & 0x0f) << 4) | (mode & 0x0f);
  return [
    pad64([0x03, 0xfe, 0xb0, layer + 1, 0x08, 0, 0, 0, 0, 0, 0x01, 0, code]),
    pad64([0x03, 0xfd, 0xfe, 0xff]),
  ];
}

// Declared OUTPUT-report data length for REPORT_ID (excludes the report-id byte).
// Read from the device descriptor so we match exactly what the firmware expects
// (the diagnostic showed id 3 = 64 fields). Falls back to 63 if unknown.
function outputReportLength(device, reportId) {
  for (const c of device.collections || []) {
    for (const r of c.outputReports || []) {
      if (r.reportId === reportId) {
        const bits = (r.items || []).reduce((n, i) => n + (i.reportSize || 8) * (i.reportCount || 0), 0);
        if (bits) return Math.ceil(bits / 8);
      }
    }
  }
  return 63;
}

// Send one packet via WebHID, sized to the report's declared data length.
async function sendPacket(device, packet, dataLen) {
  if (REPORT_ID === 0) {
    await device.sendReport(0, packet);
    return;
  }
  const len = dataLen || 63;
  const data = new Uint8Array(len);
  data.set(packet.subarray(1, 1 + len)); // drop report-id byte, pad/truncate to len
  await device.sendReport(REPORT_ID, data);
}

// Upload the WHOLE profile: every bound key across ALL layers, each with its
// commit sequence. This is what makes resets impossible — nothing is left partial.
// profile.layers[L] is an object { keyId: binding }.
export async function uploadProfile(device, profile, onProgress) {
  const all = [];
  for (let layer = 0; layer < profile.layers.length; layer++) {
    const layerKeys = profile.layers[layer] || {};
    for (const keyIdStr of Object.keys(layerKeys)) {
      const msgs = buildKeyMessages(Number(keyIdStr), layer, layerKeys[keyIdStr]);
      all.push(...msgs);
    }
  }
  const dataLen = outputReportLength(device, REPORT_ID);
  for (let i = 0; i < all.length; i++) {
    await sendPacket(device, all[i], dataLen);
    onProgress?.(i + 1, all.length);
  }
  return all.length;
}

// Convenience: build messages as hex strings (for the dry-run / 디버그 view).
export function previewHex(keyId, layer, binding) {
  return buildKeyMessages(keyId, layer, binding).map((p) =>
    Array.from(p.slice(0, 16)).map((b) => b.toString(16).padStart(2, "0")).join(" ") + " …"
  );
}
