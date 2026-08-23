namespace EnhancedCountdown.Application.Ports;

internal interface IHitSoundScheduler
{
  string GetCompatibilityFailureReason();
  bool Prepare();
  void Activate();
  void Refreeze();
  void Reset(bool keepInstalledSchedule = false);
  void Pump();
}
