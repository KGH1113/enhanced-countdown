using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.Ports;

internal interface IFrozenVisuals
{
  void ClearHitTexts();
  void HideStartUi(scrController controller);
  void ScrubToTime(double logicalSongPosition);
  FrozenVisualWarmupSample CaptureWarmupSample();
  void StartPreLandingMotion(MetronomePlayback? playback);
  void UpdatePreLandingMotion();
  void RestorePlayer(scrPlayer player);
  void ResumePreparedEffects();
  void RefreezePreparedEffects();
  void RestoreAll();
}
