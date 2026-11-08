"""Give each item icon render the shared frame.

Each render comes with its own gold frame (different widths, rounded or square, some with white
corners). This crops it to the art inside its frame and puts it under one shared ring cut from
tools/panel_frame.png (the games' panel frame).

Reads tools/drafts/<base>.png (all of them, or those named) and writes icons/<base>.png.

    python tools/frame_icons.py [base ...] [--size 256]
"""
import argparse
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
DRAFTS = ROOT / "tools" / "drafts"
ICONS = ROOT / "icons"
FRAME = ROOT / "tools" / "panel_frame.png"
BORDER = 12 / 256   # panel_frame's gold band, as a share of its width
CORNER = 34 / 256   # the corner gems reach this far in
# Renders whose art fools the frame finder into cutting too deep (a pale or metal rim reaching the
# render's own frame reads as more frame): how many frames to peel at most.
MAX_PEELS = {"silver_ward": 1, "reed_parma": 1, "ghostwood_parma": 1, "voidsteel_parma": 1}


def is_frame(p):
    """Anything that is not the purple/indigo backdrop inside the frame."""
    r, g, b = p[:3]
    return not (b >= r and b > g + 15 and b > 40 and min(r, g, b) < 200)


def inset(im, side):
    """How far the render's own frame reaches in from one side."""
    w, h = im.size
    px = im.load()
    deepest = 0
    for t in (0.3, 0.4, 0.5, 0.6, 0.7):
        run, seen = 0, False   # some frames sit inset, with backdrop outside them
        for d in range(int(w * 0.25)):
            x, y = {"l": (d, int(h * t)), "r": (w - 1 - d, int(h * t)),
                    "t": (int(w * t), d), "b": (int(w * t), h - 1 - d)}[side]
            if is_frame(px[x, y]):
                run, seen = 0, True
            elif seen:
                run += 1
            if run >= 6:   # six inside pixels in a row: past the frame
                deepest = max(deepest, d - 5)
                break
    return deepest


def ring(size):
    """panel_frame cut down to its gold band and corner gems."""
    src = Image.open(FRAME).convert("RGBA").resize((size, size), Image.LANCZOS)
    b, c = round(size * BORDER), round(size * CORNER)
    inside = src.getpixel((size // 2, size // 2))
    mask = Image.new("L", (size, size), 255)
    ImageDraw.Draw(mask).rectangle((b, b, size - 1 - b, size - 1 - b), fill=0)
    # Keep the gem pixels that poke inside the band at each corner.
    diff = ImageChops.difference(src.convert("RGB"), Image.new("RGB", src.size, inside[:3])).convert("L")
    gems = diff.point(lambda v: 255 if v > 40 else 0)
    for x0, y0 in ((0, 0), (size - c, 0), (0, size - c), (size - c, size - c)):
        box = (x0, y0, x0 + c, y0 + c)
        mask.paste(ImageChops.lighter(mask.crop(box), gems.crop(box)), box[:2])
    src.putalpha(ImageChops.multiply(mask, src.getchannel("A")))
    return src, b


def frame_icons(bases=(), size=256):
    """Frames the drafts of <bases> (every draft when empty) into icons/."""
    frame, b = ring(size)
    ICONS.mkdir(exist_ok=True)
    paths = [DRAFTS / f"{base}.png" for base in bases] if bases else sorted(DRAFTS.glob("*.png"))
    for path in paths:
        art = Image.open(path).convert("RGB")
        # Some renders nest a thin inner frame inside the outer one, so keep peeling while every
        # side still finds a frame to cut. A real frame is about as wide on every side; pale art
        # reads as frame on one or two sides only.
        for peel in range(MAX_PEELS.get(path.stem, 3)):
            w, h = art.size
            sides = [inset(art, s) for s in "ltrb"]
            if peel == 0 and max(sides) - min(sides) > 16:
                sides = [sorted(sides)[1]] * 4   # the outer frame is always there
            elif min(sides) < 3 or max(sides) - min(sides) > 16:
                break
            pad = 4   # a little extra so no sliver of the old frame survives
            l, t, r, b_ = (s + pad for s in sides)
            art = art.crop((l, t, w - r, h - b_))
        art = art.resize((size - 2 * b + 2, size - 2 * b + 2), Image.LANCZOS)
        icon = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        icon.paste(art, (b - 1, b - 1))
        icon.alpha_composite(frame)
        icon.save(ICONS / path.name, optimize=True)
        print(path.stem, "framed", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("bases", nargs="*")
    parser.add_argument("--size", type=int, default=256)
    args = parser.parse_args()
    frame_icons(args.bases, args.size)
