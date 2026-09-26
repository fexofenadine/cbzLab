"""
Procedural comic-book cover art for the seven sample books the screenshots use.

Each cover is drawn at 2x (SS) and downsampled with Lanczos for clean edges. All
coordinates below are in final-cover space (W x H) and scaled by P()/S() helpers.
Trade dress (masthead, corner box, publisher badge, credits, barcode) is shared;
the illustration underneath is per book, driven by its genre and summary.

Usage (needs Pillow; fonts are read from C:/Windows/Fonts):

    python make_sample_covers.py <preview_dir> <source_books_dir> <dest_books_dir> [book.cbz ...]

Writes a PNG preview of each cover to preview_dir, and a copy of each source book
to dest_books_dir with only page001.jpg replaced - entry order and ComicInfo.xml
stay byte-identical. The sample books themselves live in testingbooks/fake_samples,
which is gitignored. Names default to all seven books when none are given.
"""
import io
import math
import os
import random
import sys
import zipfile

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps

W, H = 1000, 1500
SS = 2
FONTS = "C:/Windows/Fonts/"


def S(v):
    return int(round(v * SS))


def P(pts):
    return [(x * SS, y * SS) for x, y in pts]


def font(name, size):
    return ImageFont.truetype(FONTS + name, S(size))


def lerp(a, b, t):
    return a + (b - a) * t


def mix(c1, c2, t):
    return tuple(int(round(lerp(a, b, t))) for a, b in zip(c1, c2))


def stops_at(stops, t):
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        if t <= p1:
            return mix(c0, c1, 0 if p1 == p0 else (t - p0) / (p1 - p0))
    return stops[-1][1]


def new_canvas(stops):
    img = Image.new("RGB", (S(W), S(H)))
    d = ImageDraw.Draw(img)
    for y in range(S(H)):
        d.line([(0, y), (S(W), y)], fill=stops_at(stops, y / (S(H) - 1)))
    return img


def layer():
    return Image.new("RGBA", (S(W), S(H)), (0, 0, 0, 0))


def over(base, top):
    return Image.alpha_composite(base.convert("RGBA"), top).convert("RGB")


def glow(base, cx, cy, r, color, strength=1.0, blur=None):
    """Screen-blended soft light - the backlight every comic cover leans on."""
    mask = Image.new("L", base.size, 0)
    ImageDraw.Draw(mask).ellipse([S(cx - r), S(cy - r), S(cx + r), S(cy + r)], fill=int(255 * strength))
    mask = mask.filter(ImageFilter.GaussianBlur(S(blur if blur is not None else r * 0.45)))
    lit = Image.composite(Image.new("RGB", base.size, color), Image.new("RGB", base.size, 0), mask)
    return ImageChops.screen(base, lit)


def halftone(base, color, density, spacing=11, region=None, max_r=0.62):
    """Ben-Day dot screen. density(x, y) -> 0..1 in cover coords decides each dot's size."""
    top = layer()
    d = ImageDraw.Draw(top)
    x0, y0, x1, y1 = region or (0, 0, W, H)
    row = 0
    y = y0
    while y < y1:
        off = spacing / 2 if row % 2 else 0
        x = x0 + off
        while x < x1:
            k = density(x, y)
            if k > 0.02:
                r = spacing * max_r * min(1.0, k)
                d.ellipse([S(x - r), S(y - r), S(x + r), S(y + r)], fill=color)
            x += spacing
        y += spacing * 0.866
        row += 1
    return over(base, top)


def rays(base, cx, cy, count, color, length=2400, spread=0.5, start=0.0):
    top = layer()
    d = ImageDraw.Draw(top)
    for i in range(count):
        a = start + i * 2 * math.pi / count
        w = spread * math.pi / count
        pts = [(cx, cy),
               (cx + length * math.cos(a - w), cy + length * math.sin(a - w)),
               (cx + length * math.cos(a + w), cy + length * math.sin(a + w))]
        d.polygon(P(pts), fill=color)
    return over(base, top)


def speed_lines(base, count, color, box, direction=(1, 0), rnd=None, width=(2, 7), length=(120, 420)):
    rnd = rnd or random.Random(1)
    top = layer()
    d = ImageDraw.Draw(top)
    x0, y0, x1, y1 = box
    dx, dy = direction
    for _ in range(count):
        x = rnd.uniform(x0, x1)
        y = rnd.uniform(y0, y1)
        ln = rnd.uniform(*length)
        d.line(P([(x, y), (x + dx * ln, y + dy * ln)]), fill=color, width=S(rnd.uniform(*width)))
    return over(base, top)


def limb(d, pts, width, fill):
    """Thick polyline with round joints - enough to pose a silhouette figure."""
    d.line(P(pts), fill=fill, width=S(width), joint="curve")
    for x, y in pts:
        r = width / 2
        d.ellipse([S(x - r), S(y - r), S(x + r), S(y + r)], fill=fill)


def paper(img, amount=10):
    """Print grain plus a soft vignette, so flat fills stop looking digital."""
    noise = Image.effect_noise(img.size, 40).convert("RGB")
    img = Image.blend(img, ImageChops.overlay(img, noise), amount / 100)
    vig = Image.new("L", img.size, 0)
    ImageDraw.Draw(vig).ellipse([-S(W * 0.25), -S(H * 0.2), S(W * 1.25), S(H * 1.2)], fill=255)
    vig = vig.filter(ImageFilter.GaussianBlur(S(160)))
    return Image.composite(img, ImageChops.multiply(img, Image.new("RGB", img.size, (150, 140, 150))), vig)


#trade dress

def fit_font(name, text, max_w, start, min_size=20):
    size = start
    while size > min_size:
        f = font(name, size)
        l, t, r, b = f.getbbox(text)
        if (r - l) <= S(max_w):
            return f
        size -= 2
    return font(name, min_size)


def masthead(img, text, fontname, y, max_w=900, size=220, fill=(255, 255, 255), outline=(10, 10, 12),
             stroke=9, shadow=(0, 0, 0), shadow_off=(10, 12), skew=0.0, gradient=None, x=None, anchor="mt"):
    """Big logo text: hard drop shadow, thick ink stroke, optional two-tone fill and italic skew."""
    f = fit_font(fontname, text, max_w, size)
    top = layer()
    d = ImageDraw.Draw(top)
    cx = S(W / 2 if x is None else x)
    sx, sy = S(shadow_off[0]), S(shadow_off[1])
    if shadow is not None:
        d.text((cx + sx, S(y) + sy), text, font=f, fill=shadow + (255,), anchor=anchor,
               stroke_width=S(stroke), stroke_fill=shadow + (255,))
    d.text((cx, S(y)), text, font=f, fill=fill + (255,), anchor=anchor, stroke_width=S(stroke),
           stroke_fill=outline + (255,))
    if gradient:
        #re-fill only the glyph interiors with a vertical gradient
        mask = Image.new("L", top.size, 0)
        ImageDraw.Draw(mask).text((cx, S(y)), text, font=f, fill=255, anchor=anchor)
        l, t, r, b = mask.getbbox()
        grad = Image.new("RGBA", top.size)
        gd = ImageDraw.Draw(grad)
        for yy in range(t, b):
            gd.line([(l, yy), (r, yy)], fill=stops_at(gradient, (yy - t) / max(1, b - t - 1)) + (255,))
        top = Image.composite(grad, top, mask)
    if skew:
        top = top.transform(top.size, Image.AFFINE, (1, skew, -skew * S(y + 100), 0, 1, 0), Image.BICUBIC)
    return over(img, top)


