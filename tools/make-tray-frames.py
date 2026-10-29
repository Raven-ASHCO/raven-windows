#!/usr/bin/env python3
"""Make the tray icon's "breathing" frames from Assets/RavenTray.ico (standard library only).

    python3 tools/make-tray-frames.py

Writes Assets/RavenTrayDim1.ico (80% opacity) and Assets/RavenTrayDim2.ico (60% opacity): the
same sizes as RavenTray.ico, with every pixel's alpha scaled. The app shows the frames in the
order 100%, 80%, 60%, 80%, one step per second, only while Raven listens (as the macOS menu bar
mark does), and never with Windows "Animation effects" off or Raven's "Reduced motion" on.
RavenTray.ico must hold 32-bit BMP entries (it does); PNG entries are refused.
"""
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "src", "Raven.Windows", "Assets")
FRAMES = {"RavenTrayDim1.ico": 0.8, "RavenTrayDim2.ico": 0.6}


def scale_alpha(ico: bytes, factor: float) -> bytes:
    _, kind, count = struct.unpack_from("<HHH", ico, 0)
    if kind != 1:
        sys.exit("not an icon file")
    out = bytearray(ico)
    for i in range(count):
        w, h, _, _, _, bpp, size, offset = struct.unpack_from("<BBBBHHII", ico, 6 + 16 * i)
        w, h = w or 256, h or 256
        if ico[offset:offset + 8] == b"\x89PNG\r\n\x1a\n" or bpp != 32:
            sys.exit("entry %d is not a 32-bit BMP" % i)
        header = struct.unpack_from("<I", ico, offset)[0]  # BITMAPINFOHEADER size (40)
        pixels = offset + header
        for p in range(w * h):
            a = pixels + p * 4 + 3  # BGRA
            out[a] = round(ico[a] * factor)
    return bytes(out)


def main() -> None:
    with open(os.path.join(ASSETS, "RavenTray.ico"), "rb") as f:
        base = f.read()
    for name, factor in FRAMES.items():
        with open(os.path.join(ASSETS, name), "wb") as f:
            f.write(scale_alpha(base, factor))
        print("wrote", name)


if __name__ == "__main__":
    main()
