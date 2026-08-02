#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"

configuration="${1:-Debug}"

mkdir -p "$ENHANCED_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT"
DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
  "$DOTNET_EXE" build "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown.Bootstrap/EnhancedCountdown.Bootstrap.csproj" \
    --configuration "$configuration" \
    -p:OutputPath="$ENHANCED_COUNTDOWN_BOOTSTRAP_BUILD_OUTPUT/" \
    -p:AdofaiManaged="$ADOFAI_MANAGED" \
    -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL"
