using System;

namespace EnhancedCountdown.Application.Ports;

internal interface IModLogger
{
  void Log(string message);
  void LogError(string context, Exception exception);
}
