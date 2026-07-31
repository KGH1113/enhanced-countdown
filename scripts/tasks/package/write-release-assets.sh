#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

require_command shasum
require_file "$REMOVE_COUNTDOWN_PACKAGE_ZIP_PATH"

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$REMOVE_COUNTDOWN_PROJECT_ROOT/RemoveCountdown/Info.json" | head -n 1)"
[ -n "$version" ] || fail "RemoveCountdown version is missing from Info.json."
package_bytes="$(wc -c < "$REMOVE_COUNTDOWN_PACKAGE_ZIP_PATH" | tr -d '[:space:]')"
package_sha256="$(shasum -a 256 "$REMOVE_COUNTDOWN_PACKAGE_ZIP_PATH" | awk '{print $1}')"

mkdir -p "$(dirname "$REMOVE_COUNTDOWN_UPDATE_MANIFEST_PATH")"
printf '{\n  "schemaVersion": 1,\n  "version": "%s",\n  "packageAsset": "RemoveCountdown.zip",\n  "packageBytes": %s,\n  "packageSha256": "%s",\n  "runtimePath": "RemoveCountdown/Runtime/versions/%s"\n}\n' \
  "$version" "$package_bytes" "$package_sha256" "$version" > "$REMOVE_COUNTDOWN_UPDATE_MANIFEST_PATH"

printf 'Update manifest: %s\n' "$REMOVE_COUNTDOWN_UPDATE_MANIFEST_PATH"
