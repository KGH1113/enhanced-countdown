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

The command validates local ADOFAI dependencies, builds the mod into `build/RemoveCountdown/`, and installs it
to `$ADOFAI_MODS_DIR/RemoveCountdown/`.

## Check

```bash
./scripts/run.sh check
```

## Package

```bash
./scripts/run.sh package
```

The command builds the Release configuration and writes `build/RemoveCountdown.zip` together with
`build/RemoveCountdown.update.json` containing its version, size, and SHA-256 digest.

There is no start prompt or forced judgment override. Automatic tiles are prepared before the frozen manual input.