def text(img, xy, s, fontname, size, fill, anchor="la", stroke=0, stroke_fill=(0, 0, 0), spacing=0):
    top = layer()
    d = ImageDraw.Draw(top)
    f = font(fontname, size)
    if spacing:
        #manual letter-spacing, the trick every masthead subtitle uses
        x, y = xy
        total = sum(f.getlength(c) for c in s) + S(spacing) * (len(s) - 1)
        if anchor[0] == "m":
            x = x - total / SS / 2
        for c in s:
            d.text((S(x), S(y)), c, font=f, fill=fill + (255,), anchor="l" + anchor[1],
                   stroke_width=S(stroke), stroke_fill=stroke_fill + (255,))
            x += (f.getlength(c) + S(spacing)) / SS
    else:
        d.text(P([xy])[0], s, font=f, fill=fill + (255,), anchor=anchor, stroke_width=S(stroke),
               stroke_fill=stroke_fill + (255,))
    return over(img, top)


def corner_box(img, number, month, price="$3.99", x=34, y=34, bg=(255, 255, 255), fg=(15, 15, 15), accent=(210, 30, 40)):
    top = layer()
    d = ImageDraw.Draw(top)
    w, h = 132, 168 if number else 118
    d.rectangle(P([(x, y), (x + w, y + h)]), fill=bg + (255,), outline=fg + (255,), width=S(5))
    if number:
        d.text(P([(x + w / 2, y + 12)])[0], "No.", font=font("impact.ttf", 26), fill=accent + (255,), anchor="mt")
        d.text(P([(x + w / 2, y + 36)])[0], str(number), font=font("impact.ttf", 78), fill=fg + (255,), anchor="mt")
        yy = y + 120
    else:
        yy = y + 20
    d.line(P([(x + 12, yy), (x + w - 12, yy)]), fill=fg + (255,), width=S(3))
    d.text(P([(x + w / 2, yy + 8)])[0], month, font=font("arialbd.ttf", 19), fill=fg + (255,), anchor="mt")
    d.text(P([(x + w / 2, yy + 30)])[0], price, font=font("arialbd.ttf", 17), fill=fg + (255,), anchor="mt")
    return over(img, top)


def publisher_badge(img, name, x=None, y=40, color=(255, 255, 255), fg=(20, 20, 20)):
    x = W - 110 if x is None else x
    top = layer()
    d = ImageDraw.Draw(top)
    r = 64
    d.ellipse(P([(x - r, y), (x + r, y + 2 * r)]), fill=color + (255,), outline=fg + (255,), width=S(5))
    words = name.upper().split()
    d.text(P([(x, y + r - 14)])[0], words[0], font=font("impact.ttf", 30), fill=fg + (255,), anchor="mm")
    d.text(P([(x, y + r + 16)])[0], " ".join(words[1:]), font=font("arialbd.ttf", 15), fill=fg + (255,), anchor="mm")
    return over(img, top)


def barcode(img, x=W - 190, y=H - 170, seed=7):
    rnd = random.Random(seed)
    top = layer()
    d = ImageDraw.Draw(top)
    d.rectangle(P([(x, y), (x + 150, y + 120)]), fill=(255, 255, 255, 255), outline=(0, 0, 0, 255), width=S(2))
    bx = x + 12
    while bx < x + 138:
        bw = rnd.choice([1.5, 1.5, 3, 4.5])
        d.rectangle(P([(bx, y + 12), (bx + bw, y + 86)]), fill=(0, 0, 0, 255))
        bx += bw + rnd.choice([1.5, 3])
    d.text(P([(x + 75, y + 100)])[0], "0 74470 81425 7", font=font("arial.ttf", 13), fill=(0, 0, 0, 255), anchor="mm")
    return over(img, top)


def credits(img, line, y=H - 58, fill=(255, 255, 255), stroke=(0, 0, 0)):
    return text(img, (W / 2, y), line.upper(), "arialbd.ttf", 24, fill, anchor="mm", stroke=3, stroke_fill=stroke, spacing=2)


def banner(img, s, y, bg, fg, fontname="impact.ttf", size=46, pad=22, tilt=0):
    f = font(fontname, size)
    l, t, r, b = f.getbbox(s)
    w, h = (r - l) / SS + pad * 2, (b - t) / SS + pad
    top = layer()
    d = ImageDraw.Draw(top)
    x0 = W / 2 - w / 2
    d.polygon(P([(x0 - 10, y), (x0 + w + 10, y - tilt), (x0 + w, y + h - tilt), (x0, y + h)]),
              fill=bg + (255,), outline=(0, 0, 0, 255))
    d.text(P([(W / 2, y + h / 2 - tilt / 2)])[0], s, font=f, fill=fg + (255,), anchor="mm")
    return over(img, top)


#figures

def courier_leap(d, x, y, h, fill, scarf=None):
    """Side-on figure mid-leap, facing right; (x, y) is the top of the head box."""
    def F(u, v):
        return (x + u * h, y + v * h)
    lw = h * 0.085
    #back leg, back arm (drawn first so the torso overlaps them)
    limb(d, [F(0.40, 0.50), F(0.22, 0.66), F(0.02, 0.72)], lw, fill)
    limb(d, [F(0.50, 0.24), F(0.34, 0.32), F(0.20, 0.24)], lw * 0.8, fill)
    #torso leaning forward
    d.polygon(P([F(0.47, 0.16), F(0.63, 0.20), F(0.50, 0.52), F(0.36, 0.48)]), fill=fill)
    #front leg tucked forward, front arm reaching
    limb(d, [F(0.44, 0.50), F(0.66, 0.58), F(0.76, 0.78)], lw, fill)
    limb(d, [F(0.56, 0.22), F(0.74, 0.28), F(0.90, 0.20)], lw * 0.8, fill)
    #head
    r = h * 0.075
    hx, hy = F(0.62, 0.08)
    d.ellipse(P([(hx - r, hy - r), (hx + r, hy + r)]), fill=fill)
    #satchel on a strap
    d.line(P([F(0.60, 0.19), F(0.40, 0.44)]), fill=fill, width=S(h * 0.02))
    d.rounded_rectangle(P([F(0.28, 0.40), F(0.42, 0.52)]), radius=S(h * 0.02), fill=fill)
    if scarf:
        d.polygon(P([F(0.56, 0.15), F(0.30, 0.10), F(0.12, 0.16), F(-0.05, 0.09), F(0.10, 0.20),
                     F(0.32, 0.17), F(0.54, 0.20)]), fill=scarf)


