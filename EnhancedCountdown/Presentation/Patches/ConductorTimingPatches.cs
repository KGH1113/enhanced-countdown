using System.Collections;
using EnhancedCountdown.Bootstrap;
using HarmonyLib;

namespace EnhancedCountdown.Presentation.Patches;

[HarmonyPatch(typeof(scrConductor), "DesyncFix")]
internal static class ConductorDesyncFixPatch
{
  [HarmonyPrefix]
  [HarmonyPriority(Priority.First)]
  private static bool Prefix(scrConductor __instance, ref IEnumerator __result)
  {
    if (
      !object.ReferenceEquals(__instance, ADOBase.conductor)
      || ModCompositionRoot.Coordinator?.OwnsAudioTimeline != true
    )
    {
      return true;
    }

    __result = EmptyCoroutine();
    return false;
  }

  private static IEnumerator EmptyCoroutine()
  {
    yield break;
  }
}
