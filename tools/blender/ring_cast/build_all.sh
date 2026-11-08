#!/bin/sh
# Збирає все: кліпи, вісім персонажів, текстури, експорт FBX/GLB, дані сторінки.
#   sh tools/blender/ring_cast/build_all.sh <робоча тека> [персонажі...]
# Кеш кліпів (<робоча тека>/cache/*.npz) робить extract_clips.py; якщо його немає, запускається спершу.
set -e
WORK="$1"; shift
R="$(cd "$(dirname "$0")/../../.." && pwd)"
BL="${BLENDER:-C:/Program Files/Blender Foundation/Blender 4.5/blender.exe}"
PY="python -I -X utf8"
CHARS="${*:-stalevyi dzhura baron hammer komet vdova novachka lina}"
OUTC="$R/Assets/Art/Ring/Cast"
SRC="$R/ArtSource/Ring/Cast"
WEB="$R/tools/ring_preview/data"
mkdir -p "$OUTC" "$SRC" "$WEB" "$WORK/clips" "$WORK/work" "$WORK/tex" "$WORK/logs"
cd "$R"

if [ -z "$SKIP_CLIPS" ]; then
  $PY tools/blender/ring_cast/clips_build.py "$WORK/cache" "$WORK/clips" m f
  for s in m f; do
    S=$(echo "$s" | tr 'mf' 'MF')
    "$BL" -b --factory-startup --python tools/blender/ring_cast/clips_write.py -- "$WORK/clips/clips_$s.npz" "$WORK/clips/clips_$s.json" "$SRC/RING_Cast_$S.blend" "$OUTC/Anim/RING_Cast_$S.fbx" "$WEB/anim_$s.glb" > "$WORK/logs/anim_$s.log" 2>&1 &
  done
  wait
  $PY tools/blender/ring_cast/cast_graph.py "$WORK/clips" "$WEB" "$OUTC/CAST_ANIM.md"
fi

build_one() {
  c="$1"
  sex=$($PY -c "import sys; sys.path.insert(0,'tools/blender/ring_cast'); import cast_spec as C; print(C.CAST['$c']['sex'])")
  "$BL" -b --factory-startup --python tools/blender/ring_cast/cast_build.py -- "$R" "$c" "$WORK/work" "$WORK/clips/clips_$sex.npz" > "$WORK/logs/$c.build.log" 2>&1
  PYTHONIOENCODING=utf-8 $PY tools/blender/ring_cast/cast_paint.py "$R" "$c" "$WORK/work" "$OUTC/$c/Textures" > "$WORK/logs/$c.paint.log" 2>&1
  "$BL" -b --factory-startup --python tools/blender/ring_cast/cast_finish.py -- "$R" "$c" "$WORK/work" "$OUTC/$c/Textures" "$SRC/$c.blend" "$OUTC/$c/$c.fbx" "$WEB/$c.glb" > "$WORK/logs/$c.finish.log" 2>&1
  $PY tools/blender/ring_cast/cast_web_tex.py "$OUTC/$c/Textures" "$WEB" "$c" > "$WORK/logs/$c.webtex.log" 2>&1
  echo "done $c $(tail -1 "$WORK/logs/$c.finish.log" | cut -c1-60)"
}
N=0
for c in $CHARS; do
  build_one "$c" &
  N=$((N+1))
  if [ $((N % 3)) -eq 0 ]; then wait; fi
done
wait

# Ринг для сторінки: об'єкти ring_* з ігрових venue_*.fbx (арена, зала, спортзал)
"$BL" -b --factory-startup --python tools/blender/ring_cast/ring_web.py -- "$R" "$WEB" > "$WORK/logs/ring_web.log" 2>&1 && echo "done ring"
