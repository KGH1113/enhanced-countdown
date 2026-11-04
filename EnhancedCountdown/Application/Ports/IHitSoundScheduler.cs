namespace EnhancedCountdown.Application.Ports;

internal interface IHitSoundScheduler
{
  string GetCompatibilityFailureReason();
  bool Prepare(int frozenFloorIndex);
  void Activate(double earliestPlaybackDspTime);
  void Refreeze();
  void Reset(bool keepInstalledSchedule = false);
}
