using System;
using System.Collections;
using System.Reflection;
using EnhancedCountdown.Application.Ports;
using EnhancedCountdown.Domain.MidRun;
using HarmonyLib;
using UnityEngine;

namespace EnhancedCountdown.Infrastructure.Adofai;

internal sealed class AdofaiHitSoundScheduler : IHitSoundScheduler
{
  private static readonly FieldInfo HitSoundsDataField = AccessTools.Field(typeof(scrConductor), "hitSoundsData");
  private static readonly FieldInfo NextHitSoundField = AccessTools.Field(
    typeof(scrConductor),
    "nextHitSoundToSchedule"
  );
  private static readonly FieldInfo HoldSoundsDataField = AccessTools.Field(typeof(scrConductor), "holdSoundsData");
  private static readonly FieldInfo NextHoldSoundField = AccessTools.Field(
    typeof(scrConductor),
    "nextHoldSoundToSchedule"
  );
  private static readonly FieldInfo ExtraTicksField = AccessTools.Field(typeof(scrConductor), "extraTicksCountdown");
  private static readonly FieldInfo NextExtraTickField = AccessTools.Field(
    typeof(scrConductor),
    "nextExtraTickToSchedule"
  );
  private static readonly FieldInfo CountdownTimesField = AccessTools.Field(typeof(scrConductor), "countdownTimes");
  private static readonly FieldInfo PlayCountdownHihatsField = AccessTools.Field(
    typeof(scrConductor),
    "playCountdownHihats"
  );
  private static readonly FieldInfo PlayEndingCymbalField = AccessTools.Field(typeof(scrConductor), "playEndingCymbal");

  private readonly ConductorHitSoundAccessor accessor;
  private readonly IModLogger logger;
  private scrConductor conductor;
  private IList hitSounds;
  private IList holdSounds;
  private IList extraTicks;
  private double[] countdownTimes;
  private double scheduleDspTimeSong;
  private double endingCymbalTime;
  private ScheduledHitSound? missedHitSound;
  private bool playCountdownHihats;
  private bool playEndingCymbal;
  private bool prepared;
  private bool active;

  internal AdofaiHitSoundScheduler(ConductorHitSoundAccessor accessor, IModLogger logger)
  {
    this.accessor = accessor;
    this.logger = logger;
  }

  public string GetCompatibilityFailureReason()
  {
    return ReflectionContractIsAvailable() ? null : "the hit-sound schedule layout is incompatible";
  }

  public bool Prepare()
  {
    Reset();
    conductor = ADOBase.conductor;
    if (conductor == null || !ReflectionContractIsAvailable())
    {
      conductor = null;
      return false;
    }

    try
    {
      AudioManager.Instance.StopAllSounds();
      double currentDspTime = conductor.dspTime;
      try
      {
        conductor.dspTime = double.NegativeInfinity;
        conductor.PlayHitTimes();
      }
      finally
      {
        conductor.dspTime = currentDspTime;
      }

      hitSounds = HitSoundsDataField.GetValue(conductor) as IList;
      holdSounds = HoldSoundsDataField.GetValue(conductor) as IList;
      extraTicks = ExtraTicksField.GetValue(conductor) as IList;
      if (hitSounds == null || holdSounds == null || extraTicks == null)
      {
        throw new InvalidOperationException("The conductor sound schedule lists are unavailable.");
      }

      missedHitSound = accessor.CaptureFirstElapsed(conductor, currentDspTime);
      SetFirstFutureIndex(hitSounds, NextHitSoundField, currentDspTime);
      SetFirstFutureIndex(holdSounds, NextHoldSoundField, currentDspTime);
      SetFirstFutureIndex(extraTicks, NextExtraTickField, currentDspTime);

      countdownTimes = (CountdownTimesField.GetValue(conductor) as double[])?.Clone() as double[];
      playCountdownHihats = PlayCountdownHihatsField.GetValue(conductor) is true && !conductor.fastTakeoff;
      playEndingCymbal = PlayEndingCymbalField.GetValue(conductor) is true;
      endingCymbalTime = CalculateEndingCymbalTime();
      scheduleDspTimeSong = conductor.dspTimeSong;
      prepared = true;
      active = false;

      // PlayHitTimes schedules the short countdown and ending cymbal immediately.
      // Keep only the captured schedule while the listener and timeline are frozen.
      AudioManager.Instance.StopAllSounds();
      return true;
    }
    catch (Exception exception)
    {
      logger.LogError("Could not prepare the frozen hit-sound schedule", exception);
      scrConductor failedConductor = conductor;
      ClearState();
      if (failedConductor != null)
      {
        try
        {
          failedConductor.PlayHitTimes();
        }
        catch (Exception restoreException)
        {
          logger.LogError("Could not restore hit sounds after schedule preparation failed", restoreException);
        }
      }
      return false;
    }
  }

