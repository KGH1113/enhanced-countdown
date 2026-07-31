#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

assert_non_root_path "$REMOVE_COUNTDOWN_PACKAGE_BUILD_ROOT"
assert_child_path "$REMOVE_COUNTDOWN_PACKAGE_STAGE" "$REMOVE_COUNTDOWN_PACKAGE_BUILD_ROOT"
require_file "$REMOVE_COUNTDOWN_BUILD_OUTPUT/RemoveCountdown.dll"
require_file "$REMOVE_COUNTDOWN_BUILD_OUTPUT/Info.json"

mkdir -p "$REMOVE_COUNTDOWN_PACKAGE_BUILD_ROOT"
if [ -e "$REMOVE_COUNTDOWN_PACKAGE_STAGE" ]; then
  safe_remove_tree "$REMOVE_COUNTDOWN_PACKAGE_STAGE" "$REMOVE_COUNTDOWN_PACKAGE_BUILD_ROOT"
fi
mkdir -p "$REMOVE_COUNTDOWN_PACKAGE_STAGE"

cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/RemoveCountdown.dll" "$REMOVE_COUNTDOWN_PACKAGE_STAGE/"
cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/Info.json" "$REMOVE_COUNTDOWN_PACKAGE_STAGE/"

for optional_file in RemoveCountdown.pdb RemoveCountdown.deps.json; do
  if [ -f "$REMOVE_COUNTDOWN_BUILD_OUTPUT/$optional_file" ]; then
    cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/$optional_file" "$REMOVE_COUNTDOWN_PACKAGE_STAGE/"
  fi
done
