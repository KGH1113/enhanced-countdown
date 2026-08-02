#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

assert_child_path "$ENHANCED_COUNTDOWN_INSTALL_PATH" "$ADOFAI_MODS_DIR"
require_file "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/EnhancedCountdown.dll"
require_file "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/Info.json"
require_file "$ENHANCED_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/EnhancedCountdown.Bootstrap.dll"
require_file "$ENHANCED_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/EnhancedCountdown.UpdateEngine.dll"
for platform in mac win linux; do
  require_file "$ENHANCED_COUNTDOWN_ASSET_SOURCE/$platform/enhancedcountdown_ui.bundle"
done

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/Info.json" | head -n 1)"
[ -n "$version" ] || fail "EnhancedCountdown version is missing from Info.json."

mkdir -p "$ENHANCED_COUNTDOWN_INSTALL_PATH"
if [ -e "$ENHANCED_COUNTDOWN_INSTALL_PATH/Runtime" ]; then
  safe_remove_tree "$ENHANCED_COUNTDOWN_INSTALL_PATH/Runtime" "$ENHANCED_COUNTDOWN_INSTALL_PATH"
fi
rm -f \
  "$ENHANCED_COUNTDOWN_INSTALL_PATH/EnhancedCountdown.dll" \
  "$ENHANCED_COUNTDOWN_INSTALL_PATH/EnhancedCountdown.pdb" \
  "$ENHANCED_COUNTDOWN_INSTALL_PATH/EnhancedCountdown.deps.json" \
  "$ENHANCED_COUNTDOWN_INSTALL_PATH/EnhancedCountdown.Bootstrap.dll" \
  "$ENHANCED_COUNTDOWN_INSTALL_PATH/Info.json"
rm -f "$ENHANCED_COUNTDOWN_INSTALL_PATH"/EnhancedCountdown.dll.*.cache*

runtime="$ENHANCED_COUNTDOWN_INSTALL_PATH/Runtime/versions/$version"
mkdir -p "$runtime"
cp "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/Info.json" "$ENHANCED_COUNTDOWN_INSTALL_PATH/Info.json"
cp "$ENHANCED_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/EnhancedCountdown.Bootstrap.dll" "$ENHANCED_COUNTDOWN_INSTALL_PATH/"
if [ ! -f "$ENHANCED_COUNTDOWN_INSTALL_PATH/UpdateSettings.json" ]; then
  printf '{\n  "ReceiveBetaUpdates": false\n}\n' > "$ENHANCED_COUNTDOWN_INSTALL_PATH/UpdateSettings.json"
fi
cp "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/EnhancedCountdown.dll" "$runtime/"
cp "$ENHANCED_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/EnhancedCountdown.UpdateEngine.dll" "$runtime/"
cp "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/Info.json" "$runtime/Info.json"
cp -R "$ENHANCED_COUNTDOWN_ASSET_SOURCE" "$runtime/Assets"

for optional_file in EnhancedCountdown.pdb EnhancedCountdown.deps.json; do
  if [ -f "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/$optional_file" ]; then
    cp "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/$optional_file" "$runtime/$optional_file"
  fi
done

printf '{\n  "SchemaVersion": 2,\n  "Current": "%s",\n  "Previous": null,\n  "Trial": null,\n  "RejectedVersion": null,\n  "FailureCount": 0,\n  "LastFailureUtc": null\n}\n' \
  "$version" > "$ENHANCED_COUNTDOWN_INSTALL_PATH/Runtime/state.json"

printf 'Installed to %s\n' "$ENHANCED_COUNTDOWN_INSTALL_PATH"
