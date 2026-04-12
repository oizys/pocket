"""
Batch prompt runner for ComfyUI.

Loads your saved workflow, swaps in each prompt from a text file,
and queues them all via the local API. Each gets a unique seed.

Usage:
    python batch_prompts.py prompts.txt
    python batch_prompts.py prompts.txt --seeds fixed   # same seed for all (compare prompts)
    python batch_prompts.py prompts.txt --steps 30      # override step count
"""

import csv
import os
import json
import shutil
import time
import random
import argparse
import urllib.request
from datetime import datetime
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


_load_dotenv(Path(__file__).parent / ".env")

# Defaults — overridable via CLI args or env vars so this package is portable.
DEFAULT_API = os.environ.get("COMFYUI_API_URL", "http://127.0.0.1:8001")
DEFAULT_WORKFLOW = Path(__file__).parent / "workflow.json"
DEFAULT_OUTPUT_DIR = Path(
    os.environ.get("COMFYUI_OUTPUT_DIR")
    or (Path(__file__).parent.parent / "output")
)
# Local work directory where we keep copies of generated images + sidecars.
# Decoupling from COMFYUI_OUTPUT_DIR keeps this tool self-contained and lets
# us wipe the dir between runs for clean cross-iteration hygiene.
DEFAULT_WORK_DIR = Path(__file__).parent / "work" / "images"


def load_workflow(path: Path) -> dict:
    """Load a ComfyUI UI workflow JSON and convert to API format.

    The UI format stores nodes as a list with visual metadata.
    The API wants a flat dict keyed by node ID with just class_type + inputs.
    """
    raw = json.loads(path.read_text())

    # Build a lookup: link_id -> (source_node_id, source_output_index)
    links = {}
    for link in raw["links"]:
        # link format: [link_id, src_node, src_output_idx, dst_node, dst_input_idx, type]
        link_id, src_node, src_slot = link[0], link[1], link[2]
        links[link_id] = (str(src_node), src_slot)

    api_prompt = {}
    for node in raw["nodes"]:
        node_id = str(node["id"])
        inputs = {}

        # Widget values are positional — match them to input names.
        # Gotcha: ComfyUI auto-inserts a "control_after_generate" widget right
        # after any seed widget. That extra value IS in widgets_values but
        # NOT in the inputs list, so we have to skip it manually.
        widget_names = []
        for inp in node.get("inputs", []):
            if "widget" in inp:
                widget_names.append(inp["widget"]["name"])

        widget_values = list(node.get("widgets_values", []))
        vi = 0
        for name in widget_names:
            if vi >= len(widget_values):
                break
            inputs[name] = widget_values[vi]
            vi += 1
            # Skip the control_after_generate value that follows seed widgets
            if name in ("seed", "noise_seed") and vi < len(widget_values):
                vi += 1

        # Linked inputs override widget values
        for inp in node.get("inputs", []):
            link_id = inp.get("link")
            if link_id is not None and link_id in links:
                src_node, src_slot = links[link_id]
                inputs[inp["name"]] = [src_node, src_slot]

        api_prompt[node_id] = {
            "class_type": node["type"],
            "inputs": inputs,
        }

    return api_prompt


def queue_prompt(api_prompt: dict, api_url: str) -> dict:
    """POST a prompt to ComfyUI and return the response."""
    data = json.dumps({"prompt": api_prompt}).encode("utf-8")
    req = urllib.request.Request(
        f"{api_url}/prompt",
        data=data,
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read())


def wait_for_completion(prompt_id: str, api_url: str, timeout: float = 600.0) -> list[dict]:
    """Poll /history/{prompt_id} until the job finishes, return list of output image info dicts.

    Each dict has keys: filename, subfolder, type.
    """
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(f"{api_url}/history/{prompt_id}") as resp:
                history = json.loads(resp.read())
        except urllib.error.URLError:
            history = {}

        entry = history.get(prompt_id)
        if entry and entry.get("outputs"):
            images = []
            for node_output in entry["outputs"].values():
                images.extend(node_output.get("images", []))
            if images:
                return images
        time.sleep(1.0)
    return []


