#!/usr/bin/env bash
# Depth-recipe design tool. Out of Pockets.sln (pulls SkiaSharp only for the PNG).
#
#   generate.sh                       # frontier printout from the world start
#   generate.sh frontier --reached quiet+5,gloam+1
#   generate.sh diagram               # PNG -> vault assets/depth-recipes-v1.png
#   generate.sh diagram --out FOO.png
#   generate.sh validate              # run the integrity checks
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet run --project "$here/DepthRecipes.csproj" -c Release -- "$@"
