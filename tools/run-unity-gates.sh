#!/bin/bash
# Step-4 gate runner. Requires an activated Unity license (open Unity Hub, sign in).
# Runs: headless compile -> EditMode tests (§3 parity) -> procedural scene bootstrap.
# Prints per-stage wall-clock — these numbers ARE the H3 Unity-round measurement.
set -u
UNITY="/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/MacOS/Unity"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$REPO/unity"
LOGDIR="$REPO/unity/Logs"
mkdir -p "$LOGDIR"
overall=0

stage() {
  local name="$1"; shift
  local t0=$(date +%s)
  "$@" >/dev/null 2>&1
  local code=$?
  local t1=$(date +%s)
  echo "stage $name: exit $code, $((t1 - t0))s"
  [ $code -ne 0 ] && overall=1
  return $code
}

echo "=== step-4 gates ($(date -u +%H:%M:%SZ)) ==="

stage compile "$UNITY" -batchmode -quit -nographics -projectPath "$PROJ" -logFile "$LOGDIR/compile.log"
if [ $overall -ne 0 ]; then
  echo "--- compile errors:"; grep -E "error CS|Scripts have compile errors|License" "$LOGDIR/compile.log" | head -10
  exit 1
fi
grep -qE "error CS" "$LOGDIR/compile.log" && { echo "compile errors:"; grep -E "error CS" "$LOGDIR/compile.log" | head -10; exit 1; }

stage editmode-tests "$UNITY" -batchmode -nographics -projectPath "$PROJ" \
  -runTests -testPlatform EditMode -testResults "$LOGDIR/editmode-results.xml" -logFile "$LOGDIR/tests.log"
grep -oE 'result="[^"]*" total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$LOGDIR/editmode-results.xml" 2>/dev/null | head -1

stage bootstrap "$UNITY" -batchmode -quit -nographics -projectPath "$PROJ" \
  -executeMethod Hellfire.EditorTools.SceneBootstrap.Build -logFile "$LOGDIR/bootstrap.log"
[ -f "$PROJ/Assets/Scenes/Main.unity" ] && echo "scene: Assets/Scenes/Main.unity generated" || { echo "scene missing"; overall=1; }

echo "=== overall: $([ $overall -eq 0 ] && echo PASS || echo FAIL) ==="
exit $overall
