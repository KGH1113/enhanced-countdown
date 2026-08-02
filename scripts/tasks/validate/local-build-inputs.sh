#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

require_executable "$DOTNET_EXE"
require_dir "$ADOFAI_MANAGED"
require_dir "$ADOFAI_MODS_DIR"
require_file "$UNITY_MOD_MANAGER_DLL"
require_file "$HARMONY_DLL"
require_file "$ADOFAI_MANAGED/Assembly-CSharp.dll"
require_file "$ADOFAI_MANAGED/RDTools.dll"
require_file "$ADOFAI_MANAGED/UnityEngine.IMGUIModule.dll"
require_file "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown.Bootstrap/EnhancedCountdown.Bootstrap.csproj"
require_file "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown.UpdateEngine/EnhancedCountdown.UpdateEngine.csproj"
