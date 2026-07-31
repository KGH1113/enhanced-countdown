#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$REMOVE_COUNTDOWN_PROJECT_ROOT/RemoveCountdown/Info.json" | head -n 1)"
[ -n "$version" ] || fail "RemoveCountdown version is missing from Info.json."
runtime="$REMOVE_COUNTDOWN_PACKAGE_STAGE/Runtime/versions/$version"

require_file "$REMOVE_COUNTDOWN_PACKAGE_STAGE/Info.json"
require_file "$REMOVE_COUNTDOWN_PACKAGE_STAGE/RemoveCountdown.Bootstrap.dll"
require_file "$REMOVE_COUNTDOWN_PACKAGE_STAGE/UpdateSettings.json"
require_file "$REMOVE_COUNTDOWN_PACKAGE_STAGE/Runtime/state.json"
require_file "$runtime/Info.json"
require_file "$runtime/RemoveCountdown.dll"
require_file "$runtime/RemoveCountdown.UpdateEngine.dll"

grep -Fq "\"Current\": \"$version\"" "$REMOVE_COUNTDOWN_PACKAGE_STAGE/Runtime/state.json" || fail "Package state does not select $version."
grep -Fq '"SchemaVersion": 2' "$REMOVE_COUNTDOWN_PACKAGE_STAGE/Runtime/state.json" || fail "Package state schema is invalid."