def standing(d, x, y, h, fill, coat=True, hat=False, lantern_hand=None):
    """Front-on standing figure; (x, y) is the top of the head, x is the centre line."""
    def F(u, v):
        return (x + u * h, y + v * h)
    r = h * 0.07
    d.ellipse(P([(x - r, y), (x + r, y + 2 * r)]), fill=fill)
    if hat:
        d.polygon(P([F(-0.16, 0.05), F(0.16, 0.05), F(0.10, 0.03), F(0.08, -0.06), F(-0.08, -0.06), F(-0.10, 0.03)]), fill=fill)
    if coat:
        d.polygon(P([F(-0.12, 0.15), F(0.12, 0.15), F(0.20, 0.82), F(-0.20, 0.82)]), fill=fill)
    else:
        d.polygon(P([F(-0.12, 0.15), F(0.12, 0.15), F(0.09, 0.52), F(-0.09, 0.52)]), fill=fill)
    lw = h * 0.07
    limb(d, [F(-0.06, 0.78), F(-0.07, 1.0)], lw, fill)
    limb(d, [F(0.06, 0.78), F(0.07, 1.0)], lw, fill)
    if lantern_hand == "right":
        limb(d, [F(0.11, 0.18), F(0.24, 0.30), F(0.30, 0.16)], lw * 0.8, fill)
        limb(d, [F(-0.11, 0.18), F(-0.16, 0.52)], lw * 0.8, fill)
    else:
        limb(d, [F(0.11, 0.18), F(0.15, 0.52)], lw * 0.8, fill)
        limb(d, [F(-0.11, 0.18), F(-0.16, 0.52)], lw * 0.8, fill)


def skyline(d, base_y, rnd, color, window=None, x0=-20, x1=W + 20, hmin=120, hmax=520, wmin=50, wmax=130, window_chance=0.35):
    x = x0
    while x < x1:
        w = rnd.uniform(wmin, wmax)
        h = rnd.uniform(hmin, hmax)
        top = base_y - h
        d.rectangle(P([(x, top), (x + w, base_y + 5)]), fill=color)
        if rnd.random() < 0.35:
            d.rectangle(P([(x + w * 0.35, top - h * 0.12), (x + w * 0.65, top)]), fill=color)
            d.line(P([(x + w * 0.5, top - h * 0.12), (x + w * 0.5, top - h * 0.24)]), fill=color, width=S(3))
        if window:
            for wy in range(int(top + 14), int(base_y - 10), 22):
                for wx in range(int(x + 8), int(x + w - 10), 16):
                    if rnd.random() < window_chance:
                        d.rectangle(P([(wx, wy), (wx + 7, wy + 10)]), fill=rnd.choice(window))
        x += w + rnd.uniform(-6, 10)


#the seven covers

def cover_comet_city():
    rnd = random.Random(7)
    img = new_canvas([(0, (8, 6, 30)), (0.45, (48, 16, 82)), (0.72, (170, 40, 120)), (0.86, (250, 120, 90)), (1, (40, 10, 40))])
    #stars
    top = layer()
    d = ImageDraw.Draw(top)
    for _ in range(260):
        x, y = rnd.uniform(0, W), rnd.uniform(0, 800)
        r = rnd.choice([0.8, 1.1, 1.6, 2.2])
        d.ellipse(P([(x - r, y - r), (x + r, y + r)]), fill=(255, 255, 255, rnd.randint(120, 255)))
    img = over(img, top)
    #orbital relay ring, flickering dead
    img = glow(img, 640, 330, 250, (120, 60, 200), 0.6)
    top = layer()
    d = ImageDraw.Draw(top)
    d.ellipse(P([(380, 250), (900, 410)]), outline=(30, 20, 60, 255), width=S(26))
    d.ellipse(P([(380, 250), (900, 410)]), outline=(120, 220, 255, 170), width=S(5))
    for i in range(7):
        a = i * 0.9 + 0.3
        px_, py_ = 640 + 260 * math.cos(a), 330 + 80 * math.sin(a)
        pts = [(px_, py_)]
        for k in range(5):
            pts.append((pts[-1][0] + rnd.uniform(-30, 30), pts[-1][1] + rnd.uniform(-26, 26)))
        d.line(P(pts), fill=(170, 240, 255, 230), width=S(3))
    img = over(img, top)
    #the comet
    top = layer()
    d = ImageDraw.Draw(top)
    for i in range(40):
        t = i / 40
        x, y = lerp(40, 420, t), lerp(90, 520, t)
        r = lerp(4, 34, t)
        d.ellipse(P([(x - r, y - r), (x + r, y + r)]), fill=(200, 250, 255, int(40 + 150 * t)))
    img = over(img, top.filter(ImageFilter.GaussianBlur(S(6))))
    img = glow(img, 420, 520, 90, (220, 255, 255), 1.0, blur=40)
    #halftone in the upper sky
    img = halftone(img, (255, 60, 170, 70), lambda x, y: max(0, 1 - y / 700) * 0.55, spacing=12)
    #neon skyline, two depths
    top = layer()
    d = ImageDraw.Draw(top)
    skyline(d, 1180, rnd, (60, 20, 90, 255), window=[(255, 90, 200, 255), (90, 230, 255, 255)], hmin=160, hmax=460, window_chance=0.25)
    img = over(img, top)
    img = glow(img, 500, 1180, 520, (255, 80, 160), 0.35, blur=120)
    top = layer()
    d = ImageDraw.Draw(top)
    skyline(d, 1320, rnd, (14, 6, 26, 255), window=[(255, 220, 120, 255), (90, 230, 255, 255), (255, 90, 200, 255)],
            hmin=220, hmax=640, wmin=70, wmax=170, window_chance=0.3)
    img = over(img, top)
    #rooftops in the foreground and the courier mid-leap between them
    img = speed_lines(img, 60, (255, 255, 255, 60), (0, 820, 520, 1150), direction=(-1, 0.18), rnd=rnd)
    img = glow(img, 600, 930, 230, (90, 230, 255), 0.55, blur=100)
    top = layer()
    d = ImageDraw.Draw(top)
    d.polygon(P([(-10, 1180), (330, 1140), (360, 1510), (-10, 1510)]), fill=(8, 4, 16, 255))
    d.polygon(P([(700, 1120), (1010, 1090), (1010, 1510), (680, 1510)]), fill=(8, 4, 16, 255))
    d.rectangle(P([(760, 1040), (800, 1120)]), fill=(8, 4, 16, 255))
    courier_leap(d, 380, 760, 420, (6, 2, 12, 255), scarf=(255, 60, 150, 255))
    img = over(img, top)
    img = paper(img, 8)
    #trade dress
    img = masthead(img, "COMET CITY", "ERASBD.TTF", 38, max_w=640, size=150, x=575,
                   gradient=[(0, (255, 255, 255)), (0.5, (120, 240, 255)), (1, (255, 70, 190))], skew=-0.12)
    img = text(img, (575, 205), "CHRONICLES", "ERASBD.TTF", 46, (255, 230, 120), anchor="mt", stroke=5, spacing=12)
    img = corner_box(img, 7, "JUN 2024", bg=(20, 230, 255), fg=(10, 10, 30), accent=(200, 20, 120))
    img = banner(img, "SKYLINE STATIC", 1330, (255, 60, 150), (255, 255, 255), size=50, tilt=8)
    img = credits(img, "Elena Marsh  •  Devon Ashworth")
    img = barcode(img, x=40, y=H - 180)
    return img


