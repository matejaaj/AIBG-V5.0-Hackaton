#!/usr/bin/env bash
# Run two bot instances against the same gameId, log each separately.
# Usage: tools/run_match.sh <gameId> [BotA name] [BotB name] [--debug]
set -euo pipefail

cd "$(dirname "$0")/.."

GAME_ID="${1:-game$(date +%s)}"
BOT_A="${2:-BotA}"
BOT_B="${3:-BotB}"
DEBUG_FLAG=""
if [[ "${4:-}" == "--debug" ]]; then DEBUG_FLAG="--debug"; fi

PY="../venv/bin/python"
[[ -x "$PY" ]] || PY="python"

mkdir -p logs

echo "Starting match $GAME_ID: $BOT_A vs $BOT_B"
"$PY" igor_bot.py http://localhost:8080 "$GAME_ID" "$BOT_A" $DEBUG_FLAG &
PID_A=$!
sleep 0.3
"$PY" igor_bot.py http://localhost:8080 "$GAME_ID" "$BOT_B" $DEBUG_FLAG &
PID_B=$!

trap 'kill $PID_A $PID_B 2>/dev/null || true' EXIT INT TERM
wait $PID_A $PID_B
