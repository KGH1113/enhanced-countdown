namespace RemoveCountdown.Application.Ports;

internal interface IFrozenVisuals
{
  void HideStartUi(scrController controller);
  void ScrubToTime(double logicalSongPosition);
  void ApplyPreLandingOffset();
  void RestorePlayer(scrPlayer player);
  void RestoreAll();
}
