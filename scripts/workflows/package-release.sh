#!/usr/bin/env bash
set -euo pipefail

WORKFLOW_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPTS_DIR="$(cd "$WORKFLOW_DIR/.." && pwd)"
TASKS_DIR="$SCRIPTS_DIR/tasks"
# shellcheck source=../lib/context.sh
source "$SCRIPTS_DIR/lib/context.sh"
# shellcheck source=../lib/logging.sh
source "$SCRIPTS_DIR/lib/logging.sh"

run_task "Validate release package inputs" "$TASKS_DIR/validate/release-package-inputs.sh"
run_task "Build mod (Release)" "$TASKS_DIR/build/mod.sh" Release
run_task "Stage package" "$TASKS_DIR/package/stage.sh"
run_task "Create package archive" "$TASKS_DIR/package/archive.sh"
run_task "Write release metadata" "$TASKS_DIR/package/write-release-assets.sh"
