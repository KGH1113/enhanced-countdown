# Remove Countdown

A UnityModManager mod for A Dance of Fire and Ice that replaces the countdown and lead-in for editor runs started
from a middle tile. After loading the real run state, it freezes the planets at the next manual tile's Pure Perfect
timestamp and repeats the game's countdown hat at a power-of-two-normalized 200–500 BPM. The first input stops the
metronome, follows the game's normal input path, then music and gameplay continue normally.

## Setup

1. Copy `.env.example` to `.env` and adjust the ADOFAI paths if needed.
2. Run `dotnet tool restore` to install the pinned formatter.

## Build and install

```bash
./scripts/run.sh build
```

The command validates local ADOFAI dependencies, builds the fixed launcher, versioned update engine, and mod payload,
then installs them under `$ADOFAI_MODS_DIR/RemoveCountdown/`. The runtime lives at
`Runtime/versions/<version>`; `UpdateSettings.json` remains at the mod root.

Version `0.2.0` is the first full-runtime updater baseline and must be installed manually once. Later releases can
update in place from GitHub Releases in [`KGH1113/enhanced-countdown`](https://github.com/KGH1113/enhanced-countdown).

At game startup, the fixed launcher runs the current version's update engine before loading the payload. The engine
selects a release, verifies its tag and `RemoveCountdown.update.json`, checks the complete ZIP size and SHA-256,
extracts it into a versioned runtime, and activates it only after the payload initializes successfully. Network,
manifest, and package failures load the current runtime. A runtime that fails initialization is quarantined and is
not retried; publish a higher version to replace it. The current and previous successful runtimes are retained.

The Unity Mod Manager GUI includes a `Receive beta updates` toggle. It is disabled by default and saved to
`UpdateSettings.json`; changes apply on the next game launch. Disabling beta updates never downgrades an installed
prerelease.

## Check

```bash
./scripts/run.sh check
```

## Package

```bash
./scripts/run.sh package
```

The command builds and tests the Release configuration, verifies the launcher/runtime package layout, and writes:

- `build/RemoveCountdown.zip`
- `build/RemoveCountdown.update.json`

The manifest contains the version, package size, SHA-256, and `RemoveCountdown/Runtime/versions/<version>` path.
Release assets are immutable: never replace the assets attached to an existing version.

## Publish

```bash
./scripts/run.sh publish --check
./scripts/run.sh publish
```

Publishing requires an authenticated GitHub CLI, a clean `main` branch equal to `origin/main`, and package artifacts
from `./scripts/run.sh package`. The command creates a draft `v<version>` release, uploads and verifies both assets,
then publishes it. Versions containing a SemVer prerelease suffix are marked as prereleases; other versions become
stable releases. A failed verification leaves the release as a draft.

There is no start prompt or forced judgment override. Automatic tiles are prepared before the frozen manual input.
