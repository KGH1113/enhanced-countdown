#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"

[ -f "$ENHANCED_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/EnhancedCountdown.Bootstrap.dll" ]
[ -f "$ENHANCED_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/EnhancedCountdown.UpdateEngine.dll" ]

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
  "$DOTNET_EXE" run --project "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown.UpdateTests/EnhancedCountdown.UpdateTests.csproj" \
    --configuration Release \
    -p:AdofaiManaged="$ADOFAI_MANAGED" \
    -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL" \
    -p:EnhancedCountdownBootstrapDll="$ENHANCED_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/EnhancedCountdown.Bootstrap.dll" \
    -p:EnhancedCountdownUpdateEngineDll="$ENHANCED_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/EnhancedCountdown.UpdateEngine.dll"
