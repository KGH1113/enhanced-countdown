namespace EnhancedCountdown.Domain.MidRun;

internal enum FrozenStartPhase
{
  Idle,
  WaitingForScrub,
  WaitingForSchedule,
  Preparing,
  Warming,
  Frozen,
  Releasing,
}
