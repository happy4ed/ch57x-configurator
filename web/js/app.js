import { KEYCODE_ORDER, MEDIA_CODES } from "./keycodes.js";
import {
  VENDOR_ID, PRODUCT_IDS, uploadProfile, readProfile, readDeviceInfo, sendRawPacket, buttonId, knobId, previewHex,
} from "./protocol.js";

// ---------- model ----------
const DEVCFG_KEY = "ch57x.device.v1";
function loadDevCfg() { try { return JSON.parse(localStorage.getItem(DEVCFG_KEY)); } catch { return null; } }
const devCfg = loadDevCfg() || { keyCount: 9, knobCount: 3 };
let NUM_BUTTONS = devCfg.keyCount;   // manual selector, or auto-detected on connect
let NUM_KNOBS = devCfg.knobCount;
const NUM_LAYERS = 3;
function setDeviceCounts(keyCount, knobCount) {
  NUM_BUTTONS = keyCount; NUM_KNOBS = knobCount;
  devCfg.keyCount = keyCount; devCfg.knobCount = knobCount;
  localStorage.setItem(DEVCFG_KEY, JSON.stringify(devCfg));
}
const KNOB_ACTIONS = [
  { a: 0, icon: "↺", name: "반시계" },
  { a: 1, icon: "⬇", name: "누름" },
  { a: 2, icon: "↻", name: "시계" },
];
const STORAGE_KEY = "ch57x.profile.v1";

// LED modes/colors — see docs/PROTOCOL.md §6. code = (color<<4)|mode
const LED_MODES = [
  { v: 0, label: "끄기" },
  { v: 1, label: "백라이트 (색상)" },
  { v: 5, label: "백라이트 흰색" },
  { v: 4, label: "누르면 켜짐 (색상)" },
  { v: 2, label: "누르면 효과1 (색상)" },
  { v: 3, label: "누르면 효과2 (색상)" },
];
const LED_COLORS = [
  { v: 1, label: "빨강", css: "#e44" }, { v: 2, label: "주황", css: "#f80" },
  { v: 3, label: "노랑", css: "#dd0" }, { v: 4, label: "초록", css: "#3c3" },
  { v: 5, label: "청록", css: "#0cc" }, { v: 6, label: "파랑", css: "#46f" },
  { v: 7, label: "보라", css: "#a4f" },
];
const ledUsesColor = (mode) => mode !== 0 && mode !== 5;

const emptyProfile = () => ({
  name: "내 프로필",
  layers: Array.from({ length: NUM_LAYERS }, () => ({})),
  led: Array.from({ length: NUM_LAYERS }, () => ({ mode: 0, color: 1 })),
});

let profile = loadProfile() || emptyProfile();
// migrate older saved profiles
if (!Array.isArray(profile.led)) profile.led = Array.from({ length: NUM_LAYERS }, () => ({ mode: 0, color: 1 }));
let device = null;
let curLayer = 0;
let selected = null; // { keyId, title }

// ---------- persistence ----------
function saveProfile() {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(profile));
}
function loadProfile() {
  try { return JSON.parse(localStorage.getItem(STORAGE_KEY)); } catch { return null; }
}
const bindingOf = (keyId) => profile.layers[curLayer][keyId] || null;
function setBinding(keyId, b) {
  if (!b || b.type === "none") delete profile.layers[curLayer][keyId];
  else profile.layers[curLayer][keyId] = b;
  saveProfile();
  render();
}

// ---------- summaries ----------
function summarize(b) {
  if (!b) return "—";
  if (b.type === "key") {
    return (b.steps || []).map((s) =>
      [...(s.mods || []), s.code].filter(Boolean).join("+")
    ).join(" → ") || "—";
  }
  if (b.type === "text") return "📝 " + (b.text ? `"${b.text.slice(0, 14)}${b.text.length > 14 ? "…" : ""}"` : "");
  if (b.type === "media") return "🎵 " + (MEDIA_CODES[b.media]?.label || b.media);
  if (b.type === "mouse") return "🖱 " + b.action;
  return "—";
}

// ---------- rendering ----------
const $ = (sel) => document.querySelector(sel);

function render() {
  renderStatus();
  renderDeviceCfg();
  renderLayers();
  renderGrid();
  renderEditor();
  renderLed();
  $("#profileName").value = profile.name;
}

