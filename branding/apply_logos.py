#copies branding/'s artwork, at the right sizes, into every place the apps and site use it.
#run after make_logo.py: py branding/apply_logos.py
import pathlib

from PIL import Image

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

print("applied")
