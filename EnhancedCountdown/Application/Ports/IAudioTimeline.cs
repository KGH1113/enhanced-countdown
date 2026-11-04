using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.Ports;

internal interface IAudioTimeline
{
  bool IsAvailable { get; }
  double CurrentSongPosition { get; }
  double Crotchet { get; }
  float Pitch { get; }
  double Calibration { get; }
  double EarliestPlaybackDspTime { get; }

  AudioRuntimeSnapshot CaptureAndFreeze();
  void PrimeSongSources(double audioSongPosition);
  bool RebaseAtFrozenTime(double frozenSongPosition, double elapsedSinceInput = 0.0);
  void StartPrimedSources();
  void RefreezePrimedSources();
  void Restore(AudioRuntimeSnapshot snapshot, bool startPrimedSources);
  void RebaseAsyncInputClock();
  void PumpDiagnostics();
}
