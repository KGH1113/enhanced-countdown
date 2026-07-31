namespace RemoveCountdown.Application.Ports;

internal interface IMetronome
{
  void Start();
  void Stop(string reason = null);
}
