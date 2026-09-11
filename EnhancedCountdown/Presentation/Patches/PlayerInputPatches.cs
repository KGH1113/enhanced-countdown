using System.Reflection;
using EnhancedCountdown.Bootstrap;
using EnhancedCountdown.Infrastructure.Compatibility;
using HarmonyLib;

namespace EnhancedCountdown.Presentation.Patches;

[HarmonyPatch]
internal static class FrozenPlayerUpdatePatch
{
  private static MethodBase TargetMethod()
  {
    return AdofaiRuntimeApi.PlayerUpdateMethod;
  }

  [HarmonyPrefix]
  private static bool Prefix(scrPlayer __instance, object[] __args)
  {
    bool clearTargetTick = false;
    bool runOriginal = ModCompositionRoot.Coordinator?.PreparePlayerUpdate(__instance, out clearTargetTick) ?? true;
    if (clearTargetTick && __args.Length > 0)
    {
      __args[0] = null;
    }
    return runOriginal;
  }

  [HarmonyPostfix]
  private static void Postfix(scrPlayer __instance)
  {
    ModCompositionRoot.Coordinator?.CompletePlayerUpdate(__instance);
  }
}

[HarmonyPatch]
internal static class FirstManualHitPatch
{
  private static MethodBase TargetMethod()
  {
    return AdofaiRuntimeApi.PlayerHitMethod;
  }

  [HarmonyPrefix]
  private static void Prefix(scrPlayer __instance, bool isAuto)
  {
    ModCompositionRoot.Coordinator?.OnManualHitStarting(__instance, isAuto);
  }

  [HarmonyPostfix]
  private static void Postfix(scrPlayer __instance, bool isAuto, bool __result)
  {
    ModCompositionRoot.Coordinator?.OnManualHitCompleted(__instance, isAuto, __result);
  }
}
