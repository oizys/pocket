"""
Copy PNGs from a source dir to a destination dir with ALL embedded metadata
stripped (no tEXt/iTXt chunks, no ComfyUI workflow, no prompt). Used to feed
images into the blind describer stage without leaking the original prompt.

Usage:
    python strip_metadata.py <src_dir> <dst_dir>
"""
import sys
from pathlib import Path
from PIL import Image


def main():
    if len(sys.argv) != 3:
        print("Usage: python strip_metadata.py <src_dir> <dst_dir>")
        sys.exit(1)

    src = Path(sys.argv[1])
    dst = Path(sys.argv[2])
    if not src.exists():
        print(f"Source not found: {src}")
        sys.exit(1)
    dst.mkdir(parents=True, exist_ok=True)

    count = 0
    for png in src.glob("*.png"):
        img = Image.open(png)
        # Create a fresh image with the same pixel data but no info dict.
        clean = Image.new(img.mode, img.size)
        clean.putdata(list(img.getdata()))
        # pnginfo=None and no info= means no text chunks get written
        clean.save(dst / png.name, format="PNG")
        count += 1

    print(f"Stripped metadata from {count} PNG files -> {dst}")


if __name__ == "__main__":
    main()