def cover_copper_skies():
    rnd = random.Random(21)
    img = new_canvas([(0, (18, 60, 80)), (0.35, (70, 110, 110)), (0.6, (230, 140, 60)), (0.78, (250, 190, 110)), (1, (120, 50, 25))])
    img = glow(img, 330, 900, 300, (255, 220, 150), 0.9, blur=110)
    #sun disc with halftone rings
    top = layer()
    ImageDraw.Draw(top).ellipse(P([(160, 730), (500, 1070)]), fill=(255, 210, 120, 255))
    img = over(img, top)
    img = halftone(img, (230, 110, 40, 150), lambda x, y: max(0, (math.hypot(x - 330, y - 900) - 60) / 200) if math.hypot(x - 330, y - 900) < 170 else 0,
                   spacing=12, region=(150, 720, 510, 1080))
    #clouds
    cl = layer()
    d = ImageDraw.Draw(cl)
    for cx, cy, s, col in [(820, 520, 1.3, (255, 230, 200, 200)), (140, 610, 1.0, (255, 220, 190, 170)),
                           (560, 1120, 1.6, (140, 60, 40, 220)), (60, 1200, 1.4, (110, 45, 30, 230)), (940, 1180, 1.2, (120, 50, 30, 230))]:
        for _ in range(9):
            r = rnd.uniform(50, 110) * s
            x, y = cx + rnd.uniform(-130, 130) * s, cy + rnd.uniform(-30, 30) * s
            d.ellipse(P([(x - r, y - r * 0.6), (x + r, y + r * 0.6)]), fill=col)
    img = over(img, cl.filter(ImageFilter.GaussianBlur(S(8))))
    #distant airships
    top = layer()
    d = ImageDraw.Draw(top)
    for x, y, s in [(760, 380, 0.35), (170, 470, 0.25), (880, 700, 0.2)]:
        d.ellipse(P([(x - 170 * s, y - 50 * s), (x + 170 * s, y + 50 * s)]), fill=(60, 40, 40, 200))
        d.rectangle(P([(x - 50 * s, y + 55 * s), (x + 50 * s, y + 80 * s)]), fill=(60, 40, 40, 200))
    img = over(img, top)
    #hero airship
    top = layer()
    d = ImageDraw.Draw(top)
    ink = (38, 20, 16, 255)
    cx, cy = 560, 700
    d.ellipse(P([(cx - 400, cy - 150), (cx + 380, cy + 150)]), fill=ink)
    #cruciform tail fins, swept back and kept small so the envelope reads as a gasbag
    d.polygon(P([(cx - 300, cy - 110), (cx - 440, cy - 230), (cx - 470, cy - 220), (cx - 400, cy - 60)]), fill=ink)
    d.polygon(P([(cx - 300, cy + 110), (cx - 440, cy + 230), (cx - 470, cy + 220), (cx - 400, cy + 60)]), fill=ink)
    d.polygon(P([(cx - 380, cy - 14), (cx - 500, cy - 20), (cx - 500, cy + 20), (cx - 380, cy + 14)]), fill=ink)
    img = over(img, top)
    #copper rim light and envelope ribs
    top = layer()
    d = ImageDraw.Draw(top)
    d.arc(P([(cx - 392, cy - 142), (cx + 372, cy + 142)]), 150, 330, fill=(255, 170, 80, 255), width=S(9))
    for i in range(1, 8):
        xx = cx - 400 + i * 95
        half = 150 * math.sqrt(max(0, 1 - ((xx - (cx - 10)) / 390) ** 2))
        d.line(P([(xx, cy - half), (xx, cy + half)]), fill=(120, 60, 30, 220), width=S(4))
    d.line(P([(cx - 380, cy), (cx + 360, cy)]), fill=(120, 60, 30, 220), width=S(4))
    img = over(img, top)
    top = layer()
    d = ImageDraw.Draw(top)
    #gondola, struts, propeller
    d.polygon(P([(cx - 170, cy + 210), (cx + 150, cy + 210), (cx + 110, cy + 290), (cx - 130, cy + 290)]), fill=ink)
    for sx in (-140, -40, 60, 130):
        d.line(P([(cx + sx, cy + 140), (cx + sx * 0.9, cy + 212)]), fill=ink, width=S(8))
    for i in range(6):
        d.rectangle(P([(cx - 140 + i * 45, cy + 228), (cx - 118 + i * 45, cy + 250)]), fill=(255, 200, 110, 255))
    #pusher propeller on the tail spike
    d.ellipse(P([(cx - 520, cy - 80), (cx - 500, cy + 80)]), fill=ink)
    d.ellipse(P([(cx - 516, cy - 8), (cx - 500, cy + 8)]), fill=(255, 170, 80, 255))
    img = over(img, top)
    img = speed_lines(img, 30, (255, 240, 220, 70), (600, 560, 1000, 900), direction=(1, 0), rnd=rnd, length=(80, 240))
    #gear silhouettes along the bottom edge
    top = layer()
    d = ImageDraw.Draw(top)
    for gx, gy, gr in [(120, 1420, 150), (380, 1480, 110), (880, 1440, 170)]:
        for k in range(12):
            a = k * math.pi / 6
            d.polygon(P([(gx + gr * math.cos(a - 0.12), gy + gr * math.sin(a - 0.12)),
                         (gx + (gr + 34) * math.cos(a - 0.08), gy + (gr + 34) * math.sin(a - 0.08)),
                         (gx + (gr + 34) * math.cos(a + 0.08), gy + (gr + 34) * math.sin(a + 0.08)),
                         (gx + gr * math.cos(a + 0.12), gy + gr * math.sin(a + 0.12))]), fill=(30, 14, 10, 255))
        d.ellipse(P([(gx - gr, gy - gr), (gx + gr, gy + gr)]), fill=(30, 14, 10, 255))
        d.ellipse(P([(gx - gr * 0.3, gy - gr * 0.3), (gx + gr * 0.3, gy + gr * 0.3)]), fill=(120, 50, 25, 255))
    img = over(img, top)
    img = paper(img, 12)
    img = masthead(img, "COPPER SKIES", "ROCKB.TTF", 70, max_w=900, size=170,
                   gradient=[(0, (255, 235, 180)), (0.45, (235, 140, 60)), (1, (130, 55, 25))], outline=(40, 16, 8))
    img = text(img, (W / 2, 250), "A TALE OF THE AIRSHIP ERA", "ROCKB.TTF", 30, (255, 240, 210), anchor="mt", stroke=4,
               stroke_fill=(40, 16, 8), spacing=6)
    img = publisher_badge(img, "Driftwood Comics", x=W - 90, y=H - 190, color=(250, 230, 200), fg=(60, 25, 12))
    img = credits(img, "A Driftwood Comics Adventure", y=H - 60, stroke=(40, 16, 8))
    return img


def petal(d, cx, cy, angle, length, width, fill, outline, rivets=None):
    ca, sa = math.cos(angle), math.sin(angle)

    def R(u, v):
        return (cx + u * ca - v * sa, cy + u * sa + v * ca)
    pts = [R(0, 0)]
    for i in range(1, 12):
        t = i / 12
        pts.append(R(length * t, width * math.sin(math.pi * t) * (1 - 0.35 * t)))
    pts.append(R(length * 1.04, 0))
    for i in range(11, 0, -1):
        t = i / 12
        pts.append(R(length * t, -width * math.sin(math.pi * t) * (1 - 0.35 * t)))
    d.polygon(P(pts), fill=fill, outline=outline, width=S(5))
    d.line(P([R(length * 0.12, 0), R(length * 0.9, 0)]), fill=outline, width=S(3))
    if rivets:
        for t in (0.3, 0.5, 0.7):
            for side in (-1, 1):
                x, y = R(length * t, side * width * 0.45 * math.sin(math.pi * t))
                d.ellipse(P([(x - 5, y - 5), (x + 5, y + 5)]), fill=rivets)


