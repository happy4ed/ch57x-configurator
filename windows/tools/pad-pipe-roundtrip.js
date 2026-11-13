// CH57x PC 앱 0.3.1 — 권한 질문 파이프 왕복 시험 (CI windows-latest · 앱을 --pipe-test 로 띄운 뒤).
// 붙는 쪽은 에이전트 패드 확장의 src/pcPipe.ts 와 같은 방식(같은 이름 규칙 · net.connect · 줄 단위 JSON · 300ms 안에 못 붙으면 없음).
// pcPipe.ts 진본은 autographer 저장소(비공개)라 여기 베끼지 않고 동작만 같게 다시 적었다 — 바뀌면 이 파일도 맞출 것.
//
//   node pad-pipe-roundtrip.js run <pipe-test.log>     각본 시험 (앱이 「준비됨」을 적은 뒤)
//   node pad-pipe-roundtrip.js squat <ms> <ready 파일>  같은 이름 파이프를 먼저 열고 ms 동안 쥐고 있음(FirstPipeInstance 시험)
'use strict';
const net = require('node:net');
const os = require('node:os');
const fs = require('node:fs');

const PIPE_PROTO = 1;
const DECISIONS = ['allow', 'allow_always', 'deny', 'none'];

// ── pcPipe.ts pipePath 와 같음
function pipePath(platform = process.platform, env = process.env) {
  if (env.CH57X_PAD_PIPE) return env.CH57X_PAD_PIPE;
  if (platform !== 'win32') return undefined;
  let user = env.USERNAME || '';
  try {
    user = os.userInfo().username || user;
  } catch {
    // 사용자 이름을 못 읽음
  }
  return `\\\\.\\pipe\\ch57x-agent-pad-${user.replace(/[^A-Za-z0-9._-]/g, '_')}`;
}

// ── pcPipe.ts sendToPcApp 와 같음 (+ 시험용 closedByApp: 앱이 먼저 끊었는지)
function sendToPcApp(path, req, deadline, connectMs = 300) {
  if (!path) return Promise.resolve(undefined);
  return new Promise((resolve) => {
    let settled = false;
    const sock = net.connect(path);
    const timer = setTimeout(() => fail(), connectMs);
    function fail() {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      sock.destroy();
      resolve(undefined);
    }
    sock.on('error', () => fail());
    sock.on('connect', () => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      let answered = () => undefined;
      const answer = new Promise((r) => (answered = r));
      const extra = { closedByApp: false, others: [] };
      let ended = false;
      let buf = '';
      sock.setEncoding('utf8');
      sock.on('data', (chunk) => {
        buf += chunk;
        if (buf.length > 64 * 1024) return void sock.destroy();
        let i;
        while ((i = buf.indexOf('\n')) >= 0) {
          const line = buf.slice(0, i);
          buf = buf.slice(i + 1);
          let m;
          try {
            m = JSON.parse(line);
          } catch {
            continue;
          }
          // 다른 request_id 의 답은 버림 (시험은 따로 적어 둠)
          if (m && m.type === 'decision' && m.request_id === req.request_id && DECISIONS.includes(m.decision)) answered(m.decision);
          else extra.others.push(line);
        }
      });
      sock.on('close', () => {
        extra.closedByApp = !ended;
        answered(undefined);
      });
      sock.on('error', () => answered(undefined));
      sock.write(JSON.stringify({ type: 'permission', v: PIPE_PROTO, ...req, deadline }) + '\n');
      resolve({
        answer,
        extra,
        destroy: () => {
          ended = true;
          sock.destroy();
        },
        close: () => {
          ended = true;
          if (!sock.destroyed) {
            sock.write(JSON.stringify({ type: 'close', request_id: req.request_id }) + '\n');
            sock.end();
          }
        },
      });
    });
  });
}

