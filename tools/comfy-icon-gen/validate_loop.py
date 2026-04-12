"""
Ralph-style loop for the 3-blind validation pipeline.

Each iteration:
  1. batch_prompts.py queues prompts through ComfyUI and emits manifest.csv
  2. strip_metadata.py copies PNGs without metadata into output_blind/
  3. describe.py runs the blind describer -> descriptions.tsv
  4. compare.py runs the blind comparator -> scores.tsv
  5. build_failed.py pulls the sub-threshold prompts into failed_prompts.txt

The next iteration uses failed_prompts.txt as its input, regenerating with
fresh seeds. Stops when remaining failures drop below MIN_REMAINING or MAX_ITER
is reached.

Usage:
    python validate_loop.py prompts.txt
    python validate_loop.py prompts.txt --threshold 8 --max-iter 3
"""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path


def _load_dotenv(path: Path) -> None:
    """Minimal .env loader: KEY=VALUE per line, # comments, optional quotes.
    Uses setdefault so real env vars (e.g. CI) override the file."""
    if not path.exists():
        return
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, val = line.split("=", 1)
        os.environ.setdefault(key.strip(), val.strip().strip('"').strip("'"))


HERE = Path(__file__).parent
_load_dotenv(HERE / ".env")

DEFAULT_OUTPUT_DIR = Path(
    os.environ.get("COMFYUI_OUTPUT_DIR")
    or (HERE.parent / "output")
)
DEFAULT_WORKFLOW = HERE / "workflow.json"
DEFAULT_WORK_DIR = HERE / "work" / "images"
BLIND_DIR = HERE / "output_blind"


def run(cmd: list[str]) -> bool:
    print(f"\n$ {' '.join(cmd)}")
    return subprocess.call(cmd) == 0


def count_lines(path: Path) -> int:
    if not path.exists():
        return 0
    return sum(1 for line in path.read_text(encoding="utf-8").splitlines() if line.strip())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("prompts_file", help="Initial prompts file (one per line)")
    parser.add_argument("--threshold", type=int, default=7,
                        help="Score threshold: anything below is retried")
    parser.add_argument("--max-iter", type=int, default=5)
    parser.add_argument("--min-remaining", type=int, default=3,
                        help="Stop when remaining failures drop below this count")
    parser.add_argument("--workflow", default=str(DEFAULT_WORKFLOW),
                        help="Path to workflow JSON; defaults to ./workflow.json")
    parser.add_argument("--output-dir", default=str(DEFAULT_OUTPUT_DIR),
                        help="ComfyUI output directory (where ComfyUI itself writes raw images; "
                             "the pipeline reads from here and copies into --work-dir). "
                             "Env: COMFYUI_OUTPUT_DIR")
    parser.add_argument("--work-dir", default=str(DEFAULT_WORK_DIR),
                        help="Local working directory for image copies + sidecars. "
                             f"Default: {DEFAULT_WORK_DIR}. Wiped at the start of each "
                             "iteration so cross-iteration state cannot leak.")
    parser.add_argument("--api-url", default=os.environ.get("COMFYUI_API_URL", "http://127.0.0.1:8001"),
                        help="ComfyUI API URL. Env: COMFYUI_API_URL")
    args = parser.parse_args()

    output_dir = Path(args.output_dir)
    work_dir = Path(args.work_dir)

    python = sys.executable  # use the same interpreter running this script
    current_input = args.prompts_file

    for i in range(1, args.max_iter + 1):
        print(f"\n{'='*60}")
        print(f"  Iteration {i}/{args.max_iter}  (input: {current_input})")
        print(f"{'='*60}")

        steps = [
            [python, str(HERE / "batch_prompts.py"), current_input,
             "--workflow", args.workflow,
             "--output-dir", str(output_dir),
             "--work-dir", str(work_dir),
             "--api-url", args.api_url],
            [python, str(HERE / "strip_metadata.py"), str(work_dir), str(BLIND_DIR)],
            [python, str(HERE / "describe.py")],
            [python, str(HERE / "compare.py")],
            [python, str(HERE / "build_failed.py"), "--threshold", str(args.threshold)],
        ]
        for cmd in steps:
            if not run(cmd):
                print(f"\nStep failed: {' '.join(cmd)}")
                sys.exit(1)

        remaining = count_lines(Path("failed_prompts.txt"))
        print(f"\n>>> {remaining} prompts still below threshold {args.threshold}")

        if remaining < args.min_remaining:
            print(">>> Good enough, stopping.")
            break

        current_input = "failed_prompts.txt"
    else:
        print(f"\nReached max iterations ({args.max_iter}). {remaining} prompts still failing.")
        print("See scores.tsv for the stragglers; they likely need prompt tuning.")

    print("\nFinal results: scores.tsv")


if __name__ == "__main__":
    main()
