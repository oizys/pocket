"""
Read scores.tsv and emit failed_prompts.txt — one prompt per line for retry.

A prompt is "failed" if its score is strictly below THRESHOLD (default 7).
Errors (score = -1) are also treated as failures.

Usage:
    python build_failed.py              # threshold=7
    python build_failed.py 8            # threshold=8
    python build_failed.py --threshold 6 --scores scores.tsv --out failed_prompts.txt
"""
import argparse
import sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("threshold_pos", nargs="?", type=int, default=None,
                        help="Positional shortcut: threshold value")
    parser.add_argument("--threshold", type=int, default=7)
    parser.add_argument("--scores", default="scores.tsv")
    parser.add_argument("--out", default="failed_prompts.txt")
    args = parser.parse_args()

    threshold = args.threshold_pos if args.threshold_pos is not None else args.threshold

    scores_path = Path(args.scores)
    out_path = Path(args.out)
    if not scores_path.exists():
        print(f"Scores file not found: {scores_path}"); sys.exit(1)

    failed = []
    kept = 0
    with scores_path.open(encoding="utf-8") as f:
        header = f.readline()  # discard
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 5:
                continue
            img, score_str, verdict, reason, prompt = parts[0], parts[1], parts[2], parts[3], parts[4]
            try:
                score = int(score_str)
            except ValueError:
                score = -1
            if score < threshold:
                failed.append(prompt)
            else:
                kept += 1

    out_path.write_text("\n".join(failed) + ("\n" if failed else ""), encoding="utf-8")
    print(f"Threshold: {threshold}")
    print(f"  Kept:   {kept}")
    print(f"  Failed: {len(failed)} -> {out_path}")


if __name__ == "__main__":
    main()