// ── 날 연결 (약속에 안 맞는 줄 시험)
function raw(path) {
  return new Promise((resolve, reject) => {
    const sock = net.connect(path);
    let data = '';
    let closed = false;
    const waiters = [];
    sock.setEncoding('utf8');
    sock.on('data', (c) => {
      data += c;
      waiters.splice(0).forEach((w) => w());
    });
    sock.on('close', () => {
      closed = true;
      waiters.splice(0).forEach((w) => w());
    });
    sock.on('error', () => undefined);
    sock.on('connect', () =>
      resolve({
        sock,
        get data() {
          return data;
        },
        get closed() {
          return closed;
        },
        write: (s) => new Promise((r) => sock.write(s, () => r())),
        /** 끊길 때까지(최대 ms) — 끊겼으면 true */
        waitClosed: (ms) =>
          new Promise((r) => {
            if (closed) return r(true);
            const t = setTimeout(() => r(closed), ms);
            waiters.push(() => {
              if (closed) (clearTimeout(t), r(true));
            });
          }),
        /** 한 줄이 올 때까지 */
        waitLine: (ms) =>
          new Promise((r) => {
            const check = () => {
              const i = data.indexOf('\n');
              if (i >= 0) return (clearTimeout(t), r(data.slice(0, i)), true);
              if (closed) return (clearTimeout(t), r(undefined), true);
              return false;
            };
            const t = setTimeout(() => r(undefined), ms);
            if (!check()) waiters.push(function again() { if (!check()) waiters.push(again); });
          }),
      }),
    );
    sock.on('error', (e) => reject(e));
  });
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const race = (p, ms, v = 'no-answer') => Promise.race([p, sleep(ms).then(() => v)]);
let seq = 0;
// 앞 8자가 앱 기록(request_id 앞 8자)에서 겹치지 않게 — 무작위 8자를 앞에
const rid = (tag) => `${Math.random().toString(16).slice(2, 10).padEnd(8, '0')}-${tag}-${++seq}`;
const base = (over = {}) => ({
  request_id: rid('t'),
  cli: 'claude',
  tool: 'test:wait',
  summary: 'npm test -- [링크](http://e.x) ‮evil',
  where: '터미널 「bash」 · tmux ci:0.0 · repo',
  timeout_ms: 120000,
  session: 's' + seq,
  // 훅 0.3.2 rulesSaid 꼴 (60자 상한 규칙 내용)
  yes_all: '이 저장소에서(내 로컬 설정) 「npm test」 로 시작하는 명령은 다시 묻지 않음',
  ...over,
});

async function runAll(logPath) {
  const path = pipePath();
  console.log(`파이프 이름(pcPipe.ts 방식): ${path} · os.userInfo().username=${os.userInfo().username} · USERNAME=${process.env.USERNAME}`);
  const log = () => (fs.existsSync(logPath) ? fs.readFileSync(logPath, 'utf8') : '');
  const results = [];
  async function t(name, fn) {
    const t0 = Date.now();
    let ok = false;
    let detail = '';
    try {
      const r = await fn();
      ok = r === true || (r && r.ok);
      detail = r && r.detail ? r.detail : '';
    } catch (e) {
      detail = '예외 ' + (e && e.stack ? e.stack : e);
    }
    results.push({ name, ok, ms: Date.now() - t0, detail });
    console.log(`${ok ? 'PASS' : 'FAIL'} ${name} (${Date.now() - t0}ms) ${detail}`);
    await sleep(150);
  }
  const now = () => Date.now();

  await t('1 패드 F19(진짜 키 입력) → allow · 1초 뒤', async () => {
    const t0 = now();
    const s = await sendToPcApp(path, base({ tool: 'test:key:F19@1200' }), now() + 120000);
    if (!s) return { ok: false, detail: '못 붙음(300ms)' };
    const d = await race(s.answer, 5000);
    const ms = now() - t0;
    s.close();
    return { ok: d === 'allow' && ms >= 1000, detail: `답 ${d} · ${ms}ms` };
  });

  await t('2 바뀐 지 1초 안 F21 무시 → 뒤 F20 = allow_always', async () => {
    const r = base({ tool: 'test:key:F21@300,key:F20@1400' });
    const s = await sendToPcApp(path, r, now() + 120000);
    if (!s) return { ok: false, detail: '못 붙음' };
    const d = await race(s.answer, 5000);
    s.close();
    const ignored = log().includes(`${r.request_id.slice(0, 8)} — 바뀐 지 1초 안이라 패드 9 No 무시`);
    return { ok: d === 'allow_always' && ignored, detail: `답 ${d} · 무시 기록 ${ignored}` };
  });

  await t('3 단추 No → deny', async () => {
    const s = await sendToPcApp(path, base({ tool: 'test:click:deny@1200' }), now() + 120000);
    const d = await race(s.answer, 5000);
    s.close();
    return { ok: d === 'deny', detail: `답 ${d}` };
  });

  await t('4 Codex · 단추 Yes · No 둘 · 패드 8(F20) = Yes → allow', async () => {
    const r = base({ cli: 'codex', yes_all: 'Codex 는 이번만 허용', tool: 'test:key:F20@1200', timeout_ms: 60000 });
    const s = await sendToPcApp(path, r, now() + 60000);
    const d = await race(s.answer, 5000);
    s.close();
    const two = log().includes(`] 보임 ${r.request_id} tool=test:key:F20@1200 대기=0 단추=Yes·No `);
    return { ok: d === 'allow' && two, detail: `답 ${d} · 단추 둘 ${two}` };
  });

  await t('5 두 창 동시 — 한 건씩 · 답은 제 연결로 · 대기 1건 · 다음 건도 1초', async () => {
    const a = base({ tool: 'test:click:allow@1200' });
    const b = base({ tool: 'test:key:F21@1200' });
    const sa = await sendToPcApp(path, a, now() + 120000);
    await sleep(150);
    const sb = await sendToPcApp(path, b, now() + 120000);
    let ta = 0;
    let tb = 0;
    const [da, db] = await Promise.all([
      race(sa.answer.then((d) => ((ta = now()), d)), 6000),
      race(sb.answer.then((d) => ((tb = now()), d)), 8000),
    ]);
    sa.close();
    sb.close();
    const waitingShown = log().includes(`${a.request_id} 각본 click:allow · 남은 시간 칸 「`) && /각본 click:allow · 남은 시간 칸 「\d+초 뒤 팝업만 닫힘 · 터미널에서 답」 · 대기 칸 「뒤에 1건 더」/.test(log());
    const cross = sa.extra.others.length + sb.extra.others.length;
    return { ok: da === 'allow' && db === 'deny' && tb - ta >= 1000 && waitingShown && cross === 0, detail: `A ${da} · B ${db} · 간격 ${tb - ta}ms · 대기 1건 보임 ${waitingShown} · 남의 답 ${cross}` };
  });

  await t('6 close → 그 팝업 닫힘 · 다음 질문이 바로', async () => {
    const a = base({ tool: 'test:wait' });
    const sa = await sendToPcApp(path, a, now() + 120000);
    await sleep(400);
    sa.close();
    const sb = await sendToPcApp(path, base({ tool: 'test:key:F19@1200' }), now() + 120000);
    const d = await race(sb.answer, 4000);
    sb.close();
    const closed = log().includes(`${a.request_id.slice(0, 8)} 닫힘 — 다른 곳에서 답함(확장 close)`);
    return { ok: d === 'allow' && closed, detail: `B ${d} · A 닫힘 기록 ${closed}` };
  });

  await t('7 다른 연결의 close 는 무시', async () => {
    const a = base({ tool: 'test:click:deny@1500' });
    const sa = await sendToPcApp(path, a, now() + 120000);
    await sleep(300);
    const r = await raw(path);
    await r.write(JSON.stringify({ type: 'close', request_id: a.request_id }) + '\n');
    const d = await race(sa.answer, 5000);
    sa.close();
    r.sock.destroy();
    return { ok: d === 'deny', detail: `A ${d}` };
  });

  await t('8 시간 초과(deadline 1.5초) → 닫고 답 안 함', async () => {
    const a = base({ tool: 'test:wait', timeout_ms: 1500 });
    const sa = await sendToPcApp(path, a, now() + 1500);
    const d = await race(sa.answer, 3000);
    const timedOut = log().includes(`${a.request_id.slice(0, 8)} 시간 초과 — 닫음 · 답 안 함`);
    sa.close();
    return { ok: d === 'no-answer' && timedOut, detail: `답 ${d} · 시간 초과 기록 ${timedOut}` };
  });

  await t('9 이미 지난 deadline → 안 띄움 · 답 안 함', async () => {
    const a = base({ tool: 'test:click:allow@100' });
    const sa = await sendToPcApp(path, a, now() - 1000);
    const d = await race(sa.answer, 1200);
    sa.close();
    const skipped = log().includes(`권한 질문 ${a.request_id.slice(0, 8)} — 이미 받았거나 시간이 지나 안 띄움`);
    return { ok: d === 'no-answer' && skipped, detail: `답 ${d} · 안 띄움 기록 ${skipped}` };
  });

  await t('10 64KB 넘는 줄 → 앱이 끊음', async () => {
    const r = await raw(path);
    await r.write('a'.repeat(70000));
    const c = await r.waitClosed(3000);
    return { ok: c && r.data === '', detail: `끊김 ${c} · 받은 글 ${r.data.length}` };
  });

  await t('11 JSON 아닌 줄 → 끊음', async () => {
    const r = await raw(path);
    await r.write('{bad\n');
    const c = await r.waitClosed(3000);
    return { ok: c, detail: `끊김 ${c}` };
  });

  await t('12 판 v:2 → 끊음 (답 없음)', async () => {
    const r = await raw(path);
    await r.write(JSON.stringify({ type: 'permission', v: 2, ...base({ tool: 'test:click:allow@100' }), deadline: now() + 60000 }) + '\n');
    const c = await r.waitClosed(3000);
    return { ok: c && r.data === '', detail: `끊김 ${c} · 받은 글 ${JSON.stringify(r.data)}` };
  });

  await t('13 모르는 type 은 버리고 같은 연결로 계속', async () => {
    const r = await raw(path);
    const a = base({ tool: 'test:key:F19@1200' });
    await r.write(JSON.stringify({ type: 'ping' }) + '\n');
    await r.write(JSON.stringify({ type: 'permission', v: 1, ...a, deadline: now() + 60000 }) + '\n');
    const line = await r.waitLine(5000);
    r.sock.destroy();
    const want = JSON.stringify({ type: 'decision', request_id: a.request_id, decision: 'allow' });
    return { ok: line === want, detail: `받은 줄 ${line}` };
  });

  await t('14 끊으면 그 질문 닫힘 → 다음 질문', async () => {
    const a = base({ tool: 'test:wait' });
    const sa = await sendToPcApp(path, a, now() + 120000);
    await sleep(300);
    sa.destroy();
    const sb = await sendToPcApp(path, base({ tool: 'test:key:F19@1200' }), now() + 120000);
    const d = await race(sb.answer, 4000);
    sb.close();
    const dropped = log().includes(`${a.request_id.slice(0, 8)} 닫힘 — 연결 끊김`);
    return { ok: d === 'allow' && dropped, detail: `B ${d} · 끊김 기록 ${dropped}` };
  });

  await t('15 받기 끔 → 연결 끊김(확장은 VS Code 알림으로) · 못 붙음 · 다시 켬 → 됨', async () => {
    const sa = await sendToPcApp(path, base({ tool: 'test:off@300,on@2500' }), now() + 120000);
    const d = await race(sa.answer, 2000);
    const closedByApp = sa.extra.closedByApp;
    await sleep(200);
    const mid = await sendToPcApp(path, base({ tool: 'test:wait' }), now() + 60000);
    if (mid) mid.close();
    await sleep(2600);
    const sb = await sendToPcApp(path, base({ tool: 'test:key:F19@1200' }), now() + 120000);
    const db = sb ? await race(sb.answer, 4000) : 'not-connected';
    if (sb) sb.close();
    return { ok: d === undefined && closedByApp && !mid && db === 'allow', detail: `끌 때 답 ${d} · 앱이 끊음 ${closedByApp} · 끈 동안 붙음 ${!!mid} · 켠 뒤 ${db}` };
  });

  await t('16 닫기 단추(터미널에서 답) → none · 팝업 닫힘', async () => {
    const r = base({ tool: 'test:click:none@1200,wait@1700' });
    const s = await sendToPcApp(path, r, now() + 120000);
    const d = await race(s.answer, 5000);
    await sleep(800);
    s.close();
    const sent = log().includes(`${r.request_id.slice(0, 8)} 끝 — 터미널에서 답(none) 보냄 · 팝업 닫음`);
    const hidden = new RegExp(`${r.request_id} 각본 wait · .* 보임=False`).test(log());
    return { ok: d === 'none' && sent && hidden, detail: `답 ${d} · 기록 ${sent} · 뒤에 팝업 숨음 ${hidden}` };
  });

  await t('17 팝업이 포커스를 가져가지 않음(보임 기록 전부) · 패드 키 잡음', async () => {
    const l = log();
    const shown = l.split('\n').filter((x) => x.includes('] 보임 '));
    const stole = shown.filter((x) => !x.includes('포커스=안 가져감'));
    const keys = l.includes('패드 키 F19 · F20 · F21 잡음');
    return { ok: shown.length > 0 && stole.length === 0 && keys, detail: `보임 ${shown.length}회 · 포커스 가져감 ${stole.length} · 키 잡음 ${keys}` };
  });

  // 끝냄
  const q = await sendToPcApp(path, base({ tool: 'test:quit@100' }), now() + 60000);
  if (q) await race(q.answer, 1500);

  const fail = results.filter((r) => !r.ok);
  console.log(`\n결과: 통과 ${results.length - fail.length} / ${results.length}`);
  fs.writeFileSync('pad-pipe-roundtrip.json', JSON.stringify(results, null, 1));
  process.exit(fail.length ? 1 : 0);
}

function squat(ms, readyFile) {
  const path = pipePath();
  const srv = net.createServer(() => undefined);
  srv.on('error', (e) => {
    console.log('squat 실패', e.code);
    process.exit(2);
  });
  srv.listen(path, () => {
    console.log('squat: 먼저 엶', path);
    fs.writeFileSync(readyFile, 'ready');
    setTimeout(() => srv.close(() => process.exit(0)), ms);
  });
}

const [mode, a1, a2] = process.argv.slice(2);
if (mode === 'run') runAll(a1);
else if (mode === 'squat') squat(Number(a1) || 15000, a2 || 'squat.ready');
else {
  console.log('쓰는 법: run <pipe-test.log> | squat <ms> <ready 파일>');
  process.exit(2);
}
