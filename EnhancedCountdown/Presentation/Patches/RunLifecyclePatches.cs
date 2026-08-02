using HarmonyLib;
using EnhancedCountdown.Bootstrap;

namespace EnhancedCountdown.Presentation.Patches;

[HarmonyPatch(typeof(scrController), nameof(scrController.Start_Rewind))]
internal static class StartRewindPatch
{
  [HarmonyPrefix]
  private static void Prefix(scrController __instance, int _currentSeqID)
  {
    ModCompositionRoot.Coordinator?.OnStartRewind(__instance, _currentSeqID);
  }
}

[HarmonyPatch(typeof(scrController), nameof(scrController.Scrub))]
internal static class InitialScrubPatch
{
  [HarmonyPrefix]
  private static void Prefix(int floorNum, ref bool forceDontStartMusicFourTilesBefore)
  {
    if (ModCompositionRoot.Coordinator?.PrepareInitialScrub(floorNum) == true)
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
    ModCompositionRoot.Coordinator?.OnMusicScheduled(__instance);
  }
}

[HarmonyPatch(typeof(scrController), nameof(scrController.TogglePauseGame))]
internal static class PauseFrozenStartPatch
{
  [HarmonyPrefix]
  private static void Prefix(scrController __instance)
  {
    ModCompositionRoot.Coordinator?.OnPauseRequested(__instance);
  }
}

[HarmonyPatch(typeof(scnEditor), nameof(scnEditor.SwitchToEditMode))]
internal static class EditorPlayModeExitPatch
{
  [HarmonyPrefix]
  private static void Prefix()
  {
    ModCompositionRoot.Coordinator?.OnEditorPlayModeExited();
  }
}
