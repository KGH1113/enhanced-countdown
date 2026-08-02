namespace EnhancedCountdown.Domain.MidRun;

internal readonly struct AudioRuntimeSnapshot
{
  internal AudioRuntimeSnapshot(float timeScale, bool listenerPaused, bool conductorEnabled, double songPosition)
  {
    TimeScale = timeScale;
    ListenerPaused = listenerPaused;
    ConductorEnabled = conductorEnabled;
    SongPosition = songPosition;
  }

  internal float TimeScale { get; }
  internal bool ListenerPaused { get; }
  internal bool ConductorEnabled { get; }
  internal double SongPosition { get; }
}
