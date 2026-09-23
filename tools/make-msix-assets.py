"""ストア版（MSIX）のロゴとファイルの種類ごとのアイコンを PNG で作る。

元にするのは、すでにある画像:
  - アプリのロゴ: src/SmarcivaZip.App/Assets/smarcivazip.png（512px）
  - 書庫のアイコン: src/SmarcivaZip.App/Assets/Icons/*.ico（256px まで入っている）

出力先は packaging/msix/Assets/。ファイル名の .scale-200 や .targetsize-48 は MSIX の資源の修飾子で、
makepri が resources.pri にまとめ、Windows が画面の拡大率や表示する大きさに合わせて選ぶ。
ふだんのビルドでは実行しない。元の画像を差し替えたときだけ実行して、結果をコミットする。

    python tools/make-msix-assets.py
"""

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
APP_ART = ROOT / "src" / "SmarcivaZip.App" / "Assets" / "smarcivazip.png"
ICONS = ROOT / "src" / "SmarcivaZip.App" / "Assets" / "Icons"
OUT = ROOT / "packaging" / "msix" / "Assets"

# タスクバーやスタートメニュー、エクスプローラーが要求する大きさ。
TARGET_SIZES = [16, 20, 24, 32, 40, 48, 64, 256]

# ArchiveFileType と同じ分類。左が MSIX の中での名前、右が元のアイコン。
FILE_TYPES = {
    "zip": "zip.ico",
    "sevenzip": "7z.ico",
    "rar": "rar.ico",
    "tar": "tar.ico",
    "compressed": "gz.ico",
    "lzh": "lzh.ico",
    "archive": "archive.ico",
}


def largest_frame(ico: Path) -> Image.Image:
    """.ico から一番大きい画像を取り出す。"""
    image = Image.open(ico)
    sizes = sorted(image.info.get("sizes", {image.size}), key=lambda s: s[0])
    image.size = sizes[-1]
    return image.convert("RGBA")


def square(art: Image.Image, size: int, fill: float = 1.0) -> Image.Image:
    """透明な正方形の中央に置く。fill は正方形に対する絵の大きさの割合。"""
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    inner = max(1, round(size * fill))
    scaled = art.resize((inner, inner), Image.LANCZOS)
    offset = (size - inner) // 2
    canvas.alpha_composite(scaled, (offset, offset))
    return canvas


def wide(art: Image.Image, width: int, height: int) -> Image.Image:
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    inner = round(height * 0.8)
    scaled = art.resize((inner, inner), Image.LANCZOS)
    canvas.alpha_composite(scaled, ((width - inner) // 2, (height - inner) // 2))
    return canvas


def save(image: Image.Image, relative: str) -> None:
    path = OUT / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, optimize=True)


def main() -> None:
    art = Image.open(APP_ART).convert("RGBA")

    # アプリのロゴ。44px はタスクバーやスタートメニューの一覧、150px はタイル、50px はストア。
    for scale in (100, 150, 200, 400):
        f = scale / 100
        save(square(art, round(44 * f)), f"Square44x44Logo.scale-{scale}.png")
        save(square(art, round(150 * f), fill=0.66), f"Square150x150Logo.scale-{scale}.png")
        save(square(art, round(50 * f)), f"StoreLogo.scale-{scale}.png")
        save(wide(art, round(310 * f), round(150 * f)), f"Wide310x150Logo.scale-{scale}.png")

    # 下地の板を敷かない（altform-unplated）版と、敷く前提の版。アイコン自体に形があるので同じ絵でよい。
    for size in TARGET_SIZES:
        save(square(art, size), f"Square44x44Logo.targetsize-{size}.png")
        save(square(art, size), f"Square44x44Logo.targetsize-{size}_altform-unplated.png")

    # ファイルの種類ごとのアイコン。エクスプローラーはこの大きさの中から選ぶ。
    for name, ico in FILE_TYPES.items():
        frame = largest_frame(ICONS / ico)
        save(square(frame, 44), f"FileTypes/{name}.scale-100.png")
        for size in TARGET_SIZES:
            save(square(frame, size), f"FileTypes/{name}.targetsize-{size}.png")

    count = sum(1 for _ in OUT.rglob("*.png"))
    print(f"wrote {count} PNG files to {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
