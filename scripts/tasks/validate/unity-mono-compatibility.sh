#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

[ "$#" -ge 3 ] || fail "Usage: unity-mono-compatibility.sh CORE_DLL BOOTSTRAP_DLL UPDATE_ENGINE_DLL"

require_command grep

for assembly in "$@"; do
  require_file "$assembly"
  if LC_ALL=C grep -aFq 'DefaultInterpolatedStringHandler' "$assembly"; then
    fail "Unity/Mono-incompatible interpolated string handler found in $assembly"
  fi
  if LC_ALL=C grep -aFq 'ToStringAndClear' "$assembly"; then
    fail "Unity/Mono-incompatible interpolated string handler call found in $assembly"
  fi
done

printf 'Unity/Mono compatibility verified for %s assemblies.\n' "$#"
