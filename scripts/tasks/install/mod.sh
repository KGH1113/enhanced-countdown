#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

assert_child_path "$REMOVE_COUNTDOWN_INSTALL_PATH" "$ADOFAI_MODS_DIR"
require_file "$REMOVE_COUNTDOWN_BUILD_OUTPUT/RemoveCountdown.dll"
require_file "$REMOVE_COUNTDOWN_BUILD_OUTPUT/Info.json"

mkdir -p "$REMOVE_COUNTDOWN_INSTALL_PATH"
rm -f \
  "$REMOVE_COUNTDOWN_INSTALL_PATH/RemoveCountdown.dll" \
  "$REMOVE_COUNTDOWN_INSTALL_PATH/RemoveCountdown.pdb" \
  "$REMOVE_COUNTDOWN_INSTALL_PATH/RemoveCountdown.deps.json" \
  "$REMOVE_COUNTDOWN_INSTALL_PATH/Info.json"
rm -f "$REMOVE_COUNTDOWN_INSTALL_PATH"/RemoveCountdown.dll.*.cache
rm -f "$REMOVE_COUNTDOWN_INSTALL_PATH"/RemoveCountdown.dll.*.cache.pdb

cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/RemoveCountdown.dll" "$REMOVE_COUNTDOWN_INSTALL_PATH/RemoveCountdown.dll"
cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/Info.json" "$REMOVE_COUNTDOWN_INSTALL_PATH/Info.json"

for optional_file in RemoveCountdown.pdb RemoveCountdown.deps.json; do
  if [ -f "$REMOVE_COUNTDOWN_BUILD_OUTPUT/$optional_file" ]; then
    cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/$optional_file" "$REMOVE_COUNTDOWN_INSTALL_PATH/$optional_file"
  fi
done

printf 'Installed to %s\n' "$REMOVE_COUNTDOWN_INSTALL_PATH"
