# Icon Generation & Validation Pipeline

A portable Python pipeline that generates game asset icons via ComfyUI (Flux 2
Dev) and validates them using an isolated 3-blind agent pattern. Drop this
folder into any project to batch-generate validated icons from a prompt list.

## Concept

Generating 50 icons is easy. Knowing which ones actually look like what you
asked for is hard. This pipeline solves that with three isolated stages where
no stage can see what the others know:

1. **Generator** — ComfyUI produces images from prompts in a text file.
2. **Blind describer** — A fresh Claude CLI sees each image *with all PNG
   metadata stripped* and describes it visually. It never sees the original
   prompt.
3. **Blind comparator** — A third Claude CLI receives only the
   `(intended_prompt, observed_description)` pair with no other context and
   scores the alignment 0-10.

Below-threshold prompts get re-queued with fresh seeds in a ralph loop. Each
iteration shrinks the set of failing prompts until they drop below a minimum
or a max iteration count is reached.

The isolation matters because without it, you fool yourself: an agent that
knows the prompt reads descriptions charitably, and an agent that can see the
image metadata can cheat by reading the ComfyUI workflow embedded in the PNG.
Both failure modes are blocked here — the describer literally cannot extract
metadata (it only has the `Read` tool) and the comparator has zero tools at all.

## Requirements

- **ComfyUI running locally** with Flux 2 Dev models loaded:
  - `flux2-dev-q6_k.gguf` in `models/unet/` (GGUF unet loader)
  - `mistral_3_small_flux2_fp8.safetensors` in `models/text_encoders/`
  - `flux2-vae.safetensors` in `models/vae/`
  - ComfyUI-GGUF custom node extension installed
- **Python 3** with `Pillow` (for metadata stripping)
- **Claude Code CLI** on PATH, logged into a Claude Max subscription (used
  for the describer and comparator stages)

## Configuration

All config comes from `.env` next to the scripts, or environment variables,
or CLI flags. Precedence: CLI flag > real env var > `.env` > hardcoded default.

`.env` example:

```
COMFYUI_OUTPUT_DIR=C:\path\to\ComfyUI\output
COMFYUI_API_URL=http://127.0.0.1:8001
```

- `COMFYUI_OUTPUT_DIR` — where ComfyUI writes images. The pipeline writes
  sidecar JSON next to each image here.
- `COMFYUI_API_URL` — base URL of ComfyUI's REST API. The Desktop app
  typically uses `:8001`; standalone ComfyUI uses `:8188`.

## Files

### Scripts

- **`batch_prompts.py`** — Stage 1 generator. Reads a prompts file (one per
  line), loads `workflow.json`, converts it from UI format to ComfyUI API
  format, and POSTs each prompt to `/prompt`. Polls `/history/{prompt_id}`
  until each job finishes. Writes a JSON sidecar next to each output image
  and a `manifest.csv` mapping image filenames to their source prompts.

- **`strip_metadata.py`** — Copies PNGs from one directory to another,
  removing all embedded tEXt/iTXt chunks (the ComfyUI workflow, the prompt,
  everything). The resulting images are visually identical but carry no
  evidence of how they were generated. Feeds the blind describer.

- **`describe.py`** — Stage 2 blind describer. For each row in
  `manifest.csv`, spawns an isolated `claude -p` subprocess restricted to
  `--tools "Read"` that reads the stripped image and emits a 1-2 sentence
  visual description. The describer cannot see the original prompt and
  literally cannot extract metadata (no Bash, no Write, no Python).

- **`compare.py`** — Stage 3 blind comparator. For each row in
  `descriptions.tsv`, spawns an isolated `claude -p` subprocess with
  `--tools ""` (zero tools — pure text in/out). The comparator receives only
  the `(intended, observed)` pair and returns a JSON score + verdict.

- **`build_failed.py`** — Filters `scores.tsv` by a score threshold
  (default 7) and writes the failing prompts to `failed_prompts.txt`,
  one per line, for the next iteration.

- **`validate_loop.py`** — The ralph wrapper. Runs the full pipeline in a
  loop: generate → strip → describe → compare → build_failed. Each iteration
  after the first uses `failed_prompts.txt` as its input instead of the
  original prompts file. Stops when remaining failures drop below
  `--min-remaining` or `--max-iter` is reached.

### Data

- **`workflow.json`** — ComfyUI workflow in UI format. Defines the generation
  pipeline: UnetLoaderGGUF → CLIPLoader → CLIPTextEncode → KSampler →
  VAEDecode → ImageScale → ImageQuantize → SaveImage. The CLIPTextEncode
  node's prompt field is the one that gets swapped per-run. Swap this file
  to change the generation pipeline — the conversion logic in
  `batch_prompts.py` handles arbitrary workflows as long as they contain
  `CLIPTextEncode`, `KSampler`, and `SaveImage` nodes (plus optionally
  `FluxGuidance` and `UnetLoaderGGUF`).

- **`.env`** — Project-local config (see Configuration above).

- **`prompts.txt`** — Example input file, one prompt per line, blank lines
  ignored.

### Artifacts (generated at runtime)

- **`manifest.csv`** — CSV written by `batch_prompts.py` after each run.
  Columns: `image_filename, prompt_text`. One row per successfully generated
  image. The single source of truth connecting prompts to their outputs.

- **`output_blind/`** — Directory populated by `strip_metadata.py` with
  metadata-stripped copies of the images from `COMFYUI_OUTPUT_DIR`. Only read
  by `describe.py`.