def cover_iron_petal():
    rnd = random.Random(3)
    img = new_canvas([(0, (8, 30, 34)), (0.5, (16, 60, 58)), (1, (6, 20, 22))])
    img = rays(img, 500, 700, 28, (120, 255, 200, 26), spread=0.6)
    img = glow(img, 500, 700, 360, (120, 255, 190), 0.75, blur=150)
    img = halftone(img, (0, 20, 20, 120), lambda x, y: min(1, math.hypot(x - 500, y - 700) / 700), spacing=12)
    top = layer()
    d = ImageDraw.Draw(top)
    #stem and leaves
    d.line(P([(500, 760), (520, 1000), (470, 1250), (500, 1520)]), fill=(20, 45, 40, 255), width=S(40), joint="curve")
    petal(d, 510, 1050, math.radians(-25), 260, 70, (40, 90, 80, 255), (5, 15, 15, 255))
    petal(d, 485, 1180, math.radians(200), 240, 60, (40, 90, 80, 255), (5, 15, 15, 255))
    #outer iron petals, inner rose-steel petals, glowing core
    for i in range(10):
        a = i * 2 * math.pi / 10 + 0.15
        petal(d, 500, 700, a, 330, 92, (132, 150, 160, 255), (10, 20, 25, 255), rivets=(60, 70, 80, 255))
    for i in range(8):
        a = i * 2 * math.pi / 8 + 0.5
        petal(d, 500, 700, a, 220, 80, (220, 150, 175, 255), (40, 10, 25, 255), rivets=(150, 90, 110, 255))
    d.ellipse(P([(440, 640), (560, 760)]), fill=(255, 220, 120, 255), outline=(60, 30, 0, 255), width=S(6))
    img = over(img, top)
    img = glow(img, 500, 700, 90, (255, 240, 170), 1.0, blur=45)
    #fireflies
    top = layer()
    d = ImageDraw.Draw(top)
    for _ in range(40):
        x, y = rnd.uniform(40, 960), rnd.uniform(380, 1300)
        r = rnd.uniform(2, 5)
        d.ellipse(P([(x - r, y - r), (x + r, y + r)]), fill=(230, 255, 170, 230))
    img = over(img, top)
    img = over(img, top.filter(ImageFilter.GaussianBlur(S(10))))
    #the gardeners, small against the bloom
    top = layer()
    d = ImageDraw.Draw(top)
    d.polygon(P([(-10, 1330), (300, 1290), (700, 1310), (1010, 1280), (1010, 1510), (-10, 1510)]), fill=(4, 12, 12, 255))
    standing(d, 200, 1060, 260, (4, 12, 12, 255), coat=True, hat=True, lantern_hand="right")
    standing(d, 800, 1090, 230, (4, 12, 12, 255), coat=True, lantern_hand=None)
    img = over(img, top)
    img = glow(img, 278, 1102, 30, (255, 220, 120), 1.0, blur=18)
    img = paper(img, 10)
    img = masthead(img, "IRON PETAL", "constanb.ttf", 48, max_w=730, size=170, x=585,
                   gradient=[(0, (235, 245, 250)), (0.5, (160, 180, 190)), (1, (230, 150, 175))], outline=(10, 20, 25))
    img = text(img, (585, 238), "SOCIETY", "constanb.ttf", 58, (230, 150, 175), anchor="mt", stroke=6,
               stroke_fill=(10, 20, 25), spacing=26)
    img = corner_box(img, 3, "2023", bg=(230, 240, 235), fg=(10, 30, 30), accent=(170, 60, 100))
    img = banner(img, "A BLOOM THAT SHOULDN'T EXIST", 1352, (230, 150, 175), (20, 10, 16), fontname="constanb.ttf", size=34)
    img = credits(img, "Nova Harbor Press", y=H - 52)
    img = barcode(img, x=W - 190, y=H - 220, seed=3)
    return img


def cover_midnight_cartographer():
    rnd = random.Random(11)
    img = new_canvas([(0, (6, 10, 30)), (0.55, (18, 30, 70)), (0.85, (40, 50, 90)), (1, (10, 12, 24))])
    #moon
    img = glow(img, 730, 430, 260, (160, 180, 255), 0.55, blur=140)
    top = layer()
    ImageDraw.Draw(top).ellipse(P([(590, 290), (870, 570)]), fill=(240, 236, 210, 255))
    img = over(img, top)
    img = halftone(img, (170, 165, 140, 200), lambda x, y: 0.45 if (x - 700) ** 2 + (y - 470) ** 2 < 70 ** 2 or (x - 790) ** 2 + (y - 380) ** 2 < 40 ** 2 else 0,
                   spacing=10, region=(590, 290, 870, 570))
    #glowing street map in the sky, streets dissolving as they rise
    mp = layer()
    d = ImageDraw.Draw(mp)
    for _ in range(46):
        x = rnd.uniform(60, 940)
        y = rnd.uniform(560, 1260)
        horiz = rnd.random() < 0.5
        ln = rnd.uniform(90, 330)
        a = int(255 * min(1, max(0.12, (y - 500) / 700)))
        if horiz:
            d.line(P([(x - ln / 2, y), (x + ln / 2, y)]), fill=(255, 205, 110, a), width=S(4))
        else:
            d.line(P([(x, y - ln / 2), (x, y + ln / 2)]), fill=(255, 205, 110, a), width=S(4))
        if rnd.random() < 0.3:
            d.ellipse(P([(x - 6, y - 6), (x + 6, y + 6)]), fill=(255, 240, 180, a))
    for k in range(3):
        cx, cy = rnd.uniform(200, 800), rnd.uniform(650, 1150)
        d.arc(P([(cx - 180, cy - 180), (cx + 180, cy + 180)]), 200, 330, fill=(255, 205, 110, 160), width=S(4))
    img = over(img, mp.filter(ImageFilter.GaussianBlur(S(1.5))))
    img = over(img, mp.filter(ImageFilter.GaussianBlur(S(9))))
    #compass rose behind her
    top = layer()
    d = ImageDraw.Draw(top)
    cx, cy = 500, 1070
    for i in range(8):
        a = i * math.pi / 4 - math.pi / 2
        ln = 260 if i % 2 == 0 else 150
        w = 0.18
        d.polygon(P([(cx, cy), (cx + ln * math.cos(a - w), cy + ln * math.sin(a - w)) if False else (cx + 40 * math.cos(a - math.pi / 2), cy + 40 * math.sin(a - math.pi / 2)),
                     (cx + ln * math.cos(a), cy + ln * math.sin(a)),
                     (cx + 40 * math.cos(a + math.pi / 2), cy + 40 * math.sin(a + math.pi / 2))]),
                  fill=(255, 205, 110, 60 if i % 2 else 90))
    d.ellipse(P([(cx - 210, cy - 210), (cx + 210, cy + 210)]), outline=(255, 205, 110, 90), width=S(4))
    img = over(img, top)
    #fog bank
    fog = layer()
    d = ImageDraw.Draw(fog)
    for _ in range(18):
        x, y = rnd.uniform(-100, 1100), rnd.uniform(1280, 1440)
        r = rnd.uniform(120, 240)
        d.ellipse(P([(x - r, y - r * 0.4), (x + r, y + r * 0.4)]), fill=(150, 160, 200, 90))
    img = over(img, fog.filter(ImageFilter.GaussianBlur(S(30))))
    #the cartographer, coat and hat, lantern raised
    top = layer()
    d = ImageDraw.Draw(top)
    d.polygon(P([(-10, 1400), (1010, 1380), (1010, 1510), (-10, 1510)]), fill=(4, 6, 14, 255))
    standing(d, 470, 930, 480, (4, 6, 14, 255), coat=True, hat=True, lantern_hand="right")
    #rolled map under her arm
    d.rounded_rectangle(P([(360, 1080), (410, 1250)]), radius=S(20), fill=(4, 6, 14, 255))
    img = over(img, top)
    img = glow(img, 614, 1000, 50, (255, 210, 120), 1.0, blur=30)
    top = layer()
    ImageDraw.Draw(top).ellipse(P([(602, 988), (626, 1012)]), fill=(255, 245, 200, 255))
    img = over(img, top)
    img = paper(img, 9)
    img = masthead(img, "MIDNIGHT", "georgiab.ttf", 44, max_w=730, size=170, x=585,
                   gradient=[(0, (255, 245, 210)), (1, (230, 180, 90))], outline=(10, 12, 30))
    img = text(img, (585, 238), "CARTOGRAPHER", "georgiab.ttf", 58, (255, 205, 110), anchor="mt", stroke=6,
               stroke_fill=(10, 12, 30), spacing=10)
    img = corner_box(img, 1, "2022", bg=(255, 205, 110), fg=(10, 12, 30), accent=(150, 30, 30))
    #the classic first-issue burst
    top = layer()
    d = ImageDraw.Draw(top)
    bx, by, n = 170, 390, 16
    pts = []
    for i in range(n * 2):
        a = i * math.pi / n
        r = 105 if i % 2 == 0 else 72
        pts.append((bx + r * math.cos(a), by + r * math.sin(a)))
    d.polygon(P(pts), fill=(230, 40, 40, 255), outline=(10, 10, 10, 255), width=S(5))
    img = over(img, top)
    img = text(img, (bx, by - 22), "FIRST", "impact.ttf", 34, (255, 255, 255), anchor="mm", stroke=2)
    img = text(img, (bx, by + 18), "ISSUE!", "impact.ttf", 38, (255, 240, 120), anchor="mm", stroke=2)
    img = credits(img, "Driftwood Comics", y=H - 52)
    return img


