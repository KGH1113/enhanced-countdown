using System;
using EnhancedCountdown.Application.Ports;
using EnhancedCountdown.Domain.MidRun;
using UnityEngine;

namespace EnhancedCountdown.Infrastructure.Adofai;

internal sealed class AdofaiHitSoundScheduler : IHitSoundScheduler
{
  private readonly ConductorHitSoundAccessor accessor;
  private readonly IModLogger logger;

  internal AdofaiHitSoundScheduler(ConductorHitSoundAccessor accessor, IModLogger logger)
  {
    this.accessor = accessor;
    this.logger = logger;
  }

  public void RebuildFromCheckpoint()
  {
    scrConductor conductor = ADOBase.conductor;
    if (conductor == null)
    {
      return;
    }

    AudioManager.Instance.StopAllSounds();
    double currentDspTime = conductor.dspTime;
    ScheduledHitSound? missedHitSound = null;
    try
    {
      conductor.dspTime = double.NegativeInfinity;
      conductor.PlayHitTimes();
      missedHitSound = accessor.CaptureFirstElapsed(conductor, currentDspTime);

      conductor.dspTime = currentDspTime;
      conductor.PlayHitTimes();
    }
    finally
    {
      conductor.dspTime = currentDspTime;
    }

    if (missedHitSound is ScheduledHitSound hitSound)
    {
      double playbackTime = Math.Max(currentDspTime, AudioSettings.dspTime);
      AudioManager.Play("snd" + hitSound.SoundName, playbackTime, conductor.hitSoundGroup, hitSound.Volume);
      logger.Log(
        $"Restored frozen-start hit sound {hitSound.SoundName} "
          + $"{playbackTime - hitSound.Time:F6}s after its scheduled time."
      );
    }
  }
}
