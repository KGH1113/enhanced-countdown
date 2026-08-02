#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

require_command shasum
require_file "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH"

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/Info.json" | head -n 1)"
[ -n "$version" ] || fail "EnhancedCountdown version is missing from Info.json."
package_bytes="$(wc -c < "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH" | tr -d '[:space:]')"
package_sha256="$(shasum -a 256 "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH" | awk '{print $1}')"

mkdir -p "$(dirname "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH")"
printf '{\n  "schemaVersion": 1,\n  "version": "%s",\n  "packageAsset": "EnhancedCountdown.zip",\n  "packageBytes": %s,\n  "packageSha256": "%s",\n  "runtimePath": "EnhancedCountdown/Runtime/versions/%s"\n}\n' \
  "$version" "$package_bytes" "$package_sha256" "$version" > "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH"

printf 'Update manifest: %s\n' "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH"
