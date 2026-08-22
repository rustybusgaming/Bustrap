"""Generate the Bustrap app icon: a bus driving through a tilted square frame.

The art is drawn as vector shapes at 4x and downsampled, so it stays crisp at
every size instead of being one bitmap scaled around. Sizes at or below 32px
get a deliberately simplified variant - the full drawing turns to mush once a
window is less than two pixels wide.

Run from anywhere:  python Scripts/generate_bus_assets.py

Writes, relative to the repository root:
  Bustrap/Bustrap.png    512x512 icon used by the WPF UI
  Bustrap/Bustrap.ico    multi-size ICO used for the executable and windows
  Images/Bustrap.png     copy for the README and website
  Images/Bustrap.ico     copy
"""
from __future__ import annotations

import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter

SS = 4                                  # supersampling factor
S = 1024                                # master size in logical units
ANGLE = -15                             # frame tilt

TILE_TOP = (18, 58, 68)
TILE_BOT = (7, 24, 30)
TILE_EDGE = (94, 232, 214)
FRAME = (238, 251, 249)
FRAME_SHADE = (176, 214, 210)
BUS_BODY = (255, 196, 26)
BUS_SHADE = (233, 160, 12)
BUS_LINE = (26, 43, 51)
GLASS = (183, 231, 245)
WHEEL = (30, 34, 40)
HUB = (206, 214, 222)
LAMP = (255, 244, 205)

ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
SIMPLIFY_AT = 32                        # this size and below use the bold cut


def px(v: float) -> int:
    return int(round(v * SS))


def _layer() -> Image.Image:
    return Image.new("RGBA", (S * SS, S * SS), (0, 0, 0, 0))


def _vertical_gradient(top, bottom) -> Image.Image:
    strip = Image.new("RGB", (1, S * SS))
    for y in range(S * SS):
        t = y / (S * SS - 1)
        strip.putpixel((0, y), tuple(
            round(a + (b - a) * t) for a, b in zip(top, bottom)))
    return strip.resize((S * SS, S * SS)).convert("RGBA")


def tile(simple: bool) -> Image.Image:
    """Dark rounded app tile with a thin mint edge."""
    mask = Image.new("L", (S * SS, S * SS), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, S * SS - 1, S * SS - 1], radius=px(0.223 * S), fill=255)

    layer = _layer()
    layer.paste(_vertical_gradient(TILE_TOP, TILE_BOT), (0, 0), mask)

    # at icon sizes the edge is a fraction of a pixel and only muddies the
    # silhouette, so it is dropped from the simplified cut
    if simple:
        return layer

    edge = _layer()
    ImageDraw.Draw(edge).rounded_rectangle(
        [px(6), px(6), px(S - 6) - 1, px(S - 6) - 1],
        radius=px(0.21 * S), outline=TILE_EDGE + (170,), width=px(7))
    return Image.alpha_composite(layer, edge)


def frame(simple: bool):
    """Tilted rounded square with a square hole.

    Returns the whole frame, the near bar on its own, and the far side. The
    near bar gets drawn back over the bus so the nose comes out in front of
    the frame while the tail passes behind it.
    """
    outer = (0.64 if simple else 0.60) * S
    hole = (0.58 if simple else 0.60) * outer

    layer = _layer()
    d = ImageDraw.Draw(layer)

    x0 = (S - outer) / 2
    d.rounded_rectangle([px(x0), px(x0), px(x0 + outer), px(x0 + outer)],
                        radius=px(0.22 * outer), fill=(255, 255, 255, 255))

    if not simple:
        layer = Image.composite(_vertical_gradient(FRAME, FRAME_SHADE),
                                layer, layer.getchannel("A"))
        d = ImageDraw.Draw(layer)
    else:
        layer = Image.composite(Image.new("RGBA", layer.size, FRAME + (255,)),
                                layer, layer.getchannel("A"))
        d = ImageDraw.Draw(layer)

    h0, h1 = (S - hole) / 2, (S + hole) / 2
    d.rounded_rectangle([px(h0), px(h0), px(h1), px(h1)],
                        radius=px(0.24 * hole), fill=(0, 0, 0, 0))

    # cut in the frame's own frame of reference, so the seam follows the tilt
    # instead of slicing straight down the screen
    band = Image.new("L", layer.size, 0)
    ImageDraw.Draw(band).rectangle([0, 0, px(h0), layer.size[1]], fill=255)

    full = layer.rotate(ANGLE, resample=Image.BICUBIC)
    # hard edge: a feathered cut lets the drop shadow bleed through the seam
    band = band.rotate(ANGLE, resample=Image.BICUBIC).point(
        lambda v: 255 if v >= 128 else 0)

    near = full.copy()
    near.putalpha(Image.composite(full.getchannel("A"),
                                  Image.new("L", full.size, 0), band))
    far = ImageChops.subtract(full.getchannel("A"), band)
    return full, near, far