- **`descriptions.tsv`** — Tab-separated output of `describe.py`.
  Header: `image_filename \t prompt_text \t description`. The prompt field
  here is carried through from manifest.csv so the comparator stage has
  everything it needs on one row.

- **`scores.tsv`** — Tab-separated output of `compare.py`.
  Header: `image_filename \t score \t verdict \t reason \t prompt_text`.
  `score` is an integer 0-10 (or -1 on error). `verdict` is
  `match | partial | miss | error`. This is the final per-image quality
  report.

- **`failed_prompts.txt`** — Plain text, one prompt per line, written by
  `build_failed.py`. Contains only the prompts whose scores were below the
  threshold. Used as input for the next ralph iteration.

- **`_blind_workdir/`** — An intentionally empty directory used as the
  `cwd` for each describer/comparator subprocess. Claude Code's project
  memory is keyed off `cwd`, so running from an empty folder means no
  project memory loads. This prevents the blind agents from inheriting
  any project context (e.g. Pockets-specific memory).

- **Per-image sidecar JSON files** (`<image_filename>.json` next to each
  PNG in `COMFYUI_OUTPUT_DIR`) — Written by `batch_prompts.py`. Contains
  the prompt, seed, sampler settings, and a deep copy of the full
  API-format workflow. Enables exact reproduction of any image.

## Running

### Full pipeline with auto-retry

```
python validate_loop.py prompts.txt
python validate_loop.py prompts.txt --threshold 8 --max-iter 3 --min-remaining 2
```

### Individual stages (debugging)

```
python batch_prompts.py prompts.txt
python strip_metadata.py <comfyui_output_dir> output_blind
python describe.py
python compare.py
python build_failed.py 7
```

### batch_prompts.py standalone options

```
python batch_prompts.py prompts.txt --seeds fixed       # same seed for all (compare prompts)
python batch_prompts.py prompts.txt --steps 30          # override sampler steps
python batch_prompts.py prompts.txt --prefix run42      # custom filename prefix
python batch_prompts.py prompts.txt --no-sidecar        # skip JSON sidecar writing
python batch_prompts.py prompts.txt --workflow alt.json # use a different workflow
```

## Integration notes for Pockets

The intended workflow when integrating with Pockets:

1. Pockets project Claude writes `assets.txt` listing each game asset and a
   generation prompt, one per line.
2. Pockets invokes `python validate_loop.py assets.txt` with appropriate
   thresholds.
3. The pipeline loops until all assets meet the threshold or give up.
4. Pockets reads `scores.tsv` to know which images are keepers (by
   filename) and consumes them from `COMFYUI_OUTPUT_DIR`.
5. Any row in `scores.tsv` with `verdict=error` or persistently low scores
   indicates a prompt that needs human attention — surface these for review
   rather than silently keeping them.

The `.env` file should be edited to point at Pockets' Python environment's
ComfyUI instance. Do NOT commit `.env` if it ever contains secrets (the
defaults shipped here are local paths only, safe to commit).

## Gotchas

- **ComfyUI Desktop vs standalone ports.** The Desktop app usually listens on
  `:8001`; standalone ComfyUI on `:8188`. Set `COMFYUI_API_URL` accordingly.
- **The workflow must match the models.** `workflow.json` references specific
  model filenames (`flux2-dev-q6_k.gguf`, `mistral_3_small_flux2_fp8.safetensors`,
  `flux2-vae.safetensors`). Running on a different machine requires either
  those exact files or a modified workflow.
- **`--bare` mode breaks Claude Max auth.** The describer/comparator do NOT
  use `--bare` because it skips OAuth/keychain reads, forcing API-key auth.
  Isolation is instead achieved by running each CLI invocation with
  `cwd=_blind_workdir/`, which has no project memory.
- **Seed widgets have an extra value.** ComfyUI auto-inserts a
  `control_after_generate` widget after any seed input, which shows up in
  `widgets_values` but NOT in the inputs list. `batch_prompts.py` handles
  this in `load_workflow()` by skipping the extra value after seed widgets.
  If you see 400 errors from `/prompt`, check whether a new widget type has
  been added with a similar control widget.
- **Latent size is a trap for pixel art.** Generating at tiny native
  resolutions (e.g. 32x32) gives garbage because the latent is width/16 —
  a 32x32 image has a 2x2 latent, far below what the model can reason about.
  Always generate at 1024x1024 and downscale in post with ImageScale
  (nearest-exact) in the workflow.
- **Sequential processing.** Each stage waits for the previous one. A full
  pipeline run over 50 prompts will take on the order of (generation_time *
  50) + (claude_cold_start * 100). Parallelizing the describer/comparator
  stages is a future optimization.

## Pipeline diagram

```
prompts.txt
    |
    v
batch_prompts.py -----> ComfyUI /prompt -----> COMFYUI_OUTPUT_DIR/*.png
    |                                              + *.json (sidecars)
    |  writes
    v
manifest.csv
    |
    v
strip_metadata.py -----> output_blind/*.png  (no metadata chunks)
    |
    v
describe.py (isolated Claude, --tools Read, cwd=_blind_workdir/)
    |
    v
descriptions.tsv
    |
    v
compare.py  (isolated Claude, --tools "", cwd=_blind_workdir/)
    |
    v
scores.tsv
    |
    v
build_failed.py  (filter by threshold)
    |
    v
failed_prompts.txt  -----> next ralph iteration (or stop)
```
