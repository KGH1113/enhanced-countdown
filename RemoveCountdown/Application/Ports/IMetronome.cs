using RemoveCountdown.Domain.MidRun;

namespace RemoveCountdown.Application.Ports;

internal interface IMetronome
{
  MetronomePlayback? Start();
  void Stop(string reason = null);
}
