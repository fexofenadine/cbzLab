#copies branding/'s artwork, at the right sizes, into every place the apps and site use it.
#run after make_logo.py: py branding/apply_logos.py
import pathlib

from PIL import Image, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parent.parent
B = ROOT / "branding"
lab = Image.open(B / "logo-cbzLab.png").convert("RGBA")
lite = Image.open(B / "logo-cbzLite.png").convert("RGBA")
badge = Image.open(B / "badge.png").convert("RGBA")


def sized(im, n):
    return im.resize((n, n), Image.LANCZOS)


#desktop app: full logo for the About box and docs, the badge alone for the exe/window icon
lab.save(ROOT / "cbzLab" / "Assets" / "logo.png", optimize=True)
sizes = [16, 24, 32, 48, 64, 128, 256]
sized(badge, 256).save(ROOT / "cbzLab" / "Assets" / "icon.ico", sizes=[(s, s) for s in sizes])

#website
sized(lab, 640).save(ROOT / "site" / "assets" / "logo-640.png", optimize=True)
sized(lab, 200).save(ROOT / "site" / "assets" / "logo-200.png", optimize=True)

#cbzLite
sized(lite, 512).save(ROOT / "cbzLite" / "assets" / "logo.png", optimize=True)
sized(lite, 384).save(ROOT / "cbzLite" / "assets" / "logo.webp", quality=88, method=6)
sized(badge, 32).save(ROOT / "cbzLite" / "assets" / "favicon.png", optimize=True)

#android: an adaptive icon, a 108dp square that each launcher crops to its own shape and shows
#roughly the middle 72dp of. the uncropped square badge fills that middle over a blurred copy of
#itself out to the edges, so no launcher shape cuts the artwork or shows a flat border
square = Image.open(B / "badge-square.png").convert("RGBA")
res = ROOT / "cbzLab.Android" / "Resources"
for name, scale in {"mdpi": 1, "hdpi": 1.5, "xhdpi": 2, "xxhdpi": 3, "xxxhdpi": 4}.items():
    side = round(108 * scale)
    inner = round(80 * scale)
    canvas = square.resize((side, side), Image.LANCZOS).filter(ImageFilter.GaussianBlur(6 * scale))
    canvas.alpha_composite(square.resize((inner, inner), Image.LANCZOS), ((side - inner) // 2, (side - inner) // 2))
    folder = res / f"mipmap-{name}"
    folder.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(folder / "appicon_background.png", optimize=True)
    Image.new("RGBA", (side, side), (0, 0, 0, 0)).save(folder / "appicon_foreground.png", optimize=True)
    #launchers older than android 8 have no adaptive icons, so they get the plain badge
    sized(square, round(48 * scale)).save(folder / "appicon.png", optimize=True)
anydpi = res / "mipmap-anydpi-v26"
anydpi.mkdir(parents=True, exist_ok=True)
(anydpi / "appicon.xml").write_text('''<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@mipmap/appicon_background" />
    <foreground android:drawable="@mipmap/appicon_foreground" />
</adaptive-icon>
''', encoding="utf-8")

print("applied")
