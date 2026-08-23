namespace EnhancedCountdown.Domain.MidRun;

internal readonly struct FrozenVisualWarmupSample
{
  internal FrozenVisualWarmupSample(int pausedTweenCount, double frameDurationSeconds)
  {
    PausedTweenCount = pausedTweenCount;
    FrameDurationSeconds = frameDurationSeconds;
  }

  internal int PausedTweenCount { get; }
  internal double FrameDurationSeconds { get; }
}