def cartoon_intern(d, x, y, h, facing, skin, shirt, tie, ink):
    """Big-headed cartoon office worker, leaning back into a tug of war."""
    def F(u, v):
        return (x + u * h * facing, y + v * h)
    lw = h * 0.07
    limb(d, [F(0.0, 0.62), F(-0.12, 0.80), F(-0.22, 0.98)], lw + 8, ink)
    limb(d, [F(0.05, 0.62), F(0.14, 0.82), F(0.10, 0.99)], lw + 8, ink)
    limb(d, [F(0.0, 0.62), F(-0.12, 0.80), F(-0.22, 0.98)], lw, (40, 40, 60, 255))
    limb(d, [F(0.05, 0.62), F(0.14, 0.82), F(0.10, 0.99)], lw, (40, 40, 60, 255))
    torso = [F(-0.10, 0.34), F(0.16, 0.30), F(0.18, 0.64), F(-0.10, 0.66)]
    d.polygon(P(torso), fill=shirt, outline=ink, width=S(6))
    d.polygon(P([F(0.02, 0.34), F(0.07, 0.34), F(0.08, 0.52), F(0.045, 0.57), F(0.01, 0.52)]), fill=tie, outline=ink, width=S(3))
    #both arms reaching forward to the stapler
    limb(d, [F(0.12, 0.38), F(0.34, 0.44), F(0.50, 0.42)], lw + 8, ink)
    limb(d, [F(0.12, 0.38), F(0.34, 0.44), F(0.50, 0.42)], lw, shirt)
    hx, hy = F(-0.02, 0.16)
    r = h * 0.17
    d.ellipse(P([(hx - r, hy - r), (hx + r, hy + r)]), fill=skin, outline=ink, width=S(6))
    #gritted face: eyes and a strained mouth
    ex = hx + facing * r * 0.35
    d.ellipse(P([(ex - 8, hy - 18), (ex + 8, hy - 2)]), fill=ink)
    d.ellipse(P([(ex + facing * 36 - 8, hy - 18), (ex + facing * 36 + 8, hy - 2)]), fill=ink)
    mx0, mx1 = sorted((ex - 4 + facing * 4, ex + facing * 40))
    d.rectangle(P([(mx0, hy + 22), (mx1, hy + 34)]), fill=(255, 255, 255, 255), outline=ink, width=S(4))


def cover_paperclip():
    rnd = random.Random(4)
    img = new_canvas([(0, (255, 220, 40)), (1, (255, 180, 20))])
    img = rays(img, 500, 850, 22, (255, 110, 40, 160), spread=0.55, start=0.1)
    img = halftone(img, (230, 40, 40, 110), lambda x, y: 0.35 + 0.35 * (y / H), spacing=13)
    img = glow(img, 500, 850, 230, (255, 255, 230), 0.8, blur=90)
    top = layer()
    d = ImageDraw.Draw(top)
    ink = (15, 15, 20, 255)
    #giant paperclip leaning behind the fight
    clip = [(330, 1180), (330, 470), (480, 380), (620, 470), (620, 1060), (520, 1130), (430, 1060), (430, 560), (480, 530), (520, 560), (520, 980)]
    d.line(P(clip), fill=ink, width=S(46), joint="curve")
    d.line(P(clip), fill=(200, 210, 225, 255), width=S(30), joint="curve")
    d.line(P([(x + 6, y - 4) for x, y in clip]), fill=(255, 255, 255, 200), width=S(8), joint="curve")
    img = over(img, top)
    img = speed_lines(img, 26, (20, 20, 20, 110), (120, 1020, 880, 1240), direction=(0, 1), rnd=rnd, length=(20, 60), width=(3, 6))
    top = layer()
    d = ImageDraw.Draw(top)
    #the stapler everyone wants
    d.rounded_rectangle(P([(400, 960), (600, 1010)]), radius=S(14), fill=(220, 30, 40, 255), outline=ink, width=S(7))
    d.rounded_rectangle(P([(390, 1010), (610, 1040)]), radius=S(10), fill=(60, 60, 70, 255), outline=ink, width=S(7))
    cartoon_intern(d, 200, 820, 420, 1, (255, 205, 170, 255), (120, 170, 255, 255), (220, 30, 40, 255), ink)
    cartoon_intern(d, 800, 820, 420, -1, (200, 140, 100, 255), (255, 255, 255, 255), (40, 160, 90, 255), ink)
    img = over(img, top)
    #speech balloon
    top = layer()
    d = ImageDraw.Draw(top)
    d.ellipse(P([(470, 560), (960, 800)]), fill=(255, 255, 255, 255), outline=ink, width=S(7))
    d.polygon(P([(760, 780), (820, 790), (820, 870)]), fill=(255, 255, 255, 255))
    d.line(P([(760, 782), (820, 870), (825, 792)]), fill=ink, width=S(7))
    img = over(img, top)
    img = text(img, (715, 650), "THAT'S MY", "comicbd.ttf", 48, (15, 15, 20), anchor="mm")
    img = text(img, (715, 712), "STAPLER!!", "comicbd.ttf", 58, (220, 30, 40), anchor="mm")
    img = paper(img, 7)
    img = masthead(img, "PAPERCLIP", "SHOWG.TTF", 40, max_w=900, size=180, fill=(230, 30, 40), outline=(15, 15, 20),
                   stroke=10, shadow=(15, 15, 20), shadow_off=(12, 12), skew=-0.08)
    img = banner(img, "DETECTIVE AGENCY", 250, (15, 15, 20), (255, 230, 60), fontname="SHOWG.TTF", size=52, tilt=-10)
    img = corner_box(img, 4, "2021", price="$2.99", x=W - 170, y=380, bg=(255, 255, 255), fg=(15, 15, 20))
    img = banner(img, "OFFICE HOURS ARE OVER!", 1290, (230, 30, 40), (255, 255, 255), size=48, tilt=6)
    img = credits(img, "Driftwood Comics", y=H - 52)
    return img


