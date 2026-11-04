using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using EnhancedCountdown.Application.Ports;
using HarmonyLib;

namespace EnhancedCountdown.Infrastructure.Adofai;

internal sealed class AdofaiHitSoundScheduler : IHitSoundScheduler
{
  private const double SameTimeTolerance = 1e-6;

  // Hold sounds this close to the frozen tile are captured so a loop that is still sounding when
  // playback starts can be resumed. It covers any input offset the game allows.
  private const double HoldCaptureHorizonSeconds = 2.0;

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

  // Optional: lets PlayHitTimes skip its StopAllSounds so the first tile's sounds can be scheduled first.
  private static readonly FieldInfo PlayedHitSoundsField = AccessTools.Field(typeof(scrConductor), "playedHitSounds");
  private static readonly Dictionary<Type, ScheduleItemFields> ItemFieldsByType = new();

  private readonly IModLogger logger;
  private readonly List<CapturedSound> frozenFloorSounds = new();
  private scrConductor conductor;
  private int frozenFloorIndex = -1;
  private bool prepared;
  private bool active;

  internal AdofaiHitSoundScheduler(IModLogger logger)
  {
    this.logger = logger;
  }

  public string GetCompatibilityFailureReason()
  {
    return ReflectionContractIsAvailable() ? null : "the hit-sound schedule layout is incompatible";
  }

  public bool Prepare(int frozenFloorIndex)
  {
    ClearState();
    conductor = ADOBase.conductor;
    if (
      conductor == null
      || !ReflectionContractIsAvailable()
      || ADOBase.lm?.listFloors == null
      || frozenFloorIndex <= 0
      || frozenFloorIndex >= ADOBase.lm.listFloors.Count
    )
    {
      conductor = null;
      return false;
    }

    try
    {
      this.frozenFloorIndex = frozenFloorIndex;
      CaptureFrozenFloorSounds();
      ParkSchedules();
      prepared = true;
      active = false;
      return true;
    }
    catch (Exception exception)
    {
      logger.LogError("Could not prepare the frozen hit-sound schedule", exception);
      ClearState();
      return false;
    }
  }

