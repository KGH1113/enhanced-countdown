using System.Reflection;
using HarmonyLib;
using EnhancedCountdown.Bootstrap;
using EnhancedCountdown.Infrastructure.Settings;
using EnhancedCountdown.Presentation;
using UnityEngine;
using UnityModManagerNet;

namespace EnhancedCountdown;

public static class Main
{
  private static Harmony harmony;
  private static GameObject runtimeHostObject;
  private static UpdateSettings updateSettings;
  private static string updateSettingsPath;
  private static ModLocale guiLocale;
  private static GUIStyle localizedLabelStyle;
  private static GUIStyle localizedToggleStyle;

  public static bool Load(UnityModManager.ModEntry entry)
  {
    try
    {
      updateSettingsPath = System.IO.Path.Combine(entry.Path, "UpdateSettings.json");
      updateSettings = UpdateSettings.Load(updateSettingsPath);
      ModCompositionRoot.Initialize(entry);
      harmony = new Harmony(entry.Info.Id);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      CreateRuntimeHost();
      entry.OnToggle = OnToggle;
      entry.OnUnload = OnUnload;
      entry.OnGUI = OnGUI;
      entry.OnSaveGUI = OnSaveGUI;
      entry.Logger.Log("EnhancedCountdown loaded.");
      return true;
    }
    catch (System.Exception exception)
    {
      ModCompositionRoot.Shutdown();
      entry.Logger.Error(exception.ToString());
      return false;
    }
  }

  private static void CreateRuntimeHost()
  {
    if (runtimeHostObject != null)
    {
      return;
    }

    runtimeHostObject = new GameObject("EnhancedCountdown.RuntimeHost");
    runtimeHostObject.AddComponent<RuntimeHost>();
    Object.DontDestroyOnLoad(runtimeHostObject);
  }

  private static bool OnToggle(UnityModManager.ModEntry entry, bool enabled)
  {
    if (enabled)
    {
      if (ModCompositionRoot.Coordinator == null)
      {
        ModCompositionRoot.Initialize(entry);
      }
      if (harmony == null)
      {
        harmony = new Harmony(entry.Info.Id);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
      }
      CreateRuntimeHost();
      return true;
    }

    Disable(entry);
    return true;
  }

  private static bool OnUnload(UnityModManager.ModEntry entry)
  {
    SaveUpdateSettings(entry);
    Disable(entry);
    return true;
  }

  private static void OnGUI(UnityModManager.ModEntry entry)
  {
    ModLocale locale = ModLocalization.CurrentLocale;
    EnsureLocalizedGuiStyles(locale);
    GUILayout.Label(ModLocalization.Get(ModText.Updates, locale), localizedLabelStyle);
    bool receiveBetaUpdates = GUILayout.Toggle(
      updateSettings.ReceiveBetaUpdates,
      ModLocalization.Get(ModText.ReceiveBetaUpdates, locale),
      localizedToggleStyle
    );
    GUILayout.Label(ModLocalization.Get(ModText.BetaWarning, locale), localizedLabelStyle);
    if (receiveBetaUpdates == updateSettings.ReceiveBetaUpdates)
      return;
    updateSettings.ReceiveBetaUpdates = receiveBetaUpdates;
    SaveUpdateSettings(entry);
  }

  private static void EnsureLocalizedGuiStyles(ModLocale locale)
  {
    if (localizedLabelStyle != null && localizedToggleStyle != null && guiLocale == locale)
    {
      return;
    }

    guiLocale = locale;
    localizedLabelStyle = new GUIStyle(GUI.skin.label);
    localizedToggleStyle = new GUIStyle(GUI.skin.toggle);
    Font localizedFont = ModLocalization.GetLegacyFont(locale);
    if (localizedFont != null)
    {
      localizedLabelStyle.font = localizedFont;
      localizedToggleStyle.font = localizedFont;
    }
  }

  private static void OnSaveGUI(UnityModManager.ModEntry entry)
  {
    SaveUpdateSettings(entry);
  }

  private static void SaveUpdateSettings(UnityModManager.ModEntry entry)
  {
    try
    {
      updateSettings?.Save(updateSettingsPath);
    }
    catch (System.Exception exception)
    {
      entry.Logger.Error("[UpdateSettings] " + exception);
    }
  }

  private static void Disable(UnityModManager.ModEntry entry)
  {
    ModCompositionRoot.Shutdown();
    if (runtimeHostObject != null)
    {
      Object.Destroy(runtimeHostObject);
      runtimeHostObject = null;
    }
    harmony?.UnpatchAll(entry.Info.Id);
    harmony = null;
    localizedLabelStyle = null;
    localizedToggleStyle = null;
  }
}
