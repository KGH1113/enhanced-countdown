using System;
using RemoveCountdown.Application.Ports;
using UnityModManagerNet;

namespace RemoveCountdown.Bootstrap;

internal sealed class ModLogger : IModLogger
{
  private readonly UnityModManager.ModEntry entry;

  internal ModLogger(UnityModManager.ModEntry entry)
  {
    this.entry = entry;
  }

  public void Log(string message)
  {
    entry.Logger.Log(message);
  }

  public void LogError(string context, Exception exception)
  {
    entry.Logger.Error($"[{context}] {exception}");
  }
}
