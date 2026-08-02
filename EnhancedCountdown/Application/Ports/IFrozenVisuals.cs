using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.Ports;

internal interface IFrozenVisuals
{
  void HideStartUi(scrController controller);
  void ScrubToTime(double logicalSongPosition);
  void StartPreLandingMotion(MetronomePlayback? playback);
  void UpdatePreLandingMotion();
  void RestorePlayer(scrPlayer player);
  void RestoreAll();
}
