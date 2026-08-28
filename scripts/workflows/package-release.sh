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
run_task "Build bootstrap (Release)" "$TASKS_DIR/build/bootstrap.sh" Release
run_task "Build update engine (Release)" "$TASKS_DIR/build/update-engine.sh" Release
run_task "Build mod (Release)" "$TASKS_DIR/build/mod.sh" Release
run_task "Validate Unity/Mono compatibility" \
  "$TASKS_DIR/validate/unity-mono-compatibility.sh" \
  "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/EnhancedCountdown.dll" \
  "$ENHANCED_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/EnhancedCountdown.Bootstrap.dll" \
  "$ENHANCED_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/EnhancedCountdown.UpdateEngine.dll"
run_task "Run updater tests" "$TASKS_DIR/test/updater.sh"
run_task "Stage package" "$TASKS_DIR/package/stage.sh"
run_task "Validate package layout" "$TASKS_DIR/validate/package-stage.sh"
version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/Info.json" | head -n 1)"
run_task "Validate staged Unity/Mono compatibility" \
  "$TASKS_DIR/validate/unity-mono-compatibility.sh" \
  "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Runtime/versions/$version/EnhancedCountdown.dll" \
  "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/EnhancedCountdown.Bootstrap.dll" \
  "$ENHANCED_COUNTDOWN_PACKAGE_STAGE/Runtime/versions/$version/EnhancedCountdown.UpdateEngine.dll"
run_task "Create package archive" "$TASKS_DIR/package/archive.sh"
run_task "Validate final package Unity/Mono compatibility" \
  "$TASKS_DIR/validate/package-unity-mono-compatibility.sh"
run_task "Write release metadata" "$TASKS_DIR/package/write-release-assets.sh"
