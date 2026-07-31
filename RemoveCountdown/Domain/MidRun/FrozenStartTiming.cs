using System;

namespace RemoveCountdown.Domain.MidRun;

internal readonly struct PerfectTimingInput
{
  internal PerfectTimingInput(
    double lastHit,
    double targetExitAngle,
    double snappedLastAngle,
    bool isClockwise,
    double crotchet,
    double speed
  )
  {
    LastHit = lastHit;
    TargetExitAngle = targetExitAngle;
    SnappedLastAngle = snappedLastAngle;
    IsClockwise = isClockwise;
    Crotchet = crotchet;
    Speed = speed;
  }

  internal double LastHit { get; }
  internal double TargetExitAngle { get; }
  internal double SnappedLastAngle { get; }
  internal bool IsClockwise { get; }
  internal double Crotchet { get; }
  internal double Speed { get; }
}

internal static class FrozenStartTiming
{
  internal static double CalculatePerfectSongPosition(PerfectTimingInput input)
  {
    if (input.Speed == 0f)
    {
      throw new InvalidOperationException("A non-zero planet speed is required to calculate PP time.");
    }

    double direction = input.IsClockwise ? 1.0 : -1.0;
    return input.LastHit
      + (input.TargetExitAngle - input.SnappedLastAngle) * direction / Math.PI * input.Crotchet / input.Speed;
  }

  internal static double CalculateCalibratedAudioSongPosition(
    double logicalSongPosition,
    double calibration,
    float pitch
  )
  {
    return logicalSongPosition + calibration * pitch;
  }
}
