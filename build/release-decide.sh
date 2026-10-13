#!/usr/bin/env bash
# Decides what the Release workflow publishes and writes the answer to $GITHUB_OUTPUT.
#
# - Push to a branch (main, filtered to changelog changes by the workflow): the newest version heading in
#   CHANGELOG.md ("## [1.3.0] - 2026-11-03") is released if it has no tag yet; otherwise nothing happens.
# - Tag push or "Run workflow": the tag itself (INPUT_TAG for a manual run, else the pushed tag).
#
# Inputs (environment): EVENT, REF_TYPE, REF_NAME, INPUT_TAG, GITHUB_SHA, GITHUB_OUTPUT.
# Outputs: skip, tag, version, prerelease, ref (what to check out and tag), new_tag (true when the tag must be created).
set -euo pipefail

out="${GITHUB_OUTPUT:?GITHUB_OUTPUT is not set}"
new_tag=false

if [[ "${EVENT:-}" == "push" && "${REF_TYPE:-}" == "branch" ]]; then
  version="$(grep -m1 -oP '^##\s+\[\K[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(?=\])' CHANGELOG.md || true)"
  if [[ -z "$version" ]]; then
    echo "CHANGELOG.md has no version heading yet; nothing to release."
    echo "skip=true" >> "$out"
    exit 0
  fi
  tag="v$version"
  if git ls-remote --exit-code --tags origin "refs/tags/$tag" > /dev/null; then
    echo "$tag is already tagged; nothing to release. Add a new version heading to CHANGELOG.md to release."
    echo "skip=true" >> "$out"
    exit 0
  fi
  new_tag=true
  ref="${GITHUB_SHA:?GITHUB_SHA is not set}"
else
  tag="${INPUT_TAG:-${REF_NAME:-}}"
  ref="$tag"
fi

if [[ ! "$tag" =~ ^v([0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?)$ ]]; then
  echo "::error::'$tag' is not a version tag like v1.2.3"
  exit 1
fi
version="${BASH_REMATCH[1]}"
prerelease=false
if [[ "$version" == *-* ]]; then
  prerelease=true
fi

{
  echo "skip=false"
  echo "tag=$tag"
  echo "version=$version"
  echo "prerelease=$prerelease"
  echo "ref=$ref"
  echo "new_tag=$new_tag"
} >> "$out"
echo "Releasing $tag (version $version, pre-release: $prerelease, new tag: $new_tag) from $ref."