def cover_last_lighthouse():
    rnd = random.Random(9)
    img = new_canvas([(0, (20, 28, 40)), (0.45, (60, 80, 100)), (0.62, (120, 130, 140)), (0.63, (30, 50, 60)), (1, (8, 16, 22))])
    #storm clouds
    cl = layer()
    d = ImageDraw.Draw(cl)
    for _ in range(40):
        x, y = rnd.uniform(-100, 1100), rnd.uniform(-50, 700)
        r = rnd.uniform(90, 200)
        shade = rnd.randint(20, 60)
        d.ellipse(P([(x - r, y - r * 0.55), (x + r, y + r * 0.55)]), fill=(shade, shade + 6, shade + 14, 200))
    img = over(img, cl.filter(ImageFilter.GaussianBlur(S(18))))
    #lightning fork far out to sea
    top = layer()
    d = ImageDraw.Draw(top)
    pts = [(170, 120)]
    while pts[-1][1] < 860:
        pts.append((pts[-1][0] + rnd.uniform(-40, 40), pts[-1][1] + rnd.uniform(50, 90)))
    d.line(P(pts), fill=(230, 240, 255, 255), width=S(5))
    img = over(img, top)
    img = glow(img, 170, 500, 220, (170, 190, 230), 0.4, blur=120)
    #the beam: a soft wedge cutting across the storm
    lx, ly = 640, 610
    beam = Image.new("L", img.size, 0)
    bd = ImageDraw.Draw(beam)
    bd.polygon(P([(lx, ly - 10), (-200, ly - 280), (-200, ly + 60), (lx, ly + 10)]), fill=190)
    bd.polygon(P([(lx, ly - 10), (1200, ly - 120), (1200, ly + 60), (lx, ly + 10)]), fill=110)
    beam = beam.filter(ImageFilter.GaussianBlur(S(24)))
    img = ImageChops.screen(img, Image.composite(Image.new("RGB", img.size, (255, 236, 170)), Image.new("RGB", img.size, 0), beam))
    img = glow(img, lx, ly, 70, (255, 240, 190), 1.0, blur=35)
    #sea: layered wave bands with foam
    top = layer()
    d = ImageDraw.Draw(top)
    for band in range(9):
        y0 = 940 + band * 62
        col = mix((40, 70, 85), (6, 14, 20), band / 8) + (255,)
        pts = [(-20, 1600)]
        x = -20
        while x <= 1040:
            pts.append((x, y0 + 22 * math.sin(x / 70 + band * 1.7) + rnd.uniform(-6, 6)))
            x += 20
        pts.append((1040, 1600))
        d.polygon(P(pts), fill=col)
        for k in range(10):
            fx = rnd.uniform(0, 1000)
            d.arc(P([(fx - 40, y0 - 18), (fx + 40, y0 + 18)]), 190, 340, fill=(220, 235, 240, 180), width=S(4))
    #drowned rooftops
    for rx, ry, rw in [(90, 1030, 120), (250, 1060, 90), (820, 1010, 140)]:
        d.polygon(P([(rx, ry), (rx + rw / 2, ry - rw * 0.45), (rx + rw, ry)]), fill=(12, 18, 24, 255))
    #lighthouse on its rock
    d.polygon(P([(470, 1030), (820, 1010), (900, 1120), (400, 1140)]), fill=(10, 14, 18, 255))
    tower = [(575, 1030), (705, 1030), (680, 660), (600, 660)]
    d.polygon(P(tower), fill=(230, 225, 215, 255), outline=(10, 14, 18, 255), width=S(6))
    for yb in (760, 880, 990):
        t0, t1 = (yb - 660) / 370, (yb + 45 - 660) / 370
        d.polygon(P([(lerp(600, 575, t0), yb), (lerp(680, 705, t0), yb), (lerp(680, 705, t1), yb + 45), (lerp(600, 575, t1), yb + 45)]),
                  fill=(180, 40, 40, 255))
    d.rectangle(P([(585, 640), (695, 662)]), fill=(10, 14, 18, 255))
    d.rectangle(P([(605, 580), (675, 640)]), fill=(255, 235, 170, 255), outline=(10, 14, 18, 255), width=S(6))
    d.polygon(P([(595, 580), (685, 580), (640, 535)]), fill=(10, 14, 18, 255))
    #the returning keeper's rowboat
    d.polygon(P([(210, 1210), (380, 1210), (350, 1245), (240, 1245)]), fill=(10, 14, 18, 255))
    standing(d, 300, 1110, 110, (10, 14, 18, 255), coat=True, hat=True)
    img = over(img, top)
    #rain
    rain = layer()
    d = ImageDraw.Draw(rain)
    for _ in range(700):
        x, y = rnd.uniform(-100, 1100), rnd.uniform(0, 1500)
        d.line(P([(x, y), (x - 12, y + 44)]), fill=(200, 215, 230, 90), width=S(1.4))
    img = over(img, rain)
    img = paper(img, 11)
    img = masthead(img, "THE LAST", "georgiab.ttf", 50, max_w=560, size=80, fill=(240, 236, 225), outline=(10, 14, 18),
                   stroke=5, shadow=None)
    img = masthead(img, "LIGHTHOUSE", "georgiab.ttf", 128, max_w=900, size=160,
                   gradient=[(0, (255, 244, 210)), (1, (230, 196, 120))], outline=(10, 14, 18), stroke=7, shadow_off=(6, 8))
    img = text(img, (W / 2, 300), "A GRAPHIC NOVEL", "georgiab.ttf", 26, (240, 236, 225), anchor="mt", stroke=3,
               stroke_fill=(10, 14, 18), spacing=10)
    img = credits(img, "Driftwood Comics", y=H - 52)
    img = barcode(img, x=W - 190, y=H - 220, seed=9)
    return img