def bus(simple: bool) -> Image.Image:
    """Side profile facing right."""
    layer = _layer()
    d = ImageDraw.Draw(layer)

    if simple:
        bw, bh, bx, by = 0.88 * S, 0.28 * S, 0.06 * S, 0.36 * S
        line = px(0.030 * S)
        windows, ww, gap, lead = 2, 0.150 * S, 0.045 * S, 0.075 * S
    else:
        bw, bh, bx, by = 0.82 * S, 0.24 * S, 0.09 * S, 0.38 * S
        line = px(0.020 * S)
        windows, ww, gap, lead = 4, 0.115 * S, 0.028 * S, 0.085 * S

    body = [px(bx), px(by), px(bx + bw), px(by + bh)]
    radius = px(0.30 * bh)

    d.rounded_rectangle(body, radius=radius, fill=BUS_BODY,
                        outline=BUS_LINE, width=line)
    d.rounded_rectangle([px(bx + 0.02 * S), px(by + bh * 0.70),
                         px(bx + bw - 0.02 * S), px(by + bh)],
                        radius=px(0.10 * bh), fill=BUS_SHADE)
    d.rounded_rectangle(body, radius=radius, outline=BUS_LINE, width=line)

    wy0, wy1 = by + 0.20 * bh, by + 0.58 * bh
    wx = bx + lead
    for _ in range(windows):
        d.rounded_rectangle([px(wx), px(wy0), px(wx + ww), px(wy1)],
                            radius=px(0.030 * S), fill=GLASS,
                            outline=None if simple else BUS_LINE,
                            width=px(0.010 * S))
        wx += ww + gap

    d.rounded_rectangle([px(wx + 0.010 * S), px(wy0),
                         px(bx + bw - 0.045 * S), px(wy1 + 0.02 * S)],
                        radius=px(0.032 * S), fill=GLASS,
                        outline=None if simple else BUS_LINE,
                        width=px(0.010 * S))

    if not simple:
        lr = 0.026 * S
        lx, ly = bx + bw - 0.045 * S, by + bh * 0.76
        d.ellipse([px(lx - lr), px(ly - lr), px(lx + lr), px(ly + lr)],
                  fill=LAMP, outline=BUS_LINE, width=px(0.008 * S))

    r = (0.080 if simple else 0.070) * S
    cy = by + bh + 0.010 * S
    for cx in (bx + 0.18 * S, bx + bw - 0.22 * S):
        d.ellipse([px(cx - r), px(cy - r), px(cx + r), px(cy + r)],
                  fill=WHEEL, outline=BUS_LINE, width=line)
        if not simple:
            hr = r * 0.40
            d.ellipse([px(cx - hr), px(cy - hr), px(cx + hr), px(cy + hr)],
                      fill=HUB)

    return layer


def compose(simple: bool = False) -> Image.Image:
    full, near, far = frame(simple)

    art = Image.alpha_composite(tile(simple), full)
    art = Image.alpha_composite(art, bus(simple))

    if not simple:
        # soft shadow cast by the near bar onto the bus, masked off the far
        # side of the frame where it would only show the cut as a smudge
        drop = near.getchannel("A").filter(
            ImageFilter.GaussianBlur(px(0.016 * S)))
        drop = ImageChops.offset(drop, px(0.014 * S), px(0.014 * S))
        drop = ImageChops.multiply(drop, ImageChops.invert(far))
        drop = drop.point(lambda v: int(v * 0.45))
        shadow = Image.new("RGBA", near.size, (0, 0, 0, 0))
        shadow.paste((4, 12, 16, 255), (0, 0), drop)
        art = Image.alpha_composite(art, shadow)

    return Image.alpha_composite(art, near)


def render(size: int) -> Image.Image:
    return compose(simple=size <= SIMPLIFY_AT).resize((size, size), Image.LANCZOS)


def main() -> None:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    detailed = compose(simple=False)
    frames = [render(s) for s in ICO_SIZES]

    for directory in ("Bustrap", "Images"):
        out = os.path.join(root, directory)
        os.makedirs(out, exist_ok=True)

        detailed.resize((512, 512), Image.LANCZOS).save(
            os.path.join(out, "Bustrap.png"))
        # Pillow rebuilds every entry from the largest frame, which throws away
        # the simplified small ones, so hand it the frames it should keep
        frames[-1].save(os.path.join(out, "Bustrap.ico"),
                        format="ICO",
                        sizes=[(s, s) for s in ICO_SIZES],
                        append_images=frames[:-1])
        print("wrote", os.path.join(out, "Bustrap.png"),
              "and", os.path.join(out, "Bustrap.ico"))


if __name__ == "__main__":
    main()
