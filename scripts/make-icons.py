#!/usr/bin/env python3
"""Makes the app's icons from the masters in art/, which are the Mac app's (its Resources/AppIcon.png and MenuIcon.png).

- src/JaredsWindowsUtils/Assets/AppIcon.ico: the bubble pop sticker, cropped from the Mac's padded grid
- src/JaredsWindowsUtils/Assets/AppIcon.png: the same at 256 pixels, for the About window
- src/JaredsWindowsUtils/Assets/Tray/glyph-<size>.png: the J in shades, white on clear, a size per screen scale. The
  app tints it to suit the taskbar (TrayIcon.cs).
- docs/icon.png: for the README
- installer/wizard-*.bmp: the installer's pictures, the sticker on the menu's blue down the side of its first and last
  pages, and on white in the corner of the others, at 100% and 200% scale

Usage: python3 scripts/make-icons.py   (needs Pillow: pip install pillow)
"""
import os

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
ASSETS = os.path.join(ROOT, "src", "JaredsWindowsUtils", "Assets")
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
# The small icon at 100% to 300% scale
TRAY_SIZES = [16, 20, 24, 28, 32, 40, 48, 64]


def app_icon():
    master = Image.open(os.path.join(ROOT, "art", "AppIcon.png")).convert("RGBA")
    # The Mac grid's 824-pixel tile sits at 100..924 with its shadow below. Square around both, so it fills the icon.
    tile = master.crop((56, 64, 968, 976))
    frames = [tile.resize((size, size), Image.LANCZOS) for size in ICO_SIZES]
    frames[-1].save(os.path.join(ASSETS, "AppIcon.ico"), sizes=[(s, s) for s in ICO_SIZES], append_images=frames[:-1])
    frames[-1].save(os.path.join(ASSETS, "AppIcon.png"))
    frames[-1].save(os.path.join(ROOT, "docs", "icon.png"))


def installer_art():
    master = Image.open(os.path.join(ROOT, "art", "AppIcon.png")).convert("RGBA")
    tile = master.crop((56, 64, 968, 976))
    blue = (0x2B, 0x5E, 0xF0)
    for scale in (1, 2):
        # Down the side of the welcome and finish pages: 164 x 314 at 100%
        side = Image.new("RGB", (164 * scale, 314 * scale), blue)
        sticker = tile.resize((112 * scale, 112 * scale), Image.LANCZOS)
        side.paste(sticker, ((side.width - sticker.width) // 2, 64 * scale), sticker)
        side.save(os.path.join(ROOT, "installer", f"wizard-side-{scale * 100}.bmp"))
        # In the corner of the other pages: 55 x 55 at 100%, on the page's white
        corner = Image.new("RGB", (55 * scale, 55 * scale), (255, 255, 255))
        small = tile.resize((52 * scale, 52 * scale), Image.LANCZOS)
        corner.paste(small, ((corner.width - small.width) // 2, (corner.height - small.height) // 2), small)
        corner.save(os.path.join(ROOT, "installer", f"wizard-corner-{scale * 100}.bmp"))


def tray_glyphs():
    master = Image.open(os.path.join(ROOT, "art", "MenuIcon.png")).convert("RGBA")
    alpha = master.getchannel("A")
    for size in TRAY_SIZES:
        # A pixel's margin at 16, so it lines up with the other tray icons
        inner = round(size * 15 / 16)
        scaled = alpha.resize((inner, inner), Image.LANCZOS)
        mask = Image.new("L", (size, size), 0)
        offset = (size - inner) // 2
        mask.paste(scaled, (offset, offset))
        glyph = Image.new("RGBA", (size, size), (255, 255, 255, 0))
        glyph.putalpha(mask)
        glyph.save(os.path.join(ASSETS, "Tray", f"glyph-{size}.png"))


if __name__ == "__main__":
    os.makedirs(os.path.join(ASSETS, "Tray"), exist_ok=True)
    os.makedirs(os.path.join(ROOT, "docs"), exist_ok=True)
    os.makedirs(os.path.join(ROOT, "installer"), exist_ok=True)
    app_icon()
    tray_glyphs()
    installer_art()
    print("Wrote the app icon, the tray glyphs, the installer's pictures and docs/icon.png")
