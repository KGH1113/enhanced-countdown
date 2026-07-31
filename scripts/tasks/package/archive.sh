#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

require_command zip
require_dir "$REMOVE_COUNTDOWN_PACKAGE_STAGE"

rm -f "$REMOVE_COUNTDOWN_PACKAGE_ZIP_PATH"
mkdir -p "$(dirname "$REMOVE_COUNTDOWN_PACKAGE_ZIP_PATH")"
(
  cd "$REMOVE_COUNTDOWN_PACKAGE_BUILD_ROOT"
  zip -r "$REMOVE_COUNTDOWN_PACKAGE_ZIP_PATH" RemoveCountdown
)

printf 'Packaged to %s\n' "$REMOVE_COUNTDOWN_PACKAGE_ZIP_PATH"