def write_sidecar(image_info: dict, metadata: dict, work_dir: Path) -> Path:
    """Write a JSON sidecar in work_dir with the same basename as the image.

    Sidecars live alongside the local work copy of the image (flat — any
    ComfyUI subfolder is collapsed away since we're re-homing the files).
    """
    sidecar_path = (work_dir / image_info["filename"]).with_suffix(".json")
    sidecar_path.parent.mkdir(parents=True, exist_ok=True)
    sidecar_path.write_text(json.dumps(metadata, indent=2))
    return sidecar_path


def copy_to_workdir(image_info: dict, output_dir: Path, work_dir: Path) -> Path:
    """Copy a generated image from ComfyUI's output dir into work_dir.

    ComfyUI insists on writing to its own configured output directory; this
    helper pulls each finished image into our local work dir so the rest of
    the pipeline can operate entirely within the tool folder. Returns the
    destination path.
    """
    src = output_dir / image_info.get("subfolder", "") / image_info["filename"]
    dst = work_dir / image_info["filename"]
    work_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    return dst


def clean_work_dir(work_dir: Path) -> None:
    """Remove all files in work_dir (but not the dir itself)."""
    if not work_dir.exists():
        return
    for f in work_dir.iterdir():
        if f.is_file():
            f.unlink()


