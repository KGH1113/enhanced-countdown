using System.Diagnostics;
using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.MidRun;

internal sealed class FrozenStartSession
{
  internal FrozenStartSession(scrController controller)
  {
    Controller = controller;
    Phase = FrozenStartPhase.WaitingForScrub;
    StartupWatch.Start();
  }

  internal scrController Controller { get; }
  internal FrozenStartPhase Phase { get; set; }
  internal AudioRuntimeSnapshot AudioSnapshot { get; set; }
  internal bool HasAudioSnapshot { get; set; }
  internal scrPlayer PendingInputPlayer { get; set; }
  internal scrPlayer UpdatingPlayer { get; set; }
  internal int FrozenFrame { get; set; }
  internal double FrozenSongPosition { get; set; }
  internal double FrozenAudioSongPosition { get; set; }
  internal bool PlaybackStartedForInput { get; set; }
  internal int WarmupStartedFrame { get; set; }
  internal int WarmupRenderedFrames { get; set; }
  internal int WarmupStableFrames { get; set; }
  internal int WarmupTweenCount { get; set; } = -1;
  internal double WarmupFrameDurationSeconds { get; set; }
  internal Stopwatch StartupWatch { get; } = new();
  internal FrozenStartTimings Timings { get; } = new();

  internal void ClearPendingInput()
  {
    PendingInputPlayer = null;
  }
}

internal sealed class FrozenStartTimings
{
  internal double MusicScheduledMilliseconds { get; set; }
  internal double PrepareMilliseconds { get; set; }
  internal int AutomaticTiles { get; set; }
  internal double AutomaticTilesMilliseconds { get; set; }
  internal double VisualScrubMilliseconds { get; set; }
  internal double AudioPrimeMilliseconds { get; set; }
  internal double HitSoundPrepareMilliseconds { get; set; }
}
