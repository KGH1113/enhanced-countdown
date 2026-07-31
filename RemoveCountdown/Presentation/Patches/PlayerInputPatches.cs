using HarmonyLib;
using RemoveCountdown.Bootstrap;

namespace RemoveCountdown.Presentation.Patches;

[HarmonyPatch(typeof(scrPlayer), nameof(scrPlayer.Simulated_PlayerControl_Update))]
internal static class FrozenPlayerUpdatePatch
{
  [HarmonyPrefix]
  private static bool Prefix(scrPlayer __instance, ref ulong? targetTick)
  {
    return ModCompositionRoot.Coordinator?.PreparePlayerUpdate(__instance, ref targetTick) ?? true;
  }

  [HarmonyPostfix]
  private static void Postfix(scrPlayer __instance)
  {
    ModCompositionRoot.Coordinator?.CompletePlayerUpdate(__instance);
  }
}

[HarmonyPatch(typeof(scrPlayer), nameof(scrPlayer.Hit))]
internal static class FirstManualHitPatch
{
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
