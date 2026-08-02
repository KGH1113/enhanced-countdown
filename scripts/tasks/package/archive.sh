#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

require_command zip
require_dir "$ENHANCED_COUNTDOWN_PACKAGE_STAGE"

rm -f "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH"
mkdir -p "$(dirname "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH")"
(
  cd "$ENHANCED_COUNTDOWN_PACKAGE_BUILD_ROOT"
  zip -r "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH" EnhancedCountdown
)

printf 'Packaged to %s\n' "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH"
