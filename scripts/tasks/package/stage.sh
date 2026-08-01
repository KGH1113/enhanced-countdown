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
require_file "$REMOVE_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/RemoveCountdown.Bootstrap.dll"
require_file "$REMOVE_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/RemoveCountdown.UpdateEngine.dll"
for platform in mac win linux; do
  require_file "$REMOVE_COUNTDOWN_ASSET_SOURCE/$platform/enhancedcountdown_ui.bundle"
done

mkdir -p "$REMOVE_COUNTDOWN_PACKAGE_BUILD_ROOT"
if [ -e "$REMOVE_COUNTDOWN_PACKAGE_STAGE" ]; then
  safe_remove_tree "$REMOVE_COUNTDOWN_PACKAGE_STAGE" "$REMOVE_COUNTDOWN_PACKAGE_BUILD_ROOT"
fi
mkdir -p "$REMOVE_COUNTDOWN_PACKAGE_STAGE"

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$REMOVE_COUNTDOWN_PROJECT_ROOT/RemoveCountdown/Info.json" | head -n 1)"
[ -n "$version" ] || fail "RemoveCountdown version is missing from Info.json."
runtime="$REMOVE_COUNTDOWN_PACKAGE_STAGE/Runtime/versions/$version"

mkdir -p "$runtime"
cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/Info.json" "$REMOVE_COUNTDOWN_PACKAGE_STAGE/Info.json"
cp "$REMOVE_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/RemoveCountdown.Bootstrap.dll" "$REMOVE_COUNTDOWN_PACKAGE_STAGE/"
printf '{\n  "ReceiveBetaUpdates": false\n}\n' > "$REMOVE_COUNTDOWN_PACKAGE_STAGE/UpdateSettings.json"
cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/RemoveCountdown.dll" "$runtime/"
cp "$REMOVE_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/RemoveCountdown.UpdateEngine.dll" "$runtime/"
cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/Info.json" "$runtime/Info.json"
cp -R "$REMOVE_COUNTDOWN_ASSET_SOURCE" "$runtime/Assets"

for optional_file in RemoveCountdown.pdb RemoveCountdown.deps.json; do
  if [ -f "$REMOVE_COUNTDOWN_BUILD_OUTPUT/$optional_file" ]; then
    cp "$REMOVE_COUNTDOWN_BUILD_OUTPUT/$optional_file" "$runtime/"
  fi
done

printf '{\n  "SchemaVersion": 2,\n  "Current": "%s",\n  "Previous": null,\n  "Trial": null,\n  "RejectedVersion": null,\n  "FailureCount": 0,\n  "LastFailureUtc": null\n}\n' \
  "$version" > "$REMOVE_COUNTDOWN_PACKAGE_STAGE/Runtime/state.json"
