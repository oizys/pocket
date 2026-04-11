"""
Blind describer stage of the 3-blind validation pipeline.

Reads manifest.csv (image_filename, prompt_text) and, for each row, spawns an
isolated Claude CLI invocation that can ONLY use the Read tool, pointed at the
metadata-stripped copy of the image. The describer does not see the original
prompt — it produces a purely visual description.

Outputs descriptions.tsv with columns:
    image_filename \\t prompt_text \\t description

Usage:
    python describe.py
    python describe.py --blind-dir output_blind --manifest manifest.csv --out descriptions.tsv
"""
import argparse
import csv
import json
import shutil
import subprocess
import sys
from pathlib import Path

SYSTEM_PROMPT = (
    "You are a blind image describer. Describe what you see in the image "
    "literally in 1-2 sentences. Visual content only: name objects, colors, "
    "composition, and style. Do NOT attempt to extract or read image metadata. "
    "Do NOT speculate about intent. Output only the description, no preamble."
)

# Dedicated empty working directory so Claude's project memory loads as empty
# (project memory is keyed off cwd). We don't use --bare because it breaks
# Claude Max subscription auth.
BLIND_WORKDIR = Path(__file__).parent / "_blind_workdir"


def find_claude() -> str:
    exe = shutil.which("claude")
    if not exe:
        print("Error: 'claude' CLI not found in PATH.", file=sys.stderr)
        sys.exit(1)
    return exe


def describe_one(claude_exe: str, image_path: Path, timeout: float = 180.0) -> str:
    """Spawn an isolated Claude CLI, return its single-sentence description."""
    # Use absolute path in @file reference since cwd is the empty workdir
    user_msg = f"Read @{image_path.resolve().as_posix()} and describe what you see."
    cmd = [
        claude_exe,
        "-p",
        "--no-session-persistence",
        "--tools", "Read",
        "--append-system-prompt", SYSTEM_PROMPT,
        "--output-format", "json",
        user_msg,
    ]
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            encoding="utf-8",
            cwd=str(BLIND_WORKDIR),
        )
    except subprocess.TimeoutExpired:
        return "ERROR: timeout"

    if result.returncode != 0:
        stderr = (result.stderr or "").strip().replace("\n", " ")[:200]
        return f"ERROR: exit {result.returncode}: {stderr}"

    try:
        data = json.loads(result.stdout)
    except json.JSONDecodeError:
        return f"ERROR: non-JSON stdout: {result.stdout[:120]}"

    text = (data.get("result") or "").strip()
    # Flatten any newlines/tabs so we can stay in TSV
    return text.replace("\n", " ").replace("\t", " ")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--blind-dir", default="output_blind",
                        help="Directory containing metadata-stripped PNGs")
    parser.add_argument("--manifest", default="manifest.csv",
                        help="CSV from batch_prompts.py")
    parser.add_argument("--out", default="descriptions.tsv",
                        help="Output TSV: image, prompt, description")
    args = parser.parse_args()

    blind_dir = Path(args.blind_dir)
    manifest = Path(args.manifest)
    out = Path(args.out)

    if not manifest.exists():
        print(f"Manifest not found: {manifest}"); sys.exit(1)
    if not blind_dir.exists():
        print(f"Blind dir not found: {blind_dir} — run strip_metadata.py first"); sys.exit(1)

    BLIND_WORKDIR.mkdir(exist_ok=True)
    claude_exe = find_claude()

    with manifest.open(encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))

    print(f"Describing {len(rows)} images via isolated Claude CLIs...")
    with out.open("w", encoding="utf-8") as outf:
        outf.write("image_filename\tprompt_text\tdescription\n")
        for i, row in enumerate(rows, 1):
            img_name = row["image_filename"]
            img_path = blind_dir / img_name
            if not img_path.exists():
                print(f"  [{i}/{len(rows)}] SKIP (missing in blind dir): {img_name}")
                continue

            desc = describe_one(claude_exe, img_path)
            # Flatten the original prompt too so it stays on one TSV line
            prompt_flat = row["prompt_text"].replace("\n", " ").replace("\t", " ")
            outf.write(f"{img_name}\t{prompt_flat}\t{desc}\n")
            outf.flush()

            short = desc[:70] + ("..." if len(desc) > 70 else "")
            print(f"  [{i}/{len(rows)}] {img_name}: {short}")

    print(f"\nWrote {out}")


if __name__ == "__main__":
    main()
