#draws the cbzLab and cbzLite logos from one design, so the pair always match: a sunset and
#perspective grid badge (colours sampled from the original hand-made cbzLab logo) with a neon
#wordmark over it. also writes the badge on its own, the background every logo is built on
#
#usage: py branding/make_logo.py cbzLab cbzLite
#writes branding/badge.png plus branding/logo-<name>.png for each name given
#needs pillow; the wordmark font, Oooh Baby (SIL Open Font License), is in branding/fonts
import pathlib
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

OUT = pathlib.Path(__file__).parent
FONT = OUT / "fonts" / "OoohBaby-Regular.ttf"
FONT_SIZE = 400
#how much the letters are thickened, and how far in the pale hot core sits
STROKE = 4
CORE = 5
#0 for solid strokes; otherwise each stroke is drawn as a hollow tube, its middle this many px wide
HOLLOW = 0
#how steeply the wordmark rises to the right, and how far below the badge's centre it sits
ANGLE = 15
LIFT = 70
S = 1200
CX, CY, R = 600, 540, 300
HORIZON = CY - 30


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


#a colour at t (0..1) along a list of (position, colour) stops
def ramp(stops, t):
    t = max(0.0, min(1.0, t))
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        if t <= p1:
            return lerp(c0, c1, (t - p0) / (p1 - p0) if p1 > p0 else 0)
    return stops[-1][1]


SKY = [(0, (88, 37, 107)), (0.6, (66, 50, 170)), (0.9, (64, 84, 196)), (1, (90, 140, 220))]
GROUND = [(0, (8, 26, 92)), (0.35, (3, 10, 40)), (1, (1, 2, 14))]
#the sun runs magenta through pink and orange to yellow, top to bottom
SUN = [(0, (255, 0, 232)), (0.3, (236, 36, 214)), (0.55, (255, 70, 160)), (0.8, (255, 140, 70)), (1, (255, 214, 50))]
#the grid runs green at the horizon through cyan to blue at the front
GRID = [(0, (70, 255, 140)), (0.2, (40, 240, 190)), (0.45, (25, 190, 245)), (0.7, (30, 120, 255)), (1, (55, 70, 255))]


#adds a glow's light onto an image, so overlapping glows brighten instead of just covering
def add_light(base, glow, strength):
    rgb = glow.convert("RGB")
    alpha = glow.getchannel("A").point(lambda v: min(255, int(v * strength * 2.2)))
    lit = Image.composite(rgb, Image.new("RGB", rgb.size), alpha)
    out = ImageChops.add(base.convert("RGB"), lit)
    out.putalpha(base.getchannel("A"))
    return out


def draw_badge():
    badge = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    px = badge.load()
    for y in range(CY - R, CY + R + 1):
        if y < HORIZON:
            c = ramp(SKY, (y - (CY - R)) / (HORIZON - (CY - R)))
        else:
            c = ramp(GROUND, (y - HORIZON) / (CY + R - HORIZON))
        for x in range(CX - R, CX + R + 1):
            px[x, y] = c + (255,)

    #striped sunset, with gaps that widen towards the horizon
    sun = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sp = sun.load()
    sr = 185
    for y in range(HORIZON - sr, HORIZON):
        t = (y - (HORIZON - sr)) / sr
        gap = 2 + int(t * 7) * 2
        if t > 0.32 and (y - (HORIZON - sr)) % 24 < gap:
            continue
        half = int((sr * sr - (HORIZON - y) ** 2) ** 0.5)
        c = ramp(SUN, t) + (255,)
        for x in range(CX - half, CX + half):
            sp[x, y] = c
    badge = Image.alpha_composite(badge, sun)

    #perspective grid drawn as a mask, then coloured by depth so every line shifts green to blue
    lines = Image.new("L", (S, S), 0)
    g = ImageDraw.Draw(lines)
    y, step = HORIZON, 9
    while y < CY + R:
        g.line([(CX - R, y), (CX + R, y)], fill=255, width=4)
        step *= 1.5
        y += int(step)
    for i in range(-9, 10):
        g.line([(CX + i * 30, HORIZON), (CX + i * 210, CY + R + 200)], fill=255, width=4)
    #a brighter line where the ground meets the sky
    g.line([(CX - R, HORIZON), (CX + R, HORIZON)], fill=255, width=5)
    colour = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    cp = colour.load()
    for yy in range(HORIZON - 4, CY + R + 1):
        c = ramp(GRID, (yy - HORIZON) / (CY + R - HORIZON)) + (255,)
        for x in range(CX - R, CX + R + 1):
            cp[x, yy] = c
    grid = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grid.paste(colour, (0, 0), lines)
    #neon glow like the original: a broad haze that lights the floor, a tighter halo round each
    #line, added rather than layered so crossings and the haze build up brighter
    #faint at the horizon, where the lines crowd together, and strongest at the front
    depth = Image.new("L", (S, S), 0)
    dp = depth.load()
    for yy in range(HORIZON, S):
        v = 70 + int(185 * min(1.0, (yy - HORIZON) / (CY + R - HORIZON)))
        for x in range(CX - R, CX + R + 1):
            dp[x, yy] = v
    weighted = grid.copy()
    weighted.putalpha(ImageChops.multiply(grid.getchannel("A"), depth))
    for radius, strength in ((36, 3.4), (14, 2.6), (5, 1.4)):
        glow = weighted.filter(ImageFilter.GaussianBlur(radius))
        badge = add_light(badge, glow, strength)
    #the tubes themselves burn nearly white at the centre, tinted by their colour
    hot = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hot.paste(colour.point(lambda v: min(255, v + 80)), (0, 0), lines.filter(ImageFilter.MinFilter(3)))
    badge = Image.alpha_composite(badge, grid)
    badge = Image.alpha_composite(badge, hot)

    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).ellipse([CX - R, CY - R, CX + R, CY + R], fill=255)
    badge.putalpha(ImageChops.multiply(badge.getchannel("A"), mask))
    return badge


