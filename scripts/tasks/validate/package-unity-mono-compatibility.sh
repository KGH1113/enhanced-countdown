#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

require_command grep
require_command unzip
require_file "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH"

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/Info.json" | head -n 1)"
[ -n "$version" ] || fail "EnhancedCountdown version is missing from Info.json."

entries=(
  "EnhancedCountdown/EnhancedCountdown.Bootstrap.dll"
  "EnhancedCountdown/Runtime/versions/$version/EnhancedCountdown.dll"
  "EnhancedCountdown/Runtime/versions/$version/EnhancedCountdown.UpdateEngine.dll"
)

for entry in "${entries[@]}"; do
  unzip -Z1 "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH" | grep -Fx "$entry" >/dev/null || fail "Package assembly is missing: $entry"
  if unzip -p "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH" "$entry" | LC_ALL=C grep -aF 'DefaultInterpolatedStringHandler' >/dev/null; then
    fail "Unity/Mono-incompatible interpolated string handler found in package entry $entry"
  fi
  if unzip -p "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH" "$entry" | LC_ALL=C grep -aF 'ToStringAndClear' >/dev/null; then
    fail "Unity/Mono-incompatible interpolated string handler call found in package entry $entry"
  fi
done

printf 'Final package Unity/Mono compatibility verified for %s assemblies.\n' "${#entries[@]}"
