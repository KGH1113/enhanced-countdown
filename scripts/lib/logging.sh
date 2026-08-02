#!/usr/bin/env bash

if [ "${ENHANCED_COUNTDOWN_LOGGING_LOADED:-0}" = "1" ]; then
  return 0
fi
ENHANCED_COUNTDOWN_LOGGING_LOADED=1

log_step() {
  printf '\n==> %s\n' "$1"
}

log_success() {
  printf '    done: %s\n' "$1"
}

run_task() {
  local label="$1"
  shift

  log_step "$label"
  if "$@"; then
    log_success "$label"
  else
    local status=$?
    printf '    failed (%s): %s\n' "$status" "$label" >&2
    return "$status"
  fi
}
