using System;
using System.Reflection;

namespace EnhancedCountdown.Infrastructure.Compatibility;

// Reads a public static bool of another mod without referencing its assembly.
// It is resolved on first use because the other mod may load after this one.
internal sealed class StaticBoolProperty
{
  private readonly string assemblyName;
  private readonly string typeName;
  private readonly string propertyName;
  private PropertyInfo property;

  internal StaticBoolProperty(string assemblyName, string typeName, string propertyName)
  {
    this.assemblyName = assemblyName;
    this.typeName = typeName;
    this.propertyName = propertyName;
  }

  // Null when the mod is not loaded or does not have the property.
  internal bool? Read()
  {
    try
    {
      PropertyInfo resolved = property ?? Resolve();
      return resolved?.GetValue(null) is bool value ? value : null;
    }
    catch
    {
      return null;
    }
  }

  private PropertyInfo Resolve()
  {
    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
      if (!string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
      {
        continue;
      }

      PropertyInfo candidate = assembly
        .GetType(typeName, throwOnError: false)
        ?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
      if (candidate?.PropertyType == typeof(bool) && candidate.GetMethod != null)
      {
        property = candidate;
      }
      return property;
    }

    return null;
  }
}
