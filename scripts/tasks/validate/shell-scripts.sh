#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$TASK_DIR/../../.." && pwd)"

while IFS= read -r script; do
  bash -n "$script"
done < <(find "$PROJECT_ROOT/scripts" -type f -name '*.sh' -print)
