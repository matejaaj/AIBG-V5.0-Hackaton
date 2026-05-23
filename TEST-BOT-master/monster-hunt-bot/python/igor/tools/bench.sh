#!/usr/bin/env bash
# Run N back-to-back self-play matches and report win/HP stats.
# Usage: tools/bench.sh [N]   (default 10)
set -euo pipefail

cd "$(dirname "$0")/.."

N="${1:-10}"
PY="../venv/bin/python"
[[ -x "$PY" ]] || PY="python"

mkdir -p logs/bench
SUMMARY="logs/bench/summary_$(date +%Y%m%d_%H%M%S).txt"
: > "$SUMMARY"

for i in $(seq 1 "$N"); do
  GID="bench-$$-$i"
  echo "=== match $i / $N ($GID) ===" | tee -a "$SUMMARY"
  bash tools/run_match.sh "$GID" "BotA-$i" "BotB-$i" > /dev/null 2>&1 || true

  # Pull last decision line per bot to compute end-state HP
  LAST_A=$(tail -n 200 "logs/BotA-$i.log" | grep -o '"my_hp":[0-9]*' | tail -n1 | cut -d: -f2 || echo 0)
  LAST_B=$(tail -n 200 "logs/BotB-$i.log" | grep -o '"my_hp":[0-9]*' | tail -n1 | cut -d: -f2 || echo 0)
  TURNS_A=$(tail -n 200 "logs/BotA-$i.log" | grep -o '"turn":[0-9]*' | tail -n1 | cut -d: -f2 || echo 0)

  WINNER="?"
  if [[ "${LAST_A:-0}" -gt "${LAST_B:-0}" ]]; then WINNER="A"; fi
  if [[ "${LAST_B:-0}" -gt "${LAST_A:-0}" ]]; then WINNER="B"; fi
  if [[ "${LAST_A:-0}" == "${LAST_B:-0}" ]]; then WINNER="="; fi

  echo "match $i: winner=$WINNER  A.hp=${LAST_A:-?}  B.hp=${LAST_B:-?}  turns=${TURNS_A:-?}" | tee -a "$SUMMARY"
done

echo "---" | tee -a "$SUMMARY"
A_WINS=$(grep -c "winner=A" "$SUMMARY" || true)
B_WINS=$(grep -c "winner=B" "$SUMMARY" || true)
TIES=$(grep -c "winner==" "$SUMMARY" || true)
echo "totals: A=${A_WINS} B=${B_WINS} ties=${TIES}" | tee -a "$SUMMARY"
echo "summary: $SUMMARY"
