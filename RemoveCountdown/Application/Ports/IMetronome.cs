using RemoveCountdown.Domain.MidRun;

namespace RemoveCountdown.Application.Ports;

internal interface IMetronome
{
  bool IsEnabledForSession { get; }
  bool IsUiConsumingInput { get; }
  bool ConsumeDisableRequest();
  MetronomePlayback? Start();
  void UpdateDisplay();
  void ResetSessionSettings();
  void Stop(string reason = null);
}