function renderDeviceCfg() {
  const ks = $("#cfgKeys"), ns = $("#cfgKnobs");
  const opts = (n0, n1, sel) => { let s = ""; for (let i = n0; i <= n1; i++) s += `<option value="${i}" ${i === sel ? "selected" : ""}>${i}</option>`; return s; };
  ks.innerHTML = opts(1, 15, NUM_BUTTONS);
  ns.innerHTML = opts(0, 4, NUM_KNOBS);
}

function renderLed() {
  const led = profile.led[curLayer] || { mode: 0, color: 1 };
  const modeSel = $("#ledMode"), colorSel = $("#ledColor");
  modeSel.innerHTML = LED_MODES.map((m) =>
    `<option value="${m.v}" ${m.v === led.mode ? "selected" : ""}>${m.label}</option>`).join("");
  colorSel.innerHTML = LED_COLORS.map((c) =>
    `<option value="${c.v}" ${c.v === led.color ? "selected" : ""}>${c.label}</option>`).join("");
  colorSel.disabled = !ledUsesColor(led.mode);
  $("#ledLayerLabel").textContent = "레이어 " + (curLayer + 1);
}

function renderStatus() {
  const s = $("#status");
  if (device) { s.textContent = "● 연결됨"; s.className = "status ok"; }
  else { s.textContent = "○ 미연결"; s.className = "status off"; }
  $("#uploadBtn").disabled = !device;
  $("#downloadBtn").disabled = !device;
}

function renderLayers() {
  const tabs = $("#layers");
  tabs.innerHTML = "";
  for (let l = 0; l < NUM_LAYERS; l++) {
    const b = document.createElement("button");
    b.textContent = "레이어 " + (l + 1);
    b.className = "tab" + (l === curLayer ? " active" : "");
    b.onclick = () => { curLayer = l; selected = null; render(); };
    tabs.appendChild(b);
  }
}

function selectKey(keyId, title) {
  selected = { keyId, title };
  renderGrid(); renderEditor();
}
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));

function keycap(keyId, label) {
  const d = document.createElement("button");
  const b = bindingOf(keyId);
  d.className = "keycap" + (selected?.keyId === keyId ? " sel" : "") + (b ? " bound" : "");
  d.innerHTML = `<span class="cap-n">${label}</span><span class="cap-sum">${esc(summarize(b))}</span>`;
  d.onclick = () => selectKey(keyId, "버튼 " + label);
  return d;
}

function dial(k) {
  const wrap = document.createElement("div");
  wrap.className = "dial-wrap";
  const big = k === NUM_KNOBS - 1; // last knob is the big one (보통 노브 3)
  wrap.innerHTML = `<div class="dial-circle${big ? " big" : ""}"><span>노브 ${k + 1}</span></div>`;
  const acts = document.createElement("div");
  acts.className = "dial-acts";
  for (const act of KNOB_ACTIONS) {
    const id = knobId(k, act.a);
    const b = bindingOf(id);
    const chip = document.createElement("button");
    chip.className = "dial-chip" + (selected?.keyId === id ? " sel" : "") + (b ? " bound" : "");
    chip.innerHTML = `<span class="chip-ic">${act.icon}</span><span class="chip-sum">${esc(summarize(b))}</span>`;
    chip.title = act.name;
    chip.onclick = () => selectKey(id, `노브 ${k + 1} ${act.icon}${act.name}`);
    acts.appendChild(chip);
  }
  wrap.appendChild(acts);
  return wrap;
}

function renderGrid() {
  const keys = $("#keys");
  keys.innerHTML = "";
  keys.style.gridTemplateColumns = `repeat(${Math.min(NUM_BUTTONS, 3) || 1}, 1fr)`;
  for (let n = 0; n < NUM_BUTTONS; n++) keys.appendChild(keycap(buttonId(n), n + 1));
  const dials = $("#dials");
  dials.innerHTML = "";
  dials.style.display = NUM_KNOBS ? "flex" : "none";
  for (let k = 0; k < NUM_KNOBS; k++) dials.appendChild(dial(k));
}

function keyOptions(selectedCode) {
  return ['<option value="">(없음)</option>'].concat(
    KEYCODE_ORDER.map(([name, label]) =>
      `<option value="${name}" ${name === selectedCode ? "selected" : ""}>${label} (${name})</option>`)
  ).join("");
}

