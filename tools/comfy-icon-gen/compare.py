"""
Blind comparator stage of the 3-blind validation pipeline.

Reads descriptions.tsv (image, intended_prompt, observed_description) and for
each pair spawns an isolated Claude CLI with NO tools (pure text in/out) to
score the alignment. The comparator has no project context, no memory, no file
access — just the two strings.

Outputs scores.tsv with columns:
    image_filename \\t score \\t verdict \\t reason \\t prompt_text

Usage:
    python compare.py
    python compare.py --in descriptions.tsv --out scores.tsv
"""
import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

SYSTEM_PROMPT = (
    "You score how well an observed image description matches an intended "
    "prompt for a game asset icon. Rate on a 0-10 integer scale where 10 is "
    "a near-perfect match and 0 is unrelated. Respond ONLY with a JSON object "
    "on a single line, no code fences, no prose: "
    '{"score": <0-10>, "verdict": "match|partial|miss", "reason": "<brief>"}'
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


def score_one(claude_exe: str, intended: str, observed: str, timeout: float = 120.0) -> dict:
    user_msg = f"INTENDED: {intended}\n\nOBSERVED: {observed}"
    cmd = [
        claude_exe,
        "-p",
        "--no-session-persistence",
        "--tools", "",
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
        return {"score": -1, "verdict": "error", "reason": "timeout"}

    if result.returncode != 0:
        stderr = (result.stderr or "").strip().replace("\n", " ")[:200]
        return {"score": -1, "verdict": "error", "reason": f"exit {result.returncode}: {stderr}"}

    try:
        outer = json.loads(result.stdout)
    except json.JSONDecodeError:
        return {"score": -1, "verdict": "error", "reason": f"non-JSON stdout: {result.stdout[:120]}"}

    raw = (outer.get("result") or "").strip()
    # Extract the first JSON object in case the model wrapped it in prose
    m = re.search(r"\{.*\}", raw, re.DOTALL)
    if not m:
        return {"score": -1, "verdict": "error", "reason": f"no JSON in reply: {raw[:120]}"}
    try:
        parsed = json.loads(m.group(0))
    except json.JSONDecodeError:
        return {"score": -1, "verdict": "error", "reason": f"bad JSON: {m.group(0)[:120]}"}

    # Normalize
    try:
        parsed["score"] = int(parsed.get("score", -1))
    except (TypeError, ValueError):
        parsed["score"] = -1
    parsed.setdefault("verdict", "error")
    parsed.setdefault("reason", "")
    return parsed


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--in", dest="infile", default="descriptions.tsv")
    parser.add_argument("--out", default="scores.tsv")
    args = parser.parse_args()

    infile = Path(args.infile)
    out = Path(args.out)
    if not infile.exists():
        print(f"Input not found: {infile}"); sys.exit(1)

    BLIND_WORKDIR.mkdir(exist_ok=True)
    claude_exe = find_claude()

    rows = []
    with infile.open(encoding="utf-8") as f:
        header = f.readline()  # skip header
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) == 3:
                rows.append(parts)

    print(f"Scoring {len(rows)} pairs via isolated Claude CLIs...")
    with out.open("w", encoding="utf-8") as f:
        f.write("image_filename\tscore\tverdict\treason\tprompt_text\n")
        for i, (img, prompt, desc) in enumerate(rows, 1):
            result = score_one(claude_exe, prompt, desc)
            score = result.get("score", -1)
            verdict = result.get("verdict", "error")
            reason = str(result.get("reason", "")).replace("\t", " ").replace("\n", " ")
            f.write(f"{img}\t{score}\t{verdict}\t{reason}\t{prompt}\n")
            f.flush()
            print(f"  [{i}/{len(rows)}] {img}: score={score} verdict={verdict}")

    print(f"\nWrote {out}")


if __name__ == "__main__":
    main()
