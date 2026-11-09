#!/usr/bin/env bash
# Runs EditMode tests ("test") or a compile check ("smoke") inside the Unity editor that is already open,
# through Assets/Editor/TestBridge. Exit: 0 PASS, 1 FAIL / COMPILE_ERROR, 2 no answer (editor closed or busy).
# Usage: bash .claude/scripts/unity-bridge.sh test|test:<regex>|smoke [timeout_seconds]   (test:<regex> = only matching tests)
set -u
mode="${1:-test}"; timeout="${2:-600}"
cd "$(dirname "$0")/../.." || exit 2
[ -d Temp ] || { echo "NOT ASSESSED: no Temp/ folder - is the Unity editor open on this project?"; exit 2; }
mkdir -p test-results
rm -f test-results/bridge.log
echo "$mode" > Temp/testbridge.request
for ((i = 0; i < timeout; i++)); do
  if [ -f test-results/bridge.log ]; then
    cat test-results/bridge.log
    [ "$(head -n1 test-results/bridge.log)" = "PASS" ] && exit 0 || exit 1
  fi
  sleep 1
done
rm -f Temp/testbridge.request
echo "NOT ASSESSED: no answer from the editor in ${timeout}s (closed, in Play mode, or bridge not compiled yet)"
exit 2