let editSteps = []; // working list of {mods, code} while editing a key sequence

function renderEditor() {
  const ed = $("#editor");
  if (!selected) { ed.innerHTML = `<p class="hint">키나 노브 동작을 클릭해 설정하세요.</p>`; return; }
  const b = bindingOf(selected.keyId) || { type: "none" };
  const type = b.type || "none";

  const TYPES = [["none", "없음"], ["key", "⌨ 키보드"], ["text", "📝 상용구"], ["media", "🎵 미디어"], ["mouse", "🖱 마우스"]];
  ed.innerHTML = `
    <h3>${esc(selected.title)} <span class="dim">· 레이어 ${curLayer + 1}</span></h3>
    <div class="type-btns" id="edTypeBtns">
      ${TYPES.map(([v, l]) => `<button type="button" class="type-btn ${v === type ? "active" : ""}" data-type="${v}">${l}</button>`).join("")}
    </div>
    <input type="hidden" id="edType" value="${type}">
    <div id="edBody"></div>
    <div class="ed-actions">
      <button id="edSave" class="primary">적용</button>
      <button id="edClear">이 키 비우기</button>
    </div>
    <details class="dbg"><summary>전송 패킷 미리보기</summary><pre id="edHex"></pre></details>
  `;
  $("#edTypeBtns").querySelectorAll(".type-btn").forEach((btn) => btn.onclick = () => {
    $("#edType").value = btn.dataset.type;
    $("#edTypeBtns").querySelectorAll(".type-btn").forEach((x) => x.classList.toggle("active", x === btn));
    renderEditorBody(btn.dataset.type, b);
  });
  $("#edSave").onclick = applyEditor;
  $("#edClear").onclick = () => setBinding(selected.keyId, null);
  renderEditorBody(type, b);
}

function stepRow(s, i, removable) {
  const mk = (m) => `<label class="chk"><input type="checkbox" data-mod="${m}" ${s.mods?.includes(m)?"checked":""}>${m}</label>`;
  return `<div class="step" data-i="${i}">
    <span class="step-n">${i + 1}</span>
    <span class="mods">${["Ctrl","Shift","Alt","Win"].map(mk).join("")}</span>
    <select class="step-key">${keyOptions(s.code)}</select>
    ${removable ? `<button type="button" class="step-del" data-i="${i}">✕</button>` : ""}
  </div>`;
}
function syncSteps() {
  editSteps = [...document.querySelectorAll("#steps .step")].map((r) => ({
    mods: [...r.querySelectorAll("[data-mod]:checked")].map((e) => e.dataset.mod),
    code: r.querySelector(".step-key").value || null,
  }));
}
function renderSteps() {
  const wrap = $("#steps");
  wrap.innerHTML = editSteps.map((s, i) => stepRow(s, i, editSteps.length > 1)).join("");
  wrap.querySelectorAll(".step-del").forEach((btn) => btn.onclick = () => {
    syncSteps(); editSteps.splice(Number(btn.dataset.i), 1); renderSteps(); refreshHex();
  });
}

function renderMouseBody(b) {
  const body = $("#edBody");
  const act = b.action || "click";
  const fld = (id, label, v) => `<label>${label} <input id="${id}" type="number" value="${v || 0}"></label>`;
  const modOpts = ["", "Ctrl", "Shift", "Alt"].map((m) => `<option value="${m}" ${(b.mod || "") === m ? "selected" : ""}>${m || "(없음)"}</option>`).join("");
  const btns = ["Left", "Right", "Middle"].map((m) => `<label class="chk"><input type="checkbox" data-mbtn="${m}" ${b.buttons?.includes(m)?"checked":""}>${m}</label>`).join("");
  let f = "";
  if (act === "click" || act === "drag") f += `<div class="mods">${btns}</div>`;
  if (act === "move" || act === "drag") f += fld("edDx", "dx", b.dx) + fld("edDy", "dy", b.dy);
  if (act === "wheel") f += fld("edDelta", "휠(+위/−아래)", b.delta);
  body.innerHTML = `
    <label>동작 <select id="edMAct">${["click","wheel","move","drag"].map(a=>`<option ${act===a?"selected":""}>${a}</option>`).join("")}</select></label>
    <label>수정자 <select id="edMMod">${modOpts}</select></label>
    ${f}`;
  $("#edMAct").onchange = () => { const cur = readEditor(); cur.action = $("#edMAct").value; renderMouseBody(cur); refreshHex(); };
}

