#!/usr/bin/env bash
# Regenerate the 12 entropy glyph SVGs (into assets/glyphs/) and the contact-sheet
# PNG (into the Obsidian vault, for phone review). Pass extra args through to the
# tool, e.g. --no-sheet or --sheet <path>.
set -euo pipefail
cd "$(dirname "$0")"
dotnet run -c Release -- "$@"
