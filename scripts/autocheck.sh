#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASE_OUT_DIR="${1:-$ROOT_DIR/autologs}"
DURATION_SEC="${2:-12}"
MIN_FPS="${3:-60}"

RUN_ID="$(date +%s)-autocheck"
OUT_DIR="$BASE_OUT_DIR/$RUN_ID"
mkdir -p "$OUT_DIR"

dotnet run --project "$ROOT_DIR/src/AIG.Game/AIG.Game.csproj" -- autoperf "$OUT_DIR" "$DURATION_SEC" "$MIN_FPS"

LOG_FILE="$(ls -1 "$OUT_DIR"/autoperf-*.log | tail -n1)"
FPS_MIN="$(grep '^fps_min=' "$LOG_FILE" | cut -d'=' -f2)"
FPS_AVG="$(grep '^fps_avg=' "$LOG_FILE" | cut -d'=' -f2)"
BELOW_FRAMES="$(grep '^below_threshold_frames=' "$LOG_FILE" | cut -d'=' -f2)"
JUMP_AVG="$(grep '^scene_jump_avg=' "$LOG_FILE" | cut -d'=' -f2)"
JUMP_MAX="$(grep '^scene_jump_max=' "$LOG_FILE" | cut -d'=' -f2)"
JUMP_SPIKES="$(grep '^scene_jump_spikes=' "$LOG_FILE" | cut -d'=' -f2)"
CAP_FRAMES="$(grep '^autocap_frames_saved=' "$LOG_FILE" | cut -d'=' -f2)"
RESULT="$(grep '^result=' "$LOG_FILE" | cut -d'=' -f2)"

echo "OUT_DIR=$OUT_DIR"
echo "LOG_FILE=$LOG_FILE"
echo "FPS_MIN=$FPS_MIN FPS_AVG=$FPS_AVG BELOW_FRAMES=$BELOW_FRAMES"
echo "SCENE_JUMP_AVG=$JUMP_AVG SCENE_JUMP_MAX=$JUMP_MAX SCENE_JUMP_SPIKES=$JUMP_SPIKES"
echo "AUTO_CAP_FRAMES_SAVED=$CAP_FRAMES"
echo "RESULT=$RESULT"

if [[ "$FPS_MIN" -lt "$MIN_FPS" || "$BELOW_FRAMES" -gt 0 || "$RESULT" != "PASS" ]]; then
  echo "FAIL: FPS ниже порога или есть просадки."
  exit 2
fi

echo "PASS: FPS держится и автопроверка завершена."