function renderEditorBody(type, b) {
  const body = $("#edBody");
  if (type === "key") {
    editSteps = (b.type === "key" && b.steps?.length)
      ? b.steps.map((s) => ({ mods: (s.mods || []).slice(), code: s.code || "" }))
      : [{ mods: [], code: "" }];
    body.innerHTML = `<div id="steps"></div>
      <button type="button" id="addStep">+ 단계 추가</button>
      <label>지연(ms, 0=없음) <input id="edDelay" type="number" min="0" max="6000" value="${b.delay||0}"></label>
      <p class="hint">여러 단계를 순서대로 누릅니다 (최대 18). 단축키 하나면 한 단계만 두세요.</p>`;
    renderSteps();
    $("#addStep").onclick = () => { syncSteps(); if (editSteps.length < 18) { editSteps.push({ mods: [], code: "" }); renderSteps(); refreshHex(); } };
  } else if (type === "text") {
    body.innerHTML = `
      <label>텍스트 (상용구)
        <textarea id="edText" rows="2" maxlength="40" placeholder="예: hello@example.com">${b.type==="text"?(b.text||""):""}</textarea></label>
      <label>지연(ms, 0=없음) <input id="edDelay" type="number" min="0" max="6000" value="${b.delay||0}"></label>
      <p class="hint">문자를 키 시퀀스로 자동 변환합니다 (US 배열, 최대 18자). 한글/IME는 불가.</p>`;
  } else if (type === "media") {
    const opts = Object.entries(MEDIA_CODES).map(([k, v]) =>
      `<option value="${k}" ${b.media===k?"selected":""}>${v.label} (${k})</option>`).join("");
    body.innerHTML = `<label>미디어 키 <select id="edMedia">${opts}</select></label>`;
  } else if (type === "mouse") {
    renderMouseBody(b);
  } else {
    body.innerHTML = `<p class="hint">이 키는 비어 있습니다 (업로드 시 펌웨어 기본값).</p>`;
  }
  refreshHex();
}

function readEditor() {
  const type = $("#edType").value;
  const num = (id) => { const el = document.getElementById(id); return el ? Number(el.value) || 0 : 0; };
  if (type === "key") {
    syncSteps();
    const steps = editSteps.filter((s) => s.mods.length || s.code);
    if (!steps.length) return { type: "none" };
    return { type: "key", steps, delay: num("edDelay") };
  }
  if (type === "text") {
    const t = $("#edText").value;
    if (!t) return { type: "none" };
    return { type: "text", text: t, delay: num("edDelay") };
  }
  if (type === "media") return { type: "media", media: $("#edMedia").value };
  if (type === "mouse") {
    return {
      type: "mouse",
      action: $("#edMAct").value,
      mod: $("#edMMod")?.value || "",
      buttons: [...document.querySelectorAll("[data-mbtn]:checked")].map((e) => e.dataset.mbtn),
      dx: num("edDx"), dy: num("edDy"), delta: num("edDelta"),
    };
  }
  return { type: "none" };
}

function refreshHex() {
  const pre = $("#edHex");
  if (!pre || !selected) return;
  try {
    const b = readEditor();
    pre.textContent = previewHex(selected.keyId, curLayer, b).join("\n") || "(전송 없음)";
  } catch (e) { pre.textContent = "⚠ " + e.message; }
}

function applyEditor() {
  try {
    setBinding(selected.keyId, readEditor());
    toast("적용됨");
  } catch (e) { toast("⚠ " + e.message); }
}

