#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${1:-$ROOT_DIR/autocap}"

mkdir -p "$OUT_DIR"
rm -f "$OUT_DIR"/autocap-*.png
rm -f "$OUT_DIR"/autocap-warmup.png
rm -f "$ROOT_DIR"/autocap-*.png
rm -f "$ROOT_DIR"/autocap-warmup.png

dotnet run --project "$ROOT_DIR/src/AIG.Game/AIG.Game.csproj" -- autocap "$OUT_DIR"

if compgen -G "$ROOT_DIR/autocap-*.png" > /dev/null; then
  mv "$ROOT_DIR"/autocap-*.png "$OUT_DIR"/
fi
if [ -f "$ROOT_DIR/autocap-warmup.png" ]; then
  mv "$ROOT_DIR/autocap-warmup.png" "$OUT_DIR"/
fi

rm -f "$OUT_DIR"/autocap-warmup.png

echo "Saved screenshots:"
ls -1 "$OUT_DIR"/autocap-*.png
