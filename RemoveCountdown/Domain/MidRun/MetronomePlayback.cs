namespace RemoveCountdown.Domain.MidRun;

internal readonly struct MetronomePlayback
{
  internal MetronomePlayback(
    double originalBpm,
    double clickBpm,
    double startedRealtime,
    double dspStartTime,
    double clickInterval,
    int clickFrames
  )
  {
    OriginalBpm = originalBpm;
    ClickBpm = clickBpm;
    StartedRealtime = startedRealtime;
    DspStartTime = dspStartTime;
    ClickInterval = clickInterval;
    ClickFrames = clickFrames;
  }

  internal double OriginalBpm { get; }
  internal double ClickBpm { get; }
  internal double StartedRealtime { get; }
  internal double DspStartTime { get; }
  internal double ClickInterval { get; }
  internal int ClickFrames { get; }
  internal double OriginalBeatInterval => 60.0 / OriginalBpm;
}
