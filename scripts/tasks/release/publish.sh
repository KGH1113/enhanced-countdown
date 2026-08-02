#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

repository="KGH1113/enhanced-countdown"
expected_origin="https://github.com/$repository.git"
check_only=0
if [ "${1:-}" = "--check" ]; then
  check_only=1
elif [ "$#" -ne 0 ]; then
  fail "Usage: ./scripts/run.sh publish [--check]"
fi

require_command git
require_command gh
require_command shasum
require_file "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH"
require_file "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH"

cd "$ENHANCED_COUNTDOWN_PROJECT_ROOT"
[ "$(git branch --show-current)" = "main" ] || fail "Releases must be published from main."
[ -z "$(git status --porcelain)" ] || fail "The worktree must be clean before publishing."

origin_url="$(git remote get-url origin)"
case "$origin_url" in
  "$expected_origin"|"git@github.com:$repository.git") ;;
  *) fail "origin must point to $expected_origin (found $origin_url)." ;;
esac

gh auth status --hostname github.com >/dev/null
git fetch origin main --tags
head_sha="$(git rev-parse HEAD)"
origin_sha="$(git rev-parse origin/main)"
[ "$head_sha" = "$origin_sha" ] || fail "HEAD must match origin/main before publishing."

version="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' EnhancedCountdown/Info.json | head -n 1)"
[ -n "$version" ] || fail "EnhancedCountdown version is missing from Info.json."
tag="v$version"

manifest_version="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH" | head -n 1)"
manifest_bytes="$(sed -n 's/.*"packageBytes"[[:space:]]*:[[:space:]]*\([0-9][0-9]*\).*/\1/p' "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH" | head -n 1)"
manifest_sha="$(sed -n 's/.*"packageSha256"[[:space:]]*:[[:space:]]*"\([0-9a-fA-F]*\)".*/\1/p' "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH" | head -n 1)"
package_bytes="$(wc -c < "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH" | tr -d '[:space:]')"
package_sha="$(shasum -a 256 "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH" | awk '{print $1}')"

[ "$manifest_version" = "$version" ] || fail "Info.json and update manifest versions do not match."
[ "$manifest_bytes" = "$package_bytes" ] || fail "Update manifest package size does not match the ZIP."
[ "$(printf '%s' "$manifest_sha" | tr '[:upper:]' '[:lower:]')" = "$package_sha" ] || fail "Update manifest checksum does not match the ZIP."
grep -Fq '"packageAsset": "EnhancedCountdown.zip"' "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH" || fail "Unexpected package asset name."
grep -Fq "\"runtimePath\": \"EnhancedCountdown/Runtime/versions/$version\"" "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH" || fail "Unexpected runtime path."

if gh release view "$tag" --repo "$repository" >/dev/null 2>&1; then
  fail "GitHub release $tag already exists. Releases are immutable."
fi
if git ls-remote --exit-code --tags origin "refs/tags/$tag" >/dev/null 2>&1; then
  fail "Git tag $tag already exists."
fi

if [ "$check_only" = "1" ]; then
  printf 'Release preflight passed for %s.\n' "$tag"
  exit 0
fi

release_args=(
  release create "$tag"
  "$ENHANCED_COUNTDOWN_PACKAGE_ZIP_PATH#EnhancedCountdown.zip"
  "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH#EnhancedCountdown.update.json"
  --repo "$repository"
  --target "$head_sha"
  --title "$tag"
  --generate-notes
  --draft
)
case "$version" in
  *-*) release_args+=(--prerelease) ;;
esac
gh "${release_args[@]}"

asset_count="$(gh release view "$tag" --repo "$repository" --json assets --jq '[.assets[] | select(.name == "EnhancedCountdown.zip" or .name == "EnhancedCountdown.update.json")] | length')"
[ "$asset_count" = "2" ] || fail "The draft release does not contain both required assets."
remote_zip_bytes="$(gh release view "$tag" --repo "$repository" --json assets --jq '.assets[] | select(.name == "EnhancedCountdown.zip") | .size')"
remote_manifest_bytes="$(gh release view "$tag" --repo "$repository" --json assets --jq '.assets[] | select(.name == "EnhancedCountdown.update.json") | .size')"
[ "$remote_zip_bytes" = "$package_bytes" ] || fail "The uploaded ZIP size is incorrect."
[ "$remote_manifest_bytes" = "$(wc -c < "$ENHANCED_COUNTDOWN_UPDATE_MANIFEST_PATH" | tr -d '[:space:]')" ] || fail "The uploaded manifest size is incorrect."

gh release edit "$tag" --repo "$repository" --draft=false
printf 'Published %s to https://github.com/%s/releases/tag/%s\n' "$tag" "$repository" "$tag"
