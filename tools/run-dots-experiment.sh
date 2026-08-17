#!/bin/bash
# Step-5 DOTS experiment runner: compile -> dots determinism tests -> H1 -> H2.
# Per-stage wall-clock printed; H1/H2 result lines grepped from the Unity logs.
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

echo "=== step-5 DOTS experiment ($(date -u +%H:%M:%SZ)) ==="

stage compile "$UNITY" -batchmode -quit -nographics -projectPath "$PROJ" -logFile "$LOGDIR/dots-compile.log"
if grep -qE "error CS" "$LOGDIR/dots-compile.log"; then
  echo "--- compile errors:"; grep -E "error CS" "$LOGDIR/dots-compile.log" | head -12
  exit 1
fi

stage dots-tests "$UNITY" -batchmode -nographics -projectPath "$PROJ" \
  -runTests -testPlatform EditMode -assemblyNames "Hellfire.DotsTests" \
  -testResults "$LOGDIR/dots-results.xml" -logFile "$LOGDIR/dots-tests.log"
grep -oE 'result="[^"]*" total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$LOGDIR/dots-results.xml" 2>/dev/null | head -1

stage H1 "$UNITY" -batchmode -quit -nographics -projectPath "$PROJ" \
  -executeMethod Hellfire.Dots.DotsBenchmark.RunH1 -logFile "$LOGDIR/dots-h1.log"
grep -F "[H1]" "$LOGDIR/dots-h1.log"

stage H2 "$UNITY" -batchmode -quit -nographics -projectPath "$PROJ" \
  -executeMethod Hellfire.Dots.DotsBenchmark.RunH2 -logFile "$LOGDIR/dots-h2.log"
grep -F "[H2]" "$LOGDIR/dots-h2.log"

echo "=== overall: $([ $overall -eq 0 ] && echo PASS || echo FAIL) ==="
exit $overall
