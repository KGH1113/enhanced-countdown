using HarmonyLib;

namespace RemoveCountdown.Patches;

[HarmonyPatch(typeof(scrController), nameof(scrController.Start_Rewind))]
internal static class StartRewindPatch
{
  [HarmonyPrefix]
  private static void Prefix(scrController __instance, int _currentSeqID)
  {
    MidRunState.OnStartRewind(__instance, _currentSeqID);
  }
}

[HarmonyPatch(typeof(scrController), nameof(scrController.Scrub))]
internal static class InitialScrubPatch
{
  [HarmonyPrefix]
  private static void Prefix(int floorNum, ref bool forceDontStartMusicFourTilesBefore)
  {
    if (MidRunState.PrepareInitialScrub(floorNum))
    {
      forceDontStartMusicFourTilesBefore = true;
    }
  }
}

[HarmonyPatch(typeof(scrController), "OnMusicScheduled")]
internal static class MusicScheduledPatch
{
  [HarmonyPostfix]
  private static void Postfix(scrController __instance)
  {
    MidRunState.OnMusicScheduled(__instance);
  }
}

[HarmonyPatch(typeof(scrController), nameof(scrController.TogglePauseGame))]
internal static class PauseFrozenStartPatch
{
  [HarmonyPrefix]
  private static void Prefix(scrController __instance)
  {
    MidRunState.OnPauseRequested(__instance);
  }
}
