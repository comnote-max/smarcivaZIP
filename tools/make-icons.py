#!/usr/bin/env python3
"""アプリ本体と書庫ファイルのアイコンを、1 枚の元絵から作る。

    python tools/make-icons.py

生成先:
    src/SmarcivaZip.App/Assets/smarcivazip.ico         アプリ本体
    src/SmarcivaZip.App/Assets/Icons/<形式>.ico        書庫ファイル

必要なもの: Pillow (pip install pillow)

── 設計の理由 ──────────────────────────────────────────────

アプリと書庫で「形」を変えている。同じ絵にすると、同じフォルダに並んだときに
どれが実行ファイルか分からなくなるため。Windows の慣習どおり、
アプリは塗りつぶした角丸タイル、書庫は紙の形にしてある。

形式の区別は「色」に持たせている。エクスプローラーの詳細表示は 16px で、
その大きさでは何を書いても文字は読めない。隅に小さくラベルを置く方式は
48px 以上でしか機能しないので、16px で効く手掛かりは色と輪郭しかない。

色は元絵の紺を基準に、明度と彩度を揃えて選んである。
色相を機械的に回すと緑や黄だけが浮いて、同じ一揃いに見えなくなる。
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

# Windows が要求するサイズ。16 は詳細表示、256 は特大アイコン。
ICON_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

# 白と見なす許容差。元絵は生成物なのでノイズを含む。
WHITE_THRESHOLD = 40

# 形式ごとの帯の色。None はラベル無し（汎用）。
FORMAT_COLORS = {
    'zip': ((28, 92, 196), 'zip'),
    '7z': ((22, 122, 110), '7z'),
    'rar': ((118, 62, 168), 'rar'),
    'tar': ((176, 104, 26), 'tar'),
    'lzh': ((178, 52, 70), 'lzh'),
    'gz': ((72, 96, 132), 'gz'),
    'archive': ((38, 78, 150), None),
}


# ---------------------------------------------------------------- 元絵の下ごしらえ

def strip_background(image: Image.Image) -> Image.Image:
    """四隅から白を塗りつぶして透明にする。

    元絵は白い紙の上に角丸の四角が乗った構図なので、そのまま使うと
    白い余白が焼き付いて、他のアイコンより一回り小さく見える。
    猫も白いが、角丸の内側にあって縁と繋がっていないため塗りつぶされない。
    """
    image = image.convert('RGBA')
    width, height = image.size

    for corner in [(0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1)]:
        if image.getpixel(corner)[3] == 0:
            continue
        ImageDraw.floodfill(image, corner, (255, 255, 255, 0), thresh=WHITE_THRESHOLD)

    return image


def feather_edges(image: Image.Image) -> Image.Image:
    """塗りつぶしの境目に残る白いふちを削る。

    角丸の輪郭はアンチエイリアスされているため、閾値を超えなかった
    半端に白い画素が 1 ドット残り、縮小すると白い枠として見えてしまう。
    """
    pixels = image.load()
    width, height = image.size
    fading = []

    for y in range(height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            if a == 0 or min(r, g, b) < 255 - WHITE_THRESHOLD:
                continue

            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < width and 0 <= ny < height and pixels[nx, ny][3] == 0:
                    fading.append((x, y))
                    break

    for x, y in fading:
        r, g, b, a = pixels[x, y]
        pixels[x, y] = (r, g, b, a // 3)

    return image


def load_artwork(source: Path) -> Image.Image:
    """元絵を、余白を落とした正方形の RGBA にして返す。"""
    image = feather_edges(strip_background(Image.open(source)))

    bounds = image.getbbox()
    if bounds is None:
        raise SystemExit('画像が空です。')

    image = image.crop(bounds)

    side = max(image.size)
    canvas = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    canvas.paste(image, ((side - image.width) // 2, (side - image.height) // 2))
    return canvas


# ---------------------------------------------------------------- 書庫アイコン

def find_font(size: int):
    for name in ('segoeuib.ttf', 'arialbd.ttf', 'seguisb.ttf'):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def fit_font(draw, label: str, max_width: float, max_height: float):
    """枠に収まる最大の文字を選ぶ。けちると小さいサイズで潰れる。"""
    size = int(max_height * 1.3)

    while size > 8:
        font = find_font(size)
        box = draw.textbbox((0, 0), label, font=font)
        if box[2] - box[0] <= max_width and box[3] - box[1] <= max_height:
            return font, box
        size -= 2

    font = find_font(8)
    return font, draw.textbbox((0, 0), label, font=font)


def cat_head(artwork: Image.Image, height: int) -> Image.Image:
    """元絵から猫の頭だけを切り出す。"""
    width, full_height = artwork.size
    head = artwork.crop((int(width * 0.20), int(full_height * 0.04),
                         int(width * 0.80), int(full_height * 0.50)))

    ratio = height / head.height
    return head.resize((max(1, int(head.width * ratio)), height), Image.LANCZOS)


def document_icon(artwork: Image.Image, color, label, canvas: int = 512) -> Image.Image:
    image = Image.new('RGBA', (canvas, canvas), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    left, right = canvas * 0.14, canvas * 0.86
    top, bottom = canvas * 0.05, canvas * 0.95
    fold = canvas * 0.26

    # 紙。白いままだとエクスプローラーの白背景に溶けるので、わずかに青灰を入れる。
    page = [(left, top), (right - fold, top), (right, top + fold),
            (right, bottom), (left, bottom)]
    draw.polygon(page, fill=(238, 243, 250, 255))
    draw.line(page + [(left, top)], fill=(150, 168, 192, 255), width=max(3, canvas // 80))
    draw.polygon([(right - fold, top), (right, top + fold), (right - fold, top + fold)],
                 fill=(196, 210, 230, 255))

    # 下の帯。16px ではここの色だけが手掛かりになる。
    band_top = canvas * 0.60
    darker = tuple(int(component * 0.72) for component in color)
    draw.rectangle([left, band_top, right, bottom], fill=darker + (255,))

    if label:
        font, box = fit_font(draw, label,
                             (right - left) * 0.78, (bottom - band_top) * 0.62)
        draw.text((left + (right - left - (box[2] - box[0])) / 2 - box[0],
                   band_top + (bottom - band_top - (box[3] - box[1])) / 2 - box[1]),
                  label, font=font, fill=(255, 255, 255, 255))

    # 猫は白いので、紙に埋もれないよう地色を敷いてから載せる。
    mark_height = int((band_top - top) * 0.74)
    mark = cat_head(artwork, mark_height)
    padding = mark_height * 0.12
    mark_top = top + (band_top - top - mark_height) / 2

    draw.rounded_rectangle(
        [(canvas - mark.width) / 2 - padding, mark_top - padding,
         (canvas + mark.width) / 2 + padding, mark_top + mark_height + padding],
        radius=mark_height * 0.22, fill=color + (255,))

    image.paste(mark, (int((canvas - mark.width) / 2), int(mark_top)), mark)
    return image


def save_ico(image: Image.Image, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    frames = [image.resize((size, size), Image.LANCZOS) for size in ICON_SIZES]
    frames[-1].save(destination, format='ICO', sizes=[(s, s) for s in ICON_SIZES])
    print(f'  {destination.name:<16} {destination.stat().st_size / 1024:6.1f} KB')


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    source = root / 'assets' / 'icon-source.webp'
    assets = root / 'src' / 'SmarcivaZip.App' / 'Assets'

    if not source.exists():
        raise SystemExit(f'元絵が見つかりません: {source}')

    artwork = load_artwork(source)

    print('アプリ本体:')
    save_ico(artwork, assets / 'smarcivazip.ico')
    artwork.resize((512, 512), Image.LANCZOS).save(assets / 'smarcivazip.png')

    print('書庫ファイル:')
    for name, (color, label) in FORMAT_COLORS.items():
        save_ico(document_icon(artwork, color, label), assets / 'Icons' / f'{name}.ico')


if __name__ == '__main__':
    sys.exit(main())
