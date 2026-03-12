#!/bin/bash
set -e

dotnet publish src/Pockets.App -c Release -r win-x64 --self-contained -o publish/win-x64

cmd.exe /C start "" "$(wslpath -w publish/win-x64/Pockets.App.exe)"
