using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.Ports;

internal interface IAudioTimeline
{
  bool IsAvailable { get; }
  double CurrentSongPosition { get; }
  double Crotchet { get; }
  float Pitch { get; }
  double Calibration { get; }

  AudioRuntimeSnapshot CaptureAndFreeze();
  void PrimeSongSources();
  void PauseListener();
  bool RebaseAtFrozenTime(double frozenSongPosition, double elapsedSinceFirstInput = 0.0);
  void ReleasePrimedSources();
  void RefreezePrimedSources();
  void Restore(AudioRuntimeSnapshot snapshot, bool unpausePrimedSources);
  void RebaseAsyncInputClock();
}
