namespace EnhancedCountdown.Infrastructure.Compatibility;

internal static class TufReplayPlaybackDetector
{
  private static readonly StaticBoolProperty PlaybackActive = new(
    "TUFReplay",
    "TUFReplay.ReplayRuntime",
    "IsPlaybackActive"
  );

  internal static bool IsPlaybackActive() => PlaybackActive.Read() == true;
}
