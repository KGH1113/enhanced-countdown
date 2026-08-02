#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"

configuration="${1:-Debug}"

mkdir -p "$ENHANCED_COUNTDOWN_BUILD_OUTPUT"
DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
  "$DOTNET_EXE" build "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/EnhancedCountdown.csproj" \
    --configuration "$configuration" \
    -p:OutputPath="$ENHANCED_COUNTDOWN_BUILD_OUTPUT/" \
    -p:AdofaiManaged="$ADOFAI_MANAGED" \
    -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL" \
    -p:HarmonyDll="$HARMONY_DLL"

cp "$ENHANCED_COUNTDOWN_PROJECT_ROOT/EnhancedCountdown/Info.json" "$ENHANCED_COUNTDOWN_BUILD_OUTPUT/Info.json"
