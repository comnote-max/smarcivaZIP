#!/usr/bin/env python3
"""アプリのアイコン画像から Windows の .ico を作る。

元絵は白い紙の上に角丸の四角が乗っている、という構図で描かれている。
そのまま .ico にすると、タスクバーやエクスプローラーで白い余白が目立ち、
他のアイコンと並んだときに一回り小さく見えてしまう。

そこで
  1. 四隅から白を塗りつぶして透明にし（角丸の外側だけが対象になる）
  2. 不透明な部分の外接矩形で切り取り
  3. 各サイズに縮小して 1 つの .ico にまとめる
という手順を踏む。

猫の体も白いが、角丸の内側にあって縁から繋がっていないため、
塗りつぶしの対象にはならない。

使い方:
    python tools/make-icon.py assets/icon-source.webp src/SmarcivaZip.App/Assets/smarcivazip.ico

必要なもの: Pillow (pip install pillow)
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

# Windows がアイコンを要求してくるサイズ。
# 16 はエクスプローラーの詳細表示、256 は特大アイコン。
ICON_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

# 白と見なす許容差。元絵は JPEG 由来のノイズを含むことがあるので少し緩める。
WHITE_THRESHOLD = 40


def strip_background(image: Image.Image) -> Image.Image:
    """四隅から白を塗りつぶして透明にする。"""
    image = image.convert("RGBA")
    width, height = image.size

    for corner in [(0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1)]:
        if image.getpixel(corner)[3] == 0:
            continue
        ImageDraw.floodfill(image, corner, (255, 255, 255, 0), thresh=WHITE_THRESHOLD)

    return image


def feather_edges(image: Image.Image) -> Image.Image:
    """
    塗りつぶしの境目に残る白いふちを削る。

    角丸の輪郭はアンチエイリアスがかかっているため、閾値を超えなかった
    半端に白いピクセルが 1 ドット分だけ残る。そのままだと縮小したときに
    白い枠として見えてしまうので、透明部分に接している白っぽいピクセルの
    不透明度を落とす。
    """
    pixels = image.load()
    width, height = image.size
    to_fade = []

    for y in range(height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            if min(r, g, b) < 255 - WHITE_THRESHOLD:
                continue

            # 隣に透明ピクセルがあるか
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < width and 0 <= ny < height and pixels[nx, ny][3] == 0:
                    to_fade.append((x, y))
                    break

    for x, y in to_fade:
        r, g, b, a = pixels[x, y]
        pixels[x, y] = (r, g, b, a // 3)

    return image


def build_icon(source: Path, destination: Path) -> None:
    image = Image.open(source)
    image = strip_background(image)
    image = feather_edges(image)

    bounds = image.getbbox()
    if bounds is None:
        raise SystemExit("画像が空です。")

    image = image.crop(bounds)

    # 正方形に整える。横長・縦長のまま縮小すると比率が崩れる。
    side = max(image.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(image, ((side - image.width) // 2, (side - image.height) // 2))

    destination.parent.mkdir(parents=True, exist_ok=True)

    frames = [canvas.resize((size, size), Image.LANCZOS) for size in ICON_SIZES]
    frames[-1].save(destination, format="ICO", sizes=[(s, s) for s in ICON_SIZES])

    # README などで使う PNG も一緒に出しておく。
    png = destination.with_suffix(".png")
    canvas.resize((512, 512), Image.LANCZOS).save(png, format="PNG")

    print(f"{destination}  ({destination.stat().st_size / 1024:.1f} KB, {len(ICON_SIZES)} サイズ)")
    print(f"{png}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)

    build_icon(Path(sys.argv[1]), Path(sys.argv[2]))