def cover_wraith_and_wheel():
    rnd = random.Random(12)
    img = new_canvas([(0, (4, 10, 8)), (0.42, (14, 40, 30)), (0.5, (60, 140, 90)), (0.52, (10, 22, 18)), (1, (4, 6, 6))])
    img = glow(img, 500, 740, 420, (90, 255, 160), 0.5, blur=180)
    #highway to a vanishing point
    vx, vy = 500, 745
    top = layer()
    d = ImageDraw.Draw(top)
    d.polygon(P([(vx - 8, vy), (vx + 8, vy), (1250, 1510), (-250, 1510)]), fill=(18, 20, 22, 255))
    for i in range(14):
        t0 = (i / 14) ** 2
        t1 = ((i + 0.5) / 14) ** 2
        y0, y1 = lerp(vy, 1510, t0), lerp(vy, 1510, t1)
        w0, w1 = lerp(1, 18, t0), lerp(1, 18, t1)
        d.polygon(P([(vx - w0, y0), (vx + w0, y0), (vx + w1, y1), (vx - w1, y1)]), fill=(240, 200, 60, 255))
    for side in (-1, 1):
        d.line(P([(vx + side * 8, vy), (vx + side * 760, 1510)]), fill=(200, 200, 200, 200), width=S(6))
    img = over(img, top)
    #desert mesas on the horizon
    top = layer()
    d = ImageDraw.Draw(top)
    d.polygon(P([(-10, 750), (120, 690), (230, 690), (300, 745), (700, 745), (760, 670), (900, 670), (1010, 740), (1010, 760), (-10, 760)]),
              fill=(6, 18, 14, 255))
    img = over(img, top)
    img = speed_lines(img, 110, (160, 255, 200, 70), (0, 350, 1000, 1400), direction=(1, 0), rnd=rnd, length=(140, 520), width=(2, 5))
    #ghost wisps streaming behind the rider
    wisp = layer()
    d = ImageDraw.Draw(wisp)
    for k in range(9):
        y0 = rnd.uniform(930, 1150)
        pts = []
        for i in range(12):
            x = 520 - i * 55
            pts.append((x, y0 + 24 * math.sin(i * 0.8 + k) - i * 6))
        d.line(P(pts), fill=(140, 255, 190, 150), width=S(rnd.uniform(10, 26)), joint="curve")
    img = over(img, wisp.filter(ImageFilter.GaussianBlur(S(10))))
    #the wraith on its motorcycle, side-on, heading right
    top = layer()
    d = ImageDraw.Draw(top)
    ink = (4, 8, 6, 255)
    for wx in (330, 740):
        d.ellipse(P([(wx - 115, 1080), (wx + 115, 1310)]), fill=ink)
        d.ellipse(P([(wx - 70, 1125), (wx + 70, 1265)]), outline=(120, 255, 170, 255), width=S(8))
        d.ellipse(P([(wx - 20, 1175), (wx + 20, 1215)]), fill=(120, 255, 170, 255))
    d.polygon(P([(330, 1195), (440, 1070), (640, 1050), (760, 1110), (740, 1195), (600, 1210), (460, 1220)]), fill=ink)
    d.polygon(P([(640, 1050), (720, 980), (760, 990), (700, 1070)]), fill=ink)
    img = over(img, top)
    #ragged cloak tatters streaming straight back off the shoulders - several thin tapering
    #tongues rather than one solid shape, so they read as cloth in the wind, not a wing
    tat = layer()
    d = ImageDraw.Draw(tat)
    for k in range(7):
        y0 = 930 + k * 16
        ln = rnd.uniform(260, 420)
        amp = rnd.uniform(10, 22)
        upper, lower = [], []
        for i in range(15):
            t = i / 14
            x = 560 - t * ln
            y = y0 + amp * math.sin(t * 5.5 + k) + t * 30
            half = lerp(20, 1.5, t)
            upper.append((x, y - half))
            lower.append((x, y + half))
        d.polygon(P(upper + lower[::-1]), fill=(8, 22, 16, 235))
    img = over(img, tat)
    top = layer()
    d = ImageDraw.Draw(top)
    #rider seated low over the tank: hips on the seat, torso raked forward, arms to the bars
    d.polygon(P([(470, 1080), (560, 1070), (640, 940), (590, 905), (520, 950)]), fill=ink)
    limb(d, [(520, 1070), (600, 1120), (590, 1185)], 40, ink)
    limb(d, [(610, 935), (680, 975), (722, 990)], 30, ink)
    #hooded head, hood peak pointing forward
    d.ellipse(P([(575, 845), (655, 925)]), fill=ink)
    d.polygon(P([(600, 850), (690, 880), (650, 905)]), fill=ink)
    img = over(img, top)
    #eyes and headlight
    img = glow(img, 638, 882, 26, (140, 255, 190), 1.0, blur=10)
    top = layer()
    d = ImageDraw.Draw(top)
    d.ellipse(P([(626, 876), (636, 886)]), fill=(220, 255, 230, 255))
    d.ellipse(P([(642, 874), (652, 884)]), fill=(220, 255, 230, 255))
    img = over(img, top)
    head = Image.new("L", img.size, 0)
    ImageDraw.Draw(head).polygon(P([(760, 1000), (1200, 880), (1200, 1150)]), fill=170)
    head = head.filter(ImageFilter.GaussianBlur(S(30)))
    img = ImageChops.screen(img, Image.composite(Image.new("RGB", img.size, (230, 255, 220)), Image.new("RGB", img.size, 0), head))
    img = glow(img, 765, 1000, 40, (255, 255, 230), 1.0, blur=18)
    img = paper(img, 9)
    img = masthead(img, "WRAITH", "impact.ttf", 34, max_w=700, size=250, skew=-0.18, x=600,
                   gradient=[(0, (220, 255, 230)), (0.55, (90, 240, 150)), (1, (20, 110, 60))], outline=(4, 10, 8),
                   stroke=10, shadow_off=(14, 14))
    img = text(img, (640, 300), "& WHEEL", "impact.ttf", 90, (230, 40, 40), anchor="mt", stroke=7,
               stroke_fill=(4, 10, 8))
    img = corner_box(img, 12, "2025", bg=(230, 40, 40), fg=(255, 255, 255), accent=(255, 230, 120))
    img = banner(img, "THE FINAL RUN", 1350, (230, 40, 40), (255, 255, 255), size=56, tilt=8)
    img = credits(img, "Nova Harbor Press", y=H - 44)
    return img


COVERS = {
    "Comet City Chronicles 07.cbz": cover_comet_city,
    "Copper Skies.cbz": cover_copper_skies,
    "Iron Petal Society 03.cbz": cover_iron_petal,
    "Midnight Cartographer 01.cbz": cover_midnight_cartographer,
    "Paperclip Detective Agency 04.cbz": cover_paperclip,
    "The Last Lighthouse.cbz": cover_last_lighthouse,
    "Wraith and Wheel 12.cbz": cover_wraith_and_wheel,
}


def render(name):
    big = COVERS[name]()
    return big.resize((W, H), Image.LANCZOS)


def repack(src, dst, cover_jpeg):
    """Same entries, same order, same ComicInfo.xml bytes - only page001.jpg is swapped."""
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dst + ".tmp", "w") as zout:
        for info in zin.infolist():
            data = cover_jpeg if info.filename == "page001.jpg" else zin.read(info.filename)
            ct = zipfile.ZIP_DEFLATED if info.filename.lower().endswith(".xml") else zipfile.ZIP_STORED
            zout.writestr(zipfile.ZipInfo(info.filename, info.date_time), data, compress_type=ct)
    os.replace(dst + ".tmp", dst)


if __name__ == "__main__":
    out_dir, books_src, books_dst = sys.argv[1], sys.argv[2], sys.argv[3]
    only = sys.argv[4:] or list(COVERS)
    for name in only:
        img = render(name)
        png = os.path.join(out_dir, os.path.splitext(name)[0] + ".png")
        img.save(png)
        buf = io.BytesIO()
        img.save(buf, "JPEG", quality=90, optimize=True)
        repack(os.path.join(books_src, name), os.path.join(books_dst, name), buf.getvalue())
        print(f"{name}: {len(buf.getvalue()) // 1024} KB cover")
