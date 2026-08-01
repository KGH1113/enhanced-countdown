namespace RemoveCountdown.Domain.MidRun;

internal readonly struct MetronomePlayback
{
  internal MetronomePlayback(double originalBpm, double normalizedBpm, double startedRealtime)
  {
    OriginalBpm = originalBpm;
    NormalizedBpm = normalizedBpm;
    StartedRealtime = startedRealtime;
  }

  internal double OriginalBpm { get; }
  internal double NormalizedBpm { get; }
  internal double StartedRealtime { get; }
  internal double OriginalBeatInterval => 60.0 / OriginalBpm;
}
