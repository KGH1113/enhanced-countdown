using System;
using System.Reflection;

namespace EnhancedCountdown.Infrastructure.Compatibility;

internal static class TufReplayPlaybackDetector
{
  private const string AssemblyName = "TUFReplay";
  private const string RuntimeTypeName = "TUFReplay.ReplayRuntime";
  private const string PlaybackPropertyName = "IsPlaybackActive";

  private static PropertyInfo playbackProperty;

  internal static bool IsPlaybackActive()
  {
    try
    {
      PropertyInfo property = playbackProperty ?? ResolvePlaybackProperty();
      return property?.GetValue(null) is bool isActive && isActive;
    }
    catch
    {
      return false;
    }
  }

  private static PropertyInfo ResolvePlaybackProperty()
  {
    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
      if (!string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.Ordinal))
      {
        continue;
      }

      PropertyInfo property = assembly
        .GetType(RuntimeTypeName, throwOnError: false)
        ?.GetProperty(PlaybackPropertyName, BindingFlags.Public | BindingFlags.Static);
      if (property?.PropertyType == typeof(bool) && property.GetMethod != null)
      {
        playbackProperty = property;
      }
      return playbackProperty;
    }

    return null;
  }
}