// ---------- WebHID ----------
async function connect() {
  if (!("hid" in navigator)) { toast("이 브라우저는 WebHID 미지원 (Chrome/Edge 사용)"); return; }
  try {
    const filters = PRODUCT_IDS.map((productId) => ({ vendorId: VENDOR_ID, productId }));
    const devices = await navigator.hid.requestDevice({ filters });
    if (!devices.length) return;
    device = devices[0];
    if (!device.opened) await device.open();
    device.addEventListener?.("disconnect", () => { device = null; render(); });
    device.addEventListener?.("inputreport", onInputReport);
    render();
    renderDiag();
    $("#reWrap").style.display = "block";
    toast("연결됨: " + device.productName);
    // best-effort auto-detect of physical key/knob count (some firmware ignores 0xfb)
    const info = await readDeviceInfo(device);
    if (info && info.keyCount >= 1 && info.keyCount <= 15 && info.knobCount <= 4) {
      setDeviceCounts(info.keyCount, info.knobCount);
      selected = null; render();
      toast(`기기 감지: 키 ${NUM_BUTTONS}개 · 노브 ${NUM_KNOBS}개`);
    }
    // else: keep manual "기기 구성" selection (silent)
  } catch (e) { toast("연결 실패: " + e.message); }
}

// Dump HID collections so we can confirm sendReport() args (REPORT_ID).
function renderDiag() {
  const wrap = $("#diagWrap"), pre = $("#diag");
  if (!device) { wrap.style.display = "none"; return; }
  const lines = [`${device.productName}  (VID ${device.vendorId.toString(16)} / PID ${device.productId.toString(16)})`];
  let verdict = "출력 리포트를 못 찾음 → WebUSB 경로 검토 필요";
  const fmt = (rs) => rs?.length ? rs.map(r => `id ${r.reportId} (${r.items?.reduce((n,i)=>n+(i.reportCount||0),0)}B)`).join(", ") : "없음";
  let vendorHasInput = false;
  for (const c of device.collections) {
    const up = c.usagePage, vendorDef = up >= 0xff00;
    lines.push(`\ncollection: usagePage 0x${up.toString(16)} usage 0x${c.usage.toString(16)}${vendorDef ? "  ← vendor-defined" : ""}`);
    lines.push(`  output : ${fmt(c.outputReports)}`);
    lines.push(`  input  : ${fmt(c.inputReports)}`);
    lines.push(`  feature: ${fmt(c.featureReports)}`);
    for (const r of (c.outputReports || [])) {
      if (vendorDef && r.reportId === 3) verdict = "✅ id 3 발견 → REPORT_ID = 3 (쓰기 OK)";
      else if (vendorDef && r.reportId === 0 && verdict.startsWith("출력")) verdict = "⚠ id 0 → protocol.js 의 REPORT_ID 를 0 으로";
    }
    if (vendorDef && ((c.inputReports?.length) || (c.featureReports?.length))) vendorHasInput = true;
  }
  lines.push(`\n쓰기 판정: ${verdict}`);
  lines.push(`읽기 가능성: ${vendorHasInput
    ? "△ vendor 인터페이스에 input/feature 리포트 있음 — 읽기 RE 시도해볼 여지 있음"
    : "✗ vendor 인터페이스에 input/feature 없음 — 현재 설정 읽기 불가 (펌웨어 한계)"}`);
  pre.textContent = lines.join("\n");
  wrap.style.display = "block";
}

// ---------- RE console: listen to input reports + send probes ----------
const reLog = [];
const hex = (u8) => Array.from(u8).map((b) => b.toString(16).padStart(2, "0")).join(" ");

function onInputReport(e) {
  const bytes = new Uint8Array(e.data.buffer);
  const t = new Date().toLocaleTimeString();
  reLog.unshift(`◀ [${t}] IN  id ${e.reportId}: ${hex(bytes)}`);
  renderReLog();
}

function renderReLog() {
  const pre = $("#reLog");
  if (pre) pre.textContent = reLog.slice(0, 60).join("\n") || "대기 중…";
}

function parseHex(s) {
  return s.trim().split(/[\s,]+/).filter(Boolean).map((x) => parseInt(x, 16) & 0xff);
}

async function reSend(hexStr) {
  if (!device) { toast("먼저 연결하세요"); return; }
  try {
    const bytes = parseHex(hexStr);
    if (!bytes.length) return;
    await sendRawPacket(device, Uint8Array.from(bytes));
    reLog.unshift(`▶ [${new Date().toLocaleTimeString()}] OUT id 3: ${hex(Uint8Array.from(bytes))}`);
    renderReLog();
  } catch (err) { toast("전송 실패: " + err.message); }
}

