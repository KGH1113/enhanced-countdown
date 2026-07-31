using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace RemoveCountdown;

public static class Main
{
  private static Harmony harmony;
  private static UnityModManager.ModEntry modEntry;
  private static GameObject runtimeHostObject;

  internal static void Log(string message)
  {
    modEntry?.Logger.Log(message);
  }

  internal static void LogError(string context, System.Exception exception)
  {
    modEntry?.Logger.Error($"[{context}] {exception}");
  }

  public static bool Load(UnityModManager.ModEntry entry)
  {
    try
    {
      modEntry = entry;
      harmony = new Harmony(entry.Info.Id);
      harmony.PatchAll(Assembly.GetExecutingAssembly());
      CreateRuntimeHost();
      entry.OnToggle = OnToggle;
      entry.OnUnload = OnUnload;
      entry.Logger.Log("RemoveCountdown loaded.");
      return true;
    }
    catch (System.Exception exception)
    {
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

    runtimeHostObject = new GameObject("RemoveCountdown.RuntimeHost");
    runtimeHostObject.AddComponent<RuntimeHost>();
    Object.DontDestroyOnLoad(runtimeHostObject);
  }

  private static bool OnToggle(UnityModManager.ModEntry entry, bool enabled)
  {
    if (enabled)
    {
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
    Disable(entry);
    return true;
  }

  private static void Disable(UnityModManager.ModEntry entry)
  {
    MidRunState.Shutdown();
    if (runtimeHostObject != null)
    {
      Object.Destroy(runtimeHostObject);
      runtimeHostObject = null;
    }
    harmony?.UnpatchAll(entry.Info.Id);
    harmony = null;
  }
}
