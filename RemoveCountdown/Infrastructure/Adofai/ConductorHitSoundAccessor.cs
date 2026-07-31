using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RemoveCountdown.Domain.MidRun;

namespace RemoveCountdown.Infrastructure.Adofai;

internal sealed class ConductorHitSoundAccessor
{
  private static readonly FieldInfo HitSoundsDataField = AccessTools.Field(typeof(scrConductor), "hitSoundsData");

  internal ScheduledHitSound? CaptureFirstElapsed(scrConductor conductor, double currentDspTime)
  {
    if (HitSoundsDataField?.GetValue(conductor) is not IEnumerable hitSoundsData)
    {
      return null;
    }

    foreach (object item in hitSoundsData)
    {
      if (item == null)
      {
        continue;
      }

      Type itemType = item.GetType();
      FieldInfo hitSoundField = AccessTools.Field(itemType, "hitSound");
      FieldInfo timeField = AccessTools.Field(itemType, "time");
      FieldInfo volumeField = AccessTools.Field(itemType, "volume");
      if (hitSoundField == null || timeField == null || volumeField == null)
      {
        continue;
      }

      double time = (double)timeField.GetValue(item);
      if (time > currentDspTime)
      {
        return null;
      }

      return new ScheduledHitSound(hitSoundField.GetValue(item).ToString(), time, (float)volumeField.GetValue(item));
    }

    return null;
  }
}
