"""
Copy PNGs from a source dir to a destination dir with ALL embedded metadata
stripped (no tEXt/iTXt chunks, no ComfyUI workflow, no prompt). Used to feed
images into the blind describer stage without leaking the original prompt.

Usage:
    python strip_metadata.py                        # src=work/images, dst=output_blind
    python strip_metadata.py <src_dir> <dst_dir>    # explicit override
"""
import argparse
import sys
from pathlib import Path
from PIL import Image


HERE = Path(__file__).parent
DEFAULT_SRC = HERE / "work" / "images"
DEFAULT_DST = HERE / "output_blind"


def main():
    parser = argparse.ArgumentParser(description="Strip PNG metadata (tEXt/iTXt chunks).")
    parser.add_argument("src", nargs="?", default=str(DEFAULT_SRC),
                        help=f"Source directory of PNGs. Default: {DEFAULT_SRC}")
    parser.add_argument("dst", nargs="?", default=str(DEFAULT_DST),
                        help=f"Destination directory for stripped copies. Default: {DEFAULT_DST}")
    args = parser.parse_args()

    src = Path(args.src)
    dst = Path(args.dst)
    if not src.exists():
        print(f"Source not found: {src}")
        sys.exit(1)
    dst.mkdir(parents=True, exist_ok=True)

    count = 0
    for png in src.glob("*.png"):
        img = Image.open(png)
        # Paste the source pixels onto a fresh blank canvas. The new image
        # has an empty info dict, so no tEXt/iTXt chunks get carried over
        # when we save — the visual content is identical, the metadata is
        # gone. (Avoids the deprecated getdata/putdata round-trip.)
        clean = Image.new(img.mode, img.size)
        clean.paste(img)
        clean.save(dst / png.name, format="PNG")
        count += 1

    print(f"Stripped metadata from {count} PNG files  {src} -> {dst}")


if __name__ == "__main__":
    main()
