"""Generate 🚌 bus emoji + cool teal/black Bus theme icon assets for Bustrap.

Pillow's emoji support is unreliable on Windows, so the bus is drawn as
vector shapes (rounded body, windows, wheels). This guarantees a recognizable
yellow school bus on every machine.

Outputs to C:/Users/Te Aroha/Bustrap/Images/:
  - Bustrap.png           512x512 main app icon (rounded teal square + bus)
  - Bustrap.ico           multi-size ICO (16,32,48,64,128,256) for ApplicationIcon
  - BustrapTheme.png      1024x576 theme preview banner (teal/black gradient + bus)
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT_DIR = r"C:\Users\Te Aroha\Bustrap\Images"
os.makedirs(OUT_DIR, exist_ok=True)

# Cool teal + black palette
TEAL_BRIGHT = (38, 220, 199)    # #26DCC7
TEAL_DEEP   = (8, 92, 110)      # #085C6E
TEAL_GLOW   = (90, 245, 220)    # #5AF5DC
BLACK       = (8, 12, 18)       # #080C12
BLACK_SOFT  = (18, 26, 34)      # #121A22
GRID_LINE   = (34, 220, 195, 60)

# School bus yellow
BUS_BODY     = (255, 196, 32)
BUS_BODY_DK  = (214, 156, 12)
BUS_OUTLINE  = (28, 24, 16)
BUS_WINDOW   = (165, 220, 235)
WHEEL_TIRE   = (24, 24, 28)
WHEEL_HUB    = (170, 178, 190)
LAMP         = (255, 230, 130)


# --- Helpers -----------------------------------------------------------------

def find_font(*candidates: str):
    for c in candidates:
        if c and os.path.exists(c):
            return c
    return None


def rounded_mask(size: int, radius_ratio: float = 0.22) -> Image.Image:
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle((0, 0, size - 1, size - 1), radius=int(size * radius_ratio), fill=255)
    return m


def radial_glow(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    cx, cy = size / 2, size * 0.45
    max_r = size * 0.75
    for y in range(size):
        for x in range(size):
            dx, dy = x - cx, y - cy
            d = math.hypot(dx, dy)
            t = max(0.0, 1 - d / max_r)
            t = t ** 1.8
            r = int(BLACK[0] * (1 - t) + TEAL_DEEP[0] * t)
            g = int(BLACK[1] * (1 - t) + TEAL_DEEP[1] * t)
            b = int(BLACK[2] * (1 - t) + TEAL_DEEP[2] * t)
            img.putpixel((x, y), (r, g, b, 255))
    return img


def draw_grid_layer(size: int, spacing: int = 32) -> Image.Image:
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for i in range(0, size + spacing, spacing):
        d.line([(i, 0), (i - size, size)], fill=GRID_LINE, width=1)
        d.line([(i, 0), (i + size, size)], fill=GRID_LINE, width=1)
    return layer.filter(ImageFilter.GaussianBlur(0.6))


def draw_bus(canvas: Image.Image, cx: int, cy: int, scale: float) -> None:
    """Draw a vector school bus centered at (cx, cy). scale=1.0 ~= 256px wide."""
    s = scale
    body_w = int(260 * s)
    body_h = int(150 * s)
    body_left = cx - body_w // 2
    body_top  = cy - body_h // 2

    # Soft teal glow under the bus
    glow = Image.new("RGBA", (body_w + int(80 * s), body_h + int(80 * s)), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse((0, 0, glow.width - 1, glow.height - 1), fill=(*TEAL_GLOW, 90))
    glow = glow.filter(ImageFilter.GaussianBlur(int(20 * s)))
    canvas.alpha_composite(glow, (body_left - int(40 * s), body_top - int(40 * s)))

    # Drop shadow under the chassis
    shadow = Image.new("RGBA", (body_w + int(20 * s), int(40 * s)), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.ellipse((0, 0, shadow.width - 1, shadow.height - 1), fill=(0, 0, 0, 130))
    shadow = shadow.filter(ImageFilter.GaussianBlur(int(8 * s)))
    canvas.alpha_composite(shadow, (body_left - int(10 * s), body_top + body_h - int(10 * s)))

    # Bus body
    body_layer = Image.new("RGBA", (body_w + int(40 * s), body_h + int(40 * s)), (0, 0, 0, 0))
    bd = ImageDraw.Draw(body_layer)
    bd.rounded_rectangle(
        (0, 0, body_w, body_h),
        radius=int(28 * s),
        fill=BUS_BODY,
        outline=BUS_OUTLINE,
        width=max(2, int(3 * s)),
    )

    # Hood (front of the bus)
    hood_w = int(70 * s)
    hood_h = int(90 * s)
    hood_top = body_h - hood_h
    bd.rounded_rectangle(
        (0, hood_top, hood_w, body_h),
        radius=int(18 * s),
        fill=BUS_BODY,
        outline=BUS_OUTLINE,
        width=max(2, int(3 * s)),
    )
    # Bottom chassis strip
    bd.rectangle(
        (0, body_h - int(14 * s), body_w, body_h),
        fill=BUS_BODY_DK,
    )

    # Windows across the main body
    win_y = int(20 * s)
    win_h = int(55 * s)
    win_count = 4
    win_pad = int(10 * s)
    win_area_left = hood_w + int(10 * s)
    win_area_w = body_w - win_area_left - int(14 * s)
    win_w = (win_area_w - win_pad * (win_count - 1)) // win_count
    for i in range(win_count):
        x0 = win_area_left + i * (win_w + win_pad)
        bd.rounded_rectangle(
            (x0, win_y, x0 + win_w, win_y + win_h),
            radius=int(8 * s),
            fill=BUS_WINDOW,
            outline=BUS_OUTLINE,
            width=max(1, int(2 * s)),
        )
        bd.rectangle(
            (x0 + int(6 * s), win_y + int(6 * s), x0 + win_w - int(6 * s), win_y + int(14 * s)),
            fill=(220, 240, 250, 180),
        )

    # Driver window
    bd.rounded_rectangle(
        (int(8 * s), hood_top + int(10 * s), hood_w - int(8 * s), hood_top + int(50 * s)),
        radius=int(6 * s),
        fill=BUS_WINDOW,
        outline=BUS_OUTLINE,
        width=max(1, int(2 * s)),
    )

    # Headlight
    bd.ellipse(
        (int(8 * s), body_h - int(38 * s), int(28 * s), body_h - int(18 * s)),
        fill=LAMP, outline=BUS_OUTLINE, width=max(1, int(2 * s)),
    )

    # Door
    door_x0 = body_w - int(38 * s)
    bd.rounded_rectangle(
        (door_x0, int(60 * s), door_x0 + int(28 * s), body_h - int(2 * s)),
        radius=int(6 * s),
        fill=BUS_BODY_DK,
        outline=BUS_OUTLINE,
        width=max(1, int(2 * s)),
    )
    bd.line(
        (door_x0 + int(14 * s), int(66 * s), door_x0 + int(14 * s), body_h - int(4 * s)),
        fill=BUS_OUTLINE, width=max(1, int(2 * s)),
    )

    canvas.alpha_composite(body_layer, (body_left - int(20 * s), body_top - int(20 * s)))

    # Wheels
    wheel_r = int(26 * s)
    wheel_y = body_top + body_h - int(2 * s)
    for wx in (body_left + int(45 * s), body_left + body_w - int(55 * s)):
        wd = ImageDraw.Draw(canvas)
        wd.ellipse(
            (wx - wheel_r, wheel_y - wheel_r, wx + wheel_r, wheel_y + wheel_r),
            fill=WHEEL_TIRE, outline=BUS_OUTLINE, width=max(1, int(3 * s)),
        )
        hub_r = int(10 * s)
        wd.ellipse(
            (wx - hub_r, wheel_y - hub_r, wx + hub_r, wheel_y + hub_r),
            fill=WHEEL_HUB, outline=BUS_OUTLINE, width=max(1, int(2 * s)),
        )


# --- Main app icon (square) --------------------------------------------------

def make_app_icon(size: int = 512) -> Image.Image:
    canvas = radial_glow(size)
    canvas.alpha_composite(draw_grid_layer(size, spacing=max(24, size // 16)))
    draw_bus(canvas, size // 2, int(size * 0.5), size / 512 * 1.05)

    mask = rounded_mask(size, radius_ratio=0.22)
    canvas.putalpha(mask)

    stroke = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sd = ImageDraw.Draw(stroke)
    sd.rounded_rectangle((0, 0, size - 1, size - 1),
                         radius=int(size * 0.22),
                         outline=(*TEAL_BRIGHT, 255), width=max(2, size // 128))
    canvas.alpha_composite(stroke)
    return canvas


def make_ico(path: str, sizes=(16, 32, 48, 64, 128, 256)) -> None:
    base = make_app_icon(512)
    base.save(path, format="ICO", sizes=[(s, s) for s in sizes])


# --- Theme preview banner ----------------------------------------------------

def make_theme_banner(width: int = 1024, height: int = 576) -> Image.Image:
    img = Image.new("RGBA", (width, height), BLACK + (255,))
    d = ImageDraw.Draw(img)

    for y in range(height):
        t = y / height
        r = int(TEAL_DEEP[0] * (1 - t) + BLACK[0] * t)
        g = int(TEAL_DEEP[1] * (1 - t) + BLACK[1] * t)
        b = int(TEAL_DEEP[2] * (1 - t) + BLACK[2] * t)
        d.line([(0, y), (width, y)], fill=(r, g, b, 255))

    glow = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse((0, 0, int(width * 0.55), height),
               fill=(*TEAL_BRIGHT, 90))
    glow = glow.filter(ImageFilter.GaussianBlur(60))
    img.alpha_composite(glow)

    grid = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    gd2 = ImageDraw.Draw(grid)
    horizon = int(height * 0.6)
    for i in range(-width, width * 2, 40):
        gd2.line([(i, height), (width // 2 + (i - width // 2) // 4, horizon)],
                 fill=(*TEAL_BRIGHT, 70), width=1)
    for j in range(0, horizon + 1, 36):
        gd2.line([(0, j), (width, j)], fill=(*TEAL_BRIGHT, 50), width=1)
    grid = grid.filter(ImageFilter.GaussianBlur(0.4))
    img.alpha_composite(grid)

    draw_bus(img, int(width * 0.78), int(height * 0.5), height / 512 * 1.0)

    wordmark_font_path = find_font(
        r"C:\Windows\Fonts\segoeuib.ttf",
        r"C:\Windows\Fonts\arialbd.ttf",
        r"C:\Windows\Fonts\seguisb.ttf",
    )
    sub_font_path = find_font(
        r"C:\Windows\Fonts\segoeui.ttf",
        r"C:\Windows\Fonts\arial.ttf",
    )
    wordmark_font = (ImageFont.truetype(wordmark_font_path, int(height * 0.24))
                     if wordmark_font_path else ImageFont.load_default())
    sub_font = (ImageFont.truetype(sub_font_path, int(height * 0.07))
                if sub_font_path else ImageFont.load_default())

    d2 = ImageDraw.Draw(img)
    d2.text((int(width * 0.06), int(height * 0.30)), "Bustrap",
            font=wordmark_font, fill=(*TEAL_GLOW, 255))
    d2.text((int(width * 0.06), int(height * 0.58)),
            "cool teal + black theme",
            font=sub_font, fill=(200, 240, 235, 230))
    return img


if __name__ == "__main__":
    app_png  = os.path.join(OUT_DIR, "Bustrap.png")
    app_ico  = os.path.join(OUT_DIR, "Bustrap.ico")
    theme_png = os.path.join(OUT_DIR, "BustrapTheme.png")

    make_app_icon(512).save(app_png, format="PNG")
    print("wrote", app_png)

    make_ico(app_ico)
    print("wrote", app_ico)

    make_theme_banner(1024, 576).save(theme_png, format="PNG")
    print("wrote", theme_png)
