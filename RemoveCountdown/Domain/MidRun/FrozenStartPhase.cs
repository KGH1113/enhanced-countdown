namespace RemoveCountdown.Domain.MidRun;

internal enum FrozenStartPhase
{
  Idle,
  WaitingForScrub,
  WaitingForSchedule,
  Preparing,
  Frozen,
  Releasing,
}
