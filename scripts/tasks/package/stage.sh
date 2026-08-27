#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

assert_non_root_path "$ENHANCED_COUNTDOWN_PACKAGE_BUILD_ROOT"
assert_child_path "$ENHANCED_COUNTDOWN_PACKAGE_STAGE" "$ENHANCED_COUNTDOWN_PACKAGE_BUILD_ROOT"
require_file "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/EnhancedCountdown.dll"
require_file "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/Info.json"
require_file "$ENHANCED_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/EnhancedCountdown.Bootstrap.dll"
require_file "$ENHANCED_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/EnhancedCountdown.UpdateEngine.dll"
require_file "$ENHANCED_COUNTDOWN_PROJECT_ROOT/LICENSE.md"
require_file "$ENHANCED_COUNTDOWN_PROJECT_ROOT/THIRD_PARTY_NOTICES.md"
for platform in mac win linux; do
  require_file "$ENHANCED_COUNTDOWN_ASSET_SOURCE/$platform/enhancedcountdown_ui.bundle"
done

mkdir -p "$ENHANCED_COUNTDOWN_PACKAGE_BUILD_ROOT"
if [ -e "$ENHANCED_COUNTDOWN_PACKAGE_STAGE" ]; then
  safe_remove_tree "$ENHANCED_COUNTDOWN_PACKAGE_STAGE" "$ENHANCED_COUNTDOWN_PACKAGE_BUILD_ROOT"
fi
mkdir -p "$ENHANCED_COUNTDOWN_PACKAGE_STAGE"

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/Info.json" | head -n 1)"
[ -n "$version" ] || fail "EnhancedCountdown version is missing from Info.json."
runtime="$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Runtime/versions/$version"

mkdir -p "$runtime"
cp "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/Info.json" "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Info.json"
cp "$ENHANCED_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/EnhancedCountdown.Bootstrap.dll" "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/"
cp "$ENHANCED_COUNTDOWN_PROJECT_ROOT/LICENSE.md" "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/"
cp "$ENHANCED_COUNTDOWN_PROJECT_ROOT/THIRD_PARTY_NOTICES.md" "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/"
printf '{\n  "ReceiveBetaUpdates": false\n}\n' > "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/UpdateSettings.json"
cp "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/EnhancedCountdown.dll" "$runtime/"
cp "$ENHANCED_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/EnhancedCountdown.UpdateEngine.dll" "$runtime/"
cp "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/Info.json" "$runtime/Info.json"
cp -R "$ENHANCED_COUNTDOWN_ASSET_SOURCE" "$runtime/Assets"

for optional_file in EnhancedCountdown.pdb EnhancedCountdown.deps.json; do
  if [ -f "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/$optional_file" ]; then
    cp "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/$optional_file" "$runtime/"
  fi
done

printf '{\n  "SchemaVersion": 2,\n  "Current": "%s",\n  "Previous": null,\n  "Trial": null,\n  "RejectedVersion": null,\n  "FailureCount": 0,\n  "LastFailureUtc": null\n}\n' \
  "$version" > "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Runtime/state.json"