  public void Activate(double earliestPlaybackDspTime)
  {
    if (!prepared || active || conductor == null)
    {
      return;
    }

    try
    {
      Stopwatch watch = Stopwatch.StartNew();
      double frozenFloorTime = FloorDspTime(frozenFloorIndex);
      bool replayBeforeRebuild = PlayedHitSoundsField != null;
      if (replayBeforeRebuild)
      {
        ReplayFrozenFloorSounds(frozenFloorTime, earliestPlaybackDspTime);
      }
      RebuildScheduleAfter(earliestPlaybackDspTime, keepLiveSounds: replayBeforeRebuild);
      if (!replayBeforeRebuild)
      {
        ReplayFrozenFloorSounds(frozenFloorTime, earliestPlaybackDspTime);
      }
      active = true;
      logger.Log(
        string.Format(
          CultureInfo.InvariantCulture,
          "First input: hit-sound schedule rebuilt in {0:0.0} ms.",
          watch.Elapsed.TotalMilliseconds
        )
      );
    }
    catch (Exception exception)
    {
      logger.LogError("Could not activate the frozen hit-sound schedule", exception);
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
    try
    {
      ParkSchedules();
    }
    catch (Exception exception)
    {
      logger.LogError("Could not park the hit-sound schedule after a rejected input", exception);
    }
    active = false;
  }

  public void Reset(bool keepInstalledSchedule = false)
  {
    // Rebuild against the current clock even when preparation stopped early, so the native lists
    // never keep times from before the timeline was rebased.
    scrConductor target = conductor ?? ADOBase.conductor;
    ClearState();
    if (keepInstalledSchedule || target == null)
    {
      return;
    }

    try
    {
      AudioManager.Instance.StopAllSounds();
      target.PlayHitTimes();
    }
    catch (Exception exception)
    {
      logger.LogError("Could not restore the native hit-sound schedule", exception);
    }
  }

  private static bool ReflectionContractIsAvailable()
  {
    return HitSoundsDataField != null
      && NextHitSoundField != null
      && HoldSoundsDataField != null
      && NextHoldSoundField != null
      && ExtraTicksField != null
      && NextExtraTickField != null;
  }

  private void CaptureFrozenFloorSounds()
  {
    // Build the full schedule (PlayHitTimes drops entries at or before dspTime) to find the sounds of
    // the tile the first input lands on. The listener is paused while frozen, so the countdown and
    // cymbal that PlayHitTimes schedules immediately are silent until StopAllSounds removes them.
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
    AudioManager.Instance.StopAllSounds();

    double frozenFloorTime = FloorDspTime(frozenFloorIndex);
    CaptureHitSounds(ReadSchedule(HitSoundsDataField), frozenFloorTime);
    CaptureHoldSounds(ReadSchedule(HoldSoundsDataField), frozenFloorTime);
  }

  private void CaptureHitSounds(IList hitSounds, double frozenFloorTime)
  {
    GetFrozenFloorHitWindow(frozenFloorTime, out double windowStart, out double windowEnd);
    foreach (object item in hitSounds)
    {
      if (item == null)
      {
        continue;
      }
      ScheduleItemFields fields = ScheduleItemFields.For(item.GetType());
      double time = fields.ReadTime(item);
      if (time > windowEnd)
      {
        break;
      }
      if (time < windowStart || fields.HitSound == null || fields.Volume == null)
      {
        continue;
      }

      frozenFloorSounds.Add(
        new CapturedSound(
          string.Concat("snd", fields.HitSound.GetValue(item).ToString()),
          time - frozenFloorTime,
          relativeEndTime: null,
          (float)fields.Volume.GetValue(item)
        )
      );
    }
  }

  private void CaptureHoldSounds(IList holdSounds, double frozenFloorTime)
  {
    foreach (object item in holdSounds)
    {
      if (item == null)
      {
        continue;
      }
      ScheduleItemFields fields = ScheduleItemFields.For(item.GetType());
      if (fields.Name == null || fields.Volume == null)
      {
        continue;
      }

      double relativeTime = fields.ReadTime(item) - frozenFloorTime;
      double endTime = fields.EndTime != null ? (double)fields.EndTime.GetValue(item) : -1.0;
      bool isLoop = endTime > 0.0;
      bool capture = isLoop
        ? relativeTime <= HoldCaptureHorizonSeconds && endTime - frozenFloorTime >= -HoldCaptureHorizonSeconds
        : Math.Abs(relativeTime) <= SameTimeTolerance;
      if (capture)
      {
        frozenFloorSounds.Add(
          new CapturedSound(
            fields.Name.GetValue(item) as string,
            relativeTime,
            isLoop ? endTime - frozenFloorTime : null,
            (float)fields.Volume.GetValue(item)
          )
        );
      }
    }
  }

  private void GetFrozenFloorHitWindow(double frozenFloorTime, out double windowStart, out double windowEnd)
  {
    // PlayHitTimes schedules a tile's hit sound at its floor time minus a per-sound attack offset, and
    // planet-count sounds at the floor time itself. Neighbouring floors bound the window; floors at the
    // same time (midspins) belong to the same input.
    double largestOffset = 0.0;
    double smallestOffset = 0.0;
    Dictionary<HitSound, double> offsets = ADOBase.gc?.hitSoundOffsets;
    if (offsets != null)
    {
      foreach (double offset in offsets.Values)
      {
        largestOffset = Math.Max(largestOffset, offset);
        smallestOffset = Math.Min(smallestOffset, offset);
      }
    }

    windowStart = frozenFloorTime - largestOffset - SameTimeTolerance;
    windowEnd = frozenFloorTime - smallestOffset + SameTimeTolerance;
    int floorCount = ADOBase.lm.listFloors.Count;
    int previous = frozenFloorIndex - 1;
    while (previous >= 0 && Math.Abs(FloorDspTime(previous) - frozenFloorTime) <= SameTimeTolerance)
    {
      previous--;
    }
    if (previous >= 0)
    {
      windowStart = Math.Max(windowStart, FloorDspTime(previous) + SameTimeTolerance);
    }
    int next = frozenFloorIndex + 1;
    while (next < floorCount && Math.Abs(FloorDspTime(next) - frozenFloorTime) <= SameTimeTolerance)
    {
      next++;
    }
    if (next < floorCount)
    {
      windowEnd = Math.Min(windowEnd, FloorDspTime(next) - SameTimeTolerance);
    }
  }

  private void ReplayFrozenFloorSounds(double frozenFloorTime, double playbackTime)
  {
    // Sounds after playback starts come from the rebuilt native schedule. Elapsed ones are played at
    // the start; the rest of the elapsed audio is skipped together with the song.
    foreach (CapturedSound sound in frozenFloorSounds)
    {
      if (frozenFloorTime + sound.RelativeTime > playbackTime || string.IsNullOrEmpty(sound.Name))
      {
        continue;
      }
      if (sound.RelativeEndTime is double relativeEndTime)
      {
        double endTime = frozenFloorTime + relativeEndTime;
        if (endTime > playbackTime)
        {
          conductor.PlayWithEndTime(sound.Name, playbackTime, endTime, sound.Volume);
        }
        continue;
      }
      AudioManager.Play(sound.Name, playbackTime, conductor.hitSoundGroup, sound.Volume);
    }
  }

  private void RebuildScheduleAfter(double playbackTime, bool keepLiveSounds)
  {
    // With dspTime at the playback start, PlayHitTimes keeps only later entries and schedules the
    // countdown hats and ending cymbal exactly as the native game would.
    double currentDspTime = conductor.dspTime;
    try
    {
      conductor.dspTime = playbackTime;
      if (keepLiveSounds)
      {
        PlayedHitSoundsField.SetValue(conductor, false);
      }
      conductor.PlayHitTimes();
    }
    finally
    {
      conductor.dspTime = currentDspTime;
    }
  }

  private void ParkSchedules()
  {
    // Keeps the conductor from consuming lists built against another clock while frozen.
    NextHitSoundField.SetValue(conductor, ReadSchedule(HitSoundsDataField).Count);
    NextHoldSoundField.SetValue(conductor, ReadSchedule(HoldSoundsDataField).Count);
    NextExtraTickField.SetValue(conductor, ReadSchedule(ExtraTicksField).Count);
  }

  private IList ReadSchedule(FieldInfo field)
  {
    if (field.GetValue(conductor) is not IList schedule)
    {
      throw new InvalidOperationException(string.Concat("The conductor schedule '", field.Name, "' is unavailable."));
    }
    return schedule;
  }

  private double FloorDspTime(int floorIndex)
  {
    return conductor.dspTimeSong
      + conductor.addoffset / (double)conductor.song.pitch
      + ADOBase.lm.listFloors[floorIndex].entryTimePitchAdj;
  }

  private void ClearState()
  {
    conductor = null;
    frozenFloorIndex = -1;
    frozenFloorSounds.Clear();
    prepared = false;
    active = false;
  }

  private readonly struct CapturedSound
  {
    internal CapturedSound(string name, double relativeTime, double? relativeEndTime, float volume)
    {
      Name = name;
      RelativeTime = relativeTime;
      RelativeEndTime = relativeEndTime;
      Volume = volume;
    }

    internal string Name { get; }
    internal double RelativeTime { get; }
    internal double? RelativeEndTime { get; }
    internal float Volume { get; }
  }

  private sealed class ScheduleItemFields
  {
    private ScheduleItemFields(Type itemType)
    {
      Time = AccessTools.Field(itemType, "time") ?? throw new MissingFieldException(itemType.FullName, "time");
      EndTime = AccessTools.Field(itemType, "endTime");
      HitSound = AccessTools.Field(itemType, "hitSound");
      Name = AccessTools.Field(itemType, "name");
      Volume = AccessTools.Field(itemType, "volume");
    }

    internal FieldInfo Time { get; }
    internal FieldInfo EndTime { get; }
    internal FieldInfo HitSound { get; }
    internal FieldInfo Name { get; }
    internal FieldInfo Volume { get; }

    internal static ScheduleItemFields For(Type itemType)
    {
      if (!ItemFieldsByType.TryGetValue(itemType, out ScheduleItemFields fields))
      {
        fields = new ScheduleItemFields(itemType);
        ItemFieldsByType[itemType] = fields;
      }
      return fields;
    }

    internal double ReadTime(object item)
    {
      return (double)Time.GetValue(item);
    }
  }
}