def draw_logo(badge, name):
    #neon wordmark: tubes like the original - a pink stroke with a pale hot core and a soft glow
    font = ImageFont.truetype(str(FONT), FONT_SIZE)
    shape = Image.new("L", (S * 3, S), 0)
    ImageDraw.Draw(shape).text((S * 3 // 2, S // 2), name, font=font, anchor="mm", fill=255,
                               stroke_width=STROKE, stroke_fill=255)
    if HOLLOW:
        #two thin walls per stroke with a faint glow between them, as on the original sign
        inner = shape.filter(ImageFilter.MinFilter(HOLLOW))
        walls = ImageChops.subtract(shape, inner)
        tube = Image.new("RGBA", shape.size, (255, 60, 190, 0))
        tube.putalpha(walls)
        middle = Image.new("RGBA", shape.size, (255, 25, 165, 0))
        middle.putalpha(inner.point(lambda v: v * 45 // 100))
        tube = Image.alpha_composite(middle, tube)
    else:
        hot = shape.filter(ImageFilter.MinFilter(CORE)).filter(ImageFilter.GaussianBlur(1))
        tube = Image.new("RGBA", shape.size, (255, 30, 185, 0))
        tube.putalpha(shape)
        core = Image.new("RGBA", shape.size, (255, 175, 245, 0))
        core.putalpha(hot)
        tube = Image.alpha_composite(tube, core)
    tube = tube.rotate(ANGLE, resample=Image.BICUBIC)
    tube = tube.crop(tube.getbbox())

    #wider than the badge, so a big wordmark can overhang it on both sides
    width = S * 2
    canvas = Image.new("RGBA", (width, S), (0, 0, 0, 0))
    canvas.paste(badge, ((width - S) // 2, 0))
    layer = Image.new("RGBA", (width, S), (0, 0, 0, 0))
    layer.paste(tube, ((width - tube.width) // 2 + 10, CY + LIFT - tube.height // 2), tube)
    for radius, times in ((30, 2), (12, 2), (5, 1)):
        halo = layer.filter(ImageFilter.GaussianBlur(radius))
        for _ in range(times):
            canvas = Image.alpha_composite(canvas, halo)
    canvas = Image.alpha_composite(canvas, layer)

    #square, with a little room so the glow isn't clipped
    canvas = canvas.crop(canvas.getbbox())
    side = max(canvas.size) + 40
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(canvas, ((side - canvas.width) // 2, (side - canvas.height) // 2))
    return square.resize((S, S), Image.LANCZOS)


def main():
    badge = draw_badge()
    badge.crop((CX - R, CY - R, CX + R + 1, CY + R + 1)).save(OUT / "badge.png", optimize=True)
    for name in sys.argv[1:]:
        draw_logo(badge, name).save(OUT / f"logo-{name}.png", optimize=True)
        print(f"wrote logo-{name}.png")


if __name__ == "__main__":
    main()