async function download() {
  if (!device) return;
  const bar = $("#progress");
  bar.style.display = "block"; bar.value = 0; bar.max = 1;
  try {
    const map = await readProfile(device, { onProgress: (i, n) => { bar.value = i; bar.max = n; } });
    // group read bindings by the layer the device reported
    const byLayer = {};
    for (const p of map.values()) {
      const li = Math.min(Math.max((p.layer || 1) - 1, 0), 2);
      (byLayer[li] ||= {});
      if (p.binding) byLayer[li][p.keyId] = p.binding;
    }
    const seen = Object.keys(byLayer).map(Number);
    if (!seen.length) { toast("응답 없음 (키보드 연결 확인)"); return; }
    if (!confirm(`키보드에서 레이어 ${seen.map((l) => l + 1).join(", ")} 를 읽었습니다. 해당 레이어를 덮어쓸까요? (나머지 레이어는 유지)`)) return;
    let n = 0;
    for (const li of seen) { profile.layers[li] = byLayer[li]; n += Object.keys(byLayer[li]).length; }
    saveProfile(); selected = null; render();
    toast(`불러오기 완료: 레이어 ${seen.map((l) => l + 1).join(",")} · ${n}개 키`);
  } catch (e) {
    toast("불러오기 실패: " + e.message);
  } finally {
    setTimeout(() => { bar.style.display = "none"; }, 800);
  }
}

async function upload() {
  if (!device) return;
  const bar = $("#progress");
  bar.style.display = "block";
  try {
    const n = await uploadProfile(device, profile, (i, total) => {
      bar.value = i; bar.max = total;
    });
    toast(`업로드 완료 (${n} 패킷, 전체 레이어 전송)`);
  } catch (e) {
    toast("업로드 실패: " + e.message);
  } finally {
    setTimeout(() => { bar.style.display = "none"; }, 800);
  }
}

// ---------- profile import/export ----------
function exportProfile() {
  const blob = new Blob([JSON.stringify(profile, null, 2)], { type: "application/json" });
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = (profile.name || "profile") + ".json";
  a.click();
}
function importProfile(file) {
  const r = new FileReader();
  r.onload = () => {
    try {
      const p = JSON.parse(r.result);
      if (!Array.isArray(p.layers)) throw new Error("형식 오류");
      if (!Array.isArray(p.led)) p.led = Array.from({ length: NUM_LAYERS }, () => ({ mode: 0, color: 1 }));
      profile = p; saveProfile(); selected = null; render(); toast("불러옴");
    } catch (e) { toast("불러오기 실패: " + e.message); }
  };
  r.readAsText(file);
}

// ---------- toast ----------
let toastTimer;
function toast(msg) {
  const t = $("#toast");
  t.textContent = msg; t.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove("show"), 2200);
}

// ---------- wire up ----------
$("#connectBtn").onclick = connect;
$("#uploadBtn").onclick = upload;
$("#downloadBtn").onclick = download;
$("#ledMode").onchange = (e) => { profile.led[curLayer].mode = Number(e.target.value); saveProfile(); renderLed(); };
$("#ledColor").onchange = (e) => { profile.led[curLayer].color = Number(e.target.value); saveProfile(); };
$("#cfgKeys").onchange = (e) => { setDeviceCounts(Number(e.target.value), NUM_KNOBS); selected = null; render(); };
$("#cfgKnobs").onchange = (e) => { setDeviceCounts(NUM_BUTTONS, Number(e.target.value)); selected = null; render(); };
$("#exportBtn").onclick = exportProfile;
$("#importInput").onchange = (e) => e.target.files[0] && importProfile(e.target.files[0]);
$("#clearLayerBtn").onclick = () => { if (confirm(`레이어 ${curLayer + 1} 의 모든 키를 비울까요?`)) { profile.layers[curLayer] = {}; saveProfile(); selected = null; render(); } };
$("#resetBtn").onclick = () => { if (confirm("전체 레이어를 모두 비울까요?")) { profile = emptyProfile(); saveProfile(); selected = null; render(); } };
$("#profileName").oninput = (e) => { profile.name = e.target.value; saveProfile(); };
$("#reSend").onclick = () => reSend($("#reHex").value);
$("#reClear").onclick = () => { reLog.length = 0; renderReLog(); };
document.querySelectorAll(".re-probe").forEach((b) => {
  b.onclick = () => { $("#reHex").value = b.dataset.hex; reSend(b.dataset.hex); };
});
document.addEventListener("input", (e) => { if (e.target.closest("#editor")) refreshHex(); });

render();
