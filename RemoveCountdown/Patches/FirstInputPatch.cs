using HarmonyLib;

namespace RemoveCountdown.Patches;

[HarmonyPatch(typeof(scrPlayer), nameof(scrPlayer.Simulated_PlayerControl_Update))]
internal static class FrozenPlayerUpdatePatch
{
  [HarmonyPrefix]
  private static bool Prefix(scrPlayer __instance, ref ulong? targetTick)
  {
    return MidRunState.PreparePlayerUpdate(__instance, ref targetTick);
  }

  [HarmonyPostfix]
  private static void Postfix(scrPlayer __instance)
  {
    MidRunState.CompletePlayerUpdate(__instance);
  }
}

[HarmonyPatch(typeof(scrPlayer), nameof(scrPlayer.Hit))]
internal static class FirstManualHitPatch
{
  [HarmonyPrefix]
  private static void Prefix(scrPlayer __instance, bool isAuto)
  {
    MidRunState.OnManualHitStarting(__instance, isAuto);
  }

  [HarmonyPostfix]
  private static void Postfix(scrPlayer __instance, bool isAuto, bool __result)
  {
    MidRunState.OnManualHitCompleted(__instance, isAuto, __result);
  }
}
