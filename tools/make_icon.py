"""Generate assets/CoffeeCup.ico (the .exe / installer icon) and a preview strip.

The tray icon itself is drawn at runtime by MugRenderer.cs; this script uses the
same 32x32 design grid so both look alike. Run: python tools/make_icon.py
Requires Pillow.
"""
import math
import pathlib

from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
SS = 16  # supersampling factor

# Design grid is 32x32 units (same constants as MugRenderer.cs).
BODY_L, BODY_R, BODY_T, BODY_B, BODY_RADIUS = 4.0, 22.0, 12.0, 29.0, 4.5
HANDLE_CX, HANDLE_CY, HANDLE_R = 22.0, 19.5, 4.6
STROKE = 2.6
FULL_TOP = 14.6  # coffee surface when the mug is full


def render(size, level, steam_phase, outline, body_fill=None, coffee=(124, 74, 38, 255),
           crema=(201, 154, 98, 255), steam=True):
    px = size * SS
    k = px / 32.0
    img = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    s = STROKE * k

    def box(l, t, r, b):
        return [l * k, t * k, r * k, b * k]

    # Handle (drawn first so the body covers its inner half).
    d.ellipse(box(HANDLE_CX - HANDLE_R, HANDLE_CY - HANDLE_R, HANDLE_CX + HANDLE_R, HANDLE_CY + HANDLE_R),
              outline=outline, width=round(s))

    # Body interior (mask) and coffee.
    inner = box(BODY_L + STROKE / 2, BODY_T - 2, BODY_R - STROKE / 2, BODY_B - STROKE / 2)
    mask = Image.new("L", (px, px), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle(inner, radius=(BODY_RADIUS - STROKE / 2) * k, fill=255)
    md.rectangle([0, 0, px, BODY_T * k], fill=0)
    layer = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    if body_fill:
        ld.rectangle([0, 0, px, px], fill=body_fill)
    if level > 0:
        bottom = BODY_B - STROKE / 2
        top = bottom - (bottom - FULL_TOP) * level
        ld.rectangle([0, top * k, px, px], fill=coffee)
        ld.rectangle([0, top * k, px, (top + 1.4) * k], fill=crema)
    img.paste(layer, (0, 0), mask)
    # Wipe the handle's inner half that pokes into the body.
    d.rounded_rectangle(box(BODY_L, BODY_T - 6, BODY_R, BODY_B), radius=BODY_RADIUS * k, outline=outline, width=round(s))
    d.rectangle(box(BODY_L - 1, BODY_T - 7, BODY_R + 1, BODY_T - STROKE / 2 + 0.01), fill=(0, 0, 0, 0))
    # Rim line across the top.
    d.line([BODY_L * k - s / 2 + 1, (BODY_T - STROKE / 2 + STROKE / 2) * k,
            BODY_R * k + s / 2 - 1, (BODY_T - STROKE / 2 + STROKE / 2) * k], fill=outline, width=round(s))

    if steam and level > 0.05:
        a = int(255 * min(1.0, level * 1.4))
        col = outline[:3] + (min(a, outline[3]) * 3 // 4,)
        for i, x0 in enumerate((8.5, 13.0, 17.5)):
            pts = []
            for j in range(25):
                t = j / 24
                y = 9.2 - t * 8.0
                x = x0 + 1.0 * math.sin(t * 2 * math.pi + steam_phase + i * 0.8)
                pts.append((x * k, y * k))
            d.line(pts, fill=col, width=round(STROKE * 0.75 * k), joint="curve")
    return img.resize((size, size), Image.LANCZOS)


def main():
    dark_brown = (59, 36, 20, 255)
    cream = (250, 243, 232, 255)
    big = [render(sz, 1.0, 0.6, dark_brown, body_fill=cream) for sz in (256, 128, 64, 48, 32, 24, 16)]
    big[0].save(ROOT / "assets" / "CoffeeCup.ico", sizes=[(i.width, i.height) for i in big], append_images=big[1:])

    # Preview of tray states on dark and light taskbars, at 16, 24 and 32 px.
    states = [(0.0, 0), (0.35, 0), (0.7, 1), (1.0, 2), (1.0, 3), (0.5, 2), (0.15, 4)]
    rows = []
    for bg, fg in (((32, 32, 32, 255), (240, 240, 240, 255)), ((243, 243, 243, 255), (32, 32, 32, 255))):
        for sz in (16, 24, 32):
            row = Image.new("RGBA", ((sz + 8) * len(states), sz + 8), bg)
            for n, (lvl, ph) in enumerate(states):
                ic = render(sz, lvl, ph * 1.4, fg)
                row.alpha_composite(ic, (n * (sz + 8) + 4, 4))
            rows.append(row)
    w = max(r.width for r in rows)
    sheet = Image.new("RGBA", (w, sum(r.height for r in rows)), (128, 128, 128, 255))
    y = 0
    for r in rows:
        sheet.alpha_composite(r, (0, y))
        y += r.height
    sheet = sheet.resize((sheet.width * 4, sheet.height * 4), Image.NEAREST)
    sheet.save(ROOT / "assets" / "tray-preview.png")


if __name__ == "__main__":
    main()
