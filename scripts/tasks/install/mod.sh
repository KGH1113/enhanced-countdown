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
require_file "$REMOVE_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/RemoveCountdown.Bootstrap.dll"
require_file "$REMOVE_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/RemoveCountdown.UpdateEngine.dll"

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$REMOVE_COUNTDOWN_PROJECT_ROOT/RemoveCountdown/Info.json" | head -n 1)"
[ -n "$version" ] || fail "RemoveCountdown version is missing from Info.json."

mkdir -p "$REMOVE_COUNTDOWN_INSTALL_PATH"
if [ -e "$REMOVE_COUNTDOWN_INSTALL_PATH/Runtime" ]; then
  safe_remove_tree "$REMOVE_COUNTDOWN_INSTALL_PATH/Runtime" "$REMOVE_COUNTDOWN_INSTALL_PATH"
fi
rm -f \
  "$REMOVE_COUNTDOWN_INSTALL_PATH/RemoveCountdown.dll" \
  "$REMOVE_COUNTDOWN_INSTALL_PATH/RemoveCountdown.pdb" \
  "$REMOVE_COUNTDOWN_INSTALL_PATH/RemoveCountdown.deps.json" \
  "$REMOVE_COUNTDOWN_INSTALL_PATH/RemoveCountdown.Bootstrap.dll" \
  "$REMOVE_COUNTDOWN_INSTALL_PATH/Info.json"
rm -f "$REMOVE_COUNTDOWN_INSTALL_PATH"/RemoveCountdown.dll.*.cache*

runtime="$REMOVE_COUNTDOWN_INSTALL_PATH/Runtime/versions/$version"
mkdir -p "$runtime"
cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/Info.json" "$REMOVE_COUNTDOWN_INSTALL_PATH/Info.json"
cp "$REMOVE_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/RemoveCountdown.Bootstrap.dll" "$REMOVE_COUNTDOWN_INSTALL_PATH/"
if [ ! -f "$REMOVE_COUNTDOWN_INSTALL_PATH/UpdateSettings.json" ]; then
  printf '{\n  "ReceiveBetaUpdates": false\n}\n' > "$REMOVE_COUNTDOWN_INSTALL_PATH/UpdateSettings.json"
fi
cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/RemoveCountdown.dll" "$runtime/"
cp "$REMOVE_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/RemoveCountdown.UpdateEngine.dll" "$runtime/"
cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/Info.json" "$runtime/Info.json"

for optional_file in RemoveCountdown.pdb RemoveCountdown.deps.json; do
  if [ -f "$REMOVE_COUNTDOWN_BUILD_OUTPUT/$optional_file" ]; then
    cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/$optional_file" "$runtime/$optional_file"
  fi
done

printf '{\n  "SchemaVersion": 2,\n  "Current": "%s",\n  "Previous": null,\n  "Trial": null,\n  "RejectedVersion": null,\n  "FailureCount": 0,\n  "LastFailureUtc": null\n}\n' \
  "$version" > "$REMOVE_COUNTDOWN_INSTALL_PATH/Runtime/state.json"

printf 'Installed to %s\n' "$REMOVE_COUNTDOWN_INSTALL_PATH"
