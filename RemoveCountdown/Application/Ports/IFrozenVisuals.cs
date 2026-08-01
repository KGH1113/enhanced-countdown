using RemoveCountdown.Domain.MidRun;

namespace RemoveCountdown.Application.Ports;

internal interface IFrozenVisuals
{
  void HideStartUi(scrController controller);
  void ScrubToTime(double logicalSongPosition);
  void StartPreLandingMotion(MetronomePlayback? playback);
  void UpdatePreLandingMotion();
  void RestorePlayer(scrPlayer player);
  void RestoreAll();
}
