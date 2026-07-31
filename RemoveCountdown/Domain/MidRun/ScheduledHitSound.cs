namespace RemoveCountdown.Domain.MidRun;

internal readonly struct ScheduledHitSound
{
  internal ScheduledHitSound(string soundName, double time, float volume)
  {
    SoundName = soundName;
    Time = time;
    Volume = volume;
  }

  internal string SoundName { get; }
  internal double Time { get; }
  internal float Volume { get; }
}
