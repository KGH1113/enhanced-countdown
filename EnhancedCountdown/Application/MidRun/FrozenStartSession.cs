using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.MidRun;

internal sealed class FrozenStartSession
{
  internal FrozenStartSession(scrController controller)
  {
    Controller = controller;
    Phase = FrozenStartPhase.WaitingForScrub;
  }

  internal scrController Controller { get; }
  internal FrozenStartPhase Phase { get; set; }
  internal AudioRuntimeSnapshot AudioSnapshot { get; set; }
  internal bool HasAudioSnapshot { get; set; }
  internal scrPlayer PendingInputPlayer { get; set; }
  internal ulong? PendingInputTick { get; set; }
  internal int FrozenFrame { get; set; }
  internal double FrozenSongPosition { get; set; }
  internal double FrozenAudioSongPosition { get; set; }
  internal bool AudioReleasedForInput { get; set; }
  internal bool TimelineRebasedForInput { get; set; }

  internal void ClearPendingInput()
  {
    PendingInputPlayer = null;
    PendingInputTick = null;
  }
}
