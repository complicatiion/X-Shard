# Regenerates the Inno Setup wizard images (run from the repository root): python installer/wizard/make-wizard-images.py
# Needs Pillow. Inno Setup 6.5.2+ reads PNG (with transparency) for WizardImageFile and WizardSmallImageFile.
#  - wizard-small-<dpi>.png: the logo only, transparent background (header of the inner pages, white in Setup).
#  - wizard-large-<dpi>.png: the logo on the X-Shard green gradient, no text or version (welcome and finish pages).
from PIL import Image, ImageDraw, ImageFilter
GREEN_LOGO = 'src/XShard/Assets/app-logo-light.png'   # all-green logo: visible on the white wizard header
COLOR_LOGO = 'src/XShard/Assets/app-logo-color.png'   # green ring + white X: for the dark gradient
TOP, MID, BOT = (0x0F, 0x15, 0x11), (0x16, 0x2C, 0x1A), (0x2A, 0x7F, 0x36)

def lerp(a, b, t): return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))

def gradient(w, h):
    im = Image.new('RGB', (w, h)); px = im.load()
    for y in range(h):
        for x in range(w):
            t = min(1, max(0, (y / h) * 0.85 + (x / w) * 0.15))
            px[x, y] = lerp(TOP, MID, t / 0.5) if t < 0.5 else lerp(MID, BOT, (t - 0.5) / 0.5)
    glow = Image.new('L', (w, h), 0); d = ImageDraw.Draw(glow); r = int(w * 0.55); cx, cy = w // 2, int(h * 0.42)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=38); glow = glow.filter(ImageFilter.GaussianBlur(r * 0.45))
    return Image.composite(Image.new('RGB', (w, h), (0x6A, 0x8A, 0x70)), im, glow)

def large(scale, logo):
    w, h = round(164 * scale), round(314 * scale)
    im = gradient(w, h).convert('RGBA')
    s = int(w * 0.80); lg = logo.resize((s, s), Image.LANCZOS)
    shadow = Image.new('RGBA', lg.size, (0, 0, 0, 0)); shadow.putalpha(lg.getchannel('A').point(lambda a: int(a * 0.45)))
    shadow = shadow.filter(ImageFilter.GaussianBlur(max(2, s // 40)))
    x, y = (w - s) // 2, int(h * 0.42) - s // 2
    im.alpha_composite(shadow, (x, y + max(2, s // 50))); im.alpha_composite(lg, (x, y))
    return im.convert('RGB')

def small(scale, logo):
    n = round(55 * scale); pad = max(1, round(n * 0.04))
    im = Image.new('RGBA', (n, n), (0, 0, 0, 0)); lg = logo.resize((n - 2 * pad, n - 2 * pad), Image.LANCZOS)
    im.alpha_composite(lg, (pad, pad)); return im

if __name__ == '__main__':
    color, green = Image.open(COLOR_LOGO).convert('RGBA'), Image.open(GREEN_LOGO).convert('RGBA')
    for pct, sc in [(100, 1), (125, 1.25), (150, 1.5), (175, 1.75), (200, 2)]:
        large(sc, color).save(f'installer/wizard/wizard-large-{pct}.png', optimize=True)
        small(sc, green).save(f'installer/wizard/wizard-small-{pct}.png', optimize=True)
