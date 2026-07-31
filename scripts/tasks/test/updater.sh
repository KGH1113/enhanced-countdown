#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"

[ -f "$REMOVE_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/RemoveCountdown.Bootstrap.dll" ]
[ -f "$REMOVE_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/RemoveCountdown.UpdateEngine.dll" ]

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
  "$DOTNET_EXE" run --project "$REMOVE_COUNTDOWN_PROJECT_ROOT/RemoveCountdown.UpdateTests/RemoveCountdown.UpdateTests.csproj" \
    --configuration Release \
    -p:AdofaiManaged="$ADOFAI_MANAGED" \
    -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL" \
    -p:RemoveCountdownBootstrapDll="$REMOVE_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/RemoveCountdown.Bootstrap.dll" \
    -p:RemoveCountdownUpdateEngineDll="$REMOVE_COUNTDOWN_UPDATE_ENGINE_BUILD_OUTPUT/RemoveCountdown.UpdateEngine.dll"
