#!/bin/sh
# Usage: tools/shot.sh <name> ["windrive cmd" ...]  - sends input to the game, then screenshots it.
export PATH="$HOME/.claude/umode/universal-modder/bin:$PATH"
O="$(cd "$(dirname "$0")/.." && pwd -W)/out"
n="$1"; shift
if [ $# -gt 0 ]; then um win drive --proc "Planet Crafter" "focus" "$@" 2>&1 | grep -v -- "-> ok"; fi
um win shot --exe "Planet Crafter.exe" "$O/$n.png" --scale 0.4 >/dev/null 2>&1
echo "$O/${n}_small.png"