def main():
    parser = argparse.ArgumentParser(description="Queue ComfyUI prompts from a text file")
    parser.add_argument("prompts_file", help="Text file with one prompt per line")
    parser.add_argument("--seeds", choices=["random", "fixed"], default="random",
                        help="'random' = unique seed per prompt, 'fixed' = same seed for all")
    parser.add_argument("--steps", type=int, help="Override number of sampling steps")
    parser.add_argument("--guidance", type=float, help="Override guidance value")
    parser.add_argument("--prefix", default="batch", help="Filename prefix for saved images")
    parser.add_argument("--no-sidecar", action="store_true", help="Skip writing JSON sidecar metadata")
    parser.add_argument("--manifest", default="manifest.csv", help="Path to emit (image_filename, prompt_text) manifest")
    parser.add_argument("--workflow", default=str(DEFAULT_WORKFLOW),
                        help="Path to workflow JSON (UI format). Defaults to ./workflow.json next to this script.")
    parser.add_argument("--output-dir", default=str(DEFAULT_OUTPUT_DIR),
                        help="ComfyUI output directory (where ComfyUI writes raw images — "
                             "this tool reads from here and copies into --work-dir). "
                             "Env: COMFYUI_OUTPUT_DIR.")
    parser.add_argument("--work-dir", default=str(DEFAULT_WORK_DIR),
                        help="Local working directory for image copies + sidecars. "
                             f"Default: {DEFAULT_WORK_DIR}. Wiped at start of each run "
                             "unless --no-clean is passed.")
    parser.add_argument("--no-clean", action="store_true",
                        help="Don't wipe --work-dir before this run. Useful when "
                             "accumulating across multiple manual runs.")
    parser.add_argument("--api-url", default=DEFAULT_API,
                        help="ComfyUI API base URL. Env: COMFYUI_API_URL.")
    args = parser.parse_args()

    workflow_path = Path(args.workflow)
    output_dir = Path(args.output_dir)
    work_dir = Path(args.work_dir)
    if not workflow_path.exists():
        print(f"Workflow not found: {workflow_path}"); return

    if not args.no_clean:
        clean_work_dir(work_dir)
    work_dir.mkdir(parents=True, exist_ok=True)

    prompts = [line.strip() for line in Path(args.prompts_file).read_text().splitlines() if line.strip()]
    if not prompts:
        print("No prompts found in file.")
        return

    api_prompt = load_workflow(workflow_path)
    fixed_seed = random.randint(0, 2**53) if args.seeds == "fixed" else None

    # Identify node IDs by class_type
    nodes = {v["class_type"]: k for k, v in api_prompt.items()}

    clip_encode_id = nodes["CLIPTextEncode"]
    ksampler_id = nodes["KSampler"]
    save_id = nodes["SaveImage"]
    guidance_id = nodes.get("FluxGuidance")

    print(f"Workflow:   {workflow_path}")
    print(f"ComfyUI out: {output_dir}")
    print(f"Work dir:   {work_dir}")
    print(f"API:        {args.api_url}")
    print(f"Queueing {len(prompts)} prompts...\n")

    manifest_path = Path(args.manifest)
    manifest_file = manifest_path.open("w", newline="", encoding="utf-8")
    manifest_writer = csv.writer(manifest_file)
    manifest_writer.writerow(["image_filename", "prompt_text"])

    for i, prompt_text in enumerate(prompts):
        # Set the prompt
        api_prompt[clip_encode_id]["inputs"]["text"] = prompt_text

        # Set seed
        seed = fixed_seed if fixed_seed else random.randint(0, 2**53)
        api_prompt[ksampler_id]["inputs"]["seed"] = seed

        # Optional overrides
        if args.steps:
            api_prompt[ksampler_id]["inputs"]["steps"] = args.steps
        if args.guidance and guidance_id:
            api_prompt[guidance_id]["inputs"]["guidance"] = args.guidance

        # Filename prefix so you can tell them apart
        api_prompt[save_id]["inputs"]["filename_prefix"] = f"{args.prefix}_{i:03d}"

        result = queue_prompt(api_prompt, args.api_url)
        prompt_id = result.get("prompt_id", "???")
        short = prompt_text[:60] + ("..." if len(prompt_text) > 60 else "")
        print(f"  [{i+1}/{len(prompts)}] {prompt_id[:8]}  seed={seed}  \"{short}\"")

        if args.no_sidecar:
            continue

        # Snapshot the metadata for this run before we mutate the prompt for the next iteration
        metadata = {
            "prompt_id": prompt_id,
            "queued_at": datetime.now().isoformat(timespec="seconds"),
            "prompt_text": prompt_text,
            "seed": seed,
            "steps": api_prompt[ksampler_id]["inputs"]["steps"],
            "cfg": api_prompt[ksampler_id]["inputs"]["cfg"],
            "sampler": api_prompt[ksampler_id]["inputs"]["sampler_name"],
            "scheduler": api_prompt[ksampler_id]["inputs"]["scheduler"],
            "guidance": api_prompt[guidance_id]["inputs"]["guidance"] if guidance_id else None,
            "model": api_prompt[nodes.get("UnetLoaderGGUF", "")]["inputs"].get("unet_name") if "UnetLoaderGGUF" in nodes else None,
            "workflow_api": json.loads(json.dumps(api_prompt)),  # deep copy frozen at this point
        }

        images = wait_for_completion(prompt_id, args.api_url)
        if not images:
            print(f"       (timed out waiting for output, no sidecar written)")
            continue

        for img in images:
            local_path = copy_to_workdir(img, output_dir, work_dir)
            metadata["image_filename"] = img["filename"]
            sidecar = write_sidecar(img, metadata, work_dir)
            manifest_writer.writerow([img["filename"], prompt_text])
            manifest_file.flush()
            print(f"       copied {img['filename']} -> {local_path}  +  {sidecar.name}")

    manifest_file.close()
    print(f"\nDone. {len(prompts)} prompts queued with prefix '{args.prefix}_'")
    print(f"ComfyUI source: {output_dir}")
    print(f"Local copies:   {work_dir}")
    print(f"Manifest:       {manifest_path}")


if __name__ == "__main__":
    main()
