#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

require_command zip
require_command shasum
require_file "$DOTNET_EXE"
require_dir "$ADOFAI_MANAGED"
require_file "$UNITY_MOD_MANAGER_DLL"
require_file "$HARMONY_DLL"
require_file "$ADOFAI_MANAGED/UnityEngine.IMGUIModule.dll"
require_file "$ADOFAI_MANAGED/UnityEngine.AssetBundleModule.dll"
require_file "$ADOFAI_MANAGED/UnityEngine.InputLegacyModule.dll"
require_file "$ADOFAI_MANAGED/Unity.TextMeshPro.dll"
require_file "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/EnhancedCountdown.csproj"
require_file "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown.Bootstrap/EnhancedCountdown.Bootstrap.csproj"
require_file "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown.UpdateEngine/EnhancedCountdown.UpdateEngine.csproj"
require_file "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/Info.json"
for platform in mac win linux; do
  require_file "$ENHANCED_COUNTDOWN_ASSET_SOURCE/$platform/enhancedcountdown_ui.bundle"
done