  public void Activate()
  {
    if (!prepared || active || conductor == null)
    {
      return;
    }

    try
    {
      double shift = conductor.dspTimeSong - scheduleDspTimeSong;
      ShiftSchedule(hitSounds, shift);
      ShiftSchedule(holdSounds, shift);
      ShiftSchedule(extraTicks, shift);
      ShiftCountdownTimes(shift);
      endingCymbalTime += shift;
      scheduleDspTimeSong = conductor.dspTimeSong;

      double now = Math.Max(conductor.dspTime, AudioSettings.dspTime);
      if (missedHitSound is ScheduledHitSound hitSound)
      {
        AudioManager.Play("snd" + hitSound.SoundName, now, conductor.hitSoundGroup, hitSound.Volume);
      }
      ScheduleDirectSounds(now);
      active = true;
    }
    catch (Exception exception)
    {
      logger.LogError("Could not activate the prepared hit-sound schedule", exception);
      scrConductor failedConductor = conductor;
      ClearState();
      try
      {
        failedConductor?.PlayHitTimes();
      }
      catch (Exception restoreException)
      {
        logger.LogError("Could not fall back to the native hit-sound schedule", restoreException);
      }
    }
  }

  public void Refreeze()
  {
    if (!prepared || !active)
    {
      return;
    }
    AudioManager.Instance.StopAllSounds();
    active = false;
  }

  public void Reset(bool keepInstalledSchedule = false)
  {
    if (!prepared)
    {
      ClearState();
      return;
    }

    scrConductor preparedConductor = conductor;
    bool rebuildNativeSchedule = !keepInstalledSchedule && preparedConductor != null;
    ClearState();
    if (rebuildNativeSchedule)
    {
      try
      {
        AudioManager.Instance.StopAllSounds();
        preparedConductor.PlayHitTimes();
      }
      catch (Exception exception)
      {
        logger.LogError("Could not restore the native hit-sound schedule", exception);
      }
    }
  }

  public void Pump()
  {
    // Once activated, scrConductor consumes the prepared private lists normally.
  }

  private static bool ReflectionContractIsAvailable()
  {
    return HitSoundsDataField != null
      && NextHitSoundField != null
      && HoldSoundsDataField != null
      && NextHoldSoundField != null
      && ExtraTicksField != null
      && NextExtraTickField != null
      && CountdownTimesField != null
      && PlayCountdownHihatsField != null
      && PlayEndingCymbalField != null;
  }

  private void SetFirstFutureIndex(IList schedule, FieldInfo indexField, double currentDspTime)
  {
    int index = 0;
    while (index < schedule.Count && ReadTime(schedule[index]) <= currentDspTime)
    {
      index++;
    }
    indexField.SetValue(conductor, index);
  }

  private static void ShiftSchedule(IList schedule, double shift)
  {
    if (schedule == null || shift == 0.0)
    {
      return;
    }

    for (int index = 0; index < schedule.Count; index++)
    {
      object item = schedule[index];
      if (item == null)
      {
        continue;
      }
      Type itemType = item.GetType();
      FieldInfo timeField = AccessTools.Field(itemType, "time");
      if (timeField == null)
      {
        throw new MissingFieldException(itemType.FullName, "time");
      }
      timeField.SetValue(item, (double)timeField.GetValue(item) + shift);
      FieldInfo endTimeField = AccessTools.Field(itemType, "endTime");
      if (endTimeField != null)
      {
        double endTime = (double)endTimeField.GetValue(item);
        if (endTime > 0.0)
        {
          endTimeField.SetValue(item, endTime + shift);
        }
      }
      schedule[index] = item;
    }
  }

  private void ShiftCountdownTimes(double shift)
  {
    if (countdownTimes == null || shift == 0.0)
    {
      return;
    }
    for (int index = 0; index < countdownTimes.Length; index++)
    {
      if (countdownTimes[index] > 0.0)
      {
        countdownTimes[index] += shift;
      }
    }
    CountdownTimesField.SetValue(conductor, countdownTimes);
  }

  private void ScheduleDirectSounds(double now)
  {
    if (playCountdownHihats && countdownTimes != null)
    {
      foreach (double countdownTime in countdownTimes)
      {
        if (countdownTime > now)
        {
          AudioManager.Play("sndHat", countdownTime, conductor.hitSoundGroup, conductor.hitSoundVolume, 10);
        }
      }
    }
    if (playEndingCymbal && endingCymbalTime > now)
    {
      AudioManager.Play("sndCymbalCrash", endingCymbalTime, conductor.hitSoundGroup, conductor.hitSoundVolume, 10);
    }
  }

  private double CalculateEndingCymbalTime()
  {
    if (!playEndingCymbal || ADOBase.lm?.listFloors == null || ADOBase.lm.listFloors.Count == 0)
    {
      return 0.0;
    }
    int floorIndex = GCS.practiceMode
      ? Math.Min(GCS.checkpointNum + GCS.practiceLength, ADOBase.lm.listFloors.Count - 1)
      : ADOBase.lm.listFloors.Count - 1;
    return conductor.dspTimeSong
      + ADOBase.lm.listFloors[floorIndex].entryTimePitchAdj
      + conductor.addoffset / conductor.song.pitch;
  }

  private static double ReadTime(object item)
  {
    if (item == null)
    {
      return double.PositiveInfinity;
    }
    FieldInfo timeField = AccessTools.Field(item.GetType(), "time");
    if (timeField == null)
    {
      throw new MissingFieldException(item.GetType().FullName, "time");
    }
    return (double)timeField.GetValue(item);
  }

  private void ClearState()
  {
    conductor = null;
    hitSounds = null;
    holdSounds = null;
    extraTicks = null;
    countdownTimes = null;
    scheduleDspTimeSong = 0.0;
    endingCymbalTime = 0.0;
    missedHitSound = null;
    playCountdownHihats = false;
    playEndingCymbal = false;
    prepared = false;
    active = false;
  }
}
