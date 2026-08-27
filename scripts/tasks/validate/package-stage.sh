#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/Info.json" | head -n 1)"
[ -n "$version" ] || fail "EnhancedCountdown version is missing from Info.json."
runtime="$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Runtime/versions/$version"

require_file "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Info.json"
require_file "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/EnhancedCountdown.Bootstrap.dll"
require_file "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/LICENSE.md"
require_file "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/THIRD_PARTY_NOTICES.md"
require_file "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/UpdateSettings.json"
require_file "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Runtime/state.json"
require_file "$runtime/Info.json"
require_file "$runtime/EnhancedCountdown.dll"
require_file "$runtime/EnhancedCountdown.UpdateEngine.dll"
for platform in mac win linux; do
  require_file "$runtime/Assets/$platform/enhancedcountdown_ui.bundle"
done

grep -Fq "\"Current\": \"$version\"" "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Runtime/state.json" || fail "Package state does not select $version."
grep -Fq '"SchemaVersion": 2' "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Runtime/state.json" || fail "Package state schema is invalid."
