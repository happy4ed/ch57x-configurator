#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""앱 아이콘(icon.ico) 만들기 — PC 앱 0.3.0 새 앱 아이콘(신원 표식).

원본: windows/Ch57x.App/Assets/app-icon.svg (2판 개선안 brand/app-icon.svg 그대로 옮김 —
      autographer artifacts/ch57x-pc-app-ux/brand/, 생성기 projects/ch57x/tools/pc-app-ux/build3.py APP_ICON)
결과: windows/Ch57x.App/icon.ico — 16 · 20 · 24 · 32 · 48 · 256 px.
      크기마다 SVG 에서 따로 그린다(큰 그림을 줄이지 않음 — 16px 에서 흐려지지 않게).
      256 은 PNG 압축, 나머지는 32비트 BMP(DIB) — System.Drawing.Icon(트레이) · WPF 디코더 둘 다 읽는 형식.

쓰기:  python3 windows/tools/make-icons.py          (icon.ico 를 다시 씀)
검사:  python3 windows/tools/make-icons.py --check  (지금 icon.ico 가 원본에서 만든 것과 같은지 · 다르면 종료 코드 1)
필요:  cairosvg · Pillow
"""
import io, os, struct, sys

import cairosvg
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
APP = os.path.join(HERE, '..', 'Ch57x.App')
SVG = os.path.join(APP, 'Assets', 'app-icon.svg')
ICO = os.path.join(APP, 'icon.ico')
SIZES = [16, 20, 24, 32, 48, 256]


def render(px: int) -> Image.Image:
    png = cairosvg.svg2png(url=SVG, output_width=px, output_height=px)
    return Image.open(io.BytesIO(png)).convert('RGBA')


def dib(img: Image.Image) -> bytes:
    """32비트 BGRA DIB (아래에서 위로) + AND 마스크(투명 = 1)"""
    w, h = img.size
    header = struct.pack('<IiiHHIIiiII', 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    px = img.load()
    rows = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            rows += bytes((b, g, r, a))
    stride = ((w + 31) // 32) * 4
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        row = bytearray(stride)
        for x in range(w):
            if px[x, y][3] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        mask += row
    return header + bytes(rows) + bytes(mask)


def build() -> bytes:
    entries = []
    for s in SIZES:
        img = render(s)
        if s >= 256:
            buf = io.BytesIO()
            img.save(buf, format='PNG', optimize=False)
            data = buf.getvalue()
        else:
            data = dib(img)
        entries.append((s, data))
    out = bytearray(struct.pack('<HHH', 0, 1, len(entries)))
    offset = 6 + 16 * len(entries)
    for s, data in entries:
        dim = 0 if s >= 256 else s
        out += struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in entries:
        out += data
    return bytes(out)


def main(argv):
    data = build()
    if '--check' in argv:
        cur = open(ICO, 'rb').read() if os.path.exists(ICO) else b''
        if cur != data:
            print('icon.ico 가 원본 SVG 에서 만든 것과 다름 — python3 windows/tools/make-icons.py')
            return 1
        print(f'icon.ico 최신 ({len(SIZES)}크기 · {len(data)} 바이트)')
        return 0
    with open(ICO, 'wb') as f:
        f.write(data)
    print(f'썼음: {ICO} ({", ".join(map(str, SIZES))} · {len(data)} 바이트)')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
