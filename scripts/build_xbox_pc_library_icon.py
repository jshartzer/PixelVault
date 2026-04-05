"""One-off / regen: Xbox PC library header icon — solid sphere from Xbox logo + \"PC\" wordmark, tight margins, transparent background."""
from __future__ import annotations

import os
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[1]
REF_PATH = REPO / "assets" / "Xbox Library Logo.png"
OUT_PATH = REPO / "assets" / "Xbox PC Library Icon.png"


def pick_bold_font(size: int) -> ImageFont.FreeTypeFont:
    candidates = [
        os.environ.get("WINDIR", r"C:\Windows") + r"\Fonts\segoeuib.ttf",
        os.environ.get("WINDIR", r"C:\Windows") + r"\Fonts\arialbd.ttf",
        os.environ.get("WINDIR", r"C:\Windows") + r"\Fonts\calibrib.ttf",
    ]
    for path in candidates:
        if os.path.isfile(path):
            try:
                return ImageFont.truetype(path, size=size)
            except OSError:
                continue
    return ImageFont.load_default()


def main() -> None:
    ref = Image.open(REF_PATH).convert("RGBA")
    # Sphere-only crop (see gap at ~row 864 in alpha mask of reference asset).
    sphere = ref.crop((528, 0, 1392, 864))

    out_w, out_h = 1024, 682
    canvas = Image.new("RGBA", (out_w, out_h), (255, 255, 255, 0))

    # Fill most of the vertical space; match approximate Xbox + wordmark proportions.
    margin_top = 22
    margin_bottom = 28
    usable_h = out_h - margin_top - margin_bottom
    sphere_side = int(min(out_w * 0.82, usable_h * 0.68))
    sphere_r = sphere.resize((sphere_side, sphere_side), Image.Resampling.LANCZOS)

    x0 = (out_w - sphere_side) // 2
    canvas.alpha_composite(sphere_r, (x0, margin_top))

    label = "PC"
    # Taller than single-line default for short string so it balances the Xbox sphere.
    font_px = max(80, int(sphere_side * 0.38))
    font = pick_bold_font(font_px)
    draw = ImageDraw.Draw(canvas)
    bbox = draw.textbbox((0, 0), label, font=font)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    gap = max(18, int(sphere_side * 0.07))
    tx = (out_w - tw) // 2
    ty = margin_top + sphere_side + gap - bbox[1]
    draw.text((tx, ty), label, font=font, fill=(0, 0, 0, 255))

    a = canvas.split()[3]
    bbox = a.getbbox()
    if bbox:
        pad = 14
        tight = canvas.crop(
            (bbox[0] - pad, bbox[1] - pad, bbox[2] + pad, bbox[3] + pad)
        )
        tw, th = tight.size
        out_w, out_h = 1024, 682
        inner_w, inner_h = out_w - 24, out_h - 24
        scale = min(inner_w / tw, inner_h / th)
        nw, nh = max(1, int(tw * scale)), max(1, int(th * scale))
        scaled = tight.resize((nw, nh), Image.Resampling.LANCZOS)
        final = Image.new("RGBA", (out_w, out_h), (255, 255, 255, 0))
        ox = (out_w - nw) // 2
        oy = (out_h - nh) // 2
        final.alpha_composite(scaled, (ox, oy))
        canvas = final

    canvas.save(OUT_PATH, "PNG")
    print(f"Wrote {OUT_PATH}")


if __name__ == "__main__":
    main()
