using System;
using System.Collections;
using EnhancedCountdown.Bootstrap;
using HarmonyLib;

namespace EnhancedCountdown.Presentation.Patches;

[HarmonyPatch(typeof(scrConductor), "DesyncFix")]
internal static class ConductorDesyncFixPatch
{
  [HarmonyPostfix]
  private static void Postfix(scrConductor __instance, ref IEnumerator __result)
  {
    if (__result == null || !object.ReferenceEquals(__instance, ADOBase.conductor))
    {
      return;
    }

    __result = GuardExecution(__instance, __result);
  }

  private static IEnumerator GuardExecution(scrConductor conductor, IEnumerator original)
  {
    try
    {
      while (true)
      {
        if (
          object.ReferenceEquals(conductor, ADOBase.conductor)
          && ModCompositionRoot.Coordinator?.OwnsAudioTimeline == true
        )
        {
          yield break;
        }

        if (!original.MoveNext())
        {
          yield break;
        }

        yield return original.Current;
      }
    }
    finally
    {
      (original as IDisposable)?.Dispose();
    }
  }
}
